using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.World
{
    /// <summary>
    /// Settings for ASH/World/Scatter Vegetation: prototype lists (placeholder art; swap prefabs/textures and
    /// re-run), per-biome densities, clearances and terrain draw distances. Presets: Low / Default / Extreme.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/World/Vegetation Settings", fileName = "ASH_VegetationSettings")]
    public sealed class ASHVegetationSettings : ScriptableObject
    {
        [Serializable]
        public sealed class TreeEntry
        {
            public GameObject prefab;
            [Tooltip("Relative weight per biome / near creeks (0 = never).")]
            [Range(0f, 2f)] public float ridge = 1f, gully = 0.5f, prairie = 0f, creek = 0.5f;
            [Range(0.3f, 3f)] public float minScale = 0.8f, maxScale = 1.25f;
        }

        [Serializable]
        public sealed class GrassEntry
        {
            public Texture2D texture;
            [Tooltip("Optional mesh detail instead of a texture.")] public GameObject mesh;
            [Range(0.05f, 4f)] public float minWidth = 0.4f, maxWidth = 0.8f, minHeight = 0.3f, maxHeight = 0.7f;
            public Color healthy = new Color(0.72f, 0.68f, 0.55f), dry = new Color(0.6f, 0.57f, 0.5f);
            [Range(0f, 2f)] public float ridge = 0.4f, gully = 0.8f, prairie = 1f, creek = 0.5f;
        }

        public enum Preset { Low, Default, Extreme }

        [Header("Prototypes (placeholders; only valid terrain trees are used)")]
        public List<TreeEntry> trees = new List<TreeEntry>();
        public List<GrassEntry> grass = new List<GrassEntry>();

        [Header("Biome bands (world X, metres; boundaries are noise-warped)")]
        public float ridgeToGully = 850f;
        public float gullyToPrairie = 2050f;
        [Range(0f, 800f)] public float boundaryWarp = 450f;

        [Header("Tree density (trees per hectare)")]
        [Range(0f, 400f)] public float ridgeTrees = 70f;
        [Range(0f, 200f)] public float gullyTrees = 6f;
        [Range(0f, 400f)] public float creekTrees = 55f;
        [Range(0f, 50f)] public float prairieTrees = 0.4f;
        [Range(5f, 200f)] public float creekBand = 45f;
        [Range(0f, 1f)] [Tooltip("Clumping: 0 = even, 1 = strong groves and clearings.")] public float clumping = 0.65f;
        [Range(10f, 60f)] public float maxTreeSlope = 38f;
        [Range(0.1f, 5f)] public float treeDensityScale = 1f;

        [Header("Grass detail (instances per detail cell)")]
        [Range(0f, 48f)] public float grassDensity = 12f;
        [Range(10f, 70f)] public float maxGrassSlope = 40f;
        [Range(0f, 1f)] [Tooltip("Grass reduction on steep rock.")] public float rockReduction = 0.85f;

        [Header("Roads and trails (hard rule: no trees on them). Splines = ASH_RegionLayout roads/trails")]
        [Tooltip("Painted road half-width (matches ASHRegionBuilder.PaintRoutes).")] public float roadHalfWidth = 3.5f;
        [Tooltip("Painted trail half-width.")] public float trailHalfWidth = 1.1f;
        [Tooltip("Trees at least this far from the road EDGE.")] public float roadEdgeClearTrees = 6f;
        [Tooltip("Trees at least this far from the trail CENTERLINE.")] public float trailClearTrees = 4f;
        [Tooltip("Also no trees where the route soil splat is painted, within this band of a route spline.")] public float routeSplatBand = 12f;
        [Range(0.05f, 1f)] public float routeSplatThreshold = 0.3f;
        [Tooltip("Grass ramps from almost none on the route surface to full over this distance past the edge.")] public float routeGrassFade = 2.5f;
        [Range(0f, 1f)] [Tooltip("Grass multiplier on the road/trail surface itself.")] public float grassOnRoute = 0.04f;

        [Header("Other clearances (metres): trees / grass")]
        public float poiClearTrees = 28f; public float poiClearGrass = 6f;
        public float cabinClearTrees = 25f; public float cabinClearGrass = 3f;
        public float spawnClearTrees = 15f;
        public float pickupClearTrees = 4f; public float pickupClearGrass = 1f;

        [Header("Terrain rendering")]
        [Range(100f, 5000f)] public float treeDistance = 1500f;
        [Range(20f, 1000f)] public float billboardStart = 150f;
        [Range(0f, 200f)] public float treeCrossFade = 30f;
        [Range(10f, 10000f)] public int maxFullLodTrees = 120;
        [Range(10f, 250f)] public float detailDistance = 130f;
        [Range(0f, 1f)] public float detailObjectDensity = 1f;

        public int seed = 1337;

        public void Apply(Preset p)
        {
            switch (p)
            {
                case Preset.Low: treeDensityScale = 0.35f; grassDensity = 6f; treeDistance = 800f; billboardStart = 80f; maxFullLodTrees = 50; detailDistance = 80f; detailObjectDensity = 0.6f; break;
                case Preset.Default: treeDensityScale = 1f; grassDensity = 12f; treeDistance = 1500f; billboardStart = 150f; maxFullLodTrees = 120; detailDistance = 130f; detailObjectDensity = 1f; break;
                case Preset.Extreme: treeDensityScale = 3f; grassDensity = 24f; treeDistance = 2500f; billboardStart = 300f; maxFullLodTrees = 400; detailDistance = 250f; detailObjectDensity = 1f; break;
            }
        }
    }
}
