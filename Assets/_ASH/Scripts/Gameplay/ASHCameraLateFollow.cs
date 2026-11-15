using Invector.vCamera;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Smooth third-person follow, copied from Genesis's DMCameraLateFollow (the walk-jitter fix): drives Invector's camera in
    /// LateUpdate every rendered frame (vThirdPersonCamera.DMFollowInLateUpdate, already in ASH's Invector copy) and turns on
    /// Rigidbody interpolation on the player, so the camera follows the interpolated body instead of the 50 Hz physics pose.
    /// Added to the camera at scene load. Order 600 = after the player input LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(600)]
    [DisallowMultipleComponent]
    public sealed class ASHCameraLateFollow : MonoBehaviour
    {
        public static bool Enabled = true;
        public bool interpolatePlayer = true;
        vThirdPersonCamera _cam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Enabled) return;
            var cam = vThirdPersonCamera.instance ? vThirdPersonCamera.instance : FindAnyObjectByType<vThirdPersonCamera>();
            if (cam && !cam.GetComponent<ASHCameraLateFollow>()) cam.gameObject.AddComponent<ASHCameraLateFollow>();
        }

        void Awake() { _cam = GetComponent<vThirdPersonCamera>(); }

        void OnEnable()
        {
            vThirdPersonCamera.DMFollowInLateUpdate = true;
            if (interpolatePlayer)
            {
                var cc = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>();
                var rb = cc ? cc.GetComponent<Rigidbody>() : null;
                if (rb) rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        void OnDisable() { vThirdPersonCamera.DMFollowInLateUpdate = false; }

        void LateUpdate() { if (_cam && _cam.enabled) _cam.DMLateTick(); }
    }
}
