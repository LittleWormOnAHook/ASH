using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ASH.Vehicles;

namespace ASH.EditorTools
{
    /// <summary>t389: ASH/Systems/Vehicles/Setup Driver (idempotent). Builds Resources/ASH_DriverAnimator (Props Animations Driver.fbx
    /// IdleDrive / DriveTurnLeft / DriveTurnRight on "Steer", Malbers sit-down / stand-up for Enter / Exit, IK pass on) and splits each
    /// vehicle prefab's steering wheel out of its dashboard mesh into a turnable ASH_SteeringWheel (new mesh assets; vendor files untouched).</summary>
    public static class ASHDriverSetup
    {
        const string CtrlPath = "Assets/_ASH/Resources/ASH_DriverAnimator.controller";
        const string GenDir = "Assets/_ASH/Art/Vehicles/Generated";
        const string DriverFbx = "Assets/Props Animations/Animations/Driver.fbx";
        const string SitIn = "Assets/Malbers Animations/Common/Human Anims/Actions/Sit/S_Idle_to_Sit_Front.fbx";
        const string SitOut = "Assets/Malbers Animations/Common/Human Anims/Actions/Sit/S_Sit_to_Idle.fbx";
        static void L(string m) => Debug.Log("[ASH] DRVSETUP " + m);

        [MenuItem("ASH/Systems/Vehicles/Setup Driver (Animator + Steering Wheels)")]
        public static void Run()
        {
            if (Application.isPlaying) { Debug.LogWarning("[ASH] Stop Play first"); return; }
            BuildController();
            SplitWheel("Assets/_ASH/Prefabs/Vehicles/ASH_Vehicle_Pickup.prefab", "PickUp_PickUp_Dashboard");
            SplitWheel("Assets/_ASH/Prefabs/Vehicles/ASH_Vehicle_Car.prefab", "Car_Dashboard");
            SplitBrakeLamps("Assets/_ASH/Prefabs/Vehicles/ASH_Vehicle_Pickup.prefab");
            SplitBrakeLamps("Assets/_ASH/Prefabs/Vehicles/ASH_Vehicle_Car.prefab");
            AssetDatabase.SaveAssets();
            L("done");
        }

        static AnimationClip Clip(string path, string name)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            var c = name == null ? all.FirstOrDefault() : all.FirstOrDefault(x => x.name == name);
            if (!c) L($"missing clip {name} in {path} (have: {string.Join(",", all.Select(x => x.name))})");
            return c;
        }

