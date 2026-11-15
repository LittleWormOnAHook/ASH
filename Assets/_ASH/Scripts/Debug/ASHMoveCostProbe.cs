using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>One lightweight timing run (Play Mode, exec ASH.DebugTools.ASHMoveCostProbe.Run): frame time standing still vs
    /// auto-walking, built-in ProfilerRecorder counters (no deep profiling), then walking with one suspect system disabled at a
    /// time. Logs "[ASH] MOVECOST ...". Restores everything it touches.</summary>
    public sealed class ASHMoveCostProbe : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_MoveCostProbe").AddComponent<ASHMoveCostProbe>(); }

        static readonly (ProfilerCategory cat, string name)[] Markers =
        {
            (ProfilerCategory.Internal, "Main Thread"), (ProfilerCategory.Render, "Render Thread"), (ProfilerCategory.Memory, "GC Allocated In Frame"),
            (ProfilerCategory.Scripts, "BehaviourUpdate"), (ProfilerCategory.Scripts, "LateBehaviourUpdate"), (ProfilerCategory.Scripts, "FixedBehaviourUpdate"),
            (ProfilerCategory.Physics, "Physics.Simulate"), (ProfilerCategory.Animation, "Director.ProcessFrame"), (ProfilerCategory.Particles, "ParticleSystem.Update"),
            (ProfilerCategory.Render, "Terrain.Details.Render"), (ProfilerCategory.Render, "Terrain.Trees.Render"), (ProfilerCategory.Render, "Inl_UniversalRenderPipeline.RenderSingleCameraInternal"),
            (ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"), (ProfilerCategory.Render, "Gfx.WaitForRenderThread"), (ProfilerCategory.Internal, "Gfx.WaitForGfxCommandsFromMainThread"),
            (ProfilerCategory.Render, "Batches Count"), (ProfilerCategory.Render, "SetPass Calls Count"), (ProfilerCategory.Render, "Triangles Count"),
        };
        readonly List<ProfilerRecorder> _rec = new List<ProfilerRecorder>();
        readonly StringBuilder _sb = new StringBuilder();
        int _fixedSteps;
        void FixedUpdate() { _fixedSteps++; }

        static List<MonoBehaviour> ByName(string type)
        {
            var l = new List<MonoBehaviour>();
            foreach (var m in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)) if (m && m.GetType().Name == type) l.Add(m);
            return l;
        }

        IEnumerator Start()
        {
            foreach (var (c, n) in Markers) _rec.Add(ProfilerRecorder.StartNew(c, n, 400));
            // outdoor slice start (the opening keeps the player in the cabin)
            ASH.Opening.ASHEruptionSequence.SuppressIfPresent();
            var input = FindAnyObjectByType<ASH.Player.ASHInput>();
            var cc = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>();
            var rb = cc ? cc.GetComponent<Rigidbody>() : null; var an = cc ? cc.GetComponent<Animator>() : null;
            _sb.AppendLine($"[ASH] MOVECOST setup: input {(bool)input} player {(bool)cc} rb interp {(rb ? rb.interpolation.ToString() : "-")} fixedDt {Time.fixedDeltaTime:F4} vSync {QualitySettings.vSyncCount} target {Application.targetFrameRate} animator {(an ? an.updateMode + " rootMotion " + an.applyRootMotion : "-")}");
            if (!input || !cc) { Finish(); yield break; }
            var tp = cc.GetComponent<Invector.vCharacterController.vThirdPersonInput>(); bool lock0 = tp && tp.lockCameraInput; if (tp) tp.lockCameraInput = true;
            yield return new WaitForSeconds(1.5f);

            yield return Phase("stand", input, cc, false, 3f);
            yield return Phase("walk", input, cc, true, 4f);
            var terrain = Terrain.activeTerrain;
            string[] suspects = { "ASHAccumulationSurface", "ASHDeformationField", "ASHDeformer", "ASHFootstepPuffs", "ASHSpindrift", "ASHCloudLayer", "ASHAccumulationController", "TERRAIN_FOLIAGE" };
            foreach (var s in suspects)
            {
                List<MonoBehaviour> off = null; bool fol = false;
                if (s == "TERRAIN_FOLIAGE") { if (!terrain) continue; fol = terrain.drawTreesAndFoliage; terrain.drawTreesAndFoliage = false; }
                else { off = ByName(s); if (off.Count == 0) { _sb.AppendLine($"[ASH] MOVECOST walk -{s}: (none in scene)"); continue; } foreach (var m in off) m.enabled = false; }
                yield return new WaitForSeconds(0.3f);
                yield return Phase("walk -" + s, input, cc, true, 3f);
                if (off != null) foreach (var m in off) if (m) m.enabled = true;
                if (s == "TERRAIN_FOLIAGE" && terrain) terrain.drawTreesAndFoliage = fol;
            }
            yield return Phase("walk again", input, cc, true, 3f);
            yield return Phase("stand again", input, cc, false, 2f);
            input.SimMove = null; if (tp) tp.lockCameraInput = lock0;
            Finish();
        }

        IEnumerator Phase(string name, ASH.Player.ASHInput input, Invector.vCharacterController.vThirdPersonController cc, bool walk, float secs)
        {
            var dts = new List<float>(); var vals = new double[_rec.Count]; int frames = 0; _fixedSteps = 0;
            float t0 = Time.unscaledTime; Vector3 p0 = cc.transform.position; float ang = 0f;
            var jerk = new List<float>(); var cam = Camera.main; Vector3 a = Vector3.zero, b = Vector3.zero; int jn = 0;
            while (Time.unscaledTime - t0 < secs)
            {
                if (walk) { ang += Time.deltaTime * 0.35f; input.SimMove = new Vector2(Mathf.Sin(ang) * 0.35f, 1f); } else input.SimMove = Vector2.zero;
                yield return new WaitForEndOfFrame();
                dts.Add(Time.unscaledDeltaTime * 1000f); frames++;
                for (int i = 0; i < _rec.Count; i++) if (_rec[i].Valid) vals[i] += _rec[i].LastValue;
                if (cam) { var c = cam.transform.InverseTransformPoint(cc.transform.position); if (jn >= 2) jerk.Add(((c - b) - (b - a)).magnitude * 1000f); a = b; b = c; jn++; }
            }
            dts.Sort(); jerk.Sort();
            float avg = 0f; foreach (var d in dts) avg += d; avg /= Mathf.Max(1, dts.Count);
            float p95 = dts.Count > 0 ? dts[(int)(dts.Count * 0.95f)] : 0f, mx = dts.Count > 0 ? dts[dts.Count - 1] : 0f;
            float j95 = jerk.Count > 0 ? jerk[(int)(jerk.Count * 0.95f)] : 0f;
            var l = new StringBuilder($"[ASH] MOVECOST {name}: fps {1000f / Mathf.Max(0.01f, avg):F1} frame avg {avg:F1} p95 {p95:F1} max {mx:F1} ms, fixed/frame {(float)_fixedSteps / Mathf.Max(1, frames):F2}, moved {(cc.transform.position - p0).magnitude:F1} m, cam-space jerk p95 {j95:F1} mm |");
            for (int i = 0; i < _rec.Count; i++)
            {
                if (!_rec[i].Valid) continue; double v = vals[i] / Mathf.Max(1, frames);
                var u = _rec[i].UnitType;
                l.Append(' ').Append(Markers[i].name).Append('=').Append(u == ProfilerMarkerDataUnit.TimeNanoseconds ? (v / 1e6).ToString("F2") + "ms" : u == ProfilerMarkerDataUnit.Bytes ? (v / 1024).ToString("F1") + "KB" : v.ToString("F0"));
            }
            _sb.AppendLine(l.ToString());
            Debug.Log(l.ToString());
        }

        void Finish()
        {
            foreach (var r in _rec) r.Dispose(); _rec.Clear();
            Debug.Log(_sb.ToString() + "[ASH] MOVECOST DONE");
            Destroy(gameObject);
        }
    }
}
