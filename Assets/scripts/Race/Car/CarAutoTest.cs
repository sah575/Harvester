using System.Collections;
using System.Text;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Araba ayarlarını ölçen otomatik sürüş testi (carTest sahnesi). Sadece editörde ve debug build'de çalışır.
    /// Senaryolar: son hız ve 0–100, fren mesafesi, iki hızda dönüş yarıçapı ve yatma açısı, el freniyle drift,
    /// %8 yokuşta durma (düz ve yan), rampadan atlama, ters dönünce sıfırlama.
    /// Sonuçlar "[CarTest]" önekiyle konsola yazılır; hepsi bitince <see cref="LastReport"/> dolar.
    /// Başlatma: runOnStart, F8 ya da komut satırında -carTest. Shift+F8 / -carTestAll / RunAllCars(): katalogdaki her araba
    /// için sırayla (stok) çalışır ve sonunda "[CarTest] ÖZET" tablosu yazar (<see cref="SummaryReport"/>).
    /// </summary>
    public class CarAutoTest : MonoBehaviour
    {
        public static string LastReport { get; private set; } = "";
        public static bool Running { get; private set; }
        /// <summary>Bütün arabalar testinin markdown özet tablosu (RunAllCars bitince dolar).</summary>
        public static string SummaryReport { get; private set; } = "";

        // Tek koşunun özet ölçümleri
        private float mT100, mTop, mBrake40, mBrakeTime40, mDriftSlip, mDriftEnd, mMaxRoll;
        private readonly float[] mRadius = new float[3];
        private readonly float[] mLateral = new float[3];
        private bool mFlipped, mDriftRecovered;

        [Tooltip("Oyun başlayınca testi kendiliğinden başlat")]
        [SerializeField] private bool runOnStart;
        [Tooltip("Test edilecek araba (boşsa sahnedeki ilk araba)")]
        [SerializeField] private RaceCar car;
        [Tooltip("Uzun düzlüğün başı (ileri +Z boyunca boş olmalı)")]
        [SerializeField] private Transform straightStart;
        [Tooltip("Dönüş ve drift testi için geniş boş alan")]
        [SerializeField] private Transform turnStart;
        [Tooltip("%8 yokuşun ortası (yokuş yukarı bakar)")]
        [SerializeField] private Transform slopeStart;
        [Tooltip("Rampaya doğru yaklaşma noktası")]
        [SerializeField] private Transform rampStart;

        private readonly StringBuilder report = new StringBuilder();

        private void Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-carTestAll") >= 0) StartCoroutine(RunAll());
            else if (runOnStart || System.Array.IndexOf(args, "-carTest") >= 0) StartCoroutine(Run());
#else
            enabled = false;
#endif
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Running && Input.GetKeyDown(KeyCode.F8))
                StartCoroutine(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? RunAll() : Run());
