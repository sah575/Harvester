using System.Collections.Generic;
using System.IO;
using Harvester.Net;
using Harvester.Race;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Yapay zekânın sürdüğü yarış harvester'ının prefab'ını koddan kurar ve testini sahneye koyar.
// Menü: Harvester > Rebuild Harvester AI, Harvester > Harvester Test > ...
// Görsel Assets/Models/Harvester/HarvesterGorsel.prefab'tan kopyalanır (eski FRANKV2'nin yalnız görsel kopyası, sadece okunur). Model -Z'ye bakıyor; "Model" çocuğu
// 180° döndürülür, böylece kök +Z'ye bakar.
public static class HarvesterAIBuilder
{
    const string SourcePath = "Assets/Models/Harvester/HarvesterGorsel.prefab";
    const string Folder = "Assets/Prefabs/race";
    const string PrefabPath = Folder + "/HarvesterAI.prefab";
    const string DifficultyFolder = "Assets/scripts/Race/Harvester/Data";
    const string DifficultyPath = DifficultyFolder + "/HarvesterSeviye1.asset";
    const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    const string CarPrefabPath = "Assets/Prefabs/race/RaceCar.prefab";
    const string TestObjectName = "HarvesterTest (geçici)";

    // Tekerlek sırası HarvesterAI ile aynı: ön sol, ön sağ, arka sol, arka sağ (kökün yönüne göre)
    static readonly string[] WheelNames = { "front_left", "fron_right", "back_left", "back_right" };

