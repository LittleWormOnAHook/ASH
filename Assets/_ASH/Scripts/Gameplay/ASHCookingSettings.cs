using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Campfire / stove cooking tuning (Resources/ASH_CookingSettings; created by ASH/Systems/Fire Cooking).
    /// Cook times are REAL seconds per item (recipe cookSeconds overrides the by-type defaults), sped up by the Cooking skill.</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Cooking Settings", fileName = "ASH_CookingSettings")]
    public sealed class ASHCookingSettings : ScriptableObject
    {
        [Header("Time (REAL seconds per item, before the Cooking skill)")]
        [Tooltip("Anything that is not meat / fish or water (berries, combos without meat).")]
        [Range(0.2f, 300f)] public float secondsDefault = 5f;
        [Tooltip("Meat and fish (raw meat, raw fish, stews, chili).")]
        [Range(0.2f, 300f)] public float secondsMeat = 10f;
        [Tooltip("Boiling water (creek water -> boiled water, broths, tea).")]
        [Range(0.2f, 300f)] public float secondsWater = 15f;
        [Tooltip("Multiplies every cook time (0.05 = nearly instant, 20 = very slow).")]
        [Range(0.05f, 20f)] public float cookTimeMultiplier = 1f;
        [Tooltip("Cook slower while the fire is low on fuel (follows the fire's heat; never below 25%).")]
        public bool scaleWithHeat = true;

        [Header("Fire / stove")]
        [Tooltip("Batches that cook at the same time on one fire (more wait in the queue).")]
        [Range(1, 8)] public int slotsPerFire = 2;
        [Tooltip("Batches one fire can hold (cooking + queued + waiting to be taken).")]
        [Range(1, 16)] public int maxBatchesPerFire = 6;
        [Tooltip("Most items in one batch (quantity selector max).")]
        [Range(1, 99)] public int maxPerBatch = 20;
        [Tooltip("Fuel (game hours) the fire needs before something new can go on it.")]
        [Range(0f, 6f)] public float minFuelHoursToStart = 0.1f;
        [Tooltip("Extra fuel (game hours) burned per game hour for each batch cooking. 0 = cooking costs no extra fuel.")]
        [Range(0f, 5f)] public float fuelDrawPerCookHour = 0f;
        [Tooltip("Cooked food left on a lit fire this many REAL seconds after it is done burns and is lost. 0 = never burns. Water never burns.")]
        [Range(0f, 900f)] public float burnAfterSeconds = 0f;
        [Tooltip("Finished items go straight into the pack while the player is within this many metres. 0 = always take them at the fire.")]
        [Range(0f, 30f)] public float autoCollectRange = 4f;
        [Tooltip("Object names that become cook stoves at runtime (case-insensitive 'contains'). Add ASHCookStove to anything else.")]
        public string[] stoveNamePatterns = { "stove", "furnace", "woodburner" };

        [Header("Cooking skill (learn by doing)")]
        [Tooltip("XP for each item cooked.")]
        [Range(0f, 200f)] public float xpPerItem = 6f;
        [Tooltip("Extra XP for each combined (two-ingredient) item cooked.")]
        [Range(0f, 200f)] public float xpPerComboItem = 10f;
        [Tooltip("XP for discovering a new combination.")]
        [Range(0f, 1000f)] public float xpDiscover = 40f;
        [Tooltip("XP to go from level L to L+1 = base * L^exponent.")]
        [Range(1f, 1000f)] public float xpBase = 30f;
        [Range(0f, 3f)] public float xpExponent = 1.35f;
        [Range(1, 50)] public int maxLevel = 20;
        [Tooltip("Cook time cut per level above 1 (0.04 = 4% faster each level).")]
        [Range(0f, 0.25f)] public float timeCutPerLevel = 0.04f;
        [Tooltip("Largest total cook time cut from the skill.")]
        [Range(0f, 0.95f)] public float maxTimeCut = 0.6f;
        [Tooltip("Chance per level above 1 that an item gives one extra output.")]
        [Range(0f, 0.2f)] public float bonusChancePerLevel = 0.015f;

        [Header("Combinations")]
        [Tooltip("A wrong pair wastes one of each ingredient (otherwise the items stay in the pack).")]
        public bool wrongGuessWastesItems = false;

        [Header("Water containers (refillable water bottle)")]
        [Tooltip("Percent of a container's water used for each item cooked when the container stands in for the water (boiling, stews, broths, tea).")]
        [Range(1f, 100f)] public float containerWaterPercentPerItem = 10f;

        [Header("Food safety")]
        [Tooltip("Multiplies every item's sickness chance (raw meat, creek water). 0 = never sick.")]
        [Range(0f, 3f)] public float sicknessChanceMultiplier = 1f;

        [Header("Audio (CC0, Docs/AudioCredits.md; assigned by the setup menu)")]
        public AudioClip cookLoop;
        public AudioClip boilLoop;
        [Range(0f, 1f)] public float loopVolume = 0.45f;
        public AudioClip[] startClips;
        public AudioClip[] doneClips;
        [Range(0f, 1f)] public float sfxVolume = 0.6f;

        static ASHCookingSettings _i;
        public static ASHCookingSettings I { get { if (!_i) { _i = Resources.Load<ASHCookingSettings>("ASH_CookingSettings"); if (!_i) _i = CreateInstance<ASHCookingSettings>(); } return _i; } }

        /// <summary>Cook-time factor from the Cooking skill level (1 = no cut).</summary>
        public float SkillTimeFactor(int level) => 1f - Mathf.Min(maxTimeCut, Mathf.Max(0, level - 1) * timeCutPerLevel);
        public float SkillBonusChance(int level) => Mathf.Clamp01(Mathf.Max(0, level - 1) * bonusChancePerLevel);

        public void ApplyPreset(int preset)
        {
            switch (preset)
            {
                case 0: // Relaxed
                    secondsDefault = 2f; secondsMeat = 4f; secondsWater = 6f; cookTimeMultiplier = 1f; scaleWithHeat = false; slotsPerFire = 4; maxBatchesPerFire = 10; maxPerBatch = 40;
                    minFuelHoursToStart = 0f; fuelDrawPerCookHour = 0f; burnAfterSeconds = 0f; autoCollectRange = 10f;
                    xpPerItem = 15f; xpPerComboItem = 20f; xpDiscover = 80f; xpBase = 20f; xpExponent = 1.2f; timeCutPerLevel = 0.05f; maxTimeCut = 0.7f; bonusChancePerLevel = 0.03f;
                    wrongGuessWastesItems = false; sicknessChanceMultiplier = 0.3f; containerWaterPercentPerItem = 5f; break;
                case 2: // Extreme (stress test)
                    secondsDefault = 30f; secondsMeat = 60f; secondsWater = 90f; cookTimeMultiplier = 1.5f; scaleWithHeat = true; slotsPerFire = 1; maxBatchesPerFire = 2; maxPerBatch = 3;
                    minFuelHoursToStart = 0.75f; fuelDrawPerCookHour = 1.5f; burnAfterSeconds = 20f; autoCollectRange = 0f;
                    xpPerItem = 2f; xpPerComboItem = 4f; xpDiscover = 15f; xpBase = 80f; xpExponent = 1.8f; timeCutPerLevel = 0.02f; maxTimeCut = 0.3f; bonusChancePerLevel = 0f;
                    wrongGuessWastesItems = true; sicknessChanceMultiplier = 2.5f; containerWaterPercentPerItem = 34f; break;
                default:
                    secondsDefault = 5f; secondsMeat = 10f; secondsWater = 15f; cookTimeMultiplier = 1f; scaleWithHeat = true; slotsPerFire = 2; maxBatchesPerFire = 6; maxPerBatch = 20;
                    minFuelHoursToStart = 0.1f; fuelDrawPerCookHour = 0f; burnAfterSeconds = 0f; autoCollectRange = 4f;
                    xpPerItem = 6f; xpPerComboItem = 10f; xpDiscover = 40f; xpBase = 30f; xpExponent = 1.35f; timeCutPerLevel = 0.04f; maxTimeCut = 0.6f; bonusChancePerLevel = 0.015f;
                    wrongGuessWastesItems = false; sicknessChanceMultiplier = 1f; containerWaterPercentPerItem = 10f; break;
            }
        }
        [ContextMenu("Preset/Relaxed")] void PresetRelaxed() => ApplyPreset(0);
        [ContextMenu("Preset/Default")] void PresetDefault() => ApplyPreset(1);
        [ContextMenu("Preset/Extreme (stress test)")] void PresetExtreme() => ApplyPreset(2);
    }
}
