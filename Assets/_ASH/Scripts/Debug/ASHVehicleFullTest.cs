using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using ASH.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace ASH.DebugTools
{
    /// <summary>t388: one combined terrain pass through the real E / menu paths. Logs "[ASH] VEHF" PASS/FAIL per step.</summary>
    public sealed class ASHVehicleFullTest : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHVehicleFullTest()
        {
            _fails = default;
            _errors = default;
        }

        public static void Run() { if (Application.isPlaying) new GameObject("ASH_VehicleFullTest").AddComponent<ASHVehicleFullTest>(); }
        static void L(string m) => Debug.Log("[ASH] VEHF " + m);
        static void R(string step, bool ok, string info = "") { Debug.Log($"[ASH] VEHF {(ok ? "PASS" : "FAIL")} {step} {info}"); if (!ok) _fails++; }
        static int _fails, _errors;
        GameObject _player; ASHInteractor _it; ASHInventory _inv; ASHInput _input;
        System.Reflection.MethodInfo _build, _use;

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }
        static void OnLog(string msg, string st, LogType t) { if ((t == LogType.Error || t == LogType.Exception || t == LogType.Assert || (t == LogType.Warning && msg.Contains("referenced script"))) && !msg.Contains("VEHF")) { _errors++; Debug.Log("[ASH] VEHF console-error: " + msg.Split('\n')[0]); } }

        IEnumerator Start()
        {
            _fails = 0; _errors = 0;
            _player = ASHVehicle.PlayerRoot(null); _it = FindAnyObjectByType<ASHInteractor>(); _inv = FindAnyObjectByType<ASHInventory>(); _input = FindAnyObjectByType<ASHInput>();
            _build = typeof(ASHInteractor).GetMethod("BuildContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            _use = typeof(ASHInteractor).GetMethod("Use", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ASHVehicle car = null, truck = null; foreach (var v in ASHVehicle.All) { if (v.vehicleId == "car") car = v; if (v.vehicleId == "pickup") truck = v; }
            if (!_player || !_it || !_inv || !_input || !car || !truck || _use == null) { L("setup missing"); yield break; }

            // ---------------- CAR
            yield return UseAt(car.exitPoint.position, car.transform, car.transform.position + Vector3.up * 0.4f);
            R("car E prompt", _lastTarget is ASHVehicle lv && lv == car, $"prompt='{_lastPrompt}'");
            R("car E opens menu", ASHVehicleMenu.IsOpen);
            var door = car.driverDoor; float doorMax = 0f;
            Click("DRIVE");
            float t = 0f; while (t < 3f) { t += Time.deltaTime; if (door) doorMax = Mathf.Max(doorMax, door.Open01); yield return null; }
            R("car enter (Drive)", ASHVehicle.Driving == car, $"engine={car.EngineRunning}");
            R("car door opened + closed", !door || (doorMax > 0.9f && door.IsClosed), $"maxOpen={doorMax:0.00}");
            R("car lights on at enter", car.LightsOn);
            car.Lights.Toggle(); yield return null; bool off = !car.LightsOn; car.Lights.Toggle(); yield return null;
            R("car L toggles lights", off && car.LightsOn);
            R("car gas gauge HUD", ASHVehicleHud.Visible, $"fuel={car.Fuel.litres:0.0}L");
            yield return Drive(car, 15f, 0.55f);
            yield return ExitAndCheck(car, "car");
            R("car lights off after exit", !car.LightsOn);

            // ---------------- TRUCK
            yield return UseAt(truck.exitPoint.position, truck.transform, truck.transform.position + Vector3.up * 0.4f);
            R("truck E opens menu", ASHVehicleMenu.IsOpen, $"why='{truck.Condition.WhyNoStart(truck.Fuel)}'");
            ASHVehicleMenu.CloseIfOpen(); yield return null;
            var c = truck.Condition;
            var group = GameObject.Find(ASHVehicleItemsNames.Group);
            var tires = Object.FindObjectsByType<ASHItemPickup>(FindObjectsInactive.Exclude).Where(p => p.name.ToLowerInvariant().StartsWith("tire replacement")).ToList();
            var parts = Object.FindObjectsByType<ASHItemPickup>(FindObjectsInactive.Exclude).Where(p => group && p.transform.IsChildOf(group.transform)).ToList();
            L($"pickups: parts={parts.Count} ({string.Join(",", parts.Select(p => p.name))}) tires={tires.Count} at {string.Join(" ", tires.Select(x => x.transform.position.ToString("F2")))}");
            int b0 = _inv.Count(c.BatteryItem), f0 = _inv.Count(c.BeltItem), t0 = _inv.Count(c.TireItem); float l0 = ASHFuelCan.TotalLitres(_inv);
            foreach (var p in parts.Concat(tires).ToList()) { if (!p) continue; var tr = p.transform; var col = p.GetComponentInChildren<Collider>(); yield return UseAt(tr.position, tr, col ? col.bounds.center : tr.position); }
            R("pick up battery", _inv.Count(c.BatteryItem) == b0 + 1); R("pick up fan belt", _inv.Count(c.BeltItem) == f0 + 1);
            R("pick up both tires", _inv.Count(c.TireItem) == t0 + 2); R("pick up fuel can", ASHFuelCan.TotalLitres(_inv) > l0 + 0.5f, $"can={ASHFuelCan.TotalLitres(_inv):0.0}L");

            yield return UseAt(truck.exitPoint.position, truck.transform, truck.transform.position + Vector3.up * 0.4f);
            R("truck menu reopens", ASHVehicleMenu.IsOpen);
            for (int i = 0; i < 6; i++) { if (!Click("REPAIR")) break; yield return new WaitForSeconds(0.25f); }
            R("repair battery", c.batteryInstalled && c.BatteryOk); R("repair fan belt", c.beltInstalled && c.BeltOk); R("fit both tires", c.MissingTireCount == 0);
            float fuel0 = truck.Fuel.litres; Click("FUEL UP"); yield return null; yield return null;
            t = 0f; while (t < 15f && (FindLabel("STOP") != null)) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.2f);
            R("fuel up", truck.Fuel.litres > fuel0 + 0.5f, $"tank={truck.Fuel.litres:0.0}L poured={truck.Fuel.litres - fuel0:0.0} canLeft={ASHFuelCan.TotalLitres(_inv):0.0}");
            R("truck ready to start", c.WhyNoStart(truck.Fuel) == null, c.WhyNoStart(truck.Fuel) ?? "");
            var dog = ASH.Companion.ASHDogCompanion.Active;
            if (dog && Vector3.Distance(dog.transform.position, truck.transform.position) > truck.dogRideRange * 0.8f) dog.TeleportNearPlayer();
            yield return new WaitForSeconds(0.5f);
            door = truck.driverDoor; doorMax = 0f;
            if (!ASHVehicleMenu.IsOpen) yield return UseAt(truck.exitPoint.position, truck.transform, truck.transform.position + Vector3.up * 0.4f);
            Click("DRIVE");
            t = 0f; while (t < 3f) { t += Time.deltaTime; if (door) doorMax = Mathf.Max(doorMax, door.Open01); yield return null; }
            R("truck enter (Drive)", ASHVehicle.Driving == truck, $"engine={truck.EngineRunning}");
            R("truck door opened + closed", !door || (doorMax > 0.9f && door.IsClosed), $"maxOpen={doorMax:0.00}");
            R("truck lights on at enter", truck.LightsOn, "(needs the battery fitted)"); R("truck gas gauge HUD", ASHVehicleHud.Visible);
            t = 0f; while (dog && dog.Riding && !dog.InVehicle && t < 10f) { t += Time.deltaTime; yield return null; }
            R("dog jumps in bed", dog && dog.InVehicle, dog ? $"after {t:0.0}s" : "no dog");
            float fu = truck.Fuel.litres;
            yield return Drive(truck, 15f, 0.5f);
            R("dog rides along", dog && dog.InVehicle && Vector3.Distance(dog.transform.position, truck.dogSeat.position) < 0.6f, dog ? $"seatDist={Vector3.Distance(dog.transform.position, truck.dogSeat.position):0.00}" : "");
            R("gas gauge drops", truck.Fuel.litres < fu, $"{fu:0.00}->{truck.Fuel.litres:0.00}L");
            yield return ExitAndCheck(truck, "truck");
            t = 0f; while (dog && dog.Riding && t < 4f) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.8f);
            R("dog jumps out", dog && !dog.Riding && !dog.InVehicle, dog ? $"dogToPlayer={Vector3.Distance(dog.transform.position, _player.transform.position):0.0}m" : "");
            R("truck lights off after exit", !truck.LightsOn);
            R("console clean (no errors / missing scripts)", _errors == 0, $"errors={_errors}");
            L($"done fails={_fails}");
            Destroy(gameObject);
        }

        object _lastTarget; string _lastPrompt;
        IEnumerator UseAt(Vector3 stand, Transform target, Vector3 aim)
        {
            var dir = Vector3.ProjectOnPlane(stand - aim, Vector3.up); if (dir.sqrMagnitude < 0.01f) dir = Vector3.back; dir.Normalize();
            var pos = stand; if (Vector3.Distance(Vector3.ProjectOnPlane(stand - aim, Vector3.up), Vector3.zero) < 0.5f || target.GetComponent<ASHItemPickup>()) pos = aim + dir * 1.3f;
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var h, 6f, ~(1 << 2), QueryTriggerInteraction.Ignore)) pos = h.point;
            var rb = _player.GetComponent<Rigidbody>(); if (rb) { rb.position = pos + Vector3.up * 0.05f; rb.linearVelocity = Vector3.zero; } _player.transform.position = pos + Vector3.up * 0.05f;
            yield return new WaitForSeconds(0.5f);
            var eye = _player.transform.position + Vector3.up * 1.6f;
            _it.SimRay = new Ray(eye, (aim - eye).normalized);
            yield return new WaitForSeconds(0.35f);
            _lastTarget = _it.Target; _lastPrompt = _it.Prompt;
            if (_it.Target != null) { _use.Invoke(_it, new object[] { _build.Invoke(_it, null) }); }
            else L($"no target near {target.name}");
            yield return new WaitForSeconds(0.4f);
            _it.SimRay = null;
        }

        IEnumerator Drive(ASHVehicle v, float seconds, float throttle)
        {
            var rb = v.GetComponent<Rigidbody>(); var st = v.GetComponent<ASHVehicleStability>();
            float t = 0f, maxK = 0f, peakUp = 0f, peakTilt = 0f, peakRoll = 0f; var p0 = rb.position;
            while (t < seconds)
            {
                _input.SimMove = new Vector2(t > seconds * 0.5f ? 0.6f : 0f, throttle);
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                maxK = Mathf.Max(maxK, v.SpeedKph); peakUp = Mathf.Max(peakUp, rb.linearVelocity.y); peakTilt = Mathf.Max(peakTilt, Vector3.Angle(v.transform.up, Vector3.up));
                if (st) peakRoll = Mathf.Max(peakRoll, Mathf.Abs(st.RollDegrees));
            }
            _input.SimMove = new Vector2(0f, -1f); yield return new WaitForSeconds(2.5f); _input.SimMove = null;
            float w = 0f; while (v.SpeedKph > 2f && w < 6f) { w += Time.deltaTime; yield return null; }
            R($"{v.vehicleId} drive {seconds:0}s (no launch / flip)", maxK > 8f && peakUp < 3f && peakTilt < 50f, $"dist={Vector3.Distance(p0, rb.position):0}m maxKph={maxK:0} peakVelY={peakUp:0.0} peakTilt={peakTilt:0} peakRoll={peakRoll:0.0}");
        }

        IEnumerator ExitAndCheck(ASHVehicle v, string n)
        {
            v.Exit(); yield return new WaitForSeconds(3f);
            var cam = Invector.vCamera.vThirdPersonCamera.instance;
            float cd = Camera.main ? Vector3.Distance(Camera.main.transform.position, _player.transform.position) : -1f;
            R($"{n} exit", ASHVehicle.Driving == null && !ASHInput.VehicleMode, $"playerToVehicle={Vector3.Distance(_player.transform.position, v.transform.position):0.0}m");
            R($"{n} camera follows player", cam && cam.enabled && cam.mainTarget && cam.mainTarget.gameObject == _player && cd < 12f, $"camDist={cd:0.0}");
        }

        static Text FindLabel(string label)
        {
            var root = GameObject.Find("ASH_VehicleMenu"); if (!root) return null;
            foreach (var tx in root.GetComponentsInChildren<Text>(false)) if (tx.text.Contains(label) && tx.GetComponentInParent<Button>()) return tx;
            return null;
        }
        static bool Click(string label)
        {
            var tx = FindLabel(label); if (!tx) { L($"no '{label}' button"); return false; }
            var b = tx.GetComponentInParent<Button>(); if (!b) { L($"'{label}' has no Button"); return false; }
            b.onClick.Invoke(); return true;
        }
    }
    static class ASHVehicleItemsNames { public const string Group = "ASH_VehicleItems_Cabin"; }
}