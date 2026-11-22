using UnityEngine;

namespace ASH.UI
{
    /// <summary>Item hover popup (Resources/ASH_ItemTooltipSettings). Read live, so Play-mode edits apply at once and are kept (it is an asset).
    /// Created by ASH/Systems/Journal/Setup Item Tooltip And Journal Art (idempotent).</summary>
    [CreateAssetMenu(menuName = "ASH/UI/Item Tooltip Settings", fileName = "ASH_ItemTooltipSettings")]
    public sealed class ASHItemTooltipSettings : ScriptableObject
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHItemTooltipSettings() { _i = null; }

        static ASHItemTooltipSettings _i;
        public static ASHItemTooltipSettings I { get { if (!_i) { _i = Resources.Load<ASHItemTooltipSettings>("ASH_ItemTooltipSettings"); if (!_i) _i = CreateInstance<ASHItemTooltipSettings>(); } return _i; } }

        [Header("Behaviour")]
        public bool tooltipsEnabled = true;
        [Tooltip("Seconds the pointer rests on a slot before the popup shows.")] [Range(0f, 3f)] public float hoverDelay = 0.35f;
        [Tooltip("Fade in / out time (s).")] [Range(0f, 1f)] public float fadeTime = 0.12f;
        [Tooltip("Show the popup beside a slot focused with the gamepad / keyboard (UI selection).")] public bool gamepadFocus = true;
        [Tooltip("Hide while a mouse button is held (dragging, clicking).")] public bool hideWhileMouseHeld = true;
        [Tooltip("Live refresh interval while shown (condition, counts, have/need).")] [Range(0.05f, 2f)] public float refreshInterval = 0.25f;

        [Header("Layout (pixels at 1920x1080)")]
        [Range(0.5f, 2.5f)] public float scale = 1f;
        [Range(200f, 900f)] public float maxWidth = 380f;
        [Range(24f, 160f)] public float iconSize = 64f;
        [Range(10, 40)] public int nameSize = 21;
        [Range(8, 30)] public int subSize = 14;
        [Range(8, 30)] public int bodySize = 15;
        [Tooltip("Size of the panel's torn 9-slice border on screen (1 = PNG pixels).")] [Range(0.2f, 2f)] public float panelBorderScale = 0.6f;
        [Tooltip("Text padding inside that border.")] [Range(-40f, 60f)] public float innerPadding = 6f;
        [Tooltip("Offset from the cursor (x right, y down).")] public Vector2 cursorOffset = new Vector2(22f, 18f);
        [Tooltip("Gap kept to the screen edges.")] [Range(0f, 80f)] public float screenPadding = 10f;
        [Tooltip("Canvas sorting order (above the HUD, menus and the journal book).")] [Range(0, 32000)] public int sortingOrder = 900;

        [Header("Look (Grandpa's pages)")]
        [Tooltip("Thin rarity-coloured strip on the panel's left edge.")] public bool rarityPip = true;
        [Range(2f, 14f)] public float rarityPipWidth = 5f;
        [Tooltip("Put the icon in ASH_UI_SlotFrame when that PNG is in.")] public bool useSlotFrame = true;
        public Color panelTint = Color.white;
        public Color ink = new Color32(0x38, 0x2E, 0x24, 0xFF);
        public Color dimInk = new Color32(0x6B, 0x58, 0x45, 0xFF);
        public Color goodInk = new Color32(0x3B, 0x63, 0x28, 0xFF);
        public Color badInk = new Color32(0x8E, 0x2D, 0x1E, 0xFF);
        public Color headerInk = new Color32(0x86, 0x55, 0x1C, 0xFF);
        [Tooltip("How far rarity colours are pulled towards the ink so names read on parchment.")] [Range(0f, 1f)] public float rarityInkMix = 0.35f;
        public Color copperInk = new Color32(0x8C, 0x48, 0x18, 0xFF);
        public Color silverInk = new Color32(0x56, 0x5E, 0x68, 0xFF);
        public Color goldInk = new Color32(0x86, 0x66, 0x10, 0xFF);
        [Range(0f, 1f)] public float ruleAlpha = 0.35f;

        [Header("Fields shown")]
        public bool showIcon = true;
        public bool showCategory = true;
        public bool showRarity = true;
        public bool showDescription = true;
        public bool showEffects = true;
        public bool showClothingStats = true;
        public bool showCondition = true;
        public bool showStack = true;
        public bool showValue = true;
        public bool showBreakdown = true;
        public bool showLevelLock = true;
        public bool showRecipe = true;
        [Tooltip("Short use hint (LMB wear / eat, [B] break down).")] public bool showUseHint = true;
    }
}
