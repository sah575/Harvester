using System.Diagnostics;
using Harvester.Net;
using LiteNetLib;
using LiteNetLib.Utils;

// Relay sunucusunu uçtan uca deneyen test istemcisi. Tek süreçte bir host ve birkaç istemci açar.
// Kullanım:
//   dotnet run -- selftest [adres] [port]          oda, katılma, veri, atma, kapanma senaryoları
//   dotnet run -- bench <oda> <saniye> [adres] [port]   4 kişilik odalarda 30 Hz trafikle yük testi
string mode = args.Length > 0 ? args[0] : "selftest";
return mode switch
{
    "selftest" => SelfTest.Run(Arg(1, "127.0.0.1"), int.Parse(Arg(2, RelayProtocol.DefaultPort.ToString()))),
    "bench" => Bench.Run(int.Parse(Arg(1, "10")), int.Parse(Arg(2, "30")), Arg(3, "127.0.0.1"), int.Parse(Arg(4, RelayProtocol.DefaultPort.ToString()))),
    _ => 2,
};

string Arg(int i, string fallback) => args.Length > i ? args[i] : fallback;

/// <summary>Relay'e bağlanan tek bir sanal oyuncu.</summary>
sealed class TestPeer
{
    public readonly string Name;
    public readonly NetManager Net;
    public NetPeer? Server;
    public bool IsHost;
    public bool Connected;
    public bool Disconnected;
    public DisconnectReason Reason;
    public RelayError Error = RelayError.None;
    public string? Code;
    public ushort Id;
    public readonly List<ushort> Joined = new(), Left = new();
    public readonly List<(ushort From, byte[] Data, DeliveryMethod Method)> Received = new();

    /// <summary>Host: gelen güvenilmez veriyi gönderene geri yollar (gecikme ölçümü için).</summary>
    public bool Echo;
    /// <summary>İstemci: geri dönen zaman damgalarından ölçülen gidiş-dönüş süreleri (ms).</summary>
    public readonly List<double> Rtts = new();
    public int Sent;
    public bool KeepData = true;

    readonly NetDataWriter _w = new();

    public TestPeer(string name)
    {
        Name = name;
        var listener = new EventBasedNetListener();
        Net = new NetManager(listener) { AutoRecycle = true, IPv6Enabled = false, DisconnectTimeout = RelayProtocol.DisconnectTimeoutMs };
        listener.PeerConnectedEvent += _ => Connected = true;
        listener.PeerDisconnectedEvent += (_, info) =>
        {
            Disconnected = true;
            Reason = info.Reason;
            if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
                Error = (RelayError)info.AdditionalData.GetByte();
        };
        listener.NetworkReceiveEvent += OnReceive;
        Net.Start();
    }

    public void Create(string address, int port, byte maxPlayers)
    {
        IsHost = true;
        var w = Header(RelayConnectOp.Create);
        w.Put(maxPlayers);
        Server = Net.Connect(address, port, w);
    }

    public void Join(string address, int port, string code)
    {
        var w = Header(RelayConnectOp.Join);
        w.Put(code);
        Server = Net.Connect(address, port, w);
    }

    static NetDataWriter Header(RelayConnectOp op)
    {
        var w = new NetDataWriter();
        w.Put(RelayProtocol.Magic);
        w.Put(RelayProtocol.Version);
        w.Put((byte)op);
        return w;
    }

    public void Send(ushort to, byte[] data, DeliveryMethod method)
    {
        _w.Reset();
        _w.Put((byte)RelayMsg.Data);
        if (IsHost) _w.Put(to);
        _w.Put(data);
        Server!.Send(_w, method);
    }

    public void Kick(ushort id)
    {
        _w.Reset();
        _w.Put((byte)RelayMsg.Kick);
        _w.Put(id);
        Server!.Send(_w, DeliveryMethod.ReliableOrdered);
    }

