using System.IO;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// Slice ground treatment for the master terrain: builds ASH TerrainLayers from Gaia sample textures and paints
    /// splat weights from slope, relative height, east-west biome position (prairie east -> foothills west), the dry
    /// creek polyline and ash fallout. Never touches heights. Anthony's ASH/Terrain Auto Painter can reuse the layers.
    /// </summary>
    public static class ASHTerrainPainter
    {
        private const string LayerFolder = ASHTerrainImporter.TerrainFolder + "/Layers";
        private const string NM = "Assets/Procedural Worlds/Packages - Install/Asset Samples/NatureManufacture/";

        // Layer order matters for the splat painter below. Textures: Gaia "Asset Samples" (NatureManufacture),
        // tinted per layer with the TerrainLayer diffuse remap (no vendor files modified).
        // tintMode: 0 = vendor texture as-is, 1 = tinted copy, 2 = desaturated + tinted copy (written to
        // Assets/_ASH/Art/Terrain/Textures; URP's terrain shader ignores the TerrainLayer diffuse remap tint).
        private static readonly (string name, string tex, Vector3 tint, float tile, float smoothMax, Vector2 offset, int tintMode)[] LayerDefs =
        {
            ("ASH_TL_PrairieGrass", "T_ground_grass_01", new Vector3(0.95f, 0.9f, 0.7f), 7f, 0.25f, Vector2.zero, 1),
            ("ASH_TL_DryGrass",     "T_ground_grass_01", new Vector3(1.25f, 1.0f, 0.55f), 5.5f, 0.2f, new Vector2(1.7f, 3.1f), 1),
            ("ASH_TL_Soil",         "T_Ground_Sand_02",  new Vector3(0.62f, 0.5f, 0.4f), 4.5f, 0.3f, Vector2.zero, 1),
            ("ASH_TL_Rock",         "T_ground_rock_01",  new Vector3(1f, 1f, 1f), 11f, 0.45f, Vector2.zero, 0),
            ("ASH_TL_CreekGravel",  "T_ground_rock_02",  new Vector3(1f, 1f, 1f), 3f, 0.5f, Vector2.zero, 0),
            ("ASH_TL_Ash",          "T_Ground_Snow_1",   new Vector3(0.25f, 0.245f, 0.24f), 4f, 0.08f, Vector2.zero, 2),
        };
        private const string TexFolder = ASHTerrainImporter.TerrainFolder + "/Textures";

        /// <summary>Dry creek centerline (world XZ, metres) traced from flow accumulation near the spawn.</summary>
        public static readonly Vector2[] CreekPolyline =
        {
            new Vector2(2140f, 1690f), new Vector2(2086f, 1664f), new Vector2(2062f, 1664f), new Vector2(2039f, 1648f),
            new Vector2(2016f, 1641f), new Vector2(1992f, 1641f), new Vector2(1969f, 1641f), new Vector2(1945f, 1633f),
            new Vector2(1922f, 1633f), new Vector2(1898f, 1633f), new Vector2(1878f, 1628f),
        };

        [MenuItem("ASH/World/Paint Active Terrain (Gaia sample layers + ash)")]
        public static void PaintActive()
        {
            var t = Terrain.activeTerrain;
            if (t == null) throw new System.Exception("No active terrain in the open scene.");
            Undo.RegisterCompleteObjectUndo(t.terrainData, "ASH paint terrain");
            Paint(t.terrainData);
        }

        /// <summary>Creates/updates the ASH TerrainLayer assets only (for use in the Terrain Auto Painter window).</summary>
        [MenuItem("ASH/World/Create ASH Terrain Layers")]
        public static TerrainLayer[] EnsureLayers()
        {
            ASHTerrainImporter.EnsureFolder(LayerFolder);
            var layers = new TerrainLayer[LayerDefs.Length];
            for (int l = 0; l < LayerDefs.Length; l++) layers[l] = EnsureLayer(LayerDefs[l]);
            AssetDatabase.SaveAssets();
            return layers;
        }

        public static void Paint(TerrainData data)
        {
            var layers = EnsureLayers();
            data.terrainLayers = layers;

            int res = data.alphamapResolution;
            int hRes = data.heightmapResolution;
            float[,] h = data.GetHeights(0, 0, hRes, hRes);
            float cell = data.size.x / (hRes - 1);
            float hScale = data.size.y;
            var maps = new float[res, res, layers.Length];
            float worldPerTexel = data.size.x / res;
            Vector2 camp = ASHWorldPoints.CurrentCampsite();

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

                    int r = 40;
                    int x0 = Mathf.Clamp(hx - r, 0, hRes - 1), x1 = Mathf.Clamp(hx + r, 0, hRes - 1);
                    int y0 = Mathf.Clamp(hy - r, 0, hRes - 1), y1 = Mathf.Clamp(hy + r, 0, hRes - 1);
                    float ring = (h[hy, x0] + h[hy, x1] + h[y0, hx] + h[y1, hx]) * 0.25f;
                    float relief = (h[hy, hx] - ring) * hScale;

                    float west = Mathf.InverseLerp(2600f, 600f, wx);
                    float n1 = Mathf.PerlinNoise(wx * 0.012f + 31.7f, wz * 0.012f + 7.3f);
                    float n2 = Mathf.PerlinNoise(wx * 0.06f + 3.1f, wz * 0.06f + 91.2f);
                    float n3 = Mathf.PerlinNoise(wx * 0.0035f + 57.1f, wz * 0.0035f + 12.9f);

                    float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f - 6f * west, 38f - 6f * west, slope));
                    rock = Mathf.Max(rock, Mathf.Clamp01((relief - 6f) / 8f) * west * 0.6f * n2);
                    float soil = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 34f, slope)) * (1f - rock) * 0.8f;
                    soil = Mathf.Max(soil, Mathf.Clamp01((-relief - 5f) / 8f) * 0.35f);
                    float dry = Mathf.Clamp01(n1 * 1.4f - 0.35f + west * 0.25f) * (1f - rock) * (1f - soil);
                    float grass = Mathf.Max(0.05f, 1f - rock - soil - dry);

                    float creekDist = DistanceToPolyline(new Vector2(wx, wz), CreekPolyline);
                    float gravel = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.5f + n2 * 1.5f, 5.5f + n2 * 2f, creekDist));
                    float bank = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 11f, creekDist));

                    // Ash: drifted fallout in hollows and lee slopes, heavier to the west (toward the fire), plus the
                    // burn scar around the old campsite. Kept patchy so the prairie still reads as prairie.
                    float hollow = Mathf.Clamp01((-relief + 1f) / 6f);
                    float ash = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.78f, n3 + west * 0.12f)) * (0.45f + 0.55f * hollow) * (1f - rock * 0.7f);
                    ash *= 0.6f + 0.4f * n2;
                    float campD = Vector2.Distance(new Vector2(wx, wz), camp);
                    ash = Mathf.Max(ash, (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 13f + n2 * 4f, campD))) * 0.85f);

                    float rest = 1f - gravel;
                    soil = soil * rest + bank * 0.35f * rest;
                    grass *= rest; dry *= rest; rock *= rest;
                    float ashW = ash * rest;
                    float keep = 1f - ashW;
                    grass *= keep; dry *= keep; soil *= keep; rock *= keep;

                    float sum = grass + dry + soil + rock + gravel + ashW;
                    maps[y, x, 0] = grass / sum;
                    maps[y, x, 1] = dry / sum;
                    maps[y, x, 2] = soil / sum;
                    maps[y, x, 3] = rock / sum;
                    maps[y, x, 4] = gravel / sum;
                    maps[y, x, 5] = ashW / sum;
                }
            }
            data.SetAlphamaps(0, 0, maps);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Terrain painted: prairie/dry grass/soil/rock/creek gravel/ash (Gaia sample textures).");
        }

        private static TerrainLayer EnsureLayer((string name, string tex, Vector3 tint, float tile, float smoothMax, Vector2 offset, int tintMode) d)
        {
            string layerPath = $"{LayerFolder}/{d.name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, layerPath); }
            var src = Load(d.tex + "_A_SM", d.tex + "_A_Sm");
            layer.diffuseTexture = d.tintMode == 0 || src == null ? src : TintedCopy(src, d.tint, d.tintMode == 2, $"{TexFolder}/{d.name}_D.png");
            layer.normalMapTexture = Load(d.tex + "_N");
            layer.maskMapTexture = d.tintMode == 2 ? null : Load(d.tex + "_M");
            if (layer.diffuseTexture == null) Debug.LogWarning("[ASH] missing texture " + d.tex);
            layer.tileSize = new Vector2(d.tile, d.tile);
            layer.tileOffset = d.offset;
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = Vector4.one;
            layer.maskMapRemapMin = Vector4.zero;
            layer.maskMapRemapMax = new Vector4(1f, 1f, 1f, d.smoothMax);
            layer.normalScale = 1f;
            layer.smoothness = d.smoothMax * 0.5f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            if (layer.normalMapTexture != null)
            {
                var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(layer.normalMapTexture)) as TextureImporter;
                if (imp != null && imp.textureType != TextureImporterType.NormalMap)
                    Debug.LogWarning("[ASH] " + imp.assetPath + " is not imported as a Normal Map (vendor import setting; left unchanged).");
            }
            return layer;
        }

        /// <summary>
        /// Tinted copy of a vendor texture, read back through the GPU (no need for the vendor texture to be
        /// readable, and its import settings stay untouched). Alpha (smoothness) is kept and scaled.
        /// </summary>
        private static Texture2D TintedCopy(Texture2D src, Vector3 tint, bool desaturate, string path, int size = 1024)
        {
            ASHTerrainImporter.EnsureFolder(TexFolder);
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (desaturate)
                {
                    float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    l = Mathf.Clamp01((l - 0.5f) * 2.2f + 0.5f); // bring out the drift texture
                    c = new Color(l, l, l, c.a);
                }
                px[i] = new Color(Mathf.Clamp01(c.r * tint.x), Mathf.Clamp01(c.g * tint.y), Mathf.Clamp01(c.b * tint.z), c.a * (desaturate ? 0.2f : 1f));
            }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = true; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Repeat; imp.anisoLevel = 4; imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D Load(params string[] names)
        {
            foreach (var n in names)
                foreach (var ext in new[] { ".tga", ".png", ".jpg" })
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(NM + n + ext);
                    if (t != null) return t;
                }
            return null;
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
    }
}
