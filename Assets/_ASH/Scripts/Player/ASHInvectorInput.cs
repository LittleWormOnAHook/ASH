using ASH.Core;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// ASH adapter over Invector's vThirdPersonInput (the controller Genesis uses). Invector stays unmodified;
    /// this subclass feeds it from <see cref="ASHInput"/> (Unity Input System) instead of legacy axes and
    /// disables features ASH does not use (roll, strafe toggle). Sprint is hold-to-sprint for the slice.
    /// </summary>
    public class ASHInvectorInput : vThirdPersonInput
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
        [Tooltip("Farthest follow distance (m).")]
        [Range(2f, 40f)] [SerializeField] private float zoomMaxDistance = 12f;
        [Tooltip("Seconds to ease to the next click's distance (smaller = snappier).")]
        [Range(0.01f, 1.5f)] [SerializeField] private float zoomSmoothTime = 0.18f;
        [Tooltip("Raw scroll units per wheel click (Windows mice report 120; 0 = auto-detect 120 or 1).")]
        [Range(0f, 240f)] [SerializeField] private float scrollPerClick = 0f;

        float _zTarget, _zEased, _zVel, _zLastSet = -1f; bool _zInit;

        protected override void Start()
        {
            if (ashInput == null) ashInput = GetComponent<ASHInput>();
            base.Start();
            if (cc != null) cc.useContinuousSprint = !holdToSprint;
            ASHUIState.ApplyCursor();
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
            if (aim) { _zInit = false; return; }
            tpCamera.Zoom(0f); // lets Invector set up the free-look zoom range on this state (no movement)
            float min = Mathf.Min(zoomMinDistance, zoomMaxDistance - 0.1f), max = Mathf.Max(zoomMaxDistance, min + 0.1f);
            if (st != null && (min < st.minDistance || max > st.maxDistance)) { st.minDistance = Mathf.Min(st.minDistance, min); st.maxDistance = Mathf.Max(st.maxDistance, max); } // only when widened past Invector's 1.5-12 m
            float cur = tpCamera.CurrentZoom;
            if (!_zInit || Mathf.Abs(cur - _zLastSet) > 0.05f) { _zTarget = _zEased = Mathf.Clamp(cur, min, max); _zVel = 0f; _zInit = true; }

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
