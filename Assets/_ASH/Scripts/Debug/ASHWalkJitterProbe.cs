using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Short A/B (Play Mode, exec ASH.DebugTools.ASHWalkJitterProbe.Run): runs straight ahead with Invector's stock motor
    /// reads (transform.position) and then with the physics-pose reads, logging how much the body's speed surges step to step and
    /// how far the rendered transform lags the body in FixedUpdate. "[ASH] WALKJITTER ...".</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class ASHWalkJitterProbe : MonoBehaviour
    {
        readonly List<float> _jerk = new List<float>(), _cj = new List<float>(); Vector3 _a, _b, _ca, _cb; int _jn;
        void LateUpdate() // after the camera: the player's rendered position as the camera sees it (what jitters on screen)
        {
            if (!_rec || !_t || !Camera.main) { _jn = 0; return; }
            var c = Camera.main.transform.InverseTransformPoint(_t.position);
            var w = Camera.main.transform.position;
            if (_jn >= 2) { _jerk.Add(((c - _b) - (_b - _a)).magnitude * 1000f); _cj.Add(((w - _cb) - (_cb - _ca)).magnitude * 1000f); }
            _a = _b; _b = c; _ca = _cb; _cb = w; _jn++;
        }
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_WalkJitterProbe").AddComponent<ASHWalkJitterProbe>(); }
        Rigidbody _rb; Transform _t; bool _rec; readonly List<float> _spd = new List<float>(); readonly List<float> _lag = new List<float>();
        void FixedUpdate()
        {
            if (!_rec || !_rb) return;
            var v = _rb.linearVelocity; v.y = 0f; _spd.Add(v.magnitude);
            var d = _rb.position - _t.position; d.y = 0f; _lag.Add(d.magnitude);
        }
        IEnumerator Start()
        {
            ASH.Opening.ASHEruptionSequence.SuppressIfPresent();
            var input = FindAnyObjectByType<ASH.Player.ASHInput>();
            var cc = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>();
            if (!input || !cc) { Debug.Log("[ASH] WALKJITTER no player"); Destroy(gameObject); yield break; }
            _rb = cc.GetComponent<Rigidbody>(); _t = cc.transform;
            var tp = cc.GetComponent<Invector.vCharacterController.vThirdPersonInput>(); bool l0 = tp && tp.lockCameraInput; if (tp) tp.lockCameraInput = true;
            Debug.Log($"[ASH] WALKJITTER setup interp {_rb.interpolation} fixedDt {Time.fixedDeltaTime:F4} rotateViaRb {Invector.vCharacterController.vThirdPersonMotor.DMRotateViaRigidbody}");
            yield return new WaitForSeconds(1f);
            foreach (var sprint in new[] { false, true })
                foreach (var mode in new[] { 0, 1 })
                {
                    Invector.vCamera.vThirdPersonCamera.DMCameraDirect = mode >= 1; Invector.vCharacterController.vThirdPersonMotor.DMRotateViaRigidbody = mode >= 1;
                    input.SimSprint = sprint; float ang = 0f; float t0 = Time.time;
                    while (Time.time - t0 < 1.0f) { ang += Time.deltaTime * 0.6f; input.SimMove = new Vector2(Mathf.Sin(ang) * 0.3f, 1f); yield return null; } // settle
                    _spd.Clear(); _lag.Clear(); _jerk.Clear(); _cj.Clear(); _jn = 0; _rec = true; t0 = Time.time;
                    while (Time.time - t0 < 2.5f) { ang += Time.deltaTime * 0.6f; input.SimMove = new Vector2(Mathf.Sin(ang) * 0.3f, 1f); yield return null; }
                    _rec = false;
                    float mean = 0f; foreach (var s in _spd) mean += s; mean /= Mathf.Max(1, _spd.Count);
                    float dsum = 0f; for (int i = 1; i < _spd.Count; i++) dsum += Mathf.Abs(_spd[i] - _spd[i - 1]); float dmean = dsum / Mathf.Max(1, _spd.Count - 1);
                    float lag = 0f; foreach (var x in _lag) lag += x; lag /= Mathf.Max(1, _lag.Count);
                    _cj.Sort(); float c50 = _cj.Count > 0 ? _cj[_cj.Count / 2] : 0f; _jerk.Sort(); float j50 = _jerk.Count > 0 ? _jerk[_jerk.Count / 2] : 0f, j95 = _jerk.Count > 0 ? _jerk[(int)(_jerk.Count * 0.95f)] : 0f;
                    Debug.Log($"[ASH] WALKJITTER {(sprint ? "run" : "walk")} {(mode == 0 ? "before" : "fixed")}: speed {mean:F2} m/s, step-to-step speed change {dmean:F3} m/s ({100f * dmean / Mathf.Max(0.01f, mean):F1}%), transform lags body {lag * 100f:F1} cm in FixedUpdate, steps {_spd.Count}; on-screen jitter (player vs camera, mm per frame^2) median {j50:F1} p95 {j95:F1}; camera world jerk median {c50:F1}; fps {_jerk.Count / 2.5f:F0}");
                }
            input.SimMove = null; input.SimSprint = null; if (tp) tp.lockCameraInput = l0;
            Invector.vCharacterController.vThirdPersonMotor.DMRotateViaRigidbody = true; Invector.vCamera.vThirdPersonCamera.DMCameraDirect = true;
            Debug.Log("[ASH] WALKJITTER DONE"); Destroy(gameObject);
        }
    }
}
