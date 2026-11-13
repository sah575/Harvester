using System;
using System.Collections;
using System.Collections.Generic;
using Harvester.Net;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Lobideki bir oyuncunun herkese senkron edilen bilgisi
public struct LobbyPlayer : INetworkSerializable, IEquatable<LobbyPlayer>
{
    public ulong ClientId;
    public FixedString32Bytes Name;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Name);
    }

    public bool Equals(LobbyPlayer other)
    {
        return ClientId == other.ClientId && Name.Equals(other.Name);
    }
}

public class lobby : NetworkBehaviour
{
    public const int MaxPlayers = 4;
    // Editörde ve development build'de tek başına girip test edilebilsin
    public static int MinPlayers => Debug.isDebugBuild ? 1 : 2;
    const int MaxNameLength = 16;
    const ushort Port = 7777;

    [Header("Ana menü")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private TMP_InputField nameInput, passwordInput, addressInput;
    [SerializeField] private Button hostButton, joinButton;
    [SerializeField] private TMP_Text menuStatusText;

    [Header("Online / LAN")]
    [SerializeField] private Button onlineTabButton, lanTabButton;
    [SerializeField] private GameObject onlineFields, lanFields;
    [SerializeField] private TMP_InputField codeInput;

    [Header("Oda")]
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private TMP_Text[] slotTexts;
    [SerializeField] private Button startButton, leaveButton;
    [SerializeField] private TMP_Text roomStatusText;
    [SerializeField] private GameObject roomCodeRow;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private Button copyCodeButton;

    // Herkes yarışçı, harvester'ı sunucudaki yapay zekâ sürer
    const string RaceSceneName = "race";

    // Lobi sahnesi açılınca menüde gösterilecek mesaj (ör. host bağlantısı koptu)
    public static string PendingStatus;

    // Online: VDS'teki relay üzerinden davet koduyla. LAN: aynı ağda IP ile (UnityTransport).
    const string OnlinePrefKey = "Harvester.OnlineMode";
    const string NamePrefKey = "Harvester.PlayerName";
    private static bool OnlineMode
    {
        get => PlayerPrefs.GetInt(OnlinePrefKey, 1) == 1;
        set => PlayerPrefs.SetInt(OnlinePrefKey, value ? 1 : 0);
    }

    private readonly NetworkList<LobbyPlayer> players = new NetworkList<LobbyPlayer>();
    private readonly Dictionary<ulong, string> pendingNames = new Dictionary<ulong, string>();
    private string hostPassword;
    private bool gameStarted;

    private void Awake()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        startButton.onClick.AddListener(StartGame);
        leaveButton.onClick.AddListener(Leave);
        onlineTabButton.onClick.AddListener(() => SetMode(true));
        lanTabButton.onClick.AddListener(() => SetMode(false));
        copyCodeButton.onClick.AddListener(CopyRoomCode);

        // Kod büyük harfle görünsün; boşluk ve tire kabul edilir, sunucu onları atar.
        codeInput.onValidateInput = (text, index, c) => char.ToUpperInvariant(c);
        nameInput.text = PlayerPrefs.GetString(NamePrefKey, "");
        SetMode(OnlineMode);

        // Oyundan dönünce imleç kilitli kalmasın
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ShowMenu(PendingStatus ?? "");
        PendingStatus = null;
    }

    // Development build'de komut satırından otomatik test (editörde çalışmaz):
    //   -autoHost [-autoStart N] [-autoRounds R]                     oda kur, N oyuncu olunca başlat, R tur oyna
    //   -autoJoin <adres>                                            odaya bağlan
    //   -autoOnline                                                  LAN yerine relay kullan. Host davet kodunu
    //                                                                AutoTestCodePath dosyasına yazar; -autoJoin
    //                                                                adresi "kod" ise istemci kodu oradan okur,
    //                                                                değilse verilen değeri kod olarak kullanır
    //   -relayAddress <ip> -relayPort <port>                         başka bir relay sunucusu (ör. yerel test)
    private static bool autoTestStarted;
    private static int autoStartPlayers, autoRoundsLeft;

