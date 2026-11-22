#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// ASH/Systems/Testing/Place Test Items In Grandpas Project: one pickup of every clothing piece, then every other item, in a tight even
    /// grid on the "Platform" floor pieces of the "Granpas Project" cabin. Idempotent: rebuilds only its own "ASH_TestItems" children.
    /// Undoable, marks the scene dirty, never saves it. Key / journal items are skipped. Items that don't fit are reported (no stacking,
    /// nothing outside the floor). Delete ASH_TestItems to remove everything.
    /// </summary>
    public static class ASHTestItemsPlacer
    {
        const string ParentName = "ASH_TestItems";
        static readonly string[] CabinNames = { "Granpas Project", "Grandpas Project", "Grandpa's Project", "Granpa's Project" };
        const string MatDir = "Assets/_ASH/Materials/Pickups";
        /// <summary>Grid cell (metres) - items are as tight as their size allows; long items span several cells along the row.</summary>
        public static float Cell = 0.34f;
        public static float EdgeMargin = 0.22f;
        public static bool Labels = false;

        [MenuItem("ASH/Systems/Testing/Place Test Items In Grandpas Project", false, 300)]
        public static void Place()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }
            var cabin = FindCabin();
            if (!cabin) { Debug.LogWarning("[ASH] Test items: no 'Granpas Project' object in the open scene."); return; }
            var floors = cabin.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Platform") && t.gameObject.activeInHierarchy).ToList();
            if (floors.Count == 0) { Debug.LogWarning("[ASH] Test items: no 'Platform' floor pieces under " + cabin.name); return; }

            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Place Test Items");
            var parentT = cabin.transform.Find(ParentName);
            GameObject parent;
            if (parentT) { parent = parentT.gameObject; for (int i = parentT.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(parentT.GetChild(i).gameObject); }
            else { parent = new GameObject(ParentName); Undo.RegisterCreatedObjectUndo(parent, "Test items"); Undo.SetTransformParent(parent.transform, cabin.transform, "Test items"); parent.transform.localPosition = Vector3.zero; parent.transform.localRotation = Quaternion.identity; parent.transform.localScale = Vector3.one; }
            parent.tag = "Untagged";
            Physics.SyncTransforms();

            // floor frame: grid axes follow the first Platform's yaw
            var rot = Quaternion.Euler(0f, floors[0].eulerAngles.y, 0f); var inv = Quaternion.Inverse(rot);
            var floorCols = new HashSet<Collider>(); var floorBoxes = new List<(Vector2 min, Vector2 max, float top, Transform t)>();
            foreach (var f in floors)
            {
                Bounds? wb = null;
                foreach (var c in f.GetComponentsInChildren<Collider>(true)) { if (c.isTrigger || !c.enabled) continue; floorCols.Add(c); wb = wb.HasValue ? Enc(wb.Value, c.bounds) : c.bounds; }
                if (!wb.HasValue) foreach (var r in f.GetComponentsInChildren<Renderer>(true)) wb = wb.HasValue ? Enc(wb.Value, r.bounds) : r.bounds;
                if (!wb.HasValue) continue;
                var b = wb.Value; Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
                foreach (var corner in Corners(b)) { var l = inv * corner; mn = Vector2.Min(mn, new Vector2(l.x, l.z)); mx = Vector2.Max(mx, new Vector2(l.x, l.z)); }
                floorBoxes.Add((mn, mx, b.max.y, f));
            }
            if (floorBoxes.Count == 0) { Debug.LogWarning("[ASH] Test items: Platform pieces have no colliders or renderers."); return; }
            Vector2 gMin = new Vector2(floorBoxes.Min(x => x.min.x), floorBoxes.Min(x => x.min.y)) + Vector2.one * EdgeMargin;
            Vector2 gMax = new Vector2(floorBoxes.Max(x => x.max.x), floorBoxes.Max(x => x.max.y)) - Vector2.one * EdgeMargin;
            int cols = Mathf.Max(0, Mathf.FloorToInt((gMax.x - gMin.x) / Cell)), rows = Mathf.Max(0, Mathf.FloorToInt((gMax.y - gMin.y) / Cell));

            // usable cells: on a Platform (inset), floor hit, nothing standing there (walls / props)
            var ok = new bool[cols, rows]; var floorY = new float[cols, rows]; int usable = 0;
            for (int cx = 0; cx < cols; cx++) for (int cz = 0; cz < rows; cz++)
            {
                var l2 = new Vector2(gMin.x + (cx + 0.5f) * Cell, gMin.y + (cz + 0.5f) * Cell);
                float top = float.MinValue;
                foreach (var fb in floorBoxes) if (l2.x >= fb.min.x + 0.05f && l2.x <= fb.max.x - 0.05f && l2.y >= fb.min.y + 0.05f && l2.y <= fb.max.y - 0.05f) top = Mathf.Max(top, fb.top);
                if (top == float.MinValue) continue;
                var w = rot * new Vector3(l2.x, 0f, l2.y); w.y = top;
                float y = top; bool hitFloor = false;
                foreach (var h in Physics.RaycastAll(w + Vector3.up * 0.6f, Vector3.down, 1.6f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                {
                    if (floorCols.Contains(h.collider)) { y = h.point.y; hitFloor = true; break; }
                    break; // something on top of the floor here (prop, wall)
                }
                if (!hitFloor && floorCols.Count > 0) continue;
                var center = new Vector3(w.x, y + 0.25f, w.z);
                var blockers = Physics.OverlapBox(center, new Vector3(Cell * 0.45f, 0.22f, Cell * 0.45f), rot, ~0, QueryTriggerInteraction.Ignore);
                if (blockers.Any(c => !floorCols.Contains(c) && !c.transform.IsChildOf(parent.transform))) continue;
                ok[cx, cz] = true; floorY[cx, cz] = y; usable++;
            }

            // items: clothing by slot, then the rest by category; key / journal items skipped
            var all = AssetDatabase.FindAssets("t:ASHItemDefinition", new[] { "Assets/_ASH" }).Select(g => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(i => i && !string.IsNullOrEmpty(i.itemId)).GroupBy(i => i.itemId).Select(g => g.First()).ToList();
            var skipped = all.Where(i => ASHInventory.IsKeyItem(i)).Select(i => i.displayName).ToList();
            var items = all.Where(i => !ASHInventory.IsKeyItem(i))
                .OrderBy(i => i.IsWearable ? 0 : 1).ThenBy(i => i.IsWearable ? (int)i.wearSlot : (int)i.category).ThenBy(i => i.IsWearable ? -(int)i.rarity : 0).ThenBy(i => i.displayName).ToList();

            int placed = 0, cursorX = 0, cursorZ = 0; var notFit = new List<string>(); int cellsNeeded = 0;
            foreach (var it in items)
            {
                var go = Spawn(it, parent.transform);
                // footprint (cells) with the long side along the row
                go.transform.rotation = rot;
                var ls = LocalSize(go, inv);
                float lenX = Mathf.Max(ls.x, ls.y), lenZ = Mathf.Min(ls.x, ls.y);
                if (ls.y > ls.x) go.transform.rotation = rot * Quaternion.Euler(0f, 90f, 0f);
                int span = Mathf.Max(1, Mathf.CeilToInt((lenX + 0.04f) / Cell)), depth = Mathf.Max(1, Mathf.CeilToInt((lenZ + 0.04f) / Cell));
                cellsNeeded += span * depth;
                if (!FindSpot(ok, cols, rows, span, depth, ref cursorX, ref cursorZ, out int sx, out int sz))
                {
                    notFit.Add(it.displayName); Undo.DestroyObjectImmediate(go); continue; // registered below only when placed
                }
                for (int x = sx; x < sx + span; x++) for (int z = sz; z < sz + depth; z++) ok[x, z] = false;
                float y = float.MinValue; for (int x = sx; x < sx + span; x++) for (int z = sz; z < sz + depth; z++) y = Mathf.Max(y, floorY[x, z]);
                var l = new Vector2(gMin.x + (sx + span * 0.5f) * Cell, gMin.y + (sz + depth * 0.5f) * Cell);
                var p = rot * new Vector3(l.x, 0f, l.y); p.y = y;
                go.transform.position = p;
                var nb = WorldBounds(go); go.transform.position += Vector3.up * (y - nb.min.y + 0.005f);
                placed++;
            }

            EditorSceneManager.MarkSceneDirty(cabin.scene);
            Undo.CollapseUndoOperations(group);
            string more = "";
            if (notFit.Count > 0)
            {
                float extra = Mathf.Max(Cell * Cell, (cellsNeeded - usable) * Cell * Cell) * 1.15f; // +15% for packing gaps
                float w = Mathf.Max(Cell, cols * Cell);
                more = $" NOT placed (no room) {notFit.Count}: {string.Join(", ", notFit)}. Need about {extra:0.0} m2 more free floor (e.g. {w:0.0} m wide x {extra / w:0.0} m longer).";
            }
            Debug.Log($"[ASH] Test items: placed {placed} of {items.Count} in {cabin.name}/{ParentName} on {floors.Count} Platform pieces ({usable} free {Cell:0.00} m cells, grid {cols}x{rows}). Skipped key items: {string.Join(", ", skipped)}.{more} Scene NOT saved - save it to keep them.");
        }

        /// <summary>Renderer size along the grid axes (x = row, y = depth).</summary>
        static Vector2 LocalSize(GameObject go, Quaternion inv)
        {
            var b = WorldBounds(go); Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in Corners(b)) { var l = inv * c; mn = Vector2.Min(mn, new Vector2(l.x, l.z)); mx = Vector2.Max(mx, new Vector2(l.x, l.z)); }
            return mx - mn;
        }


        /// <summary>Row-major scan from the cursor for a free span x depth block.</summary>
        static bool FindSpot(bool[,] ok, int cols, int rows, int span, int depth, ref int cx, ref int cz, out int sx, out int sz)
        {
            for (int z = 0; z + depth <= rows; z++)
                for (int x = 0; x + span <= cols; x++)
                {
                    bool free = true;
                    for (int a = x; a < x + span && free; a++) for (int b = z; b < z + depth && free; b++) free = ok[a, b];
                    if (free) { sx = x; sz = z; cx = x + span; cz = z; return true; }
                }
            sx = sz = -1; return false;
        }

        static GameObject Spawn(ASHItemDefinition it, Transform parent)
        {
            GameObject go;
            if (it.worldPickupPrefab)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(it.worldPickupPrefab, parent);
            }
            else
            {
                go = new GameObject();
                go.transform.SetParent(parent, false);
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.name = "Visual"; vis.transform.SetParent(go.transform, false);
                float e = it.IsWearable ? ASHClothingSettings.I.pickupBoxSize : 0.2f;
                vis.transform.localScale = it.IsWearable ? new Vector3(e, e, e) : new Vector3(0.22f, 0.12f, 0.22f);
                vis.transform.localPosition = Vector3.up * vis.transform.localScale.y * 0.5f;
                vis.GetComponent<Renderer>().sharedMaterial = BoxMat(it);
                var col = go.AddComponent<BoxCollider>(); col.isTrigger = true;
                col.size = new Vector3(Mathf.Max(0.3f, vis.transform.localScale.x + 0.1f), 0.3f, Mathf.Max(0.3f, vis.transform.localScale.z + 0.1f)); col.center = Vector3.up * 0.15f;
                int layer = LayerMask.NameToLayer("Resource");
                if (layer >= 0) foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            }
            go.name = "Test_" + it.itemId;
            var pk = go.GetComponent<ASHItemPickup>(); if (!pk) pk = go.AddComponent<ASHItemPickup>();
            var so = new SerializedObject(pk);
            so.FindProperty("item").objectReferenceValue = it;
            so.FindProperty("amount").intValue = 1;
            so.FindProperty("amountMin").intValue = 0; so.FindProperty("amountMax").intValue = 0;
            so.FindProperty("condition").floatValue = 1f;
            so.FindProperty("rollCondition").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            Undo.RegisterCreatedObjectUndo(go, "Test item");
            return go;
        }

        static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        /// <summary>Shared box materials (one per rarity + a neutral one) under Materials/Pickups, created once.</summary>
        static Material BoxMat(ASHItemDefinition it)
        {
            string key = it.IsWearable ? it.rarity.ToString() : "Item";
            if (_mats.TryGetValue(key, out var m) && m) return m;
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/_ASH/Materials", "Pickups");
            string path = MatDir + "/ASH_GearBox_" + key + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(sh);
                var c = it.IsWearable ? ASHClothingSettings.I.RarityColor(it.rarity) : new Color(0.55f, 0.52f, 0.47f);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); m.color = c;
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
                AssetDatabase.CreateAsset(m, path);
                AssetDatabase.SaveAssetIfDirty(m);
            }
            _mats[key] = m; return m;
        }

        static GameObject FindCabin()
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    foreach (var n in CabinNames) if (t.name.Trim() == n) return t.gameObject;
            return null;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.2f);
            var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds); return b;
        }

        static Bounds Enc(Bounds a, Bounds b) { a.Encapsulate(b); return a; }
        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++) yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        }
    }
}
#endif
