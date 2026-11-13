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

        private GUIStyle label, big, center, table;
        private float scale = 1f;
        private float eliminatedAt = -1f;
        private bool spectating;
        private RacePhase lastPhase = RacePhase.Waiting;
        private float goShownUntil;
        private readonly StringBuilder sb = new StringBuilder();

        private void Update()
        {
            var manager = RaceManager.Instance;
            if (manager == null) return;

            if (manager.Phase == RacePhase.Racing && lastPhase == RacePhase.Countdown) goShownUntil = Time.time + 1.2f;
            lastPhase = manager.Phase;

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
            return System.Text.RegularExpressions.Regex.Replace(text, "<color=[^>]*>|</color>", "");
        }

        private static string FormatTime(float t)
        {
            int m = Mathf.FloorToInt(t / 60f);
            return m + ":" + (t - m * 60f).ToString("00.0");
        }

        private void OnGUI()
        {
            var manager = RaceManager.Instance;
            if (manager == null) return;
            EnsureStyles();
            float w = Screen.width, h = Screen.height, s = scale;
            var car = RaceCar.Local;

            RacerState me;
            bool hasMe = manager.TryGetLocalState(out me);
            int total = manager.Standings.Count;
            RacePhase phase = manager.Phase;

            // Sol üst: sıra, tur, süre
            if (hasMe && phase != RacePhase.Waiting)
            {
                sb.Length = 0;
                sb.Append("SIRA ").Append(me.Position).Append('/').Append(total).Append('\n');
                if (manager.LoopTrack) sb.Append("TUR ").Append(me.Lap).Append('/').Append(manager.Laps).Append('\n');
                sb.Append("SÜRE ").Append(FormatTime(me.Status == RacerStatus.Racing ? manager.RaceTime : me.Time));
                Shadowed(new Rect(24 * s, 18 * s, 500 * s, 160 * s), sb.ToString(), label);
            }

            // Sol alt: hız
            if (car != null && !car.IsEliminated)
            {
                int kmh = Mathf.RoundToInt(Mathf.Abs(car.ForwardSpeed) * 3.6f);
                Shadowed(new Rect(24 * s, h - 70 * s, 400 * s, 60 * s), kmh + " km/sa", label);
            }

            // Geri sayım
            if (phase == RacePhase.Countdown)
            {
                float remaining = manager.PhaseRemaining;
                string text = remaining > 3f ? "HAZIR" : Mathf.CeilToInt(remaining).ToString();
                Shadowed(new Rect(0, h * 0.25f, w, 200 * s), text, big);
            }
            else if (Time.time < goShownUntil)
            {
                Shadowed(new Rect(0, h * 0.25f, w, 200 * s), "<color=#7CFC00>BAŞLA!</color>", big);
            }

            // Harvester uyarısı
            Transform harvester = manager.HarvesterTransform;
            if (phase == RacePhase.Racing && harvester != null && car != null && !car.IsEliminated && (!hasMe || me.Status == RacerStatus.Racing))
            {
                float distance = Vector3.Distance(harvester.position, car.transform.position);
                if (distance < warnDistance)
                {
                    float t = Mathf.InverseLerp(warnDistance, dangerDistance, distance);
                    Color c = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.1f, 0.1f), t);
                    bool blink = t > 0.7f && Mathf.Repeat(Time.time * 4f, 1f) < 0.35f;
                    if (!blink)
                        Shadowed(new Rect(0, 20 * s, w, 70 * s),
                            "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">HARVESTER " + Mathf.RoundToInt(distance) + " m!</color>", center);
                }
            }

            // Kişisel durum
            if (hasMe && phase != RacePhase.Finished)
            {
                if (me.Status == RacerStatus.Eliminated || (car != null && car.IsEliminated))
                {
                    Shadowed(new Rect(0, h * 0.35f, w, 90 * s), "<color=#FF3030>ELENDİN</color>", center);
                    if (spectating) Shadowed(new Rect(0, h * 0.35f + 80 * s, w, 50 * s), "Harvester'ı izliyorsun", new GUIStyle(table) { alignment = TextAnchor.MiddleCenter });
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

            GUI.Label(new Rect(box.x, box.y + 6 * s, box.width, rowH), "SONUÇLAR", new GUIStyle(center) { fontSize = Mathf.RoundToInt(36 * s) });
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
                    "Lobiye dönülüyor: " + Mathf.CeilToInt(manager.PhaseRemaining), new GUIStyle(table) { alignment = TextAnchor.MiddleCenter });
        }
    }
}
