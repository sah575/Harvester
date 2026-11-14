using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Harvester.Net
{
    /// <summary>
    /// Başka bir oyuncunun sürdüğü aracı yumuşak ve gecikmesi telafi edilmiş gösterir.
    ///
    /// 1. Yumuşatma: NGO durumları tick zamanıyla damgalıyor ama sahibi onları tick'ten 0–1 kare sonra
    ///    örnekliyor. 17 m/sn'de bu her durumda 0–35 cm gürültü demek ve araç titriyor. NetworkTransform'un
    ///    verdiği konum bir alfa-beta süzgecinden geçer: konum ve hız birlikte izlenir, gürültü süzülür,
    ///    sabit hızda gecikme eklenmez.
    /// 2. İleri tahmin (dead reckoning): gördüğümüz konum gerçeğin ağ gecikmesi + yorumlama tamponu kadar
    ///    gerisinde. Süzgecin hızıyla o kadar ileri gösterilir. Harvester ağır ve öngörülebilir olduğu için
    ///    işe yarar; robotlar ani yön değiştirdiği için onlarda kullanılmaz.
    ///
    /// Çarpışmalar da gösterilen konuma göre olur: robot harvester'ı gerçekte olduğu yere yakın görür ve ona çarpar.
    /// NetworkTransform her karede konumu yazar; bu bileşen LateUpdate'te üstüne yazar.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class RemoteExtrapolator : MonoBehaviour
    {
        /// <summary>İleri tahmin açık mı (karşılaştırma için F5 ya da -noExtrapolation). Yumuşatma her zaman açık.</summary>
        public static bool Enabled = true;

        [Tooltip("Konum süzgecinin zaman sabiti (sn). Büyüdükçe daha yumuşak ama düzeltmesi daha yavaş.")]
        public float PositionSmoothing = 0.1f;
        [Tooltip("Tahmin edilen gecikmenin ne kadarı kadar ileri gidilsin (1 = tamamı)")]
        [Range(0f, 1.5f)] public float Strength = 1f;
        [Tooltip("En fazla bu kadar saniye ileri tahmin (yüksek pingde NGO'nun tamponu RTT ile büyüyor)")]
        public float MaxLookahead = 1f;
        [Tooltip("Tahminin konumu en fazla kaç metre öteleyebileceği")]
        public float MaxOffset = 45f;
        [Tooltip("Tahminin dönüşü en fazla kaç derece çevirebileceği")]
        public float MaxYawOffset = 45f;
        [Tooltip("Gösterilen konum ağdakinden bu kadar uzaklaşırsa (ışınlanma) süzgeç sıfırlanır")]
        public float SnapDistance = 8f;

        /// <summary>Son karede uygulanan ileri tahmin süresi (sn). Ölçüm/teşhis için.</summary>
        public float LastLookahead { get; private set; }

        // ------------------------------------------------------------------ Ölçümle kalibrasyon
        // Formülle tahmin (EstimatedDelay) NGO'nun iç tamponlarına, jitter'a ve kayba göre yanılabiliyor
        // (100 ms ±20 ms'de 186 ms eksik kaldı). Sahibi saniyede birkaç kez "şu gerçek saatte şuradaydım" damgası yollar;
        // alıcı ekranda gösterdiği (NetworkTransform'un verdiği) konum geçmişinde o noktaya ne zaman geldiğini bulur.
        // Fark = gösterilen konumun gerçek gecikmesi. Ölçüm varken ileri tahmin bununla yapılır.

        /// <summary>Ölçülen görüntü gecikmesi (sn); ölçüm yoksa NaN.</summary>
        public float MeasuredDelay { get; private set; } = float.NaN;

        const float ProbeInterval = 0.2f;
        const float HistorySeconds = 3f;
        const float ProbeEvaluateAfter = 1.2f;

        static readonly Dictionary<ulong, RemoteExtrapolator> byObjectId = new Dictionary<ulong, RemoteExtrapolator>();
        static readonly Dictionary<ulong, float> measuredByOwner = new Dictionary<ulong, float>();
        static bool probeHooked;

        /// <summary>Bir oyuncunun arabası için ölçülen görüntü gecikmesi (CarContactSolver kullanır).</summary>
        public static bool TryGetMeasuredDelay(ulong ownerClientId, out float delay)
        {
            return measuredByOwner.TryGetValue(ownerClientId, out delay) && !float.IsNaN(delay);
        }

        struct Sample { public double Time; public Vector3 Position; }
        struct Probe { public double Stamp; public Vector3 Position; public double EvaluateAt; }
        readonly List<Sample> history = new List<Sample>();
        readonly List<Probe> pendingProbes = new List<Probe>();
        float nextProbeSend;
        // Son ölçümler: ortancası kullanılır (tek yanlış eşleşme, ör. kalkıştaki ışınlanma, sonucu bozmasın)
        const int MeasureWindow = 7, MeasureMin = 3;
        readonly List<float> recent = new List<float>();
        readonly List<float> sortBuffer = new List<float>();

        void OnEnable()
        {
            if (!probeHooked) { NetStatsHUD.ProbeReceived += OnProbeReceived; probeHooked = true; }
        }

        static void OnProbeReceived(ulong objectId, double stamp, Vector3 position)
        {
            RemoteExtrapolator ex;
            if (!byObjectId.TryGetValue(objectId, out ex) || ex == null) return;
            ex.pendingProbes.Add(new Probe { Stamp = stamp, Position = position, EvaluateAt = NetStatsHUD.RealServerTime + ProbeEvaluateAfter });
        }

        void Register()
        {
            if (networkObject != null && networkObject.IsSpawned) byObjectId[networkObject.NetworkObjectId] = this;
        }

        void Unregister()
        {
            if (networkObject == null) return;
            RemoteExtrapolator current;
            if (byObjectId.TryGetValue(networkObject.NetworkObjectId, out current) && current == this) byObjectId.Remove(networkObject.NetworkObjectId);
            if (networkObject.IsPlayerObject) measuredByOwner.Remove(networkObject.OwnerClientId);
        }

        void OwnerSendProbe()
        {
            if (Time.unscaledTime < nextProbeSend || !NetStatsHUD.ClockReady) return;
            nextProbeSend = Time.unscaledTime + ProbeInterval;
            NetStatsHUD.SendProbe(networkObject.NetworkObjectId, NetStatsHUD.RealServerTime, transform.position);
        }

        void RecordAndCalibrate(Vector3 basePosition)
        {
            if (!NetStatsHUD.ClockReady) return;
            double now = NetStatsHUD.RealServerTime;
            if (history.Count == 0 || now > history[history.Count - 1].Time)
                history.Add(new Sample { Time = now, Position = basePosition });
            int drop = 0;
            while (drop < history.Count && history[drop].Time < now - HistorySeconds) drop++;
            if (drop > 0) history.RemoveRange(0, drop);

            for (int i = pendingProbes.Count - 1; i >= 0; i--)
            {
                if (pendingProbes[i].EvaluateAt > now) continue;
                Evaluate(pendingProbes[i]);
                pendingProbes.RemoveAt(i);
            }
        }

        void Evaluate(Probe probe)
        {
            // Geçmişte damga konumuna en yakın nokta (doğru parçaları üzerinde)
            float best = float.MaxValue;
            double bestTime = 0;
            float bestSpeed = 0f;
            for (int i = 1; i < history.Count; i++)
            {
                Vector3 a = history[i - 1].Position, b = history[i].Position;
                Vector3 ab = b - a;
                float len2 = ab.sqrMagnitude;
                double segDt = history[i].Time - history[i - 1].Time;
                // Işınlanma/yeniden konumlama parçası: gerçek bir hareket değil, eşleştirme
                if (segDt > 1e-4 && len2 > 90f * 90f * (float)(segDt * segDt)) continue;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(probe.Position - a, ab) / len2) : 0f;
                float d = (a + ab * t - probe.Position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    double dt = history[i].Time - history[i - 1].Time;
                    bestTime = history[i - 1].Time + dt * t;
                    bestSpeed = dt > 1e-4 ? Mathf.Sqrt(len2) / (float)dt : 0f;
                }
            }
            // Duran ya da çok yavaş nesnede eşleşme belirsiz; konum çok uzaksa (ışınlanma, kayıp) ölçme
            if (best > 2.5f * 2.5f || bestSpeed < 4f) return;
            float delay = (float)(bestTime - probe.Stamp);
            if (delay < -0.05f || delay > 1.5f) return;
            delay = Mathf.Max(0f, delay);

            recent.Add(delay);
            if (recent.Count > MeasureWindow) recent.RemoveAt(0);
            if (recent.Count < MeasureMin) return;
            sortBuffer.Clear();
            sortBuffer.AddRange(recent);
            sortBuffer.Sort();
            float median = sortBuffer[sortBuffer.Count / 2];
            MeasuredDelay = float.IsNaN(MeasuredDelay) ? median : Mathf.Lerp(MeasuredDelay, median, 0.3f);
            if (networkObject.IsPlayerObject) measuredByOwner[networkObject.OwnerClientId] = MeasuredDelay;
        }

        NetworkObject networkObject;
        Vector3 lastWrittenPosition;
        Quaternion lastWrittenRotation;
        Vector3 lastBasePosition;
        Quaternion lastBaseRotation;

        // Süzgeç durumu
        Vector3 filteredPosition, filteredVelocity;
        Quaternion filteredRotation;
        float yawRate;
        bool active;

        void Awake()
        {
            networkObject = GetComponent<NetworkObject>();
        }

        void LateUpdate()
        {
            if (networkObject == null || !networkObject.IsSpawned)
            {
                active = false;
                return;
            }
            Register();
            if (networkObject.IsOwner)
            {
                active = false;
                OwnerSendProbe();
                return;
            }

            // NetworkTransform bu karede yazmadıysa (araç duruyor, yeni durum yok) ağdaki konum öncekiyle aynı.
            bool untouched = active && transform.position == lastWrittenPosition && transform.rotation == lastWrittenRotation;
            Vector3 basePosition = untouched ? lastBasePosition : transform.position;
            Quaternion baseRotation = untouched ? lastBaseRotation : transform.rotation;
            lastBasePosition = basePosition;
            lastBaseRotation = baseRotation;
            RecordAndCalibrate(basePosition);

            float dt = Time.deltaTime;
            if (!active || dt <= 0f || (basePosition - filteredPosition).sqrMagnitude > SnapDistance * SnapDistance)
            {
                filteredPosition = basePosition;
                filteredVelocity = Vector3.zero;
                filteredRotation = baseRotation;
                yawRate = 0f;
                active = true;
            }
            else
            {
                // Alfa-beta süzgeci: tahmin et, ölçümle farkın bir kısmı kadar düzelt.
                float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, PositionSmoothing));
                float beta = alpha * alpha / (2f - alpha); // Benedict–Bordner: aşırı salınım yapmayan çift
                Vector3 predicted = filteredPosition + filteredVelocity * dt;
                Vector3 residual = basePosition - predicted;
                filteredPosition = predicted + alpha * residual;
                filteredVelocity += beta / dt * residual;

                // Dönüş: aynı fikir, sadece Y açısı için hız tutuluyor.
                Quaternion predictedRotation = Quaternion.AngleAxis(yawRate * dt, Vector3.up) * filteredRotation;
                float yawResidual = Mathf.DeltaAngle(predictedRotation.eulerAngles.y, baseRotation.eulerAngles.y);
                filteredRotation = Quaternion.Slerp(predictedRotation, baseRotation, alpha);
                yawRate += beta / dt * yawResidual;
            }

            // Ölçüm varsa onu kullan (Strength ile kısılmaz: ölçüm zaten doğru); yoksa formül
            float wanted = !float.IsNaN(MeasuredDelay) ? MeasuredDelay : Strength * EstimatedDelay();
            float lookahead = Enabled ? Mathf.Min(MaxLookahead, wanted) : 0f;
            LastLookahead = lookahead;
            float yawOffset = Mathf.Clamp(yawRate * lookahead, -MaxYawOffset, MaxYawOffset);
            Vector3 offset = Vector3.ClampMagnitude(ArcOffset(filteredVelocity, yawOffset, lookahead), MaxOffset);

            transform.SetPositionAndRotation(filteredPosition + offset, Quaternion.AngleAxis(yawOffset, Vector3.up) * filteredRotation);
            lastWrittenPosition = transform.position;
            lastWrittenRotation = transform.rotation;
        }

        /// <summary>
        /// Araç dönerken düz çizgi yerine yay boyunca ileri tahmin: hız vektörü süre boyunca toplam
        /// <paramref name="totalYaw"/> derece döner. Birkaç adımlık sayısal toplam yeterince doğru ve ucuz.
        /// </summary>
        static Vector3 ArcOffset(Vector3 velocity, float totalYaw, float duration)
        {
            const int Steps = 8;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z); // yükseklik zemine bırakılsın
            if (duration <= 0f) return Vector3.zero;
            if (Mathf.Abs(totalYaw) < 0.5f) return flat * duration;
            Vector3 sum = Vector3.zero;
            float step = duration / Steps;
            for (int i = 0; i < Steps; i++)
                sum += Quaternion.AngleAxis(totalYaw * (i + 0.5f) / Steps, Vector3.up) * flat * step;
            return sum;
        }

        /// <summary>
        /// Gördüğümüz konum gerçeğin ne kadar gerisinde: ağdaki yol + NetworkTransform'un yorumlama tamponu.
        /// Sahip host ise yol tek bacak (host → biz), değilse iki bacak (sahip → host → biz).
        /// </summary>
        float EstimatedDelay()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return 0f;

            float path;
            if (nm.IsServer)
            {
                path = NetStatsHUD.ClientRtt(networkObject.OwnerClientId) * 0.5f;
            }
            else
            {
                float oneWay = NetStatsHUD.LocalRtt * 0.5f;
                path = networkObject.OwnerClientId == NetworkManager.ServerClientId ? oneWay : oneWay * 2f;
            }
            // Tampon: NGO'nun tick gecikmesi kadar tick. Üstüne ölçülen (11 Kasım, iki build + VDS) ek ~2 tick:
            // sahibin durumu tick'ten sonra örneklemesi, karenin sonunda yollaması ve bizim bir sonraki karede işlememiz.
            float tick = 1f / Mathf.Max(1, nm.NetworkConfig.TickRate);
            float buffer = (nm.NetworkTimeSystem.TickLatency + 2f) * tick;
            return path + buffer;
        }

        void OnDestroy()
        {
            Unregister();
        }

        void OnDisable()
        {
            // Devre dışı kalırken ağdaki konuma dön ki NetworkTransform temiz konumdan devam etsin
            if (active && transform.position == lastWrittenPosition)
                transform.SetPositionAndRotation(lastBasePosition, lastBaseRotation);
            active = false;
        }
    }
}
