using System.Text;
using UnityEngine;

namespace Harvester.Race
{
    /// <summary>
    /// Yarış göstergesi (OnGUI, bugün için basit): sıra, tur, süre, hız, geri sayım, harvester uyarısı,
    /// "ELENDİN" / "BİTİRDİN", sonuç tablosu. Sağ alt köşe NetStatsHUD'un, oraya bir şey konmaz.
    /// Yerel araba elenince birkaç saniye sonra izleyici kamerasına (harvester'ın arkası) geçer.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        [Tooltip("Harvester bu mesafeden yakınsa uyarı göster (m)")]
        [SerializeField] private float warnDistance = 120f;
        [Tooltip("Uyarının tamamen kırmızı olduğu mesafe (m)")]
        [SerializeField] private float dangerDistance = 20f;
        [Tooltip("Elendikten kaç saniye sonra izleyici kamerasına geçilsin")]
        [SerializeField] private float spectateDelay = 2f;

        private GUIStyle label, big, center, table, centeredTable, resultsTitle;
        private string cachedStats, cachedSpeed, cachedHarvester;
        private int statsPosition=-1, statsTotal=-1, statsLap=-1, statsLaps=-1, statsTenth=-1;
        private int cachedKmh=int.MinValue, cachedDistance=int.MinValue;
        private float scale = 1f;
        private float eliminatedAt = -1f;
        private bool spectating;
        private RacePhase lastPhase = RacePhase.Waiting;
        private float goShownUntil;
        private readonly StringBuilder sb = new StringBuilder();
        /// <summary>Eski OnGUI göstergeleri (sıra/süre, hız, ışıklar, harvester uyarısı). Yeni arayüz bunları çiziyor.</summary>
        public static bool DrawLegacyHud = false;

        private void Update()
        {
            var manager = RaceManager.Instance;
            if (manager == null) return;

            RacePhase shownPhase = manager.StartLightsOut && manager.Phase == RacePhase.Countdown ? RacePhase.Racing : manager.Phase;
            if (shownPhase == RacePhase.Racing && lastPhase == RacePhase.Countdown) goShownUntil = Time.time + 1.2f;
            lastPhase = shownPhase;

            var car = RaceCar.Local;
            if (car != null && car.IsEliminated)
            {
                if (eliminatedAt < 0f) eliminatedAt = Time.time;
                if (!spectating && Time.time - eliminatedAt >= spectateDelay)
                {
                    spectating = RaceSpectatorCamera.Begin() != null;
                }
            }
        }

