using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Çit duvarları normalde kimseyi dışarı bırakmaz. Bu, fizik açıklarına karşı yedek:
// tarladan taşan ya da zeminin altına düşen oyuncuyu içeri ışınlar.
// Hareket owner-authority olduğu için kontrolü sahibi olan client yapar.
// Spawn'da da sunucu, karakteri sahibine bununla spawn noktasına yerleştirtir.
public class BoundaryGuard : NetworkBehaviour
{
    [SerializeField] private float margin = 1f; // Pivotun sınıra bu kadar yaklaşması "dışarı taştı" sayılır
    [SerializeField] private float fallLimit = 20f; // Zeminin bu kadar altı "düştü" sayılır

    private Rigidbody rb;
    private NetworkTransform networkTransform;
    private float activeAfter;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        networkTransform = GetComponent<NetworkTransform>();
    }

    public override void OnNetworkSpawn()
    {
        // Spawn anında Rigidbody bir süre prefabın varsayılan konumunda kalabiliyor; yanlış ışınlamasın
        activeAfter = Time.time + 1f;
    }

    // SpamChars spawn ettikten sonra çağırır. Sahibinin client'ında Rigidbody prefabın varsayılan
    // konumunda kalabiliyor (Rigidbody interpolasyonu transform'u eziyor) ve owner-authority olduğu için
    // o konum herkese yayılıyor. Spawn noktasını sahibine açıkça gönderip oraya ışınlatıyoruz.
    [Rpc(SendTo.Owner)]
    public void PlaceAtSpawnRpc(Vector3 position, Quaternion rotation)
    {
        Teleport(position, rotation);
        activeAfter = Time.time + 1f;
    }

    private void Teleport(Vector3 position, Quaternion rotation)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        networkTransform.Teleport(position, rotation, transform.localScale);
    }

    private void FixedUpdate()
    {
        var field = FieldBounds.Current;
        if (!IsOwner || !IsSpawned || field == null || Time.time < activeAfter) return;

        Vector3 position = transform.position;
        bool fell = position.y < field.Center.y - fallLimit;
        if (!fell && field.Contains(position, margin)) return;

        Vector3 target = fell
            ? field.Center + Vector3.up * 3f
            : field.ClampInside(position, margin + 2f);

        Teleport(target, rb.rotation);
    }
}
