using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

// Mısır tarlasının verisi ve çizimi. Ağdan bağımsızdır: biçme kararlarını CropSync uygular.
//
// Performans: tarla cellSize'lık hücrelere bölünür, her hücrede bir mısır öbeği (aynı mesh'in instance'ı).
// GameObject yok. Hücreler chunk'lara gruplanır; her kamera kendi pozisyonuna göre chunk'ları
// mesafeye göre LOD'lara ayırıp GPU instancing ile birkaç draw call'da çizer. Sisin ötesi hiç çizilmez.
// Biçilen hücre sadece kendi chunk'ını kirletir, o chunk bir sonraki çizimde yeniden toplanır.
[ExecuteAlways]
[RequireComponent(typeof(FieldBounds))]
public class CropField : MonoBehaviour
{
    public static CropField Current { get; private set; }

    const int MaxInstancesPerDraw = 1023;
    const int MaxBenders = 4;

    [Header("Izgara")]
    [SerializeField] private float cellSize = 2f;
    [SerializeField] private float headlandWidth = 6f; // Çit boyunca ekilmemiş şerit
    [SerializeField] private int chunkCells = 8;
    [SerializeField] private int seed = 1234;

    [Header("Görünüm")]
    [SerializeField] private Material cornMaterial;
    [SerializeField] private Material stubbleMaterial;
    [SerializeField] private float lod0Distance = 30f;
    [SerializeField] private float lod1Distance = 80f;
    [SerializeField] private float drawDistance = 110f; // Sis bundan ötesini zaten gizliyor
    [SerializeField] private float stubbleDistance = 60f;
    [SerializeField] private float shadowDistance = 15f; // Sadece bu kadar yakındaki öbekler gölge atar (en pahalı kısım)
    [SerializeField] private float frustumMargin = 6f; // Kenardaki öbeklerin gölgesi/eğilmesi kesilmesin diye
    [SerializeField] private float textureCameraDistanceScale = 0.35f; // Ayna gibi render texture kameraları daha az çizsin

    static readonly ProfilerMarker DrawMarker = new ProfilerMarker("CropField.Draw");
    static readonly int BendersId = Shader.PropertyToID("_CornBenders");

    class Chunk
    {
        public Bounds bounds;
        public int[] cells;
        public Matrix4x4[] standing;
        public int standingCount;
        public Matrix4x4[] stubble;
        public int stubbleCount;
        public bool dirty = true;
    }

    private FieldBounds field;
    private int cellsX, cellsZ;
    private Vector3 origin; // Izgaranın min köşesi
    private bool[] planted;
    private bool[] cut;
    private Matrix4x4[] standingMatrices;
    private Matrix4x4[] stubbleMatrices;
    private Chunk[] chunks;
    private int chunksX;
    private Mesh[] lodMeshes;
    private Mesh stubbleMesh;
    private Matrix4x4[][] lodBuffers; // 0: yakın + gölgeli, 1: yakın, 2: orta, 3: uzak
    private int[] lodCounts;
    private readonly Plane[] frustumPlanes = new Plane[6];
    private Matrix4x4[] stubbleBuffer;
    private Bounds worldBounds;
    private bool needsRebuild = true;
    private readonly Vector4[] benderData = new Vector4[MaxBenders];

    public int CellCount => cellsX * cellsZ;
    public int PlantedCount { get; private set; }
    public int CutCount { get; private set; }
    public float CutRatio => PlantedCount == 0 ? 0f : (float)CutCount / PlantedCount;
    public float CellSize => cellSize;

    private void OnEnable()
    {
        Current = this;
        needsRebuild = true;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        if (Current == this) Current = null;
        DestroyMeshes();
    }

    private void OnValidate()
    {
        needsRebuild = true;
    }

    private void Update()
    {
        UpdateBenders();
    }

    #region Izgara

