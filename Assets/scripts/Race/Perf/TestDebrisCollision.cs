using System;
using UnityEngine;

namespace Harvester.Race
{
    // GEÇİCİ TEST: enkaz kaynaklı tekrar kazaları kaldırıp daha uzun sürüş testi yapabilmek için.
    // Nihai enkaz/çarpışma tasarımı değildir. Kullanıcı kararı sonrası bu kanca kaldırılabilir.
    public static class TestDebrisCollision
    {
        public const string PreferenceKey="Harvester.Test.EnkazCarpismaYok";
        public const string Argument="-testEnkazCarpismaYok";
        static bool commandLineEnabled;
        public static int DisabledBodies { get; private set; }
        public static bool Enabled
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return commandLineEnabled||PlayerPrefs.GetInt(PreferenceKey,0)==1;
#else
                return false;
#endif
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            commandLineEnabled=Array.IndexOf(Environment.GetCommandLineArgs(),Argument)>=0;
            DisabledBodies=0;
            if(Enabled)Debug.Log("[Test] GEÇİCİ: elenen araç enkazlarının collider'ları kapalı. Nihai oyun kuralı değildir.");
        }
        public static void Configure(Rigidbody debris)
        {
            if(!Enabled||debris==null)return;
            foreach(var collider in debris.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            debris.detectCollisions=false;
            DisabledBodies++;
        }
    }
}
