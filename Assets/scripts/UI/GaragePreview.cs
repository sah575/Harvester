using Harvester.Race;
using UnityEngine;
using UnityEngine.UI;

namespace Harvester.UI
{
    // Önizleme yalnızca kendi katmanını çizer; yarış nesnesi veya fizik gövdesi üretmez.
    public sealed class GaragePreview : MonoBehaviour
    {
        GameObject stage;
        CarVisual visual;
        Camera view;
        RenderTexture texture;
        RawImage image;
        public void Initialize(RawImage target)
        {
            image=target;
            stage=new GameObject("Garaj önizleme alanı"); stage.transform.SetParent(transform,false); stage.transform.position=new Vector3(0,-1000,0);
            var car=new GameObject("Döner platform"); car.transform.SetParent(stage.transform,false); visual=car.AddComponent<CarVisual>();
            var cameraObject=new GameObject("Garaj kamerası"); cameraObject.transform.SetParent(stage.transform,false); view=cameraObject.AddComponent<Camera>();
            view.clearFlags=CameraClearFlags.SolidColor; view.backgroundColor=new Color(.025f,.035f,.032f); view.cullingMask=1<<30; view.fieldOfView=38;
            texture=new RenderTexture(640,400,24){filterMode=FilterMode.Point}; texture.Create(); view.targetTexture=texture; target.texture=texture;
            LightAt(new Vector3(2,5,-3),new Color(.7f,.85f,1),170);
            LightAt(new Vector3(-3,3,2),new Color(1,.43f,.16f),120);
            Zoom(false);
        }
        void LightAt(Vector3 p,Color color,float power)
        {
            var go=new GameObject("Atölye ışığı"); go.transform.SetParent(stage.transform,false); go.transform.localPosition=p;
            var light=go.AddComponent<Light>(); light.type=LightType.Point; light.color=color; light.range=12; light.intensity=power; light.cullingMask=1<<30;
        }
        public void Show(CarDefinition def,CarLoadout loadout)
        {
            if(visual==null)return; visual.Build(def); visual.Apply(loadout);
            foreach(var t in stage.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
        }
        public void Zoom(bool close)
        { view.transform.localPosition=close?new Vector3(2.8f,1.4f,2.8f):new Vector3(4.8f,2.5f,4.8f); view.transform.LookAt(stage.transform.position+Vector3.up*.65f); }
        void Update() { if(visual!=null)visual.transform.Rotate(Vector3.up,12*Time.unscaledDeltaTime,Space.Self); }
        public void Visible(bool value) { if(stage!=null)stage.SetActive(value); if(image!=null)image.gameObject.SetActive(value); }
        void OnDestroy() { if(texture!=null){texture.Release();Destroy(texture);} }
    }
}