    [MenuItem("Harvester/Rebuild Harvester AI")]
    public static void Rebuild()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogError("Harvester modeli yok: " + SourcePath);
            return;
        }
        Directory.CreateDirectory(Folder);
        var difficulty = GetDifficulty();

        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("HarvesterAI");
            SceneManager.MoveGameObjectToScene(root, preview);
            Build(root, source, difficulty, preview);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
        AssetDatabase.SaveAssets();

        // Prefab sahne dışında kaydedilince GlobalObjectIdHash yanlış yazılabiliyor; yeniden serileştir ve doğrula.
        AssetDatabase.ForceReserializeAssets(new[] { PrefabPath });
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        RegisterNetworkPrefab(prefab);
        CheckHash(PrefabPath);
        Debug.Log("Harvester AI prefab'ı kuruldu: " + PrefabPath);
    }

    static void Build(GameObject root, GameObject source, HarvesterDifficulty difficulty, Scene preview)
    {
        // 1. Görseli kopyala (prefab bağlantısız düz kopya)
        var copy = (GameObject)Object.Instantiate(source);
        SceneManager.MoveGameObjectToScene(copy, preview);
        copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        model.localRotation = Quaternion.Euler(0f, 180f, 0f);
        model.localScale = copy.transform.localScale;
        var children = new List<Transform>();
        foreach (Transform child in copy.transform) children.Add(child);
        foreach (var child in children) child.SetParent(model, false);
        Object.DestroyImmediate(copy);

        // Kabin kamerası noktası: eski sürücü kamerasının göz hizası (kök orijinde, dünya = kök uzayı). Eski kamera
        // 29° aşağı bakıyordu; yarışta yolu görmek için 10°.
        Vector3 eye = new Vector3(0f, 6.6f, 5.4f);
        var oldCamera = model.Find("Driver/Camera");
        if (oldCamera != null) eye = oldCamera.position;

        // 2. Eski davranış, kamera ve fizik parçalarını ayıkla
        var driver = model.Find("Driver");
        if (driver != null) Object.DestroyImmediate(driver.gameObject);
        var wheelColliders = model.Find("Wheel/Colliders");
        if (wheelColliders != null) Object.DestroyImmediate(wheelColliders.gameObject);
        foreach (var c in model.GetComponentsInChildren<Component>(true))
        {
            if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) continue;
            Object.DestroyImmediate(c);
        }

        // 3. Ölçüler (kök orijinde, dünya = kök uzayı)
        var wheels = new Transform[4];
        var radii = new float[4];
        var probes = new Vector3[4];
        float groundY = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            wheels[i] = FindDeep(model, WheelNames[i]);
            var r = wheels[i] != null ? wheels[i].GetComponent<Renderer>() : null;
            if (r == null)
            {
                Debug.LogError("Tekerlek bulunamadı: " + WheelNames[i]);
                continue;
            }
            Bounds b = r.bounds;
            radii[i] = b.extents.y;
            probes[i] = new Vector3(b.center.x, 0f, b.center.z);
            groundY = Mathf.Min(groundY, b.min.y);
        }
        // Tekerleklerin altı kökün y=0'ına otursun
        model.localPosition = new Vector3(0f, -groundY, 0f);
        var cabinView = new GameObject("KabinKamera").transform;
        cabinView.SetParent(root.transform, false);
        cabinView.localPosition = eye + new Vector3(0f, -groundY, 0f);
        cabinView.localRotation = Quaternion.Euler(10f, 0f, 0f);

        var reel = FindDeep(model, "spin");
        var tool = FindDeep(model, "tool");
        var header = model.Find("Front");

        Bounds body = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBody = false;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (header != null && r.transform.IsChildOf(header)) continue;
            if (!hasBody) { body = r.bounds; hasBody = true; }
            else body.Encapsulate(r.bounds);
        }
        Bounds headerBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasHeader = false;
        if (header != null)
            foreach (var r in header.GetComponentsInChildren<Renderer>())
            {
                if (!hasHeader) { headerBounds = r.bounds; hasHeader = true; }
                else headerBounds.Encapsulate(r.bounds);
            }

        // 4. Fizik
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.mass = 20000f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // Gövde: tablanın gerisinde, yerden biraz yukarıda (tekerlekler dahil genişlik)
        float headerBack = hasHeader ? headerBounds.min.z : body.max.z;
        float bodyFront = Mathf.Min(body.max.z, headerBack);
        float bodyBottom = 0.4f;
        var bodyCollider = new GameObject("GovdeCarpisma").AddComponent<BoxCollider>();
        bodyCollider.transform.SetParent(root.transform, false);
        bodyCollider.center = new Vector3(body.center.x, (bodyBottom + body.max.y) * 0.5f, (body.min.z + bodyFront) * 0.5f);
        bodyCollider.size = new Vector3(body.size.x, body.max.y - bodyBottom, bodyFront - body.min.z);

        var headerCollider = new GameObject("TablaCarpisma").AddComponent<BoxCollider>();
        headerCollider.transform.SetParent(root.transform, false);
        if (hasHeader)
        {
            headerCollider.center = new Vector3(0f, Mathf.Max(0.3f, headerBounds.min.y) * 0.5f + headerBounds.max.y * 0.5f, headerBounds.center.z);
            headerCollider.size = new Vector3(difficulty.headerWidth, headerBounds.max.y - Mathf.Max(0.3f, headerBounds.min.y), headerBounds.size.z);
        }
        else
        {
            headerCollider.center = new Vector3(0f, 1.4f, body.max.z + 1.2f);
            headerCollider.size = new Vector3(difficulty.headerWidth, 2.4f, 2.4f);
        }

        // 5. Farlar: modelde ışık yok (flash_* sadece mesh), iki güçlü spot ekle
        Vector3 lampLeft = new Vector3(-1.8f, 3.9f, 6.2f), lampRight = new Vector3(1.8f, 3.9f, 6.2f);
        var flashA = FindDeep(model, "flash_cam");
        var flashB = FindDeep(model, "flash_light");
        if (flashA != null && flashB != null)
        {
            Vector3 a = flashA.GetComponent<Renderer>().bounds.center, b = flashB.GetComponent<Renderer>().bounds.center;
            lampLeft = a.x < b.x ? a : b;
            lampRight = a.x < b.x ? b : a;
            lampLeft.z += 0.3f;
            lampRight.z += 0.3f;
        }
        AddSpot(root.transform, "Far_Sol", lampLeft);
        AddSpot(root.transform, "Far_Sag", lampRight);

        // 6. Ağ: NetworkObject önce
        root.AddComponent<NetworkObject>();
        var nt = root.AddComponent<NetworkTransform>();
        ConfigureNetworkTransform(nt);
        var ex = root.AddComponent<RemoteExtrapolator>();
        // NetSyncBuilder'daki harvester değerleri
        ex.PositionSmoothing = 0.1f;
        ex.Strength = 1f;
        ex.MaxLookahead = 0.6f;
        ex.MaxOffset = 12f;
        ex.MaxYawOffset = 45f;
        ex.SnapDistance = 8f;

        // 7. Yapay zekâ
        var ai = root.AddComponent<HarvesterAI>();
        var so = new SerializedObject(ai);
        so.FindProperty("difficulty").objectReferenceValue = difficulty;
        so.FindProperty("reel").objectReferenceValue = reel;
        so.FindProperty("headerVisual").objectReferenceValue = header;
        so.FindProperty("headerVisualWidth").floatValue = hasHeader ? headerBounds.size.x : 14.9f;
        so.FindProperty("headerVisualBaseScale").vector3Value = header != null ? header.localScale : Vector3.one;
        so.FindProperty("bodyCollider").objectReferenceValue = bodyCollider;
        so.FindProperty("headerCollider").objectReferenceValue = headerCollider;
        so.FindProperty("cabinView").objectReferenceValue = cabinView;
        var wheelsProp = so.FindProperty("wheels");
        var radiiProp = so.FindProperty("wheelRadii");
        var probesProp = so.FindProperty("groundProbes");
        wheelsProp.arraySize = radiiProp.arraySize = probesProp.arraySize = 4;
        for (int i = 0; i < 4; i++)
        {
            wheelsProp.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
            radiiProp.GetArrayElementAtIndex(i).floatValue = radii[i];
            probesProp.GetArrayElementAtIndex(i).vector3Value = probes[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        // Tabla görseli editörde de seviyenin genişliğinde görünsün (çalışırken HarvesterAI aynı şeyi yapar)
        if (header != null && hasHeader && headerBounds.size.x > 0.1f)
        {
            Vector3 s = header.localScale;
            s.x *= difficulty.headerWidth / headerBounds.size.x;
            header.localScale = s;
        }

        if (tool == null) Debug.LogWarning("Tabla görseli (tool) bulunamadı");
        // Fizik ölçümleri özgün modelden tamamlandıktan sonra yalnız görsel LOD'ları bağla.
        HarvesterLodBuilder.Attach(root, model, ai);
        Debug.Log(string.Format("Harvester ölçüleri: gövde {0}, tabla görseli {1:F1} m genişlik → {2} m, tekerlek yarıçapları {3:F2}/{4:F2}",
            bodyCollider.size.ToString("F1"), hasHeader ? headerBounds.size.x : 0f, difficulty.headerWidth, radii[0], radii[2]));
    }

    static void ConfigureNetworkTransform(NetworkTransform nt)
    {
        // Sunucu otoritesi: sadece sunucu sürer
        nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
        // Kayıplı bağlantıda donup sıçramasın: güncellemeler güvenilmez gider, kaybolan paket beklenmez.
        nt.UseUnreliableDeltas = true;
        nt.UseHalfFloatPrecision = true;
        nt.InLocalSpace = false;
        nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        nt.Interpolate = true;
        nt.PositionInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
        nt.RotationInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
        nt.PositionMaxInterpolationTime = 0.1f;
        nt.RotationMaxInterpolationTime = 0.1f;
        nt.PositionThreshold = 0.01f;
        // Zeminde eğilir: tam dönüş, sıkıştırılmış quaternion olarak.
        nt.SyncRotAngleX = nt.SyncRotAngleY = nt.SyncRotAngleZ = true;
        nt.UseQuaternionSynchronization = true;
        nt.UseQuaternionCompression = true;
        nt.RotAngleThreshold = 0.3f;
        // Yumuşatmayı RemoteExtrapolator'ın süzgeci yapıyor; NetworkTransform'unki sadece gecikme ekler.
        nt.PositionLerpSmoothing = false;
        nt.RotationLerpSmoothing = false;
    }

    static void AddSpot(Transform parent, string name, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(9f, 0f, 0f); // yola bakar
        var light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.range = 130f;
        light.spotAngle = 55f;
        light.innerSpotAngle = 18f;
        light.intensity = 600f; // URP'de ters kare düşüş: 40 m'de yolu aydınlatsın
        light.color = new Color(1f, 0.96f, 0.88f); // sert, soğuk beyaz
        light.shadows = LightShadows.None;
    }

    static HarvesterDifficulty GetDifficulty()
    {
        var d = AssetDatabase.LoadAssetAtPath<HarvesterDifficulty>(DifficultyPath);
        if (d != null) return d;
        Directory.CreateDirectory(DifficultyFolder);
        d = ScriptableObject.CreateInstance<HarvesterDifficulty>();
        d.level = 1;
        d.displayName = "Seviye 1";
        AssetDatabase.CreateAsset(d, DifficultyPath);
        AssetDatabase.SaveAssets();
        return d;
    }

    static void RegisterNetworkPrefab(GameObject prefab)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
        if (list == null)
        {
            Debug.LogError("Ağ prefab listesi yok: " + NetworkPrefabsPath);
            return;
        }
        if (!list.Contains(prefab))
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
            Debug.Log("Ağ prefab listesine eklendi: " + PrefabPath);
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
        }
    }

    static void CheckHash(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var no = prefab.GetComponent<NetworkObject>();
        string disk = System.Array.Find(File.ReadAllLines(path), l => l.Contains("GlobalObjectIdHash"));
        if (disk == null || !disk.Trim().EndsWith(no.PrefabIdHash.ToString()))
            Debug.LogError(path + ": diskteki hash (" + (disk ?? "yok").Trim() + ") editördekinden (" + no.PrefabIdHash + ") farklı");
        else
            Debug.Log(path + ": GlobalObjectIdHash doğru (" + no.PrefabIdHash + ")");
    }

    static Transform FindDeep(Transform parent, string path)
    {
        if (path.Contains("/"))
        {
            var direct = parent.Find(path);
            if (direct != null) return direct;
            path = path.Substring(path.LastIndexOf('/') + 1);
        }
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == path && t != parent) return t;
        return null;
    }

    // ------------------------------------------------------------------ Test (sahne, editör sırası gerekir)

    [MenuItem("Harvester/Harvester Test/Sahneye Kur")]
    public static void SetupTest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[HarvesterTest] play mode'dan çıkıp kurun");
            return;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var car = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefabPath);
        if (prefab == null || car == null)
        {
            Debug.LogError("[HarvesterTest] önce prefab'lar kurulmalı: " + PrefabPath + ", " + CarPrefabPath);
            return;
        }
        RemoveTest();
        var holder = new GameObject(TestObjectName);
        var harvester = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        harvester.transform.SetParent(holder.transform, false);
        var track = Object.FindAnyObjectByType<RaceTrack>();
        if (track != null)
        {
            Pose pose = track.GetHarvesterStartPose();
            harvester.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }
        var runner = holder.AddComponent<HarvesterTestRunner>();
        var so = new SerializedObject(runner);
        so.FindProperty("carPrefab").objectReferenceValue = car;
        so.FindProperty("harvester").objectReferenceValue = harvester.GetComponent<HarvesterAI>();
        so.FindProperty("harvesterPrefab").objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(holder.scene);
        Debug.Log("[HarvesterTest] sahneye kuruldu (kaydetmeyin, iş bitince 'Kaldır'). Play'e basınca test başlar.");
    }

    [MenuItem("Harvester/Harvester Test/Kaldır")]
    public static void RemoveTest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var existing = GameObject.Find(TestObjectName);
        if (existing == null) return;
        var scene = existing.scene;
        Object.DestroyImmediate(existing);
        EditorSceneManager.MarkSceneDirty(scene);
    }
}
