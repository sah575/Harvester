using Harvester.Net;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Lobby sahnesindeki arayüzü sıfırdan kurar ve lobby script'ine bağlar.
// Menü: Harvester > Rebuild Lobby UI
public static class LobbyUIBuilder
{
    static readonly Color BackgroundColor = new Color(0.022f, 0.029f, 0.027f, 1f);
    static readonly Color PanelColor = new Color(0.045f, 0.055f, 0.052f, 0.98f);
    static readonly Color SlotColor = new Color(0.11f, 0.12f, 0.15f, 1f);
    static readonly Color ButtonColor = new Color(0.16f, 0.17f, 0.2f, 1f);
    static readonly Color AccentColor = new Color(0.75f, 0.42f, 0.16f, 1f);
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
        var menu = CreatePanel("MenuPanel", root, new Vector2(800, 0));
        CreateText("Title", menu, "BAĞLANTI", 48, FontStyles.Bold, AccentColor, 64);
        CreateText("Subtitle", menu, "Yarış. Arkandaki biçerdövere yakalanma.", 24, FontStyles.Normal, MutedTextColor, 36);
        CreateSpacer(menu, 12);
        var tabRow = CreateRow(menu, 52);
        var onlineTab = CreateTab(tabRow, resources, "ÇEVRİMİÇİ");
        var lanTab = CreateTab(tabRow, resources, "LAN");
        CreateSpacer(menu, 4);
        CreateText("NameLabel", menu, "İsim", 22, FontStyles.Normal, MutedTextColor, 30);
        var nameInput = CreateInput(menu, resources, "İsmini gir...", TMP_InputField.ContentType.Standard);
        nameInput.characterLimit = 16;

        // Online: davet koduyla relay üzerinden
        var onlineFields = CreateGroup(menu, "OnlineFields");
        CreateText("CodeLabel", onlineFields, "Davet kodu (katılırken)", 22, FontStyles.Normal, MutedTextColor, 30);
        var codeInput = CreateInput(onlineFields, resources, "ör. K7M2QX", TMP_InputField.ContentType.Standard);
        codeInput.characterLimit = RelayProtocol.CodeLength + 2;
        CreateText("OnlineHint", onlineFields, "Oda kuran bir kod alır; arkadaşların o kodla katılır.", 18, FontStyles.Italic, MutedTextColor, 26);

        // LAN: aynı ağda IP ile
        var lanFields = CreateGroup(menu, "LanFields");
        CreateText("PasswordLabel", lanFields, "Oda şifresi", 22, FontStyles.Normal, MutedTextColor, 30);
        var passwordInput = CreateInput(lanFields, resources, "Şifre (boş olabilir)", TMP_InputField.ContentType.Password);
        CreateText("AddressLabel", lanFields, "Adres (katılırken)", 22, FontStyles.Normal, MutedTextColor, 30);
        var addressInput = CreateInput(lanFields, resources, "127.0.0.1", TMP_InputField.ContentType.Standard);
        addressInput.text = "127.0.0.1";
        lanFields.gameObject.SetActive(false);
        CreateSpacer(menu, 16);
        var hostButton = CreateButton(menu, resources, "ODA KUR", AccentColor);
        var joinButton = CreateButton(menu, resources, "ODAYA KATIL", ButtonColor);
        var menuStatus = CreateText("Status", menu, "", 22, FontStyles.Normal, new Color(0.95f, 0.55f, 0.45f), 60);

        // --- Oda ---
        var room = CreatePanel("RoomPanel", root, new Vector2(1000, 0));
        CreateText("Title", room, "YARIŞ ODASI", 42, FontStyles.Bold, TextColor, 55);
        CreateText("Subtitle", room, "2–4 yarışçı. Biçerdöveri yapay zekâ sürüyor, geride kalanı biçer.", 22, FontStyles.Normal, MutedTextColor, 34);
        CreateSpacer(room, 6);
        var codeRow = CreateRow(room, 60);
        codeRow.name = "CodeRow";
        codeRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        var roomCodeText = CreateText("CodeText", codeRow, "Davet kodu alınıyor...", 34, FontStyles.Normal, TextColor, 60);
        roomCodeText.alignment = TextAlignmentOptions.MidlineLeft;
        roomCodeText.GetComponent<LayoutElement>().flexibleWidth = 1;
        var copyButton = CreateButton(codeRow, resources, "KOPYALA", ButtonColor);
        copyButton.GetComponent<LayoutElement>().preferredWidth = 180;
        CreateSpacer(room, 6);
        var slots = new TMP_Text[lobby.MaxPlayers];
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = CreateImage("Slot" + (i + 1), room, SlotColor);
            SetHeight(slot.transform, 52);
            var text = CreateText("Text", slot.transform, "— boş —", 28, FontStyles.Normal, TextColor, 64);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            var textRect = (RectTransform)text.transform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(24, 0);
            textRect.offsetMax = new Vector2(-24, 0);
            slots[i] = text;
        }
        CreateSpacer(room, 12);
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
        so.FindProperty("startButton").objectReferenceValue = startButton;
        so.FindProperty("leaveButton").objectReferenceValue = leaveButton;
        so.FindProperty("roomStatusText").objectReferenceValue = roomStatus;
        so.FindProperty("onlineTabButton").objectReferenceValue = onlineTab;
        so.FindProperty("lanTabButton").objectReferenceValue = lanTab;
        so.FindProperty("onlineFields").objectReferenceValue = onlineFields.gameObject;
        so.FindProperty("lanFields").objectReferenceValue = lanFields.gameObject;
        so.FindProperty("codeInput").objectReferenceValue = codeInput;
        so.FindProperty("roomCodeRow").objectReferenceValue = codeRow.gameObject;
        so.FindProperty("roomCodeText").objectReferenceValue = roomCodeText;
        so.FindProperty("copyCodeButton").objectReferenceValue = copyButton;
        so.ApplyModifiedProperties();

        // Online oyun için NetworkManager'da relay transport'u da bulunsun (LAN için UnityTransport kalır).
        var nm = Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (nm != null && nm.GetComponent<RelayTransport>() == null)
        {
            Undo.AddComponent<RelayTransport>(nm.gameObject);
            Debug.Log("NetworkManager'a RelayTransport eklendi.");
        }
        // Ping göstergesi ve yapay gecikme kısayolları
        if (nm != null && nm.GetComponent<NetStatsHUD>() == null)
        {
            Undo.AddComponent<NetStatsHUD>(nm.gameObject);
            Debug.Log("NetworkManager'a NetStatsHUD eklendi.");
        }

        LobbyMenuBuilder.Build(lobbyComponent);
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
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Yükseklik içeriğe göre (Online/LAN alanları farklı uzunlukta)
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rect;
    }

    static RectTransform CreateGroup(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return (RectTransform)go.transform;
    }

    // Seçili sekme pasif (interactable=false) olur ve vurgu renginde görünür.
    static Button CreateTab(Transform parent, TMP_DefaultControls.Resources resources, string label)
    {
        var button = CreateButton(parent, resources, label, ButtonColor);
        var colors = button.colors;
        colors.disabledColor = AccentColor;
        button.colors = colors;
        return button;
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
        ((TMP_Text)input.placeholder).color = MutedTextColor;
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
