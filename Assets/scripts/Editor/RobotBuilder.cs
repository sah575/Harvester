using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Parçalı robot modelini (roboParcali.fbx, Blender'da BlenderSources/robot.py ile üretilir) robot prefab'larına yerleştirir.
// Menü: Harvester > Rebuild Robot Prefabs
//
// - FBX kliplerinden kök ve ölçek eğrileri atılmış, yolları "Robot/" altına taşınmış .anim kopyaları üretir
//   (FBX klipleri kök objeyi de döndürüyor; prefab kökü 0.4 ölçekli ve hareket bileşenlerini taşıyor)
// - FBX'teki Blender materyallerini (Govde, Eklem, Goz) projedeki materyallere eşler
// - Prefab'ın eski model çocuklarını silip modeli "Robot" adıyla ekler; robo2'de gövde sarı
// - Hareket için kök objeye tek bir CapsuleCollider koyar (parçaların collider'ı ölünce eklenir)
public static class RobotBuilder
{
    const string ModelPath = "Assets/Prefabs/chars/roboParcali.fbx";
    const string ControllerPath = "Assets/anims/charsAnims/robotParcali.controller";
    const string IdleClipPath = "Assets/anims/charsAnims/robotIdle.anim";
    const string RunClipPath = "Assets/anims/charsAnims/robotRun.anim";
    const string EyeMaterialPath = "Assets/Metarials/colors/RobotGoz.mat";
    const string JointMaterialPath = "Assets/Metarials/colors/New Material 1.mat";
    const string DefaultBodyMaterialPath = "Assets/Metarials/robot.mat";
    const string ModelName = "Robot";

    // Eski skinli modelin prefab'daki çocukları
    static readonly string[] OldModelChildren = { "body", "head", "legs", "metarig.001", ModelName };

    // Robot boyu ~4.7 m; kök 0.4 ölçekli olduğu için yerel değerler 2.5 katı
    const float CapsuleHeight = 4.6f;
    const float CapsuleRadius = 0.6f;

    static readonly (string prefab, string bodyMaterial)[] Robots =
    {
        ("Assets/Prefabs/chars/robo1.prefab", "Assets/Metarials/robot.mat"),
        ("Assets/Prefabs/chars/robo2.prefab", "Assets/Metarials/colors/yellow.mat"),
    };

    [MenuItem("Harvester/Rebuild Robot Prefabs")]
    public static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"{ModelPath} yok. Önce Blender'dan dışa aktar.");
            return;
        }

        var defaultBody = AssetDatabase.LoadAssetAtPath<Material>(DefaultBodyMaterialPath);
        RemapMaterials(defaultBody);
        model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        var idle = CopyClip("idle", IdleClipPath);
        var run = CopyClip("run", RunClipPath);
        var controller = BuildController(idle, run);

        foreach (var (prefabPath, bodyPath) in Robots)
        {
            var body = AssetDatabase.LoadAssetAtPath<Material>(bodyPath);
            BuildPrefab(prefabPath, model, controller, defaultBody, body);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Robot prefab'ları parçalı modelle yeniden kuruldu.");
    }

    static void RemapMaterials(Material body)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Govde"), body);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Eklem"), AssetDatabase.LoadAssetAtPath<Material>(JointMaterialPath));
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Goz"), GetEyeMaterial());
        importer.SaveAndReimport();
    }

    static AnimationClip CopyClip(string name, string targetPath)
    {
        AnimationClip source = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is AnimationClip clip && clip.name == name) source = clip;
        }
        if (source == null) throw new System.Exception($"{ModelPath} içinde '{name}' klibi yok.");

        var copy = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(targetPath), frameRate = source.frameRate };
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            if (binding.path == "" || binding.propertyName.StartsWith("m_LocalScale")) continue;
            var target = binding;
            target.path = ModelName + "/" + binding.path;
            AnimationUtility.SetEditorCurve(copy, target, AnimationUtility.GetEditorCurve(source, binding));
        }
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);

        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(copy, existing);
            return existing;
        }
        AssetDatabase.CreateAsset(copy, targetPath);
        return copy;
    }

    // "run" > 0.1 ise koşu (CharMovement ileri girdiyi yazar)
    static AnimatorController BuildController(AnimationClip idle, AnimationClip run)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("run", AnimatorControllerParameterType.Float);
        var machine = controller.layers[0].stateMachine;
        var idleState = machine.AddState("idle");
        idleState.motion = idle;
        var runState = machine.AddState("run");
        runState.motion = run;
        machine.defaultState = idleState;

        var toRun = idleState.AddTransition(runState);
        toRun.hasExitTime = false;
        toRun.duration = 0.05f;
        toRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "run");
        var toIdle = runState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.25f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "run");
        return controller;
    }

    static Material GetEyeMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(EyeMaterialPath);
        if (material != null) return material;

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.3f, 0.03f, 0.01f));
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", new Color(1f, 0.15f, 0.05f) * 4f);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        AssetDatabase.CreateAsset(material, EyeMaterialPath);
        return material;
    }

    static void BuildPrefab(string prefabPath, GameObject model, RuntimeAnimatorController controller, Material defaultBody, Material body)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            foreach (string childName in OldModelChildren)
            {
                var child = root.transform.Find(childName);
                if (child != null) Object.DestroyImmediate(child.gameObject);
            }
            foreach (var oldCollider in root.GetComponents<Collider>()) Object.DestroyImmediate(oldCollider);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = ModelName;
            float scale = 1f / root.transform.localScale.x;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localScale = Vector3.one * scale;

            // Gövde rengi prefab'a göre (robo1 gri, robo2 sarı)
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == defaultBody) materials[i] = body;
                }
                renderer.sharedMaterials = materials;
            }

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.height = CapsuleHeight * scale;
            capsule.radius = CapsuleRadius * scale;
            capsule.center = new Vector3(0f, CapsuleHeight * 0.5f * scale, 0f);

            var animator = root.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = null;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
