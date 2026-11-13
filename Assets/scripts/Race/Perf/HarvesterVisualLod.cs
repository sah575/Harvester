using UnityEngine;

namespace Harvester.Race
{
    // Yalnız görsel LOD kopyası: yapay zekâ/fizik özgün parçalarda çalışmaya devam eder.
    [DefaultExecutionOrder(2000)]
    public sealed class HarvesterVisualLod : MonoBehaviour
    {
        public Transform[] sources,targets;
        void LateUpdate()
        {
            for(int i=0;i<sources.Length;i++)if(sources[i]!=null&&targets[i]!=null)
            {targets[i].localPosition=sources[i].localPosition;targets[i].localRotation=sources[i].localRotation;targets[i].localScale=sources[i].localScale;}
        }
    }
}
