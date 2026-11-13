using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Kimsenin sürmediği, yapay zekânın sürdüğü harvester: pisti takip eder, önündeki en yakın arabayı kovalar,
    /// tablasına giren arabayı biçer (eler), gövdesine çarpan arabayı savurur.
    ///
    /// Yetki: sadece sunucu karar verir ve sürer. İstemciler NetworkTransform (sunucu otoritesi) +
    /// RemoteExtrapolator ile görür; makara hızı ve hedef NetworkVariable ile gelir. Ağ yokken (NetworkManager yok ya da
    /// dinlemiyor) yerelde sunucu gibi çalışır, tek sahnede test edilebilir.
    ///
    /// Hareket: kinematik Rigidbody + MovePosition/MoveRotation. Yatayda basit bir araç modeli (yön, dönüş hızı, hız)
    /// ivme ve dönüş sınırlarıyla yumuşak sürülür; yükseklik ve eğim tekerlek noktalarından aşağı atılan ışınlarla bulunur.
    /// Bariyerlerin içinde kalması için pistin ortasından yanal sapması sınırlanır.
    ///
    /// Hız (lastik bant): hedef uzaktayken hızlanıp yakalar, yakınken hedefin biraz üstünde ama arabanın son hızının
    /// biraz altında bir tavanla baskı kurar. İyi süren düzlükte biraz açar, hata yapan yakalanır. Ayarlar
    /// <see cref="HarvesterDifficulty"/>'de.
    ///
    /// Biçme ve çarpma, host'ta uzak arabaların gösterilen (RemoteExtrapolator ile tahmini) konumuna göre yapılır:
    /// "sürücünün lehine" kuralı, host ne görüyorsa o.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class HarvesterAI : NetworkBehaviour
    {
        /// <summary>Sahnedeki harvester (izleyici kamerası ve HUD için).</summary>
        public static HarvesterAI Instance { get; private set; }

        /// <summary>Sunucuda (ya da ağsız) bir araba biçildiğinde.</summary>
        public event Action<RaceCar> OnCarCut;
        /// <summary>Sunucuda (ya da ağsız) bir arabaya çarpıldığında (araba, hız değişimi).</summary>
        public event Action<RaceCar, Vector3> OnCarHit;

        [Header("Zorluk")]
        [Tooltip("Seviye ayarları. Boşsa Seviye 1 varsayılanları kullanılır.")]
        [SerializeField] private HarvesterDifficulty difficulty;
        [Tooltip("Ağ yokken (tek sahne testi) kendiliğinden çalışmaya başlasın. Ağda yarış yöneticisi SetRunning(true) çağırır.")]
        [SerializeField] private bool runWhenOffline = true;

        [Header("Görsel (kurucu doldurur)")]
        [Tooltip("Makara (yerel X ekseninde döner)")]
        [SerializeField] private Transform reel;
        [Tooltip("Tabla görselinin kökü; tabla genişliğine göre X'te ölçeklenir")]
        [SerializeField] private Transform headerVisual;
        [Tooltip("Tabla görselinin taban ölçekte genişliği (m)")]
        [SerializeField] private float headerVisualWidth = 14.9f;
        [SerializeField] private Vector3 headerVisualBaseScale = Vector3.one;
        [Tooltip("Tekerlek görselleri: ön sol, ön sağ, arka sol, arka sağ")]
        [SerializeField] private Transform[] wheels = new Transform[4];
        [Tooltip("Tekerlek yarıçapları (m)")]
        [SerializeField] private float[] wheelRadii = { 1.6f, 1.6f, 1.3f, 1.3f };
        [Tooltip("Arka tekerlekler direksiyon görseli için dönsün (biçerdöverler arkadan döner)")]
        [SerializeField] private float maxRearSteerVisual = 30f;

        [Tooltip("Kabin kamerası noktası: sürücünün göz hizası, ileri bakar (izleme kamerası için)")]
        [SerializeField] private Transform cabinView;

        [Header("Gövde (kurucu doldurur)")]
        [Tooltip("Gövde çarpışma kutusu (çarpma tespiti de buna göre)")]
        [SerializeField] private BoxCollider bodyCollider;
        [Tooltip("Tablanın fiziksel kutusu (biçme kutusu bunun önüne göre kurulur)")]
        [SerializeField] private BoxCollider headerCollider;
        [Tooltip("Zemin ışınlarının atıldığı yerel noktalar: ön sol, ön sağ, arka sol, arka sağ (yükseklik önemsiz)")]
        [SerializeField] private Vector3[] groundProbes =
        {
            new Vector3(-3.7f, 0f, 3.8f), new Vector3(3.7f, 0f, 3.8f), new Vector3(-2.9f, 0f, -3.6f), new Vector3(2.9f, 0f, -3.6f)
        };

        [Header("Pist")]
        [Tooltip("Asfalt kenarından bariyerin iç yüzüne kadar pay (m, TrackBuilder.Shoulder)")]
        [SerializeField] private float shoulder = 0.6f;
        [Tooltip("Bariyerle harvester'ın en geniş yeri arasında bırakılan boşluk (m)")]
        [SerializeField] private float barrierClearance = 0.8f;

        [Header("Makara")]
        [Tooltip("Çalışırken boşta makara hızı (derece/sn)")]
        [SerializeField] private float reelIdleSpeed = 90f;
        [Tooltip("Biçme modunda makara hızı (derece/sn)")]
        [SerializeField] private float reelMowSpeed = 420f;
        [Tooltip("Bu hızın üstünde tabla biçer (derece/sn)")]
        [SerializeField] private float reelCutSpeed = 250f;
        [Tooltip("Makara hız değişimi (derece/sn²)")]
        [SerializeField] private float reelAcceleration = 450f;

        [Header("Çarpma tespiti")]
        [Tooltip("Gövde kutusunun çarpma tespiti için şişirilmesi (m)")]
        [SerializeField] private float ramMargin = 0.5f;

        // Ağ: sadece sunucu yazar
        private readonly NetworkVariable<float> netReelSpeed = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<ulong> netTargetId = new NetworkVariable<ulong>(NoTarget);
        private const ulong NoTarget = ulong.MaxValue;

        private Rigidbody body;
        private NetworkTransform networkTransform;
        private HarvesterDifficulty fallbackDifficulty;

        // Sürüş durumu (sunucu)
        private Vector3 position;
        private float yaw, yawRate, speed, verticalSpeed;
        private Quaternion rotation = Quaternion.identity;
        private Vector3 groundNormal = Vector3.up;
        private float aimLateral;
        private float trackDistance;
        private bool hasTrackHint;
        private bool stateInitialized;
        private bool runningSetExplicitly;

        // Hedef (sunucu)
        private RaceCar target;
        private float targetGap = float.PositiveInfinity;
        private float nearestAheadGap = float.PositiveInfinity;
        private float targetSpeedSmoothed; // simetrik süzgeç (av modu tespiti)
        private float targetSpeedRef;      // asimetrik: hızlı yükselir, yavaş düşer (kontrolcünün referansı)
        private float creep;               // baskı sürünmesi: hedeflenen zaman açığından düşülen (sn)
        private float targetPace;          // hedefin yerinde yarış temposu (m/sn)
        private float targetChosenAt = -10f;
        private float punishUntil = -10f;
        private bool launching;
        private float launchStartTime = -100f;
        private RaceCar nearestAheadCar;
        private float lastTeleportTime = -100f;
        private readonly Dictionary<RaceCar, float> carDistanceCache = new Dictionary<RaceCar, float>();
        private readonly float[] lateralHistory = new float[96];
        private readonly float[] lateralHistoryTime = new float[96];
        private int lateralHistoryHead, lateralHistoryCount;

        // Temas (sunucu)
        private readonly Collider[] overlapBuffer = new Collider[32];
        private readonly HashSet<RaceCar> touchedThisStep = new HashSet<RaceCar>();
        private readonly Dictionary<RaceCar, float> lastHitTime = new Dictionary<RaceCar, float>();
        private readonly Dictionary<Rigidbody, RaceCar> carByBody = new Dictionary<Rigidbody, RaceCar>();
        private readonly RaycastHit[] rayBuffer = new RaycastHit[16];

        // Görsel
        private float reelSpeed;
        private float[] wheelSpin = new float[4];
        private Quaternion[] wheelBaseRotations;
        private Vector3 lastVisualPosition;
        private float lastVisualYaw;
        private float visualSpeed, visualYawRate;
        private float appliedHeaderWidth = -1f;

        // ------------------------------------------------------------------ Sunulanlar

        /// <summary>Kullanılan zorluk ayarları (asla null değil).</summary>
        public HarvesterDifficulty Difficulty
        {
            get
            {
                if (difficulty != null) return difficulty;
                if (fallbackDifficulty == null) fallbackDifficulty = HarvesterDifficulty.CreateDefault();
                return fallbackDifficulty;
            }
        }

        /// <summary>Pist üzerindeki ilerleme (m). Her makinede kendi konumundan hesaplanır.</summary>
        public float TrackDistance => trackDistance;
        /// <summary>İleri hız (m/sn). İstemcide konum değişiminden tahmin.</summary>
        public float Speed => Simulates ? speed : visualSpeed;
        /// <summary>Kovalanan araba (yoksa null). İstemcide ağdan gelir.</summary>
        public RaceCar Target => Simulates ? target : ResolveTarget(netTargetId.Value);
        /// <summary>Hedefe pist boyunca açık (m, sadece sunucuda anlamlı; hedef yoksa sonsuz).</summary>
        public float TargetGap => targetGap;
        /// <summary>Çalışıyor mu (sürüyor, biçiyor). Yarış yöneticisi geri sayımda kapalı tutar.</summary>
        public bool Running { get; private set; }
        /// <summary>Kalkış atağı: çalışmaya başladıktan sonra sürüye yetişene dek yüksek ivme ve hız tavanı.</summary>
        public bool Launching => Running && launching;
        /// <summary>Kalkış penceresi: çalışmaya başladıktan sonraki launchWindow saniye (hedefin gerisinde durur, ceza yok).</summary>
        public bool InLaunchWindow => Running && Time.time - launchStartTime < Difficulty.launchWindow;
        /// <summary>Görünmezken yetişme ışınlanması sayısı (test).</summary>
        public int TeleportCount { get; private set; }
        /// <summary>Kabin kamerası noktası (sürücü gözü, ileri bakar). Yoksa null.</summary>
        public Transform CabinView => cabinView;
        /// <summary>Hedefe zaman açığı (sn): tablanın önünden açık / hedef hızı referansı (sunucuda; hedef yoksa sonsuz).</summary>
        public float TimeGap => target != null && targetGap >= -Difficulty.behindTolerance && !float.IsInfinity(targetGap)
            ? EffectiveGap / Mathf.Max(targetSpeedRef, Difficulty.timeGapMinSpeed) : float.PositiveInfinity;
        /// <summary>Şu anki hedef zaman açığı τ* (baskı sürünmesi düşülmüş, sn).</summary>
        public float CurrentTimeGapTarget => Mathf.Max(0f, Difficulty.timeGapTarget - creep);
        /// <summary>Hedef hızı referansı (m/sn, sunucuda).</summary>
        public float TargetSpeedRef => targetSpeedRef;
        /// <summary>Şu anki davranış (HUD ve test için, sunucuda).</summary>
        public string ModeName
        {
            get
            {
                if (!Running) return "durdu";
                if (target == null) return "hedef yok";
                if (InLaunchWindow) return Launching ? "kalkış atağı" : "kalkış";
                if (Punishing) return "av";
                return TimeGap > Difficulty.timeGapBandHigh ? "yakalama" : "baskı (τ* " + CurrentTimeGapTarget.ToString("F2") + " sn)";
            }
        }
        /// <summary>Önündeki en yakın arabaya pist boyunca açık (m, kökten; sunucuda).</summary>
        public float NearestAheadGap => nearestAheadGap;
        /// <summary>Tablanın önünün kök noktasından uzaklığı (m). Açık (gap) kökten ölçülür; baskı mesafesi tablanın önünden.</summary>
        public float HeaderReach => headerCollider != null ? headerCollider.center.z + headerCollider.size.z * 0.5f : 10f;
        /// <summary>Av modu: hedef hata yaptı (yakında ve yavaş), harvester virajda daha sert ve tavansız gelir.</summary>
        public bool Punishing => Running && Time.time < punishUntil;
        /// <summary>Makara dönüş hızı (derece/sn).</summary>
        public float ReelSpeed => reelSpeed;
        /// <summary>Tabla şu an biçiyor mu (makara yeterince hızlı).</summary>
        public bool IsCutting => Running && reelSpeed >= reelCutSpeed;
        /// <summary>Bu makine harvester'ı sürüyor mu: sunucuysa ya da ağ hiç çalışmıyorsa.</summary>
        public bool Simulates
        {
            get
            {
                if (IsSpawned) return IsServer;
                var nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening;
            }
        }

        // Test ölçümleri
        public int CutCount { get; private set; }
        public int HitCount { get; private set; }
        public int BarrierClampSteps { get; private set; }

        /// <summary>Sadece sunucu: çalıştır/durdur. Durunca frenleyip durur, makara durur.</summary>
        public void SetRunning(bool running)
        {
            if (IsSpawned && !IsServer) return;
            if (running && !Running) StartLaunch();
            Running = running;
            runningSetExplicitly = true;
        }

        /// <summary>Sadece sunucu: zorluk seviyesini değiştirir (anında etkili).</summary>
        public void SetDifficulty(HarvesterDifficulty newDifficulty)
        {
            difficulty = newDifficulty;
            ApplyHeaderWidth();
        }

        /// <summary>Sadece sunucu: pistin harvester başlangıç yerine koyar, hızı sıfırlar.</summary>
        public void PlaceAtStart(int racerCount = 4)
        {
            var track = RaceTrack.Current;
            if (track == null) return;
            Pose pose = track.GetHarvesterStartPose(racerCount);
            PlaceAt(pose.position, pose.rotation);
        }

        /// <summary>Sadece sunucu: verilen yere ışınlar, hızı sıfırlar.</summary>
        public void PlaceAt(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (!Simulates) return;
            Vector3 flat = Vector3.ProjectOnPlane(worldRotation * Vector3.forward, Vector3.up);
            yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : 0f;
            position = worldPosition;
            speed = yawRate = verticalSpeed = 0f;
            aimLateral = 0f;
            hasTrackHint = false;
            target = null;
            targetGap = float.PositiveInfinity;
            lateralHistoryCount = 0;
            stateInitialized = true;

            Vector3 grounded, normal;
            if (SampleGround(position, yaw, out grounded, out normal))
            {
                position = grounded;
                groundNormal = normal;
            }
            else groundNormal = Vector3.up;
            rotation = YawOnSlope(yaw, groundNormal);

            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            UpdateTrackDistance();
            if (IsSpawned && networkTransform != null && networkTransform.CanCommitToTransform)
                networkTransform.Teleport(position, rotation, transform.localScale);
        }

        /// <summary>
        /// Arabanın harvester'ın önünde pist boyunca ne kadar ileride olduğu (m). Turlu pistte sarar:
        /// sonuç [-80, Length - 80) aralığında (80 m'ye kadar gerideki eksi). A'dan B'ye pistte geride olan eksi.
        /// </summary>
        public float GapTo(RaceCar car)
        {
            var track = RaceTrack.Current;
            if (track == null || car == null) return float.PositiveInfinity;
            return Gap(track, CarDistance(track, car));
        }

        // ------------------------------------------------------------------ Yaşam döngüsü

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            networkTransform = GetComponent<NetworkTransform>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            wheelBaseRotations = new Quaternion[wheels.Length];
            wheelSpin = new float[wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null) wheelBaseRotations[i] = wheels[i].localRotation;
            lastVisualPosition = transform.position;
            lastVisualYaw = transform.eulerAngles.y;
            ApplyHeaderWidth();
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (!IsSpawned && Simulates)
            {
                InitializeState();
                if (runWhenOffline && !runningSetExplicitly)
                {
                    StartLaunch();
                    Running = true;
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                InitializeState();
            }
            else
            {
                // NetworkTransform konumu doğrudan yazar; Rigidbody yumuşatması onunla çatışmasın.
                body.interpolation = RigidbodyInterpolation.None;
                Running = false;
            }
        }

        private void StartLaunch()
        {
            launching = true;
            launchStartTime = Time.time;
        }

        /// <summary>Kalkış atağının bitip bitmediğine karar verir (hedef seçiminden sonra).</summary>
        private void UpdateLaunch(HarvesterDifficulty diff)
        {
            if (!launching || !Running) return;
            bool timeout = Time.time - launchStartTime >= diff.launchMaxDuration;
            // Baskı mesafesine girince hedefin hızı ne olursa olsun biter; pencerenin geri kalanında hedefin gerisinde durur
            bool caughtUp = target != null && targetGap >= -diff.behindTolerance
                            && targetGap - HeaderReach <= diff.pressureDistance;
            if (timeout || caughtUp) launching = false;
        }

        private void InitializeState()
        {
            if (stateInitialized) return;
            position = transform.position;
            rotation = transform.rotation;
            Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : 0f;
            stateInitialized = true;
        }

        // ------------------------------------------------------------------ Sunucu döngüsü

        private void FixedUpdate()
        {
            if (!Simulates) return;
            InitializeState();
            var track = RaceTrack.Current;
            float dt = Time.fixedDeltaTime;
            var diff = Difficulty;
            if (appliedHeaderWidth != diff.headerWidth) ApplyHeaderWidth();

            if (track != null && track.Length > 0f)
            {
                UpdateTrackDistance();
                UpdateTarget(track, diff);
                UpdateLaunch(diff);
                CheckCatchupTeleport(track, diff);
            }
            else
            {
                target = null;
                targetGap = nearestAheadGap = float.PositiveInfinity;
            }

            UpdateReel(diff, dt);
            Drive(track, diff, dt);
            if (Running) CheckContacts(diff);

            if (IsSpawned)
            {
                ulong id = target != null && target.IsSpawned ? target.NetworkObjectId : NoTarget;
                if (netTargetId.Value != id) netTargetId.Value = id;
                if (Mathf.Abs(netReelSpeed.Value - reelSpeed) > 15f || (reelSpeed == 0f && netReelSpeed.Value != 0f))
                    netReelSpeed.Value = reelSpeed;
            }
        }

        private void UpdateTrackDistance()
        {
            var track = RaceTrack.Current;
            if (track == null) return;
            trackDistance = track.GetNearestDistance(Simulates ? position : transform.position, hasTrackHint ? trackDistance : -1f, 40f);
            hasTrackHint = true;
        }

        /// <summary>Turlu pistte bu kadar gerideki araba "arkada" sayılır, daha gerisi bir tur önde (m).</summary>
        private const float BehindWindow = 80f;
        /// <summary>Yanal hız sönümü (sn): hedef nokta yanal hız × bu kadar geri çekilir.</summary>
        private const float LateralDamping = 0.35f;

        private float Gap(RaceTrack track, float carDistance)
        {
            float g = carDistance - trackDistance;
            if (track.Loop)
            {
                // Arkadaki pencere: bu kadar gerideki araba "arkada" sayılır (bir tur önde değil)
                float back = Mathf.Min(BehindWindow, track.Length * 0.25f);
                g = Mathf.Repeat(g + back, track.Length) - back;
            }
            return g;
        }

        private float CarDistance(RaceTrack track, RaceCar car)
        {
            float hint;
            bool has = carDistanceCache.TryGetValue(car, out hint);
            float d = track.GetNearestDistance(car.transform.position, has ? hint : -1f, 30f);
            carDistanceCache[car] = d;
            return d;
        }

        // ------------------------------------------------------------------ Görünmezken yetişme

        /// <summary>Harvester'ın görülebildiği mesafe (m): ayardan ya da sisten.</summary>
        public float VisibleDistance
        {
            get
            {
                var diff = Difficulty;
                if (diff.visibleDistanceOverride > 0f) return diff.visibleDistanceOverride;
                float d = 150f;
                if (RenderSettings.fog)
                {
                    float keep = Mathf.Clamp(diff.fogVisibilityThreshold, 0.01f, 0.99f);
                    switch (RenderSettings.fogMode)
                    {
                        case FogMode.Linear:
                            // Doğrusal sis: görünürlük = (bitiş - d) / (bitiş - başlangıç)
                            d = Mathf.Lerp(RenderSettings.fogEndDistance, RenderSettings.fogStartDistance, keep);
                            break;
                        case FogMode.Exponential:
                            d = RenderSettings.fogDensity > 1e-5f ? -Mathf.Log(keep) / RenderSettings.fogDensity : 400f;
                            break;
                        default: // ExponentialSquared
                            d = RenderSettings.fogDensity > 1e-5f ? Mathf.Sqrt(-Mathf.Log(keep)) / RenderSettings.fogDensity : 400f;
                            break;
                    }
                }
                return Mathf.Max(diff.minVisibleDistance, d);
            }
        }

        /// <summary>
        /// Lastik bant tavanı: önündeki en yakın araba (yoluna çıkacak "son" araba) maxGap'ten uzaktaysa ve hiçbir araba
        /// harvester'ı göremiyorsa (hepsi görüş mesafesinin dışında), o arabanın maxGap gerisine ışınlanır. Hedef nokta da
        /// hiçbir arabadan görünmemeli (turlu pistte iç saha karşısından görülebilir); değilse geriye doğru aranır.
        /// "En yakın öndeki" tur farkını kendiliğinden çözer: harvester arabaları geçmediği için yoluna ilk çıkacak araba odur.
        /// </summary>
        private void CheckCatchupTeleport(RaceTrack track, HarvesterDifficulty diff)
        {
            if (!diff.catchupTeleport || !Running || launching || nearestAheadCar == null) return;
            if (float.IsInfinity(nearestAheadGap) || nearestAheadGap <= diff.maxGap) return;
            if (Time.time - lastTeleportTime < diff.teleportCooldown) return;

            float visible = VisibleDistance;
            if (AnyCarWithin(position, visible)) return;

            float carDistance = CarDistance(track, nearestAheadCar);
            // maxGap'ten başlayarak hedef noktayı ara; görüş mesafesinden yakına inme
            for (float gap = Mathf.Max(diff.maxGap, 1f); gap < nearestAheadGap - 10f; gap += 10f)
            {
                float d = track.WrapDistance(carDistance - gap);
                Vector3 point = track.GetPoint(d);
                if (AnyCarWithin(point, visible)) continue;
                TeleportAlongTrack(track, d);
                return;
            }
        }

        private static bool AnyCarWithin(Vector3 point, float distance)
        {
            float d2 = distance * distance;
            var all = RaceCar.All;
            for (int i = 0; i < all.Count; i++)
            {
                var car = all[i];
                if (car == null || car.IsEliminated || !car.isActiveAndEnabled) continue;
                if ((car.transform.position - point).sqrMagnitude < d2) return true;
            }
            return false;
        }

        /// <summary>Sunucu: pist üzerinde verilen mesafeye ışınlar; hızı, hedefi ve kalkış durumunu korur.</summary>
        private void TeleportAlongTrack(RaceTrack track, float distance)
        {
            Vector3 forward = track.GetForward(distance);
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : yaw;
            yawRate = 0f;
            aimLateral = 0f;
            verticalSpeed = 0f;
            position = track.GetPoint(distance);
            Vector3 grounded, normal;
            if (SampleGround(position, yaw, out grounded, out normal))
            {
                position = grounded;
                groundNormal = normal;
            }
            rotation = YawOnSlope(yaw, groundNormal);
            trackDistance = distance;
            hasTrackHint = true;
            // Hedefin hızının altında kalmasın ki ışınlanınca hemen geri düşmesin
            speed = Mathf.Max(speed, targetSpeedRef);

            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            if (IsSpawned && networkTransform != null && networkTransform.CanCommitToTransform)
                networkTransform.Teleport(position, rotation, transform.localScale);
            lastTeleportTime = Time.time;
            TeleportCount++;
        }

        // ------------------------------------------------------------------ Hedef seçimi

        private void UpdateTarget(RaceTrack track, HarvesterDifficulty diff)
        {
            RaceCar bestAhead = null, bestBehind = null;
            float bestAheadGap = float.PositiveInfinity, bestBehindGap = float.NegativeInfinity;
            float currentGap = float.PositiveInfinity;
            bool currentValid = false;

            var all = RaceCar.All;
            for (int i = 0; i < all.Count; i++)
            {
                var car = all[i];
                if (car == null || car.IsEliminated || !car.isActiveAndEnabled) continue;
                float gap = Gap(track, CarDistance(track, car));
                if (car == target)
                {
                    currentGap = gap;
                    currentValid = gap >= -diff.behindTolerance;
                }
                if (gap >= -diff.behindTolerance)
                {
                    if (gap < bestAheadGap) { bestAheadGap = gap; bestAhead = car; }
                }
                else if (gap > bestBehindGap) { bestBehindGap = gap; bestBehind = car; }
            }

            nearestAheadGap = bestAheadGap;
            nearestAheadCar = bestAhead;
            RaceCar chosen = target;
            if (!currentValid)
            {
                // Mevcut hedef yok, elendi ya da geride kaldı: öndeki en yakın, o da yoksa arkadaki en yakın
                chosen = bestAhead != null ? bestAhead : bestBehind;
            }
            else if (bestAhead != null && bestAhead != target
                     && bestAheadGap < currentGap - diff.targetSwitchMargin
                     && Time.time - targetChosenAt >= diff.minTargetHold)
            {
                chosen = bestAhead; // histerezis: belirgin şekilde daha yakın olan varsa
            }

            if (chosen != target)
            {
                target = chosen;
                targetChosenAt = Time.time;
                lateralHistoryCount = 0;
                if (target != null) targetSpeedRef = targetSpeedSmoothed = Vector3.Dot(target.Velocity, track.GetForward(CarDistance(track, target)));
                creep = 0f;
            }

            if (target == null)
            {
                targetGap = float.PositiveInfinity;
                return;
            }

            float td = CarDistance(track, target);
            targetGap = Gap(track, td);
            float rawSpeed = Vector3.Dot(target.Velocity, track.GetForward(td));
            targetSpeedSmoothed = Mathf.Lerp(targetSpeedSmoothed, rawSpeed, 1f - Mathf.Exp(-Time.fixedDeltaTime / 0.25f));
            // Yarış temposu: hedefin bulunduğu yerde temiz süren bir arabanın hızı (virajları ve freni hesaba katar)
            targetPace = TrackSpeedPlanner.SafeSpeed(track, td, Mathf.Max(rawSpeed, 1f), diff.paceTopSpeed, diff.paceLateralAcceleration, diff.paceBraking);
            // Hata: tempodan belirgin şekilde yavaş (dönme, bariyer). Virajda frenlemek tempo düştüğü için hata sayılmaz.
            // Kalkış penceresinde yok: kalkıştaki doğal yavaşlık hata sayılmaz.
            if (!InLaunchWindow && targetGap >= -diff.behindTolerance && targetGap < diff.punishRange
                && targetSpeedSmoothed < targetPace * diff.punishSpeedRatio)
                punishUntil = Time.time + diff.punishHold;
            // Referans: yükselişe hemen, düşüşe geç inanır. Av modunda yarış temposuna çekilir: harvester tempoyu korur.
            if (Punishing) targetSpeedRef = Mathf.Max(Mathf.Max(targetSpeedRef, rawSpeed), targetPace);
            else
            {
                float tc = rawSpeed > targetSpeedRef ? diff.targetSpeedRiseTime : diff.targetSpeedFallTime;
                targetSpeedRef = Mathf.Lerp(targetSpeedRef, rawSpeed, 1f - Mathf.Exp(-Time.fixedDeltaTime / Mathf.Max(0.01f, tc)));
            }
            PushLateral(track.GetLateralOffset(target.transform.position, td));
        }

        private void PushLateral(float lateral)
        {
            lateralHistory[lateralHistoryHead] = lateral;
            lateralHistoryTime[lateralHistoryHead] = Time.time;
            lateralHistoryHead = (lateralHistoryHead + 1) % lateralHistory.Length;
            lateralHistoryCount = Mathf.Min(lateralHistoryCount + 1, lateralHistory.Length);
        }

        /// <summary>Hedefin gecikme payı kadar önceki yanal konumu.</summary>
        private float DelayedTargetLateral(float delay)
        {
            if (lateralHistoryCount == 0) return 0f;
            float when = Time.time - delay;
            int newest = (lateralHistoryHead - 1 + lateralHistory.Length) % lateralHistory.Length;
            float value = lateralHistory[newest];
            for (int k = 0; k < lateralHistoryCount; k++)
            {
                int i = (newest - k + lateralHistory.Length) % lateralHistory.Length;
                value = lateralHistory[i];
                if (lateralHistoryTime[i] <= when) break;
            }
            return value;
        }

        // ------------------------------------------------------------------ Sürüş

        /// <summary>Pistin ortasından izin verilen en büyük yana sapma (m).</summary>
        public float LateralLimit
        {
            get
            {
                var track = RaceTrack.Current;
                if (track == null) return 0f;
                float halfWidth = Mathf.Max(Difficulty.headerWidth * 0.5f, BodyHalfWidth);
                return Mathf.Max(0f, track.Width * 0.5f + shoulder - halfWidth - barrierClearance);
            }
        }

        private float BodyHalfWidth => bodyCollider != null ? bodyCollider.size.x * 0.5f : 4.4f;

        private void Drive(RaceTrack track, HarvesterDifficulty diff, float dt)
        {
            float desiredSpeed = 0f;
            float desiredYawRate = 0f;

            if (track != null && track.Length > 0f)
            {
                float limit = LateralLimit;

                // Yanal hedef: kovalanan arabanın (gecikmeli) yanal konumu; uzaktayken yolun ortası
                float wantLateral = 0f;
                if (target != null && targetGap >= -diff.behindTolerance)
                {
                    float weight = Mathf.InverseLerp(90f, 35f, targetGap);
                    wantLateral = DelayedTargetLateral(diff.reactionDelay) * weight;
                }
                // Biraz içeride hedefle: takip gecikmesiyle taşma bariyer payını yemesin
                float aimLimit = Mathf.Max(0f, limit - 0.4f);
                wantLateral = Mathf.Clamp(wantLateral, -aimLimit, aimLimit);
                aimLateral = Mathf.MoveTowards(aimLateral, wantLateral, diff.lateralAimSpeed * dt);

                // Saf takip: hızla uzayan bakış payı kadar ilerideki nokta
                float look = Mathf.Clamp(8f + speed * 0.45f, 10f, 32f);
                float aimDistance = trackDistance + look;
                // Saf takip virajda içe keser (yay ile kiriş farkı ≈ bakış² × eğrilik / 8). Hedef noktayı o kadar dışa
                // kaydır ki harvester istenen yanal konumda kalsın ve bariyere sürtünmesin.
                Vector3 f0 = track.GetForward(trackDistance), f1 = track.GetForward(aimDistance);
                f0.y = 0f;
                f1.y = 0f;
                float turn = Vector3.SignedAngle(f0, f1, Vector3.up) * Mathf.Deg2Rad; // sağa dönüş artı
                float sagitta = Mathf.Clamp(look * turn / 8f, -3f, 3f);
                // Sınırı aştıysa (yana kaçan hedefi kovalarken virajda) içeri doğru ek düzeltme
                float actualLateral = track.GetLateralOffset(position, trackDistance);
                float excess = actualLateral - Mathf.Clamp(actualLateral, -limit, limit);
                // Yanal hız sönümü (PD): yana hızla kayarken hedefi erkenden geri çek, sınırı aşıp bariyere taşmasın
                float headingError = Mathf.DeltaAngle(Quaternion.LookRotation(f0.sqrMagnitude > 1e-6f ? f0 : Vector3.forward).eulerAngles.y, yaw);
                float lateralVelocity = speed * Mathf.Sin(headingError * Mathf.Deg2Rad);
                Vector3 aimPoint = track.GetPoint(aimDistance) + track.GetRight(aimDistance)
                                   * (aimLateral - sagitta - excess * 2f - lateralVelocity * LateralDamping);
                Vector3 toAim = aimPoint - position;
                toAim.y = 0f;
                if (toAim.sqrMagnitude > 0.01f)
                {
                    float error = Mathf.DeltaAngle(yaw, Quaternion.LookRotation(toAim).eulerAngles.y);
                    float maxRate = Mathf.Min(diff.turnRate, LateralAcceleration(diff) / Mathf.Max(1f, speed) * Mathf.Rad2Deg);
                    desiredYawRate = Mathf.Clamp(error * 2.5f, -maxRate, maxRate);
                }

                desiredSpeed = Running ? DesiredSpeed(track, diff) : 0f;
            }

            // Hız ve dönüş, ivme sınırlarıyla
            float accel = desiredSpeed > speed ? (InLaunchWindow ? diff.launchAcceleration : diff.acceleration) : diff.braking;
            speed = Mathf.MoveTowards(speed, desiredSpeed, accel * dt);
            if (speed < 0.05f && desiredSpeed <= 0f) desiredYawRate = 0f; // dururken yerinde dönmesin
            yawRate = Mathf.MoveTowards(yawRate, desiredYawRate * Mathf.Clamp01(speed / 2f), diff.yawAcceleration * dt);
            yaw = Mathf.Repeat(yaw + yawRate * dt, 360f);

            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 next = position + forward * speed * dt;

            // Bariyerlerin içinde kal
            if (track != null && track.Length > 0f)
            {
                float d = track.GetNearestDistance(next, trackDistance, 20f);
                float lateral = track.GetLateralOffset(next, d);
                float hard = LateralLimit + 0.3f; // en geniş yer bariyerin iç yüzüne 0.5 m kalır
                if (Mathf.Abs(lateral) > hard)
                {
                    Vector3 right = track.GetRight(d);
                    next -= right * (lateral - Mathf.Clamp(lateral, -hard, hard));
                    BarrierClampSteps++;
                }
            }

            // Zemine otur
            // Yön tam olarak yaw'dan gelir; sadece eğim (zemin normali) yumuşatılır, gövde dönüşte geri kalmasın.
            Vector3 grounded, normal;
            if (SampleGround(next, yaw, out grounded, out normal))
            {
                verticalSpeed = 0f;
                float k = 1f - Mathf.Exp(-dt / 0.06f);
                next.y = grounded.y > position.y + 1.5f || grounded.y < position.y - 1.5f ? grounded.y : Mathf.Lerp(position.y, grounded.y, k);
                groundNormal = Vector3.Slerp(groundNormal, normal, 1f - Mathf.Exp(-dt / 0.08f));
            }
            else
            {
                // Zemin yok: düş; çok düşerse piste geri koy
                verticalSpeed += Physics.gravity.y * dt;
                next.y = position.y + verticalSpeed * dt;
                groundNormal = Vector3.Slerp(groundNormal, Vector3.up, 1f - Mathf.Exp(-dt / 0.3f));
                if (track != null && next.y < track.GetPoint(trackDistance).y - 30f)
                {
                    PlaceAt(track.GetPoint(trackDistance) + Vector3.up * 2f, Quaternion.LookRotation(track.GetForward(trackDistance)));
                    return;
                }
            }

            rotation = YawOnSlope(yaw, groundNormal);
            position = next;
            body.MovePosition(position);
            body.MoveRotation(rotation);
        }

        /// <summary>Tablanın önünden hedefe açık (m); hedef yoksa ya da arkadaysa 0.</summary>
        private float EffectiveGap => target != null && targetGap >= 0f && !float.IsInfinity(targetGap) ? Mathf.Max(0f, targetGap - HeaderReach) : 0f;

        private float LateralAcceleration(HarvesterDifficulty diff)
        {
            // Uzaktayken virajı daha sert alır (oyuncu görmüyor); av modunda da
            float boost = Mathf.Max(diff.CornerBoostForGap(EffectiveGap), Punishing ? diff.punishCornerBoost : 1f);
            return diff.lateralAcceleration * boost;
        }

        /// <summary>Lastik bant: hedefe göre istenen hız, viraj sınırıyla.</summary>
        private float DesiredSpeed(RaceTrack track, HarvesterDifficulty diff)
        {
            float desired;
            if (target == null)
            {
                desired = diff.cruiseSpeed;
            }
            else if (targetGap < -diff.behindTolerance)
            {
                // Herkes geride (yakınımızda): yavaşla, yanımıza gelsinler; yanımıza gelen hedef olur.
                // En az bir hızla gider: duran harvester'ın arkasına sıkışan arabayla kilitlenmesin
                desired = Mathf.Max(diff.cruiseSpeed * 0.4f, targetSpeedSmoothed - 6f);
            }
            else
            {
                // Zaman açığı modeli (açıklaması sınıf belgesinde ve HarvesterDifficulty'de):
                //   g  = tablanın önünden hedefe açık (m),  v_r = hedef hızı referansı (asimetrik süzgeç)
                //   g* = max(g_min, τ*·max(v_r, v_min))     hedeflenen açık
                //   e  = g − g*                              açık hatası
                //   c  = e ≥ 0 ? min(Kp·e, √(2·a_soft·e))   yakalama, banda yumuşak oturma
                //            : max(Kp·e, −c_geri)            çok yakın: az geri çekil (baskı)
                //   v  = min(v_r + c, tavan(g)), viraj ve fren sınırları ayrıca
                float gap = EffectiveGap;
                float vr = Mathf.Max(0f, targetSpeedRef);
                bool window = InLaunchWindow;
                float gMin = diff.launchHoldGap;
                // Baskı sürünmesi: bantta kaldıkça τ* daralır; hedef açığı açınca sıfırlanır
                float vRef = Mathf.Max(vr, diff.timeGapMinSpeed);
                float tau = gap / vRef;
                if (window || tau > diff.timeGapTarget + diff.creepResetMargin) creep = 0f;
                else if (!Punishing) creep = Mathf.Min(diff.timeGapTarget, creep + diff.pressureCreepRate * Time.fixedDeltaTime);
                float tauStar = Mathf.Max(0f, diff.timeGapTarget - creep);
                float gStar = window ? Mathf.Max(gMin, tauStar * vRef) : tauStar * vRef;
                float e = gap - gStar;
                float c;
                if (e >= 0f)
                {
                    // Kalkış atağı aynı yasanın sert hâli: Kp sınırı yok, oturma yavaşlaması daha büyük
                    c = window && Launching ? Mathf.Sqrt(2f * diff.launchLandingDecel * e)
                                            : Mathf.Min(diff.gapGain * e, Mathf.Sqrt(2f * diff.softLandingDecel * e));
                }
                else c = Mathf.Max(diff.gapGain * e, window ? float.NegativeInfinity : -diff.maxBackoff); // kalkışta tam geri çekilir
                if (Punishing) c = Mathf.Max(c, diff.punishClosingSpeed);

                float cap;
                if (window && Launching) cap = diff.launchMaxSpeed;               // kalkış: bant dışından yakalama, yüksek tavan
                else if (window) cap = Mathf.Max(diff.SpeedCapForGap(gap), vr + 3f); // sürü hızlanırken geri düşmesin
                else cap = diff.SpeedCapForGap(gap);
                if (Punishing && gap <= diff.pressureDistance) cap = Mathf.Max(cap, diff.punishSpeedCap);
                desired = Mathf.Clamp(vr + c, 0f, cap);
            }

            float top = Launching ? Mathf.Max(diff.maxSpeed, diff.launchMaxSpeed) : diff.maxSpeed;
            desired = Mathf.Min(desired, top);
            desired = Mathf.Min(desired, TrackSpeedPlanner.SafeSpeed(track, trackDistance, speed, top, LateralAcceleration(diff), diff.braking));
            if (!track.Loop)
            {
                // A'dan B'ye pistin sonunda dur
                float left = track.Length - trackDistance - 15f;
                desired = Mathf.Min(desired, Mathf.Sqrt(Mathf.Max(0f, 2f * diff.braking * left)));
            }
            return desired;
        }

        /// <summary>
        /// Tekerlek noktalarından aşağı ışın atıp zemin yüksekliğini ve eğimini bulur. Arabalar, enkaz ve kendi
        /// gövdesi (Rigidbody'li her şey) yok sayılır.
        /// </summary>
        private static Quaternion YawOnSlope(float yawDegrees, Vector3 normal)
        {
            Vector3 forward = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;
            Vector3 onSlope = Vector3.ProjectOnPlane(forward, normal);
            return Quaternion.LookRotation(onSlope.sqrMagnitude > 1e-4f ? onSlope : forward, normal);
        }

        private bool SampleGround(Vector3 at, float yawDegrees, out Vector3 grounded, out Vector3 groundNormalOut)
        {
            Quaternion yawRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            float[] heights = sampleHeights;
            int hits = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector3 offset = yawRotation * new Vector3(groundProbes[i].x, 0f, groundProbes[i].z);
                Vector3 origin = at + offset + Vector3.up * 5f;
                float h;
                if (CastGround(origin, 14f, out h)) { heights[i] = h; hits++; }
                else heights[i] = float.NaN;
            }
            if (hits < 2)
            {
                grounded = at;
                groundNormalOut = Vector3.up;
                return false;
            }
            // Eksik ışını karşı tekerlekle doldur
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < 4; i++) if (!float.IsNaN(heights[i])) { sum += heights[i]; n++; }
            float average = sum / n;
            for (int i = 0; i < 4; i++) if (float.IsNaN(heights[i])) heights[i] = average;

            float frontZ = (groundProbes[0].z + groundProbes[1].z) * 0.5f, rearZ = (groundProbes[2].z + groundProbes[3].z) * 0.5f;
            float leftX = (groundProbes[0].x + groundProbes[2].x) * 0.5f, rightX = (groundProbes[1].x + groundProbes[3].x) * 0.5f;
            float front = (heights[0] + heights[1]) * 0.5f, rear = (heights[2] + heights[3]) * 0.5f;
            float left = (heights[0] + heights[2]) * 0.5f, right = (heights[1] + heights[3]) * 0.5f;

            Vector3 f = yawRotation * Vector3.forward * Mathf.Max(0.5f, frontZ - rearZ) + Vector3.up * (front - rear);
            Vector3 r = yawRotation * Vector3.right * Mathf.Max(0.5f, rightX - leftX) + Vector3.up * (right - left);
            groundNormalOut = Vector3.Cross(f, r).normalized;
            grounded = new Vector3(at.x, average, at.z);
            return true;
        }

        private readonly float[] sampleHeights = new float[4];

        private bool CastGround(Vector3 origin, float length, out float height)
        {
            height = 0f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, rayBuffer, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = rayBuffer[i];
                if (hit.rigidbody != null) continue; // araba, enkaz, kendimiz
                if (hit.normal.y < 0.5f) continue;   // bariyerin yan yüzü
                if (hit.distance < best)
                {
                    best = hit.distance;
                    height = hit.point.y;
                    found = true;
                }
            }
            return found;
        }

        // ------------------------------------------------------------------ Makara, biçme, çarpma

        private void UpdateReel(HarvesterDifficulty diff, float dt)
        {
            float want = 0f;
            // Hedef ya da başka bir araba tablaya yaklaşınca biçme hızına çık
            if (Running) want = Mathf.Min(targetGap, nearestAheadGap) < diff.mowingRange ? reelMowSpeed : reelIdleSpeed;
            reelSpeed = Mathf.MoveTowards(reelSpeed, want, reelAcceleration * dt);
        }

        private void CheckContacts(HarvesterDifficulty diff)
        {
            if (headerCollider == null || bodyCollider == null) return;
            touchedThisStep.Clear();
            // Uzak arabaların gösterilen (tahmini) konumları collider'lara geçsin
            Physics.SyncTransforms();

            // 1. Tabla: biçme kutusu fiziksel tablanın önüne doğru taşar
            float headerFront = headerCollider.center.z + headerCollider.size.z * 0.5f;
            float depth = Mathf.Max(0.5f, diff.headerDepth);
            float height = Mathf.Max(3f, headerCollider.size.y + 1f);
            Vector3 cutCenter = new Vector3(0f, height * 0.5f - 0.3f, headerFront + diff.headerLead - depth * 0.5f); // yerden başlar, alçak araba altından kaçmasın
            Vector3 cutHalf = new Vector3(diff.headerWidth * 0.5f, height * 0.5f, depth * 0.5f);
            bool cutting = IsCutting;
            int count = Physics.OverlapBoxNonAlloc(transform.TransformPoint(cutCenter), cutHalf, overlapBuffer, transform.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var car = CarOf(overlapBuffer[i]);
                if (car == null || car.IsEliminated || !touchedThisStep.Add(car)) continue;
                if (cutting) Cut(car);
                else Ram(car, diff, true);
            }

            // 2. Gövde: yandan ya da arkadan temas eden arabayı savur
            Vector3 bodyHalf = bodyCollider.size * 0.5f + Vector3.one * ramMargin;
            Vector3 bodyCenter = bodyCollider.transform.TransformPoint(bodyCollider.center);
            count = Physics.OverlapBoxNonAlloc(bodyCenter, bodyHalf, overlapBuffer, bodyCollider.transform.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var car = CarOf(overlapBuffer[i]);
                if (car == null || car.IsEliminated || !touchedThisStep.Add(car)) continue;
                Ram(car, diff, false);
            }
        }

        private RaceCar CarOf(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb == null || rb == body) return null;
            RaceCar car;
            if (!carByBody.TryGetValue(rb, out car))
            {
                car = rb.GetComponent<RaceCar>();
                carByBody[rb] = car;
            }
            return car;
        }

        private void Cut(RaceCar car)
        {
            car.Eliminate();
            CutCount++;
            if (OnCarCut != null) OnCarCut(car);
        }

        private void Ram(RaceCar car, HarvesterDifficulty diff, bool header)
        {
            float last;
            if (lastHitTime.TryGetValue(car, out last) && Time.time - last < diff.hitCooldown) return;
            lastHitTime[car] = Time.time;

            Vector3 carPosition = car.transform.position;
            Vector3 local = transform.InverseTransformPoint(carPosition);
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float side = local.x >= 0f ? 1f : -1f;
            Vector3 harvesterVelocity = forward * speed;
            Vector3 relative = harvesterVelocity - car.Velocity;
            float speedFactor = Mathf.Clamp01(speed / Mathf.Max(1f, diff.maxSpeed));

            float bodyRear = bodyCollider.center.z - bodyCollider.size.z * 0.5f;
            Vector3 change;
            if (header)
            {
                // Makara dönmüyorken tablaya değdi: önüne iter
                float push = Mathf.Max(0f, Vector3.Dot(relative, forward));
                change = forward * (diff.hitForwardSpeed + push * 0.8f) + right * side * diff.hitSideSpeed * 0.4f;
            }
            else if (local.z < bodyRear + 1f)
            {
                // Arkadan çarptı: geri ve yana sek
                change = -forward * diff.hitRearSpeed + right * side * diff.hitSideSpeed * 0.5f;
            }
            else
            {
                // Yandan: yana ve ileri savur
                float push = Mathf.Max(0f, Vector3.Dot(relative, right * side));
                change = right * side * (diff.hitSideSpeed + push * 0.5f) + forward * diff.hitForwardSpeed * (0.4f + 0.6f * speedFactor);
            }

            // Temas noktası: gövde kutusunun arabaya en yakın noktası
            Vector3 c = bodyCollider.center, h = bodyCollider.size * 0.5f;
            Vector3 clamped = new Vector3(Mathf.Clamp(local.x, c.x - h.x, c.x + h.x), Mathf.Clamp(local.y, c.y - h.y, c.y + h.y),
                header ? headerCollider.center.z + headerCollider.size.z * 0.5f : Mathf.Clamp(local.z, c.z - h.z, c.z + h.z));
            Vector3 point = transform.TransformPoint(clamped);
            point.y = carPosition.y + 0.5f;

            car.ApplyHit(change, point);
            HitCount++;
            if (OnCarHit != null) OnCarHit(car, change);
        }

        // ------------------------------------------------------------------ Görsel (her makinede)

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Işınlanma (görünmezken yetişme): tek karede büyük sıçrama hız tahminini ve pist ipucunu bozmasın
            Vector3 delta = transform.position - lastVisualPosition;
            bool jumped = delta.sqrMagnitude > 25f * 25f;
            if (jumped)
            {
                delta = Vector3.zero;
                if (!Simulates) hasTrackHint = false;
            }

            if (!Simulates)
            {
                reelSpeed = Mathf.MoveTowards(reelSpeed, netReelSpeed.Value, reelAcceleration * dt);
                UpdateTrackDistance();
            }

            // Hız ve sapma hızı gösterilen konumdan (istemcide tek kaynak, sunucuda da görsel için yeterli)
            lastVisualPosition = transform.position;
            float y = transform.eulerAngles.y;
            if (!jumped)
            {
                float forwardDelta = Vector3.Dot(delta, transform.forward) / dt;
                visualSpeed = Mathf.Lerp(visualSpeed, forwardDelta, 1f - Mathf.Exp(-dt / 0.15f));
                visualYawRate = Mathf.Lerp(visualYawRate, Mathf.DeltaAngle(lastVisualYaw, y) / dt, 1f - Mathf.Exp(-dt / 0.15f));
            }
            lastVisualYaw = y;

            if (reel != null) reel.Rotate(reelSpeed * dt, 0f, 0f, Space.Self);

            float wheelSpeed = Simulates ? speed : visualSpeed;
            float yawRateRad = (Simulates ? yawRate : visualYawRate) * Mathf.Deg2Rad;
            float wheelBase = Mathf.Abs(groundProbes[0].z - groundProbes[2].z);
            float rearSteer = Mathf.Abs(wheelSpeed) > 1f
                ? Mathf.Clamp(-Mathf.Atan(yawRateRad * wheelBase / wheelSpeed) * Mathf.Rad2Deg, -maxRearSteerVisual, maxRearSteerVisual)
                : 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                Transform wheel = wheels[i];
                if (wheel == null) continue;
                float radius = i < wheelRadii.Length ? Mathf.Max(0.1f, wheelRadii[i]) : 1f;
                wheelSpin[i] = Mathf.Repeat(wheelSpin[i] + wheelSpeed / radius * Mathf.Rad2Deg * dt, 360f);
                Transform parent = wheel.parent;
                Vector3 axisRight = parent != null ? parent.InverseTransformDirection(transform.right) : Vector3.right;
                Vector3 axisUp = parent != null ? parent.InverseTransformDirection(transform.up) : Vector3.up;
                float steer = i >= 2 ? rearSteer : 0f;
                wheel.localRotation = Quaternion.AngleAxis(steer, axisUp) * Quaternion.AngleAxis(wheelSpin[i], axisRight) * wheelBaseRotations[i];
            }
        }

        private void ApplyHeaderWidth()
        {
            var diff = Difficulty;
            appliedHeaderWidth = diff.headerWidth;
            if (headerVisual != null && headerVisualWidth > 0.1f)
            {
                Vector3 s = headerVisualBaseScale;
                s.x *= diff.headerWidth / headerVisualWidth;
                headerVisual.localScale = s;
            }
            if (headerCollider != null)
            {
                Vector3 size = headerCollider.size;
                size.x = diff.headerWidth;
                headerCollider.size = size;
            }
        }

        private RaceCar ResolveTarget(ulong id)
        {
            if (id == NoTarget || NetworkManager == null || NetworkManager.SpawnManager == null) return null;
            NetworkObject no;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out no) || no == null) return null;
            return no.GetComponent<RaceCar>();
        }

        private void OnDrawGizmosSelected()
        {
            if (headerCollider == null) return;
            var diff = difficulty != null ? difficulty : null;
            float width = diff != null ? diff.headerWidth : headerCollider.size.x;
            float depth = diff != null ? diff.headerDepth : 3.5f;
            float lead = diff != null ? diff.headerLead : 1.2f;
            float headerFront = headerCollider.center.z + headerCollider.size.z * 0.5f;
            float height = Mathf.Max(3f, headerCollider.size.y + 1f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.35f);
            Gizmos.DrawCube(new Vector3(0f, height * 0.5f - 0.3f, headerFront + lead - depth * 0.5f), new Vector3(width, height, depth));
            Gizmos.color = Color.cyan;
            if (groundProbes != null)
                foreach (var p in groundProbes) Gizmos.DrawLine(p + Vector3.up * 3f, p - Vector3.up * 1f);
        }
    }
}
