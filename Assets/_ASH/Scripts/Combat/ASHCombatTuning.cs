using UnityEngine;

namespace ASH.Combat
{
    /// <summary>
    /// Combat-wide tuning (Resources/ASH_CombatTuning). Defaults are the Genesis DM_CombatCoreProfile values (hit stop, poise,
    /// training dummy pivot kick, player stagger). Wide ranges; Relaxed / Default / Extreme presets.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Combat/Combat Tuning", fileName = "ASH_CombatTuning")]
    public sealed class ASHCombatTuning : ScriptableObject
    {
        public enum Preset { Relaxed, Default, Extreme }
        [Tooltip("Pick a preset, then press Apply Preset (context menu) or let ASH/Systems/Combat re-apply it.")]
        public Preset preset = Preset.Default;

        [Header("Global")]
        [Tooltip("Multiplier on all damage the player deals.")]
        [Range(0f, 10f)] public float outgoingDamage = 1f;
        [Tooltip("Multiplier on all damage the player takes (goes to ASH health).")]
        [Range(0f, 10f)] public float incomingDamage = 1f;

        [Header("Hit stop (Genesis: light 2, heavy 6, crit +2, weight +2 frames at 0.3 speed)")]
        public bool hitStop = true;
        [Range(0, 30)] public int hitStopLightFrames = 2;
        [Range(0, 30)] public int hitStopHeavyFrames = 6;
        [Range(0, 30)] public int hitStopCritBonusFrames = 2;
        [Range(0, 30)] public int hitStopWeightBonusFrames = 2;
        [Range(0f, 1f)] public float hitStopHoldSpeed = 0.3f;
        public bool hitStopFreezesAttacker = true;
        [Tooltip("Weapon body weight at or above this counts as heavy (Genesis heavyAttackWeightMultiplier 1.35).")]
        [Range(1f, 4f)] public float heavyWeight = 1.35f;

        [Header("Poise (Genesis)")]
        [Range(0f, 1000f)] public float dummyPoiseMax = 20f;
        [Range(0f, 1000f)] public float humanoidPoiseMax = 80f;
        [Range(0f, 1f)] public float maxPoiseFractionPerHit = 0.4f;
        [Range(0f, 100f)] public float poiseRegenPerSecond = 1f;
        [Range(0f, 60f)] public float poiseRegenDelay = 5f;
        [Range(0f, 5f)] public float poiseBreakKickScale = 0.83f;

        [Header("Training dummy (Genesis pivot kick)")]
        [Range(1f, 100000f)] public float dummyMaxHealth = 500f;
        [Range(0f, 60f)] public float dummyResetDelay = 2f;
        [Range(0.05f, 10f)] public float dummyTiltFrequency = 1f;
        [Range(0f, 5f)] public float dummyTiltDampingRatio = 0.79f;
        [Range(0f, 89f)] public float dummyMaxTiltDegrees = 55.8f;
        [Range(0f, 2000f)] public float dummyMeleeKick = 275f;
        [Range(0f, 2000f)] public float dummyRangedKick = 300f;
        [Range(0f, 5f)] public float dummyKickWindowSeconds = 0.3f;
        [Range(0f, 2000f)] public float dummyKickWindowCap = 140f;
        [Tooltip("Hits from closer than this count as melee for the kick (Genesis 3 m).")]
        [Range(0f, 20f)] public float dummyMeleeDistance = 3f;
        public bool damageNumbers = true;

        [Header("Player body weight (Genesis stagger rules)")]
        [Tooltip("Chance a hit staggers the player (hit reaction), lerped from min at full health to max near death.")]
        [Range(0f, 1f)] public float playerStaggerChanceMin = 0.2f;
        [Range(0f, 1f)] public float playerStaggerChanceMax = 0.4f;
        [Range(0f, 30f)] public float playerStaggerImmunitySeconds = 1.5f;
        [Range(0f, 200f)] public float playerStaggerStaminaCost = 18f;

        [Header("Hit marks")]
        [Tooltip("Marks kept per target (oldest goes first; Genesis 5).")]
        [Range(0, 50)] public int maxMarksPerTarget = 5;
        [Range(1f, 3600f)] public float markLifetime = 90f;
        [Range(0.01f, 1f)] public float markSize = 0.07f;
        public GameObject fleshHit, woodHit, stoneHit, defaultHit;
        public Material markMaterial;
        public Color fleshMarkColour = new Color(0.35f, 0.02f, 0.02f, 0.9f);
        public Color woodMarkColour = new Color(0.16f, 0.1f, 0.05f, 0.9f);

