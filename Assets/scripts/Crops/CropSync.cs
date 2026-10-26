using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Biçilen hücrelerin ağ senkronu. Harvester'ı süren oyuncu biçtiği hücreleri kendi ekranında hemen uygular
// ve sunucuya gönderir; sunucu doğrulayıp diğer herkese iletir. Her pakette sadece yeni biçilen hücreler gider.
[RequireComponent(typeof(CropField))]
public class CropSync : NetworkBehaviour
{
    public static CropSync Instance { get; private set; }

    [SerializeField] private float maxCutDistance = 30f; // Hücre, sunucunun gördüğü Harvester'a bundan uzaksa reddedilir

    private CropField field;
    private readonly List<ushort> accepted = new List<ushort>();

    private void Awake()
    {
        field = GetComponent<CropField>();
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    // Harvester'ın sahibi çağırır
    public void SubmitCuts(List<ushort> cells)
    {
        foreach (ushort cell in cells) field.Cut(cell);

        var array = cells.ToArray();
        if (IsServer) BroadcastCutsRpc(array);
        else SubmitCutsRpc(array);
    }

    [Rpc(SendTo.Server)]
    private void SubmitCutsRpc(ushort[] cells, RpcParams rpcParams = default)
    {
        var gm = GameManager.Instance;
        ulong sender = rpcParams.Receive.SenderClientId;
        if (gm == null || gm.Phase != MatchPhase.Playing || sender != gm.HarvesterClientId) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(sender, out var client) || client.PlayerObject == null) return;

        Vector3 harvester = client.PlayerObject.transform.position;
        float maxSqr = maxCutDistance * maxCutDistance;
        accepted.Clear();
        foreach (ushort cell in cells)
        {
            Vector3 offset = field.CellCenter(cell) - harvester;
            offset.y = 0f;
            if (offset.sqrMagnitude > maxSqr) continue;
            field.Cut(cell);
            accepted.Add(cell);
        }

        if (accepted.Count > 0) BroadcastCutsRpc(accepted.ToArray());
    }

    // Sürücü de alır; onda zaten biçili olduğu için etkisiz
    [Rpc(SendTo.NotServer)]
    private void BroadcastCutsRpc(ushort[] cells)
    {
        foreach (ushort cell in cells) field.Cut(cell);
    }
}
