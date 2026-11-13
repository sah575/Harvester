using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Harvester.Race
{
    /// <summary>
    /// race sahnesi bütün istemcilerde yüklenince (OnLoadEventCompleted) sunucu her bağlı istemciye bir araba,
    /// kendine de harvester oluşturur ve RaceManager.BeginRace'i çağırır. Eski oyundaki SpamChars'ın karşılığı.
    /// Hareket sahibinde olduğu için başlangıç yeri sahibine ayrıca PlaceAtRpc ile bildirilir
    /// (yoksa sahibin rigidbody'si prefab pozunda kalabiliyor).
    /// </summary>
    public class RaceSpawner : MonoBehaviour
    {
        [Tooltip("Assets/Prefabs/race/RaceCar.prefab (DefaultNetworkPrefabs'ta kayıtlı olmalı)")]
        [SerializeField] private GameObject carPrefab;
        [Tooltip("Assets/Prefabs/race/HarvesterAI.prefab (boşsa harvester oluşturulmaz)")]
        [SerializeField] private GameObject harvesterPrefab;
        [Tooltip("Başlangıç yerinin zeminden yüksekliği (m); araba süspansiyonuna oturur")]
        [SerializeField] private float dropHeight = 0.3f;

        [Tooltip("Ağsız denemede (race sahnesi tek başına açıldı) kaç araba oluşturulsun. Editörde EditorPrefs \"Harvester.RaceOfflineCars\" geçersiz kılar.")]
        [SerializeField] private int offlineCars = 1;

        private void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) return;
            // Ağsız deneme: arabaları ve harvester'ı ağsız oluştur; RaceManager sahnedeki arabalarla yerel yarış başlatır
            int count = offlineCars + DebugBots;
#if UNITY_EDITOR
            count = UnityEditor.EditorPrefs.GetInt("Harvester.RaceOfflineCars", count);
#endif
            if (RaceCar.All.Count > 0 || carPrefab == null) return; // sahneye elle konmuş araba varsa onu kullan
            var track = RaceTrack.Current;
            for (int i = 0; i < Mathf.Clamp(count, 1, 8); i++)
            {
                Pose pose = track != null ? track.GetStartPose(i) : new Pose(transform.position + Vector3.right * 4f * i, transform.rotation);
                var go = Instantiate(carPrefab, pose.position + Vector3.up * dropHeight, pose.rotation);
                go.name = "RaceCar_yerel" + i;
            }
            if (harvesterPrefab != null && HarvesterLink.Instance == null && !DebugNoHarvester)
            {
                Pose pose = track != null ? track.GetHarvesterStartPose(Mathf.Max(count, 1)) : new Pose(transform.position - transform.forward * 30f, transform.rotation);
                Instantiate(harvesterPrefab, pose.position, pose.rotation);
                HarvesterLink.SetRunning(false); // ağsızda kendiliğinden kalkmasın; RaceManager yarışta kaldırır
            }
        }

        /// <summary>Test botlarının yarışçı numarası bundan başlar (gerçek istemcilerle çakışmasın).</summary>
        public const ulong BotClientIdBase = 1000;
        /// <summary>Sadece editör/development: -autoBots N ile eklenen sunucu botu sayısı.</summary>
        public static int DebugBots { get; private set; }
        /// <summary>Sadece editör/development: -autoNoHarvester ile harvester oluşturulmaz.</summary>
        public static bool DebugNoHarvester { get; private set; }

        private static void ReadDebugArgs()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = new List<string>(System.Environment.GetCommandLineArgs());
#if UNITY_EDITOR
            args.AddRange(UnityEditor.EditorPrefs.GetString("Harvester.RaceTestArgs", "").Split(' '));
#endif
            int i = args.IndexOf("-autoBots");
            int n;
            DebugBots = i >= 0 && i + 1 < args.Count && int.TryParse(args[i + 1], out n) ? Mathf.Clamp(n, 0, 6) : 0;
            DebugNoHarvester = args.Contains("-autoNoHarvester");
