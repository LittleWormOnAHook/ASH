using UnityEngine;

namespace ASH.World.Accumulation
{
    /// <summary>t471: the LOOK of the snow/ash accumulation (colours, surface textures, grain, sparkle, macro variation).
    /// Lives in Resources/ASH_AccumulationLook so edits made in Play are kept. ASHAccumulationController reads it every frame
    /// (it overrides the controller's old Look / Surface Texture fields, which were migrated into it 1:1).
    /// Coverage, drift, banking and base edge blending stay on the controller and are not touched by this asset.</summary>
    [CreateAssetMenu(menuName = "ASH/Accumulation Look Settings", fileName = "ASH_AccumulationLook")]
    public sealed class ASHAccumulationLookSettings : ScriptableObject
    {
        static ASHAccumulationLookSettings _i;
        public static ASHAccumulationLookSettings I => _i ? _i : (_i = Resources.Load<ASHAccumulationLookSettings>("ASH_AccumulationLook"));

        [Header("Colours (migrated from the controller)")]
        public Color ashColor = new Color(0.44f, 0.44f, 0.46f);
        [Range(0.1f, 1f)] public float wetAshDarkness = 0.5f;
        [Tooltip("How much snow/ash colour stays in footprints and trails (0 = bare ground, 1 = trails only dent).")] [Range(0f, 1f)] public float trailCoverageKeep = 0.75f;
        public Color snowColor = new Color(0.93f, 0.94f, 0.96f);

        [Header("Surface textures (migrated from the controller)")]
        [Tooltip("Ash albedo (world-projected).")] public Texture2D ashTexture;
        [Tooltip("Ash normal map (world-projected). Optional.")] public Texture2D ashNormal;
        [Tooltip("Snow albedo (world-projected).")] public Texture2D snowTexture;
        [Tooltip("Snow normal map (world-projected). Optional.")] public Texture2D snowNormal;
        [Tooltip("Metres per texture repeat.")] [Range(0.05f, 50f)] public float ashTiling = 3f, snowTiling = 4f;
        [Tooltip("Strength of the ash/snow normal maps.")] [Range(0f, 4f)] public float surfaceNormalStrength = 1f;
        [Tooltip("Mean linear luminance of the ash texture (0.05 for ASH_TL_Ash_D).")] [Range(0.005f, 1f)] public float ashTextureMean = 0.05f;
        [Tooltip("Fine detail albedo over both layers (grey 0.5 = neutral). Optional.")] public Texture2D detailTexture;
        [Tooltip("Fine detail normal over both layers (crystalline grain). Optional.")] public Texture2D detailNormal;
        [Tooltip("Metres per detail repeat.")] [Range(0.02f, 10f)] public float detailTiling = 0.5f;
        [Range(0f, 2f)] public float detailAlbedoStrength = 0.5f;
        [Range(0f, 4f)] public float detailNormalStrength = 0.5f;
        [Tooltip("Mip bias for all surface textures (negative = sharper).")] [Range(-3f, 3f)] public float surfaceMipBias = 0f;
        [Tooltip("Editor: max import size for the ASH surface textures (controller context menu).")] public int surfaceMaxTextureSize = 2048;

        [Header("Ash colour + grain (t471)")]
        [Tooltip("0 = old look (ash texture brightness around Ash Color). 1 = the ash texture's own colour variation (normalised to Ash Color).")]
        [Range(0f, 1f)] public float ashTextureColour = 0f;
        [Tooltip("Second ash colour (darker grey-brown) blended in clumps.")] public Color ashColorVariation = new Color(0.30f, 0.27f, 0.24f);
        [Tooltip("How much of the grey-brown variation shows (0 = off).")] [Range(0f, 1f)] public float ashColorVariationAmount = 0f;
        [Tooltip("Ash grain: fine clumpy brightness noise (0 = off).")] [Range(0f, 1f)] public float ashGrain = 0f;
        [Tooltip("Ash grain size: grains per metre.")] [Range(1f, 200f)] public float ashGrainScale = 30f;

        [Header("Snow sparkle (t471)")]
        [Tooltip("Glint brightness (0 = off). View-dependent: glints twinkle as the camera moves.")] [Range(0f, 4f)] public float snowSparkle = 0f;
        [Tooltip("Glint cells per metre.")] [Range(5f, 400f)] public float sparkleScale = 60f;
        [Tooltip("Share of cells that glint.")] [Range(0f, 0.2f)] public float sparkleDensity = 0.02f;

        [Header("Macro variation (t471: breaks up tiling)")]
        [Tooltip("Size of the large-scale patches, metres.")] [Range(1f, 500f)] public float macroScale = 25f;
        [Tooltip("Brightness variation of the patches (0 = off).")] [Range(0f, 1f)] public float macroAlbedoVariation = 0f;
        [Tooltip("UV warp by the patches, in texture repeats (0 = off).")] [Range(0f, 2f)] public float macroUvWarp = 0f;

        [Header("Normal by depth (t471)")]
        [Tooltip("0 = normal strength the same everywhere. 1 = thin dustings are flat and the bump grows to full with depth.")]
        [Range(0f, 1f)] public float normalDepthFade = 0f;
        [Tooltip("Relative layer thickness (0..1) where the normals reach full strength.")] [Range(0.01f, 2f)] public float normalFullDepth = 0.35f;

        void OnValidate() { var c = ASHAccumulationController.Instance; if (c && c.isActiveAndEnabled) c.PushGlobals(); }

        public void CopyFrom(ASHAccumulationController c)
        {
            ashColor = c.ashColor; wetAshDarkness = c.wetAshDarkness; trailCoverageKeep = c.trailCoverageKeep; snowColor = c.snowColor;
            ashTexture = c.ashTexture; ashNormal = c.ashNormal; snowTexture = c.snowTexture; snowNormal = c.snowNormal;
            ashTiling = c.ashTiling; snowTiling = c.snowTiling; surfaceNormalStrength = c.surfaceNormalStrength; ashTextureMean = c.ashTextureMean;
            detailTexture = c.detailTexture; detailNormal = c.detailNormal; detailTiling = c.detailTiling; detailAlbedoStrength = c.detailAlbedoStrength;
            detailNormalStrength = c.detailNormalStrength; surfaceMipBias = c.surfaceMipBias; surfaceMaxTextureSize = c.surfaceMaxTextureSize;
        }
        public void CopyTo(ASHAccumulationController c)
        {
            c.ashColor = ashColor; c.wetAshDarkness = wetAshDarkness; c.trailCoverageKeep = trailCoverageKeep; c.snowColor = snowColor;
            c.ashTexture = ashTexture; c.ashNormal = ashNormal; c.snowTexture = snowTexture; c.snowNormal = snowNormal;
            c.ashTiling = ashTiling; c.snowTiling = snowTiling; c.surfaceNormalStrength = surfaceNormalStrength; c.ashTextureMean = ashTextureMean;
            c.detailTexture = detailTexture; c.detailNormal = detailNormal; c.detailTiling = detailTiling; c.detailAlbedoStrength = detailAlbedoStrength;
            c.detailNormalStrength = detailNormalStrength; c.surfaceMipBias = surfaceMipBias; c.surfaceMaxTextureSize = surfaceMaxTextureSize;
        }
    }
}