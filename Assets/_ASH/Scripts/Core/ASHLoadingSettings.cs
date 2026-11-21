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
        [Tooltip("Show the screen for scene changes to the main menu too (async load + shader warmup).")]
        public bool coverMainMenuLoads = true;
        [Tooltip("t555: Play (or the built game) starting on the main menu: cover it while the menu's shaders and the game's shader variants warm up.")]
        public bool coverEditorPlayInMenu = true;
        [Tooltip("t555: warm the shader variant collection while on the menu screen too (the game scene load is then faster).")]
        public bool warmupOnMenu = true;
        [Tooltip("Seconds the menu gets to build its UI before warmup starts.")]
        [Range(0f, 10f)] public float menuSettleSeconds = 0.4f;
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

        [Header("Look: background (t557/t558)")]
        [Tooltip("Background image, faded. Default: Grandpa's cabin, Assets/_ASH/Art/UI/ASH_Loading_Cabin.png. Swap freely; a new image named ASH_Loading_Cabin.* is picked up automatically when the slot is empty.")]
        public Sprite background;
        [Tooltip("Colour behind the image (and the whole background when no image is set).")]
        public Color backgroundTint = new Color(0.03f, 0.028f, 0.025f, 1f);
        [Tooltip("How visible the image is (0 = hidden, 1 = full strength).")]
        [Range(0f, 1f)] public float backgroundOpacity = 0.7f;
        [Tooltip("Dark wash over the image so the circle and percentage read clearly.")]
        [Range(0f, 1f)] public float backgroundDarken = 0.35f;
        public Color darkenColor = new Color(0.02f, 0.018f, 0.016f, 1f);
        [Tooltip("Darker edges.")]
        [Range(0f, 1f)] public float vignette = 0.6f;
        [Tooltip("Image zoom (fills the screen, keeps aspect).")]
        [Range(1f, 2f)] public float backgroundZoom = 1.03f;
        [Tooltip("Slow push-in while loading (zoom added over 20 s). 0 = still.")]
        [Range(0f, 0.3f)] public float backgroundPushIn = 0.04f;

        [Header("Look: progress circle (saving-icon style, t556)")]
        [Tooltip("Circle diameter (pixels at 1920x1080).")]
        [Range(40f, 600f)] public float ringSize = 150f;
        [Tooltip("Ring line thickness, fraction of the radius.")]
        [Range(0.01f, 0.4f)] public float ringThickness = 0.06f;
        [Tooltip("Dot size, fraction of the circle diameter (saving icon: 0.235).")]
        [Range(0.02f, 0.6f)] public float dotSize = 0.12f;
        public Color ringColor = new Color(0.95f, 0.62f, 0.28f, 1f);
        public Color dotColor = new Color(0.93f, 0.9f, 0.84f, 1f);
        [Tooltip("Dot orbit speed, degrees per second (negative = clockwise... positive spins the same way as the saving icon).")]
        [Range(-1440f, 1440f)] public float ringSpeed = 240f;
        [Tooltip("Circle position from the screen centre.")]
        public Vector2 circleOffset = new Vector2(0f, 30f);
        [Range(10, 140)] public int percentFontSize = 44;
        public Color percentColor = new Color(0.93f, 0.9f, 0.84f, 1f);
        [Tooltip("Gap between the circle and the percentage.")]
        [Range(0f, 300f)] public float percentGap = 26f;
        [Tooltip("Small status line under the percentage (Loading / Preparing shaders / ...).")]
        public bool showStatusText = true;
        [Range(8, 80)] public int statusFontSize = 20;
        [Tooltip("How fast the shown percentage catches up with real progress (fraction per second). It always reaches 100% before the fade.")]
        [Range(0.1f, 10f)] public float progressSmoothing = 1.2f;

        [Header("Look: extras")]
        public bool showTitle = true;
        public bool showProgressBar = false;
        public bool showTips = true;
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
