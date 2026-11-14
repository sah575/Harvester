using System;
using System.Reflection;
using Harvester.Race;
using Harvester.UI;
using UnityEditor;
using UnityEngine;

// Testler oyuncunun gerçek tercihlerini geri yükler; ayrı test paketi gerektirmez.
public static class LobbyUIValidation
{
    [MenuItem("Harvester/Arayüz/Kayıt ve Ekonomi Testi")]
    public static void Test()
    {
        const string economyKey="Harvester.Ekonomi.v1";
        bool hadEconomy=PlayerPrefs.HasKey(economyKey), hadLoadout=PlayerPrefs.HasKey(CarLoadoutStore.PrefKey);
        string economy=PlayerPrefs.GetString(economyKey), loadout=PlayerPrefs.GetString(CarLoadoutStore.PrefKey);
        var cache=typeof(HurdaEkonomisi).GetField("veri",BindingFlags.NonPublic|BindingFlags.Static);
        object previousCache=cache.GetValue(null);
        var catalog=ScriptableObject.CreateInstance<CarCatalog>();
        try
        {
            PlayerPrefs.DeleteKey(economyKey);cache.SetValue(null,null);
            Check(HurdaEkonomisi.Bakiye==12000,"Başlangıç bakiyesi");
            Check(HurdaEkonomisi.Sahip("araba/RacerCoupe_01"),"Başlangıç aracı");
            Check(HurdaEkonomisi.SatinAl("deneme/parça",1000)&&HurdaEkonomisi.Bakiye==11000,"Satın alma");
            Check(HurdaEkonomisi.SatinAl("deneme/parça",1000)&&HurdaEkonomisi.Bakiye==11000,"Tekrar satın almada ücret kesilmemesi");
            Check(!HurdaEkonomisi.SatinAl("deneme/pahalı",11001)&&HurdaEkonomisi.Bakiye==11000,"Yetersiz bakiye");
            Check(!HurdaEkonomisi.SatinAl("deneme/eksi",-1),"Negatif fiyat");
            HurdaEkonomisi.ParaEkle(400,"Arayüz testi");
            Check(HurdaEkonomisi.Bakiye==11400,"Ödül API'si");
            cache.SetValue(null,null);Check(HurdaEkonomisi.Bakiye==11400&&HurdaEkonomisi.Sahip("deneme/parça"),"Ekonomi yeniden yükleme");
            HurdaEkonomisi.ParaEkle(int.MaxValue,"Taşma testi");Check(HurdaEkonomisi.Bakiye==int.MaxValue,"Tamsayı taşması");
            var registry=AssetDatabase.LoadAssetAtPath<MenuCarRegistry>("Assets/UI/MenuCarRegistry.asset");
            Check(registry!=null&&registry.definitions.Count>0,"Araç envanteri");catalog.cars.AddRange(registry.definitions);
            var def=catalog.Get(0);var selection=def.CreateDefaultLoadout(0);
            if(def.slots.Count>0&&def.slots[0].variants.Count>1)selection.SetSlot(0,1);
            if(def.upgrades.Count>0&&def.upgrades[0].levels.Length>0)selection.SetUpgrade(def.upgrades[0].type,1);
            if(def.paints.Count>0)selection.SetPaint(0,new Color32(44,88,122,255));
            CarLoadoutStore.Save(catalog,selection);Check(CarLoadoutStore.Load(catalog).Equals(selection),"Araç seçimi kayıt döngüsü");
            Debug.Log("Arayüz testleri geçti: başlangıç, satın alma, tekrar satın alma, yetersiz bakiye, negatif fiyat, ödül, yeniden yükleme, taşma, araç kaydı.");
        }
        finally
        {
            if(hadEconomy)PlayerPrefs.SetString(economyKey,economy);else PlayerPrefs.DeleteKey(economyKey);
            if(hadLoadout)PlayerPrefs.SetString(CarLoadoutStore.PrefKey,loadout);else PlayerPrefs.DeleteKey(CarLoadoutStore.PrefKey);
            cache.SetValue(null,previousCache);PlayerPrefs.Save();UnityEngine.Object.DestroyImmediate(catalog);
        }
    }
    static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException("Arayüz testi başarısız: "+name);}
}
