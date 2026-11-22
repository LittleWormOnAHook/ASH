using System;
using System.Collections;
using System.Collections.Generic;
using ASH.Core;
using ASH.Interaction;
using ASH.Player;
using NWH.VehiclePhysics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ASH.Vehicles
{
    /// <summary>Drivable vehicle (t356) on NWH Vehicle Physics: E at the driver door gets in / out (door swings, ASHVehicleDoor),
    /// Tab switches 3rd / 1st person (ASHVehicleCamera), L headlights, Space handbrake, WASD drive. Fuel (ASHVehicleFuel), battery /
    /// belt hooks (ASHVehicleCondition), the dog rides in the pickup bed. Saves pose, fuel, parts, lights and whether you are in it.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleController))]
    public sealed class ASHVehicle : MonoBehaviour, IASHUsable, IASHSaveable
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHVehicle()
        {
            Driving = default;
            DrivingChanged = null;
            All.Clear();
            OpenMenu = null;
            Inspected = null;
        }

        public static ASHVehicle Driving { get; private set; }
        public static event Action<ASHVehicle> DrivingChanged;
        public static readonly List<ASHVehicle> All = new List<ASHVehicle>();
        /// <summary>Set by ASHVehicleMenu: E at the door opens the vehicle menu (Drive / Fuel Up / Repair). Null = E gets straight in.</summary>
        public static System.Func<ASHVehicle, ASH.Inventory.ASHInventory, Transform, bool> OpenMenu;
        /// <summary>E on a destroyed vehicle (prologue: inspect the crushed car).</summary>
        public static event Action<ASHVehicle> Inspected;
        /// <summary>Story damage (M1: the tree on the car): no entry or driving, an Inspect prompt instead, physics frozen. Saved.</summary>
        public bool Destroyed { get; private set; }
        public void SetDestroyed(bool on)
        {
            if (Destroyed == on) return;
            Destroyed = on;
            if (on && Driving == this) Exit(true);
            var rb = GetComponent<Rigidbody>();
            if (rb) { if (on && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } rb.isKinematic = on; }
            if (_vc) { if (on) { _vc.input.Vertical = 0f; _vc.input.Horizontal = 0f; _vc.input.Handbrake = 1f; try { if (_vc.engine.IsRunning) _vc.engine.Stop(); } catch { } _vc.Active = false; } }
            if (on && _lights) _lights.Set(false);
        }
        [Tooltip("E at the door opens the vehicle menu (E again drives). Off = E gets straight in.")] public bool useMenu = true;

        [Header("Identity")]
        public string vehicleId = "pickup";
        public string displayName = "Pickup";
        [Tooltip("The dog jumps in the back (pickup bed).")] public bool carriesDog = true;
        [Header("Points (made by ASH/Systems/Vehicles/Setup Vehicles)")]
        public Transform seat, head, exitPoint, dogSeat, dogTailgate;
        public ASHVehicleDoor driverDoor;
        [Header("Enter / exit")]
        [Tooltip("How close to the driver door (exit point) E works, metres.")] [Range(0.5f, 6f)] public float enterRange = 2.6f;
        [Tooltip("No getting out above this speed.")] [Range(0f, 80f)] public float maxExitSpeedKph = 8f;
        public bool stopEngineOnExit = true;
        [Tooltip("Turn the engine on when you get in (if battery, belt and fuel allow).")] public bool autoStartEngine = true;
        [Header("Driving")]
        [Range(0.5f, 20f)] public float steerResponse = 4f;
        [Range(0.5f, 20f)] public float throttleResponse = 6f;
        [Header("Dog")]
        [Range(2f, 80f)] public float dogRideRange = 25f;
        [Tooltip("Runtime: keep the handbrake on until the driver steers or presses throttle (prologue car start on a slope).")] [System.NonSerialized] public bool holdBrakeUntilInput;
        [Tooltip("The dog may ride in the passenger seat (chance: companion settings passengerSeatChance). Pickup only by default.")] public bool dogPassengerSeat = true;
        [Tooltip("Dog passenger seat point (made at Play: the driver seat mirrored across the cab).")] public Transform passengerSeat;
        [Tooltip("Dog passenger door point (the driver exit point mirrored).")] public Transform passengerDoor;
        [Tooltip("Shift of the dog's passenger seat point (x right, y up, z forward), metres.")] public Vector3 passengerSeatOffset = Vector3.zero;
        Vector3 _passBase;
        public float SteerInput => _steer;
        public ASHVehicleDriver Driver => _driver;
        ASHVehicleDriver _driver;

        VehicleController _vc; ASHVehicleFuel _fuel; ASHVehicleCondition _cond; ASHVehicleLights _lights; ASHVehicleCamera _cam;
        bool _busy; float _enteredAt, _steer, _throttle;
        // player state while seated
        RigidbodyInterpolation _prbInterp; GameObject _player; readonly List<Behaviour> _off = new List<Behaviour>(); readonly List<Collider> _cols = new List<Collider>(); readonly List<Renderer> _hidden = new List<Renderer>();
        Rigidbody _prb; bool _prbKin, _prbDet; Animator _panim; bool _panimOn; Transform _pParent;
        Camera _mainCam; Transform _camParent; Vector3 _camLocalPos; Quaternion _camLocalRot; Behaviour _tpCam;

        public VehicleController Controller => _vc ? _vc : _vc = GetComponent<VehicleController>();
        public ASHVehicleFuel Fuel => _fuel; public ASHVehicleCondition Condition => _cond; public ASHVehicleLights Lights => _lights;
        public float SpeedKph => Controller ? Mathf.Abs(Controller.SpeedKPH) : 0f;
        public bool EngineRunning => Controller && Controller.engine.IsRunning;
        public bool LightsOn => _lights && _lights.On;
        public string GearText { get { try { return Controller.transmission.GearName; } catch { return ""; } } }
        public bool IsOccupied => Driving == this;
        public ASHVehicleCamera Cam => _cam;

        void Awake()
        {
            _vc = GetComponent<VehicleController>(); _fuel = GetComponent<ASHVehicleFuel>(); _cond = GetComponent<ASHVehicleCondition>(); _lights = GetComponent<ASHVehicleLights>();
            _cam = GetComponent<ASHVehicleCamera>(); if (!_cam) _cam = gameObject.AddComponent<ASHVehicleCamera>(); if (!_cam.head) _cam.head = head;
            if (_cond && !_restored) _cond.ApplyStartBroken();
            if (_lights) _lights.Set(false);
            var body = GetComponent<Rigidbody>(); if (body) body.maxDepenetrationVelocity = maxDepenetration; // t378: no violent pop-outs
            _driver = GetComponent<ASHVehicleDriver>(); if (!_driver) _driver = gameObject.AddComponent<ASHVehicleDriver>();
            if (!GetComponent<ASHVehicleTracks>()) gameObject.AddComponent<ASHVehicleTracks>(); // t399/t400 packed snow ruts
            if (!GetComponent<ASHVehicleSettle>()) gameObject.AddComponent<ASHVehicleSettle>(); // t414 level onto 4 wheels after the tire repair
            if (carriesDog && dogPassengerSeat && seat)
            {
                if (!passengerSeat)
                {
                    var lp = transform.InverseTransformPoint(seat.position); lp.x = -lp.x;
                    passengerSeat = new GameObject("ASH_PassengerSeat").transform; passengerSeat.SetParent(transform, false); passengerSeat.localPosition = lp;
                }
                _passBase = passengerSeat.localPosition;
                if (!passengerDoor && exitPoint)
                {
                    var lp = transform.InverseTransformPoint(exitPoint.position); lp.x = -lp.x;
                    passengerDoor = new GameObject("ASH_PassengerDoor").transform; passengerDoor.SetParent(transform, false); passengerDoor.localPosition = lp;
                }
            }
            if (!GetComponent<ASHVehicleStability>()) { var st = gameObject.AddComponent<ASHVehicleStability>(); if (vehicleId == "pickup") st.comHeight = 0.55f; } // t381/t384
        }
        [Tooltip("Cap on how fast physics pushes the body out of an overlap (m/s). Low = no launches.")] [Range(0.5f, 20f)] public float maxDepenetration = 3f;
        void OnEnable() { if (!All.Contains(this)) All.Add(this); ASHSaveSystem.Register(this); }
        void OnDisable() { All.Remove(this); ASHSaveSystem.Unregister(this); }
        [Tooltip("t415: engine power multiplier over the NWH maxPower set on the vehicle (1 = as authored). Applied at Play start; live while tuning.")] [Range(0.5f, 3f)] public float powerMultiplier = 1f;
        float _basePower = -1f;
        public float BasePower => _basePower;
        public void ApplyPower() { if (!_vc) return; if (_basePower < 0f) _basePower = _vc.engine.maxPower; _vc.engine.maxPower = _basePower * Mathf.Max(0.1f, powerMultiplier) * (1f + _slopeBoost); }
        void OnValidate() { if (Application.isPlaying && _vc) { ApplyPower(); ApplyReverse(); } }
        void Start() { if (_vc) { _vc.engine.runOnEnable = false; ApplyPower(); ApplyReverse(); } }

        // ---- t416 slopes: more pull uphill (forward or reverse), reverse gear as strong as 1st, hill-start push
        [Header("Slopes (t416)")]
        [Tooltip("Reverse gear ratio (magnitude). NWH ships -5 (weaker than 1st = 8). 0 = keep the authored ratio.")] [Range(0f, 20f)] public float reverseGearRatio = 8f;
        [Tooltip("Extra engine power when driving up a slope at Slope Full Angle or steeper (0.7 = +70%). Forward and reverse.")] [Range(0f, 3f)] public float slopePowerBoost = 0.7f;
        [Range(5f, 45f)] public float slopeFullAngle = 25f;
        [Tooltip("Fraction of the downhill pull of gravity cancelled while you drive up it (0 = none, 1 = feels flat).")] [Range(0f, 1f)] public float slopeGravityAssist = 0.5f;
        [Tooltip("Hill start: push (m/s^2) while the vehicle has not yet started moving the way you are driving (NWH brakes against roll-back until it does).")] [Range(0f, 10f)] public float hillStartAccel = 2.5f;
        [Tooltip("Hill start ends once moving this fast the right way, m/s.")] [Range(0.1f, 5f)] public float hillStartUntilSpeed = 1f;
        float _slopeBoost; float _authoredReverse = float.NaN; Rigidbody _rbody; ASHVehicleStability _stab;
        /// <summary>Debug: current slope climb angle (deg, + = driving uphill) and power boost.</summary>
        public float ClimbAngle { get; private set; }
        public float SlopeBoost => _slopeBoost;
        public void ApplyReverse()
        {
            var src = _vc ? _vc.transmission.ReverseGears : null; if (src == null || src.Count == 0) return;
            if (float.IsNaN(_authoredReverse)) _authoredReverse = src[0];
            var g = new System.Collections.Generic.List<float>(src) { [0] = reverseGearRatio > 0.01f ? -Mathf.Abs(reverseGearRatio) : _authoredReverse };
            _vc.transmission.ReverseGears = g; // setter rebuilds NWH's gear list
        }
        void FixedUpdate()
        {
            if (!_vc) return;
            float th = Driving == this && !_busy ? _vc.input.Vertical : 0f;
            float boost = 0f; ClimbAngle = 0f;
            if (Mathf.Abs(th) > 0.05f && _vc.engine.IsRunning)
            {
                if (!_rbody) _rbody = GetComponent<Rigidbody>(); if (!_stab) _stab = GetComponent<ASHVehicleStability>();
                float dir = Mathf.Sign(th);
                float climb = Mathf.Asin(Mathf.Clamp(Vector3.Dot(transform.forward, Vector3.up), -1f, 1f)) * dir; // rad, + = uphill the way we drive
                ClimbAngle = climb * Mathf.Rad2Deg;
                bool grounded = !_stab || _stab.GroundedWheels >= 2;
                if (climb > 0.017f && grounded && _rbody && !_rbody.isKinematic)
                {
                    boost = slopePowerBoost * Mathf.Clamp01(ClimbAngle / Mathf.Max(1f, slopeFullAngle));
                    float g = Physics.gravity.magnitude;
                    float along = _vc.ForwardVelocity * dir; // speed the way we want to go
                    float assist = along < hillStartUntilSpeed ? 1f : slopeGravityAssist;
                    var f = transform.forward * dir * (_rbody.mass * g * Mathf.Sin(climb) * assist * Mathf.Abs(th));
                    if (along < hillStartUntilSpeed) f += transform.forward * dir * (_rbody.mass * hillStartAccel * Mathf.Abs(th));
                    _rbody.AddForce(f, ForceMode.Force);
                }
            }
            float nb = Mathf.MoveTowards(_slopeBoost, boost, Time.fixedDeltaTime * 2f);
            if (!Mathf.Approximately(nb, _slopeBoost)) { _slopeBoost = nb; ApplyPower(); }
        }

        // ------------------------------------------------------------------ use (E at the driver door)
        public Transform UseTransform => exitPoint ? exitPoint : transform;
        public float GetUsePriority(in ASHUseContext ctx)
        {
            if (Destroyed) return Vector3.Distance(ctx.PlayerPosition, exitPoint ? exitPoint.position : transform.position) <= enterRange + 1.5f ? 6f : -1f;
            if (_busy || Driving) return -1f;
            var p = exitPoint ? exitPoint.position : transform.position;
            return Vector3.Distance(ctx.PlayerPosition, p) <= enterRange ? 6f : -1f;
        }
        public string GetPrompt(in ASHUseContext ctx) => Destroyed ? "Inspect " + displayName : useMenu && OpenMenu != null ? displayName : "Get in " + displayName;
        public float GetHoldDuration(in ASHUseContext ctx) => 0f;
        public bool TryUse(in ASHUseContext ctx)
        {
            if (Destroyed) { Inspected?.Invoke(this); return true; }
            if (_busy || Driving) return false;
            if (useMenu && OpenMenu != null) return OpenMenu(this, ctx.Inventory, ctx.PlayerTransform ? ctx.PlayerTransform : transform);
            StartCoroutine(EnterRoutine(PlayerRoot(ctx.PlayerTransform), false)); return true;
        }

        public void Enter(bool instant = false) { if (!_busy && !Driving && !Destroyed) StartCoroutine(EnterRoutine(PlayerRoot(null), instant)); }

        /// <summary>The player's root (the Invector controller with the Rigidbody), never a tagged child mesh (t369: the tagged
        /// object is the character model, so moving it left the real body, its colliders and the camera target behind).</summary>
        public static GameObject PlayerRoot(Transform hint)
        {
            Invector.vCharacterController.vThirdPersonController c = hint ? hint.GetComponentInParent<Invector.vCharacterController.vThirdPersonController>() : null;
            if (!c) c = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>();
            if (c) return c.gameObject;
            var tagged = GameObject.FindWithTag("Player"); if (!tagged) return hint ? hint.gameObject : null;
            var rb = tagged.GetComponentInParent<Rigidbody>(); return rb ? rb.gameObject : tagged.transform.root.gameObject;
        }
        public void Exit(bool force = false) { if (!_busy && Driving == this) StartCoroutine(ExitRoutine(force)); }

        IEnumerator EnterRoutine(GameObject player, bool instant)
        {
            if (!player) yield break;
            _busy = true; Driving = this; ASHInput.VehicleMode = true;
            SeatPlayer(player, false);                         // controls off now, body still standing at the door
            if (driverDoor && !instant) yield return driverDoor.OpenRoutine();
            SeatPlayer(player, true);                          // into the seat (t389: visible driver, else body hidden)
            BeginCamera();
            if (ShowDriver) yield return _driver.SitIn(player, _panim, seat ? seat : transform);
            if (driverDoor && !instant) StartCoroutine(driverDoor.CloseRoutine());
            _vc.Active = true; _vc.input.Handbrake = 0f;
            if (autoStartEngine) TryStartEngine(!instant);
            if (_lights)
            {
                bool on = _lights.lightsOnEnter;
                if (!on && _lights.autoOnAtNight) { var clk = FindAnyObjectByType<ASH.World.ASHDayNightClock>(); on = clk && clk.IsNight; }
                if (on && (!_cond || _cond.HasLightPower)) _lights.Set(true);
            }
            if (carriesDog) { var dog = ASH.Companion.ASHDogCompanion.Active; if (dog && dogSeat) dog.BeginRide(dogSeat, dogTailgate ? dogTailgate : dogSeat, dogPassengerSeat ? passengerSeat : null, dogPassengerSeat ? passengerDoor : null, dogRideRange, instant); }
            ASHVehicleHud.Show(this);
            _enteredAt = Time.time; _busy = false;
            DrivingChanged?.Invoke(this);
        }

        IEnumerator ExitRoutine(bool force)
        {
            if (!force && SpeedKph > maxExitSpeedKph) { Toast("Slow down to get out."); yield break; }
            if (!FindExit(out var spot, out var face)) { if (!force) { Toast("No room to get out here."); yield break; } spot = transform.position + Vector3.up * 2.5f; face = transform.rotation; }
            _busy = true;
            _vc.input.Vertical = 0f; _vc.input.Horizontal = 0f; _vc.input.Handbrake = 1f;
            if (driverDoor && !force) yield return driverDoor.OpenRoutine();
            EndCamera();
            if (_driver && _driver.Seated) { if (force) _driver.Restore(); else yield return _driver.SitOut(_player, spot, face); }
            UnseatPlayer(spot, face);
            if (driverDoor && !force) StartCoroutine(driverDoor.CloseRoutine());
            if (stopEngineOnExit && _vc.engine.IsRunning) _vc.engine.Stop();
            if (_lights) _lights.Set(false);   // lights only while someone is in it (t361)
            if (carriesDog) { var dog = ASH.Companion.ASHDogCompanion.Active; if (dog && dog.Riding) dog.EndRide(spot); }
            ASHVehicleHud.Show(null);
            Driving = null; ASHInput.VehicleMode = false; _busy = false;
            yield return new WaitForSeconds(0.5f);
            if (Driving != this && _vc && SpeedKph < 1f) _vc.Active = false;
            DrivingChanged?.Invoke(null);
        }

        // ------------------------------------------------------------------ driving
        void Update()
        {
            if (!_vc) return;
            bool running = _vc.engine.IsRunning;
            float dt = Time.deltaTime;
            if (Driving == this && !_busy)
            {
                var input = FindInput();
                var kb = Keyboard.current;
                Vector2 mv = input ? input.Move : Vector2.zero;
                _steer = Mathf.MoveTowards(_steer, mv.x, steerResponse * dt);
                _throttle = Mathf.MoveTowards(_throttle, mv.y, throttleResponse * dt);
                _vc.input.Horizontal = _steer; _vc.input.Vertical = _throttle;
                if (holdBrakeUntilInput && mv.sqrMagnitude > 0.04f) holdBrakeUntilInput = false;
                _vc.input.Handbrake = kb != null && kb.spaceKey.isPressed || holdBrakeUntilInput ? 1f : 0f;
                _vc.input.horn = kb != null && kb.hKey.isPressed;
                if (input && !input.GameplayBlocked)
                {
                    if (input.VehicleCameraPressed) _cam.ToggleView();
                    if (input.VehicleLightsPressed && _lights) { _lights.Toggle(); if (_lights.On && _cond && !_cond.HasLightPower) { _lights.Set(false); Toast("The battery is dead."); } }
                    if (kb != null && kb.rKey.wasPressedThisFrame && !running && autoStartEngine) TryStartEngine(true);
                    if (input.InteractPressed && Time.time > _enteredAt + 0.5f) Exit();
                }
                if (_lights) _lights.SetBraking(running && (_throttle < -0.1f && _vc.ForwardVelocity > 0.5f || _vc.input.Handbrake > 0.5f));
            }
            else if (_lights && _lights.Braking) _lights.SetBraking(false);
            if (_fuel)
            {
                float rpm01 = running ? Mathf.InverseLerp(_vc.engine.minRPM, _vc.engine.maxRPM, _vc.engine.RPM) : 0f;
                _fuel.Tick(dt, running, Driving == this ? Mathf.Max(0f, _throttle) : 0f, rpm01);
                if (running && _fuel.Empty) { _vc.engine.Stop(); if (Driving == this) Toast("The engine sputters out. Out of gas."); }
            }
            if (_cond)
            {
                _cond.Tick(dt, running, LightsOn);
                if (LightsOn && !_cond.HasLightPower) _lights.Set(false);
            }
        }

        void TryStartEngine(bool say)
        {
            if (_vc.engine.IsRunning) return;
            string why = _cond ? _cond.WhyNoStart(_fuel) : (_fuel && _fuel.Empty ? "Out of gas." : null);
            if (why != null) { if (say) Toast(why); return; }
            _vc.engine.Start(); if (_cond) _cond.OnStarted();
        }

        // ------------------------------------------------------------------ player in / out
        void SeatPlayer(GameObject p, bool intoSeat)
        {
            if (!intoSeat)
            {
                _player = p; _off.Clear(); _cols.Clear(); _hidden.Clear();
                foreach (var b in p.GetComponentsInChildren<Behaviour>(true))
                {
                    if (!b || !b.enabled) continue;
                    if (b is Invector.vCharacterController.vThirdPersonInput || b is Invector.vCharacterController.vThirdPersonController || b is ASHInteractor
                        || b is ASH.Gameplay.ASHEquipment || b is ASH.Gameplay.ASHWeaponAim)
                    { b.enabled = false; _off.Add(b); }
                }
                _prb = p.GetComponent<Rigidbody>();
                if (_prb) { _prbKin = _prb.isKinematic; _prbDet = _prb.detectCollisions; _prbInterp = _prb.interpolation; _prb.interpolation = RigidbodyInterpolation.None; /* t409: an interpolated kinematic body lags its moving parent (bob over bumps) */ if (!_prb.isKinematic) _prb.linearVelocity = Vector3.zero; _prb.isKinematic = true; _prb.detectCollisions = false; }
                foreach (var c in p.GetComponentsInChildren<Collider>()) if (c.enabled) { c.enabled = false; _cols.Add(c); }
                _panim = p.GetComponentInChildren<Animator>(); _panimOn = _panim && _panim.enabled;
                return;
            }
            _pParent = p.transform.parent;
            if (ShowDriver) { p.transform.SetParent(seat ? seat : transform, true); return; }   // t389: ASHVehicleDriver slides it in and animates it
            if (_panim) _panim.enabled = false;
            p.transform.SetParent(seat ? seat : transform, false);
            p.transform.localPosition = Vector3.zero; p.transform.localRotation = Quaternion.identity;
            foreach (var r in p.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; _hidden.Add(r); }
        }

        bool ShowDriver => _driver && _driver.showDriver && _panim;
        void LateUpdate() { if (passengerSeat && _passBase != Vector3.zero) passengerSeat.localPosition = _passBase + passengerSeatOffset; }

        void UnseatPlayer(Vector3 spot, Quaternion face)
        {
            var p = _player; if (!p) return;
            p.transform.SetParent(_pParent, true);
            p.transform.SetPositionAndRotation(spot, face);
            if (_prb) { _prb.position = spot; _prb.rotation = face; }
            Physics.SyncTransforms();
            StartCoroutine(IgnoreVehicleCollisions(_cols.ToArray(), 1.0f));
            foreach (var r in _hidden) if (r) r.enabled = true;
            foreach (var c in _cols) if (c) c.enabled = true;
            if (_prb) { _prb.interpolation = _prbInterp; _prb.isKinematic = _prbKin; _prb.detectCollisions = _prbDet; if (!_prb.isKinematic) _prb.linearVelocity = Vector3.zero; }
            if (_panim) _panim.enabled = _panimOn;
            foreach (var b in _off) if (b) b.enabled = true;
            _off.Clear(); _cols.Clear(); _hidden.Clear(); _player = null;
        }

        void BeginCamera()
        {
            _mainCam = Camera.main; if (!_mainCam) return;
            var tp = Invector.vCamera.vThirdPersonCamera.instance; _tpCam = tp && tp.enabled ? tp : null; if (_tpCam) _tpCam.enabled = false;
            _camParent = _mainCam.transform.parent; _camLocalPos = _mainCam.transform.localPosition; _camLocalRot = _mainCam.transform.localRotation;
            _cam.head = head; _cam.Begin(_mainCam);
        }
        void EndCamera()
        {
            _cam.End();
            if (_mainCam) { _mainCam.transform.SetParent(_camParent, false); _mainCam.transform.localPosition = _camLocalPos; _mainCam.transform.localRotation = _camLocalRot; }
            var tp = Invector.vCamera.vThirdPersonCamera.instance;
            if (tp)
            {
                tp.enabled = true;
                if (_player) { tp.SetMainTarget(_player.transform); tp.ResetAngleWithoutSmooth(); }
            }
            if (_mainCam) { _mainCam.enabled = true; if (!_mainCam.CompareTag("MainCamera")) _mainCam.tag = "MainCamera"; }
            _tpCam = null; _mainCam = null;
        }

        /// <summary>Clear ground by the driver door, else the other side, the back, the front.</summary>
        bool FindExit(out Vector3 spot, out Quaternion face)
        {
            spot = default; face = Quaternion.identity;
            var b = LocalBounds();
            var cands = new List<Vector3>();
            if (exitPoint) cands.Add(transform.InverseTransformPoint(exitPoint.position));
            float side = b.extents.x + 0.75f;
            cands.Add(new Vector3(-side, 0f, b.center.z + 0.3f)); cands.Add(new Vector3(side, 0f, b.center.z + 0.3f));
            cands.Add(new Vector3(0f, 0f, b.min.z - 1.0f)); cands.Add(new Vector3(0f, 0f, b.max.z + 1.0f));
            cands.Add(new Vector3(-side - 0.8f, 0f, b.center.z)); cands.Add(new Vector3(side + 0.8f, 0f, b.center.z));
            foreach (var lp in cands)
            {
                var w = transform.TransformPoint(new Vector3(lp.x, b.center.y, lp.z));
                if (!GroundAt(w + Vector3.up * 2.5f, 6f, out var g)) continue;
                if (Blocked(g)) continue;
                spot = g + Vector3.up * 0.05f;
                var away = Vector3.ProjectOnPlane(g - transform.position, Vector3.up);
                face = Quaternion.LookRotation(away.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, away).normalized * (lp.x < 0f ? 1f : -1f) : transform.forward, Vector3.up);
                face = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, Vector3.up);
                return true;
            }
            return false;
        }
        IEnumerator IgnoreVehicleCollisions(Collider[] playerCols, float seconds)
        {
            var mine = GetComponentsInChildren<Collider>(true);
            foreach (var a in playerCols) foreach (var b in mine) if (a && b) Physics.IgnoreCollision(a, b, true);
            yield return new WaitForSeconds(seconds);
            foreach (var a in playerCols) foreach (var b in mine) if (a && b) Physics.IgnoreCollision(a, b, false);
        }

        bool GroundAt(Vector3 from, float dist, out Vector3 g)
        {
            g = default; float best = float.MaxValue; bool ok = false;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, dist, ~0, QueryTriggerInteraction.Ignore))
            { if (Mine(h.collider)) continue; if (h.distance < best) { best = h.distance; g = h.point; ok = true; } }
            return ok;
        }
        bool Blocked(Vector3 g)
        {
            foreach (var c in Physics.OverlapCapsule(g + Vector3.up * 0.45f, g + Vector3.up * 1.6f, 0.32f, ~0, QueryTriggerInteraction.Ignore))
                if (!Mine(c) && !(c is TerrainCollider)) return true;
            return false;
        }
        bool Mine(Collider c) => c.transform.IsChildOf(transform) || (_player && c.transform.IsChildOf(_player.transform)) || c.CompareTag("Player");
        Bounds LocalBounds()
        {
            var b = new Bounds(Vector3.zero, Vector3.zero); bool f = true;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                var lb = r.bounds; var c = transform.InverseTransformPoint(lb.center); var e = lb.extents;
                var bb = new Bounds(c, e * 2f); if (f) { b = bb; f = false; } else b.Encapsulate(bb);
            }
            return b;
        }

        ASHInput _in; ASHInput FindInput() => _in ? _in : _in = FindAnyObjectByType<ASHInput>();
        static void Toast(string m) { var h = FindAnyObjectByType<ASH.UI.ASHHud>(); if (h) h.Toast(m); else Debug.Log("[ASH] Vehicle: " + m); }

        // ------------------------------------------------------------------ save
        [Serializable] sealed class State { public Vector3 pos; public Quaternion rot; public float fuel = -1f, battery = 1f, belt = 1f; public bool hasBattery = true, hasBelt = true, lights, occupied, engine; public int view, tireMask; public bool destroyed; }
        bool _restored;
        public string SaveKey => "vehicle_" + vehicleId;
        public int RestoreOrder => 40;
        public string CaptureJson()
        {
            var s = new State { pos = transform.position, rot = transform.rotation, fuel = _fuel ? _fuel.litres : -1f, lights = LightsOn, occupied = Driving == this, engine = EngineRunning, view = (int)_cam.view, destroyed = Destroyed };
            if (_cond) { s.battery = _cond.batteryCharge; s.belt = _cond.beltCondition; s.hasBattery = _cond.batteryInstalled; s.hasBelt = _cond.beltInstalled; s.tireMask = _cond.MissingMask; }
            return JsonUtility.ToJson(s);
        }
        public void RestoreJson(string json)
        {
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json); if (s == null) return;
            _restored = true;
            if (Driving == this) Exit(true);
            var rb = GetComponent<Rigidbody>();
            if (rb) { rb.position = s.pos; rb.rotation = s.rot; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            transform.SetPositionAndRotation(s.pos, s.rot);
            if (_fuel && s.fuel >= 0f) _fuel.litres = Mathf.Clamp(s.fuel, 0f, _fuel.capacityLitres);
            if (_cond) { _cond.batteryCharge = s.battery; _cond.beltCondition = s.belt; _cond.batteryInstalled = s.hasBattery; _cond.beltInstalled = s.hasBelt; _cond.SetMissingMask(s.tireMask); }
            SetDestroyed(s.destroyed);
            if (_lights) _lights.Set(s.lights && s.occupied && !s.destroyed);
            if (_cam) _cam.view = (ASHVehicleCamera.View)Mathf.Clamp(s.view, 0, 1);
            if (s.occupied) StartCoroutine(EnterNextFrame());
            else { var st = GetComponent<ASHVehicleSettle>(); if (st) st.QueueLoadCheck(); } // t414: repaired but tilted -> settle
        }
        IEnumerator EnterNextFrame() { yield return null; yield return null; Enter(true); }
    }
}
