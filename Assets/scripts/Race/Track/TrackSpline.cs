using System.Collections.Generic;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Kontrol noktalarından geçen merkezcil (centripetal) Catmull-Rom eğrisi ve eşit aralıklı örnekleme.
    /// Merkezcil sürüm sıkışık noktalarda ilmek yapmaz, virajlar yumuşak çıkar.
    /// </summary>
    public static class TrackSpline
    {
        const int SubSteps = 48; // iki kontrol noktası arası ara örnek (yay uzunluğu ölçümü için)

        /// <summary>Eğriyi yaklaşık <paramref name="spacing"/> metre aralıklı noktalara böler. Turluda son nokta ilkini tekrarlamaz.</summary>
        public static List<Vector3> Sample(IList<Vector3> points, bool loop, float spacing)
        {
            var result = new List<Vector3>();
            int n = points.Count;
            if (n < 2) { result.AddRange(points); return result; }

            // 1) Sık ara örnekler
            var dense = new List<Vector3>();
            int segments = loop ? n : n - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector3 p1 = points[i], p2 = points[(i + 1) % n];
                Vector3 p0 = loop || i > 0 ? points[(i - 1 + n) % n] : 2f * p1 - p2;
                Vector3 p3 = loop || i + 2 < n ? points[(i + 2) % n] : 2f * p2 - p1;
                for (int s = 0; s < SubSteps; s++) dense.Add(Evaluate(p0, p1, p2, p3, s / (float)SubSteps));
            }
            dense.Add(loop ? dense[0] : points[n - 1]);

            // 2) Yay uzunluğuna göre eşit aralıklı yeniden örnekleme
            var cumulative = new float[dense.Count];
            for (int i = 1; i < dense.Count; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(dense[i - 1], dense[i]);
            float length = cumulative[dense.Count - 1];
            int count = Mathf.Max(2, Mathf.RoundToInt(length / Mathf.Max(0.5f, spacing)));
            int j = 0;
            int last = loop ? count - 1 : count;
            for (int k = 0; k <= last; k++)
            {
                float d = length * k / count;
                while (j < dense.Count - 2 && cumulative[j + 1] < d) j++;
                float segLength = cumulative[j + 1] - cumulative[j];
                float t = segLength > 1e-6f ? (d - cumulative[j]) / segLength : 0f;
                result.Add(Vector3.Lerp(dense[j], dense[j + 1], Mathf.Clamp01(t)));
            }
            return result;
        }

        static Vector3 Evaluate(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float t0 = 0f;
            float t1 = Knot(t0, p0, p1);
            float t2 = Knot(t1, p1, p2);
            float t3 = Knot(t2, p2, p3);
            float t = Mathf.Lerp(t1, t2, u);
            Vector3 a1 = Blend(p0, p1, t0, t1, t), a2 = Blend(p1, p2, t1, t2, t), a3 = Blend(p2, p3, t2, t3, t);
            Vector3 b1 = Blend(a1, a2, t0, t2, t), b2 = Blend(a2, a3, t1, t3, t);
            return Blend(b1, b2, t1, t2, t);
        }

        static float Knot(float t, Vector3 a, Vector3 b) => t + Mathf.Sqrt(Mathf.Max(Vector3.Distance(a, b), 1e-4f));

        static Vector3 Blend(Vector3 a, Vector3 b, float ta, float tb, float t) =>
            (tb - t) / (tb - ta) * a + (t - ta) / (tb - ta) * b;
    }
}
