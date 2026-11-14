using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Harvester'ı izleyen kamera: kabinden (sürücü gözü) ya da arkadan-yukarıdan takip (önündeki hedef araba da kadrajda).
    /// Şimdilik sadece test koşucusu kullanıyor; oyuna bağlı değil. İzleyici kamerası ileride aynı bileşeni bir kameraya
    /// ekleyip <see cref="Mode"/> ile kullanabilir. Harvester'ın ağ kopyasında da çalışır (sadece Transform okur).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HarvesterViewCamera : MonoBehaviour
    {
        public enum ViewMode { Kabin, Takip }

        [Tooltip("Başlangıç görünümü")]
        public ViewMode Mode = ViewMode.Kabin;
        [Tooltip("İzlenen harvester (boşsa HarvesterAI.Instance)")]
        public HarvesterAI Harvester;

        [Header("Kabin")]
        [Tooltip("Kabinde görüş açısı (derece)")]
        public float CabinFieldOfView = 72f;
        [Tooltip("Kabinde yakın kırpma (m): kabin içi doğru görünsün")]
        public float CabinNearClip = 0.05f;
        [Tooltip("Kabindeyken gizlenen camlar (ad öneki). Modelin camları yarı saydam beyaz; içeriden bakınca yolu örtüyor.")]
        public string CabinHiddenPrefix = "cam_";
        [Tooltip("Sarsıntı genliği (m), duranken")]
        public float ShakeBase = 0.01f;
        [Tooltip("Sarsıntının hızla artışı (m / (m/sn))")]
        public float ShakePerSpeed = 0.0009f;

        [Header("Takip")]
        public float FollowFieldOfView = 62f;
        public float FollowDistance = 24f;
        public float FollowHeight = 12f;
        [Tooltip("Hedef araba bu mesafeden yakınsa kadraja alınır (m)")]
        public float FrameTargetWithin = 150f;

        private Camera cam;
        private float defaultNear;
        private ViewMode appliedMode = (ViewMode)(-1);
        private HarvesterAI glassOwner;
        private readonly System.Collections.Generic.List<Renderer> hiddenGlass = new System.Collections.Generic.List<Renderer>();

        private void Awake()
        {
            cam = GetComponent<Camera>();
            defaultNear = cam.nearClipPlane;
        }

        /// <summary>Kabin ↔ takip.</summary>
        public void Toggle()
        {
            Mode = Mode == ViewMode.Kabin ? ViewMode.Takip : ViewMode.Kabin;
        }

        private void LateUpdate()
        {
            var h = Harvester != null ? Harvester : HarvesterAI.Instance;
            if (h == null) return;
            bool snap = appliedMode != Mode;
            if (snap)
            {
                appliedMode = Mode;
                cam.nearClipPlane = Mode == ViewMode.Kabin ? CabinNearClip : Mathf.Max(0.1f, defaultNear);
                cam.fieldOfView = Mode == ViewMode.Kabin ? CabinFieldOfView : FollowFieldOfView;
            }
            if (snap || glassOwner != h) SetGlassHidden(h, Mode == ViewMode.Kabin);
            if (Mode == ViewMode.Kabin) PlaceCabin(h);
            else PlaceFollow(h, snap);
        }

        /// <summary>Kabindeyken camları gizler (sadece bu makinede, görsel).</summary>
        private void SetGlassHidden(HarvesterAI h, bool hidden)
        {
            foreach (var r in hiddenGlass) if (r != null) r.forceRenderingOff = false;
            hiddenGlass.Clear();
            glassOwner = h;
            if (!hidden || h == null || string.IsNullOrEmpty(CabinHiddenPrefix)) return;
            foreach (var r in h.GetComponentsInChildren<Renderer>())
            {
                if (!r.name.StartsWith(CabinHiddenPrefix)) continue;
                r.forceRenderingOff = true;
                hiddenGlass.Add(r);
            }
        }

        private void OnDisable()
        {
            foreach (var r in hiddenGlass) if (r != null) r.forceRenderingOff = false;
            hiddenGlass.Clear();
            glassOwner = null;
            appliedMode = (ViewMode)(-1);
        }

        private void PlaceCabin(HarvesterAI h)
        {
            Transform eye = h.CabinView != null ? h.CabinView : h.transform;
            // Hafif motor/yol sarsıntısı: hızla artan, düşük frekanslı gürültü. Eğimi kabin noktası zaten izliyor.
            float amplitude = ShakeBase + ShakePerSpeed * Mathf.Abs(h.Speed);
            float t = Time.time * 9f;
            Vector3 shake = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * 2f * amplitude;
            float roll = (Mathf.PerlinNoise(t * 0.5f, 5.1f) - 0.5f) * 40f * amplitude;
            transform.SetPositionAndRotation(eye.TransformPoint(shake / Mathf.Max(0.0001f, eye.lossyScale.x)),
                eye.rotation * Quaternion.Euler(0f, 0f, roll));
        }

        private void PlaceFollow(HarvesterAI h, bool snap)
        {
            Transform ht = h.transform;
            Vector3 forward = Vector3.ProjectOnPlane(ht.forward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            Vector3 wanted = ht.position - forward * FollowDistance + Vector3.up * FollowHeight;
            Vector3 look = ht.position + forward * 25f;
            var target = h.Target;
            if (target != null && !target.IsEliminated)
            {
                Vector3 tp = target.transform.position;
                if ((tp - ht.position).sqrMagnitude < FrameTargetWithin * FrameTargetWithin)
                    look = Vector3.Lerp(ht.position + forward * 10f, tp, 0.6f);
            }
            float k = snap ? 1f : 1f - Mathf.Exp(-Time.deltaTime / 0.2f);
            // Işınlanmada (görünmezken yetişme) kayarak değil atlayarak gel
            Vector3 position = snap || (transform.position - wanted).sqrMagnitude > 60f * 60f ? wanted : Vector3.Lerp(transform.position, wanted, k);
            Quaternion rotation = Quaternion.LookRotation(look - position, Vector3.up);
            transform.SetPositionAndRotation(position, snap ? rotation : Quaternion.Slerp(transform.rotation, rotation, k));
        }
    }
}
