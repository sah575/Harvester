using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Yerel oyuncunun arabasını arkadan takip eden kamera. Viraja hafif gecikmeyle döner, hızlandıkça
    /// görüş açısı genişler ve biraz geri çekilir. Arkada duvar varsa öne yaklaşır.
    /// Araba sahibinde RaceCar.Follow ile kurulur: sahnedeki ana kamerayı ele geçirir, yoksa yenisini açar.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CarCamera : MonoBehaviour
    {
        public static CarCamera Current { get; private set; }

        [Tooltip("Arabanın arkasındaki mesafe (m)")]
        [SerializeField] private float distance = 7f;
        [Tooltip("Son hızda eklenen geri çekilme (m)")]
        [SerializeField] private float speedPullback = 2f;
        [Tooltip("Arabanın üstündeki yükseklik (m)")]
        [SerializeField] private float height = 2.6f;
        [Tooltip("Bakılan noktanın arabadan yüksekliği (m)")]
        [SerializeField] private float lookHeight = 1.1f;
        [Tooltip("Bakılan noktanın arabanın ne kadar önünde olduğu (m)")]
        [SerializeField] private float lookAhead = 4f;
        [Tooltip("Kameranın arabanın yönüne dönme hızı (1/sn). Küçüldükçe virajda daha çok gecikir.")]
        [SerializeField] private float headingFollow = 4f;
        [Tooltip("Yükseklik/eğim takibinin yumuşaklığı (sn)")]
        [SerializeField] private float verticalSmoothing = 0.15f;
        [Tooltip("Dururken görüş açısı (derece)")]
        [SerializeField] private float minFov = 62f;
        [Tooltip("Son hızda görüş açısı (derece)")]
        [SerializeField] private float maxFov = 80f;
        [Tooltip("Engel algılama yarıçapı (m)")]
        [SerializeField] private float collisionRadius = 0.3f;

        /// <summary>C tuşuyla değişen kamera açıları.</summary>
        public enum Mode { YakinTakip, UzakTakip, Kaput, Kokpit }

        const string ModePrefKey = "Harvester.CarCameraMode";
        static readonly string[] ModeNames = { "Yakın takip", "Uzak takip", "Kaput", "Kokpit" };

        [Header("Uzak takip")]
        [SerializeField] private float farDistance = 11f;
        [SerializeField] private float farHeight = 4f;

        [Header("Kaput kamerası")]
        [Tooltip("Kameranın arabanın üst yüzünden yüksekliği (m)")]
        [SerializeField] private float hoodHeight = 0.25f;
        [Tooltip("Kameranın araba ortasından öne kayması, araba boyunun oranı olarak")]
        [SerializeField] private float hoodForward = 0.12f;
        [Tooltip("Kaput kamerasının arabanın eğimini/yatmasını izleme hızı (yüksek = daha sert)")]
        [SerializeField] private float hoodRotationFollow = 12f;
        [SerializeField] private float hoodFov = 72f;

        [Header("Kokpit kamerası (modelin camera_cockpit noktası)")]
        [Tooltip("Gövdenin eğim/yatmasının kameraya geçen oranı (0 = ufuk sabit, 1 = gövdeye çakılı)")]
        [Range(0f, 1f)] [SerializeField] private float cockpitTiltFollow = 0.45f;
        [Tooltip("Eğim/yatma takibinin yumuşaklığı (sn). Büyüdükçe kafa daha az sallanır.")]
        [SerializeField] private float cockpitTiltSmoothing = 0.12f;
        [Tooltip("Yol hissi: gövdenin dikey sarsıntısından kafaya geçen oran")]
        [Range(0f, 1f)] [SerializeField] private float cockpitBumpFollow = 0.35f;
        [Tooltip("Kafanın gövdeye göre en fazla dikey kayması (m)")]
        [SerializeField] private float cockpitBumpLimit = 0.03f;
        [Tooltip("Hızlanırken/frenlerken kafanın en fazla ileri-geri eğilmesi (derece)")]
        [SerializeField] private float cockpitPitchKick = 2f;
        [SerializeField] private float cockpitFov = 72f;
        [SerializeField] private float cockpitNearClip = 0.03f;
        [Tooltip("Kameranın camera_cockpit noktasından yukarı kayması (m). Torpido yolu daha az kapatsın.")]
        [SerializeField] private float cockpitRaise = 0.06f;
        [Tooltip("Kameranın camera_cockpit noktasından öne kayması (m)")]
        [SerializeField] private float cockpitForward = 0.03f;
        [Tooltip("Kokpit kamerasının sabit aşağı bakışı (derece). Ufuk ekranın ortasının biraz üstüne gelir, yol görünür.")]
        [SerializeField] private float cockpitLookDown = 7f;

        public Mode CurrentMode { get; private set; }

        private RaceCar target;
        private Camera cam;
        private float defaultNearClip;
        private float modeMessageUntil;
        private Quaternion hoodRotation;
        private float cockpitPitch, cockpitRoll, cockpitHeadOffset, cockpitPrevY, cockpitPrevSpeed, cockpitAccelPitch;
        private GUIStyle messageStyle;
        private RaceCar cockpitViewCar;
        private CarVisual cockpitViewVisual;
        private float heading;
        private float smoothedHeight;
        private float heightVelocity;
        private bool snapped;
        private readonly RaycastHit[] hits = new RaycastHit[16];

        public RaceCar Target => target;

        /// <summary>Kamerayı bu arabaya bağlar. Ana kamerayı kullanır, yoksa yenisini oluşturur.</summary>
        public static CarCamera Follow(RaceCar car)
        {
            CarCamera follower = Current;
            if (follower == null)
            {
                Camera main = Camera.main;
                if (main == null)
                {
                    var go = new GameObject("CarCamera");
                    go.tag = "MainCamera";
                    main = go.AddComponent<Camera>();
                    go.AddComponent<AudioListener>();
                }
                follower = main.GetComponent<CarCamera>();
                if (follower == null) follower = main.gameObject.AddComponent<CarCamera>();
            }
            follower.SetTarget(car);
            return follower;
        }

        public void SetTarget(RaceCar car)
        {
            target = car;
            snapped = false;
        }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            defaultNearClip = cam.nearClipPlane;
            Current = this;
            CurrentMode = (Mode)Mathf.Clamp(PlayerPrefs.GetInt(ModePrefKey, 0), 0, ModeNames.Length - 1);
        }

        private void OnDisable()
        {
            SetCockpitViewCar(null);
        }

        private void OnDestroy()
        {
            SetCockpitViewCar(null);
            if (Current == this) Current = null;
        }

        /// <summary>
        /// Kokpit görünümü (camlar gizli) yalnız kokpit modunda ve yalnız yerel oyuncunun kendi arabasında açık olur.
        /// Mod, hedef ya da yerel araba değişince önceki arabanınki geri alınır.
        /// </summary>
        private void UpdateCockpitView()
        {
            RaceCar want = target != null && CurrentMode == Mode.Kokpit && target == RaceCar.Local
                && !target.IsEliminated && target.CockpitAnchor != null ? target : null;
            if (want != cockpitViewCar) SetCockpitViewCar(want);
        }

        private void SetCockpitViewCar(RaceCar car)
        {
            if (cockpitViewVisual != null) cockpitViewVisual.SetCockpitView(false);
            cockpitViewCar = car;
            cockpitViewVisual = car != null ? car.GetComponent<CarVisual>() : null;
            if (cockpitViewVisual != null) cockpitViewVisual.SetCockpitView(true);
        }

        private void Update()
        {
            if (target == null || !Input.GetKeyDown(KeyCode.C)) return;
            CurrentMode = (Mode)(((int)CurrentMode + 1) % ModeNames.Length);
            PlayerPrefs.SetInt(ModePrefKey, (int)CurrentMode);
            modeMessageUntil = Time.unscaledTime + 1.5f;
            snapped = false;
        }

        private void LateUpdate()
        {
            UpdateCockpitView();
            if (target == null) return;
            float dt = Time.deltaTime;
            Transform t = target.transform;

            if (CurrentMode == Mode.Kaput)
            {
                UpdateHood(t, dt);
                return;
            }
            if (CurrentMode == Mode.Kokpit)
            {
                UpdateCockpit(t, dt);
                return;
            }
            cam.nearClipPlane = defaultNearClip;
            float camDistance = CurrentMode == Mode.UzakTakip ? farDistance : distance;
            float camHeight = CurrentMode == Mode.UzakTakip ? farHeight : height;

            Vector3 flatForward = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            float carYaw = flatForward.sqrMagnitude > 0.01f ? Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg : heading;
            float speed01 = Mathf.Clamp01(Mathf.Abs(target.ForwardSpeed) / Mathf.Max(1f, target.TopSpeed));

            if (!snapped)
            {
                heading = carYaw;
                smoothedHeight = t.position.y;
                heightVelocity = 0f;
                snapped = true;
            }
            heading = Mathf.LerpAngle(heading, carYaw, 1f - Mathf.Exp(-headingFollow * dt));
            smoothedHeight = Mathf.SmoothDamp(smoothedHeight, t.position.y, ref heightVelocity, verticalSmoothing);

            Quaternion yaw = Quaternion.Euler(0f, heading, 0f);
            Vector3 basePos = new Vector3(t.position.x, smoothedHeight, t.position.z);
            Vector3 pivot = basePos + Vector3.up * lookHeight;
            Vector3 desired = basePos - yaw * Vector3.forward * (camDistance + speedPullback * speed01) + Vector3.up * camHeight;

            // Arkada duvar ya da bariyer varsa kamera öne gelsin (arabaların kendisi yok sayılır)
            Vector3 toCam = desired - pivot;
            float length = toCam.magnitude;
            if (length > 0.01f)
            {
                int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, toCam / length, hits, length,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float nearest = length;
                for (int i = 0; i < count; i++)
                {
                    if (hits[i].collider.attachedRigidbody != null) continue; // arabalar, enkaz, harvester
                    if (hits[i].distance > 0f && hits[i].distance < nearest) nearest = hits[i].distance;
                }
                desired = pivot + toCam / length * Mathf.Max(1f, nearest - 0.1f);
            }

            transform.position = desired;
            Vector3 look = pivot + yaw * Vector3.forward * lookAhead - transform.position;
            if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);

            float fov = Mathf.Lerp(minFov, maxFov, speed01 * speed01);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-3f * dt));
        }

        /// <summary>
        /// Kaput kamerası: sürücü gözü hizasında, kaputun hemen üstünde. Modelde kabin olmadığı için kamera içeride değil,
        /// üst yüzün biraz üstünde ve önde duruyor. Arabanın eğimini ve yatmasını hafif yumuşatarak izler, hız hissi verir.
        /// </summary>
        private void UpdateHood(Transform t, float dt)
        {
            var box = target.GetComponent<BoxCollider>();
            Vector3 local = box != null
                ? new Vector3(box.center.x, box.center.y + box.size.y * 0.5f + hoodHeight, box.center.z + box.size.z * hoodForward)
                : new Vector3(0f, 1.3f, 0.5f);
            transform.position = t.TransformPoint(local);

            if (!snapped)
            {
                hoodRotation = t.rotation;
                snapped = true;
            }
            hoodRotation = Quaternion.Slerp(hoodRotation, t.rotation, 1f - Mathf.Exp(-hoodRotationFollow * dt));
            transform.rotation = hoodRotation;

            cam.nearClipPlane = 0.05f;
            float speed01 = Mathf.Clamp01(Mathf.Abs(target.ForwardSpeed) / Mathf.Max(1f, target.TopSpeed));
            float fov = Mathf.Lerp(hoodFov, hoodFov + 10f, speed01 * speed01);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-3f * dt));
        }

        /// <summary>
        /// Kokpit kamerası: modelin sürücü göz noktasında (camera_cockpit). Yön gövdenin sapmasını birebir izler;
        /// eğim ve yatma kısmen ve yumuşatılarak geçer, kafa sallanmaz. Yol hissi için gövdenin dikey hareketinin küçük
        /// bir kısmı ve hızlanma/frenlemede hafif bir öne-arkaya eğilme eklenir. Model noktası yoksa kaput kamerası.
        /// </summary>
        private void UpdateCockpit(Transform t, float dt)
        {
            Transform anchor = target.CockpitAnchor;
            if (anchor == null)
            {
                UpdateHood(t, dt);
                return;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.ProjectOnPlane(-t.up, Vector3.up);
            Quaternion yaw = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            // Gövdenin yawsız eğim ve yatması
            Vector3 local = (Quaternion.Inverse(yaw) * t.rotation).eulerAngles;
            float bodyPitch = Mathf.DeltaAngle(0f, local.x);
            float bodyRoll = Mathf.DeltaAngle(0f, local.z);

            float speed = target.ForwardSpeed;
            float y = anchor.position.y;
            if (!snapped)
            {
                cockpitPitch = bodyPitch;
                cockpitRoll = bodyRoll;
                cockpitHeadOffset = 0f;
                cockpitPrevY = y;
                cockpitPrevSpeed = speed;
                cockpitAccelPitch = 0f;
                snapped = true;
            }
            float k = dt > 0f ? 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, cockpitTiltSmoothing)) : 0f;
            cockpitPitch = Mathf.Lerp(cockpitPitch, bodyPitch, k);
            cockpitRoll = Mathf.Lerp(cockpitRoll, bodyRoll, k);

            // Yol hissi: gövdenin ani dikey hareketinin bir kısmı kafaya gecikmeli geçer (sonra merkeze döner)
            float dy = y - cockpitPrevY;
            cockpitPrevY = y;
            cockpitHeadOffset = Mathf.Clamp(cockpitHeadOffset - dy * cockpitBumpFollow, -cockpitBumpLimit, cockpitBumpLimit);
            cockpitHeadOffset = Mathf.Lerp(cockpitHeadOffset, 0f, dt > 0f ? 1f - Mathf.Exp(-dt / 0.08f) : 0f);

            // Hızlanmada kafa hafif geriye, frende öne
            float accel = dt > 0f ? (speed - cockpitPrevSpeed) / dt : 0f;
            cockpitPrevSpeed = speed;
            float kickTarget = Mathf.Clamp(-accel * 0.12f, -cockpitPitchKick, cockpitPitchKick);
            cockpitAccelPitch = Mathf.Lerp(cockpitAccelPitch, kickTarget, dt > 0f ? 1f - Mathf.Exp(-dt / 0.25f) : 0f);

            transform.position = anchor.position + t.up * (cockpitHeadOffset + cockpitRaise) + t.forward * cockpitForward;
            transform.rotation = yaw * Quaternion.Euler(cockpitPitch * cockpitTiltFollow + cockpitAccelPitch + cockpitLookDown, 0f, cockpitRoll * cockpitTiltFollow);

            cam.nearClipPlane = cockpitNearClip;
            float speed01 = Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(1f, target.TopSpeed));
            float fov = Mathf.Lerp(cockpitFov, cockpitFov + 8f, speed01 * speed01);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-3f * dt));
        }

        private void OnGUI()
        {
            if (target == null || Time.unscaledTime > modeMessageUntil) return;
            if (messageStyle == null)
                messageStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            float scale = Screen.height / 1080f;
            messageStyle.fontSize = Mathf.RoundToInt(28 * scale);
            var rect = new Rect(0f, Screen.height * 0.78f, Screen.width, 40f * scale);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), "Kamera: " + ModeNames[(int)CurrentMode], messageStyle);
            GUI.color = Color.white;
            GUI.Label(rect, "Kamera: " + ModeNames[(int)CurrentMode] + "  <size=70%>(C)</size>", messageStyle);
        }
    }
}
