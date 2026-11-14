using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Harvester.Race;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Araba tanımlarını (CarDefinition) modelden ölçer ve kataloğu günceller. CarBuilder'ın parçası.
// Menü: Harvester > Araç > Update Car Definitions; ayrıca tanım asset'inin inspector'ında "Modelden ölç" düğmesi.
//
// Model adlandırma sözleşmesi (Antigravity/Blender):
//   wheel-front-left/right, wheel-back-left/right (pivot tekerlek merkezinde)
//   wheel-*_rim_<sıra>_<ad>, wheel-*_tire_lettering, wheel-*_caliper ya da kökte caliper-*
//   mod_<yuva>_<sıra>_<ad>[_left|_right], decal_<sıra>_<ad>
//   Paint_* / Neon / Glass_Tint boyanabilir malzemeler, Light_Tail stop lambası
//   camera_cockpit, light_head_L/R, light_tail_L/R boş noktaları
public static partial class CarBuilder
{
    static readonly Regex ModRegex = new Regex(@"^mod_(.+?)_(\d+)_(.+)$");
    static readonly Regex RimRegex = new Regex(@"^(.+)_rim_(\d+)_(.+)$");
    static readonly Regex DecalRegex = new Regex(@"^decal_(\d+)_(.+)$");
    const string TireLetteringSuffix = "_tire_lettering";
    static readonly string[] PaintableExtra = { "Neon", "Glass_Tint" };
    static readonly string[] PaintOrder = { "Paint_Body", "Paint_Rim", "Paint_Caliper", "Paint_Accent", "Neon", "Glass_Tint" };
    const string BrakeLightMaterialName = "Light_Tail";

    static readonly Dictionary<string, string> SlotNames = new Dictionary<string, string>
    {
        { "bumper_front", "Ön tampon" }, { "bumper_rear", "Arka tampon" }, { "exhaust", "Egzoz" },
        { "fender", "Çamurluk" }, { "frunk", "Ön bagaj" }, { "headlight", "Farlar" }, { "hood", "Kaput" },
        { "interior_passenger", "Yolcu koltuğu" }, { "interior_pillar", "Direk göstergesi" }, { "mirror", "Aynalar" },
        { "neon", "Neon" }, { "rollcage", "Rollbar" }, { "roof_scoop", "Tavan hava girişi" }, { "skirts", "Marşpiyel" },
        { "spoiler", "Rüzgârlık" }, { "steering", "Direksiyon" }, { "taillight", "Stop lambaları" },
        { "rim", "Jantlar" }, { "decal", "Çıkartma" }, { "tire_lettering", "Lastik yazısı" },
    };

    static readonly Dictionary<string, string> VariantNames = new Dictionary<string, string>
    {
        { "stock", "Standart" }, { "street", "Sokak" }, { "cup", "Kupa" }, { "race", "Yarış" }, { "track", "Pist" },
        { "timeattack", "Time Attack" }, { "center", "Ortada" }, { "quad", "Dörtlü" }, { "upswept", "Yukarı kalkık" },
        { "louvers", "Panjurlu" }, { "widebody", "Geniş kasa" }, { "nos_bay", "NOS bölmesi" }, { "amber", "Amber" },
        { "halo", "Halka" }, { "rally", "Ralli" }, { "blower", "Kompresör" }, { "carbon", "Karbon" }, { "seat", "Koltuk" },
        { "nos", "NOS tüpü" }, { "gauges", "Göstergeler" }, { "aero", "Aero" }, { "none", "Yok" }, { "sides", "Yanlar" },
        { "full", "Tam" }, { "half", "Yarım" }, { "single", "Tekli" }, { "twin", "İkiz" }, { "sharkfin", "Köpekbalığı yüzgeci" },
        { "ducktail", "Ördek kuyruğu" }, { "swanneck", "Kuğu boynu" }, { "gtwing", "GT kanadı" }, { "formula", "Formula" },
        { "drift", "Drift" }, { "clear", "Şeffaf" }, { "cyber", "Siber" }, { "sport", "Spor" }, { "5spoke", "5 kollu" },
        { "turbofan", "Türbin" }, { "mesh", "Örgü" }, { "off", "Kapalı" }, { "on", "Açık" }, { "wide", "Geniş" },
        { "center_stripes", "Orta şeritler" }, { "side_rocker", "Yan şerit" }, { "number_plates", "Yarış numarası" },
        { "side_slashes", "Yan çizgiler" }, { "windshield_banner", "Cam bandı" },
    };

