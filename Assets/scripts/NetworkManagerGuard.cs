using Unity.Netcode;
using UnityEngine;

// Lobby sahnesi tekrar yüklendiğinde (tur sonu dönüş) sahnedeki NetworkManager'ın ikinci
// bir kopyası oluşur. NGO bunu kendisi yok etmediği için fazlasını burada temizliyoruz.
[DefaultExecutionOrder(-1000)]
public class NetworkManagerGuard : MonoBehaviour
{
    private void Awake()
    {
        var singleton = NetworkManager.Singleton;
        if (singleton != null && singleton.gameObject != gameObject)
        {
            Destroy(gameObject);
        }
    }
}
