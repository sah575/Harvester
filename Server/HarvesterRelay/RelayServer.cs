using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Harvester.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace HarvesterRelay;

/// <summary>Bir bağlantının relay tarafındaki durumu (NetPeer.Tag içinde tutulur).</summary>
sealed class PeerState
{
    public Room? Room;
    public ushort Id;          // odadaki numarası; host için 0
    public bool IsHost;
    public IPAddress Address = IPAddress.None;

    // Hız sınırı (token bucket)
    public double Tokens;
    public long LastRefillMs;
}

sealed class Room
{
    public Room(string code, NetPeer host, int maxPlayers, long createdMs)
    {
        Code = code;
        Host = host;
        MaxPlayers = maxPlayers;
        CreatedMs = createdMs;
    }

    public readonly string Code;
    public readonly NetPeer Host;
    public readonly int MaxPlayers;
    public readonly long CreatedMs;
    public readonly Dictionary<ushort, NetPeer> Clients = new();

    // Numaralar tekrar kullanılmaz: host'a geç ulaşan bir paket yeni gelen oyuncuya karışmasın.
    public ushort NextId = 1;

    public int PlayerCount => 1 + Clients.Count;
}

/// <summary>
/// Odaları tutar ve paketleri host ile istemciler arasında taşır. Oyun mantığı çalıştırmaz:
/// oyunun otoritesi odayı kuran oyuncudadır (NGO host'u).
/// </summary>
sealed class RelayServer
{
    // Her bağlantı saniyede ortalama bu kadar bayt gönderebilir; kısa patlamalara izin var.
    // Oyunun normal trafiği bunun çok altında (oyuncu başına birkaç KB/sn).
    const double RateBytesPerSec = 1_000_000;
    const double RateBurstBytes = 2_000_000;
    const int PacketOverhead = 32;

    // Kod tahminiyle odalara girmeye çalışanları yavaşlat.
    const int MaxJoinFailsPerMinute = 20;

    const long StatsIntervalMs = 60_000;

    readonly int _port, _maxRooms, _maxPeers;
    readonly EventBasedNetListener _listener = new();
    readonly NetManager _net;
    readonly Dictionary<string, Room> _rooms = new();
    readonly Dictionary<IPAddress, (int Count, long StartMs)> _joinFails = new();
    readonly NetDataWriter _w = new(true, 1500);
    readonly Stopwatch _clock = Stopwatch.StartNew();

    readonly object _gate = new();
    readonly bool _unsynced, _flush;
    bool _forwarded;
    long _lastStatsMs;
    long _lastBytesIn, _lastBytesOut;
    string _lastStatsLine = "";

    /// <param name="unsynced">Olayları LiteNetLib'in alma iş parçacığında hemen işle (bekleme döngüsü yok).</param>
    /// <param name="updateMs">LiteNetLib'in gönderme/ping turu aralığı.</param>
    /// <param name="flush">Her taşımadan sonra gönderme turunu beklemeden tetikle.</param>
    public RelayServer(int port, int maxRooms, int maxPeers, bool unsynced, int updateMs, bool flush)
    {
        _unsynced = unsynced;
        _flush = flush;
        _port = port;
        _maxRooms = maxRooms;
        _maxPeers = maxPeers;

        _net = new NetManager(_listener)
        {
            AutoRecycle = true,
            IPv6Enabled = false,
            UnconnectedMessagesEnabled = false,
            DisconnectTimeout = RelayProtocol.DisconnectTimeoutMs,
            PingInterval = 1000,
            UpdateTime = updateMs,
            EnableStatistics = true,
            UnsyncedEvents = unsynced,
        };

        // Olaylar birden çok iş parçacığından gelebilir (alma ve zaman aşımı); hepsi tek kilitle sıraya girer.
        _listener.ConnectionRequestEvent += req => { lock (_gate) OnConnectionRequest(req); };
        _listener.PeerDisconnectedEvent += (peer, info) => { lock (_gate) OnPeerDisconnected(peer, info); };
        _listener.NetworkReceiveEvent += (peer, reader, channel, method) => { lock (_gate) OnReceive(peer, reader, channel, method); };
    }

