using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// Imports the ASH master heightmap with the exact settings from Docs/ASH_heightmap_import.md:
    /// 4097x4097, 16-bit, little-endian, Flip Vertically OFF, 4000 x 1400 x 4000 m, GameObject Y offset 2000 m.
    /// The RAW source is only read, never modified. The terrain is a PROCEDURAL FALLBACK, not real DEM data.
    /// "Flip off" reproduces Unity's Import Raw: file row r -> heightmap row r (world Z = r * spacing).
    /// File rows run north->south, so the reference north edge lands at world Z = 0 (documented in PrototypeStatus).
    /// </summary>
    public static class ASHTerrainImporter
    {
        public const string RawPath = "Assets/Terrain/ASH_heightmap_4km_R16LE_4097.raw";
        public const string TerrainFolder = "Assets/_ASH/Art/Terrain";
        public const string DataPath = TerrainFolder + "/ASH_Master_TerrainData.asset";
        public const int Resolution = 4097;
        public static readonly Vector3 Size = new Vector3(4000f, 1400f, 4000f);
        public const float WorldYOffset = 2000f;
        public const int AlphamapResolution = 2048;

        [MenuItem("ASH/World/1. Import Master Terrain Data")]
        public static void ImportMenu() => ImportOrUpdate();

        public static TerrainData ImportOrUpdate()
        {
            string full = Path.GetFullPath(RawPath);
            byte[] bytes = File.ReadAllBytes(full);
            long expected = (long)Resolution * Resolution * 2;
            if (bytes.Length != expected)
                throw new InvalidDataException($"[ASH] {RawPath} is {bytes.Length} bytes, expected {expected} (4097^2 x 16-bit).");

            EnsureFolder(TerrainFolder);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath);
            bool created = data == null;
            if (created) data = new TerrainData();

            data.heightmapResolution = Resolution; // must precede size
            data.size = Size;

            var heights = new float[Resolution, Resolution];
            const float inv = 1f / 65535f;
            int i = 0;
            for (int row = 0; row < Resolution; row++)
            {
                for (int col = 0; col < Resolution; col++, i += 2)
                {
                    ushort v = (ushort)(bytes[i] | (bytes[i + 1] << 8)); // little-endian
                    heights[row, col] = v * inv; // flip vertically OFF
                }
            }
            data.SetHeights(0, 0, heights);

            data.alphamapResolution = AlphamapResolution;
            data.baseMapResolution = 1024;
            data.SetDetailResolution(1024, 32);
            data.wavingGrassStrength = 0.25f;
            data.wavingGrassAmount = 0.35f;
            data.wavingGrassSpeed = 0.35f;

            if (created) AssetDatabase.CreateAsset(data, DataPath);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] Master terrain imported: {Resolution}^2, size {Size}, flip=off, raw untouched. Min/max normalized from {RawPath}.");
            return data;
        }

        public static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int p = 1; p < parts.Length; p++)
            {
                string next = cur + "/" + parts[p];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[p]);
                cur = next;
            }
        }
    }
}
