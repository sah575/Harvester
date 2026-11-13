using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Harvester yapay zekâsını ağsız ölçen otomatik test (race sahnesi, geçici). Sadece editörde ve debug build'de çalışır.
    /// Arabaları CarTrackFollower ile gerçek araba fiziğiyle sürdürür; harvester'ın yakalayıp yakalamadığını, ne kadar
    /// sürede, biçip biçmediğini, takılıp takılmadığını ve bariyere ne kadar yaklaştığını ölçer.
    ///
    /// Senaryolar:
    /// 1. İyi sürücü, tek araba, harvester yaklaşınca yana kaçar.
    /// 2. İyi sürücü, kaçmadan (orta çizgide).
    /// 3. Hata yapan: 25. saniyede 1.5 sn döner (el freni + tam direksiyon), sonra devam eder.
    /// 4. Üç araba: hız çarpanı 1.0 / 0.93 / 0.85, yana kaçarlar.
    /// Sonuçlar "[HarvesterTest]" önekiyle konsola yazılır; hepsi bitince <see cref="LastReport"/> dolar.
    /// Başlatma: runOnStart, F9 ya da komut satırında -harvesterTest.
    /// </summary>
    public class HarvesterTestRunner : MonoBehaviour
    {
        public static string LastReport { get; private set; } = "";
        public static bool IsRunning { get; private set; }

        [Tooltip("Oyun başlayınca testi kendiliğinden başlat")]
        [SerializeField] private bool runOnStart = true;
        [Tooltip("Araba prefab'ı (RaceCar)")]
        [SerializeField] private GameObject carPrefab;
        [Tooltip("Sahnedeki harvester (boşsa HarvesterAI.Instance, o da yoksa prefab'dan kurulur)")]
        [SerializeField] private HarvesterAI harvester;
        [SerializeField] private GameObject harvesterPrefab;
        [Tooltip("Arabalar kalktıktan kaç saniye sonra harvester salınır (RaceManager.harvesterDelay ile aynı tutun)")]
        [SerializeField] private float releaseDelay = 1f;
        [Tooltip("Senaryo başına en uzun süre (sn)")]
        [SerializeField] private float scenarioTimeout = 120f;
        [Tooltip("Ara ölçüm aralığı (sn)")]
        [SerializeField] private float sampleInterval = 5f;
        [Tooltip("Sadece bu senaryoları çalıştır: virgülle sıra numaraları (0'dan), boş = hepsi. Ör. \"5,7,8\"")]
        [SerializeField] private string onlyScenarios = "";
        [Tooltip("Harvester bu kadar yakınken (m) yana kaçan sürücüler kaçar")]
        [SerializeField] private float dodgeDistance = 40f;

        private class Scenario
        {
            public string Name;
            public float[] SpeedScales;
            public float[] Lanes;
            public bool Dodge;
            public float MistakeAt = -1f;      // bu saniyeden sonra, harvester MistakeGap'ten yakınken döner
            public float MistakeGap = 60f;
            public float MistakeDuration = 1.5f;
            public float HarvesterAhead;       // >0: harvester arabaların bu kadar önünden başlar (çarpma testi), <0: bu kadar gerisinden (ışınlanma testi)
            public float MaxLateralAccel = -1f; // >0: sürücünün viraj sınırı (uzman ~15)
            public float PlanningBrake = -1f;   // >0: sürücünün fren planı
            public float Duration = -1f;        // >0: bu senaryonun süresi (sn)
            public float TimeLoss = -1f;        // >0: saf zaman kaybı testi: araba pist boyunca TimeLoss × hız kadar geri alınır (hız korunur)
            public float TimeLossAfter = 20f;   // en erken bu saniyede
            public float TimeLossTriggerGap = 0.8f; // ve zaman açığı bunun altındayken (sn)
        }

        private class CarRun
        {
            public RaceCar Car;
            public CarTrackFollower Follower;
            public float BaseLane;
            public float CutTime = -1f;
            public int Hits;
            public float MinGap = float.PositiveInfinity;
            public bool Mistaken;
            public float MistakeStart = -1f;
            public bool MistakeEndLogged;
        }

        private readonly StringBuilder report = new StringBuilder();
        private Camera followCamera;
        private HarvesterViewCamera viewCamera;
        private string currentScenario = "";
        [Tooltip("Test sırasında Game ekranında harvester'ı izleyen kamera ve özet")]
        [SerializeField] private bool followHarvester = true;
        private readonly List<CarRun> runs = new List<CarRun>();
        private float scenarioStart;

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool commandLine = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-harvesterTest") >= 0;
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

        public IEnumerator Run()
        {
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
            // Sahnede başka arabalar varsa ölçümü bozmasın
            foreach (var other in new List<RaceCar>(RaceCar.All)) other.gameObject.SetActive(false);
            // Sahnedeki yarış yöneticisi ağsız kendi yarışını başlatır (girdiyi kapatır, harvester'ı durdurur): test boyunca kapat
            var manager = RaceManager.Instance != null ? RaceManager.Instance : FindAnyObjectByType<RaceManager>();
            if (manager != null)
            {
                manager.CancelInvoke();
                manager.enabled = false;
            }
            RaceCar.InputEnabled = true;

            IsRunning = true;
            report.Length = 0;
            if (followHarvester) CreateFollowCamera();
            var d = harvester.Difficulty;
            Log(string.Format("başladı: pist {0:F0} m ({1}), genişlik {2:F0} m, zorluk '{3}': baskı tavanı {4} m/sn, tavan 65 m açıkta {5:F0} / 140 m'de {6:F0} m/sn, yanal ivme {7} m/sn², tabla {8} m, yanal sınır ±{9:F1} m, maxGap {10} m, görüş {11:F0} m, salma gecikmesi {12} sn",
                track.Length, track.Loop ? "turlu" : "A→B", track.Width, d.displayName, d.pressureSpeedCap, d.SpeedCapForGap(65f), d.SpeedCapForGap(140f),
                d.lateralAcceleration, d.headerWidth, harvester.LateralLimit, d.maxGap, harvester.VisibleDistance, releaseDelay));

            var scenarios = new[]
            {
                new Scenario { Name = "iyi sürücü, yana kaçar", SpeedScales = new[] { 1f }, Lanes = new[] { 0f }, Dodge = true },
                new Scenario { Name = "iyi sürücü, kaçmaz", SpeedScales = new[] { 1f }, Lanes = new[] { 0f }, Dodge = false },
                new Scenario { Name = "hata yapan (15. sn'den sonra harvester 60 m'deyken 1.5 sn döner)", SpeedScales = new[] { 1f }, Lanes = new[] { 0f }, Dodge = true, MistakeAt = 15f },
                new Scenario { Name = "üç araba (1.0/0.93/0.85)", SpeedScales = new[] { 1f, 0.93f, 0.85f }, Lanes = new[] { -3f, 3f, 0f }, Dodge = true },
                new Scenario { Name = "yandan geçme (çarpma), harvester 60 m önde", SpeedScales = new[] { 1f }, Lanes = new[] { 5f }, Dodge = false, HarvesterAhead = 60f },
                new Scenario { Name = "uzman sürücü (gaz kökte, viraj 15 m/sn², hata yok)", SpeedScales = new[] { 1f }, Lanes = new[] { 0f }, Dodge = false,
                               MaxLateralAccel = 15f, PlanningBrake = 18f, Duration = 180f },
                new Scenario { Name = "uzman sürücü, yana kaçar", SpeedScales = new[] { 1f }, Lanes = new[] { 0f }, Dodge = true,
                               MaxLateralAccel = 15f, PlanningBrake = 18f, Duration = 180f },
                LossScenario(0.3f), LossScenario(0.5f), LossScenario(0.8f), LossScenario(1.2f),
                new Scenario { Name = "uzaktan başlama (500 m geride, ışınlanma testi)", SpeedScales = new[] { 1f }, Lanes = new[] { 0f },
                               MaxLateralAccel = 15f, PlanningBrake = 18f, HarvesterAhead = -500f, Duration = 45f },
            };
            var selected = new HashSet<int>();
            foreach (string part in (onlyScenarios ?? "").Split(','))
            {
                int index;
                if (int.TryParse(part.Trim(), out index)) selected.Add(index);
            }
            for (int i = 0; i < scenarios.Length; i++)
                if (selected.Count == 0 || selected.Contains(i)) yield return RunScenario(track, scenarios[i]);

            Cleanup();
            IsRunning = false;
            if (followCamera != null) Destroy(followCamera.gameObject);
            Log("bitti");
            LastReport = report.ToString();
        }

        private static Scenario LossScenario(float loss)
        {
            return new Scenario
            {
                Name = "zaman kaybı " + loss.ToString("F1") + " sn (uzman)", SpeedScales = new[] { 1f }, Lanes = new[] { 0f },
                MaxLateralAccel = 15f, PlanningBrake = 18f, TimeLoss = loss, Duration = 100f
            };
        }

        private IEnumerator RunScenario(RaceTrack track, Scenario scenario)
        {
            currentScenario = scenario.Name;
            Cleanup();
            harvester.SetRunning(false);
            harvester.PlaceAtStart(scenario.SpeedScales.Length);
            if (scenario.HarvesterAhead != 0f)
            {
                float hd = track.StartDistance + scenario.HarvesterAhead;
                harvester.PlaceAt(track.GetPoint(hd), Quaternion.LookRotation(track.GetForward(hd)));
            }

            for (int i = 0; i < scenario.SpeedScales.Length; i++)
            {
                Pose pose = track.GetStartPose(i);
                var go = Instantiate(carPrefab, pose.position + Vector3.up * 0.3f, pose.rotation);
                go.name = "TestAraba" + i;
                var car = go.GetComponent<RaceCar>();
                var follower = CarTrackFollower.Attach(car);
                follower.SpeedScale = scenario.SpeedScales[i];
                follower.LaneOffset = scenario.Lanes[i];
                follower.LogEvents = false;
                if (scenario.MaxLateralAccel > 0f) follower.MaxLateralAccel = scenario.MaxLateralAccel;
                if (scenario.PlanningBrake > 0f) follower.PlanningBrake = scenario.PlanningBrake;
                runs.Add(new CarRun { Car = car, Follower = follower, BaseLane = scenario.Lanes[i] });
            }

            System.Action<RaceCar> onCut = c =>
            {
                var r = runs.Find(x => x.Car == c);
                if (r != null && r.CutTime < 0f) r.CutTime = Time.time - scenarioStart;
            };
            System.Action<RaceCar, Vector3> onHit = (c, v) =>
            {
                var r = runs.Find(x => x.Car == c);
                if (r != null) r.Hits++;
            };
            harvester.OnCarCut += onCut;
            harvester.OnCarHit += onHit;

            Log("--- senaryo: " + scenario.Name);
            scenarioStart = Time.time;
            float nextSample = sampleInterval;
            float stuckTimer = 0f, maxStuck = 0f, maxLateral = 0f, speedSum = 0f, maxSpeed = 0f;
            int speedSamples = 0, clampStart = harvester.BarrierClampSteps;
            bool released = false;
            // Kalkış ölçümü: yarış başından harvester'ın en arkadaki ve en öndeki arabaya baskı mesafesine girmesine kadar
            float pressureGap = harvester.Difficulty.pressureDistance + harvester.HeaderReach;
            float launchLast = -1f, launchLead = -1f, launchEnd = -1f;
            // Açık istatistiği: harvester'ın önündeki en yakın arabaya açık (kökten), her fizik adımında
            var gaps = new List<float>();
            var timeGaps = new List<float>();
            float lossAt = -1f, lossTau = 0f, lossEnd = -1f;
            bool lossReleased = false;
            Vector3 frozenVelocity = Vector3.zero, frozenAngular = Vector3.zero;
            float over120 = 0f, inBand = 0f;
            int teleportStart = harvester.TeleportCount;
            float timeout = scenario.Duration > 0f ? scenario.Duration : scenarioTimeout;

            while (true)
            {
                yield return new WaitForFixedUpdate();
                float t = Time.time - scenarioStart;

                if (!released && t >= releaseDelay)
                {
                    released = true;
                    harvester.SetRunning(true);
                }

                // Sürücü davranışları: yana kaçma ve hata
                float hLateral = track.GetLateralOffset(harvester.transform.position, harvester.TrackDistance);
                foreach (var r in runs)
                {
                    if (r.Car == null || r.Car.IsEliminated) continue;
                    float gap = harvester.GapTo(r.Car);
                    if (released) r.MinGap = Mathf.Min(r.MinGap, gap);

                    if (scenario.MistakeAt >= 0f && !r.Mistaken && t >= scenario.MistakeAt && gap <= scenario.MistakeGap)
                    {
                        r.Mistaken = true;
                        r.MistakeStart = t;
                        Log(string.Format("{0:F1} sn: {1} döndü (açık {2:F0} m, hız {3:F1}, harvester {4:F1})", t, r.Car.name, gap, r.Car.ForwardSpeed, harvester.Speed));
                    }
                    if (r.MistakeStart >= 0f && t < r.MistakeStart + scenario.MistakeDuration)
                    {
                        r.Follower.enabled = false;
                        r.Car.SetExternalInput(0.3f, 1f, true);
                        continue;
                    }
                    if (r.MistakeStart >= 0f && !r.MistakeEndLogged)
                    {
                        r.MistakeEndLogged = true;
                        Log(string.Format("{0:F1} sn: {1} toparlandı (açık {2:F0} m, hız {3:F1})", t, r.Car.name, gap, r.Car.ForwardSpeed));
                    }
                    if (!r.Follower.enabled) r.Follower.enabled = true;

                    if (scenario.Dodge && gap < dodgeDistance && gap > -5f)
                    {
                        float carLateral = track.GetLateralOffset(r.Car.transform.position, r.Car.TrackDistance);
                        float side = Mathf.Abs(hLateral) > 0.5f ? -Mathf.Sign(hLateral) : (carLateral >= 0f ? 1f : -1f);
                        r.Follower.LaneOffset = side * track.Width * 0.38f;
                    }
                    else r.Follower.LaneOffset = r.BaseLane;
                }

                if (released && scenario.HarvesterAhead <= 0f)
                {
                    float nearest = float.PositiveInfinity, farthest = float.NegativeInfinity;
                    foreach (var r in runs)
                    {
                        if (r.Car == null || r.Car.IsEliminated) continue;
                        float g = harvester.GapTo(r.Car);
                        if (g < -5f || g > 300f) continue;
                        nearest = Mathf.Min(nearest, g);
                        farthest = Mathf.Max(farthest, g);
                    }
                    if (launchLast < 0f && nearest <= pressureGap) launchLast = t;
                    if (launchLead < 0f && farthest > float.NegativeInfinity && farthest <= pressureGap) launchLead = t;
                    if (launchEnd < 0f && !harvester.Launching) launchEnd = t;
                }

                if (released && scenario.HarvesterAhead <= 0f)
                {
                    float g = harvester.NearestAheadGap;
                    float tau = harvester.TimeGap;
                    if (!float.IsInfinity(tau) && !harvester.InLaunchWindow) timeGaps.Add(tau);
                    if (!float.IsInfinity(g) && g < 1000f)
                    {
                        gaps.Add(g);
                        if (g > 120f) over120 += Time.fixedDeltaTime;
                        if (g >= 20f && g <= 60f) inBand += Time.fixedDeltaTime;
                    }
                }

                if (released)
                {
                    float v = harvester.Speed;
                    speedSum += v;
                    speedSamples++;
                    maxSpeed = Mathf.Max(maxSpeed, v);
                    maxLateral = Mathf.Max(maxLateral, Mathf.Abs(hLateral));
                    if (v < 2f && harvester.Target != null) stuckTimer += Time.fixedDeltaTime; else stuckTimer = 0f;
                    maxStuck = Mathf.Max(maxStuck, stuckTimer);
                }

                if (t >= nextSample)
                {
                    nextSample += sampleInterval;
                    var line = new StringBuilder();
                    line.AppendFormat("{0,5:F0} sn: harvester {1,4:F1} m/sn, mesafe {2,5:F0}, hedef {3}", t, harvester.Speed, harvester.TrackDistance,
                        harvester.Target != null ? harvester.Target.name : "-");
                    foreach (var r in runs)
                    {
                        if (r.Car == null) continue;
                        if (r.Car.IsEliminated) line.AppendFormat(" | {0}: biçildi", r.Car.name);
                        else line.AppendFormat(" | {0}: açık {1:F0} m, {2:F1} m/sn", r.Car.name, harvester.GapTo(r.Car), r.Car.ForwardSpeed);
                    }
                    Log(line.ToString());
                }

                // Saf zaman kaybı: araba TimeLoss saniye boyunca yerinde donar (kinematik), sonra aynı hızla devam eder.
                // Kaybedilen mesafe tam olarak TimeLoss × hız; harvester bunu gerçek bir hata gibi (hedef birden durdu) görür.
                if (scenario.TimeLoss > 0f && lossAt < 0f && t >= scenario.TimeLossAfter && runs.Count > 0 && runs[0].Car != null
                    && !runs[0].Car.IsEliminated && harvester.Target == runs[0].Car && harvester.TimeGap <= scenario.TimeLossTriggerGap)
                {
                    var car = runs[0].Car;
                    frozenVelocity = car.Body.linearVelocity;
                    frozenAngular = car.Body.angularVelocity;
                    car.Body.linearVelocity = Vector3.zero;
                    car.Body.angularVelocity = Vector3.zero;
                    car.Body.isKinematic = true;
                    lossAt = t;
                    lossTau = harvester.TimeGap;
                    Log(string.Format("{0:F1} sn: {1:F1} sn zaman kaybı (donma, {2:F0} m), olay anında zaman açığı {3:F2} sn ({4:F0} m kökten), hız {5:F1}, harvester {6:F1}",
                        t, scenario.TimeLoss, scenario.TimeLoss * frozenVelocity.magnitude, lossTau, harvester.NearestAheadGap, frozenVelocity.magnitude, harvester.Speed));
                }
                if (lossAt >= 0f && !lossReleased && t >= lossAt + scenario.TimeLoss && runs[0].Car != null)
                {
                    lossReleased = true;
                    var car = runs[0].Car;
                    if (!car.IsEliminated)
                    {
                        car.Body.isKinematic = false;
                        car.Body.linearVelocity = frozenVelocity;
                        car.Body.angularVelocity = frozenAngular;
                    }
                }
                if (lossAt >= 0f && lossEnd < 0f && t >= lossAt + 15f) lossEnd = t;

                bool allCut = runs.TrueForAll(r => r.Car == null || r.Car.IsEliminated);
                if (allCut || t >= timeout || lossEnd >= 0f) break;
            }

            harvester.OnCarCut -= onCut;
            harvester.OnCarHit -= onHit;
            harvester.SetRunning(false);

            float total = Time.time - scenarioStart;
            if (scenario.HarvesterAhead <= 0f)
                Log(string.Format("kalkış ({0}): harvester {1:F1} sn gecikmeyle salındı; en arkadaki arabaya baskı mesafesi ({2:F0} m) {3}, en öndekine {4}, atak bitişi {5}",
                    scenario.Name, releaseDelay, pressureGap, FormatTime(launchLast), FormatTime(launchLead), FormatTime(launchEnd)));
            if (gaps.Count > 0)
            {
                var sorted = new List<float>(gaps);
                sorted.Sort();
                float sum = 0f;
                foreach (float g in gaps) sum += g;
                float seconds = gaps.Count * Time.fixedDeltaTime;
                Log(string.Format("açık ({0}): ort. {1:F0} m, p95 {2:F0} m, en fazla {3:F0} m, 120 m üstünde {4:F1} sn, 20–60 m bandında %{5:F0}, ışınlanma {6}",
                    scenario.Name, sum / gaps.Count, sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))], sorted[sorted.Count - 1],
                    over120, 100f * inBand / Mathf.Max(0.01f, seconds), harvester.TeleportCount - teleportStart));
            }
            if (timeGaps.Count > 0)
            {
                var sortedTau = new List<float>(timeGaps);
                sortedTau.Sort();
                float sumTau = 0f;
                int inTauBand = 0;
                foreach (float x in timeGaps)
                {
                    sumTau += Mathf.Min(x, 10f);
                    if (x <= harvester.Difficulty.timeGapBandHigh) inTauBand++;
                }
                Log(string.Format("zaman açığı ({0}): ort. {1:F2} sn, p5 {2:F2}, medyan {3:F2}, p95 {4:F2}, en fazla {5:F2}, bantta (≤{6:F1} sn) %{7:F0}",
                    scenario.Name, sumTau / timeGaps.Count, sortedTau[(int)(sortedTau.Count * 0.05f)], sortedTau[sortedTau.Count / 2],
                    sortedTau[Mathf.Min(sortedTau.Count - 1, (int)(sortedTau.Count * 0.95f))], sortedTau[sortedTau.Count - 1],
                    harvester.Difficulty.timeGapBandHigh, 100f * inTauBand / timeGaps.Count));
            }
            if (scenario.TimeLoss > 0f)
            {
                var r0 = runs.Count > 0 ? runs[0] : null;
                if (lossAt < 0f) Log(string.Format("zaman kaybı {0:F1} sn: TETİKLENMEDİ (zaman açığı {1:F1} sn altına inmedi)", scenario.TimeLoss, scenario.TimeLossTriggerGap));
                else if (r0 != null && r0.CutTime >= lossAt)
                    Log(string.Format("zaman kaybı {0:F1} sn (olayda açık {1:F2} sn): BİÇİLDİ, {2:F1} sn sonra", scenario.TimeLoss, lossTau, r0.CutTime - lossAt));
                else Log(string.Format("zaman kaybı {0:F1} sn (olayda açık {1:F2} sn): kurtuldu (15 sn izlendi)", scenario.TimeLoss, lossTau));
            }
            int earlyCuts = runs.FindAll(r => r.CutTime >= 0f && r.CutTime < 10f).Count;
            if (scenario.HarvesterAhead <= 0f && scenario.MistakeAt < 0f)
                Log(string.Format("ilk 10 sn'de biçilen: {0}", earlyCuts));
            Log(string.Format("sonuç ({0}): {1:F0} sn, harvester ort. {2:F1} m/sn, en yüksek {3:F1}, en büyük yanal sapma {4:F1} m (sınır {5:F1}), bariyer kırpma adımı {6}, en uzun takılma {7:F1} sn",
                scenario.Name, total, speedSamples > 0 ? speedSum / speedSamples : 0f, maxSpeed, maxLateral, harvester.LateralLimit,
                harvester.BarrierClampSteps - clampStart, maxStuck));
            foreach (var r in runs)
            {
                Log(string.Format("  {0} (hız x{1:F2}): {2}, en küçük açık {3:F0} m, çarpma {4}, bariyer {5}, sıfırlama {6}",
                    r.Car != null ? r.Car.name : "?", r.Follower != null ? r.Follower.SpeedScale : 0f,
                    r.CutTime >= 0f ? "biçildi " + r.CutTime.ToString("F1") + " sn'de" + (r.MistakeStart >= 0f ? " (hatadan " + (r.CutTime - r.MistakeStart).ToString("F1") + " sn sonra)" : "") : "kurtuldu",
                    r.MinGap, r.Hits, r.Follower != null ? r.Follower.WallHits : 0, r.Follower != null ? r.Follower.Resets : 0));
            }
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
            viewCamera.Mode = HarvesterViewCamera.ViewMode.Kabin;
        }

        private void OnGUI()
        {
            if (!IsRunning || harvester == null || !followHarvester) return;
            var target = harvester.Target;
            float tau = harvester.TimeGap;
            float gap = harvester.NearestAheadGap;
            string text = string.Format("{0}\nmod: {1}\nzaman açığı: {2} ({3})\nharvester: {4:F1} m/sn   hedef: {5}\nışınlanma: {6}   biçilen: {7}   kamera: {8} (V)",
                currentScenario, harvester.ModeName,
                float.IsInfinity(tau) ? "-" : tau.ToString("F2") + " sn",
                float.IsInfinity(gap) ? "-" : gap.ToString("F0") + " m kökten",
                harvester.Speed, target != null ? target.ForwardSpeed.ToString("F1") + " m/sn" : "-",
                harvester.TeleportCount, harvester.CutCount, viewCamera != null ? viewCamera.Mode.ToString().ToLowerInvariant() : "-");
            GUI.Box(new Rect(10f, 10f, 420f, 110f), GUIContent.none);
            GUI.Label(new Rect(18f, 14f, 410f, 105f), text);
        }

        private static string FormatTime(float t)
        {
            return t >= 0f ? t.ToString("F1") + ". sn" : "yok";
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
        }
    }
}