    void OnReceive(NetPeer peer, NetPacketReader r, byte channel, DeliveryMethod method)
    {
        switch ((RelayMsg)r.GetByte())
        {
            case RelayMsg.RoomCreated: Code = r.GetString(); break;
            case RelayMsg.Joined: Id = r.GetUShort(); Code = r.GetString(); break;
            case RelayMsg.PeerJoined: Joined.Add(r.GetUShort()); break;
            case RelayMsg.PeerLeft: Left.Add(r.GetUShort()); break;
            case RelayMsg.Data:
                ushort from = IsHost ? r.GetUShort() : (ushort)0;
                byte[] data = r.GetRemainingBytes();
                if (Echo && method == DeliveryMethod.Unreliable && data.Length == 16 && data[0] == 0xEE)
                {
                    Send(from, data, method);
                    return;
                }
                if (!IsHost && data.Length == 16 && method == DeliveryMethod.Unreliable && data[0] == 0xEE)
                {
                    long sentTicks = BitConverter.ToInt64(data, 8);
                    Rtts.Add((Stopwatch.GetTimestamp() - sentTicks) * 1000.0 / Stopwatch.Frequency);
                    return;
                }
                if (KeepData) Received.Add((from, data, method));
                break;
        }
    }

    /// <summary>Gecikme ölçüm paketi: [0xEE][..][zaman damgası].</summary>
    public void SendPing()
    {
        var p = new byte[16];
        p[0] = 0xEE;
        BitConverter.TryWriteBytes(p.AsSpan(8), Stopwatch.GetTimestamp());
        Send(0, p, DeliveryMethod.Unreliable);
        Sent++;
    }

    public void Close() => Net.Stop(true);
}

static class Util
{
    public static readonly List<TestPeer> All = new();
    public static int Failures;

    public static TestPeer Peer(string name)
    {
        var p = new TestPeer(name);
        All.Add(p);
        return p;
    }

    public static void PollAll()
    {
        foreach (var p in All)
        {
            p.Net.PollEvents();
            p.Net.TriggerUpdate();
        }
    }

    public static bool WaitFor(Func<bool> condition, int timeoutMs = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            PollAll();
            if (condition()) return true;
            Thread.Sleep(1);
        }
        PollAll();
        return condition();
    }

    public static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { PollAll(); Thread.Sleep(1); }
    }

    public static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "[TAMAM] " : "[HATA]  ") + what);
        if (!ok) Failures++;
    }

    public static string Stats(List<double> v)
    {
        if (v.Count == 0) return "ölçüm yok";
        var s = v.OrderBy(x => x).ToList();
        double P(double q) => s[Math.Min(s.Count - 1, (int)(q * s.Count))];
        return $"p50 {P(0.5):0.0} ms, p95 {P(0.95):0.0} ms, en kötü {s[^1]:0.0} ms";
    }
}