        private void EnsureStyles()
        {
            float s = Mathf.Max(0.6f, Screen.height / 1080f);
            if (label != null && Mathf.Approximately(s, scale)) return;
            scale = s;
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(30 * s), fontStyle = FontStyle.Bold, richText = true };
            label.normal.textColor = Color.white;
            big = new GUIStyle(label) { fontSize = Mathf.RoundToInt(140 * s), alignment = TextAnchor.MiddleCenter };
            center = new GUIStyle(label) { fontSize = Mathf.RoundToInt(52 * s), alignment = TextAnchor.MiddleCenter };
            table = new GUIStyle(label) { fontSize = Mathf.RoundToInt(28 * s), fontStyle = FontStyle.Normal };
            centeredTable = new GUIStyle(table) { alignment = TextAnchor.MiddleCenter };
            resultsTitle = new GUIStyle(center) { fontSize = Mathf.RoundToInt(36 * s) };
        }

        private static void Shadowed(Rect r, string text, GUIStyle style)
        {
            Color c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), StripColor(text), style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        private static string StripColor(string text)
        {
            if (text.IndexOf("<color", System.StringComparison.Ordinal) < 0) return text;
            return System.Text.RegularExpressions.Regex.Replace(text, "<color=[^>]*>|</color>", "");
        }

        private static string FormatTime(float t)
        {
            int m = Mathf.FloorToInt(t / 60f);
            return m + ":" + (t - m * 60f).ToString("00.0");
        }

        private void OnGUI()
        {
            // Gösterge yalnız sabit Rect/Label çizer; Layout/girdi olaylarında metin üretmeye gerek yok.
            if (Event.current.type != EventType.Repaint) return;
            var manager = RaceManager.Instance;
            if (manager == null) return;
            EnsureStyles();
            float w = Screen.width, h = Screen.height, s = scale;
            var car = RaceCar.Local;

            RacerState me;
            bool hasMe = manager.TryGetLocalState(out me);
            int total = manager.Standings.Count;
            RacePhase phase = manager.Phase;

            // Sol üst, hız, ışıklar ve harvester uyarısı artık Codex'in YarisArayuzu'nda (UI/Yaris). Burada yalnız
            // C4/C5 gelene kadar kişisel durum ve sonuç tablosu kalıyor.
            if (DrawLegacyHud && hasMe && phase != RacePhase.Waiting)
            {
                float time = me.Status == RacerStatus.Racing ? manager.RaceTime : me.Time;
                int tenth = Mathf.RoundToInt(time * 10f);
                if (statsPosition != me.Position || statsTotal != total || statsLap != me.Lap || statsLaps != manager.Laps || statsTenth != tenth)
                {
                    statsPosition=me.Position; statsTotal=total; statsLap=me.Lap; statsLaps=manager.Laps; statsTenth=tenth;
                    sb.Length=0;
                    sb.Append("SIRA ").Append(me.Position).Append('/').Append(total).Append('\n');
                    if (manager.LoopTrack) sb.Append("TUR ").Append(me.Lap).Append('/').Append(manager.Laps).Append('\n');
                    sb.Append("SÜRE ").Append(FormatTime(time)); cachedStats=sb.ToString();
                }
                Shadowed(new Rect(24 * s, 18 * s, 500 * s, 160 * s), cachedStats, label);
            }

            // Sol alt: hız
            if (DrawLegacyHud && car != null && !car.IsEliminated)
            {
                int kmh = Mathf.RoundToInt(Mathf.Abs(car.ForwardSpeed) * 3.6f);
                if (kmh != cachedKmh) { cachedKmh=kmh; cachedSpeed=kmh+" km/sa"; }
                Shadowed(new Rect(24 * s, h - 70 * s, 400 * s, 60 * s), cachedSpeed, label);
            }

            // Geri sayım
            if (DrawLegacyHud && phase == RacePhase.Countdown && !manager.StartLightsOut)
            {
                // Geçici: Codex'in yarış arayüzü gelene kadar basit kırmızı ışıklar
                int lit = manager.StartLightsLit;
                float size = 70 * s, gap = 24 * s, rowW = RaceManager.StartLightCount * size + (RaceManager.StartLightCount - 1) * gap;
                Color old = GUI.color;
                for (int i = 0; i < RaceManager.StartLightCount; i++)
                {
                    var r = new Rect((w - rowW) * 0.5f + i * (size + gap), h * 0.18f, size, size);
                    GUI.color = new Color(0f, 0f, 0f, 0.8f);
                    GUI.DrawTexture(new Rect(r.x - 6 * s, r.y - 6 * s, r.width + 12 * s, r.height + 12 * s), Texture2D.whiteTexture);
                    GUI.color = i < lit ? new Color(1f, 0.08f, 0.05f) : new Color(0.18f, 0.03f, 0.03f);
                    GUI.DrawTexture(r, Texture2D.whiteTexture);
                }
                GUI.color = old;
            }
            else if (DrawLegacyHud && Time.time < goShownUntil)
            {
                Shadowed(new Rect(0, h * 0.25f, w, 200 * s), "<color=#7CFC00>BAŞLA!</color>", big);
            }

            // Harvester uyarısı
            Transform harvester = manager.HarvesterTransform;
            if (DrawLegacyHud && phase == RacePhase.Racing && harvester != null && car != null && !car.IsEliminated && (!hasMe || me.Status == RacerStatus.Racing))
            {
                // Tablanın önünden arabanın arkasına pist boyunca açık (harvester'ın tasması bununla ölçülür); yanımızda/arkamızdaysa düz mesafe
                float distance = Vector3.Distance(harvester.position, car.transform.position);
                var ai = HarvesterAI.Instance;
                if (ai != null)
                {
                    float open = ai.OpenGapTo(car);
                    if (open > -12f && open < 1000f) distance = Mathf.Max(0f, open);
                }
                if (distance < warnDistance)
                {
                    float t = Mathf.InverseLerp(warnDistance, dangerDistance, distance);
                    Color c = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.1f, 0.1f), t);
                    bool blink = t > 0.7f && Mathf.Repeat(Time.time * 4f, 1f) < 0.35f;
                    if (!blink)
                    {
                        int meters=Mathf.RoundToInt(distance);
                        if (meters != cachedDistance) { cachedDistance=meters; cachedHarvester="HARVESTER "+meters+" m!"; }
                        Color oldColor=center.normal.textColor; center.normal.textColor=c;
                        Shadowed(new Rect(0, 20 * s, w, 70 * s), cachedHarvester, center);
                        center.normal.textColor=oldColor;
                    }
                }
            }

            // Kişisel durum
            if (hasMe && phase != RacePhase.Finished)
            {
                if (me.Status == RacerStatus.Eliminated || (car != null && car.IsEliminated))
                {
                    Shadowed(new Rect(0, h * 0.35f, w, 90 * s), "<color=#FF3030>ELENDİN</color>", center);
                    if (spectating) Shadowed(new Rect(0, h * 0.35f + 80 * s, w, 50 * s), "Harvester'ı izliyorsun", centeredTable);
                }
                else if (me.Status == RacerStatus.Finished)
                {
                    Shadowed(new Rect(0, h * 0.35f, w, 90 * s), "<color=#7CFC00>BİTİRDİN — " + me.Position + ".</color>", center);
                }
            }

            if (phase == RacePhase.Finished) DrawResults(manager, w, h, s);
        }

        private void DrawResults(RaceManager manager, float w, float h, float s)
        {
            int rows = manager.Standings.Count;
            float rowH = 44 * s;
            float boxW = Mathf.Min(w - 32, 760 * s), boxH = (rows + 3) * rowH;
            Rect box = new Rect((w - boxW) * 0.5f, (h - boxH) * 0.5f, boxW, boxH);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;

            GUI.Label(new Rect(box.x, box.y + 6 * s, box.width, rowH), "SONUÇLAR", resultsTitle);
            ulong localId = Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening
                ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0;
            for (int i = 0; i < rows; i++)
            {
                var r = manager.Standings[i];
                string status;
                switch (r.Status)
                {
                    case RacerStatus.Finished: status = FormatTime(r.Time); break;
                    case RacerStatus.Eliminated: status = "<color=#FF5050>Elendi</color> (" + FormatTime(r.Time) + ")"; break;
                    case RacerStatus.Left: status = "<color=#AAAAAA>Çıktı</color>"; break;
                    default: status = "Bitiremedi"; break;
                }
                string name = r.Name.ToString();
                if (r.ClientId == localId) name = "<color=#FFD700>" + name + "</color>";
                float y = box.y + (i + 1.3f) * rowH;
                GUI.Label(new Rect(box.x + 30 * s, y, 70 * s, rowH), r.Position + ".", table);
                GUI.Label(new Rect(box.x + 100 * s, y, boxW * 0.5f, rowH), name, table);
                GUI.Label(new Rect(box.x + boxW * 0.6f, y, boxW * 0.4f, rowH), status, table);
            }
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
                GUI.Label(new Rect(box.x, box.yMax - rowH * 1.2f, box.width, rowH),
                    "Lobiye dönülüyor: " + Mathf.CeilToInt(manager.PhaseRemaining), centeredTable);
        }
    }
}
