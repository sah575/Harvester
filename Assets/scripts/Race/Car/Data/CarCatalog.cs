using System.Collections.Generic;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Oyundaki bütün arabaların listesi. Listedeki sıra ağ indeksidir (CarLoadout.definition): bütün oyuncuların
    /// build'inde aynı olmalı, yeni araba sona eklenir. Lobide araba seçimi de buradan okunur.
    /// </summary>
    [CreateAssetMenu(menuName = "Harvester/Araba Kataloğu", fileName = "ArabaKatalogu")]
    public class CarCatalog : ScriptableObject
    {
        [Tooltip("Sıra ağ indeksidir; araya ekleme, sona ekle (en fazla 256)")]
        public List<CarDefinition> cars = new List<CarDefinition>();

        public int Count => cars.Count;

        public CarDefinition Get(int index)
        {
            return index >= 0 && index < cars.Count ? cars[index] : null;
        }

        public int IndexOf(CarDefinition definition)
        {
            return definition == null ? -1 : cars.IndexOf(definition);
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < cars.Count; i++)
                if (cars[i] != null && cars[i].id == id) return i;
            return -1;
        }

        /// <summary>Seçimin tanımı; geçersizse ilk araba.</summary>
        public CarDefinition Resolve(CarLoadout loadout, out int index)
        {
            index = loadout.IsValid ? loadout.definition : 0;
            var def = Get(index);
            if (def == null)
            {
                index = 0;
                def = Get(0);
            }
            return def;
        }

        public CarLoadout DefaultLoadout(int index = 0)
        {
            var def = Get(index);
            return def != null ? def.CreateDefaultLoadout(index) : CarLoadout.Create(index);
        }
    }
}
