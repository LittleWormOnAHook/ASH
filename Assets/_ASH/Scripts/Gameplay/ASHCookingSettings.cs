using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Fire cooking / boiling tuning (Resources/ASH_CookingSettings; created by ASH/Systems/Fire Cooking).
    /// Cook times live on each Campfire recipe (cookGameMinutes, GAME minutes: 1 game hour = 2 real minutes at the 48-minute day).</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Cooking Settings", fileName = "ASH_CookingSettings")]
    public sealed class ASHCookingSettings : ScriptableObject
    {
        [Header("Time (game clock)")]
        [Tooltip("Multiplies every recipe's cook time (0.05 = nearly instant, 20 = very slow).")]
        [Range(0.05f, 20f)] public float cookTimeMultiplier = 1f;
        [Tooltip("Cook slower while the fire is low on fuel (follows the fire's heat; never below 25%).")]
        public bool scaleWithHeat = true;

        [Header("Fire")]
        [Tooltip("Items that can be on one fire at once (cooking or waiting to be taken).")]
        [Range(1, 8)] public int slotsPerFire = 3;
        [Tooltip("Fuel (game hours) the fire needs before something new can go on it.")]
        [Range(0f, 6f)] public float minFuelHoursToStart = 0.1f;
        [Tooltip("Extra fuel (game hours) burned per game hour for each item cooking. 0 = cooking costs no extra fuel.")]
        [Range(0f, 5f)] public float fuelDrawPerCookHour = 0f;
        [Tooltip("Food left on a lit fire this many game minutes after it is done burns and is lost. 0 = never burns. Water never burns.")]
        [Range(0f, 600f)] public float burnAfterGameMinutes = 0f;
        [Tooltip("When something finishes and the player is within this many metres it goes straight into the pack. 0 = always take it at the fire.")]
        [Range(0f, 30f)] public float autoCollectRange = 4f;

        [Header("Audio (CC0, Docs/AudioCredits.md; assigned by the setup menu)")]
        public AudioClip cookLoop;
        public AudioClip boilLoop;
        [Range(0f, 1f)] public float loopVolume = 0.45f;
        public AudioClip[] startClips;
        public AudioClip[] doneClips;
        [Range(0f, 1f)] public float sfxVolume = 0.6f;

        static ASHCookingSettings _i;
        public static ASHCookingSettings I { get { if (!_i) { _i = Resources.Load<ASHCookingSettings>("ASH_CookingSettings"); if (!_i) _i = CreateInstance<ASHCookingSettings>(); } return _i; } }

        public void ApplyPreset(int preset)
        {
            switch (preset)
            {
                case 0: cookTimeMultiplier = 0.4f; scaleWithHeat = false; slotsPerFire = 6; minFuelHoursToStart = 0f; fuelDrawPerCookHour = 0f; burnAfterGameMinutes = 0f; autoCollectRange = 10f; break;   // Relaxed
                case 2: cookTimeMultiplier = 3f; scaleWithHeat = true; slotsPerFire = 1; minFuelHoursToStart = 0.75f; fuelDrawPerCookHour = 1.5f; burnAfterGameMinutes = 15f; autoCollectRange = 0f; break; // Extreme
                default: cookTimeMultiplier = 1f; scaleWithHeat = true; slotsPerFire = 3; minFuelHoursToStart = 0.1f; fuelDrawPerCookHour = 0f; burnAfterGameMinutes = 0f; autoCollectRange = 4f; break;
            }
        }
        [ContextMenu("Preset/Relaxed")] void PresetRelaxed() => ApplyPreset(0);
        [ContextMenu("Preset/Default")] void PresetDefault() => ApplyPreset(1);
        [ContextMenu("Preset/Extreme (stress test)")] void PresetExtreme() => ApplyPreset(2);
    }
}