    public bool Start()
    {
        if (!_net.Start(_port)) return false;
        Log($"Relay başladı: UDP {_port}, protokol v{RelayProtocol.Version}, en çok {_maxRooms} oda / {_maxPeers} bağlantı"
            + $" ({(_unsynced ? "olay güdümlü" : "döngü")}, güncelleme {_net.UpdateTime} ms{(_flush ? ", anında gönderim" : "")})");
        return true;
    }

    public void Stop()
    {
        Log("Relay kapanıyor");
        _net.Stop(true);
    }

    /// <summary>Döngü modunda olayları işler. Olay güdümlü modda sadece istatistik yazar.</summary>
    public void Poll()
    {
        if (_unsynced)
        {
            lock (_gate)
            {
                long t = _clock.ElapsedMilliseconds;
                if (t - _lastStatsMs >= StatsIntervalMs) WriteStats(t);
            }
            return;
        }

        _net.PollEvents();

        // LiteNetLib paketleri normalde bir sonraki güncelleme turunda (UpdateTime) yollar;
        // taşınan veri varsa beklemeden gönder ki relay gecikmeye ms eklemesin.
        if (_forwarded && _flush)
        {
            _forwarded = false;
            _net.TriggerUpdate();
        }

        long now = _clock.ElapsedMilliseconds;
        if (now - _lastStatsMs >= StatsIntervalMs)
            WriteStats(now);
    }

    // ---------------------------------------------------------------- bağlantı

    void OnConnectionRequest(ConnectionRequest req)
    {
        IPAddress ip = req.RemoteEndPoint.Address;
        NetDataReader r = req.Data;

        if (!r.TryGetUInt(out uint magic) || magic != RelayProtocol.Magic
            || !r.TryGetByte(out byte version) || !r.TryGetByte(out byte op))
        {
            Reject(req, RelayError.BadRequest);
            return;
        }
        if (version != RelayProtocol.Version)
        {
            Reject(req, RelayError.VersionMismatch);
            return;
        }
        if (_net.ConnectedPeersCount >= _maxPeers)
        {
            Reject(req, RelayError.ServerFull);
            return;
        }

        switch ((RelayConnectOp)op)
        {
            case RelayConnectOp.Create: HandleCreate(req, r, ip); break;
            case RelayConnectOp.Join: HandleJoin(req, r, ip); break;
            default: Reject(req, RelayError.BadRequest); break;
        }
    }

    void HandleCreate(ConnectionRequest req, NetDataReader r, IPAddress ip)
    {
        if (!r.TryGetByte(out byte maxPlayers))
        {
            Reject(req, RelayError.BadRequest);
            return;
        }
        if (_rooms.Count >= _maxRooms)
        {
            Reject(req, RelayError.ServerFull);
            return;
        }

        int max = Math.Clamp((int)maxPlayers, RelayProtocol.MinPlayers, RelayProtocol.MaxPlayersLimit);
        NetPeer peer = req.Accept();
        var room = new Room(NewCode(), peer, max, _clock.ElapsedMilliseconds);
        _rooms.Add(room.Code, room);
        peer.Tag = NewState(room, 0, true, ip);

        _w.Reset();
        _w.Put((byte)RelayMsg.RoomCreated);
        _w.Put(room.Code);
        _w.Put((byte)max);
        peer.Send(_w, DeliveryMethod.ReliableOrdered);

        Log($"Oda {room.Code} kuruldu ({ip}, en çok {max} oyuncu). Açık oda: {_rooms.Count}");
    }

    void HandleJoin(ConnectionRequest req, NetDataReader r, IPAddress ip)
    {
        long now = _clock.ElapsedMilliseconds;
        if (_joinFails.TryGetValue(ip, out var fails) && now - fails.StartMs < 60_000 && fails.Count >= MaxJoinFailsPerMinute)
        {
            Reject(req, RelayError.TooManyAttempts);
            return;
        }
        if (!r.TryGetString(out string raw) || raw.Length > 32)
        {
            Reject(req, RelayError.BadRequest);
            return;
        }

        string code = RelayProtocol.NormalizeCode(raw);
        if (!_rooms.TryGetValue(code, out Room? room))
        {
            NoteJoinFail(ip, now);
            Reject(req, RelayError.RoomNotFound);
            return;
        }
        if (room.PlayerCount >= room.MaxPlayers || room.NextId == ushort.MaxValue)
        {
            Reject(req, RelayError.RoomFull);
            return;
        }

        NetPeer peer = req.Accept();
        ushort id = room.NextId++;
        room.Clients.Add(id, peer);
        peer.Tag = NewState(room, id, false, ip);

        _w.Reset();
        _w.Put((byte)RelayMsg.Joined);
        _w.Put(id);
        _w.Put(room.Code);
        peer.Send(_w, DeliveryMethod.ReliableOrdered);

        _w.Reset();
        _w.Put((byte)RelayMsg.PeerJoined);
        _w.Put(id);
        room.Host.Send(_w, DeliveryMethod.ReliableOrdered);

        Log($"Oda {room.Code}: oyuncu #{id} katıldı ({ip}). Oyuncu: {room.PlayerCount}/{room.MaxPlayers}");
    }

