using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Harvester.Race;
using UnityEditor;
using UnityEngine;

// Kimlik kartından (Assets/Cars/<id>/<id>_kimlik.json) performans değerleri üretir ve araba tanımına yazar:
// temel fizik değerleri (baseStats), performans geliştirme ağacı ve sıfır olan parça fiyatları.
// Menü: Harvester > Araç > Kimlik Kartından Performans Üret.
//
// Kart sözleşmesi (.agents/skills/arac-modelleme/references/spesifikasyon.md, bölüm 8):
//   sinif (coupe, supercar, muscle, hatchback, grandtourer...), cekis (RWD/AWD/FWD), tahminiKutleKg,
//   karakter { sonHiz, ivme, yolTutus, fren, dayaniklilik } her biri 1–5.
//
// Üç katman:
//   1. Puan tablosu (aşağıdaki diziler): karakter puanı → ana değer. Bütün arabalar aynı tabloyu kullanır, yani
//      stok arabalar arasındaki fark tablonun iki ucu kadardır (son hız 38.5–43 m/sn, %12).
//   2. Sınıf profili: puanla anlatılamayan kişilik (itiş eğrisi, arka/ön tutuş oranı, direksiyon tepkisi, drift'e
//      yatkınlık, bastırma, ağırlık merkezi payı, yatma sertliği, fiyat çarpanı).
//   3. Geometri ve kütle: ağırlık merkezi yüksekliği modelden, direksiyon açısı dingil mesafesinden, yay/sönüm/viraj
//      demiri kütleden ve iz genişliğinden türetilir (aynı oturma ve yatma).
// Geliştirme ağacı puanın tersine ölçeklenir (zayıf olduğu konuda daha çok gelişir), fiyatlar sınıf çarpanıyla.
public static partial class CarBuilder
{
    // ------------------------------------------------------------------ 1. Puan tablosu (indeks = puan − 1)

    static readonly float[] SonHizTablosu = { 37f, 38.5f, 40f, 41.5f, 43f };        // topSpeed (m/sn)
    static readonly float[] IvmeTablosu = { 9.5f, 10.5f, 11.5f, 12.5f, 13.5f };    // maxAcceleration (m/sn²)
    static readonly float[] FrenTablosu = { 18f, 20f, 22f, 24f, 26f };             // brakeDeceleration (m/sn²)
    static readonly float[] VirajTablosu = { 12.5f, 13.5f, 14.5f, 15.25f, 16f };   // maxCornerAccel (m/sn²)
    static readonly float[] SurtunmeTablosu = { 1.35f, 1.42f, 1.5f, 1.57f, 1.65f }; // tireFriction
    static readonly float[] OnTutusTablosu = { 24f, 26f, 28f, 30f, 32f };           // frontGrip (1/sn); arka = ön × sınıf oranı
    static readonly float[] DayaniklilikTablosu = { 40f, 50f, 60f, 70f, 80f };      // durability (puan)
    static readonly float[] GelisimPayi = { 1.3f, 1.15f, 1f, 0.85f, 0.7f };         // geliştirme etkisinin çarpanı

    // ------------------------------------------------------------------ 2. Sınıf profili

    class SinifProfili
    {
        public Keyframe[] egri;            // itiş eğrisi (hız/son hız → ivme oranı)
        public float arkaOnOrani;          // rearGrip / frontGrip: 1'e yaklaştıkça ürkek, büyüdükçe sakin
        public float direksiyonHizi;       // steerSpeed (derece/sn)
        public float yuksekHizAcisi;       // highSpeedSteerAngle (derece)
        public float donusYardimi;         // steerAssist (1/sn)
        public float elFreniArkaTutus;     // handbrakeRearGrip: küçük = kuyruk kolay kayar
        public float driftSapma;           // driftYawBoost
        public float tutusYuksekligi;      // gripForceHeight: küçük = virajda çok yatar
        public float bastirma;             // downforce
        public float bosVites;             // coastDeceleration
        public float agirlikMerkeziPayi;   // ölçülen ağırlık merkezi yüksekliğine eklenir (m)
        public float yatmaSertligi;        // viraj demirinin çarpanı
        public float fiyatCarpani;         // parça ve geliştirme fiyatları
        public float direksiyonEki;        // en büyük direksiyon açısına eklenen (derece)
        public string ozet;
    }