static class SelfTest
{
    public static int Run(string address, int port)
    {
        Console.WriteLine($"Relay testi: {address}:{port}");

        // 1. Oda kur
        var host = Util.Peer("host");
        host.Create(address, port, 4);
        Util.Check(Util.WaitFor(() => host.Code != null), $"Oda kuruldu, kod: {host.Code}");
        if (host.Code == null) return 1;
        Util.Check(RelayProtocol.IsValidCode(host.Code), "Kod geçerli biçimde");

        // 2. Yanlış kod
        var wrong = Util.Peer("yanlış");
        wrong.Join(address, port, "ZZZZZZ" == host.Code ? "YYYYYY" : "ZZZZZZ");
        Util.Check(Util.WaitFor(() => wrong.Disconnected) && wrong.Error == RelayError.RoomNotFound,
            $"Yanlış kod reddedildi ({wrong.Error})");

        // 3. Üç istemci katılır (biri küçük harf ve tireyle yazıyor)
        var c1 = Util.Peer("c1");
        var c2 = Util.Peer("c2");
        var c3 = Util.Peer("c3");
        c1.Join(address, port, host.Code);
        c2.Join(address, port, host.Code.ToLowerInvariant());
        c3.Join(address, port, host.Code[..3] + "-" + host.Code[3..]);
        var clients = new[] { c1, c2, c3 };
        Util.Check(Util.WaitFor(() => clients.All(c => c.Code != null) && host.Joined.Count == 3),
            $"3 istemci katıldı, host'a bildirildi (id: {string.Join(",", host.Joined)})");

        // 4. Dolu oda
        var extra = Util.Peer("fazla");
        extra.Join(address, port, host.Code);
        Util.Check(Util.WaitFor(() => extra.Disconnected) && extra.Error == RelayError.RoomFull,
            $"Dolu odaya giriş reddedildi ({extra.Error})");

        // 5. Sıralı güvenilir veri iki yönde
        const int N = 200;
        for (int i = 0; i < N; i++)
        {
            c1.Send(0, BitConverter.GetBytes(i), DeliveryMethod.ReliableOrdered);
            host.Send(c2.Id, BitConverter.GetBytes(i), DeliveryMethod.ReliableOrdered);
        }
        Util.WaitFor(() => host.Received.Count(x => x.From == c1.Id) == N && c2.Received.Count == N);
        var fromC1 = host.Received.Where(x => x.From == c1.Id).Select(x => BitConverter.ToInt32(x.Data)).ToList();
        var toC2 = c2.Received.Select(x => BitConverter.ToInt32(x.Data)).ToList();
        Util.Check(fromC1.SequenceEqual(Enumerable.Range(0, N)), $"İstemci → host {N} güvenilir mesaj, sırası doğru");
        Util.Check(toC2.SequenceEqual(Enumerable.Range(0, N)), $"Host → istemci {N} güvenilir mesaj, sırası doğru");
        Util.Check(c1.Received.Count == 0 && c3.Received.Count == 0, "Veri sadece hedefine gitti");

        // 6. Büyük mesaj (parçalanma) ve sığmayan güvenilmez paket
        var big = new byte[60_000];
        new Random(7).NextBytes(big);
        host.Send(c3.Id, big, DeliveryMethod.ReliableOrdered);
        Util.Check(Util.WaitFor(() => c3.Received.Count == 1) && c3.Received[0].Data.SequenceEqual(big),
            "60 KB'lık mesaj bozulmadan ulaştı");

        host.Received.Clear();
        int maxUnreliable = c3.Server!.GetMaxSinglePacketSize(DeliveryMethod.Unreliable) - 1;
        var edge = new byte[maxUnreliable];
        new Random(8).NextBytes(edge);
        c3.Send(0, edge, DeliveryMethod.Unreliable);
        Util.Check(Util.WaitFor(() => host.Received.Count == 1) && host.Received[0].Data.SequenceEqual(edge),
            $"Sınırda güvenilmez paket ({maxUnreliable} B) relay başlığıyla da ulaştı");

        // 7. Gecikme: 3 istemci 10 sn boyunca 30 Hz paket yollar, host geri yansıtır
        host.Echo = true;
        Console.WriteLine("Gecikme ölçülüyor (10 sn, 30 Hz)...");
        var sw = Stopwatch.StartNew();
        long next = 0;
        while (sw.ElapsedMilliseconds < 10_000)
        {
            if (sw.ElapsedMilliseconds >= next)
            {
                foreach (var c in clients) c.SendPing();
                next += 33;
            }
            Util.PollAll();
            Thread.Sleep(1);
        }
        Util.Pump(500);
        host.Echo = false;
        foreach (var c in clients)
        {
            double loss = 100.0 * (c.Sent - c.Rtts.Count) / c.Sent;
            Console.WriteLine($"        {c.Name}: istemci→relay→host→relay→istemci {Util.Stats(c.Rtts)}, kayıp %{loss:0.0}");
        }
        Util.Check(clients.All(c => c.Rtts.Count > c.Sent * 0.9), "Gecikme paketlerinin %90'ından fazlası döndü");

        // 8. Atma
        host.Kick(c3.Id);
        Util.Check(Util.WaitFor(() => c3.Disconnected && host.Left.Contains(c3.Id)) && c3.Error == RelayError.Kicked,
            $"Atılan oyuncu bağlantıyı kaybetti ({c3.Error}), host'a bildirildi");

        // 9. İstemci kendi ayrılır
        c2.Close();
        Util.Check(Util.WaitFor(() => host.Left.Contains(c2.Id)), "Ayrılan istemci host'a bildirildi");

        // 10. Host ayrılınca oda kapanır
        string code = host.Code;
        host.Close();
        Util.Check(Util.WaitFor(() => c1.Disconnected) && c1.Error == RelayError.RoomClosed,
            $"Host ayrılınca kalan istemci çıkarıldı ({c1.Error})");
        var late = Util.Peer("geç");
        late.Join(address, port, code);
        Util.Check(Util.WaitFor(() => late.Disconnected) && late.Error == RelayError.RoomNotFound,
            "Kapanan odanın koduyla artık girilemiyor");

        // 11. Yanlış sürüm
        var old = Util.Peer("eski");
        var w = new NetDataWriter();
        w.Put(RelayProtocol.Magic);
        w.Put((byte)(RelayProtocol.Version + 1));
        w.Put((byte)RelayConnectOp.Create);
        w.Put((byte)4);
        old.Net.Connect(address, port, w);
        Util.Check(Util.WaitFor(() => old.Disconnected) && old.Error == RelayError.VersionMismatch,
            $"Farklı sürüm reddedildi ({old.Error})");

        foreach (var p in Util.All) p.Close();
        Console.WriteLine(Util.Failures == 0 ? "\nBütün testler geçti." : $"\n{Util.Failures} test başarısız.");
        return Util.Failures == 0 ? 0 : 1;
    }
}

