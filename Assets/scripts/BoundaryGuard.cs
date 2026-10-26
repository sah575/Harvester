using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Çit duvarları normalde kimseyi dışarı bırakmaz. Bu, fizik açıklarına karşı yedek:
// tarladan taşan ya da zeminin altına düşen oyuncuyu içeri ışınlar.
// Hareket owner-authority olduğu için kontrolü sahibi olan client yapar.
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

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = target;
        networkTransform.Teleport(target, rb.rotation, transform.localScale);
    }
}
