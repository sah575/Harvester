using System.Collections.Generic;
using System.IO;
using Harvester.Race;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// race sahnesindeki yarış pistini kurar: orta çizgi (RaceTrack), yol, kenar çizgileri, bariyerler, zemin,
// başlangıç/bitiş işaretleri, sokak lambaları, ay ışığı ve sis.
// Menü: Harvester > Rebuild Race Track (sahnedeki düzeni, yoksa turlu pisti yeniden kurar)
//       Harvester > Race Track Layout > Turlu Pist / A-B Pist (o düzeni kurar)
// Pist düzenleri Assets/scripts/Race/Track/Data altındaki RaceTrackLayout asset'leridir; noktaları oradan değiştirilebilir.
public static class TrackBuilder
{
    const string ScenePath = "Assets/Scenes/race.unity";
    const string DataFolder = "Assets/scripts/Race/Track/Data";
    const string LoopLayoutPath = DataFolder + "/TurluPist.asset";
    const string PointToPointLayoutPath = DataFolder + "/ABPist.asset";
    const string SkyboxPath = "Assets/SKYBOXS/NightSkyFog.mat";

    // Yol kesiti (m)
    const float Shoulder = 0.6f;        // kenar çizgisinden bariyere kadar asfalt payı
    const float BarrierBase = 0.6f;     // bariyer taban kalınlığı
    const float BarrierHeight = 0.85f;  // görünen bariyer yüksekliği
    const float BarrierColliderHeight = 2.5f; // çarpışma duvarı daha yüksek: zıplayan araba aşmasın
    const float LineHeight = 0.015f;    // çizgilerin asfalttan yüksekliği (z-fighting olmasın)
    const int ChunkSegments = 50;       // bir yol parçasındaki orta çizgi aralığı sayısı (~100 m)

    // Zemin
    const float GroundCell = 6f;
    const int GroundChunkCells = 24;    // ~144 m'lik parçalar: ışık başına nesne limiti düşük kalite ayarlarında da yetsin
    const float GroundMargin = 220f;    // pistin çevresinde zeminin uzandığı mesafe (sis ötesini zaten gizler)
    const float GroundBelowRoad = 0.3f;
    const float FlatBand = 12f;         // yol kenarından sonra düz kalan şerit
    const float BlendBand = 30f;        // düz şeritten doğal zemine geçiş (şev)

    // Lambalar
    const float LampSpacing = 140f;
    const float LampHeight = 8f;

    // Kısıtlar (kurucu bunları kontrol edip uyarır)
    const float MinRadiusLimit = 30f;
    const float MaxGradeLimit = 0.08f;
    const float MaxHeightRange = 8.5f;

    struct Frame
    {
        public Vector3 position, forward, right, normal;
        public float distance;
    }

    class MeshData
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<List<int>> submeshes = new List<List<int>>();

        public MeshData(int submeshCount)
        {
            for (int i = 0; i < submeshCount; i++) submeshes.Add(new List<int>());
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        public int TriangleCount
        {
            get { int t = 0; foreach (var s in submeshes) t += s.Count / 3; return t; }
        }
    }

    class Materials
    {
        public Material asphalt, line, barrier, reflector, ground, checkerBlack, checkerWhite, pole, lampHead, endWall;
        public PhysicsMaterial barrierPhysics;
    }

    // İstatistik sayaçları
    static int meshCount, triangleCount;

    [MenuItem("Harvester/Rebuild Race Track")]
    public static void Rebuild()
    {
        if (!OpenRaceScene()) return;
        var source = Object.FindFirstObjectByType<RaceTrackSource>();
        var layout = source != null && source.layout != null ? source.layout : GetLayout(true);
        Build(layout);
    }

    [MenuItem("Harvester/Race Track Layout/Turlu Pist")]
    public static void BuildLoop()
    {
        if (!OpenRaceScene()) return;
        Build(GetLayout(true));
    }

    [MenuItem("Harvester/Race Track Layout/A-B Pist")]
    public static void BuildPointToPoint()
    {
        if (!OpenRaceScene()) return;
        Build(GetLayout(false));
    }

    // ---------------------------------------------------------------- sahne ve veri

