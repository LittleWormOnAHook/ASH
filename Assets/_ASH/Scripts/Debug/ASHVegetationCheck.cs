using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>
    /// Quick vegetation check in Play Mode (no profiler): FPS sample from the player camera, then screenshots from
    /// temporary vantage cameras (cabin, ridge timber, prairie, a road and a trail to verify clearance).
    /// exec ASH.DebugTools.ASHVegetationCheck.Run. Shots: AshBridge/shots/veg/.
    /// </summary>
    public sealed class ASHVegetationCheck : MonoBehaviour
    {
        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] VEG check requires Play Mode"); return; }
            new GameObject("ASH_VegetationCheck").AddComponent<ASHVegetationCheck>();
        }

        /// <summary>Only the steep-slope trunk close-ups (exec ASH.DebugTools.ASHVegetationCheck.RunSteep).</summary>
        public static void RunSteep()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] VEG check requires Play Mode"); return; }
            new GameObject("ASH_VegetationCheck").AddComponent<ASHVegetationCheck>()._steepOnly = true;
        }
        bool _steepOnly;

        [Serializable] class XZ { public float x, z; }
        [Serializable] class Route { public string id; public XZ[] pts; }
        [Serializable] class Layout { public Route[] roads, trails; }

        const string Dir = "AshBridge/shots/veg/";
        Terrain _t;

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir); _t = Terrain.activeTerrain;
            if (_steepOnly) { yield return null; yield return Steep(); Debug.Log("[ASH] VEG steep check done; shots in " + Path.GetFullPath(Dir)); Destroy(gameObject); yield break; }
            var td = _t ? _t.terrainData : null;
            Debug.Log($"[ASH] VEG terrain trees {(td ? td.treeInstanceCount : 0)}, prototypes {(td ? td.treePrototypes.Length : 0)}, detail layers {(td ? td.detailPrototypes.Length : 0)}; treeDist {(_t ? _t.treeDistance : 0)}, billboard {(_t ? _t.treeBillboardDistance : 0)}, detailDist {(_t ? _t.detailObjectDistance : 0)}");

            // FPS from the player's own camera at spawn
            yield return new WaitForSecondsRealtime(2f);
            int frames = 0; float t0 = Time.realtimeSinceStartup, worst = 0f;
            while (Time.realtimeSinceStartup - t0 < 6f) { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
            float avg = frames / (Time.realtimeSinceStartup - t0);
            Debug.Log($"[ASH] VEG FPS player view: avg {avg:F1}, worst frame {worst * 1000f:F1} ms ({frames} frames)");
            ScreenCapture.CaptureScreenshot(Dir + "00_player_spawn.png"); yield return null; yield return null;

            var cam = new GameObject("ASH_VegCam").AddComponent<Camera>(); cam.depth = 100; cam.farClipPlane = 3000f; cam.fieldOfView = 60f;
            var main = Camera.main; if (main) { cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; }

            var cabin = GameObject.Find("Grandpa Cabin");
            if (cabin) { var c = cabin.transform.position; yield return Shot(cam, "01_cabin.png", c + new Vector3(28f, 0f, -30f), 10f, c + Vector3.up * 3f); }
            yield return Shot(cam, "02_ridge_timber.png", new Vector3(700f, 0f, 1100f), 90f, new Vector3(250f, 0f, 1500f), true);
            yield return Shot(cam, "03_prairie.png", new Vector3(2900f, 0f, 1900f), 20f, new Vector3(3300f, 0f, 2150f), true);

            var path = Path.Combine(Application.dataPath, "_ASH/Data/Map/ASH_RegionLayout.json");
            if (File.Exists(path))
            {
                var L = JsonUtility.FromJson<Layout>(File.ReadAllText(path));
                if (L.roads != null && L.roads.Length > 0) yield return RouteShot(cam, "04_road.png", L.roads[0], 0.45f, 14f);
                if (L.trails != null && L.trails.Length > 0) yield return OnRouteShot(cam, "05_trail.png", L.trails[0], 0.5f);
            }

            // FPS from the ridge vantage (densest timber)
            cam.transform.position = Ground(new Vector3(400f, 0f, 1400f)) + Vector3.up * 2f; cam.transform.rotation = Quaternion.Euler(5f, 270f, 0f);
            yield return new WaitForSecondsRealtime(1f);
            frames = 0; t0 = Time.realtimeSinceStartup; worst = 0f;
            while (Time.realtimeSinceStartup - t0 < 4f) { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
            Debug.Log($"[ASH] VEG FPS inside ridge timber: avg {frames / (Time.realtimeSinceStartup - t0):F1}, worst frame {worst * 1000f:F1} ms");
            Destroy(cam.gameObject);
            Debug.Log("[ASH] VEG check done; shots in " + Path.GetFullPath(Dir));
            Destroy(gameObject);
        }

        /// <summary>Low trunk-height shots of ASH trees on 26-38 deg slopes (one ridge, one gully), viewed along the contour.</summary>
        IEnumerator Steep()
        {
            var cam = new GameObject("ASH_VegCam").AddComponent<Camera>(); cam.depth = 100; cam.farClipPlane = 1500f; cam.fieldOfView = 55f;
            var td = _t.terrainData; var size = td.size; var o = _t.GetPosition(); var protos = td.treePrototypes; var inst = td.treeInstances;
            int shot = 0; float[] bandMin = { 0f, 900f }, bandMax = { 850f, 2050f }; string[] names = { "06_steep_ridge.png", "07_steep_gully.png" };
            for (int b = 0; b < 2; b++)
            {
                int best = -1; float bestScore = 0f;
                for (int i = 0; i < inst.Length; i += 3)
                {
                    var pr = protos[inst[i].prototypeIndex].prefab; if (!pr || pr.name.Contains("Snow") || pr.name.StartsWith("PW_")) continue;
                    float wx = o.x + inst[i].position.x * size.x; if (wx < bandMin[b] || wx > bandMax[b]) continue;
                    float sl = td.GetSteepness(inst[i].position.x, inst[i].position.z); if (sl < 26f || sl > 38f) continue;
                    float score = sl + inst[i].heightScale * 4f; if (score > bestScore) { bestScore = score; best = i; }
                }
                if (best < 0) { Debug.Log($"[ASH] VEG no steep ASH tree found in band {b}"); continue; }
                var ti = inst[best]; var w = Vector3.Scale(ti.position, size) + o;
                var n = td.GetInterpolatedNormal(ti.position.x, ti.position.z); var contour = Vector3.Cross(n, Vector3.up).normalized;
                var cpos = Ground(w + contour * 6f) + Vector3.up * 1.1f;
                cam.transform.position = cpos; cam.transform.LookAt(new Vector3(w.x, Ground(w).y + 0.6f, w.z));
                Debug.Log($"[ASH] VEG steep shot {names[b]}: {protos[ti.prototypeIndex].prefab.name} slope {td.GetSteepness(ti.position.x, ti.position.z):F0} deg, sunk {Ground(w).y - w.y:F2} m at ({w.x:F0},{w.z:F0})");
                yield return new WaitForSecondsRealtime(1.2f);
                ScreenCapture.CaptureScreenshot(Dir + names[b]); yield return null; yield return null; yield return null; shot++;
            }
            Destroy(cam.gameObject);
        }

        Vector3 Ground(Vector3 p) { if (_t) p.y = _t.SampleHeight(p) + _t.GetPosition().y; return p; }

        IEnumerator Shot(Camera cam, string file, Vector3 from, float height, Vector3 at, bool groundAt = false)
        {
            var f = Ground(from) + Vector3.up * height; var a = groundAt ? Ground(at) + Vector3.up * 5f : at;
            cam.transform.position = f; cam.transform.LookAt(a);
            yield return new WaitForSecondsRealtime(1.2f); // let terrain trees/details stream
            ScreenCapture.CaptureScreenshot(Dir + file); yield return null; yield return null; yield return null;
        }

        /// <summary>Eye-level shot standing on a route waypoint, looking along it.</summary>
        IEnumerator OnRouteShot(Camera cam, string file, Route r, float along)
        {
            if (r.pts == null || r.pts.Length < 2) yield break;
            int i = Mathf.Clamp(Mathf.RoundToInt(along * (r.pts.Length - 1)), 0, r.pts.Length - 2);
            var a = new Vector3(r.pts[i].x, 0f, r.pts[i].z); var b = new Vector3(r.pts[i + 1].x, 0f, r.pts[i + 1].z);
            cam.transform.position = Ground(a) + Vector3.up * 2.2f; cam.transform.LookAt(Ground(b) + Vector3.up * 1.5f);
            yield return new WaitForSecondsRealtime(1.2f);
            ScreenCapture.CaptureScreenshot(Dir + file); yield return null; yield return null; yield return null;
        }

        IEnumerator RouteShot(Camera cam, string file, Route r, float along, float height)
        {
            if (r.pts == null || r.pts.Length < 2) yield break;
            int i = Mathf.Clamp(Mathf.RoundToInt(along * (r.pts.Length - 1)), 1, r.pts.Length - 1);
            var a = new Vector3(r.pts[i - 1].x, 0f, r.pts[i - 1].z); var b = new Vector3(r.pts[i].x, 0f, r.pts[i].z);
            var dir = (b - a).normalized; var at = a + (b - a) * 0.5f;
            yield return Shot(cam, file, at - dir * 30f, height, at + dir * 40f, true);
        }
    }
}
