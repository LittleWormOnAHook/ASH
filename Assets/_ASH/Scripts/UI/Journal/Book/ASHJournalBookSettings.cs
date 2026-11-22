using UnityEngine;

namespace ASH.UI
{
    /// <summary>3D journal book (EndlessBook) settings. Resources/ASH_JournalBookSettings; editable in Play, edits persist (asset).
    /// The vendor prefab, clips and materials are wired by ASH/Systems/Journal/Setup Journal Book (idempotent).</summary>
    [CreateAssetMenu(menuName = "ASH/Journal Book Settings", fileName = "ASH_JournalBookSettings")]
    public sealed class ASHJournalBookSettings : ScriptableObject
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHJournalBookSettings() { _i = null; }
        static ASHJournalBookSettings _i;
        public static ASHJournalBookSettings I { get { if (!_i) { _i = Resources.Load<ASHJournalBookSettings>("ASH_JournalBookSettings"); if (!_i) _i = CreateInstance<ASHJournalBookSettings>(); } return _i; } }

        [Header("Book")]
        [Tooltip("Off (or EndlessBook missing) = the flat 1200x720 journal panel, exactly as before.")] public bool use3DBook = true;
        [Tooltip("EndlessBook's Book.prefab (vendor, git-ignored). Wired by the setup menu.")] public GameObject bookPrefab;
        [Tooltip("Hunger / thirst (and other survival drains) pause while the book is open.")] public bool pauseNeedsWhileOpen = true;

        [Header("Timing (seconds, unscaled time)")]
        [Range(0.05f, 3f)] public float openTime = 0.7f;
        [Range(0.05f, 3f)] public float closeTime = 0.55f;
        [Tooltip("One spread flip (arrows, d-pad, page edge, on-page arrows).")] [Range(0.05f, 3f)] public float singleTurnTime = 0.45f;
        [Tooltip("Total time for a ribbon jump across several spreads.")] [Range(0.05f, 4f)] public float groupTurnTime = 0.9f;
        [Tooltip("Max pages in the air during a ribbon jump.")] [Range(1, 10)] public int maxPagesTurning = 4;
        [Tooltip("Fade of the journal content after the book has opened.")] [Range(0f, 1f)] public float contentFadeTime = 0.15f;

        [Header("Stage (off-screen, rendered to a texture)")]
        [Tooltip("Unused layer for the book + its lights (30 is unnamed in ASH's tag manager).")] [Range(0, 31)] public int bookLayer = 30;
        public Vector3 stagePosition = new Vector3(0f, -5000f, 0f);
        [Tooltip("Book rotation on the stage (x tilts it towards the camera).")] public Vector3 bookEuler = new Vector3(0f, 0f, 0f);
        [Range(0.1f, 10f)] public float bookScale = 1f;
        [Tooltip("Camera direction from the book (looking at it). With autoFrame the distance is fitted to the open book.")] public Vector3 cameraOffset = new Vector3(0f, 3.4f, -1.25f);
        [Tooltip("Fit the camera distance so the open book fills the screen (uses the book's size).")] public bool autoFrame = true;
        [Tooltip("Open book width on screen (fraction of the screen width / height, whichever is tighter).")] [Range(0.3f, 1.2f)] public float frameFill = 0.86f;
        public Vector3 cameraLookOffset = new Vector3(0f, 0f, 0.05f);
        [Range(5f, 90f)] public float cameraFov = 34f;
        [Tooltip("Render texture size relative to the screen.")] [Range(0.25f, 2f)] public float renderScale = 1f;
        [Tooltip("MSAA samples for the book texture.")] [Range(1, 8)] public int msaa = 4;
        [Tooltip("High / Medium / Low open-middle stand-in when not animating.")] public Standin standinQuality = Standin.High;
        public enum Standin { High = 0, Low = 1, Medium = 2 }

        [Header("Look")]
        [Range(0f, 1f)] public float backgroundDim = 0.62f;
        public Color dimColor = new Color(0.03f, 0.025f, 0.02f, 1f);
        public Color keyLightColor = new Color(1f, 0.86f, 0.68f, 1f);
        [Range(0f, 20f)] public float keyLightIntensity = 2.2f;
        public Vector3 keyLightEuler = new Vector3(55f, -30f, 0f);
        public Color fillLightColor = new Color(0.55f, 0.6f, 0.7f, 1f);
        [Range(0f, 10f)] public float fillLightIntensity = 0.45f;
        [Tooltip("Canvas order of the book image (the HUD with the journal panel is 50).")] public int sortingOrder = 49;

        [Header("Materials (URP copies in Assets/_ASH/Materials/Journal, wired by the setup menu)")]
        public Material coverMaterial;
        public Material pageEdgeFrontMaterial;
        public Material pageEdgeBackMaterial;
        [Tooltip("Blank page material (paper). Page materials are instanced from it at runtime.")] public Material pageMaterial;
        [Tooltip("Left / right page paper (sheet-2 flattened pages). Null = the page material's own texture.")] public Texture2D pagePaperLeft, pagePaperRight;
        [Tooltip("Spreads in the book (each tab gets one in B2). B1 shows blank paper.")] [Range(1, 40)] public int spreads = 7;

        [Header("Audio (EndlessBook Demo 02 clips)")]
        public AudioClip openClip; public AudioClip closeClip; public AudioClip pageTurnClip; public AudioClip pagesFlippingClip;
        [Range(0f, 1f)] public float openVolume = 0.7f;
        [Range(0f, 1f)] public float closeVolume = 0.7f;
        [Range(0f, 1f)] public float pageTurnVolume = 0.6f;
        [Range(0f, 1f)] public float pagesFlippingVolume = 0.5f;
    }
}
