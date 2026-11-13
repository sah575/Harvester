using System;
using System.Collections.Generic;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>Performans geliştirme kategorileri. Değer CarLoadout'taki bayt sırasıdır: sona ekle, araya ekleme (en fazla 8).</summary>
    public enum CarUpgradeType : byte
    {
        Motor = 0,
        Sanziman = 1,
        Lastik = 2,
        Fren = 3,
        Suspansiyon = 4,
        Agirlik = 5,
        Nitro = 6,
    }

    /// <summary>Bir parça yuvasının türü: modelde nesnelerin nasıl adlandırıldığı ve nasıl seçildiği.</summary>
    public enum CarSlotKind
    {
        /// <summary>mod_&lt;yuva&gt;_&lt;sıra&gt;_&lt;ad&gt;: her varyant bir ya da birkaç nesne, sadece seçilen açık.</summary>
        Parca,
        /// <summary>wheel-*_rim_&lt;sıra&gt;_&lt;ad&gt;: dört tekerlekte aynı jant.</summary>
        Jant,
        /// <summary>decal_&lt;sıra&gt;_&lt;ad&gt;: 0 = çıkartma yok, diğerleri tek tek.</summary>
        Cikartma,
        /// <summary>Aç/kapa (ör. lastik yazısı): 0 = kapalı, 1 = açık.</summary>
        AcKapa,
    }

    [Serializable]
    public class CarVariant
    {
        [Tooltip("Modeldeki ad parçası (ör. 2_cup). Kayıtlar bunu kullanır; değiştirme.")]
        public string key;
        [Tooltip("Oyuncuya görünen ad")]
        public string displayName;
        [Tooltip("Fiyat (ekonomi ileride)")]
        public int price;
        [Tooltip("Bu varyant seçilince açılan nesnelerin adları (modelde)")]
        public string[] objectNames = new string[0];
    }

    [Serializable]
    public class CarModSlot
    {
        [Tooltip("Kalıcı kimlik (ör. bumper_front). Kayıtlar bunu kullanır; değiştirme.")]
        public string id;
        public string displayName;
        public CarSlotKind kind;
        [Tooltip("0. varyant stok/varsayılan")]
        public List<CarVariant> variants = new List<CarVariant>();
    }

    [Serializable]
    public class CarPaintSlot
    {
        [Tooltip("Kalıcı kimlik (malzeme adı, ör. Paint_Body)")]
        public string id;
        public string displayName;
        [Tooltip("Modelde bu malzemeyi kullanan her yüzey bu renkle boyanır (MaterialPropertyBlock, _BaseColor)")]
        public Material material;
        public Color defaultColor = Color.white;
        [Tooltip("0'dan büyükse _EmissionColor = renk × bu (neon gibi parlayan yüzeyler)")]
        public float emissionIntensity;
    }

    [Serializable]
    public class CarStatModifier
    {
        public CarStat stat;
        [Tooltip("true: değer çarpılır (1.05 = %5 artış). false: değer eklenir.")]
        public bool multiply = true;
        public float value = 1f;
    }

    [Serializable]
    public class CarUpgradeLevel
    {
        public string displayName;
        public int price;
        [Tooltip("Bu seviyenin TOPLAM etkisi (önceki seviyelerin üstüne eklenmez; 3. seviye stoka göre farkı yazar)")]
        public CarStatModifier[] modifiers = new CarStatModifier[0];
    }

    [Serializable]
    public class CarUpgradeCategory
    {
        public CarUpgradeType type;
        public string displayName;
        [Tooltip("1. seviyeden başlar; 0. seviye stok")]
        public CarUpgradeLevel[] levels = new CarUpgradeLevel[0];
    }

    /// <summary>Modelden ölçülen geometri (CarBuilder doldurur). Kök uzayında, ön +Z.</summary>
    [Serializable]
    public class CarGeometry
    {
        [Tooltip("Tekerlek nesneleri, sıra: ön sol, ön sağ, arka sol, arka sağ")]
        public string[] wheelNames = { "wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right" };
        [Tooltip("Kökte ayrı duran kaliperler (varsa). Tekerleğin çocuğu olan kaliperler CarDefinition.caliperSuffix ile bulunur.")]
        public string[] caliperNames = { "caliper-front-left", "caliper-front-right", "caliper-back-left", "caliper-back-right" };
        public Vector3[] wheelCenters = new Vector3[4];
        public float[] wheelRadii = { 0.35f, 0.35f, 0.35f, 0.35f };
        public float wheelBase = 2.5f;
        public float frontTrack = 1.6f;
        public float rearTrack = 1.6f;
        [Tooltip("Ağırlık merkezinin Z'si (dingillerin ortası). Yüksekliği CarStats.centerOfMassHeight'ta.")]
        public float centerOfMassZ;
        [Tooltip("Gövdenin BoxCollider'ı. Tekerlekler kutunun içinde kalmalı (CarContactSolver).")]
        public Vector3 colliderCenter = new Vector3(0f, 0.8f, 0f);
        public Vector3 colliderSize = new Vector3(2f, 1f, 4.4f);
        [Tooltip("Gövde boyutu (bilgi): genişlik × yükseklik × uzunluk")]
        public Vector3 bodySize;
    }

    /// <summary>
    /// Bir arabanın bütün verisi: kimlik, model, temel fizik değerleri, modelden ölçülen geometri, modifiye yuvaları,
    /// boyanabilir malzemeler ve performans geliştirme ağacı. Araba başına bir asset (Assets/Cars/&lt;ad&gt;/&lt;ad&gt;_Tanim.asset).
    /// Ölçüm ve yuva çıkarımı CarBuilder'da (Harvester/Araç menüsü ya da bu asset'in inspector'ındaki düğme).
    /// </summary>
    [CreateAssetMenu(menuName = "Harvester/Araba Tanımı", fileName = "Araba_Tanim")]
    public class CarDefinition : ScriptableObject
    {
        [Header("Kimlik")]
        [Tooltip("Kalıcı kimlik (ör. RacerCoupe_01). Kayıtlar bunu kullanır; değiştirme.")]
        public string id;
        public string displayName;
        [Tooltip("Fiyat (ekonomi ileride)")]
        public int price;

        [Header("Görsel")]
        [Tooltip("Arabanın model prefab'ı. Yerelde arabanın çocuğu olarak açılır (ağ objesi değil).")]
        public GameObject modelPrefab;
        [Tooltip("Sürücü göz hizası (kokpit kamerası)")]
        public string cockpitAnchor = "camera_cockpit";
        public string[] headlightAnchors = { "light_head_L", "light_head_R" };
        public string[] taillightAnchors = { "light_tail_L", "light_tail_R" };
        [Tooltip("Frende parlayan malzeme (stop lambası)")]
        public Material brakeLightMaterial;
        [Tooltip("Tekerlek altında olup dönmeyen nesnelerin ad soneki (kaliper)")]
        public string caliperSuffix = "_caliper";
        [Tooltip("Parçalanınca ayrı uçan panellerin ad önekleri (modelin doğrudan çocukları). Gerisi tek kasa parçası olur.")]
        public string[] shatterPrefixes = { "door_", "fender_", "mod_hood_", "mod_bumper_", "roof_cabin", "engine_decklid", "mod_spoiler_" };

        [Header("Fizik")]
        public CarStats baseStats = CarStats.Default;
        public CarGeometry geometry = new CarGeometry();

        [Header("Modifiye")]
        public List<CarPaintSlot> paints = new List<CarPaintSlot>();
        public List<CarModSlot> slots = new List<CarModSlot>();

        [Header("Performans geliştirme")]
        public List<CarUpgradeCategory> upgrades = new List<CarUpgradeCategory>();

        public int SlotIndex(string slotId)
        {
            for (int i = 0; i < slots.Count; i++) if (slots[i].id == slotId) return i;
            return -1;
        }

        public int PaintIndex(string paintId)
        {
            for (int i = 0; i < paints.Count; i++) if (paints[i].id == paintId) return i;
            return -1;
        }

        public CarUpgradeCategory GetUpgrade(CarUpgradeType type)
        {
            for (int i = 0; i < upgrades.Count; i++) if (upgrades[i].type == type) return upgrades[i];
            return null;
        }

        /// <summary>Tanımın varsayılan seçimi: stok parçalar, stok performans, varsayılan renkler.</summary>
        public CarLoadout CreateDefaultLoadout(int catalogIndex)
        {
            var l = CarLoadout.Create(catalogIndex);
            for (int i = 0; i < paints.Count && i < CarLoadout.MaxPaints; i++) l.SetPaint(i, paints[i].defaultColor);
            return l;
        }

        /// <summary>
        /// Seçimdeki geliştirme seviyelerini temel değerlere uygular. Her seviye kendi toplam etkisini yazar.
        /// Önce bütün çarpanlar, sonra bütün eklemeler uygulanır (kategori sırası sonucu değiştirmez). Çöp üretmez
        /// (eğri temel değerlerle paylaşılır, değiştirilmemeli).
        /// </summary>
        public CarStats ComputeStats(CarLoadout loadout)
        {
            CarStats s = baseStats;
            if (s.accelerationCurve == null || s.accelerationCurve.length == 0) s.accelerationCurve = CarStats.DefaultCurve();
            for (int pass = 0; pass < 2; pass++)
            {
                bool multiplyPass = pass == 0;
                for (int c = 0; c < upgrades.Count; c++)
                {
                    var cat = upgrades[c];
                    int level = loadout.GetUpgrade(cat.type);
                    if (level <= 0 || cat.levels == null) continue;
                    if (level > cat.levels.Length) level = cat.levels.Length;
                    var mods = cat.levels[level - 1].modifiers;
                    if (mods == null) continue;
                    for (int m = 0; m < mods.Length; m++)
                    {
                        var mod = mods[m];
                        if (mod == null || mod.multiply != multiplyPass) continue;
                        float v = s.Get(mod.stat);
                        s.Set(mod.stat, multiplyPass ? v * mod.value : v + mod.value);
                    }
                }
            }
            return s;
        }

        /// <summary>Seçimi bu tanıma göre geçerli aralığa çeker (eski kayıt, bozuk paket).</summary>
        public CarLoadout Sanitize(CarLoadout l)
        {
            for (int i = 0; i < CarLoadout.MaxUpgrades; i++)
            {
                var cat = GetUpgrade((CarUpgradeType)i);
                int max = cat != null && cat.levels != null ? cat.levels.Length : 0;
                if (l.GetUpgrade(i) > max) l.SetUpgrade(i, max);
            }
            for (int i = 0; i < CarLoadout.MaxSlots; i++)
            {
                int count = i < slots.Count ? slots[i].variants.Count : 0;
                if (l.GetSlot(i) >= Mathf.Max(1, count)) l.SetSlot(i, 0);
            }
            for (int i = 0; i < paints.Count && i < CarLoadout.MaxPaints; i++)
                if (l.GetPaint(i).a == 0) l.SetPaint(i, paints[i].defaultColor);
            return l;
        }
    }
}
