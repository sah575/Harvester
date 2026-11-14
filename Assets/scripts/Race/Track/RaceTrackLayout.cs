using System.Collections.Generic;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Bir pist düzeni: orta çizginin geçtiği kontrol noktaları (x, y = yükseklik, z), turlu mu, genişlik.
    /// Pist kurucusu (Harvester > Rebuild Race Track) bu veriden eğriyi, yolu, bariyerleri ve zemini üretir.
    /// </summary>
    [CreateAssetMenu(menuName = "Harvester/Race Track Layout", fileName = "PistDuzeni")]
    public class RaceTrackLayout : ScriptableObject
    {
        [Tooltip("Menüde ve konsolda görünen ad")]
        public string displayName = "Pist";
        [Tooltip("Orta çizginin geçtiği noktalar (dünya koordinatı, y = yükseklik). Eğri bunların hepsinden geçer.")]
        public List<Vector3> controlPoints = new List<Vector3>();
        [Tooltip("Turlu pist (son nokta ilkine bağlanır) ya da A'dan B'ye")]
        public bool loop = true;
        [Tooltip("Asfaltın genişliği (m)")]
        public float width = 14f;
        [Tooltip("Orta çizgi örnek aralığı (m)")]
        public float sampleSpacing = 2f;

        /// <summary>Eşit aralıklı orta çizgi noktaları.</summary>
        public List<Vector3> SampleCenterline() => TrackSpline.Sample(controlPoints, loop, sampleSpacing);
    }
}
