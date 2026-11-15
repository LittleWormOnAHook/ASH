using System.Linq;
using ASH.Inventory;
using ASH.Survival;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools.Systems
{
    /// <summary>Ash exposure / wet rag / gas mask / water bottle setup (idempotent). Scene edits are marked dirty, never saved.</summary>
    public static class ASHExposureInstaller
    {
        const string SettingsPath = ASHMenuInstaller.ResDir + "/ASH_ExposureSettings.asset";
        const string AudioDir = "Assets/_ASH/Audio/Exposure";

        [MenuItem("ASH/Systems/Exposure/Install Exposure (settings, items, sounds)", false, 160)]
        public static void Install()
        {
            ASHFieldManualInstaller.Install(); // items (rags, bottle, mask, filter) + recipes
            var s = ASHMenuInstaller.Ensure<ASHExposureSettings>(SettingsPath);
            if (s.coughClips == null || s.coughClips.Length == 0 || s.coughClips.All(c => !c))
                s.coughClips = AssetDatabase.FindAssets("t:AudioClip ASH_Cough", new[] { AudioDir }).Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c).ToArray();
            if (!s.breathingLoop) s.breathingLoop = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/ASH_Breathing_Loop.ogg");
            EditorUtility.SetDirty(s); AssetDatabase.SaveAssets();
            Selection.activeObject = s;
            Debug.Log($"[ASH] Exposure installed: {SettingsPath} (coughs {s.coughClips.Length}, breathing {(s.breathingLoop ? s.breathingLoop.name : "none")}). Next: Add Shelter Zone to Grandpa's Cabin, place the gas mask / filters.");
        }

        [MenuItem("ASH/Systems/Exposure/Add Shelter Zone to Grandpa's Cabin", false, 161)]
        public static void CabinZone()
        {
            var wo = Object.FindAnyObjectByType<ASH.World.Weather.ASHWeatherOutputs>();
            if (!wo || !wo.interior) { Debug.LogWarning("[ASH] No cabin interior (ASHWeatherOutputs.interior) in the open scene."); return; }
            var z = wo.interior.GetComponent<ASHShelterZone>();
            if (!z) z = Undo.AddComponent<ASHShelterZone>(wo.interior.gameObject);
            z.volume = wo.interior; z.sealing = 1f; z.label = "Grandpa's cabin";
            EditorUtility.SetDirty(z); EditorSceneManager.MarkSceneDirty(z.gameObject.scene); Selection.activeObject = z.gameObject;
            Debug.Log($"[ASH] Shelter zone (ash-tight) on {z.gameObject.name}. Scene not saved.");
        }

        static void ZoneOnSelection(float sealing, string label)
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
            {
                var col = go.GetComponent<Collider>();
                if (!col || (col is MeshCollider mc && !mc.convex))
                {
                    var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { Debug.LogWarning($"[ASH] {go.name}: no collider or renderers to size a zone."); continue; }
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    var holder = new GameObject("ASH_ShelterZone"); Undo.RegisterCreatedObjectUndo(holder, "Shelter zone");
                    holder.transform.SetParent(go.transform, false); holder.transform.position = b.center; holder.transform.rotation = Quaternion.identity;
                    holder.layer = 2; // Ignore Raycast: never blocks interaction rays
                    var bc = holder.AddComponent<BoxCollider>(); bc.isTrigger = true;
                    var ls = holder.transform.lossyScale; bc.size = new Vector3(b.size.x / Mathf.Max(1e-4f, ls.x), b.size.y / Mathf.Max(1e-4f, ls.y), b.size.z / Mathf.Max(1e-4f, ls.z)) * 0.92f;
                    var zz = holder.AddComponent<ASHShelterZone>(); zz.volume = bc; zz.sealing = sealing; zz.label = label; n++;
                    EditorSceneManager.MarkSceneDirty(holder.scene);
                    continue;
                }
                var z = go.GetComponent<ASHShelterZone>(); if (!z) z = Undo.AddComponent<ASHShelterZone>(go);
                z.volume = col; z.sealing = sealing; if (string.IsNullOrEmpty(z.label) || z.label == "Shelter") z.label = label; n++;
                EditorUtility.SetDirty(z); EditorSceneManager.MarkSceneDirty(go.scene);
            }
            Debug.Log($"[ASH] Shelter zone (sealing {sealing:0.##}) on {n} object(s). Scene not saved.");
        }

        [MenuItem("ASH/Systems/Exposure/Add Shelter Zone to Selection (ash-tight building)", false, 162)]
        public static void ZoneTight() => ZoneOnSelection(1f, "Shelter");
        [MenuItem("ASH/Systems/Exposure/Add Shelter Zone to Selection (leaky lean-to 0.5)", false, 163)]
        public static void ZoneLeaky() => ZoneOnSelection(0.5f, "Lean-to");

        static bool ScenePoint(out Vector3 p, out float yaw)
        {
            p = default; yaw = 0; var sv = SceneView.lastActiveSceneView; if (!sv) { Debug.LogWarning("[ASH] Open a Scene view and look at the spot first."); return false; }
            var cam = sv.camera.transform; yaw = cam.eulerAngles.y;
            p = Physics.Raycast(cam.position, cam.forward, out var hit, 500f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : cam.position + cam.forward * 4f;
            return true;
        }

        [MenuItem("ASH/Systems/Exposure/Place Gas Mask at Scene View", false, 180)]
        public static void PlaceMask()
        {
            var it = ASHItemRegistry.I.Find(ASHFaceProtection.MaskId) ?? AssetDatabase.LoadAssetAtPath<ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_GasMask.asset");
            if (!it) { Debug.LogWarning("[ASH] Run Install Exposure first."); return; }
            if (!ScenePoint(out var p, out var y)) return;
            var go = ASHFieldManualInstaller.PlacePickup(it, "ASH_Pickup_GasMask", p, y, PrimitiveType.Sphere, new Vector3(0.2f, 0.16f, 0.18f));
            Selection.activeObject = go; Debug.Log($"[ASH] Gas mask placed at {p:F1} (scene not saved).");
        }

        [MenuItem("ASH/Systems/Exposure/Place Filter Cartridge at Scene View (random condition)", false, 181)]
        public static void PlaceFilter()
        {
            var it = ASHItemRegistry.I.Find(ASHFaceProtection.FilterId) ?? AssetDatabase.LoadAssetAtPath<ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_FilterCartridge.asset");
            if (!it) { Debug.LogWarning("[ASH] Run Install Exposure first."); return; }
            if (!ScenePoint(out var p, out var y)) return;
            int k = 1; while (GameObject.Find($"ASH_Pickup_FilterCartridge_{k:00}")) k++;
            var go = ASHFieldManualInstaller.PlacePickup(it, $"ASH_Pickup_FilterCartridge_{k:00}", p, y, PrimitiveType.Cylinder, new Vector3(0.09f, 0.03f, 0.09f), roll: true);
            Selection.activeObject = go; Debug.Log($"[ASH] Filter cartridge {k} placed at {p:F1}; it rolls a random condition each new game (Exposure Settings > found filter condition). Scene not saved.");
        }
    }
}
