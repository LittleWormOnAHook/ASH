using System.Linq;
using ASH.Core;
using ASH.Inventory;
using ASH.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.EditorTools.Systems
{
    /// <summary>Main menu + save system setup. Idempotent; never saves the open game scene.</summary>
    public static class ASHMenuInstaller
    {
        public const string ResDir = "Assets/_ASH/Resources";
        public const string MenuScene = "Assets/_ASH/Scenes/ASH_MainMenu.unity";

        public static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/'); string cur = parts[0];
            for (int i = 1; i < parts.Length; i++) { var next = cur + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]); cur = next; }
        }

        public static T Ensure<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path); if (a) return a;
            Folder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a, path); return a;
        }

        /// <summary>Save settings, menu config and the item registry (all items in the project) in Resources.</summary>
        [MenuItem("ASH/Systems/Saves/Install Save Data (settings, item registry)", false, 100)]
        public static void InstallSaveData()
        {
            Ensure<ASHSaveSettings>($"{ResDir}/ASH_SaveSettings.asset");
            var cfg = Ensure<ASHMenuConfig>($"{ResDir}/ASH_MenuConfig.asset");
            RefreshRegistry();
            AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] Save data installed: {ResDir}/ASH_SaveSettings, ASH_MenuConfig (game scene {cfg.gameScene}), ASH_ItemRegistry");
        }

        public static void RefreshRegistry()
        {
            var reg = Ensure<ASHItemRegistry>($"{ResDir}/ASH_ItemRegistry.asset");
            reg.items = AssetDatabase.FindAssets("t:ASHItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(i => i).OrderBy(i => i.itemId).ToList();
            EditorUtility.SetDirty(reg);
        }

        /// <summary>Creates (or rebuilds) Assets/_ASH/Scenes/ASH_MainMenu.unity as a separate scene, additively, without touching
        /// or saving the open scene. The open scene becomes the game scene in ASH_MenuConfig.</summary>
        [MenuItem("ASH/Systems/Saves/Create Main Menu Scene", false, 101)]
        public static void CreateMenuScene()
        {
            if (Application.isPlaying) { Debug.LogWarning("[ASH] Exit Play Mode first"); return; }
            InstallSaveData();
            var game = SceneManager.GetActiveScene();
            var cfg = AssetDatabase.LoadAssetAtPath<ASHMenuConfig>($"{ResDir}/ASH_MenuConfig.asset");
            if (!string.IsNullOrEmpty(game.path) && game.path != MenuScene) { cfg.gameScene = game.path; }
            cfg.mainMenuScene = MenuScene; EditorUtility.SetDirty(cfg); AssetDatabase.SaveAssets();

            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.path == MenuScene) EditorSceneManager.CloseScene(s, true); }
            Folder("Assets/_ASH/Scenes");
            var menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var cam = new GameObject("MenuCamera", typeof(Camera), typeof(AudioListener));
            var c = cam.GetComponent<Camera>(); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.06f, 0.055f, 0.05f); c.tag = "MainCamera";
            var root = new GameObject("ASH_MainMenu", typeof(ASHMainMenu));
            SceneManager.MoveGameObjectToScene(cam, menu); SceneManager.MoveGameObjectToScene(root, menu);
            EditorSceneManager.SaveScene(menu, MenuScene);
            if (game.IsValid()) SceneManager.SetActiveScene(game);
            EditorSceneManager.CloseScene(menu, true);
            Debug.Log($"[ASH] Main menu scene written: {MenuScene}. Game scene: {cfg.gameScene}. In the Editor, press Play in the menu scene (open it from the Project window). " +
                      "For builds, add both scenes to File > Build Profiles > Scene List (menu first).");
        }

        [MenuItem("ASH/Systems/Saves/Open Save Folder", false, 102)]
        static void OpenSaves() { System.IO.Directory.CreateDirectory(ASHSaveSystem.Dir); EditorUtility.RevealInFinder(ASHSaveSystem.Dir); }

        [MenuItem("ASH/Systems/Saves/Debug: Save to Slot 1", false, 120)]
        static void DSave() { if (ASHSaveSystem.Instance) ASHSaveSystem.Instance.Save("1"); else Debug.LogWarning("[ASH] Play Mode in the game scene only"); }
        [MenuItem("ASH/Systems/Saves/Debug: Load Newest", false, 121)]
        static void DLoad() { if (Application.isPlaying) ASHSaveSystem.Load(ASHSaveSystem.Newest()); }
    }
}
