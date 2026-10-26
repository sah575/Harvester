using System;
using System.Collections;
using System.Collections.Generic;
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
    public bool IsHarvester;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Name);
        serializer.SerializeValue(ref IsHarvester);
    }

    public bool Equals(LobbyPlayer other)
    {
        return ClientId == other.ClientId && Name.Equals(other.Name) && IsHarvester == other.IsHarvester;
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

    [Header("Oda")]
    [SerializeField] private GameObject roomPanel;
    [SerializeField] private TMP_Text[] slotTexts;
    [SerializeField] private Button harvesterButton, robotButton, startButton, leaveButton;
    [SerializeField] private TMP_Text roomStatusText;

    [SerializeField] private string gameSceneName = "world";

    // Lobi sahnesi açılınca menüde gösterilecek mesaj (ör. host bağlantısı koptu)
    public static string PendingStatus;

    private readonly NetworkList<LobbyPlayer> players = new NetworkList<LobbyPlayer>();
    private readonly Dictionary<ulong, string> pendingNames = new Dictionary<ulong, string>();
    private string hostPassword;
    private bool gameStarted;

    private void Awake()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        harvesterButton.onClick.AddListener(() => RequestRoleRpc(true));
        robotButton.onClick.AddListener(() => RequestRoleRpc(false));
        startButton.onClick.AddListener(StartGame);
        leaveButton.onClick.AddListener(Leave);

        // Oyundan dönünce imleç kilitli kalmasın
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ShowMenu(PendingStatus ?? "");
        PendingStatus = null;
    }

    // Development build'de komut satırından otomatik test (editörde çalışmaz):
    //   -autoHost [-autoHarvester] [-autoStart N] [-autoRounds R]   oda kur, istenirse Harvester ol,
    //                                                                N oyuncu olunca başlat, R tur oyna
    //   -autoJoin <adres> [-autoHarvester]                           odaya bağlan, istenirse Harvester'ı seç
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
        AutoTestReporter.Create();
        autoStartPlayers = host ? IntArg(args, "-autoStart", 0) : 0;
        autoRoundsLeft = autoStartPlayers > 0 ? IntArg(args, "-autoRounds", 1) : 0;
        StartCoroutine(AutoTest(host, join >= 0 && join + 1 < args.Length ? args[join + 1] : "127.0.0.1",
            Array.IndexOf(args, "-autoHarvester") >= 0));
    }

    private static int IntArg(string[] args, string name, int fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int value) ? value : fallback;
    }

    private IEnumerator AutoTest(bool host, string address, bool harvester)
    {
        nameInput.text = host ? "TestHost" : "TestClient";
        addressInput.text = address;
        if (host) OnHostClicked();
        else OnJoinClicked();

        while (!IsSpawned) yield return null;
        if (harvester) RequestRoleRpc(true);
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

    private void OnHostClicked()
    {
        var nm = NetworkManager.Singleton;
        hostPassword = passwordInput.text;
        gameStarted = false;
        pendingNames.Clear();
        MatchSettings.Players.Clear();
        MatchSettings.RoomPassword = hostPassword;

        // 0.0.0.0'ı dinle ki aynı ağdaki diğer bilgisayarlar da bağlanabilsin
        nm.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", Port, "0.0.0.0");
        nm.NetworkConfig.ConnectionData = BuildPayload();
        SubscribeCallbacks();
        nm.ConnectionApprovalCallback = ApprovalCheck;

        if (!nm.StartHost())
        {
            UnsubscribeCallbacks();
            ShowMenu("Oda kurulamadı. Port " + Port + " kullanımda olabilir.");
            return;
        }
        SetMenuInteractable(false);
    }

    private void OnJoinClicked()
    {
        var nm = NetworkManager.Singleton;
        string address = string.IsNullOrWhiteSpace(addressInput.text) ? "127.0.0.1" : addressInput.text.Trim();

        nm.GetComponent<UnityTransport>().SetConnectionData(address, Port);
        nm.NetworkConfig.ConnectionData = BuildPayload();
        SubscribeCallbacks();

        if (!nm.StartClient())
        {
            UnsubscribeCallbacks();
            ShowMenu("Bağlanılamadı.");
            return;
        }
        SetMenuInteractable(false);
        menuStatusText.text = "Bağlanıyor...";
    }

    // Bağlantı paketi: "şifre\nisim"
    private byte[] BuildPayload()
    {
        string playerName = nameInput.text.Trim();
        if (string.IsNullOrEmpty(playerName)) playerName = "Oyuncu";
        if (playerName.Length > MaxNameLength) playerName = playerName.Substring(0, MaxNameLength);
        return System.Text.Encoding.UTF8.GetBytes(passwordInput.text + "\n" + playerName);
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
    }

    private void SetMenuInteractable(bool value)
    {
        nameInput.interactable = value;
        passwordInput.interactable = value;
        addressInput.interactable = value;
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
    }

    private void UnsubscribeCallbacks()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
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
        RefreshRoom();
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
        string reason = string.IsNullOrEmpty(nm.DisconnectReason) ? "Bağlantı koptu." : nm.DisconnectReason;
        UnsubscribeCallbacks();
        ShowMenu(reason);
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        string payload = System.Text.Encoding.UTF8.GetString(request.Payload);
        int split = payload.IndexOf('\n');
        string password = split >= 0 ? payload.Substring(0, split) : payload;
        string playerName = split >= 0 ? payload.Substring(split + 1) : "Oyuncu";
        if (playerName.Length > MaxNameLength) playerName = playerName.Substring(0, MaxNameLength);

        response.CreatePlayerObject = false; // Karakterleri world sahnesinde SpamChars oluşturuyor

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

        // Önceki turdan dönen oyuncu ismini ve rolünü korur
        MatchSettings.Players.TryGetValue(clientId, out var previous);
        if (!pendingNames.TryGetValue(clientId, out string playerName)) playerName = previous.Name;

        players.Add(new LobbyPlayer
        {
            ClientId = clientId,
            Name = new FixedString32Bytes(string.IsNullOrEmpty(playerName) ? "Oyuncu" : playerName),
            IsHarvester = previous.IsHarvester && HarvesterIndex() < 0,
        });
    }

    #endregion

    #region Roller ve başlatma

    [Rpc(SendTo.Server)]
    private void RequestRoleRpc(bool harvester, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        int index = IndexOf(sender);
        if (index < 0) return;

        // Harvester'ı sadece 1 kişi seçebilir
        if (harvester && HarvesterIndex() >= 0 && HarvesterIndex() != index) return;

        var player = players[index];
        if (player.IsHarvester == harvester) return;
        player.IsHarvester = harvester;
        players[index] = player;
    }

    private void StartGame()
    {
        if (!IsServer || gameStarted || !CanStart(out _)) return;

        MatchSettings.Players.Clear();
        foreach (var p in players)
        {
            MatchSettings.Players[p.ClientId] = new MatchSettings.PlayerInfo { Name = p.Name.ToString(), IsHarvester = p.IsHarvester };
        }

        gameStarted = true;
        NetworkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    private bool CanStart(out string reason)
    {
        if (players.Count < MinPlayers)
        {
            reason = "Başlamak için en az " + MinPlayers + " oyuncu gerekli.";
            return false;
        }
        // Tek kişilik debug turunda rol serbest: robot seçen Harvester'sız dolaşır
        if (HarvesterIndex() < 0 && players.Count > 1)
        {
            reason = "Bir oyuncu Harvester'ı seçmeli.";
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

    private int HarvesterIndex()
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].IsHarvester) return i;
        }
        return -1;
    }

    #endregion

    private void RefreshRoom()
    {
        if (!IsSpawned) return;

        ulong localId = NetworkManager.LocalClientId;
        int harvesterIndex = HarvesterIndex();
        bool localIsHarvester = false;

        for (int i = 0; i < slotTexts.Length; i++)
        {
            if (i >= players.Count)
            {
                slotTexts[i].text = "<color=#555>— boş —</color>";
                continue;
            }

            var p = players[i];
            bool isLocal = p.ClientId == localId;
            if (isLocal) localIsHarvester = p.IsHarvester;

            string role = p.IsHarvester ? "<color=#E03A2F>HARVESTER</color>" : "<color=#8FB3D9>Robot</color>";
            string tags = (p.ClientId == NetworkManager.ServerClientId ? "  <color=#C9A227>[host]</color>" : "")
                        + (isLocal ? "  <color=#AAA>(sen)</color>" : "");
            slotTexts[i].text = p.Name + tags + "<pos=70%>" + role;
        }

        harvesterButton.interactable = !localIsHarvester && harvesterIndex < 0;
        robotButton.interactable = localIsHarvester;

        startButton.gameObject.SetActive(IsServer);
        bool canStart = CanStart(out string reason);
        startButton.interactable = canStart;

        if (!canStart) roomStatusText.text = reason;
        else roomStatusText.text = IsServer ? "Hazır! Oyunu başlatabilirsin." : "Host'un oyunu başlatması bekleniyor...";
    }
}
