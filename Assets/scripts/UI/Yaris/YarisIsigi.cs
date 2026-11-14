using UnityEngine;
using UnityEngine.UI;

namespace Harvester.UI.Yaris
{
    public sealed class YarisIsigi : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var rect=rectTransform.rect;Vector2 center=rect.center;
            float radius=Mathf.Min(rect.width,rect.height)*.47f;
            vh.AddVert(center,color,Vector2.zero);
            const int segments=40;
            for(int i=0;i<=segments;i++)
            {
                float angle=i*Mathf.PI*2/segments;
                vh.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,color,Vector2.zero);
                if(i>0)vh.AddTriangle(0,i,i+1);
            }
        }
    }
}