    private void Start()
    {
        if (!Debug.isDebugBuild || Application.isEditor) return;

        // Tur sonunda lobi yeniden yüklendi: oda zaten açık, sıradaki turu başlat
        if (autoTestStarted)
        {
            if (autoRoundsLeft > 0) StartCoroutine(AutoStartWhenReady());
            return;
        }

        var args = Environment.GetCommandLineArgs();
        bool host = Array.IndexOf(args, "-autoHost") >= 0;
        int join = Array.IndexOf(args, "-autoJoin");
        if (!host && join < 0) return;

        autoTestStarted = true;
        // Yarıştaki test bayraklarını (-autoDrive, -autoTrace, -ntBuffer...) RaceAutoTest okur
        bool online = Array.IndexOf(args, "-autoOnline") >= 0;
        SetMode(online);
        int relayAddress = Array.IndexOf(args, "-relayAddress");
        if (relayAddress >= 0 && relayAddress + 1 < args.Length) Relay.Address = args[relayAddress + 1];
        Relay.Port = (ushort)IntArg(args, "-relayPort", Relay.Port);
        autoStartPlayers = host ? IntArg(args, "-autoStart", 0) : 0;
        autoRoundsLeft = autoStartPlayers > 0 ? IntArg(args, "-autoRounds", 1) : 0;
        StartCoroutine(AutoTest(host, join >= 0 && join + 1 < args.Length ? args[join + 1] : "127.0.0.1"));
    }