        [Header("Arrows")]
        [Tooltip("Chance a shot arrow breaks on impact (0 = every stuck arrow can be picked back up).")]
        [Range(0f, 1f)] public float arrowBreakChance = 0f;
        [Range(0, 500)] public int maxStuckArrows = 30;
        [Tooltip("Seconds a stuck arrow stays in what it hit before it is cleaned up.")]
        [Range(5f, 7200f)] public float stuckArrowSeconds = 600f;
        [Tooltip("Size of the shot / stuck arrow model (1 = the HQ Survival Pack wooden arrow, ~0.83 m).")]
        [Range(0.25f, 3f)] public float arrowVisualScale = 1f;

        [Header("Aim smoothing (ranged weapons)")]
        [Tooltip("Run the player Animator every rendered frame while aiming a gun or the bow, so Invector's aim IK updates every frame (fixes the aim jitter). Off = Invector default (physics rate).")]
        public bool perFrameAnimatorWhileAiming = true;
        [Tooltip("Also run it every frame whenever a gun or the bow is out, aiming or not.")]
        public bool perFrameAnimatorWhileRangedOut = false;

        [Header("Muzzle flash")]
        [Tooltip("Flash light time per shot (s); the flash particles are emitted once per shot, never looped.")]
        [Range(0.01f, 0.5f)] public float muzzleFlashSeconds = 0.06f;
        [Range(1, 10)] public int muzzleFlashParticles = 1;

        [Header("References (set by ASH/Systems/Combat)")]
        [Tooltip("Muzzle flash spawned at the gun muzzle per shot (WarFX if found).")]
        public GameObject muzzleFlash;
        [Tooltip("Invector shooter UI (crosshair / bow crosshair). Spawned once if the scene has no vControlAimCanvas.")]
        public GameObject shooterUiPrefab;
        [Tooltip("Invector shooter camera states: Aiming / CrouchingAiming are added to the runtime copy of the player's camera list.")]
        public Invector.vThirdPersonCameraListData shooterCameraStates;

        static ASHCombatTuning _live;
        public static ASHCombatTuning Live
        {
            get
            {
                if (!_live) _live = Resources.Load<ASHCombatTuning>("ASH_CombatTuning");
                if (!_live) { _live = CreateInstance<ASHCombatTuning>(); _live.name = "ASH_CombatTuning (defaults)"; }
                return _live;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _live = null;

        [ContextMenu("Apply Preset")]
        public void ApplyPreset() => ApplyPreset(preset);

        public void ApplyPreset(Preset p)
        {
            preset = p;
            // Default = Genesis values
            outgoingDamage = 1f; incomingDamage = 1f;
            hitStopLightFrames = 2; hitStopHeavyFrames = 6; hitStopCritBonusFrames = 2; hitStopWeightBonusFrames = 2; hitStopHoldSpeed = 0.3f;
            dummyMaxHealth = 500f; dummyResetDelay = 2f;
            playerStaggerChanceMin = 0.2f; playerStaggerChanceMax = 0.4f; playerStaggerImmunitySeconds = 1.5f; playerStaggerStaminaCost = 18f;
            arrowBreakChance = 0f; humanoidPoiseMax = 80f; dummyPoiseMax = 20f;
            if (p == Preset.Relaxed)
            {
                outgoingDamage = 1.5f; incomingDamage = 0.5f; playerStaggerChanceMin = 0.05f; playerStaggerChanceMax = 0.15f;
                playerStaggerImmunitySeconds = 3f; playerStaggerStaminaCost = 8f; arrowBreakChance = 0f;
            }
            else if (p == Preset.Extreme)
            {
                outgoingDamage = 0.7f; incomingDamage = 2.5f; playerStaggerChanceMin = 0.4f; playerStaggerChanceMax = 0.75f;
                playerStaggerImmunitySeconds = 0.6f; playerStaggerStaminaCost = 30f; arrowBreakChance = 0.5f;
                hitStopLightFrames = 3; hitStopHeavyFrames = 9; humanoidPoiseMax = 140f; dummyMaxHealth = 900f;
            }
        }
    }
}
