using UnityEngine;

namespace ASH.Combat
{
    /// <summary>Play-editable torch strike, burn and wolf-scare numbers. Edits are kept.</summary>
    [CreateAssetMenu(menuName = "ASH/Combat/Torch Settings", fileName = "ASH_TorchSettings")]
    public sealed class ASHTorchSettings : ScriptableObject
    {
        static ASHTorchSettings _live;
        public static ASHTorchSettings Live => _live ? _live : (_live = Resources.Load<ASHTorchSettings>("ASH_TorchSettings"));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _live = null;

        [Header("Strike")]
        [Tooltip("LMB torch strike is only available while the torch is lit. Unlit, it is just a light you haven't started.")]
        public bool requireLitToStrike = true;
        [Tooltip("Used only if there is no ASH_Weapon_Torch row. Prefer editing the weapon data for damage / reach / stamina.")]
        [Range(0f, 100f)] public float fallbackDamage = 6f;
        [Range(0f, 50f)] public float fallbackStamina = 8f;
        [Range(0.3f, 6f)] public float fallbackReach = 1.6f;

        [Header("Burn (lit strike only)")]
        [Range(0f, 30f)] public float burnSeconds = 3f;
        [Range(0f, 20f)] public float burnDps = 2f;
        [Range(0.1f, 2f)] public float burnTick = 0.5f;

        [Header("Wolves (lit torch)")]
        [Tooltip("Wolves back off when a lit torch is this close, even without a strike. Used by wildlife in M2-C8.")]
        [Range(1f, 30f)] public float scareRadius = 8f;
        [Range(0.5f, 30f)] public float scareSeconds = 6f;
        [Range(0.1f, 2f)] public float presenceInterval = 0.5f;
        [Tooltip("A direct torch hit refreshes the scare for this long (usually a bit longer than presence).")]
        [Range(0.5f, 30f)] public float hitScareSeconds = 8f;
    }
}
