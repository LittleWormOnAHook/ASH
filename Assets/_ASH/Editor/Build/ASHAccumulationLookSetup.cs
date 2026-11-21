using ASH.World.Accumulation;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>t471: accumulation Look asset. Step 1 migrates the scene controller's Look + Surface Texture values 1:1 into
    /// Resources/ASH_AccumulationLook (no visual change). Step 2 applies the textured look (project/vendor textures, grain,
    /// sparkle, macro variation, normals by depth). Every value stays editable in the asset (also in Play).</summary>
    public static class ASHAccumulationLookSetup
    {
        const string AssetPath = "Assets/_ASH/Resources/ASH_AccumulationLook.asset";
        const string SnowN = "Assets/Procedural Worlds/Packages - Install/Asset Samples/NatureManufacture/T_Ground_Snow_1_N.tga"; // pairs with the snow albedo already used
        const string AshN = "Assets/HQ Survival Pack/Models/Objects/Campfire/Materials/Campfire_Ash_NRM.png";
        const string DetailN = "Assets/NatureManufacture Assets/Snow Cover Textures/Snow Cover_N_v1.tga";

        static ASHAccumulationLookSettings GetOrCreate(out bool created)
        {
            var a = AssetDatabase.LoadAssetAtPath<ASHAccumulationLookSettings>(AssetPath); created = false;
            if (!a) { a = ScriptableObject.CreateInstance<ASHAccumulationLookSettings>(); AssetDatabase.CreateAsset(a, AssetPath); created = true; }
            return a;
        }

        [MenuItem("ASH/Systems/Accumulation/1. Migrate Look From Scene Controller")]
        public static void Migrate()
        {
            var c = Object.FindAnyObjectByType<ASHAccumulationController>(FindObjectsInactive.Include);
            if (!c) { Debug.LogWarning("[ASH] Accum look: no ASHAccumulationController in the open scene"); return; }
            var prev = c.look; c.look = null; // read the controller's own values
            var a = GetOrCreate(out bool created);
            if (!created) { Debug.Log("[ASH] Accum look: asset already exists (kept, not overwritten): " + AssetPath); c.look = prev; return; }
            a.CopyFrom(c); EditorUtility.SetDirty(a); AssetDatabase.SaveAssets(); c.look = prev;
            Debug.Log($"[ASH] Accum look: migrated controller values into {AssetPath} (ash {AssetDatabase.GetAssetPath(a.ashTexture)}, snow {AssetDatabase.GetAssetPath(a.snowTexture)}, tiling {a.ashTiling}/{a.snowTiling})");
        }

        [MenuItem("ASH/Systems/Accumulation/2. Apply Textured Look (t471)")]
        public static void ApplyTextured()
        {
            var a = GetOrCreate(out _); Undo.RecordObject(a, "textured look");
            Texture2D L(string p) { var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p); if (!t) Debug.LogWarning("[ASH] Accum look: missing " + p); return t; }
            if (!a.snowNormal) a.snowNormal = L(SnowN);
            if (!a.ashNormal) a.ashNormal = L(AshN);
            if (!a.detailNormal) a.detailNormal = L(DetailN);
            a.surfaceNormalStrength = 0.8f; a.detailTiling = 0.35f; a.detailNormalStrength = 0.45f; a.detailAlbedoStrength = 0.5f;
            a.ashTextureColour = 0.6f; a.ashColorVariation = new Color(0.29f, 0.26f, 0.23f); a.ashColorVariationAmount = 0.45f;
            a.ashGrain = 0.3f; a.ashGrainScale = 35f;
            a.snowSparkle = 0.8f; a.sparkleScale = 70f; a.sparkleDensity = 0.015f;
            a.macroScale = 30f; a.macroAlbedoVariation = 0.12f; a.macroUvWarp = 0.35f;
            a.normalDepthFade = 0.6f; a.normalFullDepth = 0.35f;
            EditorUtility.SetDirty(a); AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Accum look: textured look applied (snow N " + SnowN + ", ash N " + AshN + ", detail N " + DetailN + ")");
        }
    }
}