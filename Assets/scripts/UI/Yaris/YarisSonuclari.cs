using System.Collections.Generic;
using System.Globalization;
using Harvester.Race;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Harvester.UI.Yaris
{
    public sealed class YarisSonuclari : MonoBehaviour
    {
        // Her yarış ayrı RaceManager ile yüklenir. Panelin yeniden açılması ikinci ödeme yapmaz.
        static readonly HashSet<RaceManager> paid = new HashSet<RaceManager>();
        GameObject panel;
        TMP_Text countdown, reward;
        readonly List<TMP_Text> rows = new List<TMP_Text>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPayments() { paid.Clear(); }
        public static int Prize(int position)
        {
            switch(position) { case 1:return 10000;case 2:return 5000;case 3:return 3000;case 4:return 2000;default:return 0; }
        }
        void Start()
        {
            var box=MenuWidgets.Column(transform,"Yarış sonuçları");panel=box.gameObject;
            MenuWidgets.Fill(box,new Vector2(.2f,.22f),new Vector2(.8f,.8f));
            MenuWidgets.Text(box,"YARIŞ SONUÇLARI",38,65).color=MenuWidgets.Accent;
            MenuWidgets.Text(box,"SIRA / İSİM                         SÜRE / ÖDÜL",22,42);
            for(int i=0;i<lobby.MaxPlayers;i++)rows.Add(MenuWidgets.Text(box,"",25,68));
            reward=MenuWidgets.Text(box,"",25,48);reward.color=MenuWidgets.Accent;
            countdown=MenuWidgets.Text(box,"",23,45);
            panel.SetActive(false);
        }
        void Update()
        {
            var manager=RaceManager.Instance;
            bool show=manager!=null&&manager.Phase==RacePhase.Finished;
            panel.SetActive(show);if(!show)return;
            ulong id=NetworkManager.Singleton!=null?NetworkManager.Singleton.LocalClientId:0;
            RacerState local;
            bool found=manager.TryGetState(id,out local);
            if(found&&local.Position>0&&!paid.Contains(manager))
            {
                paid.Add(manager);
                HurdaEkonomisi.ParaEkle(Prize(local.Position),"Yarış ödülü / "+local.Position+". sıra");
            }
            for(int i=0;i<rows.Count;i++)
            {
                if(i>=manager.Standings.Count){rows[i].text="";continue;}
                var s=manager.Standings[i];
                string result=s.Status==RacerStatus.Finished?Duration(s.Time):s.Status==RacerStatus.Left?"AYRILDI":"ELENDİ";
                rows[i].text=s.Position+". "+s.Name.ToString().Replace("<","‹")+"    "+result+"    "+HurdaEkonomisi.Dolar(Prize(s.Position));
                rows[i].color=s.ClientId==id?MenuWidgets.Accent:MenuWidgets.Ink;
            }
            reward.text=found?"KAZANDIN / "+HurdaEkonomisi.Dolar(Prize(local.Position)):"İZLEYİCİ / ÖDÜL YOK";
            countdown.text="Lobiye dönülüyor / "+Mathf.CeilToInt(manager.PhaseRemaining);
        }
        static string Duration(float t)
        { int m=Mathf.FloorToInt(t/60);return m.ToString("00")+":"+(t-m*60).ToString("00.00",CultureInfo.InvariantCulture); }
    }
}
