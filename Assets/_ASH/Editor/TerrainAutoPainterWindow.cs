using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Paints Terrain Layers from slope, height and low-frequency noise.
    /// Assign terrain layers in the order that fits this project's art direction,
    /// then use ASH/Terrain Auto Painter to generate a starting splatmap.
    /// </summary>
    public sealed class TerrainAutoPainterWindow : EditorWindow
    {
        [Serializable]
        private sealed class LayerSet
        {
            public TerrainLayer prairie;
            public TerrainLayer hills;
            public TerrainLayer rock;
            public TerrainLayer cliff;
            public TerrainLayer highland;

            public TerrainLayer[] GetAssignedLayers()
            {
                var layers = new List<TerrainLayer>(5);
                AddIfPresent(layers, prairie);
                AddIfPresent(layers, hills);
                AddIfPresent(layers, rock);
                AddIfPresent(layers, cliff);
                AddIfPresent(layers, highland);
                return layers.ToArray();
            }

            private static void AddIfPresent(List<TerrainLayer> layers, TerrainLayer layer)
            {
                if (layer != null && !layers.Contains(layer))
                    layers.Add(layer);
            }
        }

        private Terrain terrain;
        private readonly LayerSet layers = new LayerSet();

        [Header("Slope bands (degrees)")]
        [SerializeField] private float flatToHillSlope = 7f;
        [SerializeField] private float hillToRockSlope = 20f;
        [SerializeField] private float rockToCliffSlope = 37f;
        [SerializeField] private float blendWidth = 5f;

        [Header("Height bands (normalized 0-1)")]
        [SerializeField] private bool useHighlandLayer = true;
        [SerializeField, Range(0f, 1f)] private float highlandStart = 0.70f;
        [SerializeField, Range(0f, 1f)] private float highlandFull = 0.88f;

        [Header("Natural breakup")]
        [SerializeField, Range(0f, 1f)] private float noiseStrength = 0.12f;
        [SerializeField, Min(1f)] private float noiseScale = 7f;
        [SerializeField] private int noiseSeed = 1847;

        [MenuItem("ASH/Terrain Auto Painter")]
        private static void Open()
        {
            GetWindow<TerrainAutoPainterWindow>("Terrain Auto Painter");
        }

        private void OnEnable()
        {
            if (terrain == null && Selection.activeGameObject != null)
                terrain = Selection.activeGameObject.GetComponent<Terrain>();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("ASH Terrain Auto Painter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates a starting terrain splatmap from slope, height, and low-frequency noise. " +
                "It does not change the heightmap or Terrain Layers themselves.", MessageType.Info);

            terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);
            if (terrain == null)
            {
                EditorGUILayout.HelpBox("Select the Terrain you imported, then assign it here.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Terrain Layers", EditorStyles.boldLabel);
            layers.prairie = LayerField("Prairie / Flat", layers.prairie);
            layers.hills = LayerField("Hills / Scrub", layers.hills);
            layers.rock = LayerField("Rocky Hills", layers.rock);
            layers.cliff = LayerField("Cliffs", layers.cliff);
            layers.highland = LayerField("High Ridges (optional)", layers.highland);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Slope bands", EditorStyles.boldLabel);
            flatToHillSlope = EditorGUILayout.Slider("Flat → Hill", flatToHillSlope, 0f, 45f);
            hillToRockSlope = EditorGUILayout.Slider("Hill → Rock", hillToRockSlope, 1f, 60f);
            rockToCliffSlope = EditorGUILayout.Slider("Rock → Cliff", rockToCliffSlope, 2f, 89f);
            blendWidth = EditorGUILayout.Slider("Blend Width", blendWidth, 0.5f, 15f);

            EditorGUILayout.Space();
            useHighlandLayer = EditorGUILayout.Toggle("Use High Ridge Layer", useHighlandLayer);
            using (new EditorGUI.DisabledScope(!useHighlandLayer || layers.highland == null))
            {
                highlandStart = EditorGUILayout.Slider("Highland Begins", highlandStart, 0f, 1f);
                highlandFull = EditorGUILayout.Slider("Highland Full", highlandFull, 0f, 1f);
            }

            EditorGUILayout.Space();
            noiseStrength = EditorGUILayout.Slider("Noise Strength", noiseStrength, 0f, 0.35f);
            noiseScale = EditorGUILayout.Slider("Noise Scale", noiseScale, 1f, 30f);
            noiseSeed = EditorGUILayout.IntField("Noise Seed", noiseSeed);

            EditorGUILayout.Space();
            DrawReadout();

            using (new EditorGUI.DisabledScope(!CanPaint(out _)))
            {
                if (GUILayout.Button("Auto Paint Terrain", GUILayout.Height(32f)))
                    Paint();
            }

            if (!CanPaint(out string validation))
                EditorGUILayout.HelpBox(validation, MessageType.Warning);
        }

        private static TerrainLayer LayerField(string label, TerrainLayer current)
        {
            return (TerrainLayer)EditorGUILayout.ObjectField(label, current, typeof(TerrainLayer), false);
        }

        private void DrawReadout()
        {
            EditorGUILayout.HelpBox(
                $"Flat: < {flatToHillSlope:0}°   |   Hills: {flatToHillSlope:0}–{hillToRockSlope:0}°   |   " +
                $"Rock: {hillToRockSlope:0}–{rockToCliffSlope:0}°   |   Cliff: > {rockToCliffSlope:0}°\n" +
                "Starting values are deliberately conservative for the ASH foothills. Fine-paint trails, camps, and hero areas afterward.",
                MessageType.None);
        }

        private bool CanPaint(out string message)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                message = "Assign a Terrain with TerrainData.";
                return false;
            }

            if (layers.prairie == null || layers.hills == null || layers.rock == null || layers.cliff == null)
            {
                message = "Assign Prairie, Hills, Rock, and Cliff Terrain Layers. Highland is optional.";
                return false;
            }

            if (!(flatToHillSlope < hillToRockSlope && hillToRockSlope < rockToCliffSlope))
            {
                message = "Slope thresholds must increase from Flat → Hill → Rock → Cliff.";
                return false;
            }

            if (highlandStart > highlandFull)
            {
                message = "Highland Begins must be lower than Highland Full.";
                return false;
            }

            message = null;
            return true;
        }

        private void Paint()
        {
            if (!CanPaint(out string validation))
            {
                EditorUtility.DisplayDialog("Cannot Auto Paint", validation, "OK");
                return;
            }

            TerrainData data = terrain.terrainData;
            TerrainLayer[] assigned = layers.GetAssignedLayers();
            int prairieIndex = Array.IndexOf(assigned, layers.prairie);
            int hillIndex = Array.IndexOf(assigned, layers.hills);
            int rockIndex = Array.IndexOf(assigned, layers.rock);
            int cliffIndex = Array.IndexOf(assigned, layers.cliff);
            int highlandIndex = Array.IndexOf(assigned, layers.highland);

            Undo.RegisterCompleteObjectUndo(data, "Auto Paint ASH Terrain");
            data.terrainLayers = assigned;

            int width = data.alphamapWidth;
            int height = data.alphamapHeight;
            var map = new float[height, width, assigned.Length];

            try
            {
                for (int y = 0; y < height; y++)
                {
                    float normalizedY = y / (float)(height - 1);
                    EditorUtility.DisplayProgressBar(
                        "ASH Terrain Auto Painter",
                        $"Painting row {y + 1:N0} of {height:N0}",
                        normalizedY);

                    for (int x = 0; x < width; x++)
                    {
                        float normalizedX = x / (float)(width - 1);
                        float slope = data.GetSteepness(normalizedX, normalizedY);
                        float terrainHeight = data.GetInterpolatedHeight(normalizedX, normalizedY) / Mathf.Max(0.001f, data.size.y);
                        float noise = (Mathf.PerlinNoise(
                                           (normalizedX + noiseSeed * 0.017f) * noiseScale,
                                           (normalizedY + noiseSeed * 0.031f) * noiseScale) - 0.5f) * 2f;

                        float prairie = 1f - SmoothBand(slope, flatToHillSlope - blendWidth, flatToHillSlope + blendWidth);
                        float hill = SmoothBand(slope, flatToHillSlope - blendWidth, flatToHillSlope + blendWidth) *
                                     (1f - SmoothBand(slope, hillToRockSlope - blendWidth, hillToRockSlope + blendWidth));
                        float rock = SmoothBand(slope, hillToRockSlope - blendWidth, hillToRockSlope + blendWidth) *
                                     (1f - SmoothBand(slope, rockToCliffSlope - blendWidth, rockToCliffSlope + blendWidth));
                        float cliff = SmoothBand(slope, rockToCliffSlope - blendWidth, rockToCliffSlope + blendWidth);

                        // Break up broad prairie/hill/rock transitions without affecting pure cliff faces.
                        float prairieShift = noise * noiseStrength;
                        prairie = Mathf.Max(0f, prairie + prairieShift);
                        hill = Mathf.Max(0f, hill - prairieShift * 0.55f);
                        rock = Mathf.Max(0f, rock - prairieShift * 0.20f);

                        float highland = 0f;
                        if (useHighlandLayer && highlandIndex >= 0)
                        {
                            highland = SmoothBand(terrainHeight, highlandStart, highlandFull);
                            // High ridges supplement ground materials; cliffs retain their rock face.
                            float replaceable = prairie + hill + rock;
                            prairie *= 1f - highland;
                            hill *= 1f - highland;
                            rock *= 1f - highland;
                            highland *= replaceable;
                        }

                        float total = prairie + hill + rock + cliff + highland;
                        if (total <= 0.0001f)
                            prairie = total = 1f;

                        map[y, x, prairieIndex] = prairie / total;
                        map[y, x, hillIndex] = hill / total;
                        map[y, x, rockIndex] = rock / total;
                        map[y, x, cliffIndex] = cliff / total;
                        if (highlandIndex >= 0)
                            map[y, x, highlandIndex] = highland / total;
                    }
                }

                data.SetAlphamaps(0, 0, map);
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                Selection.activeObject = terrain;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static float SmoothBand(float value, float start, float end)
        {
            if (Mathf.Approximately(start, end))
                return value >= end ? 1f : 0f;

            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
        }
    }
}
