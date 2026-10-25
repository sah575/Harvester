using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SpamChars : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints; // Spawn noktaları
    public List<GameObject> prefabs; // Karakter prefab'ları listesi. 0: Harvester, geri kalanı yayalar

    private void Awake()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // Tüm client'lar sahneyi yükleyince spawn et.
        // Start yerine Awake: tek kişilik host'ta olay Start'tan önce tetikleniyor.
        nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
    }

    private void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.SceneManager != null)
        {
            nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }
    }

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameObject.scene.name) return;
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        clientsCompleted.Sort(); // Host (id 0) her zaman ilk sırada olsun
        SpawnCharacters(clientsCompleted);
    }

    private void SpawnCharacters(List<ulong> clientIds)
    {
        for (int i = 0; i < clientIds.Count; i++)
        {
            var spawnPoint = spawnPoints[i % spawnPoints.Length];

            // İlk oyuncu (host) Harvester, diğerleri yaya prefab'ları arasında döner
            GameObject characterPrefab = i == 0 || prefabs.Count == 1
                ? prefabs[0]
                : prefabs[1 + (i - 1) % (prefabs.Count - 1)];

            // Karakteri spawn et
            GameObject character = Instantiate(characterPrefab, spawnPoint.position, spawnPoint.rotation);
            character.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientIds[i]); // NetworkObject'i client ile ilişkilendir
        }
    }
}
