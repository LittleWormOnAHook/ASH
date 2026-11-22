using UnityEngine;

namespace ASH.Core
{
    /// <summary>Save system tuning. Lives at Resources/ASH_SaveSettings (created by ASH/Systems/Install Data); defaults are used when missing.</summary>
    [CreateAssetMenu(menuName = "ASH/Core/Save Settings", fileName = "ASH_SaveSettings")]
    public sealed class ASHSaveSettings : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHSaveSettings()
        {
            _i = default;
        }

        [Header("Autosave")]
        [Tooltip("Real minutes between autosaves (0 = off). Skipped during quakes, aiming, placement, menus and death; retried every few seconds.")]
        [Range(0f, 60f)] public float autosaveMinutes = 5f;
        [Tooltip("Retry interval when an autosave was skipped (s).")]
        [Range(1f, 60f)] public float autosaveRetrySeconds = 5f;
        [Tooltip("Autosave when the player enters a shelter zone (sealing at least Shelter Autosave Min Sealing).")]
        public bool autosaveOnShelter = true;
        [Range(0f, 1f)] public float shelterAutosaveMinSealing = 0.5f;
        [Tooltip("Minimum real seconds between two shelter autosaves.")]
        [Range(0f, 900f)] public float shelterAutosaveCooldown = 90f;

        [Header("Slots")]
        [Range(1, 20)] public int manualSlots = 6;
        [Tooltip("Seconds the small 'Saving...' icon stays up.")]
        [Range(0.3f, 10f)] public float savingIconSeconds = 1.6f;
        [Tooltip("Fade from black after a load (s).")]
        [Range(0f, 5f)] public float loadFadeSeconds = 1.2f;

        static ASHSaveSettings _i;
        public static ASHSaveSettings I { get { if (!_i) { _i = Resources.Load<ASHSaveSettings>("ASH_SaveSettings"); if (!_i) _i = CreateInstance<ASHSaveSettings>(); } return _i; } }
    }
}
