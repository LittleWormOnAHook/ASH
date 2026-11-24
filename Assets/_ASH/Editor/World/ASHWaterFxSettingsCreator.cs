using ASH.World.Water;
using UnityEditor;

namespace ASH.EditorTools.World
{
    static class ASHWaterFxSettingsCreator
    {
        const string Path = "Assets/_ASH/Resources/ASH_WaterFxSettings.asset";
        [InitializeOnLoadMethod]
        static void Ensure()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.LoadAssetAtPath<ASHWaterFxSettings>(Path)) return;
                AssetDatabase.CreateAsset(UnityEngine.ScriptableObject.CreateInstance<ASHWaterFxSettings>(), Path);
                AssetDatabase.SaveAssets();
            };
        }
        [MenuItem("ASH/Systems/Water/Select Water FX Settings")]
        static void Select() { Selection.activeObject = AssetDatabase.LoadAssetAtPath<ASHWaterFxSettings>(Path); }
    }
}
