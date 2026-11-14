using Harvester.Race;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Harvester.UI.Yaris
{
    // Son oylama özeti sahne geçişinde korunur; seçilen pist açılan sahneden okunur.
    public sealed class PistKarariBildirimi : MonoBehaviour
    {
        static bool pending, tie;
        GameObject notice;
        float until;
        public static void Capture(lobby n)
        {
            if(n==null||!n.InRoom)return;
            if(lobby.SinglePlayer){pending=false;return;}
            pending=n.AutoStartRemaining>=0;
            int maximum=0,count=0;
            for(int i=0;i<lobby.TrackNames.Length;i++)
            {
                int votes=n.TrackVotes(i);
                if(votes>maximum){maximum=votes;count=1;}
                else if(votes>0&&votes==maximum)count++;
            }
            tie=count>1;
        }
        void Start()
        {
            if(!pending)return;
            pending=false;
            int index=System.Array.IndexOf(lobby.TrackScenes,SceneManager.GetActiveScene().name);
            if(index<0)return;
            var box=MenuWidgets.Box(transform,"Pist kararı",new Color(.025f,.035f,.025f,.94f));
            MenuWidgets.Fill(box,new Vector2(.27f,.60f),new Vector2(.68f,.67f));
            var label=MenuWidgets.Text(box,(tie?"Eşitlik → rastgele: ":"Seçilen pist: ")+lobby.TrackNames[index],26);
            MenuWidgets.Fill((RectTransform)label.transform, new Vector2(.04f,0),new Vector2(.96f,1));
            label.alignment=TMPro.TextAlignmentOptions.Center;label.color=MenuWidgets.Accent;
            notice=box.gameObject;until=Time.unscaledTime+4;
        }
        void Update(){if(notice!=null&&Time.unscaledTime>=until){Destroy(notice);notice=null;}}
    }
}
