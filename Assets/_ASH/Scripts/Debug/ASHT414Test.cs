using System.Collections;
using ASH.Player;
using ASH.Vehicles;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Play Mode, exec ASH.DebugTools.ASHT414Test.Run: truck tire repair -> settle on 4 wheels (tilt, grounded, bounce, no launch),
    /// then 0-50 km/h at power x1 vs the tuned multiplier for truck and car. Puts vehicles back where they were. Logs "[ASH] T414 ...".</summary>
    public sealed class ASHT414Test : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHT414Test()
        {
            SkipSlope = default;
            _top = default;
        }

        static bool SkipSlope; static float _top;
        public static void RunNoSlope() { SkipSlope = true; if (Application.isPlaying) new GameObject("ASH_T414Test").AddComponent<ASHT414Test>(); }
        public static void Run() { SkipSlope = false; if (Application.isPlaying) new GameObject("ASH_T414Test").AddComponent<ASHT414Test>(); }
        static void L(string m) => Debug.Log("[ASH] T414 " + m);
        ASHInput _input; GameObject _player;
        IEnumerator Start()
        {
            yield return null;
            _input = FindAnyObjectByType<ASHInput>();
            var pc = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>(); _player = pc ? pc.gameObject : null;
            ASHVehicle truck = null, car = null; foreach (var v in ASHVehicle.All) { if (v.vehicleId == "pickup") truck = v; else car = v; }
            if (!truck) { L("FAIL no truck"); yield break; }
            var c = truck.Condition; var st = truck.GetComponent<ASHVehicleSettle>(); var stab = truck.GetComponent<ASHVehicleStability>(); var rb = truck.GetComponent<Rigidbody>();
            var tp = rb.position; var tr = rb.rotation; int origMask = c ? c.MissingMask : 0;
            if (c) { c.InstallBattery(1f); c.InstallBelt(1f); } if (truck.Fuel) truck.Fuel.litres = truck.Fuel.capacityLitres;
            if (c && c.HasTires && st)
            {
                c.RemoveTire(0); c.RemoveTire(3);
                yield return new WaitForSeconds(3f);
                float before = st.GroundTilt; int n0 = st.SettleCount;
                c.InstallTire(0); yield return new WaitForFixedUpdate();
                bool early = st.Settling;
                c.InstallTire(3);
                float maxUp = 0f, y0 = rb.position.y, yMax = float.MinValue, yMin = float.MaxValue; bool released = false, sawKin = false; float t = 0f;
                while ((st.Settling || t < 0.2f) && t < 8f)
                {
                    t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate();
                    if (rb.isKinematic) sawKin = true; if (sawKin && !rb.isKinematic) { if (!released) { released = true; yMax = yMin = rb.position.y; } maxUp = Mathf.Max(maxUp, rb.linearVelocity.y); yMax = Mathf.Max(yMax, rb.position.y); yMin = Mathf.Min(yMin, rb.position.y); }
                }
                yield return new WaitForSeconds(1f);
                float after = st.GroundTilt; int g = stab ? stab.GroundedWheels : -1;
                bool ok = !early && st.SettleCount == n0 + 1 && after < 3f && g == 4 && !rb.isKinematic && maxUp < 1f;
                L("INFO at rest " + st.DebugClearance());
                L((ok ? "PASS" : "FAIL") + $" truck settle after tire repair: tilt {before:0.0} -> {after:0.0} deg, grounded {g}/4, runs {st.SettleCount - n0}, early={early}, drop/bounce {yMax - yMin:0.000} m, max rise {maxUp:0.00} m/s");
            }
            else L("FAIL truck has no tire slots / settle component");

            foreach (var v in new[] { truck, car })
            {
                if (!v) continue;
                float tuned = v.powerMultiplier; string res = "";
                foreach (float m in tuned != 1f ? new[] { 1f, tuned } : new[] { 1f })
                {
                    v.powerMultiplier = m; v.ApplyPower();
                    if (FindFlat(truck.transform.position, out var fp, out var fd)) { var tr0 = Terrain.activeTerrain; Pose(v, fp + Vector3.up * 1.0f, Quaternion.LookRotation(fd, Vector3.up)); yield return new WaitForSeconds(1.5f); }
                    float s = -1f; yield return Accel(v, x => s = x);
                    res += $" x{m:0.00} ({v.Controller.engine.maxPower:0} kW): {(s < 0f ? $">15 s (top {_top:0} km/h)" : s.ToString("0.0") + " s")}";
                }
                v.powerMultiplier = tuned; v.ApplyPower();
                L($"INFO {v.vehicleId} 0-50 km/h{res} (base {v.BasePower:0} kW)");
            }
            // t416 slopes: reverse up (facing downhill) and forward up, assist off vs on
            if (!SkipSlope && FindSlope(truck.transform.position, out var sp, out var up, out var sa))
            {
                foreach (var v in new[] { truck, car })
                {
                    if (!v) continue;
                    var vrb = v.GetComponent<Rigidbody>(); var vp = vrb.position; var vr = vrb.rotation;
                    float b = v.slopePowerBoost, ga = v.slopeGravityAssist, hs = v.hillStartAccel, rg = v.reverseGearRatio;
                    string res = "";
                    foreach (bool on in new[] { false, true })
                    {
                        if (on) { v.slopePowerBoost = b; v.slopeGravityAssist = ga; v.hillStartAccel = hs; v.reverseGearRatio = rg; }
                        else { v.slopePowerBoost = 0f; v.slopeGravityAssist = 0f; v.hillStartAccel = 0f; v.reverseGearRatio = 5f; }
                        v.ApplyReverse();
                        foreach (bool rev in new[] { true, false })
                        {
                            float gain = 0f, vmax = 0f, boostMax = 0f;
                            yield return Climb(v, sp, up, rev, (g2, vm, bm) => { gain = g2; vmax = vm; boostMax = bm; });
                            res += $" [{(on ? "assist" : "stock")} {(rev ? "reverse" : "forward")}: +{gain:0.0} m uphill in 8 s, top {vmax:0.0} km/h, boost +{boostMax * 100f:0}%]";
                        }
                    }
                    v.slopePowerBoost = b; v.slopeGravityAssist = ga; v.hillStartAccel = hs; v.reverseGearRatio = rg; v.ApplyReverse();
                    Pose(v, vp, vr); yield return new WaitForSeconds(0.5f);
                    L($"INFO {v.vehicleId} slope {sa:0} deg at {sp:F0}:{res}");
                }
            }
            else L("INFO no clear 15-20 deg slope found near the truck");

            Pose(truck, tp, tr); if (c) c.SetMissingMask(origMask);
            L("DONE");
            Destroy(gameObject);
        }
        static bool FindFlat(Vector3 near, out Vector3 pos, out Vector3 dir)
        {
            pos = default; dir = Vector3.forward; var ter = Terrain.activeTerrain; if (!ter) return false;
            for (float r = 10f; r <= 300f; r += 10f)
                for (float a = 0f; a < 360f; a += 20f)
                    for (float h = 0f; h < 360f; h += 45f)
                    {
                        var p = near + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r; var d = Quaternion.Euler(0f, h, 0f) * Vector3.forward; var e = p + d * 90f;
                        float y0 = ter.SampleHeight(p), y1 = ter.SampleHeight(e), ym = ter.SampleHeight((p + e) * 0.5f);
                        if (Mathf.Abs(y1 - y0) > 3f || Mathf.Abs(ym - (y0 + y1) * 0.5f) > 1f) continue;
                        if (!Clear(p, e)) continue;
                        pos = new Vector3(p.x, y0 + ter.transform.position.y, p.z); dir = d; return true;
                    }
            return false;
        }
        static bool Clear(Vector3 a, Vector3 b)
        {
            for (float s = 0f; s <= 1f; s += 0.1f)
            {
                var p = Vector3.Lerp(a, b, s);
                if (!Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var h, 60f, ~(1 << 2), QueryTriggerInteraction.Ignore) || !(h.collider is TerrainCollider)) return false;
                foreach (var col in Physics.OverlapBox(h.point + Vector3.up * 1.5f, new Vector3(1.6f, 1.2f, 1.6f), Quaternion.identity, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    if (!(col is TerrainCollider) && !col.GetComponentInParent<ASHVehicle>() && !col.attachedRigidbody) return false;
            }
            return true;
        }
        /// <summary>Spot within 250 m where the terrain rises 15-20 deg steadily over 25 m with nothing in the way. up = uphill direction (flat).</summary>
        static bool FindSlope(Vector3 near, out Vector3 pos, out Vector3 up, out float ang)
        {
            pos = default; up = Vector3.forward; ang = 0f;
            var ter = Terrain.activeTerrain; if (!ter) return false;
            for (float r = 20f; r <= 250f; r += 10f)
                for (float a = 0f; a < 360f; a += 15f)
                {
                    var p = near + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                    var tp = p - ter.transform.position; var size = ter.terrainData.size;
                    if (tp.x < 5f || tp.z < 5f || tp.x > size.x - 5f || tp.z > size.z - 5f) continue;
                    var n = ter.terrainData.GetInterpolatedNormal(tp.x / size.x, tp.z / size.z);
                    float s = Vector3.Angle(n, Vector3.up); if (s < 15f || s > 20f) continue;
                    var dir = -new Vector3(n.x, 0f, n.z).normalized; // uphill
                    var lo = p - dir * 6f; var hi = p + dir * 20f;
                    float hLo = ter.SampleHeight(lo) + ter.transform.position.y, hHi = ter.SampleHeight(hi) + ter.transform.position.y;
                    float avg = Mathf.Atan2(hHi - hLo, 26f) * Mathf.Rad2Deg; if (avg < 13f || avg > 22f) continue;
                    if (!Clear(lo, hi)) continue;
                    pos = new Vector3(p.x, ter.SampleHeight(p) + ter.transform.position.y, p.z); up = dir; ang = avg; return true;
                }
            return false;
        }
        IEnumerator Climb(ASHVehicle v, Vector3 at, Vector3 uphill, bool reverse, System.Action<float, float, float> done)
        {
            var rb = v.GetComponent<Rigidbody>();
            var ter = Terrain.activeTerrain; var tp = at - ter.transform.position; var sz = ter.terrainData.size;
            var n = ter.terrainData.GetInterpolatedNormal(tp.x / sz.x, tp.z / sz.z);
            var face = Vector3.ProjectOnPlane(reverse ? -uphill : uphill, n).normalized;
            if (v.Condition) { v.Condition.InstallBattery(1f); v.Condition.InstallBelt(1f); v.Condition.SetMissingMask(0); } if (v.Fuel) v.Fuel.litres = v.Fuel.capacityLitres;
            Pose(v, at + n * 1.2f, Quaternion.LookRotation(face, n));
            yield return new WaitForSeconds(0.3f);
            if (_player && v.exitPoint) { var prb = _player.GetComponent<Rigidbody>(); if (prb) { prb.position = v.exitPoint.position; prb.linearVelocity = Vector3.zero; } _player.transform.position = v.exitPoint.position; Physics.SyncTransforms(); }
            v.Enter(true); float t = 0f; while ((ASHVehicle.Driving != v || !v.EngineRunning) && t < 6f) { t += Time.deltaTime; if (ASHVehicle.Driving == v) _input.SimMove = new Vector2(0f, 0.05f); yield return null; }
            _input.SimMove = null; t = 0f; while (t < 1.5f) { t += Time.deltaTime; yield return null; }
            float start = Vector3.Dot(rb.position, uphill), vmax = 0f, bmax = 0f;
            t = 0f; while (t < 8f) { _input.SimMove = new Vector2(0f, reverse ? -1f : 1f); yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; vmax = Mathf.Max(vmax, v.SpeedKph); bmax = Mathf.Max(bmax, v.SlopeBoost); }
            float gain = Vector3.Dot(rb.position, uphill) - start;
            _input.SimMove = null;
            v.Exit(true); float w = 0f; while (ASHVehicle.Driving == v && w < 5f) { w += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.3f);
            done(gain, vmax, bmax);
        }
        IEnumerator Accel(ASHVehicle v, System.Action<float> done)
        {
            var rb = v.GetComponent<Rigidbody>(); var p0 = rb.position; var r0 = rb.rotation;
            if (v.Condition) { v.Condition.InstallBattery(1f); v.Condition.InstallBelt(1f); v.Condition.SetMissingMask(0); } if (v.Fuel) v.Fuel.litres = v.Fuel.capacityLitres;
            if (_player && v.exitPoint) { var prb = _player.GetComponent<Rigidbody>(); if (prb) { prb.position = v.exitPoint.position; prb.linearVelocity = Vector3.zero; } _player.transform.position = v.exitPoint.position; Physics.SyncTransforms(); }
            yield return new WaitForSeconds(0.5f);
            v.Enter(true); float t = 0f; while ((ASHVehicle.Driving != v || !v.EngineRunning) && t < 6f) { t += Time.deltaTime; if (ASHVehicle.Driving == v) _input.SimMove = new Vector2(0f, 0.05f); yield return null; }
            _input.SimMove = null; yield return new WaitForSeconds(1.5f);
            t = 0f; float res = -1f;
            _top = 0f; while (t < 15f) { _input.SimMove = new Vector2(0f, 1f); yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; _top = Mathf.Max(_top, v.SpeedKph); if (v.SpeedKph >= 50f) { res = t; break; } }
            _input.SimMove = new Vector2(0f, -1f); float w = 0f; while (v.SpeedKph > 2f && w < 8f) { w += Time.deltaTime; yield return null; }
            _input.SimMove = null;
            v.Exit(true); w = 0f; while (ASHVehicle.Driving == v && w < 5f) { w += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.5f);
            Pose(v, p0, r0); yield return new WaitForSeconds(1f);
            done(res);
        }
        static void Pose(ASHVehicle v, Vector3 p, Quaternion r)
        {
            var rb = v.GetComponent<Rigidbody>(); bool k = rb.isKinematic;
            if (!k) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.position = p; rb.rotation = r; v.transform.SetPositionAndRotation(p, r); Physics.SyncTransforms();
        }
    }
}
