#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.EditorTools
{
    /// <summary>
    /// Gaia Water's planar reflections temporarily write QualitySettings.lodBias and
    /// maximumLODLevel. If that state is serialized, trees and vegetation vanish.
    /// This keeps the URP Gaia Water prefab usable with planar reflections off and a
    /// local ReflectionProbe, and never saves the open scene.
    /// </summary>
    [InitializeOnLoad]
    public static class ASHGaiaWaterSetup
    {
        const string PrefabPath = "Assets/Procedural Worlds/Packages - Install/Gaia Water/Water Prefabs/URP/Gaia Water.prefab";
        const string ProbeName = "ASH Local Reflections";
        const float PcLodBias = 2f;
        const int PcMaxLod = 0;

        static ASHGaiaWaterSetup()
        {
            EditorApplication.delayCall += RestoreQualityIfGaiaCorrupted;
            EditorApplication.playModeStateChanged += _ => RestoreQualityIfGaiaCorrupted();
        }

        [MenuItem("ASH/Systems/Water/Configure Gaia Water (local reflections)", false, 240)]
        public static void Configure()
        {
            RestoreQualityIfGaiaCorrupted();

            int prefabCount = 0;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                if (ConfigurePrefabAsset()) prefabCount++;
            }
            else
            {
                Debug.LogWarning("[ASH] Gaia Water prefab not found (vendor pack missing?). " + PrefabPath);
            }

            int sceneChanged = 0, sceneFound = 0;
            var seen = new System.Collections.Generic.HashSet<GameObject>();
            foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (tr == null) continue;
                bool named = tr.name == "Gaia Water" || tr.name.StartsWith("Gaia Water (");
                bool hasWater = false;
                foreach (var mb in tr.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    var n = mb.GetType().Name;
                    if (n == "PWS_WaterSystem" || n == "GRC_Water") { hasWater = true; break; }
                }
                if (!named && !hasWater) continue;

                Transform water = tr;
                if (hasWater && !named)
                {
                    var p = tr.parent;
                    while (p != null)
                    {
                        if (p.name == "Gaia Water" || p.name.StartsWith("Gaia Water (")) { water = p; break; }
                        p = p.parent;
                    }
                }
                if (!seen.Add(water.gameObject)) continue;
                sceneFound++;
                var go = water.gameObject;
                bool changed = ConfigureRoot(go, addProbe: true);
                if (changed)
                {
                    sceneChanged++;
                    EditorUtility.SetDirty(go);
                }
                Debug.Log("[ASH] Gaia Water instance '" + PathOf(water) + "' " + (changed ? "configured" : "already set") +
                          " (planar off, local probe '" + ProbeName + "').");
            }

            DisableLoosePlanarInScene();
            Debug.Log("[ASH] Gaia Water: planar reflections stay off (they write project QualitySettings). " +
                      "Local ReflectionProbe '" + ProbeName + "' provides reflections on the water object. " +
                      "Prefab " + (prefabCount > 0 ? "updated" : "unchanged") +
                      ", scene instances found: " + sceneFound + ", updated: " + sceneChanged +
                      " (scene not saved). Quality lodBias=" + QualitySettings.lodBias +
                      " maximumLODLevel=" + QualitySettings.maximumLODLevel + ".");
        }

        [MenuItem("ASH/Systems/Water/Reimport Accumulation Shaders", false, 241)]
        public static void ReimportAccumulationShaders()
        {
            const string dir = "Assets/_ASH/Shaders/Accumulation";
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                n++;
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log("[ASH] Reimported " + n + " accumulation shader assets in " + dir + ".");
        }

        static string PathOf(Transform t)
        {
            if (t == null) return "?";
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        internal static void RestoreQualityIfGaiaCorrupted()
        {
            // GaiaPlanarReflections.Set() assigns maximumLODLevel = 1 and halves lodBias
            // (repeatedly down to ~0.01 if Restore does not run). Those hide tree/veg LODs.
            bool badLod = QualitySettings.lodBias <= 0.05f;
            bool badMax = QualitySettings.maximumLODLevel > 0;
            if (!badLod && !badMax) return;
            if (badLod) QualitySettings.lodBias = PcLodBias;
            if (badMax) QualitySettings.maximumLODLevel = PcMaxLod;
            Debug.Log("[ASH] Restored QualitySettings after Gaia planar reflections changed them: lodBias=" +
                      QualitySettings.lodBias + " maximumLODLevel=" + QualitySettings.maximumLODLevel + ".");
        }

        static bool ConfigurePrefabAsset()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                bool changed = ConfigureRoot(root, addProbe: true);
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static bool ConfigureRoot(GameObject root, bool addProbe)
        {
            if (root == null) return false;
            bool changed = false;

            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var so = new SerializedObject(mb);
                var disableAll = so.FindProperty("m_disableAllReflections");
                if (disableAll != null && !disableAll.boolValue)
                {
                    disableAll.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }

                if (mb.GetType().Name == "GaiaPlanarReflections")
                {
                    if (mb.enabled)
                    {
                        mb.enabled = false;
                        changed = true;
                    }
                    var settings = so.FindProperty("m_settings");
                    var enableRefl = settings != null ? settings.FindPropertyRelative("m_enableReflections") : null;
                    if (enableRefl != null && enableRefl.boolValue)
                    {
                        enableRefl.boolValue = false;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;
                    }
                }
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == "Gaia Planar Reflections" && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                    changed = true;
                }
            }

            if (addProbe && EnsureLocalProbe(root)) changed = true;
            return changed;
        }

        static bool EnsureLocalProbe(GameObject root)
        {
            Transform existing = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == ProbeName) { existing = t; break; }
            }

            ReflectionProbe probe;
            if (existing == null)
            {
                var go = new GameObject(ProbeName);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = Vector3.zero;
                probe = go.AddComponent<ReflectionProbe>();
            }
            else
            {
                probe = existing.GetComponent<ReflectionProbe>();
                if (probe == null) probe = existing.gameObject.AddComponent<ReflectionProbe>();
            }

            bool changed = existing == null;
            if (probe.mode != ReflectionProbeMode.Realtime) { probe.mode = ReflectionProbeMode.Realtime; changed = true; }
            if (probe.refreshMode != ReflectionProbeRefreshMode.OnAwake) { probe.refreshMode = ReflectionProbeRefreshMode.OnAwake; changed = true; }
            if (probe.timeSlicingMode != ReflectionProbeTimeSlicingMode.IndividualFaces)
            {
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                changed = true;
            }
            if (probe.resolution != 256) { probe.resolution = 256; changed = true; }
            if (!probe.hdr) { probe.hdr = true; changed = true; }
            if (probe.size != new Vector3(200f, 80f, 200f)) { probe.size = new Vector3(200f, 80f, 200f); changed = true; }
            if (probe.center != new Vector3(0f, 10f, 0f)) { probe.center = new Vector3(0f, 10f, 0f); changed = true; }
            if (!probe.boxProjection) { probe.boxProjection = true; changed = true; }
            if (probe.clearFlags != ReflectionProbeClearFlags.Skybox) { probe.clearFlags = ReflectionProbeClearFlags.Skybox; changed = true; }
            return changed;
        }

        static void DisableLoosePlanarInScene()
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null || mb.GetType().Name != "GaiaPlanarReflections") continue;
                if (mb.enabled)
                {
                    mb.enabled = false;
                    EditorUtility.SetDirty(mb);
                }
                if (mb.gameObject.activeSelf && mb.gameObject.name == "Gaia Planar Reflections")
                {
                    mb.gameObject.SetActive(false);
                    EditorUtility.SetDirty(mb.gameObject);
                }
            }
        }
    }
}
#endif
