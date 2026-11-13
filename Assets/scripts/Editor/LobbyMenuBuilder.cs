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
            font.TryAddCharacters("ÇĞİÖŞÜçğıöşü←↑→↓—×²•");EditorUtility.SetDirty(font);AssetDatabase.SaveAssetIfDirty(font);
        }
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
        menus.balance=MenuWidgets.Text(root,"HURDA",24);menus.balance.alignment=TMPro.TextAlignmentOptions.Right;MenuWidgets.Fill((RectTransform)menus.balance.transform,new Vector2(.75f,.92f),new Vector2(.95f,.97f));
        var raw=MenuWidgets.Box(root,"Araç vitrini",Color.white);MenuWidgets.Fill(raw,new Vector2(.53f,.26f),new Vector2(.96f,.85f));
        Object.DestroyImmediate(raw.GetComponent<Image>());menus.previewImage=raw.gameObject.AddComponent<RawImage>();menus.previewImage.raycastTarget=false;
        var caption=MenuWidgets.Text(raw,"02—04 YARIŞÇI   /   TEK KURAL: GERİDE KALMA",21);MenuWidgets.Fill((RectTransform)caption.transform,new Vector2(.02f,-.18f),new Vector2(.98f,-.04f));
        menus.home=Panel(root,"Ana menü",new Vector2(.05f,.19f),new Vector2(.46f,.85f));
        MenuWidgets.Text(menus.home.transform,"HASAT\nBAŞLIYOR.",70,190);
        MenuWidgets.Text(menus.home.transform,"Motoru çalıştır. Sisin içinden çık.\nBiçerdöver kimseyi beklemez.",24,76);
        foreach(string label in new[]{"OYNA","GARAJ","MAĞAZA","AYARLAR","ÇIKIŞ"})MenuWidgets.Button(menus.home.transform,label);
        MenuWidgets.Button(menus.connection.transform,"ANA MENÜ");MenuWidgets.Button(menus.room.transform,"AYARLAR");
        menus.garage=Page(root,"Garaj","GARAJ / ATÖLYE",out menus.garageContent);
        var garageBack=(RectTransform)menus.garage.transform.Find("GERİ");MenuWidgets.Fill(garageBack,new Vector2(.04f,.025f),new Vector2(.48f,.10f));
        var save=MenuWidgets.Button(menus.garage.transform,"KAYDET");MenuWidgets.Fill((RectTransform)save.transform,new Vector2(.52f,.025f),new Vector2(.96f,.10f));
        menus.shop=Page(root,"Mağaza","MAĞAZA / TEDARİK",out menus.shopContent);
        menus.settings=Page(root,"Ayarlar","AYARLAR",out menus.settingsContent);
        var roomSummary=MenuWidgets.Column(menus.room.transform,"Aracım");MenuWidgets.Height(roomSummary,105);menus.roomCarContent=roomSummary;
        menus.message=MenuWidgets.Text(root,"",23);MenuWidgets.Fill((RectTransform)menus.message.transform,new Vector2(.05f,.035f),new Vector2(.95f,.1f));
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
