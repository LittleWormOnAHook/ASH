using System.Collections;
using System.IO;
using ASH.World.Accumulation;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>
    /// Accumulation check in Play Mode (no profiler): FPS at spawn, then cabin / ridge trees / prairie ground shots.
    /// exec ASH.DebugTools.ASHAccumulationCheck.Run. Shots: AshBridge/shots/accum/.
    /// </summary>
    public sealed class ASHAccumulationCheck : MonoBehaviour
    {
        const string Dir = "AshBridge/shots/accum/";
        Terrain _t;
        static bool _prevBg;

        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] ACCUM check requires Play Mode"); return; }
            _prevBg = Application.runInBackground; Application.runInBackground = true; // keep ticking while the editor is unfocused (runtime only, not saved)
            new GameObject("ASH_AccumulationCheck").AddComponent<ASHAccumulationCheck>();
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir); _t = Terrain.activeTerrain;
            var c = ASHAccumulationController.Instance;
            Debug.Log($"[ASH] ACCUM controller {(c ? "ok" : "MISSING")}, terrain material {(_t && _t.materialTemplate ? _t.materialTemplate.shader.name : "none")}, layers {Shader.GetGlobalVector("_ASH_Layers")}");
            yield return new WaitForSecondsRealtime(2f);
            int frames = 0; float t0 = Time.realtimeSinceStartup, worst = 0f;
            while (Time.realtimeSinceStartup - t0 < 6f) { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
            Debug.Log($"[ASH] ACCUM FPS player view at spawn: avg {frames / (Time.realtimeSinceStartup - t0):F1}, worst frame {worst * 1000f:F1} ms ({frames} frames)");
            ScreenCapture.CaptureScreenshot(Dir + "00_player_spawn.png"); yield return null; yield return null;
            if (c) { var p = Camera.main ? Camera.main.transform.position : Vector3.zero; c.GetLayers(p, out var a, out var s); Debug.Log($"[ASH] ACCUM at spawn: depth {c.GetDepthMetres(p):F2} m (ash {a:F2}, snow {s:F2})"); }

            var cam = new GameObject("ASH_AccumCam").AddComponent<Camera>(); cam.depth = 100; cam.farClipPlane = 3000f; cam.fieldOfView = 60f;
            var main = Camera.main; if (main) { cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; }
            var cabin = GameObject.Find("Grandpa Cabin");
            if (cabin) { var cp = cabin.transform.position; yield return Shot(cam, "01_cabin.png", cp + new Vector3(16f, 0f, -18f), 6f, cp + Vector3.up * 2.5f); }
            yield return Shot(cam, "02_ridge_trees.png", new Vector3(520f, 0f, 1300f), 12f, new Vector3(380f, 0f, 1420f), true);
            yield return Shot(cam, "03_prairie_ground.png", new Vector3(2900f, 0f, 1900f), 3f, new Vector3(2960f, 0f, 1930f), true, -3f);
            // path markers: nearest marker to the cabin, eye-level view along the path edge (also shows reduced buildup on the path)
            var markers = GameObject.Find("ASH_PathMarkers");
            if (markers)
            {
                Transform best = null; float bd = float.MaxValue; var refPos = cabin ? cabin.transform.position : Vector3.zero;
                foreach (Transform route in markers.transform) foreach (Transform m in route) { float d = (m.position - refPos).sqrMagnitude; if (d < bd && d > 400f) { bd = d; best = m; } }
                if (best)
                {
                    Transform nb = null; float nd = float.MaxValue;
                    foreach (Transform m in best.parent) { if (m == best) continue; float d = (m.position - best.position).sqrMagnitude; if (d < nd) { nd = d; nb = m; } }
                    var along = nb ? (best.position - nb.position).normalized : Vector3.forward; along.y = 0f;
                    var from = best.position + along * 7f + Vector3.Cross(Vector3.up, along) * 1.5f;
                    cam.transform.position = Ground(from) + Vector3.up * 1.7f; cam.transform.LookAt(best.position + Vector3.up * 0.8f - along * 2f);
                    yield return new WaitForSecondsRealtime(1.2f);
                    ScreenCapture.CaptureScreenshot(Dir + "04_path_markers.png"); yield return null; yield return null; yield return null;
                    Debug.Log($"[ASH] ACCUM marker shot at {best.parent.name}/{best.name} {best.position}; depth on marker spot {(c ? c.GetDepthMetres(best.position) : 0f):F2} m");
                }
            }
            Destroy(cam.gameObject);
            Debug.Log("[ASH] ACCUM check done; shots in " + Path.GetFullPath(Dir));
            Application.runInBackground = _prevBg;
            Destroy(gameObject);
        }

        Vector3 Ground(Vector3 p) { if (_t) p.y = _t.SampleHeight(p) + _t.GetPosition().y; return p; }

        IEnumerator Shot(Camera cam, string file, Vector3 from, float height, Vector3 at, bool groundAt = false, float atLift = 5f)
        {
            cam.transform.position = Ground(from) + Vector3.up * height; cam.transform.LookAt(groundAt ? Ground(at) + Vector3.up * atLift : at);
            yield return new WaitForSecondsRealtime(1.2f);
            ScreenCapture.CaptureScreenshot(Dir + file); yield return null; yield return null; yield return null;
        }
    }
}