    static bool OpenRaceScene()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.path == ScenePath) return true;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

        if (File.Exists(ScenePath))
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        else
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        return true;
    }

    static RaceTrackLayout GetLayout(bool loop)
    {
        string path = loop ? LoopLayoutPath : PointToPointLayoutPath;
        var layout = AssetDatabase.LoadAssetAtPath<RaceTrackLayout>(path);
        if (layout != null) return layout;

        EnsureFolder(DataFolder);
        layout = ScriptableObject.CreateInstance<RaceTrackLayout>();
        if (loop)
        {
            // ~1.4 km: uzun başlangıç düzlüğü, tepeye tırmanan geniş sağ kavis, orta sert S, geri dönüşte ikinci tepe
            layout.displayName = "Turlu Pist";
            layout.loop = true;
            layout.controlPoints = new List<Vector3>
            {
                new Vector3(0f, 0.5f, -120f), new Vector3(0f, 0.5f, 0f), new Vector3(0f, 2f, 120f),
                new Vector3(30f, 5f, 200f), new Vector3(100f, 7.5f, 230f), new Vector3(170f, 8f, 200f),
                new Vector3(200f, 6f, 130f), new Vector3(260f, 2.5f, 90f), new Vector3(330f, 1f, 110f),
                new Vector3(390f, 1f, 80f), new Vector3(400f, 1f, 0f), new Vector3(360f, 1.5f, -70f),
                new Vector3(290f, 3.5f, -90f), new Vector3(230f, 5.5f, -150f), new Vector3(160f, 5f, -200f),
                new Vector3(80f, 2.5f, -210f), new Vector3(25f, 1f, -180f),
            };
        }
        else
        {
            // ~1.9 km: geniş kavislerle kıvrılan, iki tepeden geçen A'dan B'ye yol
            layout.displayName = "A-B Pist";
            layout.loop = false;
            layout.controlPoints = new List<Vector3>
            {
                new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0.5f, 150f), new Vector3(30f, 2.5f, 300f),
                new Vector3(110f, 5f, 400f), new Vector3(220f, 7f, 430f), new Vector3(330f, 7.5f, 390f),
                new Vector3(420f, 5f, 330f), new Vector3(520f, 2f, 330f), new Vector3(600f, 1.5f, 400f),
                new Vector3(640f, 2f, 500f), new Vector3(600f, 4.5f, 600f), new Vector3(520f, 7f, 650f),
                new Vector3(440f, 8f, 720f), new Vector3(440f, 7f, 820f), new Vector3(500f, 4.5f, 900f),
                new Vector3(600f, 2f, 920f), new Vector3(720f, 1f, 880f), new Vector3(820f, 1f, 880f),
            };
        }
        AssetDatabase.CreateAsset(layout, path);
        AssetDatabase.SaveAssets();
        return layout;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    // ---------------------------------------------------------------- kurulum

    static void Build(RaceTrackLayout layout)
    {
        meshCount = 0;
        triangleCount = 0;
        if (layout.controlPoints.Count < (layout.loop ? 3 : 2))
        {
            Debug.LogError($"Pist düzeni '{layout.displayName}' için yeterli kontrol noktası yok.");
            return;
        }

        var points = layout.SampleCenterline();
        bool loop = layout.loop;
        float width = layout.width;

        // Pist kökü: RaceTrack bileşeni + hangi düzenden kurulduğu
        var track = Object.FindFirstObjectByType<RaceTrack>();
        if (track == null)
        {
            var go = new GameObject("RaceTrack");
            track = go.AddComponent<RaceTrack>();
        }
        var root = track.transform;
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.localScale = Vector3.one;
        var source = track.GetComponent<RaceTrackSource>();
        if (source == null) source = track.gameObject.AddComponent<RaceTrackSource>();
        source.layout = layout;
        track.SetCenterline(points, loop, width);
        EditorUtility.SetDirty(track);
        EditorUtility.SetDirty(source);

        var old = root.Find("Uretilen");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var generated = new GameObject("Uretilen").transform;
        generated.SetParent(root, false);

        var frames = BuildFrames(points, loop);
        var materials = GetMaterials();

        BuildRoad(frames, loop, width, generated, materials);
        BuildBarriers(frames, loop, width, generated, materials);
        BuildGround(frames, width, generated, materials);
        BuildMarkers(track, frames, loop, width, generated, materials);
        BuildLamps(track, frames, loop, width, generated, materials);
        SetupAtmosphere(track);

        LogStats(layout, frames, loop);
        var scene = track.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static Frame[] BuildFrames(List<Vector3> points, bool loop)
    {
        int n = points.Count;
        var frames = new Frame[n];
        float distance = 0f;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) distance += Vector3.Distance(points[i - 1], points[i]);
            Vector3 prev = loop || i > 0 ? points[(i - 1 + n) % n] : points[i];
            Vector3 next = loop || i < n - 1 ? points[(i + 1) % n] : points[i];
            Vector3 forward = (next - prev).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            right.y = 0f;
            right.Normalize();
            frames[i] = new Frame
            {
                position = points[i],
                forward = forward,
                right = right,
                normal = Vector3.Cross(forward, right).normalized,
                distance = distance,
            };
        }
        return frames;
    }

    static int SegmentCount(Frame[] frames, bool loop) => loop ? frames.Length : frames.Length - 1;

    // Segment sınırı k (0..SegmentCount) için çerçeve; turluda son sınır ilk noktadır ama mesafesi pist uzunluğudur
    static Frame FrameAt(Frame[] frames, int k)
    {
        if (k < frames.Length) return frames[k];
        var f = frames[k % frames.Length];
        var last = frames[frames.Length - 1];
        f.distance = last.distance + Vector3.Distance(last.position, f.position);
        return f;
    }

    /// <summary>
    /// Bir kesit profilini (x = yana, sağ artı; y = yukarı) orta çizgi boyunca [k0, k1] sınırları arasında uzatır.
    /// Her profil kenarı kendi köşelerini alır (sert kenarlı). Yüzey normali = ileri × profil yönü,
    /// yani soldan sağa giden kenar yukarı, aşağı inen kenar sağa bakar.
    /// </summary>
    static void Extrude(MeshData md, int submesh, Frame[] frames, Vector2[] profile, int k0, int k1, float uvScale)
    {
        float u = 0f;
        for (int j = 0; j < profile.Length - 1; j++)
        {
            Vector2 a = profile[j], b = profile[j + 1];
            float edgeLength = Vector2.Distance(a, b);
            int start = md.vertices.Count;
            for (int k = k0; k <= k1; k++)
            {
                var f = FrameAt(frames, k);
                Vector3 dir = f.right * (b.x - a.x) + Vector3.up * (b.y - a.y);
                Vector3 normal = Vector3.Cross(f.forward, dir).normalized;
                md.vertices.Add(f.position + f.right * a.x + Vector3.up * a.y);
                md.vertices.Add(f.position + f.right * b.x + Vector3.up * b.y);
                md.normals.Add(normal);
                md.normals.Add(normal);
                md.uvs.Add(new Vector2(u * uvScale, f.distance * uvScale));
                md.uvs.Add(new Vector2((u + edgeLength) * uvScale, f.distance * uvScale));
            }
            var tris = md.submeshes[submesh];
            for (int k = 0; k < k1 - k0; k++)
            {
                int v = start + k * 2;
                tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
                tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3);
            }
            u += edgeLength;
        }
    }

    static Vector2[] Mirror(Vector2[] profile)
    {
        var result = new Vector2[profile.Length];
        for (int i = 0; i < profile.Length; i++)
        {
            var p = profile[profile.Length - 1 - i];
            result[i] = new Vector2(-p.x, p.y);
        }
        return result;
    }

    static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material[] materials, bool castShadows, Mesh collider, PhysicsMaterial physics = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (materials != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            meshCount++;
            triangleCount += (int)CountTriangles(mesh);
        }
        if (collider != null)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = collider;
            if (physics != null) mc.sharedMaterial = physics;
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        return go;
    }

    static long CountTriangles(Mesh mesh)
    {
        long t = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) t += mesh.GetIndexCount(i) / 3;
        return t;
    }

    // ---------------------------------------------------------------- yol ve çizgiler

    static void BuildRoad(Frame[] frames, bool loop, float width, Transform parent, Materials m)
    {
        float hw = width * 0.5f;
        var roadParent = new GameObject("Yol").transform;
        roadParent.SetParent(parent, false);
        var lineParent = new GameObject("Cizgiler").transform;
        lineParent.SetParent(parent, false);

        var roadProfile = new[] { new Vector2(-(hw + Shoulder), 0f), new Vector2(hw + Shoulder, 0f) };
        var leftLine = new[] { new Vector2(-(hw - 0.19f), LineHeight), new Vector2(-(hw - 0.41f), LineHeight) };
        var rightLine = new[] { new Vector2(hw - 0.41f, LineHeight), new Vector2(hw - 0.19f, LineHeight) };
        var centerLine = new[] { new Vector2(-0.1f, LineHeight), new Vector2(0.1f, LineHeight) };

        int segments = SegmentCount(frames, loop);
        int chunk = 0;
        for (int k0 = 0; k0 < segments; k0 += ChunkSegments, chunk++)
        {
            int k1 = Mathf.Min(k0 + ChunkSegments, segments);

            var road = new MeshData(1);
            Extrude(road, 0, frames, roadProfile, k0, k1, 0.25f); // doku 4 m'de bir tekrar eder
            var roadMesh = road.ToMesh("Yol_" + chunk);
            CreateMeshObject("Yol_" + chunk, roadParent, roadMesh, new[] { m.asphalt }, false, roadMesh);

            var lines = new MeshData(1);
            Extrude(lines, 0, frames, leftLine, k0, k1, 1f);
            Extrude(lines, 0, frames, rightLine, k0, k1, 1f);
            // Orta kesik çizgi: 6 m çizgi, 6 m boşluk
            for (int k = k0; k < k1; k++)
            {
                if ((k / 3) % 2 != 0) continue;
                int end = k;
                while (end < k1 && (end / 3) % 2 == 0) end++;
                Extrude(lines, 0, frames, centerLine, k, end, 1f);
                k = end;
            }
            CreateMeshObject("Cizgi_" + chunk, lineParent, lines.ToMesh("Cizgi_" + chunk), new[] { m.line }, false, null);
        }
    }

    // ---------------------------------------------------------------- bariyerler

    static void BuildBarriers(Frame[] frames, bool loop, float width, Transform parent, Materials m)
    {
        float hw = width * 0.5f;
        float xi = -(hw + Shoulder);       // sol bariyerin iç yüzü
        float xo = xi - BarrierBase;       // dış yüzü

        // Sol bariyer görünen kesiti (dış alt → dış üst → üst → iç yüz → yol altı). Alt kısım zemine gömülü.
        var visualLeft = new[]
        {
            new Vector2(xo, -1.5f), new Vector2(xo, 0f), new Vector2(xo + 0.15f, BarrierHeight),
            new Vector2(xi - 0.12f, BarrierHeight), new Vector2(xi - 0.09f, 0.62f),
            new Vector2(xi, 0f), new Vector2(xi, -1.5f),
        };
        // Reflektör şeridi: iç yüzün üst kısmı (ayrı alt mesh, parlayan malzeme)
        int reflectorEdge = 3;
        var colliderLeft = new[]
        {
            new Vector2(xo, -1f), new Vector2(xo, BarrierColliderHeight), new Vector2(xi, BarrierColliderHeight),
            new Vector2(xi, -1f), new Vector2(xo, -1f),
        };
        var visualRight = Mirror(visualLeft);
        var colliderRight = Mirror(colliderLeft);
        int reflectorEdgeRight = visualLeft.Length - 2 - reflectorEdge;

        var barrierParent = new GameObject("Bariyerler").transform;
        barrierParent.SetParent(parent, false);

        int segments = SegmentCount(frames, loop);
        int chunk = 0;
        for (int k0 = 0; k0 < segments; k0 += ChunkSegments, chunk++)
        {
            int k1 = Mathf.Min(k0 + ChunkSegments, segments);
            var visual = new MeshData(2);
            ExtrudeSplit(visual, frames, visualLeft, reflectorEdge, k0, k1);
            ExtrudeSplit(visual, frames, visualRight, reflectorEdgeRight, k0, k1);
            var collider = new MeshData(1);
            Extrude(collider, 0, frames, colliderLeft, k0, k1, 1f);
            Extrude(collider, 0, frames, colliderRight, k0, k1, 1f);
            CreateMeshObject("Bariyer_" + chunk, barrierParent, visual.ToMesh("Bariyer_" + chunk), new[] { m.barrier, m.reflector }, true,
                collider.ToMesh("BariyerCarpisma_" + chunk), m.barrierPhysics);
        }

        // A'dan B'ye pistin iki ucunu duvarla kapat
        if (!loop)
        {
            var first = frames[0];
            var last = frames[frames.Length - 1];
            CreateEndWall("BaslangicDuvari", barrierParent, first.position - first.forward * 1f, first.forward, hw, m);
            CreateEndWall("SonDuvar", barrierParent, last.position + last.forward * 1f, last.forward, hw, m);
        }
    }

    static void ExtrudeSplit(MeshData md, Frame[] frames, Vector2[] profile, int specialEdge, int k0, int k1)
    {
        for (int j = 0; j < profile.Length - 1; j++)
            Extrude(md, j == specialEdge ? 1 : 0, frames, new[] { profile[j], profile[j + 1] }, k0, k1, 1f);
    }

    static void CreateEndWall(string name, Transform parent, Vector3 position, Vector3 forward, float hw, Materials m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        Vector3 flat = new Vector3(forward.x, 0f, forward.z).normalized;
        go.transform.SetPositionAndRotation(position + Vector3.up * 0.75f, Quaternion.LookRotation(flat, Vector3.up));
        go.transform.localScale = new Vector3((hw + Shoulder + BarrierBase) * 2f, 3f, 1.2f);
        go.GetComponent<MeshRenderer>().sharedMaterial = m.endWall;
        go.GetComponent<BoxCollider>().sharedMaterial = m.barrierPhysics;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        meshCount++;
        triangleCount += 12;
    }

    // ---------------------------------------------------------------- zemin

    static float NaturalHeight(float x, float z)
    {
        // Hafif dalgalı tarla; yolun en alçak yerinden de alçakta kalsın (yol hep zeminin üstünde ya da şevde)
        return 0.8f * Mathf.Sin(x * 0.013f + 1.3f) * Mathf.Cos(z * 0.011f - 0.4f)
             + 0.5f * Mathf.Sin((x - z) * 0.021f) - 0.7f;
    }

    static void BuildGround(Frame[] frames, float width, Transform parent, Materials m)
    {
        float hw = width * 0.5f;
        Vector3 min = frames[0].position, max = frames[0].position;
        foreach (var f in frames) { min = Vector3.Min(min, f.position); max = Vector3.Max(max, f.position); }
        float x0 = Mathf.Floor((min.x - GroundMargin) / GroundCell) * GroundCell;
        float z0 = Mathf.Floor((min.z - GroundMargin) / GroundCell) * GroundCell;
        int nx = Mathf.CeilToInt((max.x + GroundMargin - x0) / GroundCell / GroundChunkCells) * GroundChunkCells;
        int nz = Mathf.CeilToInt((max.z + GroundMargin - z0) / GroundCell / GroundChunkCells) * GroundChunkCells;

        // Orta çizgi noktalarını kaba ızgaraya dağıt: her zemin köşesi sadece yakınındakilere baksın
        float influence = hw + FlatBand + BlendBand;
        float bucketSize = influence;
        var buckets = new Dictionary<long, List<int>>();
        for (int i = 0; i < frames.Length; i++)
        {
            long key = BucketKey(Mathf.FloorToInt(frames[i].position.x / bucketSize), Mathf.FloorToInt(frames[i].position.z / bucketSize));
            List<int> list;
            if (!buckets.TryGetValue(key, out list)) buckets[key] = list = new List<int>();
            list.Add(i);
        }

        // Yükseklikler (kenarlarda bir hücre fazlası: parça sınırlarında normaller tutarlı olsun)
        int sx = nx + 3, sz = nz + 3;
        var heights = new float[sx * sz];
        for (int iz = 0; iz < sz; iz++)
        {
            for (int ix = 0; ix < sx; ix++)
            {
                float x = x0 + (ix - 1) * GroundCell, z = z0 + (iz - 1) * GroundCell;
                float h = NaturalHeight(x, z);
                int bx = Mathf.FloorToInt(x / bucketSize), bz = Mathf.FloorToInt(z / bucketSize);
                float best = float.MaxValue;
                float roadY = 0f;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        List<int> list;
                        if (!buckets.TryGetValue(BucketKey(bx + dx, bz + dz), out list)) continue;
                        foreach (int i in list)
                        {
                            Vector3 p = frames[i].position;
                            float d2 = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z);
                            if (d2 < best) { best = d2; roadY = p.y; }
                        }
                    }
                }
                if (best < influence * influence)
                {
                    float d = Mathf.Sqrt(best);
                    float t = Mathf.SmoothStep(0f, 1f, (d - hw - FlatBand) / BlendBand);
                    h = Mathf.Lerp(roadY - GroundBelowRoad, h, t);
                }
                heights[iz * sx + ix] = h;
            }
        }

        var groundParent = new GameObject("Zemin").transform;
        groundParent.SetParent(parent, false);
        int cells = GroundChunkCells;
        for (int cz = 0; cz < nz / cells; cz++)
        {
            for (int cx = 0; cx < nx / cells; cx++)
            {
                Vector2 center = new Vector2(x0 + (cx + .5f) * cells * GroundCell, z0 + (cz + .5f) * cells * GroundCell);
                float nearest = float.MaxValue;
                foreach (var frame in frames)
                {
                    Vector2 delta = new Vector2(frame.position.x, frame.position.z) - center;
                    nearest = Mathf.Min(nearest, delta.sqrMagnitude);
                }
                // Tüm parça yolun düz/şev bandının dışındaysa 18 m hücre yeterlidir.
                float safe = cells * GroundCell * .707107f + influence + GroundCell;
                int step = nearest > safe * safe ? 3 : 1;
                int grid = cells / step;
                var md = new MeshData(1);
                for (int iz = 0; iz <= cells; iz += step)
                {
                    for (int ix = 0; ix <= cells; ix += step)
                    {
                        int gx = cx * cells + ix + 1, gz = cz * cells + iz + 1; // dolgulu dizideki yer
                        float x = x0 + (gx - 1) * GroundCell, z = z0 + (gz - 1) * GroundCell;
                        float h = heights[gz * sx + gx];
                        float hl = heights[gz * sx + gx - 1], hr = heights[gz * sx + gx + 1];
                        float hd = heights[(gz - 1) * sx + gx], hu = heights[(gz + 1) * sx + gx];
                        md.vertices.Add(new Vector3(x, h, z));
                        md.normals.Add(new Vector3(hl - hr, 2f * GroundCell, hd - hu).normalized);
                        md.uvs.Add(new Vector2(x / 8f, z / 8f)); // doku 8 m'de bir tekrar
                    }
                }
                var tris = md.submeshes[0];
                int row = grid + 1;
                for (int iz = 0; iz < grid; iz++)
                {
                    for (int ix = 0; ix < grid; ix++)
                    {
                        int v = iz * row + ix;
                        tris.Add(v); tris.Add(v + row); tris.Add(v + 1);
                        tris.Add(v + 1); tris.Add(v + row); tris.Add(v + row + 1);
                    }
                }
                if (step > 1)
                {
                    // İnce/seyrek sınırda küçük yükseklik farkı gökyüzüne açılan çatlak bırakmasın.
                    GroundSkirt(md, 0, 1, grid);
                    GroundSkirt(md, grid * row, 1, grid);
                    GroundSkirt(md, 0, row, grid);
                    GroundSkirt(md, grid, row, grid);
                }
                var mesh = md.ToMesh($"Zemin_{cx}_{cz}");
                CreateMeshObject(mesh.name, groundParent, mesh, new[] { m.ground }, false, mesh);
            }
        }
    }

    static void GroundSkirt(MeshData md, int first, int step, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            int a = first + i * step, b = a + step, low = md.vertices.Count;
            md.vertices.Add(md.vertices[a] + Vector3.down * .5f); md.vertices.Add(md.vertices[b] + Vector3.down * .5f);
            md.normals.Add(md.normals[a]); md.normals.Add(md.normals[b]); md.uvs.Add(md.uvs[a]); md.uvs.Add(md.uvs[b]);
            var tris = md.submeshes[0];
            tris.Add(a); tris.Add(b); tris.Add(low); tris.Add(b); tris.Add(low + 1); tris.Add(low);
        }
    }

    static long BucketKey(int x, int z) => ((long)x << 32) ^ (uint)z;

    // ---------------------------------------------------------------- işaretler

    static void BuildMarkers(RaceTrack track, Frame[] frames, bool loop, float width, Transform parent, Materials m)
    {
        float hw = width * 0.5f;
        var markers = new GameObject("Isaretler").transform;
        markers.SetParent(parent, false);

        // Bitiş çizgisi (turluda başlangıç/bitiş aynı çizgi, 0 mesafede)
        float finish = loop ? 0f : track.FinishDistance;
        CreateCheckerLine(loop ? "BaslangicBitisCizgisi" : "BitisCizgisi", markers, track, finish, hw, m);
        CreateGantry(loop ? "BaslangicBitisKapisi" : "BitisKapisi", markers, track, finish, hw, m);
        if (!loop)
        {
            // A'dan B'ye pistte dizilişin hemen önünde başlangıç çizgisi
            float start = track.StartDistance + 6f;
            CreateCheckerLine("BaslangicCizgisi", markers, track, start, hw, m);
            CreateGantry("BaslangicKapisi", markers, track, start, hw, m);
        }

        // Diziliş kutuları: arabanın önünde bir çizgi, iki yanında kısa çizgiler (ters U)
        for (int slot = 0; slot < 4; slot++)
        {
            var pose = track.GetStartPose(slot);
            var go = new GameObject("Dizilis_" + slot);
            go.transform.SetParent(markers, false);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var md = new MeshData(1);
            AddFlatQuad(md, new Vector2(-1.5f, 2.4f), new Vector2(1.5f, 2.65f));
            AddFlatQuad(md, new Vector2(-1.5f, 0.6f), new Vector2(-1.25f, 2.4f));
            AddFlatQuad(md, new Vector2(1.25f, 0.6f), new Vector2(1.5f, 2.4f));
            var renderer = CreateMeshObject("Cizgi", go.transform, md.ToMesh("Dizilis_" + slot), new[] { m.line }, false, null);
            renderer.transform.localPosition = Vector3.zero;
        }

        // Harvester'ın başlangıç yeri: sadece sahne görünümünde işaret
        var harvesterPose = track.GetHarvesterStartPose();
        var harvester = new GameObject("HarvesterBaslangic");
        harvester.transform.SetParent(markers, false);
        harvester.transform.SetPositionAndRotation(harvesterPose.position, harvesterPose.rotation);
        var gizmo = harvester.AddComponent<TrackGizmo>();
        gizmo.size = new Vector3(7f, 4.5f, 10f);
        gizmo.label = "Harvester başlangıcı";
    }

    // Yerel düzlemde (y = çizgi yüksekliği) dikdörtgen; normal yukarı
    static void AddFlatQuad(MeshData md, Vector2 a, Vector2 b)
    {
        int v = md.vertices.Count;
        md.vertices.Add(new Vector3(a.x, LineHeight, a.y));
        md.vertices.Add(new Vector3(a.x, LineHeight, b.y));
        md.vertices.Add(new Vector3(b.x, LineHeight, a.y));
        md.vertices.Add(new Vector3(b.x, LineHeight, b.y));
        for (int i = 0; i < 4; i++) md.normals.Add(Vector3.up);
        md.uvs.Add(new Vector2(0, 0)); md.uvs.Add(new Vector2(0, 1)); md.uvs.Add(new Vector2(1, 0)); md.uvs.Add(new Vector2(1, 1));
        var tris = md.submeshes[0];
        tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
        tris.Add(v + 2); tris.Add(v + 1); tris.Add(v + 3);
    }

    static void CreateCheckerLine(string name, Transform parent, RaceTrack track, float distance, float hw, Materials m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(track.GetPoint(distance), Quaternion.LookRotation(track.GetForward(distance), Vector3.up));
        var md = new MeshData(2);
        int columns = Mathf.RoundToInt(hw * 2f);
        float cell = hw * 2f / columns;
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                var sub = new MeshData(1);
                AddFlatQuad(sub, new Vector2(-hw + col * cell, (row - 1) * cell), new Vector2(-hw + (col + 1) * cell, row * cell));
                int baseIndex = md.vertices.Count;
                md.vertices.AddRange(sub.vertices);
                md.normals.AddRange(sub.normals);
                md.uvs.AddRange(sub.uvs);
                foreach (int t in sub.submeshes[0]) md.submeshes[(row + col) % 2].Add(baseIndex + t);
            }
        }
        CreateMeshObject("Dama", go.transform, md.ToMesh(name), new[] { m.checkerBlack, m.checkerWhite }, false, null);
    }

    // Çizginin üstünde iki direk ve damalı bir kiriş
    static void CreateGantry(string name, Transform parent, RaceTrack track, float distance, float hw, Materials m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Vector3 forward = track.GetForward(distance);
        forward.y = 0f;
        go.transform.SetPositionAndRotation(track.GetPoint(distance), Quaternion.LookRotation(forward.normalized, Vector3.up));
        float postX = hw + Shoulder + BarrierBase + 0.6f;
        const float beamY = 6.5f;
        for (int side = -1; side <= 1; side += 2)
            CreateDecor("Direk", go.transform, new Vector3(side * postX, beamY * 0.5f - 0.5f, 0f), new Vector3(0.45f, beamY + 1f, 0.45f), m.pole, PrimitiveType.Cube);
        int panels = Mathf.RoundToInt(postX * 2f / 1.2f);
        float panel = postX * 2f / panels;
        for (int i = 0; i < panels; i++)
        {
            for (int row = 0; row < 2; row++)
            {
                CreateDecor("Pano", go.transform, new Vector3(-postX + (i + 0.5f) * panel, beamY + row * 0.6f, 0f),
                    new Vector3(panel, 0.6f, 0.3f), (i + row) % 2 == 0 ? m.checkerWhite : m.checkerBlack, PrimitiveType.Cube);
            }
        }
        // Kapının altını aydınlatan ışık
        var lightGo = new GameObject("KapiIsigi");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, beamY - 0.6f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 30f;
        light.intensity = 250f;
        light.color = new Color(0.95f, 0.95f, 1f);
        light.shadows = LightShadows.None;
    }

    static GameObject CreateDecor(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material, PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>()); // süs; bariyerin dışında, araba ulaşamaz
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        int detailLayer = LayerMask.NameToLayer(VisibilityBudget.DetailLayer);
        if (detailLayer >= 0) go.layer = detailLayer;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        meshCount++;
        triangleCount += (int)CountTriangles(go.GetComponent<MeshFilter>().sharedMesh);
        return go;
    }

    // ---------------------------------------------------------------- lambalar

    static void BuildLamps(RaceTrack track, Frame[] frames, bool loop, float width, Transform parent, Materials m)
    {
        float hw = width * 0.5f;
        var lamps = new GameObject("Lambalar").transform;
        lamps.SetParent(parent, false);

        float length = track.Length;
        float first = loop ? 0f : 20f, last = loop ? length : length - 20f;
        int count = Mathf.Max(2, Mathf.RoundToInt((last - first) / LampSpacing));
        float finish = loop ? 0f : track.FinishDistance;
        int alternate = 1;
        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            float d = first + (i + 0.5f) * (last - first) / count;
            // Kapının hemen yanına koyma, orası zaten aydınlık
            float toFinish = Mathf.Abs(d - finish);
            if (loop) toFinish = Mathf.Min(toFinish, length - toFinish);
            if (toFinish < 30f) d += 40f;

            // Virajın dış tarafına koy (dönüşü gösterir); düzlükte sırayla iki yan
            Vector3 before = track.GetForward(d - 15f), after = track.GetForward(d + 15f);
            float turn = Vector3.Cross(before, after).y; // artı = sağa dönüş
            int side;
            if (Mathf.Abs(turn) > 0.05f) side = turn > 0f ? -1 : 1;
            else { side = alternate; alternate = -alternate; }

            Vector3 center = track.GetPoint(d);
            Vector3 right = track.GetRight(d);
            Vector3 basePos = center + right * side * (hw + Shoulder + BarrierBase + 1.6f) + Vector3.down * GroundBelowRoad;
            var lamp = new GameObject("Lamba_" + placed);
            lamp.transform.SetParent(lamps, false);
            lamp.transform.SetPositionAndRotation(basePos, Quaternion.LookRotation(-right * side, Vector3.up)); // ileri = yola doğru

            float poleTop = LampHeight + GroundBelowRoad;
            var pole = CreateDecor("Direk", lamp.transform, new Vector3(0f, poleTop * 0.5f, 0f), new Vector3(0.22f, poleTop * 0.5f, 0.22f), m.pole, PrimitiveType.Cylinder);
            var arm = CreateDecor("Kol", lamp.transform, new Vector3(0f, poleTop - 0.1f, 2.25f), new Vector3(0.12f, 0.12f, 4.5f), m.pole, PrimitiveType.Cube);
            var head = CreateDecor("Bas", lamp.transform, new Vector3(0f, poleTop - 0.25f, 4.4f), new Vector3(0.45f, 0.14f, 0.8f), m.lampHead, PrimitiveType.Cube);

            CombineLamp(lamp.transform, pole, arm, head);

            var lightGo = new GameObject("Isik");
            lightGo.transform.SetParent(lamp.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, poleTop - 0.6f, 4.6f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 40f;
            light.intensity = 300f;
            light.color = new Color(1f, 0.78f, 0.5f);
            light.shadows = LightShadows.None; // performans: lambalar gölge atmaz
            placed++;
        }
    }

    static void CombineLamp(Transform root, GameObject pole, GameObject arm, GameObject head)
    {
        Matrix4x4 inverse = root.worldToLocalMatrix;
        var metal = new Mesh();
        metal.CombineMeshes(new[] {
            new CombineInstance { mesh = pole.GetComponent<MeshFilter>().sharedMesh, transform = inverse * pole.transform.localToWorldMatrix },
            new CombineInstance { mesh = arm.GetComponent<MeshFilter>().sharedMesh, transform = inverse * arm.transform.localToWorldMatrix }
        }, true, true);
        var mesh = new Mesh { name = "LambaGovdesi" };
        mesh.CombineMeshes(new[] {
            new CombineInstance { mesh = metal, transform = Matrix4x4.identity },
            new CombineInstance { mesh = head.GetComponent<MeshFilter>().sharedMesh, transform = inverse * head.transform.localToWorldMatrix }
        }, false, true);
        var go = new GameObject("LambaGovdesi"); go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { pole.GetComponent<MeshRenderer>().sharedMaterial, head.GetComponent<MeshRenderer>().sharedMaterial };
        int layer = LayerMask.NameToLayer(VisibilityBudget.DetailLayer); if (layer >= 0) go.layer = layer;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        Object.DestroyImmediate(metal); Object.DestroyImmediate(pole); Object.DestroyImmediate(arm); Object.DestroyImmediate(head);
        meshCount -= 2; // üç süs yerine tek renderer; üçgen sayısı aynı
    }

    // ---------------------------------------------------------------- ışık, sis, kamera

    static void SetupAtmosphere(RaceTrack track)
    {
        // Gece + sis, ama world sahnesinden hafif: yol 100 m ötesine kadar okunsun
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.017f;
        RenderSettings.fogColor = new Color(0.16f, 0.18f, 0.23f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.14f, 0.16f, 0.23f);
        RenderSettings.ambientEquatorColor = new Color(0.07f, 0.075f, 0.1f);
        RenderSettings.ambientGroundColor = new Color(0.025f, 0.025f, 0.03f);
        var skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        if (skybox != null) RenderSettings.skybox = skybox;

        // Ay ışığı
        var moon = GameObject.Find("Ay Isigi");
        if (moon == null) moon = new GameObject("Ay Isigi");
        moon.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
        var light = moon.GetComponent<Light>();
        if (light == null) light = moon.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.55f, 0.65f, 1f);
        light.intensity = 0.5f;
        light.shadows = LightShadows.Soft;
        RenderSettings.sun = light;

        // Önizleme kamerası (oyuncu kamerasını Ağ ajanı kuracak). Sadece yoksa oluşturulur ve başlangıca konur.
        if (Camera.main == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            var pose = track.GetStartPose(3);
            Vector3 forward = pose.rotation * Vector3.forward;
            camGo.transform.position = pose.position - forward * 9f + Vector3.up * 3.5f;
            camGo.transform.rotation = Quaternion.LookRotation(pose.position + forward * 25f - camGo.transform.position, Vector3.up);
        }
    }

    // ---------------------------------------------------------------- istatistik

    static void LogStats(RaceTrackLayout layout, Frame[] frames, bool loop)
    {
        int n = frames.Length;
        const int k = 4; // eğrilik ölçümünde iki yana 8 m
        float minRadius = float.MaxValue;
        Vector3 minRadiusAt = Vector3.zero;
        for (int i = loop ? 0 : k; i < (loop ? n : n - k); i++)
        {
            Vector3 a = frames[(i - k + n) % n].position, b = frames[i].position, c = frames[(i + k) % n].position;
            float r = CircumRadius(new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z));
            if (r < minRadius) { minRadius = r; minRadiusAt = b; }
        }
        float maxGrade = 0f;
        Vector3 maxGradeAt = Vector3.zero;
        float minY = float.MaxValue, maxY = float.MinValue;
        int segments = SegmentCount(frames, loop);
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = frames[i].position, b = frames[(i + 1) % n].position;
            float horizontal = new Vector2(b.x - a.x, b.z - a.z).magnitude;
            float grade = horizontal > 1e-3f ? Mathf.Abs(b.y - a.y) / horizontal : 0f;
            if (grade > maxGrade) { maxGrade = grade; maxGradeAt = a; }
            minY = Mathf.Min(minY, a.y);
            maxY = Mathf.Max(maxY, a.y);
        }
        var last = FrameAt(frames, segments);

        Debug.Log($"Pist kuruldu: '{layout.displayName}' ({(loop ? "turlu" : "A'dan B'ye")}), uzunluk {last.distance:F0} m, {n} orta çizgi noktası, " +
                  $"en küçük viraj yarıçapı {minRadius:F1} m ({minRadiusAt.x:F0}, {minRadiusAt.z:F0}), en büyük eğim %{maxGrade * 100f:F1} ({maxGradeAt.x:F0}, {maxGradeAt.z:F0}), " +
                  $"yükseklik {minY:F1}..{maxY:F1} m, {meshCount} çizilen mesh, {triangleCount} üçgen.");
        if (minRadius < MinRadiusLimit) Debug.LogWarning($"Pist: en küçük viraj yarıçapı {minRadius:F1} m, sınır {MinRadiusLimit} m. Kontrol noktalarını aç.");
        if (maxGrade > MaxGradeLimit) Debug.LogWarning($"Pist: en büyük eğim %{maxGrade * 100f:F1}, sınır %{MaxGradeLimit * 100f}.");
        if (maxY - minY > MaxHeightRange) Debug.LogWarning($"Pist: yükseklik farkı {maxY - minY:F1} m, sınır {MaxHeightRange} m.");
    }

    static float CircumRadius(Vector2 a, Vector2 b, Vector2 c)
    {
        float ab = Vector2.Distance(a, b), bc = Vector2.Distance(b, c), ca = Vector2.Distance(c, a);
        float cross = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
        return cross > 1e-6f ? ab * bc * ca / (2f * cross) : float.MaxValue;
    }

    // ---------------------------------------------------------------- malzemeler ve dokular

    static Materials GetMaterials()
    {
        EnsureFolder(DataFolder);
        var asphaltTex = GetTexture("AsfaltDoku", AsphaltPixel);
        var groundTex = GetTexture("ZeminDoku", GroundPixel);
        var m = new Materials
        {
            asphalt = GetMaterial("Asfalt", new Color(0.36f, 0.36f, 0.38f), 0.3f, Color.black, asphaltTex),
            line = GetMaterial("YolCizgisi", new Color(0.85f, 0.85f, 0.82f), 0.2f, new Color(0.22f, 0.22f, 0.21f), null),
            barrier = GetMaterial("Bariyer", new Color(0.42f, 0.42f, 0.44f), 0.1f, Color.black, null),
            reflector = GetMaterial("Reflektor", new Color(0.9f, 0.45f, 0.08f), 0.5f, new Color(1.1f, 0.5f, 0.08f), null),
            ground = GetMaterial("Zemin", new Color(1f, 1f, 0.95f), 0.05f, Color.black, groundTex),
            checkerBlack = GetMaterial("DamaSiyah", new Color(0.03f, 0.03f, 0.03f), 0.2f, Color.black, null),
            checkerWhite = GetMaterial("DamaBeyaz", new Color(0.9f, 0.9f, 0.9f), 0.2f, new Color(0.3f, 0.3f, 0.3f), null),
            pole = GetMaterial("Direk", new Color(0.12f, 0.12f, 0.13f), 0.4f, Color.black, null),
            lampHead = GetMaterial("LambaBasi", new Color(1f, 0.85f, 0.6f), 0.5f, new Color(2.5f, 1.8f, 1.0f), null),
            endWall = GetMaterial("UcDuvari", new Color(0.6f, 0.12f, 0.08f), 0.1f, new Color(0.15f, 0.02f, 0.01f), null),
        };

        string physicsPath = DataFolder + "/BariyerFizik.asset";
        m.barrierPhysics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsPath);
        if (m.barrierPhysics == null)
        {
            m.barrierPhysics = new PhysicsMaterial("BariyerFizik");
            AssetDatabase.CreateAsset(m.barrierPhysics, physicsPath);
        }
        // Bariyere sürten araba takılmasın, kaysın
        m.barrierPhysics.dynamicFriction = 0.05f;
        m.barrierPhysics.staticFriction = 0.05f;
        m.barrierPhysics.bounciness = 0.1f;
        m.barrierPhysics.frictionCombine = PhysicsMaterialCombine.Minimum;
        m.barrierPhysics.bounceCombine = PhysicsMaterialCombine.Average;
        EditorUtility.SetDirty(m.barrierPhysics);
        AssetDatabase.SaveAssets();
        return m;
    }

    static Material GetMaterial(string name, Color color, float smoothness, Color emission, Texture2D texture)
    {
        string path = DataFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", 0f);
        material.SetTexture("_BaseMap", texture);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    delegate Color PixelFunc(int x, int y, System.Random random);

    // Basit gürültü dokusu (yoksa üretilir ve PNG olarak kaydedilir)
    static Texture2D GetTexture(string name, PixelFunc pixel)
    {
        string path = DataFolder + "/" + name + ".png";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture != null) return texture;

        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        var random = new System.Random(1234);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, pixel(x, y, random));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.anisoLevel = 4;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Döşemeli Perlin gürültüsü (256 piksellik dokuda kenarlar birleşsin diye periyodik örnekleme)
    static float TiledNoise(int x, int y, float frequency)
    {
        float u = x / 256f, v = y / 256f;
        float a = Mathf.PerlinNoise(u * frequency, v * frequency);
        float b = Mathf.PerlinNoise((u - 1f) * frequency, v * frequency);
        float c = Mathf.PerlinNoise(u * frequency, (v - 1f) * frequency);
        float d = Mathf.PerlinNoise((u - 1f) * frequency, (v - 1f) * frequency);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    static Color AsphaltPixel(int x, int y, System.Random random)
    {
        // Koyu, ince taneli asfalt; ara sıra açık çakıl
        float grain = (float)random.NextDouble();
        float patch = TiledNoise(x, y, 6f);
        float v = 0.22f + patch * 0.08f + (grain - 0.5f) * 0.12f;
        if (grain > 0.985f) v += 0.15f;
        return new Color(v, v, v * 1.03f);
    }

    static Color GroundPixel(int x, int y, System.Random random)
    {
        // Biçilmiş tarla: koyu toprak ve kuru ot lekeleri, hafif sıra izleri
        float patch = TiledNoise(x, y, 5f);
        float fine = TiledNoise(x, y, 24f);
        float rows = 0.5f + 0.5f * Mathf.Sin(x / 256f * Mathf.PI * 2f * 16f);
        float grain = (float)random.NextDouble();
        Color soil = new Color(0.16f, 0.12f, 0.08f);
        Color grass = new Color(0.20f, 0.24f, 0.10f);
        Color straw = new Color(0.36f, 0.31f, 0.16f);
        Color c = Color.Lerp(soil, grass, Mathf.SmoothStep(0.3f, 0.7f, patch));
        c = Color.Lerp(c, straw, rows * fine * 0.6f);
        return c * (0.85f + grain * 0.3f);
    }

    // ---------------------------------------------------------------- dış haritalar (Antigravity)

    const string DagFolder = "Assets/Maps/DagTirmanisi";
    const string DagScenePath = "Assets/Scenes/dag.unity";

    [System.Serializable]
    class MapCenterline
    {
        public float genislik = 14f;
        public float baslangic_m = 75f;
        public float bitis_m = -1f;
    }

    /// <summary>
    /// Dağ Tırmanışı: race sahnesinin kopyası (ışık, sis, LethalLook, ağ objeleri aynen), pist geometrisi yerine
    /// Antigravity'nin Dag_Yol.fbx'i, orta çizgi Dag_Merkez.json'dan. Yeniden çalıştırılabilir.
    /// </summary>
    [MenuItem("Harvester/Harita/Dağ Tırmanışı Sahnesini Kur")]
    public static void BuildMountain()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string jsonPath = DagFolder + "/Dag_Merkez.json";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(DagFolder + "/Dag_Yol.fbx");
        if (!File.Exists(jsonPath) || model == null) { Debug.LogError("[Harita] Dag_Merkez.json ya da Dag_Yol.fbx yok"); return; }

        // JSON: {"genislik":14,"noktalar":[[x,y,z],...],"baslangic_m":75,"bitis_m":2453}. JsonUtility iç içe dizi
        // okuyamadığı için noktalar düz sayı listesine çevrilir.
        string text = File.ReadAllText(jsonPath);
        int open = text.IndexOf('[', text.IndexOf("\"noktalar\""));
        int depth = 0, close = open;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '[') depth++;
            else if (text[i] == ']' && --depth == 0) { close = i; break; }
        }
        string flat = text.Substring(open, close - open + 1).Replace("[", "").Replace("]", "");
        string meta = text.Remove(open, close - open + 1).Replace("\"noktalar\":,", "").Replace(",\"noktalar\":", "").Replace("\"noktalar\":", "");
        var info = JsonUtility.FromJson<MapCenterline>(meta) ?? new MapCenterline();
        var points = new List<Vector3>();
        var parts = flat.Split(',');
        for (int i = 0; i + 2 < parts.Length; i += 3)
            points.Add(new Vector3(float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture),
                                   float.Parse(parts[i + 1], System.Globalization.CultureInfo.InvariantCulture),
                                   float.Parse(parts[i + 2], System.Globalization.CultureInfo.InvariantCulture)));
        if (points.Count < 10) { Debug.LogError("[Harita] Orta çizgide yeterli nokta yok: " + points.Count); return; }

        if (!File.Exists(DagScenePath)) AssetDatabase.CopyAsset(ScenePath, DagScenePath);
        var scene = EditorSceneManager.OpenScene(DagScenePath, OpenSceneMode.Single);

        var track = Object.FindFirstObjectByType<RaceTrack>();
        if (track == null) { Debug.LogError("[Harita] Sahnede RaceTrack yok"); return; }
        var root = track.transform;
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var source = track.GetComponent<RaceTrackSource>();
        if (source != null) Object.DestroyImmediate(source); // "Rebuild Race Track" bu sahneyi düzenden yeniden kurmasın
        foreach (var n in new[] { "Uretilen", "Harita" })
        {
            var old = root.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        track.SetCenterline(points, false, info.genislik > 1f ? info.genislik : 14f);
        var so = new SerializedObject(track);
        so.FindProperty("startDistance").floatValue = Mathf.Max(60f, info.baslangic_m);
        if (info.bitis_m > 0f) so.FindProperty("finishMargin").floatValue = Mathf.Max(10f, track.Length - info.bitis_m);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(track);

        // Görsel + çarpışma: yol, bariyer, duvar, tünel, kemer ve arazi çarpışır; yol çizgileri yalnız görsel.
        var map = (GameObject)PrefabUtility.InstantiatePrefab(model);
        map.name = "Harita";
        map.transform.SetParent(root, false);
        var materials = GetMaterials();
        int colliders = 0;
        foreach (var mf in map.GetComponentsInChildren<MeshFilter>(true))
        {
            string n = mf.gameObject.name;
            if (n.StartsWith("Yol_Cizgi")) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            if (n.StartsWith("Bariyer") || n.Contains("Duvar") || n.StartsWith("Tunel")) mc.sharedMaterial = materials.barrierPhysics;
            colliders++;
            GameObjectUtility.SetStaticEditorFlags(mf.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            var r = mf.GetComponent<MeshRenderer>();
            if (r != null) r.shadowCastingMode = n.StartsWith("Arazi") ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }

        // Diziliş ve çizgiler: turlu pistle aynı işaretler, orta çizgiye göre
        var generated = new GameObject("Uretilen").transform;
        generated.SetParent(root, false);
        var frames = BuildFrames(points, false);
        BuildMarkers(track, frames, false, track.Width, generated, materials);

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(x => x.path == DagScenePath)) { scenes.Add(new EditorBuildSettingsScene(DagScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Harita] Dağ Tırmanışı kuruldu: " + points.Count + " nokta, " + track.Length.ToString("F0") + " m, başlangıç " +
                  track.StartDistance.ToString("F0") + " m, bitiş " + track.FinishDistance.ToString("F0") + " m, çarpışan mesh " + colliders);
    }
}
