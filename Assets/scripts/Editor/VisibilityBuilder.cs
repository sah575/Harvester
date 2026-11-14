using UnityEditor;
using UnityEngine;

public static class VisibilityBuilder
{
    [MenuItem("Harvester/Apply Visibility Budget")]
    public static void Build()
    {
        var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers=tags.FindProperty("layers");
        if(LayerMask.NameToLayer(Harvester.Race.VisibilityBudget.DetailLayer)>=0)return;
        // 30 numaralı katman garaj önizlemesine ayrılmıştır.
        for(int i=8;i<30;i++)if(string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
        {
            layers.GetArrayElementAtIndex(i).stringValue=Harvester.Race.VisibilityBudget.DetailLayer;
            tags.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
            Debug.Log("Detay katmanı kuruldu: "+i);return;
        }
        throw new System.InvalidOperationException("Detay için boş katman bulunamadı.");
    }
}