    static readonly Dictionary<string, string> PaintNames = new Dictionary<string, string>
    {
        { "Paint_Body", "Gövde" }, { "Paint_Rim", "Jant" }, { "Paint_Caliper", "Kaliper" }, { "Paint_Accent", "Detay" },
        { "Neon", "Neon" }, { "Glass_Tint", "Cam filmi" },
    };

    [MenuItem("Harvester/Araç/Update Car Definitions")]
    public static CarCatalog UpdateDefinitions()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CarCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<CarCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        foreach (string id in Cars)
        {
            var def = EnsureDefinition(id);
            if (def == null) continue;
            if (!catalog.cars.Contains(def)) catalog.cars.Add(def);
        }
        // Listede olmayan tanımlar da (yeni arabalar) sona; mevcutlar yeniden ölçülür
        foreach (var def in FindAllDefinitions())
        {
            if (catalog.cars.Contains(def)) continue;
            MeasureDefinition(def, false);
            catalog.cars.Add(def);
        }
        // Boş kayıtlar SİLİNMEZ: silmek sonraki arabaların ağ indeksini kaydırır. Yer tutucu kalır (Resolve ilk arabaya düşer).
        for (int i = 0; i < catalog.cars.Count; i++)
            if (catalog.cars[i] == null) Debug.LogWarning("Araba kataloğunda " + i + ". kayıt boş (tanım silinmiş); indeks korunuyor: " + CatalogPath);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log("Araba kataloğu güncellendi: " + CatalogPath + " (" + catalog.Count + " araba)");
        return catalog;
    }

    /// <summary>Assets/Cars altındaki bütün araba tanımları, kimliğe göre sıralı (kararlı).</summary>
    public static List<CarDefinition> FindAllDefinitions()
    {
        var list = new List<CarDefinition>();
        foreach (var guid in AssetDatabase.FindAssets("t:CarDefinition", new[] { "Assets/Cars" }))
        {
            var def = AssetDatabase.LoadAssetAtPath<CarDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (def != null && !list.Contains(def)) list.Add(def);
        }
        list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return list;
    }

    /// <summary>Katalogda olmayan tanımları sona ekler; mevcut sıraya dokunmaz. Katalog yoksa bir şey yapmaz.</summary>
    public static int AppendNewDefinitionsToCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CarCatalog>(CatalogPath);
        if (catalog == null) return 0;
        int added = 0;
        foreach (var def in FindAllDefinitions())
        {
            if (catalog.cars.Contains(def) || string.IsNullOrEmpty(def.id)) continue;
            catalog.cars.Add(def);
            // Birleştirme (CarVisual) mesh okur: yeni arabanın FBX import ayarında Read/Write açılır (sadece .meta)
            MakeModelReadable(def);
            added++;
            Debug.Log("Araba kataloğuna eklendi (indeks " + (catalog.cars.Count - 1) + "): " + def.id);
        }
        if (added > 0)
        {
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }
        return added;
    }

    /// <summary>Assets/Cars/&lt;id&gt;/&lt;id&gt;_Tanim.asset'i oluşturur ya da günceller ve modelden ölçer.</summary>
    public static CarDefinition EnsureDefinition(string id)
    {
        string folder = "Assets/Cars/" + id;
        string path = folder + "/" + id + "_Tanim.asset";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/" + id + ".prefab");
        if (model == null) model = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/" + id + ".fbx");
        if (model == null)
        {
            Debug.LogError("Araba modeli yok: " + folder + "/" + id + ".prefab");
            return null;
        }
        var def = AssetDatabase.LoadAssetAtPath<CarDefinition>(path);
        bool created = def == null;
        if (created)
        {
            def = ScriptableObject.CreateInstance<CarDefinition>();
            def.id = id;
            def.displayName = id.Replace('_', ' ');
            def.baseStats = CarStats.Default;
            AssetDatabase.CreateAsset(def, path);
        }
        if (def.modelPrefab == null) def.modelPrefab = model;
        MeasureDefinition(def, created);
        return def;
    }

    /// <summary>
    /// Tanımın modelini önizleme sahnesinde açıp ölçer: tekerlekler, collider, ağırlık merkezi Z'si, yuvalar, boyalar.
    /// Elle düzenlenen görünen adlar ve fiyatlar (anahtar eşleşirse) korunur. firstTime ise ağırlık merkezi yüksekliği de
    /// modelden önerilir ve varsayılan performans ağacı yazılır.
    /// </summary>
    public static void MeasureDefinition(CarDefinition def, bool firstTime)
    {
        if (def == null || def.modelPrefab == null)
        {
            Debug.LogError("Tanımda model yok: " + (def != null ? def.name : "null"));
            return;
        }
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Olcum");
            SceneManager.MoveGameObjectToScene(root, preview);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(def.modelPrefab, preview);
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            float wheelY = MeasureGeometry(def, root.transform, model.transform);
            if (firstTime || def.baseStats.centerOfMassHeight <= 0f)
                def.baseStats.centerOfMassHeight = Mathf.Round(wheelY * 0.82f * 100f) / 100f;
            ExtractSlots(def, model.transform);
            ExtractPaints(def, model.transform);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
        if (def.upgrades == null || def.upgrades.Count == 0) def.upgrades = DefaultUpgrades();
        EditorUtility.SetDirty(def);
        AssetDatabase.SaveAssets();
        var g = def.geometry;
        Debug.Log(def.name + " ölçüldü: dingil " + g.wheelBase.ToString("F3") + " m, iz " + g.frontTrack.ToString("F2") + "/" + g.rearTrack.ToString("F2")
                  + ", yarıçap " + g.wheelRadii[0].ToString("F3") + "/" + g.wheelRadii[2].ToString("F3")
                  + ", gövde " + g.bodySize.ToString("F2") + ", collider " + g.colliderCenter.ToString("F2") + " / " + g.colliderSize.ToString("F2")
                  + ", " + def.slots.Count + " yuva, " + def.paints.Count + " boya");
    }

    static float MeasureGeometry(CarDefinition def, Transform root, Transform model)
    {
        var g = def.geometry;
        if (g.wheelNames == null || g.wheelNames.Length != 4) g.wheelNames = (string[])WheelNames.Clone();
        if (g.wheelCenters == null || g.wheelCenters.Length != 4) g.wheelCenters = new Vector3[4];
        if (g.wheelRadii == null || g.wheelRadii.Length != 4) g.wheelRadii = new float[4];

        for (int i = 0; i < 4; i++)
        {
            var wheel = FindDeep(model, g.wheelNames[i]);
            if (wheel == null)
            {
                Debug.LogError(def.name + ": modelde tekerlek yok: " + g.wheelNames[i]);
                continue;
            }
            g.wheelCenters[i] = root.InverseTransformPoint(wheel.position);
            // Yarıçap: tekerleğin kendi mesh'i (lastik), yoksa altındaki her şey
            var r = wheel.GetComponent<Renderer>();
            Bounds b = r != null ? r.bounds : RendererBounds(root, wheel, null);
            g.wheelRadii[i] = Mathf.Max(b.extents.y, b.extents.z);
        }
        float wheelY = (g.wheelCenters[0].y + g.wheelCenters[1].y + g.wheelCenters[2].y + g.wheelCenters[3].y) / 4f;
        float frontZ = (g.wheelCenters[0].z + g.wheelCenters[1].z) / 2f;
        float backZ = (g.wheelCenters[2].z + g.wheelCenters[3].z) / 2f;
        g.wheelBase = frontZ - backZ;
        g.frontTrack = g.wheelCenters[1].x - g.wheelCenters[0].x;
        g.rearTrack = g.wheelCenters[3].x - g.wheelCenters[2].x;
        g.centerOfMassZ = (frontZ + backZ) / 2f;

        // Gövde sınırı: stok görünümdeki (açık) parçalar, aynalar hariç (kutu dar kalsın), tekerlekler dahil
        Bounds body = RendererBounds(root, model, t => t.name.StartsWith("mod_mirror"));
        g.bodySize = body.size;
        // Çarpışma kutusu: altı yerden yüksek (ışınlar zemini görsün), tekerlekler içinde (CarContactSolver)
        float bottom = wheelY * 0.7f;
        g.colliderCenter = new Vector3(body.center.x, (bottom + body.max.y) / 2f, body.center.z);
        g.colliderSize = new Vector3(body.size.x, body.max.y - bottom, body.size.z);
        return wheelY;
    }

    /// <summary>Açık renderer'ların kök uzayındaki sınırı. exclude, nesnenin ya da bir atasının adına bakar.</summary>
    static Bounds RendererBounds(Transform root, Transform under, System.Func<Transform, bool> exclude)
    {
        Bounds bounds = new Bounds();
        bool any = false;
        foreach (var r in under.GetComponentsInChildren<Renderer>(false))
        {
            if (exclude != null)
            {
                bool skip = false;
                for (var t = r.transform; t != null && t != under; t = t.parent)
                    if (exclude(t)) { skip = true; break; }
                if (skip) continue;
            }
            Bounds b = r.bounds;
            b.center = root.InverseTransformPoint(b.center);
            if (any) bounds.Encapsulate(b);
            else bounds = b;
            any = true;
        }
        return bounds;
    }

    // ------------------------------------------------------------------ Yuvalar

    class SlotBuild
    {
        public string id;
        public CarSlotKind kind;
        public SortedDictionary<int, VariantBuild> variants = new SortedDictionary<int, VariantBuild>();
    }

    class VariantBuild
    {
        public string key;
        public string name;
        public List<string> objects = new List<string>();
    }

    static void AddVariant(Dictionary<string, SlotBuild> slots, string slotId, CarSlotKind kind, int index, string name, string objectName)
    {
        SlotBuild slot;
        if (!slots.TryGetValue(slotId, out slot))
        {
            slot = new SlotBuild { id = slotId, kind = kind };
            slots.Add(slotId, slot);
        }
        VariantBuild v;
        if (!slot.variants.TryGetValue(index, out v))
        {
            v = new VariantBuild { key = index + "_" + name, name = name };
            slot.variants.Add(index, v);
        }
        if (objectName != null && !v.objects.Contains(objectName)) v.objects.Add(objectName);
    }

    static void ExtractSlots(CarDefinition def, Transform model)
    {
        var found = new Dictionary<string, SlotBuild>();
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            Match m;
            if ((m = ModRegex.Match(n)).Success)
            {
                string name = m.Groups[3].Value;
                // Sol/sağ çift (marşpiyel): aynı varyantın iki nesnesi
                if (name.EndsWith("_left")) name = name.Substring(0, name.Length - 5);
                else if (name.EndsWith("_right")) name = name.Substring(0, name.Length - 6);
                AddVariant(found, m.Groups[1].Value, CarSlotKind.Parca, int.Parse(m.Groups[2].Value), name, n);
            }
            else if ((m = RimRegex.Match(n)).Success)
                AddVariant(found, "rim", CarSlotKind.Jant, int.Parse(m.Groups[2].Value), m.Groups[3].Value, n);
            else if ((m = DecalRegex.Match(n)).Success)
            {
                AddVariant(found, "decal", CarSlotKind.Cikartma, 0, "none", null);
                AddVariant(found, "decal", CarSlotKind.Cikartma, int.Parse(m.Groups[1].Value), m.Groups[2].Value, n);
            }
            else if (n.EndsWith(TireLetteringSuffix))
            {
                AddVariant(found, "tire_lettering", CarSlotKind.AcKapa, 0, "off", null);
                AddVariant(found, "tire_lettering", CarSlotKind.AcKapa, 1, "on", n);
            }
        }

        // Eski sıra korunur (kayıtlar id ile ama yine de ağ indeksi kaymasın), yeniler sona alfabetik
        var oldSlots = def.slots ?? new List<CarModSlot>();
        var order = new List<string>();
        foreach (var s in oldSlots) if (found.ContainsKey(s.id)) order.Add(s.id);
        var fresh = new List<string>();
        foreach (var id in found.Keys) if (!order.Contains(id)) fresh.Add(id);
        fresh.Sort(System.StringComparer.Ordinal);
        order.AddRange(fresh);
        if (order.Count > CarLoadout.MaxSlots)
            Debug.LogError(def.name + ": " + order.Count + " yuva var, seçim en fazla " + CarLoadout.MaxSlots + " taşır");

        var result = new List<CarModSlot>();
        foreach (var id in order)
        {
            var b = found[id];
            var old = oldSlots.Find(s => s.id == id);
            var slot = new CarModSlot
            {
                id = id,
                kind = b.kind,
                displayName = old != null && !string.IsNullOrEmpty(old.displayName) ? old.displayName : Pretty(SlotNames, id),
            };
            foreach (var kv in b.variants)
            {
                var vb = kv.Value;
                var oldVariant = old != null ? old.variants.Find(v => v.key == vb.key) : null;
                slot.variants.Add(new CarVariant
                {
                    key = vb.key,
                    displayName = oldVariant != null && !string.IsNullOrEmpty(oldVariant.displayName) ? oldVariant.displayName : Pretty(VariantNames, vb.name),
                    price = oldVariant != null ? oldVariant.price : 0,
                    objectNames = vb.objects.ToArray(),
                });
            }
            if (!slot.variants[0].key.StartsWith("0_"))
                Debug.LogWarning(def.name + ": '" + id + "' yuvasında 0. (stok) varyant yok, ilk varyant stok sayılacak");
            result.Add(slot);
        }
        def.slots = result;
    }

    static string Pretty(Dictionary<string, string> names, string key)
    {
        string n;
        if (names.TryGetValue(key, out n)) return n;
        string s = key.Replace('_', ' ');
        return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
    }

    // ------------------------------------------------------------------ Boyalar

    static void ExtractPaints(CarDefinition def, Transform model)
    {
        var mats = new List<Material>();
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && !mats.Contains(m)) mats.Add(m);

        var oldPaints = def.paints ?? new List<CarPaintSlot>();
        var result = new List<CarPaintSlot>();
        // Eski sıra korunur, yeniler sona
        foreach (var old in oldPaints)
        {
            var m = mats.Find(x => x.name == old.id);
            if (m == null) continue;
            old.material = m;
            if (string.IsNullOrEmpty(old.displayName)) old.displayName = Pretty(PaintNames, old.id);
            result.Add(old);
        }
        // Yeni yuvalar: önce bilinen sıra (gövde ilk: botlar ve hızlı renk seçimi onu kullanır), sonra alfabetik
        mats.Sort((a, b) =>
        {
            int ia = System.Array.IndexOf(PaintOrder, a.name), ib = System.Array.IndexOf(PaintOrder, b.name);
            if (ia < 0) ia = int.MaxValue;
            if (ib < 0) ib = int.MaxValue;
            return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a.name, b.name);
        });
        foreach (var m in mats)
        {
            bool paintable = m.name.StartsWith("Paint_") || System.Array.IndexOf(PaintableExtra, m.name) >= 0;
            if (!paintable || result.Exists(p => p.id == m.name)) continue;
            Color baseColor = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            float emission = 0f;
            if (m.HasProperty("_EmissionColor"))
            {
                Color e = m.GetColor("_EmissionColor");
                float baseMax = Mathf.Max(baseColor.r, baseColor.g, baseColor.b);
                float emissionMax = Mathf.Max(e.r, e.g, e.b);
                if (emissionMax > 0.01f && baseMax > 0.01f) emission = Mathf.Round(emissionMax / baseMax * 100f) / 100f;
            }
            result.Add(new CarPaintSlot
            {
                id = m.name,
                displayName = Pretty(PaintNames, m.name),
                material = m,
                defaultColor = baseColor,
                emissionIntensity = emission,
            });
        }
        if (result.Count > CarLoadout.MaxPaints)
            Debug.LogError(def.name + ": " + result.Count + " boya yuvası var, seçim en fazla " + CarLoadout.MaxPaints + " taşır");
        def.paints = result;
        def.brakeLightMaterial = mats.Find(x => x.name == BrakeLightMaterialName);
    }

    // ------------------------------------------------------------------ Performans ağacı

    static CarStatModifier Mul(CarStat stat, float value) { return new CarStatModifier { stat = stat, multiply = true, value = value }; }
    static CarStatModifier Add(CarStat stat, float value) { return new CarStatModifier { stat = stat, multiply = false, value = value }; }

    static CarUpgradeLevel Level(string name, int price, params CarStatModifier[] mods)
    {
        return new CarUpgradeLevel { displayName = name, price = price, modifiers = mods };
    }

    /// <summary>Başlangıç ağacı (denge sonra elle ayarlanır). Her seviye stoka göre toplam farkı yazar.</summary>
    static List<CarUpgradeCategory> DefaultUpgrades()
    {
        return new List<CarUpgradeCategory>
        {
            new CarUpgradeCategory { type = CarUpgradeType.Motor, displayName = "Motor", levels = new[]
            {
                Level("Sokak", 1000, Mul(CarStat.SonHiz, 1.03f), Mul(CarStat.Ivme, 1.05f)),
                Level("Spor", 2500, Mul(CarStat.SonHiz, 1.06f), Mul(CarStat.Ivme, 1.10f)),
                Level("Yarış", 5000, Mul(CarStat.SonHiz, 1.10f), Mul(CarStat.Ivme, 1.16f)),
            } },
            new CarUpgradeCategory { type = CarUpgradeType.Sanziman, displayName = "Şanzıman", levels = new[]
            {
                Level("Sokak", 800, Mul(CarStat.Ivme, 1.04f), Mul(CarStat.GeriIvme, 1.05f)),
                Level("Spor", 2000, Mul(CarStat.Ivme, 1.08f), Mul(CarStat.SonHiz, 1.02f), Mul(CarStat.GeriIvme, 1.10f)),
                Level("Yarış", 4000, Mul(CarStat.Ivme, 1.12f), Mul(CarStat.SonHiz, 1.04f), Mul(CarStat.GeriIvme, 1.15f)),
            } },
            new CarUpgradeCategory { type = CarUpgradeType.Lastik, displayName = "Lastik", levels = new[]
            {
                Level("Sokak", 700, Mul(CarStat.OnTutus, 1.04f), Mul(CarStat.ArkaTutus, 1.04f), Add(CarStat.LastikSurtunmesi, 0.05f), Add(CarStat.VirajIvmeSiniri, 0.5f)),
                Level("Spor", 1800, Mul(CarStat.OnTutus, 1.08f), Mul(CarStat.ArkaTutus, 1.08f), Add(CarStat.LastikSurtunmesi, 0.10f), Add(CarStat.VirajIvmeSiniri, 1.0f)),
                Level("Yarış", 3500, Mul(CarStat.OnTutus, 1.12f), Mul(CarStat.ArkaTutus, 1.12f), Add(CarStat.LastikSurtunmesi, 0.15f), Add(CarStat.VirajIvmeSiniri, 1.5f)),
            } },
            new CarUpgradeCategory { type = CarUpgradeType.Fren, displayName = "Fren", levels = new[]
            {
                Level("Sokak", 600, Mul(CarStat.Fren, 1.08f)),
                Level("Spor", 1500, Mul(CarStat.Fren, 1.16f), Mul(CarStat.ElFreniYavaslama, 1.1f)),
                Level("Yarış", 3000, Mul(CarStat.Fren, 1.25f), Mul(CarStat.ElFreniYavaslama, 1.2f)),
            } },
            new CarUpgradeCategory { type = CarUpgradeType.Suspansiyon, displayName = "Süspansiyon", levels = new[]
            {
                Level("Sokak", 800, Mul(CarStat.VirajDemiri, 1.10f), Add(CarStat.AgirlikMerkeziYuksekligi, -0.02f)),
                Level("Spor", 2000, Mul(CarStat.VirajDemiri, 1.20f), Add(CarStat.AgirlikMerkeziYuksekligi, -0.04f), Mul(CarStat.Yay, 1.05f), Mul(CarStat.Sonum, 1.05f)),
                Level("Yarış", 4000, Mul(CarStat.VirajDemiri, 1.30f), Add(CarStat.AgirlikMerkeziYuksekligi, -0.06f), Mul(CarStat.Yay, 1.10f), Mul(CarStat.Sonum, 1.10f), Add(CarStat.DonusYardimi, 0.5f)),
            } },
            new CarUpgradeCategory { type = CarUpgradeType.Agirlik, displayName = "Ağırlık azaltma", levels = new[]
            {
                // Yay kütleyle birlikte hafifler, yoksa araba yüksekte durur
                Level("Sokak", 900, Mul(CarStat.Kutle, 0.96f), Mul(CarStat.Yay, 0.96f), Mul(CarStat.Sonum, 0.98f), Mul(CarStat.Ivme, 1.02f)),
                Level("Spor", 2200, Mul(CarStat.Kutle, 0.92f), Mul(CarStat.Yay, 0.92f), Mul(CarStat.Sonum, 0.96f), Mul(CarStat.Ivme, 1.04f)),
                Level("Yarış", 4500, Mul(CarStat.Kutle, 0.88f), Mul(CarStat.Yay, 0.88f), Mul(CarStat.Sonum, 0.94f), Mul(CarStat.Ivme, 1.06f)),
            } },
        };
    }

    // ------------------------------------------------------------------ Geliştirici kısayolları

    [MenuItem("Harvester/Araç/Kayıtlı modifiyeyi rastgele yap")]
    static void RandomizeSaved()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CarCatalog>(CatalogPath);
        if (catalog == null || catalog.Count == 0)
        {
            Debug.LogError("Önce Harvester/Araç/Update Car Definitions");
            return;
        }
        var rng = new System.Random(System.Environment.TickCount);
        var l = CarLoadoutStore.Load(catalog);
        var def = catalog.Get(l.definition);
        l = CarLoadoutStore.RandomizeLooks(def, l, rng);
        CarLoadoutStore.Save(catalog, l);
        Debug.Log("[Araba] Kayıtlı seçim (bir sonraki spawn'da): " + CarLoadoutStore.Describe(def, l));
    }

    [MenuItem("Harvester/Araç/Kayıtlı modifiyeyi sil")]
    static void ClearSaved()
    {
        CarLoadoutStore.Clear();
        Debug.Log("[Araba] Kayıtlı seçim silindi; varsayılan kullanılacak");
    }
}

/// <summary>Araba tanımı inspector'ı: modelden ölçme düğmesi ve ölçüm özeti.</summary>
[CustomEditor(typeof(CarDefinition))]
public class CarDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var def = (CarDefinition)target;
        EditorGUILayout.HelpBox("Modelden ölç: tekerlekler, collider, yuvalar (mod_*, jant, çıkartma) ve boyalar modelin adlarından yeniden çıkarılır. " +
                                "Elle verilen görünen adlar ve fiyatlar korunur.", MessageType.Info);
        if (GUILayout.Button("Modelden ölç ve yuvaları çıkar"))
            CarBuilder.MeasureDefinition(def, false);
        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}

/// <summary>Yeni araba tanımı (Assets/Cars/*/*_Tanim.asset) içeri alınınca kataloğun SONUNA ekler; mevcut indeksler değişmez.</summary>
public sealed class CarCatalogPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] previous)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (var p in imported)
            if (p.StartsWith("Assets/Cars/", System.StringComparison.Ordinal) && p.EndsWith("_Tanim.asset", System.StringComparison.Ordinal))
            {
                EditorApplication.delayCall += () => CarBuilder.AppendNewDefinitionsToCatalog();
                return;
            }
    }
}
