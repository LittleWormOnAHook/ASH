using System;
using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Clothing system settings (Resources/ASH_ClothingSettings). Every value is read live, so Play-mode edits apply at once
    /// and are kept (it is an asset). Wide ranges. Created / filled by ASH/Systems/Clothing/Setup Clothing (idempotent).</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Clothing Settings", fileName = "ASH_ClothingSettings")]
    public sealed class ASHClothingSettings : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHClothingSettings() { _i = null; }

        static ASHClothingSettings _i;
        public static ASHClothingSettings I { get { if (!_i) { _i = Resources.Load<ASHClothingSettings>("ASH_ClothingSettings"); if (!_i) _i = CreateInstance<ASHClothingSettings>(); } return _i; } }

        [Serializable]
        public sealed class SetBonus
        {
            public string setName = "Fur";
            [Range(1, 10)] public int piecesRequired = 3;
            [Range(0f, 100f)] public float coldResist;
            [Range(0f, 100f)] public float ashResist;
            [Range(-50f, 100f)] public float bonusHealth;
            [Range(-50f, 100f)] public float bonusStamina;
            [Range(0.5f, 1.5f)] public float staminaDrainMul = 1f;
            [Range(0.5f, 1.5f)] public float healthDrainMul = 1f;
            [TextArea(1, 2)] public string description;
        }

        [Header("Master switches")]
        [Tooltip("Off = worn clothing gives no stats (slots still work).")] public bool clothingStatsEnabled = true;
        [Tooltip("Set bonuses (Fur / Hide / Ash walker) on or off.")] public bool setBonusesEnabled = true;

        [Header("Caps (totals of everything worn)")]
        [Range(0f, 1f)] [Tooltip("Max cold resist (0.75 = body heat drops at 25% speed at most).")] public float maxColdResist = 0.75f;
        [Range(0f, 1f)] public float maxAshResist = 0.8f;
        [Range(0.1f, 1f)] [Tooltip("Lowest total drain multiplier (health / stamina / hunger / thirst).")] public float minDrainMultiplier = 0.6f;
        [Range(0f, 300f)] public float maxBonusHealth = 60f;
        [Range(0f, 300f)] public float maxBonusStamina = 60f;

        [Header("Condition (0..1 per worn piece)")]
        [Tooltip("Effects scale with condition: at 0 condition a piece gives this share of its stats.")] [Range(0f, 1f)] public float effectAtZeroCondition = 0f;
        [Tooltip("Below this condition the piece is shown as worn out.")] [Range(0f, 1f)] public float lowConditionThreshold = 0.3f;
        [Tooltip("Game hours of outdoor wear from 100% to 0% (0 = clothes never wear out).")] [Range(0f, 2000f)] public float wearHoursOutdoors = 240f;
        [Tooltip("Wear speed multiplier in ash / storms.")] [Range(0f, 10f)] public float stormWearMultiplier = 2f;
        [Tooltip("Found clothing (crates) rolls a condition in this range.")] [Range(0f, 1f)] public float foundConditionMin = 0.35f;
        [Range(0f, 1f)] public float foundConditionMax = 0.9f;

        [Header("Wet (rain / thunderstorm outdoors)")]
        [Tooltip("Share of cold resist lost by non-waterproof pieces while wet.")] [Range(0f, 1f)] public float wetColdPenalty = 0.35f;
        [Tooltip("Shelter sealing at or above this keeps you dry.")] [Range(0f, 1f)] public float dryWhenSealing = 0.5f;

        [Header("Set bonuses")]
        public List<SetBonus> sets = new List<SetBonus>();

        [Header("Rarity (pickup box + pip colours, loot weights)")]
        public Color commonColor = new Color32(0x8A, 0x7A, 0x66, 0xFF);
        public Color uncommonColor = new Color32(0x5E, 0x6B, 0x4A, 0xFF);
        public Color rareColor = new Color32(0x3D, 0x5A, 0x73, 0xFF);
        public Color legendaryColor = new Color32(0xB0, 0x8A, 0x3E, 0xFF);
        [Range(0f, 10f)] public float commonWeight = 2f;
        [Range(0f, 10f)] public float uncommonWeight = 0.8f;
        [Range(0f, 10f)] public float rareWeight = 0.35f;
        [Range(0f, 10f)] public float legendaryWeight = 0.05f;

        [Header("Pickups")]
        [Tooltip("Dropped wearables without their own pickup prefab show as a small square box in the rarity colour.")] public bool rarityBoxPickups = true;
        [Range(0.08f, 0.8f)] public float pickupBoxSize = 0.24f;

        [Header("Supply crates")]
        [Tooltip("Chance a supply crate also holds one piece of clothing (picked by rarity weight).")] [Range(0f, 1f)] public float crateClothingChance = 0.35f;
        [Tooltip("Legendary pieces never come from crates when off (hand-placed only).")] public bool legendaryInCrates = true;
        [Tooltip("Wearables crates can give (filled by the setup menu; edit freely).")] public List<ASHItemDefinition> crateClothing = new List<ASHItemDefinition>();

        [Header("Waterproofing (C3)")]
        [Tooltip("Item used from the pack to waterproof a piece.")] public string waterproofItemId = "waterproofing";
        [Tooltip("Hours of wet weather one tin keeps a piece dry (wears off only while wet). Stacks up to the cap.")] [Range(1f, 1000f)] public float waterproofHoursPerTin = 48f;
        [Range(1f, 5000f)] public float waterproofMaxHours = 144f;

        [Header("Repair (C3)")]
        [Tooltip("Thread used per repair.")] public string repairThreadId = "sinewthread";
        [Tooltip("Patch material for cloth pieces (first one carried is used).")] public List<string> repairClothIds = new List<string> { "cloth", "rags", "oldshirt" };
        [Tooltip("Patch material for hide / fur / leather pieces (falls back to cloth).")] public List<string> repairHideIds = new List<string> { "hidestrips", "rabbitfur" };
        [Tooltip("One of these must be carried (needle or knife).")] public List<string> repairToolIds = new List<string> { "boneneedle", "huntingknife", "boneknife", "antlerknife", "improvisedknife" };
        [Tooltip("Condition restored per repair (0..1).")] [Range(0.01f, 1f)] public float repairAmount = 0.35f;
        [Tooltip("Repairs can't raise condition above this.")] [Range(0.1f, 1f)] public float repairMaxCondition = 1f;
        [Tooltip("Crafting XP per repair.")] [Range(0f, 100f)] public float repairCraftingXp = 4f;

        [Header("World scatter (C4)")]
        [Tooltip("On a new game, scatter clothing pickups near existing loot spots (supply crates, scene pickups). Saved like dropped items.")] public bool worldScatter = true;
        [Range(0, 200)] public int scatterCount = 14;
        [Tooltip("Distance from the loot spot (min / max metres).")] [Range(0.5f, 30f)] public float scatterMinDistance = 1.5f;
        [Range(0.5f, 60f)] public float scatterMaxDistance = 6f;
        [Tooltip("Legendaries can be scattered when on (they are hand-placed otherwise).")] public bool legendaryInScatter = false;
        [Tooltip("0 = random every new game.")] public int scatterSeed = 0;
        [Tooltip("Seconds after the scene loads before scattering (lets a save restore first).")] [Range(0f, 30f)] public float scatterDelay = 4f;

        [Header("3D character visuals (C4)")]
        [Tooltip("Show wornVisualPrefab of worn pieces on the player.")] public bool showWornVisuals = true;
        [Tooltip("Debug: small rarity-coloured boxes at each worn slot's attach point (no art needed). Off by default.")] public bool showPlaceholderVisuals = false;
        [Range(0.02f, 0.5f)] public float placeholderSize = 0.08f;
        [Tooltip("Default bone + offset per wear slot (item wornAttach overrides).")] public List<SlotAttach> slotAttach = new List<SlotAttach>();

        [Serializable]
        public sealed class SlotAttach
        {
            public ASHWearSlot slot;
            public HumanBodyBones bone = HumanBodyBones.Chest;
            public Vector3 localPosition;
            public Vector3 localEuler;
        }

        public SlotAttach AttachFor(ASHWearSlot slot)
        {
            foreach (var a in slotAttach) if (a != null && a.slot == slot) return a;
            switch (slot)
            {
                case ASHWearSlot.Head: return new SlotAttach { slot = slot, bone = HumanBodyBones.Head, localPosition = new Vector3(0f, 0.12f, 0f) };
                case ASHWearSlot.Face: return new SlotAttach { slot = slot, bone = HumanBodyBones.Head, localPosition = new Vector3(0f, 0.06f, 0.1f) };
                case ASHWearSlot.Hands: return new SlotAttach { slot = slot, bone = HumanBodyBones.RightHand };
                case ASHWearSlot.Legs: return new SlotAttach { slot = slot, bone = HumanBodyBones.Hips, localPosition = new Vector3(0f, -0.15f, 0f) };
                case ASHWearSlot.Feet: return new SlotAttach { slot = slot, bone = HumanBodyBones.RightFoot };
                case ASHWearSlot.Backpack: return new SlotAttach { slot = slot, bone = HumanBodyBones.Chest, localPosition = new Vector3(0f, 0f, -0.2f) };
                case ASHWearSlot.Accessory: return new SlotAttach { slot = slot, bone = HumanBodyBones.Neck, localPosition = new Vector3(0f, 0f, 0.08f) };
                case ASHWearSlot.Outerwear: return new SlotAttach { slot = slot, bone = HumanBodyBones.Chest, localPosition = new Vector3(0f, 0f, 0.12f) };
                default: return new SlotAttach { slot = slot, bone = HumanBodyBones.Chest, localPosition = new Vector3(0f, 0f, 0.1f) };
            }
        }

        [Header("Recipes")]
        [Tooltip("Clothing recipes added to the player's crafting list at runtime (no player prefab / scene edits). Filled by the setup menu.")]
        public List<ASHRecipe> recipes = new List<ASHRecipe>();

        // Money moved to Resources/ASH_EconomySettings (Copper / Silver / Gold Bits). Kept hidden so old assets still load.
        [HideInInspector] public string currencyName = "";
        [HideInInspector] public int startingCurrency = 0;

        public Color RarityColor(ASHItemRarity r)
        {
            switch (r)
            {
                case ASHItemRarity.Uncommon: return uncommonColor;
                case ASHItemRarity.Rare: return rareColor;
                case ASHItemRarity.Legendary: return legendaryColor;
                default: return commonColor;
            }
        }

        public float RarityWeight(ASHItemRarity r)
        {
            switch (r)
            {
                case ASHItemRarity.Uncommon: return uncommonWeight;
                case ASHItemRarity.Rare: return rareWeight;
                case ASHItemRarity.Legendary: return legendaryWeight;
                default: return commonWeight;
            }
        }

        public SetBonus FindSet(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in sets) if (s != null && string.Equals(s.setName, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }
    }
}
