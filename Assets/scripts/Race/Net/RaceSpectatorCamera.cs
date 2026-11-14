using System.Collections.Generic;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Elenen oyuncunun kamerası. Hedefler: harvester ve hâlâ yarışan arabalar.
    /// ← / → (ya da A / D) hedefler arasında geçer, H harvester'a döner. Fare sağ tuşu basılıyken etrafında döndürülür.
    /// Yerel araba elenince RaceHUD kurar. Arayüz (Codex) Active, TargetName, WatchingHarvester ve Next/Previous/WatchHarvester'ı kullanır.
    /// </summary>
    [DefaultExecutionOrder(1100)] // RemoteExtrapolator'dan (1000) sonra: tahmini konumu takip etsin
    public class RaceSpectatorCamera : MonoBehaviour
    {
        [Header("Harvester izlerken")]
        [SerializeField] private float distance = 22f;
        [SerializeField] private float height = 10f;
        [SerializeField] private float lookAhead = 8f;
        [Header("Araba izlerken")]
        [SerializeField] private float carDistance = 8f;
        [SerializeField] private float carHeight = 3f;
        [SerializeField] private float carLookAhead = 6f;
        [SerializeField] private float follow = 3f;

        /// <summary>Açık izleyici kamerası; yoksa null.</summary>
        public static RaceSpectatorCamera Active { get; private set; }

        private float yawOffset;
        private Vector3 velocity;
        private bool snapped;
        private bool watchHarvester = true;
        private RaceCar watchedCar;
        private readonly List<RaceCar> candidates = new List<RaceCar>();

        public static RaceSpectatorCamera Begin()
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            if (CarCamera.Current != null)
            {
                CarCamera.Current.SetTarget(null);
                CarCamera.Current.enabled = false;
            }
            var spectator = cam.GetComponent<RaceSpectatorCamera>();
            if (spectator == null) spectator = cam.gameObject.AddComponent<RaceSpectatorCamera>();
            spectator.enabled = true;
            return spectator;
        }

        private void OnEnable() { Active = this; }
        private void OnDisable() { if (Active == this) Active = null; }

        /// <summary>Harvester'ı mı izliyoruz (yoksa bir arabayı).</summary>
        public bool WatchingHarvester => Target != null && watchHarvester && HarvesterTarget() != null;

        /// <summary>İzlenen arabanın sahibinin adı ya da "Harvester".</summary>
        public string TargetName
        {
            get
            {
                if (WatchingHarvester) return "Harvester";
                if (watchedCar == null) return "";
                var manager = RaceManager.Instance;
                if (manager != null)
                    foreach (var r in manager.Standings)
                        if (watchedCar.IsSpawned && r.CarObjectId == watchedCar.NetworkObjectId) return r.Name.ToString();
                return "Araba";
            }
        }

        /// <summary>İzlenen arabanın sahibinin ClientId'si; harvester ya da bilinmiyorsa ulong.MaxValue.</summary>
        public ulong TargetClientId
        {
            get
            {
                if (WatchingHarvester || watchedCar == null) return ulong.MaxValue;
                var manager = RaceManager.Instance;
                if (manager != null)
                    foreach (var r in manager.Standings)
                        if (watchedCar.IsSpawned && r.CarObjectId == watchedCar.NetworkObjectId) return r.ClientId;
                return ulong.MaxValue;
            }
        }

        public Transform Target
        {
            get
            {
                if (watchHarvester)
                {
                    var h = HarvesterTarget();
                    if (h != null) return h;
                }
                if (watchedCar != null && !watchedCar.IsEliminated) return watchedCar.transform;
                RefreshCandidates();
                watchedCar = candidates.Count > 0 ? candidates[0] : null;
                if (watchedCar != null) { watchHarvester = false; return watchedCar.transform; }
                return HarvesterTarget();
            }
        }

        public void WatchHarvester()
        {
            if (HarvesterTarget() == null) return;
            watchHarvester = true;
            snapped = false;
        }

        public void Next() { Step(1); }
        public void Previous() { Step(-1); }

        private void Step(int direction)
        {
            RefreshCandidates();
            bool hasHarvester = HarvesterTarget() != null;
            // Sıra: [harvester], araba1, araba2, ... (sıralamaya göre)
            int count = candidates.Count + (hasHarvester ? 1 : 0);
            if (count == 0) return;
            int current = watchHarvester && hasHarvester ? 0 : (hasHarvester ? 1 : 0) + Mathf.Max(0, candidates.IndexOf(watchedCar));
            int next = ((current + direction) % count + count) % count;
            if (hasHarvester && next == 0) { watchHarvester = true; }
            else { watchHarvester = false; watchedCar = candidates[next - (hasHarvester ? 1 : 0)]; }
            snapped = false;
        }

        private static Transform HarvesterTarget()
        {
            var manager = RaceManager.Instance;
            return manager != null ? manager.HarvesterTransform : null;
        }

        private void RefreshCandidates()
        {
            candidates.Clear();
            var manager = RaceManager.Instance;
            if (manager != null)
            {
                foreach (var r in manager.Standings)
                {
                    if (r.Status != RacerStatus.Racing) continue;
                    var car = manager.CarOf(r.ClientId);
                    if (car != null && !car.IsEliminated) candidates.Add(car);
                }
            }
            if (candidates.Count == 0)
                foreach (var car in RaceCar.All)
                    if (car != null && !car.IsEliminated) candidates.Add(car);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Next();
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) Previous();
            if (Input.GetKeyDown(KeyCode.H)) WatchHarvester();
        }

        private void LateUpdate()
        {
            Transform target = Target;
            if (target == null) return;
            if (Input.GetMouseButton(1)) yawOffset += Input.GetAxis("Mouse X") * 3f;

            bool harvester = WatchingHarvester;
            float d = harvester ? distance : carDistance, hgt = harvester ? height : carHeight, ahead = harvester ? lookAhead : carLookAhead;

            Vector3 flat = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            Quaternion yaw = Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(0f, yawOffset, 0f);
            Vector3 desired = target.position - yaw * Vector3.forward * d + Vector3.up * hgt;
            if (!snapped)
            {
                transform.position = desired;
                velocity = Vector3.zero;
                snapped = true;
            }
            // Arabalar hızlı: kamera daha sıkı takip eder
            float smooth = 1f / Mathf.Max(0.1f, harvester ? follow : follow * 3f);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smooth);
            Vector3 look = target.position + yaw * Vector3.forward * ahead - transform.position;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look, Vector3.up), 1f - Mathf.Exp(-8f * Time.deltaTime));
        }
    }
}
