using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Harvester.Race
{
    /// <summary>
    /// Yerel oyuncunun arabası için sanal dikiz aynası. Tamamen yerel: ağ objesi ya da RPC yok.
    ///
    /// - Tek bir arka kamera, sadece yerel arabada (RaceCar.Local ve CarCamera'nın hedefi o olduğunda).
    ///   Araba elenince, izleyici kamerasına geçilince ya da sahnede yerel araba yokken kapalıdır.
    /// - Kamera küçük bir RenderTexture'a (varsayılan iç çözünürlük 320×80) her N karede bir çizer (varsayılan 2).
    ///   Kare atlama kameranın enabled'ını LateUpdate'te açıp kapatarak yapılır: URP kamera listesini
    ///   LateUpdate'ten sonra topladığı için aynı karede geçerli olur, ek çağrı ve çöp üretmez. Çizilmeyen
    ///   karelerde doku son görüntüyü tutar.
    /// - Gölge, derinlik/opak dokusu, MSAA, HDR yok; uzak kırpma ~170 m (sis zaten kapatıyor).
    ///   "Harvester/Setup Rear View Mirror" kurulduysa özelliksiz "RearViewMirror_Renderer" kullanılır
    ///   (LethalLook tam ekran geçişi aynada çalışmaz); posterize + dither aynayı ekrana basan arayüz
    ///   shader'ında aynanın kendi pikselleri üzerinde yapılır.
    /// - Ekranın üst ortasında çerçeveli şerit, yatayda aynalanmış (RawImage.uvRect genişliği -1).
    ///   M tuşu açar/kapatır, tercih PlayerPrefs'te saklanır.
    ///
    /// Sahneye eklemek gerekmez: oyun başında kendiliğinden kalıcı bir obje kurulur ve yerel arabayı bekler.
    /// </summary>
    [DefaultExecutionOrder(1100)] // CarCamera ve RemoteExtrapolator'dan (1000) sonra
    public class RearViewMirror : MonoBehaviour
    {
        public const string PrefKey = "Harvester.RearViewMirror";
        public const string RendererName = "RearViewMirror_Renderer";
        public const string CameraName = "AynaKamerasi";
        public const KeyCode ToggleKey = KeyCode.M;

        public static RearViewMirror Instance { get; private set; }

        /// <summary>Ölçüm için aynayı oyuncu tercihine dokunmadan kapatır (RearViewMirrorBenchmark).</summary>
        public static bool Suppressed;

        /// <summary>Oyuncunun tercihi (M tuşu, PlayerPrefs). Varsayılan açık.</summary>
        public static bool UserEnabled
        {
            get { return PlayerPrefs.GetInt(PrefKey, 1) != 0; }
            set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); }
        }

        [Header("Çözünürlük ve hız")]
        [Tooltip("Aynanın iç çözünürlüğü (piksel). Doku, URP'nin renderScale'ine bölünerek büyütülür ki aynanın pikselleri oyunun pikselleriyle aynı boyda olsun.")]
        public Vector2Int pixelResolution = new Vector2Int(320, 80);
        [Tooltip("Ayna kaç karede bir çizilsin (1 = her kare, 2 = ~30 Hz)")]
        [Range(1, 6)] public int frameInterval = 2;
        [Tooltip("Uzak kırpma (m). Sis ~150 m'de neredeyse her şeyi kapatıyor.")]
        public float farClip = 170f;
        [Tooltip("Dikey görüş açısı (derece). 4:1 şeritte 20° ≈ 70° yatay.")]
        public float verticalFov = 20f;
        [Tooltip("Post-processing (renk ayarı, ton eşleme, bloom). Kapalıyken ayna oyundan karanlık ve doygun görünür.")]
        public bool postProcessing = true;
        [Tooltip("Aynanın göstermeyeceği katmanlar (ana kameranın maskesinden ayrıca çıkarılır)")]
        public LayerMask excludedLayers = 0;

        [Header("Kamera yeri")]
        [Tooltip("Kameranın arabanın üst yüzünden yüksekliği (m)")]
        public float mountHeight = 0.15f;
        [Tooltip("Kameranın arka kenardan öne kayması (m). Arabanın kendisi görüntüye girmesin.")]
        public float mountInset = 0.2f;
        [Tooltip("Aşağı bakış açısı (derece)")]
        public float pitchDown = 3f;

        [Header("Görünüm")]
        [Tooltip("Takip kameralarında da göster (kaput kamerasında her zaman gösterilir)")]
        public bool showInFollowModes = true;
        [Tooltip("1080p'de bir ayna pikselinin kaç ekran pikseli olduğu (oyun renderScale 0.5 ile 2 piksel)")]
        public float pixelsPerTexelAt1080 = 2f;
        [Tooltip("Aynanın üst kenarının ekranın tepesinden uzaklığı (1080p'de piksel). RaceHUD'un harvester uyarısı 20–90 arasında.")]
        public float topMargin = 96f;
        [Tooltip("Çerçeve kalınlığı (1080p'de piksel)")]
        public float frameThickness = 6f;
        public bool posterize = true;
        [Range(2, 64)] public float colorLevels = 14f;
        [Range(0, 1)] public float ditherStrength = 0.6f;

        // Çalışma zamanı
        private Camera cam;
        private UniversalAdditionalCameraData camData;
        private RenderTexture texture;
        private float textureRenderScale = -1f;
        private Vector2Int textureResolution;
        private Canvas canvas;
        private RectTransform frameRect, rimRect, imageRect, toastRect;
        private RawImage image;
        private Text toast;
        private Material material;
        private float toastUntil;
        private int layoutWidth = -1, layoutHeight = -1;
        private bool mirrorVisible, canvasVisible;
        private bool renderedOnce;
        private int frameCounter;
        private RaceCar mountedCar;
        private BoxCollider mountedBox;

        /// <summary>Bu karede ayna kamerası çiziyor mu (ölçüm için).</summary>
        public bool RendersThisFrame => cam != null && cam.enabled;
        public bool IsShowing => mirrorVisible;
        public Camera MirrorCamera => cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("RearViewMirror");
            DontDestroyOnLoad(go);
            go.AddComponent<RearViewMirror>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Teardown();
            if (Instance == this) Instance = null;
        }

        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            // Sahne değişince kamera ana kameranın ayarlarını yeniden alsın
            Teardown();
        }

        private void LateUpdate()
        {
            RaceCar car = RaceCar.Local;
            CarCamera follower = CarCamera.Current;
            bool driving = car != null && !car.IsEliminated && follower != null && follower.isActiveAndEnabled && follower.Target == car;

            if (driving && Input.GetKeyDown(ToggleKey))
            {
                bool on = !UserEnabled;
                UserEnabled = on;
                ShowToast(on ? "Dikiz aynası açık  <size=70%>(M)</size>" : "Dikiz aynası kapalı  <size=70%>(M)</size>");
            }

            bool want = driving && UserEnabled && !Suppressed && (showInFollowModes || follower.CurrentMode == CarCamera.Mode.Kaput);
            bool toastOn = driving && Time.unscaledTime < toastUntil;

            if (!want && !toastOn)
            {
                if (car == null && cam != null) Teardown(); // sahnede yerel araba yok: her şeyi bırak
                else Hide();
                return;
            }

            EnsureBuilt();
            if (canvas == null) return;
            if (Screen.width != layoutWidth || Screen.height != layoutHeight) Layout();

            if (want)
            {
                Mount(car);
                // İlk karede hemen çiz ki dokuda çöp görünmesin, sonra her frameInterval karede bir
                bool render = !renderedOnce || (++frameCounter >= Mathf.Max(1, frameInterval));
                if (render) frameCounter = 0;
                if (cam.enabled != render) cam.enabled = render;
                if (render) renderedOnce = true;
            }
            else
            {
                if (cam.enabled) cam.enabled = false;
                renderedOnce = false; // yeniden açılınca eski görüntü bir kare bile görünmesin
            }

            SetMirrorVisible(want && renderedOnce);
            if (toast != null)
            {
                bool showToast = toastOn;
                if (toast.enabled != showToast) toast.enabled = showToast;
            }
            SetCanvasVisible(mirrorVisible || toastOn);
        }

        private void ShowToast(string text)
        {
            EnsureBuilt();
            if (toast == null) return;
            toast.text = text;
            toastUntil = Time.unscaledTime + 1.5f;
        }

        // ------------------------------------------------------------------ Kamera

        private void Mount(RaceCar car)
        {
            if (car != mountedCar)
            {
                mountedCar = car;
                mountedBox = car.GetComponent<BoxCollider>();
            }
            Transform t = car.transform;
            Vector3 local = mountedBox != null
                ? new Vector3(mountedBox.center.x,
                              mountedBox.center.y + mountedBox.size.y * 0.5f + mountHeight,
                              mountedBox.center.z - mountedBox.size.z * 0.5f + mountInset)
                : new Vector3(0f, 1.2f, -1.5f);
            cam.transform.SetPositionAndRotation(t.TransformPoint(local), t.rotation * Quaternion.Euler(pitchDown, 180f, 0f));
        }

        private void EnsureBuilt()
        {
            if (cam == null) BuildCamera();
            float scale = CurrentRenderScale();
            if (texture == null || !Mathf.Approximately(scale, textureRenderScale) || textureResolution != pixelResolution)
                BuildTexture(scale);
            if (canvas == null) BuildUI();
        }

        private static float CurrentRenderScale()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return 1f;
            float s = asset.renderScale;
            return Mathf.Abs(1f - s) < 0.05f ? 1f : Mathf.Clamp(s, 0.1f, 1f); // URP de 0.05'ten küçük farkı yok sayar
        }

        private static int FindMirrorRenderer()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return -1;
            var list = asset.rendererDataList;
            for (int i = 0; i < list.Length; i++)
                if (list[i] != null && list[i].name == RendererName) return i;
            return -1;
        }

        private void BuildCamera()
        {
            Camera main = Camera.main;
            var go = new GameObject(CameraName);
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.enabled = false;

            int uiLayer = LayerMask.NameToLayer("UI");
            int mask = main != null ? main.cullingMask : ~0;
            if (uiLayer >= 0) mask &= ~(1 << uiLayer);
            mask &= ~excludedLayers.value;
            cam.cullingMask = mask;
            cam.clearFlags = main != null ? main.clearFlags : CameraClearFlags.Skybox;
            cam.backgroundColor = main != null ? main.backgroundColor : Color.black;
            cam.depth = (main != null ? main.depth : 0f) - 10f; // ana kameradan önce
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = farClip;
            cam.fieldOfView = verticalFov;
            cam.useOcclusionCulling = true;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.allowDynamicResolution = false;

            camData = go.AddComponent<UniversalAdditionalCameraData>();
            camData.renderType = CameraRenderType.Base;
            camData.renderShadows = false;
            camData.requiresDepthOption = CameraOverrideOption.Off;
            camData.requiresColorOption = CameraOverrideOption.Off;
            camData.antialiasing = AntialiasingMode.None;
            camData.stopNaN = false;
            camData.dithering = false;
            camData.allowXRRendering = false;
            camData.allowHDROutput = false;
            camData.renderPostProcessing = postProcessing;
            var mainData = main != null ? main.GetComponent<UniversalAdditionalCameraData>() : null;
            if (postProcessing)
            {
                camData.volumeLayerMask = mainData != null ? mainData.volumeLayerMask : (LayerMask)1;
            }
            else
            {
                // Post yokken volume yığınını her kare güncellemenin anlamı yok
                camData.volumeLayerMask = 0;
                cam.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);
            }
            int rendererIndex = FindMirrorRenderer();
            if (rendererIndex >= 0) camData.SetRenderer(rendererIndex);

            renderedOnce = false;
            frameCounter = 0;
        }

        private void BuildTexture(float renderScale)
        {
            ReleaseTexture();
            textureRenderScale = renderScale;
            textureResolution = pixelResolution;
            // URP renderScale'i RenderTexture'a çizen kameralara da uygular: doku iç çözünürlüğün 1/renderScale katı,
            // kamera içeride pixelResolution'da çizer ve Point filtreyle dokuya büyütür (ana ekranla aynı pikseller).
            int w = Mathf.Max(16, Mathf.RoundToInt(pixelResolution.x / renderScale));
            int h = Mathf.Max(8, Mathf.RoundToInt(pixelResolution.y / renderScale));
            var desc = new RenderTextureDescriptor(w, h, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, 16)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
            };
            texture = new RenderTexture(desc)
            {
                name = "DikizAynasi",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.Create();
            cam.targetTexture = texture;
            cam.aspect = (float)pixelResolution.x / pixelResolution.y;
            if (image != null) image.texture = texture;
            if (material != null) material.SetVector("_PixelGrid", new Vector4(pixelResolution.x, pixelResolution.y, 0f, 0f));
            renderedOnce = false;
            layoutWidth = -1;
        }

        private void ReleaseTexture()
        {
            if (texture == null) return;
            if (cam != null) cam.targetTexture = null;
            texture.Release();
            Destroy(texture);
            texture = null;
        }

        // ------------------------------------------------------------------ Arayüz

        private void BuildUI()
        {
            var root = new GameObject("DikizAynasiArayuz", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvas.pixelPerfect = true;
            canvas.enabled = false;
            canvasVisible = false;

            frameRect = NewRect("Cerceve", root.transform);
            var frame = frameRect.gameObject.AddComponent<Image>();
            frame.color = new Color(0.04f, 0.04f, 0.05f, 0.92f);
            frame.raycastTarget = false;

            rimRect = NewRect("Kenar", frameRect);
            var rim = rimRect.gameObject.AddComponent<Image>();
            rim.color = new Color(0.32f, 0.33f, 0.36f, 1f);
            rim.raycastTarget = false;

            imageRect = NewRect("Goruntu", rimRect);
            image = imageRect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.uvRect = new Rect(1f, 0f, -1f, 1f); // yatayda aynala
            image.raycastTarget = false;
            if (posterize)
            {
                var shader = Resources.Load<Shader>("RearViewMirrorUI");
                if (shader != null && shader.isSupported)
                {
                    material = new Material(shader) { name = "DikizAynasiMat" };
                    material.SetFloat("_ColorLevels", colorLevels);
                    material.SetFloat("_DitherStrength", ditherStrength);
                    material.SetVector("_PixelGrid", new Vector4(pixelResolution.x, pixelResolution.y, 0f, 0f));
                    image.material = material;
                }
            }

            toastRect = NewRect("Bildirim", root.transform);
            toast = toastRect.gameObject.AddComponent<Text>();
            toast.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            toast.fontStyle = FontStyle.Bold;
            toast.alignment = TextAnchor.UpperCenter;
            toast.supportRichText = true;
            toast.horizontalOverflow = HorizontalWrapMode.Overflow;
            toast.color = Color.white;
            toast.raycastTarget = false;
            toast.enabled = false;
            var shadow = toastRect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            shadow.effectDistance = new Vector2(2f, -2f);

            frameRect.gameObject.SetActive(false);
            mirrorVisible = false;
            layoutWidth = -1;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            return rt;
        }

        /// <summary>Ekran boyutuna göre yerleşim: ayna pikselleri tam sayı ekran pikseli olsun (Point filtreyle düzgün).</summary>
        private void Layout()
        {
            layoutWidth = Screen.width;
            layoutHeight = Screen.height;
            float s = Screen.height / 1080f;
            int ppt = Mathf.Max(1, Mathf.RoundToInt(pixelsPerTexelAt1080 * s));
            // Çok dar ekranlarda ekran genişliğinin %60'ını geçmesin
            while (ppt > 1 && pixelResolution.x * ppt > Screen.width * 0.6f) ppt--;
            float w = pixelResolution.x * ppt, h = pixelResolution.y * ppt;
            float border = Mathf.Max(2f, Mathf.Round(frameThickness * s));
            float rimWidth = Mathf.Max(1f, Mathf.Round(border * 0.34f));
            float top = Mathf.Round(topMargin * s);

            frameRect.anchoredPosition = new Vector2(0f, -top);
            frameRect.sizeDelta = new Vector2(w + 2f * (border + rimWidth), h + 2f * (border + rimWidth));
            rimRect.anchoredPosition = new Vector2(0f, -border);
            rimRect.sizeDelta = new Vector2(w + 2f * rimWidth, h + 2f * rimWidth);
            imageRect.anchoredPosition = new Vector2(0f, -rimWidth);
            imageRect.sizeDelta = new Vector2(w, h);

            toast.fontSize = Mathf.RoundToInt(26 * Mathf.Max(0.6f, s));
            toastRect.anchoredPosition = new Vector2(0f, -(top + frameRect.sizeDelta.y + 6f * s));
            toastRect.sizeDelta = new Vector2(Screen.width, 40f * Mathf.Max(0.6f, s));
        }

        private void SetMirrorVisible(bool visible)
        {
            if (mirrorVisible == visible || frameRect == null) return;
            mirrorVisible = visible;
            frameRect.gameObject.SetActive(visible);
        }

        private void SetCanvasVisible(bool visible)
        {
            if (canvasVisible == visible || canvas == null) return;
            canvasVisible = visible;
            canvas.enabled = visible;
        }

        private void Hide()
        {
            if (cam != null && cam.enabled) cam.enabled = false;
            SetMirrorVisible(false);
            SetCanvasVisible(false);
            renderedOnce = false; // yeniden açılınca ilk karede hemen çizsin
        }

        private void Teardown()
        {
            ReleaseTexture();
            if (cam != null) Destroy(cam.gameObject);
            if (canvas != null) Destroy(canvas.gameObject);
            if (material != null) Destroy(material);
            cam = null;
            camData = null;
            canvas = null;
            image = null;
            toast = null;
            material = null;
            frameRect = rimRect = imageRect = toastRect = null;
            mirrorVisible = canvasVisible = false;
            renderedOnce = false;
            mountedCar = null;
            mountedBox = null;
            textureRenderScale = -1f;
            layoutWidth = -1;
        }
    }
}