#endif
        }

        private void Log(string line)
        {
            report.AppendLine(line);
            Debug.Log("[CarTest] " + line);
        }

        /// <summary>Katalogdaki bütün arabaları sırayla ölçer (editörden de çağrılabilir).</summary>
        public void RunAllCars()
        {
            if (!Running) StartCoroutine(RunAll());
        }

        private IEnumerator RunAll()
        {
            if (car == null && RaceCar.All.Count > 0) car = RaceCar.All[0];
            if (car == null || car.Catalog == null)
            {
                Debug.LogError("[CarTest] araba ya da katalog yok");
                yield break;
            }
            var table = new StringBuilder();
            table.AppendLine("| Araba | 0–100 (sn) | Son hız (m/sn) | 40→0 fren (m) | R@10 | R@20 | R@30 (m) | Yanal@30 (m/sn²) | Drift kayma / çıkış | En büyük yatma | Devrilme |");
            table.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < car.Catalog.Count; i++)
            {
                var def = car.Catalog.Get(i);
                if (def == null) continue;
                car.SetDefinitionIndex(i);
                yield return new WaitForSeconds(0.5f);
                yield return Run();
                table.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "| {0} | {1:F2} | {2:F1} | {3:F1} | {4:F1} | {5:F1} | {6:F1} | {7:F1} | {8:F0}° / {9:F0}° {10} | {11:F1}° | {12} |",
                    def.id, mT100, mTop, mBrake40, mRadius[0], mRadius[1], mRadius[2], mLateral[2], mDriftSlip, mDriftEnd,
                    mDriftRecovered ? "toparladı" : "TOPARLAYAMADI", mMaxRoll, mFlipped ? "EVET" : "hayır"));
            }
            SummaryReport = table.ToString();
            Debug.Log("[CarTest] ÖZET\n" + SummaryReport);
        }

        private IEnumerator Run()
        {
            mFlipped = false;
            mMaxRoll = 0f;
            if (car == null && RaceCar.All.Count > 0) car = RaceCar.All[0];
            if (car == null)
            {
                Debug.LogError("[CarTest] araba yok");
                yield break;
            }
            Running = true;
            report.Length = 0;
            RaceCar.AutoDrive = true;
            Log("başladı: " + car.name + (car.Definition != null ? " (" + car.Definition.id + ")" : "") + " kütle " + car.Body.mass + " kg");

            yield return Acceleration();
            yield return Brake40();
            yield return Turn(10f, false);
            yield return Turn(20f, false);
            yield return Turn(30f, false);
            yield return Turn(25f, true);
            yield return Slope(false);
            yield return Slope(true);
            yield return Ramp();
            yield return Flip();

            SetInput(0f, 0f, false);
            RaceCar.AutoDrive = false;
            Running = false;
            Log("bitti");
            LastReport = report.ToString();
        }

        private static void SetInput(float throttle, float steer, bool handbrake)
        {
            RaceCar.AutoThrottle = throttle;
            RaceCar.AutoSteer = steer;
            RaceCar.AutoHandbrake = handbrake;
        }

        private IEnumerator Place(Transform at, float yawOffset = 0f)
        {
            SetInput(0f, 0f, false);
            Quaternion rotation = at.rotation * Quaternion.Euler(0f, yawOffset, 0f);
            car.PlaceAt(at.position + at.up * 0.3f, rotation);
            // Süspansiyon otursun
            yield return new WaitForSeconds(1.5f);
        }

        private float Tilt()
        {
            return Vector3.Angle(car.transform.up, Vector3.up);
        }

        private float Roll()
        {
            return Mathf.Abs(Mathf.DeltaAngle(0f, car.transform.eulerAngles.z));
        }

        private bool Flipped()
        {
            return car.transform.up.y < 0.3f;
        }

        // Hıza ulaşmak ve korumak için basit P denetleyici
        private float HoldSpeed(float target)
        {
            return Mathf.Clamp((target - car.ForwardSpeed) * 0.5f, -1f, 1f);
        }

        private IEnumerator Acceleration()
        {
            yield return Place(straightStart);
            Vector3 start = car.transform.position;
            float t0 = Time.time, t100 = -1f, top = 0f, at10 = 0f, at20 = 0f;
            SetInput(1f, 0f, false);
            while (Time.time - t0 < 30f)
            {
                float v = car.ForwardSpeed;
                top = Mathf.Max(top, v);
                if (t100 < 0f && v >= 27.78f) t100 = Time.time - t0;
                if (at10 == 0f && Time.time - t0 >= 10f) at10 = v;
                if (at20 == 0f && Time.time - t0 >= 20f) at20 = v;
                yield return new WaitForFixedUpdate();
            }
            mT100 = t100;
            mTop = top;
            float driven = Vector3.Distance(start, car.transform.position);
            Log(string.Format("hızlanma: 0-100 km/sa {0:F2} sn, 10 sn'de {1:F1} m/sn, 20 sn'de {2:F1}, en yüksek {3:F1} m/sn ({4:F0} km/sa), {5:F0} m yol, en büyük eğim {6:F1}°",
                t100, at10, at20, top, top * 3.6f, driven, Tilt()));

            // Fren
            Vector3 brakeStart = car.transform.position;
            float vBrake = car.ForwardSpeed, tb = Time.time;
            SetInput(-1f, 0f, false);
            while (car.ForwardSpeed > 0.3f && Time.time - tb < 10f) yield return new WaitForFixedUpdate();
            Log(string.Format("fren: {0:F1} m/sn'den {1:F1} m'de, {2:F2} sn'de durdu", vBrake,
                Vector3.Distance(brakeStart, car.transform.position), Time.time - tb));

            // Geri vites
            float tr = Time.time, minV = 0f;
            while (Time.time - tr < 4f)
            {
                minV = Mathf.Min(minV, car.ForwardSpeed);
                yield return new WaitForFixedUpdate();
            }
            Log(string.Format("geri vites: 4 sn'de {0:F1} m/sn", -minV));
            SetInput(0f, 0f, false);
        }

        /// <summary>40 m/sn'den (ya da son hız altındaysa ulaşabildiği en yüksek hızdan) tam fren.</summary>
        private IEnumerator Brake40()
        {
            yield return Place(straightStart);
            float target = Mathf.Min(40f, car.TopSpeed - 0.5f);
            float t0 = Time.time;
            SetInput(1f, 0f, false);
            while (car.ForwardSpeed < target && Time.time - t0 < 30f) yield return new WaitForFixedUpdate();
            Vector3 p0 = car.transform.position;
            float v0 = car.ForwardSpeed, tb = Time.time;
            SetInput(-1f, 0f, false);
            while (car.ForwardSpeed > 0.3f && Time.time - tb < 10f) yield return new WaitForFixedUpdate();
            float dist = Vector3.Distance(p0, car.transform.position);
            // 40 m/sn'ye ölçekle (v² oranı) ki son hızı 40'ın altındaki araba da karşılaştırılabilsin
            mBrake40 = v0 > 1f ? dist * (40f * 40f) / (v0 * v0) : dist;
            mBrakeTime40 = Time.time - tb;
            Log(string.Format("fren 40: {0:F1} m/sn'den {1:F1} m'de, {2:F2} sn'de durdu (40 m/sn'ye ölçekli {3:F1} m)", v0, dist, mBrakeTime40, mBrake40));
            SetInput(0f, 0f, false);
        }

        private IEnumerator Turn(float speed, bool drift)
        {
            yield return Place(turnStart);
            float t0 = Time.time;
            while (car.ForwardSpeed < speed - 0.3f && Time.time - t0 < 15f)
            {
                SetInput(HoldSpeed(speed) + 0.3f, 0f, false);
                yield return new WaitForFixedUpdate();
            }
            float entry = car.ForwardSpeed;

            float yawSum = 0f, speedSum = 0f, maxRoll = 0f, maxSlip = 0f;
            int samples = 0;
            bool flipped = false;
            float duration = drift ? 2.5f : 4f;
            float prevYaw = car.transform.eulerAngles.y;
            t0 = Time.time;
            while (Time.time - t0 < duration)
            {
                if (drift) SetInput(Time.time - t0 < 1.2f ? 0.6f : 1f, 1f, Time.time - t0 < 1.2f);
                else SetInput(HoldSpeed(speed), 1f, false);
                yield return new WaitForFixedUpdate();
                float yaw = car.transform.eulerAngles.y;
                float yawRate = Mathf.DeltaAngle(prevYaw, yaw) / Time.fixedDeltaTime;
                prevYaw = yaw;
                if (Time.time - t0 > 1f) // geçişi ölçme
                {
                    yawSum += yawRate;
                    speedSum += car.Velocity.magnitude;
                    samples++;
                }
                maxRoll = Mathf.Max(maxRoll, Roll());
                Vector3 v = Vector3.ProjectOnPlane(car.Velocity, car.transform.up);
                if (v.magnitude > 2f) maxSlip = Mathf.Max(maxSlip, Vector3.Angle(v, car.transform.forward));
                flipped |= Flipped();
            }
            // Drift'ten çıkış: direksiyonu bırak, hafif gaz; 2 sn sonra kayma açısı
            string recovery = "";
            if (drift)
            {
                float tr = Time.time;
                while (Time.time - tr < 2f)
                {
                    SetInput(0.5f, 0f, false);
                    yield return new WaitForFixedUpdate();
                    flipped |= Flipped();
                }
                Vector3 vEnd = Vector3.ProjectOnPlane(car.Velocity, car.transform.up);
                float slipEnd = vEnd.magnitude > 1f ? Vector3.Angle(vEnd, car.transform.forward) : 0f;
                mDriftEnd = slipEnd;
                mDriftRecovered = slipEnd < 12f;
                recovery = string.Format(", bırakınca 2 sn sonra kayma {0:F1}° hız {1:F1} ({2})", slipEnd, vEnd.magnitude, slipEnd < 12f ? "toparladı" : "TOPARLAYAMADI");
            }
            float avgYaw = samples > 0 ? yawSum / samples * Mathf.Deg2Rad : 0f;
            float avgSpeed = samples > 0 ? speedSum / samples : 0f;
            float radius = Mathf.Abs(avgYaw) > 0.01f ? avgSpeed / Mathf.Abs(avgYaw) : -1f;
            mFlipped |= flipped;
            mMaxRoll = Mathf.Max(mMaxRoll, maxRoll);
            int slot = Mathf.Approximately(speed, 10f) ? 0 : Mathf.Approximately(speed, 20f) ? 1 : Mathf.Approximately(speed, 30f) ? 2 : -1;
            if (!drift && slot >= 0)
            {
                mRadius[slot] = radius;
                mLateral[slot] = avgSpeed * Mathf.Abs(avgYaw);
            }
            if (drift) mDriftSlip = maxSlip;
            Log(string.Format("{0} {1:F0} m/sn: giriş {2:F1}, ort. hız {3:F1}, yarıçap {4:F1} m, yanal ivme {5:F1} m/sn², en büyük yatma {6:F1}°, en büyük kayma açısı {7:F1}°, devrildi: {8}",
                drift ? "drift" : "dönüş", speed, entry, avgSpeed, radius, avgSpeed * Mathf.Abs(avgYaw), maxRoll, maxSlip, flipped ? "EVET" : "hayır") + recovery);
            SetInput(0f, 0f, false);
        }

        private IEnumerator Slope(bool sideways)
        {
            yield return Place(slopeStart, sideways ? 90f : 0f);
            Vector3 p0 = car.transform.position;
            yield return new WaitForSeconds(5f);
            float slide = Vector3.Distance(p0, car.transform.position);
            string line = string.Format("yokuş %8 ({0}): gazsız 5 sn'de {1:F3} m kaydı", sideways ? "yan" : "düz", slide);
            if (!sideways)
            {
                float t0 = Time.time;
                SetInput(1f, 0f, false);
                while (Time.time - t0 < 3f) yield return new WaitForFixedUpdate();
                line += string.Format(", 3 sn gazla yokuş yukarı {0:F1} m/sn", car.ForwardSpeed);
            }
            Log(line);
            SetInput(0f, 0f, false);
        }

        private IEnumerator Ramp()
        {
            yield return Place(rampStart);
            float t0 = Time.time, airTime = 0f, maxHeight = car.transform.position.y, maxTilt = 0f;
            float y0 = car.transform.position.y;
            bool flipped = false;
            SetInput(1f, 0f, false);
            while (Time.time - t0 < 9f)
            {
                yield return new WaitForFixedUpdate();
                if (car.GroundedWheels == 0) airTime += Time.fixedDeltaTime;
                maxHeight = Mathf.Max(maxHeight, car.transform.position.y);
                maxTilt = Mathf.Max(maxTilt, Tilt());
                flipped |= Flipped();
            }
            mFlipped |= flipped;
            SetInput(-1f, 0f, false);
            yield return new WaitForSeconds(3f);
            Log(string.Format("rampa: havada {0:F2} sn, en yüksek {1:F1} m, en büyük eğim {2:F0}°, devrildi: {3}, sonunda düz: {4}",
                airTime, maxHeight - y0, maxTilt, flipped ? "EVET" : "hayır", Flipped() ? "HAYIR" : "evet"));
            SetInput(0f, 0f, false);
        }

        private IEnumerator Flip()
        {
            yield return Place(turnStart);
            car.PlaceAt(car.transform.position + Vector3.up * 1.5f, car.transform.rotation * Quaternion.Euler(0f, 0f, 180f));
            yield return new WaitForSeconds(3f);
            bool wasFlipped = Flipped();
            car.ResetToTrack();
            yield return new WaitForSeconds(2f);
            Log(string.Format("ters dönme: ters kaldı: {0}, sıfırlamadan sonra düz: {1}", wasFlipped ? "evet" : "hayır", Flipped() ? "HAYIR" : "evet"));
        }
    }
}
