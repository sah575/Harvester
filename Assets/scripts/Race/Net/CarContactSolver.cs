using System;
using System.Collections.Generic;
using System.Globalization;
using Harvester.Net;
using Unity.Netcode;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Araba–araba çarpışmalarının ağ senkronu.
    ///
    /// Sorun: her oyuncu kendi arabasını fizikle sürüyor, diğer arabalar onun ekranında kinematik ve tahmini
    /// konumda. Unity fiziğinde dinamik araba kinematik arabaya çarpınca sonsuz kütleli duvara çarpmış gibi seker,
    /// karşı araba hiç tepki vermez (onun fiziği başka makinede).
    ///
    /// Çözüm (her makinede, sadece o makinenin kendi arabası için):
    /// 1. Arabalar arasındaki fizik çarpışması kapatılır (Physics.IgnoreCollision, araba çiftleri için). Harvester,
    ///    bariyer ve zemin normal fizikle çarpışmaya devam eder.
    /// 2. Sahip kendi arabasıyla ekranında GÖRDÜĞÜ diğer arabalar arasındaki örtüşmeyi yatay düzlemde yönlü
    ///    kutu–kutu (SAT) testiyle bulur. Yaklaşma hızı varsa eşit kütle varsayımıyla iki tarafa eşit ve zıt hız
    ///    değişimi hesaplar ve SADECE KENDİ YARISINI kendi arabasına uygular. Karşı taraf aynı çarpışmayı kendi
    ///    ekranında görüp kendi yarısını uygular; böylece her iki taraf da tepki verir, RPC gerekmez, çift itme olmaz.
    /// 3. Birbirinin içine girme, hedef bir ayrılma hızına doğru yumuşak bir itmeyle çözülür (her taraf yarısını).
    /// 4. Görüş farkı: gecikme yüzünden çarpışmayı sadece bir taraf görebilir. Sert vuruşlarda vuran taraf karşı
    ///    arabanın sahibine bir vuruş RPC'si yollar. Her araba çifti için bir "birleştirme penceresi" tutulur:
    ///    pencere içinde gelen her yeni vuruş (yerel tespit ya da RPC) sadece daha büyükse ve aradaki fark kadar
    ///    uygulanır (en büyüğü alınır, toplanmaz). Pencere, karşı arabanın bizim tepkimizi görmesi için gereken
    ///    süre kadar (ağ yolu + yorumlama tamponu + hız süzgeci) tutulur; bu sürede karşının hızı, onun da kendi
    ///    yarısını uyguladığı varsayılarak düzeltilir. Böylece eski hız görüntüsü yüzünden tekrar tekrar itme olmaz.
    ///
    /// Bütün uygulanan vuruşlar "[CarHit]" önekiyle loglanır; iki tarafın kayıtları karşılaştırılarak ölçülür.
    /// RaceManager ile aynı sahne objesinde durur (RaceSceneNetBuilder ekler). Ağ yokken RPC'siz çalışır.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CarContactSolver : NetworkBehaviour
    {
        public static CarContactSolver Instance { get; private set; }

        /// <summary>Uygulanan vuruş sayısı (test özeti için).</summary>
        public static int HitCount { get; private set; }
        /// <summary>Son uygulanan vuruşun büyüklüğü (m/sn).</summary>
        public static float LastHitMagnitude { get; private set; }

        /// <summary>Şu an süren en uzun araba–araba temasının süresi (sn, test için).</summary>
        public static float LongestActiveContact { get; private set; }

        private static readonly Dictionary<RaceCar, int> hitsByCar = new Dictionary<RaceCar, int>();
        /// <summary>Bu arabaya uygulanan vuruş sayısı (test sürücüsü için).</summary>
        public static int HitsOn(RaceCar car)
        {
            int c;
            return car != null && hitsByCar.TryGetValue(car, out c) ? c : 0;
        }

        [Header("Vuruş")]
        [Tooltip("Sekme katsayısı: 0 = yapışır, 1 = tam esnek. Arcade için düşük-orta.")]
        [Range(0f, 1f)] [SerializeField] private float restitution = 0.35f;
        [Tooltip("Yan sürtünme katsayısı: sürtünerek geçen arabalar birbirini biraz sürükler")]
        [Range(0f, 1f)] [SerializeField] private float friction = 0.2f;
        [Tooltip("Bu yaklaşma hızının altında vuruş yok, sadece ayırma itmesi (m/sn)")]
        [SerializeField] private float minApproachSpeed = 0.8f;
        [Tooltip("Vuruşta kendi arabaya en az bu kadar hız değişimi (m/sn)")]
        [SerializeField] private float minBump = 0.6f;
        [Tooltip("Tek vuruşta en fazla hız değişimi (m/sn). Tahmin hatalarında arabalar uçmasın.")]
        [SerializeField] private float maxImpulse = 14f;
        [Tooltip("Temas sayılan boşluk (m): kutular bu kadar yaklaşınca vuruş başlar")]
        [SerializeField] private float skin = 0.05f;

        [Tooltip("Temas bu süreden uzun sürüyorsa yeni vuruş sadece sert yaklaşmada (sn)")]
        [SerializeField] private float freshContactTime = 0.1f;
        [Tooltip("Süren temasta bile vuruş sayılan yaklaşma hızı (m/sn)")]
        [SerializeField] private float hardApproachSpeed = 3f;

        [Header("Ayırma")]
        [Tooltip("İç içe geçince hedef ayrılma hızı = derinlik × bu (1/sn)")]
        [SerializeField] private float separationGain = 12f;
        [Tooltip("Hedef ayrılma hızının üst sınırı (m/sn)")]
        [SerializeField] private float maxSeparationSpeed = 6f;
        [Tooltip("Ayrılma hızına yaklaşma hızı (1/sn)")]
        [SerializeField] private float separationResponse = 25f;

        [Header("Ağ")]
        [Tooltip("Bu büyüklükten (m/sn) sert vuruşlar karşı tarafa RPC ile de bildirilir")]
        [SerializeField] private float rpcThreshold = 0.3f;
        [Tooltip("Birleştirme penceresinin en kısa ve en uzun süresi (sn)")]
        [SerializeField] private Vector2 mergeWindow = new Vector2(0.15f, 0.8f);
        [Tooltip("Karşının bizim tepkimizi hızında görmesi için ağ gecikmesine eklenen süre (hız süzgeçleri, sn)")]
        [SerializeField] private float velocityFilterLag = 0.25f;
        [Tooltip("Host'ta istemcinin arabası için RemoteExtrapolator'ın tahmin süresinden düşülen tick sayısı. "
               + "Ölçüm (13 Kasım, iki build + VDS): genel +2 tick payı host'ta istemci arabasını ~1.3 m (≈2 tick) fazla ileri gösteriyordu; "
               + "istemcide host arabası doğruydu (0.1 m).")]
        [SerializeField] private float serverViewTickCorrection = 2f;
        [Tooltip("Süren temasta ayırma itmesinin karşı tarafa yollanma aralığı (sn)")]
        [SerializeField] private float pushSendInterval = 0.1f;
        [Tooltip("Bundan eski vuruş RPC'leri yok sayılır (sn)")]
        [SerializeField] private float maxRpcAge = 0.6f;

        [Header("Kayıt")]
        [Tooltip("Temas başlangıç/bitişini de logla (vuruşlar her zaman loglanır)")]
        [SerializeField] private bool logContacts = true;

        private struct Shape
        {
            public Vector2 Center, Forward, Right;
            public float HalfWidth, HalfLength;
            public float Y, HalfHeight;
        }

        private class PairState
        {
            public float WindowStart = -100f;
            public float Window;
            public Vector3 Applied;      // pencere içinde kendi arabamıza uygulanan toplam vuruş
            public Vector3 OtherAssumed; // karşının uyguladığını varsaydığımız vuruş (hız düzeltmesi için)
            public bool Touching;
            public float TouchStart;
            public int Impulses, Pushes;
            public float PushTotal, ImpulseTotal;
            public Vector3 PendingPush;
            public float NextPushSend;
            public int RemotePushes;
            public float RemotePushTotal, LastRemotePushLog = -10f;
            public float MaxDepth;
        }

        private readonly Dictionary<long, PairState> pairs = new Dictionary<long, PairState>();
        private readonly Dictionary<RaceCar, BoxCollider> boxes = new Dictionary<RaceCar, BoxCollider>();
        private readonly List<RaceCar> ignoredCars = new List<RaceCar>();
        private float nextIgnoreRefresh;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        // ------------------------------------------------------------------ Uzak araba tahmini düzeltmesi

        private readonly Dictionary<RaceCar, RemoteExtrapolator> extrapolators = new Dictionary<RaceCar, RemoteExtrapolator>();

        private void Update()
        {
            // Host'ta istemcilerin arabaları: RemoteExtrapolator'ın gecikme tahmini 2 tick fazla, Strength ile kıs.
            // (RemoteExtrapolator ortak kod; harvester için doğru ayarlı olduğundan orada değiştirmiyoruz.)
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !nm.IsServer || serverViewTickCorrection <= 0f) return;
            float tick = 1f / Mathf.Max(1, nm.NetworkConfig.TickRate);
            for (int i = 0; i < RaceCar.All.Count; i++)
            {
                RaceCar car = RaceCar.All[i];
                if (car == null || !car.IsSpawned || car.IsOwner) continue;
                RemoteExtrapolator ex;
                if (!extrapolators.TryGetValue(car, out ex))
                {
                    ex = car.GetComponent<RemoteExtrapolator>();
                    extrapolators[car] = ex;
                }
                if (ex == null) continue;
                float delay = RawViewDelay(car.OwnerClientId);
                ex.Strength = delay > 0.001f ? Mathf.Clamp01((delay - serverViewTickCorrection * tick) / delay) : 1f;
            }
        }

        // ------------------------------------------------------------------ Fizik çarpışmasını kapat

        private void RefreshIgnoredCollisions()
        {
            bool changed = ignoredCars.Count != RaceCar.All.Count;
            for (int i = 0; !changed && i < ignoredCars.Count; i++) changed = ignoredCars[i] != RaceCar.All[i];
            if (!changed && Time.time < nextIgnoreRefresh) return;
            nextIgnoreRefresh = Time.time + 1f; // çarpıştırıcılar kapanıp açılınca durum sıfırlanabiliyor

            ignoredCars.Clear();
            ignoredCars.AddRange(RaceCar.All);
            for (int a = 0; a < ignoredCars.Count; a++)
            {
                if (ignoredCars[a] == null) continue;
                var collidersA = ignoredCars[a].GetComponentsInChildren<Collider>();
                for (int b = a + 1; b < ignoredCars.Count; b++)
                {
                    if (ignoredCars[b] == null) continue;
                    var collidersB = ignoredCars[b].GetComponentsInChildren<Collider>();
                    foreach (var ca in collidersA)
                        foreach (var cb in collidersB)
                            if (ca != null && cb != null && !ca.isTrigger && !cb.isTrigger) Physics.IgnoreCollision(ca, cb, true);
                }
            }
        }

        // ------------------------------------------------------------------ Tespit

        private void FixedUpdate()
        {
            RefreshIgnoredCollisions();
            float longest = 0f;
            foreach (var p in pairs.Values) if (p.Touching) longest = Mathf.Max(longest, Time.time - p.TouchStart);
            LongestActiveContact = longest;
            // Ağda sadece kendi arabamız sürülüyor; ağsız denemede sahnedeki her araba yerel, hepsi için çöz.
            // Aynı adımda hepsi AddForce'tan önceki hızları görür, yani ağsız durumda da iki tarafın yarısı simetrik.
            for (int i = 0; i < RaceCar.All.Count; i++)
            {
                RaceCar car = RaceCar.All[i];
                if (car != null && car.DrivesLocally) SolveFor(car);
            }
        }

        private void SolveFor(RaceCar local)
        {
            if (local.IsEliminated || local.Body == null || local.Body.isKinematic || !local.isActiveAndEnabled) return;

            Shape a;
            if (!TryGetShape(local, local.Body.position, local.Body.rotation, out a)) return;
            Vector3 vA = Flat(local.Body.linearVelocity);
            float dt = Time.fixedDeltaTime;

            for (int i = 0; i < RaceCar.All.Count; i++)
            {
                RaceCar other = RaceCar.All[i];
                if (other == null || other == local || other.IsEliminated || !other.isActiveAndEnabled) continue;

                Shape b;
                if (!TryGetShape(other, other.transform.position, other.transform.rotation, out b)) continue;
                PairState pair = GetPair(local, other);

                Vector2 normal;
                float overlap;
                Overlap(a, b, out normal, out overlap);
                bool near = Mathf.Abs(a.Y - b.Y) < a.HalfHeight + b.HalfHeight && overlap > -skin;
                if (!near)
                {
                    EndTouch(local, other, pair);
                    continue;
                }

                float depth = Mathf.Max(0f, overlap);
                Vector3 n = new Vector3(normal.x, 0f, normal.y);
                Vector3 point = ContactPoint(a, b, local.Body.position.y, other.transform.position.y);
                if (!pair.Touching)
                {
                    pair.Touching = true;
                    pair.TouchStart = Time.time;
                    pair.MaxDepth = 0f;
                    pair.Impulses = pair.Pushes = 0;
                    pair.PushTotal = pair.ImpulseTotal = 0f;
                    if (logContacts) Log("temas", local, other, Vector3.zero, n, depth, point, vA, Flat(other.Velocity), "");
                }
                pair.MaxDepth = Mathf.Max(pair.MaxDepth, depth);

                // Karşının hızı: gördüğümüz + pencere içindeyse onun da kendi yarısını uyguladığı varsayımı (sönerek)
                Vector3 vB = Flat(other.Velocity) + AssumedOtherChange(pair);
                Vector3 relative = vA - vB;
                float vn = Vector3.Dot(relative, n); // eksi: yaklaşıyoruz

                // Vuruş sadece temasın başında ya da sert yaklaşmada. Süren temasta (sürtünme, itişme) her pencere
                // sonunda yeni vuruş titreme yapıyordu (13 Kasım ölçümü: host'ta yanal ivme p95 36 m/sn²); orada yay çözer.
                bool freshContact = Time.time - pair.TouchStart < freshContactTime;
                if (vn < -minApproachSpeed && (freshContact || vn < -hardApproachSpeed))
                {
                    // Eşit kütle: toplam normal vuruş (1+e)·|vn|, her tarafa yarısı
                    float j = Mathf.Max(minBump, -(1f + restitution) * vn * 0.5f);
                    Vector3 dv = n * j;
                    // Sürtünme: teğet bağıl hızın yarısını sönümle, normal vuruşun μ katıyla sınırlı
                    Vector3 tangent = relative - n * vn;
                    dv += Vector3.ClampMagnitude(-tangent * 0.5f, friction * j);
                    dv = Vector3.ClampMagnitude(dv, maxImpulse);
                    Merge(local, other, pair, dv, point, "yerel", true, vA, vB, n, depth);
                    vA = Flat(local.Body.linearVelocity + dv); // aynı adımda diğer arabalar için güncel hız (AddForce henüz işlemedi)
                    relative = vA - vB;
                }

                // İç içe girme: hedef ayrılma hızına yumuşakça yaklaş (her taraf yarısını)
                if (depth > 0f)
                {
                    float separating = Vector3.Dot(relative, n);
                    float target = Mathf.Min(depth * separationGain, maxSeparationSpeed);
                    if (separating < target)
                    {
                        float push = (target - separating) * 0.5f * (1f - Mathf.Exp(-separationResponse * dt));
                        local.Body.AddForce(n * push, ForceMode.VelocityChange);
                        pair.Pushes++;
                        pair.PushTotal += push;
                        pair.PendingPush -= n * push; // karşıya düşen yarı: eşit ve zıt
                        vA += n * push;
                    }
                }
                SendPendingPush(local, other, pair, false);
            }
        }

        /// <summary>
        /// Süren temasta ayırma itmesinin karşıya düşen yarısını ~10 Hz toplu yollar. Karşı taraf teması kendi
        /// ekranında görüyorsa kendi yayı çalıştığı için yok sayar; görmüyorsa uygular (yoksa sadece bir taraf itilirdi).
        /// </summary>
        private void SendPendingPush(RaceCar local, RaceCar other, PairState pair, bool force)
        {
            if (!IsSpawned || !other.IsSpawned || other.DrivesLocally) { pair.PendingPush = Vector3.zero; return; }
            if (pair.PendingPush.sqrMagnitude < 1e-4f || (!force && Time.time < pair.NextPushSend)) return;
            pair.NextPushSend = Time.time + pushSendInterval;
            CarPushRpc(local.NetworkObjectId, other.NetworkObjectId, pair.PendingPush, RpcTarget.Single(other.OwnerClientId, RpcTargetUse.Temp));
            pair.PendingPush = Vector3.zero;
        }

        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        private void CarPushRpc(ulong fromCarId, ulong toCarId, Vector3 velocityChange, RpcParams rpcParams = default)
        {
            RaceCar local = RaceCar.Local;
            if (local == null || !local.IsSpawned || local.NetworkObjectId != toCarId || local.IsEliminated || local.Body.isKinematic) return;
            NetworkObject fromObject;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(fromCarId, out fromObject)) return;
            RaceCar other = fromObject.GetComponent<RaceCar>();
            if (other == null) return;
            PairState pair = GetPair(local, other);
            if (pair.Touching) return; // kendimiz de görüyoruz, yayımız zaten itiyor
            Vector3 dv = Vector3.ClampMagnitude(velocityChange, 2f);
            local.Body.AddForce(dv, ForceMode.VelocityChange);
            pair.RemotePushes++;
            pair.RemotePushTotal += dv.magnitude;
            if (Time.time - pair.LastRemotePushLog > 1f)
            {
                pair.LastRemotePushLog = Time.time;
                Log("uzak-itme", local, other, dv, dv.normalized, 0f, local.transform.position, Flat(local.Body.linearVelocity), Flat(other.Velocity),
                    "toplamUzakİtme=" + F(pair.RemotePushTotal) + " sayı=" + pair.RemotePushes);
            }
        }

        private void EndTouch(RaceCar local, RaceCar other, PairState pair)
        {
            if (!pair.Touching) return;
            SendPendingPush(local, other, pair, true);
            pair.Touching = false;
            if (logContacts)
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[CarHit] {0} ayrıldı araba={4} karşı={1} süre={2:F2}sn enDerin={3:F2}m vuruş={5} toplamVuruş={6:F2} itme={7} toplamİtme={8:F2}",
                    Stamp(), Label(other), Time.time - pair.TouchStart, pair.MaxDepth, Label(local),
                    pair.Impulses, pair.ImpulseTotal, pair.Pushes, pair.PushTotal));
        }

        private Vector3 AssumedOtherChange(PairState pair)
        {
            float age = Time.time - pair.WindowStart;
            if (age >= pair.Window || pair.Window <= 0f) return Vector3.zero;
            return pair.OtherAssumed * (1f - age / pair.Window);
        }

        /// <summary>
        /// Bir vuruşu çiftin birleştirme penceresine göre uygular: pencere yoksa uygular ve pencere açar;
        /// varsa sadece öncekinden büyükse farkını uygular.
        /// </summary>
        private void Merge(RaceCar local, RaceCar other, PairState pair, Vector3 dv, Vector3 point, string source, bool sendRpc,
            Vector3 vA, Vector3 vB, Vector3 n, float depth)
        {
            float now = Time.time;
            bool open = now - pair.WindowStart < pair.Window;
            Vector3 toApply;
            string kind = source;
            if (open)
            {
                if (dv.magnitude <= pair.Applied.magnitude + 0.05f)
                {
                    // Aynı çarpışmanın tekrarı (eski hız görüntüsü ya da karşının RPC'si): daha küçük, yok say
                    if (source == "uzak") Log("uzak-yoksay", local, other, dv, n, depth, point, vA, vB, "uygulanan=" + F(pair.Applied.magnitude));
                    return;
                }
                toApply = dv - pair.Applied;
                pair.Applied = dv;
                pair.OtherAssumed = other.DrivesLocally ? Vector3.zero : -dv;
                kind = source + "+birleşim";
                sendRpc = false; // ilk vuruşta zaten yollandı ya da karşıdan geldi
            }
            else
            {
                toApply = dv;
                pair.WindowStart = now;
                pair.Window = Mathf.Clamp(ViewDelay(OwnerOf(other)) + velocityFilterLag, mergeWindow.x, mergeWindow.y);
                pair.Applied = dv;
                // Karşı araba bu makinede sürülüyorsa (ağsız deneme) hızı zaten gerçek, varsayım gerekmez
                pair.OtherAssumed = other.DrivesLocally ? Vector3.zero : -dv;
            }

            local.ApplyHit(toApply, point);
            pair.Impulses++;
            pair.ImpulseTotal += toApply.magnitude;
            HitCount++;
            int c;
            hitsByCar.TryGetValue(local, out c);
            hitsByCar[local] = c + 1;
            LastHitMagnitude = toApply.magnitude;
            Log(kind, local, other, toApply, n, depth, point, vA, vB, "pencere=" + F(pair.Window));

            if (sendRpc && IsSpawned && dv.magnitude >= rpcThreshold && other.IsSpawned)
            {
                // Karşının yarısı: eşit ve zıt
                CarHitRpc(local.NetworkObjectId, other.NetworkObjectId, -dv, point, NetworkManager.ServerTime.Time,
                    RpcTarget.Single(other.OwnerClientId, RpcTargetUse.Temp));
            }
        }

        // ------------------------------------------------------------------ Vuruş RPC'si

        /// <summary>Vuran taraf, karşı arabanın sahibine ona düşen hız değişimini bildirir (sunucu üzerinden yönlenir).</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void CarHitRpc(ulong fromCarId, ulong toCarId, Vector3 velocityChange, Vector3 point, double sentServerTime, RpcParams rpcParams = default)
        {
            RaceCar local = RaceCar.Local;
            if (local == null || !local.IsSpawned || local.NetworkObjectId != toCarId || local.IsEliminated) return;
            NetworkObject fromObject;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(fromCarId, out fromObject)) return;
            RaceCar other = fromObject.GetComponent<RaceCar>();
            if (other == null) return;

            double age = NetworkManager.ServerTime.Time - sentServerTime;
            Vector3 vA = Flat(local.Body.linearVelocity), vB = Flat(other.Velocity);
            if (age > maxRpcAge)
            {
                Log("uzak-eski", local, other, velocityChange, Vector3.zero, 0f, point, vA, vB, "yaş=" + F((float)age));
                return;
            }
            Merge(local, other, GetPair(local, other), Vector3.ClampMagnitude(velocityChange, maxImpulse), point, "uzak", false,
                vA, vB, velocityChange.normalized, 0f);
        }

        // ------------------------------------------------------------------ Geometri

        private bool TryGetShape(RaceCar car, Vector3 position, Quaternion rotation, out Shape shape)
        {
            shape = default;
            BoxCollider box;
            if (!boxes.TryGetValue(car, out box) || box == null)
            {
                box = car.GetComponent<BoxCollider>();
                if (box == null) box = car.GetComponentInChildren<BoxCollider>();
                boxes[car] = box;
            }
            if (box == null) return false;

            Vector3 scale = box.transform.lossyScale;
            Vector3 size = Vector3.Scale(box.size, scale);
            // Kutu kökteyse verilen poz (sahibinde fizik pozu), değilse çocuğun dünyadaki yeri
            Vector3 center = box.transform == car.transform
                ? position + rotation * Vector3.Scale(box.center, scale)
                : box.transform.TransformPoint(box.center);
            Vector3 forward = Flat(rotation * Vector3.forward);
            if (forward.sqrMagnitude < 0.01f) forward = Flat(rotation * Vector3.up);
            forward.Normalize();

            shape.Center = new Vector2(center.x, center.z);
            shape.Forward = new Vector2(forward.x, forward.z);
            shape.Right = new Vector2(forward.z, -forward.x);
            shape.HalfWidth = size.x * 0.5f;
            shape.HalfLength = size.z * 0.5f;
            shape.Y = center.y;
            shape.HalfHeight = size.y * 0.5f + 0.3f; // eğimde burunlar yükselir, biraz pay
            return true;
        }

        /// <summary>Yatay düzlemde iki yönlü dikdörtgenin SAT testi. Normal B'den A'ya doğru, overlap eksiyse ayrılar.</summary>
        private static bool Overlap(Shape a, Shape b, out Vector2 normal, out float overlap)
        {
            normal = Vector2.zero;
            overlap = float.MaxValue;
            Vector2 d = a.Center - b.Center;
            Vector2 axisA0 = a.Forward, axisA1 = a.Right, axisB0 = b.Forward, axisB1 = b.Right;
            for (int k = 0; k < 4; k++)
            {
                Vector2 axis = k == 0 ? axisA0 : k == 1 ? axisA1 : k == 2 ? axisB0 : axisB1;
                float ra = a.HalfLength * Mathf.Abs(Vector2.Dot(a.Forward, axis)) + a.HalfWidth * Mathf.Abs(Vector2.Dot(a.Right, axis));
                float rb = b.HalfLength * Mathf.Abs(Vector2.Dot(b.Forward, axis)) + b.HalfWidth * Mathf.Abs(Vector2.Dot(b.Right, axis));
                float dist = Vector2.Dot(d, axis);
                float o = ra + rb - Mathf.Abs(dist);
                if (o < overlap)
                {
                    overlap = o;
                    normal = dist >= 0f ? axis : -axis;
                }
            }
            return true;
        }

        /// <summary>Temas noktası: iki kutunun birbirinin merkezine en yakın noktalarının ortası.</summary>
        private static Vector3 ContactPoint(Shape a, Shape b, float yA, float yB)
        {
            Vector2 pb = ClosestPoint(b, a.Center);
            Vector2 pa = ClosestPoint(a, b.Center);
            Vector2 p = (pa + pb) * 0.5f;
            return new Vector3(p.x, (a.Y + b.Y) * 0.5f, p.y);
        }

        private static Vector2 ClosestPoint(Shape s, Vector2 p)
        {
            Vector2 d = p - s.Center;
            float f = Mathf.Clamp(Vector2.Dot(d, s.Forward), -s.HalfLength, s.HalfLength);
            float r = Mathf.Clamp(Vector2.Dot(d, s.Right), -s.HalfWidth, s.HalfWidth);
            return s.Center + s.Forward * f + s.Right * r;
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        private PairState GetPair(RaceCar local, RaceCar other)
        {
            long key = ((long)local.GetInstanceID() << 32) ^ (uint)other.GetInstanceID();
            PairState pair;
            if (!pairs.TryGetValue(key, out pair))
            {
                pair = new PairState();
                pairs[key] = pair;
            }
            return pair;
        }

        // ------------------------------------------------------------------ Gecikme

        /// <summary>Kayıtta arabanın adı: ağda sahibinin istemci numarası, ağsızda obje adı.</summary>
        private static string Label(RaceCar car)
        {
            if (!car.IsSpawned) return car.name;
            return car.NetworkObject.IsPlayerObject ? car.OwnerClientId.ToString() : "bot" + car.NetworkObjectId;
        }

        private static ulong OwnerOf(RaceCar car)
        {
            return car.IsSpawned ? car.OwnerClientId : ulong.MaxValue;
        }

        /// <summary>
        /// Karşı arabayı ne kadar geriden görüyoruz (o da bizi aşağı yukarı o kadar geriden görüyor):
        /// ağ yolu (sahibi host ise tek bacak, değilse iki) + NGO yorumlama tamponu. RemoteExtrapolator ile aynı hesap.
        /// </summary>
        public static float ViewDelay(ulong otherOwner)
        {
            // Ölçülen gecikme varsa o (RemoteExtrapolator'ın kalibrasyonu; kullanıcı onayı 14 Kasım), yoksa eski formül
            float measured;
            if (RemoteExtrapolator.TryGetMeasuredDelay(otherOwner, out measured)) return measured;
            float delay = RawViewDelay(otherOwner);
            var nm = NetworkManager.Singleton;
            if (Instance != null && nm != null && nm.IsListening && nm.IsServer)
                delay = Mathf.Max(0f, delay - Instance.serverViewTickCorrection / Mathf.Max(1, nm.NetworkConfig.TickRate));
            return delay;
        }

        /// <summary>RemoteExtrapolator'ın kullandığı ham tahmin (düzeltmesiz).</summary>
        private static float RawViewDelay(ulong otherOwner)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || otherOwner == ulong.MaxValue) return 0f;
            float path;
            if (nm.IsServer) path = NetStatsHUD.ClientRtt(otherOwner) * 0.5f;
            else
            {
                float oneWay = NetStatsHUD.LocalRtt * 0.5f;
                path = otherOwner == NetworkManager.ServerClientId ? oneWay : oneWay * 2f;
            }
            float tick = 1f / Mathf.Max(1, nm.NetworkConfig.TickRate);
            return path + (nm.NetworkTimeSystem.TickLatency + 2f) * tick;
        }

        // ------------------------------------------------------------------ Kayıt

        private static string F(float v)
        {
            return v.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static string V(Vector3 v)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F2},{1:F2})", v.x, v.z);
        }

        private static string Stamp()
        {
            var nm = NetworkManager.Singleton;
            double server = nm != null && nm.IsListening ? nm.ServerTime.Time : Time.timeAsDouble;
            double ms = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
            ulong me = nm != null && nm.IsListening ? nm.LocalClientId : 0;
            return string.Format(CultureInfo.InvariantCulture, "ms={0:F1} st={1:F3} ben={2}", ms, server, me);
        }

        private void Log(string kind, RaceCar local, RaceCar other, Vector3 dv, Vector3 n, float depth, Vector3 point,
            Vector3 vA, Vector3 vB, string extra)
        {
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[CarHit] {0} {1} araba={14} karşı={2} dv={3} |dv|={4:F2} n={5} derinlik={6:F2} nokta={7} benimKonum={8} karşıKonum={9} vBen={10} vKarşı={11} gecikme={12:F3} {13}",
                Stamp(), kind, Label(other), V(dv), dv.magnitude, V(n), depth, V(point),
                V(local.transform.position), V(other.transform.position), V(vA), V(vB), ViewDelay(OwnerOf(other)), extra, Label(local)));
        }
    }
}
