using ASH.Gameplay;
using ASH.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Testing: places 2 Raw Meat + 2 Raw Fish pickups (x10 each, instance override) around the campfire outside the
    /// cabin, snapped to the ground, under "ASH_TestPickups". Idempotent (replaces its own previous pickups). Leaves the scene
    /// unsaved; save it yourself to keep them.</summary>
    public static class ASHTestMeatPickups
    {
        const string Root = "ASH_TestPickups";
        const string MeatPath = "Assets/_ASH/Prefabs/Pickups/ASH_Pickup_RawMeat.prefab";
        const string FishPath = "Assets/_ASH/Prefabs/Pickups/ASH_Pickup_RawFish.prefab";
        const int Amount = 10;

        [MenuItem("ASH/Systems/Testing/Place Meat Pickups at Cabin Campfire")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Test pickups: stop Play mode first."); return; }
            var meat = AssetDatabase.LoadAssetAtPath<GameObject>(MeatPath);
            var fish = AssetDatabase.LoadAssetAtPath<GameObject>(FishPath);
            if (!meat || !fish) { Debug.LogWarning("[ASH] Test pickups: pickup prefabs not found."); return; }

            var fire = FindCabinFire(out string how);
            if (!fire) { Debug.LogWarning("[ASH] Test pickups: no campfire found in the open scene."); return; }
            var scene = fire.gameObject.scene;

            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects()) if (go.name == Root) { root = go; break; }
            if (!root) { root = new GameObject(Root); SceneManager_Move(root, scene); Undo.RegisterCreatedObjectUndo(root, "ASH test pickups"); }
            for (int i = root.transform.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);

            var c = fire.transform.position;
            var prefabs = new[] { meat, fish, meat, fish };
            for (int i = 0; i < prefabs.Length; i++)
            {
                float a = (45f + 90f * i) * Mathf.Deg2Rad;
                var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.6f;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
                Undo.RegisterCreatedObjectUndo(go, "ASH test pickups");
                go.transform.SetParent(root.transform, true);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                Snap(go, fire.transform);
                var pk = go.GetComponentInChildren<ASHItemPickup>();
                if (pk)
                {
                    var so = new SerializedObject(pk);
                    var amt = so.FindProperty("amount"); if (amt != null) { amt.intValue = Amount; so.ApplyModifiedProperties(); }
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[ASH] Test pickups: 2 Raw Meat + 2 Raw Fish (x{Amount} each) placed around '{fire.name}' at {c} ({how}). Scene NOT saved - save it to keep them.");
        }

        static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene s) { if (go.scene != s) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, s); }

        /// <summary>Scene campfire closest to the cabin door that is not inside the cabin interior.</summary>
        public static ASHCampfire FindCabinFire(out string how)
        {
            how = "";
            var door = GameObject.Find("ASH_CabinDoor");
            var interiorGo = GameObject.Find("ASH_CabinInterior");
            Bounds? interior = null;
            if (interiorGo) { var col = interiorGo.GetComponentInChildren<Collider>(); if (col) interior = col.bounds; }
            Vector3 refPos = door ? door.transform.position : interiorGo ? interiorGo.transform.position : Vector3.zero;
            ASHCampfire best = null; float bd = float.PositiveInfinity;
            foreach (var f in Object.FindObjectsByType<ASHCampfire>(FindObjectsInactive.Include))
            {
                if (!f || !f.gameObject.scene.IsValid() || f.GetComponent<ASHPlacedObject>() || f.name == "ASH_PlacementGhost") continue;
                if (interior.HasValue && interior.Value.Contains(f.transform.position)) continue;
                float d = (f.transform.position - refPos).sqrMagnitude;
                if (d < bd) { bd = d; best = f; }
            }
            if (best) how = door ? $"nearest outside fire to the cabin door, {Mathf.Sqrt(bd):0.0} m" : "nearest fire";
            return best;
        }

        public static void Snap(GameObject go, Transform fire)
        {
            var p = go.transform.position;
            float ground = p.y; bool found = false;
            var hits = Physics.RaycastAll(p + Vector3.up * 5f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            float bestY = 0f, bestD = float.PositiveInfinity; // surface nearest the fire's height (not a branch or roof above)
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(go.transform) || h.collider.transform.IsChildOf(fire)) continue;
                float d = Mathf.Abs(h.point.y - fire.position.y);
                if (d < bestD) { bestD = d; bestY = h.point.y; found = true; }
            }
            if (found) ground = bestY;
            else { var t = Terrain.activeTerrain; if (t) ground = t.SampleHeight(p) + t.transform.position.y; }
            go.transform.position = new Vector3(p.x, ground, p.z);
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                go.transform.position += Vector3.up * (ground + 0.02f - b.min.y);
            }
        }
    }
}
