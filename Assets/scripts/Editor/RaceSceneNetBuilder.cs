using Harvester.Race;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// race sahnesine yarışın ağ objelerini ekler (Harvester > Rebuild Race Network):
///   RaceManager  : NetworkObject (sahne objesi) + RaceManager + CarContactSolver
///   RaceSystems  : RaceSpawner (araba ve harvester prefab'ları atanır) + RaceHUD
/// Var olanları silip baştan kurar. Araba ve harvester prefab'ları DefaultNetworkPrefabs'ta yoksa ekler.
/// Sahne açık değilse açar ve sonunda kaydeder.
/// </summary>
public static class RaceSceneNetBuilder
{
    const string ScenePath = "Assets/Scenes/race.unity";
    const string CarPrefabPath = "Assets/Prefabs/race/RaceCar.prefab";
    const string HarvesterPrefabPath = "Assets/Prefabs/race/HarvesterAI.prefab";
    const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

    [MenuItem("Harvester/Rebuild Race Network")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Rebuild Race Network: play mode'dan çıkın.");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "RaceManager" || root.name == "RaceSystems") Object.DestroyImmediate(root);

        var managerGo = new GameObject("RaceManager");
        managerGo.AddComponent<NetworkObject>();
        managerGo.AddComponent<RaceManager>();
        managerGo.AddComponent<CarContactSolver>();

        var systems = new GameObject("RaceSystems");
        var spawner = systems.AddComponent<RaceSpawner>();
        systems.AddComponent<RaceHUD>();

        var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefabPath);
        var harvesterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HarvesterPrefabPath);
        var so = new SerializedObject(spawner);
        so.FindProperty("carPrefab").objectReferenceValue = carPrefab;
        so.FindProperty("harvesterPrefab").objectReferenceValue = harvesterPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        if (carPrefab == null) Debug.LogError("Rebuild Race Network: " + CarPrefabPath + " yok");
        if (harvesterPrefab == null) Debug.LogWarning("Rebuild Race Network: " + HarvesterPrefabPath + " henüz yok; harvester'sız kuruldu, prefab gelince tekrar çalıştırın.");

        EnsureRegistered(carPrefab);
        EnsureRegistered(harvesterPrefab);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // Sahne objesinin GlobalObjectIdHash'i kayıtta üretilir; boş kalmışsa uyar
        var no = managerGo.GetComponent<NetworkObject>();
        var hashField = typeof(NetworkObject).GetField("GlobalObjectIdHash",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        object hash = hashField != null ? hashField.GetValue(no) : null;
        if (hash != null && hash.ToString() == "0") Debug.LogError("Rebuild Race Network: RaceManager NetworkObject hash'i 0, sahneyi tekrar kaydedin.");
        Debug.Log("Rebuild Race Network: RaceManager (hash " + hash + ") ve RaceSystems kuruldu, sahne kaydedildi.");
    }

    static void EnsureRegistered(GameObject prefab)
    {
        if (prefab == null || prefab.GetComponent<NetworkObject>() == null) return;
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
        if (list == null)
        {
            Debug.LogError("Rebuild Race Network: " + NetworkPrefabsPath + " yok");
            return;
        }
        if (list.Contains(prefab)) return;
        list.Add(new NetworkPrefab { Prefab = prefab });
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();
        Debug.Log("Rebuild Race Network: " + prefab.name + " DefaultNetworkPrefabs'a eklendi.");
    }
}
