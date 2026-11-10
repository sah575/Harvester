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
        public float MaxLookahead = 0.6f;
        [Tooltip("Tahminin konumu en fazla kaç metre öteleyebileceği")]
        public float MaxOffset = 12f;
        [Tooltip("Tahminin dönüşü en fazla kaç derece çevirebileceği")]
        public float MaxYawOffset = 45f;
        [Tooltip("Gösterilen konum ağdakinden bu kadar uzaklaşırsa (ışınlanma) süzgeç sıfırlanır")]
        public float SnapDistance = 8f;

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
            if (networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner)
            {
                active = false;
                return;
            }

            // NetworkTransform bu karede yazmadıysa (araç duruyor, yeni durum yok) ağdaki konum öncekiyle aynı.
            bool untouched = active && transform.position == lastWrittenPosition && transform.rotation == lastWrittenRotation;
            Vector3 basePosition = untouched ? lastBasePosition : transform.position;
            Quaternion baseRotation = untouched ? lastBaseRotation : transform.rotation;
            lastBasePosition = basePosition;
            lastBaseRotation = baseRotation;

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

            float lookahead = Enabled ? Mathf.Min(MaxLookahead, Strength * EstimatedDelay()) : 0f;
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

        void OnDisable()
        {
            // Devre dışı kalırken ağdaki konuma dön ki NetworkTransform temiz konumdan devam etsin
            if (active && transform.position == lastWrittenPosition)
                transform.SetPositionAndRotation(lastBasePosition, lastBaseRotation);
            active = false;
        }
    }
}
