using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>t478/t484: harvest highlight look. Lives in its own file so Unity binds Resources/ASH_OutlineSettings to it (inside
    /// ASHHarvestOutline.cs the asset had no script, never loaded, and logged "referenced script is missing"). Resources/ASH_OutlineSettings (edits in Play are kept). The range is NOT set here:
    /// the highlight follows the interactor's own harvest/use distance (only the current target inside that range lights up).
    /// Solid meshes (rocks, crates) use the inverted-hull outline; alpha-cut foliage uses a clipped gold rim/glow instead.</summary>
    [CreateAssetMenu(menuName = "ASH/Harvest Outline Settings", fileName = "ASH_OutlineSettings")]
    public sealed class ASHOutlineSettings : ScriptableObject
    {
        static ASHOutlineSettings _i;
        public static ASHOutlineSettings I => _i ? _i : (_i = Resources.Load<ASHOutlineSettings>("ASH_OutlineSettings"));
        [Tooltip("Highlight on harvestable resources (rocks, trees, logs, bushes, plants) while they are targeted within harvest distance.")]
        public bool outlineEnabled = true;
        public Color color = new Color(1f, 0.85f, 0.45f, 1f);
        [Tooltip("Outline width in screen pixels (solid meshes).")] [Range(0.25f, 16f)] public float widthPixels = 2.5f;
        [Tooltip("Inverted-hull outline shader (kept referenced so it is in builds).")] public Shader shader;
        [Tooltip("Max mesh parts highlighted per resource (big prefabs).")] [Range(1, 64)] public int maxParts = 16;
        [Header("Foliage (alpha-cut leaves, branches, grass)")]
        [Tooltip("Glow shader that clips to the leaf texture (kept referenced so it is in builds).")] public Shader foliageShader;
        [Tooltip("Solid meshes (rocks, crates, trunks). Auto = outline.")] public ASHOutlineMode solidMode = ASHOutlineMode.Outline;
        [Tooltip("Alpha-cut foliage (bushes, tree crosses, grass, plant fiber). Auto = glow.")] public ASHOutlineMode foliageMode = ASHOutlineMode.Auto;
        [Tooltip("Gold tint on the leaf pixels themselves.")] [Range(0f, 2f)] public float foliageTintStrength = 0.7f;
        [Tooltip("Higher = tighter rim on leaf edges.")] [Range(0.25f, 12f)] public float foliageRimPower = 2.5f;
        [Tooltip("Subtle brightness pulse on foliage (0 = still).")] [Range(0f, 1f)] public float foliagePulse = 0.12f;
    }
}
