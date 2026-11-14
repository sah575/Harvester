using Harvester.Race;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Harvester.UI
{
    // Atölye yalnızca önizleme kamerasının katmanında yaşar; yarış sahnesi ve ışıkları değişmez.
    public sealed class GaragePreview : MonoBehaviour
    {
        GameObject stage;
        CarVisual visual;
        Camera view;
        RenderTexture texture;
        RawImage image;
        Material steel, concrete, dark, amber, white, rubber;
        Vector3 target = new Vector3(0, .65f, 0), currentTarget;
        float yaw = 36, pitch = 14, distance = 8.4f, currentDistance = 8.4f, idleAfter;
        bool held,focused;
        public bool IsHeld => held;
        public float Yaw => yaw;
        public void Initialize(RawImage output)
        {
            image = output;
            var drag = output.gameObject.GetComponent<GaragePreviewDrag>() ?? output.gameObject.AddComponent<GaragePreviewDrag>();
            drag.preview = this;
            image.raycastTarget = true;
            stage = new GameObject("Gece vardiyası atölyesi");
            stage.transform.position = new Vector3(0, -1000, 0);
            steel = Material("Platform çeliği", new Color(.15f,.17f,.16f), .55f);
            concrete = Material("Beton", new Color(.095f,.105f,.09f), 0);
            dark = Material("Koyu metal", new Color(.055f,.065f,.061f), .3f);
            amber = Material("Emniyet sarısı", new Color(.64f,.55f,.22f), .15f);
            white = Material("Atölye ışık şeridi", new Color(.77f,.85f,.65f), 0, 2);
            rubber = Material("Lastik kauçuğu", new Color(.025f,.028f,.025f), 0);
            BuildWorkshop();
            var car = new GameObject("Sergilenen araç"); car.transform.SetParent(stage.transform, false);
            visual = car.AddComponent<CarVisual>();
            var cameraObject = new GameObject("Garaj kamerası"); cameraObject.transform.SetParent(stage.transform, false);
            view = cameraObject.AddComponent<Camera>(); view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.022f,.027f,.021f); view.cullingMask = 1 << 30;
            view.fieldOfView = 42; view.nearClipPlane = .08f; view.farClipPlane = 45;
            texture = new RenderTexture(1280, 720, 24) { filterMode = FilterMode.Bilinear };
            texture.Create(); view.targetTexture = texture; output.texture = texture;
            LightAt(new Vector3(1,5,2), new Color(.93f,.94f,.77f), 65, 20);
            LightAt(new Vector3(-4,3,0), new Color(.52f,.69f,.77f), 40, 18);
            LightAt(new Vector3(0,3,-5), new Color(1,.48f,.2f), 55, 18);
            currentTarget = target; UpdateCamera(true); SetLayers();
        }
        Material Material(string name, Color color, float metallic, float glow = 0)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", .3f);
            if (glow > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * glow); }
            return m;
        }
        GameObject Shape(string name, PrimitiveType type, Vector3 p, Vector3 scale, Material material, Vector3 rotation = default)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(stage.transform, false);
            go.transform.localPosition = p; go.transform.localScale = scale; go.transform.localEulerAngles = rotation;
            go.GetComponent<Renderer>().sharedMaterial = material; Destroy(go.GetComponent<Collider>()); return go;
        }
        void BuildWorkshop()
        {
            Shape("Beton zemin", PrimitiveType.Cube, new Vector3(0,-.38f,0), new Vector3(28,.3f,28), concrete);
            Shape("Platform alt bilezik", PrimitiveType.Cylinder, new Vector3(0,-.23f,0), new Vector3(6.8f,.11f,6.8f), dark);
            Shape("Yuvarlak sergi platformu", PrimitiveType.Cylinder, new Vector3(0,-.1f,0), new Vector3(6.5f,.1f,6.5f), steel);
            for (int i=0; i<48; i++)
            {
                float angle = i * 360f / 48; float rad = angle * Mathf.Deg2Rad;
                Shape("Platform emniyet dilimi", PrimitiveType.Cube, new Vector3(Mathf.Sin(rad)*3.24f,-.085f,Mathf.Cos(rad)*3.24f), new Vector3(.32f,.055f,.12f), i%2==0?amber:dark, new Vector3(0,angle,0));
            }
            for (int i=-5; i<=5; i++)
            {
                Shape("Zemin derzi",PrimitiveType.Cube,new Vector3(i*2,-.222f,0),new Vector3(.018f,.007f,22),dark);
                Shape("Zemin derzi",PrimitiveType.Cube,new Vector3(0,-.222f,i*2),new Vector3(22,.007f,.018f),dark);
            }
            for (int side=-1; side<=1; side+=2)
            {
                Shape("Atölye yan duvarı",PrimitiveType.Cube,new Vector3(side*11.5f,2.5f,0),new Vector3(.3f,6,24),concrete);
                for (int z=-9; z<=9; z+=9)
                    Shape("Çelik kolon",PrimitiveType.Cube,new Vector3(side*10.8f,2.6f,z),new Vector3(.32f,5.8f,.35f),dark);
                Shape("Takım dolabı",PrimitiveType.Cube,new Vector3(side*9,.65f,-6),new Vector3(1.4f,1.8f,.65f),side<0?steel:amber);
                for (int i=0; i<5; i++)Shape("Çekmece",PrimitiveType.Cube,new Vector3(side*9,.06f+i*.3f,-5.66f),new Vector3(1.2f,.025f,.03f),dark);
                for (int i=0; i<3; i++)
                    Shape("Yedek lastik",PrimitiveType.Cylinder,new Vector3(side*9,.15f+i*.3f,5),new Vector3(.82f,.14f,.82f),rubber);
                Shape("Tavan aydınlatması",PrimitiveType.Cube,new Vector3(side*3,4.5f,-.4f),new Vector3(.12f,.06f,5),white);
            }
            for(int side=-1;side<=1;side+=2)
            {
                Shape("Servis lifti",PrimitiveType.Cube,new Vector3(side*10,1.6f,-5),new Vector3(.28f,3.8f,.32f),dark);
                Shape("Lift emniyet tabanı",PrimitiveType.Cube,new Vector3(side*10,.2f,-5),new Vector3(.44f,.8f,.46f),amber);
                Shape("Tavan kirişi",PrimitiveType.Cube,new Vector3(0,4.8f,side*5),new Vector3(22,.2f,.25f),dark);
            }
            Shape("Arka duvar",PrimitiveType.Cube,new Vector3(0,2.3f,-11.5f),new Vector3(23,5.4f,.25f),concrete);
            Shape("Servis kepengi",PrimitiveType.Cube,new Vector3(0,1.8f,-11.3f),new Vector3(6,4,.15f),steel);
            for(int i=0;i<15;i++)Shape("Kepenk çıtası",PrimitiveType.Cube,new Vector3(0,-.1f+i*.27f,-11.19f),new Vector3(5.9f,.035f,.06f),dark);
            Shape("Ön duvar",PrimitiveType.Cube,new Vector3(0,2.3f,11.5f),new Vector3(23,5.4f,.25f),concrete);
            Shape("Ön servis kepengi",PrimitiveType.Cube,new Vector3(0,1.8f,11.3f),new Vector3(6,4,.15f),steel);
            for(int i=0;i<15;i++)Shape("Ön kepenk çıtası",PrimitiveType.Cube,new Vector3(0,-.1f+i*.27f,11.19f),new Vector3(5.9f,.035f,.06f),dark);
            Sign("HASAT / SERVİS 03",new Vector3(0,4.15f,-11.15f),5.5f,.5f);
            Sign("GECE VARDİYASI",new Vector3(-5,2.9f,-11.15f),3,.4f);
        }
        void Sign(string label, Vector3 p, float width, float height)
        {
            var go=new GameObject(label,typeof(TextMeshPro));go.transform.SetParent(stage.transform,false);go.transform.localPosition=p;
            go.transform.localRotation=Quaternion.Euler(0,180,0);
            var t=go.GetComponent<TextMeshPro>();t.font=MenuWidgets.Font;t.text=label;t.fontSize=4;t.color=new Color(.71f,.76f,.56f);t.alignment=TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta=new Vector2(width,height);
        }
        void LightAt(Vector3 position,Color color,float power,float range)
        {
            var go=new GameObject("Atölye lambası");go.transform.SetParent(stage.transform,false);go.transform.localPosition=position;
            var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=color;light.intensity=power;light.range=range;light.cullingMask=1<<30;
        }
        void SetLayers(){foreach(var t in stage.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;}
        public void Show(CarDefinition definition,CarLoadout loadout)
        {
            if(visual==null)return;visual.Build(definition);
            visual.Apply(loadout);SetLayers();
        }
        public void Overview()
        {focused=false;target=new Vector3(0,.65f,0);distance=8.4f;pitch=14;yaw=36;idleAfter=Time.unscaledTime+2;}
        public void Focus(string slot)
        {
            focused=true;target=new Vector3(0,.65f,0);distance=6.2f;pitch=12;
            if(slot=="rim"||slot=="tire_lettering"||slot=="Paint_Rim"||slot=="Paint_Caliper")
            {target=visual.Definition!=null?visual.Definition.geometry.wheelCenters[0]:new Vector3(-.8f,.4f,1.2f);distance=2.6f;yaw=-75;pitch=9;}
            else if(slot.Contains("head")||slot.Contains("front")||slot=="hood"||slot=="frunk")
            {target=new Vector3(0,.65f,1.3f);distance=4.6f;yaw=25;}
            else if(slot.Contains("tail")||slot.Contains("rear")||slot=="spoiler"||slot=="exhaust")
            {target=new Vector3(0,.7f,-1.4f);distance=4.8f;yaw=155;}
            else if(slot=="mirror"||slot=="skirts"||slot=="fender"){distance=5.6f;yaw=-65;}
            else if(slot.StartsWith("interior")||slot=="steering"){target=new Vector3(-.3f,.9f,0);distance=3.5f;yaw=-45;pitch=25;}
            idleAfter=Time.unscaledTime+8;
        }
        public void Zoom(bool close){if(close){distance=5;idleAfter=Time.unscaledTime+5;}else Overview();}
        public void Hold(bool value){held=value;idleAfter=Time.unscaledTime+2;}
        public void Drag(Vector2 delta){yaw-=delta.x*.28f;pitch=Mathf.Clamp(pitch+delta.y*.15f,5,42);}
        public void Scroll(float delta){distance=Mathf.Clamp(distance-delta*.45f,2.4f,8.8f);idleAfter=Time.unscaledTime+3;}
        void Update()
        {if(view==null||!stage.activeSelf)return;if(!held&&!focused&&Time.unscaledTime>idleAfter)yaw+=7*Time.unscaledDeltaTime;UpdateCamera(false);}
        void UpdateCamera(bool instant)
        {
            float k=instant?1:1-Mathf.Exp(-8*Time.unscaledDeltaTime);currentTarget=Vector3.Lerp(currentTarget,target,k);currentDistance=Mathf.Lerp(currentDistance,distance,k);
            var direction=Quaternion.Euler(-pitch,yaw,0)*Vector3.forward;
            view.transform.localPosition=currentTarget+direction*currentDistance;view.transform.LookAt(stage.transform.TransformPoint(currentTarget));
            if(image.rectTransform.rect.height>1)view.aspect=image.rectTransform.rect.width/image.rectTransform.rect.height;
        }
        public void Visible(bool value){if(stage!=null)stage.SetActive(value);if(image!=null)image.gameObject.SetActive(value);if(!value)held=false;}
        void OnDestroy()
        {
            if(stage!=null)Destroy(stage);if(texture!=null){texture.Release();Destroy(texture);}foreach(var m in new[]{steel,concrete,dark,amber,white,rubber})if(m!=null)Destroy(m);
        }
    }
}
