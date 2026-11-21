using UnityEngine;

namespace ASH.UI
{
    /// <summary>M1 (t438): HUD objectives layout. The focused / main story objective sits on top, slightly larger with slightly darker
    /// text; up to <see cref="maxOtherObjectives"/> other active objectives are listed below it in the smaller style. Read live by
    /// the HUD (5 Hz), so edits in Edit and Play mode show at once and are kept (it is the asset itself, no runtime copy).</summary>
    [CreateAssetMenu(menuName = "ASH/Objective HUD Settings", fileName = "ASH_ObjectiveHudSettings")]
    public sealed class ASHObjectiveHudSettings : ScriptableObject
    {
        [Header("Focused / main objective (top)")]
        [Range(10, 72)] public int focusTitleSize = 30;
        public Color focusTitleColor = new Color(0.80f, 0.50f, 0.20f);
        [Range(8, 60)] public int focusLineSize = 22;
        public Color focusLineColor = new Color(0.80f, 0.77f, 0.72f);
        [Tooltip("Gap between the focused title and its lines (px).")] [Range(0f, 40f)] public float titleGap = 8f;

        [Header("Other active objectives (below)")]
        [Tooltip("How many other active objectives are listed under the focused one.")] [Range(0, 8)] public int maxOtherObjectives = 2;
        [Range(8, 60)] public int otherTitleSize = 20;
        public Color otherTitleColor = new Color(0.95f, 0.62f, 0.28f);
        [Range(8, 60)] public int otherLineSize = 17;
        public Color otherLineColor = new Color(0.93f, 0.90f, 0.85f);
        [Tooltip("Show only the current step of each other objective (off = all its open steps).")] public bool otherCurrentStepOnly = true;

        [Header("More hint")]
        public bool showMoreHint = true;
        [Range(8, 40)] public int moreHintSize = 15;
        public Color moreHintColor = new Color(0.72f, 0.68f, 0.62f);
        public Color doneLineColor = new Color(0.62f, 0.83f, 0.61f);

        static ASHObjectiveHudSettings _i;
        public static ASHObjectiveHudSettings I
        {
            get
            {
                if (_i) return _i;
                _i = Resources.Load<ASHObjectiveHudSettings>("ASH_ObjectiveHudSettings");
#if UNITY_EDITOR
                if (!_i)
                {
                    _i = CreateInstance<ASHObjectiveHudSettings>();
                    System.IO.Directory.CreateDirectory("Assets/_ASH/Resources");
                    UnityEditor.AssetDatabase.CreateAsset(_i, "Assets/_ASH/Resources/ASH_ObjectiveHudSettings.asset");
                    UnityEditor.AssetDatabase.SaveAssets();
                    Debug.Log("[ASH] Created Assets/_ASH/Resources/ASH_ObjectiveHudSettings.asset (objective HUD sizes, colours, count)");
                }
#endif
                if (!_i) _i = CreateInstance<ASHObjectiveHudSettings>(); // build without the asset: defaults
                return _i;
            }
        }
    }
}
