using UnityEngine;

// Çitle çevrili tarlanın sınırı. Merkez bu objenin pozisyonu, boyut XZ düzleminde.
// Objenin rotasyonu ve ölçeği olmamalı (sınır hesapları world eksenlerine göre).
public class FieldBounds : MonoBehaviour
{
    public static FieldBounds Current { get; private set; }

    [SerializeField] private Vector2 size = new Vector2(100f, 100f);

    public Vector2 Size => size;
    public Vector3 Center => transform.position;

    private void OnEnable()
    {
        Current = this;
    }

    private void OnDisable()
    {
        if (Current == this) Current = null;
    }

    public bool Contains(Vector3 point, float margin = 0f)
    {
        Vector3 local = point - Center;
        return Mathf.Abs(local.x) <= size.x * 0.5f - margin
            && Mathf.Abs(local.z) <= size.y * 0.5f - margin;
    }

    public Vector3 ClampInside(Vector3 point, float margin = 0f)
    {
        float halfX = size.x * 0.5f - margin;
        float halfZ = size.y * 0.5f - margin;
        return new Vector3(
            Mathf.Clamp(point.x, Center.x - halfX, Center.x + halfX),
            point.y,
            Mathf.Clamp(point.z, Center.z - halfZ, Center.z + halfZ));
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(Center + Vector3.up, new Vector3(size.x, 2f, size.y));
    }
}
