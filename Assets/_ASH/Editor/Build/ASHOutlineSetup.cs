using ASH.Interaction;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    public static class ASHOutlineSetup
    {
        [MenuItem("ASH/Systems/Interaction/Create Harvest Outline Settings")]
        public static void Create()
        {
            const string p = "Assets/_ASH/Resources/ASH_OutlineSettings.asset";
            var a = AssetDatabase.LoadAssetAtPath<ASHOutlineSettings>(p);
            if (!a) { a = ScriptableObject.CreateInstance<ASHOutlineSettings>(); AssetDatabase.CreateAsset(a, p); }
            if (!a.shader) a.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_ASH/Shaders/Rendering/ASH_HarvestOutline.shader");
            if (!a.foliageShader) a.foliageShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_ASH/Shaders/Rendering/ASH_HarvestFoliageGlow.shader");
            EditorUtility.SetDirty(a); AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] Harvest outline settings: {p} (outline {(a.shader ? a.shader.name : "MISSING")}, foliage {(a.foliageShader ? a.foliageShader.name : "MISSING")})");
        }
    }
}
