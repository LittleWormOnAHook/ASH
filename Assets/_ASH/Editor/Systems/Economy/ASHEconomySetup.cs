#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using ASH.EditorTools.Systems;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Economy + vendor + legendary finds (idempotent, saves only its own assets, never the scene):
    /// - Setup Economy And Vendor: Resources/ASH_EconomySettings; converts item values from old AC to copper once (x50, legendaries x100);
    ///   default vendor stock when empty.
    /// - Place Vendor Near Grandpas Cabin: one ASH_Vendor (capsule placeholder + ASHVendor) outside "Grandpa Cabin", on clear flat ground.
    /// - Place Legendary Finds: ASH_LegendaryFinds with the Expedition Parka (near the supply crate farthest from the cabin) and the
    ///   Bearskin Hood (on the Lake Tinkiller shore), using the ASH_Pickup_GearBox prefab.
    /// Scene menus: Undo, scene marked dirty, not saved; they rebuild only their own objects.
    /// </summary>
    public static class ASHEconomySetup
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_EconomySettings.asset";
        const string GearBoxPath = "Assets/_ASH/Prefabs/Pickups/ASH_Pickup_GearBox.prefab";
        const string MatDir = "Assets/_ASH/Materials/Pickups";
        static readonly string[] CabinNames = { "Grandpa Cabin", "Grandpas Cabin", "Grandpa's Cabin", "Granpas Cabin" };

        static ASHItemDefinition I(string id) => ASHItemRegistry.I ? ASHItemRegistry.I.Find(id) : null;

        [MenuItem("ASH/Systems/Economy/Setup Economy And Vendor (idempotent)", false, 240)]
        public static void Setup()
        {
            var S = ASHMenuInstaller.Ensure<ASHEconomySettings>(SettingsPath);
            var touched = new List<Object> { S };
            int converted = 0;
            var items = AssetDatabase.FindAssets("t:ASHItemDefinition", new[] { "Assets/_ASH" }).Select(g => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(i => i).ToList();
            if (!S.valuesInCopper)
            {
                foreach (var it in items)
                {
                    if (it.sellValue <= 0) continue;
                    it.sellValue = ASHClothingSetup.Copper(it.sellValue, it.rarity);
                    EditorUtility.SetDirty(it); touched.Add(it); converted++;
                }
                S.valuesInCopper = true;
            }
            if (S.stock == null || S.stock.Count == 0) S.stock = DefaultStock();
            EditorUtility.SetDirty(S);
            foreach (var o in touched) AssetDatabase.SaveAssetIfDirty(o);
            Debug.Log($"[ASH] Economy setup: {converted} item values converted AC -> copper (x{ASHEconomy.LegacyCopperPerAC}, legendary x100), {S.stock.Count} stock entries, starting purse {ASHEconomy.Format(S.startingPurse, false)}. Saved {touched.Count} own assets (scene untouched).");
        }

        static List<ASHEconomySettings.StockEntry> DefaultStock()
        {
            var l = new List<ASHEconomySettings.StockEntry>();
            void A(string id, int q, string skill = "", int lv = 0) { var it = I(id); if (it) l.Add(new ASHEconomySettings.StockEntry { item = it, quantity = q, skill = skill, minLevel = lv }); }
            A("bottledwater", 6); A("cannedbeans", 5); A("cannedpeaches", 4); A("energybar", 6); A("jerky", 4);
            A("bandage", 6); A("antisepticbandage", 3); A("painkillers", 3); A("firstaidkit", 1);
            A("filter_cartridge", 3); A("rubbingalcohol", 3); A("battery", 4);
            A("rope", 4); A("cloth", 8); A("sinewthread", 6); A("boneneedle", 2); A("waterproofing", 3); A("canopener", 2); A("kindling", 10);
            A("woodenarrow", 20); A("pistolrounds", 24, ASHSkills.Hunting, 1); A("riflerounds", 20, ASHSkills.Hunting, 2); A("shotgunshells", 16, ASHSkills.Hunting, 2);
            A("watchcap", 2); A("workgloves", 2); A("workpants", 2); A("workboots", 1); A("woolscarf", 1); A("canvasworkcoat", 1);
            A("canvasrucksack", 1, ASHSkills.Crafting, 2); A("scoutpack", 1, ASHSkills.Crafting, 4);
            return l;
        }

        // ------------------------------------------------------------------ vendor
        static GameObject FindNamed(params string[] names)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    foreach (var n in names) if (t.name.Trim() == n) return t.gameObject;
            return null;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 4f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        /// <summary>Clear, fairly flat ground at p (nothing but terrain / ground within radius up to height). Returns the ground point.</summary>
        static bool Clear(Vector3 p, float radius, float height, out Vector3 ground)
        {
            ground = p;
            if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (Vector3.Angle(hit.normal, Vector3.up) > 18f) return false;
            ground = hit.point;
            var cols = Physics.OverlapCapsule(hit.point + Vector3.up * (radius + 0.15f), hit.point + Vector3.up * height, radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in cols) if (!(c is TerrainCollider) && c != hit.collider) return false;
            return true;
        }

        [MenuItem("ASH/Systems/Economy/Place Vendor Near Grandpas Cabin", false, 241)]
        public static void PlaceVendor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }
            var cabin = FindNamed(CabinNames);
            if (!cabin) { Debug.LogWarning("[ASH] Vendor: no 'Grandpa Cabin' in the open scene."); return; }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Place Vendor");
            var old = FindNamed("ASH_Vendor"); if (old) Undo.DestroyObjectImmediate(old);
            Physics.SyncTransforms();
            var b = BoundsOf(cabin); var c = b.center; float r = Mathf.Max(b.extents.x, b.extents.z);
            var fwd = cabin.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
            Vector3 pos = Vector3.zero; bool found = false;
            // rings outside the cabin's footprint, starting in front of it and sweeping round both sides
            for (float extra = 3f; extra <= 14f && !found; extra += 1.5f)
                for (int k = 0; k <= 12 && !found; k++)
                {
                    float ang = (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 28f;
                    var dir = Quaternion.Euler(0f, 35f + ang, 0f) * fwd;
                    var p = c + dir * (r + extra);
                    if (Clear(p, 0.6f, 2.2f, out var g)) { pos = g; found = true; }
                }
            if (!found) { Debug.LogWarning("[ASH] Vendor: no clear flat spot within 14 m of Grandpa Cabin (nothing placed)."); return; }

            var root = new GameObject("ASH_Vendor");
            Undo.RegisterCreatedObjectUndo(root, "Vendor");
            root.transform.position = pos;
            var look = c - pos; look.y = 0f; if (look.sqrMagnitude > 0.01f) root.transform.rotation = Quaternion.LookRotation(-look.normalized); // back to the cabin
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.name = "Placeholder";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false); body.transform.localPosition = Vector3.up * 0.9f; body.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            body.GetComponent<Renderer>().sharedMaterial = Mat("ASH_Vendor_Placeholder", new Color(0.36f, 0.27f, 0.2f));
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube); head.name = "Hat";
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(root.transform, false); head.transform.localPosition = new Vector3(0f, 1.86f, 0f); head.transform.localScale = new Vector3(0.5f, 0.08f, 0.5f);
            head.GetComponent<Renderer>().sharedMaterial = Mat("ASH_Vendor_Hat", new Color(0.2f, 0.17f, 0.13f));
            var col = root.AddComponent<CapsuleCollider>(); col.center = Vector3.up * 0.9f; col.height = 1.8f; col.radius = 0.35f;
            root.AddComponent<ASHVendor>();
            EditorSceneManager.MarkSceneDirty(cabin.scene);
            Undo.CollapseUndoOperations(group);
            Debug.Log($"[ASH] Vendor placed at {pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0} ({Vector3.Distance(new Vector3(pos.x, c.y, pos.z), c):0.0} m from the Grandpa Cabin centre, outside it). Scene NOT saved.");
        }

        static Material Mat(string name, Color c)
        {
            ASHMenuInstaller.Folder(MatDir);
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); m.color = c;
            AssetDatabase.CreateAsset(m, path); AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ hand-placed gear box + legendary finds
        public static GameObject EnsureGearBox()
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(GearBoxPath); if (pf) return pf;
            ASHMenuInstaller.Folder("Assets/_ASH/Prefabs/Pickups");
            var root = new GameObject("ASH_Pickup_GearBox");
            try
            {
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cube); vis.name = "Box";
                Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.transform.SetParent(root.transform, false); vis.transform.localScale = Vector3.one * 0.3f; vis.transform.localPosition = Vector3.up * 0.15f;
                vis.GetComponent<Renderer>().sharedMaterial = Mat("ASH_GearBox_Item", new Color(0.55f, 0.52f, 0.47f));
                var band = GameObject.CreatePrimitive(PrimitiveType.Cube); band.name = "Strap";
                Object.DestroyImmediate(band.GetComponent<Collider>());
                band.transform.SetParent(root.transform, false); band.transform.localScale = new Vector3(0.32f, 0.31f, 0.05f); band.transform.localPosition = Vector3.up * 0.15f;
                band.GetComponent<Renderer>().sharedMaterial = Mat("ASH_GearBox_Strap", new Color(0.18f, 0.15f, 0.12f));
                var bc = root.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(0.45f, 0.36f, 0.45f); bc.center = Vector3.up * 0.18f;
                root.AddComponent<ASHItemPickup>();
                root.AddComponent<ASHGearBoxTint>();
                int layer = LayerMask.NameToLayer("Resource");
                if (layer >= 0) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                pf = PrefabUtility.SaveAsPrefabAsset(root, GearBoxPath);
            }
            finally { Object.DestroyImmediate(root); }
            return pf;
        }

        [MenuItem("ASH/Systems/Clothing/Place Legendary Finds (Expedition Parka, Bearskin Hood)", false, 231)]
        public static void PlaceLegendaries()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }
            var parka = I("expeditionparka"); var hood = I("bearskinhood");
            if (!parka || !hood) { Debug.LogWarning("[ASH] Legendary finds: run ASH/Systems/Clothing/Setup Clothing first."); return; }
            var cabin = FindNamed(CabinNames); var lake = FindNamed("Lake Tinkiller");
            var pf = EnsureGearBox();
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Place Legendary Finds");
            var root = FindNamed("ASH_LegendaryFinds");
            if (root) for (int i = root.transform.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
            else { root = new GameObject("ASH_LegendaryFinds"); Undo.RegisterCreatedObjectUndo(root, "Legendary finds"); }
            Physics.SyncTransforms();
            var home = cabin ? BoundsOf(cabin).center : Vector3.zero;
            var log = new System.Text.StringBuilder();

            // Expedition Parka: by the supply crate farthest from the cabin (a reward for exploring)
            Vector3? parkaPos = null; string parkaWhere = "";
            var crates = Object.FindObjectsByType<ASHSupplyCrate>(FindObjectsInactive.Exclude).OrderByDescending(cr => (cr.transform.position - home).sqrMagnitude).ToList();
            foreach (var cr in crates)
            {
                for (int k = 0; k < 12 && parkaPos == null; k++)
                {
                    var p = cr.transform.position + Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward * 1.6f;
                    if (Clear(p, 0.3f, 0.6f, out var g)) { parkaPos = g; parkaWhere = $"next to supply crate '{cr.name}'"; }
                }
                if (parkaPos != null) break;
            }
            // Bearskin Hood: on the Lake Tinkiller shore, the side facing the cabin first
            Vector3? hoodPos = null; string hoodWhere = "";
            if (lake)
            {
                var lb = BoundsOf(lake); float lr = Mathf.Max(lb.extents.x, lb.extents.z); float water = lb.max.y;
                var toHome = home - lb.center; toHome.y = 0f; if (toHome.sqrMagnitude < 0.01f) toHome = Vector3.forward; toHome.Normalize();
                for (float out_ = 2f; out_ <= 20f && hoodPos == null; out_ += 2f)
                    for (int k = 0; k < 16 && hoodPos == null; k++)
                    {
                        var dir = Quaternion.Euler(0f, (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 22.5f, 0f) * toHome;
                        var p = lb.center + dir * (lr + out_); p.y = lb.center.y;
                        if (Clear(p, 0.3f, 0.6f, out var g) && g.y > water + 0.1f) { hoodPos = g; hoodWhere = "on the Lake Tinkiller shore"; }
                    }
            }
            int placed = 0;
            if (parkaPos != null) { Spawn(pf, parka, parkaPos.Value, root.transform); placed++; log.Append($" Expedition Parka at {Fmt(parkaPos.Value)} {parkaWhere};"); }
            else log.Append(" Expedition Parka NOT placed (no supply crate with clear ground);");
            if (hoodPos != null) { Spawn(pf, hood, hoodPos.Value, root.transform); placed++; log.Append($" Bearskin Hood at {Fmt(hoodPos.Value)} {hoodWhere};"); }
            else log.Append(lake ? " Bearskin Hood NOT placed (no clear shore spot);" : " Bearskin Hood NOT placed (no 'Lake Tinkiller' object);");
            EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(group);
            Debug.Log($"[ASH] Legendary finds: {placed} placed in ASH_LegendaryFinds.{log} Scene NOT saved.");
        }

        static string Fmt(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";

        static void Spawn(GameObject pf, ASHItemDefinition it, Vector3 pos, Transform parent)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            go.name = "Find_" + it.itemId;
            go.transform.position = pos + Vector3.up * 0.01f; go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var pk = go.GetComponent<ASHItemPickup>(); var so = new SerializedObject(pk);
            so.FindProperty("item").objectReferenceValue = it; so.FindProperty("amount").intValue = 1;
            so.FindProperty("condition").floatValue = 1f; so.FindProperty("rollCondition").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.GetComponent<ASHGearBoxTint>()?.Apply();
            Undo.RegisterCreatedObjectUndo(go, "Legendary find");
        }
    }
}
#endif
