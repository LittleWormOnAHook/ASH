using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Campfire fuel tuning (Resources/ASH_CampfireSettings; created by ASH/Systems/Campfire Fuel Settings).
    /// All durations are GAME hours (1 game hour = 2 real minutes with the default 48-minute day).</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Campfire Settings", fileName = "ASH_CampfireSettings")]
    public sealed class ASHCampfireSettings : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHCampfireSettings()
        {
            _i = default;
        }

        [Serializable] public sealed class Fuel { public string itemId; [Range(0f, 48f)] public float hours = 1f; }

        [Header("Fuel (game hours)")]
        [Tooltip("Most fuel a fire can hold.")]
        [Range(0.1f, 72f)] public float maxFuelHours = 6f;
        [Tooltip("Fuel in a freshly placed (crafted) campfire.")]
        [Range(0f, 72f)] public float placedStartFuelHours = 1.5f;
        [Tooltip("Fuel in campfires authored in the scene (when their own Fuel Hours is -1).")]
        [Range(0f, 72f)] public float sceneStartFuelHours = 3f;
        [Tooltip("Burn speed (1 = one fuel hour per game hour, 0 = never burns down).")]
        [Range(0f, 50f)] public float burnRateMultiplier = 1f;
        [Tooltip("Pack items that can be burned, in the order they are taken, with the game hours each adds.")]
        public List<Fuel> fuels = new List<Fuel>
        {
            new Fuel { itemId = "firewood", hours = 1.5f },
            new Fuel { itemId = "reclaimedwood", hours = 1.25f },
            new Fuel { itemId = "deadwood", hours = 1f },
            new Fuel { itemId = "kindling", hours = 0.35f },
        };

        [Header("Low fuel")]
        [Tooltip("Below this many game hours the flame light dims.")]
        [Range(0f, 12f)] public float lowFuelHours = 0.5f;
        [Range(0f, 1f)] public float lowFuelLightScale = 0.45f;

        [Header("Interaction and audio")]
        [Range(0f, 5f)] public float putOutHoldSeconds = 0.6f;
        [Tooltip("Played when fuel is added (from Assets/Audio, assigned by the setup menu).")]
        public AudioClip[] addFuelClips;
        [Range(0f, 1f)] public float sfxVolume = 0.7f;

        static ASHCampfireSettings _i;
        public static ASHCampfireSettings I { get { if (!_i) { _i = Resources.Load<ASHCampfireSettings>("ASH_CampfireSettings"); if (!_i) _i = CreateInstance<ASHCampfireSettings>(); } return _i; } }

        public void ApplyPreset(int preset)
        {
            switch (preset)
            {
                case 0: maxFuelHours = 12f; placedStartFuelHours = 4f; sceneStartFuelHours = 6f; burnRateMultiplier = 0.5f; break;   // Relaxed
                case 2: maxFuelHours = 1f; placedStartFuelHours = 0.25f; sceneStartFuelHours = 0.25f; burnRateMultiplier = 20f; break; // Extreme: burns out in under a real minute
                default: maxFuelHours = 6f; placedStartFuelHours = 1.5f; sceneStartFuelHours = 3f; burnRateMultiplier = 1f; break;
            }
        }
        [ContextMenu("Preset/Relaxed")] void PresetRelaxed() => ApplyPreset(0);
        [ContextMenu("Preset/Default")] void PresetDefault() => ApplyPreset(1);
        [ContextMenu("Preset/Extreme (stress test)")] void PresetExtreme() => ApplyPreset(2);
    }
}