        static void BuildController()
        {
            var idle = Clip(DriverFbx, "IdleDrive"); var left = Clip(DriverFbx, "DriveTurnLeft"); var right = Clip(DriverFbx, "DriveTurnRight");
            var sin = Clip(SitIn, null); var sout = Clip(SitOut, null);
            if (AssetDatabase.LoadAssetAtPath<Object>(CtrlPath)) AssetDatabase.DeleteAsset(CtrlPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
            ac.AddParameter("Steer", AnimatorControllerParameterType.Float);
            var sm = ac.layers[0].stateMachine;
            var drive = ac.CreateBlendTreeInController("Drive", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D; tree.blendParameter = "Steer"; tree.useAutomaticThresholds = false;
            if (left) tree.AddChild(left, -1f); if (idle) tree.AddChild(idle, 0f); if (right) tree.AddChild(right, 1f);
            var enter = sm.AddState("Enter"); enter.motion = sin ? sin : (Motion)idle;
            var tr = enter.AddTransition(drive); tr.hasExitTime = true; tr.exitTime = 0.85f; tr.duration = 0.25f;
            var exit = sm.AddState("Exit"); exit.motion = sout ? sout : (Motion)idle;
            sm.defaultState = drive;
            var layers = ac.layers; layers[0].iKPass = true; ac.layers = layers;
            EditorUtility.SetDirty(ac);
            L($"controller {CtrlPath}: idle={(bool)idle} left={(bool)left} right={(bool)right} sitIn={(sin ? sin.name : "-")} sitOut={(sout ? sout.name : "-")} loop={(idle ? idle.isLooping : false)}");
        }

        class UF { readonly int[] p; public UF(int n) { p = new int[n]; for (int i = 0; i < n; i++) p[i] = i; } public int F(int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; } public void U(int a, int b) { a = F(a); b = F(b); if (a != b) p[a] = b; } }

        static Transform Deep(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

        static void SplitWheel(string prefabPath, string dashName)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var rt = root.transform; string pn = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                var drv = root.GetComponent<ASHVehicleDriver>(); if (!drv) drv = root.AddComponent<ASHVehicleDriver>();
                if (drv.wheelMaxAngle > 90f) drv.wheelMaxAngle = 90f;   // t391
                if (drv.tuningVersion < 396)
                {
                    // t396 defaults (once; later edits are kept): wheel turned the wrong way, truck driver higher, cab dog forward
                    drv.invertWheel = true;
                    var veh = root.GetComponent<ASHVehicle>();
                    if (veh && veh.vehicleId == "pickup") { drv.playerHeightOffset = 0.2f; veh.passengerSeatOffset = new Vector3(0f, 0f, 0.12f); EditorUtility.SetDirty(veh); }
                    drv.tuningVersion = 396; EditorUtility.SetDirty(drv);
                    L($"{pn}: t396 defaults: invertWheel=on playerHeightOffset={drv.playerHeightOffset:0.00}{(veh && veh.vehicleId == "pickup" ? " passengerSeatOffset.z=0.12" : "")}");
                }
                if (drv.tuningVersion < 398)
                {
                    // t398: the red brake Light objects were far too bright; scale both levels (tail and brake share brakeIntensity) to 0.2
                    var lt = root.GetComponent<ASHVehicleLights>();
                    if (lt) { lt.brakeIntensity = Mathf.Round(lt.brakeIntensity * 0.2f * 100f) / 100f; EditorUtility.SetDirty(lt); L($"{pn}: t398 brakeIntensity -> {lt.brakeIntensity:0.00} (tail = x{lt.tailFraction:0.00})"); }
                    drv.tuningVersion = 398; EditorUtility.SetDirty(drv);
                }
                if (drv.tuningVersion < 401)
                {
                    // t401: truck driver higher again; hands on the rim at 9 / 3 o'clock (palm / finger frame from the rim)
                    var veh = root.GetComponent<ASHVehicle>();
                    if (veh && veh.vehicleId == "pickup") drv.playerHeightOffset = Mathf.Max(drv.playerHeightOffset, 0.32f);
                    drv.leftGripAngle = -90f; drv.rightGripAngle = 90f; drv.leftHandEuler = drv.rightHandEuler = Vector3.zero;
                    drv.tuningVersion = 401; EditorUtility.SetDirty(drv);
                    L($"{pn}: t401 defaults: playerHeightOffset={drv.playerHeightOffset:0.00} grips -90/+90");
                }
                if (drv.tuningVersion < 405)
                {
                    // t405: hip fit now measured from the real Drive pose; keep the look Anthony tuned (car unchanged, truck +0.12 over t396)
                    if (Mathf.Abs(drv.hipHeight - 0.1f) < 0.001f) drv.hipHeight = -0.26f;
                    drv.tuningVersion = 405; EditorUtility.SetDirty(drv);
                    L($"{pn}: t405 hipHeight={drv.hipHeight:0.00} (+ playerHeightOffset {drv.playerHeightOffset:0.00})");
                }
                if (drv.tuningVersion < 411)
                {
                    // t411: brake Light objects down to 0.2 (Anthony t398); tail glow keeps its ratio (tailFraction)
                    var lt = root.GetComponent<ASHVehicleLights>();
                    if (lt) { lt.brakeIntensity = 0.2f; EditorUtility.SetDirty(lt); L($"{pn}: t411 brakeIntensity -> 0.20 (tail = x{lt.tailFraction:0.00})"); }
                    drv.tuningVersion = 411; EditorUtility.SetDirty(drv);
                }
                if (drv.tuningVersion < 412)
                {
                    if (drv.autoReachMax < 0.3f) drv.autoReachMax = 0.3f;   // t411: reach the rim at full lock
                    drv.tuningVersion = 412; EditorUtility.SetDirty(drv); L($"{pn}: t412 autoReachMax={drv.autoReachMax:0.00} handFollowMaxAngle={drv.handFollowMaxAngle:0}");
                }
                if (drv.tuningVersion < 415)
                {
                    // t415: truck pulls away quicker (+30% engine power); settle-after-repair component
                    var veh = root.GetComponent<ASHVehicle>();
                    if (veh && veh.vehicleId == "pickup") { veh.powerMultiplier = 1.3f; EditorUtility.SetDirty(veh); }
                    if (!root.GetComponent<ASHVehicleSettle>()) root.AddComponent<ASHVehicleSettle>();
                    drv.tuningVersion = 415; EditorUtility.SetDirty(drv); L($"{pn}: t415 powerMultiplier={(veh ? veh.powerMultiplier : 1f):0.00} + ASHVehicleSettle");
                }
                if (!root.GetComponent<ASHVehicleTracks>()) { root.AddComponent<ASHVehicleTracks>(); L($"{pn}: t403 added ASHVehicleTracks (packed snow ruts)"); }
                var existing = Deep(rt, "ASH_SteeringWheel");
                if (existing) { drv.steeringWheel = existing; PrefabUtility.SaveAsPrefabAsset(root, prefabPath); L($"{pn}: steering wheel already split"); return; }
                var dash = Deep(rt, dashName); var seat = Deep(rt, "ASH_Seat");
                var mf = dash ? dash.GetComponent<MeshFilter>() : null; var mr = dash ? dash.GetComponent<MeshRenderer>() : null;
                if (!mf || !mf.sharedMesh || !seat) { L($"{pn}: no dashboard mesh / seat ({dashName})"); PrefabUtility.SaveAsPrefabAsset(root, prefabPath); return; }
                var mesh = mf.sharedMesh;
                var v = mesh.vertices; var nrm = mesh.normals; var uv = mesh.uv;
                if (v == null || v.Length == 0) { L($"{pn}: dashboard mesh not readable"); return; }
                var toRoot = rt.worldToLocalMatrix * dash.localToWorldMatrix;
                var lv = v.Select(x => toRoot.MultiplyPoint3x4(x)).ToArray();
                // weld by position, union per triangle
                var weld = new Dictionary<Vector3Int, int>(); var wid = new int[v.Length];
                for (int i = 0; i < v.Length; i++) { var k = Vector3Int.RoundToInt(v[i] * 2000f); if (!weld.TryGetValue(k, out var id)) { id = weld.Count; weld[k] = id; } wid[i] = id; }
                var uf = new UF(weld.Count);
                var subs = new List<int[]>(); for (int s = 0; s < mesh.subMeshCount; s++) subs.Add(mesh.GetTriangles(s));
                foreach (var tri in subs) for (int i = 0; i < tri.Length; i += 3) { uf.U(wid[tri[i]], wid[tri[i + 1]]); uf.U(wid[tri[i]], wid[tri[i + 2]]); }
                var comps = new Dictionary<int, (Bounds b, List<int> verts)>();
                for (int i = 0; i < v.Length; i++)
                {
                    int c = uf.F(wid[i]);
                    if (!comps.TryGetValue(c, out var e)) e = (new Bounds(lv[i], Vector3.zero), new List<int>());
                    e.b.Encapsulate(lv[i]); e.verts.Add(i); comps[c] = e;
                }
                var seatL = rt.InverseTransformPoint(seat.position);
                L($"{pn}: {comps.Count} islands, seat={seatL:F2}");
                foreach (var kv in comps.OrderByDescending(k => k.Value.verts.Count).Take(14)) L($"  island {kv.Key} n={kv.Value.verts.Count} c={kv.Value.b.center:F2} s={kv.Value.b.size:F2}");
                // ring: two big extents ~0.28-0.6 m, ahead of the seat, near its x
                float Score(Bounds b)
                {
                    var s = b.size; var e = new[] { s.x, s.y, s.z }.OrderBy(x => x).ToArray();
                    if (e[2] < 0.25f || e[2] > 0.65f || e[1] < 0.2f) return -1f;
                    if (b.center.z < seatL.z || Mathf.Abs(b.center.x - seatL.x) > 0.4f) return -1f;
                    float flat = 1f - Mathf.Clamp01(e[0] / e[1]);
                    return flat * 2f - Mathf.Abs(b.center.x - seatL.x) * 2f - Mathf.Abs(e[2] - e[1]) * 3f;
                }
                var best = comps.Where(k => Score(k.Value.b) > -0.5f).OrderByDescending(k => Score(k.Value.b)).FirstOrDefault();
                if (best.Value.verts == null) { L($"{pn}: no steering wheel island found"); PrefabUtility.SaveAsPrefabAsset(root, prefabPath); return; }
                var ring = best.Value.b; float rad = Mathf.Max(ring.size.x, ring.size.y, ring.size.z) * 0.5f;
                var ringB = ring; ringB.Expand(0.03f);
                var pick = new HashSet<int>();
                foreach (var kv in comps) { var b = kv.Value.b; if (ringB.Contains(b.min) && ringB.Contains(b.max)) pick.Add(kv.Key); }
                var wv = new List<int>(); foreach (var k in pick) wv.AddRange(comps[k].verts);
                // centre = ring mean; axis = least-variance direction (PCA)
                Vector3 c0 = Vector3.zero; foreach (var i in best.Value.verts) c0 += lv[i]; c0 /= best.Value.verts.Count;
                var axis = LeastAxis(best.Value.verts.Select(i => lv[i] - c0).ToList());
                if (Vector3.Dot(axis, seatL - c0) < 0f) axis = -axis;   // points toward the driver
                float grip = 0f; foreach (var i in best.Value.verts) grip += Vector3.ProjectOnPlane(lv[i] - c0, axis).magnitude; grip /= best.Value.verts.Count;
                var up = Vector3.ProjectOnPlane(Vector3.up, axis).normalized; var pivotRot = Quaternion.LookRotation(axis, up); var inv = Quaternion.Inverse(pivotRot);
                // wheel mesh
                var map = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>();
                foreach (var i in wv) { map[i] = nv.Count; nv.Add(inv * (lv[i] - c0)); if (nrm != null && nrm.Length == v.Length) nn.Add(inv * toRoot.MultiplyVector(nrm[i]).normalized); if (uv != null && uv.Length == v.Length) nu.Add(uv[i]); }
                var wheel = new Mesh { name = pn + "_SteeringWheel" }; wheel.SetVertices(nv); if (nn.Count == nv.Count) wheel.SetNormals(nn); if (nu.Count == nv.Count) wheel.SetUVs(0, nu);
                wheel.subMeshCount = subs.Count;
                var dashM = Object.Instantiate(mesh); dashM.name = pn + "_Dashboard_NoWheel";
                int moved = 0;
                for (int s = 0; s < subs.Count; s++)
                {
                    var keep = new List<int>(); var wt = new List<int>(); var tri = subs[s];
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        if (map.ContainsKey(tri[i]) && map.ContainsKey(tri[i + 1]) && map.ContainsKey(tri[i + 2])) { wt.Add(map[tri[i]]); wt.Add(map[tri[i + 1]]); wt.Add(map[tri[i + 2]]); moved++; }
                        else { keep.Add(tri[i]); keep.Add(tri[i + 1]); keep.Add(tri[i + 2]); }
                    }
                    wheel.SetTriangles(wt, s); dashM.SetTriangles(keep, s);
                }
                wheel.RecalculateBounds(); if (nn.Count != nv.Count) wheel.RecalculateNormals(); wheel.RecalculateTangents();
                if (!AssetDatabase.IsValidFolder("Assets/_ASH/Art/Vehicles")) AssetDatabase.CreateFolder("Assets/_ASH/Art", "Vehicles");
                if (!AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.CreateFolder("Assets/_ASH/Art/Vehicles", "Generated");
                string wp = $"{GenDir}/{wheel.name}.asset", dp = $"{GenDir}/{dashM.name}.asset";
                AssetDatabase.DeleteAsset(wp); AssetDatabase.DeleteAsset(dp);
                AssetDatabase.CreateAsset(wheel, wp); AssetDatabase.CreateAsset(dashM, dp);
                mf.sharedMesh = dashM;
                var go = new GameObject("ASH_SteeringWheel"); go.transform.SetParent(rt, false); go.transform.localPosition = c0; go.transform.localRotation = pivotRot;
                go.AddComponent<MeshFilter>().sharedMesh = wheel; var wr = go.AddComponent<MeshRenderer>(); wr.sharedMaterials = mr.sharedMaterials;
                go.layer = dash.gameObject.layer;
                drv.steeringWheel = go.transform; drv.wheelAxis = Vector3.forward; drv.gripRadius = Mathf.Clamp(grip, 0.05f, 0.4f);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                L($"{pn}: steering wheel split: {pick.Count} islands, {moved} tris, centre={c0:F3} axis={axis:F2} rimRadius={rad:F3} grip={grip:F3}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>t390: the lamp mesh / slot that glows with the headlights also holds the tail lamps (they glowed white). Move the rear
        /// triangles (behind the vehicle centre) into ASH_BrakeLamps (own material instance: red, brightens on brake) and add a red point
        /// light at each rear lamp cluster (there were no rear Light objects).</summary>
        static void SplitBrakeLamps(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var rt = root.transform; string pn = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                var lights = root.GetComponent<ASHVehicleLights>(); if (!lights) { L($"{pn}: no ASHVehicleLights"); return; }
                var done = Deep(rt, "ASH_BrakeLamps");
                if (done) { L($"{pn}: brake lamps already split"); return; }
                L($"{pn}: lamp slot = {(lights.headlightEmissive.Count > 0 && lights.headlightEmissive[0].renderer ? lights.headlightEmissive[0].renderer.name + " #" + lights.headlightEmissive[0].material + " " + lights.headlightEmissive[0].renderer.sharedMaterials[lights.headlightEmissive[0].material].name : "-")}; old brake slots={lights.brakeEmissive.Count}");
                if (lights.headlightEmissive.Count == 0 || !lights.headlightEmissive[0].renderer) { L($"{pn}: no headlight lamp slot"); return; }
                var slot = lights.headlightEmissive[0]; var r = slot.renderer; var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) { L($"{pn}: lamp renderer has no mesh"); return; }
                var mesh = mf.sharedMesh; var v = mesh.vertices; var nrm = mesh.normals; var uv = mesh.uv;
                var toRoot = rt.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var tri = mesh.GetTriangles(slot.material);
                var keep = new List<int>(); var rear = new List<int>();
                for (int i = 0; i < tri.Length; i += 3)
                {
                    var c = (toRoot.MultiplyPoint3x4(v[tri[i]]) + toRoot.MultiplyPoint3x4(v[tri[i + 1]]) + toRoot.MultiplyPoint3x4(v[tri[i + 2]])) / 3f;
                    (c.z < 0f ? rear : keep).AddRange(new[] { tri[i], tri[i + 1], tri[i + 2] });
                }
                if (rear.Count == 0) { L($"{pn}: no rear lamp triangles in {r.name} slot {slot.material}"); return; }
                var map = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<int>();
                Vector3 sl = Vector3.zero, sr = Vector3.zero; int cl = 0, cr = 0;
                foreach (var i in rear)
                {
                    if (!map.TryGetValue(i, out var j)) { j = nv.Count; map[i] = j; nv.Add(v[i]); if (nrm.Length == v.Length) nn.Add(nrm[i]); if (uv.Length == v.Length) nu.Add(uv[i]); var p = toRoot.MultiplyPoint3x4(v[i]); if (p.x < 0f) { sl += p; cl++; } else { sr += p; cr++; } }
                    nt.Add(j);
                }
                var lamp = new Mesh { name = pn + "_BrakeLamps" }; lamp.SetVertices(nv); if (nn.Count == nv.Count) lamp.SetNormals(nn); if (nu.Count == nv.Count) lamp.SetUVs(0, nu);
                lamp.SetTriangles(nt, 0); lamp.RecalculateBounds(); if (nn.Count != nv.Count) lamp.RecalculateNormals(); lamp.RecalculateTangents();
                var rest = Object.Instantiate(mesh); rest.name = pn + "_" + mesh.name + "_NoRear"; rest.SetTriangles(keep, slot.material);
                if (!AssetDatabase.IsValidFolder("Assets/_ASH/Art/Vehicles")) AssetDatabase.CreateFolder("Assets/_ASH/Art", "Vehicles");
                if (!AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.CreateFolder("Assets/_ASH/Art/Vehicles", "Generated");
                string lp = $"{GenDir}/{lamp.name}.asset", rp = $"{GenDir}/{rest.name}.asset";
                AssetDatabase.DeleteAsset(lp); AssetDatabase.DeleteAsset(rp); AssetDatabase.CreateAsset(lamp, lp); AssetDatabase.CreateAsset(rest, rp);
                mf.sharedMesh = rest;
                var go = new GameObject("ASH_BrakeLamps"); go.transform.SetParent(r.transform, false); go.layer = r.gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = lamp;
                // t391: its own material (a copy), so brake emission never touches the shared lamp / body material
                var src = r.sharedMaterials[slot.material]; string mp = $"{GenDir}/{pn}_BrakeLamp.mat";
                AssetDatabase.DeleteAsset(mp); var bm = new Material(src) { name = pn + "_BrakeLamp" }; AssetDatabase.CreateAsset(bm, mp);
                go.AddComponent<MeshRenderer>().sharedMaterial = bm;
                lights.brakeEmissive = new List<ASHVehicleLights.Slot> { new ASHVehicleLights.Slot { renderer = go.GetComponent<MeshRenderer>(), material = 0 } };
                lights.brakeLights = new List<Light>();
                foreach (var (sum, n, side) in new[] { (sl, cl, "L"), (sr, cr, "R") })
                {
                    if (n == 0) continue;
                    var lg = new GameObject("ASH_BrakeLight_" + side); lg.transform.SetParent(rt, false); lg.transform.localPosition = sum / n + new Vector3(0f, 0f, -0.12f);
                    var li = lg.AddComponent<Light>(); li.type = LightType.Point; li.color = lights.brakeColor; li.range = lights.brakeRange; li.intensity = 0f; li.enabled = false; li.shadows = LightShadows.None;
                    lights.brakeLights.Add(li);
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                L($"{pn}: brake lamps split from {r.name} slot {slot.material}: {rear.Count / 3} tris, lights L={cl > 0} R={cr > 0} at {(cl > 0 ? (sl / cl).ToString("F2") : "-")} {(cr > 0 ? (sr / cr).ToString("F2") : "-")}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Vector3 LeastAxis(List<Vector3> p)
        {
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var d in p) { xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
            Vector3 M(Vector3 a) => new Vector3(xx * a.x + xy * a.y + xz * a.z, xy * a.x + yy * a.y + yz * a.z, xz * a.x + yz * a.y + zz * a.z);
            Vector3 e1 = new Vector3(1f, 0.3f, 0.2f).normalized; for (int i = 0; i < 60; i++) e1 = M(e1).normalized;
            Vector3 e2 = Vector3.Cross(e1, new Vector3(0.2f, 1f, 0.3f)).normalized;
            for (int i = 0; i < 60; i++) { e2 = M(e2); e2 = (e2 - Vector3.Dot(e2, e1) * e1).normalized; }
            return Vector3.Cross(e1, e2).normalized;
        }
    }
}
