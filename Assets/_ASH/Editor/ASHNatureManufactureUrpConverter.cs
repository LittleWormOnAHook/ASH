using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Converts the Dynamic Nature Mountain Tree Pack's legacy custom materials to URP/Lit,
    /// preserving their existing texture assignments. Original material assets are copied to
    /// a backup folder before their shaders are changed.
    /// </summary>
    public static class ASHNatureManufactureUrpConverter
    {
        private const string SourceFolder =
            "Assets/NatureManufacture Assets/Dynamic Nature - Mountain Tree Pack/Models/Materials";
        private const string BackupFolder = "Assets/_ASH/Art/MaterialBackups/NatureManufactureMountainTrees";

        [MenuItem("ASH/Rendering/Convert NatureManufacture Mountain Trees to URP")]
        public static string ConvertMountainTreeMaterials()
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
                throw new InvalidOperationException("URP/Lit shader was not found. Confirm that URP is active before converting.");

            if (!AssetDatabase.IsValidFolder(SourceFolder))
                throw new DirectoryNotFoundException("Source materials folder was not found: " + SourceFolder);

            EnsureFolder(BackupFolder);
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { SourceFolder });
            int converted = 0;
            int backedUp = 0;

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                        continue;

                    EditorUtility.DisplayProgressBar(
                        "ASH URP Material Conversion",
                        Path.GetFileNameWithoutExtension(path),
                        guids.Length == 0 ? 1f : i / (float)guids.Length);

                    BackupMaterial(path, material.name, ref backedUp);
                    ConvertMaterial(material, urpLit);
                    EditorUtility.SetDirty(material);
                    converted++;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[ASH] Converted {converted} NatureManufacture tree materials to URP/Lit. Created {backedUp} backups in {BackupFolder}.");
                return $"converted={converted}; backups={backedUp}; backupFolder={BackupFolder}";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ConvertMaterial(Material material, Shader urpLit)
        {
            Texture main = GetTexture(material, "_BaseMap", "_MainTex", "_Albedo");
            Vector2 mainScale = GetScale(material, "_BaseMap", "_MainTex", "_Albedo");
            Vector2 mainOffset = GetOffset(material, "_BaseMap", "_MainTex", "_Albedo");
            Texture normal = GetTexture(material, "_BumpMap", "_NormalMap");
            Texture packed = GetTexture(material, "_MaskMap", "_MetalicRAOGSmothnessA", "_SpecularRGBSmoothnessA", "_AmbientOcclusionGSmoothnessA");
            Color color = GetColor(material, "_BaseColor", "_Color");
            float cutoff = GetFloat(material, 0.4f, "_Cutoff");
            float smoothness = GetFloat(material, 0.35f, "_Smoothness", "_Glossiness", "_GlossMapScale");
            bool foliage = IsFoliage(material.name);

            material.shader = urpLit;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", main);
            material.SetTextureScale("_BaseMap", mainScale);
            material.SetTextureOffset("_BaseMap", mainOffset);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);

            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }

            // NatureManufacture's packed texture convention is metallic in R, AO in G,
            // and smoothness in A. URP/Lit reads the same channels from these two slots.
            if (packed != null)
            {
                material.SetTexture("_MetallicGlossMap", packed);
                material.SetTexture("_OcclusionMap", packed);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_OCCLUSIONMAP");
            }

            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 1f);
            material.enableInstancing = true;

            if (foliage)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", cutoff);
                material.SetFloat("_Cull", 0f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else
            {
                material.SetFloat("_AlphaClip", 0f);
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "Opaque");
            }
        }

        private static bool IsFoliage(string name)
        {
            return name.IndexOf("leaves", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("cross", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Texture GetTexture(Material material, params string[] properties)
        {
            foreach (string property in properties)
                if (material.HasProperty(property) && material.GetTexture(property) != null)
                    return material.GetTexture(property);
            return null;
        }

        private static Vector2 GetScale(Material material, params string[] properties)
        {
            foreach (string property in properties)
                if (material.HasProperty(property)) return material.GetTextureScale(property);
            return Vector2.one;
        }

        private static Vector2 GetOffset(Material material, params string[] properties)
        {
            foreach (string property in properties)
                if (material.HasProperty(property)) return material.GetTextureOffset(property);
            return Vector2.zero;
        }

        private static Color GetColor(Material material, params string[] properties)
        {
            foreach (string property in properties)
                if (material.HasProperty(property)) return material.GetColor(property);
            return Color.white;
        }

        private static float GetFloat(Material material, float fallback, params string[] properties)
        {
            foreach (string property in properties)
                if (material.HasProperty(property)) return material.GetFloat(property);
            return fallback;
        }

        private static void BackupMaterial(string sourcePath, string materialName, ref int backedUp)
        {
            string safeName = materialName.Replace('/', '_').Replace('\\', '_');
            string backupPath = $"{BackupFolder}/{safeName}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(backupPath) != null)
                return;

            if (AssetDatabase.CopyAsset(sourcePath, backupPath))
                backedUp++;
            else
                Debug.LogWarning($"[ASH] Could not back up material: {sourcePath}");
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] pieces = assetPath.Split('/');
            string current = pieces[0];
            for (int i = 1; i < pieces.Length; i++)
            {
                string next = current + "/" + pieces[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, pieces[i]);
                current = next;
            }
        }
    }
}
