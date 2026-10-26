using System;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sadece komut satırı otomatik testinde (lobby -autoHost / -autoJoin) oluşturulur.
// Sahne değişse de yaşar, birkaç saniyede bir oyunun durumunu Player.log'a yazar.
//   -autoKill S   (host) her tur başladıktan S sn sonra robotları sunucudan öldürür (makara tespiti hariç ölüm yolunu dener)
//   -autoShot     öldürmeden 0.5 ve 2 sn sonra ekran görüntüsü alır (Player.log'un yanına)
public class AutoTestReporter : MonoBehaviour
{
    const float Interval = 2f;
    private float timer;
    private float killAfter = -1f;
    private bool screenshots;
    private float playingSince = -1f;
    private bool killedThisRound;
    private float killedAt = -1f;
    private int shotsTaken;

    public static void Create()
    {
        var go = new GameObject("AutoTestReporter");
        DontDestroyOnLoad(go);
        var reporter = go.AddComponent<AutoTestReporter>();
        var args = Environment.GetCommandLineArgs();
        int kill = Array.IndexOf(args, "-autoKill");
        if (kill >= 0 && kill + 1 < args.Length && float.TryParse(args[kill + 1], out float seconds)) reporter.killAfter = seconds;
        reporter.screenshots = Array.IndexOf(args, "-autoShot") >= 0;
    }

    private void Update()
    {
        UpdateKillTest();

        timer += Time.unscaledDeltaTime;
        if (timer < Interval) return;
        timer = 0f;

        var sb = new StringBuilder("[AutoTest] sahne=").Append(SceneManager.GetActiveScene().name);
        var gm = GameManager.Instance;
        if (gm != null && gm.IsSpawned)
        {
            sb.Append($" faz={gm.Phase} sonuç={gm.Result} robot={gm.AliveRobots}/{gm.TotalRobots} kalan={gm.RemainingSeconds:F0}");
        }
        foreach (var harvester in FindObjectsByType<frankMovement>(FindObjectsSortMode.None))
        {
            sb.Append($" | Harvester(sahip={harvester.OwnerClientId}) {harvester.transform.position:F1} yön={harvester.transform.eulerAngles.y:F0}");
        }
        foreach (var robot in CharMovement.All)
        {
            sb.Append($" | Robot(sahip={robot.OwnerClientId}) {robot.transform.position:F1}{(robot.IsDead ? " ÖLÜ" : "")}");
        }
        // Ölen robotun kemiklerinden kopup sahne köküne düşen parçalar
        int debris = 0;
        float maxHeight = 0f, maxSpread = 0f;
        foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (body.transform.parent != null || !body.TryGetComponent(out MeshRenderer _)) continue;
            debris++;
            maxHeight = Mathf.Max(maxHeight, body.position.y);
            foreach (var robot in CharMovement.All) maxSpread = Mathf.Max(maxSpread, Vector3.Distance(body.position, robot.transform.position));
        }
        if (debris > 0) sb.Append($" | parça={debris} enYüksek={maxHeight:F1} enUzak={maxSpread:F1}");
        Debug.Log(sb.ToString());
    }

    private void UpdateKillTest()
    {
        var gm = GameManager.Instance;
        bool playing = gm != null && gm.IsSpawned && gm.Phase == MatchPhase.Playing;
        if (!playing)
        {
            playingSince = -1f;
            killedThisRound = false;
        }
        else if (playingSince < 0f) playingSince = Time.time;

        // Robotlardan biri ölünce (her client kendi gözlemiyle) ekran görüntüsü zamanlayıcısı başlar
        if (killedAt < 0f && CharMovement.All.Exists(r => r.IsDead)) killedAt = Time.time;
        if (screenshots && killedAt >= 0f && shotsTaken < 2 && Time.time - killedAt >= (shotsTaken == 0 ? 0.5f : 2f))
        {
            string role = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer ? "host" : "client";
            string path = System.IO.Path.Combine(Application.persistentDataPath, $"autotest_{role}_{shotsTaken}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[AutoTest] Ekran görüntüsü: " + path);
            shotsTaken++;
        }

        if (killAfter < 0f || !playing || killedThisRound || !NetworkManager.Singleton.IsServer || Time.time - playingSince < killAfter) return;
        killedThisRound = true;
        foreach (var robot in CharMovement.All.ToArray())
        {
            Debug.Log("[AutoTest] Robot öldürülüyor: " + robot.OwnerClientId);
            robot.Kill();
            gm.ReportRobotDeath(robot.OwnerClientId);
        }
    }
}
