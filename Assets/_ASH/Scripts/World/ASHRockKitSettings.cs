using UnityEngine;

namespace ASH.World
{
    /// <summary>
    /// Kit and placement ranges for the Rocks and Cliffs tool (Resources/ASH_RockKitSettings).
    /// Prefabs are referenced from the ANGRY MESH and Quantum Theory vendor packs (those folders stay out of git).
    /// Wide ranges plus Relaxed / Default / Extreme presets.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/World/Rock Kit Settings", fileName = "ASH_RockKitSettings")]
    public sealed class ASHRockKitSettings : ScriptableObject
    {
        public enum Preset { Relaxed, Default, Extreme }
        public enum Style { Boulder, BoulderPile, Outcrop, Rubble, Spire, Cliff, Sharp }

        [Header("Preset")]
        public Preset preset = Preset.Default;

        [Header("Vendor prefab kits (scanned by ASH/World/Rocks and Cliffs/Install Settings)")]
        [Tooltip("ANGRY MESH snow (or chosen set) individual rocks.")]
        public GameObject[] boulders;
        [Tooltip("Smaller pieces for rubble and pile fillers (ANGRY MESH detail rocks, or the smaller boulders).")]
        public GameObject[] rubble;
        [Tooltip("Quantum Theory large / wide / concave pieces, used for cliffs, outcrops and spires.")]
        public GameObject[] cliffs;
        [Tooltip("Optional sharper / cubic pieces (Quantum Theory RockCubic and similar). Falls back to cliffs.")]
        public GameObject[] sharp;

        [Header("Folders to scan (relative to Assets/)")]
        public string boulderFolder = "ANGRY MESH/Nature Pack - PBR Rocks/Prefabs Rocks Desktop/Snow Rocks";
        public string rubbleFolder = "ANGRY MESH/Nature Pack - PBR Rocks/Prefabs Rocks Desktop/Terrain Detail Unlit";
        public string cliffFolder = "Quantum Theory/Better Rocks and Cliffs/Prefabs";

        [Header("Scale (uniform random between min and max)")]
        [Range(0.05f, 8f)] public float boulderScaleMin = 0.7f;
        [Range(0.05f, 16f)] public float boulderScaleMax = 1.4f;
        [Range(0.05f, 8f)] public float pileScaleMin = 0.45f;
        [Range(0.05f, 16f)] public float pileScaleMax = 1.2f;
        [Range(0.05f, 8f)] public float outcropScaleMin = 0.8f;
        [Range(0.05f, 16f)] public float outcropScaleMax = 1.6f;
        [Range(0.02f, 4f)] public float rubbleScaleMin = 0.4f;
        [Range(0.05f, 8f)] public float rubbleScaleMax = 1.0f;
        [Range(0.2f, 8f)] public float spireScaleMin = 0.7f;
        [Range(0.2f, 16f)] public float spireScaleMax = 1.5f;
        [Range(0.2f, 16f)] public float cliffScaleMin = 0.85f;
        [Range(0.2f, 24f)] public float cliffScaleMax = 1.4f;

        [Header("Cluster counts")]
        [Range(1, 8)] public int pileCountMin = 3;
        [Range(1, 24)] public int pileCountMax = 7;
        [Range(1, 12)] public int outcropCountMin = 2;
        [Range(1, 16)] public int outcropCountMax = 5;
        [Range(2, 40)] public int rubbleCountMin = 6;
        [Range(2, 80)] public int rubbleCountMax = 14;
        [Range(2, 12)] public int spireCountMin = 2;
        [Range(2, 16)] public int spireCountMax = 5;
        [Tooltip("Cliff pieces per metre of the drawn line.")]
        [Range(0.05f, 4f)] public float cliffPiecesPerMetre = 0.35f;
        [Range(1, 40)] public int cliffCountMin = 3;
        [Range(1, 80)] public int cliffCountMax = 16;

        [Header("Scatter / spacing (metres)")]
        [Range(0.1f, 20f)] public float pileRadius = 2.4f;
        [Range(0.1f, 30f)] public float outcropLength = 5.5f;
        [Range(0.1f, 30f)] public float rubbleRadius = 4.0f;
        [Range(0f, 8f)] public float cliffOverlap = 0.6f;
        [Range(0f, 6f)] public float brushSpacing = 3.5f;

        [Header("Snap, tilt and sink")]
        [Tooltip("Layers the placer rays against (Terrain and Default cover the ground and most props).")]
        public LayerMask snapMask = ~0;
        [Tooltip("Also snap onto already-placed rocks so piles can stack. Off = terrain only.")]
        public bool snapToProps = true;
        [Range(0f, 1f)] public float tiltStrength = 0.65f;
        [Tooltip("Max ground tilt applied to a placed rock, in degrees.")]
        [Range(0f, 89f)] public float maxTiltDegrees = 45f;
        [Tooltip("How far to sink a rock into the ground on flat terrain (metres).")]
        [Range(0f, 8f)] public float baseSinkMetres = 0.25f;
        [Tooltip("Extra sink per degree of slope, so bases don't hover on steep ground.")]
        [Range(0f, 0.2f)] public float sinkPerDegree = 0.012f;
        [Range(0f, 12f)] public float maxSinkMetres = 2.5f;
        [Tooltip("Skip boulder/rubble placement on terrain steeper than this (cliffs and spires still place). 0 = never skip.")]
        [Range(0f, 89f)] public float skipBoulderAboveDegrees = 55f;

