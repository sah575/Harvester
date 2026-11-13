using Harvester.UI;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Yarışa giden seçimin sahiplik denetimi (yerel Hurda kaydı, HurdaEkonomisi). Sahip olunmayan araba seçilmişse
    /// katalogdaki ilk arabanın (başlangıç arabası, herkesin sahip olduğu) varsayılanına; sahip olunmayan parça stoğa;
    /// sahip olunmayan geliştirme seviyesi sahip olunan en yüksek alt seviyeye düşer. Renkler serbesttir.
    /// Anahtarlar garajınkiyle aynı: araba/&lt;id&gt;, parca/&lt;id&gt;/&lt;yuva&gt;/&lt;varyant&gt;, gelistirme/&lt;id&gt;/&lt;tür&gt;/&lt;seviye&gt;.
    /// Sunucu doğrulaması yok (ekonomi yerel); bu sadece eski/elle düzenlenmiş kayıtların yarışa sızmasını önler.
    /// </summary>
    public static class CarOwnership
    {
        public static string CarKey(CarDefinition def) { return "araba/" + def.id; }
        public static string PartKey(CarDefinition def, int slot, int variant)
        {
            return "parca/" + def.id + "/" + def.slots[slot].id + "/" + def.slots[slot].variants[variant].key;
        }
        public static string UpgradeKey(CarDefinition def, CarUpgradeType type, int level)
        {
            return "gelistirme/" + def.id + "/" + type + "/" + level;
        }

        /// <summary>Seçimi sahipliğe göre düzeltir. Neyin düştüğünü (log için) <paramref name="changes"/>'e yazar.</summary>
        public static CarLoadout Enforce(CarCatalog catalog, CarLoadout l, out string changes)
        {
            changes = "";
            if (catalog == null || catalog.Count == 0) return l;
            int index;
            var def = catalog.Resolve(l, out index);
            if (def == null) return l;
            if (!l.IsValid || l.definition != index) l = def.CreateDefaultLoadout(index);
            if (index != 0 && !HurdaEkonomisi.Sahip(CarKey(def)))
            {
                changes = "araba sahip olunmamış (" + def.id + ")";
                return catalog.DefaultLoadout(0);
            }
            l = def.Sanitize(l);
            for (int s = 0; s < def.slots.Count && s < CarLoadout.MaxSlots; s++)
            {
                int v = l.GetSlot(s);
                if (v <= 0 || v >= def.slots[s].variants.Count || HurdaEkonomisi.Sahip(PartKey(def, s, v))) continue;
                l.SetSlot(s, 0);
                changes += " parça " + def.slots[s].id + "/" + def.slots[s].variants[v].key;
            }
            foreach (var cat in def.upgrades)
            {
                int level = l.GetUpgrade(cat.type), owned = level;
                while (owned > 0 && !HurdaEkonomisi.Sahip(UpgradeKey(def, cat.type, owned))) owned--;
                if (owned == level) continue;
                l.SetUpgrade(cat.type, owned);
                changes += " geliştirme " + cat.type + " " + level + "→" + owned;
            }
            return l;
        }

        /// <summary>Kayıtlı seçimi yükler ve sahipliğe göre düzeltir (yarışta spawn için).</summary>
        public static CarLoadout LoadForRace(CarCatalog catalog)
        {
            string changes;
            var l = Enforce(catalog, CarLoadoutStore.Load(catalog), out changes);
            if (changes.Length > 0) Debug.LogWarning("[Araba] Kayıtlı seçimde sahip olunmayanlar varsayılana döndü:" + changes);
            return l;
        }
    }
}
