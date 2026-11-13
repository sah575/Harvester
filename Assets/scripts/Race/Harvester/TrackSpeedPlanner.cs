using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Pistin önündeki virajlara göre güvenli hız. Harvester yapay zekâsı ve test sürücüsü kullanır.
    /// Her örnekte viraj hızı (karekök(yanal ivme / eğrilik)) ve oraya kadar frenle inilebilecek hız hesaplanır;
    /// en küçüğü bugünkü hız sınırıdır.
    /// </summary>
    public static class TrackSpeedPlanner
    {
        /// <summary>Verilen mesafede orta çizginin yatay eğriliği (1/m).</summary>
        public static float Curvature(RaceTrack track, float distance, float span = 10f)
        {
            Vector3 a = track.GetForward(distance - span * 0.5f);
            Vector3 b = track.GetForward(distance + span * 0.5f);
            a.y = 0f;
            b.y = 0f;
            return Vector3.Angle(a, b) * Mathf.Deg2Rad / span;
        }

        /// <summary>Önümüzdeki virajlara frenle yetişebilmek için şu an gidilebilecek en yüksek hız.</summary>
        public static float SafeSpeed(RaceTrack track, float distance, float currentSpeed, float maxSpeed,
            float lateralAcceleration, float braking, float step = 5f)
        {
            float horizon = Mathf.Min(160f, currentSpeed * currentSpeed / (2f * Mathf.Max(0.1f, braking)) + 25f);
            float best = maxSpeed;
            for (float s = 0f; s <= horizon; s += step)
            {
                if (!track.Loop && distance + s > track.Length) break;
                float k = Curvature(track, distance + s);
                if (k < 1e-4f) continue;
                float corner = Mathf.Sqrt(lateralAcceleration / k);
                float allowed = Mathf.Sqrt(corner * corner + 2f * braking * s);
                if (allowed < best) best = allowed;
            }
            return best;
        }
    }
}
