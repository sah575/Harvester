using System;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sadece komut satırı otomatik testinde (lobby -autoHost / -autoJoin) oluşturulur.
// Sahne değişse de yaşar, birkaç saniyede bir oyunun durumunu Player.log'a yazar.
//   -autoKill S   (host) her tur başladıktan S sn sonra robotları sunucudan öldürür (makara tespiti hariç ölüm yolunu dener)
//   -autoShot     öldürmeden 0.5 ve 2 sn sonra ekran görüntüsü alır (Player.log'un yanına)
//   -autoDrive    harvester tarla merkezinin çevresinde daire çizerek, robot zikzak koşarak kendi kendine hareket eder
//   -ntBuffer N   NetworkTransform yorumlama tamponuna N tick ekler (deneme için)
//   -ntInterp T   yorumlama türünü zorlar: LegacyLerp, Lerp, SmoothDampening (deneme için)
//   -autoTrace    her karede (en çok 60 Hz) saat damgalı konumları trace_<host|client>.csv'ye yazar;
//                 aynı makinedeki iki kopyanın kayıtları karşılaştırılarak senkron hatası ölçülür
// Konum kaydı, harvester tahmini (RemoteExtrapolator) uygulandıktan sonra alınsın
[DefaultExecutionOrder(2000)]
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
    private System.IO.StreamWriter trace;
    private double lastTraceMs;

    public static void Create()
    {
        var go = new GameObject("AutoTestReporter");
        DontDestroyOnLoad(go);
        var reporter = go.AddComponent<AutoTestReporter>();
        var args = Environment.GetCommandLineArgs();
        int kill = Array.IndexOf(args, "-autoKill");
        if (kill >= 0 && kill + 1 < args.Length && float.TryParse(args[kill + 1], out float seconds)) reporter.killAfter = seconds;
        reporter.screenshots = Array.IndexOf(args, "-autoShot") >= 0;
        if (Array.IndexOf(args, "-autoDrive") >= 0)
        {
            frankMovement.AutoDrive = true;
            CharMovement.AutoRun = true;
        }
        int interp = Array.IndexOf(args, "-ntInterp");
        if (interp >= 0 && interp + 1 < args.Length && Enum.TryParse(args[interp + 1], true, out Unity.Netcode.Components.NetworkTransform.InterpolationTypes type))
        {
            forcedInterpolation = type;
            Debug.Log("[AutoTest] NetworkTransform yorumlama türü: " + type);
        }
        int buffer = Array.IndexOf(args, "-ntBuffer");
        if (buffer >= 0 && buffer + 1 < args.Length && int.TryParse(args[buffer + 1], out int ticks))
        {
            Unity.Netcode.Components.NetworkTransform.InterpolationBufferTickOffset = ticks;
            Debug.Log("[AutoTest] NetworkTransform tampon payı: " + ticks + " tick");
        }
        if (Array.IndexOf(args, "-autoTrace") >= 0)
        {
            string role = Array.IndexOf(args, "-autoHost") >= 0 ? "host" : "client";
            string path = System.IO.Path.Combine(Application.persistentDataPath, $"trace_{role}.csv");
            reporter.trace = new System.IO.StreamWriter(path, false) { AutoFlush = true };
            reporter.trace.WriteLine("ms,hx,hz,hyaw,rx,rz,hpitch,hroll");
            Debug.Log("[AutoTest] Konum kaydı: " + path);
        }
    }

    private static Unity.Netcode.Components.NetworkTransform.InterpolationTypes? forcedInterpolation;
    private float nextInterpolationApply;
    private bool diagnosed;

    private void LateUpdate()
    {
        if (forcedInterpolation.HasValue && Time.time >= nextInterpolationApply)
        {
            nextInterpolationApply = Time.time + 1f;
            foreach (var nt in FindObjectsByType<Unity.Netcode.Components.NetworkTransform>(FindObjectsSortMode.None))
            {
                nt.PositionInterpolationType = forcedInterpolation.Value;
                nt.RotationInterpolationType = forcedInterpolation.Value;
            }
        }
        if (trace == null) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.IsSpawned || gm.Phase != MatchPhase.Playing) return;
        double ms = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
        if (ms - lastTraceMs < 16) return;
        lastTraceMs = ms;

        var harvester = FindFirstObjectByType<frankMovement>();
        CharMovement robot = CharMovement.All.Count > 0 ? CharMovement.All[0] : null;
        if (harvester == null || robot == null) return;
        if (!diagnosed && Time.time - playingSince > 5f)
        {
            diagnosed = true;
            var rb = harvester.GetComponent<Rigidbody>();
            var nt = harvester.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            var nm = NetworkManager.Singleton;
            Debug.Log($"[AutoTest] Harvester tanı: sahip={harvester.IsOwner} kinematik={rb.isKinematic} rbInterp={rb.interpolation} ntInterp={nt.Interpolate} tür={nt.PositionInterpolationType} unrel={nt.UseUnreliableDeltas} half={nt.UseHalfFloatPrecision} tickLatency={nm.NetworkTimeSystem.TickLatency} rtt={nm.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId)}ms ping={Harvester.Net.NetStatsHUD.LocalRtt * 1000f:F0}ms fixedDt={Time.fixedDeltaTime} tampon={Unity.Netcode.Components.NetworkTransform.InterpolationBufferTickOffset}");
        }
        Vector3 h = harvester.transform.position, r = robot.transform.position;
        Vector3 e = harvester.transform.eulerAngles;
        trace.WriteLine(FormattableString.Invariant($"{ms:F1},{h.x:F3},{h.z:F3},{e.y:F2},{r.x:F3},{r.z:F3},{Mathf.DeltaAngle(0f, e.x):F2},{Mathf.DeltaAngle(0f, e.z):F2}"));
    }

    private void OnDestroy()
    {
        if (trace != null) trace.Dispose();
    }

    private void OnApplicationQuit()
    {
        if (trace != null) trace.Flush();
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
