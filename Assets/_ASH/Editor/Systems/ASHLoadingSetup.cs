#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ASH.Core;
using ASH.EditorTools.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.EditorTools
{
    /// <summary>
    /// ASH/Systems/Loading/Setup Loading Screen (idempotent): creates Resources/ASH_LoadingSettings and collects the shader variants.
    /// ASH/Systems/Loading/Collect Shader Variants (idempotent): Assets/_ASH/Settings/ASH_ShaderVariants.shadervariants from
    ///  (a) the variants the Editor has compiled this session (play the game a bit first for the best list) and
    ///  (b) the keyword sets of every material in the open scenes, the bird / wildlife prefabs and the loading settings' prewarm list.
    /// Never touches ProjectSettings (Graphics > Preloaded Shaders stays as it is); the loading screen warms the collection itself.
    /// </summary>
    public static class ASHLoadingSetup
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_LoadingSettings.asset";
        const string SvcPath = "Assets/_ASH/Settings/ASH_ShaderVariants.shadervariants";

        [MenuItem("ASH/Systems/Loading/Setup Loading Screen (idempotent)", false, 280)]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }
            var s = EnsureSettings();
            Collect(s);
        }

        [MenuItem("ASH/Systems/Loading/Collect Shader Variants (idempotent)", false, 281)]
        public static void CollectMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }
            Collect(EnsureSettings());
        }

        static ASHLoadingSettings EnsureSettings()
        {
            ASHMenuInstaller.Folder("Assets/_ASH/Resources");
            var s = AssetDatabase.LoadAssetAtPath<ASHLoadingSettings>(SettingsPath);
            if (!s) { s = ScriptableObject.CreateInstance<ASHLoadingSettings>(); AssetDatabase.CreateAsset(s, SettingsPath); }
            return s;
        }

        static void Collect(ASHLoadingSettings s)
        {
            ASHMenuInstaller.Folder("Assets/_ASH/Settings");
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(SvcPath);
            int fromSession = 0;
            // (a) variants the Editor tracked this session (internal API, read through reflection)
            var su = typeof(ShaderUtil);
            var save = su.GetMethod("SaveCurrentShaderVariantCollection", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            const string tmp = "Assets/_ASH/Settings/ASH_ShaderVariants_Session.shadervariants";
            if (save != null)
            {
                try
                {
                    save.Invoke(null, new object[] { tmp });
                    AssetDatabase.ImportAsset(tmp, ImportAssetOptions.ForceSynchronousImport);
                }
                catch (System.Exception e) { Debug.LogWarning("[ASH] Loading: session variants: " + (e.InnerException ?? e).Message); }
            }
            var session = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(tmp);
            if (!svc)
            {
                svc = new ShaderVariantCollection { name = "ASH_ShaderVariants" };
                AssetDatabase.CreateAsset(svc, SvcPath);
            }
            int before = svc.variantCount;
            if (session)
            {
                // copy: the session collection serializes per shader the list of (keywords, passType)
                var so = new SerializedObject(session);
                var shaders = so.FindProperty("m_Shaders");
                for (int i = 0; shaders != null && i < shaders.arraySize; i++)
                {
                    var e = shaders.GetArrayElementAtIndex(i);
                    var sh = e.FindPropertyRelative("first").objectReferenceValue as Shader;
                    var vars = e.FindPropertyRelative("second.variants");
                    if (!sh || vars == null) continue;
                    for (int v = 0; v < vars.arraySize; v++)
                    {
                        var ve = vars.GetArrayElementAtIndex(v);
                        var kw = ve.FindPropertyRelative("keywords").stringValue;
                        var pt = (PassType)ve.FindPropertyRelative("passType").intValue;
                        if (TryAdd(svc, sh, pt, string.IsNullOrEmpty(kw) ? new string[0] : kw.Split(' '))) fromSession++;
                    }
                }
                AssetDatabase.DeleteAsset(tmp);
            }

            // (b) materials: open scenes, bird + wildlife prefabs, prewarm list
            var mats = new HashSet<Material>();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var sc = EditorSceneManager.GetSceneAt(i);
                if (!sc.isLoaded) continue;
                foreach (var root in sc.GetRootGameObjects())
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) mats.Add(m);
                    foreach (var t in root.GetComponentsInChildren<Terrain>(true)) if (t.materialTemplate) mats.Add(t.materialTemplate);
                }
            }
            var prefabDirs = new[] { "Assets/_ASH/Prefabs/Birds", "Assets/_ASH/Generated/Birds", "Assets/_ASH/Prefabs/Wildlife" }.Where(AssetDatabase.IsValidFolder).ToArray();
            if (prefabDirs.Length > 0)
                foreach (var g in AssetDatabase.FindAssets("t:Prefab", prefabDirs))
                {
                    var p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (p) foreach (var r in p.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) mats.Add(m);
                }
            if (s.prewarmPrefabs != null) foreach (var p in s.prewarmPrefabs) if (p) foreach (var r in p.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) mats.Add(m);
            if (s.prewarmMaterials != null) foreach (var m in s.prewarmMaterials) if (m) mats.Add(m);
            int fromMats = 0;
            var passTypes = new[] { PassType.ScriptableRenderPipelineDefaultUnlit, PassType.ScriptableRenderPipeline, PassType.ShadowCaster };
            foreach (var m in mats)
            {
                if (!m.shader) continue;
                var kws = m.shaderKeywords;
                foreach (var pt in passTypes) if (TryAdd(svc, m.shader, pt, kws)) fromMats++;
            }
            if (!s.shaderVariants) s.shaderVariants = svc;
            EditorUtility.SetDirty(svc);
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] Loading screen ready: {SettingsPath}. Shader variants {SvcPath}: {svc.shaderCount} shaders, {svc.variantCount} variants (was {before}; +{fromSession} from this Editor session, +{fromMats} from {mats.Count} materials). ProjectSettings untouched. Tip: play the game a while in the Editor, stop, then run ASH/Systems/Loading/Collect Shader Variants again to capture more.");
        }

        static bool TryAdd(ShaderVariantCollection svc, Shader sh, PassType pt, string[] kws)
        {
            try
            {
                var v = new ShaderVariantCollection.ShaderVariant(sh, pt, kws);
                if (svc.Contains(v)) return false;
                return svc.Add(v);
            }
            catch { return false; } // that pass / keyword set doesn't exist in this shader
        }
    }
}
#endif
