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

// Yarış arabası prefab'ını, araba tanımlarını/kataloğunu ve ağsız araba test sahnesini koddan kurar.
// Menü: Harvester > Rebuild Race Car, Harvester > Build Car Test Scene, Harvester > Araç > ...
//
// Tek network prefab'ı (RaceCar.prefab) modelsizdir: gövde (Rigidbody, BoxCollider), ağ bileşenleri, RaceCar ve CarVisual.
// Görsel model spawn'da seçimdeki tanıma göre yerelde açılır. Yeni araba: Assets/Cars/<ad>/ altına model prefab'ı,
// Cars listesine bir satır, sonra Harvester/Araç/Update Car Definitions (ya da Rebuild Race Car).
public static partial class CarBuilder
{
    const string Folder = "Assets/Prefabs/race";
    const string PrefabPath = Folder + "/RaceCar.prefab";
    const string MaterialFolder = Folder + "/Materials";
    const string PhysicsMaterialPath = Folder + "/CarBody.physicMaterial";
    const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
    const string TestScenePath = "Assets/Scenes/carTest.unity";
    const string CatalogPath = "Assets/Cars/ArabaKatalogu.asset";

    // Katalog sırası = ağ indeksi (CarLoadout.definition) ve garajdaki sıra. İLK KURULUM sırası budur; katalog asset'i
    // varken mevcut sıra hiç değişmez, bu listede olmayan yeni tanımlar (Assets/Cars/*/*_Tanim.asset) sona eklenir.
    // Yeni araba: bu listenin sonuna ekle (ya da sadece tanımını oluştur, CarCatalogPostprocessor sona ekler).
    static readonly string[] Cars = { "RacerCoupe_01", "SupercarV10_01", "GrandTourerV8_01", "MuscleFastback_01", "HotHatch_01" };

    // Tekerlek sırası RaceCar ile aynı: ön sol, ön sağ, arka sol, arka sağ
    static readonly string[] WheelNames = { "wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right" };

    [MenuItem("Harvester/Rebuild Race Car")]
    public static void RebuildRaceCar()
    {
        var catalog = UpdateDefinitions();
        if (catalog == null || catalog.Count == 0 || catalog.Get(0) == null)
        {
            Debug.LogError("Araba kataloğu boş: " + CatalogPath);
            return;
        }
        // Birleştirme (CarVisual) çalışma zamanında mesh okur: modellerin Read/Write'ı açık olmalı
        foreach (var def in catalog.cars) MakeModelReadable(def);
        Directory.CreateDirectory(Folder);

        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("RaceCar");
            SceneManager.MoveGameObjectToScene(root, preview);
            Build(root, catalog);
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
        Debug.Log("Yarış arabası kuruldu: " + PrefabPath + " (katalog: " + catalog.Count + " araba)");
    }

