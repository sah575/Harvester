using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Harvester tehdit modelini ağsız ölçen otomatik test (race sahnesi). Sadece editörde ve development build'de çalışır.
    /// Arabaları CarTrackFollower ile gerçek araba fiziğiyle sürdürür ve tasmayı (açık ≤ leashMax), baskı bandını,
    /// kaza → biçme süresini, kalkışı, yarış bitişini ve hedef değişimini ölçer.
    ///
    /// Senaryolar (sıra numarası = onlyScenarios):
    ///  0 A1 tam gaz, SupercarV10 (en hızlı)      1 A2 tam gaz, HotHatch (en yavaş)
    ///  2 B  uzman sürücü, 120 sn                  3 C1 kasıtlı kaza: 30. sn'de 1 sn durur
    ///  4 C2 kasıtlı kaza: 30. sn'de duvara vurur  5 D  kalkış, 4 araba
    ///  6 F  üç araba, en arkadaki iki kez kaza     7 E  yarış bitişi (yarış yöneticisiyle 1 tur)
    /// Açık = tablanın önünden arabanın arkasına, pist boyunca (HarvesterAI.TargetGap). Kalkış penceresi ve hedef değişiminden
    /// sonraki yeniden yakalama süresi 30 m istatistiğinden ayrı raporlanır.
    /// Sonuçlar "[HarvesterTest]" önekiyle konsola, Builds/reports/harvester-tehdit/ altına (özet + açık CSV'leri) yazılır.
    /// Başlatma: runOnStart, F9, komut satırında -harvesterTest [-harvesterOnly 0,1] ya da Launch().
    /// Test sırasında kamera harvester'ın arkasından hedefi gösterir (V: kabin), sol üstte özet.
    /// </summary>
    public class HarvesterTestRunner : MonoBehaviour
    {
        public static string LastReport { get; private set; } = "";
        public static bool IsRunning { get; private set; }
        public static bool Finished { get; private set; }

        [Tooltip("Oyun başlayınca testi kendiliğinden başlat")]
        [SerializeField] private bool runOnStart = true;
        [Tooltip("Araba prefab'ı (RaceCar)")]
        [SerializeField] private GameObject carPrefab;
        [Tooltip("Sahnedeki harvester (boşsa HarvesterAI.Instance, o da yoksa prefab'dan kurulur)")]
        [SerializeField] private HarvesterAI harvester;
        [SerializeField] private GameObject harvesterPrefab;
        [Tooltip("Arabalar kalktıktan kaç saniye sonra harvester salınır (RaceManager.harvesterDelay ile aynı tutun)")]
        [SerializeField] private float releaseDelay = 1f;
        [Tooltip("Sadece bu senaryoları çalıştır: virgülle sıra numaraları (0'dan), boş = hepsi. Ör. \"0,1\"")]
        [SerializeField] private string onlyScenarios = "";
        [Tooltip("Zaman ölçeği (fizik adımı değişmez, sadece daha hızlı koşar)")]
        [SerializeField] private float timeScale = 1f;
        [Tooltip("Test sırasında Game ekranında harvester'ı izleyen kamera ve özet")]
        [SerializeField] private bool followHarvester = true;

        private enum Driver { TamGaz, Uzman, Normal }
        private enum CrashKind { Yok, Dur, Duvar }

        private class Scenario
        {
            public string Name;
            public int[] Cars;          // katalog indeksi
            public float[] Speeds;      // hız çarpanı
            public Driver Driver;
            public float Duration = 120f;
            public int Laps;            // >0: öndeki araba bu kadar tur atınca biter
            public CrashKind Crash;
            public float[] CrashAt = new float[0]; // en arkadaki yarışan araba bu saniyelerde kaza yapar
            public bool UseManager;     // yarış yöneticisiyle gerçek akış (geri sayım, bitiş)
        }

        private class CarRun
        {
            public RaceCar Car;
            public CarTrackFollower Follower;
            public float CutTime = -1f;
            public float CrashDetectTime = -1f;
            public string CrashReason = "";
            public float LastWallHitTime = -100f;
            public int LastWallHits;
            public float MinSpeedBeforeCut = float.PositiveInfinity;
            public float WallSide = 1f;
            public float DeliberateAt = -1f;   // kasıtlı kaza anı (duvarda: çarpma anı)
        }

        private class Stats
        {
            public readonly List<float> Gaps = new List<float>();
            public float Over30, Seconds, InBand;
            public float LaunchApproach = -1f;       // salındıktan ≤30 m'ye kadar (sn)
            public readonly List<string> Reacquire = new List<string>();
            public readonly List<string> Events = new List<string>();
            public float MaxHarvesterSpeed;
            public int StartLeashSteps, StartTeleports, StartHits, StartCuts;
        }

        private readonly StringBuilder report = new StringBuilder();
        private readonly List<CarRun> runs = new List<CarRun>();
        private Camera followCamera;
        private HarvesterViewCamera viewCamera;
        private string currentScenario = "";
        private float scenarioStart;
        private float scenarioTime;
        private string reportDir;

        /// <summary>Editörden (execute_code) ya da başka koddan testi başlatır.</summary>
        public static HarvesterTestRunner Launch(GameObject carPrefab, string only, float timeScale)
        {
            var go = new GameObject("HarvesterTestRunner");
            var runner = go.AddComponent<HarvesterTestRunner>();
            runner.runOnStart = true;
            runner.carPrefab = carPrefab;
            runner.onlyScenarios = only ?? "";
            runner.timeScale = Mathf.Max(0.1f, timeScale);
            return runner;
        }

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-harvesterOnly");
            if (i >= 0 && i + 1 < args.Length) onlyScenarios = args[i + 1];
            bool commandLine = System.Array.IndexOf(args, "-harvesterTest") >= 0;
            if (runOnStart || commandLine) StartCoroutine(Run());
#else
            enabled = false;
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!IsRunning && Input.GetKeyDown(KeyCode.F9)) StartCoroutine(Run());
            if (viewCamera != null && Input.GetKeyDown(KeyCode.V)) viewCamera.Toggle();
