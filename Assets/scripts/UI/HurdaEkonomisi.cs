using System;
using System.Collections.Generic;
using UnityEngine;

namespace Harvester.UI
{
    // Yerel ekonomi sınırı: sunucu doğrulaması geldiğinde işlemler bu sınıftan geçirilebilir.
    public static class HurdaEkonomisi
    {
        const string Key = "Harvester.Ekonomi.v1";
        [Serializable] class Kayit { public int hurda = 12000; public List<string> sahip = new List<string> { "araba/RacerCoupe_01" }; }
        static Kayit veri;
        static Kayit Veri
        {
            get
            {
                if (veri != null) return veri;
                try { veri = JsonUtility.FromJson<Kayit>(PlayerPrefs.GetString(Key, "")); } catch (Exception) { }
                if (veri == null) veri = new Kayit();
                if (veri.sahip == null) veri.sahip = new List<string>();
                if (!veri.sahip.Contains("araba/RacerCoupe_01")) veri.sahip.Add("araba/RacerCoupe_01");
                veri.hurda = Mathf.Max(0, veri.hurda);
                return veri;
            }
        }
        public static int Bakiye => Veri.hurda;
        public static bool Sahip(string kimlik) => Veri.sahip.Contains(kimlik);
        public static bool SatinAl(string kimlik, int fiyat)
        {
            if (string.IsNullOrEmpty(kimlik) || fiyat < 0) return false;
            if (Sahip(kimlik)) return true;
            if (Bakiye < fiyat) return false;
            Veri.hurda -= fiyat;
            Veri.sahip.Add(kimlik);
            Kaydet();
            return true;
        }
        public static void ParaEkle(int miktar, string sebep)
        {
            if (miktar <= 0 || string.IsNullOrWhiteSpace(sebep)) return;
            Veri.hurda = (int)Math.Min(int.MaxValue, (long)Bakiye + miktar);
            Kaydet();
            Debug.Log("Hurda eklendi: " + miktar + " / " + sebep);
        }
        static void Kaydet() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(Veri)); PlayerPrefs.Save(); }
    }
}
