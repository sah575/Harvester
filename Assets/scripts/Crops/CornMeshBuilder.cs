using System.Collections.Generic;
using UnityEngine;

// Mısır öbeği mesh'lerini kodla üretir. Tarladaki bütün öbekler aynı mesh'in instance'ı olarak çizilir.
// Vertex verisi shader için: UV.y = yükseklik oranı (rüzgâr ve renk geçişi), renk.rgb = ton, renk.a = tepe rengine kayma.
public static class CornMeshBuilder
{
    public const float StalkHeight = 5.4f;

    static readonly Vector3[] StalkOffsets =
    {
        new Vector3(-0.35f, 0f, -0.2f),
        new Vector3(0.3f, 0f, -0.3f),
        new Vector3(0.05f, 0f, 0.35f),
    };

    static readonly Color StalkColor = new Color(0.8f, 0.85f, 0.72f, 0f);
    static readonly Color TasselColor = new Color(1f, 0.95f, 0.8f, 0.9f);
    static readonly Color CobColor = new Color(1f, 0.95f, 0.75f, 0.55f);

    // lod 0: 3 sap, yapraklar eğri; lod 1: 2 sap, az yaprak; lod 2: çapraz iki düzlem (uzak)
    public static Mesh BuildClump(int lod)
    {
        var b = new Builder();
        var rng = new System.Random(7);

        if (lod >= 2)
        {
            // Uzak LOD: sisin içinde siluet yeterli. Yukarı doğru incelen üç kart, kutu gibi görünmesin.
            Color c = new Color(0.85f, 0.9f, 0.8f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float yaw = i * Mathf.PI / 3f + 0.4f;
                Vector3 side = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                Vector3 top = Vector3.up * StalkHeight;
                b.Quad(-side * 0.6f, side * 0.6f, side * 0.15f + top, -side * 0.15f + top, c);
            }
            return b.ToMesh("Corn_LOD2");
        }

        int stalks = lod == 0 ? 3 : 2;
        int leaves = lod == 0 ? 8 : 4;
        int segments = lod == 0 ? 3 : 2;

        for (int s = 0; s < stalks; s++)
        {
            Vector3 root = StalkOffsets[s];
            float height = StalkHeight * Range(rng, 0.9f, 1f);
            b.Prism(root, height, 0.09f, 0.03f, lod == 0 ? 4 : 3, StalkColor);

            float yaw = Range(rng, 0f, Mathf.PI * 2f);
            for (int l = 0; l < leaves; l++)
            {
                float t = (l + 0.5f) / leaves;
                float y = Mathf.Lerp(0.6f, height * 0.82f, t);
                float length = Mathf.Lerp(1.5f, 0.9f, t) * Range(rng, 0.85f, 1.1f);
                float shade = Range(rng, 0.85f, 1.05f);
                b.Leaf(root + Vector3.up * y, yaw, length, 0.2f, segments, new Color(shade, shade, shade, 0f));
                yaw += Mathf.PI + Range(rng, -0.4f, 0.4f); // yapraklar sırayla karşı tarafa
            }

            if (lod == 0)
            {
                // Tepe püskülü
                Vector3 top = root + Vector3.up * height;
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.PI * 2f / 3f + yaw;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * 0.04f;
                    Vector3 tip = top + dir * 0.25f + Vector3.up * 0.55f;
                    b.Quad(top - side, top + side, tip + side, tip - side, TasselColor);
                }

                // Koçan: sapa yaslı, hafif dışa eğik
                Vector3 cobBase = root + Vector3.up * height * 0.5f;
                Vector3 cobDir = (new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * 0.4f + Vector3.up).normalized;
                b.Prism(cobBase, 0.55f, 0.11f, 0.05f, 4, CobColor, cobDir);
            }
        }

        return b.ToMesh("Corn_LOD" + lod);
    }

    // Biçilmiş hücrede kalan anız
    public static Mesh BuildStubble()
    {
        var b = new Builder();
        var rng = new System.Random(11);
        foreach (var offset in StalkOffsets)
        {
            b.Prism(offset, Range(rng, 0.35f, 0.5f), 0.07f, 0.06f, 4, StalkColor);
        }
        return b.ToMesh("Corn_Stubble");
    }

    static float Range(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }

    class Builder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();

        // a,b alt kenar, c,d üst kenar (saat yönü tersine)
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            Vector3 normal = Vector3.Cross(b - a, d - a).normalized;
            int start = vertices.Count;
            Add(a, normal, color);
            Add(b, normal, color);
            Add(c, normal, color);
            Add(d, normal, color);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
        }

        public void Prism(Vector3 root, float height, float bottomRadius, float topRadius, int sides, Color color)
        {
            Prism(root, height, bottomRadius, topRadius, sides, color, Vector3.up);
        }

        public void Prism(Vector3 root, float height, float bottomRadius, float topRadius, int sides, Color color, Vector3 axis)
        {
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis);
            Vector3 top = root + axis * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = rotation * new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = rotation * new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Quad(root + d1 * bottomRadius, root + d0 * bottomRadius, top + d0 * topRadius, top + d1 * topRadius, color);
            }
        }

        // Önce yukarı kalkıp sonra sarkan yaprak şeridi
        public void Leaf(Vector3 basePosition, float yaw, float length, float width, int segments, Color color)
        {
            Vector3 dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 previous = basePosition;
            float previousWidth = width * 0.5f;

            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 center = basePosition
                    + dir * (length * t)
                    + Vector3.up * (Mathf.Sin(t * Mathf.PI * 0.8f) * length * 0.35f - t * t * length * 0.45f);
                float w = Mathf.Max(0.01f, width * (1f - t * 0.9f));
                Quad(previous - side * previousWidth * 0.5f, previous + side * previousWidth * 0.5f,
                     center + side * w * 0.5f, center - side * w * 0.5f, color);
                previous = center;
                previousWidth = w;
            }
        }

        void Add(Vector3 position, Vector3 normal, Color color)
        {
            vertices.Add(position);
            normals.Add(normal);
            uvs.Add(new Vector2(0f, Mathf.Clamp01(position.y / StalkHeight)));
            colors.Add(color);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
