using UnityEngine;
namespace Harvester.Race
{
    // Geçici görsel parçalar kendi boya malzemelerinin ömrünü taşır; fizik kuralı içermez.
    public sealed class VisualMaterialLease : MonoBehaviour
    {
        public Material[] materials;
        void OnDestroy(){if(materials!=null)foreach(var material in materials)if(material!=null)Destroy(material);}
    }
}
