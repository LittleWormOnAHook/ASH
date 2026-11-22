using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ASH.EditorTools
{
    /// <summary>
    /// t566 load-time settings, approved by Anthony: Enter Play Mode without a domain reload (scene reload stays on),
    /// Graphics > Log Shader Compilation on, and a 4096 additional-light shadow atlas on the PC URP asset.
    /// Applied once per machine after this compiles (the ProjectSettings files are committed with the same values, this just keeps
    /// the open Editor's in-memory settings in step so it doesn't write the old values back). The menu re-applies on demand.
    /// Revert domain reload: Project Settings > Editor > Enter Play Mode Settings > tick "Reload Domain".
    /// </summary>
    [InitializeOnLoad]
    static class ASHLoadTimeSettings
    {
        const string DoneKey = "ASH_t566_LoadTimeSettingsApplied";
        const string QualityName = "PC";
        const int ShadowAtlas = 4096;

        static ASHLoadTimeSettings()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(DoneKey, false)) return;
                Apply();
                EditorPrefs.SetBool(DoneKey, true);
            };
        }

        [MenuItem("ASH/Systems/Project/Apply Load-Time Settings (no domain reload, shader log, 4096 shadow atlas)")]
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play first."); return; }
            Apply();
        }

        static void Apply()
        {
            var log = new System.Text.StringBuilder("[ASH] Load-time settings:");

            var want = (EditorSettings.enterPlayModeOptions | EnterPlayModeOptions.DisableDomainReload) & ~EnterPlayModeOptions.DisableSceneReload;
            if (!EditorSettings.enterPlayModeOptionsEnabled) EditorSettings.enterPlayModeOptionsEnabled = true;
            if (EditorSettings.enterPlayModeOptions != want) EditorSettings.enterPlayModeOptions = want;
            log.Append(" domain reload off (scene reload on);");

            if (!GraphicsSettings.logWhenShaderIsCompiled) GraphicsSettings.logWhenShaderIsCompiled = true;
            log.Append(" Log Shader Compilation on;");

            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] != QualityName) continue;
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset urp)
                {
                    if (urp.additionalLightsShadowmapResolution < ShadowAtlas)
                    {
                        urp.additionalLightsShadowmapResolution = ShadowAtlas;
                        EditorUtility.SetDirty(urp);
                        AssetDatabase.SaveAssetIfDirty(urp);
                    }
                    log.Append(" additional-light shadow atlas ").Append(urp.additionalLightsShadowmapResolution).Append(" on ").Append(urp.name).Append('.');
                }
            }
            Debug.Log(log.ToString());
        }
    }
}
