using UnityEngine;

namespace ASH.Inventory
{
    public enum ASHItemCategory { Resource, Consumable, Placeable }

    /// <summary>
    /// Item definition. Slimmed from Genesis Project.Data.ItemData (identity, icon, world prefab, max stack,
    /// type, survival restores); combat/progression/XP fields removed.
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

        [Header("Consumable restores (absolute points)")]
        public float restoreHunger;
        public float restoreThirst;
        public float restoreHealth;
        public float restoreStamina;

        [Header("Placeable")]
        [Tooltip("Spawned in the world when this item is placed.")]
        public GameObject placeablePrefab;
        [Tooltip("Footprint used for placement validity checks (metres).")]
        public Vector3 placementFootprint = new Vector3(1.4f, 0.6f, 1.4f);
        [Range(0f, 45f)] public float maxPlacementSlope = 22f;

        public bool IsConsumable => category == ASHItemCategory.Consumable;
        public bool IsPlaceable => category == ASHItemCategory.Placeable && placeablePrefab != null;
    }
}
