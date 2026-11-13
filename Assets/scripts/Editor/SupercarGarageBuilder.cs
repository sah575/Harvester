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
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Araç entegrasyonu için önce oynatma modundan çık.");
            return;
        }
        var importer = AssetImporter.GetAtPath(Folder + Id + ".fbx") as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Supercar V10 FBX modeli bulunamadı.");
        // CarVisual'ın mesh birleştirmesi çalışma zamanında mesh verisini okur.
        if (!importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + Id + ".fbx");
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
            instance.name = Id;
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                var match = Part.Match(t.name);
                if (!match.Success) match = Rim.Match(t.name);
                if (match.Success) t.gameObject.SetActive(int.Parse(match.Groups[1].Value) == 0);
                else if (t.name.StartsWith("decal_", StringComparison.Ordinal) || t.name.EndsWith("_tire_lettering", StringComparison.Ordinal))
                    t.gameObject.SetActive(false);
            }
            PrefabUtility.SaveAsPrefabAsset(instance, Folder + Id + ".prefab");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }

        bool created = AssetDatabase.LoadAssetAtPath<CarDefinition>(Folder + Id + "_Tanim.asset") == null;
        var definition = CarBuilder.EnsureDefinition(Id);
        if (definition == null) throw new InvalidOperationException("Supercar V10 tanımı oluşturulamadı.");
        if (created)
        {
            definition.displayName = "V10 Orta Motor Süper Spor";
            definition.price = 8000;
        }
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssetIfDirty(definition);
        var registry = MenuCarRegistryBuilder.RefreshRegistry();
        // Var olan ağ kataloğu varsa sırası korunur; yeni araç yalnızca sona eklenir.
        if (registry.catalog != null && registry.catalog.IndexOf(Id) < 0)
        {
            if (registry.catalog.Count >= 256) throw new InvalidOperationException("Araç kataloğu dolu.");
            registry.catalog.cars.Add(definition);
            EditorUtility.SetDirty(registry.catalog);
            AssetDatabase.SaveAssetIfDirty(registry.catalog);
        }
        Debug.Log("Supercar V10 garaja eklendi: " + definition.slots.Count + " parça yuvası, " + definition.paints.Count + " boya yuvası, " + definition.upgrades.Count + " geliştirme kategorisi.");
    }
}