#endif
        }

        private void Awake()
        {
            ReadDebugArgs();
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            // Start yerine Awake: tek kişilik host'ta olay Start'tan önce tetikleniyor (SpamChars ile aynı)
            nm.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
        }

        private void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null) nm.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (sceneName != gameObject.scene.name) return;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
            if (timedOut.Count > 0) Debug.LogWarning("[Race] Sahneyi zamanında yükleyemeyenler: " + string.Join(",", timedOut));
            completed.Sort(); // host (0) ön sırada
            Spawn(completed);
        }

        private void Spawn(List<ulong> clientIds)
        {
            var track = RaceTrack.Current;
            if (track == null) Debug.LogError("[Race] Sahnede RaceTrack yok");
            if (carPrefab == null)
            {
                Debug.LogError("[Race] RaceSpawner: araba prefab'ı atanmamış");
                return;
            }

            var cars = new List<RaceCar>();
            var ids = new List<ulong>();

            // Test botları (sadece editör/development, -autoBots N): sunucuya ait, host sürer. Önce oluşturulurlar ki
            // host'un kendi arabası en son doğup RaceCar.Local (kamera) olsun; diziliş yerleri oyunculardan sonra.
            var bots = new List<RaceCar>();
            int players = 0;
            foreach (var id in clientIds) if (NetworkManager.Singleton.ConnectedClients.ContainsKey(id)) players++;
            for (int b = 0; b < DebugBots; b++)
            {
                Pose pose = track != null ? track.GetStartPose(players + b) : new Pose(transform.position + Vector3.left * 4f * (b + 1), transform.rotation);
                GameObject go = Instantiate(carPrefab, pose.position + Vector3.up * dropHeight, pose.rotation);
                go.name = "RaceCar_bot" + b;
                go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
                var bot = go.GetComponent<RaceCar>();
                bot.PlaceAt(pose.position + Vector3.up * dropHeight, pose.rotation);
                bots.Add(bot);
            }

            for (int i = 0; i < clientIds.Count; i++)
            {
                ulong clientId = clientIds[i];
                if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) continue;
                Pose pose = track != null ? track.GetStartPose(cars.Count) : new Pose(transform.position + Vector3.right * 4f * i, transform.rotation);
                Vector3 position = pose.position + Vector3.up * dropHeight;

                GameObject go = Instantiate(carPrefab, position, pose.rotation);
                go.name = "RaceCar_" + clientId;
                go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, destroyWithScene: true);
                var car = go.GetComponent<RaceCar>();
                car.PlaceAtRpc(position, pose.rotation);
                cars.Add(car);
                ids.Add(clientId);
            }

            for (int b = 0; b < bots.Count; b++)
            {
                cars.Add(bots[b]);
                ids.Add(BotClientIdBase + (ulong)b);
            }

            NetworkObject harvester = null;
            if (harvesterPrefab != null && !DebugNoHarvester)
            {
                Pose pose = track != null ? track.GetHarvesterStartPose(Mathf.Max(cars.Count, 1)) : new Pose(transform.position - transform.forward * 30f, transform.rotation);
                GameObject go = Instantiate(harvesterPrefab, pose.position, pose.rotation);
                harvester = go.GetComponent<NetworkObject>();
                if (harvester != null) harvester.Spawn(destroyWithScene: true);
                HarvesterLink.PlaceAtStart(Mathf.Max(cars.Count, 1));
            }
            else Debug.LogWarning("[Race] RaceSpawner: harvester prefab'ı atanmamış, harvestersız yarış");

            Debug.Log("[Race] Oluşturuldu: " + cars.Count + " araba" + (harvester != null ? " + harvester" : ""));
            var manager = RaceManager.Instance;
            if (manager != null) manager.BeginRace(cars, ids, harvester);
            else Debug.LogError("[Race] Sahnede RaceManager yok");
        }
    }
}
