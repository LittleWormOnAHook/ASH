using UnityEngine;

namespace ASH.Core
{
    /// <summary>Scene paths for the main menu flow. Resources/ASH_MenuConfig, written by ASH/Systems/Create Main Menu Scene.
    /// Builds also need both scenes in File > Build Profiles (scene list).</summary>
    [CreateAssetMenu(menuName = "ASH/Core/Menu Config", fileName = "ASH_MenuConfig")]
    public sealed class ASHMenuConfig : ScriptableObject
    {
        public string mainMenuScene = "Assets/_ASH/Scenes/ASH_MainMenu.unity";
        public string gameScene = "Assets/_ASH/Scenes/ASH_Prototype.unity";
        [TextArea(1, 3)] public string subtitle = "Survive the fallout.";

        static ASHMenuConfig _i;
        public static ASHMenuConfig I { get { if (!_i) { _i = Resources.Load<ASHMenuConfig>("ASH_MenuConfig"); if (!_i) _i = CreateInstance<ASHMenuConfig>(); } return _i; } }
    }
}
