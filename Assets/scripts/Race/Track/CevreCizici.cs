using UnityEngine;
using UnityEngine.Rendering;

namespace Harvester.Race
{
    /// <summary>
    /// CevreYerlesimi'ni GPU instancing ile çizer: görüş alanında ve uzaklık sınırındaki hücreler, yakınsa LOD0 değilse LOD1.
    /// GameObject yok, çarpışma yok (nesneler bariyerin dışında, araba ulaşamaz). Bütün kameralarda görünür (dikiz aynası dahil);
    /// hücre seçimi ana kameraya göre yapılır.
    /// </summary>
    [ExecuteAlways]
    public class CevreCizici : MonoBehaviour
    {
        public CevreYerlesimi data;
        [Tooltip("Uzaklık sınırlarının çarpanı (kalite ayarı için)")]
        [Range(0.3f, 2f)] public float distanceScale = 1f;

        readonly Plane[] planes = new Plane[6];

        /// <summary>Son karede çizilen örnek sayısı (ölçüm için).</summary>
        public int LastDrawn { get; private set; }

        void Update()
        {
            LastDrawn = 0;
            if (data == null || data.cells.Count == 0) return;
            Camera cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null) cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 eye = cam.transform.position;

            for (int i = 0; i < data.cells.Count; i++)
            {
                var cell = data.cells[i];
                var model = data.models[cell.model];
                float max = model.drawDistance * distanceScale;
                float d = Mathf.Sqrt(cell.bounds.SqrDistance(eye));
                if (d > max) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, cell.bounds)) continue;
                Mesh mesh = d < model.lodDistance * distanceScale || model.lod1 == null ? model.lod0 : model.lod1;
                if (mesh == null) continue;
                int subMeshes = Mathf.Min(mesh.subMeshCount, model.materials.Length);
                for (int s = 0; s < subMeshes; s++)
                {
                    if (model.materials[s] == null) continue;
                    // Kurucuyla oluştur: varsayılanlar (render katmanı maskesi vb.) dolsun; sıfır maske URP'de çizimi eliyor
                    var rp = new RenderParams(model.materials[s]);
                    rp.layer = gameObject.layer;
                    rp.shadowCastingMode = model.castShadows && d < model.lodDistance * distanceScale * 1.5f ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    rp.receiveShadows = true;
                    rp.worldBounds = cell.bounds;
                    Graphics.RenderMeshInstanced(rp, mesh, s, cell.matrices);
                }
                LastDrawn += cell.matrices.Length;
            }
        }
    }
}
