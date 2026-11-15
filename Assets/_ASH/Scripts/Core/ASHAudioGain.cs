using UnityEngine;

namespace ASH.Core
{
    /// <summary>Volume category gain applied as an audio filter on the AudioSource next to it (no mixer needed): scripts can keep
    /// writing AudioSource.volume and the player's Music / Ambience / SFX setting still applies.</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ASHAudioGain : MonoBehaviour
    {
        public enum Category { Music, Ambience, Sfx }
        public Category category = Category.Music;
        volatile float _gain = 1f;

        void Update()
        {
            _gain = category == Category.Music ? ASHGameSettings.CachedMusic : category == Category.Ambience ? ASHGameSettings.CachedAmbience : ASHGameSettings.CachedSfx;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float g = _gain; if (g >= 0.999f) return;
            for (int i = 0; i < data.Length; i++) data[i] *= g;
        }
    }
}
