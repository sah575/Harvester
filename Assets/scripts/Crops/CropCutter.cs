using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Harvester'ın biçme tablası. Makara dönerken tablanın altına giren mısır hücrelerini biçer
// ve tablaya giren robotları öldürür. Hesabı sürücü yapar (hareket de onda):
// biçilen hücreler CropSync ile, öldürme istekleri bu sınıfın RPC'siyle sunucuya gider.
[RequireComponent(typeof(frankMovement))]
public class CropCutter : NetworkBehaviour
{
    [SerializeField] private float interval = 0.1f;
    [SerializeField] private float minReelSpeed = 40f; // derece/sn; makara bundan yavaşsa biçmez
    [SerializeField] private float padding = 0.5f; // Tabla dikdörtgenini bu kadar genişlet

    [Header("Öldürme")]
    [SerializeField] private float killHeight = 1.2f; // Ayakları tablanın altından bu kadar yukarıdaki (zıplayan) robot kurtulur
    [SerializeField] private float serverKillTolerance = 3f; // Sunucu, gecikme payıyla robotu tablaya bu kadar yakın görmeli
    [SerializeField] private float killRetryDelay = 0.5f; // Aynı robot için isteği bundan sık gönderme

    private frankMovement harvester;
    private Vector2 headerMin, headerMax; // Tablanın Harvester'a göre yerel XZ dikdörtgeni
    private float headerBottom; // Tablanın yerel en alt noktası (Y)
    private float timer;
    private readonly List<ushort> cells = new List<ushort>();
    private readonly Dictionary<ulong, float> killRequestTimes = new Dictionary<ulong, float>();

    private void Awake()
    {
        harvester = GetComponent<frankMovement>();
        ComputeHeaderRect();
    }

    // Makaranın ve tablanın (Front altındaki mesh'ler) yerel sınırları
    private void ComputeHeaderRect()
    {
        headerMin = new Vector2(float.MaxValue, float.MaxValue);
        headerMax = new Vector2(float.MinValue, float.MinValue);
        headerBottom = float.MaxValue;
        foreach (var filter in harvester.spinns.transform.parent.GetComponentsInChildren<MeshFilter>())
        {
            Bounds b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                headerMin = Vector2.Min(headerMin, new Vector2(local.x, local.z));
                headerMax = Vector2.Max(headerMax, new Vector2(local.x, local.z));
                headerBottom = Mathf.Min(headerBottom, local.y);
            }
        }
        headerMin -= Vector2.one * padding;
        headerMax += Vector2.one * padding;
    }

    private void Update()
    {
        if (!IsOwner) return;

        bool reelActive = harvester.ReelSpeed >= minReelSpeed && GameManager.InputAllowed;
        // Robot kontrolü her karede: yüksek hızda tabla 0.1 sn'de robotun içinden geçebilir
        if (reelActive) CheckRobots();

        var field = CropField.Current;
        if (field == null || CropSync.Instance == null) return;

        timer += Time.deltaTime;
        if (timer < interval) return;
        timer = 0f;

        if (!reelActive) return;

        // Tabla dikdörtgeninin dünya AABB'si → aday hücreler → gerçekten dikdörtgenin içinde olanlar
        Bounds area = new Bounds(transform.TransformPoint(new Vector3(headerMin.x, 0f, headerMin.y)), Vector3.zero);
        area.Encapsulate(transform.TransformPoint(new Vector3(headerMax.x, 0f, headerMin.y)));
        area.Encapsulate(transform.TransformPoint(new Vector3(headerMin.x, 0f, headerMax.y)));
        area.Encapsulate(transform.TransformPoint(new Vector3(headerMax.x, 0f, headerMax.y)));
        if (!field.GetCellRange(area, out int x0, out int z0, out int x1, out int z1)) return;

        cells.Clear();
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int index = field.CellIndex(x, z);
                if (!field.IsStanding(index)) continue;
                Vector3 local = transform.InverseTransformPoint(field.CellCenter(index));
                if (local.x >= headerMin.x && local.x <= headerMax.x && local.z >= headerMin.y && local.z <= headerMax.y)
                {
                    cells.Add((ushort)index);
                }
            }
        }

        if (cells.Count > 0) CropSync.Instance.SubmitCuts(cells);
    }

    #region Öldürme

    private bool InsideHeader(Vector3 localPoint, float margin)
    {
        return localPoint.x >= headerMin.x - margin && localPoint.x <= headerMax.x + margin
            && localPoint.z >= headerMin.y - margin && localPoint.z <= headerMax.y + margin;
    }

    // Sürücü tarafı: tablaya giren robotu bul, sunucuya bildir
    private void CheckRobots()
    {
        float scale = transform.lossyScale.x; // Yerel dikdörtgen ölçekli birimlerde
        float lethalTop = transform.TransformPoint(new Vector3(0f, headerBottom, 0f)).y + killHeight;

        foreach (var robot in CharMovement.All)
        {
            if (robot.IsDead || !robot.TryGetBounds(out Bounds bounds)) continue;
            if (bounds.min.y > lethalTop) continue; // Tablanın üstünden zıpladı

            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) / scale;
            if (!InsideHeader(transform.InverseTransformPoint(bounds.center), radius)) continue;

            ulong robotId = robot.OwnerClientId;
            if (killRequestTimes.TryGetValue(robotId, out float last) && Time.time - last < killRetryDelay) continue;
            killRequestTimes[robotId] = Time.time;

            if (IsServer) TryKill(robotId, OwnerClientId);
            else KillRobotRpc(robotId);
        }
    }

    [Rpc(SendTo.Server)]
    private void KillRobotRpc(ulong robotId, RpcParams rpcParams = default)
    {
        TryKill(robotId, rpcParams.Receive.SenderClientId);
    }

    // Sunucu tarafı: isteği kendi gördüğü durumla doğrula
    private void TryKill(ulong robotId, ulong sender)
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.Phase != MatchPhase.Playing) return;
        if (sender != OwnerClientId || sender != gm.HarvesterClientId) return;
        if (harvester.ReelSpeed < minReelSpeed * 0.5f) return; // Ağdan gelen makara hızı biraz gecikmeli

        var robotObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(robotId);
        if (robotObject == null || !robotObject.TryGetComponent(out CharMovement robot) || robot.IsDead) return;

        float tolerance = serverKillTolerance / transform.lossyScale.x;
        if (!InsideHeader(transform.InverseTransformPoint(robot.transform.position), tolerance)) return;

        robot.Kill();
        gm.ReportRobotDeath(robotId);
    }

    #endregion
}
