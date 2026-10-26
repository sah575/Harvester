using TMPro;
using UnityEngine;

// Oyun içi ekran: kalan süre, rol, yaşayan robotlar, geri sayım ve sonuç.
public class GameHUD : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText, roleText, robotsText, centerText, subText;

    const string HarvesterColor = "#E03A2F";
    const string RobotColor = "#8FB3D9";

    private void Update()
    {
        var gm = GameManager.Instance;
        bool visible = gm != null && gm.IsSpawned && gm.Phase != MatchPhase.Waiting;
        SetVisible(visible);
        if (!visible) return;

        bool harvester = gm.IsLocalHarvester;
        roleText.text = harvester ? $"<color={HarvesterColor}>HARVESTER</color>" : $"<color={RobotColor}>ROBOT</color>";
        robotsText.text = $"Robotlar: {gm.AliveRobots}/{gm.TotalRobots}";

        float remaining = gm.RemainingSeconds;
        switch (gm.Phase)
        {
            case MatchPhase.Countdown:
                timerText.text = FormatTime(gm.MatchSeconds);
                centerText.text = Mathf.CeilToInt(remaining).ToString();
                subText.text = harvester
                    ? "Robotları makarayla avla, ekinleri biçerek saklanacak yer bırakma."
                    : "Ekinlerin arasında saklan ve süre bitene kadar hayatta kal.";
                break;

            case MatchPhase.Playing:
                timerText.text = remaining <= 30f ? $"<color={HarvesterColor}>{FormatTime(remaining)}</color>" : FormatTime(remaining);
                centerText.text = gm.MatchSeconds - remaining < 1.5f ? "BAŞLA!" : "";
                subText.text = "";
                break;

            case MatchPhase.Ended:
                timerText.text = FormatTime(0f);
                centerText.text = ResultTitle(gm.Result, harvester);
                subText.text = ResultReason(gm.Result) + $"\nLobiye dönülüyor... {Mathf.CeilToInt(remaining)}";
                break;
        }
    }

    private void SetVisible(bool visible)
    {
        timerText.gameObject.SetActive(visible);
        roleText.gameObject.SetActive(visible);
        robotsText.gameObject.SetActive(visible);
        centerText.gameObject.SetActive(visible);
        subText.gameObject.SetActive(visible);
    }

    private static string ResultTitle(MatchResult result, bool localIsHarvester)
    {
        bool harvesterWon = result == MatchResult.HarvesterWin;
        bool won = harvesterWon == localIsHarvester;
        string title = harvesterWon ? "HARVESTER KAZANDI" : "ROBOTLAR KAZANDI";
        return won ? $"<color=#7BD88F>{title}</color>" : $"<color={HarvesterColor}>{title}</color>";
    }

    private static string ResultReason(MatchResult result)
    {
        switch (result)
        {
            case MatchResult.RobotsWin: return "Süre doldu, robotlar hayatta kaldı.";
            case MatchResult.HarvesterWin: return "Hayatta robot kalmadı.";
            case MatchResult.HarvesterLeft: return "Harvester oyundan ayrıldı.";
            default: return "";
        }
    }

    private static string FormatTime(float seconds)
    {
        int total = Mathf.CeilToInt(seconds);
        return $"{total / 60}:{total % 60:00}";
    }
}
