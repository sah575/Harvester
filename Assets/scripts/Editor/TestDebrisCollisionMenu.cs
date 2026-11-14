using Harvester.Race;
using UnityEditor;
using UnityEngine;

// Bu tercih yalnız test oturumları içindir; normal build'e varsayılan olarak taşınmaz.
public static class TestDebrisCollisionMenu
{
    const string Path="Harvester/Test/Enkaz çarpışmasını kapat (geçici)";
    [MenuItem(Path)]
    static void Toggle()
    {
        bool enabled=PlayerPrefs.GetInt(TestDebrisCollision.PreferenceKey,0)!=1;
        PlayerPrefs.SetInt(TestDebrisCollision.PreferenceKey,enabled?1:0);PlayerPrefs.Save();
        Debug.Log("[Test] Yeni oluşacak enkazlarda çarpışma "+(enabled?"KAPALI":"AÇIK")+". Karşılaştırma için yarışı yeniden başlat.");
    }
    [MenuItem(Path,true)]
    static bool Validate(){Menu.SetChecked(Path,TestDebrisCollision.Enabled);return true;}
}
