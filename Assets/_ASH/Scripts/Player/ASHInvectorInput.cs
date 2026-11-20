using ASH.Core;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// ASH adapter over Invector's full shooter + melee input (vShooterMeleeInput, which extends vMeleeCombatInput and
    /// vThirdPersonInput, as in Genesis). Invector stays unmodified; this subclass feeds locomotion from <see cref="ASHInput"/>
    /// (Unity Input System) and disables features ASH does not use (roll, strafe toggle, blocking, strong attack, scope,
    /// camera-side switch). R reloads guns (Invector reload; rounds come from the ASH pack). Combat: LMB attacks with a melee weapon
    /// (axe, clubs) or fires/looses while RMB aims a gun or draws the bow (Invector archery: hold LMB to charge bow power).
    /// Melee only runs while ASH says so (a melee weapon is held, not chopping, no UI open): see ASH.Combat.ASHCombatBridge.
    /// Sprint is hold-to-sprint.
    /// </summary>
    public class ASHInvectorInput : vShooterMeleeInput
    {
        [Header("ASH")]
        [SerializeField] private ASHInput ashInput;
        [Tooltip("Hold Sprint to sprint (off = Invector toggle sprint, as in Genesis).")]
        [SerializeField] private bool holdToSprint = true;

        public ASHInput AshInput => ashInput;

        [Header("ASH Zoom (mouse wheel)")]
        [Tooltip("Step zoom: each wheel click moves a fixed step, eased smoothly. Off = Invector's raw scroll zoom.")]
        [SerializeField] private bool stepZoom = true;
        [Tooltip("Wheel clicks to go from closest to farthest (and back).")]
        [Range(1, 30)] [SerializeField] private int zoomClicksAcrossRange = 6;
        [Tooltip("Closest follow distance (m). The free-look camera state floors this at 1.5 m.")]
        [Range(0.5f, 10f)] [SerializeField] private float zoomMinDistance = 1.5f;
        [Tooltip("Farthest follow distance (m). 6 m matches Invector's own default max (its Default state is 3.5 m, Crouch 6.55 m).")]
        [Range(2f, 40f)] [SerializeField] private float zoomFarDistance = 6f; // was zoomMaxDistance (12 m was too far) so old values reset
        [Tooltip("Seconds to ease to the next click's distance (smaller = snappier).")]
        [Range(0.01f, 1.5f)] [SerializeField] private float zoomSmoothTime = 0.18f;
        [Tooltip("Raw scroll units per wheel click (Windows mice report 120; 0 = auto-detect 120 or 1).")]
        [Range(0f, 240f)] [SerializeField] private float scrollPerClick = 0f;

        [Tooltip("After aiming (any weapon or light), ease back to the free-look distance you had scrolled to before the aim.")]
        [SerializeField] private bool restoreZoomAfterAim = true;
        float _zTarget, _zEased, _zVel, _zLastSet = -1f; bool _zInit;
        bool _zAiming, _zHaveSaved; float _zSaved, _zRestoreUntil;
        /// <summary>Free-look distance the scroll zoom is easing to (diagnostics / tests).</summary>
        public float ZoomTarget => _zTarget;

        protected override void Start()
        {
            if (ashInput == null) ashInput = GetComponent<ASHInput>();
            // fallback if ASH/Systems/Combat setup was not run on this player: the shooter input needs its managers
            if (!GetComponent<Invector.vShooter.vShooterManager>()) gameObject.AddComponent<Invector.vShooter.vShooterManager>().useAmmoDisplay = false;
            if (!GetComponent<Invector.vMelee.vMeleeManager>()) gameObject.AddComponent<Invector.vMelee.vMeleeManager>();
            if (!GetComponent<Invector.vItemManager.vAmmoManager>()) gameObject.AddComponent<Invector.vItemManager.vAmmoManager>();
            if (!GetComponent<ASH.Combat.ASHCombatBridge>()) gameObject.AddComponent<ASH.Combat.ASHCombatBridge>();
            base.Start();
            if (cc != null) cc.useContinuousSprint = !holdToSprint;
            // ASH has no blocking, shields, scope, camera-side switch or strong attack: those Invector keys clash
            // with ASH binds (Tab = pack, Z = weapon quad, 1 = hotbar), so they are switched off here.
            if (strongAttackInput != null) strongAttackInput.useInput = false;
            if (blockInput != null) blockInput.useInput = false;
            if (switchCameraSideInput != null) switchCameraSideInput.useInput = false;
            if (scopeViewInput != null) scopeViewInput.useInput = false;
            if (strafeInput != null) strafeInput.useInput = false;
            if (crouchInput != null) crouchInput.useInput = false;   // crouch is ASHInput (Left Ctrl); C is the dog call key
            ASHUIState.ApplyCursor();
        }

        /// <summary>Set by ASH.Combat.ASHCombatBridge: true while a melee weapon is out and nothing else owns LMB.</summary>
        public System.Func<bool> MeleeAllowed;
        /// <summary>Set by ASH.Combat.ASHCombatBridge: true while an Invector gun / bow is out.</summary>
        public System.Func<bool> ShooterAllowed;

        protected override bool MeleeAttackConditions()
        {
            if (ASHUIState.BlocksGameplay) return false;
            if (MeleeAllowed != null && !MeleeAllowed()) return false;
            return base.MeleeAttackConditions();
        }

        public override void BlockingInput() { isBlocking = false; } // no shields / blocking in ASH
        public override void MeleeStrongAttackInput() { }            // no strong attack bind yet (data-driven later)

        public override void AimInput()
        {
            if (ShooterAllowed != null && !ShooterAllowed()) { isAimingByInput = false; return; }
            base.AimInput();
            // test harness (ASHInput.SimSecondaryHeld): Invector reads RMB through its own GenericInput, so simulated aim never reached it
            if (ashInput && ashInput.SimSecondaryHeld == true && shooterManager && CurrentActiveWeapon && !isEquipping && !cc.ragdolled && !cc.customAction) isAimingByInput = true;
        }

        public override void ShotInput()
        {
            if (ShooterAllowed != null && !ShooterAllowed()) return;
            // test harness (ASHInput.SimDraw): hold the bow at a set draw
            var w = CurrentActiveWeapon;
            if (ashInput && ashInput.SimDraw >= 0f && IsAiming && w && w.chargeWeapon)
            {
                w.powerCharge = Mathf.Clamp01(ashInput.SimDraw);
                if (cc && cc.animator) cc.animator.SetFloat("PowerCharger", w.powerCharge);
                return;
            }
            base.ShotInput();
        }

        public override void InputHandle()
        {
            if (ashInput == null) return;
            if (ASHUIState.BlocksGameplay)
            {
                cc.input = Vector3.zero;
                if (cc.isSprinting) cc.Sprint(false);
                return;
            }
            base.InputHandle();
        }

        public override void MoveInput()
        {
            if (!lockMoveInput)
            {
                Vector2 m = ashInput.Move;
                cc.input = new Vector3(m.x, 0f, m.y);
            }
            if (ashInput.WalkTogglePressed) cc.alwaysWalkByDefault = !cc.alwaysWalkByDefault;
            cc.ControlKeepDirection();
        }

        public override void SprintInput()
        {
            bool held = ashInput.SprintHeld;
            cc.Sprint(cc.useContinuousSprint ? held && !cc.isSprinting : held);
        }

        public override void CrouchInput()
        {
            cc.AutoCrouch();
            if (ashInput.CrouchPressed) cc.Crouch();
        }

        public override void JumpInput()
        {
            if (ashInput.JumpPressed && JumpConditions()) cc.Jump(true);
        }

        public override void StrafeInput() { }
        public override void RollInput() { }

        ASHAimPoseRestore _poseRestore; ASH.Combat.ASHAimCameraSync _aimSync;

        /// <summary>
        /// Aim jitter fix. Invector only reads camera input and runs its aim IK on frames that had a physics step (its Animator is
        /// on Fixed), so with a gun or the bow aimed the arms/weapon and the mouse look stepped at physics rate against the
        /// per-frame camera. On the other frames this reads the camera input (ASHCombatTuning.perFrameCameraInput) and, while a
        /// ranged weapon is out, runs the aim IK too (perFrameAimIK) on the raw animated pose that ASHAimPoseRestore puts back.
        /// The Animator itself stays on Fixed (running it per frame made the jitter much worse).
        /// </summary>
        protected override void LateUpdate()
        {
            if (cc == null) return;
            var t = ASH.Combat.ASHCombatTuning.Live;
            bool fixedAnim = animator && animator.updateMode == AnimatorUpdateMode.Fixed;
            bool ranged = shooterManager && CurrentActiveWeapon != null;
            bool aimEveryFrame = t.perFrameAimIK && ranged && fixedAnim;
            if (!_poseRestore && animator) { _poseRestore = animator.GetComponent<ASHAimPoseRestore>(); if (!_poseRestore) _poseRestore = animator.gameObject.AddComponent<ASHAimPoseRestore>(); }
            if (_poseRestore) _poseRestore.restoreOnStaleFrames = aimEveryFrame;
            if (!_aimSync && animator) { _aimSync = animator.GetComponent<ASH.Combat.ASHAimCameraSync>(); if (!_aimSync) _aimSync = animator.gameObject.AddComponent<ASH.Combat.ASHAimCameraSync>(); }
            if (updateIK || !fixedAnim) { base.LateUpdate(); return; } // physics-step frame: Invector's own path
            if (t.perFrameCameraInput) { CameraInput(); UpdateCameraStates(); }
            if (aimEveryFrame && !cc.isDead) UpdateAimBehaviour();
        }

        public override void CameraInput()
        {
            if (!cameraMain || tpCamera == null) return;
            Vector2 look = lockCameraInput ? Vector2.zero : ashInput.Look;
            if (invertCameraInputHorizontal) look.x = -look.x;
            if (invertCameraInputVertical) look.y = -look.y;
            tpCamera.RotateCamera(look.x, look.y);
            if (!lockCameraInput) { if (stepZoom) StepZoom(); else tpCamera.Zoom(ashInput.Zoom); }
        }

        /// <summary>
        /// Click-stepped zoom: the full min-max range takes zoomClicksAcrossRange wheel clicks, eased with SmoothDamp, fed to
        /// Invector through SetZoomTarget (its own culling/collision and state handling stay in charge). Aim/Scope states
        /// keep their authored distances. If anything else changes the zoom (state change, camera reset) we adopt it.
        /// </summary>
        void StepZoom()
        {
            var st = tpCamera.currentState;
            bool aim = st != null && !string.IsNullOrEmpty(st.Name) &&
                       (st.Name.IndexOf("Aim", System.StringComparison.OrdinalIgnoreCase) >= 0 || st.Name.IndexOf("Scope", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (aim)
            {
                if (!_zAiming) { _zAiming = true; _zHaveSaved = _zInit; _zSaved = _zTarget; } // remember the scrolled distance
                _zInit = false; return;
            }
            bool restoring = false;
            if (_zAiming) { _zAiming = false; if (restoreZoomAfterAim && _zHaveSaved) { _zRestoreUntil = Time.unscaledTime + 0.75f; restoring = true; } }
            tpCamera.Zoom(0f); // lets Invector set up the free-look zoom range on this state (no movement)
            float min = Mathf.Min(zoomMinDistance, zoomFarDistance - 0.1f), max = Mathf.Max(zoomFarDistance, min + 0.1f);
            if (st != null && (min < st.minDistance || max > st.maxDistance)) { st.minDistance = Mathf.Min(st.minDistance, min); st.maxDistance = Mathf.Max(st.maxDistance, max); } // only when widened past Invector's 1.5-12 m
            float cur = tpCamera.CurrentZoom;
            if (restoring || (Time.unscaledTime < _zRestoreUntil && _zHaveSaved))
            {
                // Invector snapped the zoom to the state's default when the aim ended: ease from there back to the saved distance
                if (restoring || Mathf.Abs(cur - _zLastSet) > 0.05f) { _zEased = Mathf.Clamp(cur, min, max); _zVel = 0f; }
                _zTarget = Mathf.Clamp(_zSaved, min, max); _zInit = true;
            }
            else if (!_zInit || Mathf.Abs(cur - _zLastSet) > 0.05f) { _zTarget = _zEased = Mathf.Clamp(cur, min, max); _zVel = 0f; _zInit = true; }

            float raw = ashInput.Zoom * 1200f; // ASHInput divides by 1200 and zeroes it while UI blocks gameplay
            if (Mathf.Abs(raw) > 1e-4f)
            {
                float notch = scrollPerClick > 0f ? scrollPerClick : (Mathf.Abs(raw) >= 30f ? 120f : 1f);
                float clicks = Mathf.Sign(raw) * Mathf.Max(1f, Mathf.Round(Mathf.Abs(raw) / notch));
                _zTarget = Mathf.Clamp(_zTarget - clicks * (max - min) / Mathf.Max(1, zoomClicksAcrossRange), min, max);
            }
            _zEased = Mathf.SmoothDamp(_zEased, _zTarget, ref _zVel, zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            if (Mathf.Abs(_zEased - _zTarget) < 0.001f) { _zEased = _zTarget; _zVel = 0f; }
            tpCamera.SetZoomTarget(_zEased);
            _zLastSet = tpCamera.CurrentZoom;
        }
    }
}
