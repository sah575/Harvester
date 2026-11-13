using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Arcade yarış arabası: Rigidbody + 4 ışınlı süspansiyon (WheelCollider yok).
    /// Hareket sahibinde (owner-authoritative): fiziği sadece sahibi çalıştırır, NetworkTransform herkese yayar,
    /// uzak kopyada NetworkRigidbody gövdeyi kinematik yapar ve RemoteExtrapolator yumuşatır.
    /// Ağ yokken (NetworkManager yok ya da dinlemiyor) araba yerel sahipmiş gibi çalışır; tek sahnede test edilebilir.
    ///
    /// Sürüş modeli kasıtlı olarak basit ve oyunsu:
    /// - Her tekerlekte yay + sönüm, sol/sağ arasında viraj demiri (devrilmeye karşı).
    /// - Lastik yanal tutuşu: tekerleğin yana kayma hızını her adımda bir oranda söndüren kuvvet, yüke göre sınırlı.
    ///   El freni arka tutuşu düşürür, araba kayar (drift).
    /// - Motor itişi hız eğrisiyle gövdenin ağırlık merkezine uygulanır (patinaj yok), son hız ayarlanabilir.
    /// - Direksiyon açısı hızla azalır; dönüşü bisiklet modeline göre hedef sapma hızına yardımla tamamlar.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RaceCar : NetworkBehaviour
    {
        /// <summary>Sahnedeki bütün arabalar.</summary>
        public static readonly List<RaceCar> All = new List<RaceCar>();
        /// <summary>Bu makinede sürülen araba (kamera ve HUD için).</summary>
        public static RaceCar Local { get; private set; }
        /// <summary>Yarış yöneticisi geri sayımda kapatır.</summary>
        public static bool InputEnabled = true;

        /// <summary>Herhangi bir araba elendiğinde (her makinede, parçalanmadan hemen sonra).</summary>
        public static event Action<RaceCar> AnyEliminated;
        /// <summary>Bu araba elendiğinde (her makinede). İzleyici kamerası buna bağlanır.</summary>
        public event Action OnEliminated;

        // ---- Test kancası: sadece editörde ve debug build'de girdinin yerine geçer ----
        /// <summary>Otomatik sürüş testi (CarAutoTest) girdiyi buradan verir.</summary>
        public static bool AutoDrive;
        public static float AutoThrottle;
        public static float AutoSteer;
        public static bool AutoHandbrake;

        [Header("Tekerlekler (sıra: ön sol, ön sağ, arka sol, arka sağ)")]
        [Tooltip("Görsel tekerlek objeleri (modelin parçaları). Boş olabilir; fizik bunlara bağlı değil.")]
        [SerializeField] private Transform[] wheelVisuals = new Transform[4];
        [Tooltip("Tekerlek merkezlerinin gövdeye göre dinlenme konumu (yerel). Işınlar bunun suspensionUp üstünden atılır.")]
        [SerializeField] private Vector3[] wheelCenters = new Vector3[4];
        [Tooltip("Tekerlek yarıçapı (m)")]
        [SerializeField] private float wheelRadius = 0.5f;

        [Header("Süspansiyon")]
        [Tooltip("Tekerleğin dinlenme konumundan yukarı ne kadar sıkışabileceği (m)")]
        [SerializeField] private float suspensionUp = 0.22f;
        [Tooltip("Dinlenme konumundan aşağı ne kadar sarkabileceği (m). Duran arabada yay bu kadar sıkışık olsun diye yay sertliği buna göre seçilir.")]
        [SerializeField] private float suspensionDroop = 0.12f;
        [Tooltip("Yay sertliği (N/m, tekerlek başına). Yaklaşık kütle*9.81/4/sarkma.")]
        [SerializeField] private float springStrength = 26000f;
        [Tooltip("Sönüm (N·sn/m, tekerlek başına). Kritik sönümün ~yarısı iyi: 2*sqrt(k*kütle/4)*0.5")]
        [SerializeField] private float damperStrength = 3200f;
        [Tooltip("Viraj demiri: sol-sağ sıkışma farkı başına kuvvet (N/m). Virajda yatmayı ve devrilmeyi azaltır.")]
        [SerializeField] private float antiRoll = 9000f;

        [Header("Motor ve fren")]
        [Tooltip("Son hız (m/sn). 40 m/sn = 144 km/sa")]
        [SerializeField] private float topSpeed = 40f;
        [Tooltip("Kalkıştaki en yüksek ivme (m/sn²). 0–100 km/sa süresi kabaca 27.8/(ivme*0.85)")]
        [SerializeField] private float maxAcceleration = 12f;
        [Tooltip("İtiş eğrisi: yatay eksen hız/son hız (0–1), dikey eksen ivme oranı")]
        [SerializeField] private AnimationCurve accelerationCurve = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.5f, 0.85f), new Keyframe(0.85f, 0.45f), new Keyframe(1f, 0f));
        [Tooltip("Fren yavaşlaması (m/sn²)")]
        [SerializeField] private float brakeDeceleration = 22f;
        [Tooltip("Geri vites son hızı (m/sn)")]
        [SerializeField] private float reverseTopSpeed = 12f;
        [Tooltip("Geri vites ivmesi (m/sn²)")]
        [SerializeField] private float reverseAcceleration = 8f;
        [Tooltip("Gaz bırakılınca yavaşlama (m/sn²)")]
        [SerializeField] private float coastDeceleration = 2.5f;
        [Tooltip("El freninin kendi yavaşlatması (m/sn²)")]
        [SerializeField] private float handbrakeDeceleration = 6f;
        [Tooltip("Hızın karesiyle artan bastırma kuvveti katsayısı (N/(m/sn)²). Yüksek hızda tutuşu artırır.")]
        [SerializeField] private float downforce = 2.5f;

        [Header("Direksiyon")]
        [Tooltip("Düşük hızda en büyük direksiyon açısı (derece)")]
        [SerializeField] private float maxSteerAngle = 34f;
        [Tooltip("Son hızda en büyük direksiyon açısı (derece)")]
        [SerializeField] private float highSpeedSteerAngle = 9f;
        [Tooltip("Direksiyon açısının en küçüğe indiği hız (m/sn)")]
        [SerializeField] private float steerFalloffSpeed = 35f;
        [Tooltip("Direksiyonun dönme hızı (derece/sn)")]
        [SerializeField] private float steerSpeed = 160f;
        [Tooltip("Dingil mesafesi (m); dönüş yardımının hedef sapma hızı için")]
        [SerializeField] private float wheelBase = 2.6f;
        [Tooltip("Dönüş yardımı (1/sn): gövdenin sapma hızını bisiklet modelinin hedefine çeker. 0 = sadece lastik kuvvetleri.")]
        [SerializeField] private float steerAssist = 5f;
        [Tooltip("Dönüş yardımının istediği en büyük yanal ivme (m/sn²). Hedef sapma hızı bunu aşmaz; yüksek hızda tam kırınca araba dönmez, savrulmaz.")]
        [SerializeField] private float maxCornerAccel = 15f;

        [Header("Lastik tutuşu")]
        [Tooltip("Ön lastiklerin yanal tutuşu (1/sn): yana kaymanın ne hızla söndüğü")]
        [SerializeField] private float frontGrip = 28f;
        [Tooltip("Arka lastiklerin yanal tutuşu (1/sn)")]
        [SerializeField] private float rearGrip = 34f;
        [Tooltip("Sürtünme katsayısı: yanal kuvvet tekerlek yükünün en fazla bu katı olabilir")]
        [SerializeField] private float tireFriction = 1.5f;
        [Tooltip("El freninde arka tutuşun çarpanı (drift)")]
        [Range(0f, 1f)] [SerializeField] private float handbrakeRearGrip = 0.55f;
        [Tooltip("El freni çekiliyken dönüş yardımının hedef sapma hızı çarpanı (kuyruğu savurur)")]
        [SerializeField] private float driftYawBoost = 1.25f;
        [Tooltip("Yanal kuvvetin uygulandığı yükseklik: 0 = lastik teması (çok yatar), 1 = ağırlık merkezi (hiç yatmaz)")]
        [Range(0f, 1f)] [SerializeField] private float gripForceHeight = 0.75f;

        [Header("Denge")]
        [Tooltip("Ağırlık merkezi (gövdeye göre yerel). Düşük tutulursa devrilmez.")]
        [SerializeField] private Vector3 centerOfMass = new Vector3(0f, 0.35f, 0f);
        [Tooltip("Havada gövdeyi düz tutan dönme ivmesi (1/sn²)")]
        [SerializeField] private float airUpright = 4f;
        [Tooltip("Havada dönmenin sönümü (1/sn)")]
        [SerializeField] private float airAngularDamping = 1.5f;
        [Tooltip("Bu hızın altında, gaz yokken araba eğimde kaymaz (m/sn)")]
        [SerializeField] private float holdSpeed = 1.5f;

        [Header("Sıfırlama (R)")]
        [Tooltip("İki sıfırlama arasında en az süre (sn)")]
        [SerializeField] private float resetCooldown = 1.5f;
        [Tooltip("Sıfırlarken zeminden yükseklik (m)")]
        [SerializeField] private float resetHeight = 0.6f;

        [Header("Ağ")]
        [Tooltip("Pist ilerlemesinin ağda paylaşılma aralığı (sn)")]
        [SerializeField] private float trackSyncInterval = 0.1f;

        [Header("Parçalanma")]
        [Tooltip("Parçaların savrulma hızı (m/sn)")]
        [SerializeField] private float shatterSpeed = 8f;
        [Tooltip("Yukarı fırlama hızı (en fazla, m/sn)")]
        [SerializeField] private float shatterLift = 7f;
        [Tooltip("Parçaların sahnede kalma süresi (sn)")]
        [SerializeField] private float debrisLifetime = 8f;

        [Header("Çarpma")]
        [Tooltip("ApplyHit'te çarpma noktasından gelen dönme etkisinin çarpanı")]
        [SerializeField] private float hitSpin = 0.5f;
        [Tooltip("Dayanıklılık puanı (0–100). 60'ta çarpma olduğu gibi uygulanır; her 10 puan itmeyi %10 azaltır (CarStats.HitScale).")]
        [SerializeField] private float durability = CarStats.ReferenceDurability;

        [Header("Araba tanımı")]
        [Tooltip("Araba kataloğu. Boşsa araba bu sınıftaki ayarlarla ve prefab'taki modelle sürer (eski davranış). " +
                 "Doluysa model, geometri ve fizik değerleri seçimdeki (CarLoadout) tanımdan gelir; buradaki değerlerin yerine geçer.")]
        [SerializeField] private CarCatalog catalog;

        // Sadece sunucu yazar.
        private readonly NetworkVariable<bool> eliminated = new NetworkVariable<bool>(false);
        // Sahibi yazar, herkes okur.
        private readonly NetworkVariable<float> trackDistance = new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // Sahibi yazar (araba, parçalar, renkler, performans), herkes uygular. Sonradan bağlanan da spawn'da alır.
        private readonly NetworkVariable<CarLoadout> loadout = new NetworkVariable<CarLoadout>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private Rigidbody body;
        private NetworkTransform networkTransform;
        private Collider[] ownColliders;
        private bool localEliminated;
        private bool eliminationApplied;

        // Girdi (Update'te okunur, FixedUpdate'te kullanılır)
        private float throttleInput, steerInput;
        private bool handbrakeInput;
        private float lastResetTime = -10f;
        private bool externalInput;
        private float externalThrottle, externalSteer;
        private bool externalHandbrake;

        // Fizik durumu
        private float steerAngle;
        private float forwardSpeed;
        private readonly float[] compression = new float[4];
        private readonly float[] wheelLoad = new float[4];
        private readonly bool[] grounded = new bool[4];
        private readonly RaycastHit[] hitBuffer = new RaycastHit[8];
        private int groundedCount;

        // Pist ilerlemesi
        private float localTrackDistance;
        private bool hasTrackHint;
        private float nextTrackSync;

        // Görsel
        private Quaternion[] wheelBaseRotations;
        private Transform[] wheelHubs = new Transform[4];
        private Quaternion[] hubBaseRotations = new Quaternion[4];
        private readonly float[] radii = new float[4];
        private readonly float[] wheelSpins = new float[4];
        private Vector3 lastVisualPosition;
        private Vector3 estimatedVelocity;
        private float lastVisualYaw, smoothedYawRate;
        private Light[] lights;

        // Araba tanımı ve seçim
        private CarVisual visual;
        private CarDefinition currentDefinition;
        private CarLoadout appliedLoadout;
        private bool hasAppliedLoadout;

        public Rigidbody Body => body;
        /// <summary>İleri hız (m/sn, ileri artı). Uzak kopyada konum değişiminden tahmin edilir.</summary>
        public float ForwardSpeed => forwardSpeed;
        /// <summary>Dünya hızı. Sahibinde Rigidbody'den, uzak kopyada konum değişiminden.</summary>
        public Vector3 Velocity => DrivesLocally ? body.linearVelocity : estimatedVelocity;
        public bool IsEliminated => IsSpawned ? eliminated.Value : localEliminated;
        /// <summary>Pist üzerindeki ilerleme (m, RaceTrack.GetNearestDistance). Turlu pistte 0..Length arasında sarar; tur sayımı yarış yöneticisinde.</summary>
        public float TrackDistance => IsSpawned && !IsOwner ? trackDistance.Value : localTrackDistance;
        /// <summary>Kaç tekerlek yere değiyor (0–4).</summary>
        public int GroundedWheels => groundedCount;
        public float SteerAngle => steerAngle;
        public float TopSpeed => topSpeed;
        public float WheelBase => wheelBase;
        public float BrakeDeceleration => brakeDeceleration;
        /// <summary>Dönüş yardımının izin verdiği en büyük yanal ivme (m/sn²); botlar viraj hızını buna göre planlar.</summary>
        public float MaxCornerAccel => maxCornerAccel;
        /// <summary>Dayanıklılık puanı (0–100).</summary>
        public float Durability => durability;
        /// <summary>Şu anki hızda direksiyonun en büyük açısı (derece).</summary>
        public float CurrentMaxSteerAngle => Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, Mathf.Clamp01(Mathf.Abs(forwardSpeed) / steerFalloffSpeed));
        /// <summary>Girdi klavyeden değil SetExternalInput'tan mı geliyor (bot, test sürücüsü).</summary>
        public bool HasExternalInput => externalInput;
        /// <summary>Araba kataloğu (boşsa eski, tanımsız araba).</summary>
        public CarCatalog Catalog => catalog;
        /// <summary>Uygulanmış araba tanımı (tanımsız arabada null).</summary>
        public CarDefinition Definition => currentDefinition;
        /// <summary>Uygulanmış seçim (henüz uygulanmadıysa ağdaki değer).</summary>
        public CarLoadout Loadout => hasAppliedLoadout ? appliedLoadout : loadout.Value;
        /// <summary>Sürücü göz hizası (kokpit kamerası). Modelde yoksa null.</summary>
        public Transform CockpitAnchor => visual != null ? visual.CockpitAnchor : null;

        /// <summary>Bu arabanın girdisini klavye yerine verir (bot ya da test sürücüsü). InputEnabled ve InputAllowed yine geçerli.</summary>
        public void SetExternalInput(float throttle, float steer, bool handbrake)
        {
            externalInput = true;
            externalThrottle = Mathf.Clamp(throttle, -1f, 1f);
            externalSteer = Mathf.Clamp(steer, -1f, 1f);
            externalHandbrake = handbrake;
        }

        /// <summary>Girdiyi klavyeye geri verir.</summary>
        public void ClearExternalInput()
        {
            externalInput = false;
        }

        /// <summary>Bu makine arabayı sürüyor mu: ağda sahibiyse ya da ağ hiç çalışmıyorsa.</summary>
        public bool DrivesLocally
        {
            get
            {
                if (IsSpawned) return IsOwner;
                var nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening;
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            networkTransform = GetComponent<NetworkTransform>();
            body.centerOfMass = centerOfMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            ownColliders = GetComponentsInChildren<Collider>();
            lights = GetComponentsInChildren<Light>();

            wheelBaseRotations = new Quaternion[wheelVisuals.Length];
            for (int i = 0; i < wheelVisuals.Length; i++)
                if (wheelVisuals[i] != null) wheelBaseRotations[i] = wheelVisuals[i].localRotation;
            for (int i = 0; i < 4; i++) radii[i] = wheelRadius;
            lastVisualPosition = transform.position;
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
            if (Local == this) Local = null;
        }

        private void Start()
        {
            if (!IsSpawned && DrivesLocally)
            {
                // NetworkRigidbody Awake'te gövdeyi kinematik yapıyor; ağ yokken fiziği biz çalıştırıyoruz.
                body.isKinematic = false;
                // Ağsız test: sahnedeki ilk araba yerel oyuncunun arabası olur.
                if (Local == null) BecomeLocal();
                if (catalog != null && !hasAppliedLoadout)
                    ApplyLoadout(Local == this ? CarOwnership.LoadForRace(catalog) : BotLoadout(BotSeed()));
            }
        }

        public override void OnNetworkSpawn()
        {
            eliminated.OnValueChanged += OnEliminatedChanged;
            loadout.OnValueChanged += OnLoadoutChanged;
            if (catalog != null)
            {
                if (IsOwner)
                {
                    // Oyuncunun arabası kayıtlı seçimi (sahip olunmayanlar varsayılana düşer), sunucunun botları
                    // katalogdan sırayla farklı bir arabayı farklı renkte alır
                    var l = NetworkObject.IsPlayerObject ? CarOwnership.LoadForRace(catalog) : BotLoadout(NetworkBotSeed());
                    loadout.Value = l;
                    ApplyLoadout(l);
                }
                // Uzak kopya: sahibinin seçimi henüz gelmediyse (boş) model kurulmaz, OnValueChanged'de kurulur
                else if (loadout.Value.IsValid) ApplyLoadout(loadout.Value);
            }
            if (IsOwner) BecomeLocal();
            if (eliminated.Value) ApplyElimination();
        }

        public override void OnNetworkDespawn()
        {
            eliminated.OnValueChanged -= OnEliminatedChanged;
            loadout.OnValueChanged -= OnLoadoutChanged;
            if (Local == this) Local = null;
        }

        private void BecomeLocal()
        {
            Local = this;
            CarCamera.Follow(this);
        }

        // ------------------------------------------------------------------ Girdi

        private void Update()
        {
            UpdateVisuals();
            if (!DrivesLocally || IsEliminated)
            {
                throttleInput = steerInput = 0f;
                handbrakeInput = false;
                return;
            }

            bool allowed = InputEnabled && GameManager.InputAllowed;
            throttleInput = allowed ? Input.GetAxis("Vertical") : 0f;
            steerInput = allowed ? Input.GetAxis("Horizontal") : 0f;
            handbrakeInput = allowed && Input.GetKey(KeyCode.Space);
            if (externalInput)
            {
                throttleInput = allowed ? externalThrottle : 0f;
                steerInput = allowed ? externalSteer : 0f;
                handbrakeInput = allowed && externalHandbrake;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Otomatik test girdisi de normal girdiyle aynı kapıdan geçer (geri sayımda kalkmasın)
            if (AutoDrive)
            {
                throttleInput = allowed ? Mathf.Clamp(AutoThrottle, -1f, 1f) : 0f;
                steerInput = allowed ? Mathf.Clamp(AutoSteer, -1f, 1f) : 0f;
                handbrakeInput = allowed && AutoHandbrake;
            }
#endif
            // Ters dönünce sıfırlama geri sayımda da serbest
            if (Input.GetKeyDown(KeyCode.R)) ResetToTrack();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // F6: rastgele modifiye (garaj arayüzü gelene kadar test yolu)
            if (Local == this && catalog != null) CarLoadoutStore.HandleDebugKeys(this);
#endif
        }

        // ------------------------------------------------------------------ Fizik

        private void FixedUpdate()
        {
            if (!DrivesLocally || IsEliminated || body.isKinematic) return;

            float dt = Time.fixedDeltaTime;
            Vector3 up = transform.up, forward = transform.forward;
            Vector3 velocity = body.linearVelocity;
            forwardSpeed = Vector3.Dot(velocity, forward);
            float speed = Mathf.Abs(forwardSpeed);
            float mass = body.mass;

            // Direksiyon: hız arttıkça en büyük açı küçülür
            float maxAngle = Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, Mathf.Clamp01(speed / steerFalloffSpeed));
            steerAngle = Mathf.MoveTowards(steerAngle, steerInput * maxAngle, steerSpeed * dt);

            Vector3 normalSum = Vector3.zero;
            groundedCount = 0;
            float restLength = suspensionUp + suspensionDroop;

            // 1. Süspansiyon
            for (int i = 0; i < 4; i++)
            {
                Vector3 origin = WheelOrigin(i);
                RaycastHit hit;
                if (CastWheel(origin, -up, restLength + radii[i], out hit))
                {
                    float springLength = hit.distance - radii[i];
                    float c = restLength - springLength;
                    float upSpeed = Vector3.Dot(body.GetPointVelocity(origin), up);
                    float force = c * springStrength - upSpeed * damperStrength;
                    // Tampon: yay sonuna dayanınca sertleşir, gövde zemine oturmasın
                    if (c > suspensionUp + suspensionDroop * 0.9f) force += (c - suspensionUp) * springStrength * 4f;
                    force = Mathf.Max(0f, force);
                    body.AddForceAtPosition(up * force, origin);

                    grounded[i] = true;
                    compression[i] = c;
                    wheelLoad[i] = force;
                    normalSum += hit.normal;
                    groundedCount++;
                }
                else
                {
                    grounded[i] = false;
                    compression[i] = 0f;
                    wheelLoad[i] = 0f;
                }
            }

            // 2. Viraj demirleri (ön ve arka)
            for (int axle = 0; axle < 2; axle++)
            {
                int l = axle * 2, r = l + 1;
                // Daha çok sıkışan taraf kaldırılır, öbür taraf bastırılır: gövde yatmaz
                float diff = (compression[l] - compression[r]) * antiRoll;
                if (grounded[l]) body.AddForceAtPosition(up * diff, WheelOrigin(l));
                if (grounded[r]) body.AddForceAtPosition(-up * diff, WheelOrigin(r));
            }

            if (groundedCount == 0)
            {
                // Havada: gövdeyi yavaşça düzelt, savrulmayı sönümle
                Vector3 axis = Vector3.Cross(up, Vector3.up);
                body.AddTorque(axis * airUpright, ForceMode.Acceleration);
                body.AddTorque(-body.angularVelocity * airAngularDamping, ForceMode.Acceleration);
                UpdateTrackDistance();
                return;
            }

            Vector3 groundNormal = normalSum.normalized;
            Vector3 worldCom = body.worldCenterOfMass;
            float staticLoad = mass * -Physics.gravity.y * 0.25f;

            // 3. Lastik yanal tutuşu
            for (int i = 0; i < 4; i++)
            {
                if (!grounded[i]) continue;
                bool front = i < 2;
                Vector3 origin = WheelOrigin(i);
                Vector3 wheelForward = front ? Quaternion.AngleAxis(steerAngle, up) * forward : forward;
                Vector3 wheelRight = Vector3.Cross(up, wheelForward).normalized;
                float lateral = Vector3.Dot(body.GetPointVelocity(origin), wheelRight);

                float grip = front ? frontGrip : rearGrip;
                if (!front && handbrakeInput) grip *= handbrakeRearGrip;
                float removed = lateral * (1f - Mathf.Exp(-grip * dt));
                float force = -removed / dt * mass * 0.25f;
                float limit = tireFriction * Mathf.Clamp(wheelLoad[i], 0f, staticLoad * 2.5f);
                if (!front && handbrakeInput) limit *= handbrakeRearGrip;
                force = Mathf.Clamp(force, -limit, limit);

                Vector3 contact = origin - up * (suspensionUp + radii[i]);
                Vector3 at = contact + up * (Vector3.Dot(worldCom - contact, up) * gripForceHeight);
                body.AddForceAtPosition(wheelRight * force, at);
            }

            // 4. Motor, fren, el freni
            float groundFactor = groundedCount / 4f;
            Vector3 driveDir = Vector3.ProjectOnPlane(forward, groundNormal).normalized;
            // Motor itişi (drive) ile hareketi durduran frenler (brake, büyüklük) ayrı: fren aracı ters yöne itemez
            float drive = 0f, brake = 0f;
            bool holding = false;
            if (throttleInput > 0.05f)
            {
                if (forwardSpeed < -0.5f) brake += brakeDeceleration * throttleInput;
                else drive = maxAcceleration * accelerationCurve.Evaluate(Mathf.Clamp01(forwardSpeed / topSpeed)) * throttleInput;
            }
            else if (throttleInput < -0.05f)
            {
                float t = -throttleInput;
                if (forwardSpeed > 0.5f) brake += brakeDeceleration * t;
                else drive = -reverseAcceleration * t * Mathf.Clamp01(1f - (-forwardSpeed) / reverseTopSpeed);
            }
            else if (speed < holdSpeed && groundedCount >= 3)
            {
                holding = true;
            }
            else
            {
                brake += coastDeceleration;
            }
            if (handbrakeInput) brake += handbrakeDeceleration;
            float accel = drive - MotionSign() * Mathf.Min(brake, speed / dt);
            body.AddForce(driveDir * accel * mass * groundFactor);

            if (holding || (handbrakeInput && speed < holdSpeed))
            {
                // Eğimde kaymasın: yer düzlemindeki yerçekimini ve kalan kaymayı sıfırla
                Vector3 planarGravity = Vector3.ProjectOnPlane(Physics.gravity, groundNormal);
                Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, groundNormal);
                body.AddForce((-planarGravity - planarVelocity * 15f) * mass * groundFactor);
            }

            // 5. Bastırma kuvveti
            body.AddForce(-up * downforce * forwardSpeed * forwardSpeed);

            // 6. Dönüş yardımı: sapma hızını bisiklet modelinin hedefine çek
            float targetYaw = forwardSpeed * Mathf.Tan(steerAngle * Mathf.Deg2Rad) / Mathf.Max(0.5f, wheelBase);
            float yawLimit = maxCornerAccel / Mathf.Max(3f, speed);
            float assist = steerAssist;
            if (handbrakeInput)
            {
                targetYaw *= driftYawBoost;
                yawLimit *= driftYawBoost;
                assist *= 0.6f;
            }
            targetYaw = Mathf.Clamp(targetYaw, -yawLimit, yawLimit);
            float yawRate = Vector3.Dot(body.angularVelocity, up);
            body.AddTorque(up * (targetYaw - yawRate) * assist * groundFactor, ForceMode.Acceleration);

            UpdateTrackDistance();
        }

        private float MotionSign()
        {
            return Mathf.Abs(forwardSpeed) < 0.05f ? 0f : Mathf.Sign(forwardSpeed);
        }

        private Vector3 WheelOrigin(int i)
        {
            return transform.TransformPoint(wheelCenters[i] + Vector3.up * suspensionUp);
        }

        private bool CastWheel(Vector3 origin, Vector3 direction, float length, out RaycastHit best)
        {
            best = default;
            int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int k = 0; k < count; k++)
            {
                var h = hitBuffer[k];
                if (h.distance >= bestDistance || IsIgnoredByWheels(h.collider)) continue;
                best = h;
                bestDistance = h.distance;
                found = true;
            }
            return found;
        }

        private bool IsIgnoredByWheels(Collider c)
        {
            var attached = c.attachedRigidbody;
            if (attached == body) return true;
            // Arabalar arası fizik çarpışması kapalı (CarContactSolver); ışın başka arabanın gövdesine basmasın
            if (attached != null)
                for (int i = 0; i < All.Count; i++)
                    if (All[i] != null && All[i].body == attached) return true;
            for (int i = 0; i < ownColliders.Length; i++)
                if (ownColliders[i] == c) return true;
            return false;
        }

        // ------------------------------------------------------------------ Pist ilerlemesi

        private void UpdateTrackDistance()
        {
            var track = RaceTrack.Current;
            if (track == null) return;
            localTrackDistance = track.GetNearestDistance(transform.position, hasTrackHint ? localTrackDistance : -1f);
            hasTrackHint = true;

            if (IsSpawned && IsOwner && Time.time >= nextTrackSync)
            {
                nextTrackSync = Time.time + trackSyncInterval;
                if (Mathf.Abs(trackDistance.Value - localTrackDistance) > 0.05f)
                    trackDistance.Value = localTrackDistance;
            }
        }

        // ------------------------------------------------------------------ Sıfırlama ve ışınlama

        /// <summary>Pistte en yakın noktaya düz şekilde koyar (ters dönünce R). Pist yoksa olduğu yerde düzeltir.</summary>
        public void ResetToTrack()
        {
            if (!DrivesLocally || IsEliminated || Time.time - lastResetTime < resetCooldown) return;
            lastResetTime = Time.time;

            Vector3 position;
            Quaternion rotation;
            var track = RaceTrack.Current;
            if (track != null)
            {
                float d = track.GetNearestDistance(transform.position, hasTrackHint ? localTrackDistance : -1f);
                float lateral = Mathf.Clamp(track.GetLateralOffset(transform.position, d), -track.Width * 0.3f, track.Width * 0.3f);
                Vector3 forward = track.GetForward(d);
                position = track.GetPoint(d) + track.GetRight(d) * lateral;
                rotation = Quaternion.LookRotation(forward, Vector3.up);
                localTrackDistance = d;
                hasTrackHint = true;
            }
            else
            {
                Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (flat.sqrMagnitude < 0.01f) flat = Vector3.ProjectOnPlane(transform.up, Vector3.up);
                position = transform.position;
                rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }

            // Zemini bul (pist orta çizgisi yolun yüzeyinde ama emin olalım)
            RaycastHit hit;
            if (CastWheel(position + Vector3.up * 5f, Vector3.down, 30f, out hit)) position = hit.point;
            PlaceAt(position + Vector3.up * resetHeight, rotation);
        }

        /// <summary>Arabayı hızını sıfırlayarak verilen yere ışınlar. Sadece süren makinede çalışır.</summary>
        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            if (!DrivesLocally) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            steerAngle = 0f;
            hasTrackHint = false;
            if (IsSpawned && networkTransform != null && networkTransform.CanCommitToTransform)
                networkTransform.Teleport(position, rotation, transform.localScale);
        }

        /// <summary>Sunucu (spawner) sahibine başlangıç yerini bununla bildirir.</summary>
        [Rpc(SendTo.Owner)]
        public void PlaceAtRpc(Vector3 position, Quaternion rotation)
        {
            PlaceAt(position, rotation);
        }

        // ------------------------------------------------------------------ Çarpma

        /// <summary>Anlık hız değişimi uygular. Her yerden çağrılabilir; sahibi değilsek sahibine iletilir.</summary>
        public void ApplyHit(Vector3 velocityChange, Vector3 worldPoint)
        {
            if (IsEliminated) return;
            if (DrivesLocally)
            {
                ApplyHitLocal(velocityChange, worldPoint);
                return;
            }
            if (IsSpawned) ApplyHitRpc(velocityChange, worldPoint);
        }

        [Rpc(SendTo.Owner)]
        private void ApplyHitRpc(Vector3 velocityChange, Vector3 worldPoint)
        {
            if (!IsEliminated) ApplyHitLocal(velocityChange, worldPoint);
        }

        private void ApplyHitLocal(Vector3 velocityChange, Vector3 worldPoint)
        {
            if (body.isKinematic) return;
            // Dayanıklı araba daha az savrulur, narin araba daha çok (araba–araba ve harvester çarpmaları)
            velocityChange *= CarStats.HitScale(durability);
            body.AddForce(velocityChange, ForceMode.VelocityChange);
            // Kenardan vurulan araba savrulsun ama takla atmasın: sadece Y ekseninde dönme
            Vector3 r = worldPoint - body.worldCenterOfMass;
            float yawKick = Vector3.Cross(r, velocityChange).y / Mathf.Max(1f, r.sqrMagnitude) * hitSpin;
            body.AddTorque(Vector3.up * yawKick, ForceMode.VelocityChange);
        }

        // ------------------------------------------------------------------ Eleme

        /// <summary>Sadece sunucu: arabayı eler, herkeste parçalanır. Ağ yokken yerelde uygular.</summary>
        public void Eliminate()
        {
            if (IsSpawned)
            {
                if (!IsServer || eliminated.Value) return;
                eliminated.Value = true;
                // Sunucuda OnValueChanged da tetiklenir; emin olmak için ApplyElimination tekrar çağrılmaya karşı korumalı.
                ApplyElimination();
                return;
            }
            if (localEliminated) return;
            localEliminated = true;
            ApplyElimination();
        }

        private void OnEliminatedChanged(bool previous, bool current)
        {
            if (current) ApplyElimination();
        }

        private void ApplyElimination()
        {
            if (eliminationApplied) return;
            eliminationApplied = true;

            Vector3 carVelocity = Velocity;
            Shatter(carVelocity);
            foreach (var c in ownColliders) if (c != null) c.enabled = false;
            foreach (var l in lights) if (l != null) l.enabled = false;

            if (DrivesLocally)
            {
                throttleInput = steerInput = 0f;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            OnEliminated?.Invoke();
            AnyEliminated?.Invoke(this);
        }

        /// <summary>
        /// Modelin parçalarını (gövde, tekerlekler, rüzgârlık) koparıp fizikle saçar. Sadece görsel: her makine
        /// kendi ekranında o anki pozdan başlatır, ağdan senkronlanmaz.
        /// </summary>
        private void Shatter(Vector3 carVelocity)
        {
            if (visual != null && visual.Model != null)
            {
                // Tanımlı model: tekerlekler, paneller (kapı, çamurluk, kaput, tampon, tavan) ve kasa ayrı uçar
                string id = visual.Definition != null ? visual.Definition.id : "?";
                visual.Shatter(carVelocity, shatterSpeed, shatterLift, debrisLifetime);
                Debug.Log("[Araba] Parçalandı: " + name + (IsSpawned ? " sahip=" + OwnerClientId : "") + " araba=" + id);
                for (int i = 0; i < wheelVisuals.Length; i++) wheelVisuals[i] = null;
                for (int i = 0; i < wheelHubs.Length; i++) wheelHubs[i] = null;
                return;
            }
            Vector3 center = transform.position + transform.up * 0.5f;
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                Transform piece = r.transform;
                piece.SetParent(null, true);
                piece.gameObject.AddComponent<BoxCollider>();
                var rb = piece.gameObject.AddComponent<Rigidbody>();
                rb.mass = 30f;
                Vector3 away = piece.position - center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = UnityEngine.Random.insideUnitSphere;
                away.y = 0f;
                rb.linearVelocity = carVelocity * 0.6f
                                  + away.normalized * shatterSpeed * UnityEngine.Random.Range(0.6f, 1.4f)
                                  + Vector3.up * UnityEngine.Random.Range(shatterLift * 0.4f, shatterLift);
                rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 10f;
                Destroy(piece.gameObject, debrisLifetime + UnityEngine.Random.Range(0f, 2f));
            }
            for (int i = 0; i < wheelVisuals.Length; i++) wheelVisuals[i] = null;
        }

        // ------------------------------------------------------------------ Görsel

        private void UpdateVisuals()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!DrivesLocally)
            {
                // Uzak kopya: hızı konum değişiminden tahmin et (gövde kinematik)
                Vector3 v = (transform.position - lastVisualPosition) / dt;
                estimatedVelocity = Vector3.Lerp(estimatedVelocity, v, 1f - Mathf.Exp(-dt / 0.15f));
                forwardSpeed = Vector3.Dot(estimatedVelocity, transform.forward);
                float maxAngle = Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, Mathf.Clamp01(Mathf.Abs(forwardSpeed) / steerFalloffSpeed));
                // Uzaktaki arabanın direksiyonunu sapma hızından kabaca çıkar (bisiklet modeli tersten)
                float yaw = transform.eulerAngles.y;
                float yawRate = Mathf.DeltaAngle(lastVisualYaw, yaw) / dt * Mathf.Deg2Rad;
                lastVisualYaw = yaw;
                smoothedYawRate = Mathf.Lerp(smoothedYawRate, yawRate, 1f - Mathf.Exp(-dt / 0.15f));
                float steerEstimate = Mathf.Abs(forwardSpeed) > 1f
                    ? Mathf.Atan(smoothedYawRate * wheelBase / forwardSpeed) * Mathf.Rad2Deg
                    : steerAngle;
                steerAngle = Mathf.MoveTowards(steerAngle, Mathf.Clamp(steerEstimate, -maxAngle, maxAngle), steerSpeed * dt);
            }
            lastVisualPosition = transform.position;

            if (IsEliminated) return;
            Vector3 up = transform.up;
            float restLength = suspensionUp + suspensionDroop;
            for (int i = 0; i < wheelVisuals.Length && i < 4; i++)
            {
                Transform wheel = wheelVisuals[i];
                if (wheel == null) continue;
                float radius = radii[i];
                wheelSpins[i] = Mathf.Repeat(wheelSpins[i] + forwardSpeed / Mathf.Max(0.1f, radius) * Mathf.Rad2Deg * dt, 360f);
                Vector3 origin = WheelOrigin(i);
                RaycastHit hit;
                float springLength = CastWheel(origin, -up, restLength + radius, out hit)
                    ? Mathf.Max(0f, hit.distance - radius)
                    : restLength;
                float steer = i < 2 ? steerAngle : 0f;

                Transform hub = wheelHubs[i];
                if (hub != null)
                {
                    // Göbek (kaliperle) direksiyonla döner, tekerlek göbeğin içinde kendi aksında yuvarlanır
                    hub.position = origin - up * springLength;
                    Transform hubParent = hub.parent;
                    Vector3 hubUp = hubParent != null ? hubParent.InverseTransformDirection(up) : Vector3.up;
                    hub.localRotation = Quaternion.AngleAxis(steer, hubUp) * hubBaseRotations[i];
                    wheel.localRotation = Quaternion.AngleAxis(wheelSpins[i], Vector3.right) * wheelBaseRotations[i];
                    continue;
                }

                wheel.position = origin - up * springLength;
                Transform parent = wheel.parent;
                Vector3 axisRight = parent != null ? parent.InverseTransformDirection(transform.right) : Vector3.right;
                Vector3 axisUp = parent != null ? parent.InverseTransformDirection(up) : Vector3.up;
                wheel.localRotation = Quaternion.AngleAxis(steer, axisUp) * Quaternion.AngleAxis(wheelSpins[i], axisRight) * wheelBaseRotations[i];
            }
        }

        // ------------------------------------------------------------------ Araba tanımı ve seçim

        /// <summary>
        /// Seçimi değiştirir. Ağda sadece sahibi yazabilir (herkese gider); ağ yokken yerelde uygulanır.
        /// Kalıcı kayıt için CarLoadoutStore.Save ayrıca çağrılır.
        /// </summary>
        public void SetLoadout(CarLoadout value)
        {
            if (catalog == null) return;
            if (IsSpawned)
            {
                if (!IsOwner) return;
                loadout.Value = value;
            }
            ApplyLoadout(value);
        }

        private void OnLoadoutChanged(CarLoadout previous, CarLoadout current)
        {
            ApplyLoadout(current);
        }

        /// <summary>Ağdaki botun sırası: kendisinden önce oluşturulan botların sayısı + 1 (ilk bot katalogdaki 2. araba).</summary>
        private int NetworkBotSeed()
        {
            int n = 1;
            for (int i = 0; i < All.Count && All[i] != this; i++)
                if (All[i] != null && All[i].IsSpawned && !All[i].NetworkObject.IsPlayerObject) n++;
            return n;
        }

        /// <summary>Ağsız denemede botun sırası: sahnedeki arabalar arasındaki yeri (isimden bağımsız, kararlı).</summary>
        private int BotSeed()
        {
            int i = All.IndexOf(this);
            return i >= 0 ? i : GetInstanceID();
        }

        /// <summary>Botun seçimi: katalogdan sıraya göre bir araba (stok), gövde rengi sıraya göre farklı.</summary>
        private CarLoadout BotLoadout(int seed)
        {
            int count = Mathf.Max(1, catalog.Count);
            int index = ((seed % count) + count) % count;
            if (catalog.Get(index) == null) index = 0;
            var l = catalog.DefaultLoadout(index);
            var def = catalog.Get(index);
            if (def != null && def.paints.Count > 0)
            {
                var rng = new System.Random(seed * 7919 + 17);
                Color c = Color.HSVToRGB((float)rng.NextDouble(), 0.75f, 0.85f);
                l.SetPaint(Mathf.Max(0, def.PaintIndex("Paint_Body")), c);
            }
            return l;
        }

        /// <summary>
        /// Test ve bot kancası: arabanın seçimini verilen katalog indeksine çevirir (stok, isteğe bağlı gövde rengi).
        /// Ağda sadece sahibi yazabilir; kayıt (CarLoadoutStore) değişmez.
        /// </summary>
        public void SetDefinitionIndex(int index, Color? bodyColor = null)
        {
            if (catalog == null || catalog.Get(index) == null) return;
            var l = catalog.DefaultLoadout(index);
            var def = catalog.Get(index);
            if (bodyColor.HasValue && def.paints.Count > 0) l.SetPaint(Mathf.Max(0, def.PaintIndex("Paint_Body")), bodyColor.Value);
            SetLoadout(l);
        }

        /// <summary>Seçimi uygular: tanım değiştiyse modeli kurar, geometriyi ve collider'ı ayarlar; parçalar, renkler, fizik.</summary>
        private void ApplyLoadout(CarLoadout value)
        {
            if (catalog == null || eliminationApplied) return;
            int index;
            var def = catalog.Resolve(value, out index);
            if (def == null) return;
            value = value.IsValid && value.definition == index ? def.Sanitize(value) : def.CreateDefaultLoadout(index);
            if (hasAppliedLoadout && def == currentDefinition && appliedLoadout.Equals(value)) return;

            if (visual == null) visual = GetComponent<CarVisual>();
            if (visual == null) visual = gameObject.AddComponent<CarVisual>();
            if (def != currentDefinition)
            {
                if (!visual.Build(def))
                {
                    Debug.LogWarning("[Araba] " + name + ": tanımın modeli yok (" + def.name + "), eski görünüm kalıyor");
                    return;
                }
                currentDefinition = def;
                ApplyGeometry(def.geometry);
                SetWheelVisuals(visual.Wheels, visual.Hubs);
                ownColliders = GetComponentsInChildren<Collider>();
                lights = GetComponentsInChildren<Light>();
            }
            visual.Apply(value);
            ApplyStats(def.ComputeStats(value));
            appliedLoadout = value;
            hasAppliedLoadout = true;
            Color32 body = value.GetPaint(Mathf.Max(0, def.PaintIndex("Paint_Body")));
            Debug.Log("[Araba] Seçim uygulandı: " + name + (IsSpawned ? " sahip=" + OwnerClientId + (IsOwner ? " (ben)" : "") : "")
                      + " araba=" + def.id + " gövde=#" + ColorUtility.ToHtmlStringRGB(body) + " son hız=" + topSpeed.ToString("F1")
                      + " birleşik=" + visual.IsCombined);
        }

        private void ApplyGeometry(CarGeometry g)
        {
            for (int i = 0; i < 4; i++)
            {
                if (g.wheelCenters != null && i < g.wheelCenters.Length) wheelCenters[i] = g.wheelCenters[i];
                if (g.wheelRadii != null && i < g.wheelRadii.Length && g.wheelRadii[i] > 0.05f) radii[i] = g.wheelRadii[i];
            }
            wheelRadius = (radii[0] + radii[1] + radii[2] + radii[3]) / 4f;
            if (g.wheelBase > 0.5f) wheelBase = g.wheelBase;
            var box = GetComponent<BoxCollider>();
            if (box != null && g.colliderSize.sqrMagnitude > 0.01f)
            {
                box.center = g.colliderCenter;
                box.size = g.colliderSize;
            }
        }

        private void SetWheelVisuals(Transform[] wheels, Transform[] hubs)
        {
            if (wheelVisuals == null || wheelVisuals.Length != 4) wheelVisuals = new Transform[4];
            wheelBaseRotations = new Quaternion[4];
            for (int i = 0; i < 4; i++)
            {
                wheelVisuals[i] = wheels[i];
                wheelHubs[i] = hubs[i];
                wheelBaseRotations[i] = wheels[i] != null ? wheels[i].localRotation : Quaternion.identity;
                hubBaseRotations[i] = hubs[i] != null ? hubs[i].localRotation : Quaternion.identity;
            }
        }

        /// <summary>Şu anki ayar alanlarını CarStats olarak okur (tanımsız arabanın değerleri).</summary>
        public CarStats CaptureStats()
        {
            return new CarStats
            {
                topSpeed = topSpeed, maxAcceleration = maxAcceleration, accelerationCurve = accelerationCurve,
                brakeDeceleration = brakeDeceleration, reverseTopSpeed = reverseTopSpeed, reverseAcceleration = reverseAcceleration,
                coastDeceleration = coastDeceleration, handbrakeDeceleration = handbrakeDeceleration, downforce = downforce,
                maxSteerAngle = maxSteerAngle, highSpeedSteerAngle = highSpeedSteerAngle, steerFalloffSpeed = steerFalloffSpeed,
                steerSpeed = steerSpeed, steerAssist = steerAssist, maxCornerAccel = maxCornerAccel,
                frontGrip = frontGrip, rearGrip = rearGrip, tireFriction = tireFriction, handbrakeRearGrip = handbrakeRearGrip,
                driftYawBoost = driftYawBoost, gripForceHeight = gripForceHeight,
                mass = body != null ? body.mass : 1200f, centerOfMassHeight = centerOfMass.y,
                suspensionUp = suspensionUp, suspensionDroop = suspensionDroop, springStrength = springStrength,
                damperStrength = damperStrength, antiRoll = antiRoll, airUpright = airUpright, airAngularDamping = airAngularDamping,
                durability = durability,
            };
        }

        /// <summary>Fizik değerlerini uygular (tanımlı arabada seçim değişince).</summary>
        private void ApplyStats(CarStats s)
        {
            topSpeed = s.topSpeed;
            maxAcceleration = s.maxAcceleration;
            if (s.accelerationCurve != null && s.accelerationCurve.length > 0) accelerationCurve = s.accelerationCurve;
            brakeDeceleration = s.brakeDeceleration;
            reverseTopSpeed = s.reverseTopSpeed;
            reverseAcceleration = s.reverseAcceleration;
            coastDeceleration = s.coastDeceleration;
            handbrakeDeceleration = s.handbrakeDeceleration;
            downforce = s.downforce;
            maxSteerAngle = s.maxSteerAngle;
            highSpeedSteerAngle = s.highSpeedSteerAngle;
            steerFalloffSpeed = Mathf.Max(1f, s.steerFalloffSpeed);
            steerSpeed = s.steerSpeed;
            steerAssist = s.steerAssist;
            maxCornerAccel = s.maxCornerAccel;
            frontGrip = s.frontGrip;
            rearGrip = s.rearGrip;
            tireFriction = s.tireFriction;
            handbrakeRearGrip = s.handbrakeRearGrip;
            driftYawBoost = s.driftYawBoost;
            gripForceHeight = s.gripForceHeight;
            suspensionUp = s.suspensionUp;
            suspensionDroop = s.suspensionDroop;
            springStrength = s.springStrength;
            damperStrength = s.damperStrength;
            antiRoll = s.antiRoll;
            airUpright = s.airUpright;
            airAngularDamping = s.airAngularDamping;
            durability = s.durability > 0f ? s.durability : CarStats.ReferenceDurability;
            float comZ = currentDefinition != null ? currentDefinition.geometry.centerOfMassZ : centerOfMass.z;
            centerOfMass = new Vector3(0f, s.centerOfMassHeight, comZ);
            if (body != null)
            {
                if (s.mass > 1f) body.mass = s.mass;
                body.centerOfMass = centerOfMass;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (wheelCenters == null) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < wheelCenters.Length && i < 4; i++)
            {
                Vector3 origin = transform.TransformPoint(wheelCenters[i] + Vector3.up * suspensionUp);
                Gizmos.DrawLine(origin, origin - transform.up * (suspensionUp + suspensionDroop + wheelRadius));
                Gizmos.DrawWireSphere(transform.TransformPoint(wheelCenters[i]), Application.isPlaying ? radii[i] : wheelRadius);
            }
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(transform.TransformPoint(centerOfMass), 0.1f);
        }
    }
}
