using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using Unity.Netcode;
using UnityEngine;

namespace Harvester.Net
{
    /// <summary>
    /// İnternet üzerinden oyun için NGO transport'u. Bütün bağlantılar VDS'teki relay sunucusundan geçer
    /// (Server/HarvesterRelay); kimsenin modemde port açması gerekmez.
    /// Odayı kuran NGO host'u olur ve bir davet kodu alır, diğerleri o kodla katılır.
    /// LAN için aynı NetworkManager üzerindeki UnityTransport kullanılmaya devam eder.
    /// </summary>
    public class RelayTransport : NetworkTransport
    {
        [Tooltip("Relay sunucusunun adresi")]
        public string Address = RelayProtocol.DefaultAddress;
        public ushort Port = RelayProtocol.DefaultPort;
        [Range(RelayProtocol.MinPlayers, RelayProtocol.MaxPlayersLimit)]
        public int MaxPlayers = 4;

        [Header("Test: yapay kötü bağlantı (editör ve development build)")]
        [Tooltip("Gidiş-dönüşe eklenen gecikme (ms). Yarısı giden, yarısı gelen paketlere uygulanır.")]
        public int SimLatencyMs;
        [Tooltip("Her pakete rastgele eklenen 0..N ms (her yönde)")]
        public int SimJitterMs;
        [Tooltip("Güvenilmez paketlerin her yönde kaybolma yüzdesi")]
        [Range(0f, 50f)] public float SimLossPercent;

        public bool SimActive => Debug.isDebugBuild && (SimLatencyMs > 0 || SimJitterMs > 0 || SimLossPercent > 0f);

        /// <summary>Katılmadan (StartClient) önce ayarlanır.</summary>
        public string JoinCode { get; set; }

        /// <summary>Oda kurulunca (host) ya da odaya girilince (istemci) dolar.</summary>
        public string RoomCode { get; private set; }

        /// <summary>Son bağlantı kopmasının sebebi; oyuncuya gösterilecek Türkçe metin. Sorun yoksa null.</summary>
        public string LastErrorMessage { get; private set; }

        /// <summary>Davet kodu belli olunca çağrılır.</summary>
        public event Action<string> RoomReady;

        /// <summary>Relay ile aradaki gidiş-dönüş süresi (ms).</summary>
        public int RelayRtt => relay != null ? relay.RoundTripTime : 0;

        public bool IsRelayConnected => relay != null && relay.ConnectionState == ConnectionState.Connected;

        /// <summary>Relay bağlantısındaki paket kaybı yüzdesi (LiteNetLib ölçümü).</summary>
        public float PacketLossPercent => relay != null ? relay.Statistics.PacketLossPercent : 0f;
        public long BytesSent => net != null ? (long)net.Statistics.BytesSent : 0;
        public long BytesReceived => net != null ? (long)net.Statistics.BytesReceived : 0;

        public override ulong ServerClientId => 0; // relay numaraları 1'den başlar

        const float KickDelay = 0.25f;

        NetManager net;
        NetPeer relay;
        bool isHost;
        bool joined;
        bool polling;
        bool stopRequested;

        // Host tarafı: bağlı istemciler ve ayrılmış olanlar (numaralar tekrar kullanılmaz;
        // ayrılan birinden geç gelen paket yeni bir bağlantı açmasın).
        readonly HashSet<ushort> clients = new HashSet<ushort>();
        readonly HashSet<ushort> departed = new HashSet<ushort>();
        readonly List<KeyValuePair<ushort, float>> pendingKicks = new List<KeyValuePair<ushort, float>>();
        readonly NetDataWriter writer = new NetDataWriter(true, 1500);

        // Yapay gecikme kuyrukları. Güvenilir paketlerin sırası korunur (çıkış zamanı hiç geriye gitmez).
        struct SimPacket
        {
            public byte[] Data;
            public DeliveryMethod Method;
            public ushort From;
            public float Due;
        }
        readonly List<SimPacket> simOutgoing = new List<SimPacket>();
        readonly List<SimPacket> simIncoming = new List<SimPacket>();
        float lastReliableOutDue, lastReliableInDue;

        public override void Initialize(NetworkManager networkManager = null) { }

        public override bool StartServer()
        {
            isHost = true;
            return Connect(RelayConnectOp.Create);
        }

        public override bool StartClient()
        {
            isHost = false;
            return Connect(RelayConnectOp.Join);
        }

