using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.Vehicles;
using NWH.VehiclePhysics;
using NWH.WheelController3D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>ASH/Systems/Vehicles/Setup Vehicles (t356-t361): builds ASH_Vehicle_Pickup / ASH_Vehicle_Car prefabs from the scene's HPA
    /// "Pickup" and "Car" on the NWH Vehicle Physics demo car (engine, gearbox, sound, axles), with the model (and the lights placed on
    /// it) copied exactly, and swaps them into the open scene at the same place. The source objects are deactivated, not deleted.
    /// Idempotent: run again to rebuild. Does not save the scene. Vendor files are only read.</summary>
    public static class ASHVehicleSetup
    {
        const string Demo = "Assets/VehiclePhysics/Prefabs/Vehicles/Car.prefab";
        const string Dir = "Assets/_ASH/Prefabs/Vehicles";
        static StringBuilder _log;

        [MenuItem("ASH/Systems/Vehicles/Setup Vehicles")]
        public static void Setup()
        {
            _log = new StringBuilder("[ASH] VEHS Setup Vehicles");
            if (EditorApplication.isPlaying) { Debug.LogWarning("[ASH] VEHS stop Play first"); return; }
            if (!AssetDatabase.IsValidFolder(Dir)) { AssetDatabase.CreateFolder("Assets/_ASH/Prefabs", "Vehicles"); }
            Build("Pickup", "pickup", "Pickup", 2000f, true);
            Build("Car", "car", "Car", 1300f, false);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log(_log.ToString());
        }

        static Transform FindSource(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t.name != name || t.parent != null) continue;
                if (t.GetComponentInChildren<VehicleController>(true) || t.GetComponentInChildren<ASHVehicle>(true)) continue;
                if (!t.GetComponentInChildren<LODGroup>(true)) continue;
                return t;
            }
            return null;
        }

        static void Build(string srcName, string id, string disp, float mass, bool pickup)
        {
            var src = FindSource(srcName);
            if (!src) { _log.Append($"\n{disp}: source '{srcName}' not found"); return; }
            string vName = "ASH_Vehicle_" + disp;
            foreach (var old in Object.FindObjectsByType<ASHVehicle>(FindObjectsInactive.Include))
                if (old.vehicleId == id) Undo.DestroyObjectImmediate(old.gameObject);

            var demoPf = AssetDatabase.LoadAssetAtPath<GameObject>(Demo);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(demoPf);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = vName;
            root.transform.SetPositionAndRotation(src.position, Quaternion.Euler(0f, src.eulerAngles.y, 0f));
            foreach (Transform c in root.transform.Cast<Transform>().ToList()) if (c.name != "WheelControllers") Object.DestroyImmediate(c.gameObject);

            var model = Object.Instantiate(src.gameObject, root.transform); model.name = "Model"; model.SetActive(true);
            model.transform.SetPositionAndRotation(src.position, src.rotation); model.transform.localScale = src.localScale;
            var R = root.transform;

            // ---- colliders: drop wheel + duplicate colliders, the rest convex (dynamic body)
            foreach (var col in model.GetComponentsInChildren<Collider>(true))
            {
                string n = col.name.ToLowerInvariant();
                if (n.Contains("wheel") || n.EndsWith("(1)")) { Object.DestroyImmediate(col); continue; }
                if (col is MeshCollider mc) mc.convex = true;
            }

            // ---- wheels
            var wheelRends = model.GetComponentsInChildren<Renderer>(true).Where(r => { var n = r.name.ToLowerInvariant(); return n.Contains("wheel") && !n.Contains("collider") && !n.Contains("steer"); }).ToList();
            var all = Enc(wheelRends, R);
            var groups = new Dictionary<int, List<Renderer>>();
            foreach (var r in wheelRends) { var c = Enc(new List<Renderer> { r }, R).center; int k = (c.x < all.center.x ? 0 : 1) + (c.z < all.center.z ? 0 : 2); if (!groups.TryGetValue(k, out var l)) groups[k] = l = new List<Renderer>(); l.Add(r); }
            var vc = root.GetComponent<VehicleController>();
            var wcs = root.GetComponentsInChildren<WheelController>(true);
            float springLen = pickup ? 0.3f : 0.25f;
            float massK = mass / 1200f;
            float groundY = float.MaxValue;
            foreach (var kv in groups)
            {
                var b = Enc(kv.Value, R); bool left = (kv.Key & 1) == 0, front = (kv.Key & 2) != 0;
                var pivot = new GameObject("ASH_WheelPivot_" + (front ? "F" : "B") + (left ? "L" : "R")).transform;
                pivot.SetParent(model.transform, false); pivot.position = R.TransformPoint(b.center); pivot.rotation = R.rotation;
                // move the top-most node of each mesh that holds only this wheel under the pivot
                var moved = new HashSet<Transform>();
                foreach (var r in kv.Value)
                {
                    var top = r.transform;
                    while (top.parent && top.parent != model.transform && OnlyThisWheel(top.parent, kv.Value, wheelRends)) top = top.parent;
                    if (moved.Add(top)) top.SetParent(pivot, true);
                }
                var wc = wcs.FirstOrDefault(w => (w.transform.localPosition.x < 0f) == left && (w.transform.localPosition.z > 0f) == front);
                if (!wc) { _log.Append($"\n{disp}: no WheelController for {pivot.name}"); continue; }
                float radius = b.size.y * 0.5f;
                wc.transform.localPosition = new Vector3(b.center.x, b.center.y + springLen * 0.4f, b.center.z);
                wc.name = "WheelController_" + (front ? "F" : "B") + (left ? "L" : "R");
                wc.wheel.tireRadius = radius; wc.wheel.width = Mathf.Max(0.12f, b.size.x); wc.wheel.visual = pivot.gameObject; wc.parent = root;
                wc.wheel.mass = pickup ? 30f : 22f;
                wc.spring.maxLength = springLen; wc.spring.maxForce = 22000f * massK;
                wc.damper.unitBumpForce = 800f * massK; wc.damper.unitReboundForce = 1000f * massK;
                wc.useRimCollider = false;
                EditorUtility.SetDirty(wc);
                groundY = Mathf.Min(groundY, b.center.y - radius);
                _log.Append($"\n{disp}: {pivot.name} c={b.center:F2} r={radius:F2} w={b.size.x:F2} meshes={kv.Value.Count} top={string.Join(",", moved.Select(m => m.name))}");
            }
            if (groundY == float.MaxValue) groundY = 0f;

            // ---- body / physics
            var rb = root.GetComponent<Rigidbody>(); rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var com = root.GetComponent<CenterOfMass>(); if (com) com.centerOfMassOffset = new Vector3(0f, pickup ? -0.35f : -0.25f, pickup ? 0.15f : 0.05f);
            vc.engine.runOnEnable = false; vc.engine.runOnStartup = false; vc.engine.stopOnDisable = true;
            vc.engine.maxPower = pickup ? 160f : 110f;
            vc.fuel.useFuel = false;
            vc.damage.enabled = false;
            var so = new SerializedObject(vc); var act = so.FindProperty("active"); if (act != null) act.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(vc);

            // ---- ASH components
            var veh = root.AddComponent<ASHVehicle>(); veh.vehicleId = id; veh.displayName = disp; veh.carriesDog = pickup;
            var fuel = root.AddComponent<ASHVehicleFuel>(); fuel.capacityLitres = pickup ? 80f : 50f; fuel.litres = pickup ? 30f : 20f;
            fuel.idleLitresPerHour = pickup ? 1.8f : 1.0f; fuel.fullThrottleLitresPerHour = pickup ? 28f : 16f;
            var cond = root.AddComponent<ASHVehicleCondition>();
            cond.batteryItem = AssetDatabase.LoadAssetAtPath<ASH.Inventory.ASHItemDefinition>("Assets/_ASH/Data/Items/Optional_TruckParts/ASH_Item_CarBattery.asset");
            cond.beltItem = AssetDatabase.LoadAssetAtPath<ASH.Inventory.ASHItemDefinition>("Assets/_ASH/Data/Items/Optional_TruckParts/ASH_Item_FanBelt.asset");
            if (pickup)
            {
                // t364: the truck starts broken: no battery, no belt, empty tank, front-left and rear-right tires missing
                cond.startBroken = true;
                cond.tireItem = AssetDatabase.LoadAssetAtPath<ASH.Inventory.ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_Tire.asset");
                cond.wheelSlots.Clear();
                foreach (var n in new[] { "WheelController_FL", "WheelController_FR", "WheelController_BL", "WheelController_BR" })
                    cond.wheelSlots.Add(root.GetComponentsInChildren<WheelController>(true).FirstOrDefault(w => w.name == n));
                cond.startMissingTires = new List<int> { 0, 3 };
            }
            var cam = root.AddComponent<ASHVehicleCamera>(); cam.distance = pickup ? 7f : 6f;
            var lights = root.AddComponent<ASHVehicleLights>();

            var mb = Enc(model.GetComponentsInChildren<Renderer>(true).ToList(), R);
            // seat / head
            var seatR = model.GetComponentsInChildren<Renderer>(true).Where(r => r.name.ToLowerInvariant().Contains("seat")).OrderBy(r => Enc(new List<Renderer> { r }, R).center.x).FirstOrDefault();
            Bounds sb = seatR ? Enc(new List<Renderer> { seatR }, R) : new Bounds(new Vector3(-0.4f, groundY + 0.9f, 0f), Vector3.one * 0.5f);
            veh.seat = Point(R, "ASH_Seat", new Vector3(sb.center.x, sb.min.y + 0.35f, sb.center.z));
            veh.head = Point(R, "ASH_Head", new Vector3(sb.center.x, sb.min.y + 1.05f, sb.center.z + 0.05f));
            cam.head = veh.head;
            // driver door
            var door = FindDriverDoor(model.transform, R);
            if (door)
            {
                var d = door.gameObject.AddComponent<ASHVehicleDoor>();
                d.hingeAxisLocal = door.InverseTransformDirection(R.up).normalized;
                d.openAngle = OutwardSign(door, d.hingeAxisLocal, R) * 65f;
                d.doorColliders = door.GetComponentsInChildren<Collider>(true);
                d.openClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Doors/openDoor.wav");
                d.closeClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Doors/closeDoor.wav");
                veh.driverDoor = d;
                _log.Append($"\n{disp}: door {door.name} axis={d.hingeAxisLocal:F2} angle={d.openAngle} cols={d.doorColliders.Length}");
            }
            else _log.Append($"\n{disp}: NO driver door pivot found");
            float doorZ = door ? R.InverseTransformPoint(door.position).z - 0.45f : sb.center.z;
            veh.exitPoint = Point(R, "ASH_ExitPoint", new Vector3(mb.min.x - 0.55f, groundY + 0.05f, doorZ));
            if (pickup)
            {
                var gate = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.ToLowerInvariant().Contains("backdoor"));
                float gateZ = gate ? R.InverseTransformPoint(gate.position).z : mb.min.z + 0.1f;
                float cabZ = sb.center.z - 0.55f;
                float bedZ = (gateZ + cabZ) * 0.5f;
                float floorY = BedFloor(model, R, new Vector3(0f, mb.max.y + 1f, bedZ), groundY + 0.9f);
                veh.dogSeat = Point(R, "ASH_DogSeat", new Vector3(0f, floorY + 0.02f, bedZ));
                veh.dogTailgate = Point(R, "ASH_DogTailgate", new Vector3(0f, groundY + 0.05f, mb.min.z - 0.9f));
                _log.Append($"\n{disp}: bed z={bedZ:F2} floorY={floorY:F2} gateZ={gateZ:F2} cabZ={cabZ:F2}");
            }

            // ---- lights: his placed Light objects, untouched (runtime switches them off until someone gets in)
            foreach (var l in model.GetComponentsInChildren<Light>(true))
            {
                float z = R.InverseTransformPoint(l.transform.position).z;
                if (z >= mb.center.z) lights.headLights.Add(l); else lights.brakeLights.Add(l);
            }
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) if (ms[i] && ms[i].name.EndsWith("HeadLights")) lights.headlightEmissive.Add(new ASHVehicleLights.Slot { renderer = r, material = i });
            }
            lights.keepAuthoredSettings = true;

            // ---- swap: hide the source, save the prefab, keep the instance in the scene
            Undo.RecordObject(src.gameObject, "Setup Vehicles"); src.gameObject.SetActive(false);
            string path = $"{Dir}/{vName}.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.AutomatedAction);
            Undo.RegisterCreatedObjectUndo(root, "Setup Vehicles");
            _log.Append($"\n{disp}: built {path} mass={mass} lights head={lights.headLights.Count} brake={lights.brakeLights.Count} emissive={lights.headlightEmissive.Count} seat={veh.seat.localPosition:F2} head={veh.head.localPosition:F2} exit={veh.exitPoint.localPosition:F2} groundY={groundY:F2} bodyLayer={model.layer}");
        }

        static bool OnlyThisWheel(Transform t, List<Renderer> mine, List<Renderer> wheels)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) if (!mine.Contains(r)) return false;
            return true;
        }

        static Transform FindDriverDoor(Transform model, Transform R)
        {
            Transform best = null; float bestZ = float.MinValue;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLowerInvariant();
                if (!n.StartsWith("pivot") || n.Contains("wheel") || n.Contains("back") || n.Contains("boot")) continue;
                bool hasDoor = t.GetComponentsInChildren<Renderer>(true).Any(r => r.name.ToLowerInvariant().Contains("door"));
                if (!hasDoor) continue;
                var lp = R.InverseTransformPoint(t.position);
                if (lp.x >= 0f) continue;
                if (lp.z > bestZ) { bestZ = lp.z; best = t; }
            }
            return best;
        }

        static float OutwardSign(Transform pivot, Vector3 axis, Transform R)
        {
            var rends = pivot.GetComponentsInChildren<Renderer>(true).ToList();
            var q = pivot.localRotation; float x0 = Enc(rends, R).center.x;
            pivot.localRotation = q * Quaternion.AngleAxis(30f, axis); float xp = Enc(rends, R).center.x;
            pivot.localRotation = q;
            return xp < x0 ? 1f : -1f;   // driver side is -x: outward = x decreasing
        }

        static float BedFloor(GameObject model, Transform R, Vector3 fromLocal, float fallback)
        {
            var from = R.TransformPoint(fromLocal); float best = float.MaxValue; float y = fallback;
            foreach (var c in model.GetComponentsInChildren<Collider>(true))
            {
                var n = c.name.ToLowerInvariant(); if (n.Contains("door")) continue;
                bool was = c.enabled;
                if (c is MeshCollider mc) { mc.convex = false; }
                if (c.Raycast(new Ray(from, -R.up), out var h, 10f) && h.distance < best) { best = h.distance; y = R.InverseTransformPoint(h.point).y; }
                if (c is MeshCollider mc2) mc2.convex = true;
            }
            return y;
        }

        static Transform Point(Transform R, string name, Vector3 local)
        {
            var t = new GameObject(name).transform; t.SetParent(R, false); t.localPosition = local; t.localRotation = Quaternion.identity; return t;
        }

        /// <summary>Bounds of renderers in R's local space (from mesh bounds corners).</summary>
        static Bounds Enc(List<Renderer> rs, Transform R)
        {
            bool f = true; var b = new Bounds();
            foreach (var r in rs)
            {
                Bounds lb; Matrix4x4 m = r.transform.localToWorldMatrix;
                var mf = r.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh) lb = mf.sharedMesh.bounds; else if (r is SkinnedMeshRenderer sk) lb = sk.localBounds; else { lb = r.bounds; m = Matrix4x4.identity; }
                for (int i = 0; i < 8; i++)
                {
                    var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = R.InverseTransformPoint(m.MultiplyPoint3x4(c));
                    if (f) { b = new Bounds(p, Vector3.zero); f = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
