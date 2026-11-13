using System;
using System.Text.RegularExpressions;
using Harvester.Race;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Teslim edilen V10 modelini mevcut araç API'siyle garaja hazırlar; yarış prefab'ını yeniden kurmaz.
public static class SupercarGarageBuilder
{
    const string Id = "SupercarV10_01";
    const string Folder = "Assets/Cars/" + Id + "/";
    static readonly Regex Part = new Regex(@"^mod_.+?_(\d+)_");
    static readonly Regex Rim = new Regex(@"^wheel-.+_rim_(\d+)_");

    [MenuItem("Harvester/Arayüz/Supercar V10'u Garaja Ekle")]
    public static void Build() { Integrate(Id,"V10 Orta Motor Süper Spor",8000); }

    [MenuItem("Harvester/Arayüz/Grand Tourer V8'i Garaja Ekle")]
    public static void BuildGrandTourer() { Integrate("GrandTourerV8_01","V8 Uzun Kaput",10000); }

    [MenuItem("Harvester/Arayüz/Muscle Fastback'i Garaja Ekle")]
    public static void BuildMuscle() { Integrate("MuscleFastback_01","V8 Kaslı Fastback",9000); }

    [MenuItem("Harvester/Arayüz/Hot Hatch'i Garaja Ekle")]
    public static void BuildHotHatch()
    {
        Integrate("HotHatch_01","Turbo Hatch",6000);
        var definition=AssetDatabase.LoadAssetAtPath<CarDefinition>("Assets/Cars/HotHatch_01/HotHatch_01_Tanim.asset");
        var identity=JsonUtility.FromJson<HotIdentity>(System.IO.File.ReadAllText("Assets/Cars/HotHatch_01/HotHatch_01_kimlik.json"));
        foreach(var slot in definition.slots)
        {
            var field=typeof(HotParts).GetField(slot.id);
            var names=slot.id=="rim"?identity.jantlar:field!=null&&identity.slotlar!=null?(string[])field.GetValue(identity.slotlar):null;
            if(names==null)continue;
            for(int i=0;i<names.Length&&i<slot.variants.Count;i++)
            {
                string name=names[i].Replace("time attack","zamana karşı").Replace("aero","aerodinamik").Replace("stok","standart").Replace("file","örgü");
                if(name.Length>0)slot.variants[i].displayName=char.ToUpper(name[0],System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))+name.Substring(1);
            }
            if(slot.id=="rollcage")slot.displayName="Takla kafesi";
        }
        EditorUtility.SetDirty(definition);AssetDatabase.SaveAssetIfDirty(definition);
    }
    // Model kimlikleri korunur; yalnızca oyuncuya gösterilen adlar teslim JSON'undan alınır.
    [Serializable] class HotIdentity { public HotParts slotlar;public string[] jantlar; }
    [Serializable] class HotParts
    {
        public string[] bumper_front,bumper_rear,skirts,fender,hood,spoiler,exhaust,headlight,taillight;
        public string[] mirror,roof_scoop,rollcage,neon,steering,interior_passenger,interior_pillar,frunk;
    }

    public static void Integrate(string id,string displayName,int price)
    {
        string folder="Assets/Cars/"+id+"/";
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Araç entegrasyonu için önce oynatma modundan çık.");
            return;
        }
        var importer = AssetImporter.GetAtPath(folder + id + ".fbx") as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Supercar V10 FBX modeli bulunamadı.");
        // CarVisual'ın mesh birleştirmesi çalışma zamanında mesh verisini okur.
        if (!importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(folder + id + ".fbx");
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
            instance.name = id;
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                var match = Part.Match(t.name);
                if (!match.Success) match = Rim.Match(t.name);
                if (match.Success) t.gameObject.SetActive(int.Parse(match.Groups[1].Value) == 0);
                else if (t.name.StartsWith("decal_", StringComparison.Ordinal) || t.name.EndsWith("_tire_lettering", StringComparison.Ordinal))
                    t.gameObject.SetActive(false);
            }
            PrefabUtility.SaveAsPrefabAsset(instance, folder + id + ".prefab");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }

        bool created = AssetDatabase.LoadAssetAtPath<CarDefinition>(folder + id + "_Tanim.asset") == null;
        var definition = CarBuilder.EnsureDefinition(id);
        if (definition == null) throw new InvalidOperationException("Supercar V10 tanımı oluşturulamadı.");
        if (created)
        {
            definition.displayName = displayName;
            definition.price = price;
        }
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssetIfDirty(definition);
        var registry = MenuCarRegistryBuilder.RefreshRegistry();
        // Var olan ağ kataloğu varsa sırası korunur; yeni araç yalnızca sona eklenir.
        if (registry.catalog != null && registry.catalog.IndexOf(id) < 0)
        {
            if (registry.catalog.Count >= 256) throw new InvalidOperationException("Araç kataloğu dolu.");
            registry.catalog.cars.Add(definition);
            EditorUtility.SetDirty(registry.catalog);
            AssetDatabase.SaveAssetIfDirty(registry.catalog);
        }
        Debug.Log("Araç garaja eklendi: " + definition.slots.Count + " parça yuvası, " + definition.paints.Count + " boya yuvası, " + definition.upgrades.Count + " geliştirme kategorisi.");
    }
}
