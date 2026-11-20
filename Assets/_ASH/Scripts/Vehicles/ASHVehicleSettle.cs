using System.Collections;
using System.Collections.Generic;
using NWH.VehiclePhysics;
using NWH.WheelController3D;
using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>t414/t415: once the last missing tire is fitted (and on load when repaired but tilted), lift the vehicle a few cm and
    /// level it to the ground under its wheels (kinematic, eased, yaw kept), then let it drop from Release Height onto its own springs
    /// so it settles with the suspension's natural bounce. Never moves anything but the vehicle body.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHVehicleSettle : MonoBehaviour
    {
        [Tooltip("Settle once when the last missing tire is fitted.")] public bool settleAfterRepair = true;
        [Tooltip("On load: settle if all tires are on but the body is tilted past Load Tilt Threshold against the ground.")] public bool settleOnLoadIfTilted = true;
        [Tooltip("Lift / level time, s.")] [Range(0.1f, 3f)] public float duration = 0.8f;
        [Tooltip("Extra lift at the middle of the move (clears the hub off the ground), m.")] [Range(0f, 0.5f)] public float liftHeight = 0.08f;
        [Tooltip("Gap between the lowest fully extended tire and the ground when it is let go, m. Bigger = more spring bounce.")] [Range(0f, 0.4f)] public float releaseHeight = 0.06f;
        [Tooltip("Max upward speed allowed for this long after release (no launch), m/s.")] [Range(0.1f, 5f)] public float maxRiseSpeed = 0.6f;
        [Range(0f, 5f)] public float guardSeconds = 2f;
        [Range(0.5f, 45f)] public float loadTiltThreshold = 3f;

        public bool Settling { get; private set; }
        public int SettleCount { get; private set; }
        /// <summary>Tilt (deg) of the body against the plane fitted under the wheels.</summary>
        public float GroundTilt => FitGround(out var n, out _) ? Vector3.Angle(transform.up, n) : -1f;

        Rigidbody _rb; ASHVehicleCondition _cond; VehicleController _vc; WheelController[] _wheels;
        void Awake() { _rb = GetComponent<Rigidbody>(); _cond = GetComponent<ASHVehicleCondition>(); _vc = GetComponent<VehicleController>(); _wheels = GetComponentsInChildren<WheelController>(true); }
        void OnEnable() => ASHVehicleCondition.PartChanged += OnPart;
        void OnDisable() => ASHVehicleCondition.PartChanged -= OnPart;

        void OnPart(ASHVehicleCondition c, ASHVehicleCondition.Part p)
        {
            if (c != _cond || p != ASHVehicleCondition.Part.Tire || !settleAfterRepair || !isActiveAndEnabled) return;
            if (c.MissingTireCount == 0) Settle();
        }

        public void QueueLoadCheck() { if (settleOnLoadIfTilted && isActiveAndEnabled) StartCoroutine(LoadCheck()); }
        IEnumerator LoadCheck()
        {
            yield return new WaitForSeconds(0.6f);
            if (_cond && _cond.MissingTireCount > 0) yield break;
            float t = GroundTilt; if (t > loadTiltThreshold) { Debug.Log($"[ASH] Vehicle {name}: loaded tilted {t:0.0} deg, settling"); Settle(); }
        }

        public void Settle() { if (!Settling && _rb && (ASHVehicle.Driving == null || ASHVehicle.Driving.gameObject != gameObject)) StartCoroutine(Run()); }

        IEnumerator Run()
        {
            Settling = true;
            // wake NWH (it freezes a parked car kinematic), let it unfreeze, then take over
            for (int i = 0; i < 3; i++) { if (_vc) _vc.ResetInactivityTimer(); yield return new WaitForFixedUpdate(); }
            var cd = _rb.collisionDetectionMode; var interp = _rb.interpolation;
            _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; _rb.isKinematic = true;
            Vector3 p0 = _rb.position; Quaternion r0 = _rb.rotation;
            if (!FitGround(out var n, out _)) n = Vector3.up;
            var fwd = Vector3.ProjectOnPlane(transform.forward, n); if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            Quaternion r1 = Quaternion.LookRotation(fwd.normalized, n);
            Vector3 p1 = p0 + Vector3.up * ReleaseOffset(p0, r1);
            float tilt0 = Vector3.Angle(transform.up, n);
            float t = 0f, dur = Mathf.Max(0.05f, duration);
            while (t < dur)
            {
                t += Time.fixedDeltaTime; float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                var p = Vector3.Lerp(p0, p1, s) + Vector3.up * (liftHeight * Mathf.Sin(s * Mathf.PI));
                if (!_rb.isKinematic) _rb.isKinematic = true;
                _rb.MovePosition(p); _rb.MoveRotation(Quaternion.Slerp(r0, r1, s));
                if (_vc) _vc.ResetInactivityTimer();
                yield return new WaitForFixedUpdate();
            }
            _rb.isKinematic = false; _rb.collisionDetectionMode = cd; _rb.interpolation = interp;
            _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; _rb.WakeUp();
            SettleCount++;
            Debug.Log($"[ASH] Vehicle {name}: settled on its wheels (tilt {tilt0:0.0} -> {Vector3.Angle(r1 * Vector3.up, n):0.0} deg, released {releaseHeight:0.00} m up)");
            // free drop onto the springs; only clamp a launch
            float g = 0f;
            while (g < guardSeconds)
            {
                g += Time.fixedDeltaTime; if (_vc) _vc.ResetInactivityTimer();
                var v = _rb.linearVelocity; if (v.y > maxRiseSpeed) { v.y = maxRiseSpeed; _rb.linearVelocity = v; }
                yield return new WaitForFixedUpdate();
            }
            Settling = false;
        }

        /// <summary>How far to raise (world up) so the lowest fully extended tire hangs Release Height above its ground.</summary>
        float ReleaseOffset(Vector3 pos, Quaternion rot)
        {
            float minClear = float.PositiveInfinity;
            foreach (var w in _wheels)
            {
                if (!w || !w.enabled) continue;
                var local = transform.InverseTransformPoint(w.transform.position);
                var top = pos + rot * Vector3.Scale(local, transform.lossyScale);
                if (!Ground(top, out var hit)) continue;
                float need = w.spring.maxLength + w.wheel.tireRadius;
                minClear = Mathf.Min(minClear, top.y - hit.y - need);
            }
            return float.IsInfinity(minClear) ? releaseHeight : releaseHeight - minClear;
        }

        /// <summary>Debug: per wheel, gap (m) between the fully extended tire and the ground hit under it (+ = in the air), and what it hit.</summary>
        public string DebugClearance()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var w in _wheels)
            {
                if (!w) continue;
                string hitName = "-"; float gap = float.NaN;
                if (GroundHit(w.transform.position, out var h)) { gap = w.transform.position.y - h.point.y - (w.spring.maxLength + w.wheel.tireRadius); hitName = h.collider.name; }
                sb.Append($"{w.name}:{gap:0.000}({hitName},len {w.spring.length:0.00}/{w.spring.maxLength:0.00} r {w.wheel.tireRadius:0.00}) ");
            }
            return sb.ToString();
        }
        bool GroundHit(Vector3 from, out RaycastHit best)
        {
            best = default; float d = float.PositiveInfinity;
            foreach (var h in Physics.RaycastAll(from + Vector3.up * 1.5f, Vector3.down, 8f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && h.distance < d) { d = h.distance; best = h; }
            return d < float.PositiveInfinity;
        }

        bool Ground(Vector3 from, out Vector3 point)
        {
            point = default; float best = float.PositiveInfinity;
            foreach (var h in Physics.RaycastAll(from + Vector3.up * 1.5f, Vector3.down, 8f, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && h.distance < best) { best = h.distance; point = h.point; }
            return best < float.PositiveInfinity;
        }

        /// <summary>Plane through the ground points under the wheels (all of them, missing tires too).</summary>
        bool FitGround(out Vector3 normal, out Vector3 centre)
        {
            normal = Vector3.up; centre = transform.position;
            var pts = new List<(float a, Vector3 p)>();
            if (_wheels == null) return false;
            foreach (var w in _wheels)
            {
                if (!w) continue;
                if (!Ground(w.transform.position, out var p)) continue;
                var l = transform.InverseTransformPoint(w.transform.position);
                pts.Add((Mathf.Atan2(l.x, l.z), p));
            }
            if (pts.Count < 3) return false;
            pts.Sort((x, y) => x.a.CompareTo(y.a));
            centre = Vector3.zero; foreach (var q in pts) centre += q.p; centre /= pts.Count;
            Vector3 nn = Vector3.zero;
            for (int i = 0; i < pts.Count; i++) nn += Vector3.Cross(pts[i].p - centre, pts[(i + 1) % pts.Count].p - centre);
            if (nn.sqrMagnitude < 1e-8f) return false;
            normal = nn.normalized; if (normal.y < 0f) normal = -normal;
            return true;
        }
    }
}