static class Bench
{
    /// <summary>
    /// Her odada 1 host + 3 istemci. İstemciler 30 Hz'de 60 baytlık güvenilmez paket yollar (hareket benzeri),
    /// host her istemciye 30 Hz'de 3 oyuncunun durumunu yollar ve gecikme paketlerini yansıtır.
    /// </summary>
    public static int Run(int rooms, int seconds, string address, int port)
    {
        Console.WriteLine($"Yük testi: {rooms} oda × 4 oyuncu, {seconds} sn, {address}:{port}");
        var hosts = new List<TestPeer>();
        var clients = new List<TestPeer>();
        for (int i = 0; i < rooms; i++)
        {
            var h = Util.Peer($"h{i}");
            h.KeepData = false;
            h.Create(address, port, 4);
            hosts.Add(h);
        }
        if (!Util.WaitFor(() => hosts.All(h => h.Code != null), 8000))
        {
            Console.WriteLine("Odalar kurulamadı.");
            return 1;
        }
        foreach (var h in hosts)
        {
            h.Echo = true;
            for (int j = 0; j < 3; j++)
            {
                var c = Util.Peer($"{h.Name}c{j}");
                c.KeepData = false;
                c.Join(address, port, h.Code!);
                clients.Add(c);
            }
        }
        if (!Util.WaitFor(() => clients.All(c => c.Code != null), 8000))
        {
            Console.WriteLine("İstemciler katılamadı.");
            return 1;
        }

        var move = new byte[60];
        var state = new byte[180];
        var sw = Stopwatch.StartNew();
        long next = 0;
        while (sw.ElapsedMilliseconds < seconds * 1000L)
        {
            if (sw.ElapsedMilliseconds >= next)
            {
                foreach (var c in clients)
                {
                    c.Send(0, move, DeliveryMethod.Unreliable);
                    c.SendPing();
                }
                foreach (var h in hosts)
                    foreach (ushort id in h.Joined)
                        h.Send(id, state, DeliveryMethod.Unreliable);
                next += 33;
            }
            Util.PollAll();
            Thread.Sleep(1);
        }
        Util.Pump(500);

        var all = clients.SelectMany(c => c.Rtts).ToList();
        int sent = clients.Sum(c => c.Sent);
        Console.WriteLine($"Gecikme (iki yönlü, iki relay geçişi): {Util.Stats(all)}, kayıp %{100.0 * (sent - all.Count) / sent:0.0}");
        foreach (var p in Util.All) p.Close();
        return 0;
    }
}
