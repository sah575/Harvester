using Harvester.Race;
using UnityEngine;
using UnityEngine.UI;

namespace Harvester.UI.Yaris
{
    [DefaultExecutionOrder(1200)]
    public sealed class YarisMiniHarita : MonoBehaviour
    {
        const float Radius=132,Range=150,Scale=Radius/Range;
        GameObject panel;
        RectTransform map;
        YarisPistCizgisi line;
        RaceTrack builtTrack;
        Image[] cars;
        Image harvester;
        Sprite disk,triangle;
        Texture2D diskTexture,triangleTexture;
        static readonly Color Self=new Color(.34f,.95f,.35f);
        static readonly Color Other=new Color(1,.84f,.20f);
        static readonly Color Danger=new Color(1,.18f,.12f);

        void Start()
        {
            disk=Shape(false,out diskTexture);triangle=Shape(true,out triangleTexture);
            var border=MenuWidgets.Box(transform,"Mini harita çerçevesi",new Color(.34f,.42f,.25f,.95f));
            border.GetComponent<Image>().sprite=disk;
            border.anchorMin=border.anchorMax=Vector2.one;border.pivot=new Vector2(.5f,.5f);
            border.anchoredPosition=new Vector2(-190,-175);border.sizeDelta=new Vector2(280,280);panel=border.gameObject;
            var clip=MenuWidgets.Box(border,"150 metre radar",new Color(.015f,.025f,.018f,.94f));
            clip.anchorMin=clip.anchorMax=new Vector2(.5f,.5f);clip.sizeDelta=Vector2.one*Radius*2;
            clip.GetComponent<Image>().sprite=disk;clip.gameObject.AddComponent<Mask>().showMaskGraphic=true;
            var path=new GameObject("Önbellekli pist çizgisi",typeof(RectTransform),typeof(CanvasRenderer),typeof(YarisPistCizgisi));
            path.transform.SetParent(clip,false);line=path.GetComponent<YarisPistCizgisi>();map=(RectTransform)path.transform;
            map.anchorMin=map.anchorMax=map.pivot=new Vector2(.5f,.5f);
            cars=new Image[Mathf.Max(16,lobby.MaxPlayers)];
            for(int i=0;i<cars.Length;i++)cars[i]=Marker(clip,"Araç işareti",Other);
            harvester=Marker(clip,"Harvester işareti",Danger);
            var caption=MenuWidgets.Text(border,"150 m",16);
            MenuWidgets.Fill((RectTransform)caption.transform,new Vector2(.32f,-.09f),new Vector2(.68f,.02f));
            caption.alignment=TMPro.TextAlignmentOptions.Center;
            foreach(var image in border.GetComponentsInChildren<Image>())image.raycastTarget=false;
        }
        Image Marker(Transform parent,string name,Color color)
        {
            var rect=MenuWidgets.Box(parent,name,color);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
            rect.sizeDelta=new Vector2(15,19);var image=rect.GetComponent<Image>();image.sprite=triangle;image.raycastTarget=false;
            image.gameObject.SetActive(false);return image;
        }
        // Şekiller yalnız kurulumda üretilir; kamera, RenderTexture veya ek render geçişi yoktur.
        static Sprite Shape(bool arrow,out Texture2D texture)
        {
            const int size=64;
            texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            texture.name=arrow?"Radar üçgeni":"Radar dairesi";texture.wrapMode=TextureWrapMode.Clamp;
            texture.filterMode=FilterMode.Bilinear;
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float px=(x+.5f)/size*2-1,py=(y+.5f)/size*2-1;
                float alpha=arrow?Mathf.Clamp01(((1-py)*.5f-Mathf.Abs(px))*size):Mathf.Clamp01((1-Mathf.Sqrt(px*px+py*py))*size);
                pixels[y*size+x]=new Color32(255,255,255,(byte)(255*alpha));
            }
            texture.SetPixels32(pixels);texture.Apply(false,true);
            return Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),size);
        }
        public static Vector2 RadarPoint(Vector3 delta,float heading)
        {
            float radians=heading*Mathf.Deg2Rad,c=Mathf.Cos(radians),s=Mathf.Sin(radians);
            return Vector2.ClampMagnitude(new Vector2(c*delta.x-s*delta.z,s*delta.x+c*delta.z)*Scale,Radius-12);
        }
        static void Place(Image marker,Transform target,Transform center,float heading)
        {
            bool show=target!=null;marker.gameObject.SetActive(show);if(!show)return;
            marker.rectTransform.anchoredPosition=RadarPoint(target.position-center.position,heading);
            marker.rectTransform.localRotation=Quaternion.Euler(0,0,heading-target.eulerAngles.y);
        }
        void LateUpdate()
        {
            if(panel==null)return;
            var manager=RaceManager.Instance;var track=RaceTrack.Current;
            var local=RaceCar.Local;
            var spectator=RaceSpectatorCamera.Active;
            Transform center=spectator!=null?spectator.Target:local!=null&&!local.IsEliminated?local.transform:null;
            bool visible=MenuSettings.MiniMapEnabled&&manager!=null&&track!=null&&center!=null&&
                (manager.Phase==RacePhase.Countdown||manager.Phase==RacePhase.Racing);
            panel.SetActive(visible);if(!visible)return;
            if(builtTrack!=track){line.Build(track,Scale);builtTrack=track;}
            float heading=center.eulerAngles.y;
            map.localRotation=Quaternion.Euler(0,0,heading);
            var offset=(new Vector2(center.position.x,center.position.z)-line.Origin)*Scale;
            map.anchoredPosition=-(Vector2)(map.localRotation*new Vector3(offset.x,offset.y,0));
            int used=0;
            for(int i=0;i<RaceCar.All.Count&&used<cars.Length;i++)
            {
                var car=RaceCar.All[i];if(car==null||car.IsEliminated)continue;
                var marker=cars[used++];marker.color=car==local?Self:Other;
                Place(marker,car.transform,center,heading);
            }
            for(int i=used;i<cars.Length;i++)cars[i].gameObject.SetActive(false);
            var h=HarvesterAI.Instance;
            Transform harvesterTransform=h!=null?h.transform:manager.HarvesterTransform;
            Place(harvester,harvesterTransform,center,heading);
        }
        void OnDestroy()
        {
            if(disk!=null)Destroy(disk);if(triangle!=null)Destroy(triangle);
            if(diskTexture!=null)Destroy(diskTexture);if(triangleTexture!=null)Destroy(triangleTexture);
        }
    }
}
