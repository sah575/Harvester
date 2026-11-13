using System.Linq;
using Harvester.Race;
using Harvester.UI;
using UnityEditor;
using UnityEngine;

public sealed class MenuCarRegistryBuilder : AssetPostprocessor
{
    const string Path="Assets/UI/MenuCarRegistry.asset";
    public static MenuCarRegistry RefreshRegistry()
    {
        var registry=AssetDatabase.LoadAssetAtPath<MenuCarRegistry>(Path);
        if(registry==null){registry=ScriptableObject.CreateInstance<MenuCarRegistry>();AssetDatabase.CreateAsset(registry,Path);}
        var definitions=AssetDatabase.FindAssets("t:CarDefinition",new[]{"Assets/Cars"}).Select(g=>AssetDatabase.LoadAssetAtPath<CarDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d=>d!=null).OrderBy(d=>d.id=="RacerCoupe_01"?0:1).ThenBy(d=>d.id,System.StringComparer.Ordinal).ToList();
        var catalog=AssetDatabase.FindAssets("t:CarCatalog").Select(g=>AssetDatabase.LoadAssetAtPath<CarCatalog>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
        if(registry.catalog!=catalog||!registry.definitions.SequenceEqual(definitions))
        {registry.catalog=catalog;registry.definitions=definitions;EditorUtility.SetDirty(registry);AssetDatabase.SaveAssetIfDirty(registry);}
        return registry;
    }
    static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] previous)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(imported.Concat(deleted).Concat(moved).Concat(previous).Any(p=>p.StartsWith("Assets/Cars/",System.StringComparison.Ordinal)&&p.EndsWith(".asset",System.StringComparison.Ordinal)) || imported.Any(p=>p.EndsWith(".asset")&&AssetDatabase.GetMainAssetTypeAtPath(p)==typeof(CarCatalog)))
            EditorApplication.delayCall+=()=>RefreshRegistry();
    }
}