    static Keyframe[] Egri(params float[] xy)
    {
        var k = new Keyframe[xy.Length / 2];
        for (int i = 0; i < k.Length; i++) k[i] = new Keyframe(xy[i * 2], xy[i * 2 + 1]);
        return k;
    }

    static readonly Dictionary<string, SinifProfili> Siniflar = new Dictionary<string, SinifProfili>
    {
        { "coupe", new SinifProfili { egri = Egri(0f, 1f, 0.5f, 0.85f, 0.85f, 0.45f, 1f, 0f), arkaOnOrani = 1.2f, direksiyonHizi = 160f,
            yuksekHizAcisi = 9f, donusYardimi = 5f, elFreniArkaTutus = 0.55f, driftSapma = 1.25f, tutusYuksekligi = 0.75f, bastirma = 2.5f,
            bosVites = 2.5f, agirlikMerkeziPayi = 0f, yatmaSertligi = 1f, fiyatCarpani = 1f, direksiyonEki = 0f,
            ozet = "dengeli, öngörülebilir" } },
        { "supercar", new SinifProfili { egri = Egri(0f, 0.9f, 0.5f, 0.9f, 0.85f, 0.6f, 1f, 0f), arkaOnOrani = 1.05f, direksiyonHizi = 190f,
            yuksekHizAcisi = 10f, donusYardimi = 5.5f, elFreniArkaTutus = 0.5f, driftSapma = 1.3f, tutusYuksekligi = 0.8f, bastirma = 3.5f,
            bosVites = 2.5f, agirlikMerkeziPayi = -0.01f, yatmaSertligi = 1.15f, fiyatCarpani = 1.2f, direksiyonEki = 0f,
            ozet = "kalkışta patinaj payı, üst devirde güçlü; arka hafif, direksiyon sert ve hızlı (ürkek)" } },
        { "muscle", new SinifProfili { egri = Egri(0f, 1f, 0.4f, 0.92f, 0.8f, 0.38f, 1f, 0f), arkaOnOrani = 1.04f, direksiyonHizi = 140f,
            yuksekHizAcisi = 8f, donusYardimi = 4.2f, elFreniArkaTutus = 0.42f, driftSapma = 1.45f, tutusYuksekligi = 0.7f, bastirma = 1.8f,
            bosVites = 2.2f, agirlikMerkeziPayi = 0.04f, yatmaSertligi = 0.85f, fiyatCarpani = 1.1f, direksiyonEki = 0f,
            ozet = "alt devirde tork, üstte erken söner; ağır burun, yumuşak gövde, el freninde kuyruk kolay çıkar" } },
        { "hatchback", new SinifProfili { egri = Egri(0f, 1f, 0.5f, 0.85f, 0.85f, 0.42f, 1f, 0f), arkaOnOrani = 1.25f, direksiyonHizi = 180f,
            yuksekHizAcisi = 10f, donusYardimi = 5.5f, elFreniArkaTutus = 0.6f, driftSapma = 1.15f, tutusYuksekligi = 0.75f, bastirma = 2.2f,
            bosVites = 2.8f, agirlikMerkeziPayi = 0.03f, yatmaSertligi = 1.1f, fiyatCarpani = 0.9f, direksiyonEki = 2f,
            ozet = "çevik ve sakin arka, kısa dönüş; yüksek gövde" } },
        { "grandtourer", new SinifProfili { egri = Egri(0f, 0.95f, 0.5f, 0.85f, 0.85f, 0.5f, 1f, 0f), arkaOnOrani = 1.28f, direksiyonHizi = 145f,
            yuksekHizAcisi = 8.5f, donusYardimi = 4.5f, elFreniArkaTutus = 0.58f, driftSapma = 1.15f, tutusYuksekligi = 0.78f, bastirma = 2.8f,
            bosVites = 2.4f, agirlikMerkeziPayi = 0.02f, yatmaSertligi = 1f, fiyatCarpani = 1.15f, direksiyonEki = 0f,
            ozet = "yüksek hızda sakin ve kararlı, tepkileri ağır" } },
    };

