using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Dikiz aynası için özelliksiz URP renderer'ını kurar / kaldırır.
// Menü: Harvester > Setup Rear View Mirror / Remove Rear View Mirror
//
// Kurulum:
// - Kalite seviyesinin varsayılan renderer'ını Assets/Settings/RearViewMirror_Renderer.asset olarak kopyalar,
//   kopyadan bütün renderer özelliklerini (LethalLook tam ekran geçişi) siler. Ara doku "Auto" olur ki
//   özelliksiz kamera mümkünse doğrudan dokuya çizsin; derinlik ön geçişi kapanır.
// - Bu renderer'ı her kalite seviyesinin URP asset'inin renderer listesine EKLER (varsayılan renderer değişmez,
//   diğer kameralar etkilenmez). RearViewMirror çalışma zamanında listede adıyla arar; yoksa varsayılanı kullanır.
// Kaldırma: listelerden çıkarır ve asset'i siler.
public static class RearViewMirrorBuilder
{
    const string RendererPath = "Assets/Settings/RearViewMirror_Renderer.asset";
    const string RendererName = Harvester.Race.RearViewMirror.RendererName;

    [MenuItem("Harvester/Setup Rear View Mirror")]
    public static void Setup()
    {
        var mirror = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (mirror == null)
        {
            var source = DefaultRenderer(GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset);
            if (source == null)
            {
                Debug.LogError("Dikiz aynası: kaynak URP renderer'ı bulunamadı.");
                return;
            }
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), RendererPath))
            {
                Debug.LogError("Dikiz aynası: renderer kopyalanamadı.");
                return;
            }
            mirror = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }

        mirror.name = RendererName;
        RemoveAllFeatures(mirror);
        mirror.intermediateTextureMode = IntermediateTextureMode.Auto;
        mirror.depthPrimingMode = DepthPrimingMode.Disabled;
        EditorUtility.SetDirty(mirror);

        int added = 0;
        foreach (var asset in Pipelines())
        {
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == mirror) present = true;
            if (present) continue;
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = mirror;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            added++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Dikiz aynası renderer'ı kuruldu: " + RendererPath + " (" + added + " URP asset'ine eklendi).");
    }

    [MenuItem("Harvester/Remove Rear View Mirror")]
    public static void Remove()
    {
        var mirror = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        int removed = 0;
        foreach (var asset in Pipelines())
        {
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            int defaultIndex = so.FindProperty("m_DefaultRendererIndex").intValue;
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue;
                bool isMirror = data == null ? false : (data == mirror || data.name == RendererName);
                if (!isMirror || i == defaultIndex) continue;
                list.GetArrayElementAtIndex(i).objectReferenceValue = null;
                list.DeleteArrayElementAtIndex(i);
                removed++;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
        }
        AssetDatabase.SaveAssets();
        if (mirror != null) AssetDatabase.DeleteAsset(RendererPath);
        Debug.Log("Dikiz aynası renderer'ı kaldırıldı (" + removed + " listeden çıkarıldı).");
    }

    static IEnumerable<UniversalRenderPipelineAsset> Pipelines()
    {
        var done = new HashSet<UniversalRenderPipelineAsset>();
        var defaultAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (defaultAsset != null && done.Add(defaultAsset)) yield return defaultAsset;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            var asset = QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset;
            if (asset != null && done.Add(asset)) yield return asset;
        }
    }

    static UniversalRendererData DefaultRenderer(UniversalRenderPipelineAsset asset)
    {
        if (asset == null) return null;
        var so = new SerializedObject(asset);
        var list = so.FindProperty("m_RendererDataList");
        int index = Mathf.Clamp(so.FindProperty("m_DefaultRendererIndex").intValue, 0, Mathf.Max(0, list.arraySize - 1));
        return list.arraySize > 0 ? list.GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData : null;
    }

    // Kopyalanan renderer'ın özellikleri asset'in alt objeleri; listeden ve asset'ten silinir (LookBuilder'daki gibi)
    static void RemoveAllFeatures(ScriptableRendererData renderer)
    {
        var so = new SerializedObject(renderer);
        var features = so.FindProperty("m_RendererFeatures");
        var map = so.FindProperty("m_RendererFeatureMap");
        var toDelete = new List<Object>();
        for (int i = features.arraySize - 1; i >= 0; i--)
        {
            var feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
            if (feature != null) toDelete.Add(feature);
            features.GetArrayElementAtIndex(i).objectReferenceValue = null;
            features.DeleteArrayElementAtIndex(i);
        }
        map.ClearArray();
        so.ApplyModifiedProperties();

        // Listede olmayan artık alt objeler de (ScriptableRendererFeature) temizlensin
        foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GetAssetPath(renderer)))
            if (sub is ScriptableRendererFeature && !toDelete.Contains(sub)) toDelete.Add(sub);
        foreach (var obj in toDelete)
        {
            AssetDatabase.RemoveObjectFromAsset(obj);
            Object.DestroyImmediate(obj, true);
        }
        EditorUtility.SetDirty(renderer);
    }
}
