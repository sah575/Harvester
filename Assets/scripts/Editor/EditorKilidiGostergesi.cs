using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Harvester.EditorTools
{
    /// <summary>
    /// Ajan koordinasyonu (tools/koord.py) editör kilidini Unity'de gösterir.
    /// Pencere başlığına kilidin kimde olduğunu yazar; kilit doluyken play mode'a girilirse konsola uyarı basar.
    /// Engellemez: kullanıcı her zaman oynayabilir. Kurallar: docs/KOORDINASYON.md
    /// </summary>
    [InitializeOnLoad]
    static class EditorKilidiGostergesi
    {
        static readonly string KilitYolu = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".koordinasyon", "editor_kilidi.json"));

        [Serializable]
        class Kilit
        {
            public string ajan;
            public string @is;
            public double bitis;
        }

        static string sonMetin = "";
        static double sonKontrol;

        static EditorKilidiGostergesi()
        {
            EditorApplication.updateMainWindowTitle += BaslikGuncelle;
            EditorApplication.update += Yokla;
            EditorApplication.playModeStateChanged += PlayDegisti;
        }

        static Kilit Oku()
        {
            try
            {
                if (!File.Exists(KilitYolu)) return null;
                // "is" C#'ta anahtar kelime; JsonUtility alan adını olduğu gibi kullanır, @is "is" olarak eşleşir.
                var k = JsonUtility.FromJson<Kilit>(File.ReadAllText(KilitYolu));
                double simdi = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                return k != null && k.bitis > simdi ? k : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void Yokla()
        {
            if (EditorApplication.timeSinceStartup - sonKontrol < 2.0) return;
            sonKontrol = EditorApplication.timeSinceStartup;
            var k = Oku();
            string metin = k == null ? "" : "  [EDİTÖR: " + k.ajan + " — " + k.@is + "]";
            if (metin != sonMetin)
            {
                sonMetin = metin;
                EditorApplication.UpdateMainWindowTitle();
            }
        }

        static void BaslikGuncelle(ApplicationTitleDescriptor d)
        {
            d.title += sonMetin;
        }

        static void PlayDegisti(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            var k = Oku();
            if (k != null)
                Debug.LogWarning("[Koordinasyon] Editör kilidi '" + k.ajan + "' ajanında (" + k.@is + "). Kilit sende değilse play mode'dan çık ve bekle: python3 tools/koord.py bekle <ajan> \"<iş>\"");
        }
    }
}
