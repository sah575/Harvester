using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Harvester.Race
{
    /// <summary>
    /// Arabanın görsel modelini kurar ve bir seçimi (CarLoadout) uygular. Ağ objesi değil: her makine modeli
    /// kendi başına RaceCar'ın çocuğu olarak açar. RaceCar seçim değişince Build/Apply çağırır.
    ///
    /// - Parça varyantlarını açıp kapatır (tanımdaki CarModSlot listesi).
    /// - Renkleri araba başına önbellekli malzemelerle verir; ortak asset değişmez, SRP Batcher korunur.
    /// - light_head_* noktalarına spot far koyar; stoplar yalnız emisyonla frende parlar.
    /// - Tekerlek başına bir "göbek" oluşturur: göbek direksiyonla döner ve kaliperi taşır, tekerlek (jantıyla) göbeğin içinde yuvarlanır.
    /// - Gövdeyi ve her yuvarlanan tekerleği ayrı birleşik mesh yapar; malzeme yuvaları korunur.
    ///   Parçalanmada birleşik mesh silinip asıl parçalar geri açılır.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarVisual : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Optimizasyon")]
        [Tooltip("Gövde ve tekerlek parçalarını birleştir. Model mesh'leri okunabilir (Read/Write) olmalı.")]
        [SerializeField] private bool combineStaticMeshes = true;

        [Header("Farlar (light_head_*)")]
        [SerializeField] private float headlightRange = 60f;
        [SerializeField] private float headlightSpotAngle = 75f;
        [SerializeField] private float headlightInnerAngle = 40f;
        [Tooltip("URP ışığı mesafenin karesiyle sönümlüyor: 20–40 m'de yolu aydınlatmak için yüksek olmalı")]
        [SerializeField] private float headlightIntensity = 60f;
        [SerializeField] private Color headlightColor = new Color(1f, 0.95f, 0.85f);
        [Tooltip("Farın yere bakma açısı (derece)")]
        [SerializeField] private float headlightPitch = 4f;
        [Tooltip("Işığın boş noktanın ne kadar önünde durduğu (m)")]
        [SerializeField] private float headlightForwardOffset = 0.1f;

        [Header("Stop lambaları (light_tail_*)")]
        [Tooltip("Frende stop malzemesinin parlaklık çarpanı")]
        [SerializeField] private float brakeEmissionBoost = 3f;
        [Tooltip("Bu yavaşlamanın üstü fren sayılır (m/sn²). Boşta yavaşlama 2.5, fren 22.")]
        [SerializeField] private float brakeDetectDecel = 5f;

        public CarDefinition Definition { get; private set; }
        public GameObject Model { get; private set; }
        public Transform CockpitAnchor { get; private set; }
        /// <summary>Yuvarlanan tekerlekler (ön sol, ön sağ, arka sol, arka sağ).</summary>
        public Transform[] Wheels { get; } = new Transform[4];
        /// <summary>Direksiyonla dönen göbekler; konumu süspansiyon belirler.</summary>
        public Transform[] Hubs { get; } = new Transform[4];
        public bool IsCombined => combinedObjects.Count > 0;
        public bool AnimateWheels { get; private set; } = true;

        private RaceCar car;
        private readonly Dictionary<string, Transform> parts = new Dictionary<string, Transform>();
        private readonly List<Light> headLights = new List<Light>();
        private float nextLightBudget;
        private readonly List<Renderer> combinedSources = new List<Renderer>();
        private readonly List<GameObject> combinedObjects = new List<GameObject>();
        private readonly List<Mesh> combinedMeshes = new List<Mesh>();
        private readonly Dictionary<Material, Material> materialCopies = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> originalMaterials = new Dictionary<Material, Material>();
        private Renderer[] modelRenderers;
        private ShadowCastingMode[] originalShadowModes;
        private bool geometryApplied, shadowStateKnown, distantShadows;
        private CarLoadout appliedLoadout;
        private bool braking;
        private float lastSpeed;
        private float smoothedDecel;
        private static bool warnedUnreadable;

        // Her araba boyasının tek malzeme kopyası -> boya yuvası ya da stop lambası.
        private struct PaintTarget
        {
            public Material material;
            public int paint;       // -1 = boya değil
            public bool brakeLight;
        }
        private readonly List<PaintTarget> targets = new List<PaintTarget>();

        private void Awake()
        {
            car = GetComponent<RaceCar>();
        }

        /// <summary>Tanımın modelini açar (eskisini siler). Modeli aynıysa bir şey yapmaz.</summary>
        public bool Build(CarDefinition def)
        {
            if (def == null || def.modelPrefab == null) return false;
            if (Model != null && Definition == def) return true;
            Clear();
            Definition = def;

            Model = Instantiate(def.modelPrefab, transform, false);
            Model.name = "Model";
            Model.transform.localPosition = Vector3.zero;
            Model.transform.localRotation = Quaternion.identity;
            Model.transform.localScale = Vector3.one;
            // Modelde collider varsa gövdenin ışınlarını ve çarpışmasını bozmasın
            foreach (var c in Model.GetComponentsInChildren<Collider>(true)) Destroy(c);

            foreach (var t in Model.GetComponentsInChildren<Transform>(true))
                if (!parts.ContainsKey(t.name)) parts.Add(t.name, t);

            BuildHubs(def);
            CockpitAnchor = Find(def.cockpitAnchor);
            foreach (var n in def.headlightAnchors) AddHeadlight(Find(n));
            return true;
        }

        /// <summary>Seçimi modele uygular: varyantlar, renkler, (bayrak açıksa) birleştirme.</summary>
        public void Apply(CarLoadout loadout)
        {
            var def = Definition;
            if (def == null || Model == null) return;
            bool same = geometryApplied;
            for (int s = 0; same && s < def.slots.Count && s < CarLoadout.MaxSlots; s++)
                if (appliedLoadout.GetSlot(s) != loadout.GetSlot(s)) same = false;
            if (same) { appliedLoadout = loadout; ApplyColors(); return; }
            Uncombine();
            appliedLoadout = loadout;

            for (int s = 0; s < def.slots.Count; s++)
            {
                var slot = def.slots[s];
                int chosen = s < CarLoadout.MaxSlots ? loadout.GetSlot(s) : 0;
                if (chosen >= slot.variants.Count) chosen = 0;
                for (int v = 0; v < slot.variants.Count; v++)
                {
                    var names = slot.variants[v].objectNames;
                    if (names == null) continue;
                    for (int n = 0; n < names.Length; n++)
                    {
                        var t = Find(names[n]);
                        if (t != null && t.gameObject.activeSelf != (v == chosen)) t.gameObject.SetActive(v == chosen);
                    }
                }
            }

            if (combineStaticMeshes)
            {
                CombineAt(Model.transform, true);
                for (int i = 0; i < Wheels.Length; i++) if (Wheels[i] != null) CombineAt(Wheels[i], false);
            }
            CollectTargets();
            ApplyColors();
            VisibilityBudget.MarkCarDetails(Model);
            geometryApplied = true;
            modelRenderers = Model.GetComponentsInChildren<Renderer>(true);
            originalShadowModes = new ShadowCastingMode[modelRenderers.Length];
            for (int i = 0; i < modelRenderers.Length; i++) originalShadowModes[i] = modelRenderers[i].shadowCastingMode;
            shadowStateKnown = false;
        }

        // ------------------------------------------------------------------ Model parçaları

        private Transform Find(string name)
        {
            Transform t;
            return !string.IsNullOrEmpty(name) && parts.TryGetValue(name, out t) ? t : null;
        }

        private void BuildHubs(CarDefinition def)
        {
            for (int i = 0; i < 4; i++)
            {
                Wheels[i] = Hubs[i] = null;
                var wheel = def.geometry.wheelNames != null && i < def.geometry.wheelNames.Length ? Find(def.geometry.wheelNames[i]) : null;
                if (wheel == null) continue;
                var hub = new GameObject("Gobek_" + wheel.name).transform;
                hub.SetParent(wheel.parent, false);
                hub.position = wheel.position;
                hub.localRotation = Quaternion.identity;
                // Kaliper tekerlekle dönmesin: göbeğe taşınır (direksiyonla döner, yuvarlanmaz). İki model düzeni de
                // kabul edilir: tekerleğin çocuğu (wheel-*_caliper) ya da kökte ayrı nesne (caliper-front-left gibi).
                var calipers = new List<Transform>();
                foreach (Transform child in wheel)
                    if (!string.IsNullOrEmpty(def.caliperSuffix) && child.name.EndsWith(def.caliperSuffix)) calipers.Add(child);
                var rootCaliper = def.geometry.caliperNames != null && i < def.geometry.caliperNames.Length ? Find(def.geometry.caliperNames[i]) : null;
                if (rootCaliper != null && !calipers.Contains(rootCaliper)) calipers.Add(rootCaliper);
                wheel.SetParent(hub, true);
                foreach (var c in calipers) c.SetParent(hub, true);
                Wheels[i] = wheel;
                Hubs[i] = hub;
            }
        }

        private void AddHeadlight(Transform anchor)
        {
            if (anchor == null) return;
            var go = new GameObject("Far");
            go.transform.SetParent(anchor, false);
            // Nokta far gövdesinin içinde olabilir: ışığı biraz öne al (gölge yok, ama mesh'in arkasında kalmasın)
            go.transform.localPosition = new Vector3(0f, 0f, headlightForwardOffset);
            go.transform.localRotation = Quaternion.Euler(headlightPitch, 0f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = headlightRange;
            light.spotAngle = headlightSpotAngle;
            light.innerSpotAngle = headlightInnerAngle;
            light.intensity = headlightIntensity;
            light.color = headlightColor;
            light.shadows = LightShadows.None;
            headLights.Add(light);
        }

        // Yerel araç daima tam; uzaktaki farlar 60–120 m arasında yumuşakça söner.
        static float HeadlightFactor(float distance, bool local)
        { return local ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, 120f, distance)); }

        private void UpdateLightBudget()
        {
            nextLightBudget = Time.unscaledTime + .2f;
            var observer = CarCamera.Current;
            bool local = car == RaceCar.Local || observer == null;
            float distance = local ? 0 : Vector3.Distance(transform.position, observer.transform.position);
            float factor = HeadlightFactor(distance, local);
            AnimateWheels = local || distance < 80f;
            bool far = !local && distance > 80f;
            if (modelRenderers != null && (!shadowStateKnown || distantShadows != far))
            {
                for (int i = 0; i < modelRenderers.Length; i++) if (modelRenderers[i] != null)
                    modelRenderers[i].shadowCastingMode = far ? ShadowCastingMode.Off : originalShadowModes[i];
                distantShadows = far; shadowStateKnown = true;
            }
            foreach (var light in headLights) if (light != null)
            { light.intensity = headlightIntensity * factor; light.enabled = factor > .001f; }
        }

        private bool IsUnderHub(Transform t)
        {
            for (int i = 0; i < 4; i++)
                if (Hubs[i] != null && t.IsChildOf(Hubs[i])) return true;
            return false;
        }

        // ------------------------------------------------------------------ Renk

        private Material Original(Material material)
        {
            Material original;
            return material != null && originalMaterials.TryGetValue(material, out original) ? original : material;
        }
        private Material Copy(Material original)
        {
            Material copy;
            if (!materialCopies.TryGetValue(original, out copy))
            {
                copy = new Material(original) { name = original.name + " (araç boyası)" };
                materialCopies.Add(original, copy); originalMaterials.Add(copy, original);
            }
            return copy;
        }
        private void CollectTargets()
        {
            targets.Clear(); var seen = new HashSet<Material>(); var def = Definition;
            foreach (var r in Model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    var original = Original(mats[m]); if (original == null) continue;
                    int paint = -1;
                    for (int p = 0; p < def.paints.Count; p++) if (def.paints[p].material == original) { paint = p; break; }
                    bool brake = original == def.brakeLightMaterial;
                    if (paint < 0 && !brake) continue;
                    var copy = Copy(original); mats[m] = copy; changed = true;
                    // Önceki MPB'yi temizle; artık shader verisi malzeme sabit tamponunda tutulur.
                    r.SetPropertyBlock(null, m);
                    if (seen.Add(copy)) targets.Add(new PaintTarget { material = copy, paint = paint, brakeLight = brake });
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
        private void ApplyColors()
        {
            var def = Definition;
            foreach (var t in targets)
            {
                if (t.material == null) continue;
                if (t.paint >= 0)
                {
                    var slot = def.paints[t.paint];
                    Color32 c32 = t.paint < CarLoadout.MaxPaints ? appliedLoadout.GetPaint(t.paint) : new Color32(0,0,0,0);
                    Color c = c32.a == 0 ? slot.defaultColor : (Color)c32;
                    t.material.SetColor(BaseColorId, c);
                    if (slot.emissionIntensity > 0f) t.material.SetColor(EmissionColorId, new Color(c.r,c.g,c.b,1f) * slot.emissionIntensity);
                }
                else if (t.brakeLight)
                {
                    Color e = def.brakeLightMaterial.HasProperty(EmissionColorId) ? def.brakeLightMaterial.GetColor(EmissionColorId) : Color.red;
                    t.material.SetColor(EmissionColorId, braking ? e * brakeEmissionBoost : e);
                }
            }
        }

        private void Update()
        {
            if (Model == null || car == null) return;
            if (Time.unscaledTime >= nextLightBudget) UpdateLightBudget();
            // Frenleme: ileri hızın düşüşünden (sahibinde de uzak kopyada da aynı kural)
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float speed = car.ForwardSpeed;
            float decel = (Mathf.Abs(lastSpeed) - Mathf.Abs(speed)) / dt;
            lastSpeed = speed;
            smoothedDecel = Mathf.Lerp(smoothedDecel, decel, 1f - Mathf.Exp(-dt / 0.1f));
            bool now = !car.IsEliminated && Mathf.Abs(speed) > 1f && smoothedDecel > brakeDetectDecel;
            if (now == braking) return;
            braking = now;
            ApplyBrakeColor();
        }

        private void ApplyBrakeColor()
        {
            var def = Definition;
            if (def == null || def.brakeLightMaterial == null) return;
            Color e = def.brakeLightMaterial.HasProperty(EmissionColorId) ? def.brakeLightMaterial.GetColor(EmissionColorId) : Color.red;
            foreach (var t in targets) if (t.brakeLight && t.material != null)
                t.material.SetColor(EmissionColorId, braking ? e * brakeEmissionBoost : e);
        }

        // ------------------------------------------------------------------ Birleştirme

        private void CombineAt(Transform root, bool body)
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            Matrix4x4 toModel = root.worldToLocalMatrix;
            var sources = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled || (body && IsUnderHub(r.transform))) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                if (!mesh.isReadable)
                {
                    if (!warnedUnreadable)
                        Debug.LogWarning("[Araba] " + Definition.name + ": model mesh'leri okunabilir değil (Read/Write kapalı), birleştirme yapılmadı. CarBuilder bunu açar.");
                    warnedUnreadable = true;
                    return;
                }
                var mats = r.sharedMaterials;
                Matrix4x4 m = toModel * r.localToWorldMatrix;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material mat = mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                    mat = Original(mat);
                    if (mat == null) continue;
                    List<CombineInstance> list;
                    if (!groups.TryGetValue(mat, out list))
                    {
                        list = new List<CombineInstance>();
                        groups.Add(mat, list);
                        order.Add(mat);
                    }
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m });
                }
                sources.Add(r);
            }
            if (sources.Count < 2) return;

            var finals = new CombineInstance[order.Count];
            int vertexCount = 0;
            for (int i = 0; i < order.Count; i++)
            {
                var part = new Mesh();
                part.indexFormat = IndexFormat.UInt32;
                part.CombineMeshes(groups[order[i]].ToArray(), true, true);
                vertexCount += part.vertexCount;
                finals[i] = new CombineInstance { mesh = part, transform = Matrix4x4.identity };
            }
            var combinedMesh = new Mesh { name = Definition.name + "_birlesik" };
            combinedMesh.indexFormat = vertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            combinedMesh.CombineMeshes(finals, false, false);
            for (int i = 0; i < finals.Length; i++) Destroy(finals[i].mesh);

            var combinedObject = new GameObject(body ? "Birlesik" : "TekerlekBirlesik");
            combinedObject.transform.SetParent(root, false);
            combinedObject.AddComponent<MeshFilter>().sharedMesh = combinedMesh;
            var renderer = combinedObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = order.ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            foreach (var r in sources) r.enabled = false;
            combinedObjects.Add(combinedObject); combinedMeshes.Add(combinedMesh); combinedSources.AddRange(sources);
        }

        private void Uncombine()
        {
            if (modelRenderers != null && originalShadowModes != null)
                for (int i = 0; i < modelRenderers.Length; i++) if (modelRenderers[i] != null)
                    modelRenderers[i].shadowCastingMode = originalShadowModes[i];
            // Destroy kare sonunda tamamlanır; eski mesh yeni birleştirmeye tekrar katılmasın.
            foreach (var go in combinedObjects) if (go != null) { go.SetActive(false); Destroy(go); }
            foreach (var mesh in combinedMeshes) if (mesh != null) Destroy(mesh);
            combinedObjects.Clear(); combinedMeshes.Clear();
            foreach (var r in combinedSources) if (r != null) r.enabled = true;
            combinedSources.Clear();
        }

        // ------------------------------------------------------------------ Parçalanma

        /// <summary>
        /// Modeli parçalara ayırıp fizikle saçar: tekerlekler (göbekleriyle), tanımdaki paneller (kapı, çamurluk, kaput,
        /// tampon, tavan...) ayrı uçar; kalan her şey (şasi, iç, camlar) tek ağır kasa parçası olur. Sadece görsel.
        /// </summary>
        public void Shatter(Vector3 carVelocity, float speed, float lift, float lifetime)
        {
            if (Model == null) return;
            Uncombine();
            for (int i = 0; i < headLights.Count; i++) if (headLights[i] != null) headLights[i].enabled = false;

            Vector3 center = transform.position + transform.up * 0.5f;
            var pieces = new List<Transform>();
            for (int i = 0; i < 4; i++) if (Hubs[i] != null) pieces.Add(Hubs[i]);
            var prefixes = Definition.shatterPrefixes;
            foreach (Transform child in Model.transform)
            {
                if (!child.gameObject.activeSelf || prefixes == null) continue;
                for (int p = 0; p < prefixes.Length; p++)
                    if (!string.IsNullOrEmpty(prefixes[p]) && child.name.StartsWith(prefixes[p])) { pieces.Add(child); break; }
            }

            foreach (var piece in pieces)
            {
                piece.SetParent(null, true);
                if (!AddFittedBox(piece.gameObject)) { Destroy(piece.gameObject); continue; }
                var rb = piece.gameObject.AddComponent<Rigidbody>();
                TestDebrisCollision.Configure(rb);
                rb.mass = 30f;
                Vector3 away = piece.position - center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                away.y = 0f;
                rb.linearVelocity = carVelocity * 0.6f
                                  + away.normalized * speed * Random.Range(0.6f, 1.4f)
                                  + Vector3.up * Random.Range(lift * 0.4f, lift);
                rb.angularVelocity = Random.insideUnitSphere * 10f;
                Destroy(piece.gameObject, lifetime + Random.Range(0f, 2f));
            }

            // Kalan kasa: yavaşça savrulur
            var hull = Model;
            // Boya kaynakları görsel parçalar kadar yaşar; araba despawn olsa da pembeleşmezler.
            if (materialCopies.Count > 0)
            {
                var lease = hull.AddComponent<VisualMaterialLease>();
                lease.materials = new Material[materialCopies.Count];materialCopies.Values.CopyTo(lease.materials, 0);
                materialCopies.Clear(); originalMaterials.Clear();
            }
            hull.transform.SetParent(null, true);
            hull.name = "Kasa_enkaz";
            if (AddFittedBox(hull))
            {
                var hullBody = hull.AddComponent<Rigidbody>();
                TestDebrisCollision.Configure(hullBody);
                hullBody.mass = 400f;
                hullBody.linearVelocity = carVelocity * 0.7f + Vector3.up * lift * 0.35f;
                hullBody.angularVelocity = Random.insideUnitSphere * 2f;
            }
            Destroy(hull, lifetime + 2f);

            Model = null;
            CockpitAnchor = null;
            for (int i = 0; i < 4; i++) Wheels[i] = Hubs[i] = null;
            parts.Clear();
            targets.Clear();
            headLights.Clear();
        }

        /// <summary>Nesnenin açık mesh'lerini saran kutu collider ekler (kendi uzayında). Mesh yoksa false.</summary>
        private static bool AddFittedBox(GameObject go)
        {
            Matrix4x4 inv = go.transform.worldToLocalMatrix;
            Bounds bounds = new Bounds();
            bool any = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = inv * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (any) bounds.Encapsulate(p);
                    else { bounds = new Bounds(p, Vector3.zero); any = true; }
                }
            }
            if (!any) return false;
            var box = go.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.05f);
            return true;
        }

        // ------------------------------------------------------------------ Temizlik

        private void Clear()
        {
            Uncombine();
            if (Model != null) Destroy(Model);
            Model = null;
            Definition = null;
            geometryApplied = false; modelRenderers = null; originalShadowModes = null;
            foreach (var material in materialCopies.Values) if (material != null) Destroy(material);
            materialCopies.Clear(); originalMaterials.Clear();
            CockpitAnchor = null;
            for (int i = 0; i < 4; i++) Wheels[i] = Hubs[i] = null;
            parts.Clear();
            targets.Clear();
            headLights.Clear();
        }

        private void OnDestroy()
        {
            foreach (var mesh in combinedMeshes) if (mesh != null) Destroy(mesh);
            foreach (var material in materialCopies.Values) if (material != null) Destroy(material);
        }
    }
}
