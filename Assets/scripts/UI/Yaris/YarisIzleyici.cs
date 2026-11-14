using Harvester.Race;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Harvester.UI.Yaris
{
    public sealed class YarisIzleyici : MonoBehaviour
    {
        GameObject panel;
        TMP_Text target;
        Button previous,next,harvester;
        void Start()
        {
            if(GetComponent<GraphicRaycaster>()==null)gameObject.AddComponent<GraphicRaycaster>();
            if(EventSystem.current==null)new GameObject("Yarış arayüzü girdisi",typeof(EventSystem),typeof(StandaloneInputModule));
            var box=MenuWidgets.Column(transform,"Elenen oyuncu izleyici paneli");panel=box.gameObject;
            MenuWidgets.Fill(box,new Vector2(.34f,.04f),new Vector2(.66f,.32f));
            MenuWidgets.Text(box,"ELENDİN",38,52).color=new Color(.94f,.22f,.13f);
            target=MenuWidgets.Text(box,"",24,42);target.richText=false;
            var controls=MenuWidgets.Box(box,"İzleyici düğmeleri",Color.clear);MenuWidgets.Height(controls,48);
            var layout=controls.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.childForceExpandWidth=true;
            previous=MenuWidgets.Button(controls,"←",()=>{var s=RaceSpectatorCamera.Active;if(s!=null)s.Previous();});
            harvester=MenuWidgets.Button(controls,"HARVESTER",()=>{var s=RaceSpectatorCamera.Active;if(s!=null)s.WatchHarvester();});
            next=MenuWidgets.Button(controls,"→",()=>{var s=RaceSpectatorCamera.Active;if(s!=null)s.Next();});
            MenuWidgets.Text(box,"← → / A D: hedef değiştir · H: Harvester\nSağ fare: kamerayı döndür",18,52);
            panel.SetActive(false);
        }
        void Update()
        {
            var m=RaceManager.Instance;RacerState state;
            ulong id=NetworkManager.Singleton!=null?NetworkManager.Singleton.LocalClientId:0;
            bool eliminated=m!=null&&m.TryGetState(id,out state)&&state.Status==RacerStatus.Eliminated;
            bool show=eliminated&&m.Phase==RacePhase.Racing;
            panel.SetActive(show);if(!show)return;
            var spectator=RaceSpectatorCamera.Active;
            target.text=spectator==null?"İzleyici kamerası hazırlanıyor…":string.IsNullOrEmpty(spectator.TargetName)?"İzlenecek hedef bekleniyor…":"İZLENİYOR / "+spectator.TargetName;
            previous.interactable=next.interactable=harvester.interactable=spectator!=null;
            harvester.GetComponent<Image>().color=spectator!=null&&spectator.WatchingHarvester?new Color(.27f,.34f,.20f):new Color(.14f,.18f,.16f);
        }
    }
}
