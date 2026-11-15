using ASH.Survival;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools.Systems
{
    /// <summary>Body temperature setup (idempotent). Scene edits are marked dirty, never saved.</summary>
    public static class ASHTemperatureInstaller
    {
        const string SettingsPath = ASHMenuInstaller.ResDir + "/ASH_TemperatureSettings.asset";

        [MenuItem("ASH/Systems/Temperature/Install Temperature (settings)", false, 200)]
        public static void Install()
        {
            var s = ASHMenuInstaller.Ensure<ASHTemperatureSettings>(SettingsPath);
            if (s.exhaleClips == null || s.exhaleClips.Length == 0 || System.Array.TrueForAll(s.exhaleClips, c => !c))
            {
                // Assets/Audio first (vendor library, not in git), then our CC0 breathing clip
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Others/Breath End.wav");
                if (!c) c = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_ASH/Audio/Exposure/ASH_Breathing_Loop.ogg");
                s.exhaleClips = c ? new[] { c } : new AudioClip[0];
            }
            EditorUtility.SetDirty(s); AssetDatabase.SaveAssets(); Selection.activeObject = s;
            Debug.Log($"[ASH] Temperature installed: {SettingsPath} (exhale: {(s.exhaleClips.Length > 0 ? s.exhaleClips[0].name : "none")}). Campfires get a heat source automatically at runtime; add lanterns/stoves/fireplaces with ASH/Systems/Add Heat Source to Selected.");
        }

        static void Add(ASHHeatSource.Preset p)
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
            {
                var h = go.GetComponent<ASHHeatSource>(); if (!h) h = Undo.AddComponent<ASHHeatSource>(go); else Undo.RecordObject(h, "Heat source");
                h.ApplyPreset(p);
                if (p == ASHHeatSource.Preset.Lantern && !go.GetComponentInChildren<Light>(true)) h.requireLight = false; // no light to follow: always warm
                EditorUtility.SetDirty(h); EditorSceneManager.MarkSceneDirty(go.scene); n++;
            }
            Debug.Log(n == 0 ? "[ASH] Select one or more objects first." : $"[ASH] {p} heat source on {n} object(s). Scene not saved.");
        }

        [MenuItem("ASH/Systems/Add Heat Source to Selected/Lantern", false, 210)] public static void Lantern() => Add(ASHHeatSource.Preset.Lantern);
        [MenuItem("ASH/Systems/Add Heat Source to Selected/Stove", false, 211)] public static void Stove() => Add(ASHHeatSource.Preset.Stove);
        [MenuItem("ASH/Systems/Add Heat Source to Selected/Fireplace", false, 212)] public static void Fireplace() => Add(ASHHeatSource.Preset.Fireplace);
        [MenuItem("ASH/Systems/Add Heat Source to Selected/Campfire", false, 213)] public static void Campfire() => Add(ASHHeatSource.Preset.Campfire);
        [MenuItem("ASH/Systems/Add Heat Source to Selected/Torch", false, 214)] public static void Torch() => Add(ASHHeatSource.Preset.Torch);

        [MenuItem("ASH/Systems/Add Heat Source to Selected/Lantern", true)] [MenuItem("ASH/Systems/Add Heat Source to Selected/Stove", true)]
        [MenuItem("ASH/Systems/Add Heat Source to Selected/Fireplace", true)] [MenuItem("ASH/Systems/Add Heat Source to Selected/Campfire", true)]
        [MenuItem("ASH/Systems/Add Heat Source to Selected/Torch", true)]
        static bool HasSelection() => Selection.gameObjects.Length > 0;
    }
}
