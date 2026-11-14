using System;
using System.Collections.Generic;
using Harvester.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Harvester.Race
{
    /// <summary>
    /// Yarış akışı: Waiting → Countdown (3-2-1) → Racing → Finished → lobiye dönüş.
    /// Sunucu otoriteli: bütün kararları (tur sayımı, sıralama, bitiş, eleme) sunucu verir, istemciler
    /// NetworkVariable/NetworkList'ten okur. Ağ yokken (race sahnesi tek başına açıldı) aynı mantık yerelde çalışır.
    ///
    /// İlerleme: her arabanın sahibi TrackDistance'ı (pist üzerindeki mesafe, turluda 0..Length sarar) yayınlıyor.
    /// Sunucu her örnekte değişimi en kısa yönde (sarmayı hesaba katarak) toplar. Geri gidince ilerleme de azalır,
    /// yani çizgiyi geri geçip tekrar ileri geçmek tur kazandırmaz. Tek örnekteki ileri sıçrama makul hızla sınırlı.
    /// İlerleme başlangıç çizgisinde 0'dır; tur = floor(ilerleme / Length) + 1; tur sayısı × Length'e varan bitirir.
    /// A'dan B'ye pistte ilerleme doğrudan mesafedir, FinishDistance'a varan bitirir.
    /// </summary>
    public class RaceManager : NetworkBehaviour
    {
        public static RaceManager Instance { get; private set; }

        /// <summary>Faz değiştiğinde (her makinede).</summary>
        public static event Action<RacePhase> PhaseChanged;

        [Header("Kurallar")]
        [Tooltip("Turlu pistte tur sayısı")]
        [SerializeField] private int laps = 3;
        [Header("Başlangıç ışıkları (F1 usulü)")]
        [Tooltip("Arabalar dizildikten sonra ilk ışık yanana kadar (sn)")]
        [SerializeField] private float lightsLead = 1f;
        [Tooltip("Işıklar arası (sn). 5 ışık sırayla yanar.")]
        [SerializeField] private float lightInterval = 1f;
        [Tooltip("Beş ışık yandıktan sonra sönmelerine kadar rastgele bekleme (sn): en az / en çok")]
        [SerializeField] private float lightsHoldMin = 0.4f, lightsHoldMax = 2f;
        [Tooltip("Yarış başladıktan kaç saniye sonra harvester kalkar")]
        [SerializeField] private float harvesterDelay = 1f;
        [Tooltip("Yarışın ne zaman biteceği")]
        [SerializeField] private RaceEndRule endRule = RaceEndRule.AllDone;
        [Tooltip("İlk bitirenden sonra diğerlerine kalan süre (sn). 0 = sınırsız.")]
        [SerializeField] private float finishGraceSeconds = 0f;
        [Tooltip("Yarışın en uzun süresi (sn). 0 = sınırsız.")]
        [SerializeField] private float maxRaceSeconds = 0f;
        [Tooltip("Sonuç ekranının süresi (sn)")]
        [SerializeField] private float resultsSeconds = 8f;
        [SerializeField] private string lobbySceneName = "Lobby";

        [Header("Ağ")]
        [Tooltip("Sıralamanın yayınlanma aralığı (sn). Durum değişiklikleri (bitirme, eleme) hemen yayınlanır.")]
        [SerializeField] private float publishInterval = 0.25f;
        [Tooltip("Tek örnekte kabul edilen en yüksek ileri hız (m/sn); pist kolları arasında sıçramaya karşı")]
        [SerializeField] private float maxProgressSpeed = 90f;

        private readonly NetworkVariable<RacePhase> phase = new NetworkVariable<RacePhase>(RacePhase.Waiting);
        private readonly NetworkVariable<double> phaseEndTime = new NetworkVariable<double>();
        private readonly NetworkVariable<double> raceStartTime = new NetworkVariable<double>();
        private readonly NetworkVariable<double> lightsStartTime = new NetworkVariable<double>();
        private double offlineLightsStart;
        private readonly NetworkVariable<byte> lapCount = new NetworkVariable<byte>(3);
        private readonly NetworkVariable<ulong> harvesterObjectId = new NetworkVariable<ulong>(ulong.MaxValue);
        private NetworkList<RacerState> standings;

        // Ağ yokken yerel kopyalar
        private RacePhase offlinePhase;
        private double offlinePhaseEnd, offlineRaceStart;

        // Herkesin okuduğu sıralama (sıraya göre dizili)
        private readonly List<RacerState> view = new List<RacerState>();

        // Sadece sunucu
        private class Racer
        {
            public ulong ClientId;
            public RaceCar Car;
            public ulong CarObjectId;
            public string Name;
            public float Progress;
            public float LastDistance;
            public double LastSampleTime;
            public RacerStatus Status;
            public float Time;
            public int FinishOrder;
        }
        private readonly List<Racer> racers = new List<Racer>();
        private int finishCounter;
        private double firstFinishTime = -1;
        private double nextPublish;
        private bool dirty;
        private bool harvesterStarted;
        private double harvesterStartTime;
        private bool harvesterReached;
        [Tooltip("Harvester herhangi bir yarışan arabaya bu kadar yaklaşınca \"sürüye yetişti\" loglanır (m)")]
        [SerializeField] private float reachDistance = 25f;
        private bool offline;
        private RacePhase lastSeenPhase = (RacePhase)255;

        // ------------------------------------------------------------------ Okuma

        public RacePhase Phase => IsSpawned ? phase.Value : offlinePhase;
        public int Laps => IsSpawned ? lapCount.Value : Mathf.Max(1, laps);
        public bool LoopTrack => RaceTrack.Current != null && RaceTrack.Current.Loop;
        /// <summary>Sıralama, 1. sıradan başlayarak.</summary>
        public IReadOnlyList<RacerState> Standings => view;
        public double Now => IsSpawned ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
        public float PhaseRemaining => Mathf.Max(0f, (float)((IsSpawned ? phaseEndTime.Value : offlinePhaseEnd) - Now));
        /// <summary>Yarış başından beri geçen süre (yarış başlamadıysa 0).</summary>
        public float RaceTime => Phase >= RacePhase.Racing ? Mathf.Max(0f, (float)(Now - (IsSpawned ? raceStartTime.Value : offlineRaceStart))) : 0f;
        public bool IsAuthority => IsSpawned ? IsServer : offline;

        // ------------------------------------------------------------------ Başlangıç ışıkları

        public const int StartLightCount = 5;

        /// <summary>
        /// Yanan başlangıç ışığı sayısı (0..5). Işıklar sunucu saatine bağlı: her makinede aynı anda yanar ve söner.
        /// Beşi yandıktan sonra rastgele bir süre bekler; sönünce (StartLightsOut) yarış başlar. Kalan süre gösterilmez.
        /// </summary>
        public int StartLightsLit
        {
            get
            {
                if (Phase != RacePhase.Countdown || StartLightsOut) return 0;
                double t = Now - (IsSpawned ? lightsStartTime.Value : offlineLightsStart) - lightsLead;
                if (t < 0) return 0;
                return Mathf.Clamp(Mathf.FloorToInt((float)(t / Mathf.Max(0.05f, lightInterval))) + 1, 0, StartLightCount);
            }
        }

        /// <summary>Işıklar söndü: yarış başladı. İstemcide faz değişikliği gecikmeyle gelse de sunucu saatine göre aynı anda true olur.</summary>
        public bool StartLightsOut => Phase >= RacePhase.Racing || (Phase == RacePhase.Countdown && Now >= PhaseEnd);

        // ------------------------------------------------------------------ Aralar (saniye)

        /// <summary>Öndeki yarışçıyla aradaki süre (sn): öndeki, bizim şu an olduğumuz noktadan kaç saniye önce geçti. Yoksa NaN.</summary>
        public float GapToAhead(ulong clientId) { float a, b; ComputeGaps(clientId, out a, out b); return a; }

        /// <summary>Arkadaki yarışçıyla aradaki süre (sn): arkadaki, bizim geçtiğimiz noktaya kaç saniye sonra geldi. Yoksa NaN.</summary>
        public float GapToBehind(ulong clientId) { float a, b; ComputeGaps(clientId, out a, out b); return b; }

        /// <summary>Harvester'ın pist boyunca bize uzaklığı (m, tablanın önünden arabanın arkasına). Yoksa NaN.</summary>
        public float HarvesterGapMeters(ulong clientId)
        {
            var car = CarOf(clientId);
            var ai = HarvesterAI.Instance;
            Transform harvester = HarvesterTransform;
            if (car == null || harvester == null) return float.NaN;
            float open = ai != null ? ai.OpenGapTo(car) : float.NaN;
            if (!float.IsNaN(open) && open > -12f && open < 1000f) return Mathf.Max(0f, open);
            return Vector3.Distance(harvester.position, car.transform.position);
        }

        /// <summary>Harvester bizim şu an olduğumuz yere kaç saniye sonra gelir (bizim geçmiş hızımıza göre). Yoksa NaN.</summary>
        public float HarvesterGapSeconds(ulong clientId)
        {
            float meters = HarvesterGapMeters(clientId);
            RacerState me;
            if (float.IsNaN(meters) || !TryGetState(clientId, out me)) return float.NaN;
            History h;
            if (!history.TryGetValue(clientId, out h) || h.Count == 0) return float.NaN;
            double at = h.TimeAt(h.LastProgress - meters);
            if (double.IsNaN(at))
            {
                // Geçmiş yetmiyorsa anlık hızdan tahmin
                var car = CarOf(clientId);
                float speed = car != null ? Mathf.Max(5f, Mathf.Abs(car.ForwardSpeed)) : 20f;
                return meters / speed;
            }
            return Mathf.Max(0f, (float)(h.LastTime - at));
        }

        private void ComputeGaps(ulong clientId, out float ahead, out float behind)
        {
            ahead = behind = float.NaN;
            int index = -1;
            for (int i = 0; i < view.Count; i++) if (view[i].ClientId == clientId) { index = i; break; }
            if (index < 0 || view[index].Status != RacerStatus.Racing) return;
            History mine;
            if (!history.TryGetValue(clientId, out mine) || mine.Count == 0) return;
            for (int i = index - 1; i >= 0; i--)
            {
                if (view[i].Status != RacerStatus.Racing) continue;
                History other;
                if (history.TryGetValue(view[i].ClientId, out other))
                {
                    double at = other.TimeAt(mine.LastProgress);
                    if (!double.IsNaN(at)) ahead = Mathf.Max(0f, (float)(mine.LastTime - at));
                }
                break;
            }
            for (int i = index + 1; i < view.Count; i++)
            {
                if (view[i].Status != RacerStatus.Racing) continue;
                History other;
                if (history.TryGetValue(view[i].ClientId, out other) && other.Count > 0)
                {
                    double at = mine.TimeAt(other.LastProgress);
                    if (!double.IsNaN(at)) behind = Mathf.Max(0f, (float)(other.LastTime - at));
                }
                break;
            }
        }

        /// <summary>Bir yarışçının (ilerleme, zaman) geçmişi; ara süreleri hesaplamak için. Her makine kendi tutar.</summary>
        private class History
        {
            const int Capacity = 600; // 10 Hz'de 60 sn
            readonly float[] progress = new float[Capacity];
            readonly double[] time = new double[Capacity];
            int start, count;
            public int Count => count;
            public float LastProgress => progress[(start + count - 1) % Capacity];
            public double LastTime => time[(start + count - 1) % Capacity];

            public void Add(float p, double t)
            {
                if (count > 0 && (p <= LastProgress || t - LastTime < 0.09)) return;
                int i = (start + count) % Capacity;
                if (count == Capacity) start = (start + 1) % Capacity; else count++;
                progress[i] = p; time[i] = t;
            }

            /// <summary>İlerlemenin p olduğu an (doğrusal ara değer). Geçmiş o noktayı kapsamıyorsa NaN.</summary>
            public double TimeAt(float p)
            {
                if (count == 0) return double.NaN;
                int last = (start + count - 1) % Capacity;
                if (p > progress[last]) return double.NaN;
                if (p < progress[start]) return double.NaN;
                for (int k = count - 1; k > 0; k--)
                {
                    int b = (start + k) % Capacity, a = (start + k - 1) % Capacity;
                    if (p >= progress[a])
                    {
                        float span = progress[b] - progress[a];
                        double f = span > 1e-4f ? (p - progress[a]) / span : 1.0;
                        return time[a] + (time[b] - time[a]) * f;
                    }
                }
                return time[start];
            }
        }

        private readonly Dictionary<ulong, History> history = new Dictionary<ulong, History>();

        private void SampleHistory()
        {
            if (Phase != RacePhase.Racing) { if (Phase == RacePhase.Countdown) history.Clear(); return; }
            double now = Now;
            for (int i = 0; i < view.Count; i++)
            {
                var r = view[i];
                if (r.Status != RacerStatus.Racing) continue;
                History h;
                if (!history.TryGetValue(r.ClientId, out h)) { h = new History(); history[r.ClientId] = h; }
                h.Add(r.Progress, now);
            }
        }

        /// <summary>Harvester'ın sahnedeki objesi (her makinede). Yoksa null.</summary>
        public Transform HarvesterTransform
        {
            get
            {
                if (IsSpawned && harvesterObjectId.Value != ulong.MaxValue)
                {
                    NetworkObject obj;
                    if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(harvesterObjectId.Value, out obj) && obj != null) return obj.transform;
                }
                var ai = HarvesterLink.Instance;
                return ai != null ? ai.transform : null;
            }
        }

        public bool TryGetState(ulong clientId, out RacerState state)
        {
            for (int i = 0; i < view.Count; i++)
                if (view[i].ClientId == clientId) { state = view[i]; return true; }
            state = default;
            return false;
        }

        /// <summary>Sadece sunucu: yarışçının arabası (yoksa null).</summary>
        public RaceCar CarOf(ulong clientId)
        {
            foreach (var r in racers) if (r.ClientId == clientId) return r.Car;
            return null;
        }

        /// <summary>Sadece sunucu: araba hâlâ yarışıyor mu (bitirmedi, elenmedi, çıkmadı). Yarışçı listesinde yoksa true (test).</summary>
        public bool IsCarRacing(RaceCar car)
        {
            foreach (var r in racers) if (r.Car == car) return r.Status == RacerStatus.Racing;
            return true;
        }

        /// <summary>Sadece sunucu, yarış başlamadan: tur sayısını değiştirir (test).</summary>
        public void OverrideLaps(int count)
        {
            if (!IsAuthority || Phase == RacePhase.Racing || Phase == RacePhase.Finished) return;
            laps = Mathf.Clamp(count, 1, 255);
            if (IsSpawned && lapCount.Value != laps) lapCount.Value = (byte)laps;
        }

        /// <summary>Bu makinenin yarışçısı.</summary>
        public bool TryGetLocalState(out RacerState state)
        {
            if (IsSpawned) return TryGetState(NetworkManager.LocalClientId, out state);
            return TryGetState(0, out state);
        }

        // ------------------------------------------------------------------ Yaşam döngüsü

        private void OnEnable()
        {
            Instance = this; // editörde oyun sırasında yeniden derlemeden sonra da
        }

        private void Awake()
        {
            Instance = this;
            standings = new NetworkList<RacerState>();
        }

        private void Start()
        {
            // Host kapanınca istemcinin objeleri despawn olur; GameManager'daki gibi burada dinliyoruz
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped += OnLocalClientStopped;
            RaceCar.AnyEliminated += OnAnyEliminated;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                // Sahne ağsız açıldı: sahnedeki arabalarla yerel yarış (tek kişilik deneme)
                offline = true;
                Invoke(nameof(BeginOffline), 0.5f);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RaceAutoTest.EnsureCreated();
#endif
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped -= OnLocalClientStopped;
            RaceCar.AnyEliminated -= OnAnyEliminated;
            RaceCar.InputEnabled = true; // başka sahneler (araba testi) etkilenmesin
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            standings.OnListChanged += OnStandingsChanged;
            if (IsServer)
            {
                lapCount.Value = (byte)Mathf.Clamp(laps, 1, 255);
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
            RebuildView();
        }

        public override void OnNetworkDespawn()
        {
            standings.OnListChanged -= OnStandingsChanged;
            if (IsServer) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private void OnStandingsChanged(NetworkListEvent<RacerState> change)
        {
            if (!IsServer) RebuildView();
        }

        private void RebuildView()
        {
            if (IsServer && IsSpawned) return; // sunucu view'i kendisi doldurur
            view.Clear();
            foreach (var s in standings) view.Add(s);
            view.Sort((x, y) => x.Position.CompareTo(y.Position));
        }

        // ------------------------------------------------------------------ Başlatma (sunucu)

        /// <summary>Sadece sunucu: RaceSpawner arabaları oluşturduktan sonra çağırır.</summary>
        public void BeginRace(IList<RaceCar> cars, IList<ulong> clientIds, NetworkObject harvester)
        {
            if (!IsAuthority) return;
            var track = RaceTrack.Current;
            racers.Clear();
            finishCounter = 0;
            firstFinishTime = -1;
            harvesterStarted = false;
            harvesterReached = false;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                if (car == null) continue;
                ulong id = clientIds[i];
                // Diziliş: slot i, sırada ikişer araba, sıralar arası 9 m (RaceTrack.GetStartPose ile aynı)
                float startDistance = track != null ? track.StartDistance - (i / 2) * 9f : 0f;
                racers.Add(new Racer
                {
                    ClientId = id,
                    Car = car,
                    CarObjectId = car.IsSpawned ? car.NetworkObjectId : 0,
                    Name = PlayerName(id, i),
                    LastDistance = startDistance,
                    Progress = ToProgress(startDistance),
                    LastSampleTime = Now,
                    Status = RacerStatus.Racing,
                });
            }
            if (IsSpawned) harvesterObjectId.Value = harvester != null ? harvester.NetworkObjectId : ulong.MaxValue;
            HarvesterLink.SetRunning(false);
            // F1 usulü: ışıklar sırayla yanar, rastgele bir beklemeden sonra söner. Bekleme sunucuda seçilir,
            // istemciler yalnız bitiş anını (phaseEndTime) bilir; kimse ne zaman söneceğini önceden göremez.
            if (IsSpawned) lightsStartTime.Value = Now; else offlineLightsStart = Now;
            float hold = UnityEngine.Random.Range(lightsHoldMin, Mathf.Max(lightsHoldMin, lightsHoldMax));
            SetPhase(RacePhase.Countdown, lightsLead + lightInterval * (StartLightCount - 1) + Mathf.Max(0.2f, lightInterval) + hold);
            Publish();
            Debug.Log("[Race] Yarış hazırlanıyor: " + racers.Count + " yarışçı, " + Laps + " tur, pist " +
                      (track != null ? (track.Loop ? "turlu " : "A→B ") + track.Length.ToString("F0") + " m" : "YOK"));
        }

        private void BeginOffline()
        {
            if (Phase != RacePhase.Waiting) return;
            var cars = new List<RaceCar>(RaceCar.All);
            var ids = new List<ulong>();
            for (int i = 0; i < cars.Count; i++) ids.Add((ulong)i);
            if (RaceCar.Local != null && cars.Remove(RaceCar.Local)) cars.Insert(0, RaceCar.Local);
            BeginRace(cars, ids, null);
        }

        private static string PlayerName(ulong clientId, int index)
        {
            if (clientId >= RaceSpawner.BotClientIdBase) return "Bot " + (clientId - RaceSpawner.BotClientIdBase + 1);
            MatchSettings.PlayerInfo info;
            if (MatchSettings.Players.TryGetValue(clientId, out info) && !string.IsNullOrEmpty(info.Name)) return info.Name;
            return "Oyuncu " + (index + 1);
        }

        /// <summary>Pist mesafesini ilerlemeye çevirir. Turlu pistte başlangıç dizilişi çizginin gerisinde, yani eksi.</summary>
        private float ToProgress(float distance)
        {
            var track = RaceTrack.Current;
            if (track == null || !track.Loop) return distance;
            float length = track.Length;
            return distance > length * 0.5f ? distance - length : distance;
        }

        // ------------------------------------------------------------------ Akış

        private void Update()
        {
            RacePhase current = Phase;
            if (current != lastSeenPhase)
            {
                lastSeenPhase = current;
                PhaseChanged?.Invoke(current);
            }
            // Girdi sadece yarışta açık (her makinede). Işıklar sunucu saatine göre söner: istemci, faz değişikliğinin
            // gelmesini (gecikme kadar) beklemeden herkesle aynı anda kalkar.
            RaceCar.InputEnabled = current == RacePhase.Racing || StartLightsOut || (offline && current == RacePhase.Finished);
            SampleHistory();

            if (!IsAuthority) return;
            double now = Now;
            switch (current)
            {
                case RacePhase.Countdown:
                    SampleProgress(false);
                    if (now >= PhaseEnd) StartRacing();
                    break;
                case RacePhase.Racing:
                    SampleProgress(true);
                    if (!harvesterStarted && now - RaceStart >= harvesterDelay)
                    {
                        harvesterStarted = true;
                        harvesterStartTime = now;
                        HarvesterLink.SetRunning(true);
                        Debug.Log("[Race] Harvester kalktı (yarışın " + RaceTime.ToString("F1") + ". sn'si)");
                    }
                    if (harvesterStarted && !harvesterReached) CheckHarvesterReach(now);
                    CheckEnd();
                    break;
                case RacePhase.Finished:
                    if (now >= PhaseEnd) ReturnToLobby();
                    break;
            }
            if (dirty || now >= nextPublish) Publish();
        }

        private double PhaseEnd => IsSpawned ? phaseEndTime.Value : offlinePhaseEnd;
        private double RaceStart => IsSpawned ? raceStartTime.Value : offlineRaceStart;

        private void StartRacing()
        {
            if (IsSpawned) raceStartTime.Value = Now;
            else offlineRaceStart = Now;
            SetPhase(RacePhase.Racing, 0f);
            Debug.Log("[Race] Yarış başladı");
        }

        private void SampleProgress(bool racing)
        {
            var track = RaceTrack.Current;
            if (track == null) return;
            float length = track.Length;
            double now = Now;
            foreach (var r in racers)
            {
                if (r.Car == null || r.Status != RacerStatus.Racing) continue;
                float d = r.Car.TrackDistance;
                if (!racing)
                {
                    // Geri sayımda arabalar yerinde: ilerlemeyi sahibinin bildirdiği yere eşitle
                    r.LastDistance = d;
                    r.Progress = ToProgress(d);
                    r.LastSampleTime = now;
                    continue;
                }
                if (Mathf.Approximately(d, r.LastDistance)) continue;
                float delta = d - r.LastDistance;
                if (track.Loop)
                {
                    // En kısa yön: çizgiden geçerken sarma
                    if (delta > length * 0.5f) delta -= length;
                    else if (delta < -length * 0.5f) delta += length;
                }
                float elapsed = Mathf.Max(0.02f, (float)(now - r.LastSampleTime));
                delta = Mathf.Min(delta, maxProgressSpeed * elapsed + 5f);
                r.Progress += delta;
                r.LastDistance = d;
                r.LastSampleTime = now;

                float goal = track.Loop ? Laps * length : track.FinishDistance;
                if (r.Progress >= goal) Finish(r);
            }
        }

        private void CheckHarvesterReach(double now)
        {
            Transform harvester = HarvesterTransform;
            if (harvester == null) return;
            float nearest = float.MaxValue;
            string who = "";
            foreach (var r in racers)
            {
                if (r.Car == null || r.Status != RacerStatus.Racing) continue;
                float d = Vector3.Distance(harvester.position, r.Car.transform.position);
                if (d < nearest) { nearest = d; who = r.Name; }
            }
            if (nearest > reachDistance) return;
            harvesterReached = true;
            Debug.Log("[Race] Harvester sürüye yetişti: kalkıştan " + (now - harvesterStartTime).ToString("F2") + " sn sonra, yarışın " +
                      RaceTime.ToString("F2") + ". sn'si, " + who + " ile " + nearest.ToString("F1") + " m");
        }

        private void Finish(Racer r)
        {
            r.Status = RacerStatus.Finished;
            r.Time = RaceTime;
            r.FinishOrder = ++finishCounter;
            if (firstFinishTime < 0) firstFinishTime = Now;
            dirty = true;
            Debug.Log("[Race] Bitirdi: " + r.Name + " (" + r.ClientId + ") " + r.FinishOrder + ". sıra, " + r.Time.ToString("F2") + " sn");
        }

        private void OnAnyEliminated(RaceCar car)
        {
            if (!IsAuthority) return;
            foreach (var r in racers)
            {
                if (r.Car != car || r.Status != RacerStatus.Racing) continue;
                r.Status = RacerStatus.Eliminated;
                r.Time = RaceTime;
                dirty = true;
                Debug.Log("[Race] Elendi: " + r.Name + " (" + r.ClientId + ") " + r.Time.ToString("F2") + " sn");
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            foreach (var r in racers)
            {
                if (r.ClientId != clientId || r.Status == RacerStatus.Left) continue;
                if (r.Status == RacerStatus.Racing) r.Time = RaceTime;
                // Bitiren biri sonradan çıkarsa sonucu kalsın
                if (r.Status != RacerStatus.Finished) r.Status = RacerStatus.Left;
                r.Car = null;
                dirty = true;
                Debug.Log("[Race] Çıktı: " + r.Name + " (" + clientId + ")");
            }
        }

        private void CheckEnd()
        {
            // Sahnede kaybolan araba (despawn) yarıştan çıkmış sayılır
            foreach (var r in racers)
            {
                if (r.Status == RacerStatus.Racing && r.Car == null)
                {
                    r.Status = RacerStatus.Left;
                    r.Time = RaceTime;
                    dirty = true;
                }
                else if (r.Status == RacerStatus.Racing && r.Car.IsEliminated)
                {
                    OnAnyEliminated(r.Car);
                }
            }

            int racing = 0;
            foreach (var r in racers) if (r.Status == RacerStatus.Racing) racing++;

            bool end;
            switch (endRule)
            {
                case RaceEndRule.LastStanding:
                    end = racing == 0 || (racing <= 1 && racers.Count > 1);
                    break;
                default:
                    end = racing == 0;
                    break;
            }
            if (!end && finishGraceSeconds > 0f && firstFinishTime >= 0 && Now - firstFinishTime >= finishGraceSeconds) end = true;
            if (!end && maxRaceSeconds > 0f && RaceTime >= maxRaceSeconds) end = true;
            if (end) EndRace();
        }

        private void EndRace()
        {
            HarvesterLink.SetRunning(false);
            SetPhase(RacePhase.Finished, resultsSeconds);
            Publish();
            var sb = new System.Text.StringBuilder("[Race] Yarış bitti:");
            foreach (var s in view) sb.Append(" | ").Append(s.Position).Append(". ").Append(s.Name).Append(' ').Append(s.Status).Append(' ').Append(s.Time.ToString("F2"));
            Debug.Log(sb.ToString());
        }

        private void SetPhase(RacePhase newPhase, float duration)
        {
            if (IsSpawned)
            {
                phase.Value = newPhase;
                phaseEndTime.Value = Now + duration;
            }
            else
            {
                offlinePhase = newPhase;
                offlinePhaseEnd = Now + duration;
            }
            dirty = true;
        }

        private void ReturnToLobby()
        {
            if (!IsSpawned) return; // ağsız denemede sonuç ekranında kalır
            phase.Value = RacePhase.Waiting;
            NetworkManager.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        }

        // ------------------------------------------------------------------ Sıralama ve yayın

        private static int StatusRank(RacerStatus s)
        {
            switch (s)
            {
                case RacerStatus.Finished: return 0;
                case RacerStatus.Racing: return 1;
                default: return 2;
            }
        }

        private void Publish()
        {
            dirty = false;
            nextPublish = Now + publishInterval;

            var order = new List<Racer>(racers);
            order.Sort((x, y) =>
            {
                int c = StatusRank(x.Status).CompareTo(StatusRank(y.Status));
                if (c != 0) return c;
                if (x.Status == RacerStatus.Finished) return x.FinishOrder.CompareTo(y.FinishOrder);
                if (x.Status == RacerStatus.Racing) return y.Progress.CompareTo(x.Progress);
                return y.Time.CompareTo(x.Time); // elenenlerden daha geç elenen önde
            });

            var track = RaceTrack.Current;
            float length = track != null ? Mathf.Max(1f, track.Length) : 1f;
            bool loop = track != null && track.Loop;
            view.Clear();
            for (int i = 0; i < order.Count; i++)
            {
                var r = order[i];
                int lap = loop ? Mathf.Clamp(Mathf.FloorToInt(r.Progress / length) + 1, 1, Laps) : 1;
                view.Add(new RacerState
                {
                    ClientId = r.ClientId,
                    CarObjectId = r.CarObjectId,
                    Name = new FixedString32Bytes(Truncate(r.Name, 28)),
                    Status = r.Status,
                    Position = (byte)(i + 1),
                    Lap = (byte)lap,
                    // Ağda gereksiz değişiklik yollamamak için yarım metreye yuvarla
                    Progress = Mathf.Round(r.Progress * 2f) * 0.5f,
                    Time = r.Time,
                });
            }

            if (!IsSpawned || !IsServer) return;
            // NetworkList sadece değişen elemanları yollar
            for (int i = 0; i < view.Count; i++)
            {
                if (i < standings.Count)
                {
                    if (!standings[i].Equals(view[i])) standings[i] = view[i];
                }
                else standings.Add(view[i]);
            }
            while (standings.Count > view.Count) standings.RemoveAt(standings.Count - 1);
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // ------------------------------------------------------------------ Bağlantı kopması

        // Host oyunu kapattı ya da bağlantı koptu: lobiye kendimiz dönüyoruz (GameManager ile aynı davranış)
        private void OnLocalClientStopped(bool wasHost)
        {
            if (wasHost)
            {
                var relay = NetworkManager.Singleton != null
                    ? NetworkManager.Singleton.NetworkConfig.NetworkTransport as RelayTransport
                    : null;
                if (relay == null || string.IsNullOrEmpty(relay.LastErrorMessage)) return;
                lobby.PendingStatus = relay.LastErrorMessage;
            }
            else
            {
                lobby.PendingStatus = lobby.DescribeDisconnect("Host ile bağlantı koptu.");
            }
            SceneManager.LoadScene(lobbySceneName);
        }
    }
}
