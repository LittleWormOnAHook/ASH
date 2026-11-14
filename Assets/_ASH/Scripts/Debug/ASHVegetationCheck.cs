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

        [Serializable] class XZ { public float x, z; }
        [Serializable] class Route { public string id; public XZ[] pts; }
        [Serializable] class Layout { public Route[] roads, trails; }

        const string Dir = "AshBridge/shots/veg/";
        Terrain _t;

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir); _t = Terrain.activeTerrain;
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
