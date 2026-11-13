using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Harvester yapay zekâsının bütün zorluk ayarları tek yerde. Her seviye ayrı bir varlık (asset);
    /// "Seviye 1" varsayılanı <c>Assets/scripts/Race/Harvester/Data/HarvesterSeviye1.asset</c>.
    /// Silahlar (roket, yağ, makineli) ileride buraya kendi başlıklarıyla eklenecek; HarvesterAI ayarları
    /// her FixedUpdate'te buradan okur, yani yarış sırasında seviye değiştirmek (SetDifficulty) anında etki eder.
    /// </summary>
    [CreateAssetMenu(menuName = "Harvester/Harvester Zorluk Seviyesi", fileName = "HarvesterSeviye")]
    public class HarvesterDifficulty : ScriptableObject
    {
        [Tooltip("Seviye numarası (HUD ve ileride silah açılımı için)")]
        public int level = 1;
        [Tooltip("Oyunda gösterilecek ad")]
        public string displayName = "Seviye 1";

        [Header("Hız")]
        [Tooltip("Mutlak en yüksek hız (m/sn). Asıl tavan açığa göre catchupSpeedCurve'den gelir; bu sadece üst sınır. Arabanın son hızı ~40 m/sn.")]
        public float maxSpeed = 75f;
        [Tooltip("Hedef yokken gezinme hızı (m/sn)")]
        public float cruiseSpeed = 30f;
        [Tooltip("Hızlanma (m/sn²)")]
        public float acceleration = 8f;
        [Tooltip("Yavaşlama/fren (m/sn²)")]
        public float braking = 14f;
        [Tooltip("Virajda en büyük yanal ivme (m/sn²). Viraj hızı = karekök(bu × yarıçap). Arabanın test sürücüsü 13, en fazla ~15; bunun altında kalmalı ki iyi sürücü virajda açabilsin.")]
        public float lateralAcceleration = 13f;

        [Header("Zaman açığı modeli")]
        [Tooltip("Hedef zaman açığı τ* (sn): tablanın önü ile hedef arası açık / hedefin hızı. Baskıda harvester buraya yumuşakça oturur. " +
                 "Hata payı bundan gelir: bundan büyük bir zaman kaybı biçilmek demek.")]
        public float timeGapTarget = 0.6f;
        [Tooltip("Bandın üst ucu (sn). Bunun üstü 'yakalama', altı 'baskı' (sadece etiket ve test; kontrolcü τ*'a çalışır).")]
        public float timeGapBandHigh = 1.5f;
        [Tooltip("Zaman açığı hesabında hedef hızının alt sınırı (m/sn): yavaş/duran hedefte açık sıfıra çökmesin")]
        public float timeGapMinSpeed = 15f;
        [Tooltip("Açık hatası kazancı Kp (1/sn): kapanma hızı = Kp × (açık − τ*·v)")]
        public float gapGain = 0.5f;
        [Tooltip("Banda yumuşak oturma (m/sn²): kapanma hızı en fazla karekök(2 × bu × açık hatası). Kapanma bu yavaşlamayla sıfıra iner, salınım olmaz.")]
        public float softLandingDecel = 4f;
        [Tooltip("Çok yakınken en fazla geri çekilme (m/sn). Küçük: baskıda neredeyse hiç geri çekilmez, küçük hata açığı daraltır.")]
        public float maxBackoff = 1.5f;
        [Tooltip("Baskı sürünmesi (sn/sn): hedef bandın içinde kaldıkça hedeflenen zaman açığı her saniye bu kadar daralır. " +
                 "Kaçamayan (yavaş) araba eninde sonunda biçilir; açığı τ* + creepResetMargin üstüne açan araba sürünmeyi sıfırlar.")]
        public float pressureCreepRate = 0.012f;
        [Tooltip("Zaman açığı τ* + bu değerin (sn) üstüne çıkınca baskı sürünmesi sıfırlanır")]
        public float creepResetMargin = 0.3f;
        [Tooltip("Hedef hızı referansının yükselme zaman sabiti (sn)")]
        public float targetSpeedRiseTime = 0.25f;
        [Tooltip("Hedef hızı referansının düşme zaman sabiti (sn): harvester hedefin yavaşlamasına geç inanır, hata yapanın üstüne gelir")]
        public float targetSpeedFallTime = 1.2f;

        [Header("Kalkış atağı")]
        [Tooltip("Çalışmaya başlayınca (SetRunning(true)) sürüye yetişene dek ivme (m/sn²). Araba 12 ile kalkıyor.")]
        public float launchAcceleration = 28f;
        [Tooltip("Kalkış atağında hız tavanı (m/sn)")]
        public float launchMaxSpeed = 58f;
        [Tooltip("Kalkış atağında banda oturma yavaşlaması (m/sn²): kapanma hızı = karekök(2 × bu × açık hatası); frenden küçük olmalı")]
        public float launchLandingDecel = 10f;
        [Tooltip("Kalkış atağı en fazla bu kadar sürer (sn). Hedef baskı mesafesine girince (hızı ne olursa olsun) hemen biter.")]
        public float launchMaxDuration = 8f;
        [Tooltip("Kalkış penceresi (sn, çalışmaya başladıktan itibaren): bu sürede harvester hedefin launchHoldGap gerisinde durur, " +
                 "baskı payı ve av modu yok, ivme launchAcceleration. Kalkan araba ilk saniyelerde biçilmesin, ama sürü de kaçmasın.")]
        public float launchWindow = 10f;
        [Tooltip("Kalkış penceresinde en küçük takip açığı (m, tablanın önünden); pencerede geri çekilme sınırı da yok")]
        public float launchHoldGap = 12f;

        [Header("Lastik bant (insafsızlık)")]
        [Tooltip("Bu mesafenin içinde (tablanın önünden) baskı modundadır: hız tavanı pressureSpeedCap")]
        public float pressureDistance = 25f;
        [Tooltip("Baskı mesafesinin dışında hız tavanı (m/sn), açığa (tablanın önünden, m) göre. Gerçekçilik dışı: uzaktaki harvester'ı oyuncu görmüyor. " +
                 "Varsayılan: 25 m → 39.5, 65 m (kökten ~75 m) → 50, 140 m (kökten ~150 m) → 70.")]
        public AnimationCurve catchupSpeedCurve = Linear(new Vector2(25f, 39.5f), new Vector2(65f, 50f), new Vector2(140f, 70f));
        [Tooltip("Viraj yanal ivmesinin çarpanı, açığa göre (uzaktayken virajı daha hızlı alır)")]
        public AnimationCurve catchupCornerCurve = Linear(new Vector2(40f, 1f), new Vector2(90f, 1.6f), new Vector2(150f, 2.2f));
        [Tooltip("Baskı modunda hız tavanı (m/sn). Arabanın son hızının biraz altında olmalı ki iyi sürücü düzlükte açabilsin.")]
        public float pressureSpeedCap = 39.5f;

        [Header("Hataya ceza")]
        [Tooltip("Hedef bu mesafeden (m) yakınken yarış temposunun punishSpeedRatio katının altına düşerse (döndü, bariyere çarptı) av modu açılır")]
        public float punishRange = 50f;
        [Tooltip("Av modu eşiği: hedef hızı / yarış temposu (o noktada temiz sürüşün hızı)")]
        [Range(0f, 1f)] public float punishSpeedRatio = 0.6f;
        [Tooltip("Yarış temposu: temiz süren arabanın son hızı (m/sn)")]
        public float paceTopSpeed = 40f;
        [Tooltip("Yarış temposu: temiz süren arabanın viraj yanal ivmesi (m/sn²). Araba ~15'e kadar çıkabiliyor.")]
        public float paceLateralAcceleration = 15f;
        [Tooltip("Yarış temposu: temiz süren arabanın fren planı (m/sn²)")]
        public float paceBraking = 18f;
        [Tooltip("Av modunda viraj yanal ivmesinin çarpanı")]
        public float punishCornerBoost = 2f;
        [Tooltip("Av modunda yakındayken hız tavanı (m/sn); baskı tavanının yerine geçer")]
        public float punishSpeedCap = 46f;
        [Tooltip("Av modunda en az kapanma hızı (m/sn). 0: av modunda geri çekilmez ve hedef hızı referansı yarış temposuna çekilir, " +
                 "yani harvester tempoyu korur ve arabanın kaybettiği zaman kadar açık kapanır (hata payı = zaman açığı).")]
        public float punishClosingSpeed = 0f;
        [Tooltip("Av modu tetiklendikten sonra en az bu kadar sürer (sn)")]
        public float punishHold = 2f;

        [Header("Görünmezken yetişme")]
        [Tooltip("Görünmezken yetişme açık mı: harvester önündeki en yakın arabanın maxGap'ten fazla gerisindeyse ve hiçbir araba onu göremiyorsa pist boyunca ileri ışınlanır")]
        public bool catchupTeleport = true;
        [Tooltip("En fazla açık (m, kökten pist boyunca)")]
        public float maxGap = 120f;
        [Tooltip("Görüş mesafesi (m). 0 = sisten hesapla (RenderSettings), sis yoksa 150.")]
        public float visibleDistanceOverride = 0f;
        [Tooltip("Sisten hesapta: görünürlük bu oranın altına düşünce (sis faktörü) görünmez say. 0.15 = %85 sis.")]
        [Range(0.01f, 0.5f)] public float fogVisibilityThreshold = 0.15f;
        [Tooltip("Görüş mesafesinin alt sınırı (m)")]
        public float minVisibleDistance = 80f;
        [Tooltip("İki ışınlanma arasında en az süre (sn)")]
        public float teleportCooldown = 3f;

        [Header("Direksiyon")]
        [Tooltip("En büyük dönüş hızı (derece/sn)")]
        public float turnRate = 50f;
        [Tooltip("Dönüş hızının değişim hızı (derece/sn²); büyük = daha çevik")]
        public float yawAcceleration = 160f;
        [Tooltip("Hedefin yanal konumuna doğru kayma hızı (m/sn); yana kaçan arabayı ne çabuk izler")]
        public float lateralAimSpeed = 6.5f;

        [Header("Tepki")]
        [Tooltip("Gecikme payı (sn): harvester hedefin yanal konumunu bu kadar eskiden görür. Yana kaçmayı mümkün kılar.")]
        public float reactionDelay = 0.2f;

        [Header("Tabla (biçme)")]
        [Tooltip("Tabla genişliği (m). Yol 14 m; genişledikçe yana kaçmak zorlaşır. Görsel de buna göre ölçeklenir.")]
        public float headerWidth = 9f;
        [Tooltip("Biçme kutusunun derinliği (m, tablanın önünden geriye)")]
        public float headerDepth = 3.5f;
        [Tooltip("Biçme kutusunun tablanın önünden ne kadar ileri taştığı (m). Araba fiziksel temastan hemen önce biçilir.")]
        public float headerLead = 1.2f;
        [Tooltip("Hedef bu mesafeden yakınsa makara biçme hızına çıkar (m)")]
        public float mowingRange = 45f;

        [Header("Çarpma")]
        [Tooltip("Yandan çarpınca arabaya verilen yana savrulma hızı (m/sn)")]
        public float hitSideSpeed = 9f;
        [Tooltip("Yandan çarpınca ileri savrulma hızı (m/sn), harvester hızıyla ölçeklenir")]
        public float hitForwardSpeed = 5f;
        [Tooltip("Arkadan çarpan arabayı geri itme hızı (m/sn)")]
        public float hitRearSpeed = 4f;
        [Tooltip("Aynı arabaya iki çarpma arasında en az süre (sn)")]
        public float hitCooldown = 0.7f;

        [Header("Hedef seçimi")]
        [Tooltip("Yeni aday mevcut hedeften en az bu kadar (m) yakınsa hedef değişir")]
        public float targetSwitchMargin = 15f;
        [Tooltip("Hedef en az bu kadar süre (sn) tutulur")]
        public float minTargetHold = 1.5f;
        [Tooltip("Bu kadar gerideki (m) araba hâlâ 'önde' sayılır (yanındaki araba)")]
        public float behindTolerance = 6f;

        // İleride: [Header("Silahlar")] roket, yağ, makineli ayarları buraya.

        /// <summary>Uzaklık tabanlı tavan: baskı mesafesinin dışında catchupSpeedCurve, içinde pressureSpeedCap.</summary>
        public float SpeedCapForGap(float effectiveGap)
        {
            if (effectiveGap <= pressureDistance || catchupSpeedCurve == null || catchupSpeedCurve.length == 0) return pressureSpeedCap;
            return Mathf.Max(pressureSpeedCap, catchupSpeedCurve.Evaluate(effectiveGap));
        }

        /// <summary>Viraj yanal ivmesi çarpanı, açığa göre.</summary>
        public float CornerBoostForGap(float effectiveGap)
        {
            if (catchupCornerCurve == null || catchupCornerCurve.length == 0) return 1f;
            return Mathf.Max(1f, catchupCornerCurve.Evaluate(effectiveGap));
        }

        /// <summary>Doğrusal (köşeli) eğri; uçlardan sonra sabit.</summary>
        public static AnimationCurve Linear(params Vector2[] points)
        {
            var keys = new Keyframe[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float inT = i > 0 ? (points[i].y - points[i - 1].y) / (points[i].x - points[i - 1].x) : 0f;
                float outT = i < points.Length - 1 ? (points[i + 1].y - points[i].y) / (points[i + 1].x - points[i].x) : 0f;
                keys[i] = new Keyframe(points[i].x, points[i].y, inT, outT);
            }
            return new AnimationCurve(keys);
        }

        /// <summary>Varlık atanmamışsa kullanılan "Seviye 1" varsayılanı.</summary>
        public static HarvesterDifficulty CreateDefault()
        {
            var d = CreateInstance<HarvesterDifficulty>();
            d.name = "Seviye 1 (varsayılan)";
            d.hideFlags = HideFlags.DontSave;
            return d;
        }
    }
}
