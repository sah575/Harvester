using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Harvester'ın biçme tablası. Makara dönerken tablanın altına giren mısır hücrelerini biçer.
// Hesabı sürücü yapar (hareket de onda), sonuç CropSync ile herkese gider.
[RequireComponent(typeof(frankMovement))]
public class CropCutter : NetworkBehaviour
{
    [SerializeField] private float interval = 0.1f;
    [SerializeField] private float minReelSpeed = 40f; // derece/sn; makara bundan yavaşsa biçmez
    [SerializeField] private float padding = 0.5f; // Tabla dikdörtgenini bu kadar genişlet

    private frankMovement harvester;
    private Vector2 headerMin, headerMax; // Tablanın Harvester'a göre yerel XZ dikdörtgeni
    private float timer;
    private readonly List<ushort> cells = new List<ushort>();

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
        foreach (var filter in harvester.spinns.transform.parent.GetComponentsInChildren<MeshFilter>())
        {
            Bounds b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                headerMin = Vector2.Min(headerMin, new Vector2(local.x, local.z));
                headerMax = Vector2.Max(headerMax, new Vector2(local.x, local.z));
            }
        }
        headerMin -= Vector2.one * padding;
        headerMax += Vector2.one * padding;
    }

    private void Update()
    {
        var field = CropField.Current;
        if (!IsOwner || field == null || CropSync.Instance == null) return;

        timer += Time.deltaTime;
        if (timer < interval) return;
        timer = 0f;

        if (harvester.ReelSpeed < minReelSpeed || !GameManager.InputAllowed) return;

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
}
