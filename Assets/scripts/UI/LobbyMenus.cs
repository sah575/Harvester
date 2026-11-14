using System.Collections.Generic;
using Harvester.Race;
using Harvester.UI.Yaris;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Harvester.UI
{
    public sealed class LobbyMenus : MonoBehaviour
    {
        public GameObject home,connection,room,garage,shop,settings;
        public RectTransform garageContent,garageModules,garageStats,shopContent,shopStats,settingsContent,roomCarContent;
        public TMP_Text message,balance,garageCarName,shopCarName,shopCounter;
        public TMP_FontAsset font;
        public RawImage previewImage;
        public CarCatalog catalog;
        public MenuCarRegistry registry;
        public List<CarDefinition> definitions=new List<CarDefinition>();
        public bool fallbackCatalog;
        GaragePreview preview;
        CarLoadout draft,candidate;
        int selected,selectedSlot,selectedPaint,selectedUpgrade,variant,level;
        string section="GÖRÜNÜM",lastSection="";
        bool started,ownsCatalog;
        TMP_Text roomPlayers,roomInfo;
        Button readyButton;
        readonly List<Button> trackButtons=new List<Button>();
        TMP_Text trackInfo;
        bool choosingTrack;
        GameObject connectionOverlay;
        TMP_Text connectionTitle,connectionNotice;
        Button connectionAction;
        bool connecting;
        lobby network;
        float nextRoomUpdate;
        CarDefinition Car=>catalog!=null?catalog.Get(selected):null;
        void Start()
        {
            MenuWidgets.Font=font;
            if(registry!=null){catalog=registry.catalog;definitions=registry.definitions;fallbackCatalog=catalog==null;}
            if(catalog==null){ownsCatalog=true;catalog=ScriptableObject.CreateInstance<CarCatalog>();catalog.cars.AddRange(definitions);}
            draft=CarLoadoutStore.Load(catalog);selected=draft.definition;candidate=draft;
            if(CarLoadoutStore.HasSave)GarageLoadouts.Remember(catalog,draft);
            preview=gameObject.AddComponent<GaragePreview>();preview.Initialize(previewImage);
            Bind(home,"TEK OYUNCU",()=>lobby.Instance.StartSinglePlayer());Bind(home,"ÇOK OYUNCU",()=>Open(connection));
            Bind(home,"AYARLAR",()=>Open(settings));Bind(home,"ÇIKIŞ",()=>Application.Quit());
            Bind(connection,"ANA MENÜ",()=>ShowMain(""));Bind(garage,"GERİ",ReturnToSession);Bind(garage,"KAYDET",Save);
            Bind(shop,"GERİ",ReturnToSession);Bind(shop,"SATIN AL",BuyCar);
            Bind(garage,"ÖNCEKİ ARAÇ",()=>Browse(-1));Bind(garage,"SONRAKİ ARAÇ",()=>Browse(1));
            Bind(shop,"ÖNCEKİ ARAÇ",()=>Browse(-1));Bind(shop,"SONRAKİ ARAÇ",()=>Browse(1));
            foreach(string tab in new[]{"GÖRÜNÜM","BOYA","PERFORMANS","ARACIM"})
            {string choice=tab;Bind(garage,tab,()=>{section=choice;candidate=draft;DrawGarage();if(choice=="ARACIM")preview.Overview();else FocusModule();});}
            Bind(garage,"TÜM ARACI GÖR",()=>preview.Overview());
            Bind(settings,"GERİ",()=>{ReturnToSession();});Bind(room,"AYARLAR",()=>Open(settings));
            MenuSettings.Build(settingsContent);started=true;
            network=lobby.Instance;
            if(network!=null){network.RoomChanged+=RoomChanged;network.MenuReturned+=MenuReturned;}
            BuildConnectionOverlay();
            if(lobby.Instance!=null&&lobby.Instance.InRoom)ShowRoom();else ShowMain(message.text);
        }
        static void Bind(GameObject panel,string name,UnityEngine.Events.UnityAction action)
        {foreach(var b in panel.GetComponentsInChildren<Button>(true))if(b.name==name)b.onClick.AddListener(action);}
        void Open(GameObject panel)
        {
            foreach(var p in new[]{home,connection,room,garage,shop,settings})p.SetActive(p==panel);
            if(preview!=null)
            {
                preview.Visible(panel==garage||panel==shop||panel==home);
                bool showcase=panel==garage||panel==shop;
                MenuWidgets.Fill(previewImage.rectTransform,showcase?new Vector2(0,.1f):new Vector2(.48f,.16f),showcase?new Vector2(1,.92f):new Vector2(1,.89f));
            }
            Notice("");
        }
        public void ShowMain(string status)
        {
            foreach(var p in new[]{home,connection,room,garage,shop,settings})if(p!=null)p.SetActive(p==home);
            if(message!=null)message.text=status;
            if(!started)return;
            draft=CarLoadoutStore.Load(catalog);selected=draft.definition;candidate=draft;
            preview.Visible(true);MenuWidgets.Fill(previewImage.rectTransform,new Vector2(.48f,.16f),new Vector2(1,.89f));preview.Overview();RefreshPreview();Notice(status);
            ShowConnectionError(status);
        }
        public void ShowRoom()
        {
            Open(room);
            if(!started)return;
            // Ağ bileşeninin eski alanları yaşamaya devam eder; oda görünümünü bu panel çizer.
            foreach(Transform child in room.transform)child.gameObject.SetActive(child==roomCarContent);
            MenuWidgets.Clear(roomCarContent);
            choosingTrack=false;
            MenuWidgets.Height(roomCarContent,650);
            roomPlayers=MenuWidgets.Text(roomCarContent,"",23,165);roomPlayers.richText=false;
            roomInfo=MenuWidgets.Text(roomCarContent,"",22,85);
            trackButtons.Clear();trackInfo=null;
            MenuWidgets.Button(roomCarContent,"GARAJ / ARAÇ SEÇ",OpenGarage);
            MenuWidgets.Button(roomCarContent,"MAĞAZA",OpenShop);
            MenuWidgets.Height(MenuWidgets.Button(roomCarContent,"SÜR",ShowTracks).transform,65);
            MenuWidgets.Button(roomCarContent,"AYARLAR",()=>Open(settings));
            MenuWidgets.Button(roomCarContent,"ANA MENÜ",()=>lobby.Instance.LeaveRoom());
            readyButton=null;
            UpdateRoom();
        }
        void ShowTracks()
        {
            Open(room);choosingTrack=true;
            foreach(Transform child in room.transform)child.gameObject.SetActive(child==roomCarContent);
            MenuWidgets.Clear(roomCarContent);MenuWidgets.Height(roomCarContent,760);
            trackInfo=MenuWidgets.Text(roomCarContent,lobby.SinglePlayer?"PİST SEÇİMİ":"PİST OYLAMASI",28,44);
            roomPlayers=MenuWidgets.Text(roomCarContent,"",20,145);roomPlayers.richText=false;
            roomInfo=MenuWidgets.Text(roomCarContent,"",21,78);trackButtons.Clear();
            for(int i=0;i<lobby.TrackNames.Length;i++)
            {
                int index=i;
                var button=MenuWidgets.Button(roomCarContent,lobby.TrackNames[i],()=>{
                    var n=lobby.Instance;if(n==null||!n.InRoom)return;
                    if(lobby.SinglePlayer){n.SetTrack(index);n.SetReady(true);}
                    else {if(n.LocalReady)n.SetReady(false);n.VoteTrack(n.LocalVote==index?-1:index);}
                    UpdateRoom();
                });
                MenuWidgets.Height(button.transform,125);
                var label=button.GetComponentInChildren<TMP_Text>();label.richText=false;label.fontSize=21;
                trackButtons.Add(button);
            }
            readyButton=lobby.SinglePlayer?null:MenuWidgets.Button(roomCarContent,"HAZIR",()=>{
                var n=lobby.Instance;if(n!=null)n.SetReady(!n.LocalReady);
            });
            MenuWidgets.Button(roomCarContent,"GERİ",ShowRoom);UpdateRoom();
        }
        void ReturnToSession(){if(lobby.Instance!=null&&lobby.Instance.InRoom)ShowRoom();else ShowMain("");}
        void RoomChanged(){if(room.activeSelf)UpdateRoom();}
        void BuildConnectionOverlay()
        {
            var shade=MenuWidgets.Box(transform,"Bağlantı bildirimi",new Color(0,0,0,.82f));
            MenuWidgets.Fill(shade,Vector2.zero,Vector2.one);connectionOverlay=shade.gameObject;
            var box=MenuWidgets.Column(shade,"Bağlantı kutusu");
            MenuWidgets.Fill(box,new Vector2(.3f,.35f),new Vector2(.7f,.65f));
            connectionTitle=MenuWidgets.Text(box,"",32,55);connectionTitle.color=MenuWidgets.Accent;
            connectionNotice=MenuWidgets.Text(box,"",24,120);connectionNotice.richText=false;
            connectionNotice.enableWordWrapping=true;connectionNotice.overflowMode=TextOverflowModes.Ellipsis;
            connectionAction=MenuWidgets.Button(box,"TAMAM",()=>{
                bool cancel=connecting;connecting=false;connectionOverlay.SetActive(false);
                if(cancel&&lobby.Instance!=null)lobby.Instance.LeaveRoom();
                Notice("");
            });
            connectionOverlay.SetActive(false);
        }
        void MenuReturned(string status){ShowConnectionError(status);}
        void ShowConnectionError(string status)
        {
            if(connectionOverlay==null)return;
            connecting=false;
            connectionOverlay.SetActive(!string.IsNullOrWhiteSpace(status));
            if(!connectionOverlay.activeSelf)return;
            connectionOverlay.transform.SetAsLastSibling();
            connectionTitle.text="BAĞLANTI UYARISI";connectionNotice.text=status;
            connectionAction.GetComponentInChildren<TMP_Text>().text="TAMAM";
        }
        void UpdateConnection()
        {
            var n=lobby.Instance;var nm=NetworkManager.Singleton;
            bool pending=connection.activeSelf&&n!=null&&nm!=null&&nm.IsListening&&!n.InRoom;
            if(pending)
            {
                connecting=true;connectionOverlay.SetActive(true);connectionOverlay.transform.SetAsLastSibling();
                connectionTitle.text="BAĞLANIYOR…";connectionNotice.text="Odaya bağlanılıyor. Lütfen bekle.";
                connectionAction.GetComponentInChildren<TMP_Text>().text="İPTAL";
            }
            else if(connecting){connecting=false;connectionOverlay.SetActive(false);}
        }
        void UpdateRoom()
        {
            var n=lobby.Instance;if(n==null||!n.InRoom||roomPlayers==null)return;
            foreach(Transform child in room.transform)child.gameObject.SetActive(child==roomCarContent);
            var lines=new System.Text.StringBuilder(lobby.SinglePlayer?"TEK OYUNCU\n":"YARIŞ ODASI\n");
            for(int i=0;i<n.PlayerCount;i++){
                var p=n.GetPlayer(i);lines.Append(n.IsLocal(p)?"• ":"  ").Append(p.Name.ToString().Replace("<","‹"))
                    .Append(n.IsRoomOwner(p)?" / KURUCU":"").Append(p.Ready?" / HAZIR":" / BEKLİYOR")
                    .Append(lobby.SinglePlayer?"":p.VotedTrack>=0?" / OY: "+lobby.TrackNames[p.VotedTrack]:" / OY VERMEDİ").Append('\n');
            }
            roomPlayers.text=lines.ToString();
            if(choosingTrack)
            {
                for(int i=0;i<trackButtons.Count;i++)
                {
                    var button=trackButtons[i];
                    string details=i==0?"TURLU · yaklaşık 1,4 km":"SPRİNT · yaklaşık 5 km";
                    var names=new System.Text.StringBuilder();
                    if(!lobby.SinglePlayer)
                        for(int j=0;j<n.PlayerCount;j++)
                        {var p=n.GetPlayer(j);if(p.VotedTrack==i){if(names.Length>0)names.Append(", ");names.Append(p.Name.ToString());}}
                    button.GetComponentInChildren<TMP_Text>().text=lobby.TrackNames[i]+"\n"+details+
                        (lobby.SinglePlayer?"\nSEÇ VE SÜR":"\n"+n.TrackVotes(i)+" OY · "+(names.Length==0?"Henüz oy yok":names.ToString()));
                    button.GetComponent<Image>().color=!lobby.SinglePlayer&&n.LocalVote==i?new Color(.27f,.34f,.20f):new Color(.14f,.18f,.16f);
                }
            }
            roomInfo.text=lobby.SinglePlayer?"Aracını hazırla ve yola çık.":"DAVET KODU / "+n.RoomCode+
                (n.AutoStartRemaining>=0?"\nBAŞLIYOR / "+Mathf.CeilToInt(n.AutoStartRemaining):"\nOyunu ver, ardından HAZIR düğmesine bas.");
            if(readyButton!=null)readyButton.GetComponentInChildren<TMP_Text>().text=n.LocalReady?"HAZIR DEĞİLİM":"HAZIR";
            PistKarariBildirimi.Capture(n);
        }
        void LoadoutChanged(){var n=lobby.Instance;if(n!=null&&n.InRoom&&n.LocalReady)n.SetReady(false);}
        public void OpenGarage()
        {
            LoadoutChanged();draft=CarLoadoutStore.Load(catalog);selected=draft.definition;candidate=draft;section="GÖRÜNÜM";
            Open(garage);preview.Overview();DrawGarage();
        }
        public void OpenShop()
        {selected=CarLoadoutStore.Load(catalog).definition;draft=catalog.DefaultLoadout(selected);candidate=draft;Open(shop);preview.Overview();DrawShop();}
        void Notice(string text){if(message!=null)message.text=text;if(balance!=null)balance.text=HurdaEkonomisi.Dolar(HurdaEkonomisi.Bakiye);}
        void RefreshPreview(){if(Car!=null)preview.Show(Car,candidate);}
        public void Browse(int direction)
        {
            if(catalog.Count==0)return;
            for(int i=0;i<catalog.Count;i++)
            {
                selected=(selected+direction+catalog.Count)%catalog.Count;
                if(shop.activeSelf||HurdaEkonomisi.Sahip("araba/"+Car.id))break;
            }
            draft=shop.activeSelf?catalog.DefaultLoadout(selected):GarageLoadouts.Load(catalog,selected);candidate=draft;
            preview.Overview();if(shop.activeSelf)DrawShop();else DrawGarage();Notice("");
        }
        void DrawGarage()
        {
            var moduleScroll=garageModules.parent.parent.GetComponent<ScrollRect>();
            float scroll=lastSection==section?moduleScroll.verticalNormalizedPosition:1;lastSection=section;
            MenuWidgets.Clear(garageModules);MenuWidgets.Clear(garageContent);MenuWidgets.Clear(garageStats);
            if(Car==null){MenuWidgets.Text(garageContent,"Henüz araç yok.");return;}
            garageCarName.text=Car.displayName;
            foreach(var b in garage.GetComponentsInChildren<Button>(true))
                if(b.name=="GÖRÜNÜM"||b.name=="BOYA"||b.name=="PERFORMANS"||b.name=="ARACIM")b.GetComponent<Image>().color=b.name==section?MenuWidgets.Accent:new Color(.09f,.12f,.105f,.94f);
            if(section=="GÖRÜNÜM")DrawParts();
            else if(section=="BOYA")DrawPaint();
            else if(section=="PERFORMANS")DrawUpgrades();
            else
            {
                MenuWidgets.Text(garageModules,"SAHİP OLDUĞUN ARAÇLAR",20,50);
                for(int i=0;i<catalog.Count;i++)
                {
                    int index=i;var def=catalog.Get(i);if(def==null||!HurdaEkonomisi.Sahip("araba/"+def.id))continue;
                    MenuWidgets.Button(garageModules,(i==selected?"• ":"")+def.displayName,()=>{selected=index;draft=GarageLoadouts.Load(catalog,index);candidate=draft;DrawGarage();preview.Overview();});
                }
                MenuWidgets.Text(garageContent,"YOLA HAZIR",30,48);
                MenuWidgets.Text(garageContent,"Aracını seç, modifiyeni tamamla ve kaydet.",22,90);
                MenuWidgets.Button(garageContent,"KAYITLI MODİFİYEYE DÖN",()=>{draft=GarageLoadouts.Load(catalog,selected);candidate=draft;DrawGarage();Notice("Kayıtlı modifiye yüklendi.");});
            }
            DrawStats(garageStats,draft,candidate);RefreshPreview();Canvas.ForceUpdateCanvases();moduleScroll.verticalNormalizedPosition=scroll;
        }
        void FocusModule()
        {
            if(Car==null)return;
            if(section=="GÖRÜNÜM"&&Car.slots.Count>0)preview.Focus(Car.slots[selectedSlot].id);
            else if(section=="BOYA"&&Car.paints.Count>0){if(selectedPaint==0)preview.Overview();else preview.Focus(Car.paints[selectedPaint].id);}
            else preview.Overview();
        }
        string PartKey(int slot,int v)=>"parca/"+Car.id+"/"+Car.slots[slot].id+"/"+Car.slots[slot].variants[v].key;
        string UpgradeKey(CarUpgradeType type,int l)=>"gelistirme/"+Car.id+"/"+type+"/"+l;
        void DrawParts()
        {
            MenuWidgets.Text(garageModules,"01 / GÖRSEL MODÜLLER",21,38);
            if(Car.slots.Count==0)return;selectedSlot=Mathf.Clamp(selectedSlot,0,Mathf.Min(Car.slots.Count,CarLoadout.MaxSlots)-1);
            for(int s=0;s<Car.slots.Count&&s<CarLoadout.MaxSlots;s++)
            {
                int slot=s;ModuleButton(garageModules,Car.slots[s].displayName,s==selectedSlot,()=>{selectedSlot=slot;candidate=draft;DrawGarage();FocusModule();});
            }
            var mod=Car.slots[selectedSlot];variant=Mathf.Clamp(candidate.GetSlot(selectedSlot),0,mod.variants.Count-1);
            MenuWidgets.Text(garageContent,mod.displayName.ToUpperInvariant(),28,46);
            MenuWidgets.Text(garageContent,"PARÇAYI ARAÇ ÜZERİNDE DENE",18,28);
            for(int v=0;v<mod.variants.Count;v++)
            {
                int option=v;var part=mod.variants[v];bool owned=v==0||HurdaEkonomisi.Sahip(PartKey(selectedSlot,v));
                string state=draft.GetSlot(selectedSlot)==v?"TAKILI":owned?"SAHİP OLUNAN":HurdaEkonomisi.Dolar(HurdaEkonomisi.Fiyat(part.price));
                ModuleButton(garageContent,part.displayName+"\n<size=17>"+state+"</size>",v==variant,()=>{candidate=draft;candidate.SetSlot(selectedSlot,option);DrawGarage();Notice("Parça önizleniyor. Uygulamak için parçayı tak.");});
            }
            int price=HurdaEkonomisi.Fiyat(mod.variants[variant].price);bool have=variant==0||HurdaEkonomisi.Sahip(PartKey(selectedSlot,variant));
            var confirm=MenuWidgets.Button(garageContent,have?"PARÇAYI TAK":"SATIN AL VE TAK / "+HurdaEkonomisi.Dolar(price),()=>
            {
                if(variant>0&&!HurdaEkonomisi.SatinAl(PartKey(selectedSlot,variant),price)){Notice("Yeterli dolar yok.");return;}
                LoadoutChanged();draft=candidate;Notice("Parça takıldı. Modifiyeni kaydetmeyi unutma.");DrawGarage();
            });confirm.interactable=have||HurdaEkonomisi.Bakiye>=price;
        }
        void DrawPaint()
        {
            MenuWidgets.Text(garageModules,"02 / BOYA ATÖLYESİ",21,38);if(Car.paints.Count==0)return;
            selectedPaint=Mathf.Clamp(selectedPaint,0,Mathf.Min(Car.paints.Count,CarLoadout.MaxPaints)-1);
            for(int p=0;p<Car.paints.Count&&p<CarLoadout.MaxPaints;p++)
            {int paint=p;ModuleButton(garageModules,Car.paints[p].displayName,p==selectedPaint,()=>{selectedPaint=paint;candidate=draft;DrawGarage();FocusModule();});}
            MenuWidgets.Text(garageContent,Car.paints[selectedPaint].displayName.ToUpperInvariant(),28,46);
            MenuWidgets.Text(garageContent,"RENK PALETİ",18,30);
            var palette=MenuWidgets.Box(garageContent,"Renk paleti",Color.clear);MenuWidgets.Height(palette,130);
            var grid=palette.gameObject.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(52,52);grid.spacing=new Vector2(7,7);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=5;
            foreach(string hex in new[]{"D6D0B8","242C29","A64028","D29233","537C72","455F86","B4C859","762B3E","EEECE2","504C49"})
            {Color color;ColorUtility.TryParseHtmlString("#"+hex,out color);var choice=color;var b=MenuWidgets.Button(palette," ",()=>ApplyPaint(choice));b.GetComponent<Image>().color=color;}
            MenuWidgets.Text(garageContent,"SERBEST RENK / HEX",18,30);
            var input=MenuWidgets.Input(garageContent,ColorUtility.ToHtmlStringRGBA(draft.GetPaint(selectedPaint)),"RRGGBB veya RRGGBBAA");input.characterLimit=9;
            MenuWidgets.Button(garageContent,"RENGİ UYGULA",()=>{Color c;if(ColorUtility.TryParseHtmlString("#"+input.text.Trim().TrimStart('#'),out c))ApplyPaint(c);else Notice("Geçerli bir renk gir: ör. C05030.");});
            MenuWidgets.Text(garageContent,"Renk doğrudan önizlemeye uygulanır. Kalıcı olması için kaydet.",19,80);
        }
        void ApplyPaint(Color color)
        {LoadoutChanged();draft.SetPaint(selectedPaint,color);candidate=draft;var input=garageContent.GetComponentInChildren<TMP_InputField>();if(input!=null)input.SetTextWithoutNotify(ColorUtility.ToHtmlStringRGBA(draft.GetPaint(selectedPaint)));RefreshPreview();Notice("Renk uygulandı.");}
        void DrawUpgrades()
        {
            MenuWidgets.Text(garageModules,"03 / PERFORMANS",21,38);if(Car.upgrades.Count==0)return;
            selectedUpgrade=Mathf.Clamp(selectedUpgrade,0,Car.upgrades.Count-1);
            for(int i=0;i<Car.upgrades.Count;i++)
            {int category=i;ModuleButton(garageModules,Car.upgrades[i].displayName,i==selectedUpgrade,()=>{selectedUpgrade=category;candidate=draft;DrawGarage();preview.Overview();});}
            var cat=Car.upgrades[selectedUpgrade];level=candidate.GetUpgrade(cat.type);
            MenuWidgets.Text(garageContent,cat.displayName.ToUpperInvariant(),28,46);
            MenuWidgets.Text(garageContent,"SEVİYEYİ SEÇ / ETKİSİNİ KARŞILAŞTIR",17,32);
            for(int i=0;i<=cat.levels.Length;i++)
            {
                int option=i;bool owned=i==0||HurdaEkonomisi.Sahip(UpgradeKey(cat.type,i));
                string label=i==0?"STOK":cat.levels[i-1].displayName;
                string price=owned?"SAHİP OLUNAN":HurdaEkonomisi.Dolar(HurdaEkonomisi.Fiyat(cat.levels[i-1].price));
                ModuleButton(garageContent,label+"\n<size=17>"+price+"</size>",i==level,()=>{candidate=draft;candidate.SetUpgrade(cat.type,option);DrawGarage();Notice("Seviye önizleniyor. Uygulamak için onayla.");});
            }
            bool have=level==0||HurdaEkonomisi.Sahip(UpgradeKey(cat.type,level));
            int cost=level==0?0:HurdaEkonomisi.Fiyat(cat.levels[level-1].price);bool previous=level<=1||HurdaEkonomisi.Sahip(UpgradeKey(cat.type,level-1));
            var buy=MenuWidgets.Button(garageContent,have?"SEVİYEYİ UYGULA":"SATIN AL VE UYGULA / "+HurdaEkonomisi.Dolar(cost),()=>
            {
                if(level>0&&!HurdaEkonomisi.SatinAl(UpgradeKey(cat.type,level),cost)){Notice("Yeterli dolar yok.");return;}
                LoadoutChanged();draft=candidate;Notice("Geliştirme uygulandı. Modifiyeni kaydet.");DrawGarage();
            });buy.interactable=have||(previous&&HurdaEkonomisi.Bakiye>=cost);
            if(!previous&&!have)MenuWidgets.Text(garageContent,"Önce bir önceki seviyeyi satın al.",19,56);
        }
        static void ModuleButton(Transform parent,string label,bool active,UnityEngine.Events.UnityAction action)
        {
            var b=MenuWidgets.Button(parent,label,action);MenuWidgets.Height(b.transform,label.Contains("\n")?66:47);
            b.GetComponent<Image>().color=active?new Color(.27f,.34f,.20f,.98f):new Color(.08f,.105f,.09f,.9f);
            if(active)b.GetComponentInChildren<TMP_Text>().color=new Color(.86f,.94f,.62f);
        }
        void DrawStats(Transform parent,CarLoadout before,CarLoadout after)
        {
            MenuWidgets.Clear(parent);var a=Car.ComputeStats(before);var b=Car.ComputeStats(after);
            Stat(parent,"SON HIZ",a.topSpeed*3.6f,b.topSpeed*3.6f,300,"km/sa");Stat(parent,"İVME",a.maxAcceleration,b.maxAcceleration,30,"m/sn²");
            Stat(parent,"YOL TUTUŞ",a.tireFriction,b.tireFriction,3,"");Stat(parent,"FREN",a.brakeDeceleration,b.brakeDeceleration,50,"m/sn²");
        }
        static void Stat(Transform parent,string label,float a,float b,float maximum,string unit)
        {
            var col=MenuWidgets.Column(parent,label);col.GetComponent<Image>().color=new Color(.025f,.035f,.027f,.85f);
            var layout=col.GetComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(16,16,10,10);layout.spacing=7;
            MenuWidgets.Text(col,label,19,27);string values=Mathf.Approximately(a,b)?b.ToString("0.#"):a.ToString("0.#")+" → "+b.ToString("0.#");
            MenuWidgets.Text(col,values+" "+unit,25,34);
            var bar=MenuWidgets.Box(col,"Performans çubuğu",new Color(.12f,.15f,.12f));MenuWidgets.Height(bar,9);
            var fill=MenuWidgets.Box(bar,"Sonra",MenuWidgets.Accent);MenuWidgets.Fill(fill,Vector2.zero,new Vector2(Mathf.Clamp01(b/maximum),1));
            if(!Mathf.Approximately(a,b)){var old=MenuWidgets.Box(bar,"Önce",new Color(.82f,.83f,.74f));MenuWidgets.Fill(old,Vector2.zero,new Vector2(Mathf.Clamp01(a/maximum),.3f));}
        }
        void Save()
        {if(Car==null)return;if(!HurdaEkonomisi.Sahip("araba/"+Car.id)){Notice("Bu araç sende yok; önce satın al.");return;}LoadoutChanged();GarageLoadouts.Save(catalog,draft);candidate=draft;DrawGarage();Notice("Takılı modifiye ve araç seçimi kaydedildi.");}
        void DrawShop()
        {
            MenuWidgets.Clear(shopContent);if(Car==null)return;
            shopCarName.text=Car.displayName;shopCounter.text=(selected+1).ToString("00")+" / "+catalog.Count.ToString("00");
            bool owned=HurdaEkonomisi.Sahip("araba/"+Car.id);
            MenuWidgets.Text(shopContent,owned?"GARAJINDA MEVCUT":HurdaEkonomisi.Dolar(HurdaEkonomisi.Fiyat(Car.price)),28,42);
            foreach(var b in shop.GetComponentsInChildren<Button>(true))if(b.name=="SATIN AL")
            {b.GetComponentInChildren<TMP_Text>().text=owned?"GARAJDA DÜZENLE":"ARACI SATIN AL";b.interactable=owned||HurdaEkonomisi.Bakiye>=HurdaEkonomisi.Fiyat(Car.price);}
            DrawStats(shopStats,draft,draft);RefreshPreview();
        }
        void BuyCar()
        {
            if(Car==null)return;
            if(HurdaEkonomisi.Sahip("araba/"+Car.id))
            {int index=selected;OpenGarage();selected=index;draft=GarageLoadouts.Load(catalog,index);candidate=draft;section="ARACIM";DrawGarage();return;}
            if(!HurdaEkonomisi.SatinAl("araba/"+Car.id,HurdaEkonomisi.Fiyat(Car.price))){Notice("Yeterli dolar yok.");return;}
            DrawShop();Notice("Araç satın alındı. Garajda düzenleyebilirsin.");
        }
        void Update()
        {
            if(started){UpdateConnection();if(lobby.Instance!=null)PistKarariBildirimi.Capture(lobby.Instance);}
            if(started&&room.activeSelf&&Time.unscaledTime>=nextRoomUpdate){nextRoomUpdate=Time.unscaledTime+.1f;UpdateRoom();}
            if(!started||(!shop.activeSelf&&!(garage.activeSelf&&section=="ARACIM")))return;
            var active=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            if(active!=null&&active.GetComponent<TMP_InputField>()!=null)return;
            if(Input.GetKeyDown(KeyCode.LeftArrow))Browse(-1);if(Input.GetKeyDown(KeyCode.RightArrow))Browse(1);
        }
        void OnDestroy(){if(network!=null){network.RoomChanged-=RoomChanged;network.MenuReturned-=MenuReturned;}if(ownsCatalog&&catalog!=null)Destroy(catalog);}
    }
}