        bool Connect(RelayConnectOp op)
        {
            StopNet();
            RoomCode = null;
            LastErrorMessage = null;
            joined = false;
            clients.Clear();
            departed.Clear();
            pendingKicks.Clear();
            simOutgoing.Clear();
            simIncoming.Clear();

            string code = RelayProtocol.NormalizeCode(JoinCode);
            if (op == RelayConnectOp.Join && !RelayProtocol.IsValidCode(code))
            {
                LastErrorMessage = "Davet kodu " + RelayProtocol.CodeLength + " karakter olmalı.";
                return false;
            }

            var listener = new EventBasedNetListener();
            listener.PeerDisconnectedEvent += OnRelayDisconnected;
            listener.NetworkReceiveEvent += OnReceive;
            net = new NetManager(listener)
            {
                AutoRecycle = true,
                IPv6Enabled = false,
                UnconnectedMessagesEnabled = false,
                DisconnectTimeout = RelayProtocol.DisconnectTimeoutMs,
                PingInterval = 1000,
                UpdateTime = 15,
                EnableStatistics = true,
            };
            if (!net.Start())
            {
                net = null;
                LastErrorMessage = "Ağ başlatılamadı.";
                return false;
            }

            writer.Reset();
            writer.Put(RelayProtocol.Magic);
            writer.Put(RelayProtocol.Version);
            writer.Put((byte)op);
            if (op == RelayConnectOp.Create) writer.Put((byte)Mathf.Clamp(MaxPlayers, RelayProtocol.MinPlayers, RelayProtocol.MaxPlayersLimit));
            else writer.Put(code);

            try
            {
                relay = net.Connect(Address, Port, writer);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Relay adresi çözülemedi: " + e.Message);
                relay = null;
            }
            if (relay == null)
            {
                StopNet();
                LastErrorMessage = "Sunucu adresi geçersiz.";
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------ gönderme

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            if (!IsRelayConnected) return;
            if (isHost && !clients.Contains((ushort)clientId)) return;

            writer.Reset();
            writer.Put((byte)RelayMsg.Data);
            if (isHost) writer.Put((ushort)clientId);
            writer.Put(payload.Array, payload.Offset, payload.Count);

            DeliveryMethod method = Map(networkDelivery);
            // Tek pakete sığmayan güvenilmez veri parçalanabilen güvenilir kanala geçer
            // (bağlantının başında MTU küçük; relay de başlık ekliyor).
            if ((method == DeliveryMethod.Unreliable || method == DeliveryMethod.Sequenced)
                && writer.Length + RelayProtocol.HeaderReserve > relay.GetMaxSinglePacketSize(method))
            {
                method = method == DeliveryMethod.Unreliable ? DeliveryMethod.ReliableUnordered : DeliveryMethod.ReliableOrdered;
            }

            if (SimActive)
            {
                if (IsUnreliable(method) && UnityEngine.Random.value * 100f < SimLossPercent) return;
                simOutgoing.Add(new SimPacket { Data = writer.CopyData(), Method = method, Due = SimDue(method, ref lastReliableOutDue) });
                return;
            }
            relay.Send(writer, method);
        }

        static bool IsUnreliable(DeliveryMethod method)
        {
            return method == DeliveryMethod.Unreliable || method == DeliveryMethod.Sequenced;
        }

        float SimDue(DeliveryMethod method, ref float lastReliableDue)
        {
            float due = Time.realtimeSinceStartup + (SimLatencyMs * 0.5f + UnityEngine.Random.Range(0, SimJitterMs + 1)) * 0.001f;
            if (!IsUnreliable(method))
            {
                due = Mathf.Max(due, lastReliableDue);
                lastReliableDue = due;
            }
            return due;
        }

        void FlushSimOutgoing()
        {
            if (simOutgoing.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            int write = 0;
            for (int i = 0; i < simOutgoing.Count; i++)
            {
                var packet = simOutgoing[i];
                if (packet.Due <= now)
                {
                    if (IsRelayConnected) relay.Send(packet.Data, packet.Method);
                }
                else
                {
                    simOutgoing[write++] = packet;
                }
            }
            simOutgoing.RemoveRange(write, simOutgoing.Count - write);
        }

        void FlushSimIncoming()
        {
            if (simIncoming.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            int write = 0;
            for (int i = 0; i < simIncoming.Count; i++)
            {
                var packet = simIncoming[i];
                if (packet.Due > now)
                {
                    simIncoming[write++] = packet;
                    continue;
                }
                DeliverData(packet.From, new ArraySegment<byte>(packet.Data), now);
            }
            simIncoming.RemoveRange(write, simIncoming.Count - write);
        }

        void DeliverData(ushort from, ArraySegment<byte> data, float now)
        {
            if (isHost)
            {
                if (departed.Contains(from)) return;
                // Veri "katıldı" bildiriminden önce gelebilir (farklı kanallar): bağlantıyı burada aç.
                EnsureClient(from, now);
                InvokeOnTransportEvent(NetworkEvent.Data, from, data, now);
            }
            else if (joined)
            {
                InvokeOnTransportEvent(NetworkEvent.Data, ServerClientId, data, now);
            }
        }

        static DeliveryMethod Map(NetworkDelivery delivery)
        {
            switch (delivery)
            {
                case NetworkDelivery.Unreliable: return DeliveryMethod.Unreliable;
                case NetworkDelivery.UnreliableSequenced: return DeliveryMethod.Sequenced;
                case NetworkDelivery.Reliable: return DeliveryMethod.ReliableUnordered;
                default: return DeliveryMethod.ReliableOrdered; // ReliableSequenced, ReliableFragmentedSequenced
            }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            // Olaylar OnEarlyUpdate içinde InvokeOnTransportEvent ile veriliyor.
            clientId = 0;
            payload = default;
            receiveTime = Time.realtimeSinceStartup;
            return NetworkEvent.Nothing;
        }

        protected override void OnEarlyUpdate()
        {
            if (net == null) return;

            polling = true;
            try
            {
                net.PollEvents();
                FlushSimIncoming();
            }
            finally
            {
                polling = false;
            }
            FlushSimOutgoing();

            float now = Time.realtimeSinceStartup;
            for (int i = pendingKicks.Count - 1; i >= 0; i--)
            {
                if (pendingKicks[i].Value > now) continue;
                SendKick(pendingKicks[i].Key);
                pendingKicks.RemoveAt(i);
            }

            if (stopRequested) StopNet();
        }

        protected override void OnPostLateUpdate()
        {
            // NGO bu karenin mesajlarını az önce verdi: LiteNetLib'in bir sonraki turunu beklemeden gönder.
            if (net == null) return;
            FlushSimOutgoing();
            net.TriggerUpdate();
        }

        // ------------------------------------------------------------ alma

        void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            if (peer != relay || !reader.TryGetByte(out byte type)) return;
            float now = Time.realtimeSinceStartup;
            ushort id;
            string code;

            switch ((RelayMsg)type)
            {
                case RelayMsg.RoomCreated:
                    if (isHost && reader.TryGetString(out code))
                    {
                        RoomCode = code;
                        RoomReady?.Invoke(code);
                    }
                    break;

                case RelayMsg.Joined:
                    if (!isHost && !joined && reader.TryGetUShort(out id) && reader.TryGetString(out code))
                    {
                        joined = true;
                        RoomCode = code;
                        RoomReady?.Invoke(code);
                        InvokeOnTransportEvent(NetworkEvent.Connect, ServerClientId, default, now);
                    }
                    break;

                case RelayMsg.PeerJoined:
                    if (isHost && reader.TryGetUShort(out id)) EnsureClient(id, now);
                    break;

                case RelayMsg.PeerLeft:
                    if (isHost && reader.TryGetUShort(out id))
                    {
                        departed.Add(id);
                        simIncoming.RemoveAll(p => p.From == id);
                        if (clients.Remove(id))
                        {
                            SetDisconnectEvent(DisconnectEvents.ClosedByRemote);
                            InvokeOnTransportEvent(NetworkEvent.Disconnect, id, default, now);
                        }
                    }
                    break;

                case RelayMsg.Data:
                    id = 0;
                    if (isHost && !reader.TryGetUShort(out id)) return;
                    if (SimActive)
                    {
                        if (IsUnreliable(method) && UnityEngine.Random.value * 100f < SimLossPercent) return;
                        var data = new byte[reader.AvailableBytes];
                        Buffer.BlockCopy(reader.RawData, reader.Position, data, 0, data.Length);
                        simIncoming.Add(new SimPacket { Data = data, Method = method, From = id, Due = SimDue(method, ref lastReliableInDue) });
                        return;
                    }
                    DeliverData(id, new ArraySegment<byte>(reader.RawData, reader.Position, reader.AvailableBytes), now);
                    break;
            }
        }

        void EnsureClient(ushort id, float now)
        {
            if (departed.Contains(id) || !clients.Add(id)) return;
            InvokeOnTransportEvent(NetworkEvent.Connect, id, default, now);
        }

        void OnRelayDisconnected(NetPeer peer, DisconnectInfo info)
        {
            if (peer != relay) return;
            relay = null;
            simOutgoing.Clear();
            simIncoming.Clear();

            var error = RelayError.None;
            if (info.AdditionalData != null && info.AdditionalData.AvailableBytes > 0)
                error = (RelayError)info.AdditionalData.GetByte();

            DisconnectEvents kind;
            if (error != RelayError.None)
            {
                LastErrorMessage = RelayProtocol.Describe(error);
                kind = DisconnectEvents.ClosedByRemote;
            }
            else
            {
                switch (info.Reason)
                {
                    case DisconnectReason.ConnectionFailed:
                        LastErrorMessage = "Sunucuya ulaşılamadı. İnternet bağlantını kontrol et.";
                        kind = DisconnectEvents.MaxConnectionAttempts;
                        break;
                    case DisconnectReason.Timeout:
                        LastErrorMessage = "Sunucuyla bağlantı zaman aşımına uğradı.";
                        kind = DisconnectEvents.ProtocolTimeout;
                        break;
                    case DisconnectReason.DisconnectPeerCalled:
                        LastErrorMessage = null;
                        kind = DisconnectEvents.TransportShutdown;
                        break;
                    default:
                        LastErrorMessage = "Sunucuyla bağlantı koptu.";
                        kind = DisconnectEvents.Disconnected;
                        break;
                }
            }
            SetDisconnectEvent(kind, LastErrorMessage);
            if (LastErrorMessage != null) Debug.Log($"Relay bağlantısı kapandı: {info.Reason} {error} → {LastErrorMessage}");

            float now = Time.realtimeSinceStartup;
            if (isHost)
            {
                // Relay giderse bütün oda gider.
                clients.Clear();
                InvokeOnTransportEvent(NetworkEvent.TransportFailure, ServerClientId, default, now);
            }
            else
            {
                InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, default, now);
            }
        }

        // ------------------------------------------------------------ bağlantı yönetimi

        public override void DisconnectRemoteClient(ulong clientId)
        {
            ushort id = (ushort)clientId;
            if (!isHost || !clients.Remove(id)) return;
            departed.Add(id);
            // NGO önce ayrılma sebebini yolluyor; relay o paketi iletmeden oyuncuyu düşürmesin diye biraz bekle.
            pendingKicks.Add(new KeyValuePair<ushort, float>(id, Time.realtimeSinceStartup + KickDelay));
        }

        void SendKick(ushort id)
        {
            if (!IsRelayConnected) return;
            writer.Reset();
            writer.Put((byte)RelayMsg.Kick);
            writer.Put(id);
            relay.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public override void DisconnectLocalClient()
        {
            Shutdown();
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            // NGO saat eşitlemesi ve NetworkTransform'un yorumlama tamponu bu değere dayanıyor; düşük kalırsa
            // istemcinin saati ileri kayar, yorumlayıcı durum bulamayıp konumdan konuma sıçrar.
            // Bu yüzden NGO mesajlarıyla ölçülen gerçek gidiş-dönüşü (relay iki yön + host'un kare beklemesi) veriyoruz.
            float measured = isHost ? NetStatsHUD.ClientRtt(clientId) : NetStatsHUD.LocalRtt;
            if (measured > 0f) return (ulong)Mathf.RoundToInt(measured * 1000f);
            // Ölçüm yoksa: yol iki bacaklı (biz → relay → karşı taraf), karşı bacağı eşit say, bir kare de ekle.
            return relay != null ? (ulong)(relay.RoundTripTime * 2 + 16) : 0;
        }

        public override void Shutdown()
        {
            if (polling)
            {
                stopRequested = true;
                return;
            }
            StopNet();
        }

        void StopNet()
        {
            stopRequested = false;
            if (net == null) return;
            foreach (var kick in pendingKicks) SendKick(kick.Key);
            pendingKicks.Clear();
            FlushSimOutgoing(); // kapanmadan önce bekleyen paketleri yolla (ör. ayrılma mesajları)
            simOutgoing.Clear();
            simIncoming.Clear();
            relay = null; // kendi kapattığımızda kopma olayı NGO'ya gitmesin
            net.Stop(true);
            net = null;
            clients.Clear();
            joined = false;
        }

        void OnDestroy()
        {
            StopNet();
        }
    }
}