    private void Build()
    {
        needsRebuild = false;
        field = GetComponent<FieldBounds>();
        cellsX = Mathf.Max(1, Mathf.FloorToInt(field.Size.x / cellSize));
        cellsZ = Mathf.Max(1, Mathf.FloorToInt(field.Size.y / cellSize));
        origin = field.Center - new Vector3(cellsX * cellSize * 0.5f, 0f, cellsZ * cellSize * 0.5f);

        int count = cellsX * cellsZ;
        if (count > ushort.MaxValue) Debug.LogWarning($"CropField: {count} hücre ağ senkronu için fazla (en fazla {ushort.MaxValue}). cellSize'ı büyüt.");

        planted = new bool[count];
        cut = new bool[count];
        standingMatrices = new Matrix4x4[count];
        stubbleMatrices = new Matrix4x4[count];
        PlantedCount = 0;
        CutCount = 0;

        // Aynı seed her client'ta aynı tarlayı üretir; öbek pozisyonlarını senkronlamaya gerek yok
        var rng = new System.Random(seed);
        var terrain = Terrain.activeTerrain;
        for (int z = 0; z < cellsZ; z++)
        {
            for (int x = 0; x < cellsX; x++)
            {
                int index = z * cellsX + x;
                Vector3 center = CellCenter(index);
                Vector3 jitter = new Vector3(Next(rng, -0.3f, 0.3f), 0f, Next(rng, -0.3f, 0.3f)) * cellSize;
                Quaternion rotation = Quaternion.Euler(0f, Next(rng, 0f, 360f), 0f);
                float width = Next(rng, 0.9f, 1.1f);
                float height = Next(rng, 0.9f, 1.15f);

                Vector3 position = center + jitter;
                if (terrain != null) position.y = terrain.SampleHeight(position) + terrain.GetPosition().y;

                planted[index] = field.Contains(center, headlandWidth);
                if (planted[index]) PlantedCount++;
                standingMatrices[index] = Matrix4x4.TRS(position, rotation, new Vector3(width, height, width));
                stubbleMatrices[index] = Matrix4x4.TRS(position, rotation, Vector3.one * width);
            }
        }

        BuildChunks();
        BuildMeshes();

        lodBuffers = new Matrix4x4[4][];
        for (int i = 0; i < 4; i++) lodBuffers[i] = new Matrix4x4[count];
        lodCounts = new int[4];
        stubbleBuffer = new Matrix4x4[count];

        worldBounds = new Bounds(field.Center + Vector3.up * CornMeshBuilder.StalkHeight * 0.5f,
            new Vector3(field.Size.x + 10f, CornMeshBuilder.StalkHeight * 2f, field.Size.y + 10f));
    }

    private void BuildChunks()
    {
        chunksX = Mathf.CeilToInt((float)cellsX / chunkCells);
        int chunksZ = Mathf.CeilToInt((float)cellsZ / chunkCells);
        chunks = new Chunk[chunksX * chunksZ];

        var cells = new List<int>();
        for (int cz = 0; cz < chunksZ; cz++)
        {
            for (int cx = 0; cx < chunksX; cx++)
            {
                cells.Clear();
                for (int z = cz * chunkCells; z < Mathf.Min(cellsZ, (cz + 1) * chunkCells); z++)
                {
                    for (int x = cx * chunkCells; x < Mathf.Min(cellsX, (cx + 1) * chunkCells); x++)
                    {
                        int index = z * cellsX + x;
                        if (planted[index]) cells.Add(index);
                    }
                }

                Vector3 min = origin + new Vector3(cx * chunkCells * cellSize, 0f, cz * chunkCells * cellSize);
                Vector3 size = new Vector3(chunkCells * cellSize, CornMeshBuilder.StalkHeight, chunkCells * cellSize);
                chunks[cz * chunksX + cx] = new Chunk
                {
                    bounds = new Bounds(min + size * 0.5f, size),
                    cells = cells.ToArray(),
                    standing = new Matrix4x4[cells.Count],
                    stubble = new Matrix4x4[cells.Count],
                };
            }
        }
    }

    private void RebuildChunk(Chunk chunk)
    {
        chunk.standingCount = 0;
        chunk.stubbleCount = 0;
        foreach (int index in chunk.cells)
        {
            if (cut[index]) chunk.stubble[chunk.stubbleCount++] = stubbleMatrices[index];
            else chunk.standing[chunk.standingCount++] = standingMatrices[index];
        }
        chunk.dirty = false;
    }

    public Vector3 CellCenter(int index)
    {
        int x = index % cellsX;
        int z = index / cellsX;
        return origin + new Vector3((x + 0.5f) * cellSize, 0f, (z + 0.5f) * cellSize);
    }

    public bool IsStanding(int index)
    {
        return index >= 0 && index < planted.Length && planted[index] && !cut[index];
    }

    // Dünya AABB'sinin kapsadığı hücre aralığı (dahil)
    public bool GetCellRange(Bounds area, out int x0, out int z0, out int x1, out int z1)
    {
        EnsureBuilt();
        x0 = Mathf.Max(0, Mathf.FloorToInt((area.min.x - origin.x) / cellSize));
        z0 = Mathf.Max(0, Mathf.FloorToInt((area.min.z - origin.z) / cellSize));
        x1 = Mathf.Min(cellsX - 1, Mathf.FloorToInt((area.max.x - origin.x) / cellSize));
        z1 = Mathf.Min(cellsZ - 1, Mathf.FloorToInt((area.max.z - origin.z) / cellSize));
        return x0 <= x1 && z0 <= z1;
    }

    public int CellIndex(int x, int z) => z * cellsX + x;

