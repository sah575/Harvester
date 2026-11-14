using System.Collections.Generic;
using System.Globalization;
using Harvester.Race;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Harvester.UI.Yaris
{
    // Sahne dosyasına dokunmadan her yarışta bir kez kurulur; sahne kapanırken kendiliğinden temizlenir.
    public sealed class YarisArayuzu : MonoBehaviour
    {
        readonly List<TMP_Text> rows = new List<TMP_Text>();
        readonly List<TMP_Text> ticks = new List<TMP_Text>();
        YarisIsigi[] lights;
        GameObject lightPanel, content;
        RectTransform gaugeRoot, table;
        TMP_Text summary, speed, gear, revs, harvester, warning;
        YarisKadrani gauge;
        float maximum = -1, nextTable;
        static readonly Color Danger = new Color(.94f,.22f,.13f);
        static readonly CultureInfo Format = CultureInfo.InvariantCulture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            CreateForScene();
        }
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { CreateForScene(); }
        static void CreateForScene()
        {
            if (Object.FindFirstObjectByType<RaceManager>() == null || Object.FindFirstObjectByType<YarisArayuzu>() != null) return;
            new GameObject("Yarış arayüzü", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(YarisArayuzu));
        }
        void Awake()
        {
            gameObject.AddComponent<PistKarariBildirimi>();
            gameObject.AddComponent<YarisSonuclari>();
            gameObject.AddComponent<YarisIzleyici>();
            gameObject.AddComponent<YarisMiniHarita>();
            var canvas = GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 40;
            var scaler = GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
            content = new GameObject("Yarış bilgileri", typeof(RectTransform)); content.transform.SetParent(transform,false);
            MenuWidgets.Fill((RectTransform)content.transform,Vector2.zero,Vector2.one);
            summary = Label(content.transform,"",new Vector2(.035f,.84f),new Vector2(.34f,.96f),30);
            var lamp = MenuWidgets.Box(content.transform,"Başlangıç ışıkları",new Color(.015f,.02f,.017f,.92f));
            MenuWidgets.Fill(lamp,new Vector2(.35f,.92f),new Vector2(.65f,.995f)); lightPanel = lamp.gameObject;
            lights = new YarisIsigi[RaceManager.StartLightCount];
            for (int i=0;i<lights.Length;i++)
            {
                var light=new GameObject("Kırmızı ışık "+(i+1),typeof(RectTransform),typeof(CanvasRenderer),typeof(YarisIsigi));
                light.transform.SetParent(lamp,false);var l=(RectTransform)light.transform;
                MenuWidgets.Fill(l,new Vector2(.025f+i*.195f,.15f),new Vector2(.17f+i*.195f,.85f)); lights[i]=l.GetComponent<YarisIsigi>();lights[i].raycastTarget=false;
            }
            gaugeRoot=MenuWidgets.Box(content.transform,"Hız ve devir",new Color(.015f,.025f,.019f,.86f));
            gaugeRoot.anchorMin=gaugeRoot.anchorMax=Vector2.zero; gaugeRoot.pivot=Vector2.zero;
            gaugeRoot.anchoredPosition=new Vector2(45,35); gaugeRoot.sizeDelta=new Vector2(370,335);
            var dial=new GameObject("Kadran",typeof(RectTransform),typeof(CanvasRenderer),typeof(YarisKadrani));
            dial.transform.SetParent(gaugeRoot,false); gauge=dial.GetComponent<YarisKadrani>(); gauge.raycastTarget=false;
            var rect=(RectTransform)dial.transform; rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(320,320);
            speed=Label(gaugeRoot,"0",new Vector2(.23f,.30f),new Vector2(.77f,.48f),47);speed.alignment=TextAlignmentOptions.Center;
            var unit=Label(gaugeRoot,"km/sa",new Vector2(.3f,.23f),new Vector2(.7f,.31f),18);unit.alignment=TextAlignmentOptions.Center;
            gear=Label(gaugeRoot,"N",new Vector2(.38f,.50f),new Vector2(.62f,.65f),35);gear.alignment=TextAlignmentOptions.Center;
            revs=Label(gaugeRoot,"",new Vector2(.22f,.10f),new Vector2(.78f,.20f),19);revs.alignment=TextAlignmentOptions.Center;
            table=MenuWidgets.Box(content.transform,"Sıralama",new Color(.015f,.025f,.019f,.86f));
            MenuWidgets.Fill(table,new Vector2(.69f,.25f),new Vector2(.97f,.69f));
            Label(table,"SIRA / YARIŞÇI       ÖN / ARKA",new Vector2(.04f,.88f),new Vector2(.96f,.98f),21);
            for(int i=0;i<lobby.MaxPlayers;i++)
                rows.Add(Label(table,"",new Vector2(.04f,.73f-i*.16f),new Vector2(.96f,.87f-i*.16f),22));
            harvester=Label(table,"HARVESTER — — sn",new Vector2(.04f,.04f),new Vector2(.96f,.15f),23);
            warning=Label(content.transform,"HARVESTER YAKIN!",new Vector2(.35f,.12f),new Vector2(.68f,.19f),33);
            warning.alignment=TextAlignmentOptions.Center;
            foreach(var image in GetComponentsInChildren<Image>()) image.raycastTarget=false;
        }
        static TMP_Text Label(Transform parent,string value,Vector2 min,Vector2 max,float size)
        {
            var t=MenuWidgets.Text(parent,value,size);MenuWidgets.Fill((RectTransform)t.transform,min,max);t.overflowMode=TextOverflowModes.Overflow;return t;
        }
        void Update()
        {
            var manager=RaceManager.Instance;
            bool visible=manager!=null&&(manager.Phase==RacePhase.Countdown||manager.Phase==RacePhase.Racing);
            content.SetActive(visible);if(!visible)return;
            lightPanel.SetActive(manager.Phase==RacePhase.Countdown&&!manager.StartLightsOut);
            for(int i=0;i<lights.Length;i++)lights[i].color=i<manager.StartLightsLit?Danger:new Color(.12f,.035f,.025f);
            var car=RaceCar.Local;
            gaugeRoot.gameObject.SetActive(car!=null);
            if(car!=null)
            {
                float max=Mathf.Max(10,car.TopSpeed*3.6f);
                if(!Mathf.Approximately(max,maximum))BuildTicks(max);
                gauge.SetValues(car.SpeedKmh,car.DisplayRpm,max);
                speed.text=Mathf.RoundToInt(car.SpeedKmh).ToString(Format);
                int g=car.DisplayGear;gear.text=g<0?"R":g==0?"N":g.ToString(Format);
                revs.text=Mathf.RoundToInt(car.DisplayRpm).ToString(Format)+" dev/dk";
                revs.color=car.DisplayRpm>=6500?Danger:MenuWidgets.Ink;
            }
            if(Time.unscaledTime<nextTable)return; nextTable=Time.unscaledTime+.1f;
            ulong id=NetworkManager.Singleton!=null?NetworkManager.Singleton.LocalClientId:0;
            RacerState local;bool hasLocal=manager.TryGetState(id,out local);
            summary.text=(hasLocal?local.Position.ToString():"—")+" / "+manager.Standings.Count+"   SIRA\n"+
                (manager.LoopTrack?"TUR "+(hasLocal?local.Lap:1)+" / "+manager.Laps:
                "KALAN / "+(hasLocal&&RaceTrack.Current!=null?
                Mathf.CeilToInt(Mathf.Max(0,RaceTrack.Current.FinishDistance-local.Progress)).ToString(Format)+" m":"—"))+"\n"+TimeText(manager.RaceTime);
            DrawStandings(manager,id);
            float meters=manager.HarvesterGapMeters(id),seconds=manager.HarvesterGapSeconds(id);
            harvester.text="HARVESTER — "+(float.IsNaN(seconds)?"—":seconds.ToString("0.0",Format))+" sn";
            bool near=!float.IsNaN(meters)&&meters<30;
            harvester.color=near?Danger:MenuWidgets.Ink;
            warning.gameObject.SetActive(near);warning.color=new Color(Danger.r,Danger.g,Danger.b,.35f+.65f*Mathf.PingPong(Time.unscaledTime*3,1));
        }
        void BuildTicks(float max)
        {
            maximum=max;foreach(var t in ticks){t.gameObject.SetActive(false);Destroy(t.gameObject);}ticks.Clear();
            int count=Mathf.FloorToInt(max/10);
            for(int i=0;i<=count;i++)AddTick(i*10,max);
            if(!Mathf.Approximately(count*10,max))AddTick(max,max);
        }
        void AddTick(float value,float max)
        {
            var t=MenuWidgets.Text(gaugeRoot,Mathf.RoundToInt(value).ToString(Format),13);t.alignment=TextAlignmentOptions.Center;
            var r=(RectTransform)t.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);
            r.sizeDelta=new Vector2(30,20);r.anchoredPosition=YarisKadrani.Point(value/max,151);ticks.Add(t);
        }
        void DrawStandings(RaceManager manager,ulong local)
        {
            while(rows.Count<manager.Standings.Count)
            {
                int i=rows.Count;var t=Label(table,"",new Vector2(.04f,.73f-i*.16f),new Vector2(.96f,.87f-i*.16f),22);rows.Add(t);
            }
            for(int i=0;i<rows.Count;i++)
            {
                if(i>=manager.Standings.Count){rows[i].text="";continue;}
                var state=manager.Standings[i];bool eliminated=state.Status==RacerStatus.Eliminated||state.Status==RacerStatus.Left;
                string name=state.Name.ToString().Replace("<","‹");
                rows[i].text=(state.ClientId==local?"• ":"")+state.Position+". "+name+"\n"+
                    (eliminated?(state.Status==RacerStatus.Left?"AYRILDI":"ELENDİ"):
                    Gap(manager.GapToAhead(state.ClientId),"+")+" / "+Gap(manager.GapToBehind(state.ClientId),"-"));
                rows[i].color=eliminated?new Color(.46f,.49f,.44f):state.ClientId==local?MenuWidgets.Accent:MenuWidgets.Ink;
            }
        }
        static string Gap(float value,string sign){return float.IsNaN(value)?"—":sign+Mathf.Abs(value).ToString("0.0",Format)+" sn";}
        static string TimeText(float seconds){int min=Mathf.FloorToInt(seconds/60);return min.ToString("00")+":"+(seconds-min*60).ToString("00.0",Format);}
    }
}
