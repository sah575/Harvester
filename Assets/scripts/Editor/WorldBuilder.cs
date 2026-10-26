using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// world sahnesindeki çiti ve oyun içi HUD'u kurar.
// Menü: Harvester > Rebuild Field Fence / Rebuild Game HUD
public static class WorldBuilder
{
    const float PostSpacing = 4f;
    const float PostHeight = 2.6f;
    const float PostWidth = 0.3f;
    const float RailHeight = 0.22f;
    const float RailDepth = 0.1f;
    static readonly float[] RailHeights = { 1.0f, 2.1f };
    const float WallHeight = 30f;
    const float WallThickness = 2f;
    const string FenceMaterialPath = "Assets/Metarials/FenceWood.mat";

    [MenuItem("Harvester/Rebuild Field Fence")]
    public static void BuildFence()
    {
        var field = Object.FindFirstObjectByType<FieldBounds>();
        if (field == null)
        {
            Debug.LogError("Sahnede FieldBounds yok. Önce world sahnesini aç.");
            return;
        }

        var existing = field.transform.Find("Fence");
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        var fence = new GameObject("Fence");
        Undo.RegisterCreatedObjectUndo(fence, "Build Fence");
        fence.transform.SetParent(field.transform, false);

        var visuals = new GameObject("Visuals").transform;
        visuals.SetParent(fence.transform, false);
        var walls = new GameObject("Walls").transform;
        walls.SetParent(fence.transform, false);

        var material = GetFenceMaterial();
        float halfX = field.Size.x * 0.5f;
        float halfZ = field.Size.y * 0.5f;

        // Köşeler saat yönünde, her kenar bir sonraki köşeye
        var corners = new[]
        {
            new Vector3(-halfX, 0f, -halfZ),
            new Vector3(-halfX, 0f, halfZ),
            new Vector3(halfX, 0f, halfZ),
            new Vector3(halfX, 0f, -halfZ),
        };

        for (int side = 0; side < 4; side++)
        {
            Vector3 from = corners[side];
            Vector3 to = corners[(side + 1) % 4];
            float length = Vector3.Distance(from, to);
            int segments = Mathf.CeilToInt(length / PostSpacing);
            Vector3 dir = (to - from).normalized;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up));

            for (int i = 0; i < segments; i++)
            {
                Vector3 a = Vector3.Lerp(from, to, (float)i / segments);
                Vector3 b = Vector3.Lerp(from, to, (float)(i + 1) / segments);

                // Köşe direkleri bir kez konsun diye her kenar kendi başlangıç direğini koyar
                CreateBlock("Post", visuals, a + Vector3.up * PostHeight * 0.5f, rotation,
                    new Vector3(PostWidth, PostHeight, PostWidth), material);

                foreach (float h in RailHeights)
                {
                    CreateBlock("Rail", visuals, (a + b) * 0.5f + Vector3.up * h, rotation,
                        new Vector3(Vector3.Distance(a, b), RailHeight, RailDepth), material);
                }
            }

            // Görünmez duvar: çit çizgisinin hemen dışında, Harvester bile aşamasın
            Vector3 outward = Vector3.Cross(dir, Vector3.up);
            var wall = new GameObject("Wall_" + side);
            wall.transform.SetParent(walls, false);
            wall.transform.localPosition = (from + to) * 0.5f + outward * WallThickness * 0.5f + Vector3.up * WallHeight * 0.5f;
            wall.transform.localRotation = rotation;
            var box = wall.AddComponent<BoxCollider>();
            box.size = new Vector3(length + WallThickness * 2f, WallHeight, WallThickness);
        }

        GameObjectUtility.SetStaticEditorFlags(visuals.gameObject, StaticEditorFlags.BatchingStatic);
        EditorSceneManager.MarkSceneDirty(field.gameObject.scene);
        Debug.Log($"Çit kuruldu: {visuals.childCount} parça, 4 duvar.");
    }

    static void CreateBlock(string name, Transform parent, Vector3 localPosition, Quaternion rotation, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<BoxCollider>()); // Çarpışmayı duvarlar hallediyor
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    static Material GetFenceMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(FenceMaterialPath);
        if (material != null) return material;

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.24f, 0.17f, 0.11f));
        material.SetFloat("_Smoothness", 0.1f);
        AssetDatabase.CreateAsset(material, FenceMaterialPath);
        return material;
    }

    [MenuItem("Harvester/Rebuild Game HUD")]
    public static void BuildHud()
    {
        var existing = Object.FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        var go = new GameObject("GameHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(go, "Build HUD");
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var timer = CreateText("Timer", go.transform, 56, FontStyles.Bold, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(400, 80), TextAlignmentOptions.Top);
        var role = CreateText("Role", go.transform, 34, FontStyles.Bold, new Vector2(0f, 1f), new Vector2(40f, -36f), new Vector2(500, 60), TextAlignmentOptions.TopLeft);
        var robots = CreateText("Robots", go.transform, 30, FontStyles.Normal, new Vector2(1f, 1f), new Vector2(-40f, -38f), new Vector2(500, 60), TextAlignmentOptions.TopRight);
        var center = CreateText("Center", go.transform, 110, FontStyles.Bold, new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(1600, 160), TextAlignmentOptions.Center);
        var sub = CreateText("Sub", go.transform, 32, FontStyles.Normal, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1400, 140), TextAlignmentOptions.Top);

        // Stamina barı: alt orta, sadece robotlara görünür
        var stamina = new GameObject("Stamina", typeof(RectTransform), typeof(Image));
        stamina.transform.SetParent(go.transform, false);
        var staminaRect = (RectTransform)stamina.transform;
        staminaRect.anchorMin = staminaRect.anchorMax = staminaRect.pivot = new Vector2(0.5f, 0f);
        staminaRect.anchoredPosition = new Vector2(0f, 60f);
        staminaRect.sizeDelta = new Vector2(420f, 14f);
        var staminaBack = stamina.GetComponent<Image>();
        staminaBack.color = new Color(0f, 0f, 0f, 0.55f);
        staminaBack.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(stamina.transform, false);
        var fillRect = (RectTransform)fill.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero; // Kenar boşluğu olsa stamina 0'da genişlik eksiye düşerdi
        var fillImage = fill.GetComponent<Image>();
        fillImage.raycastTarget = false;
        stamina.SetActive(false);

        var hud = go.AddComponent<GameHUD>();
        var so = new SerializedObject(hud);
        so.FindProperty("timerText").objectReferenceValue = timer;
        so.FindProperty("roleText").objectReferenceValue = role;
        so.FindProperty("robotsText").objectReferenceValue = robots;
        so.FindProperty("centerText").objectReferenceValue = center;
        so.FindProperty("subText").objectReferenceValue = sub;
        so.FindProperty("staminaRoot").objectReferenceValue = stamina;
        so.FindProperty("staminaFill").objectReferenceValue = fillRect;
        so.FindProperty("staminaFillImage").objectReferenceValue = fillImage;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("Game HUD kuruldu.");
    }

    static TMP_Text CreateText(string name, Transform parent, float size, FontStyles style, Vector2 anchor, Vector2 position, Vector2 dimensions, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        var text = go.GetComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = new Color(0.92f, 0.92f, 0.9f);
        text.richText = true;
        text.raycastTarget = false;
        return text;
    }
}
