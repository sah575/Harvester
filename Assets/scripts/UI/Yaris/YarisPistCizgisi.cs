using System.Collections.Generic;
using Harvester.Race;
using UnityEngine;
using UnityEngine.UI;

namespace Harvester.UI.Yaris
{
    // Geometri yalnız pist kurulurken hesaplanır; hareket için RectTransform kullanılır.
    public sealed class YarisPistCizgisi : MaskableGraphic
    {
        readonly List<UIVertex> vertices=new List<UIVertex>();
        readonly List<int> indices=new List<int>();
        public Vector2 Origin {get;private set;}
        public void Build(RaceTrack track,float scale)
        {
            vertices.Clear();indices.Clear();raycastTarget=false;
            var points=track.Centerline;
            if(points.Count<2){SetVerticesDirty();return;}
            Vector2 min=new Vector2(points[0].x,points[0].z),max=min;
            for(int i=1;i<points.Count;i++)
            {
                var p=new Vector2(points[i].x,points[i].z);
                min=Vector2.Min(min,p);max=Vector2.Max(max,p);
            }
            Origin=(min+max)*.5f;
            rectTransform.sizeDelta=(max-min)*scale+Vector2.one*8;
            int segments=track.Loop?points.Count:points.Count-1;
            for(int i=0;i<segments;i++)
            {
                var a=(new Vector2(points[i].x,points[i].z)-Origin)*scale;
                var b=(new Vector2(points[(i+1)%points.Count].x,points[(i+1)%points.Count].z)-Origin)*scale;
                var direction=b-a;if(direction.sqrMagnitude<.0001f)continue;
                var side=new Vector2(-direction.y,direction.x).normalized*2;
                int start=vertices.Count;
                Add(a-side);Add(a+side);Add(b+side);Add(b-side);
                indices.Add(start);indices.Add(start+1);indices.Add(start+2);
                indices.Add(start);indices.Add(start+2);indices.Add(start+3);
            }
            SetVerticesDirty();
        }
        void Add(Vector2 point)
        {
            var vertex=UIVertex.simpleVert;vertex.position=point;vertex.color=new Color(.65f,.72f,.58f,.85f);
            vertices.Add(vertex);
        }
        protected override void OnPopulateMesh(VertexHelper helper)
        {helper.Clear();helper.AddUIVertexStream(vertices,indices);}
    }
}
