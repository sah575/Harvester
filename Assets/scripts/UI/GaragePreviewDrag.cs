using UnityEngine.EventSystems;

namespace Harvester.UI
{
    // Yalnızca araç görüntüsü üzerinde başlayan sürükleme kamerayı döndürür; menü tıklamaları etkilenmez.
    public sealed class GaragePreviewDrag : UnityEngine.MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
    {
        public GaragePreview preview;
        public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)preview.Hold(true);}
        public void OnPointerUp(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)preview.Hold(false);}
        public void OnDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)preview.Drag(e.delta);}
        public void OnScroll(PointerEventData e){preview.Scroll(e.scrollDelta.y);}
        void OnApplicationFocus(bool focused){if(!focused&&preview!=null)preview.Hold(false);}
        void OnDisable(){if(preview!=null)preview.Hold(false);}
    }
}
