using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Harvester.UI
{
    // Aynı üretim yordamları editör kurucusunda ve değişken katalog içeriğinde kullanılır.
    public static class MenuWidgets
    {
        public static TMP_FontAsset Font;
        static string Turkce(string text) { return text.Replace("Time Attack","Zamana karşı").Replace("Rollbar","Takla kafesi").Replace("Amber","Kehribar"); }
        public static void StyleFont(GameObject go) { if(Font!=null)foreach(var text in go.GetComponentsInChildren<TMP_Text>(true))text.font=Font; }
        public static readonly Color Ink = new Color(.79f,.83f,.74f);
        public static readonly Color Accent = new Color(.75f,.42f,.16f);
        public static RectTransform Box(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false); go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }
        public static void Fill(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=Vector2.zero; rect.offsetMax=Vector2.zero; }
        public static RectTransform Column(Transform parent, string name)
        {
            var r=Box(parent,name,new Color(.045f,.055f,.052f,.98f));
            var l=r.gameObject.AddComponent<VerticalLayoutGroup>();
            l.padding=new RectOffset(24,24,20,20); l.spacing=10; l.childControlWidth=true;
            l.childControlHeight=true; l.childForceExpandWidth=true; l.childForceExpandHeight=false;
            return r;
        }
        public static void Height(Transform t,float h)
        { var e=t.GetComponent<LayoutElement>()??t.gameObject.AddComponent<LayoutElement>(); e.minHeight=h; e.preferredHeight=h; }
        public static TMP_Text Text(Transform p,string text,float size=24,float height=40)
        {
            var go=new GameObject("Metin",typeof(RectTransform),typeof(TextMeshProUGUI)); go.transform.SetParent(p,false);
            var t=go.GetComponent<TextMeshProUGUI>(); if(Font!=null)t.font=Font; t.text=Turkce(text); t.fontSize=size; t.color=Ink;
            t.raycastTarget=false; t.richText=true; t.overflowMode=TextOverflowModes.Ellipsis; Height(t.transform,height); return t;
        }
        public static Button Button(Transform p,string text,UnityAction action=null)
        {
            var r=Box(p,text,new Color(.14f,.18f,.16f)); Height(r,48);
            var b=r.gameObject.AddComponent<Button>(); var c=b.colors; c.highlightedColor=new Color(.75f,.7f,.5f); c.pressedColor=Accent; b.colors=c;
            var t=Text(r,text,22); Fill((RectTransform)t.transform,new Vector2(.04f,0),new Vector2(.96f,1)); t.alignment=TextAlignmentOptions.MidlineLeft;
            if(action!=null)b.onClick.AddListener(action); return b;
        }
        public static TMP_InputField Input(Transform p,string value,string hint)
        {
            var go=TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources()); go.transform.SetParent(p,false);
            go.GetComponent<Image>().color=new Color(.15f,.18f,.17f); Height(go.transform,44);
            StyleFont(go);
            var f=go.GetComponent<TMP_InputField>(); f.textComponent.color=Ink; f.textComponent.fontSize=22;
            ((TMP_Text)f.placeholder).text=hint; ((TMP_Text)f.placeholder).color=new Color(.65f,.7f,.63f,.8f); f.text=value; return f;
        }
        public static RectTransform Scroll(Transform parent,string name)
        {
            var outer=Box(parent,name,new Color(.035f,.044f,.041f));
            var viewport=Box(outer,"Görünüm",Color.white); Fill(viewport,Vector2.zero,Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>(); viewport.GetComponent<Image>().enabled=false;
            var content=Column(viewport,"İçerik"); content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one; content.pivot=new Vector2(.5f,1); content.sizeDelta=Vector2.zero;
            var fit=content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll=outer.gameObject.AddComponent<ScrollRect>(); scroll.viewport=viewport; scroll.content=content; scroll.horizontal=false; scroll.movementType=ScrollRect.MovementType.Clamped;
            var scrollbarObject=DefaultControls.CreateScrollbar(new DefaultControls.Resources());scrollbarObject.transform.SetParent(outer,false);
            var scrollbar=scrollbarObject.GetComponent<Scrollbar>();scrollbar.direction=Scrollbar.Direction.BottomToTop;
            Fill((RectTransform)scrollbarObject.transform,new Vector2(.98f,0),Vector2.one);
            foreach(var image in scrollbarObject.GetComponentsInChildren<Image>())image.color=image.gameObject.name=="Handle"?Accent:new Color(.08f,.1f,.08f);
            scroll.verticalScrollbar=scrollbar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }
        public static void Options(Transform p,System.Collections.Generic.List<string> values,int selected,UnityAction<int> changed)
        {
            var go=TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());go.transform.SetParent(p,false);Height(go.transform,48);StyleFont(go);
            foreach(var image in go.GetComponentsInChildren<Image>(true))image.color=new Color(.18f,.22f,.19f);
            foreach(var text in go.GetComponentsInChildren<TMP_Text>(true)){text.color=Ink;text.fontSize=22;}
            var dropdown=go.GetComponent<TMP_Dropdown>();dropdown.ClearOptions();dropdown.AddOptions(values.ConvertAll(Turkce));dropdown.value=Mathf.Clamp(selected,0,values.Count-1);dropdown.onValueChanged.AddListener(changed);
        }
        public static void Clear(Transform p)
        { for(int i=p.childCount-1;i>=0;i--) { var go=p.GetChild(i).gameObject; go.SetActive(false); Object.Destroy(go); } }
    }
}
