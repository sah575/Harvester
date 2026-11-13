using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Yarışın otomatik testi (sadece editör ve development build). RaceManager kurar; komut satırında
    /// -autoHost / -autoJoin / -raceTest varsa ya da editörde EditorPrefs "Harvester.RaceTestArgs" doluysa çalışır
    /// (editörde bayraklar oradan okunur, ör. "-autoDrive -autoRam rear -autoRamSwitch 3").
    ///   -autoDrive           araba pisti CarTrackFollower ile kendi şeridinde sürer
    ///   -autoSpeedScale K    sürücünün hız çarpanı (0.2–1, varsayılan 0.7)
    ///   -autoRam MOD        MOD: rear (arkadan), side (yandan), rub (yüksek hızda yan yana sürtünme), corner (virajda kapı
    ///                        kapıya), squeeze (bariyere sıkıştırma), pit (arka çamurluğa çapraz), harvester (harvester'a yanaşma)
    ///                        Çarpma yarışın 5. sn'sinden sonra başlar; her denemeden sonra sürücü birkaç sn şeridine döner.
    ///                        -autoDrive'ı da açar. Ağsızda ilk (yerel) araba sadece sürer, diğerleri çarpar.
    ///   -autoLane L          çarpmayan sürücülerin şeridi (ör. 0: hepsi ortada, kalkışta sürü testi)
    ///   -autoBots N          (sunucu) N test botu ekler; host sürer (RaceSpawner)
    ///   -autoNoHarvester     (sunucu) harvester oluşturulmaz
    ///   -autoRamSwitch N     N vuruştan sonra arkadan ↔ yandan geçer (tek koşuda iki senaryo)
    ///   -autoElim S          (sunucu) yarış başladıktan S sn sonra son sıradaki arabayı eler
    ///   -autoHarvesterDelay S (sunucu) harvester yarışın S. saniyesine kadar durdurulur (çarpışma testi harvestersız)
    ///   -autoLaps N          (sunucu) tur sayısını değiştirir
    ///   -autoTrace           her karede (en çok 60 Hz) bütün arabaların konumunu trace_race_host|client.csv'ye yazar
    ///   -autoShot            ekran görüntüleri: HUD (yarışın 4. sn'si), ilk çarpışma, kaput kamerası, izleyici kamerası
    /// Her 2 sn'de "[AutoTest] yarış ..." satırı: faz, sıralama, konumlar, vuruş sayısı.
    /// Çarpışmalar ayrıca CarContactSolver'da "[CarHit]" önekiyle loglanır.
    /// </summary>
    [DefaultExecutionOrder(2000)] // konum kaydı RemoteExtrapolator'dan sonra
    public class RaceAutoTest : MonoBehaviour
    {
        private enum RamMode { None, Rear, Side, Rub, Corner, Squeeze, Pit, Harvester }

        private class Driver
        {
            public RaceCar Car;
            public CarTrackFollower Follower;
            public RamMode Ram;
            public float BackOffUntil;
            public int LastHits;
            public int HitsSinceSwitch;
            public int SideSign = 1;
            public bool Ramming;
            // Sürtünme/sıkıştırma durumu
            public bool Pressing;
            public float PressStart;
            public float CurrentLateral;
            public int PressSide;
            // Harvester hedefi için hız tahmini
            public Vector3 LastTargetPos;
            public Vector3 TargetVelocity;
        }

        private bool drive;
        private float speedScale = 0.7f;
        private RamMode ram;
        private int ramSwitch;
        private float elimAfter = -1f;
        private bool elimDone;
        private int lapsOverride;
        private float harvesterHold = -1f;
        private float laneOverride = float.NaN;
        private bool shots;
        private string role = "yerel";
        private StreamWriter trace;
        private double lastTraceMs;
        private float timer;
        private readonly List<Driver> drivers = new List<Driver>();

        // Ekran görüntüleri
        private bool hudShot, hitShot, hoodShot, spectatorShot, contactShot;
        private float hitShotAt = -1f, hoodShotAt = -1f, spectatorShotAt = -1f;

        public static void EnsureCreated()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = new List<string>(Environment.GetCommandLineArgs());
            bool wanted = args.Contains("-autoHost") || args.Contains("-autoJoin") || args.Contains("-raceTest");
#if UNITY_EDITOR
            string extra = UnityEditor.EditorPrefs.GetString("Harvester.RaceTestArgs", "");
            if (!string.IsNullOrEmpty(extra))
            {
                args.AddRange(extra.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                wanted = true;
            }
#endif
            if (!wanted || FindFirstObjectByType<RaceAutoTest>() != null) return;
            var go = new GameObject("RaceAutoTest");
            go.AddComponent<RaceAutoTest>().Configure(args.ToArray());
#endif
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private void Configure(string[] args)
        {
            drive = Array.IndexOf(args, "-autoDrive") >= 0;
            float f;
            int n;
            if (float.TryParse(Arg(args, "-autoSpeedScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) speedScale = Mathf.Clamp(f, 0.2f, 1f);
            if (Array.IndexOf(args, "-autoRam") >= 0)
            {
                switch (Arg(args, "-autoRam"))
                {
                    case "side": ram = RamMode.Side; break;
                    case "rub": ram = RamMode.Rub; break;
                    case "corner": ram = RamMode.Corner; break;
                    case "squeeze": ram = RamMode.Squeeze; break;
                    case "pit": ram = RamMode.Pit; break;
                    case "harvester": ram = RamMode.Harvester; break;
                    default: ram = RamMode.Rear; break;
                }
                drive = true;
            }
            if (int.TryParse(Arg(args, "-autoRamSwitch"), out n)) ramSwitch = n;
            if (float.TryParse(Arg(args, "-autoElim"), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) elimAfter = f;
            if (int.TryParse(Arg(args, "-autoLaps"), out n)) lapsOverride = n;
            if (float.TryParse(Arg(args, "-autoLane"), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) laneOverride = f;
            if (float.TryParse(Arg(args, "-autoHarvesterDelay"), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) harvesterHold = f;
            shots = Array.IndexOf(args, "-autoShot") >= 0;
            if (Array.IndexOf(args, "-autoHost") >= 0) role = "host";
            else if (Array.IndexOf(args, "-autoJoin") >= 0) role = "client";
            if (Array.IndexOf(args, "-autoTrace") >= 0)
            {
                string path = Path.Combine(Application.persistentDataPath, "trace_race_" + role + ".csv");
                trace = new StreamWriter(path, false) { AutoFlush = true };
                trace.WriteLine("ms,st,car,local,x,y,z,yaw,vx,vz,elim");
                Debug.Log("[AutoTest] Yarış konum kaydı: " + path);
            }
            Debug.Log("[AutoTest] Yarış testi: sürüş=" + drive + " hızÇarpanı=" + speedScale + " çarpma=" + ram + " geçiş=" + ramSwitch + " eleme=" + elimAfter + " tur=" + lapsOverride + " harvesterBekle=" + harvesterHold);
        }

        private void OnDestroy()
        {
            if (trace != null) trace.Dispose();
        }

        private void Update()
        {
            var manager = RaceManager.Instance;
            if (lapsOverride > 0 && manager != null) manager.OverrideLaps(lapsOverride);
            if (harvesterHold > 0f && manager != null && manager.IsAuthority && manager.Phase == RacePhase.Racing)
            {
                if (manager.RaceTime < harvesterHold) HarvesterLink.SetRunning(false);
                else if (harvesterHold > 0f) { HarvesterLink.SetRunning(true); harvesterHold = 0f; Debug.Log("[AutoTest] Harvester bırakıldı"); }
            }
            UpdateDrivers(manager);
            UpdateElimination(manager);
            UpdateScreenshots(manager);

            timer += Time.unscaledDeltaTime;
            if (timer < 2f || manager == null) return;
            timer = 0f;
            var sb = new StringBuilder("[AutoTest] yarış faz=").Append(manager.Phase)
                .Append(" süre=").Append(manager.RaceTime.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" vuruş=").Append(CarContactSolver.HitCount);
            foreach (var s in manager.Standings)
                sb.Append(" | ").Append(s.Position).Append(". ").Append(s.Name).Append('(').Append(s.ClientId).Append(") ")
                  .Append(s.Status).Append(" tur=").Append(s.Lap).Append(" ilerleme=").Append(s.Progress.ToString("F0", CultureInfo.InvariantCulture));
            foreach (var car in RaceCar.All)
            {
                if (car == null) continue;
                sb.Append(" | Araba(").Append(Label(car)).Append(car == RaceCar.Local ? ",ben" : "")
                  .Append(") ").Append(car.transform.position.ToString("F1")).Append(" v=").Append((car.Velocity.magnitude * 3.6f).ToString("F0", CultureInfo.InvariantCulture))
                  .Append(car.IsEliminated ? " ELENDİ" : "");
            }
            Transform harvester = manager.HarvesterTransform;
            if (harvester != null) sb.Append(" | Harvester ").Append(harvester.position.ToString("F1"));
            Debug.Log(sb.ToString());
        }

        private static string Label(RaceCar car)
        {
            if (!car.IsSpawned) return car.name;
            return car.NetworkObject.IsPlayerObject ? "sahip=" + car.OwnerClientId : "bot" + car.NetworkObjectId;
        }

        // ------------------------------------------------------------------ Test sürücüleri

        private void UpdateDrivers(RaceManager manager)
        {
            if (!drive) return;
            // Sürülecek arabalar: ağda kendi arabamız, ağsızda hepsi
            for (int i = 0; i < RaceCar.All.Count; i++)
            {
                RaceCar car = RaceCar.All[i];
                if (car == null || !car.DrivesLocally || drivers.Exists(d => d.Car == car)) continue;
                var driver = new Driver { Car = car, Follower = CarTrackFollower.Attach(car) };
                bool offline = !car.IsSpawned;
                // Ağsızda ilk (yerel) araba önde sürer, diğerleri çarpar
                driver.Ram = offline && car == RaceCar.Local ? RamMode.None : ram;
                ulong lane = car.IsSpawned ? car.OwnerClientId : (ulong)drivers.Count;
                driver.Follower.LaneOffset = (lane % 2 == 0 ? -1f : 1f) * (RaceTrack.Current != null ? RaceTrack.Current.Width * 0.2f : 2f);
                driver.Follower.SpeedScale = driver.Ram == RamMode.None ? speedScale : 1f;
                drivers.Add(driver);
                Debug.Log("[AutoTest] Test sürücüsü: " + Label(car) + " çarpma=" + driver.Ram + " şerit=" + driver.Follower.LaneOffset.ToString("F1", CultureInfo.InvariantCulture));
            }
            drivers.RemoveAll(d => d.Car == null);

            bool racing = manager == null || manager.Phase == RacePhase.Racing;
            foreach (var d in drivers) UpdateDriver(d, manager, racing);
        }

        private void UpdateDriver(Driver d, RaceManager manager, bool racing)
        {
            RaceCar car = d.Car;
            if (car.IsEliminated) return;
            var track = RaceTrack.Current;
            if (!float.IsNaN(laneOverride) && d.Ram == RamMode.None) d.Follower.LaneOffset = laneOverride;

            int hits = CarContactSolver.HitsOn(car);
            bool newHit = hits != d.LastHits;
            d.LastHits = hits;

            // Hedef: en yakın araba, harvester modunda harvester
            Transform target = null;
            RaceCar targetCar = null;
            if (d.Ram != RamMode.None && racing && manager != null && manager.RaceTime > 5f && Time.time >= d.BackOffUntil && track != null)
            {
                if (d.Ram == RamMode.Harvester) target = manager.HarvesterTransform;
                else
                {
                    targetCar = Nearest(car);
                    if (targetCar != null) target = targetCar.transform;
                }
                if (target != null && Vector3.Distance(target.position, car.transform.position) > 150f) { target = null; targetCar = null; }
            }
            if (target != null && d.Ram == RamMode.Corner && !d.Pressing && !BendAhead(track, car.TrackDistance)) { target = null; targetCar = null; }

            bool ramming = target != null;
            if (ramming != d.Ramming)
            {
                d.Ramming = ramming;
                d.Pressing = false;
                if (ramming)
                {
                    d.LastTargetPos = target.position;
                    d.TargetVelocity = Vector3.zero;
                    Debug.Log("[AutoTest] Çarpma denemesi: " + Label(car) + " → " + (targetCar != null ? Label(targetCar) : "harvester") + " (" + d.Ram + ")");
                }
            }
            float halfRoad = track != null ? track.Width * 0.5f - 1.6f : 4f;
            if (!ramming)
            {
                d.Follower.enabled = true;
                if (d.Ram != RamMode.None)
                {
                    d.Follower.SpeedScale = 1f;
                    d.Follower.LaneOffset = d.SideSign * halfRoad * 0.5f;
                }
                return;
            }

            // Hedefin durumu
            Vector3 tVel;
            float tDist;
            if (targetCar != null)
            {
                tVel = targetCar.Velocity;
                tDist = targetCar.TrackDistance;
            }
            else
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-3f);
                d.TargetVelocity = Vector3.Lerp(d.TargetVelocity, (target.position - d.LastTargetPos) / dt, 1f - Mathf.Exp(-dt / 0.2f));
                d.LastTargetPos = target.position;
                tVel = d.TargetVelocity;
                tDist = track.GetNearestDistance(target.position, car.TrackDistance, 80f);
            }
            float gap = Wrap(track, tDist - car.TrackDistance); // artı: hedef önümüzde
            float tLat = track.GetLateralOffset(target.position, tDist);
            float myLat = track.GetLateralOffset(car.transform.position, car.TrackDistance);
            Vector3 tf = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            Vector3 tr = Vector3.Cross(Vector3.up, tf);
            float tV = Vector3.Dot(tVel, tf);

            switch (d.Ram)
            {
                case RamMode.Rub:
                case RamMode.Corner:
                case RamMode.Squeeze:
                case RamMode.Harvester:
                    Rub(d, track, manager, gap, tLat, myLat, tV, halfRoad, targetCar != null ? 2.16f : 4.5f);
                    return;
                case RamMode.Pit:
                    Pit(d, track, target, tf, tr, gap, tLat, tV, halfRoad, newHit);
                    return;
            }

            // Arkadan / yandan tek vuruş
            if (newHit)
            {
                d.BackOffUntil = Time.time + 4f;
                d.SideSign = -d.SideSign;
                d.HitsSinceSwitch++;
                if (ramSwitch > 0 && d.HitsSinceSwitch >= ramSwitch)
                {
                    d.HitsSinceSwitch = 0;
                    d.Ram = d.Ram == RamMode.Rear ? RamMode.Side : RamMode.Rear;
                    Debug.Log("[AutoTest] Çarpma türü değişti: " + Label(car) + " → " + d.Ram);
                }
                return;
            }
            bool far = d.Ram == RamMode.Rear ? (gap > 9f || gap < -1f) : (gap > 9f || gap < -9f);
            if (far)
            {
                d.Follower.enabled = true;
                d.Follower.SpeedScale = gap < 0f ? 0.4f : 1f; // öndeysek yavaşla, hedef yetişsin
                float lane = d.Ram == RamMode.Rear ? tLat : tLat + d.SideSign * 3.4f;
                if (Mathf.Abs(lane) > halfRoad) lane = d.Ram == RamMode.Rear ? Mathf.Clamp(lane, -halfRoad, halfRoad) : tLat - d.SideSign * 3.4f;
                d.Follower.LaneOffset = Mathf.Clamp(lane, -halfRoad, halfRoad);
                return;
            }
            d.Follower.enabled = false;
            Vector3 aim;
            float targetSpeed;
            if (d.Ram == RamMode.Rear)
            {
                aim = target.position + tf * 2f;
                targetSpeed = tV + 6f;
            }
            else if (Mathf.Abs(gap) > 2f)
            {
                float side = Vector3.Dot(car.transform.position - target.position, tr) >= 0f ? 1f : -1f;
                aim = target.position + tr * side * 3.4f + tf * 8f;
                targetSpeed = tV + Mathf.Clamp(gap, -6f, 6f) * 0.8f;
            }
            else
            {
                aim = target.position + tf * 4f;
                targetSpeed = tV + 1f;
            }
            DriveAt(car, aim, targetSpeed);
        }

        /// <summary>
        /// Yan yana sürtünme (rub/corner), sıkıştırma (squeeze) ve harvester'a yanaşma: önce hedefin yanına gelir,
        /// sonra yanal mesafeyi yavaşça (0.8 m/sn) kapatıp birkaç saniye bastırır, sonra ayrılır.
        /// </summary>
        private void Rub(Driver d, RaceTrack track, RaceManager manager, float gap, float tLat, float myLat, float tV, float halfRoad, float targetWidth)
        {
            RaceCar car = d.Car;
            float pressTime = d.Ram == RamMode.Squeeze ? 4f : 3f;
            float overlap = d.Ram == RamMode.Squeeze ? 0.9f : 0.5f; // temas genişliğinden ne kadar içeri bastırılsın
            float contactSep = (2.16f + targetWidth) * 0.5f;

            if (!d.Pressing)
            {
                // Yan tarafı seç: sıkıştırmada hedefi yakın bariyere iteceğimiz taraf, diğerlerinde bulunduğumuz taraf
                d.PressSide = d.Ram == RamMode.Squeeze ? (tLat > 0f ? -1 : 1) : (myLat >= tLat ? 1 : -1);
                float approach = tLat + d.PressSide * (contactSep + 1.4f);
                if (Mathf.Abs(approach) > halfRoad + 1f) { d.PressSide = -d.PressSide; approach = tLat + d.PressSide * (contactSep + 1.4f); }
                d.CurrentLateral = Mathf.Clamp(approach, -halfRoad - 1f, halfRoad + 1f);
                d.Follower.enabled = false;
                SteerOnTrack(car, track, d.CurrentLateral, tV + Mathf.Clamp(gap, -8f, 8f) * 0.9f);
                if (Mathf.Abs(gap) < 1.2f && Mathf.Abs(myLat - d.CurrentLateral) < 0.8f)
                {
                    d.Pressing = true;
                    d.PressStart = Time.time;
                    d.CurrentLateral = myLat;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture, "[AutoTest] Bastırma başladı: {0} {1} hız={2:F1} m/sn yanal={3:F1} hedefYanal={4:F1}",
                        Label(car), d.Ram, car.ForwardSpeed, myLat, tLat));
                }
                return;
            }

            float press = tLat + d.PressSide * (contactSep - overlap);
            d.CurrentLateral = Mathf.MoveTowards(d.CurrentLateral, press, 0.8f * Time.deltaTime);
            SteerOnTrack(car, track, d.CurrentLateral, tV + Mathf.Clamp(gap, -4f, 4f) * 0.8f);
            if (Time.time - d.PressStart > pressTime)
            {
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[AutoTest] Bastırma bitti: {0} {1} hız={2:F1} m/sn", Label(car), d.Ram, car.ForwardSpeed));
                d.Pressing = false;
                d.BackOffUntil = Time.time + 6f;
            }
        }

        /// <summary>PIT: hedefin arka çamurluğuna çapraz vuruş.</summary>
        private void Pit(Driver d, RaceTrack track, Transform target, Vector3 tf, Vector3 tr, float gap, float tLat, float tV, float halfRoad, bool newHit)
        {
            RaceCar car = d.Car;
            if (newHit && d.Pressing)
            {
                d.Pressing = false;
                d.BackOffUntil = Time.time + 6f;
                d.SideSign = -d.SideSign;
                return;
            }
            if (!d.Pressing)
            {
                // Hedefin 5 m gerisinde, yanında 1.6 m açıkta konumlan
                float lane = Mathf.Clamp(tLat + d.SideSign * 1.6f, -halfRoad, halfRoad);
                d.Follower.enabled = false;
                SteerOnTrack(car, track, lane, tV + Mathf.Clamp(gap - 5f, -6f, 6f) * 0.8f);
                if (Mathf.Abs(gap - 5f) < 1f) { d.Pressing = true; d.PressStart = Time.time; Debug.Log("[AutoTest] PIT başladı: " + Label(car)); }
                return;
            }
            // Arka köşeye çapraz: köşenin biraz ötesine (karşı tarafa) nişan al
            Vector3 corner = target.position - tf * 1.7f + tr * d.SideSign * 0.9f;
            DriveAt(car, corner - tr * d.SideSign * 2.5f + tf * 1f, tV + 4f);
            if (Time.time - d.PressStart > 3f) { d.Pressing = false; d.BackOffUntil = Time.time + 4f; }
        }

        private static float Wrap(RaceTrack track, float delta)
        {
            if (!track.Loop) return delta;
            if (delta > track.Length * 0.5f) delta -= track.Length;
            else if (delta < -track.Length * 0.5f) delta += track.Length;
            return delta;
        }

        private static bool BendAhead(RaceTrack track, float d)
        {
            float a = Vector3.Angle(track.GetForward(track.WrapDistance(d + 40f)), track.GetForward(track.WrapDistance(d + 120f)));
            return a > 25f;
        }

        /// <summary>Pistte verilen yanal konumda saf takip ile sürer.</summary>
        private static void SteerOnTrack(RaceCar car, RaceTrack track, float lateral, float targetSpeed)
        {
            float look = 7f + Mathf.Abs(car.ForwardSpeed) * 0.35f;
            float ahead = track.WrapDistance(car.TrackDistance + look);
            DriveAt(car, track.GetPoint(ahead) + track.GetRight(ahead) * lateral, targetSpeed);
        }

        private static void DriveAt(RaceCar car, Vector3 aim, float targetSpeed)
        {
            Vector3 to = Vector3.ProjectOnPlane(aim - car.transform.position, Vector3.up);
            Vector3 fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up);
            float angle = Vector3.SignedAngle(fwd, to, Vector3.up);
            car.SetExternalInput(Mathf.Clamp((targetSpeed - car.ForwardSpeed) * 0.4f, -1f, 1f), Mathf.Clamp(angle / 20f, -1f, 1f), false);
        }

        private static RaceCar Nearest(RaceCar car)
        {
            RaceCar best = null;
            float bestD = float.MaxValue;
            foreach (var other in RaceCar.All)
            {
                if (other == null || other == car || other.IsEliminated) continue;
                float dd = (other.transform.position - car.transform.position).sqrMagnitude;
                if (dd < bestD) { bestD = dd; best = other; }
            }
            return best;
        }

        // ------------------------------------------------------------------ Eleme testi

        private void UpdateElimination(RaceManager manager)
        {
            if (elimAfter < 0f || elimDone || manager == null || !manager.IsAuthority) return;
            if (manager.Phase != RacePhase.Racing || manager.RaceTime < elimAfter) return;
            elimDone = true;
            var standings = manager.Standings;
            for (int i = standings.Count - 1; i >= 0; i--)
            {
                if (standings[i].Status != RacerStatus.Racing) continue;
                RaceCar car = manager.CarOf(standings[i].ClientId);
                if (car == null || car.IsEliminated) continue;
                Debug.Log("[AutoTest] Araba eleniyor: " + Label(car));
                car.Eliminate();
                return;
            }
        }

        // ------------------------------------------------------------------ Ekran görüntüleri

        private void Shot(string what)
        {
            string path = Path.Combine(Application.persistentDataPath, "race_" + role + "_" + what + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[AutoTest] Ekran görüntüsü: " + path);
        }

        private void UpdateScreenshots(RaceManager manager)
        {
            if (!shots || manager == null) return;
            if (!hudShot && manager.Phase == RacePhase.Racing && manager.RaceTime > 4f)
            {
                hudShot = true;
                Shot("hud");
                hoodShotAt = Time.time + 2f;
            }
            if (hoodShotAt > 0f && !hoodShot && Time.time >= hoodShotAt && CarCamera.Current != null && CarCamera.Current.enabled)
            {
                // Kaput kamerasına geç, görüntü al, eski moda dön
                var property = typeof(CarCamera).GetProperty("CurrentMode");
                object old = property.GetValue(CarCamera.Current);
                property.SetValue(CarCamera.Current, CarCamera.Mode.Kaput);
                hoodShot = true;
                Shot("kaput");
                StartCoroutine(RestoreMode(property, old));
            }
            if (!hitShot && CarContactSolver.HitCount > 0)
            {
                if (hitShotAt < 0f) hitShotAt = Time.time + 0.05f;
                else if (Time.time >= hitShotAt) { hitShot = true; Shot("carpisma"); }
            }
            if (!contactShot && CarContactSolver.LongestActiveContact > 1f)
            {
                contactShot = true;
                Shot("temas");
            }
            if (!spectatorShot && Camera.main != null)
            {
                var spectator = Camera.main.GetComponent<RaceSpectatorCamera>();
                if (spectator != null && spectator.enabled)
                {
                    if (spectatorShotAt < 0f) spectatorShotAt = Time.time + 1.5f;
                    else if (Time.time >= spectatorShotAt) { spectatorShot = true; Shot("izleyici"); }
                }
            }
        }

        private System.Collections.IEnumerator RestoreMode(System.Reflection.PropertyInfo property, object old)
        {
            yield return new WaitForSeconds(0.5f);
            if (CarCamera.Current != null) property.SetValue(CarCamera.Current, old);
        }

        // ------------------------------------------------------------------ Konum kaydı

        private void LateUpdate()
        {
            if (trace == null) return;
            double ms = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
            if (ms - lastTraceMs < 16) return;
            lastTraceMs = ms;
            var nm = NetworkManager.Singleton;
            double st = nm != null && nm.IsListening ? nm.ServerTime.Time : Time.timeAsDouble;
            foreach (var car in RaceCar.All)
            {
                if (car == null) continue;
                Vector3 p = car.transform.position, v = car.Velocity;
                string id = !car.IsSpawned ? car.name : car.NetworkObject.IsPlayerObject ? car.OwnerClientId.ToString() : "bot" + car.NetworkObjectId;
                trace.WriteLine(FormattableString.Invariant(
                    $"{ms:F1},{st:F3},{id},{(car.DrivesLocally ? 1 : 0)},{p.x:F3},{p.y:F3},{p.z:F3},{car.transform.eulerAngles.y:F2},{v.x:F2},{v.z:F2},{(car.IsEliminated ? 1 : 0)}"));
            }
        }
    }
}
