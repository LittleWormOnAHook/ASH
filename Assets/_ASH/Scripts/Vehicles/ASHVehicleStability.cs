using NWH.WheelController3D;
using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>t381: keeps a vehicle from launching / spinning.
    /// Root cause found in the terrain log: a wheel's suspension raycast hit the car's OWN body collider (Car_Body_Collider), so the
    /// spring pushed the car off itself. NWH wheel rays skip only the Ignore Raycast layer, so every solid collider of the vehicle is put
    /// on that layer at runtime (they still collide normally; the interactor uses all layers). Plus: zero-bounce body material,
    /// ContinuousDynamic + interpolation, depenetration / spin caps, post-impact pop-up clamp, airborne damping, upright assist, auto-right.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHVehicleStability : MonoBehaviour
    {
        [Header("Setup")]
        [Tooltip("Move the vehicle's solid colliders to Ignore Raycast so the wheels can't stand on their own body.")] public bool bodyCollidersIgnoreRaycast = true;
        [Tooltip("Zero-bounce material on the body colliders.")] public bool zeroBounceMaterial = true;
        [Range(0f, 1f)] public float bodyFriction = 0.3f;
        [Tooltip("Cap on overlap push-out speed, m/s.")] [Range(0.5f, 20f)] public float maxDepenetration = 2.5f;
        [Tooltip("Cap on spin, rad/s.")] [Range(1f, 50f)] public float maxAngularVelocity = 7f;

        [Header("Impacts")]
        [Tooltip("While the body touches something (tree, rock, wall): max upward speed it can gain, m/s.")] [Range(0f, 10f)] public float impactMaxPopUp = 1f;
        [Tooltip("Keep the impact clamps on this long after the last body contact, s.")] [Range(0f, 3f)] public float impactHoldSeconds = 0.6f;
        [Tooltip("While the body touches something: max spin, rad/s.")] [Range(0.2f, 20f)] public float impactMaxSpin = 2.5f;

        [Header("Airborne / upright")]
        [Tooltip("Spin damping with no wheel on the ground, per second.")] [Range(0f, 20f)] public float airAngularDamping = 3f;
        [Tooltip("Torque pulling the car level while airborne (per mass).")] [Range(0f, 30f)] public float airUpright = 4f;
        [Tooltip("Assist torque pulling the car upright on the ground once tilted past uprightStartAngle.")] [Range(0f, 30f)] public float groundUpright = 1f;
        [Range(5f, 90f)] public float uprightStartAngle = 30f;
        [Tooltip("Flipped / on its side and nearly stopped this long: set it back on its wheels.")] public bool autoRightWhenFlipped = true;
        [Range(0.5f, 10f)] public float autoRightSeconds = 2.5f;

        [Header("Body roll (t384)")]
        [Tooltip("Centre of mass height above the ground under the wheels, metres. Lower = less lean / harder to roll. Set at Play start (replaces NWH CenterOfMass, which drifted lower on every recompile).")] [Range(-0.5f, 1.5f)] public float comHeight = 0.45f;
        [Tooltip("Centre of mass forward (+) / back (-) shift from the collider centre, metres.")] [Range(-1.5f, 1.5f)] public float comForward = 0f;
        [Tooltip("Anti-roll bar stiffness as a multiple of one spring's max force (NWH default ~0.4-0.5).")] [Range(0f, 5f)] public float rollStiffness = 0.6f;
        [Tooltip("Spring max force multiplier over the built value.")] [Range(0.5f, 3f)] public float springStiffness = 1.15f;
        [Tooltip("Damper (bump + rebound) multiplier over the built value.")] [Range(0.5f, 3f)] public float damperStiffness = 1.2f;
        [Tooltip("Extra torque that cancels body roll while on the ground (per degree of roll, per mass).")] [Range(0f, 5f)] public float rollAssist = 0.1f;

        Rigidbody _rb; WheelController[] _wheels; float[] _baseSpring, _baseBump, _baseRebound; float _touchTime = -10f; float _flippedT; PhysicsMaterial _mat;
        public int GroundedWheels { get; private set; }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>(); _wheels = GetComponentsInChildren<WheelController>(true);
            if (zeroBounceMaterial) { _mat = new PhysicsMaterial("ASH_VehicleBody") { bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum, dynamicFriction = bodyFriction, staticFriction = bodyFriction, frictionCombine = PhysicsMaterialCombine.Minimum }; }
            int ign = 2;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c.GetComponentInParent<WheelController>()) continue;
                if (bodyCollidersIgnoreRaycast) c.gameObject.layer = ign;
                if (_mat) c.sharedMaterial = _mat;
            }
            Apply();
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            var nwhCom = GetComponent<NWH.VehiclePhysics.CenterOfMass>(); if (nwhCom) nwhCom.enabled = false;
            _baseSpring = new float[_wheels.Length]; _baseBump = new float[_wheels.Length]; _baseRebound = new float[_wheels.Length];
            for (int i = 0; i < _wheels.Length; i++) { _baseSpring[i] = _wheels[i].spring.maxForce; _baseBump[i] = _wheels[i].damper.unitBumpForce; _baseRebound[i] = _wheels[i].damper.unitReboundForce; }
            ApplyRoll();
        }

        /// <summary>COM from the wheel layout (height above their contact plane), anti-roll bars and suspension multipliers.</summary>
        public void ApplyRoll()
        {
            if (!_rb || _baseSpring == null) return;
            for (int i = 0; i < _wheels.Length; i++) { _wheels[i].spring.maxForce = _baseSpring[i] * springStiffness; _wheels[i].damper.unitBumpForce = _baseBump[i] * damperStiffness; _wheels[i].damper.unitReboundForce = _baseRebound[i] * damperStiffness; }
            var vc = GetComponent<NWH.VehiclePhysics.VehicleController>();
            if (vc != null && vc.axles != null)
                foreach (var a in vc.axles) if (a != null && a.leftWheel != null && a.leftWheel.WheelController) a.geometry.antiRollBarForce = a.leftWheel.WheelController.spring.maxForce * rollStiffness;
            if (_wheels.Length > 0)
            {
                float groundY = 0f; foreach (var w in _wheels) groundY += transform.InverseTransformPoint(w.transform.position).y - w.spring.maxLength * 0.5f - w.wheel.tireRadius; groundY /= _wheels.Length;
                bool kin = _rb.isKinematic; _rb.ResetCenterOfMass(); var auto = _rb.centerOfMass;
                _rb.centerOfMass = new Vector3(auto.x, groundY + comHeight, auto.z + comForward); _rb.isKinematic = kin;
            }
        }

        void OnValidate() { if (_rb) { Apply(); if (Application.isPlaying) ApplyRoll(); } }
        public float RollDegrees => Mathf.Asin(Mathf.Clamp(Vector3.Dot(transform.right, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        void Apply()
        {
            if (!_rb) return;
            _rb.maxDepenetrationVelocity = maxDepenetration; _rb.maxAngularVelocity = maxAngularVelocity;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (!_rb.isKinematic) _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        void OnCollisionStay(Collision c) { if (!c.collider.GetComponentInParent<ASHVehicle>()) _touchTime = Time.time; }
        void OnCollisionEnter(Collision c) { OnCollisionStay(c); }

        void FixedUpdate()
        {
            if (!_rb || _rb.isKinematic) return;
            if (_rb.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic) _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            float dt = Time.fixedDeltaTime;
            int g = 0;
            foreach (var w in _wheels)
            {
                if (!w || !w.enabled) continue;
                float len = w.spring.maxLength + w.wheel.tireRadius * 2.2f + 0.1f;
                if (Physics.Raycast(w.transform.position, -w.transform.up, len, ~(1 << 2), QueryTriggerInteraction.Ignore)) g++;
            }
            GroundedWheels = g;
            var v = _rb.linearVelocity; var av = _rb.angularVelocity;
            bool touching = Time.time - _touchTime <= impactHoldSeconds;
            if (_rb.maxAngularVelocity != maxAngularVelocity) _rb.maxAngularVelocity = maxAngularVelocity;
            if (touching)
            {
                if (v.y > impactMaxPopUp) { v.y = impactMaxPopUp; _rb.linearVelocity = v; }
                if (av.magnitude > impactMaxSpin) _rb.angularVelocity = av = av.normalized * impactMaxSpin;
            }
            float tilt = Vector3.Angle(transform.up, Vector3.up);
            var axis = Vector3.Cross(transform.up, Vector3.up); // torque direction that levels the car
            if (g == 0)
            {
                _rb.angularVelocity = av * Mathf.Exp(-airAngularDamping * dt);
                if (airUpright > 0f) _rb.AddTorque(axis * airUpright, ForceMode.Acceleration);
            }
            else if (tilt > uprightStartAngle && groundUpright > 0f) _rb.AddTorque(axis * groundUpright * (tilt / 90f), ForceMode.Acceleration);
            if (g >= 2 && rollAssist > 0f) { float roll = RollDegrees; if (Mathf.Abs(roll) < 45f) _rb.AddTorque(-transform.forward * (roll * rollAssist * 0.1f), ForceMode.Acceleration); }

            if (autoRightWhenFlipped && tilt > 75f && v.magnitude < 1.5f) { _flippedT += dt; if (_flippedT > autoRightSeconds) RightIt(); } else _flippedT = 0f;
        }

        void RightIt()
        {
            _flippedT = 0f;
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up); if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.forward;
            var p = _rb.position + Vector3.up * 1.2f;
            if (Physics.Raycast(_rb.position + Vector3.up * 3f, Vector3.down, out var h, 10f, ~(1 << 2), QueryTriggerInteraction.Ignore)) p = h.point + Vector3.up * 1.4f;
            _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
            _rb.position = p; _rb.rotation = Quaternion.LookRotation(fwd, Vector3.up); transform.SetPositionAndRotation(p, _rb.rotation);
            Physics.SyncTransforms();
            Debug.Log($"[ASH] Vehicle {name} set back on its wheels");
        }
    }
}