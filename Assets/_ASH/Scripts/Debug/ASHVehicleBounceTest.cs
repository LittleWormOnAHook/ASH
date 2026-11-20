using System.Collections;
using ASH.Player;
using ASH.Vehicles;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>t375: long-drive bounce probe on a runtime 1 km plane. Logs [ASH] VEHB lines (contacts, rb state at bounce start).</summary>
    public sealed class ASHVehicleBounceTest : MonoBehaviour
    {
        public static string which = "car"; public static float driveSeconds = 35f; public static float steer = 0f; public static float throttle = 1f;
        /// <summary>t380: off by default = drive on the real terrain from where the vehicle stands. On = runtime 1 km x 1 km plane centred under it.</summary>
        public static bool usePlane = false; public static bool chainPickup;
        /// <summary>Terrain drive: car then pickup, 20 s each, gentle steer, 70 % throttle.</summary>
        public static void RunTerrainBoth() { usePlane = false; driveSeconds = 20f; steer = 0.15f; throttle = 0.7f; which = "car"; chainPickup = true; Run(); }
        public static void RunPlaneBoth() { usePlane = true; driveSeconds = 30f; steer = 0f; throttle = 1f; which = "car"; chainPickup = true; Run(); }
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_VehicleBounceTest").AddComponent<ASHVehicleBounceTest>(); }
        public static void RunPickup() { which = "pickup"; Run(); }
        static void L(string m) => Debug.Log("[ASH] VEHB " + m);
        GameObject _plane;

        // ---- t382: scripted crash test: 1 km plane, a tree-like capsule collider ahead, hit it at ~30 km/h, log 3 s after impact
        public static void RunCrash() { if (Application.isPlaying) new GameObject("ASH_VehicleCrashTest").AddComponent<ASHVehicleBounceTest>()._crash = true; }
        bool _crash;
        IEnumerator Crash()
        {
            ASHVehicle v = null; foreach (var x in ASHVehicle.All) if (x.vehicleId == "car") v = x;
            var player = ASHVehicle.PlayerRoot(null); var input = FindAnyObjectByType<ASHInput>();
            if (!v || !player || !input) { L("crash missing"); yield break; }
            var rb = v.GetComponent<Rigidbody>();
            var origin = new Vector3(v.transform.position.x, 2000f, v.transform.position.z);
            _plane = GameObject.CreatePrimitive(PrimitiveType.Cube); _plane.name = "ASH_VehicleTestPlane";
            _plane.transform.SetPositionAndRotation(origin - Vector3.up * 0.5f, Quaternion.identity); _plane.transform.localScale = new Vector3(1000f, 1f, 1000f);
            var tree = GameObject.CreatePrimitive(PrimitiveType.Capsule); tree.name = "ASH_CrashTree"; Destroy(tree.GetComponent<Collider>());
            tree.transform.position = origin + new Vector3(0f, 4f, 22f); tree.transform.localScale = new Vector3(0.7f, 4f, 0.7f);
            var cap = tree.AddComponent<CapsuleCollider>(); cap.radius = 0.5f; cap.height = 2f; // terrain-tree style capsule, static
            v.transform.SetPositionAndRotation(origin + new Vector3(0, 1.3f, 0f), Quaternion.identity);
            rb.position = v.transform.position; rb.rotation = Quaternion.identity; if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            Physics.SyncTransforms();
            yield return new WaitForSeconds(1.5f);
            var p = player.GetComponent<Rigidbody>(); var ep = v.exitPoint.position + Vector3.up * 0.1f; if (p) { p.position = ep; p.linearVelocity = Vector3.zero; } player.transform.position = ep;
            yield return new WaitForSeconds(0.8f);
            v.Enter(); yield return new WaitForSeconds(2.5f);
            var st = v.GetComponent<ASHVehicleStability>();
            L($"crash setup driving={ASHVehicle.Driving == v} stability={(bool)st} layer={LayerMask.LayerToName(v.GetComponentInChildren<Collider>().gameObject.layer)} cd={rb.collisionDetectionMode} maxDepen={rb.maxDepenetrationVelocity} maxAng={rb.maxAngularVelocity}");
            float t = 0f, impactT = -1f, peakUp = 0f, peakAng = 0f, peakTilt = 0f, impactKph = 0f, maxY = rb.position.y, startY = rb.position.y;
            while (t < 15f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                float kph = rb.linearVelocity.magnitude * 3.6f;
                if (impactT < 0f) input.SimMove = new Vector2(0f, kph < 30f ? 1f : 0.35f);
                bool hit = impactT < 0f && rb.position.z > tree.transform.position.z - 4.5f && (kph < impactKph - 5f || Vector3.Distance(new Vector3(rb.position.x, 0, rb.position.z), new Vector3(tree.transform.position.x, 0, tree.transform.position.z)) < 3f);
                if (impactT < 0f) impactKph = Mathf.Max(impactKph, kph);
                if (hit) { impactT = t; input.SimMove = new Vector2(0f, 0f); L($"IMPACT t={t:0.00} at {impactKph:0.0} kph"); }
                if (impactT >= 0f)
                {
                    peakUp = Mathf.Max(peakUp, rb.linearVelocity.y); peakAng = Mathf.Max(peakAng, rb.angularVelocity.magnitude);
                    peakTilt = Mathf.Max(peakTilt, Vector3.Angle(v.transform.up, Vector3.up)); maxY = Mathf.Max(maxY, rb.position.y);
                    if (t - impactT >= 3f) break;
                }
            }
            input.SimMove = null;
            bool pass = impactT >= 0f && peakUp < 1.5f && peakTilt < 60f;
            L($"CRASH {(pass ? "PASS" : "FAIL")} impactKph={impactKph:0.0} peakVelY={peakUp:0.00} peakAngV={peakAng:0.00} peakTilt={peakTilt:0.0} rise={maxY - startY:0.00}m endKph={rb.linearVelocity.magnitude * 3.6f:0.0} endTilt={Vector3.Angle(v.transform.up, Vector3.up):0.0} impact={(impactT >= 0f)}");
            float w = 0f; while (rb.linearVelocity.magnitude * 3.6f > 2f && w < 6f) { w += Time.deltaTime; yield return null; }
            v.Exit(); yield return new WaitForSeconds(3f);
            var cam = Invector.vCamera.vThirdPersonCamera.instance;
            L($"crash exit driving={(ASHVehicle.Driving ? "yes" : "none")} camTarget={(cam && cam.mainTarget ? cam.mainTarget.name : "-")} tpOn={(cam ? cam.enabled : false)} camDist={(Camera.main ? Vector3.Distance(Camera.main.transform.position, player.transform.position) : -1f):0.0} mode={ASHInput.VehicleMode} playerY-plane={player.transform.position.y - origin.y:0.00}");
            Destroy(tree); if (_plane) Destroy(_plane); L("done crash"); Destroy(gameObject);
        }

        // ---- t384: body-roll test on the 1 km plane: ~40 km/h, then full lock for 4 s; peak roll + wheel lift. Car, then pickup.
        public static void RunRoll() { if (Application.isPlaying) { var t = new GameObject("ASH_VehicleRollTest").AddComponent<ASHVehicleBounceTest>(); t._roll = "car"; } }
        string _roll;
        IEnumerator Roll(string id)
        {
            ASHVehicle v = null; foreach (var x in ASHVehicle.All) if (x.vehicleId == id) v = x;
            var player = ASHVehicle.PlayerRoot(null); var input = FindAnyObjectByType<ASHInput>();
            if (!v || !player || !input) { L("roll missing " + id); yield break; }
            if (ASHVehicle.Driving) { ASHVehicle.Driving.Exit(); yield return new WaitForSeconds(3f); }
            var rb = v.GetComponent<Rigidbody>(); var st = v.GetComponent<ASHVehicleStability>();
            if (id == "pickup") { var c = v.Condition; c.InstallBattery(1f); c.InstallBelt(1f); for (int i = 0; i < 4; i++) if (c.TireMissing(i)) c.InstallTire(i); v.Fuel.Refuel(40f); }
            var origin = new Vector3(v.transform.position.x, 2000f, v.transform.position.z);
            if (_plane) Destroy(_plane);
            _plane = GameObject.CreatePrimitive(PrimitiveType.Cube); _plane.name = "ASH_VehicleTestPlane";
            _plane.transform.SetPositionAndRotation(origin - Vector3.up * 0.5f, Quaternion.identity); _plane.transform.localScale = new Vector3(1000f, 1f, 1000f);
            v.transform.SetPositionAndRotation(origin + new Vector3(0, 1.3f, -150f), Quaternion.identity);
            rb.position = v.transform.position; rb.rotation = Quaternion.identity; if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            Physics.SyncTransforms(); yield return new WaitForSeconds(1.5f);
            var p = player.GetComponent<Rigidbody>(); var ep = v.exitPoint.position + Vector3.up * 0.1f; if (p) { p.position = ep; p.linearVelocity = Vector3.zero; } player.transform.position = ep;
            yield return new WaitForSeconds(0.8f);
            v.Enter(); yield return new WaitForSeconds(2.5f);
            float groundY = 0f; var wcs = v.GetComponentsInChildren<NWH.WheelController3D.WheelController>(); foreach (var w in wcs) groundY += v.transform.InverseTransformPoint(w.transform.position).y - w.spring.maxLength * 0.5f - w.wheel.tireRadius; groundY /= wcs.Length;
            L($"roll {id} driving={ASHVehicle.Driving == v} comLocal={rb.centerOfMass:F2} comAboveGround={rb.centerOfMass.y - groundY:F2} rollStiff={st.rollStiffness} spring={st.springStiffness} damper={st.damperStiffness} assist={st.rollAssist} arb={(v.Controller.axles.Count > 0 ? v.Controller.axles[0].geometry.antiRollBarForce : 0f):0}");
            float t = 0f; while (t < 10f && rb.linearVelocity.magnitude * 3.6f < 40f) { input.SimMove = new Vector2(0f, 1f); yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; }
            float entry = rb.linearVelocity.magnitude * 3.6f, peakRoll = 0f, minKph = 999f, maxKph = 0f; int minGrounded = 4, liftFrames = 0, frames = 0;
            t = 0f;
            while (t < 4f)
            {
                float kph = rb.linearVelocity.magnitude * 3.6f;
                input.SimMove = new Vector2(1f, kph < 40f ? 0.8f : 0.25f);
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; frames++;
                if (t < 0.3f) continue;
                peakRoll = Mathf.Max(peakRoll, Mathf.Abs(st.RollDegrees)); minKph = Mathf.Min(minKph, kph); maxKph = Mathf.Max(maxKph, kph);
                minGrounded = Mathf.Min(minGrounded, st.GroundedWheels); if (st.GroundedWheels < 4) liftFrames++;
            }
            input.SimMove = new Vector2(0f, -1f); yield return new WaitForSeconds(2.5f); input.SimMove = null;
            bool pass = peakRoll < 5f && minGrounded == 4;
            L($"ROLL {id} {(pass ? "PASS" : "FAIL")} entryKph={entry:0.0} speed={minKph:0}-{maxKph:0} peakRoll={peakRoll:0.0}deg minGroundedWheels={minGrounded} liftFrames={liftFrames}/{frames}");
            float w2 = 0f; while (rb.linearVelocity.magnitude * 3.6f > 2f && w2 < 6f) { w2 += Time.deltaTime; yield return null; }
            v.Exit(); yield return new WaitForSeconds(3f);
            var cam = Invector.vCamera.vThirdPersonCamera.instance;
            L($"roll {id} exit driving={(ASHVehicle.Driving ? "yes" : "none")} tpOn={(cam ? cam.enabled : false)} mode={ASHInput.VehicleMode}");
            if (_plane) Destroy(_plane);
        }

        // ---- t385: E interaction check: aim the interactor at each vehicle from its door, read the prompt, press E (Use) and see the menu open
        public static void RunInteract() { if (Application.isPlaying) { var t = new GameObject("ASH_VehicleInteractTest").AddComponent<ASHVehicleBounceTest>(); t._interact = true; } }
        bool _interact;
        IEnumerator Interact()
        {
            var player = ASHVehicle.PlayerRoot(null); var it = FindAnyObjectByType<ASH.Interaction.ASHInteractor>();
            if (!player || !it) { L("interact missing"); yield break; }
            var build = typeof(ASH.Interaction.ASHInteractor).GetMethod("BuildContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var use = typeof(ASH.Interaction.ASHInteractor).GetMethod("Use", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var v in ASHVehicle.All)
            {
                var p = player.GetComponent<Rigidbody>(); var ep = v.exitPoint.position + Vector3.up * 0.1f; if (p) { p.position = ep; p.linearVelocity = Vector3.zero; } player.transform.position = ep;
                yield return new WaitForSeconds(0.6f);
                var cam = Camera.main.transform.position; var aim = v.transform.position + Vector3.up * 0.3f;
                it.SimRay = new Ray(cam, (aim - cam).normalized);
                yield return new WaitForSeconds(0.4f);
                string tgt = it.Target is Component c ? c.name : (it.Target != null ? it.Target.GetType().Name : "none");
                bool menu = false;
                if (it.Target != null && build != null && use != null) { use.Invoke(it, new object[] { build.Invoke(it, null) }); yield return new WaitForSeconds(0.4f); menu = ASHVehicleMenu.IsOpen; }
                L($"INTERACT {v.vehicleId} {(it.Target is ASHVehicle && menu ? "PASS" : "FAIL")} target={tgt} prompt='{it.Prompt}' menuOpen={menu} bodyLayer={LayerMask.LayerToName(v.GetComponentInChildren<Collider>().gameObject.layer)}");
                ASHVehicleMenu.CloseIfOpen(); it.SimRay = null; yield return new WaitForSeconds(0.3f);
            }
        }

        IEnumerator Start()
        {
            if (_interact) { yield return Interact(); L("done interact"); Destroy(gameObject); yield break; }
            if (_roll != null) { yield return Roll("car"); yield return Roll("pickup"); L("done roll"); Destroy(gameObject); yield break; }
            if (_crash) { yield return Crash(); yield break; }
            ASHVehicle v = null, other = null;
            foreach (var x in ASHVehicle.All) { if (x.vehicleId == which) v = x; else other = x; }
            var player = ASHVehicle.PlayerRoot(null); var input = FindAnyObjectByType<ASHInput>();
            if (!v || !player || !input) { L("missing"); yield break; }
            var rb = v.GetComponent<Rigidbody>();
            if (usePlane)
            {
                // centred under the start point: 500 m of flat ground in every direction (runtime only, destroyed at the end)
                var origin = new Vector3(v.transform.position.x, 2000f, v.transform.position.z);
                _plane = GameObject.CreatePrimitive(PrimitiveType.Cube); _plane.name = "ASH_VehicleTestPlane";
                _plane.transform.SetPositionAndRotation(origin - Vector3.up * 0.5f, Quaternion.identity); _plane.transform.localScale = new Vector3(1000f, 1f, 1000f);
                var rot = Quaternion.identity; v.transform.SetPositionAndRotation(origin + new Vector3(0, 1.3f, 0f), rot);
                rb.position = v.transform.position; rb.rotation = rot; if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                Physics.SyncTransforms();
                L($"plane 1000x1000 centred at {origin:F0}");
            }
            else L($"terrain drive from {v.transform.position:F1}");
            if (which == "pickup") { var c = v.Condition; c.InstallBattery(1f); c.InstallBelt(1f); for (int i = 0; i < 4; i++) if (c.TireMissing(i)) c.InstallTire(i); v.Fuel.Refuel(40f); }
            yield return new WaitForSeconds(1.5f);
            var p = player.GetComponent<Rigidbody>(); var ep = v.exitPoint.position + Vector3.up * 0.1f; if (p) { p.position = ep; p.linearVelocity = Vector3.zero; } player.transform.position = ep;
            yield return new WaitForSeconds(1f);
            var log = v.gameObject.AddComponent<ASHContactLog>();
            L($"setup mass={rb.mass} com={rb.centerOfMass:F2} inertia={rb.inertiaTensor:F0} solver={rb.solverIterations}/{rb.solverVelocityIterations} maxDepen={rb.maxDepenetrationVelocity} fixedDt={Time.fixedDeltaTime} interp={rb.interpolation} cd={rb.collisionDetectionMode}");
            var dog = ASH.Companion.ASHDogCompanion.Active;
            if (which == "pickup" && dog && v.dogTailgate) { dog.transform.position = v.dogTailgate.position - v.transform.forward * 2f; yield return new WaitForSeconds(0.5f); }
            v.Enter(); yield return new WaitForSeconds(2.5f);
            if (which == "pickup" && dog) { float dw = 0f; while (dog.Riding && !dog.InVehicle && dw < 8f) { dw += Time.deltaTime; yield return null; } L($"dog riding={dog.Riding} inBed={dog.InVehicle} after {dw:0.0}s"); }
            L($"entered driving={ASHVehicle.Driving == v} engine={v.EngineRunning}");
            int pc = 0; foreach (var col in player.GetComponentsInChildren<Collider>(true)) if (col.enabled && col.gameObject.activeInHierarchy) { pc++; L($"player collider still ON: {col.name} trig={col.isTrigger} layer={LayerMask.LayerToName(col.gameObject.layer)}"); }
            L($"player active colliders={pc} playerRbKin={(p ? p.isKinematic : false)} detect={(p ? p.detectCollisions : false)}");
            input.SimMove = new Vector2(steer, throttle);
            float t = 0f, nextDiag = 0f; bool bounced = false; Vector3 lastCom = rb.centerOfMass; float lastMass = rb.mass;
            while (t < driveSeconds)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                if (rb.mass != lastMass || (rb.centerOfMass - lastCom).sqrMagnitude > 1e-4f) { L($"t={t:0.00} MASS/COM CHANGED mass={rb.mass} com={rb.centerOfMass:F2}"); lastMass = rb.mass; lastCom = rb.centerOfMass; }
                float tilt = Vector3.Angle(v.transform.up, Vector3.up);
                bool b = rb.angularVelocity.magnitude > 0.6f || Mathf.Abs(rb.linearVelocity.y) > 1.2f || tilt > 8f;
                if (b && !bounced) { bounced = true; L($"BOUNCE START t={t:0.00} kph={v.SpeedKph:0.0} vel={rb.linearVelocity:F2} angV={rb.angularVelocity:F2} tilt={tilt:0.0} pos={rb.position:F1} kin={rb.isKinematic} recent={log.Recent()}"); Wheels(v); }
                if (t >= nextDiag) { nextDiag += 1f; L($"t={t:0.0} kph={v.SpeedKph:0.0} y={rb.position.y:F2} tilt={tilt:0.0} vel={rb.linearVelocity:F1} angV={rb.angularVelocity:F2} gear={v.GearText} rpm={v.Controller.engine.RPM:0} kin={rb.isKinematic} hits={Hits(v)} dog={Dog(v)}"); }
            }
            input.SimMove = new Vector2(0f, -1f); yield return new WaitForSeconds(3f); input.SimMove = null;
            float w = 0f; while (v.SpeedKph > 2f && w < 8f) { w += Time.deltaTime; yield return null; }
            v.Exit(); yield return new WaitForSeconds(3f);
            var cam = Invector.vCamera.vThirdPersonCamera.instance;
            L($"exit driving={(ASHVehicle.Driving ? ASHVehicle.Driving.name : "none")} tilt={Vector3.Angle(v.transform.up, Vector3.up):0.0} angV={rb.angularVelocity.magnitude:0.00} playerY={player.transform.position.y:F2} camTarget={(cam && cam.mainTarget ? cam.mainTarget.name : "-")} tpOn={(cam ? cam.enabled : false)} mode={ASHInput.VehicleMode} contacts={log.Summary()}");
            yield return new WaitForSeconds(2f);
            L($"after exit +2s vehAngV={rb.angularVelocity.magnitude:0.00} vehVelY={rb.linearVelocity.y:0.00} playerY={player.transform.position.y:F2}");
            if (which == "pickup" && dog) L($"dog after exit riding={dog.Riding} dist={Vector3.Distance(dog.transform.position, player.transform.position):0.0} activity={dog.Activity}");
            Destroy(log); L($"done {which}"); if (_plane) Destroy(_plane); Destroy(gameObject);
            if (chainPickup && which == "car") { chainPickup = false; which = "pickup"; Run(); }
        }

        static string Hits(ASHVehicle v) { var sb = new System.Text.StringBuilder(); foreach (var w in v.GetComponentsInChildren<NWH.WheelController3D.WheelController>()) { var c = w.wheelHit.raycastHit.collider; sb.Append(c ? c.name.Substring(0, Mathf.Min(10, c.name.Length)) : "-").Append(","); } return sb.ToString(); }
        static string Dog(ASHVehicle v) { var d = ASH.Companion.ASHDogCompanion.Active; return d ? $"{Vector3.Distance(d.transform.position, v.transform.position):0.0}m {d.Activity}" : "-"; }
        static void Wheels(ASHVehicle v)
        {
            foreach (var w in v.GetComponentsInChildren<NWH.WheelController3D.WheelController>())
                L($" wheel {w.name} on={w.enabled} localPos={w.transform.localPosition:F2} hit={(w.wheelHit.raycastHit.collider ? w.wheelHit.raycastHit.collider.name + "/" + LayerMask.LayerToName(w.wheelHit.raycastHit.collider.gameObject.layer) + "/trig=" + w.wheelHit.raycastHit.collider.isTrigger : "-")}");
        }
    }

    public sealed class ASHContactLog : MonoBehaviour
    {
        readonly System.Collections.Generic.Dictionary<string, int> _n = new();
        string _recent = ""; float _recentT;
        void OnCollisionEnter(Collision c) => Rec(c, "ENTER");
        void OnCollisionStay(Collision c) => Rec(c, null);
        void Rec(Collision c, string tag)
        {
            var me = c.contactCount > 0 ? c.GetContact(0).thisCollider : null;
            string key = $"{(me ? me.name : "?")}<->{c.collider.name}({LayerMask.LayerToName(c.collider.gameObject.layer)})";
            _n.TryGetValue(key, out int k); _n[key] = k + 1;
            _recent = $"{key} imp={c.impulse.magnitude:0} relV={c.relativeVelocity.magnitude:0.0}"; _recentT = Time.time;
            if (tag != null || c.impulse.magnitude > 5000f) Debug.Log($"[ASH] VEHB contact {tag ?? "BIG"} t={Time.time:0.00} {_recent} pt={(c.contactCount > 0 ? c.GetContact(0).point.ToString("F1") : "-")}");
        }
        public string Recent() => Time.time - _recentT < 0.3f ? _recent : "none";
        public string Summary() { var sb = new System.Text.StringBuilder(); foreach (var kv in _n) sb.Append(kv.Key).Append('x').Append(kv.Value).Append("; "); return sb.ToString(); }
    }
}