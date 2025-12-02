using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using TMPro; // TextMeshPro kütüphanesini ekleyin
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class lobby : NetworkBehaviour
{
    [SerializeField] private Button hostButton, clientButton, readyHost, readyClient;
    public GameObject page1, page2, page3;
    [SerializeField] private TMP_InputField hostNameInput, hostpassInput, clientNameInput, clientPassInput; // TMP_InputField referanslarý

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
            string userInput = hostNameInput.text; // InputField içeriðini al
            string passHost = hostpassInput.text; // InputField içeriðini al
            Debug.Log("Host Kullanýcý Giriþi: " + userInput); // Konsola yazdýr
            Debug.Log("Host Þifresi: " + passHost); // Konsola yazdýr
            NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(passHost);
            // Baðlantý onaylama ayarlarý
            hostNameInput.interactable = false;
            hostpassInput.interactable = false;
            readyHost.interactable = false;
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
            NetworkManager.Singleton.StartHost();
        });

        readyClient.onClick.AddListener(() =>
        {
            string userInput = clientNameInput.text; // InputField içeriðini al
            string passClient = clientPassInput.text; // InputField içeriðini al
            Debug.Log("Client Kullanýcý Giriþi: " + userInput); // Konsola yazdýr
            Debug.Log("Client Þifresi: " + passClient); // Konsola yazdýr
            clientNameInput.interactable = false;
            clientPassInput.interactable = false;
            readyClient.interactable = false;
            // Oda þifresini ayarlama
            NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(passClient);
            NetworkManager.Singleton.StartClient();
        });
    }

    private void Update()
    {
        if (IsServer)
        {
            if (NetworkManager.Singleton.ConnectedClients.Count == 2)
            {
                NetworkManager.SceneManager.LoadScene("world", LoadSceneMode.Single);
            }
        }
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        var connectionData = request.Payload;
        string clientPassword = System.Text.Encoding.ASCII.GetString(connectionData);

        // Host'un mevcut þifresini almak için bir mekanizma ekleyin
        string hostPassword = System.Text.Encoding.ASCII.GetString(NetworkManager.Singleton.NetworkConfig.ConnectionData);

        // Þifre kontrolü
        if (clientPassword == hostPassword)
        {
            response.Approved = true;
            response.CreatePlayerObject = false; // Host bu seçeneði ayarlayabilir
        }
        else
        {
            response.Approved = false;
            response.Reason = "Invalid room password.";
        }
    }
}
