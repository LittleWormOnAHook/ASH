using UnityEngine;

namespace ASH.UI
{
    /// <summary>
    /// Top-centre HUD compass tuning (Resources/ASH_CompassSettings; created by ASH/Systems/Compass).
    /// The strip is built at runtime by the HUD; this asset is the only thing to edit.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/UI/Compass Settings", fileName = "ASH_CompassSettings")]
    public sealed class ASHCompassSettings : ScriptableObject
    {
        [Header("Layout (pixels, 1920x1080 reference)")]
        [Tooltip("Width of the scrolling strip.")]
        [Range(120f, 1400f)] public float width = 640f;
        [Tooltip("Height of the tick / marker strip (pointer and heading sit below this).")]
        [Range(20f, 140f)] public float stripHeight = 44f;
        [Tooltip("Gap from the top of the screen to the strip.")]
        [Range(0f, 120f)] public float topInset = 16f;
        [Range(1f, 8f)] public float borderThickness = 2f;
        [Range(4f, 24f)] public float pointerHeight = 8f;
        [Range(10f, 32f)] public float headingLabelHeight = 18f;

        [Header("Field of view")]
        [Tooltip("How many degrees of heading the strip shows (180 = half a turn, 360 = the whole compass).")]
        [Range(30f, 360f)] public float fieldOfViewDegrees = 140f;
        [Tooltip("Minor tick spacing in degrees. Cardinals and intercardinals (N, NE, E...) are always labelled.")]
        [Range(5f, 45f)] public float tickIntervalDegrees = 15f;

        [Header("Ticks and labels")]
        [Range(8, 36)] public int cardinalFontSize = 16;
        [Range(8, 28)] public int intercardinalFontSize = 12;
        [Range(2f, 20f)] public float cardinalTickHeight = 10f;
        [Range(2f, 20f)] public float minorTickHeight = 6f;
        [Tooltip("Three-digit heading under the centre pointer (000–359).")]
        public bool showHeadingDegrees = true;

        [Header("Markers")]
        [Range(4f, 48f)] public float markerSize = 12f;
        [Tooltip("Markers farther than this (metres) are hidden.")]
        [Range(20f, 8000f)] public float maxMarkerDistance = 400f;
        [Range(1, 64)] public int maxVisibleMarkers = 16;
        [Tooltip("Show a metres label under each dot.")]
        public bool showDistanceText = true;
        [Range(6, 22)] public int distanceFontSize = 10;
        [Tooltip("Fade dots as they approach max distance.")]
        public bool fadeByDistance = true;
        [Tooltip("Start fading once the marker is this fraction of max distance away (0 = fade immediately, 1 = never).")]
        [Range(0f, 1f)] public float fadeStartNormalized = 0.55f;
        [Range(0f, 1f)] public float minFadeAlpha = 0.3f;
        [Tooltip("Off: hide markers outside the strip's FOV (Genesis). On: pin them to the left or right edge.")]
        public bool clampOffArcToEdge = false;

        [Header("Colors")]
        public Color poiColor = new Color(0.38f, 0.72f, 1f, 1f);
        public Color enemyColor = new Color(0.95f, 0.28f, 0.24f, 1f);
        [Header("Tracked objective (t411)")]
        [Tooltip("Gold dot for the currently tracked MAIN objective (needs a world target).")] public bool showTrackedObjective = true;
        [Tooltip("Gold #FFC83D by default.")] public Color objectiveColor = new Color(1f, 0.784f, 0.239f, 1f);
        [Range(4f, 48f)] public float objectiveMarkerSize = 14f;
        [Tooltip("Pin the objective dot to the strip edge when it is behind you (other markers follow clampOffArcToEdge).")] public bool objectiveClampToEdge = true;
        [Tooltip("Dot alpha while pinned to the edge.")] [Range(0f, 1f)] public float objectiveEdgeAlpha = 0.6f;
        public Color cardinalColor = new Color(0.95f, 0.78f, 0.42f, 1f);
        public Color northColor = new Color(0.95f, 0.5f, 0.42f, 1f);
        public Color tickColor = new Color(0.93f, 0.9f, 0.84f, 0.7f);
        public Color intercardinalColor = new Color(0.85f, 0.82f, 0.76f, 0.92f);
        public Color headingColor = new Color(0.95f, 0.78f, 0.42f, 1f);
        public Color borderColor = new Color(0.86f, 0.68f, 0.34f, 0.45f);
        public Color distanceColor = new Color(0.93f, 0.9f, 0.84f, 0.95f);

        [Header("GPS gating")]
        [Tooltip("On: the compass strip always shows. Off: the whole compass waits until the GPS is carried.")]
        public bool compassAlwaysVisible = true;
        [Tooltip("TESTING: show the compass without using the Compass item (the cabin pickup). Off = hidden until the Compass item is used (saved with the game). Presets never change this.")]
        public bool compassAlwaysOn = false;
        [Tooltip("Known POI, survey and campfire dots only appear while the GPS is in the pack, hotbar or a quick slot.")]
        public bool poiMarkersRequireGps = true;
        [Tooltip("Registered enemy (red) markers require the GPS. Off: they show whenever the compass is up.")]
        public bool enemyMarkersRequireGps = false;

        [Header("Sources")]
        public bool showKnownPois = true;
        [Tooltip("Logged or fog-revealed survey points.")]
        public bool showSurveys = true;
        [Tooltip("Campfires (placed or already in the scene).")]
        public bool showCampfires = true;

        [Header("Visibility")]
        [Tooltip("Hide the compass while a full-screen panel is open (map, journal, inventory, pause, death).")]
        public bool hideWhenPanelsOpen = true;

        static ASHCompassSettings _i;
        public static ASHCompassSettings I
        {
            get
            {
                if (!_i)
                {
                    _i = Resources.Load<ASHCompassSettings>("ASH_CompassSettings");
                    if (!_i) _i = CreateInstance<ASHCompassSettings>();
                }
                return _i;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _i = null;

        /// <summary>0 Relaxed (wide, long range), 1 Default, 2 Extreme (narrow, short range, everything faded).</summary>
        public void ApplyPreset(int preset)
        {
            switch (preset)
            {
                case 0:
                    width = 900f; stripHeight = 52f; fieldOfViewDegrees = 180f; maxMarkerDistance = 1500f;
                    markerSize = 14f; fadeByDistance = false; clampOffArcToEdge = true; showDistanceText = true;
                    poiMarkersRequireGps = true; compassAlwaysVisible = true;
                    break;
                case 2:
                    width = 360f; stripHeight = 32f; fieldOfViewDegrees = 80f; maxMarkerDistance = 80f;
                    markerSize = 8f; fadeByDistance = true; fadeStartNormalized = 0.3f; minFadeAlpha = 0.15f;
                    clampOffArcToEdge = false; showDistanceText = false;
                    break;
                default:
                    width = 640f; stripHeight = 44f; fieldOfViewDegrees = 140f; maxMarkerDistance = 400f;
                    markerSize = 12f; fadeByDistance = true; fadeStartNormalized = 0.55f; minFadeAlpha = 0.3f;
                    clampOffArcToEdge = false; showDistanceText = true;
                    poiMarkersRequireGps = true; compassAlwaysVisible = true;
                    break;
            }
        }

        [ContextMenu("Preset/Relaxed")] void PresetRelaxed() => ApplyPreset(0);
        [ContextMenu("Preset/Default")] void PresetDefault() => ApplyPreset(1);
        [ContextMenu("Preset/Extreme (narrow)")] void PresetExtreme() => ApplyPreset(2);
    }
}
