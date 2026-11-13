using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Harvester.UI
{
    public static class MenuSettings
    {
        const string Prefix="Harvester.Ayarlar.";
        static readonly Dictionary<UniversalRenderPipelineAsset,float> originalScales = new Dictionary<UniversalRenderPipelineAsset,float>();
        public static float MusicVolume => PlayerPrefs.GetFloat(Prefix+"Müzik",.7f);
        public static float EffectVolume => PlayerPrefs.GetFloat(Prefix+"Efekt",1);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplySaved()
        {
            Application.quitting -= RestoreScale; Application.quitting += RestoreScale;
            AudioListener.volume=PlayerPrefs.GetFloat(Prefix+"Ana",1);
            if(PlayerPrefs.HasKey(Prefix+"Kalite"))QualitySettings.SetQualityLevel(Mathf.Clamp(PlayerPrefs.GetInt(Prefix+"Kalite"),0,QualitySettings.names.Length-1));
            if(PlayerPrefs.HasKey(Prefix+"Genişlik"))Screen.SetResolution(PlayerPrefs.GetInt(Prefix+"Genişlik"),PlayerPrefs.GetInt(Prefix+"Yükseklik"),PlayerPrefs.GetInt(Prefix+"TamEkran",1)==1);
            ApplyScale(PlayerPrefs.GetFloat(Prefix+"Ölçek",1));
        }
        static void ApplyScale(float value)
        {
            var asset=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(asset==null)return;
            if(!originalScales.ContainsKey(asset))originalScales.Add(asset,asset.renderScale);
            asset.renderScale=value;
        }
        // Editörde paylaşılan URP asset'inin oturumluk değişikliği diske taşınmaz.
        public static void RestoreScale(){foreach(var pair in originalScales)if(pair.Key!=null)pair.Key.renderScale=pair.Value;originalScales.Clear();}
        public static void Build(Transform parent)
        {
            MenuWidgets.Text(parent,"SES",28,42);
            Volume(parent,"Ana",1,v=>AudioListener.volume=v);
            Volume(parent,"Müzik",.7f,null);Volume(parent,"Efekt",1,null);
            MenuWidgets.Text(parent,"GRAFİK",28,42);
            var resolutions=new List<Vector2Int>();
            foreach(var r in Screen.resolutions){var size=new Vector2Int(r.width,r.height);if(!resolutions.Contains(size))resolutions.Add(size);}
            var current=new Vector2Int(Screen.width,Screen.height);if(!resolutions.Contains(current))resolutions.Add(current);
            int selected=resolutions.IndexOf(current);
            Dropdown(parent,"Çözünürlük",resolutions.ConvertAll(r=>r.x+" × "+r.y),selected,i=>
            {
                var r=resolutions[i];PlayerPrefs.SetInt(Prefix+"Genişlik",r.x);PlayerPrefs.SetInt(Prefix+"Yükseklik",r.y);
                Screen.SetResolution(r.x,r.y,PlayerPrefs.GetInt(Prefix+"TamEkran",Screen.fullScreen?1:0)==1);PlayerPrefs.Save();
            });
            Dropdown(parent,"Tam ekran",new List<string>{"Kapalı","Açık"},PlayerPrefs.GetInt(Prefix+"TamEkran",Screen.fullScreen?1:0),i=>
            {PlayerPrefs.SetInt(Prefix+"TamEkran",i);PlayerPrefs.SetInt(Prefix+"Genişlik",Screen.width);PlayerPrefs.SetInt(Prefix+"Yükseklik",Screen.height);Screen.fullScreen=i==1;PlayerPrefs.Save();});
            var qualities=new List<string>();for(int i=0;i<QualitySettings.names.Length;i++)qualities.Add("Kalite "+(i+1));
            Dropdown(parent,"Kalite",qualities,QualitySettings.GetQualityLevel(),i=>{QualitySettings.SetQualityLevel(i);PlayerPrefs.SetInt(Prefix+"Kalite",i);ApplyScale(PlayerPrefs.GetFloat(Prefix+"Ölçek",1));PlayerPrefs.Save();});
            Range(parent,"Render ölçeği",PlayerPrefs.GetFloat(Prefix+"Ölçek",1),.5f,1.5f,v=>{PlayerPrefs.SetFloat(Prefix+"Ölçek",v);ApplyScale(v);PlayerPrefs.Save();});
            MenuWidgets.Text(parent,"KAMERA",28,42);
            Dropdown(parent,"Dikiz aynası",new List<string>{"Kapalı","Açık"},PlayerPrefs.GetInt("Harvester.RearViewMirror",1),i=>{Harvester.Race.RearViewMirror.UserEnabled=i==1;PlayerPrefs.Save();});
            Dropdown(parent,"Varsayılan kamera",new List<string>{"Yakın takip","Uzak takip","Kaput","Kokpit"},PlayerPrefs.GetInt("Harvester.CarCameraMode",0),i=>{PlayerPrefs.SetInt("Harvester.CarCameraMode",i);PlayerPrefs.Save();});
            MenuWidgets.Text(parent,"KONTROLLER",28,42);
            MenuWidgets.Text(parent,"W / S veya ↑ / ↓   Gaz / fren\nA / D veya ← / →   Direksiyon\nBoşluk   El freni\nR   Piste dön\nC   Kamera değiştir\nM   Dikiz aynası\nTuş değiştirme daha sonra eklenecek.",22,210);
        }
        static void Volume(Transform p,string name,float value,UnityEngine.Events.UnityAction<float> apply)
        {Range(p,name+" ses",PlayerPrefs.GetFloat(Prefix+name,value),0,1,v=>{PlayerPrefs.SetFloat(Prefix+name,v);if(apply!=null)apply(v);PlayerPrefs.Save();});}
        static void Range(Transform p,string name,float value,float min,float max,UnityEngine.Events.UnityAction<float> change)
        {
            var label=MenuWidgets.Text(p,name+" / "+value.ToString("0.00"),22,32);
            var go=DefaultControls.CreateSlider(new DefaultControls.Resources());go.transform.SetParent(p,false);MenuWidgets.Height(go.transform,34);
            foreach(var image in go.GetComponentsInChildren<Image>(true))image.color=image.gameObject.name=="Handle"?MenuWidgets.Ink:MenuWidgets.Accent;
            var slider=go.GetComponent<Slider>();slider.minValue=min;slider.maxValue=max;slider.value=value;
            slider.onValueChanged.AddListener(v=>{label.text=name+" / "+v.ToString("0.00");change(v);});
        }
        static void Dropdown(Transform p,string label,List<string> options,int selected,UnityEngine.Events.UnityAction<int> change)
        {
            MenuWidgets.Text(p,label,22,30);
            var go=TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());go.transform.SetParent(p,false);MenuWidgets.Height(go.transform,44);
            foreach(var image in go.GetComponentsInChildren<Image>(true))image.color=new Color(.18f,.22f,.19f);
            foreach(var text in go.GetComponentsInChildren<TMP_Text>(true)){text.color=MenuWidgets.Ink;text.fontSize=22;}
            MenuWidgets.StyleFont(go);
            var dropdown=go.GetComponent<TMP_Dropdown>();dropdown.ClearOptions();dropdown.AddOptions(options);dropdown.value=Mathf.Clamp(selected,0,options.Count-1);
            dropdown.onValueChanged.AddListener(change);
        }
    }
}
