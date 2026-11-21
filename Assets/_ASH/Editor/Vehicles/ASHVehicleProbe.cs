using System.Text;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    public static class ASHVehicleProbe
    {
        public static void Demo()
        {
            var sb = new StringBuilder("[ASH] VEHD");
            foreach (var n in new[] { "Car", "Sedan", "Truck", "Monster Truck" })
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VehiclePhysics/Prefabs/Vehicles/" + n + ".prefab"); if (!pf) continue;
                var vc = pf.GetComponent<NWH.VehiclePhysics.VehicleController>(); var rb = pf.GetComponent<Rigidbody>();
                sb.Append($"\n{n}: mass={rb.mass} drag={rb.linearDamping} ang={rb.angularDamping} interp={rb.interpolation} axles={vc.axles.Count} maxPower={vc.engine.maxPower} maxRPM={vc.engine.maxRPM} gears={vc.transmission.ForwardGears.Count} active={vc.Active}");
                foreach (Transform c in pf.transform) { sb.Append($"\n  {c.name} lp={c.localPosition}"); foreach (var comp in c.GetComponents<Component>()) sb.Append(" " + comp.GetType().Name); foreach (Transform cc in c) sb.Append($"\n     {cc.name} lp={cc.localPosition}"); }
                foreach (var comp in pf.GetComponents<Component>()) sb.Append(" [" + comp.GetType().Name + "]");
                foreach (var w in pf.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
                    sb.Append($"\n  WC {w.name} r={w.wheel.tireRadius} wid={w.wheel.width} m={w.wheel.mass} spring={w.spring.maxLength}/{w.spring.maxForce} damp={w.damper.unitBumpForce}/{w.damper.unitReboundForce} vis={(w.wheel.visual ? w.wheel.visual.name : "-")} parent={(w.parent ? w.parent.name : "-")}");
            }
            Debug.Log(sb.ToString());
        }
        public static void Cabin()
        {
            var sb = new StringBuilder("[ASH] VEHC");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            {
                var n = t.name.ToLowerInvariant();
                if (!(n.Contains("cabin") || n.Contains("porch") || n.Contains("deck") || n.Contains("veranda") || n.Contains("step") || n.Contains("stair"))) continue;
                if (t.parent && t.parent.name.ToLowerInvariant().Contains("cabin") && !n.Contains("porch")) continue;
                var rs = t.GetComponentsInChildren<Renderer>(); Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero); foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.Append($"\n{Path(t)} pos={t.position:F2} rot={t.eulerAngles.y:F0} b={b.center:F2}/{b.size:F2} kids={t.childCount}");
            }
            Debug.Log(sb.ToString());
        }
        public static void CabinKids()
        {
            var sb = new StringBuilder("[ASH] VEHK");
            var g = GameObject.Find("Grandpa Cabin"); if (!g) { Debug.Log("[ASH] VEHK none"); return; }
            var T = g.transform;
            foreach (var t in g.GetComponentsInChildren<Transform>())
            {
                if (t == T || t.parent != T && t.parent.parent != T) continue;
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { sb.Append($"\n {Path(t)} lp={T.InverseTransformPoint(t.position):F2} (no renderers)"); continue; }
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.Append($"\n {Path(t)} lp={T.InverseTransformPoint(t.position):F2} wc={b.center:F2} ws={b.size:F2} cols={t.GetComponentsInChildren<Collider>().Length}");
            }
            // porch probe: grid of down-rays around the door
            var door = GameObject.Find("WoodenDoor-Cabin");
            if (door)
            {
                var d = door.transform.position; sb.Append($"\n door {d:F2} fwd={door.transform.forward:F2}");
                for (int x = -3; x <= 3; x++) for (int z = -3; z <= 3; z++)
                {
                    var o = d + new Vector3(x * 0.8f, 3f, z * 0.8f);
                    if (Physics.Raycast(o, Vector3.down, out var h, 8f, ~0, QueryTriggerInteraction.Ignore)) sb.Append($"\n  ray({x},{z}) y={h.point.y:F2} {Path(h.collider.transform)} n={h.normal.y:F2}");
                }
            }
            Debug.Log(sb.ToString());
        }
        public static void Tires()
        {
            var sb = new StringBuilder("[ASH] VEHT");
            var cab = GameObject.Find("Grandpa Cabin");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (!t.name.ToLowerInvariant().Contains("tire replacement")) continue;
                sb.Append($"\n{Path(t)} active={t.gameObject.activeInHierarchy} pos={t.position:F2} rot={t.eulerAngles:F0} scale={t.lossyScale:F2} prefab={PrefabUtility.IsPartOfPrefabInstance(t)} layer={t.gameObject.layer}");
                if (cab) sb.Append($" cabinLocal={cab.transform.InverseTransformPoint(t.position):F2} dist={Vector3.Distance(cab.transform.position, t.position):F1}");
                foreach (var c in t.GetComponentsInChildren<Component>(true)) if (!(c is Transform)) sb.Append($"\n   {c.gameObject.name}:{c.GetType().Name}{(c is MeshFilter mf && mf.sharedMesh ? "(" + mf.sharedMesh.name + ")" : "")}{(c is Renderer r ? " b=" + r.bounds.size.ToString("F2") : "")}");
                if (Physics.Raycast(t.position + Vector3.up * 2f, Vector3.down, out var h, 6f, ~0, QueryTriggerInteraction.Ignore)) sb.Append($"\n   below: {Path(h.collider.transform)} y={h.point.y:F2}");
            }
            Debug.Log(sb.ToString());
        }
        public static void Lights()
        {
            var sb = new StringBuilder("[ASH] VEHL");
            foreach (var root in new[] { "Pickup", "Car" })
                foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                {
                    if (go.name != root || go.parent != null || !go.gameObject.activeInHierarchy) continue;
                    sb.Append($"\n{root}:");
                    foreach (var l in go.GetComponentsInChildren<Light>(true)) sb.Append($"\n  LIGHT {Path(l.transform)} type={l.type} int={l.intensity} range={l.range} angle={l.spotAngle} col={l.color} on={l.enabled}/{l.gameObject.activeSelf} lp={go.InverseTransformPoint(l.transform.position)}");
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        var n = r.name.ToLowerInvariant(); var mats = ""; foreach (var m in r.sharedMaterials) mats += (m ? m.name + "(" + m.shader.name + (m.IsKeywordEnabled("_EMISSION") ? ",EM" : "") + ")" : "null") + " ";
                        if (n.Contains("light") || n.Contains("lamp") || mats.ToLowerInvariant().Contains("light") || mats.Contains(",EM")) sb.Append($"\n  REND {Path(r.transform)} mats={mats} c={go.InverseTransformPoint(r.bounds.center)} s={r.bounds.size}");
                    }
                    var cols = ""; foreach (var c in go.GetComponentsInChildren<Collider>(true)) cols += $"{c.name}:{c.GetType().Name}{(c is MeshCollider mc ? (mc.convex ? "(cvx)" : "") + mc.sharedMesh?.triangles.Length / 3 : "")} ";
                    sb.Append($"\n  COLS {cols}");
                    var lods = go.GetComponentsInChildren<LODGroup>(true).Length; sb.Append($" LODGroups={lods}");
                }
            Debug.Log(sb.ToString());
        }
        /// <summary>t376: report (and strip from prefabs) missing-script components on ASH pickups, vehicle prefabs and the open scene.</summary>
        public static void MissingScripts()
        {
            var sb = new StringBuilder("[ASH] MISSING ");
            foreach (var dir in new[] { "Assets/_ASH/Prefabs/Pickups", "Assets/_ASH/Prefabs/Vehicles" })
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid); var root = PrefabUtility.LoadPrefabContents(path); int n = 0;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) n += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); sb.Append($"prefab {path} removed={n}; "); }
                    PrefabUtility.UnloadPrefabContents(root);
                }
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var r in scene.GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                {
                    int m = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (m > 0) sb.Append($"scene {t.name} missing={m} prefabInst={PrefabUtility.IsPartOfPrefabInstance(t.gameObject)}; ");
                }
            Debug.Log(sb.ToString());
        }

        public static void AnyTire()
        {
            var sb = new StringBuilder("[ASH] VEHT2");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t.name.ToLowerInvariant().Contains("tire") || (t.GetComponent<ASH.Interaction.ASHItemPickup>() && t.GetComponent<ASH.Interaction.ASHItemPickup>().ToString().ToLowerInvariant().Contains("tire")))
                    sb.Append($"\n{Path(t)} active={t.gameObject.activeInHierarchy} pos={t.position:F2}");
            sb.Append($"\nundo={Undo.GetCurrentGroupName()}");
            Debug.Log(sb.ToString());
        }

        public static void UndoPlace()
        {
            string g = Undo.GetCurrentGroupName();
            if (g == "Place Repair Items") { Undo.PerformUndo(); Debug.Log("[ASH] VEHU undone: " + g); } else Debug.Log("[ASH] VEHU skipped, top undo is: " + g);
        }

        public static void Roots()
        {
            var sb = new StringBuilder("[ASH] VEHR");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject); var ap = src ? AssetDatabase.GetAssetPath(src) : "";
                if (!(ap.Contains("/Vehicles/") || ap.ToLowerInvariant().Contains("pickup_0") || ap.ToLowerInvariant().Contains("truck"))) continue;
                var b = new Bounds(t.position, Vector3.zero); bool f = true; foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (f) { b = r.bounds; f = false; } else b.Encapsulate(r.bounds); }
                sb.Append($"\n {Path(t)} active={t.gameObject.activeInHierarchy} pos={t.position} rot={t.eulerAngles} bounds c={b.center} s={b.size} src={ap} kids={t.childCount} rb={(bool)t.GetComponent<Rigidbody>()}");
                foreach (Transform c in t) { sb.Append($"\n    - {c.name} lp={c.localPosition} lr={c.localEulerAngles} cc={c.childCount}"); foreach (Transform g in c) sb.Append($"\n        - {g.name} lp={g.localPosition}"); }
            }
            Debug.Log(sb.ToString());
        }
        public static void Probe()
        {
            var sb = new StringBuilder("[ASH] VEHP");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                var n = t.name.ToLowerInvariant();
                if (!(n.Contains("pickup_0") || n.Contains("car_0") || n.Contains("lod_car") || n.Contains("lod_pickup") || n.Contains("truck") || n.StartsWith("car") || n.Contains("vehicle"))) continue;
                if (t.parent && t.parent.name.ToLowerInvariant().Contains(n)) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                var b = new Bounds(t.position, Vector3.zero); foreach (var r in t.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                sb.Append($"\n {Path(t)} active={t.gameObject.activeInHierarchy} pos={t.position} rot={t.eulerAngles} scale={t.lossyScale} bounds={b.size} src={(src ? AssetDatabase.GetAssetPath(src) : "-")} kids={t.childCount}");
                if (t.parent == null || !t.parent.name.ToLowerInvariant().Contains("lod_"))
                    foreach (Transform c in t) { sb.Append($"\n    - {c.name} lp={c.localPosition} cc={c.childCount}"); foreach (Transform g in c) sb.Append($"\n        - {g.name} lp={g.localPosition}"); }
            }
            Debug.Log(sb.ToString());
        }
        static string Path(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}
