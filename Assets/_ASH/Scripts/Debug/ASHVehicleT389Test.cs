using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Companion;
using ASH.Player;
using ASH.Vehicles;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>t389/t390 verify pass: seated driver + wheel + hand IK + first person, dog bed behaviours (no clipping) and passenger seat,
    /// red brake lights, exit restore; truck and car. Logs "[ASH] T389 PASS/FAIL"; side shots to %USERPROFILE%/ashx/t389_*.png.</summary>
    public sealed class ASHVehicleT389Test : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHVehicleT389Test()
        {
            _fails = default;
            _errors = default;
        }

        public static void Run() { if (Application.isPlaying) new GameObject("ASH_T389Test").AddComponent<ASHVehicleT389Test>(); }
        static void L(string m) => Debug.Log("[ASH] T389 " + m);
        static int _fails, _errors;
        static void R(string step, bool ok, string info = "") { Debug.Log($"[ASH] T389 {(ok ? "PASS" : "FAIL")} {step} {info}"); if (!ok) _fails++; }
        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }
        static void OnLog(string msg, string st, LogType t) { if ((t == LogType.Error || t == LogType.Exception || (t == LogType.Warning && (msg.Contains("Invalid Layer") || msg.Contains("GroundDetection") || msg.Contains("Prefab is null")))) && !msg.Contains("T389")) { _errors++; if (_errors <= 3) Debug.Log("[ASH] T389 console-error: " + msg.Split('\n')[0]); } }
        GameObject _player; ASHInput _input; string _dir; Vector2? _lastDog;
        static string MatCheck(ASHVehicle v)
        {
            var bl = v.Lights; if (!bl || bl.brakeEmissive.Count == 0 || !bl.brakeEmissive[0].renderer) return "no brake lamp renderer";
            var br = bl.brakeEmissive[0].renderer; var bm = br.sharedMaterial;
            int shared = 0; foreach (var r in v.GetComponentsInChildren<Renderer>(true)) if (r != br) foreach (var m in r.sharedMaterials) if (m == bm) shared++;
            return $"lamp='{br.name}' mat='{(bm ? bm.name : "-")}' usedElsewhere={shared}";
        }

        IEnumerator Start()
        {
            _fails = 0; _errors = 0;
            _dir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "ashx"); Directory.CreateDirectory(_dir);
            _player = ASHVehicle.PlayerRoot(null); _input = FindAnyObjectByType<ASHInput>();
            ASHVehicle car = null, truck = null; foreach (var v in ASHVehicle.All) { if (v.vehicleId == "car") car = v; if (v.vehicleId == "pickup") truck = v; }
            var dog = ASHDogCompanion.Active;
            if (!_player || !_input || !truck) { L("setup missing"); yield break; }
            var anim = _player.GetComponentInChildren<Animator>(); var origCtrl = anim ? anim.runtimeAnimatorController : null;
            L($"driverCtrl={(ASHVehicleDriver.DriverController ? ASHVehicleDriver.DriverController.name : "MISSING")} wheel={(truck.Driver && truck.Driver.steeringWheel ? truck.Driver.steeringWheel.name : "none")} carWheel={(car && car.Driver && car.Driver.steeringWheel ? "yes" : "no")}");
            Fix(truck);
            foreach (var v in new[] { truck, car }) if (v) {
                foreach (var l in v.GetComponentsInChildren<Light>(true)) L($"probe {v.vehicleId} light '{l.name}' type={l.type} local={v.transform.InverseTransformPoint(l.transform.position):F2} col={l.color} int={l.intensity} en={l.enabled}/{l.gameObject.activeInHierarchy}");
                foreach (var r in v.GetComponentsInChildren<MeshRenderer>(true)) { var lp = v.transform.InverseTransformPoint(r.bounds.center); if (lp.z < -0.5f || r.name.ToLower().Contains("light")) L($"probe {v.vehicleId} rend '{r.name}' local={lp:F2} mats={string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "-"))}"); }
                { var gd = FindAnyObjectByType<NWH.VehiclePhysics.GroundDetection>(); var df = v.GetComponent<ASHVehicleTracks>(); L("probe " + v.vehicleId + " brakeIntensity=" + (v.Lights ? v.Lights.brakeIntensity : -1f) + " groundDetection=" + (gd ? gd.groundEntities.Count + " entities" : "none") + " trackPoints=" + (df ? 1 : 0)); }
                if (v.Lights) L($"probe {v.vehicleId} brakeLights={v.Lights.brakeLights.Count} slots={v.Lights.BrakeSlotCount}");
            }

            // ---------- truck, dog in the bed
            yield return EnterTruck(truck, dog, false);
            var drv = truck.Driver;
            yield return new WaitForSeconds(2.5f); // let the seat fit settle
            if (anim && anim.isHuman && truck.seat) { var hp = truck.seat.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Hips).position); var hd2 = truck.seat.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Head).position); L($"pose: fit={(truck.Driver ? truck.Driver.FitCorrection : Vector3.zero):F2} hipsInSeat={hp:F2} headInSeat={hd2:F2} wheelInSeat={(drv && drv.steeringWheel ? truck.seat.InverseTransformPoint(drv.steeringWheel.position).ToString("F2") : "-")} handErr={HandErr(drv, anim):0.00}"); }
            R("truck driver seated + visible", drv && drv.Seated && _player.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.enabled), $"ctrl={(anim && anim.runtimeAnimatorController ? anim.runtimeAnimatorController.name : "-")}");
            R("dog in bed", dog && dog.InVehicle && !dog.InPassengerSeat && dog.BedCollider, dog ? $"bed={(dog.BedCollider ? dog.BedCollider.name : "none")}" : "no dog");
            Shot(truck, "t389_truck_driver_side", new Vector3(-2.6f, 1.5f, 0.6f), DriverLook(truck));
            Shot(truck, "t389_truck_driver_front", new Vector3(-0.3f, 1.6f, 3.2f), DriverLook(truck));
            float maxDogMove = 0f; bool sawBrakeD = false; Color bcD = default; float biD = 0f; float minClear = 9f, maxOut = 0f, maxWheel = 0f, maxHand = 0f; int samples = 0;
            var acts = new[] { ASHDogCompanion.BedAct.Stand, ASHDogCompanion.BedAct.Sit, ASHDogCompanion.BedAct.Lie, ASHDogCompanion.BedAct.Stand, ASHDogCompanion.BedAct.Lie };
            float sd = 0f; yield return SteerDir(truck, _input, x => sd = x);
            R("truck wheel turns the right way (steer right -> rim top right)", sd > 0.1f, $"side={sd:0.00} invertWheel={(drv ? drv.invertWheel : false)}");
            var seen = new HashSet<string>();
            foreach (var act in acts)
            {
                ASHDogCompanion.ForceBedAct = act;
                float t = 0f;
                while (t < 7f)
                {
                    _input.SimMove = new Vector2(Mathf.Sin(t * 1.3f) * 0.9f, t < 5f ? 0.45f : -1f);
                    yield return new WaitForEndOfFrame(); t += Time.deltaTime;
                    if (dog && dog.InVehicle && dog.BedCollider) { var lp0 = truck.transform.InverseTransformPoint(dog.transform.position); if (_lastDog.HasValue) maxDogMove = Mathf.Max(maxDogMove, Vector2.Distance(new Vector2(lp0.x, lp0.z), _lastDog.Value) / Mathf.Max(1e-4f, Time.deltaTime)); _lastDog = new Vector2(lp0.x, lp0.z); BedCheck(dog, out var cl, out var outv); minClear = Mathf.Min(minClear, cl); maxOut = Mathf.Max(maxOut, outv); samples++; seen.Add(dog.CurrentBedAct.ToString()); }
                    { var bl0 = truck.Lights; if (bl0 && bl0.Braking) { sawBrakeD = true; var l0 = bl0.brakeLights.FirstOrDefault(x => x); if (l0 && l0.intensity > biD) { bcD = l0.color; biD = l0.intensity; } } }
                    if (drv) { maxWheel = Mathf.Max(maxWheel, Mathf.Abs(drv.SteerAngle)); maxHand = Mathf.Max(maxHand, HandErr(drv, anim)); }
                }
                if (act != ASHDogCompanion.BedAct.Stand) Shot(truck, "t389_dog_" + act + seen.Count, new Vector3(-2.8f, 2.2f, -1.2f), truck.transform.TransformPoint(new Vector3(0f, 0.9f, -1.3f)));
            }
            _input.SimMove = new Vector2(0f, -1f);
            float bt = 0f; bool sawBrake = false; Color bc = default; float bi = 0f;
            while (bt < 1.5f) { bt += Time.deltaTime; yield return null; var bl = truck.Lights; if (bl && bl.Braking) { sawBrake = true; var l = bl.brakeLights.FirstOrDefault(x => x); if (l) { bc = l.color; bi = l.intensity; } } }
            _input.SimMove = null;
            if (!sawBrake && sawBrakeD) { sawBrake = true; bc = bcD; bi = biD; }
            R("dog bed poses cycle (stand/sit/lie only)", seen.Count == 3, string.Join(",", seen));
            R("dog never clips the bed", samples > 50 && minClear > -0.03f && maxOut < 0.05f, $"samples={samples} minClearance={minClear:0.000}m maxOutside={maxOut:0.000}m");
            R("dog does not walk around the bed", maxDogMove < 0.35f, $"maxSlide={maxDogMove:0.00}m/s");
            R("steering wheel turns, max 90 each way", drv && drv.steeringWheel && maxWheel > 30f && maxWheel <= drv.wheelMaxAngle + 0.5f && drv.wheelMaxAngle <= 90.01f, $"maxAngle={maxWheel:0} limit={(drv ? drv.wheelMaxAngle : 0f):0}");
            R("hands on wheel (IK, within 0.05 m through +-90)", maxHand < 0.05f, $"maxHandErr={maxHand:0.000}m reach={(drv ? drv.ReachUsed : 0f):0.00}m");
            R("truck brake lights red + brighter", sawBrake && bc.r > 0.8f && bc.g < 0.2f && bi >= truck.Lights.brakeIntensity * 0.99f, $"color={bc} intensity={bi:0.0} lampSlots={truck.Lights.BrakeSlotCount} {MatCheck(truck)}");
            { var tr = truck.GetComponent<ASHVehicleTracks>(); var acc = ASH.World.Accumulation.ASHAccumulationController.Instance;
              R("truck leaves packed snow ruts", tr && acc && tr.Stamped > 0 && tr.SegmentCount > 0 && tr.packedDepth < 1f, tr ? $"stamps={tr.Stamped} heldSegments={tr.SegmentCount} rutWidth={tr.rutWidth:0.00} packedDepth={tr.packedDepth:0.00} persist={tr.persistSeconds:0}s field={(acc && acc.Field ? "yes" : "no")}" : "no ASHVehicleTracks");
              if (drv) { yield return new WaitForEndOfFrame(); R("player locked to the seat over bumps (t409)", drv.SeatError < 0.005f, $"seatErrAtFrameEnd={drv.SeatError:0.000}m driftCorrectedMax={drv.MaxSeatDrift:0.000}m lockToSeat={drv.lockToSeat}"); }
              R("NWH GroundDetection present (no skidmark warning)", FindAnyObjectByType<NWH.VehiclePhysics.GroundDetection>() != null, ""); }
            float w = 0f; while (truck.SpeedKph > 2f && w < 8f) { w += Time.deltaTime; yield return null; }
            // first person
            // t410: first person off (Tab does nothing) + 3-step wheel zoom (close / mid / far)
            truck.Cam.ToggleView(); yield return null;
            R("first-person view disabled (Tab stays 3rd person)", !truck.Cam.enable1stPerson && truck.Cam.view == ASHVehicleCamera.View.ThirdPerson, $"enable1stPerson={truck.Cam.enable1stPerson}");
            { var zs = truck.Cam.zoomSteps; var got = new List<string>(); bool ok = zs != null && zs.Length == 3;
              foreach (var raw in new[] { 120f, 120f, -120f, -120f, -120f })
              { _input.SimZoomRaw = raw; float zw = 0f; while (zw < 1.6f) { zw += Time.deltaTime; yield return null; }
                float want = zs[truck.Cam.ZoomStep]; got.Add($"{truck.Cam.ZoomStep}:{truck.Cam.CurrentDistance:0.0}");
                ok &= Mathf.Abs(truck.Cam.CurrentDistance - want) < 0.3f; }
              ok &= truck.Cam.ZoomStep == 2;
              R("3-step wheel zoom (in,in,out,out,out)", ok, $"steps={string.Join(" ", got)} distances={string.Join("/", zs)}"); }
            ShotMain("t389_truck_thirdperson");
            yield return ExitCheck(truck, anim, origCtrl, "truck");
            yield return new WaitForSeconds(2f);
            R("dog out of truck", dog && !dog.Riding);

            // ---------- truck, dog in the passenger seat
            yield return EnterTruck(truck, dog, true);
            { float pw = 0f; while (dog && !dog.InPassengerSeat && pw < 12f) { pw += Time.deltaTime; yield return null; } yield return new WaitForSeconds(0.5f); }
            R("dog in passenger seat", dog && dog.InPassengerSeat, dog && truck.passengerSeat ? $"dist={Vector3.Distance(dog.transform.position, truck.passengerSeat.position):0.00} doorDist={(truck.passengerDoor ? Vector3.Distance(dog.transform.position, truck.passengerDoor.position) : -1f):0.00} inVehicle={dog.InVehicle} riding={dog.Riding} activity={dog.Activity}" : "");
            Shot(truck, "t389_dog_passenger", new Vector3(2.6f, 1.6f, 0.7f), truck.passengerSeat ? truck.passengerSeat.position + truck.transform.up * 0.5f : truck.transform.position);
            yield return DriveFew(truck, 4f);
            R("dog stays in passenger seat while driving", dog && dog.InPassengerSeat && truck.passengerSeat && Vector3.Distance(dog.transform.position, truck.passengerSeat.position) < 0.1f);
            yield return ExitCheck(truck, anim, origCtrl, "truck(passenger)");
            yield return new WaitForSeconds(2f);
            R("dog out of passenger side", dog && !dog.Riding);

            // ---------- car
            if (car)
            {
                Fix(car); Put(car); yield return new WaitForSeconds(0.5f);
                car.Enter(false); float t = 0f; while ((ASHVehicle.Driving != car || !car.Driver || !car.Driver.Seated) && t < 6f) { t += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(2f);
                yield return new WaitForSeconds(2.5f);
                if (anim && anim.isHuman && car.seat) L($"car pose: fit={(car.Driver ? car.Driver.FitCorrection : Vector3.zero):F2} hipsInSeat={car.seat.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Hips).position):F2} headInSeat={car.seat.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Head).position):F2} handErr={HandErr(car.Driver, anim):0.00}");
                R("car driver seated", car.Driver && car.Driver.Seated, $"wheel={(car.Driver && car.Driver.steeringWheel ? "yes" : "no")}");
                R("dog does not ride the car", !dog || !dog.Riding);
                float cs = 0f; yield return SteerDir(car, _input, x => cs = x);
                R("car wheel turns the right way (steer right -> rim top right)", cs > 0.1f, $"side={cs:0.00} invertWheel={(car.Driver ? car.Driver.invertWheel : false)}");
                Shot(car, "t389_car_driver_side", new Vector3(-2.4f, 1.4f, 0.4f), DriverLook(car));
                float mw = 0f; bool br = false; Color c2 = default; t = 0f;
                while (t < 6f) { _input.SimMove = new Vector2(Mathf.Sin(t * 1.5f), t < 4f ? 0.45f : -1f); yield return null; t += Time.deltaTime; if (car.Driver) mw = Mathf.Max(mw, Mathf.Abs(car.Driver.SteerAngle)); if (car.Lights && car.Lights.Braking) { br = true; var l = car.Lights.brakeLights.FirstOrDefault(x => x); if (l) c2 = l.color; } }
                _input.SimMove = null;
                R("car steering wheel turns", !car.Driver.steeringWheel || mw > 30f, $"maxAngle={mw:0}");
                R("car brake lights red", br && c2.r > 0.8f && c2.g < 0.2f, $"color={c2} lampSlots={car.Lights.BrakeSlotCount} {MatCheck(car)}");
                R("car brake lamp has its own material", MatCheck(car).Contains("usedElsewhere=0"));
                w = 0f; while (car.SpeedKph > 2f && w < 8f) { w += Time.deltaTime; yield return null; }
                yield return ExitCheck(car, anim, origCtrl, "car");
            }
            L($"DONE fails={_fails} consoleErrors={_errors}");
            Destroy(gameObject);
        }

        Vector3 DriverLook(ASHVehicle v) => v.seat ? v.seat.position + v.transform.up * 0.6f : v.transform.position;

        void Fix(ASHVehicle v)
        {
            var c = v.Condition; if (c) { c.batteryInstalled = true; c.batteryCharge = 1f; c.beltInstalled = true; c.beltCondition = 1f; c.SetMissingMask(0); }
            if (v.Fuel) v.Fuel.litres = v.Fuel.capacityLitres;
        }
        void Put(ASHVehicle v)
        {
            var p = v.exitPoint ? v.exitPoint.position : v.transform.position - v.transform.right * 2f;
            var rb = _player.GetComponent<Rigidbody>(); if (rb) { rb.position = p; rb.linearVelocity = Vector3.zero; }
            _player.transform.position = p; Physics.SyncTransforms();
        }
        IEnumerator EnterTruck(ASHVehicle truck, ASHDogCompanion dog, bool passenger)
        {
            Put(truck);
            if (dog && truck.dogTailgate) { var dp = truck.dogTailgate.position - truck.transform.forward * 1.5f; dog.transform.position = dp; }
            yield return new WaitForSeconds(1f);
            ASHDogCompanion.ForcePassenger = passenger;
            truck.Enter(false);
            float t = 0f; while ((ASHVehicle.Driving != truck || !truck.Driver || !truck.Driver.Seated) && t < 6f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(2.5f);
            t = 0f; while (dog && dog.Riding && !dog.InVehicle && t < 12f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.5f);
        }
        IEnumerator DriveFew(ASHVehicle v, float s)
        {
            float t = 0f; while (t < s) { _input.SimMove = new Vector2(0.5f, t < s - 1.5f ? 0.45f : -1f); yield return null; t += Time.deltaTime; }
            _input.SimMove = null; float w = 0f; while (v.SpeedKph > 2f && w < 8f) { w += Time.deltaTime; yield return null; }
        }
        IEnumerator ExitCheck(ASHVehicle v, Animator anim, RuntimeAnimatorController orig, string n)
        {
            v.Exit(); float t = 0f; while (ASHVehicle.Driving == v && t < 6f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.5f);
            var cam = Invector.vCamera.vThirdPersonCamera.instance;
            bool rend = _player.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.enabled);
            var head = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            R($"{n} exit restores", ASHVehicle.Driving == null && anim && anim.enabled && anim.runtimeAnimatorController == orig && rend && !_player.transform.IsChildOf(v.transform)
                && cam && cam.enabled && (!head || head.localScale.x > 0.5f), $"ctrl={(anim && anim.runtimeAnimatorController ? anim.runtimeAnimatorController.name : "-")} parent={(_player.transform.parent ? _player.transform.parent.name : "root")} camOn={(cam && cam.enabled)}");
        }

        static void BedCheck(ASHDogCompanion dog, out float clear, out float outside)
        {
            var bed = dog.BedCollider; var veh = bed.GetComponentInParent<Rigidbody>().transform; var up = veh.up;
            clear = 9f; outside = 0f;
            var tr = dog.transform; var p = tr.position;
            // feet origin + the body ends (front / back at the capsule height)
            foreach (var q in new[] { p, p + tr.forward * 0.4f, p - tr.forward * 0.4f })
            {
                if (bed.Raycast(new Ray(q + up * 1.5f, -up), out var h, 3f)) { if (q == p) clear = Mathf.Min(clear, Vector3.Dot(q - h.point, up)); }
                else if (q == p) outside = Mathf.Max(outside, 1f);
                var b = bed.bounds; var l = veh.InverseTransformPoint(q); var bl = veh.InverseTransformPoint(b.center);
                var bx = bed is BoxCollider box ? Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f : b.extents;
                outside = Mathf.Max(outside, Mathf.Max(0f, Mathf.Abs(l.x - bl.x) - bx.x), Mathf.Max(0f, Mathf.Abs(l.z - bl.z) - bx.z));
            }
        }
        /// <summary>Steering right should turn the rim's top to the driver's right (clockwise as the driver sees it). +1 right, -1 wrong.</summary>
        static IEnumerator SteerDir(ASHVehicle v, ASHInput input, System.Action<float> done)
        {
            var w = v.Driver ? v.Driver.steeringWheel : null; if (!w) { done(0f); yield break; }
            input.SimMove = new Vector2(1f, 0f); float t = 0f; while (t < 1.2f) { t += Time.deltaTime; yield return null; }
            var top = Vector3.ProjectOnPlane(w.up, v.transform.forward); float side = Vector3.Dot(top.normalized, v.transform.right);
            input.SimMove = null; t = 0f; while (t < 1f) { t += Time.deltaTime; yield return null; }
            done(side);
        }
        static float HandErr(ASHVehicleDriver d, Animator a)
        {
            if (!a || !a.isHuman || !d.Seated || !d.handIK) return 0f;
            if (d.exactArmIK) return d.LastHandErr; // measured after the LateUpdate arm solve (what is rendered)
            float e = 0f;
            foreach (var left in new[] { true, false })
                if (d.HandTarget(left, out var p, out _)) e = Mathf.Max(e, Vector3.Distance(a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position, p));
            return e;
        }

        void Shot(ASHVehicle v, string name, Vector3 localCam, Vector3 lookAt)
        {
            var go = new GameObject("ASH_T389ShotCam"); var c = go.AddComponent<Camera>(); c.fieldOfView = 50f; c.nearClipPlane = 0.03f; c.enabled = false;
            go.transform.position = v.transform.TransformPoint(localCam); go.transform.LookAt(lookAt);
            Render(c, name); Destroy(go);
        }
        void ShotMain(string name) { var c = Camera.main; if (c) Render(c, name); }
        void Render(Camera c, string name)
        {
            var rt = new RenderTexture(960, 540, 24); var prev = c.targetTexture; c.targetTexture = rt; c.Render(); c.targetTexture = prev;
            var a = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(960, 540, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = a;
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG()); Destroy(tex); rt.Release(); Destroy(rt);
        }
    }
}
