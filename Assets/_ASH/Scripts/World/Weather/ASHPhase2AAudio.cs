using UnityEngine;

namespace ASH.World.Weather
{
    /// <summary>
    /// Clips used by the opening sequence and the weather ambience. Filled by ASH > Phase 2A > Install Opening Sequence from
    /// Assets/Audio (vendor library, gitignored) and Assets/_ASH/Audio/Phase2A (CC0 clips, see Docs/AudioCredits.md).
    /// Empty slots stay silent (the alert tone and radio static fall back to generated audio).
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Phase 2A Audio", fileName = "ASH_Phase2A_Audio")]
    public sealed class ASHPhase2AAudio : ScriptableObject
    {
        [Header("Radio")]
        public AudioClip[] radioMusic;
        [Tooltip("Empty = generated two-tone attention signal.")] public AudioClip alertTone;
        [Tooltip("Optional narration per broadcast line (same order as the opening profile's lines). Empty = subtitles only.")]
        public AudioClip[] broadcastVoice;
        [Tooltip("Looping radio noise bed. Empty = generated.")] public AudioClip staticLoop;
        public AudioClip[] staticBursts;
        [Header("Earthquake")]
        public AudioClip rumbleLoop;
        public AudioClip[] rumbleSwell;
        public AudioClip[] creaks;
        public AudioClip[] rattles;
        public AudioClip[] falls;
        [Header("Weather ambience")]
        public AudioClip windSoft, windStrong, windHowl;
        public AudioClip rainSoft, rainStrong;
        public AudioClip morningAmbience;
        public AudioClip[] thunder;
        [Header("Mix")]
        [Range(0f, 1f)] public float ambienceMaster = 0.8f;
        [Range(0f, 1f)] public float sfxMaster = 0.9f;
    }
}
