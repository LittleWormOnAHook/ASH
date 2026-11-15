using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Cosmetic recoil camera kick: an additive pitch/yaw offset applied after the Invector camera, removed again
    /// next frame so it never accumulates into the camera's own rotation.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    public sealed class ASHCameraKick : MonoBehaviour
    {
        [Range(1f, 40f)] public float recoverSpeed = 10f;
        Vector2 _kick; Quaternion _before, _after; bool _applied;
        public float Pitch => _kick.x;

        public static void Kick(float pitchDeg, float yawDeg)
        {
            var cam = Camera.main; if (!cam) return;
            var k = cam.GetComponent<ASHCameraKick>(); if (!k) k = cam.gameObject.AddComponent<ASHCameraKick>();
            k._kick += new Vector2(pitchDeg, yawDeg);
        }

        void LateUpdate()
        {
            if (_applied && transform.rotation == _after) transform.rotation = _before; // undo last frame if nobody rewrote it
            _applied = false;
            if (_kick.sqrMagnitude < 1e-6f) { _kick = Vector2.zero; return; }
            _before = transform.rotation;
            transform.rotation = _before * Quaternion.Euler(-_kick.x, _kick.y, 0f);
            _after = transform.rotation; _applied = true;
            _kick = Vector2.Lerp(_kick, Vector2.zero, 1f - Mathf.Exp(-recoverSpeed * Time.deltaTime));
        }
    }
}