#endif
        }

        private void Log(string line)
        {
            report.AppendLine(line);
            Debug.Log("[HarvesterTest] " + line);
        }

        private static Scenario[] BuildScenarios()
        {
            return new[]
            {
                new Scenario { Name = "A1 tam gaz, SupercarV10", Cars = new[] { 1 }, Speeds = new[] { 1f }, Driver = Driver.TamGaz, Duration = 60f },
                new Scenario { Name = "A2 tam gaz, HotHatch", Cars = new[] { 4 }, Speeds = new[] { 1f }, Driver = Driver.TamGaz, Laps = 3 },
                new Scenario { Name = "B uzman, RacerCoupe", Cars = new[] { 0 }, Speeds = new[] { 1f }, Driver = Driver.Uzman },
                new Scenario { Name = "C1 kaza: 30. sn'de 1 sn durur", Cars = new[] { 0 }, Speeds = new[] { 1f }, Driver = Driver.Uzman,
                               Crash = CrashKind.Dur, CrashAt = new[] { 30f }, Duration = 50f },
                new Scenario { Name = "C2 kaza: 30. sn'de duvara vurur", Cars = new[] { 0 }, Speeds = new[] { 1f }, Driver = Driver.Uzman,
                               Crash = CrashKind.Duvar, CrashAt = new[] { 30f }, Duration = 50f },
                new Scenario { Name = "D kalkış, 4 araba", Cars = new[] { 0, 1, 2, 4 }, Speeds = new[] { 1f, 1f, 1f, 1f }, Driver = Driver.Normal, Duration = 20f },
                new Scenario { Name = "F üç araba (1.0/0.93/0.85), en arkadaki 30. ve 60. sn'de durur", Cars = new[] { 0, 0, 0 },
                               Speeds = new[] { 1f, 0.93f, 0.85f }, Driver = Driver.Uzman, Crash = CrashKind.Dur, CrashAt = new[] { 30f, 60f }, Duration = 90f },
                new Scenario { Name = "E bitiş (yarış yöneticisi, 1 tur, 2 araba)", Cars = new[] { 0, 3 }, Speeds = new[] { 1f, 1f }, Driver = Driver.Uzman,
                               UseManager = true, Duration = 150f },
            };
        }

        public IEnumerator Run()
        {
            Finished = false;
            var track = RaceTrack.Current;
            if (track == null || carPrefab == null)
            {
                Debug.LogError("[HarvesterTest] pist ya da araba prefab'ı yok");
                yield break;
            }
            if (harvester == null) harvester = HarvesterAI.Instance;
            if (harvester == null && harvesterPrefab != null) harvester = Instantiate(harvesterPrefab).GetComponent<HarvesterAI>();
            if (harvester == null)
            {
                Debug.LogError("[HarvesterTest] harvester yok");
                yield break;
            }
            foreach (var other in new List<RaceCar>(RaceCar.All)) other.gameObject.SetActive(false);
            var manager = RaceManager.Instance != null ? RaceManager.Instance : FindAnyObjectByType<RaceManager>();
            if (manager != null)
            {
                manager.CancelInvoke();
                manager.enabled = false;
            }
            RaceCar.InputEnabled = true;
            Time.timeScale = timeScale;

            reportDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Builds", "reports", "harvester-tehdit");
#if !UNITY_EDITOR
            reportDir = Path.Combine(Application.persistentDataPath, "harvester-tehdit");
#endif
            try { Directory.CreateDirectory(reportDir); } catch { reportDir = null; }

            IsRunning = true;
            report.Length = 0;
            if (followHarvester) CreateFollowCamera();
            var d = harvester.Difficulty;
            Log(string.Format(CultureInfo.InvariantCulture,
                "başladı: pist {0:F0} m ({1}), zorluk '{2}': tasma {3} m, baskı {4} m (en az {5}), kaza oranı {6}, kalkış {7} sn, en yüksek hız {8}, salma {9} sn",
                track.Length, track.Loop ? "turlu" : "A→B", d.displayName, d.leashMax, d.pressureGap, d.pressureMinGap, d.crashSpeedRatio,
                d.launchWindow, d.maxSpeed, releaseDelay));

            var scenarios = BuildScenarios();
            var selected = new HashSet<int>();
            foreach (string part in (onlyScenarios ?? "").Split(','))
            {
                int index;
                if (int.TryParse(part.Trim(), out index)) selected.Add(index);
            }
            for (int i = 0; i < scenarios.Length; i++)
                if (selected.Count == 0 || selected.Contains(i)) yield return RunScenario(track, scenarios[i], manager, i);

            Cleanup();
            if (manager != null) manager.enabled = false;
            RaceCar.InputEnabled = true;
            IsRunning = false;
            Time.timeScale = 1f;
            if (followCamera != null) Destroy(followCamera.gameObject);
            Log("bitti");
            LastReport = report.ToString();
            if (reportDir != null) File.WriteAllText(Path.Combine(reportDir, "ozet.txt"), LastReport);
            Finished = true;
        }

        private void SpawnCars(RaceTrack track, Scenario s)
        {
            for (int i = 0; i < s.Cars.Length; i++)
            {
                Pose pose = track.GetStartPose(i);
                var go = Instantiate(carPrefab, pose.position + Vector3.up * 0.3f, pose.rotation);
                go.name = "TestAraba" + i;
                var car = go.GetComponent<RaceCar>();
                car.SetDefinitionIndex(s.Cars[i]);
                var follower = CarTrackFollower.Attach(car);
                follower.SpeedScale = s.Speeds[i];
                follower.LaneOffset = (i % 2 == 0 ? -1f : 1f) * track.Width * 0.2f;
                follower.LogEvents = false;
                follower.FullThrottle = s.Driver == Driver.TamGaz;
                if (s.Driver == Driver.Uzman)
                {
                    // İyi çizgi, kazasız: arabanın viraj sınırının %90'ı, fren planı %75'i
                    follower.CornerFraction = 0.9f;
                    follower.BrakeFraction = 0.75f;
                }
                runs.Add(new CarRun { Car = car, Follower = follower });
            }
        }

        private IEnumerator RunScenario(RaceTrack track, Scenario s, RaceManager manager, int index)
        {
            currentScenario = s.Name;
            Cleanup();
            harvester.SetRunning(false);
            harvester.PlaceAtStart(s.Cars.Length);
            yield return new WaitForFixedUpdate();
            SpawnCars(track, s);
            yield return null; // SetDefinitionIndex modeli kursun
            var names = new StringBuilder();
            foreach (var r in runs) names.Append(r.Car.Definition != null ? r.Car.Definition.name : "?").Append(' ');

            var st = new Stats { StartLeashSteps = harvester.LeashPullSteps, StartTeleports = harvester.TeleportCount, StartHits = harvester.HitCount, StartCuts = harvester.CutCount };
            System.Action<RaceCar> onCut = c =>
            {
                var r = runs.Find(x => x.Car == c);
                if (r == null || r.CutTime >= 0f) return;
                r.CutTime = scenarioTime;
                float crashAt = harvester.CrashTimeOf(c);
                string why = harvester.IsCrashed(c)
                    ? string.Format(CultureInfo.InvariantCulture, "kaza tespitinden {0:F1} sn sonra", Time.time - crashAt)
                    : "KAZA TESPİTİ YOK";
                st.Events.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1} sn: {1} biçildi ({2}; son duvar çarpması {3:F1} sn önce, biçilmeden önceki en düşük hız {4:F1} m/sn)",
                    scenarioTime, c.name, why, scenarioTime - r.LastWallHitTime, r.MinSpeedBeforeCut));
                if (r.DeliberateAt >= 0f)
                    st.Events.Add(string.Format(CultureInfo.InvariantCulture, "KAZA → BİÇİLME: {0:F2} sn ({1})", scenarioTime - r.DeliberateAt, c.name));
            };
            System.Action<RaceCar, string> onCrash = (c, reason) =>
            {
                var r = runs.Find(x => x.Car == c);
                if (r == null) return;
                r.CrashDetectTime = scenarioTime;
                r.CrashReason = reason;
                st.Events.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1} sn: kaza tespiti {1} ({2})", scenarioTime, c.name, reason));
            };
            harvester.OnCarCut += onCut;
            harvester.OnCrashDetected += onCrash;

            Log("--- senaryo " + index + ": " + s.Name + " [" + names.ToString().Trim() + "]");
            StreamWriter csv = null;
            if (reportDir != null)
            {
                csv = new StreamWriter(Path.Combine(reportDir, "senaryo" + index + ".csv"), false);
                csv.WriteLine("t,mod,acik,tasma,harvesterHiz,hedefHiz,hedef");
            }

            scenarioStart = Time.time;
            bool released = false;
            float releasedAt = -1f;
            RaceCar lastTarget = null;
            float reacquireStart = -1f;
            int crashIndex = 0;
            CarRun crashing = null;
            float crashStart = -1f, crashWallHit = -1f;
            int crashWallBase = 0;
            float finishAt = -1f;
            int finishHits = 0, finishCuts = 0, finishOverlaps = 0;
            bool ghostSeen = false;
            float nextCsv = 0f;

            if (s.UseManager && manager != null)
            {
                manager.enabled = true;
                var cars = new List<RaceCar>();
                var ids = new List<ulong>();
                for (int i = 0; i < runs.Count; i++) { cars.Add(runs[i].Car); ids.Add((ulong)i); }
                manager.BeginRace(cars, ids, null);
                manager.OverrideLaps(1);
            }

            while (true)
            {
                yield return new WaitForFixedUpdate();
                float t = Time.time - scenarioStart;
                scenarioTime = t;

                if (s.UseManager)
                {
                    if (!released && harvester.Running) { released = true; releasedAt = t; }
                }
                else if (!released && t >= releaseDelay)
                {
                    released = true;
                    releasedAt = t;
                    harvester.SetRunning(true);
                }

                foreach (var r in runs)
                {
                    if (r.Car == null || r.Car.IsEliminated) continue;
                    if (r.Follower.WallHits != r.LastWallHits) { r.LastWallHits = r.Follower.WallHits; r.LastWallHitTime = t; }
                    r.MinSpeedBeforeCut = t - r.LastWallHitTime < 5f || r.CrashDetectTime >= 0f ? Mathf.Min(r.MinSpeedBeforeCut, r.Car.ForwardSpeed) : float.PositiveInfinity;
                }

                // Kasıtlı kaza: en arkadaki yarışan araba
                if (s.Crash != CrashKind.Yok && crashing == null && crashIndex < s.CrashAt.Length && t >= s.CrashAt[crashIndex])
                {
                    crashIndex++;
                    crashing = Rearmost();
                    if (crashing != null)
                    {
                        crashStart = t;
                        crashing.DeliberateAt = t;
                        crashWallHit = -1f;
                        crashWallBase = crashing.Follower.WallHits;
                        crashing.Follower.enabled = false;
                        if (s.Crash == CrashKind.Dur)
                        {
                            crashing.Car.Body.linearVelocity = Vector3.zero;
                            crashing.Car.Body.angularVelocity = Vector3.zero;
                        }
                        st.Events.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1} sn: {1} kasıtlı kaza ({2}), hız {3:F1} m/sn, açık {4:F1} m",
                            t, crashing.Car.name, s.Crash == CrashKind.Dur ? "1 sn durma" : "duvara vurma", crashing.Car.ForwardSpeed, harvester.OpenGapTo(crashing.Car)));
                    }
                }
                if (crashing != null)
                {
                    var car = crashing.Car;
                    bool done = car == null || car.IsEliminated;
                    if (!done && s.Crash == CrashKind.Dur)
                    {
                        car.SetExternalInput(0f, 0f, true);
                        if (t - crashStart < 0.15f) car.Body.linearVelocity = Vector3.zero;
                        if (t - crashStart >= 1f) done = true;
                    }
                    else if (!done)
                    {
                        // Uzaktaki bariyere doğru tam gaz kır (yakındakine sıyırarak değil, açılı vursun)
                        float lat = track.GetLateralOffset(car.transform.position, car.TrackDistance);
                        if (t - crashStart < 0.05f) crashing.WallSide = lat >= 0f ? -1f : 1f;
                        car.SetExternalInput(1f, crashing.WallSide, false);
                        if (crashWallHit < 0f && crashing.Follower.WallHits > crashWallBase)
                        {
                            crashWallHit = t;
                            crashing.DeliberateAt = t;
                            st.Events.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1} sn: {1} duvara çarptı", t, car.name));
                        }
                        if ((crashWallHit >= 0f && t - crashWallHit > 0.3f) || t - crashStart > 4f) done = true;
                    }
                    if (done)
                    {
                        if (car != null && !car.IsEliminated) crashing.Follower.enabled = true;
                        crashing = null;
                    }
                }
                // Ölçüm
                if (released)
                {
                    st.MaxHarvesterSpeed = Mathf.Max(st.MaxHarvesterSpeed, harvester.Speed);
                    var tgt = harvester.Target;
                    float gap = harvester.TargetGap;
                    bool valid = tgt != null && !float.IsInfinity(gap) && harvester.Running
                                 && harvester.CurrentMode != HarvesterAI.Mode.Geride && harvester.CurrentMode != HarvesterAI.Mode.HedefYok;
                    if (valid)
                    {
                        if (lastTarget != null && tgt != lastTarget && gap > harvester.Difficulty.leashMax) reacquireStart = t;
                        lastTarget = tgt;
                        bool launch = harvester.InLaunchWindow;
                        if (st.LaunchApproach < 0f && gap <= harvester.Difficulty.leashMax) st.LaunchApproach = t - releasedAt;
                        if (reacquireStart >= 0f && gap <= harvester.Difficulty.leashMax)
                        {
                            st.Reacquire.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1} sn ({1})", t - reacquireStart, tgt.name));
                            reacquireStart = -1f;
                        }
                        if (!launch && reacquireStart < 0f && harvester.CurrentMode != HarvesterAI.Mode.Saldiri)
                        {
                            st.Gaps.Add(gap);
                            st.Seconds += Time.fixedDeltaTime;
                            if (gap > harvester.Difficulty.leashMax + 0.05f) st.Over30 += Time.fixedDeltaTime;
                            if (gap >= harvester.Difficulty.pressureBand.x && gap <= harvester.Difficulty.pressureBand.y) st.InBand += Time.fixedDeltaTime;
                        }
                    }
                    if (csv != null && t >= nextCsv)
                    {
                        nextCsv = t + 0.1f;
                        csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1},{2:F2},{3:F1},{4:F1},{5:F1},{6}", t, harvester.CurrentMode,
                            valid ? gap : -1f, harvester.LeashLimit, harvester.Speed, tgt != null ? tgt.ForwardSpeed : 0f, tgt != null ? tgt.name : "-"));
                    }
                }

                // Bitiş (E)
                if (s.UseManager && manager != null)
                {
                    if (finishAt < 0f && manager.Phase == RacePhase.Finished)
                    {
                        finishAt = t;
                        finishHits = harvester.HitCount;
                        finishCuts = harvester.CutCount;
                        // Ağdaki gibi: bitişte girdi kapalı, arabalar boşta kayar
                        foreach (var r in runs)
                            if (r.Car != null && !r.Car.IsEliminated) { r.Follower.enabled = false; r.Car.SetExternalInput(0f, 0f, false); }
                        st.Events.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1} sn: yarış bitti, harvester hızı {1:F1} m/sn, açık {2:F1} m", t, harvester.Speed, NearestOpenGap()));
                    }
                    if (finishAt >= 0f)
                    {
                        foreach (var r in runs) if (r.Car != null && !r.Car.IsEliminated) r.Car.SetExternalInput(0f, 0f, false);
                        ghostSeen |= harvester.IsGhost;
                        if (OverlapsHarvester()) finishOverlaps++;
                    }
                }

                bool allCut = runs.TrueForAll(r => r.Car == null || r.Car.IsEliminated);
                int laps = 0;
                foreach (var r in runs) if (r.Follower != null) laps = Mathf.Max(laps, r.Follower.Laps);
                bool lapsDone = s.Laps > 0 && laps >= s.Laps;
                bool finishDone = finishAt >= 0f && t - finishAt > 12f;
                if (allCut || t >= s.Duration || lapsDone || finishDone) break;
            }

            harvester.OnCarCut -= onCut;
            harvester.OnCrashDetected -= onCrash;
            harvester.SetRunning(false);
            if (csv != null) csv.Dispose();
            if (s.UseManager && manager != null)
            {
                manager.enabled = false;
                RaceCar.InputEnabled = true;
            }

            // Rapor
            float total = Time.time - scenarioStart;
            Log(string.Format(CultureInfo.InvariantCulture, "süre {0:F0} sn, salındıktan ≤{1} m'ye: {2}, harvester en yüksek hız {3:F1} m/sn, tasma adımı {4}, ışınlanma {5}",
                total, harvester.Difficulty.leashMax, st.LaunchApproach >= 0f ? st.LaunchApproach.ToString("F1", CultureInfo.InvariantCulture) + " sn" : "yok",
                st.MaxHarvesterSpeed, harvester.LeashPullSteps - st.StartLeashSteps, harvester.TeleportCount - st.StartTeleports));
            if (st.Gaps.Count > 0)
            {
                var sorted = new List<float>(st.Gaps);
                sorted.Sort();
                float sum = 0f;
                foreach (float g in st.Gaps) sum += g;
                Log(string.Format(CultureInfo.InvariantCulture,
                    "açık (kalkış/yakalama/saldırı hariç {0:F0} sn): ort. {1:F1} m, p5 {2:F1}, p95 {3:F1}, en fazla {4:F1} m, en az {5:F1} m, 30 m üstünde {6:F2} sn, 15–25 m bandında %{7:F0}",
                    st.Seconds, sum / st.Gaps.Count, sorted[(int)(sorted.Count * 0.05f)], sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))],
                    sorted[sorted.Count - 1], sorted[0], st.Over30, 100f * st.InBand / Mathf.Max(0.01f, st.Seconds)));
            }
            if (st.Reacquire.Count > 0) Log("yeniden yakalama (hedef değişiminden ≤30 m'ye): " + string.Join(", ", st.Reacquire.ToArray()));
            foreach (var e in st.Events) Log("  " + e);
            if (s.UseManager)
                Log(string.Format("bitiş: {0}, hayalet {1}, bitişten sonra çarpma {2}, biçme {3}, arabanın harvester kutusuna girdiği adım {4}",
                    finishAt >= 0f ? "geldi" : "GELMEDİ", ghostSeen ? "evet" : "HAYIR", harvester.HitCount - finishHits, harvester.CutCount - finishCuts, finishOverlaps));
            int early = runs.FindAll(r => r.CutTime >= 0f && r.CutTime - Mathf.Max(0f, releasedAt) < 10f).Count;
            Log(string.Format("ilk 10 sn'de biçilen: {0}, toplam çarpma {1}", early, harvester.HitCount - st.StartHits));
            foreach (var r in runs)
                Log(string.Format(CultureInfo.InvariantCulture, "  {0} {1} (x{2:F2}): {3}, tur {4}, duvar {5}, sıfırlama {6}",
                    r.Car != null ? r.Car.name : "?", r.Car != null && r.Car.Definition != null ? r.Car.Definition.name : "", r.Follower != null ? r.Follower.SpeedScale : 0f,
                    r.CutTime >= 0f ? "biçildi " + r.CutTime.ToString("F1", CultureInfo.InvariantCulture) + ". sn" : "kurtuldu",
                    r.Follower != null ? r.Follower.Laps : 0, r.Follower != null ? r.Follower.WallHits : 0, r.Follower != null ? r.Follower.Resets : 0));
        }

        private CarRun Rearmost()
        {
            CarRun best = null;
            float bestGap = float.PositiveInfinity;
            foreach (var r in runs)
            {
                if (r.Car == null || r.Car.IsEliminated) continue;
                float g = harvester.GapTo(r.Car);
                if (g < -6f) continue;
                if (g < bestGap) { bestGap = g; best = r; }
            }
            return best;
        }

        private float NearestOpenGap()
        {
            float best = float.PositiveInfinity;
            foreach (var r in runs)
                if (r.Car != null && !r.Car.IsEliminated) best = Mathf.Min(best, harvester.OpenGapTo(r.Car));
            return best;
        }

        private readonly Collider[] overlap = new Collider[16];

        /// <summary>Herhangi bir araba harvester'ın gövde/tabla hacmine girdi mi (hayalette içinden geçme).</summary>
        private bool OverlapsHarvester()
        {
            var t = harvester.transform;
            int n = Physics.OverlapBoxNonAlloc(t.TransformPoint(new Vector3(0f, 3f, 2f)), new Vector3(4.5f, 3f, 8f), overlap, t.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (overlap[i].attachedRigidbody != null && overlap[i].attachedRigidbody.GetComponent<RaceCar>() != null) return true;
            return false;
        }

        // ------------------------------------------------------------------ İzleme kamerası (sadece test)

        private void CreateFollowCamera()
        {
            var go = new GameObject("HarvesterTestKamera");
            followCamera = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null)
            {
                followCamera.CopyFrom(main);
                followCamera.depth = main.depth + 10f; // ana kameranın üstünde çizilir, oyunun kamerasına dokunmaz
            }
            followCamera.farClipPlane = 2000f;
            viewCamera = go.AddComponent<HarvesterViewCamera>();
            viewCamera.Harvester = harvester;
            viewCamera.Mode = HarvesterViewCamera.ViewMode.Takip;
        }

        private void OnGUI()
        {
            if (!IsRunning || harvester == null || !followHarvester) return;
            var target = harvester.Target;
            float gap = harvester.TargetGap;
            string text = string.Format(CultureInfo.InvariantCulture,
                "{0}   t={1:F0} sn\nmod: {2}\naçık: {3}   tasma: {4:F0} m\nharvester: {5:F1} m/sn   hedef: {6}\nbiçilen: {7}   ışınlanma: {8}   kamera: V",
                currentScenario, scenarioTime, harvester.ModeName,
                float.IsInfinity(gap) ? "-" : gap.ToString("F1", CultureInfo.InvariantCulture) + " m",
                float.IsInfinity(harvester.LeashLimit) ? 0f : harvester.LeashLimit,
                harvester.Speed, target != null ? target.name + " " + target.ForwardSpeed.ToString("F1", CultureInfo.InvariantCulture) + " m/sn" : "-",
                harvester.CutCount, harvester.TeleportCount);
            GUI.Box(new Rect(10f, 10f, 470f, 110f), GUIContent.none);
            GUI.Label(new Rect(18f, 14f, 460f, 105f), text);
        }

        private void Cleanup()
        {
            foreach (var r in runs)
                if (r.Car != null)
                {
                    r.Car.gameObject.SetActive(false); // RaceCar.All'dan hemen çıksın
                    Destroy(r.Car.gameObject);
                }
            runs.Clear();
        }

        private void OnDisable()
        {
            IsRunning = false;
            Time.timeScale = 1f;
        }
    }
}
