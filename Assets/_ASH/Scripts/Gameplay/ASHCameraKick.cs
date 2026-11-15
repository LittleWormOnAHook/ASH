using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Cosmetic camera offsets applied after the Invector camera and removed again next frame so they never
    /// accumulate into the camera's own transform: recoil kick (decaying pitch/yaw) and an externally driven shake
    /// (rotation + position, e.g. ASHEarthquakeEffects).</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    public sealed class ASHCameraKick : MonoBehaviour
    {
        [Range(1f, 40f)] public float recoverSpeed = 10f;
        Vector2 _kick; Quaternion _before, _after; bool _applied;
        Vector3 _shakeRot, _shakePos, _posBefore, _posAfter;
        public float Pitch => _kick.x;
        public static Vector3 ShakeRotation { get; private set; }

        static ASHCameraKick Get()
        {
            var cam = Camera.main; if (!cam) return null;
            var k = cam.GetComponent<ASHCameraKick>(); if (!k) k = cam.gameObject.AddComponent<ASHCameraKick>();
            return k;
        }

        public static void Kick(float pitchDeg, float yawDeg)
        {
            var k = Get(); if (k) k._kick += new Vector2(pitchDeg, yawDeg);
        }

        /// <summary>Set this frame's shake (degrees pitch/yaw/roll, metres in camera space). Zero = none. Holds until changed.</summary>
        public static void SetShake(Vector3 rotationDeg, Vector3 offsetMetres)
        {
            ShakeRotation = rotationDeg;
            if (rotationDeg == Vector3.zero && offsetMetres == Vector3.zero)
            {
                var cam = Camera.main; var e = cam ? cam.GetComponent<ASHCameraKick>() : null;
                if (e) { e._shakeRot = Vector3.zero; e._shakePos = Vector3.zero; }
                return;
            }
            var k = Get(); if (k) { k._shakeRot = rotationDeg; k._shakePos = offsetMetres; }
        }

        void OnDisable() { Undo(); }

        void Undo()
        {
            if (!_applied) return;
            if (transform.rotation == _after) transform.rotation = _before; // undo last frame if nobody rewrote it
            if (transform.position == _posAfter) transform.position = _posBefore;
            _applied = false;
        }

        void LateUpdate()
        {
            Undo();
            bool kick = _kick.sqrMagnitude >= 1e-6f; if (!kick) _kick = Vector2.zero;
            bool shake = _shakeRot != Vector3.zero || _shakePos != Vector3.zero;
            if (!kick && !shake) return;
            _before = transform.rotation; _posBefore = transform.position;
            transform.rotation = _before * Quaternion.Euler(-_kick.x + _shakeRot.x, _kick.y + _shakeRot.y, _shakeRot.z);
            if (_shakePos != Vector3.zero) transform.position = _posBefore + _before * _shakePos;
            _after = transform.rotation; _posAfter = transform.position; _applied = true;
            _kick = Vector2.Lerp(_kick, Vector2.zero, 1f - Mathf.Exp(-recoverSpeed * Time.deltaTime));
        }
    }
}