    // Parça fiyatlarının temeli (Hurda): varyant v için temel × (0.6 + 0.4·v) × sınıf çarpanı, 50'ye yuvarlanır
    static readonly Dictionary<string, int> ParcaTemelFiyat = new Dictionary<string, int>
    {
        { "bumper_front", 600 }, { "bumper_rear", 500 }, { "exhaust", 450 }, { "fender", 800 }, { "frunk", 500 },
        { "headlight", 350 }, { "hood", 600 }, { "interior_passenger", 400 }, { "interior_pillar", 300 }, { "mirror", 250 },
        { "neon", 450 }, { "rollcage", 900 }, { "roof_scoop", 400 }, { "skirts", 500 }, { "spoiler", 700 }, { "steering", 300 },
        { "taillight", 350 }, { "rim", 600 }, { "decal", 200 }, { "tire_lettering", 150 },
    };

    // ------------------------------------------------------------------ Kart

    [Serializable]
    class KimlikKarti
    {
        public string id;
        public string gorunenAd;
        public string sinif;
        public string cekis;
        public float tahminiKutleKg;
        public Karakter karakter;
        public string aciklama;
    }

    [Serializable]
    class Karakter
    {
        public int sonHiz, ivme, yolTutus, fren, dayaniklilik;
    }

    [MenuItem("Harvester/Araç/Kimlik Kartından Performans Üret")]
    public static void GeneratePerformanceFromCards()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CarCatalog>(CatalogPath);
        var defs = new List<CarDefinition>();
        if (catalog != null) defs.AddRange(catalog.cars);
        else foreach (string id in Cars) defs.Add(AssetDatabase.LoadAssetAtPath<CarDefinition>("Assets/Cars/" + id + "/" + id + "_Tanim.asset"));
        var table = new StringBuilder();
        table.AppendLine("| Araba | Sınıf | Kart (S/İ/T/F/D) | Kütle | Son hız | İvme | Fren | Viraj | Sürtünme | Ön/arka tutuş | Dayanıklılık | AM yük. |");
        table.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var def in defs)
        {
            if (def == null) continue;
            string line;
            if (ApplyCard(def, out line)) table.AppendLine(line);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Araç] Kimlik kartlarından performans üretildi:\n" + table);
    }

    static string Inv(float v, string f = "0.##") { return v.ToString(f, CultureInfo.InvariantCulture); }
    static int Puan(int p) { return Mathf.Clamp(p, 1, 5) - 1; }
    static int Yuvarla50(float v) { return Mathf.Max(50, Mathf.RoundToInt(v / 50f) * 50); }

    static KimlikKarti ReadCard(CarDefinition def)
    {
        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(def)).Replace('\\', '/');
        string path = folder + "/" + def.id + "_kimlik.json";
        if (!File.Exists(path))
        {
            Debug.LogWarning("[Araç] " + def.id + ": kimlik kartı yok (" + path + "), dengeli coupe varsayılıyor");
            return new KimlikKarti { id = def.id, sinif = "coupe", cekis = "RWD", tahminiKutleKg = 1250f,
                karakter = new Karakter { sonHiz = 3, ivme = 3, yolTutus = 3, fren = 3, dayaniklilik = 3 } };
        }
        var card = JsonUtility.FromJson<KimlikKarti>(File.ReadAllText(path));
        if (card.karakter == null || card.karakter.sonHiz == 0)
        {
            Debug.LogWarning("[Araç] " + def.id + ": kartta 'karakter' yok, bütün puanlar 3 sayıldı (" + path + ")");
            card.karakter = new Karakter { sonHiz = 3, ivme = 3, yolTutus = 3, fren = 3, dayaniklilik = 3 };
        }
        return card;
    }

    /// <summary>Kartı okuyup tanıma yazar. Rapor satırı (markdown tablo) döner.</summary>
    public static bool ApplyCard(CarDefinition def, out string line)
    {
        line = null;
        var card = ReadCard(def);
        var k = card.karakter;
        string sinif = string.IsNullOrEmpty(card.sinif) ? "coupe" : card.sinif.ToLowerInvariant();
        SinifProfili p;
        if (!Siniflar.TryGetValue(sinif, out p))
        {
            Debug.LogWarning("[Araç] " + def.id + ": bilinmeyen sınıf '" + sinif + "', coupe profili kullanıldı");
            p = Siniflar["coupe"];
        }
        string cekis = string.IsNullOrEmpty(card.cekis) ? "RWD" : card.cekis.ToUpperInvariant();
        var g = def.geometry;
        float mass = card.tahminiKutleKg > 100f ? card.tahminiKutleKg : 1250f;

        var s = CarStats.Default;
        s.topSpeed = SonHizTablosu[Puan(k.sonHiz)];
        s.maxAcceleration = IvmeTablosu[Puan(k.ivme)];
        s.accelerationCurve = new AnimationCurve(p.egri);
        s.brakeDeceleration = FrenTablosu[Puan(k.fren)];
        s.maxCornerAccel = VirajTablosu[Puan(k.yolTutus)];
        s.tireFriction = SurtunmeTablosu[Puan(k.yolTutus)];
        s.frontGrip = OnTutusTablosu[Puan(k.yolTutus)];
        s.rearGrip = s.frontGrip * p.arkaOnOrani;
        s.durability = DayaniklilikTablosu[Puan(k.dayaniklilik)];

        // Kişilik
        s.steerSpeed = p.direksiyonHizi;
        s.highSpeedSteerAngle = p.yuksekHizAcisi;
        s.steerAssist = p.donusYardimi;
        s.handbrakeRearGrip = p.elFreniArkaTutus;
        s.driftYawBoost = p.driftSapma;
        s.gripForceHeight = p.tutusYuksekligi;
        s.downforce = p.bastirma;
        s.coastDeceleration = p.bosVites;
        // Çekiş: dört çeker el freninde daha az kayar, önden çeker arkası sakin ve drift'e isteksiz
        if (cekis == "AWD") s.handbrakeRearGrip = Mathf.Min(0.7f, s.handbrakeRearGrip + 0.05f);
        else if (cekis == "FWD") { s.rearGrip *= 1.08f; s.driftYawBoost -= 0.1f; }

        // Geometri ve kütle
        float wheelY = 0f;
        for (int i = 0; i < 4; i++) wheelY += g.wheelCenters[i].y / 4f;
        float wheelBase = g.wheelBase > 0.5f ? g.wheelBase : 2.6f;
        float track = (g.frontTrack + g.rearTrack) > 1f ? (g.frontTrack + g.rearTrack) / 2f : 1.66f;
        s.mass = mass;
        s.centerOfMassHeight = Mathf.Round((wheelY * 0.82f + p.agirlikMerkeziPayi) * 100f) / 100f;
        s.maxSteerAngle = Mathf.Round((34f * wheelBase / 2.6f + p.direksiyonEki) * 10f) / 10f;
        s.steerFalloffSpeed = Mathf.Round(35f * s.topSpeed / 40f * 10f) / 10f;
        s.springStrength = Mathf.Round(mass * 9.81f / 4f / s.suspensionDroop / 100f) * 100f;
        s.damperStrength = Mathf.Round(Mathf.Sqrt(s.springStrength * mass / 4f) / 10f) * 10f;
        s.antiRoll = Mathf.Round(9000f * (mass / 1200f) * (1.66f / track) * (1.66f / track) * p.yatmaSertligi / 100f) * 100f;

        Undo.RecordObject(def, "Kimlik kartından performans");
        def.baseStats = s;
        if (!string.IsNullOrEmpty(card.gorunenAd)) def.displayName = card.gorunenAd;
        def.upgrades = BuildUpgrades(k, mass, p.fiyatCarpani);
        int priced = PriceParts(def, p.fiyatCarpani);
        EditorUtility.SetDirty(def);

        line = "| " + def.id + " | " + sinif + "/" + cekis + " | " + k.sonHiz + "/" + k.ivme + "/" + k.yolTutus + "/" + k.fren + "/" + k.dayaniklilik
             + " | " + Inv(mass, "0") + " | " + Inv(s.topSpeed) + " | " + Inv(s.maxAcceleration) + " | " + Inv(s.brakeDeceleration)
             + " | " + Inv(s.maxCornerAccel) + " | " + Inv(s.tireFriction) + " | " + Inv(s.frontGrip) + "/" + Inv(s.rearGrip, "0.#")
             + " | " + Inv(s.durability, "0") + " | " + Inv(s.centerOfMassHeight) + " |";
        Debug.Log("[Araç] " + def.id + ": " + p.ozet + "; " + priced + " parça fiyatı yazıldı");
        return true;
    }

    /// <summary>Sıfır fiyatlı modifiye varyantlarına fiyat verir (elle verilen fiyatlar korunur). Yazılan sayıyı döner.</summary>
    static int PriceParts(CarDefinition def, float factor)
    {
        int count = 0;
        foreach (var slot in def.slots)
        {
            int basePrice;
            if (!ParcaTemelFiyat.TryGetValue(slot.id, out basePrice)) basePrice = 400;
            for (int v = 1; v < slot.variants.Count; v++)
            {
                if (slot.variants[v].price > 0) continue;
                slot.variants[v].price = Yuvarla50(basePrice * (0.6f + 0.4f * v) * factor);
                count++;
            }
        }
        return count;
    }

    // ------------------------------------------------------------------ Geliştirme ağacı

    static CarUpgradeLevel Seviye(string name, float price, float factor, params CarStatModifier[] mods)
    {
        var list = new List<CarStatModifier>();
        foreach (var m in mods)
            if (m.multiply ? Mathf.Abs(m.value - 1f) > 0.0001f : Mathf.Abs(m.value) > 0.0001f) list.Add(m);
        return new CarUpgradeLevel { displayName = name, price = Yuvarla50(price * factor), modifiers = list.ToArray() };
    }

    static CarStatModifier Yuzde(CarStat stat, float delta) { return Mul(stat, 1f + delta); }

    /// <summary>
    /// Arabaya göre geliştirme ağacı. Her seviye stoka göre toplam etkiyi yazar. Bir konudaki etki o konunun kart puanının
    /// tersine ölçeklenir (GelisimPayi: puan 1 → ×1.3, puan 5 → ×0.7): zayıf araba zayıf yanında daha çok gelişir,
    /// tam geliştirilmiş arabalar arasındaki fark stoktakinden küçük olur.
    /// </summary>
    static List<CarUpgradeCategory> BuildUpgrades(Karakter k, float mass, float f)
    {
        float hiz = GelisimPayi[Puan(k.sonHiz)], ivme = GelisimPayi[Puan(k.ivme)], tutus = GelisimPayi[Puan(k.yolTutus)];
        float fren = GelisimPayi[Puan(k.fren)], govde = GelisimPayi[Puan(k.dayaniklilik)];
        float hafif = Mathf.Clamp(mass / 1400f, 0.8f, 1.2f); // ağır araba hafifletmeden daha çok kazanır
        string[] ad = { "Sokak", "Spor", "Yarış" };
        var motor = new CarUpgradeLevel[3];
        var sanziman = new CarUpgradeLevel[3];
        var lastik = new CarUpgradeLevel[3];
        var frenler = new CarUpgradeLevel[3];
        var suspansiyon = new CarUpgradeLevel[3];
        var agirlik = new CarUpgradeLevel[3];
        var govdeSeviye = new CarUpgradeLevel[3];
        float[] motorHiz = { 0.03f, 0.06f, 0.10f }, motorIvme = { 0.05f, 0.10f, 0.16f };
        float[] sanzIvme = { 0.04f, 0.08f, 0.12f }, sanzHiz = { 0f, 0.02f, 0.04f }, sanzGeri = { 0.05f, 0.10f, 0.15f };
        float[] lastikTutus = { 0.04f, 0.08f, 0.12f }, lastikSurt = { 0.05f, 0.10f, 0.15f }, lastikViraj = { 0.5f, 1f, 1.5f };
        float[] frenOran = { 0.08f, 0.16f, 0.25f }, elFreni = { 0f, 0.1f, 0.2f };
        float[] susDemir = { 0.1f, 0.2f, 0.3f }, susAm = { -0.02f, -0.04f, -0.06f }, susYay = { 0f, 0.05f, 0.10f }, susYardim = { 0f, 0f, 0.5f };
        float[] agKutle = { -0.04f, -0.08f, -0.12f }, agIvme = { 0.02f, 0.04f, 0.06f }, agDay = { -3f, -6f, -10f };
        float[] gvDay = { 8f, 16f, 24f }, gvKutle = { 0.02f, 0.04f, 0.06f };
        string[] gvAd = { "Takviye", "Güçlendirilmiş", "Zırhlı" };
        int[][] fiyat =
        {
            new[] { 1000, 2500, 5000 }, new[] { 800, 2000, 4000 }, new[] { 700, 1800, 3500 }, new[] { 600, 1500, 3000 },
            new[] { 800, 2000, 4000 }, new[] { 900, 2200, 4500 }, new[] { 700, 1700, 3400 },
        };
        for (int i = 0; i < 3; i++)
        {
            motor[i] = Seviye(ad[i], fiyat[0][i], f, Yuzde(CarStat.SonHiz, motorHiz[i] * hiz), Yuzde(CarStat.Ivme, motorIvme[i] * ivme));
            sanziman[i] = Seviye(ad[i], fiyat[1][i], f, Yuzde(CarStat.Ivme, sanzIvme[i] * ivme), Yuzde(CarStat.SonHiz, sanzHiz[i] * hiz),
                Yuzde(CarStat.GeriIvme, sanzGeri[i]));
            lastik[i] = Seviye(ad[i], fiyat[2][i], f, Yuzde(CarStat.OnTutus, lastikTutus[i] * tutus), Yuzde(CarStat.ArkaTutus, lastikTutus[i] * tutus),
                Add(CarStat.LastikSurtunmesi, lastikSurt[i] * tutus), Add(CarStat.VirajIvmeSiniri, lastikViraj[i] * tutus));
            frenler[i] = Seviye(ad[i], fiyat[3][i], f, Yuzde(CarStat.Fren, frenOran[i] * fren), Yuzde(CarStat.ElFreniYavaslama, elFreni[i]));
            suspansiyon[i] = Seviye(ad[i], fiyat[4][i], f, Yuzde(CarStat.VirajDemiri, susDemir[i]), Add(CarStat.AgirlikMerkeziYuksekligi, susAm[i]),
                Yuzde(CarStat.Yay, susYay[i]), Yuzde(CarStat.Sonum, susYay[i]), Add(CarStat.DonusYardimi, susYardim[i]));
            // Yay kütleyle birlikte değişir, yoksa araba yüksekte/alçakta durur
            float dm = agKutle[i] * hafif;
            agirlik[i] = Seviye(ad[i], fiyat[5][i], f, Yuzde(CarStat.Kutle, dm), Yuzde(CarStat.Yay, dm), Yuzde(CarStat.Sonum, dm * 0.5f),
                Yuzde(CarStat.Ivme, agIvme[i]), Add(CarStat.Dayaniklilik, agDay[i]));
            govdeSeviye[i] = Seviye(gvAd[i], fiyat[6][i], f, Add(CarStat.Dayaniklilik, gvDay[i] * govde), Yuzde(CarStat.Kutle, gvKutle[i]),
                Yuzde(CarStat.Yay, gvKutle[i]), Yuzde(CarStat.Sonum, gvKutle[i] * 0.5f));
        }
        return new List<CarUpgradeCategory>
        {
            new CarUpgradeCategory { type = CarUpgradeType.Motor, displayName = "Motor", levels = motor },
            new CarUpgradeCategory { type = CarUpgradeType.Sanziman, displayName = "Şanzıman", levels = sanziman },
            new CarUpgradeCategory { type = CarUpgradeType.Lastik, displayName = "Lastik", levels = lastik },
            new CarUpgradeCategory { type = CarUpgradeType.Fren, displayName = "Fren", levels = frenler },
            new CarUpgradeCategory { type = CarUpgradeType.Suspansiyon, displayName = "Süspansiyon", levels = suspansiyon },
            new CarUpgradeCategory { type = CarUpgradeType.Agirlik, displayName = "Ağırlık azaltma", levels = agirlik },
            new CarUpgradeCategory { type = CarUpgradeType.Govde, displayName = "Gövde güçlendirme", levels = govdeSeviye },
        };
    }
}