    static void Build(GameObject root, CarCatalog catalog)
    {
        // Varsayılan araba (katalogdaki ilk): collider ve geri dönüş değerleri ondan
        var def = catalog.Get(0);
        var g = def.geometry;
        var stats = def.baseStats;

        var box = root.AddComponent<BoxCollider>();
        box.center = g.colliderCenter;
        box.size = g.colliderSize;
        box.sharedMaterial = GetPhysicsMaterial();

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = stats.mass;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        root.AddComponent<NetworkObject>();
        var nt = root.AddComponent<NetworkTransform>();
        ConfigureNetworkTransform(nt);
        var nrb = root.AddComponent<NetworkRigidbody>();
        nrb.UseRigidBodyForMotion = false;
        nrb.AutoUpdateKinematicState = true;
        nrb.AutoSetKinematicOnDespawn = true;

        root.AddComponent<CarVisual>();
        var car = root.AddComponent<RaceCar>();
        var so = new SerializedObject(car);
        so.FindProperty("catalog").objectReferenceValue = catalog;
        // Serileştirilmiş değerler tanımla aynı olsun (katalog boşalırsa geri dönüş de aynı sürsün)
        var wheelsProp = so.FindProperty("wheelVisuals");
        var centersProp = so.FindProperty("wheelCenters");
        wheelsProp.arraySize = 4;
        centersProp.arraySize = 4;
        for (int i = 0; i < 4; i++)
        {
            wheelsProp.GetArrayElementAtIndex(i).objectReferenceValue = null;
            centersProp.GetArrayElementAtIndex(i).vector3Value = g.wheelCenters[i];
        }
        so.FindProperty("wheelRadius").floatValue = (g.wheelRadii[0] + g.wheelRadii[1] + g.wheelRadii[2] + g.wheelRadii[3]) / 4f;
        so.FindProperty("wheelBase").floatValue = g.wheelBase;
        so.FindProperty("centerOfMass").vector3Value = new Vector3(0f, stats.centerOfMassHeight, g.centerOfMassZ);
        WriteStats(so, stats);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Uzaktaki arabayı yumuşatır ve ileri tahmin eder (NetSyncBuilder'daki harvester değerleri)
        var ex = root.AddComponent<RemoteExtrapolator>();
        ex.PositionSmoothing = 0.1f;
        ex.Strength = 1f;
        ex.MaxLookahead = 1f;   // yüksek pingde ölçülen gecikme ~0.7 sn
        ex.MaxOffset = 45f;     // 47 m/sn × 1 sn; 12 m tahmini 300 ms dolaylarında kesiyordu
        ex.MaxYawOffset = 45f;
        ex.SnapDistance = 8f;
    }

    static void WriteStats(SerializedObject so, CarStats s)
    {
        so.FindProperty("topSpeed").floatValue = s.topSpeed;
        so.FindProperty("maxAcceleration").floatValue = s.maxAcceleration;
        if (s.accelerationCurve != null) so.FindProperty("accelerationCurve").animationCurveValue = s.accelerationCurve;
        so.FindProperty("brakeDeceleration").floatValue = s.brakeDeceleration;
        so.FindProperty("reverseTopSpeed").floatValue = s.reverseTopSpeed;
        so.FindProperty("reverseAcceleration").floatValue = s.reverseAcceleration;
        so.FindProperty("coastDeceleration").floatValue = s.coastDeceleration;
        so.FindProperty("handbrakeDeceleration").floatValue = s.handbrakeDeceleration;
        so.FindProperty("downforce").floatValue = s.downforce;
        so.FindProperty("maxSteerAngle").floatValue = s.maxSteerAngle;
        so.FindProperty("highSpeedSteerAngle").floatValue = s.highSpeedSteerAngle;
        so.FindProperty("steerFalloffSpeed").floatValue = s.steerFalloffSpeed;
        so.FindProperty("steerSpeed").floatValue = s.steerSpeed;
        so.FindProperty("steerAssist").floatValue = s.steerAssist;
        so.FindProperty("maxCornerAccel").floatValue = s.maxCornerAccel;
        so.FindProperty("frontGrip").floatValue = s.frontGrip;
        so.FindProperty("rearGrip").floatValue = s.rearGrip;
        so.FindProperty("tireFriction").floatValue = s.tireFriction;
        so.FindProperty("handbrakeRearGrip").floatValue = s.handbrakeRearGrip;
        so.FindProperty("driftYawBoost").floatValue = s.driftYawBoost;
        so.FindProperty("gripForceHeight").floatValue = s.gripForceHeight;
        so.FindProperty("suspensionUp").floatValue = s.suspensionUp;
        so.FindProperty("suspensionDroop").floatValue = s.suspensionDroop;
        so.FindProperty("springStrength").floatValue = s.springStrength;
        so.FindProperty("damperStrength").floatValue = s.damperStrength;
        so.FindProperty("antiRoll").floatValue = s.antiRoll;
        so.FindProperty("airUpright").floatValue = s.airUpright;
        so.FindProperty("airAngularDamping").floatValue = s.airAngularDamping;
        so.FindProperty("durability").floatValue = s.durability > 0f ? s.durability : CarStats.ReferenceDurability;
    }

    static void MakeModelReadable(CarDefinition def)
    {
        if (def == null || def.modelPrefab == null) return;
        var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(def.modelPrefab);
        string path = AssetDatabase.GetAssetPath(source != null ? source : def.modelPrefab);
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null || importer.isReadable) return;
        importer.isReadable = true;
        importer.SaveAndReimport();
        Debug.Log("Model okunabilir yapıldı (mesh birleştirme için): " + path);
    }

