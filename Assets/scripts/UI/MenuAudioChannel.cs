using UnityEngine;
namespace Harvester.UI
{
    // Gelecekteki müzik/efekt kaynaklarına eklenir; kaynak sesini kategori tercihiyle çarpar.
    [RequireComponent(typeof(AudioSource))]
    public sealed class MenuAudioChannel : MonoBehaviour
    {
        public bool muzik;
        AudioSource source;
        float baseVolume;
        void Awake(){source=GetComponent<AudioSource>();baseVolume=source.volume;}
        void Update(){source.volume=baseVolume*(muzik?MenuSettings.MusicVolume:MenuSettings.EffectVolume);}
    }
}
