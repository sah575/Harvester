using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildOptimizationBuilder
{
    [MenuItem("Harvester/Apply Build Optimizations")]
    public static void Apply()
    {
        var settings=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/UniversalRenderPipelineGlobalSettings.asset");
        if(settings==null)throw new InvalidOperationException("URP global ayarları bulunamadı.");
        var so=new SerializedObject(settings);var iterator=so.GetIterator();int count=0;
        // URP 17'nin etkin managed ayarı ve eski uyumluluk alanları birlikte tutulur.
        while(iterator.Next(true))if(iterator.name=="m_StripUnusedPostProcessingVariants"&&iterator.propertyType==SerializedPropertyType.Boolean)
        {iterator.boolValue=true;count++;}
        if(count==0)throw new InvalidOperationException("URP shader ayıklama alanı bulunamadı.");
        so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(settings);AssetDatabase.SaveAssetIfDirty(settings);
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        Debug.Log("Build optimizasyonu: Mono; kullanılmayan post işlem varyantları ayıklanıyor ("+count+" alan).");
    }
    [MenuItem("Harvester/Build/Linux OpenGL Öncelikli")]
    public static void PreferOpenGL(){SetFirst(GraphicsDeviceType.OpenGLCore,GraphicsDeviceType.Vulkan);}
    [MenuItem("Harvester/Build/Linux Vulkan Öncelikli")]
    public static void PreferVulkan(){SetFirst(GraphicsDeviceType.Vulkan,GraphicsDeviceType.OpenGLCore);}
    static void SetFirst(GraphicsDeviceType first,GraphicsDeviceType fallback)
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64,new[]{first,fallback});
        Debug.Log("Linux grafik sırası: "+first+" / "+fallback);
    }
}