    // Harvester ile aynı: güvenilmez half-float delta, sıkıştırılmış quaternion, Lerp, NT yumuşatması kapalı
    static void ConfigureNetworkTransform(NetworkTransform nt)
    {
        nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        nt.UseUnreliableDeltas = true;
        nt.UseHalfFloatPrecision = true;
        nt.SyncPositionX = nt.SyncPositionY = nt.SyncPositionZ = true;
        nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        nt.Interpolate = true;
        nt.PositionInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
        nt.RotationInterpolationType = NetworkTransform.InterpolationTypes.Lerp;
        nt.PositionMaxInterpolationTime = 0.1f;
        nt.RotationMaxInterpolationTime = 0.1f;
        nt.PositionThreshold = 0.01f;
        nt.SyncRotAngleX = nt.SyncRotAngleY = nt.SyncRotAngleZ = true;
        nt.UseQuaternionSynchronization = true;
        nt.UseQuaternionCompression = true;
        nt.RotAngleThreshold = 0.3f;
        // Yumuşatmayı RemoteExtrapolator'ın süzgeci yapıyor; NetworkTransform'unki sadece gecikme ekler.
        nt.PositionLerpSmoothing = false;
        nt.RotationLerpSmoothing = false;
    }

    static PhysicsMaterial GetPhysicsMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysicsMaterialPath);
        if (mat != null) return mat;
        // Duvara sürtünüp yapışmasın, çarpınca hafif sekisin
        mat = new PhysicsMaterial("CarBody")
        {
            dynamicFriction = 0.1f,
            staticFriction = 0.1f,
            bounciness = 0.1f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Average
        };
        AssetDatabase.CreateAsset(mat, PhysicsMaterialPath);
        return mat;
    }

    static void RegisterNetworkPrefab(GameObject prefab)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
        if (list == null)
        {
            Debug.LogError("Ağ prefab listesi yok: " + NetworkPrefabsPath);
            return;
        }
        // Yeniden kurulumda kök fileID değişirse eski kayıt kopar; boş kayıtları sadece bildir (başkasınınki olabilir)
        foreach (var entry in list.PrefabList)
            if (entry.Prefab == null && entry.Override == NetworkPrefabOverride.None)
                Debug.LogWarning("Ağ prefab listesinde boş kayıt var: " + NetworkPrefabsPath);
        if (!list.Contains(prefab))
        {
            list.Add(new NetworkPrefab { Prefab = prefab });
            Debug.Log("Ağ prefab listesine eklendi: " + PrefabPath);
        }
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();
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

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // ------------------------------------------------------------------ Test sahnesi

    [MenuItem("Harvester/Build Car Test Scene")]
    public static void BuildTestScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("Önce Harvester/Rebuild Race Car çalıştırın: " + PrefabPath + " yok");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Directory.CreateDirectory(MaterialFolder);
        var ground = GetMaterial("TestZemin", new Color(0.36f, 0.38f, 0.36f));
        var ramp = GetMaterial("TestRampa", new Color(0.85f, 0.55f, 0.15f));
        var obstacle = GetMaterial("TestEngel", new Color(0.75f, 0.15f, 0.12f));
        var marker = GetMaterial("TestIsaret", new Color(0.92f, 0.92f, 0.92f));

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sun = new GameObject("Gunes");
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.farClipPlane = 1500f;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<CarCamera>();
        camGo.transform.position = new Vector3(0f, 5f, -910f);

        // Zemin: 2 km × 2 km düzlük
        Box("Zemin", new Vector3(0f, -0.5f, 0f), Quaternion.identity, new Vector3(2000f, 1f, 2000f), ground);
        // Düzlük boyunca her 50 m'de bir işaret (hız hissi için)
        for (int z = -900; z <= 900; z += 50)
        {
            Box("Isaret", new Vector3(-10f, 0.5f, z), Quaternion.identity, new Vector3(0.4f, 1f, 0.4f), marker);
            Box("Isaret", new Vector3(10f, 0.5f, z), Quaternion.identity, new Vector3(0.4f, 1f, 0.4f), marker);
        }

        // %8 yokuş: 160 m uzunluk, +Z'ye doğru yükselir
        float slopeAngle = Mathf.Atan(0.08f) * Mathf.Rad2Deg;
        float slopeLength = 160f;
        Quaternion slopeRot = Quaternion.Euler(-slopeAngle, 0f, 0f);
        float slopeTop = slopeLength / 2f * Mathf.Sin(slopeAngle * Mathf.Deg2Rad);
        Vector3 slopeCenter = new Vector3(150f, slopeTop - 0.5f / Mathf.Cos(slopeAngle * Mathf.Deg2Rad), 0f);
        Box("Yokus_8", slopeCenter, slopeRot, new Vector3(20f, 1f, slopeLength), ramp);
        var slopeStart = Marker("Test_Yokus", slopeCenter + slopeRot * Vector3.up * 0.5f, slopeRot);

        // Rampa: 15°, 14 m
        float rampAngle = 15f, rampLength = 14f;
        Quaternion rampRot = Quaternion.Euler(-rampAngle, 0f, 0f);
        float rampY = rampLength / 2f * Mathf.Sin(rampAngle * Mathf.Deg2Rad) - 0.5f * Mathf.Cos(rampAngle * Mathf.Deg2Rad);
        Box("Rampa", new Vector3(60f, rampY, 0f), rampRot, new Vector3(8f, 1f, rampLength), ramp);
        var rampStart = Marker("Test_Rampa", new Vector3(60f, 0f, -120f), Quaternion.identity);

        // Engeller
        for (int i = 0; i < 6; i++)
        {
            Box("Engel", new Vector3(260f + (i % 2) * 12f, 1f, -60f + i * 25f), Quaternion.Euler(0f, i * 17f, 0f), new Vector3(3f, 2f, 3f), obstacle);
            var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cone.name = "Kuka";
            cone.transform.position = new Vector3(240f, 0.6f, -60f + i * 25f);
            cone.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
            cone.GetComponent<Renderer>().sharedMaterial = obstacle;
        }

        var straightStart = Marker("Test_Duzluk", new Vector3(0f, 0f, -900f), Quaternion.identity);
        var turnStart = Marker("Test_Donus", new Vector3(-400f, 0f, -300f), Quaternion.identity);

        var carGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        carGo.transform.SetPositionAndRotation(new Vector3(0f, 0.3f, -900f), Quaternion.identity);

        var testGo = new GameObject("CarAutoTest");
        var test = testGo.AddComponent<CarAutoTest>();
        var so = new SerializedObject(test);
        so.FindProperty("car").objectReferenceValue = carGo.GetComponent<RaceCar>();
        so.FindProperty("straightStart").objectReferenceValue = straightStart;
        so.FindProperty("turnStart").objectReferenceValue = turnStart;
        so.FindProperty("slopeStart").objectReferenceValue = slopeStart;
        so.FindProperty("rampStart").objectReferenceValue = rampStart;
        so.ApplyModifiedPropertiesWithoutUndo();

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
        RenderSettings.ambientEquatorColor = new Color(0.4f, 0.4f, 0.4f);
        RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.2f);

        EditorSceneManager.SaveScene(scene, TestScenePath);
        Debug.Log("Araba test sahnesi kuruldu: " + TestScenePath);
    }

    static GameObject Box(string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.isStatic = true;
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    static Transform Marker(string name, Vector3 position, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(position, rotation);
        return go.transform;
    }

    static Material GetMaterial(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
