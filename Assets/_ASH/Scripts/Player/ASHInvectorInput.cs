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
            if (!lockCameraInput) tpCamera.Zoom(ashInput.Zoom);
        }
    }
}
