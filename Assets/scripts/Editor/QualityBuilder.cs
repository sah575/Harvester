using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Gece ışığı, LethalLook ve ayna korunarak üç kalite profili üretir.
public static class QualityBuilder
{
    const string Folder="Assets/Settings/Quality";
    [MenuItem("Harvester/Apply Quality Levels")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Önce oynatma modundan çık.");
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Settings","Quality");
        var source=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Ultra_PipelineAsset.asset");
        if(source==null)throw new InvalidOperationException("Kalite şablonu bulunamadı.");
        var sourceData=new SerializedObject(source).FindProperty("m_RendererDataList");
        var rendererSource=sourceData.GetArrayElementAtIndex(0).objectReferenceValue;
        var names=new[]{"Düşük","Orta","Yüksek"};var paths=new[]{"Dusuk","Orta","Yuksek"};
        var pipelines=new UniversalRenderPipelineAsset[3];
        for(int i=0;i<3;i++)
        {
            string rendererPath=Folder+"/"+paths[i]+"_Renderer.asset";
            if(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath)==null)
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(rendererSource),rendererPath);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            renderer.renderingMode=i==0?RenderingMode.Forward:RenderingMode.ForwardPlus;
            EditorUtility.SetDirty(renderer);AssetDatabase.SaveAssetIfDirty(renderer);
            string path=Folder+"/"+paths[i]+"_Pipeline.asset";
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if(pipeline==null){pipeline=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(pipeline,path);}
            else EditorUtility.CopySerialized(source,pipeline);
            pipeline.name=paths[i]+"_Pipeline";
            var so=new SerializedObject(pipeline);
            so.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue=renderer;
            so.FindProperty("m_RenderScale").floatValue=.5f;
            so.FindProperty("m_MainLightRenderingMode").intValue=1;
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue=1;
            so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue=8;
            so.FindProperty("m_MainLightShadowsSupported").boolValue=i>0;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue=false;
            so.FindProperty("m_SoftShadowsSupported").boolValue=i==2;
            so.FindProperty("m_ShadowDistance").floatValue=i==0?0:i==1?40:80;
            so.FindProperty("m_ShadowCascadeCount").intValue=i==2?2:1;
            so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);AssetDatabase.SaveAssetIfDirty(pipeline);pipelines[i]=pipeline;
            if(renderer.rendererFeatures.Count==0)throw new InvalidOperationException("Lethal renderer özelliği eksik: "+names[i]);
            bool mirror=false;var list=so.FindProperty("m_RendererDataList");
            for(int r=0;r<list.arraySize;r++)if(list.GetArrayElementAtIndex(r).objectReferenceValue!=null&&list.GetArrayElementAtIndex(r).objectReferenceValue.name=="RearViewMirror_Renderer")mirror=true;
            if(!mirror)throw new InvalidOperationException("Ayna renderer'ı eksik: "+names[i]);
        }
        var settings=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
        var quality=new SerializedObject(settings);var levels=quality.FindProperty("m_QualitySettings");levels.arraySize=3;
        for(int i=0;i<3;i++)
        {
            var item=levels.GetArrayElementAtIndex(i);item.FindPropertyRelative("name").stringValue=names[i];
            item.FindPropertyRelative("customRenderPipeline").objectReferenceValue=pipelines[i];
            item.FindPropertyRelative("vSyncCount").intValue=0;item.FindPropertyRelative("antiAliasing").intValue=0;
            item.FindPropertyRelative("pixelLightCount").intValue=8;item.FindPropertyRelative("globalTextureMipmapLimit").intValue=0;
            item.FindPropertyRelative("shadows").intValue=i==0?0:i==1?1:2;
            item.FindPropertyRelative("shadowDistance").floatValue=i==0?0:i==1?40:80;
            item.FindPropertyRelative("shadowCascades").intValue=i==2?2:1;
        }
        quality.FindProperty("m_CurrentQuality").intValue=2;
        var defaults=quality.FindProperty("m_PerPlatformDefaultQuality");
        for(int i=0;i<defaults.arraySize;i++)defaults.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue=2;
        quality.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(settings);AssetDatabase.SaveAssetIfDirty(settings);
        QualitySettings.SetQualityLevel(2,true);
        Debug.Log("Kaliteler kuruldu: Düşük / Orta / Yüksek. LethalLook ve ayna korunuyor.");
    }
}
