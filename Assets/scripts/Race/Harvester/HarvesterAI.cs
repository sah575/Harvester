using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Kimsenin sürmediği, yapay zekânın sürdüğü harvester: en geride kalan arabanın hemen arkasında durur,
    /// kaza yapanı biçer (eler), gövdesine çarpan arabayı savurur.
    ///
    /// Yetki: sadece sunucu karar verir ve sürer. İstemciler NetworkTransform (sunucu otoritesi) +
    /// RemoteExtrapolator ile görür; makara hızı ve hedef NetworkVariable ile gelir. Ağ yokken (NetworkManager yok ya da
    /// dinlemiyor) yerelde sunucu gibi çalışır, tek sahnede test edilebilir.
    ///
    /// Hareket: kinematik, pist orta çizgisine bağlı. Durum = pist mesafesi s + yanal kayma l; konum orta çizgiden,
    /// yükseklik ve eğim tekerlek noktalarından aşağı atılan ışınlardan gelir. Virajda yavaşlama, dönüş sınırı ve
    /// bariyere sürtünme yok; hız sadece hedefe göre ayarlanır.
    ///
    /// Davranış modeli (formüller <see cref="HarvesterDifficulty"/>'nin belgesinde):
    ///   Kalkış → Baskı (açık ~20 m, en az 8 m) ↔ Saldırı (hedef kaza yaptı) ; hedef değişince Yakalama (tasma kısalır).
    ///   Tasma: kalkış dışında açık asla leashMax'ı geçmez. Yarış bitince collider'lar kapanır, kimse biçilmez.
    ///
    /// Biçme ve çarpma, host'ta uzak arabaların gösterilen (RemoteExtrapolator ile tahmini) konumuna göre yapılır:
    /// "sürücünün lehine" kuralı, host ne görüyorsa o.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class HarvesterAI : NetworkBehaviour
    {
        /// <summary>Davranış modu (sunucuda).</summary>
        public enum Mode { Durdu, HedefYok, Geride, Kalkis, Baski, Yakalama, Saldiri, Bitti }

        /// <summary>Sahnedeki harvester (izleyici kamerası ve HUD için).</summary>
        public static HarvesterAI Instance { get; private set; }

        /// <summary>Sunucuda (ya da ağsız) bir araba biçildiğinde.</summary>
        public event Action<RaceCar> OnCarCut;
        /// <summary>Sunucuda (ya da ağsız) bir arabaya çarpıldığında (araba, hız değişimi).</summary>
        public event Action<RaceCar, Vector3> OnCarHit;
        /// <summary>Sunucuda (ya da ağsız) bir arabanın kazası tespit edildiğinde (araba, gerekçe).</summary>
        public event Action<RaceCar, string> OnCrashDetected;

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
        private Collider[] ownColliders;
        private bool ghost;

        // Sürüş durumu (sunucu): pist mesafesi + yanal kayma
        private Vector3 position;
        private float yaw, yawRate, speed;
        private float lateral, lateralRate;
        private Quaternion rotation = Quaternion.identity;
        private Vector3 groundNormal = Vector3.up;
        private float trackDistance;
        private bool hasTrackHint;
        private bool stateInitialized;
        private bool runningSetExplicitly;
        private float releaseTime = -100f;

        // Hedef (sunucu)
        private RaceCar target;
        private float targetGap = float.PositiveInfinity; // açık: tablanın önünden arabanın arkasına (m)
        private float targetRootGap = float.PositiveInfinity;
        private float leashLimit = float.PositiveInfinity;
        private float nearestAnyGap = float.PositiveInfinity; // önündeki herhangi bir arabaya açık (bitirmişler dahil)
        private Mode mode = Mode.Durdu;
        private float lastTeleportTime = -100f;
        private readonly float[] lateralHistory = new float[96];
        private readonly float[] lateralHistoryTime = new float[96];
        private int lateralHistoryHead, lateralHistoryCount;

        /// <summary>Her araba için hız geçmişi ve kaza durumu (sunucu).</summary>
        private class CarWatch
        {
            public float DistanceHint = -1f;
            public float Speed;        // pist boyunca süzülmüş hız
            public float RawSpeed;     // pist boyunca anlık hız
            public float Average;      // son birkaç saniyenin ortalaması
            public float Decel;        // süzülmüş hızın düşme hızı (m/sn², yavaşlarken artı)
            public float ImpactTime = -100f; // son sert yavaşlama (çarpma) anı
            public bool Initialized;
            public bool Crashed;
            public float CrashTime = -100f;
            public string CrashReason = "";
            public int LastStep = -1;
        }
        private readonly Dictionary<RaceCar, CarWatch> watches = new Dictionary<RaceCar, CarWatch>();
        private int stepCounter;

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
        /// <summary>Hedefe açık (m, tablanın önünden arabanın arkasına; sunucuda; hedef yoksa sonsuz).</summary>
        public float TargetGap => targetGap;
        /// <summary>Önündeki en geride kalan arabaya açık (hedefle aynı, eski ad).</summary>
        public float NearestAheadGap => targetGap;
        /// <summary>Şu anki tasma uzunluğu (m): yeniden yakalamada büyük, sonra leashMax.</summary>
        public float LeashLimit => leashLimit;
        /// <summary>Çalışıyor mu (sürüyor, biçiyor). Yarış yöneticisi geri sayımda kapalı tutar.</summary>
        public bool Running { get; private set; }
        /// <summary>Kalkış penceresi: salındıktan sonraki launchWindow saniye (kaza sayılmaz, biçmez).</summary>
        public bool InLaunchWindow => Running && Time.time - releaseTime < Difficulty.launchWindow;
        /// <summary>Görünmezken yetişme ışınlanması sayısı (test).</summary>
        public int TeleportCount { get; private set; }
        /// <summary>Tasmanın harvester'ı ileri ittiği fizik adımı sayısı (test).</summary>
        public int LeashPullSteps { get; private set; }
        /// <summary>Kabin kamerası noktası (sürücü gözü, ileri bakar). Yoksa null.</summary>
        public Transform CabinView => cabinView;
        /// <summary>Davranış modu (sunucuda).</summary>
        public Mode CurrentMode => mode;
        /// <summary>Saldırı: hedef kaza yaptı, harvester üstüne geliyor.</summary>
        public bool IsAttacking => mode == Mode.Saldiri;
        /// <summary>Yarış bitti: collider'lar kapalı, kimse biçilmez (her makinede).</summary>
        public bool IsGhost => ghost;
        /// <summary>Şu anki davranış (HUD ve test için, sunucuda).</summary>
        public string ModeName
        {
            get
            {
                switch (mode)
                {
                    case Mode.Durdu: return "durdu";
                    case Mode.HedefYok: return "hedef yok";
                    case Mode.Geride: return "herkes geride";
                    case Mode.Kalkis: return "kalkış";
                    case Mode.Baski: return "baskı";
                    case Mode.Yakalama: return "yeniden yakalama";
                    case Mode.Saldiri: return "SALDIRI";
                    case Mode.Bitti: return "yarış bitti (hayalet)";
                }
                return mode.ToString();
            }
        }
        /// <summary>Tablanın önünün kök noktasından uzaklığı (m).</summary>
        public float HeaderReach => headerCollider != null ? headerCollider.center.z + headerCollider.size.z * 0.5f : 10f;
        /// <summary>Makara dönüş hızı (derece/sn).</summary>
        public float ReelSpeed => reelSpeed;
        /// <summary>Tabla şu an biçiyor mu (makara yeterince hızlı).</summary>
        public bool IsCutting => Running && !ghost && reelSpeed >= reelCutSpeed;
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

        /// <summary>Sunucuda: araba kaza yapmış sayılıyor mu (saldırı tetikleyicisi).</summary>
        public bool IsCrashed(RaceCar car)
        {
            CarWatch w;
            return car != null && watches.TryGetValue(car, out w) && w.Crashed;
        }

        /// <summary>Sunucuda: arabanın son kaza tespit zamanı (Time.time; yoksa -100).</summary>
        public float CrashTimeOf(RaceCar car)
        {
            CarWatch w;
            return car != null && watches.TryGetValue(car, out w) ? w.CrashTime : -100f;
        }

        /// <summary>Sadece sunucu: çalıştır/durdur. Çalışmaya başlayınca kalkış penceresi başlar.</summary>
        public void SetRunning(bool running)
        {
            if (IsSpawned && !IsServer) return;
            if (running && !Running)
            {
                releaseTime = Time.time;
                leashLimit = float.PositiveInfinity;
                foreach (var w in watches.Values) { w.Crashed = false; w.Initialized = false; }
            }
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
            var track = RaceTrack.Current;
            speed = yawRate = lateralRate = 0f;
            target = null;
            targetGap = targetRootGap = float.PositiveInfinity;
            leashLimit = float.PositiveInfinity;
            lateralHistoryCount = 0;
            watches.Clear();
            stateInitialized = true;
            if (track != null && track.Length > 0f)
            {
                trackDistance = track.GetNearestDistance(worldPosition);
                hasTrackHint = true;
                lateral = Mathf.Clamp(track.GetLateralOffset(worldPosition, trackDistance), -LateralLimit, LateralLimit);
                SetPoseOnTrack(track, true);
            }
            else
            {
                Vector3 flat = Vector3.ProjectOnPlane(worldRotation * Vector3.forward, Vector3.up);
                yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : 0f;
                position = worldPosition;
                rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            if (IsSpawned && networkTransform != null && networkTransform.CanCommitToTransform)
                networkTransform.Teleport(position, rotation, transform.localScale);
        }

        /// <summary>
        /// Arabanın harvester'ın önünde pist boyunca ne kadar ileride olduğu (m, kökten köke). Turlu pistte sarar:
        /// sonuç [-80, Length - 80) aralığında (80 m'ye kadar gerideki eksi). A'dan B'ye pistte geride olan eksi.
        /// </summary>
        public float GapTo(RaceCar car)
        {
            var track = RaceTrack.Current;
            if (track == null || car == null) return float.PositiveInfinity;
            return Gap(track, CarDistance(track, car));
        }

        /// <summary>Her makinede: tablanın önünden arabanın arkasına pist boyunca açık (m). HUD ve test bunu gösterir.</summary>
        public float OpenGapTo(RaceCar car)
        {
            return GapTo(car) - HeaderReach - Difficulty.carRearOffset;
        }

        // ------------------------------------------------------------------ Yaşam döngüsü

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            networkTransform = GetComponent<NetworkTransform>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            ownColliders = GetComponentsInChildren<Collider>(true);

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
                if (runWhenOffline && !runningSetExplicitly) SetRunning(true);
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

        private void InitializeState()
        {
            if (stateInitialized) return;
            PlaceAt(transform.position, transform.rotation);
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
            stepCounter++;

            if (track != null && track.Length > 0f)
            {
                UpdateCars(track, diff, dt);
                UpdateMode(diff);
                CheckCatchupTeleport(track, diff);
                UpdateReel(diff, dt);
                Drive(track, diff, dt);
                if (Running && !ghost) CheckContacts(diff);
            }
            else
            {
                target = null;
                targetGap = float.PositiveInfinity;
                mode = Running ? Mode.HedefYok : Mode.Durdu;
                UpdateReel(diff, dt);
            }

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
            trackDistance = track.GetNearestDistance(transform.position, hasTrackHint ? trackDistance : -1f, 40f);
            hasTrackHint = true;
        }

        /// <summary>Turlu pistte bu kadar gerideki araba "arkada" sayılır, daha gerisi bir tur önde (m).</summary>
        private const float BehindWindow = 80f;

        private float Gap(RaceTrack track, float carDistance)
        {
            float g = carDistance - trackDistance;
            if (track.Loop)
            {
                float back = Mathf.Min(BehindWindow, track.Length * 0.25f);
                g = Mathf.Repeat(g + back, track.Length) - back;
            }
            return g;
        }

        private CarWatch WatchOf(RaceCar car)
        {
            CarWatch w;
            if (!watches.TryGetValue(car, out w))
            {
                w = new CarWatch();
                watches[car] = w;
            }
            return w;
        }

        private float CarDistance(RaceTrack track, RaceCar car)
        {
            var w = WatchOf(car);
            if (w.LastStep == stepCounter && w.DistanceHint >= 0f && Simulates) return w.DistanceHint;
            float d = track.GetNearestDistance(car.transform.position, w.DistanceHint, 30f);
            w.DistanceHint = d;
            w.LastStep = stepCounter;
            return d;
        }

        /// <summary>Araba yarışıyor mu: elenmemiş, etkin, yarış yöneticisine göre bitirmemiş.</summary>
        private static bool IsRacing(RaceCar car)
        {
            if (car == null || car.IsEliminated || !car.isActiveAndEnabled) return false;
            var manager = RaceManager.Instance;
            return manager == null || !manager.isActiveAndEnabled || manager.IsCarRacing(car);
        }

        // ------------------------------------------------------------------ Arabalar: hız, kaza, hedef

        private void UpdateCars(RaceTrack track, HarvesterDifficulty diff, float dt)
        {
            RaceCar bestAhead = null, bestBehind = null;
            float bestAheadGap = float.PositiveInfinity, bestBehindGap = float.NegativeInfinity;
            bool launch = InLaunchWindow;
            float kSmooth = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, diff.speedSmoothing));
            float kAverage = 1f - Mathf.Exp(-dt / Mathf.Max(0.1f, diff.crashAverageTime));

            nearestAnyGap = float.PositiveInfinity;
            var all = RaceCar.All;
            for (int i = 0; i < all.Count; i++)
            {
                var car = all[i];
                if (car == null || car.IsEliminated || !car.isActiveAndEnabled) continue;
                float d = CarDistance(track, car);
                float gap = Gap(track, d);
                // Durma payı için bitirmiş arabalar da sayılır
                if (gap >= -diff.behindTolerance) nearestAnyGap = Mathf.Min(nearestAnyGap, gap - HeaderReach - diff.carRearOffset);
                if (!IsRacing(car)) continue;
                var w = WatchOf(car);
                float raw = Vector3.Dot(car.Velocity, track.GetForward(d));
                w.RawSpeed = raw;
                if (!w.Initialized)
                {
                    w.Initialized = true;
                    w.Speed = w.Average = raw;
                }
                else
                {
                    float before = w.Speed;
                    w.Speed = Mathf.Lerp(w.Speed, raw, kSmooth);
                    w.Decel = (before - w.Speed) / dt;
                    w.Average = Mathf.Lerp(w.Average, w.Speed, kAverage);
                }
                if (Running && !launch) UpdateCrash(track, diff, car, w, d);
                else w.Crashed = false;

                if (gap >= -diff.behindTolerance)
                {
                    if (gap < bestAheadGap) { bestAheadGap = gap; bestAhead = car; }
                }
                else if (gap > bestBehindGap) { bestBehindGap = gap; bestBehind = car; }
            }

            RaceCar chosen = bestAhead != null ? bestAhead : bestBehind;
            float rootGap = bestAhead != null ? bestAheadGap : bestBehindGap;
            float open = chosen != null ? rootGap - HeaderReach - diff.carRearOffset : float.PositiveInfinity;
            if (chosen != target)
            {
                target = chosen;
                lateralHistoryCount = 0;
                // Yeni hedef: tasma mevcut açıktan başlar ve kısalır (yeniden yakalama)
                leashLimit = Mathf.Max(diff.leashMax, open);
            }
            targetRootGap = chosen != null ? rootGap : float.PositiveInfinity;
            targetGap = open;
            if (target != null) PushLateral(track.GetLateralOffset(target.transform.position, CarDistance(track, target)));
        }

        /// <summary>
        /// Kaza kuralı (açıklanabilir): araba devrik, ya da durdu (hız &lt; crashStopSpeed), ya da çarptı (frenle olamayacak
        /// kadar sert yavaşlama &gt; crashImpactDecel ve hız ortalamanın crashImpactRatio katının altında), ya da hızı beklenenin
        /// crashSpeedRatio katının altına ve en az crashMinSpeedLoss kadar düştü. Beklenen = min(son birkaç sn ortalaması,
        /// pistin o yerinde temiz sürüşün hızı): virajdan önce fren yapan iyi sürücü kaza sayılmaz.
        /// Kaza bayrağı araba biçilene ya da attackMaxDuration geçip araba toparlanana dek kalır.
        /// </summary>
        private void UpdateCrash(RaceTrack track, HarvesterDifficulty diff, RaceCar car, CarWatch w, float d)
        {
            float now = Time.time;
            if (w.Crashed)
            {
                if (now - w.CrashTime > diff.attackMaxDuration && w.Speed > diff.crashStopSpeed * 2f && car.transform.up.y > diff.crashFlipUp)
                {
                    w.Crashed = false;
                    w.Average = w.Speed; // toparlandı: ortalama yeniden başlasın
                }
                return;
            }
            string reason = null;
            float expected = w.Average;
            if (car.transform.up.y < diff.crashFlipUp) reason = "devrildi";
            else if (w.Speed < diff.crashStopSpeed) reason = "durdu";
            else
            {
                // Çarpma: sert yavaşlama anı 0.5 sn akılda tutulur; hız o sürede ortalamanın altına inerse kaza
                if (w.Decel > diff.crashImpactDecel) w.ImpactTime = now;
                if (now - w.ImpactTime < 0.5f && w.Speed < diff.crashImpactRatio * w.Average) reason = "çarptı";
            }
            if (reason == null && w.Speed < diff.crashSpeedRatio * w.Average && w.Average - w.Speed > diff.crashMinSpeedLoss)
            {
                // Sadece belirgin düşüşte temiz tempoya bak (pahalı hesap)
                float pace = TrackSpeedPlanner.SafeSpeed(track, d, Mathf.Max(w.Average, 1f), diff.paceTopSpeed, diff.paceLateralAcceleration, diff.paceBraking);
                expected = Mathf.Min(w.Average, pace);
                if (w.Speed < diff.crashSpeedRatio * expected && expected - w.Speed > diff.crashMinSpeedLoss) reason = "yavaşladı";
            }
            if (reason == null) return;
            w.Crashed = true;
            w.CrashTime = now;
            w.CrashReason = string.Format("{0}: hız {1:F1} m/sn, beklenen {2:F1} m/sn", reason, w.Speed, expected);
            Debug.Log("[Harvester] kaza: " + car.name + " " + w.CrashReason);
            if (OnCrashDetected != null) OnCrashDetected(car, w.CrashReason);
        }

        private void UpdateMode(HarvesterDifficulty diff)
        {
            if (!Running) mode = ghost ? Mode.Bitti : Mode.Durdu;
            else if (target == null) mode = Mode.HedefYok;
            else if (targetRootGap < -diff.behindTolerance) mode = Mode.Geride;
            else if (InLaunchWindow) mode = Mode.Kalkis;
            else if (IsCrashed(target)) mode = Mode.Saldiri;
            else if (targetGap > diff.leashMax + 1f) mode = Mode.Yakalama;
            else mode = Mode.Baski;
        }

        private void PushLateral(float value)
        {
            lateralHistory[lateralHistoryHead] = value;
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
        /// Son çare: hedef teleportGap'ten uzaktaysa ve hiçbir araba harvester'ı göremiyorsa, hedefin gerisinde hiçbir
        /// arabadan görünmeyen en yakın noktaya ışınlanır. Kalan mesafeyi tasma kapatır.
        /// </summary>
        private void CheckCatchupTeleport(RaceTrack track, HarvesterDifficulty diff)
        {
            if (!diff.catchupTeleport || !Running || target == null || float.IsInfinity(targetGap)) return;
            if (targetRootGap < -diff.behindTolerance || targetGap <= diff.teleportGap) return;
            if (Time.time - lastTeleportTime < diff.teleportCooldown) return;

            float visible = VisibleDistance;
            if (AnyCarWithin(position, visible)) return;

            float carDistance = CarDistance(track, target);
            for (float gap = visible; gap < targetRootGap - 10f; gap += 10f)
            {
                float d = track.WrapDistance(carDistance - gap);
                if (AnyCarWithin(track.GetPoint(d), visible)) continue;
                trackDistance = d;
                SetPoseOnTrack(track, true);
                body.position = position;
                body.rotation = rotation;
                transform.SetPositionAndRotation(position, rotation);
                if (IsSpawned && networkTransform != null && networkTransform.CanCommitToTransform)
                    networkTransform.Teleport(position, rotation, transform.localScale);
                lastTeleportTime = Time.time;
                TeleportCount++;
                targetRootGap = Gap(track, carDistance);
                targetGap = targetRootGap - HeaderReach - diff.carRearOffset;
                leashLimit = Mathf.Max(diff.leashMax, targetGap);
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

        // ------------------------------------------------------------------ Sürüş

        /// <summary>Pistin ortasından izin verilen en büyük yana kayma (m).</summary>
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

        /// <summary>Hıza göre hedef hız: modlara göre (formüller HarvesterDifficulty belgesinde).</summary>
        private float DesiredSpeed(HarvesterDifficulty diff)
        {
            if (!Running) return 0f;
            float vc = target != null ? WatchOf(target).Speed : 0f;
            switch (mode)
            {
                case Mode.HedefYok: return diff.cruiseSpeed;
                case Mode.Geride: return Mathf.Max(diff.cruiseSpeed * 0.4f, vc - 6f); // yanımıza gelsinler
                case Mode.Saldiri: return Mathf.Max(0f, vc) + diff.attackClosingSpeed;
            }
            // Kalkış, baskı, yakalama: açığı pressureGap'e yumuşak oturt
            float e = targetGap - diff.pressureGap;
            float c = e >= 0f ? Mathf.Min(diff.gapGain * e, Mathf.Sqrt(2f * diff.softLandingDecel * e))
                              : Mathf.Max(diff.gapGain * e, -diff.maxBackoff);
            return Mathf.Max(0f, vc + c);
        }

        private void Drive(RaceTrack track, HarvesterDifficulty diff, float dt)
        {
            float desired = Mathf.Min(DesiredSpeed(diff), diff.maxSpeed);
            float rate = !Running ? diff.stopDeceleration : desired > speed ? diff.acceleration : diff.braking;
            speed = Mathf.MoveTowards(speed, desired, rate * dt);
            float advance = speed * dt;

            // Tasma ve en küçük açık: pist boyunca kesin sınırlar (fizik yok sayılır)
            if (Running && target != null && targetRootGap >= -diff.behindTolerance)
            {
                var w = WatchOf(target);
                // Araba bu adımda da ilerleyecek: sınırı adımın sonuna göre koy
                float predicted = targetGap + Mathf.Max(0f, w.RawSpeed) * dt;
                // Yeniden yakalama: tasma leashReelInSpeed ile kısalır, sona doğru leashLandingDecel ile yavaşlayarak oturur
                float excess = Mathf.Max(0f, Mathf.Min(leashLimit, targetGap) - diff.leashMax);
                float reel = Mathf.Min(diff.leashReelInSpeed, Mathf.Sqrt(2f * diff.leashLandingDecel * excess) + 1f);
                leashLimit = Mathf.Max(diff.leashMax, Mathf.Min(leashLimit, targetGap) - reel * dt);
                if (predicted - advance > leashLimit)
                {
                    advance = predicted - leashLimit;
                    LeashPullSteps++;
                }
                if (mode != Mode.Saldiri)
                {
                    float minGap = InLaunchWindow ? diff.launchMinGap : diff.pressureMinGap;
                    if (predicted - advance < minGap) advance = Mathf.Max(0f, predicted - minGap);
                }
                speed = advance / dt;
            }
            else if (!Running && advance > 0f && !float.IsInfinity(nearestAnyGap))
            {
                // Dururken (yarış bitti) öndeki arabaların içinden geçmesin: pressureMinGap gerisinde durur
                advance = Mathf.Min(advance, Mathf.Max(0f, nearestAnyGap - diff.pressureMinGap));
                speed = advance / dt;
            }
            if (!track.Loop)
            {
                float left = track.Length - 15f - trackDistance;
                advance = Mathf.Clamp(advance, 0f, Mathf.Max(0f, left));
                speed = advance / dt;
            }
            trackDistance = track.WrapDistance(trackDistance + advance);

            // Yanal: hedefin yanal konumu (baskıda gecikmeli, saldırıda anlık); uzaktayken yolun ortası
            float limit = Mathf.Max(0f, LateralLimit - 0.2f);
            float want = 0f;
            float lateralSpeed = diff.lateralAimSpeed;
            if (target != null && targetRootGap >= -diff.behindTolerance && Running)
            {
                if (mode == Mode.Saldiri)
                {
                    want = track.GetLateralOffset(target.transform.position, CarDistance(track, target));
                    lateralSpeed = diff.attackLateralSpeed;
                }
                else want = DelayedTargetLateral(diff.reactionDelay) * Mathf.InverseLerp(70f, 35f, targetGap);
            }
            want = Mathf.Clamp(want, -limit, limit);
            float newLateral = Mathf.MoveTowards(lateral, want, lateralSpeed * dt);
            lateralRate = (newLateral - lateral) / dt;
            lateral = newLateral;

            SetPoseOnTrack(track, false);
            body.MovePosition(position);
            body.MoveRotation(rotation);
        }

        /// <summary>Pist mesafesi + yanal kaymadan konum, zeminden yükseklik ve eğim, pist yönünden gövde yönü.</summary>
        private void SetPoseOnTrack(RaceTrack track, bool snap)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 forward = track.GetForward(trackDistance);
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 next = track.GetPoint(trackDistance) + right * lateral;
            // Yanal kayarken gövde o yöne hafifçe döner
            Vector3 heading = forward * Mathf.Max(1f, speed) + right * lateralRate;
            float wantYaw = Quaternion.LookRotation(heading).eulerAngles.y;
            float newYaw = snap ? wantYaw : Mathf.LerpAngle(yaw, wantYaw, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, Difficulty.headingSmoothing)));
            yawRate = snap ? 0f : Mathf.DeltaAngle(yaw, newYaw) / dt;
            yaw = Mathf.Repeat(newYaw, 360f);

            Vector3 grounded, normal;
            if (SampleGround(next, yaw, out grounded, out normal))
            {
                float k = snap ? 1f : 1f - Mathf.Exp(-dt / 0.06f);
                next.y = Mathf.Abs(grounded.y - position.y) > 1.5f ? grounded.y : Mathf.Lerp(position.y, grounded.y, k);
                groundNormal = snap ? normal : Vector3.Slerp(groundNormal, normal, 1f - Mathf.Exp(-dt / 0.08f));
            }
            else groundNormal = Vector3.Slerp(groundNormal, Vector3.up, snap ? 1f : 1f - Mathf.Exp(-dt / 0.3f));

            position = next;
            rotation = YawOnSlope(yaw, groundNormal);
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
            // Kalkış penceresinde ve yarış bitince biçmez; saldırıda ya da hedef yakınken biçme hızında
            if (Running && !ghost)
                want = !InLaunchWindow && (mode == Mode.Saldiri || targetGap < diff.mowingRange) ? reelMowSpeed : reelIdleSpeed;
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
                // Bitirmiş arabaya dokunulmaz; biçme sadece yarışan arabaya
                if (!IsRacing(car)) continue;
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
                if (!IsRacing(car)) continue;
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
            CarWatch w;
            float since = watches.TryGetValue(car, out w) && w.Crashed ? Time.time - w.CrashTime : -1f;
            Debug.Log("[Harvester] biçti: " + car.name + (since >= 0f ? string.Format(" (kazadan {0:F1} sn sonra; {1})", since, w.CrashReason) : " (kaza tespiti yok)"));
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
            float speedFactor = Mathf.Clamp01(speed / 40f);

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

            UpdateGhost();
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

        /// <summary>
        /// Yarış bitince (her makinede, faz ağdan gelir) harvester hayalet olur: collider'ları kapanır, boşta kayan arabalar
        /// içinden geçer, kimse biçilmez ya da savrulmaz.
        /// </summary>
        private void UpdateGhost()
        {
            var manager = RaceManager.Instance;
            bool want = manager != null && manager.isActiveAndEnabled && manager.Phase == RacePhase.Finished;
            if (want == ghost) return;
            ghost = want;
            if (ownColliders != null)
                foreach (var c in ownColliders) if (c != null) c.enabled = !ghost;
            if (ghost) Debug.Log("[Harvester] yarış bitti: collider'lar kapandı, biçme yok");
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
