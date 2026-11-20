using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.EditorTools.Build;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Vehicle items (t362/t363): Fuel Can (20 L jerry can, vendor petrol-can model), Car Battery and Fan Belt (the repair parts
    /// ASHVehicleCondition needs), their pickup prefabs and icons, and an idempotent menu that puts them on Grandpa's cabin porch,
    /// snapped onto the floor. Does not save the scene.</summary>
    public static class ASHVehicleItems
    {
        const string ItemDir = ASHContentBuilder.ItemDir;
        const string TruckDir = ItemDir + "/Optional_TruckParts";
        const string PickupDir = ASHContentBuilder.Root + "/Prefabs/Pickups";
        const string Mat = ASHContentBuilder.MatDir + "/Pickups";
        const string CanModel = "Assets/HPA/Prefabs/Objects/Products/Motor_Oil/LOD_Petrol_Can_01.prefab";
        public const string GroupName = "ASH_VehicleItems_Cabin";
        static StringBuilder _log;

        public static ASHItemDefinition FuelCan => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{ItemDir}/ASH_Item_FuelCan.asset");
        public static ASHItemDefinition Tire => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{ItemDir}/ASH_Item_Tire.asset");
        public static ASHItemDefinition Battery => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{TruckDir}/ASH_Item_CarBattery.asset");
        public static ASHItemDefinition Belt => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{TruckDir}/ASH_Item_FanBelt.asset");

        /// <summary>Called by the item catalog so a catalog rebuild keeps these definitions and models.</summary>
        public static bool IsVehicleItem(string file) => file == "FuelCan" || file == "CarBattery" || file == "FanBelt";

        [MenuItem("ASH/Systems/Vehicles/Build Vehicle Items")]
        public static void BuildItems()
        {
            _log = new StringBuilder("[ASH] VEHI Build Vehicle Items");
            ASHBuildUtil.Asset<ASHItemDefinition>($"{ItemDir}/ASH_Item_FuelCan.asset");
            ASHBuildUtil.Asset<ASHItemDefinition>($"{ItemDir}/ASH_Item_Tire.asset");
            ConfigureAll();
            AssetDatabase.SaveAssets();
            try { ASHIconBaker.BakeAll(new List<string> { "ASH_Item_FuelCan", "ASH_Item_CarBattery", "ASH_Item_FanBelt", "ASH_Item_Tire" }); } catch (System.Exception e) { Debug.LogException(e); }
            var reg = Resources.Load<ASHItemRegistry>("ASH_ItemRegistry");
            foreach (var it in new[] { FuelCan, Battery, Belt, Tire }) if (reg && it && !reg.items.Contains(it)) { reg.items.Add(it); EditorUtility.SetDirty(reg); }
            // the vehicle prefabs know their parts
            foreach (var path in new[] { "Assets/_ASH/Prefabs/Vehicles/ASH_Vehicle_Pickup.prefab", "Assets/_ASH/Prefabs/Vehicles/ASH_Vehicle_Car.prefab" })
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!pf) continue;
                var c = pf.GetComponent<ASHVehicleCondition>(); if (!c) continue;
                c.batteryItem = Battery; c.beltItem = Belt; if (c.HasTires) c.tireItem = Tire; EditorUtility.SetDirty(pf);
            }
            AssetDatabase.SaveAssets();
            foreach (var it in new[] { FuelCan, Battery, Belt, Tire }) _log.Append($"\n{(it ? it.itemId : "?")}: {(it ? it.displayName : "-")} icon={(it && it.icon ? "ok" : "MISSING")} pickup={(it && it.worldPickupPrefab ? it.worldPickupPrefab.name : "-")} litres={(it ? it.containerLitres : 0)}");
            Debug.Log(_log.ToString());
        }

        public static void ConfigureAll()
        {
            var can = FuelCan;
            if (can)
            {
                Def(can, "fuel_can", "Fuel Can", "A dented 20 litre jerry can. Pour it into a vehicle from the vehicle menu (Fuel Up). Keep it when it's empty.",
                    ASHItemCategory.Salvage, new Color(0.72f, 0.18f, 0.12f), 1);
                can.containerLitres = 20f; can.sellValue = 8;
                Pickup(can, "FuelCan", BuildCanVisual, true);
            }
            var tire = Tire;
            if (tire)
            {
                Def(tire, "tire", "Pickup Tire", "A worn but sound truck tire on its rim. The pickup is missing two; fit them from its Repair menu.",
                    ASHItemCategory.Salvage, new Color(0.14f, 0.14f, 0.14f), 1);
                tire.sellValue = 12;
                Pickup(tire, "Tire", BuildTireVisual, false);
            }
            var bat = Battery;
            if (bat)
            {
                Def(bat, "carbattery", "Car Battery", "A 12 volt lead-acid battery, still holding a charge. Fit it from a vehicle's Repair menu.",
                    ASHItemCategory.Salvage, new Color(0.25f, 0.25f, 0.27f), 1);
                bat.sellValue = 25;
                Pickup(bat, "CarBattery", BuildBatteryVisual, false);
            }
            var belt = Belt;
            if (belt)
            {
                Def(belt, "fanbelt", "Fan Belt", "A ribbed rubber engine belt. Without one the engine won't run. Fit it from a vehicle's Repair menu.",
                    ASHItemCategory.Salvage, new Color(0.2f, 0.18f, 0.16f), 2);
                belt.sellValue = 6;
                Pickup(belt, "FanBelt", BuildBeltVisual, false);
            }
        }

        static void Def(ASHItemDefinition it, string id, string name, string desc, ASHItemCategory cat, Color tint, int stack)
        {
            it.itemId = id; it.displayName = name; it.description = desc; it.category = cat; it.uiTint = tint; it.maxStack = stack; it.useVerb = "Use";
            it.keyItem = false; it.toolType = ASHToolType.None; it.requiresOpener = false; it.leftoverItem = null; it.breakdownYield = new List<ASHRecipeIngredient>();
            EditorUtility.SetDirty(it);
        }

        static void Pickup(ASHItemDefinition it, string file, System.Action<Transform> visual, bool fuelCan)
        {
            var root = new GameObject("ASH_Pickup_" + file);
            var vis = new GameObject("Visual").transform; vis.SetParent(root.transform, false);
            visual(vis);
            // sit the model on y = 0 at the pivot
            var b = Bounds(vis);
            vis.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = Bounds(vis);
            foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var col = root.AddComponent<BoxCollider>(); col.isTrigger = true;
            col.size = new Vector3(Mathf.Max(b.size.x, 0.25f) + 0.1f, Mathf.Max(b.size.y, 0.2f) + 0.05f, Mathf.Max(b.size.z, 0.25f) + 0.1f); col.center = new Vector3(0f, col.size.y * 0.5f, 0f);
            root.AddComponent<ASHItemPickup>().Configure(it, 1);
            if (fuelCan) root.AddComponent<ASHFuelCanPickup>();
            int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) ASHBuildUtil.SetLayerRecursive(root, layer);
            var pf = ASHBuildUtil.SavePrefab(root, $"{PickupDir}/ASH_Pickup_{file}.prefab");
            it.worldPickupPrefab = pf; EditorUtility.SetDirty(it);
            _log?.Append($"\n pickup {file} size={b.size:F2}");
        }

        static void BuildCanVisual(Transform parent)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CanModel);
            if (model)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
                var b = Bounds(parent);
                float h = b.size.y; if (h > 0.01f && (h < 0.38f || h > 0.6f)) go.transform.localScale *= 0.47f / h; // a 20 L jerry can is ~47 cm tall
                _log?.Append($"\n fuel can model {model.name} native height={h:F2}");
                return;
            }
            _log?.Append("\n fuel can model missing, primitive used");
            var red = ASHBuildUtil.Lit($"{Mat}/ASH_M_Pickup_FuelCan.mat", new Color(0.62f, 0.12f, 0.08f), 0.45f, null, 0.3f);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "Body", parent, new Vector3(0, 0.22f, 0), Vector3.zero, new Vector3(0.17f, 0.44f, 0.34f), red);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Spout", parent, new Vector3(0, 0.46f, 0.12f), new Vector3(25f, 0, 0), new Vector3(0.05f, 0.04f, 0.05f), red);
        }

        /// <summary>The pickup's own wheel (HPA PickUp_Wheel mesh + materials), lying flat.</summary>
        static void BuildTireVisual(Transform parent)
        {
            // t366: Anthony's own "Tire Replacement" (copied off the truck) is the model
            var his = HisTires().FirstOrDefault();
            if (his)
            {
                var copy = Object.Instantiate(his.gameObject, parent); copy.name = "Tire";
                copy.transform.localPosition = Vector3.zero; copy.transform.rotation = his.rotation; copy.transform.localScale = his.lossyScale;
                foreach (var c in copy.GetComponentsInChildren<Component>(true)) if (c is ASHItemPickup || c is Collider) Object.DestroyImmediate(c);
                foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) if (r.name.Contains("Lod1")) r.enabled = false; // the copy carries both LODs; show LOD0 only
                _log?.Append($"\n tire model from scene '{his.name}'");
                return;
            }
            MeshFilter src = null;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
                if (mf.sharedMesh && mf.name.StartsWith("PickUp_Wheel_01") && !mf.name.Contains("Lod") && !mf.name.Contains("Collider")) { src = mf; break; }
            if (src)
            {
                var go = new GameObject("Tire"); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh; go.AddComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;
                go.transform.localScale = src.transform.lossyScale;
                // lay the axle (thinnest axis of the mesh) vertical
                var e = src.sharedMesh.bounds.size;
                go.transform.localRotation = e.x <= e.y && e.x <= e.z ? Quaternion.Euler(0, 0, 90) : e.z <= e.x && e.z <= e.y ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
                _log?.Append($"\n tire model {src.sharedMesh.name} from {src.name}");
                return;
            }
            _log?.Append("\n tire model missing, primitive used");
            var rubber = ASHBuildUtil.Lit($"{Mat}/ASH_M_Pickup_FanBelt.mat", new Color(0.07f, 0.065f, 0.06f), 0.15f);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Tire", parent, new Vector3(0, 0.15f, 0), Vector3.zero, new Vector3(0.9f, 0.15f, 0.9f), rubber);
        }

        static void BuildBatteryVisual(Transform parent)
        {
            var black = ASHBuildUtil.Lit($"{Mat}/ASH_M_Pickup_CarBattery.mat", new Color(0.09f, 0.09f, 0.1f), 0.35f);
            var top = ASHBuildUtil.Lit($"{Mat}/ASH_M_Pickup_CarBatteryTop.mat", new Color(0.16f, 0.16f, 0.17f), 0.25f);
            var lead = ASHBuildUtil.Lit($"{Mat}/ASH_M_Pickup_Lead.mat", new Color(0.55f, 0.56f, 0.58f), 0.5f, null, 0.8f);
            var red = ASHBuildUtil.Lit($"{Mat}/ASH_M_Pickup_RedCap.mat", new Color(0.7f, 0.1f, 0.08f), 0.4f);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "Case", parent, new Vector3(0, 0.085f, 0), Vector3.zero, new Vector3(0.26f, 0.17f, 0.175f), black);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "Lid", parent, new Vector3(0, 0.18f, 0), Vector3.zero, new Vector3(0.265f, 0.02f, 0.18f), top);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Pos", parent, new Vector3(0.09f, 0.2f, 0.055f), Vector3.zero, new Vector3(0.025f, 0.012f, 0.025f), lead);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Neg", parent, new Vector3(-0.09f, 0.2f, 0.055f), Vector3.zero, new Vector3(0.025f, 0.012f, 0.025f), lead);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "PosCap", parent, new Vector3(0.09f, 0.215f, 0.055f), Vector3.zero, new Vector3(0.03f, 0.008f, 0.03f), red);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "Handle", parent, new Vector3(0, 0.205f, -0.03f), Vector3.zero, new Vector3(0.16f, 0.012f, 0.018f), top);
        }

        static void BuildBeltVisual(Transform parent)
        {
            var rubber = ASHBuildUtil.Lit($"{Mat}/ASH_M_Pickup_FanBelt.mat", new Color(0.07f, 0.065f, 0.06f), 0.15f);
            string meshPath = $"{Mat}/ASH_Mesh_FanBelt.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh) { mesh = BeltMesh(); AssetDatabase.CreateAsset(mesh, meshPath); }
            var go = new GameObject("Belt"); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = rubber;
        }

        /// <summary>A flat oval loop (stadium shape) with a rectangular cross-section: 30 x 14 cm, 2 cm wide, 0.6 cm thick, lying flat.</summary>
        static Mesh BeltMesh()
        {
            const int seg = 64; float a = 0.08f, r = 0.07f, t = 0.006f, w = 0.02f;
            var path = new List<Vector2>(); float per = 2f * a * 2f + 2f * Mathf.PI * r;
            for (int i = 0; i < seg; i++)
            {
                float s = i / (float)seg * per; Vector2 p;
                if (s < 2f * a) p = new Vector2(-a + s, -r);
                else if ((s -= 2f * a) < Mathf.PI * r) { float an = -Mathf.PI / 2f + s / r; p = new Vector2(a + Mathf.Cos(an) * r, Mathf.Sin(an) * r); }
                else if ((s -= Mathf.PI * r) < 2f * a) p = new Vector2(a - s, r);
                else { s -= 2f * a; float an = Mathf.PI / 2f + s / r; p = new Vector2(-a + Mathf.Cos(an) * r, Mathf.Sin(an) * r); }
                path.Add(p);
            }
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                var p = path[i]; var n = (path[(i + 1) % seg] - path[(i + seg - 1) % seg]).normalized; var o = new Vector2(n.y, -n.x); // outward
                var pin = p - o * t * 0.5f; var pout = p + o * t * 0.5f;
                v.Add(new Vector3(pout.x, 0f, pout.y)); v.Add(new Vector3(pout.x, w, pout.y)); v.Add(new Vector3(pin.x, w, pin.y)); v.Add(new Vector3(pin.x, 0f, pin.y));
            }
            for (int i = 0; i < seg; i++)
            {
                int c = i * 4, nx = ((i + 1) % seg) * 4;
                for (int k = 0; k < 4; k++) { int k2 = (k + 1) % 4; tri.AddRange(new[] { c + k, nx + k2, nx + k, c + k, c + k2, nx + k2 }); }
            }
            var m = new Mesh { name = "ASH_FanBelt" }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Bounds Bounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        // ------------------------------------------------------------------ placement on the cabin porch
        [MenuItem("ASH/Systems/Vehicles/Place Repair Items At Cabin")]
        public static void Place()
        {
            var sb = new StringBuilder("[ASH] VEHP Place Repair Items At Cabin");
            var cabin = GameObject.Find("Grandpa Cabin"); var door = GameObject.Find("ASH_CabinDoor") ?? GameObject.Find("WoodenDoor-Cabin");
            if (!cabin) { Debug.LogWarning("[ASH] VEHP no 'Grandpa Cabin' in the scene"); return; }
            var old = GameObject.Find(GroupName);
            if (old)
            {
                // t376: only our own pickups die with the old group; anything else (e.g. his Tire Replacement objects) is moved out, world pose kept
                foreach (Transform ch in old.transform.Cast<Transform>().ToList())
                    if (!ch.GetComponent<ASHItemPickup>() || ch.name.ToLowerInvariant().StartsWith("tire replacement") || !PrefabUtility.IsAnyPrefabInstanceRoot(ch.gameObject))
                        Undo.SetTransformParent(ch, old.transform.parent, true, "Place Repair Items");
                Undo.DestroyObjectImmediate(old);
            }
            Physics.SyncTransforms();
            var group = new GameObject(GroupName); Undo.RegisterCreatedObjectUndo(group, "Place Repair Items");
            var C = cabin.transform;
            // porch: between the front door (local z ~2) and the steps (local z ~5.3), floor at the door's sill height
            float doorLz = door ? C.InverseTransformPoint(door.transform.position).z : 2f;
            var floorRef = door ? door.transform.position.y : C.position.y + 0.6f;
            var items = new[] { (FuelCan, "FuelCan"), (Battery, "CarBattery"), (Belt, "FanBelt") };
            var used = new List<Vector3>();
            foreach (var (it, file) in items)
            {
                if (!it || !it.worldPickupPrefab) { sb.Append($"\n{file}: missing item or pickup prefab (run Build Vehicle Items)"); continue; }
                bool placed = false;
                // candidate spots along the front wall to the right of the door, then further out on the porch
                foreach (var lp in Candidates(doorLz))
                {
                    var w = C.TransformPoint(new Vector3(lp.x, 0f, lp.y));
                    if (used.Any(u => Vector3.Distance(u, w) < 0.55f)) continue;
                    if (!Floor(w, floorRef, cabin.transform, out var hit)) continue;
                    var size = PickupSize(it.worldPickupPrefab);
                    var rot = Quaternion.Euler(0f, C.eulerAngles.y + Random.Range(-25f, 25f) + (file == "FuelCan" ? 90f : 0f), 0f);
                    if (Blocked(hit, size, rot)) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(it.worldPickupPrefab, group.transform);
                    go.transform.SetPositionAndRotation(hit + Vector3.up * 0.003f, rot);
                    used.Add(w); placed = true;
                    sb.Append($"\n{file}: at {go.transform.position:F2} (cabin local {C.InverseTransformPoint(go.transform.position):F2})");
                    break;
                }
                if (!placed)
                {
                    // big things (tires): on the ground beside the porch steps
                    foreach (var lp in GroundCandidates())
                    {
                        var w = C.TransformPoint(new Vector3(lp.x, 0f, lp.y));
                        if (used.Any(u => Vector3.Distance(u, w) < 1.0f)) continue;
                        if (!Ground(w, out var hit, out var nrm)) continue;
                        var size = PickupSize(it.worldPickupPrefab);
                        var rot = Quaternion.FromToRotation(Vector3.up, nrm) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                        if (Blocked(hit, size, rot)) continue;
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(it.worldPickupPrefab, group.transform);
                        go.transform.SetPositionAndRotation(hit + nrm * 0.005f, rot);
                        used.Add(w); placed = true;
                        sb.Append($"\n{file}: on the ground at {go.transform.position:F2} (cabin local {C.InverseTransformPoint(go.transform.position):F2})");
                        break;
                    }
                }
                if (!placed) sb.Append($"\n{file}: NO clear spot on or near the porch");
            }
            PlaceHisTires(C, used, sb);
            EditorSceneManager.MarkSceneDirty(cabin.scene);
            Debug.Log(sb.ToString());
        }

        static IEnumerable<Vector2> Candidates(float doorLz)
        {
            float z0 = doorLz + 0.75f;
            foreach (float dz in new[] { 0f, 0.6f, 1.2f })
                foreach (float x in new[] { 0.9f, 1.5f, 2.1f, 2.7f, -0.6f, -1.2f, 0.3f, 3.2f })
                    yield return new Vector2(x, z0 + dz);
        }

        public static List<Transform> HisTires() => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .Where(t => t.name.ToLowerInvariant().StartsWith("tire replacement") && !t.GetComponentInParent<ASHVehicle>(true)).OrderBy(t => t.position.y).ToList();

        /// <summary>Anthony's "Tire Replacement" objects become Tire pickups (pickup + trigger collider), left exactly where he placed them (t368).</summary>
        static void PlaceHisTires(Transform C, List<Vector3> used, StringBuilder sb)
        {
            var tires = HisTires(); var tire = Tire;
            if (tires.Count == 0) { sb.Append("\nTire Replacement: none in the scene"); return; }
            foreach (var t in tires)
            {
                Undo.RecordObject(t.gameObject, "Tire pickup");
                var pk = t.GetComponent<ASHItemPickup>(); if (!pk) pk = Undo.AddComponent<ASHItemPickup>(t.gameObject);
                pk.Configure(tire, 1); EditorUtility.SetDirty(pk);
                var bc = t.GetComponent<BoxCollider>(); if (!bc) bc = Undo.AddComponent<BoxCollider>(t.gameObject);
                bc.isTrigger = true;
                var b = LocalBounds(t);
                bc.center = b.center; bc.size = b.size + new Vector3(0.08f, 0.08f, 0.08f);
                int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) t.gameObject.layer = layer;
            }
            foreach (var t in tires) sb.Append($"\n{t.name}: tire pickup in place at {t.position:F2} (not moved, t368)");
        }

        static Bounds LocalBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true); var b = new Bounds(); bool f = true;
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = t.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (f) { b = new Bounds(p, Vector3.zero); f = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static IEnumerable<Vector2> GroundCandidates()
        {
            foreach (float z in new[] { 5.6f, 6.4f, 4.6f, 7.2f })
                foreach (float x in new[] { 1.7f, -1.8f, 2.7f, -2.8f, 3.6f, -3.7f })
                    yield return new Vector2(x, z);
        }

        static bool Ground(Vector3 w, out Vector3 hit, out Vector3 nrm)
        {
            hit = default; nrm = Vector3.up;
            var hits = Physics.RaycastAll(w + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
            if (hits.Length == 0 || !(hits[0].collider is TerrainCollider) || hits[0].normal.y < 0.85f) return false;
            hit = hits[0].point; nrm = hits[0].normal; return true;
        }

        static bool Floor(Vector3 w, float floorRef, Transform cabin, out Vector3 hit)
        {
            hit = default;
            var o = new Vector3(w.x, floorRef + 1.2f, w.z);
            var hits = Physics.RaycastAll(o, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
            foreach (var h in hits)
            {
                if (h.normal.y < 0.92f) return false;                       // first hit is a sloped thing: skip this spot
                if (Mathf.Abs(h.point.y - floorRef) > 0.45f) return false;  // not the porch floor (ground below, a railing above)
                hit = h.point; return true;
            }
            return false;
        }

        static Vector3 PickupSize(GameObject pf) { var bc = pf.GetComponent<BoxCollider>(); return bc ? bc.size - new Vector3(0.1f, 0.05f, 0.1f) : new Vector3(0.3f, 0.3f, 0.3f); }

        static bool Blocked(Vector3 p, Vector3 size, Quaternion rot, List<Transform> ignore)
        {
            var half = new Vector3(size.x * 0.5f + 0.03f, size.y * 0.5f, size.z * 0.5f + 0.03f);
            var c = p + Vector3.up * (size.y * 0.5f + 0.02f);
            foreach (var col in Physics.OverlapBox(c, half, rot, ~0, QueryTriggerInteraction.Ignore)) if (!ignore.Any(t => col.transform.IsChildOf(t))) return true;
            return false;
        }

        static bool Blocked(Vector3 p, Vector3 size, Quaternion rot)
        {
            var half = new Vector3(size.x * 0.5f + 0.03f, size.y * 0.5f, size.z * 0.5f + 0.03f);
            var c = p + Vector3.up * (size.y * 0.5f + 0.02f);
            foreach (var col in Physics.OverlapBox(c, half, rot, ~0, QueryTriggerInteraction.Ignore)) return true;
            return false;
        }
    }
}