        [Header("Overlap")]
        [Tooltip("Try to push a new rock out of overlapping scene colliders (ignores terrain and itself).")]
        public bool avoidOverlap = true;
        [Range(1, 16)] public int overlapTries = 6;
        [Range(0.1f, 8f)] public float overlapPushMetres = 1.5f;

        [Header("Yaw")]
        [Tooltip("Random Y rotation range in degrees (0-180 means full spin).")]
        [Range(0f, 180f)] public float yawJitter = 180f;

        public void ApplyPreset(Preset p)
        {
            preset = p;
            switch (p)
            {
                case Preset.Relaxed:
                    boulderScaleMin = 0.85f; boulderScaleMax = 1.15f;
                    pileScaleMin = 0.6f; pileScaleMax = 1.1f; pileCountMin = 2; pileCountMax = 4; pileRadius = 1.8f;
                    outcropScaleMin = 0.9f; outcropScaleMax = 1.3f; outcropCountMin = 2; outcropCountMax = 3; outcropLength = 4f;
                    rubbleScaleMin = 0.5f; rubbleScaleMax = 0.9f; rubbleCountMin = 4; rubbleCountMax = 8; rubbleRadius = 3f;
                    spireScaleMin = 0.8f; spireScaleMax = 1.2f; spireCountMin = 2; spireCountMax = 3;
                    cliffScaleMin = 0.9f; cliffScaleMax = 1.2f; cliffPiecesPerMetre = 0.25f; cliffCountMin = 2; cliffCountMax = 8; cliffOverlap = 0.4f;
                    tiltStrength = 0.5f; maxTiltDegrees = 30f; baseSinkMetres = 0.15f; sinkPerDegree = 0.008f; maxSinkMetres = 1.2f;
                    skipBoulderAboveDegrees = 45f; avoidOverlap = true; overlapTries = 6; brushSpacing = 4.5f;
                    break;
                case Preset.Extreme:
                    boulderScaleMin = 0.4f; boulderScaleMax = 3.5f;
                    pileScaleMin = 0.25f; pileScaleMax = 2.8f; pileCountMin = 6; pileCountMax = 16; pileRadius = 6f;
                    outcropScaleMin = 0.6f; outcropScaleMax = 3.5f; outcropCountMin = 4; outcropCountMax = 10; outcropLength = 14f;
                    rubbleScaleMin = 0.15f; rubbleScaleMax = 1.8f; rubbleCountMin = 16; rubbleCountMax = 40; rubbleRadius = 10f;
                    spireScaleMin = 0.5f; spireScaleMax = 4f; spireCountMin = 4; spireCountMax = 10;
                    cliffScaleMin = 0.6f; cliffScaleMax = 4f; cliffPiecesPerMetre = 0.7f; cliffCountMin = 4; cliffCountMax = 40; cliffOverlap = 1.2f;
                    tiltStrength = 1f; maxTiltDegrees = 70f; baseSinkMetres = 0.6f; sinkPerDegree = 0.03f; maxSinkMetres = 8f;
                    skipBoulderAboveDegrees = 80f; avoidOverlap = true; overlapTries = 12; brushSpacing = 2f;
                    break;
                default:
                    boulderScaleMin = 0.7f; boulderScaleMax = 1.4f;
                    pileScaleMin = 0.45f; pileScaleMax = 1.2f; pileCountMin = 3; pileCountMax = 7; pileRadius = 2.4f;
                    outcropScaleMin = 0.8f; outcropScaleMax = 1.6f; outcropCountMin = 2; outcropCountMax = 5; outcropLength = 5.5f;
                    rubbleScaleMin = 0.4f; rubbleScaleMax = 1.0f; rubbleCountMin = 6; rubbleCountMax = 14; rubbleRadius = 4.0f;
                    spireScaleMin = 0.7f; spireScaleMax = 1.5f; spireCountMin = 2; spireCountMax = 5;
                    cliffScaleMin = 0.85f; cliffScaleMax = 1.4f; cliffPiecesPerMetre = 0.35f; cliffCountMin = 3; cliffCountMax = 16; cliffOverlap = 0.6f;
                    tiltStrength = 0.65f; maxTiltDegrees = 45f; baseSinkMetres = 0.25f; sinkPerDegree = 0.012f; maxSinkMetres = 2.5f;
                    skipBoulderAboveDegrees = 55f; avoidOverlap = true; overlapTries = 6; brushSpacing = 3.5f;
                    break;
            }
        }

        public GameObject[] KitFor(Style style)
        {
            switch (style)
            {
                case Style.Rubble: return FirstAvailable(rubble, boulders);
                case Style.Cliff:
                case Style.Outcrop:
                case Style.Spire: return FirstAvailable(cliffs, boulders);
                case Style.Sharp: return FirstAvailable(sharp, cliffs, boulders);
                default: return FirstAvailable(boulders, cliffs);
            }
        }

        static GameObject[] FirstAvailable(params GameObject[][] lists)
        {
            foreach (var l in lists)
            {
                if (l == null) continue;
                for (int i = 0; i < l.Length; i++) if (l[i]) return l;
            }
            return System.Array.Empty<GameObject>();
        }

        [ContextMenu("Preset/Relaxed")] void P0() => ApplyPreset(Preset.Relaxed);
        [ContextMenu("Preset/Default")] void P1() => ApplyPreset(Preset.Default);
        [ContextMenu("Preset/Extreme (stress test)")] void P2() => ApplyPreset(Preset.Extreme);
    }
}
