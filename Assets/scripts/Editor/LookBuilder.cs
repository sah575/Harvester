using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Lethal Company tarzı görünümü açar / kapatır. Menü: Harvester > Apply Lethal Look / Remove Lethal Look
// - Bütün kalite seviyelerinin URP asset'inde düşük çözünürlük (renderScale) + yumuşatmasız büyütme (Point)
// - Her URP renderer'a LethalLook.shader'ı çalıştıran Full Screen Pass özelliği (kontur + posterize + dither)
// Arayüz (HUD, lobi) Screen Space Overlay olduğu için etkilenmez, tam çözünürlükte kalır.
public static class LookBuilder
{
    const string ShaderName = "Harvester/LethalLook";
    const string MaterialPath = "Assets/Metarials/LethalLook.mat";
    const string FeatureName = "LethalLook";
    const float LowResScale = 0.5f; // 1080p ekranda 540p çizer

    [MenuItem("Harvester/Apply Lethal Look")]
    public static void Apply()
    {
        var material = GetMaterial();
        ForEachPipeline(asset =>
        {
            asset.renderScale = LowResScale;
            asset.upscalingFilter = UpscalingFilterSelection.Point;
            EditorUtility.SetDirty(asset);

            var renderer = RendererOf(asset);
            if (renderer == null || FindFeature(renderer) != null) return;

            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = FeatureName;
            feature.passMaterial = material;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color;
            feature.fetchColorBuffer = true;
            AddFeature(renderer, feature);
        });
        AssetDatabase.SaveAssets();
        Debug.Log("Lethal görünümü açıldı.");
    }

    [MenuItem("Harvester/Remove Lethal Look")]
    public static void Remove()
    {
        ForEachPipeline(asset =>
        {
            asset.renderScale = 1f;
            asset.upscalingFilter = UpscalingFilterSelection.Auto;
            EditorUtility.SetDirty(asset);

            var renderer = RendererOf(asset);
            var feature = renderer != null ? FindFeature(renderer) : null;
            if (feature != null) RemoveFeature(renderer, feature);
        });
        AssetDatabase.SaveAssets();
        Debug.Log("Lethal görünümü kapatıldı.");
    }

    static Material GetMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null) return material;
        material = new Material(Shader.Find(ShaderName));
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    static void ForEachPipeline(System.Action<UniversalRenderPipelineAsset> action)
    {
        var done = new System.Collections.Generic.HashSet<UniversalRenderPipelineAsset>();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset asset && done.Add(asset)) action(asset);
        }
    }

    static UniversalRendererData RendererOf(UniversalRenderPipelineAsset asset)
    {
        var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
        return list.arraySize > 0 ? list.GetArrayElementAtIndex(0).objectReferenceValue as UniversalRendererData : null;
    }

    static ScriptableRendererFeature FindFeature(ScriptableRendererData renderer)
    {
        foreach (var feature in renderer.rendererFeatures)
        {
            if (feature != null && feature.name == FeatureName) return feature;
        }
        return null;
    }

    // Renderer özellikleri asset'in alt objesi olarak saklanır; m_RendererFeatureMap de onların yerel kimliklerini tutar
    static void AddFeature(ScriptableRendererData renderer, ScriptableRendererFeature feature)
    {
        AssetDatabase.AddObjectToAsset(feature, renderer);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

        var so = new SerializedObject(renderer);
        var features = so.FindProperty("m_RendererFeatures");
        var map = so.FindProperty("m_RendererFeatureMap");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(renderer);
    }

    static void RemoveFeature(ScriptableRendererData renderer, ScriptableRendererFeature feature)
    {
        var so = new SerializedObject(renderer);
        var features = so.FindProperty("m_RendererFeatures");
        var map = so.FindProperty("m_RendererFeatureMap");
        for (int i = features.arraySize - 1; i >= 0; i--)
        {
            if (features.GetArrayElementAtIndex(i).objectReferenceValue != feature) continue;
            features.GetArrayElementAtIndex(i).objectReferenceValue = null;
            features.DeleteArrayElementAtIndex(i);
            if (i < map.arraySize) map.DeleteArrayElementAtIndex(i);
        }
        so.ApplyModifiedProperties();
        AssetDatabase.RemoveObjectFromAsset(feature);
        Object.DestroyImmediate(feature, true);
        EditorUtility.SetDirty(renderer);
    }
}
