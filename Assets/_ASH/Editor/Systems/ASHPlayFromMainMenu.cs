using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Editor Play starts the open scene, not build-index 0. When this toggle is on (default),
    /// Play opens ASH_MainMenu so Continue / New Game show. Off starts the open scene for quick tests.
    /// Saved in EditorPrefs. Does not change the build list or any scene asset.
    /// </summary>
    [InitializeOnLoad]
    public static class ASHPlayFromMainMenu
    {
        const string Pref = "ASH.PlayFromMainMenu";
        const string Menu = "ASH/Play From Main Menu";
        const string Scene = "Assets/_ASH/Scenes/ASH_MainMenu.unity";

        static ASHPlayFromMainMenu()
        {
            EditorApplication.delayCall += Apply;
        }

        public static bool Enabled => EditorPrefs.GetBool(Pref, true);

        [MenuItem(Menu, false, 1)]
        static void Toggle()
        {
            EditorPrefs.SetBool(Pref, !Enabled);
            Apply();
        }

        [MenuItem(Menu, true)]
        static bool ToggleValidate()
        {
            UnityEditor.Menu.SetChecked(Menu, Enabled);
            return true;
        }

        static void Apply()
        {
            if (Enabled)
            {
                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene);
                EditorSceneManager.playModeStartScene = scene;
                if (!scene)
                    Debug.LogWarning("[ASH] Play From Main Menu is on, but " + Scene + " was not found.");
            }
            else
            {
                EditorSceneManager.playModeStartScene = null;
            }
        }
    }
}
