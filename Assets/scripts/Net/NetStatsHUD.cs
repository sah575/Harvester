using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Harvester.Net
{
    /// <summary>
    /// Ağ durumu göstergesi ve gecikme ölçümü. NetworkManager ile aynı objede durur, sahneler arası kalır.
    /// Ping NGO mesajlarıyla ölçülür, yani oyundaki gerçek gecikmeyi (relay, host karesi dahil) gösterir.
    /// F3: ayrıntılar. Editör/development build'de F4: yapay gecikme ön ayarları, F5: harvester tahmini aç/kapa.
    /// Komut satırı: -simLatency 150 -simJitter 20 -simLoss 5 -noExtrapolation
    /// </summary>
    public class NetStatsHUD : MonoBehaviour
    {
        const string PingMessage = "hv_ping";
        const string PongMessage = "hv_pong";
        const float PingInterval = 1f;
        const float Smoothing = 0.3f;

        static readonly (int latency, int jitter, float loss, string label)[] SimPresets =
        {
            (0, 0, 0f, "Kapalı"),
            (100, 10, 0f, "100 ms"),
            (200, 30, 0f, "200 ms ±30"),
            (300, 50, 5f, "300 ms ±50, %5 kayıp"),
        };

        /// <summary>İstemcide host'a gidiş-dönüş süresi (sn). Ölçüm yoksa 0.</summary>
        public static float LocalRtt { get; private set; }

        static readonly Dictionary<ulong, float> clientRtts = new Dictionary<ulong, float>();

        /// <summary>Host'ta bir istemcinin gidiş-dönüş süresi (sn). Ölçüm yoksa 0.</summary>
        public static float ClientRtt(ulong clientId)
        {
            return clientRtts.TryGetValue(clientId, out float rtt) ? rtt : 0f;
        }

        NetworkManager nm;
        CustomMessagingManager registeredOn;
        float nextPing;
        bool showDetails;
        int simPreset;

        long lastBytesIn, lastBytesOut;
        float nextRateSample;
        float kbIn, kbOut;

        GUIStyle style;

        void Awake()
        {
            nm = GetComponent<NetworkManager>();

            var args = System.Environment.GetCommandLineArgs();
            var relay = GetComponent<RelayTransport>();
            if (relay != null && Debug.isDebugBuild)
            {
                relay.SimLatencyMs = IntArg(args, "-simLatency", relay.SimLatencyMs);
                relay.SimJitterMs = IntArg(args, "-simJitter", relay.SimJitterMs);
                relay.SimLossPercent = IntArg(args, "-simLoss", (int)relay.SimLossPercent);
            }
            if (System.Array.IndexOf(args, "-noExtrapolation") >= 0) RemoteExtrapolator.Enabled = false;
        }

        static int IntArg(string[] args, string name, int fallback)
        {
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int value) ? value : fallback;
        }

        void Update()
        {
            if (nm == null) return;

            bool connected = nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && nm.CustomMessagingManager != null;
            if (connected && registeredOn != nm.CustomMessagingManager) Register();
            else if (!connected && registeredOn != null) Unregister();

            if (Input.GetKeyDown(KeyCode.F3)) showDetails = !showDetails;
            if (Debug.isDebugBuild && Input.GetKeyDown(KeyCode.F4)) CycleSimPreset();
            if (Debug.isDebugBuild && Input.GetKeyDown(KeyCode.F5)) RemoteExtrapolator.Enabled = !RemoteExtrapolator.Enabled;

            if (registeredOn == null) return;

            float now = Time.realtimeSinceStartup;
            if (now >= nextPing)
            {
                nextPing = now + PingInterval;
                SendPing();
            }
            if (now >= nextRateSample)
            {
                SampleRates(now);
            }
        }

        // ------------------------------------------------------------ ping

        void Register()
        {
            registeredOn = nm.CustomMessagingManager;
            registeredOn.RegisterNamedMessageHandler(PingMessage, OnPing);
            registeredOn.RegisterNamedMessageHandler(PongMessage, OnPong);
            LocalRtt = 0f;
            clientRtts.Clear();
            nextPing = 0f;
        }

        void Unregister()
        {
            // NGO kapanınca mesaj yöneticisi zaten atılıyor; elle kaldırmak gerekmez.
            registeredOn = null;
            LocalRtt = 0f;
            clientRtts.Clear();
        }

        void SendPing()
        {
            using (var writer = new FastBufferWriter(8, Allocator.Temp))
            {
                writer.WriteValueSafe(Time.realtimeSinceStartupAsDouble);
                if (nm.IsServer)
                {
                    foreach (ulong id in nm.ConnectedClientsIds)
                    {
                        if (id != NetworkManager.ServerClientId)
                            registeredOn.SendNamedMessage(PingMessage, id, writer, NetworkDelivery.Unreliable);
                    }
                }
                else
                {
                    registeredOn.SendNamedMessage(PingMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable);
                }
            }
        }

        void OnPing(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out double sentAt);
            using (var writer = new FastBufferWriter(8, Allocator.Temp))
            {
                writer.WriteValueSafe(sentAt);
                registeredOn.SendNamedMessage(PongMessage, sender, writer, NetworkDelivery.Unreliable);
            }
        }

        void OnPong(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out double sentAt);
            float rtt = (float)(Time.realtimeSinceStartupAsDouble - sentAt);
            if (nm.IsServer)
            {
                clientRtts[sender] = clientRtts.TryGetValue(sender, out float old) && old > 0f ? Mathf.Lerp(old, rtt, Smoothing) : rtt;
            }
            else
            {
                LocalRtt = LocalRtt > 0f ? Mathf.Lerp(LocalRtt, rtt, Smoothing) : rtt;
            }
        }

        // ------------------------------------------------------------ ayrıntılar

        void SampleRates(float now)
        {
            var relay = nm.NetworkConfig.NetworkTransport as RelayTransport;
            if (relay == null) return;
            float seconds = nextRateSample > 0f ? 1f + (now - nextRateSample) : 1f;
            long bytesIn = relay.BytesReceived, bytesOut = relay.BytesSent;
            if (nextRateSample > 0f)
            {
                kbIn = (bytesIn - lastBytesIn) / 1024f / seconds;
                kbOut = (bytesOut - lastBytesOut) / 1024f / seconds;
            }
            lastBytesIn = bytesIn;
            lastBytesOut = bytesOut;
            nextRateSample = now + 1f;
        }

        void CycleSimPreset()
        {
            var relay = GetComponent<RelayTransport>();
            if (relay == null) return;
            simPreset = (simPreset + 1) % SimPresets.Length;
            var p = SimPresets[simPreset];
            relay.SimLatencyMs = p.latency;
            relay.SimJitterMs = p.jitter;
            relay.SimLossPercent = p.loss;
        }

        void OnGUI()
        {
            if (registeredOn == null) return;

            float scale = Screen.height / 1080f;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, richText = true };
            }
            style.fontSize = Mathf.RoundToInt(22 * scale);

            var lines = new System.Text.StringBuilder();
            if (nm.IsServer)
            {
                lines.Append("<color=#C9A227>Host</color>");
                foreach (ulong id in nm.ConnectedClientsIds)
                {
                    if (id == NetworkManager.ServerClientId) continue;
                    lines.Append("\nOyuncu ").Append(id).Append(": ").Append(FormatRtt(ClientRtt(id)));
                }
            }
            else
            {
                lines.Append("Ping ").Append(FormatRtt(LocalRtt));
            }

            var relay = nm.NetworkConfig.NetworkTransport as RelayTransport;
            if (relay != null && relay.SimActive)
            {
                lines.Append("\n<color=#FF9A3C>Yapay gecikme: ").Append(relay.SimLatencyMs).Append(" ms ±").Append(relay.SimJitterMs)
                     .Append(", %").Append(relay.SimLossPercent.ToString("0")).Append(" kayıp</color>");
            }
            if (Debug.isDebugBuild && !RemoteExtrapolator.Enabled)
            {
                lines.Append("\n<color=#FF9A3C>Harvester tahmini kapalı</color>");
            }

            if (showDetails)
            {
                lines.Append("\n<color=#AAAAAA>");
                if (relay != null)
                {
                    lines.Append("Online · relay ").Append(relay.RelayRtt).Append(" ms · kayıp %").Append(relay.PacketLossPercent.ToString("0.0"))
                         .Append("\nGelen ").Append(kbIn.ToString("0.0")).Append(" KB/sn · giden ").Append(kbOut.ToString("0.0")).Append(" KB/sn");
                    if (!string.IsNullOrEmpty(relay.RoomCode)) lines.Append("\nOda ").Append(relay.RoomCode);
                }
                else
                {
                    lines.Append("LAN");
                }
                lines.Append("\nTick ").Append(nm.NetworkConfig.TickRate).Append(" Hz");
                if (Debug.isDebugBuild) lines.Append("\nF4 yapay gecikme · F5 harvester tahmini");
                lines.Append("</color>");
            }

            // Sağ alt köşe: üstteki oyun göstergeleriyle (robot sayısı) çakışmasın. Development build yazısının üstünde.
            float width = 520 * scale, height = 400 * scale;
            var rect = new Rect(Screen.width - width - 16 * scale, Screen.height - height - 40 * scale, width, height);
            // Okunur olsun diye hafif gölge
            var shadow = new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height);
            string text = lines.ToString();
            var color = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(shadow, StripColors(text), style);
            GUI.color = color;
            GUI.Label(rect, text, style);
        }

        static string FormatRtt(float rtt)
        {
            if (rtt <= 0f) return "<color=#AAAAAA>…</color>";
            int ms = Mathf.RoundToInt(rtt * 1000f);
            string color = ms < 80 ? "#7BD88F" : ms < 150 ? "#E8C547" : "#FF6B5B";
            return "<color=" + color + ">" + ms + " ms</color>";
        }

        static string StripColors(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
        }
    }
}
