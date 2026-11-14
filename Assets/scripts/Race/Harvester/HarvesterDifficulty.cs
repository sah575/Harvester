using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Harvester yapay zekâsının bütün ayarları tek yerde. Her seviye ayrı bir varlık (asset);
    /// "Seviye 1" varsayılanı <c>Assets/scripts/Race/Harvester/Data/HarvesterSeviye1.asset</c>.
    /// HarvesterAI ayarları her FixedUpdate'te buradan okur, yani yarış sırasında seviye değiştirmek (SetDifficulty) anında etki eder.
    ///
    /// Model (14 Kasım 2026, kullanıcı tasarımı): "arkanda hissetmelisin, hata affı yok".
    ///   açık g   = hedef arabanın pist boyunca harvester'ın önündeki mesafesi − tabla uzunluğu − carRearOffset
    ///              (tablanın önünden arabanın arkasına, m). Hedef: önündeki en geride kalan, elenmemiş, bitirmemiş araba.
    ///   Tasma    : g hiçbir zaman leashMax'ı geçmez (kalkış hariç). Geçecekse harvester pist boyunca o kadar ileri itilir
    ///              (fizik yok sayılır, viraj yavaşlaması yok). Hedef değişince tasma mevcut açıktan başlar ve
    ///              leashReelInSpeed ile leashMax'a çekilir (yeniden yakalama).
    ///   Baskı    : v = v_araba + c,  c = e ≥ 0 ? min(Kp·e, √(2·a·e)) : max(Kp·e, −maxBackoff),  e = g − pressureGap.
    ///              Saldırı yokken g en az pressureMinGap'te tutulur: kaza yapmayan araba biçilmez.
    ///   Kaza     : v_s < crashSpeedRatio · beklenen ve (beklenen − v_s) > crashMinSpeedLoss, ya da v_s < crashStopSpeed,
    ///              ya da çarptı (yavaşlama > crashImpactDecel ve v_s < crashImpactRatio · ortalama), ya da araba devrik. beklenen = min(son ~crashAverageTime sn ortalaması, pistin o yerindeki temiz tempo).
    ///   Saldırı  : kaza yapan hedefe v = v_araba + attackClosingSpeed ile gelir, alt sınır yok, makara biçer.
    ///   Kalkış   : ilk launchWindow sn kaza sayılmaz, makara biçmez, açık en az launchMinGap.
    /// </summary>
    [CreateAssetMenu(menuName = "Harvester/Harvester Zorluk Seviyesi", fileName = "HarvesterSeviye")]
    public class HarvesterDifficulty : ScriptableObject
    {
        [Tooltip("Seviye numarası (HUD ve ileride silah açılımı için)")]
        public int level = 1;
        [Tooltip("Oyunda gösterilecek ad")]
        public string displayName = "Seviye 1";

        [Header("Hız (fizik dışı, kinematik)")]
        [Tooltip("En yüksek hız (m/sn). Arabaların son hızı 38–43 m/sn; yeniden yakalamada harvester bunun çok üstüne çıkabilir.")]
        public float maxSpeed = 100f;
        [Tooltip("Hedef yokken gezinme hızı (m/sn)")]
        public float cruiseSpeed = 30f;
        [Tooltip("Hızlanma (m/sn²). Arabadan yüksek olmalı ki tasma nadiren devreye girsin.")]
        public float acceleration = 25f;
        [Tooltip("Yavaşlama (m/sn²). Arabanın freninden (~22) yüksek olmalı ki fren yapan arabaya kendiliğinden yaklaşmasın.")]
        public float braking = 35f;
        [Tooltip("Çalışmıyorken (geri sayım, yarış bitti) durma yavaşlaması (m/sn²)")]
        public float stopDeceleration = 8f;

        [Header("Tasma")]
        [Tooltip("En fazla açık (m, tablanın önünden arabanın arkasına, pist boyunca). Kalkış penceresi dışında harvester bundan uzağa düşmez: " +
                 "düşecekse pist boyunca ileri itilir (virajda yavaşlama yok, hız sınırı yok).")]
        public float leashMax = 30f;
        [Tooltip("Yeniden yakalama (m/sn): hedef değişince ya da kalkışta tasma mevcut açıktan başlar ve en az bu bağıl hızla leashMax'a kısalır")]
        public float leashReelInSpeed = 50f;
        [Tooltip("Yeniden yakalamanın sonunda tasmanın yavaşlaması (m/sn²): kısalma hızı en fazla karekök(2 × bu × (tasma − leashMax)). " +
                 "Harvester sürüye roket gibi gelip sonra sert fren yapmasın, banda yumuşak otursun.")]
        public float leashLandingDecel = 20f;
        [Tooltip("Araba merkezinden arka tampona uzaklık (m); açık arabanın arkasına ölçülsün")]
        public float carRearOffset = 2.2f;

        [Header("Baskı")]
        [Tooltip("Hedeflenen açık (m). Harvester buraya yumuşak sönümle oturur (15–25 m bandının ortası).")]
        public float pressureGap = 20f;
        [Tooltip("Bandın alt ve üst ucu (m; sadece etiket ve test)")]
        public Vector2 pressureBand = new Vector2(15f, 25f);
        [Tooltip("Saldırı yokken en küçük açık (m). Kaza yapmayan araba bundan yakına gelinmez, yani biçilmez.")]
        public float pressureMinGap = 8f;
        [Tooltip("Açık hatası kazancı Kp (1/sn): kapanma hızı = Kp × (açık − pressureGap). Küçük: daha yumuşak.")]
        public float gapGain = 0.8f;
        [Tooltip("Banda yumuşak oturma (m/sn²): kapanma hızı en fazla karekök(2 × bu × açık hatası), salınım olmaz")]
        public float softLandingDecel = 6f;
        [Tooltip("Çok yakınken en fazla geri çekilme (m/sn, arabanın hızının altına)")]
        public float maxBackoff = 6f;

        [Header("Kaza tespiti (hata affı yok)")]
        [Tooltip("Araba hızının süzgeç zaman sabiti (sn): anlık hız gürültüsü kaza sayılmasın")]
        public float speedSmoothing = 0.15f;
        [Tooltip("Ortalama hız penceresi (sn): 'son birkaç saniyenin ortalaması' (üstel ortalama zaman sabiti)")]
        public float crashAverageTime = 3f;
        [Tooltip("Kaza eşiği: süzülmüş hız / beklenen hız bunun altına düşerse kaza. Beklenen = min(ortalama hız, o yerdeki temiz tempo).")]
        [Range(0.2f, 0.95f)] public float crashSpeedRatio = 0.65f;
        [Tooltip("Kaza sayılması için beklenenden en az bu kadar (m/sn) hız kaybı da gerekir (yavaş yerde gürültü kaza olmasın)")]
        public float crashMinSpeedLoss = 6f;
        [Tooltip("Bu hızın (m/sn) altına düşen araba durmuş sayılır: kaza")]
        public float crashStopSpeed = 5f;
        [Tooltip("Çarpma: süzülmüş hız bundan hızlı düşüyorsa (m/sn²) frenle değil çarparak yavaşlıyordur. Arabaların freni ~22 m/sn².")]
        public float crashImpactDecel = 30f;
        [Tooltip("Çarpma kaza sayılması için hız ayrıca ortalamanın bu katının altına inmeli (duvara sıyırıp hız kaybetmeyen kaza değil)")]
        [Range(0.3f, 0.95f)] public float crashImpactRatio = 0.8f;
        [Tooltip("Arabanın yukarı ekseni dünya yukarısıyla bu değerden (kosinüs) az örtüşüyorsa devrik: kaza")]
        public float crashFlipUp = 0.35f;
        [Tooltip("Temiz tempo: virajda temiz süren arabanın yanal ivmesi (m/sn²). Düşük = fren yapan iyi sürücüye daha hoşgörülü.")]
        public float paceLateralAcceleration = 13f;
        [Tooltip("Temiz tempo: viraj öncesi fren planı (m/sn²). Düşük = erken fren yapana daha hoşgörülü.")]
        public float paceBraking = 12f;
        [Tooltip("Temiz tempo: düzlükte üst hız (m/sn)")]
        public float paceTopSpeed = 45f;

        [Header("Saldırı")]
        [Tooltip("Kaza yapan hedefe arabanın hızının bu kadar (m/sn) üstünde gelir")]
        public float attackClosingSpeed = 25f;
        [Tooltip("Saldırı en fazla bu kadar sürer (sn); biçemezse (araba toparlanıp kaçtıysa) baskıya döner")]
        public float attackMaxDuration = 8f;
        [Tooltip("Saldırıda hedefin yanal konumuna kayma hızı (m/sn)")]
        public float attackLateralSpeed = 12f;

        [Header("Kalkış")]
        [Tooltip("Kalkış penceresi (sn, harvester salındıktan sonra): kaza tespiti yok, makara biçmez, açık en az launchMinGap. " +
                 "Tasma yine de geçerli: harvester sürünün hemen arkasına gelir.")]
        public float launchWindow = 10f;
        [Tooltip("Kalkış penceresinde en küçük açık (m)")]
        public float launchMinGap = 12f;

        [Header("Görünmezken ışınlanma (son çare)")]
        [Tooltip("Açık bu değeri (m) aşar ve hiçbir araba harvester'ı göremezse pist boyunca ileri ışınlanır (yeniden yakalamayı kısaltır)")]
        public bool catchupTeleport = true;
        [Tooltip("Işınlanma için en az açık (m)")]
        public float teleportGap = 120f;
        [Tooltip("Görüş mesafesi (m). 0 = sisten hesapla (RenderSettings), sis yoksa 150.")]
        public float visibleDistanceOverride = 0f;
        [Tooltip("Sisten hesapta: görünürlük bu oranın altına düşünce (sis faktörü) görünmez say. 0.15 = %85 sis.")]
        [Range(0.01f, 0.5f)] public float fogVisibilityThreshold = 0.15f;
        [Tooltip("Görüş mesafesinin alt sınırı (m)")]
        public float minVisibleDistance = 80f;
        [Tooltip("İki ışınlanma arasında en az süre (sn)")]
        public float teleportCooldown = 3f;

        [Header("Yanal takip")]
        [Tooltip("Hedefin yanal konumuna doğru kayma hızı (m/sn)")]
        public float lateralAimSpeed = 6.5f;
        [Tooltip("Gecikme payı (sn): harvester baskıda hedefin yanal konumunu bu kadar eskiden görür")]
        public float reactionDelay = 0.2f;
        [Tooltip("Gövde yönünün pist yönüne oturma zaman sabiti (sn)")]
        public float headingSmoothing = 0.08f;

        [Header("Tabla (biçme)")]
        [Tooltip("Tabla genişliği (m). Yol 14 m; genişledikçe yana kaçmak zorlaşır. Görsel de buna göre ölçeklenir.")]
        public float headerWidth = 9f;
        [Tooltip("Biçme kutusunun derinliği (m, tablanın önünden geriye)")]
        public float headerDepth = 3.5f;
        [Tooltip("Biçme kutusunun tablanın önünden ne kadar ileri taştığı (m). Araba fiziksel temastan hemen önce biçilir.")]
        public float headerLead = 1.2f;
        [Tooltip("Açık bu değerden (m) küçükse makara biçme hızına çıkar (kalkış penceresi hariç)")]
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
        [Tooltip("Bu kadar gerideki (m, kökten) araba hâlâ 'önde' sayılır (yanındaki araba)")]
        public float behindTolerance = 6f;

        // İleride: [Header("Silahlar")] roket, yağ, makineli ayarları buraya.

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
