using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Weapon / tool / light sounds, referenced straight from the audio library (Assets/Audio, kept out of git) by
    /// ASH/Build/Sound Bank (ASHSoundBankSetup). Loaded from Resources/ASH_SoundBank. Empty slots simply stay silent.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Sound Bank", fileName = "ASH_SoundBank")]
    public sealed class ASHSoundBank : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHSoundBank()
        {
            _i = default;
            _tried = default;
        }

        [Header("Bow")] public AudioClip[] bowDraw; public AudioClip[] bowRelease; public AudioClip[] arrowFlight;
        [Header("Arrow impacts")] public AudioClip[] arrowImpactWood; public AudioClip[] arrowImpactSnow; public AudioClip[] arrowImpactRock;
        [Header("Guns")] public AudioClip[] rifleShot; public AudioClip[] rifleBolt; public AudioClip[] pistolShot; public AudioClip[] dryFire;
        [Header("Axe / trees (ASHChoppable keeps its own chop + fall clips; these fill in when it has none)")]
        public AudioClip[] axeChop; public AudioClip[] treeCrack; public AudioClip[] treeFall;
        [Header("Lights")] public AudioClip[] torchOn; public AudioClip[] torchOff; public AudioClip[] flashlightOn; public AudioClip[] flashlightOff; public AudioClip[] lanternOn; public AudioClip[] lanternOff;
        [Header("Mix")]
        [Range(0f, 1f)] public float volume = 0.9f;
        [Range(0f, 1f)] public float gunVolume = 0.8f;
        [Range(0f, 1f)] public float bowVolume = 0.8f;
        [Range(0f, 1f)] public float impactVolume = 0.8f;
        [Range(0f, 1f)] public float lightVolume = 0.6f;
        [Range(0.5f, 2f)] public float pistolPitch = 1.15f;

        static ASHSoundBank _i; static bool _tried;
        public static ASHSoundBank Instance { get { if (!_i && !_tried) { _tried = true; _i = Resources.Load<ASHSoundBank>("ASH_SoundBank"); } return _i; } }
    }

    /// <summary>One-shot 3D sounds (pooled sources). Records the last clip played per key for the tests.</summary>
    public static class ASHSfx
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHSoundBank()
        {
            _next = default;
            LastKey = "";
            LastClip = "";
            Played = default;
            LastByKey.Clear();
        }

        static readonly AudioSource[] _pool = new AudioSource[16]; static int _next;
        public static string LastKey { get; private set; } = ""; public static string LastClip { get; private set; } = "";
        public static int Played { get; private set; }
        public static readonly System.Collections.Generic.Dictionary<string, string> LastByKey = new System.Collections.Generic.Dictionary<string, string>();

        public static AudioClip Pick(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            int n = 0; foreach (var c in clips) if (c) n++; if (n == 0) return null;
            int k = Random.Range(0, n); foreach (var c in clips) if (c && k-- == 0) return c; return null;
        }

        /// <summary>Plays a random clip of the set at pos. spatial 0 = 2D (player's own weapon), 1 = 3D.</summary>
        public static bool Play(string key, AudioClip[] clips, Vector3 pos, float vol, float pitch = 1f, float pitchJitter = 0.05f, float spatial = 1f, float maxDistance = 80f)
        {
            var clip = Pick(clips); if (!clip) return false;
            var s = Source(); s.transform.position = pos; s.spatialBlend = spatial; s.maxDistance = maxDistance;
            s.pitch = pitch * (1f + Random.Range(-pitchJitter, pitchJitter));
            s.PlayOneShot(clip, Mathf.Clamp01(vol * ASH.Core.ASHGameSettings.CachedSfx));
            LastKey = key; LastClip = clip.name; LastByKey[key] = clip.name; Played++;
            return true;
        }

        static AudioSource Source()
        {
            int i = _next; _next = (_next + 1) % _pool.Length;
            if (!_pool[i])
            {
                var go = new GameObject("ASH_Sfx_" + i); Object.DontDestroyOnLoad(go); go.hideFlags = HideFlags.HideInHierarchy;
                var s = go.AddComponent<AudioSource>(); s.playOnAwake = false; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 2f; s.dopplerLevel = 0f;
                _pool[i] = s;
            }
            return _pool[i];
        }
    }
}
