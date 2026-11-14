using UnityEngine;
using UnityEngine.SceneManagement;

namespace Harvester.Race
{
    // Yalnız görsel kırpma; collider, ağ ve sürüş ayarlarına dokunmaz.
    public sealed class VisibilityBudget : MonoBehaviour
    {
        public const string DetailLayer="Detay";
        Camera configuredCamera,configuredMirror;
        CarCamera follower;
        int configuredQuality=-1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go=new GameObject("Görüş bütçesi");DontDestroyOnLoad(go);go.AddComponent<VisibilityBudget>();
        }
        public static float FarDistance(int quality){return quality==0?200:quality==1?250:300;}
        void OnEnable()
        {
            SceneManager.sceneLoaded+=Loaded;
            for(int i=0;i<SceneManager.sceneCount;i++)MarkScene(SceneManager.GetSceneAt(i));
        }
        void OnDisable(){SceneManager.sceneLoaded-=Loaded;}
        void Loaded(Scene scene,LoadSceneMode mode){configuredQuality=-1;MarkScene(scene);}
        static void MarkScene(Scene scene)
        {
            if(scene.name!="race"&&scene.name!="carTest")return;
            int layer=LayerMask.NameToLayer(DetailLayer);if(layer<0)return;
            foreach(var root in scene.GetRootGameObjects())
                foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    // Kurucu adlarıyla seçilen çarpışmasız süsler; ışık nesnesi katmanı değişmez.
                    string name=renderer.name;
                    if((name=="Direk"||name=="Kol"||name=="Bas"||name=="Pano")&&renderer.GetComponent<Collider>()==null)
                        renderer.gameObject.layer=layer;
                }
        }
        public static void MarkCarDetails(GameObject model)
        {
            int layer=LayerMask.NameToLayer(DetailLayer);if(layer<0||model==null)return;
            foreach(var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                string name=r.name.ToLowerInvariant();
                if((name.Contains("caliper")||name.Contains("lettering")||name.Contains("badge"))&&r.GetComponent<Collider>()==null)
                    r.gameObject.layer=layer;
            }
        }
        void LateUpdate()
        {
            int quality=QualitySettings.GetQualityLevel();
            bool changed=quality!=configuredQuality;
            if(follower!=CarCamera.Current||changed)
            {
                follower=CarCamera.Current;
                configuredCamera=follower!=null?follower.GetComponent<Camera>():null;
                if(configuredCamera!=null){configuredCamera.farClipPlane=FarDistance(quality);ApplyDetails(configuredCamera);}
            }
            Camera mirror=RearViewMirror.Instance!=null?RearViewMirror.Instance.MirrorCamera:null;
            if(mirror!=configuredMirror||changed){configuredMirror=mirror;if(mirror!=null)ApplyDetails(mirror);}
            configuredQuality=quality;
        }
        static void ApplyDetails(Camera camera)
        {
            var values=camera.layerCullDistances;
            int layer=LayerMask.NameToLayer(DetailLayer);
            if(layer>=0){values[layer]=120;camera.layerCullDistances=values;camera.layerCullSpherical=true;}
        }
    }
}
