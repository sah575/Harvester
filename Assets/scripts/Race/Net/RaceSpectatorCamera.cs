using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Elenen oyuncunun kamerası: harvester'ı arkasından ve üstünden izler. Harvester yoksa yarışan bir arabayı.
    /// Fare sağ tuşu basılıyken etrafında döndürülebilir. Yerel araba elenince RaceHUD kurar.
    /// </summary>
    [DefaultExecutionOrder(1100)] // RemoteExtrapolator'dan (1000) sonra: tahmini konumu takip etsin
    public class RaceSpectatorCamera : MonoBehaviour
    {
        [SerializeField] private float distance = 22f;
        [SerializeField] private float height = 10f;
        [SerializeField] private float lookAhead = 8f;
        [SerializeField] private float follow = 3f;

        private float yawOffset;
        private Vector3 velocity;
        private bool snapped;

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

        private Transform FindTarget()
        {
            var manager = RaceManager.Instance;
            Transform harvester = manager != null ? manager.HarvesterTransform : null;
            if (harvester != null) return harvester;
            foreach (var car in RaceCar.All)
                if (car != null && !car.IsEliminated) return car.transform;
            return null;
        }

        private void LateUpdate()
        {
            Transform target = FindTarget();
            if (target == null) return;
            if (Input.GetMouseButton(1)) yawOffset += Input.GetAxis("Mouse X") * 3f;

            Vector3 flat = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            Quaternion yaw = Quaternion.LookRotation(flat.normalized, Vector3.up) * Quaternion.Euler(0f, yawOffset, 0f);
            Vector3 desired = target.position - yaw * Vector3.forward * distance + Vector3.up * height;
            if (!snapped)
            {
                transform.position = desired;
                snapped = true;
            }
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, 1f / Mathf.Max(0.1f, follow));
            Vector3 look = target.position + yaw * Vector3.forward * lookAhead - transform.position;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look, Vector3.up), 1f - Mathf.Exp(-6f * Time.deltaTime));
        }
    }
}
