using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Play-editable tent rules (pitch / pack, sealing, campfire distance and the fire recovery bonus). Resources/ASH_TentSettings.</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Tent Settings", fileName = "ASH_TentSettings")]
    public sealed class ASHTentSettings : ScriptableObject
    {
        public enum Kind { Packed, Built }

        [Header("Sealing (tents are not structural: the quake still hits you)")]
        [Tooltip("Ash sealing of a pitched packed tent, with no fire.")]
        [Range(0f, 1f)] public float packedSealing = 0.6f;
        [Tooltip("Ash sealing of a built tent shelter, with no fire.")]
        [Range(0f, 1f)] public float builtSealing = 0.5f;

        [Header("Campfire")]
        [Tooltip("A lit campfire this close (metres) adds its heat (already) and the recovery bonus below.")]
        [Range(0.5f, 30f)] public float fireBonusRadius = 6f;
        [Tooltip("Stamina (and health) recovery multiplier while inside a tent with a lit campfire in range.")]
        [Range(1f, 5f)] public float fireRecoveryMultiplier = 1.5f;
        [Tooltip("Campfires cannot be placed closer than this to a tent (metres, from the tent volume).")]
        [Range(0f, 10f)] public float minCampfireDistance = 1.5f;
        [Tooltip("Only one pitched tent of each type at a time.")]
        public bool onePerType = true;

        [Header("Pack up")]
        [Range(0.2f, 8f)] public float packHoldSeconds = 1.6f;
        [Range(1f, 8f)] public float packUseRange = 3f;
        public string packPackedPrompt = "Pack up tent";
        public string packBuiltPrompt = "Pack up built tent";
        public string noSpaceMessage = "No room in the pack to take the tent down.";
        public string tooCloseTentMessage = "Too close to a tent";
        public string tooCloseFireMessage = "Too close to a campfire";
        public string alreadyPitchedMessage = "A tent of this type is already pitched";

        [Header("Labels")]
        public string packedLabel = "Packed Tent";
        public string builtLabel = "Built Tent";

        public float Sealing(Kind kind) => kind == Kind.Built ? builtSealing : packedSealing;
        public string PackPrompt(Kind kind) => kind == Kind.Built ? packBuiltPrompt : packPackedPrompt;

        static ASHTentSettings _i;
        public static ASHTentSettings I
        {
            get
            {
                if (!_i) { _i = Resources.Load<ASHTentSettings>("ASH_TentSettings"); if (!_i) _i = CreateInstance<ASHTentSettings>(); }
                return _i;
            }
        }
    }
}
