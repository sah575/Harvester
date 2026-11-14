using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Temizlik sonrası prefab ve sahnelerde kayıp bileşen/referans denetimi; dosyaları değiştirmez.
public static class ProjectValidation
{
    [MenuItem("Harvester/Doğrulama/Yarış Projesi")]
    public static void Validate()
    {
        var errors=new List<string>();int checkedObjects=0;
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs","Assets/Models/Harvester","Assets/Cars"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(root!=null)checkedObjects+=Check(root,path,errors);
            foreach(string dependency in AssetDatabase.GetDependencies(path,true))
                if(!File.Exists(dependency)&&!Directory.Exists(dependency))errors.Add(path+" -> eksik bağımlılık: "+dependency);
        }
        foreach(string path in new[]{"Assets/Scenes/Lobby.unity","Assets/Scenes/race.unity","Assets/Scenes/carTest.unity"})
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try {foreach(var root in scene.GetRootGameObjects())checkedObjects+=Check(root,path,errors);}
            finally {if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
        foreach(string error in errors)Debug.LogError(error);
        if(errors.Count>0)throw new InvalidOperationException("Proje denetimi: "+errors.Count+" eksik bileşen/referans.");
        Debug.Log("Proje denetimi geçti: "+checkedObjects+" nesne; kayıp script/referans yok.");
    }
    static int Check(GameObject root,string path,List<string> errors)
    {
        int count=0;
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
        {
            count++;
            if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)errors.Add(path+" / "+t.name+": kayıp script");
            foreach(var component in t.GetComponents<Component>())
            {
                if(component==null)continue;var so=new SerializedObject(component);var p=so.GetIterator();
                while(p.NextVisible(true))
                    if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue==null&&p.objectReferenceInstanceIDValue!=0)
                        errors.Add(path+" / "+t.name+" / "+p.propertyPath+": kayıp referans");
            }
        }
        return count;
    }
}
