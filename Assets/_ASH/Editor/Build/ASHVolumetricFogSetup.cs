using System.Linq;
using ASH.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ASH.EditorTools
{
    /// <summary>ASH > Rendering > Install Volumetric Fog: adds ASHVolumetricFogFeature to every URP renderer used by the URP assets
    /// in Assets/Settings and an enabled "ASH Volumetric Fog" override to the scene's Global Volume profile (base height = ground
    /// under the player). Idempotent: existing features / overrides are left as they are. Never saves the scene.</summary>
    public static class ASHVolumetricFogSetup
    {
        const string ShaderPath = "Assets/_ASH/Shaders/Rendering/ASH_VolumetricFog.shader";

        [MenuItem("GameObject/ASH/Volumetric Fog Area", false, 10)]
        static void CreateArea(MenuCommand cmd)
        {
            var go = new GameObject("ASH Fog Area");
            var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(20f, 6f, 20f); box.center = new Vector3(0f, 3f, 0f);
            go.AddComponent<ASHFogArea>();
            GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
            if (!(cmd.context is GameObject) && SceneView.lastActiveSceneView)
            {
                var piv = SceneView.lastActiveSceneView.pivot; var t = Terrain.activeTerrain;
                if (t) piv.y = t.SampleHeight(piv) + t.GetPosition().y;
                go.transform.position = piv;
            }
            Undo.RegisterCreatedObjectUndo(go, "Create ASH Fog Area"); Selection.activeObject = go;
        }

        [MenuItem("ASH/Rendering/Install Volumetric Fog")]
        public static void Install()
        {
            var sh = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            int added = 0;
            var rendererPaths = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" })
                .Select(g => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a).SelectMany(a => { var so = new SerializedObject(a); var l = so.FindProperty("m_RendererDataList");
                    return Enumerable.Range(0, l.arraySize).Select(i => l.GetArrayElementAtIndex(i).objectReferenceValue as UniversalRendererData); })
                .Where(r => r).Distinct();
            foreach (var data in rendererPaths)
            {
                var existing = data.rendererFeatures.OfType<ASHVolumetricFogFeature>().FirstOrDefault();
                if (existing) { if (!existing.shader && sh) { existing.shader = sh; EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(data); } continue; }
                var f = ScriptableObject.CreateInstance<ASHVolumetricFogFeature>(); f.name = "ASHVolumetricFog"; f.shader = sh;
                AssetDatabase.AddObjectToAsset(f, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long id);
                var so = new SerializedObject(data); var feats = so.FindProperty("m_RendererFeatures"); var map = so.FindProperty("m_RendererFeatureMap");
                feats.arraySize++; feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = f;
                map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                so.ApplyModifiedProperties(); data.SetDirty(); EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
                added++; Debug.Log($"[ASH] VOLFOG feature added to {AssetDatabase.GetAssetPath(data)}");
            }

            var vol = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).Where(x => x.isGlobal && x.sharedProfile).OrderByDescending(x => x.priority).FirstOrDefault();
            var prof = vol ? vol.sharedProfile : null;
            if (!prof) { Debug.LogWarning("[ASH] VOLFOG no Global Volume profile in the open scene"); return; }
            if (!prof.TryGet(out ASHVolumetricFog fog))
            {
                fog = prof.Add<ASHVolumetricFog>(false); fog.name = "ASHVolumetricFog"; fog.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(fog, prof);
                fog.enabled.Override(true); fog.quality.Override(ASHFogQuality.Medium);
                fog.density.Override(fog.density.value); fog.heightFalloff.Override(fog.heightFalloff.value); fog.maxDistance.Override(fog.maxDistance.value);
                fog.shadows.Override(true); fog.useDayNightColor.Override(true);
                var t = Terrain.activeTerrain; var p = GameObject.FindWithTag("Player"); var at = p ? p.transform.position : (Camera.main ? Camera.main.transform.position : Vector3.zero);
                fog.baseHeight.Override(t ? t.SampleHeight(at) + t.GetPosition().y : at.y);
                EditorUtility.SetDirty(fog); EditorUtility.SetDirty(prof); AssetDatabase.SaveAssetIfDirty(prof);
                Debug.Log($"[ASH] VOLFOG override added to {AssetDatabase.GetAssetPath(prof)} baseHeight={fog.baseHeight.value:F1}");
            }
            Debug.Log($"[ASH] VOLFOG install done features+{added} profile={AssetDatabase.GetAssetPath(prof)}");
        }
    }
}
