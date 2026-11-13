using System;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Pisti takip eden basit sürücü: test, bot ve yapay zekâ denemeleri için.
    /// Arabanın girdisini RaceCar.SetExternalInput ile verir; fiziğe dokunmaz, yani oyuncunun arabasıyla aynı kurallarla sürer.
    ///
    /// - Direksiyon: saf takip (pure pursuit). Pistte hıza göre ileride bir hedef nokta seçilir, oraya giden yayın
    ///   eğriliği bisiklet modeliyle direksiyon açısına çevrilir.
    /// - Hız: önümüzdeki fren mesafesi boyunca pistin eğriliğine bakılır; her noktada izin verilen viraj hızı
    ///   (yanal ivme sınırı) ve oraya kadar frenle inilebilecek hız hesaplanır, en küçüğü hedef hız olur.
    /// - Takılma: gaz verildiği hâlde duruyorsa geri vitesle kurtulmayı dener, olmazsa ya da ters dönerse ya da
    ///   pistten çok uzaklaşırsa RaceCar.ResetToTrack ile pist üstüne döner.
    /// Turlu pistte tur sürelerini, ortalama hızı ve bariyere çarpmaları sayar.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarTrackFollower : MonoBehaviour
    {
        [Tooltip("Sürülen araba (boşsa aynı objedeki RaceCar)")]
        [SerializeField] private RaceCar car;
        [Tooltip("Orta çizgiden yana sapma (m, sağ artı). Botları farklı şeritlere koymak için.")]
        public float LaneOffset;
        [Tooltip("Hedef hızın çarpanı (0–1). Zorluk ayarı.")]
        [Range(0.2f, 1f)] public float SpeedScale = 1f;
        [Tooltip("Virajda izin verilen yanal ivme (m/sn²)")]
        public float MaxLateralAccel = 13f;
        [Tooltip("Hedef hıza inmek için hesapta kullanılan fren ivmesi (m/sn²); arabanınkinden biraz düşük güvenli")]
        public float PlanningBrake = 14f;
        [Tooltip("Hedef noktanın en az uzaklığı (m)")]
        public float MinLookahead = 8f;
        [Tooltip("Hedef noktanın hızla artan uzaklığı (sn): uzaklık = en az + hız * bu")]
        public float LookaheadTime = 0.55f;
        [Tooltip("Hedef hızı aşınca el freni çekilsin mi (sadece keskin virajda)")]
        public bool UseHandbrake = false;
        [Tooltip("Araba merkezi yol kenarından bu kadar dışarıdaysa pistten çıkmış sayılır (m)")]
        public float OffTrackMargin = 2f;
        [Tooltip("Tur ve takılma olaylarını konsola yazsın")]
        public bool LogEvents = true;

        public RaceCar Car => car;
        public float TargetSpeed { get; private set; }
        public int Laps { get; private set; }
        public float LastLapTime { get; private set; }
        public float BestLapTime { get; private set; } = float.PositiveInfinity;
        /// <summary>Son turun ortalama hızı (m/sn).</summary>
        public float LastLapAverageSpeed { get; private set; }
        public int WallHits { get; private set; }
        public int Resets { get; private set; }
        public int StuckRecoveries { get; private set; }
        /// <summary>Orta çizgiden görülen en büyük yana sapma (m).</summary>
        public float MaxLateralOffset { get; private set; }

        /// <summary>Tur bittiğinde (tur numarası, süre sn).</summary>
        public event Action<int, float> OnLap;

        private float lapStartTime = -1f;
        private float lapDistance;
        private float lastTrackDistance = -1f;
        private float stuckTimer, flippedTimer, offTrackTimer;
        private float reverseUntil;
        private float reverseSteer;
        private float lastWallHit;

        /// <summary>Arabaya sürücü ekler (yoksa) ve döndürür.</summary>
        public static CarTrackFollower Attach(RaceCar target)
        {
            var follower = target.GetComponent<CarTrackFollower>();
            if (follower == null) follower = target.gameObject.AddComponent<CarTrackFollower>();
            follower.car = target;
            follower.enabled = true;
            return follower;
        }

        private void Awake()
        {
            if (car == null) car = GetComponent<RaceCar>();
        }

        private void OnDisable()
        {
            if (car != null) car.ClearExternalInput();
        }

        /// <summary>Tur istatistiklerini sıfırlar (yarış başında).</summary>
        public void ResetStats()
        {
            Laps = 0;
            LastLapTime = 0f;
            BestLapTime = float.PositiveInfinity;
            WallHits = Resets = StuckRecoveries = 0;
            MaxLateralOffset = 0f;
            lapStartTime = -1f;
            lastTrackDistance = -1f;
        }

        private void FixedUpdate()
        {
            var track = RaceTrack.Current;
            if (car == null || track == null || track.Length <= 0f || !car.DrivesLocally) return;
            if (car.IsEliminated)
            {
                car.SetExternalInput(0f, 0f, false);
                return;
            }

            float dt = Time.fixedDeltaTime;
            Vector3 position = car.transform.position;
            float d = car.TrackDistance;
            float speed = car.ForwardSpeed;
            float lateral = track.GetLateralOffset(position, d);
            MaxLateralOffset = Mathf.Max(MaxLateralOffset, Mathf.Abs(lateral));
            TrackLaps(track, d, speed, dt);

            // --- Kurtarma ---
            if (HandleRecovery(track, lateral, speed, dt)) return;

            // --- Direksiyon: saf takip ---
            float lookahead = MinLookahead + Mathf.Max(0f, speed) * LookaheadTime;
            float lane = Mathf.Clamp(LaneOffset, -track.Width * 0.4f, track.Width * 0.4f);
            Vector3 target = track.GetPoint(d + lookahead) + track.GetRight(d + lookahead) * lane;
            Vector3 local = car.transform.InverseTransformPoint(target);
            local.y = 0f;
            float distance = Mathf.Max(1f, local.magnitude);
            float curvature = 2f * local.x / (distance * distance);
            float steerAngle = Mathf.Atan(curvature * car.WheelBase) * Mathf.Rad2Deg;
            float steer = steerAngle / Mathf.Max(1f, car.CurrentMaxSteerAngle);
            if (local.z < 0f) steer = Mathf.Sign(local.x); // hedef arkada kaldıysa tam kır

            // --- Hız planı ---
            TargetSpeed = PlanSpeed(track, d, speed) * SpeedScale;
            float error = TargetSpeed - speed;
            float throttle;
            bool handbrake = false;
            if (error > 0f) throttle = Mathf.Clamp01(error * 0.6f + 0.3f);
            else if (error > -1.5f) throttle = 0f;
            else
            {
                throttle = Mathf.Clamp(error * 0.25f, -1f, 0f);
                handbrake = UseHandbrake && error < -8f && Mathf.Abs(steer) > 0.7f;
            }

            car.SetExternalInput(throttle, Mathf.Clamp(steer, -1f, 1f), handbrake);
        }

        private float PlanSpeed(RaceTrack track, float d, float speed)
        {
            float top = car.TopSpeed;
            float horizon = Mathf.Max(30f, speed * speed / (2f * PlanningBrake) + 20f);
            float best = top;
            const float step = 4f, span = 8f;
            for (float s = 0f; s <= horizon; s += step)
            {
                Vector3 a = Flat(track.GetForward(d + s - span * 0.5f));
                Vector3 b = Flat(track.GetForward(d + s + span * 0.5f));
                float angle = Vector3.Angle(a, b) * Mathf.Deg2Rad;
                float k = angle / span;
                float corner = k > 1e-4f ? Mathf.Sqrt(MaxLateralAccel / k) : top;
                // Oraya kadar frenle inebileceğimiz hız
                float allowed = Mathf.Sqrt(corner * corner + 2f * PlanningBrake * s);
                if (allowed < best) best = allowed;
            }
            return Mathf.Min(best, top);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private bool HandleRecovery(RaceTrack track, float lateral, float speed, float dt)
        {
            // Geri sayımda (ya da yarış dışı) girdi kilitli: araba gitmez ama bu "takılmak" değil.
            // Sayaçlar işlerse yarış başlar başlamaz geri vitese takar ya da arabayı ışınlar.
            if (!RaceCar.InputEnabled || !GameManager.InputAllowed)
            {
                flippedTimer = offTrackTimer = stuckTimer = 0f;
                reverseUntil = 0f;
                return false;
            }

            // Ters ya da yan yatmış
            if (car.transform.up.y < 0.4f) flippedTimer += dt; else flippedTimer = 0f;
            // Pistten çıkmış: bariyerin iç yüzü kenar çizgisinin ~0.6 m dışında; araba merkezi bunun ötesindeyse bariyeri aşmıştır
            if (Mathf.Abs(lateral) > track.Width * 0.5f + OffTrackMargin) offTrackTimer += dt; else offTrackTimer = 0f;
            if (flippedTimer > 1.5f || offTrackTimer > 1.5f || stuckTimer > 8f)
            {
                if (LogEvents) Debug.Log("[CarFollow] sıfırlama: " + (flippedTimer > 1.5f ? "ters döndü" : offTrackTimer > 1.5f ? "pistten çıktı" : "takıldı") + " (mesafe " + car.TrackDistance.ToString("F0") + ")");
                car.ResetToTrack();
                Resets++;
                flippedTimer = offTrackTimer = stuckTimer = 0f;
                reverseUntil = 0f;
                return true;
            }

            // Geri vitesle kurtulma sürüyor
            if (Time.time < reverseUntil)
            {
                car.SetExternalInput(-1f, reverseSteer, false);
                return true;
            }

            // Gaz veriyoruz ama ilerlemiyoruz
            if (Mathf.Abs(speed) < 1f && TargetSpeed > 3f) stuckTimer += dt;
            else stuckTimer = Mathf.Max(0f, stuckTimer - dt * 2f);
            if (stuckTimer > 2.5f && stuckTimer - dt <= 2.5f)
            {
                // Pistin ortasına dönük olmayan tarafa kırarak geri git
                reverseSteer = lateral > 0f ? 1f : -1f;
                reverseUntil = Time.time + 1.5f;
                StuckRecoveries++;
                if (LogEvents) Debug.Log("[CarFollow] takıldı, geri vites (mesafe " + car.TrackDistance.ToString("F0") + ")");
                return true;
            }
            return false;
        }

        private void TrackLaps(RaceTrack track, float d, float speed, float dt)
        {
            if (lastTrackDistance < 0f)
            {
                lastTrackDistance = d;
                return;
            }
            float delta = d - lastTrackDistance;
            if (track.Loop)
            {
                float half = track.Length * 0.5f;
                if (delta < -half) delta += track.Length;      // başlangıç çizgisini ileri geçti
                else if (delta > half) delta -= track.Length;  // geri geçti
            }
            lastTrackDistance = d;
            if (lapStartTime < 0f)
            {
                lapStartTime = Time.time;
                lapDistance = 0f;
            }
            lapDistance += delta;

            float lapLength = track.Loop ? track.Length : track.FinishDistance - track.StartDistance;
            if (lapDistance >= lapLength)
            {
                Laps++;
                LastLapTime = Time.time - lapStartTime;
                LastLapAverageSpeed = lapLength / Mathf.Max(0.01f, LastLapTime);
                BestLapTime = Mathf.Min(BestLapTime, LastLapTime);
                if (LogEvents)
                    Debug.Log(string.Format("[CarFollow] tur {0}: {1:F2} sn, ort. {2:F1} m/sn ({3:F0} km/sa), bariyer {4}, sıfırlama {5}, takılma {6}, en büyük sapma {7:F1} m",
                        Laps, LastLapTime, LastLapAverageSpeed, LastLapAverageSpeed * 3.6f, WallHits, Resets, StuckRecoveries, MaxLateralOffset));
                OnLap?.Invoke(Laps, LastLapTime);
                lapStartTime = Time.time;
                lapDistance -= lapLength;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.rigidbody != null) return; // başka araba ya da enkaz
            // Zemin değil, yandan çarpma: temas normali yataya yakın ve darbe hissedilir
            Vector3 normal = collision.GetContact(0).normal;
            if (Mathf.Abs(normal.y) > 0.5f || collision.impulse.magnitude < 1500f) return;
            if (Time.time - lastWallHit < 0.5f) return;
            lastWallHit = Time.time;
            WallHits++;
            if (LogEvents) Debug.Log("[CarFollow] bariyere çarptı: " + collision.collider.name + " hız " + car.ForwardSpeed.ToString("F1") + " (mesafe " + car.TrackDistance.ToString("F0") + ")");
        }
    }
}
