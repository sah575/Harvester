using System;
using System.Collections.Generic;
using Harvester.Race;
using UnityEngine;

namespace Harvester.UI
{
    // Her aracın son kaydedilen modifiyesi kimlikle saklanır; yarışa giden seçim yine CarLoadoutStore'dadır.
    public static class GarageLoadouts
    {
        [Serializable] class Item { public string key; public string value; }
        [Serializable] class Record { public List<Item> slots=new List<Item>();public List<Item> paints=new List<Item>();public List<Item> upgrades=new List<Item>(); }
        const string Prefix="Harvester.Garaj.Arac.";
        public static CarLoadout Load(CarCatalog catalog,int index)
        {
            var selected=CarLoadoutStore.Load(catalog);if(selected.IsValid&&selected.definition==index)return selected;
            var def=catalog.Get(index);if(def==null)return default;
            var result=def.CreateDefaultLoadout(index);Record data;
            try{data=JsonUtility.FromJson<Record>(PlayerPrefs.GetString(Prefix+def.id,""));}catch(Exception){return result;}
            if(data==null)return result;
            if(data.slots!=null)foreach(var item in data.slots)
            {int slot=def.SlotIndex(item.key);if(slot<0)continue;int variant=def.slots[slot].variants.FindIndex(v=>v.key==item.value);if(variant>=0)result.SetSlot(slot,variant);}
            if(data.paints!=null)foreach(var item in data.paints)
            {Color color;int p=def.PaintIndex(item.key);if(p>=0&&ColorUtility.TryParseHtmlString("#"+item.value,out color))result.SetPaint(p,color);}
            if(data.upgrades!=null)foreach(var item in data.upgrades)
            {CarUpgradeType type;int level;if(Enum.TryParse(item.key,out type)&&int.TryParse(item.value,out level))result.SetUpgrade(type,level);}
            return def.Sanitize(result);
        }
        public static void Remember(CarCatalog catalog,CarLoadout loadout)
        {
            var def=catalog.Get(loadout.definition);if(def==null)return;var data=new Record();loadout=def.Sanitize(loadout);
            for(int i=0;i<def.slots.Count&&i<CarLoadout.MaxSlots;i++)data.slots.Add(new Item{key=def.slots[i].id,value=def.slots[i].variants[loadout.GetSlot(i)].key});
            for(int i=0;i<def.paints.Count&&i<CarLoadout.MaxPaints;i++)data.paints.Add(new Item{key=def.paints[i].id,value=ColorUtility.ToHtmlStringRGBA(loadout.GetPaint(i))});
            foreach(var upgrade in def.upgrades)data.upgrades.Add(new Item{key=upgrade.type.ToString(),value=loadout.GetUpgrade(upgrade.type).ToString()});
            PlayerPrefs.SetString(Prefix+def.id,JsonUtility.ToJson(data));PlayerPrefs.Save();
        }
        public static void Save(CarCatalog catalog,CarLoadout loadout)
        {Remember(catalog,loadout);CarLoadoutStore.Save(catalog,loadout);}
    }
}
