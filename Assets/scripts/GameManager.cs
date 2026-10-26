using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum MatchPhase : byte
{
    Waiting,
    Countdown,
    Playing,
    Ended,
}

public enum MatchResult : byte
{
    None,
    RobotsWin, // Süre doldu
    HarvesterWin, // Bütün robotlar öldü
    HarvesterLeft, // Frank oyundan çıktı
}

// Tur akışı: geri sayım → oyun → bitiş → lobiye dönüş. Tüm kararları sunucu verir,
// client'lar sadece senkron değerleri okur.
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private float countdownSeconds = 5f;
    [SerializeField] private float matchSeconds = 300f;
    [SerializeField] private float endScreenSeconds = 8f;
    [SerializeField] private string lobbySceneName = "Lobby";

    private readonly NetworkVariable<MatchPhase> phase = new NetworkVariable<MatchPhase>(MatchPhase.Waiting);
    private readonly NetworkVariable<double> phaseEndTime = new NetworkVariable<double>();
    private readonly NetworkVariable<MatchResult> result = new NetworkVariable<MatchResult>(MatchResult.None);
    private readonly NetworkVariable<ulong> harvesterClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    private readonly NetworkVariable<int> aliveRobots = new NetworkVariable<int>();
    private readonly NetworkVariable<int> totalRobots = new NetworkVariable<int>();

    private readonly HashSet<ulong> aliveRobotIds = new HashSet<ulong>();

    public MatchPhase Phase => phase.Value;
    public MatchResult Result => result.Value;
    public int AliveRobots => aliveRobots.Value;
    public int TotalRobots => totalRobots.Value;
    public float MatchSeconds => matchSeconds;
    public bool IsLocalHarvester => IsSpawned && NetworkManager.LocalClientId == harvesterClientId.Value;
    public float RemainingSeconds => IsSpawned ? Mathf.Max(0f, (float)(phaseEndTime.Value - NetworkManager.ServerTime.Time)) : 0f;

    // Oyuncular sadece tur oynanırken hareket edebilir. GameManager yoksa (sahne tek başına test) serbest.
    public static bool InputAllowed => Instance == null || !Instance.IsSpawned || Instance.phase.Value == MatchPhase.Playing;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Host kapanınca client'ın NetworkObject'leri despawn olur, o yüzden bunu OnNetworkSpawn yerine burada dinliyoruz
        if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped += OnLocalClientStopped;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped -= OnLocalClientStopped;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // SpamChars karakterleri oluşturduktan sonra çağırır
    public void BeginMatch(ulong harvesterId, IEnumerable<ulong> robotIds)
    {
        if (!IsServer) return;

        harvesterClientId.Value = harvesterId;
        aliveRobotIds.Clear();
        foreach (var id in robotIds) aliveRobotIds.Add(id);
        totalRobots.Value = aliveRobotIds.Count;
        aliveRobots.Value = aliveRobotIds.Count;
        result.Value = MatchResult.None;
        SetPhase(MatchPhase.Countdown, countdownSeconds);
    }

    // Faz 4'te makara bir robotu öldürünce çağrılacak
    public void ReportRobotDeath(ulong clientId)
    {
        if (!IsServer || phase.Value != MatchPhase.Playing) return;
        RemoveRobot(clientId);
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;
        if (NetworkManager.ServerTime.Time < phaseEndTime.Value) return;

        switch (phase.Value)
        {
            case MatchPhase.Countdown:
                SetPhase(MatchPhase.Playing, matchSeconds);
                break;
            case MatchPhase.Playing:
                EndMatch(MatchResult.RobotsWin);
                break;
            case MatchPhase.Ended:
                ReturnToLobby();
                break;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (phase.Value != MatchPhase.Countdown && phase.Value != MatchPhase.Playing) return;

        if (clientId == harvesterClientId.Value) EndMatch(MatchResult.HarvesterLeft);
        else RemoveRobot(clientId); // Çıkan robot ölü sayılır
    }

    private void RemoveRobot(ulong clientId)
    {
        if (!aliveRobotIds.Remove(clientId)) return;
        aliveRobots.Value = aliveRobotIds.Count;
        if (aliveRobotIds.Count == 0) EndMatch(MatchResult.HarvesterWin);
    }

    private void EndMatch(MatchResult matchResult)
    {
        result.Value = matchResult;
        SetPhase(MatchPhase.Ended, endScreenSeconds);
    }

    private void SetPhase(MatchPhase newPhase, float duration)
    {
        phase.Value = newPhase;
        phaseEndTime.Value = NetworkManager.ServerTime.Time + duration;
    }

    private void ReturnToLobby()
    {
        phase.Value = MatchPhase.Waiting;
        NetworkManager.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
    }

    // Host oyunu kapattı ya da bağlantı koptu: lobiye kendimiz dönüyoruz
    private void OnLocalClientStopped(bool wasHost)
    {
        if (wasHost) return;
        lobby.PendingStatus = "Host ile bağlantı koptu.";
        SceneManager.LoadScene(lobbySceneName);
    }
}
