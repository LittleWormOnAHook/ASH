using System.Collections;
using System.Text;
using ASH.Player;
using Invector.vCamera;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Camera settle after walking stops (exec ASH.DebugTools.ASHCameraStopTest.Run): walks 2.5 s, stops, and samples the
    /// camera-to-player offset for 3 s. Fails if the camera is still moving relative to the player more than 0.5 s after the
    /// player has stopped (the "creeps forward for ~2 s" report). Logs "[ASH] CAMSTOP ..." + SUMMARY.</summary>
    public sealed class ASHCameraStopTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_CameraStopTest").AddComponent<ASHCameraStopTest>(); }
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"CAMSTOP {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }
        static float F(object o, string name) { var f = o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); return f != null && f.FieldType == typeof(float) ? (float)f.GetValue(o) : float.NaN; }

        IEnumerator Start()
        {
            yield return null;
            var input = FindAnyObjectByType<ASHInput>(); var cc = FindAnyObjectByType<vThirdPersonController>(); var tp = cc ? cc.GetComponent<vThirdPersonInput>() : null; var cam = tp ? tp.tpCamera : null;
            Check("setup", input && cc && cam, $"late follow {vThirdPersonCamera.DMFollowInLateUpdate}");
            if (!(input && cc && cam)) { Finish(); yield break; }
            bool lock0 = tp.lockCameraInput; tp.lockCameraInput = true;
            foreach (var walk in new[] { false, true })
            {
                input.SimSprint = walk ? false : (bool?)null; input.SimMove = new Vector2(0f, walk ? 0.5f : 1f);
                yield return new WaitForSeconds(2.5f);
                input.SimMove = Vector2.zero; input.SimSprint = null;
                var sb = new StringBuilder(); float t = 0f, lastMove = 0f, tPlayerStop = -1f; Vector3 prevOff = Vector3.zero, prevP = cc.transform.position; bool first = true; float sinceHalf = 0f;
                while (t < 3f)
                {
                    yield return new WaitForEndOfFrame(); t += Time.deltaTime;
                    var p = cc.transform.position; float pv = Vector3.ProjectOnPlane(p - prevP, Vector3.up).magnitude / Mathf.Max(1e-4f, Time.deltaTime); prevP = p;
                    if (tPlayerStop < 0f && pv < 0.05f) tPlayerStop = t;
                    var off = Quaternion.Inverse(Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f)) * (cam.transform.position - p);
                    float cv = first ? 0f : (off - prevOff).magnitude / Mathf.Max(1e-4f, Time.deltaTime); prevOff = off; first = false;
                    if (tPlayerStop >= 0f && t - tPlayerStop > 0.5f) sinceHalf = Mathf.Max(sinceHalf, cv);
                    if (cv > 0.02f) lastMove = t;
                    if (Mathf.Repeat(t, 0.25f) < Time.deltaTime) sb.Append($" t{t:F2}: off({off.x:F2},{off.y:F2},{off.z:F2}) camv {cv:F2} pv {pv:F2} state {cam.currentState.Name} dist {F(cam, "distance"):F2} h {F(cam, "currentHeight"):F2};");
                }
                Debug.Log($"[ASH] CAMSTOP DIAG {(walk ? "walk" : "run")}: player stopped at {tPlayerStop:F2}s{sb}");
                Check($"camera settles after {(walk ? "walking" : "running")} stops", tPlayerStop >= 0f && sinceHalf < 0.05f, $"player stopped {tPlayerStop:F2}s, camera still moving until {lastMove:F2}s, max camera drift speed after +0.5s {sinceHalf:F3} m/s");
                yield return new WaitForSeconds(0.5f);
            }
            tp.lockCameraInput = lock0;
            Finish();
        }

        void Finish() { Debug.Log($"[ASH] CAMSTOP SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}
