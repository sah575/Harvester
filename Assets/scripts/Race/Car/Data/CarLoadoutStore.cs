using System;
using System.Collections.Generic;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Oyuncunun araba seçiminin yerel kaydı (PlayerPrefs'te JSON). Kayıt sıralara değil kimliklere göre tutulur
    /// (araba id, yuva id, varyant anahtarı, renk id), yani katalog ya da model değişince bozulmaz; tanınmayan
    /// parça stoka döner. Garaj arayüzü gelene kadar geliştirici için rastgele modifiye yolları da burada.
    /// </summary>
    public static class CarLoadoutStore
    {
        public const string PrefKey = "Harvester.ArabaSecimi";

        [Serializable]
        private class Entry
        {
            public string key;
            public string value;
        }

        [Serializable]
        private class SaveData
        {
            public string car;
            public List<Entry> upgrades = new List<Entry>();
            public List<Entry> slots = new List<Entry>();
            public List<Entry> paints = new List<Entry>();
        }

        public static bool HasSave => PlayerPrefs.HasKey(PrefKey);

        /// <summary>Kayıtlı seçimi yükler; kayıt yoksa ya da araba katalogda yoksa ilk arabanın varsayılanı.</summary>
        public static CarLoadout Load(CarCatalog catalog)
        {
            if (catalog == null || catalog.Count == 0) return default;
            string json = PlayerPrefs.GetString(PrefKey, "");
            if (string.IsNullOrEmpty(json)) return catalog.DefaultLoadout(0);
            SaveData data;
            try { data = JsonUtility.FromJson<SaveData>(json); }
            catch (Exception e)
            {
                Debug.LogWarning("[Araba] Kayıtlı seçim okunamadı, varsayılan kullanılıyor: " + e.Message);
                return catalog.DefaultLoadout(0);
            }
            int index = data != null ? catalog.IndexOf(data.car) : -1;
            if (index < 0) return catalog.DefaultLoadout(0);
            var def = catalog.Get(index);
            var l = def.CreateDefaultLoadout(index);

            foreach (var e in data.upgrades)
            {
                CarUpgradeType type;
                int level;
                if (Enum.TryParse(e.key, out type) && int.TryParse(e.value, out level)) l.SetUpgrade(type, level);
            }
            foreach (var e in data.slots)
            {
                int s = def.SlotIndex(e.key);
                if (s < 0 || s >= CarLoadout.MaxSlots) continue;
                var variants = def.slots[s].variants;
                for (int v = 0; v < variants.Count; v++)
                    if (variants[v].key == e.value) { l.SetSlot(s, v); break; }
            }
            foreach (var e in data.paints)
            {
                int p = def.PaintIndex(e.key);
                Color c;
                if (p >= 0 && p < CarLoadout.MaxPaints && ColorUtility.TryParseHtmlString("#" + e.value, out c)) l.SetPaint(p, c);
            }
            return def.Sanitize(l);
        }

        public static void Save(CarCatalog catalog, CarLoadout l)
        {
            var def = catalog != null ? catalog.Get(l.definition) : null;
            if (def == null) return;
            var data = new SaveData { car = def.id };
            for (int i = 0; i < CarLoadout.MaxUpgrades; i++)
                if (l.GetUpgrade(i) > 0)
                    data.upgrades.Add(new Entry { key = ((CarUpgradeType)i).ToString(), value = l.GetUpgrade(i).ToString() });
            for (int s = 0; s < def.slots.Count && s < CarLoadout.MaxSlots; s++)
            {
                int v = l.GetSlot(s);
                if (v > 0 && v < def.slots[s].variants.Count)
                    data.slots.Add(new Entry { key = def.slots[s].id, value = def.slots[s].variants[v].key });
            }
            for (int p = 0; p < def.paints.Count && p < CarLoadout.MaxPaints; p++)
                data.paints.Add(new Entry { key = def.paints[p].id, value = ColorUtility.ToHtmlStringRGBA(l.GetPaint(p)) });
            PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------- Rastgele (test)

        /// <summary>Parçaları ve renkleri rastgele seçer. Performans seviyelerine dokunmaz.</summary>
        public static CarLoadout RandomizeLooks(CarDefinition def, CarLoadout l, System.Random rng)
        {
            if (def == null) return l;
            for (int s = 0; s < def.slots.Count && s < CarLoadout.MaxSlots; s++)
                l.SetSlot(s, rng.Next(Mathf.Max(1, def.slots[s].variants.Count)));
            for (int p = 0; p < def.paints.Count && p < CarLoadout.MaxPaints; p++)
            {
                Color baseColor = def.paints[p].defaultColor;
                Color c = Color.HSVToRGB((float)rng.NextDouble(), 0.4f + 0.6f * (float)rng.NextDouble(), 0.25f + 0.75f * (float)rng.NextDouble());
                c.a = baseColor.a; // cam şeffaflığı korunur
                l.SetPaint(p, c);
            }
            return l;
        }

        /// <summary>Performans seviyelerini rastgele seçer.</summary>
        public static CarLoadout RandomizeUpgrades(CarDefinition def, CarLoadout l, System.Random rng)
        {
            if (def == null) return l;
            foreach (var cat in def.upgrades)
                l.SetUpgrade(cat.type, rng.Next(cat.levels.Length + 1));
            return l;
        }

        /// <summary>
        /// Geliştirici kısayolları (editör ve debug build, yerel araba): F6 rastgele görünüş, Ctrl+F6 rastgele performans,
        /// Shift+F6 varsayılana dön. Seçim kaydedilir ve arabaya uygulanır (ağda herkese gider).
        /// </summary>
        public static void HandleDebugKeys(RaceCar car)
        {
            if (!Input.GetKeyDown(KeyCode.F6) || car == null || car.Catalog == null) return;
            var catalog = car.Catalog;
            var current = car.Loadout;
            int index;
            var def = catalog.Resolve(current, out index);
            if (def == null) return;
            if (!current.IsValid) current = def.CreateDefaultLoadout(index);

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            var rng = new System.Random(Environment.TickCount);
            CarLoadout next;
            string what;
            if (shift) { next = def.CreateDefaultLoadout(index); what = "varsayılan"; }
            else if (ctrl) { next = RandomizeUpgrades(def, current, rng); what = "rastgele performans"; }
            else { next = RandomizeLooks(def, current, rng); what = "rastgele görünüş"; }

            Save(catalog, next);
            car.SetLoadout(next);
            Debug.Log("[Araba] Modifiye (" + what + "): " + Describe(def, next));
        }

        /// <summary>Okunur özet (log için).</summary>
        public static string Describe(CarDefinition def, CarLoadout l)
        {
            if (def == null) return l.ToString();
            var sb = new System.Text.StringBuilder(def.displayName);
            sb.Append(" | performans:");
            foreach (var cat in def.upgrades) sb.Append(' ').Append(cat.displayName).Append('=').Append(l.GetUpgrade(cat.type));
            sb.Append(" | parçalar:");
            for (int s = 0; s < def.slots.Count && s < CarLoadout.MaxSlots; s++)
            {
                int v = l.GetSlot(s);
                if (v > 0 && v < def.slots[s].variants.Count)
                    sb.Append(' ').Append(def.slots[s].id).Append('=').Append(def.slots[s].variants[v].key);
            }
            return sb.ToString();
        }
    }
}
