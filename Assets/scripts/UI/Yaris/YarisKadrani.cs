using UnityEngine;
using UnityEngine.UI;

namespace Harvester.UI.Yaris
{
    // Çentikler ve yaylar uGUI geometrisidir; doku veya paylaşılan malzeme gerektirmez.
    public sealed class YarisKadrani : MaskableGraphic
    {
        float speed, rpm, maximum = 240;
        public void SetValues(float kmh, float revs, float max)
        {
            if (Mathf.Approximately(speed, kmh) && Mathf.Approximately(rpm, revs) && Mathf.Approximately(maximum, max)) return;
            speed = kmh; rpm = revs; maximum = Mathf.Max(10, max); SetVerticesDirty();
        }
        public static Vector2 Point(float fraction, float radius)
        {
            float angle = Mathf.Lerp(220, -40, fraction) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color ink = MenuWidgets.Ink, accent = MenuWidgets.Accent;
            Arc(vh, 130, 4, 0, 1, new Color(.18f, .22f, .18f));
            Arc(vh, 130, 5, 0, Mathf.Clamp01(speed / maximum), accent);
            float red = Mathf.InverseLerp(Harvester.Race.RaceCar.IdleRpm, Harvester.Race.RaceCar.RedlineRpm, 6500);
            Arc(vh, 99, 7, 0, red, new Color(.15f,.18f,.14f));
            Arc(vh, 99, 7, red, 1, new Color(.55f,.12f,.08f));
            float rev = Mathf.InverseLerp(Harvester.Race.RaceCar.IdleRpm, Harvester.Race.RaceCar.RedlineRpm, rpm);
            Arc(vh, 99, 5, 0, rev, rpm >= 6500 ? new Color(.95f,.22f,.12f) : accent);
            for (int i = 0; i <= Mathf.FloorToInt(maximum / 10); i++)
            {
                float f = i * 10 / maximum;
                Line(vh, Point(f, i % 2 == 0 ? 116 : 121), Point(f, 135), 2, ink);
            }
            Line(vh, Point(Mathf.Clamp01(speed / maximum), 24), Point(Mathf.Clamp01(speed / maximum), 111), 3, accent);
        }
        static void Arc(VertexHelper vh, float radius, float width, float from, float to, Color color)
        {
            int segments = Mathf.CeilToInt((to - from) * 100);
            for (int i = 0; i < segments; i++)
                Line(vh, Point(Mathf.Lerp(from, to, (float)i / segments), radius),
                    Point(Mathf.Lerp(from, to, (float)(i + 1) / segments), radius), width, color);
        }
        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 n = new Vector2(-(b-a).y, (b-a).x).normalized * width * .5f;
            int start = vh.currentVertCount;
            vh.AddVert(a-n, color, Vector2.zero); vh.AddVert(a+n, color, Vector2.zero);
            vh.AddVert(b+n, color, Vector2.zero); vh.AddVert(b-n, color, Vector2.zero);
            vh.AddTriangle(start, start+1, start+2); vh.AddTriangle(start, start+2, start+3);
        }
    }
}
