using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Sahnedeki pistin hangi düzenden kurulduğunu hatırlar; "Rebuild Race Track" aynı düzeni yeniden kurar.
    /// Çalışma zamanında bir şey yapmaz.
    /// </summary>
    public class RaceTrackSource : MonoBehaviour
    {
        public RaceTrackLayout layout;
    }
}
