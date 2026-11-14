using UnityEngine;

namespace Harvester.Race
{
    // Kalite tercihi oyuncuya aittir; otomatik seçim yalnız ilk açılışta yapılır.
    public sealed class RuntimeQuality : MonoBehaviour
    {
        const string Key="Harvester.Ayarlar.Kalite.v2";
        const string LegacyKey="Harvester.Ayarlar.Kalite";
        static int configured=-1;
        static RearViewMirror configuredMirror;
        public static int Suggest(string gpu,int memoryMb)
        {
            string name=(gpu??"").ToLowerInvariant();
            bool integrated=name.Contains("iris")||name.Contains("uhd")||name.Contains("intel(r) hd")||name.Contains("intel(r) graphics")||name.Contains("radeon(tm) graphics");
            if(integrated||memoryMb<4096)return 0;
            return memoryMb<6144?1:2;
        }
        public static void Initialize()
        {
            int level;
            if(PlayerPrefs.HasKey(Key))level=PlayerPrefs.GetInt(Key);
            else if(PlayerPrefs.HasKey(LegacyKey))
            {int old=PlayerPrefs.GetInt(LegacyKey);level=old<=1?0:old<=3?1:2;}
            else
            {level=Suggest(SystemInfo.graphicsDeviceName,SystemInfo.graphicsMemorySize);Debug.Log("[Quality] İlk açılış otomatik seçim: "+level+" GPU="+SystemInfo.graphicsDeviceName);}
            SetUserLevel(level);
        }
        public static void SetUserLevel(int level)
        {
            level=Mathf.Clamp(level,0,QualitySettings.names.Length-1);QualitySettings.SetQualityLevel(level,true);
            PlayerPrefs.SetInt(Key,level);PlayerPrefs.Save();ApplyBudget();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {var go=new GameObject("Kalite bütçesi");DontDestroyOnLoad(go);go.AddComponent<RuntimeQuality>();configured=-1;configuredMirror=null;}
        static void ApplyBudget()
        {
            int level=QualitySettings.GetQualityLevel();Application.targetFrameRate=level==0?60:-1;QualitySettings.vSyncCount=0;
            RearViewMirror.QualityAllowed=true;
            if(RearViewMirror.Instance!=null)RearViewMirror.Instance.frameInterval=level==0?4:2;
            configured=level;configuredMirror=RearViewMirror.Instance;
        }
        void LateUpdate(){if(configured!=QualitySettings.GetQualityLevel()||configuredMirror!=RearViewMirror.Instance)ApplyBudget();}
    }
}