    private static int IntArg(string[] args, string name, int fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int value) ? value : fallback;
    }

    private static string AutoTestCodePath => System.IO.Path.Combine(Application.persistentDataPath, "autotest-code.txt");

    private IEnumerator AutoTest(bool host, string address)
    {
        nameInput.text = host ? "TestHost" : "TestClient";
        if (OnlineMode && host)
        {
            try { System.IO.File.Delete(AutoTestCodePath); } catch (Exception) { }
            OnHostClicked();
            while (string.IsNullOrEmpty(Relay.RoomCode)) yield return null;
            System.IO.File.WriteAllText(AutoTestCodePath, Relay.RoomCode);
            Debug.Log("[AutoTest] Davet kodu: " + Relay.RoomCode);
        }
        else if (OnlineMode)
        {
            if (address == "kod")
            {
                float giveUp = Time.realtimeSinceStartup + 30f;
                while (!System.IO.File.Exists(AutoTestCodePath) && Time.realtimeSinceStartup < giveUp) yield return null;
                address = System.IO.File.Exists(AutoTestCodePath) ? System.IO.File.ReadAllText(AutoTestCodePath).Trim() : "";
            }
            Debug.Log("[AutoTest] Kodla katılınıyor: " + address);
            codeInput.text = address;
            OnJoinClicked();
        }
        else
        {
            addressInput.text = address;
            if (host) OnHostClicked();
            else OnJoinClicked();
        }

        while (!IsSpawned) yield return null;
        if (autoRoundsLeft > 0) yield return AutoStartWhenReady();
    }

    private IEnumerator AutoStartWhenReady()
    {
        while (!IsSpawned || !CanStart(out _) || players.Count < autoStartPlayers) yield return null;
        yield return new WaitForSeconds(1f);
        autoRoundsLeft--;
        Debug.Log($"[AutoTest] Oyun başlatılıyor, oyuncu: {players.Count}, kalan tur: {autoRoundsLeft}");
        StartGame();
    }

    public override void OnDestroy()
    {
        UnsubscribeCallbacks();
        base.OnDestroy();
    }

    #region Menü

    private static RelayTransport Relay => NetworkManager.Singleton.GetComponent<RelayTransport>();

    private void OnHostClicked()
    {
        var nm = NetworkManager.Singleton;
        // Online odada şifre yok: davet kodu zaten şifre işlevi görüyor.
        hostPassword = OnlineMode ? "" : passwordInput.text;
        gameStarted = false;
        pendingNames.Clear();
        MatchSettings.Players.Clear();
        MatchSettings.RoomPassword = hostPassword;

        if (OnlineMode)
        {
            Relay.MaxPlayers = MaxPlayers;
            nm.NetworkConfig.NetworkTransport = Relay;
        }
        else
        {
            var utp = nm.GetComponent<UnityTransport>();
            // 0.0.0.0'ı dinle ki aynı ağdaki diğer bilgisayarlar da bağlanabilsin
            utp.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            nm.NetworkConfig.NetworkTransport = utp;
        }
        nm.NetworkConfig.ConnectionData = BuildPayload(hostPassword);
        SubscribeCallbacks();
        nm.ConnectionApprovalCallback = ApprovalCheck;

        if (!nm.StartHost())
        {
            UnsubscribeCallbacks();
            ShowMenu(OnlineMode ? Relay.LastErrorMessage ?? "Oda kurulamadı." : "Oda kurulamadı. Port " + Port + " kullanımda olabilir.");
            return;
        }
        SavePlayerName();
        SetMenuInteractable(false);
    }

    private void OnJoinClicked()
    {
        var nm = NetworkManager.Singleton;

        if (OnlineMode)
        {
            Relay.JoinCode = codeInput.text;
            nm.NetworkConfig.NetworkTransport = Relay;
            nm.NetworkConfig.ConnectionData = BuildPayload("");
        }
        else
        {
            string address = string.IsNullOrWhiteSpace(addressInput.text) ? "127.0.0.1" : addressInput.text.Trim();
            var utp = nm.GetComponent<UnityTransport>();
            utp.SetConnectionData(address, Port);
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.ConnectionData = BuildPayload(passwordInput.text);
        }
        SubscribeCallbacks();

        if (!nm.StartClient())
        {
            UnsubscribeCallbacks();
            ShowMenu(OnlineMode ? Relay.LastErrorMessage ?? "Bağlanılamadı." : "Bağlanılamadı.");
            return;
        }
        SavePlayerName();
        SetMenuInteractable(false);
        menuStatusText.text = "Bağlanıyor...";
    }

    // Bağlantı paketi: "şifre\nisim"
    private byte[] BuildPayload(string password)
    {
        string playerName = nameInput.text.Trim();
        if (string.IsNullOrEmpty(playerName)) playerName = "Oyuncu";
        if (playerName.Length > MaxNameLength) playerName = playerName.Substring(0, MaxNameLength);
        return System.Text.Encoding.UTF8.GetBytes(password + "\n" + playerName);
    }

    private void SavePlayerName()
    {
        PlayerPrefs.SetString(NamePrefKey, nameInput.text.Trim());
        PlayerPrefs.Save();
    }

    private void SetMode(bool online)
    {
        // Bağlanırken mod değişmesin
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening) return;

        OnlineMode = online;
        onlineFields.SetActive(online);
        lanFields.SetActive(!online);
        // Seçili sekme basılı (pasif) görünür
        onlineTabButton.interactable = !online;
        lanTabButton.interactable = online;
        hostButton.GetComponentInChildren<TMP_Text>().text = online ? "ODA KUR" : "ODA KUR (LAN)";
        joinButton.GetComponentInChildren<TMP_Text>().text = online ? "KODLA KATIL" : "ODAYA KATIL (LAN)";
        if (menuPanel.activeSelf) menuStatusText.text = "";
    }

    private void CopyRoomCode()
    {
        var relay = NetworkManager.Singleton.NetworkConfig.NetworkTransport as RelayTransport;
        if (relay == null || string.IsNullOrEmpty(relay.RoomCode)) return;
        GUIUtility.systemCopyBuffer = relay.RoomCode;
        roomStatusText.text = "Davet kodu kopyalandı: " + relay.RoomCode;
    }

    /// <summary>
    /// Bağlantı kopunca oyuncuya gösterilecek sebep: önce host'un yolladığı sebep (ör. "Oda dolu."),
    /// sonra relay'in sebebi (ör. "Bu kodla bir oda bulunamadı."), yoksa verilen genel mesaj.
    /// </summary>
    public static string DescribeDisconnect(string fallback)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return fallback;
        string reason = nm.DisconnectReason;
        // NGO'nun kendi ürettiği teknik metin "[Disconnect Event]..." ile başlar; onu gösterme.
        if (!string.IsNullOrEmpty(reason) && !reason.StartsWith("[")) return reason;
        var relay = nm.NetworkConfig.NetworkTransport as RelayTransport;
        if (relay != null && !string.IsNullOrEmpty(relay.LastErrorMessage)) return relay.LastErrorMessage;
        return fallback;
    }

    private void Leave()
    {
        NetworkManager.Singleton.Shutdown();
        UnsubscribeCallbacks();
        ShowMenu("");
    }

    private void ShowMenu(string status)
    {
        menuPanel.SetActive(true);
        roomPanel.SetActive(false);
        SetMenuInteractable(true);
        menuStatusText.text = status;
        var menus = GetComponent<Harvester.UI.LobbyMenus>();
        if (menus != null) menus.ShowMain(status);
    }

    private void SetMenuInteractable(bool value)
    {
        nameInput.interactable = value;
        passwordInput.interactable = value;
        addressInput.interactable = value;
        codeInput.interactable = value;
        hostButton.interactable = value;
        joinButton.interactable = value;
    }

    #endregion

    #region Ağ olayları

    private void SubscribeCallbacks()
    {
        UnsubscribeCallbacks();
        var nm = NetworkManager.Singleton;
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
        nm.OnTransportFailure += OnTransportFailure;
        var relay = nm.GetComponent<RelayTransport>();
        if (relay != null) relay.RoomReady += OnRoomReady;
    }

    private void UnsubscribeCallbacks()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
        nm.OnTransportFailure -= OnTransportFailure;
        var relay = nm.GetComponent<RelayTransport>();
        if (relay != null) relay.RoomReady -= OnRoomReady;
    }

    private void OnRoomReady(string code)
    {
        RefreshRoom();
    }

    // Host'un relay bağlantısı koptu (ya da hiç kurulamadı): NGO kendini kapatıyor, menüye dön.
    private void OnTransportFailure()
    {
        UnsubscribeCallbacks();
        ShowMenu(DescribeDisconnect("Sunucuyla bağlantı koptu."));
    }

    public override void OnNetworkSpawn()
    {
        players.OnListChanged += OnPlayersChanged;
        SubscribeCallbacks();

        if (IsServer)
        {
            // Tur sonunda lobiye dönülünce bu yeni bir lobby objesi; oda ayarlarını geri yükle
            hostPassword = MatchSettings.RoomPassword;
            gameStarted = false;
            NetworkManager.ConnectionApprovalCallback = ApprovalCheck;

            players.Clear();
            foreach (var id in NetworkManager.ConnectedClientsIds) AddPlayer(id);
        }

        menuPanel.SetActive(false);
        roomPanel.SetActive(true);
        var menus = GetComponent<Harvester.UI.LobbyMenus>();
        if (menus != null) menus.ShowRoom();
        RefreshRoom();
        if (Debug.isDebugBuild) Debug.Log("[Lobby] Oda açıldı: kurucu=" + IsServer + " bağlı=" + NetworkManager.ConnectedClientsIds.Count);
    }

    public override void OnNetworkDespawn()
    {
        players.OnListChanged -= OnPlayersChanged;
    }

    private void OnPlayersChanged(NetworkListEvent<LobbyPlayer> change)
    {
        RefreshRoom();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer && IsSpawned) AddPlayer(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm.IsServer)
        {
            if (clientId == NetworkManager.ServerClientId) return;
            int index = IndexOf(clientId);
            if (index >= 0) players.RemoveAt(index);
            pendingNames.Remove(clientId);
            return;
        }

        // Biz düştük (reddedildik ya da host kapattı)
        UnsubscribeCallbacks();
        ShowMenu(DescribeDisconnect("Bağlantı koptu."));
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        string payload = System.Text.Encoding.UTF8.GetString(request.Payload);
        int split = payload.IndexOf('\n');
        string password = split >= 0 ? payload.Substring(0, split) : payload;
        string playerName = split >= 0 ? payload.Substring(split + 1) : "Oyuncu";
        if (playerName.Length > MaxNameLength) playerName = playerName.Substring(0, MaxNameLength);

        response.CreatePlayerObject = false; // Arabaları race sahnesinde RaceSpawner oluşturuyor

        if (gameStarted)
        {
            response.Approved = false;
            response.Reason = "Oyun zaten başladı.";
        }
        else if (NetworkManager.Singleton.ConnectedClientsIds.Count >= MaxPlayers)
        {
            response.Approved = false;
            response.Reason = "Oda dolu.";
        }
        else if (password != hostPassword)
        {
            response.Approved = false;
            response.Reason = "Yanlış oda şifresi.";
        }
        else
        {
            response.Approved = true;
            pendingNames[request.ClientNetworkId] = playerName;
        }
    }

    private void AddPlayer(ulong clientId)
    {
        if (IndexOf(clientId) >= 0) return;

        // Önceki turdan dönen oyuncunun ismi korunur
        MatchSettings.Players.TryGetValue(clientId, out var previous);
        if (!pendingNames.TryGetValue(clientId, out string playerName)) playerName = previous.Name;

        players.Add(new LobbyPlayer
        {
            ClientId = clientId,
            Name = new FixedString32Bytes(string.IsNullOrEmpty(playerName) ? "Oyuncu" : playerName),
        });
    }

    #endregion

    #region Başlatma

    private void StartGame()
    {
        if (!IsServer || gameStarted || !CanStart(out _)) return;

        MatchSettings.Players.Clear();
        foreach (var p in players)
        {
            MatchSettings.Players[p.ClientId] = new MatchSettings.PlayerInfo { Name = p.Name.ToString() };
        }

        gameStarted = true;
        NetworkManager.SceneManager.LoadScene(RaceSceneName, LoadSceneMode.Single);
    }

    private bool CanStart(out string reason)
    {
        if (players.Count < MinPlayers)
        {
            reason = "Başlamak için en az " + MinPlayers + " oyuncu gerekli.";
            return false;
        }
        reason = "";
        return true;
    }

    private int IndexOf(ulong clientId)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].ClientId == clientId) return i;
        }
        return -1;
    }

    #endregion

    private void RefreshRoom()
    {
        if (!IsSpawned) return;

        ulong localId = NetworkManager.LocalClientId;

        for (int i = 0; i < slotTexts.Length; i++)
        {
            if (i >= players.Count)
            {
                slotTexts[i].text = "<color=#555>— boş —</color>";
                continue;
            }

            var p = players[i];
            bool isLocal = p.ClientId == localId;
            const string role = "<color=#8FB3D9>Yarışçı</color>";
            string tags = (p.ClientId == NetworkManager.ServerClientId ? "  <color=#C9A227>[kurucu]</color>" : "")
                        + (isLocal ? "  <color=#AAA>(sen)</color>" : "");
            slotTexts[i].text = p.Name + tags + "<pos=70%>" + role;
        }

        var relay = NetworkManager.NetworkConfig.NetworkTransport as RelayTransport;
        roomCodeRow.SetActive(relay != null);
        if (relay != null)
        {
            bool hasCode = !string.IsNullOrEmpty(relay.RoomCode);
            roomCodeText.text = hasCode ? "Davet kodu: <b><color=#E8C547>" + relay.RoomCode + "</color></b>" : "Davet kodu alınıyor...";
            copyCodeButton.interactable = hasCode;
        }

        startButton.gameObject.SetActive(IsServer);
        bool canStart = CanStart(out string reason);
        startButton.interactable = canStart;

        if (!canStart) roomStatusText.text = reason;
        else roomStatusText.text = IsServer ? "Hazır! Oyunu başlatabilirsin." : "Oda kurucusunun oyunu başlatması bekleniyor...";
    }
}
