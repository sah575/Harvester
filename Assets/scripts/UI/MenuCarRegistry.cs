using System.Collections.Generic;
using Harvester.Race;
using UnityEngine;

namespace Harvester.UI
{
    // Editör, yeni tanımlar içeri alındığında bu arayüz envanterini günceller; ağ katalog sırasını değiştirmez.
    public sealed class MenuCarRegistry : ScriptableObject
    {
        public CarCatalog catalog;
        public List<CarDefinition> definitions = new List<CarDefinition>();
    }
}
