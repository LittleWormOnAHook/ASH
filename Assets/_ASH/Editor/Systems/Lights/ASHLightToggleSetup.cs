using ASH.Gameplay;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Systems.Lights
{
    /// <summary>Creates Resources/ASH_LightToggleSettings and (in Play) attaches toggles at runtime. Never writes the open scene.</summary>
    [InitializeOnLoad]
    public static class ASHLightToggleSetup
    {
        const string SettingsPath = ASHMenuInstaller.ResDir + "/ASH_LightToggleSettings.asset";

        static ASHLightToggleSetup()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.ExitingPlayMode) return;
            var a = AssetDatabase.LoadAssetAtPath<ASHLightToggleSettings>(SettingsPath);
            if (!a) return;
            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssetIfDirty(a);
        }

        [MenuItem("ASH/Systems/Lights/Setup Fuel-Free Lights (idempotent)", false, 280)]
        public static void Setup()
        {
            var s = EnsureSettings();
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            Selection.activeObject = s;
            if (Application.isPlaying)
            {
                ASHLightToggle.EnsureAll();
                ASHLightToggle.ApplyCabinStoveStart();
                Debug.Log("[ASH] Light toggle: settings ready, runtime attach ran. Cabin stove starts off on a new game. E toggles the hanging lantern. Scene was not saved.");
                return;
            }
            Debug.Log("[ASH] Light toggle: settings at " + SettingsPath + ". Play attaches the toggle to the hanging lantern and puts the cabin stove out (empty, needs fuel). " +
                      "No scene save. Drop ASHLightToggle on any fuel-free lamp later, or add a name pattern on the settings asset. Edits in Play are kept.");
        }

        [MenuItem("ASH/Systems/Lights/Select Light Toggle Settings", false, 281)]
        public static void Select()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHLightToggleSettings>(SettingsPath);
            if (!s) { Setup(); return; }
            Selection.activeObject = s;
        }

        public static ASHLightToggleSettings EnsureSettings()
        {
            return ASHMenuInstaller.Ensure<ASHLightToggleSettings>(SettingsPath);
        }
    }
}
