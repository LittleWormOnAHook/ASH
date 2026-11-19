using System.Collections.Generic;
using UnityEngine;

namespace ASH.Inventory
{
    public enum ASHItemCategory { Resource, Consumable, Placeable, Tool, Salvage, Medical, Key, Weapon, Ammo, Light }

    /// <summary>Where an equippable item goes: main hand (weapons, hatchet), off hand (torch, flashlight) or the left hip (lantern).</summary>
    public enum ASHEquipSlot { None, MainHand, OffHand, Hip }

    /// <summary>How a held item sits in the hand (arm hanging): Handle (long axis up out of the fist, head on top: hatchet, torch, bow,
    /// rifle carried upright), PistolGrip (barrel forward: pistol), Hang (hangs from a top handle: lantern), Beam (points forward: flashlight).</summary>
    public enum ASHGripStyle { Handle, PistolGrip, Hang, Beam }

    /// <summary>Tool roles an item can fill (any matching item in the pack satisfies a requirement).</summary>
    public enum ASHToolType { None, Axe, Knife, CanOpener, GPS }

    /// <summary>
    /// Item definition. Slimmed from Genesis Project.Data.ItemData (identity, icon, world prefab, max stack,
    /// type, survival restores); combat/progression/XP fields removed. ASH adds tool roles, can-opening,
    /// leftovers (empty tins) and break-down (salvage) yields.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Inventory/Item Definition", fileName = "ASH_Item_New")]
    public sealed class ASHItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string itemId = "item";
        public string displayName = "New Item";
        [TextArea(2, 3)] public string description;
        public Sprite icon;
        public Color uiTint = new Color(0.85f, 0.8f, 0.7f);
        public ASHItemCategory category = ASHItemCategory.Resource;
        [Min(1)] public int maxStack = 20;
        [Tooltip("Verb shown when used, e.g. Eat, Drink, Apply.")]
        public string useVerb = "Use";
        [Tooltip("Key item (Grandpa's journal): stays in the pack. It can only be moved between slots, never dropped, consumed, split or destroyed.")]
        public bool keyItem;

        [Header("Consumable restores (absolute points)")]
        public float restoreHunger;
        public float restoreThirst;
        public float restoreHealth;
        public float restoreStamina;
        [Tooltip("Canned goods: needs a can opener, knife or hatchet to open cleanly.")]
        public bool requiresOpener;
        [Range(0f, 1f)] [Tooltip("Share of restores kept when smashed open without a tool.")]
        public float smashedYield = 0.65f;
        [Tooltip("Given back after use (e.g. empty tin can). Not returned when a can is smashed open.")]
        public ASHItemDefinition leftoverItem;

        [Header("Food safety (raw meat, creek water)")]
        [Range(0f, 1f)] [Tooltip("Chance eating / drinking this makes you sick. 0 = safe (cooked food, boiled water). Scaled by ASH_CookingSettings.sicknessChanceMultiplier.")]
        public float sicknessChance;
        [Range(0f, 100f)] [Tooltip("Health lost when it makes you sick.")]
        public float sicknessHealthLoss = 8f;
        [Range(0f, 100f)] [Tooltip("Thirst lost when it makes you sick (vomiting).")]
        public float sicknessThirstLoss = 10f;
        [Range(0f, 100f)] [Tooltip("Hunger lost when it makes you sick.")]
        public float sicknessHungerLoss = 6f;

        [Header("Tool")]
        public ASHToolType toolType = ASHToolType.None;

        [Header("Break down / harvest")]
        public List<ASHRecipeIngredient> breakdownYield = new List<ASHRecipeIngredient>();
        public ASHToolType breakdownTool = ASHToolType.None;
        [Tooltip("If true the tool is mandatory; otherwise it only speeds up and improves the yield.")]
        public bool breakdownToolRequired;
        [Range(0f, 15f)] public float breakdownSeconds = 2f;
        [Range(0f, 1f)] [Tooltip("Yield multiplier without the (optional) tool. Rounds down, minimum 1.")]
        public float breakdownNoToolYield = 0.5f;

        [Header("World")]
        [Tooltip("World pickup spawned when this item is dropped from the pack (ASH_Pickup_* prefab with ASHItemPickup). Filled by ASH/Build/Link Item Pickups; a simple pickup is made at runtime when empty.")]
        public GameObject worldPickupPrefab;

        [Header("Placeable")]
        [Tooltip("Spawned in the world when this item is placed.")]
        public GameObject placeablePrefab;
        [Tooltip("Footprint used for placement validity checks (metres).")]
        public Vector3 placementFootprint = new Vector3(1.4f, 0.6f, 1.4f);
        [Range(0f, 45f)] public float maxPlacementSlope = 22f;

        [Header("Equip / hold (weapons, hatchet, light sources)")]
        public ASHEquipSlot equipSlot = ASHEquipSlot.None;
        [Tooltip("Held visual spawned in the hand (ASH wrapper of the vendor model, no pickup/collider).")]
        public GameObject heldPrefab;
        [Tooltip("Extra grip offset on top of the automatic fist alignment (metres / degrees, grip space). Tune per model.")]
        public Vector3 holdPosition;
        public Vector3 holdEuler;
        public ASHGripStyle gripStyle = ASHGripStyle.Handle;
        [Tooltip("World scale of the held visual (1 = model size). Scales about the grip point, so the hand stays on the grip.")]
        [Range(0.1f, 3f)] public float heldScale = 1f;
        [Tooltip("Weapons: ammo consumed by a shot (rifle rounds, pistol rounds, arrows).")]
        public ASHItemDefinition ammoItem;

        [Header("Light source (lantern, torch, flashlight)")]
        [Tooltip("Fuel item poured in from the pack (rubbing alcohol, battery). None = the item burns itself (torch).")]
        public ASHItemDefinition fuelItem;
        [Range(10f, 7200f)] [Tooltip("Seconds of light per fuel unit, or the burn time of one item when there is no fuel item.")]
        public float fuelSecondsPerUnit = 480f;
        [Range(1, 5)] [Tooltip("Tank size in fuel units.")]
        public int fuelCapacityUnits = 2;

        public bool IsEquippable => equipSlot != ASHEquipSlot.None;
        public bool IsLightSource => category == ASHItemCategory.Light;
        public bool IsWeapon => category == ASHItemCategory.Weapon || (equipSlot == ASHEquipSlot.MainHand && toolType == ASHToolType.Axe);

        public bool IsConsumable => category == ASHItemCategory.Consumable || category == ASHItemCategory.Medical;
        public bool IsPlaceable => category == ASHItemCategory.Placeable && placeablePrefab != null;
        public bool CanBreakDown => breakdownYield != null && breakdownYield.Count > 0;
    }
}
