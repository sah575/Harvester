using System.Collections.Generic;
using Harvester.Race;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Harvester.UI
{
    public sealed class LobbyMenus : MonoBehaviour
    {
        public GameObject home, connection, room, garage, shop, settings;
        public RectTransform garageContent, shopContent, settingsContent, roomCarContent;
        public TMP_Text message, balance;
        public TMP_FontAsset font;
        public RawImage previewImage;
        public CarCatalog catalog;
        public MenuCarRegistry registry;
        public List<CarDefinition> definitions = new List<CarDefinition>();
        public bool fallbackCatalog;
        GaragePreview preview;
        CarLoadout draft;
        int selected;
        string section="Araçlar";
        bool close;
        int selectedSlot, selectedPaint, selectedUpgrade;
        bool Started;
        bool ownsCatalog;
        bool Connected => NetworkManager.Singleton!=null && NetworkManager.Singleton.IsListening;
        CarDefinition Car => catalog!=null?catalog.Get(selected):null;
        void Start()
        {
            MenuWidgets.Font=font;
            if(registry!=null){catalog=registry.catalog;definitions=registry.definitions;fallbackCatalog=catalog==null;}
            if(catalog==null){ownsCatalog=true;catalog=ScriptableObject.CreateInstance<CarCatalog>();catalog.cars.AddRange(definitions);}
            draft=CarLoadoutStore.Load(catalog); selected=draft.definition;
            preview=gameObject.AddComponent<GaragePreview>(); preview.Initialize(previewImage);
            BindHome(); MenuSettings.Build(settingsContent);
            Started=true;
            if(room.activeSelf) ShowRoom(); else ShowMain(message.text);
        }
        void BindHome()
        {
            Bind(home,"OYNA",()=>Open(connection)); Bind(home,"GARAJ",OpenGarage);
            Bind(home,"MAĞAZA",OpenShop); Bind(home,"AYARLAR",()=>Open(settings)); Bind(home,"ÇIKIŞ",()=>Application.Quit());
            Bind(connection,"ANA MENÜ",()=>ShowMain("")); Bind(garage,"GERİ",()=>ShowMain(""));
            Bind(garage,"KAYDET",Save);
            Bind(shop,"GERİ",()=>ShowMain("")); Bind(settings,"GERİ",()=>{if(Connected)ShowRoom();else ShowMain("");});
            Bind(room,"AYARLAR",()=>Open(settings));
        }
        static void Bind(GameObject p,string name,UnityEngine.Events.UnityAction action)
        { foreach(var b in p.GetComponentsInChildren<Button>(true))if(b.name==name)b.onClick.AddListener(action); }
        void Open(GameObject panel)
        {
            if((panel==garage||panel==shop||panel==home||panel==connection)&&Connected)return;
            foreach(var p in new[]{home,connection,room,garage,shop,settings})p.SetActive(p==panel);
            if(preview!=null)preview.Visible(panel==garage||panel==shop||panel==home);
            if(balance!=null)balance.text=HurdaEkonomisi.Bakiye.ToString("N0")+" HURDA";
        }
        public void ShowMain(string status)
        {
            foreach(var p in new[]{home,connection,room,garage,shop,settings})if(p!=null)p.SetActive(p==home);
            if(message!=null)message.text=status;
            if(Started){draft=CarLoadoutStore.Load(catalog);selected=draft.definition;preview.Visible(true);RefreshPreview();balance.text=HurdaEkonomisi.Bakiye.ToString("N0")+" HURDA";}
        }
        public void ShowRoom()
        {
            foreach(var p in new[]{home,connection,room,garage,shop,settings})if(p!=null)p.SetActive(p==room);
            if(!Started)return;
            preview.Visible(false); MenuWidgets.Clear(roomCarContent);
            var saved=CarLoadoutStore.Load(catalog); var def=catalog.Get(saved.definition);
            MenuWidgets.Text(roomCarContent,"ARACIM / "+(def!=null?def.displayName:"Araç tanımı yok"),22,38);
            MenuWidgets.Text(roomCarContent,"Seçim kayıtlıdır. Satın alma ve modifiye için odadan ayrıl.",18,30);
        }
        public void OpenGarage(){if(Connected)return; draft=CarLoadoutStore.Load(catalog);selected=draft.definition;section="Araçlar";Open(garage);DrawGarage();}
        public void OpenShop(){if(Connected)return;Open(shop);DrawShop();}
        void RefreshPreview(){if(Car!=null)preview.Show(Car,draft);}
        void Notice(string text){message.text=text;balance.text=HurdaEkonomisi.Bakiye.ToString("N0")+" HURDA";}
        void DrawGarage()
        {
            MenuWidgets.Clear(garageContent);
            if(fallbackCatalog)MenuWidgets.Text(garageContent,"Katalog bulunamadı; araç tanımları listeleniyor.",18,32);
            var tabs=MenuWidgets.Box(garageContent,"Sekmeler",Color.clear);MenuWidgets.Height(tabs,48);
            var tabLayout=tabs.gameObject.AddComponent<HorizontalLayoutGroup>();tabLayout.spacing=6;tabLayout.childForceExpandWidth=true;tabLayout.childControlWidth=true;
            foreach(string tab in new[]{"Araçlar","Parçalar","Boya","Performans"})
            {string label=tab;MenuWidgets.Button(tabs,(section==tab?"• ":"")+tab,()=>{section=label;DrawGarage();});}
            if(Car==null){MenuWidgets.Text(garageContent,"Henüz araç tanımı bulunamadı.");return;}
            MenuWidgets.Text(garageContent,Car.displayName,30,45);
            MenuWidgets.Button(garageContent,close?"UZAKLAŞ":"YAKINLAŞ",()=>{close=!close;preview.Zoom(close);});
            if(section=="Araçlar")DrawCars(garageContent,false);
            if(section=="Parçalar")DrawParts();
            if(section=="Boya")DrawPaint();
            if(section=="Performans")DrawUpgrades();
            Stats(garageContent,draft,draft);
            MenuWidgets.Button(garageContent,"KAYITLI SEÇİME DÖN",()=>{draft=CarLoadoutStore.Load(catalog);selected=draft.definition;DrawGarage();Notice("Kayıtlı seçim yüklendi.");});
            RefreshPreview();
        }
        void DrawCars(Transform parent,bool store)
        {
            for(int i=0;i<catalog.Count;i++)
            {
                int index=i;var def=catalog.Get(i);if(def==null)continue;
                bool owned=HurdaEkonomisi.Sahip("araba/"+def.id);
                if(!store&&!owned)continue;
                MenuWidgets.Button(parent,def.displayName+" / "+(owned?"SAHİP OLUNAN":def.price+" Hurda"),()=>
                {
                    if(Connected)return;
                    if(!owned){if(!HurdaEkonomisi.SatinAl("araba/"+def.id,def.price)){Notice("Yeterli Hurda yok.");return;}Notice("Araç satın alındı.");DrawShop();return;}
                    selected=index;
                    var saved=CarLoadoutStore.Load(catalog);draft=saved.definition==index?saved:catalog.DefaultLoadout(index);
                    if(store){RefreshPreview();Notice(def.displayName+" önizleniyor. Seçmek için garajda kaydet.");}else DrawGarage();
                });
            }
        }
        string PartKey(int slot,int variant)=>"parca/"+Car.id+"/"+Car.slots[slot].id+"/"+Car.slots[slot].variants[variant].key;
        string UpgradeKey(CarUpgradeType type,int level)=>"gelistirme/"+Car.id+"/"+type+"/"+level;
        void DrawParts()
        {
            if(Car.slots.Count==0){MenuWidgets.Text(garageContent,"Bu araçta parça yuvası yok.");return;}
            selectedSlot=Mathf.Clamp(selectedSlot,0,Mathf.Min(Car.slots.Count,CarLoadout.MaxSlots)-1);
            MenuWidgets.Options(garageContent,Car.slots.ConvertAll(s=>s.displayName),selectedSlot,i=>{selectedSlot=i;DrawGarage();});
            for(int s=selectedSlot;s==selectedSlot&&s<Car.slots.Count&&s<CarLoadout.MaxSlots;s++)
            {
                int slot=s;MenuWidgets.Text(garageContent,Car.slots[s].displayName,24,36);
                for(int v=0;v<Car.slots[s].variants.Count;v++)
                {
                    int variant=v;var part=Car.slots[s].variants[v];bool owned=v==0||HurdaEkonomisi.Sahip(PartKey(s,v));
                    MenuWidgets.Button(garageContent,(draft.GetSlot(s)==v?"[TAKILI] ":"")+part.displayName+" / "+(owned?"SEÇ":part.price+" Hurda"),()=>
                    {
                        if(Connected)return;
                        if(variant>0&&!HurdaEkonomisi.SatinAl(PartKey(slot,variant),part.price)){Notice("Yeterli Hurda yok.");return;}
                        draft.SetSlot(slot,variant);Notice("Parça seçildi. Yarışta kullanmak için kaydet.");DrawGarage();
                    });
                }
            }
        }
        void DrawPaint()
        {
            if(Car.paints.Count==0){MenuWidgets.Text(garageContent,"Bu araçta boya yuvası yok.");return;}
            selectedPaint=Mathf.Clamp(selectedPaint,0,Mathf.Min(Car.paints.Count,CarLoadout.MaxPaints)-1);
            MenuWidgets.Options(garageContent,Car.paints.ConvertAll(p=>p.displayName),selectedPaint,i=>{selectedPaint=i;DrawGarage();});
            for(int p=selectedPaint;p==selectedPaint&&p<Car.paints.Count&&p<CarLoadout.MaxPaints;p++)
            {
                int paint=p;MenuWidgets.Text(garageContent,Car.paints[p].displayName,24,36);
                var row=MenuWidgets.Box(garageContent,"Palet",Color.clear);MenuWidgets.Height(row,42);
                var layout=row.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.childForceExpandWidth=true;
                foreach(string hex in new[]{"D6D0B8","282C2C","A64028","D29233","537C72","455F86"})
                {
                    Color color;ColorUtility.TryParseHtmlString("#"+hex,out color);var chosen=color;
                    var button=MenuWidgets.Button(row," ",()=>ApplyPaint(paint,chosen));button.GetComponent<Image>().color=color;
                }
                var input=MenuWidgets.Input(garageContent,ColorUtility.ToHtmlStringRGBA(draft.GetPaint(p)),"RRGGBB veya RRGGBBAA");input.characterLimit=9;
                MenuWidgets.Button(garageContent,"RENGİ UYGULA",()=>{Color color;if(ColorUtility.TryParseHtmlString("#"+input.text.Trim().TrimStart('#'),out color))ApplyPaint(paint,color);else Notice("Geçerli bir renk gir: ör. C05030.");});
            }
        }
        void ApplyPaint(int paint,Color color)
        {if(Connected)return;draft.SetPaint(paint,color);RefreshPreview();Notice("Renk uygulandı. Kalıcı olması için kaydet.");}
        void DrawUpgrades()
        {
            if(Car.upgrades.Count==0){MenuWidgets.Text(garageContent,"Bu araçta geliştirme yok.");return;}
            selectedUpgrade=Mathf.Clamp(selectedUpgrade,0,Car.upgrades.Count-1);
            MenuWidgets.Options(garageContent,Car.upgrades.ConvertAll(c=>c.displayName),selectedUpgrade,i=>{selectedUpgrade=i;DrawGarage();});
            var category=Car.upgrades[selectedUpgrade];
            {
                var cat=category;int current=draft.GetUpgrade(cat.type);
                MenuWidgets.Text(garageContent,cat.displayName+" / Seviye "+current,24,36);
                MenuWidgets.Button(garageContent,"STOK SEVİYE",()=>{if(Connected)return;draft.SetUpgrade(cat.type,0);DrawGarage();});
                for(int l=1;l<=cat.levels.Length;l++)
                {
                    int level=l;var data=cat.levels[l-1];bool owned=HurdaEkonomisi.Sahip(UpgradeKey(cat.type,l));
                    var next=draft;next.SetUpgrade(cat.type,l);
                    Stats(garageContent,draft,next);
                    bool previous=l==1||HurdaEkonomisi.Sahip(UpgradeKey(cat.type,l-1));
                    var b=MenuWidgets.Button(garageContent,data.displayName+" / "+(owned?"SEÇ":data.price+" Hurda"),()=>
                    {
                        if(Connected)return;
                        if(!HurdaEkonomisi.SatinAl(UpgradeKey(cat.type,level),data.price)){Notice("Yeterli Hurda yok.");return;}
                        draft.SetUpgrade(cat.type,level);Notice("Geliştirme seçildi. Kalıcı olması için kaydet.");DrawGarage();
                    });b.interactable=owned||previous;
                }
            }
        }
        void Stats(Transform parent,CarLoadout before,CarLoadout after)
        {
            var a=Car.ComputeStats(before);var b=Car.ComputeStats(after);
            Stat(parent,"Son hız (km/sa)",a.topSpeed*3.6f,b.topSpeed*3.6f,300);
            Stat(parent,"İvme (m/sn²)",a.maxAcceleration,b.maxAcceleration,30);
            Stat(parent,"Yol tutuş (sürtünme)",a.tireFriction,b.tireFriction,3);
            Stat(parent,"Fren (m/sn²)",a.brakeDeceleration,b.brakeDeceleration,50);
            MenuWidgets.Text(parent,"Dayanıklılık / araç verisinde henüz tanımlı değil",17,28);
        }
        static void Stat(Transform parent,string label,float a,float b,float max)
        {
            MenuWidgets.Text(parent,label+"   "+a.ToString("0.0")+" → "+b.ToString("0.0"),18,28);
            var bar=MenuWidgets.Box(parent,"Karşılaştırma",new Color(.12f,.15f,.13f));MenuWidgets.Height(bar,7);
            var next=MenuWidgets.Box(bar,"Sonra",MenuWidgets.Accent);MenuWidgets.Fill(next,Vector2.zero,new Vector2(Mathf.Clamp01(b/max),1));
            var old=MenuWidgets.Box(bar,"Önce",MenuWidgets.Ink);MenuWidgets.Fill(old,Vector2.zero,new Vector2(Mathf.Clamp01(a/max),.4f));
        }
        void Save()
        {
            if(Connected||Car==null||!HurdaEkonomisi.Sahip("araba/"+Car.id))return;
            draft=Car.Sanitize(draft);CarLoadoutStore.Save(catalog,draft);Notice("Araç seçimi kaydedildi.");
        }
        
        void OnDestroy() { if(ownsCatalog&&catalog!=null)Destroy(catalog); }
        void DrawShop()
        {MenuWidgets.Clear(shopContent);MenuWidgets.Text(shopContent,"ARAÇ TEDARİKİ",30,48);MenuWidgets.Text(shopContent,"Parçalar ve geliştirmeler garajdan satın alınır.",20,40);DrawCars(shopContent,true);RefreshPreview();}
    }
}
