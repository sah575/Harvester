using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class lobby : MonoBehaviour
{
    [SerializeField] private Button hostButton, clientButton, readyHost, readyClient;
    public GameObject page1, page2, page3;
    [SerializeField] private TMP_InputField hostNameInput, hostpassInput, clientNameInput, clientPassInput; // TMP_InputField referansları
    [SerializeField] private int requiredPlayers = 2; // Oyunun başlaması için gereken oyuncu sayısı
    [SerializeField] private string gameSceneName = "world";

    private string hostPassword;
    private bool gameStarted;

    private void Awake()
    {
        hostButton.onClick.AddListener(() =>
        {
            page1.SetActive(false);
            page3.SetActive(false);
        });

        clientButton.onClick.AddListener(() =>
        {
            page2.SetActive(false);
            page1.SetActive(false);
        });

        readyHost.onClick.AddListener(() =>
        {
            hostPassword = hostpassInput.text;
            Debug.Log("Host Kullanıcı Girişi: " + hostNameInput.text);
            hostNameInput.interactable = false;
            hostpassInput.interactable = false;
            readyHost.interactable = false;

            var nm = NetworkManager.Singleton;
            // Host da kendi bağlantısı için onaydan geçer, o yüzden kendi şifresini gönderiyor
            nm.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(hostPassword);
            nm.ConnectionApprovalCallback = ApprovalCheck;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.StartHost();
        });

        readyClient.onClick.AddListener(() =>
        {
            Debug.Log("Client Kullanıcı Girişi: " + clientNameInput.text);
            clientNameInput.interactable = false;
            clientPassInput.interactable = false;
            readyClient.interactable = false;

            var nm = NetworkManager.Singleton;
            // Oda şifresini ayarlama
            nm.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(clientPassInput.text);
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.StartClient();
        });
    }

    private void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (!nm.IsServer || gameStarted) return;

        if (nm.ConnectedClientsIds.Count >= requiredPlayers)
        {
            gameStarted = true;
            nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
    }

    // Reddedilen client'a sebebini göster ve tekrar denemesine izin ver
    private void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm.IsServer) return;

        if (!string.IsNullOrEmpty(nm.DisconnectReason))
        {
            Debug.Log("Bağlantı reddedildi: " + nm.DisconnectReason);
        }
        clientNameInput.interactable = true;
        clientPassInput.interactable = true;
        readyClient.interactable = true;
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        string clientPassword = System.Text.Encoding.UTF8.GetString(request.Payload);
        response.CreatePlayerObject = false; // Karakterleri world sahnesinde SpamChars oluşturuyor

        if (gameStarted)
        {
            response.Approved = false;
            response.Reason = "Game already started.";
        }
        else if (NetworkManager.Singleton.ConnectedClientsIds.Count >= requiredPlayers)
        {
            response.Approved = false;
            response.Reason = "Room is full.";
        }
        else if (clientPassword != hostPassword)
        {
            response.Approved = false;
            response.Reason = "Invalid room password.";
        }
        else
        {
            response.Approved = true;
        }
    }
}
