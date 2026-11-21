using UnityEngine;

namespace ASH.Core
{
    /// <summary>Resources/ASH_LoadingSettings: loading screen shown when a game scene starts (New Game / Continue / Load, or Editor Play
    /// straight into the game scene). Editable in Play; edits made in Play stay (it's an asset).</summary>
    [CreateAssetMenu(menuName = "ASH/Loading Settings", fileName = "ASH_LoadingSettings")]
    public sealed class ASHLoadingSettings : ScriptableObject
    {
        static ASHLoadingSettings _i;
        public static ASHLoadingSettings I { get { if (!_i) { _i = Resources.Load<ASHLoadingSettings>("ASH_LoadingSettings"); if (!_i) _i = CreateInstance<ASHLoadingSettings>(); } return _i; } }

        [Header("General")]
        public bool loadingScreenEnabled = true;
        [Tooltip("Also cover Editor Play that starts directly in a game scene (Play From Main Menu off).")]
        public bool coverEditorPlayInGameScene = true;
        [Tooltip("Show the screen for scene changes to the main menu too (only the async load, no warmup).")]
        public bool coverMainMenuLoads = true;
        [Range(0f, 20f)] public float minDisplaySeconds = 2f;
        [Range(0.05f, 5f)] public float fadeInSeconds = 0.25f;
        [Range(0.05f, 5f)] public float fadeOutSeconds = 0.8f;

        [Header("Freeze until ready")]
        [Tooltip("Locks the player's input (Invector SetLockAllInput) while the screen is up.")]
        public bool lockPlayerInput = true;
        [Tooltip("Time scale while the world settles (spawners need time to run). 0 = fully frozen (spawners then fill in after the screen).")]
        [Range(0f, 1f)] public float settleTimeScale = 1f;
        [Tooltip("Time scale during shader warmup / pre-render (0 = frozen).")]
        [Range(0f, 1f)] public float warmupTimeScale = 0f;

        [Header("Shader warmup")]
        public bool warmupShaders = true;
        [Tooltip("Made by ASH/Systems/Loading/Collect Shader Variants. Warmed up progressively behind the screen.")]
        public ShaderVariantCollection shaderVariants;
        [Range(1, 2000)] public int variantsPerFrame = 64;
        [Tooltip("Optional Unity 6 GraphicsStateCollection asset (warmed through reflection if the API exists).")]
        public Object graphicsStateCollection;
        [Range(1, 2000)] public int graphicsStatesPerFrame = 64;

        [Header("Pre-render (off-screen camera, renders the world around the player before the screen lifts)")]
        public bool prerenderWorld = true;
        [Range(4, 24)] public int prerenderDirections = 8;
        [Range(1, 10)] public int framesPerDirection = 2;
        [Tooltip("Pitch angles rendered for each direction.")]
        public float[] prerenderPitches = { 8f, -30f };
        [Tooltip("Prefabs whose meshes + materials are drawn in front of the off-screen camera (birds, wildlife, pickups...). Bird type prefabs are added automatically.")]
        public GameObject[] prewarmPrefabs = new GameObject[0];
        [Tooltip("Extra materials drawn on a quad in front of the off-screen camera.")]
        public Material[] prewarmMaterials = new Material[0];
        [Range(1, 30)] public int prewarmDrawFrames = 3;

        [Header("Wait for the world to settle")]
        [Range(0f, 30f)] public float settleMinSeconds = 1.5f;
        [Range(1f, 90f)] public float settleMaxSeconds = 15f;
        [Tooltip("The world counts as settled when the renderer count stays the same this long and frames are fast.")]
        [Range(0.1f, 10f)] public float settleStableSeconds = 0.75f;
        [Range(10f, 500f)] public float settleMaxFrameMs = 60f;

        [Header("Look")]
        [Tooltip("Optional background art. Empty = dark ash gradient.")]
        public Sprite background;
        public Color backgroundTint = new Color(0.06f, 0.055f, 0.05f, 1f);
        [Range(1f, 60f)] public float tipSeconds = 6f;
        [TextArea(1, 3)]
        public string[] tips =
        {
            "Wet clothes steal heat fast. Dry them by a fire before night falls.",
            "Birds scatter when you sprint. Crouch and move slowly to get close.",
            "A gunshot carries far. Wildlife for a long way around will hear it.",
            "Big birds defend their roost. Ravens and eagles will dive at you.",
            "Shelter with good sealing triggers an autosave.",
            "Feathers and small meat: harvest a downed bird with your knife.",
            "Cook small meat before eating it.",
            "Your dog can draw birds off you, and they can't kill it.",
        };
    }
}
