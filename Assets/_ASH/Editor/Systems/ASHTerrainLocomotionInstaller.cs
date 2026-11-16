using ASH.EditorTools.Systems;
using ASH.Player;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.PlayerTools
{
    /// <summary>Idempotent: creates Resources/ASH_TerrainLocomotionSettings. The runtime component auto-attaches to the player. Scene is not saved.</summary>
    public static class ASHTerrainLocomotionInstaller
    {
        public const string SettingsPath = ASHMenuInstaller.ResDir + "/ASH_TerrainLocomotionSettings.asset";

        [MenuItem("ASH/Systems/Movement/Install Terrain Slowdown (settings)", false, 250)]
        public static void Install()
        {
            var s = ASHMenuInstaller.Ensure<ASHTerrainLocomotionSettings>(SettingsPath);
            if (s.uphillSpeed == null || s.uphillSpeed.length == 0) s.ApplyPreset(ASHTerrainLocomotionSettings.Preset.Default);
            EditorUtility.SetDirty(s); AssetDatabase.SaveAssets(); Selection.activeObject = s;
            Debug.Log($"[ASH] Terrain slowdown settings: {SettingsPath}. The player component attaches automatically in Play. Slope, depth and stamina ranges are on the asset (presets Relaxed / Default / Extreme).");
        }

        [MenuItem("ASH/Systems/Movement/Select Terrain Slowdown Settings", false, 251)]
        public static void Select()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHTerrainLocomotionSettings>(SettingsPath);
            if (!s) { Install(); return; }
            Selection.activeObject = s; EditorGUIUtility.PingObject(s);
        }

        [MenuItem("ASH/Systems/Movement/Preset/Relaxed", false, 260)] public static void Relaxed() => Set(ASHTerrainLocomotionSettings.Preset.Relaxed);
        [MenuItem("ASH/Systems/Movement/Preset/Default", false, 261)] public static void Default() => Set(ASHTerrainLocomotionSettings.Preset.Default);
        [MenuItem("ASH/Systems/Movement/Preset/Extreme (stress test)", false, 262)] public static void Extreme() => Set(ASHTerrainLocomotionSettings.Preset.Extreme);

        static void Set(ASHTerrainLocomotionSettings.Preset p)
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHTerrainLocomotionSettings>(SettingsPath) ?? ASHMenuInstaller.Ensure<ASHTerrainLocomotionSettings>(SettingsPath);
            s.ApplyPreset(p); EditorUtility.SetDirty(s); AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] Terrain slowdown preset {p}.");
        }
    }
}
