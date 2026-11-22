using System.Collections;
using ASH.Inventory;
using ASH.Player;
using ASH.Vehicles;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Vehicle smoke test (Play): drives the car, repairs + fuels + drives the pickup with the dog, logs [ASH] VEHX lines and
    /// writes screenshots to C:\Users\Teabagger\ashx\veh_*.png.</summary>
    public sealed class ASHVehicleTest : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHVehicleTest()
        {
            usePlane = false;
        }

        public static void Run() { if (Application.isPlaying) new GameObject("ASH_VehicleTest").AddComponent<ASHVehicleTest>(); }
        static void L(string m) => Debug.Log("[ASH] VEHX " + m);
        public static bool usePlane = false; // t380: terrain by default
        const string Dir = @"C:\Users\Teabagger\ashx\";

        IEnumerator Start()
        {
            ASHVehicle car = null, pickup = null;
            foreach (var v in ASHVehicle.All) { if (v.vehicleId == "car") car = v; if (v.vehicleId == "pickup") pickup = v; }
            var player = ASHVehicle.PlayerRoot(null); var inv = FindAnyObjectByType<ASHInventory>(); var input = FindAnyObjectByType<ASHInput>();
            L($"found car={(bool)car} pickup={(bool)pickup} player={(bool)player} inv={(bool)inv}");
            if (!car || !pickup || !player) yield break;
            // t372/t373: runtime-only 1 km x 1 km flat test plane high above the map; vehicles moved onto it for this Play session only
            if (usePlane) {
            var origin = new Vector3(car.transform.position.x, 2000f, car.transform.position.z);
            _plane = GameObject.CreatePrimitive(PrimitiveType.Cube); _plane.name = "ASH_VehicleTestPlane";
            _plane.transform.SetPositionAndRotation(origin - Vector3.up * 0.5f, Quaternion.identity); _plane.transform.localScale = new Vector3(1000f, 1f, 1000f);
            var pr = _plane.GetComponent<Renderer>(); pr.material.color = new Color(0.45f, 0.45f, 0.42f);
            PutOn(car, origin); PutOn(pickup, origin + new Vector3(25f, 0f, 0f));
            yield return new WaitForSeconds(1.5f);
            L($"plane at y={origin.y} car y={car.transform.position.y:F2} pickup y={pickup.transform.position.y:F2}"); }
            L($"car why={car.Condition.WhyNoStart(car.Fuel) ?? "ok"} fuel={car.Fuel.litres:0.0} | pickup why={pickup.Condition.WhyNoStart(pickup.Fuel) ?? "ok"} fuel={pickup.Fuel.litres:0.0} tiresMissing={pickup.Condition.MissingTireCount} lightsOn={car.LightsOn}/{pickup.LightsOn}");
            int on = 0; foreach (var l in pickup.Lights.headLights) if (l && l.enabled) on++; L($"pickup headlights enabled at load: {on}/{pickup.Lights.headLights.Count}");

            // ---- car
            Teleport(player, car.exitPoint.position);
            yield return new WaitForSeconds(1f);
            car.Enter();
            yield return new WaitForSeconds(2.5f);
            L($"car entered driving={ASHVehicle.Driving == car} engine={car.EngineRunning} lights={car.LightsOn} vehicleMode={ASHInput.VehicleMode} hud={ASHVehicleHud.Visible} playerParent={(player.transform.parent ? player.transform.parent.name : "-")}");
            ScreenCapture.CaptureScreenshot(Dir + "veh_car_3rd.png");
            input.SimMove = new Vector2(0f, 1f);
            float f0 = car.Fuel.litres;
            for (int i = 0; i < 5; i++) { yield return new WaitForSeconds(2f); Diag("car", car); }
            L($"car throttle 4s speed={car.SpeedKph:0.0} gear={car.GearText} fuel used={f0 - car.Fuel.litres:0.000} L");
            input.SimMove = new Vector2(0f, -1f); yield return new WaitForSeconds(0.5f);
            L($"car braking brakeLights={car.Lights.Braking}");
            car.Cam.ToggleView(); yield return new WaitForSeconds(0.6f);
            ScreenCapture.CaptureScreenshot(Dir + "veh_car_1st.png");
            car.Cam.ToggleView();
            yield return new WaitForSeconds(3f); input.SimMove = null;
            float t = 0f; while (car.SpeedKph > 2f && t < 8f) { t += Time.deltaTime; yield return null; }
            car.Exit(); yield return new WaitForSeconds(2.5f);
            L($"car exit driving={(ASHVehicle.Driving ? ASHVehicle.Driving.name : "none")} lights={car.LightsOn} engine={car.EngineRunning} playerPos={player.transform.position:F2} carPos={car.transform.position:F2} dist={Vector3.Distance(player.transform.position, car.transform.position):F1} rbKin={(player.GetComponent<Rigidbody>() ? player.GetComponent<Rigidbody>().isKinematic.ToString() : "-")} camTarget={(Invector.vCamera.vThirdPersonCamera.instance && Invector.vCamera.vThirdPersonCamera.instance.mainTarget ? Invector.vCamera.vThirdPersonCamera.instance.mainTarget.name : "-")} tpOn={(Invector.vCamera.vThirdPersonCamera.instance ? Invector.vCamera.vThirdPersonCamera.instance.enabled : false)} camDist={(Camera.main ? Vector3.Distance(Camera.main.transform.position, player.transform.position) : -1f):0.0} mode={ASHInput.VehicleMode}");

            // ---- pickup: menu, repair, fuel
            Teleport(player, pickup.exitPoint.position);
            var dog = ASH.Companion.ASHDogCompanion.Active;
            if (dog) { dog.transform.position = pickup.exitPoint.position + pickup.transform.right * -2f; L($"dog mode={dog.Mode}"); }
            yield return new WaitForSeconds(1f);
            ASHVehicleMenu.Open(pickup, inv, player.transform); yield return new WaitForSeconds(0.6f);
            ScreenCapture.CaptureScreenshot(Dir + "veh_menu_broken.png"); yield return new WaitForSeconds(0.3f);
            ASHVehicleMenu.CloseIfOpen();
            var c = pickup.Condition;
            inv.AddItem(c.BatteryItem, 1); inv.AddItem(c.BeltItem, 1); inv.AddItem(c.TireItem, 1); inv.AddItem(c.TireItem, 1);
            var can = ASHItemRegistry.I.Find("fuel_can"); inv.AddItemWithCondition(can, 1, 0.6f);
            L($"items battery={(c.BatteryItem ? c.BatteryItem.itemId : "null")} belt={(c.BeltItem ? c.BeltItem.itemId : "null")} tire={(c.TireItem ? c.TireItem.itemId : "null")} can={(can ? can.itemId : "null")} canLitres={ASHFuelCan.TotalLitres(inv):0.0}");
            if (inv.Remove(c.BatteryItem, 1)) c.InstallBattery(1f);
            if (inv.Remove(c.BeltItem, 1)) c.InstallBelt(1f);
            for (int i = 0; i < 4; i++) if (c.TireMissing(i) && inv.Remove(c.TireItem, 1)) c.InstallTire(i);
            float got = ASHFuelCan.Pour(inv, 100f); pickup.Fuel.Refuel(got);
            L($"repaired why={c.WhyNoStart(pickup.Fuel) ?? "ok"} fuel={pickup.Fuel.litres:0.0} poured={got:0.0} canLeft={ASHFuelCan.TotalLitres(inv):0.0} tiresMissing={c.MissingTireCount}");
            ASHVehicleMenu.Open(pickup, inv, player.transform); yield return new WaitForSeconds(0.6f);
            ScreenCapture.CaptureScreenshot(Dir + "veh_menu_fixed.png"); yield return new WaitForSeconds(0.3f);
            ASHVehicleMenu.CloseIfOpen();
            pickup.Enter();
            yield return new WaitForSeconds(2.5f);
            L($"pickup entered driving={ASHVehicle.Driving == pickup} engine={pickup.EngineRunning} lights={pickup.LightsOn} dogRiding={(dog ? dog.Riding : false)}");
            t = 0f; while (dog && dog.Riding && !dog.InVehicle && t < 10f) { t += Time.deltaTime; yield return null; }
            L($"dog inVehicle={(dog ? dog.InVehicle : false)} after {t:0.0}s dogPos={(dog ? dog.transform.position.ToString("F2") : "-")} seat={pickup.dogSeat.position:F2}");
            ScreenCapture.CaptureScreenshot(Dir + "veh_pickup_3rd.png");
            input.SimMove = new Vector2(0.3f, 1f);
            for (int i = 0; i < 5; i++) { yield return new WaitForSeconds(2f); Diag("pickup", pickup); }
            L($"pickup drive 4s speed={pickup.SpeedKph:0.0} dogDist={(dog ? Vector3.Distance(dog.transform.position, pickup.dogSeat.position) : -1f):0.00}");
            ScreenCapture.CaptureScreenshot(Dir + "veh_pickup_drive.png");
            input.SimMove = new Vector2(0f, -1f); yield return new WaitForSeconds(2.5f); input.SimMove = null;
            t = 0f; while (pickup.SpeedKph > 2f && t < 8f) { t += Time.deltaTime; yield return null; }
            pickup.Exit(); yield return new WaitForSeconds(3f);
            L($"pickup exit driving={(ASHVehicle.Driving ? "yes" : "none")} lights={pickup.LightsOn} dogRiding={(dog ? dog.Riding : false)} dogPos={(dog ? dog.transform.position.ToString("F2") : "-")} playerPos={player.transform.position:F2}");
            ScreenCapture.CaptureScreenshot(Dir + "veh_pickup_exit.png");
            L("done");
            if (_plane) Destroy(_plane);
            Destroy(gameObject);
        }

        static void Diag(string n, ASHVehicle v)
        {
            var rb = v.GetComponent<Rigidbody>(); var sb = new System.Text.StringBuilder();
            foreach (var w in v.GetComponentsInChildren<NWH.WheelController3D.WheelController>()) sb.Append($"{w.name.Replace("WheelController_", "")}:{(w.enabled ? "on" : "off")} ");
            L($"{n} t={Time.time:0.0} kph={v.SpeedKph:0.0} y={v.transform.position.y:F2} tilt={Vector3.Angle(v.transform.up, Vector3.up):0.0} angV={rb.angularVelocity.magnitude:0.00} velY={rb.linearVelocity.y:0.00} kin={rb.isKinematic} com={rb.centerOfMass:F2} rpm={v.Controller.engine.RPM:0} gear={v.GearText} vert={v.Controller.input.Vertical:0.0} comp={sb}");
        }

        GameObject _plane;
        static void PutOn(ASHVehicle v, Vector3 p)
        {
            var rb = v.GetComponent<Rigidbody>();
            var rot = Quaternion.Euler(0f, 0f, 0f);
            v.transform.SetPositionAndRotation(p + Vector3.up * 1.3f, rot);
            if (rb) { rb.position = v.transform.position; rb.rotation = rot; if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } }
            Physics.SyncTransforms();
        }

        static void Teleport(GameObject p, Vector3 pos)
        {
            var rb = p.GetComponent<Rigidbody>(); if (rb) { rb.position = pos + Vector3.up * 0.1f; rb.linearVelocity = Vector3.zero; }
            p.transform.position = pos + Vector3.up * 0.1f;
        }
    }
}
