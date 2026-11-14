using System.IO;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// Placeholder ground treatment for the master terrain: generates tileable noise textures (until the
    /// environment reuse pass swaps in production textures) and paints splat weights from slope, relative
    /// height, east-west biome position (prairie east -> foothills west) and the dry creek polyline.
    /// </summary>
    public static class ASHTerrainPainter
    {
        private const string TexFolder = ASHTerrainImporter.TerrainFolder + "/Textures";
        private const string LayerFolder = ASHTerrainImporter.TerrainFolder + "/Layers";

        // Layer order matters for the splat painter below.
        private static readonly (string name, Color a, Color b, float tile, float smooth)[] LayerDefs =
        {
            ("ASH_TL_PrairieGrass", new Color(0.46f, 0.47f, 0.30f), new Color(0.58f, 0.55f, 0.36f), 6f, 0.05f),
            ("ASH_TL_DryGrass",     new Color(0.62f, 0.55f, 0.38f), new Color(0.74f, 0.66f, 0.47f), 5f, 0.05f),
            ("ASH_TL_Soil",         new Color(0.45f, 0.36f, 0.27f), new Color(0.55f, 0.45f, 0.34f), 4f, 0.08f),
            ("ASH_TL_Rock",         new Color(0.47f, 0.43f, 0.40f), new Color(0.63f, 0.59f, 0.55f), 8f, 0.15f),
            ("ASH_TL_CreekGravel",  new Color(0.52f, 0.48f, 0.43f), new Color(0.70f, 0.66f, 0.60f), 2.5f, 0.2f),
        };

        /// <summary>Dry creek centerline (world XZ, metres) traced from flow accumulation near the spawn.</summary>
        public static readonly Vector2[] CreekPolyline =
        {
            new Vector2(2140f, 1690f), new Vector2(2086f, 1664f), new Vector2(2062f, 1664f), new Vector2(2039f, 1648f),
            new Vector2(2016f, 1641f), new Vector2(1992f, 1641f), new Vector2(1969f, 1641f), new Vector2(1945f, 1633f),
            new Vector2(1922f, 1633f), new Vector2(1898f, 1633f), new Vector2(1878f, 1628f),
        };

        [MenuItem("ASH/World/2. Paint Terrain (placeholder layers)")]
        public static void PaintMenu()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(ASHTerrainImporter.DataPath);
            if (data == null) data = ASHTerrainImporter.ImportOrUpdate();
            Paint(data);
        }

        public static void Paint(TerrainData data)
        {
            ASHTerrainImporter.EnsureFolder(TexFolder);
            ASHTerrainImporter.EnsureFolder(LayerFolder);
            var layers = new TerrainLayer[LayerDefs.Length];
            for (int l = 0; l < LayerDefs.Length; l++)
                layers[l] = EnsureLayer(LayerDefs[l].name, LayerDefs[l].a, LayerDefs[l].b, LayerDefs[l].tile, LayerDefs[l].smooth, l);
            data.terrainLayers = layers;

            int res = data.alphamapResolution;
            int hRes = data.heightmapResolution;
            float[,] h = data.GetHeights(0, 0, hRes, hRes);
            float cell = data.size.x / (hRes - 1);
            float hScale = data.size.y;
            var maps = new float[res, res, layers.Length];
            float worldPerTexel = data.size.x / res;

            for (int y = 0; y < res; y++)
            {
                float wz = (y + 0.5f) * worldPerTexel;
                int hy = Mathf.Clamp(Mathf.RoundToInt(wz / cell), 2, hRes - 3);
                for (int x = 0; x < res; x++)
                {
                    float wx = (x + 0.5f) * worldPerTexel;
                    int hx = Mathf.Clamp(Mathf.RoundToInt(wx / cell), 2, hRes - 3);

                    float dzdx = (h[hy, hx + 2] - h[hy, hx - 2]) * hScale / (4f * cell);
                    float dzdy = (h[hy + 2, hx] - h[hy - 2, hx]) * hScale / (4f * cell);
                    float slope = Mathf.Atan(Mathf.Sqrt(dzdx * dzdx + dzdy * dzdy)) * Mathf.Rad2Deg;

                    // local relief: centre vs. ~40 m ring -> ridges (+) and gullies (-)
                    int r = 40;
                    int x0 = Mathf.Clamp(hx - r, 0, hRes - 1), x1 = Mathf.Clamp(hx + r, 0, hRes - 1);
                    int y0 = Mathf.Clamp(hy - r, 0, hRes - 1), y1 = Mathf.Clamp(hy + r, 0, hRes - 1);
                    float ring = (h[hy, x0] + h[hy, x1] + h[y0, hx] + h[y1, hx]) * 0.25f;
                    float relief = (h[hy, hx] - ring) * hScale;

                    float west = Mathf.InverseLerp(2600f, 600f, wx); // 0 prairie east, 1 foothills west
                    float n1 = Mathf.PerlinNoise(wx * 0.012f + 31.7f, wz * 0.012f + 7.3f);
                    float n2 = Mathf.PerlinNoise(wx * 0.06f + 3.1f, wz * 0.06f + 91.2f);

                    float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f - 6f * west, 38f - 6f * west, slope));
                    rock = Mathf.Max(rock, Mathf.Clamp01((relief - 6f) / 8f) * west * 0.6f * n2);
                    float soil = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, 26f, slope)) * (1f - rock);
                    soil = Mathf.Max(soil, Mathf.Clamp01((-relief - 3f) / 6f) * 0.5f);
                    float dry = Mathf.Clamp01(n1 * 1.4f - 0.35f + west * 0.25f) * (1f - rock) * (1f - soil);
                    float grass = Mathf.Max(0.05f, 1f - rock - soil - dry);

                    float creekDist = DistanceToPolyline(new Vector2(wx, wz), CreekPolyline);
                    float gravel = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.5f + n2 * 1.5f, 5.5f + n2 * 2f, creekDist));
                    float bank = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 11f, creekDist));

                    float rest = 1f - gravel;
                    soil = soil * rest + bank * 0.35f * rest;
                    grass *= rest; dry *= rest; rock *= rest;

                    float sum = grass + dry + soil + rock + gravel;
                    maps[y, x, 0] = grass / sum;
                    maps[y, x, 1] = dry / sum;
                    maps[y, x, 2] = soil / sum;
                    maps[y, x, 3] = rock / sum;
                    maps[y, x, 4] = gravel / sum;
                }
            }
            data.SetAlphamaps(0, 0, maps);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Terrain painted with placeholder layers (prairie/dry grass/soil/rock/creek gravel).");
        }

        public static float DistanceToPolyline(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        private static TerrainLayer EnsureLayer(string name, Color a, Color b, float tile, float smoothness, int seed)
        {
            string layerPath = $"{LayerFolder}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = EnsureNoiseTexture($"{TexFolder}/{name}_D.png", a, b, seed);
            layer.tileSize = new Vector2(tile, tile);
            layer.smoothness = smoothness;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static Texture2D EnsureNoiseTexture(string path, Color a, Color b, int seed)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float n = TileFbm(u, v, seed) ;
                float grain = Hash(x * 73 + y * 151 + seed * 997) * 0.18f;
                Color c = Color.Lerp(a, b, Mathf.Clamp01(n + grain - 0.09f));
                px[y * size + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Tileable fBm built from wrapped value noise.
        private static float TileFbm(float u, float v, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int period = 4;
            for (int o = 0; o < 5; o++)
            {
                sum += amp * WrappedValueNoise(u * period, v * period, period, seed + o * 17);
                norm += amp; amp *= 0.5f; period *= 2;
            }
            return sum / norm;
        }

        private static float WrappedValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float Lattice(int ix, int iy) => Hash(((ix % period + period) % period) * 374761 + ((iy % period + period) % period) * 668265 + seed * 1013);
            float a = Lattice(x0, y0), b = Lattice(x0 + 1, y0), c = Lattice(x0, y0 + 1), d = Lattice(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483648f;
        }
    }
}
