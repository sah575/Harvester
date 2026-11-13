using System.Collections.Generic;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Pistin orta çizgisi ve ondan türetilen sorgular. Bütün yarış sistemleri pisti buradan okur:
    /// harvester yapay zekâsı yolu takip eder, yarış yöneticisi sırayı hesaplar, başlangıç dizilişi buradan gelir.
    /// Orta çizgiyi editörde pist kurucusu (TrackBuilder) doldurur; çalışırken değişmez.
    /// "Mesafe" her yerde pistin başından itibaren orta çizgi boyunca metre demektir.
    /// </summary>
    public class RaceTrack : MonoBehaviour
    {
        public static RaceTrack Current { get; private set; }

        [Tooltip("Orta çizgi noktaları (dünya koordinatı, aralıkları birkaç metre)")]
        [SerializeField] private List<Vector3> centerline = new List<Vector3>();
        [Tooltip("Turlu pist mi (son nokta ilkine bağlanır), yoksa A'dan B'ye mi")]
        [SerializeField] private bool loop;
        [Tooltip("Yolun asfalt genişliği (m)")]
        [SerializeField] private float width = 14f;
        [Tooltip("Başlangıç dizilişinin ilk sırasının mesafesi")]
        [SerializeField] private float startDistance = 80f; // A→B pistte harvester dizilişin gerisine sığsın
        [Tooltip("A'dan B'ye pistte bitiş çizgisinin sondan uzaklığı (m)")]
        [SerializeField] private float finishMargin = 30f;
        [Tooltip("Harvester'ın başlangıçta son sıranın ne kadar gerisinde durduğu (m)")]
        [SerializeField] private float harvesterStartGap = 40f;

        private float[] cumulative; // her noktaya kadar olan mesafe

        public bool Loop => loop;
        public float Width => width;
        public int PointCount => centerline.Count;
        public IReadOnlyList<Vector3> Centerline => centerline;
        public float Length { get { EnsureCache(); return cumulative.Length > 0 ? cumulative[cumulative.Length - 1] : 0f; } }
        /// <summary>
        /// Başlangıç dizilişinin ilk sırası. Turlu pistte çizginin (mesafe 0) hemen gerisi, böylece ilk tur tam sayılır;
        /// A'dan B'ye pistte <c>startDistance</c>.
        /// </summary>
        public float StartDistance => loop ? Mathf.Max(0f, Length - StartLineGap) : startDistance;

        /// <summary>Turlu pistte ön sıranın başlangıç çizgisine uzaklığı (m).</summary>
        public const float StartLineGap = 12f;
        /// <summary>A'dan B'ye pistte bitiş çizgisi. Turlu pistte tur başlangıç çizgisinde tamamlanır.</summary>
        public float FinishDistance => loop ? Length : Mathf.Max(startDistance, Length - finishMargin);

        private void Awake()
        {
            Current = this;
            cumulative = null;
            EnsureCache();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void OnValidate()
        {
            cumulative = null;
        }

        /// <summary>Editördeki pist kurucusu çağırır.</summary>
        public void SetCenterline(IList<Vector3> points, bool isLoop, float roadWidth)
        {
            centerline = new List<Vector3>(points);
            loop = isLoop;
            width = roadWidth;
            cumulative = null;
            EnsureCache();
        }

        private void EnsureCache()
        {
            if (cumulative != null && cumulative.Length == SegmentCount + 1) return;
            int segments = SegmentCount;
            cumulative = new float[segments + 1];
            for (int i = 0; i < segments; i++)
                cumulative[i + 1] = cumulative[i] + Vector3.Distance(centerline[i], centerline[(i + 1) % centerline.Count]);
        }

        private int SegmentCount => centerline.Count < 2 ? 0 : (loop ? centerline.Count : centerline.Count - 1);

        /// <summary>Mesafeyi pistin geçerli aralığına getirir (turluda sarar, değilse sınırlar).</summary>
        public float WrapDistance(float distance)
        {
            float length = Length;
            if (length <= 0f) return 0f;
            if (loop) return Mathf.Repeat(distance, length);
            return Mathf.Clamp(distance, 0f, length);
        }

        private int FindSegment(float distance, out float t)
        {
            EnsureCache();
            distance = WrapDistance(distance);
            int lo = 0, hi = SegmentCount - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (cumulative[mid] <= distance) lo = mid; else hi = mid - 1;
            }
            float segLength = cumulative[lo + 1] - cumulative[lo];
            t = segLength > 0f ? (distance - cumulative[lo]) / segLength : 0f;
            return lo;
        }

        /// <summary>Orta çizgide verilen mesafedeki nokta.</summary>
        public Vector3 GetPoint(float distance)
        {
            if (SegmentCount == 0) return centerline.Count > 0 ? centerline[0] : transform.position;
            int i = FindSegment(distance, out float t);
            return Vector3.Lerp(centerline[i], centerline[(i + 1) % centerline.Count], t);
        }

        /// <summary>Yolun o noktadaki ileri yönü (yokuşu da içerir).</summary>
        public Vector3 GetForward(float distance)
        {
            if (SegmentCount == 0) return transform.forward;
            // Köşelerde yön sıçramasın diye biraz önü ve arkası arasından ölç
            Vector3 a = GetPoint(distance - 2f), b = GetPoint(distance + 2f);
            Vector3 dir = b - a;
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
        }

        /// <summary>Yolun sağı (yatay düzlemde).</summary>
        public Vector3 GetRight(float distance)
        {
            Vector3 f = GetForward(distance);
            Vector3 right = Vector3.Cross(Vector3.up, f);
            return right.sqrMagnitude > 1e-6f ? right.normalized : transform.right;
        }

        /// <summary>
        /// Bir noktanın orta çizgiye en yakın olduğu mesafe. <paramref name="hint"/> verilirse sadece onun
        /// ±<paramref name="window"/> metresinde arar (her karede çağrılan takip kodları için ucuz ve
        /// pistin kendine yaklaştığı yerlerde yanlış kola atlamaz).
        /// </summary>
        public float GetNearestDistance(Vector3 position, float hint = -1f, float window = 60f)
        {
            int segments = SegmentCount;
            if (segments == 0) return 0f;
            EnsureCache();

            int first = 0, count = segments;
            if (hint >= 0f)
            {
                int center = FindSegment(hint, out _);
                int span = Mathf.Max(2, Mathf.CeilToInt(window / Mathf.Max(0.5f, Length / segments)));
                first = center - span;
                count = span * 2 + 1;
                if (!loop)
                {
                    int last = Mathf.Min(segments - 1, first + count - 1);
                    first = Mathf.Max(0, first);
                    count = last - first + 1;
                }
                count = Mathf.Min(count, segments);
            }

            float best = float.MaxValue, bestDistance = 0f;
            for (int k = 0; k < count; k++)
            {
                int i = ((first + k) % segments + segments) % segments;
                Vector3 a = centerline[i], b = centerline[(i + 1) % centerline.Count];
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(position - a, ab) / ab.sqrMagnitude) : 0f;
                float d2 = (a + ab * t - position).sqrMagnitude;
                if (d2 < best)
                {
                    best = d2;
                    bestDistance = cumulative[i] + (cumulative[i + 1] - cumulative[i]) * t;
                }
            }
            return bestDistance;
        }

        /// <summary>Noktanın orta çizgiden yana sapması (m, sağ artı).</summary>
        public float GetLateralOffset(Vector3 position, float distance)
        {
            return Vector3.Dot(position - GetPoint(distance), GetRight(distance));
        }

        /// <summary>Başlangıç dizilişi: sırada ikişer araba, sıralar arası 9 m. Slot 0 en öndeki.</summary>
        public Pose GetStartPose(int slot)
        {
            int row = slot / 2;
            float side = (slot % 2 == 0 ? -1f : 1f) * width * 0.25f;
            float d = StartDistance - row * 9f;
            Vector3 forward = GetForward(d);
            Vector3 position = GetPoint(d) + GetRight(d) * side;
            return new Pose(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>Harvester'ın başlangıç yeri: 4 kişilik dizilişin gerisinde, yolun ortasında.</summary>
        public Pose GetHarvesterStartPose(int racerCount = 4)
        {
            int rows = Mathf.Max(1, (racerCount + 1) / 2);
            float d = StartDistance - (rows - 1) * 9f - harvesterStartGap;
            return new Pose(GetPoint(d), Quaternion.LookRotation(GetForward(d), Vector3.up));
        }

        private void OnDrawGizmos()
        {
            if (centerline.Count < 2) return;
            Gizmos.color = Color.yellow;
            for (int i = 0; i < SegmentCount; i++)
                Gizmos.DrawLine(centerline[i], centerline[(i + 1) % centerline.Count]);
        }
    }
}
