using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Lobby sahnesindeki arayüzü sıfırdan kurar ve lobby script'ine bağlar.
// Menü: Harvester > Rebuild Lobby UI
public static class LobbyUIBuilder
{
    static readonly Color BackgroundColor = new Color(0.03f, 0.035f, 0.05f, 1f);
    static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.1f, 0.96f);
    static readonly Color SlotColor = new Color(0.11f, 0.12f, 0.15f, 1f);
    static readonly Color ButtonColor = new Color(0.16f, 0.17f, 0.2f, 1f);
    static readonly Color AccentColor = new Color(0.62f, 0.12f, 0.1f, 1f);
    static readonly Color TextColor = new Color(0.9f, 0.9f, 0.88f, 1f);
    static readonly Color MutedTextColor = new Color(0.6f, 0.62f, 0.66f, 1f);

    [MenuItem("Harvester/Rebuild Lobby UI")]
    public static void Build()
    {
        var lobbyComponent = Object.FindFirstObjectByType<lobby>(FindObjectsInactive.Include);
        if (lobbyComponent == null)
        {
            Debug.LogError("Sahnede lobby bileşeni yok. Önce Lobby sahnesini aç.");
            return;
        }

        var canvas = lobbyComponent.GetComponentInParent<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var root = (RectTransform)lobbyComponent.transform;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        }
        Stretch(root);

        var resources = new TMP_DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
        };

        var background = CreateImage("Background", root, BackgroundColor);
        Stretch((RectTransform)background.transform);

        // --- Ana menü ---
        var menu = CreatePanel("MenuPanel", root, new Vector2(560, 720));
        CreateText("Title", menu, "HARVESTER", 72, FontStyles.Bold, AccentColor, 90);
        CreateText("Subtitle", menu, "1 Harvester  vs  3 Robot", 24, FontStyles.Normal, MutedTextColor, 36);
        CreateSpacer(menu, 20);
        CreateText("NameLabel", menu, "İsim", 22, FontStyles.Normal, MutedTextColor, 30);
        var nameInput = CreateInput(menu, resources, "İsmini gir...", TMP_InputField.ContentType.Standard);
        nameInput.characterLimit = 16;
        CreateText("PasswordLabel", menu, "Oda şifresi", 22, FontStyles.Normal, MutedTextColor, 30);
        var passwordInput = CreateInput(menu, resources, "Şifre (boş olabilir)", TMP_InputField.ContentType.Password);
        CreateText("AddressLabel", menu, "Adres (katılırken)", 22, FontStyles.Normal, MutedTextColor, 30);
        var addressInput = CreateInput(menu, resources, "127.0.0.1", TMP_InputField.ContentType.Standard);
        addressInput.text = "127.0.0.1";
        CreateSpacer(menu, 16);
        var hostButton = CreateButton(menu, resources, "ODA KUR", AccentColor);
        var joinButton = CreateButton(menu, resources, "ODAYA KATIL", ButtonColor);
        var menuStatus = CreateText("Status", menu, "", 22, FontStyles.Normal, new Color(0.95f, 0.55f, 0.45f), 60);

        // --- Oda ---
        var room = CreatePanel("RoomPanel", root, new Vector2(760, 720));
        CreateText("Title", room, "ODA", 56, FontStyles.Bold, TextColor, 72);
        CreateText("Subtitle", room, "1 kişi Harvester'ı sürer, diğerleri robot olarak kaçar", 22, FontStyles.Normal, MutedTextColor, 34);
        CreateSpacer(room, 12);
        var slots = new TMP_Text[lobby.MaxPlayers];
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = CreateImage("Slot" + (i + 1), room, SlotColor);
            SetHeight(slot.transform, 64);
            var text = CreateText("Text", slot.transform, "— boş —", 28, FontStyles.Normal, TextColor, 64);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            var textRect = (RectTransform)text.transform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(24, 0);
            textRect.offsetMax = new Vector2(-24, 0);
            slots[i] = text;
        }
        CreateSpacer(room, 12);
        var roleRow = CreateRow(room, 64);
        var harvesterButton = CreateButton(roleRow, resources, "HARVESTER OL", AccentColor);
        var robotButton = CreateButton(roleRow, resources, "ROBOT OL", ButtonColor);
        var startButton = CreateButton(room, resources, "OYUNU BAŞLAT", new Color(0.2f, 0.45f, 0.25f));
        var leaveButton = CreateButton(room, resources, "AYRIL", ButtonColor);
        var roomStatus = CreateText("Status", room, "", 22, FontStyles.Normal, MutedTextColor, 40);
        room.gameObject.SetActive(false);

        // lobby script'ine bağla
        var so = new SerializedObject(lobbyComponent);
        so.FindProperty("menuPanel").objectReferenceValue = menu.gameObject;
        so.FindProperty("nameInput").objectReferenceValue = nameInput;
        so.FindProperty("passwordInput").objectReferenceValue = passwordInput;
        so.FindProperty("addressInput").objectReferenceValue = addressInput;
        so.FindProperty("hostButton").objectReferenceValue = hostButton;
        so.FindProperty("joinButton").objectReferenceValue = joinButton;
        so.FindProperty("menuStatusText").objectReferenceValue = menuStatus;
        so.FindProperty("roomPanel").objectReferenceValue = room.gameObject;
        var slotsProp = so.FindProperty("slotTexts");
        slotsProp.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++) slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        so.FindProperty("harvesterButton").objectReferenceValue = harvesterButton;
        so.FindProperty("robotButton").objectReferenceValue = robotButton;
        so.FindProperty("startButton").objectReferenceValue = startButton;
        so.FindProperty("leaveButton").objectReferenceValue = leaveButton;
        so.FindProperty("roomStatusText").objectReferenceValue = roomStatus;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(lobbyComponent.gameObject.scene);
        Debug.Log("Lobby UI yeniden kuruldu.");
    }

    static RectTransform CreatePanel(string name, Transform parent, Vector2 size)
    {
        var panel = CreateImage(name, parent, PanelColor);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 36, 36);
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return rect;
    }

    static RectTransform CreateRow(Transform parent, float height)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        SetHeight(go.transform, height);
        return (RectTransform)go.transform;
    }

    static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    static TMP_Text CreateText(string name, Transform parent, string text, float size, FontStyles style, Color color, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.richText = true;
        SetHeight(go.transform, height);
        return tmp;
    }

    static TMP_InputField CreateInput(Transform parent, TMP_DefaultControls.Resources resources, string placeholder, TMP_InputField.ContentType contentType)
    {
        var go = TMP_DefaultControls.CreateInputField(resources);
        go.transform.SetParent(parent, false);
        var input = go.GetComponent<TMP_InputField>();
        input.contentType = contentType;
        go.GetComponent<Image>().color = new Color(0.14f, 0.15f, 0.18f);
        ((TMP_Text)input.placeholder).text = placeholder;
        ((TMP_Text)input.placeholder).fontSize = 24;
        input.textComponent.fontSize = 24;
        input.textComponent.color = TextColor;
        input.caretColor = TextColor;
        SetHeight(go.transform, 52);
        return input;
    }

    static Button CreateButton(Transform parent, TMP_DefaultControls.Resources resources, string label, Color color)
    {
        var go = TMP_DefaultControls.CreateButton(resources);
        go.name = label;
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Color.white;
        var button = go.GetComponent<Button>();
        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = color * 1.25f;
        colors.pressedColor = color * 0.8f;
        colors.selectedColor = color;
        colors.disabledColor = new Color(0.12f, 0.12f, 0.14f, 0.6f);
        button.colors = colors;
        var text = go.GetComponentInChildren<TMP_Text>();
        text.text = label;
        text.fontSize = 26;
        text.fontStyle = FontStyles.Bold;
        text.color = TextColor;
        SetHeight(go.transform, 60);
        return button;
    }

    static void CreateSpacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        SetHeight(go.transform, height);
    }

    static void SetHeight(Transform t, float height)
    {
        var element = t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
