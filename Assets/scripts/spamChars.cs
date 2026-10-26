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
        ulong harvesterId = FindHarvester(clientIds);
        int robotIndex = 0;

        foreach (var clientId in clientIds)
        {
            GameObject characterPrefab;
            Transform spawnPoint;

            // Spawn noktası 0 Harvester'ın, diğerleri robotların
            if (clientId == harvesterId)
            {
                characterPrefab = prefabs[0];
                spawnPoint = spawnPoints[0];
            }
            else
            {
                characterPrefab = prefabs.Count > 1 ? prefabs[1 + robotIndex % (prefabs.Count - 1)] : prefabs[0];
                spawnPoint = spawnPoints[(1 + robotIndex) % spawnPoints.Length];
                robotIndex++;
            }

            // Karakteri spawn et
            GameObject character = Instantiate(characterPrefab, spawnPoint.position, spawnPoint.rotation);
            character.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId); // NetworkObject'i client ile ilişkilendir
        }
    }

    // Lobide Harvester'ı seçen oyuncu. Lobi atlanmışsa (doğrudan test) host Harvester olur.
    private static ulong FindHarvester(List<ulong> clientIds)
    {
        foreach (var pair in MatchSettings.Players)
        {
            if (pair.Value.IsHarvester && clientIds.Contains(pair.Key)) return pair.Key;
        }
        return clientIds[0];
    }
}
