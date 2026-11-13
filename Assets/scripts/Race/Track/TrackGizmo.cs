using UnityEngine;

namespace Harvester.Race
{
    /// <summary>Sadece sahne görünümünde çizilen işaret (ör. harvester'ın başlangıç yeri). Oyunda görünmez.</summary>
    public class TrackGizmo : MonoBehaviour
    {
        public Color color = new Color(1f, 0.55f, 0.1f);
        public Vector3 size = new Vector3(6f, 4f, 10f);
        public string label = "";

        private void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * size.y * 0.5f, size);
            // İleri yönü gösteren ok
            Vector3 tip = Vector3.forward * (size.z * 0.5f + 3f) + Vector3.up * 0.5f;
            Vector3 root = Vector3.forward * size.z * 0.5f + Vector3.up * 0.5f;
            Gizmos.DrawLine(root, tip);
            Gizmos.DrawLine(tip, tip + new Vector3(-1f, 0f, -1.2f));
            Gizmos.DrawLine(tip, tip + new Vector3(1f, 0f, -1.2f));
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(label))
                UnityEditor.Handles.Label(transform.position + Vector3.up * (size.y + 1f), label);
#endif
        }
    }
}