    // Hücreyi biç. Zaten biçilmişse ya da ekili değilse false.
    public bool Cut(int index)
    {
        EnsureBuilt();
        if (!IsStanding(index)) return false;
        cut[index] = true;
        CutCount++;
        int x = index % cellsX / chunkCells;
        int z = index / cellsX / chunkCells;
        chunks[z * chunksX + x].dirty = true;
        return true;
    }

    private void EnsureBuilt()
    {
        if (needsRebuild || planted == null) Build();
    }

    #endregion

    #region Çizim

    private void BuildMeshes()
    {
        DestroyMeshes();
        lodMeshes = new[] { CornMeshBuilder.BuildClump(0), CornMeshBuilder.BuildClump(1), CornMeshBuilder.BuildClump(2) };
        stubbleMesh = CornMeshBuilder.BuildStubble();
    }

    private void DestroyMeshes()
    {
        if (lodMeshes != null) foreach (var mesh in lodMeshes) DestroyImmediate(mesh);
        if (stubbleMesh != null) DestroyImmediate(stubbleMesh);
        lodMeshes = null;
        stubbleMesh = null;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        if (cornMaterial == null || stubbleMaterial == null) return;

        using (DrawMarker.Auto())
        {
            EnsureBuilt();

            // Ayna gibi render texture'a çizen kameralar daha kısa mesafe çizer ve gölge atmaz
            bool secondary = cam.targetTexture != null;
            float scale = secondary ? textureCameraDistanceScale : 1f;
            float lod0 = lod0Distance * scale;
            float lod1 = lod1Distance * scale;
            float maxDistance = drawDistance * scale;
            float maxStubble = stubbleDistance * scale;

            Vector3 view = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, frustumPlanes);
            float maxShadow = secondary ? 0f : shadowDistance;
            lodCounts[0] = lodCounts[1] = lodCounts[2] = lodCounts[3] = 0;
            int stubbleCount = 0;

            foreach (var chunk in chunks)
            {
                float distance = DistanceXZ(view, chunk.bounds);
                if (distance > maxDistance) continue;

                // Kameranın görmediği chunk'ları atla (kenar payıyla, gölgeler kesik görünmesin)
                Bounds padded = chunk.bounds;
                padded.Expand(frustumMargin);
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, padded)) continue;

                if (chunk.dirty) RebuildChunk(chunk);

                int bucket = distance < maxShadow ? 0 : distance < lod0 ? 1 : distance < lod1 ? 2 : 3;
                Array.Copy(chunk.standing, 0, lodBuffers[bucket], lodCounts[bucket], chunk.standingCount);
                lodCounts[bucket] += chunk.standingCount;

                if (distance < maxStubble)
                {
                    Array.Copy(chunk.stubble, 0, stubbleBuffer, stubbleCount, chunk.stubbleCount);
                    stubbleCount += chunk.stubbleCount;
                }
            }

            Draw(cam, lodMeshes[0], cornMaterial, lodBuffers[0], lodCounts[0], true);
            Draw(cam, lodMeshes[0], cornMaterial, lodBuffers[1], lodCounts[1], false);
            Draw(cam, lodMeshes[1], cornMaterial, lodBuffers[2], lodCounts[2], false);
            Draw(cam, lodMeshes[2], cornMaterial, lodBuffers[3], lodCounts[3], false);
            Draw(cam, stubbleMesh, stubbleMaterial, stubbleBuffer, stubbleCount, false);
        }
    }

    private void Draw(Camera cam, Mesh mesh, Material material, Matrix4x4[] buffer, int count, bool castShadows)
    {
        if (count == 0) return;
        var renderParams = new RenderParams(material)
        {
            camera = cam,
            worldBounds = worldBounds,
            layer = gameObject.layer,
            shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
            receiveShadows = true,
            lightProbeUsage = LightProbeUsage.Off,
        };
        for (int start = 0; start < count; start += MaxInstancesPerDraw)
        {
            Graphics.RenderMeshInstanced(renderParams, mesh, 0, buffer, Mathf.Min(MaxInstancesPerDraw, count - start), start);
        }
    }

    private static float DistanceXZ(Vector3 point, Bounds bounds)
    {
        float dx = Mathf.Max(bounds.min.x - point.x, 0f, point.x - bounds.max.x);
        float dz = Mathf.Max(bounds.min.z - point.z, 0f, point.z - bounds.max.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // Robotlar ve Harvester mısırları iterek eğer; pozisyonlar shader'a global dizi olarak gider
    private void UpdateBenders()
    {
        int i = 0;
        foreach (var bender in CropBender.Active)
        {
            if (i >= MaxBenders) break;
            Vector3 p = bender.transform.position;
            benderData[i++] = new Vector4(p.x, p.y, p.z, bender.Radius);
        }
        for (; i < MaxBenders; i++) benderData[i] = Vector4.zero;
        Shader.SetGlobalVectorArray(BendersId, benderData);
    }

    #endregion

    private static float Next(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }
}
