using ASH.UI;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Systems
{
    /// <summary>Creates Resources/ASH_CompassSettings. Idempotent; no scene changes (the HUD builds the compass at runtime).</summary>
    public static class ASHCompassInstaller
    {
        const string SettingsPath = ASHMenuInstaller.ResDir + "/ASH_CompassSettings.asset";

        [MenuItem("ASH/Systems/Compass/Install Compass Settings", false, 250)]
        public static void Install()
        {
            var s = ASHMenuInstaller.Ensure<ASHCompassSettings>(SettingsPath);
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            Selection.activeObject = s;
            Debug.Log("[ASH] Compass settings: " + SettingsPath + ". The HUD builds a top-centre compass at runtime (no scene save). " +
                      "The strip always shows; known POI / survey / campfire dots need the GPS carried (both are toggles on the asset). " +
                      "Enemies register later with ASHCompass.Register(transform, ASHCompassMarkerKind.Enemy).");
        }

        [MenuItem("ASH/Systems/Compass/Select Compass Settings", false, 251)]
        public static void Select()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHCompassSettings>(SettingsPath);
            if (!s) { Install(); return; }
            Selection.activeObject = s;
        }
    }
}
