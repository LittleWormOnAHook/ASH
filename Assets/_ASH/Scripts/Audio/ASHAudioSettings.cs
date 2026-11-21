using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace ASH.Audio
{
    /// <summary>Play-editable soundtrack and combat-music rules. Resources/ASH_AudioSettings.</summary>
    [CreateAssetMenu(menuName = "ASH/Audio/Audio Settings", fileName = "ASH_AudioSettings")]
    public sealed class ASHAudioSettings : ScriptableObject
    {
        [Header("Tracks (filled by ASH/Systems/Audio/Setup Music)")]
        [Tooltip("Every clip in Background Music and Combat except Forward2.")]
        public List<AudioClip> background = new List<AudioClip>();
        [Tooltip("Combat cue (Forward2). Fades in when a hostile aggros on the player or the dog.")]
        public AudioClip combat;

        [Header("Background")]
        [Range(0.2f, 12f)] public float crossfadeSeconds = 3f;
        [Range(0f, 30f)] public float gapMinSeconds = 2f;
        [Range(0f, 60f)] public float gapMaxSeconds = 8f;
        [Range(0f, 1f)] public float backgroundVolume = 0.7f;

        [Header("Combat (Forward2)")]
        [Range(0.1f, 8f)] public float combatFadeInSeconds = 1.5f;
        [Range(0.1f, 12f)] public float combatFadeOutSeconds = 3f;
        [Tooltip("How long after the last aggro (player or dog) before combat music starts fading out.")]
        [Range(0f, 60f)] public float combatEndDelaySeconds = 6f;
        [Range(0f, 1f)] public float combatVolume = 0.85f;
        [Range(0f, 1f)] public float backgroundDuck = 0.2f;

        [Header("Mixer (optional)")]
        [Tooltip("If set, background and combat play through this group. Otherwise volumes use the Music slider × these levels.")]
        public AudioMixerGroup musicGroup;

        [Header("New Game (Editor / development builds)")]
        [Tooltip("Shipped builds always wipe saves on New Game. In the Editor or a development build, saves are kept unless this is on.")]
        public bool wipeSavesOnNewGameInEditor;

        static ASHAudioSettings _i;
        public static ASHAudioSettings I => _i ? _i : (_i = Resources.Load<ASHAudioSettings>("ASH_AudioSettings"));
    }
}
