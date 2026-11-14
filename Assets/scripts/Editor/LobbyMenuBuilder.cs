using System.Collections.Generic;
using Harvester.Race;
using Harvester.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Yalnızca LobbyUIBuilder tarafından çağrılır; mevcut ağ bileşeni ve referansları korunur.
[InitializeOnLoad]
public static class LobbyMenuBuilder
{
    static LobbyMenuBuilder() { EditorApplication.playModeStateChanged += state => { if(state==PlayModeStateChange.ExitingPlayMode)MenuSettings.RestoreScale(); }; }
    public static void Build(lobby network)
    {
        var root=(RectTransform)network.transform;
        var font=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/UI/TerminalFont.asset");
        if(font==null)
        {
            font=TMPro.TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf"));
            font.name="Harvester Terminal";font.fallbackFontAssetTable=new List<TMPro.TMP_FontAsset>();
            AssetDatabase.CreateAsset(font,"Assets/UI/TerminalFont.asset");AssetDatabase.AddObjectToAsset(font.material,font);foreach(var atlas in font.atlasTextures)AssetDatabase.AddObjectToAsset(atlas,font);
            font.TryAddCharacters("ÇĞİÖŞÜçğıöşü←↑→↓—×²•…‹›");EditorUtility.SetDirty(font);AssetDatabase.SaveAssetIfDirty(font);
        }
        font.TryAddCharacters("ÇĞİÖŞÜçğıöşü←↑→↓—×²•…‹›");
        EditorUtility.SetDirty(font);AssetDatabase.SaveAssetIfDirty(font);
        MenuWidgets.Font=font;
        var menus=network.GetComponent<LobbyMenus>();
        if(menus==null)menus=Undo.AddComponent<LobbyMenus>(network.gameObject);
        menus.font=font;
        menus.connection=root.Find("MenuPanel").gameObject;menus.room=root.Find("RoomPanel").gameObject;
        var frame=MenuWidgets.Box(root,"Endüstriyel çerçeve",new Color(.022f,.029f,.027f));MenuWidgets.Fill(frame,Vector2.zero,Vector2.one);frame.SetSiblingIndex(1);
        // Çizgiler ve kademeli koyuluk, dış dokuya ihtiyaç duymayan düşük çözünürlüklü terminal havası verir.
        for(int i=0;i<72;i++)
        {
            var line=MenuWidgets.Box(frame,"Tarama çizgisi",new Color(.12f,.16f,.12f,i%3==0?.1f:.035f));
            MenuWidgets.Fill(line,new Vector2(0,i/72f),new Vector2(1,(i+.12f)/72));line.GetComponent<Image>().raycastTarget=false;
        }
        var header=MenuWidgets.Text(root,"H A R V E S T E R   /   GECE VARDİYASI",28);MenuWidgets.Fill((RectTransform)header.transform,new Vector2(.045f,.92f),new Vector2(.75f,.97f));
        menus.balance=MenuWidgets.Text(root,"$0",24);menus.balance.alignment=TMPro.TextAlignmentOptions.Right;MenuWidgets.Fill((RectTransform)menus.balance.transform,new Vector2(.75f,.92f),new Vector2(.95f,.97f));
        var raw=MenuWidgets.Box(root,"Araç vitrini",Color.white);MenuWidgets.Fill(raw,new Vector2(.48f,.16f),new Vector2(1,.89f));
        Object.DestroyImmediate(raw.GetComponent<Image>());menus.previewImage=raw.gameObject.AddComponent<RawImage>();menus.previewImage.raycastTarget=true;
        menus.home=Panel(root,"Ana menü",new Vector2(.05f,.19f),new Vector2(.46f,.85f));
        MenuWidgets.Text(menus.home.transform,"HASAT\nBAŞLIYOR.",70,190);
        MenuWidgets.Text(menus.home.transform,"Motoru çalıştır. Sisin içinden çık.\nBiçerdöver kimseyi beklemez.",24,76);
        foreach(string label in new[]{"TEK OYUNCU","ÇOK OYUNCU","AYARLAR","ÇIKIŞ"})MenuWidgets.Button(menus.home.transform,label);
        MenuWidgets.Button(menus.connection.transform,"ANA MENÜ");MenuWidgets.Button(menus.room.transform,"AYARLAR");
        menus.garage=Showcase(root,"Garaj");
        Label(menus.garage.transform,"ATÖLYE / 03",new Vector2(.035f,.855f),new Vector2(.23f,.91f),34);
        menus.garageCarName=Label(menus.garage.transform,"ARACIM",new Vector2(.31f,.85f),new Vector2(.72f,.91f),36);
        AtButton(menus.garage.transform,"ÖNCEKİ ARAÇ",new Vector2(.255f,.85f),new Vector2(.30f,.91f),"‹");
        AtButton(menus.garage.transform,"SONRAKİ ARAÇ",new Vector2(.73f,.85f),new Vector2(.775f,.91f),"›");
        var tabRow=MenuWidgets.Box(menus.garage.transform,"Atölye bölümleri",Color.clear);MenuWidgets.Fill(tabRow,new Vector2(.035f,.785f),new Vector2(.55f,.835f));
        var tabLayout=tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();tabLayout.spacing=6;tabLayout.childForceExpandWidth=true;tabLayout.childControlWidth=true;
        foreach(string tab in new[]{"GÖRÜNÜM","BOYA","PERFORMANS","ARACIM"})MenuWidgets.Button(tabRow,tab);
        menus.garageModules=MenuWidgets.Scroll(menus.garage.transform,"Modüller");MenuWidgets.Fill((RectTransform)menus.garageModules.parent.parent,new Vector2(.035f,.31f),new Vector2(.215f,.765f));
        menus.garageContent=MenuWidgets.Scroll(menus.garage.transform,"Parça seçenekleri");MenuWidgets.Fill((RectTransform)menus.garageContent.parent.parent,new Vector2(.77f,.31f),new Vector2(.965f,.79f));
        menus.garageStats=StatRow(menus.garage.transform,new Vector2(.24f,.145f),new Vector2(.74f,.275f));
        AtButton(menus.garage.transform,"GERİ",new Vector2(.035f,.055f),new Vector2(.20f,.105f));
        AtButton(menus.garage.transform,"TÜM ARACI GÖR",new Vector2(.24f,.055f),new Vector2(.44f,.105f));
        AtButton(menus.garage.transform,"KAYDET",new Vector2(.77f,.055f),new Vector2(.965f,.105f));
        Label(menus.garage.transform,"FAREYLE TUT + ÇEVİR   /   TEKERLEKLE YAKLAŞ",new Vector2(.30f,.295f),new Vector2(.72f,.335f),18);
        menus.shop=Showcase(root,"Araç satın alma");
        Label(menus.shop.transform,"ARAÇ GALERİSİ",new Vector2(.035f,.855f),new Vector2(.27f,.91f),32);
        menus.shopCarName=Label(menus.shop.transform,"ARAÇ",new Vector2(.28f,.81f),new Vector2(.82f,.90f),42);
        menus.shopCounter=Label(menus.shop.transform,"01 / 03",new Vector2(.84f,.84f),new Vector2(.96f,.90f),30);
        AtButton(menus.shop.transform,"ÖNCEKİ ARAÇ",new Vector2(.045f,.46f),new Vector2(.10f,.56f),"‹");
        AtButton(menus.shop.transform,"SONRAKİ ARAÇ",new Vector2(.90f,.46f),new Vector2(.955f,.56f),"›");
        menus.shopContent=MenuWidgets.Column(menus.shop.transform,"Araç fiyatı");MenuWidgets.Fill(menus.shopContent,new Vector2(.39f,.71f),new Vector2(.64f,.80f));menus.shopContent.GetComponent<Image>().color=Color.clear;
        menus.shopStats=StatRow(menus.shop.transform,new Vector2(.15f,.145f),new Vector2(.85f,.275f));
        AtButton(menus.shop.transform,"GERİ",new Vector2(.035f,.055f),new Vector2(.20f,.105f));
        AtButton(menus.shop.transform,"SATIN AL",new Vector2(.70f,.055f),new Vector2(.965f,.105f));
        Label(menus.shop.transform,"←  ARAÇ SEÇ  →     /     FAREYLE TUT + ÇEVİR",new Vector2(.32f,.295f),new Vector2(.72f,.335f),19);
        menus.settings=Page(root,"Ayarlar","AYARLAR",out menus.settingsContent);
        var roomSummary=MenuWidgets.Column(menus.room.transform,"Aracım");MenuWidgets.Height(roomSummary,105);menus.roomCarContent=roomSummary;
        menus.message=MenuWidgets.Text(root,"",23);MenuWidgets.Fill((RectTransform)menus.message.transform,new Vector2(.035f,.008f),new Vector2(.96f,.043f));
        menus.message.color=new Color(.9f,.62f,.35f);
        menus.registry=MenuCarRegistryBuilder.RefreshRegistry();
        menus.definitions=menus.registry.definitions;
        menus.catalog=menus.registry.catalog;
        menus.fallbackCatalog=menus.catalog==null;
        if(menus.fallbackCatalog)Debug.LogWarning("Araç kataloğu yok. Garaj Assets/Cars altındaki tanımları listeler; yarışın kataloğu araç kurucusuyla ayrıca kurulmalıdır.");
        foreach(var p in new[]{menus.connection,menus.room,menus.garage,menus.shop,menus.settings})p.SetActive(false);
        foreach(var text in root.GetComponentsInChildren<TMPro.TMP_Text>(true))text.font=font;
        EditorUtility.SetDirty(menus);EditorSceneManager.MarkSceneDirty(network.gameObject.scene);
    }
    static GameObject Showcase(Transform root,string name)
    {
        var r=MenuWidgets.Box(root,name,Color.clear);MenuWidgets.Fill(r,Vector2.zero,Vector2.one);r.GetComponent<Image>().enabled=false;
        var top=MenuWidgets.Box(r,"Başlık bandı",new Color(.015f,.022f,.017f,.94f));MenuWidgets.Fill(top,new Vector2(0,.775f),new Vector2(1,.92f));top.GetComponent<Image>().raycastTarget=false;
        var lower=MenuWidgets.Box(r,"Alt bilgi bandı",new Color(.015f,.022f,.017f,.84f));MenuWidgets.Fill(lower,new Vector2(0,.12f),new Vector2(1,.285f));lower.GetComponent<Image>().raycastTarget=false;
        return r.gameObject;
    }
    static TMPro.TMP_Text Label(Transform parent,string text,Vector2 min,Vector2 max,float size)
    {var label=MenuWidgets.Text(parent,text,size);MenuWidgets.Fill((RectTransform)label.transform,min,max);label.fontStyle=TMPro.FontStyles.Italic;return label;}
    static void AtButton(Transform parent,string name,Vector2 min,Vector2 max,string label=null)
    {var b=MenuWidgets.Button(parent,name);MenuWidgets.Fill((RectTransform)b.transform,min,max);if(label!=null){var t=b.GetComponentInChildren<TMPro.TMP_Text>();t.text=label;t.fontSize=42;t.alignment=TMPro.TextAlignmentOptions.Center;}}
    static RectTransform StatRow(Transform parent,Vector2 min,Vector2 max)
    {var r=MenuWidgets.Box(parent,"Araç performansı",Color.clear);MenuWidgets.Fill(r,min,max);var layout=r.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=10;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;return r;}
    static GameObject Panel(Transform root,string name,Vector2 min,Vector2 max)
    {var r=MenuWidgets.Column(root,name);MenuWidgets.Fill(r,min,max);return r.gameObject;}
    static GameObject Page(Transform root,string name,string title,out RectTransform content)
    {
        var panel=MenuWidgets.Box(root,name,new Color(.028f,.037f,.032f));MenuWidgets.Fill(panel,new Vector2(.04f,.13f),new Vector2(.51f,.88f));
        var label=MenuWidgets.Text(panel,title,32);MenuWidgets.Fill((RectTransform)label.transform,new Vector2(.04f,.9f),new Vector2(.96f,.98f));
        content=MenuWidgets.Scroll(panel,"Kaydırılabilir alan");MenuWidgets.Fill((RectTransform)content.parent.parent,new Vector2(.02f,.12f),new Vector2(.98f,.89f));
        var back=MenuWidgets.Button(panel,"GERİ");MenuWidgets.Fill((RectTransform)back.transform,new Vector2(.04f,.025f),new Vector2(.96f,.10f));return panel.gameObject;
    }
}