    void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (peer.Tag is not PeerState st || st.Room == null) return;
        Room room = st.Room;
        st.Room = null;

        if (st.IsHost)
        {
            CloseRoom(room, $"kurucu ayrıldı ({info.Reason})");
        }
        else
        {
            RemoveClient(room, st.Id);
            Log($"Oda {room.Code}: oyuncu #{st.Id} ayrıldı ({info.Reason}). Oyuncu: {room.PlayerCount}/{room.MaxPlayers}");
        }
    }

    void CloseRoom(Room room, string why)
    {
        _rooms.Remove(room.Code);
        foreach (NetPeer client in room.Clients.Values)
        {
            if (client.Tag is PeerState cs) cs.Room = null;
            client.Disconnect(new[] { (byte)RelayError.RoomClosed });
        }
        room.Clients.Clear();

        double minutes = (_clock.ElapsedMilliseconds - room.CreatedMs) / 60000.0;
        Log($"Oda {room.Code} kapandı: {why}, {minutes:0.0} dk açık kaldı. Açık oda: {_rooms.Count}");
    }

    /// <summary>İstemciyi odadan çıkarır ve host'a haber verir.</summary>
    void RemoveClient(Room room, ushort id)
    {
        if (!room.Clients.Remove(id)) return;
        _w.Reset();
        _w.Put((byte)RelayMsg.PeerLeft);
        _w.Put(id);
        room.Host.Send(_w, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Bir oyuncuyu sunucu tarafında atar (host isteği ya da hız sınırı).</summary>
    void DropClient(NetPeer peer, RelayError reason)
    {
        if (peer.Tag is PeerState st && st.Room != null)
        {
            Room room = st.Room;
            st.Room = null;
            if (st.IsHost) CloseRoom(room, RelayProtocol.Describe(reason));
            else RemoveClient(room, st.Id);
        }
        peer.Disconnect(new[] { (byte)reason });
    }

    // ---------------------------------------------------------------- veri

    void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (peer.Tag is not PeerState st || st.Room == null) return;
        if (!reader.TryGetByte(out byte type)) return;

        bool reliable = method == DeliveryMethod.ReliableOrdered
                     || method == DeliveryMethod.ReliableUnordered
                     || method == DeliveryMethod.ReliableSequenced;
        if (!Allow(st, reader.AvailableBytes, reliable))
        {
            if (st.Tokens < -RateBurstBytes)
            {
                Log($"Oda {st.Room.Code}: #{st.Id} ({st.Address}) hız sınırını aştı, atılıyor");
                DropClient(peer, RelayError.RateLimited);
            }
            return;
        }

        Room room = st.Room;
        switch ((RelayMsg)type)
        {
            case RelayMsg.Data:
                if (st.IsHost)
                {
                    // host → sunucu: [hedef][veri]  ⇒  istemci: [veri]
                    if (!reader.TryGetUShort(out ushort to)) return;
                    if (!room.Clients.TryGetValue(to, out NetPeer? target)) return;
                    _w.Reset();
                    _w.Put((byte)RelayMsg.Data);
                    _w.Put(reader.RawData, reader.Position, reader.AvailableBytes);
                    Forward(target, method);
                }
                else
                {
                    // istemci → sunucu: [veri]  ⇒  host: [gönderen][veri]
                    _w.Reset();
                    _w.Put((byte)RelayMsg.Data);
                    _w.Put(st.Id);
                    _w.Put(reader.RawData, reader.Position, reader.AvailableBytes);
                    Forward(room.Host, method);
                }
                break;

            case RelayMsg.Kick:
                if (st.IsHost && reader.TryGetUShort(out ushort kickId)
                    && room.Clients.TryGetValue(kickId, out NetPeer? kicked))
                {
                    Log($"Oda {room.Code}: host #{kickId} oyuncusunu attı");
                    DropClient(kicked, RelayError.Kicked);
                }
                break;
        }
    }

    void Forward(NetPeer target, DeliveryMethod method)
    {
        // Relay başlığı yüzünden tek pakete sığmayan güvenilmez veri, parçalanabilen güvenilir kanala geçer.
        if ((method == DeliveryMethod.Unreliable || method == DeliveryMethod.Sequenced || method == DeliveryMethod.ReliableSequenced)
            && _w.Length > target.GetMaxSinglePacketSize(method))
        {
            method = method == DeliveryMethod.Unreliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.ReliableOrdered;
        }
        target.Send(_w, method);
        if (_unsynced && _flush) _net.TriggerUpdate();
        else _forwarded = true;
    }

    /// <summary>
    /// Hız sınırı. Güvenilmez paketler sınır aşılınca atılır. Güvenilir paketler atılamaz
    /// (gönderen onları teslim edilmiş sayar), o yüzden geçer ama borç birikir; borç çok büyürse bağlantı kesilir.
    /// </summary>
    bool Allow(PeerState st, int bytes, bool reliable)
    {
        long now = _clock.ElapsedMilliseconds;
        st.Tokens = Math.Min(RateBurstBytes, st.Tokens + (now - st.LastRefillMs) * RateBytesPerSec / 1000.0);
        st.LastRefillMs = now;

        double cost = bytes + PacketOverhead;
        if (st.Tokens >= cost || reliable)
        {
            st.Tokens -= cost;
            return st.Tokens >= -RateBurstBytes;
        }
        return false;
    }

    // ---------------------------------------------------------------- yardımcılar

    PeerState NewState(Room room, ushort id, bool isHost, IPAddress ip) => new()
    {
        Room = room,
        Id = id,
        IsHost = isHost,
        Address = ip,
        Tokens = RateBurstBytes,
        LastRefillMs = _clock.ElapsedMilliseconds,
    };

    string NewCode()
    {
        Span<char> c = stackalloc char[RelayProtocol.CodeLength];
        string code;
        do
        {
            for (int i = 0; i < c.Length; i++)
                c[i] = RelayProtocol.CodeAlphabet[RandomNumberGenerator.GetInt32(RelayProtocol.CodeAlphabet.Length)];
            code = new string(c);
        } while (_rooms.ContainsKey(code));
        return code;
    }

    void NoteJoinFail(IPAddress ip, long now)
    {
        if (_joinFails.TryGetValue(ip, out var f) && now - f.StartMs < 60_000)
            _joinFails[ip] = (f.Count + 1, f.StartMs);
        else
            _joinFails[ip] = (1, now);
    }

    static void Reject(ConnectionRequest req, RelayError error) => req.Reject(new[] { (byte)error });

    void WriteStats(long now)
    {
        double seconds = Math.Max(1, (now - _lastStatsMs) / 1000.0);
        long bytesIn = (long)_net.Statistics.BytesReceived;
        long bytesOut = (long)_net.Statistics.BytesSent;
        double kbIn = (bytesIn - _lastBytesIn) / 1024.0 / seconds;
        double kbOut = (bytesOut - _lastBytesOut) / 1024.0 / seconds;
        _lastBytesIn = bytesIn;
        _lastBytesOut = bytesOut;
        _lastStatsMs = now;

        // Eski hatalı deneme kayıtlarını temizle.
        var stale = new List<IPAddress>();
        foreach (var kv in _joinFails)
            if (now - kv.Value.StartMs >= 60_000) stale.Add(kv.Key);
        foreach (IPAddress ip in stale) _joinFails.Remove(ip);

        string line = $"Durum: {_rooms.Count} oda, {_net.ConnectedPeersCount} bağlantı, giriş {kbIn:0.0} KB/sn, çıkış {kbOut:0.0} KB/sn";
        // Boşta aynı satırı her dakika tekrar yazma.
        if (line != _lastStatsLine || _rooms.Count > 0)
            Log(line);
        _lastStatsLine = line;
    }

    static void Log(string message) => Console.WriteLine(message);
}
