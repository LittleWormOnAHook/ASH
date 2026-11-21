using ASH.UI;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: imports the Rook-tab silhouette as a Sprite and creates/links ASH_CharacterTabSettings.</summary>
    public static class ASHRookTabSetup
    {
        const string SilPath = "Assets/_ASH/Art/UI/ASH_Silhouette_Man.png";
        const string AssetPath = "Assets/_ASH/Resources/ASH_CharacterTabSettings.asset";

        [MenuItem("ASH/Systems/Rook Tab/Setup Rook Tab (idempotent)", false, 230)]
        public static string Setup()
        {
            var ti = AssetImporter.GetAtPath(SilPath) as TextureImporter;
            if (ti && (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || !ti.alphaIsTransparency))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SilPath);

            var s = AssetDatabase.LoadAssetAtPath<ASHCharacterTabSettings>(AssetPath);
            if (!s)
            {
                s = ScriptableObject.CreateInstance<ASHCharacterTabSettings>();
                AssetDatabase.CreateAsset(s, AssetPath);
            }
            if (!s.silhouette && sprite) { s.silhouette = sprite; EditorUtility.SetDirty(s); }
            AssetDatabase.SaveAssetIfDirty(s);
            string msg = $"[ASH] Rook tab: settings={(s ? "ok" : "missing")} silhouette={(s.silhouette ? s.silhouette.name : "missing")}";
            Debug.Log(msg);
            return msg;
        }

        [MenuItem("ASH/Systems/Rook Tab/Select Rook Tab Settings", false, 231)]
        static void SelectSettings() { Setup(); Selection.activeObject = AssetDatabase.LoadAssetAtPath<ASHCharacterTabSettings>(AssetPath); }
    }
}
