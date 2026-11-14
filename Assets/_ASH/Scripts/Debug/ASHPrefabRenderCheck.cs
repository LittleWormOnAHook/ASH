using System.Collections;
using System.IO;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>
    /// Play Mode diagnostic: spawns the prefabs in Prefabs (set by an editor exec) in a row near the player under the scene's
    /// real lighting, takes one shot, destroys them. exec via ASHVendorMaterialTools.PlayRenderCheck. Logs "[ASH] PRC DONE".
    /// </summary>
    public sealed class ASHPrefabRenderCheck : MonoBehaviour
    {
        public static GameObject[] Prefabs = new GameObject[0];
        public static string ShotPath = "AshBridge/shots/vendor/scene_row.png";
        static bool _prevBg;

        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] PRC requires Play Mode"); return; }
            _prevBg = Application.runInBackground; Application.runInBackground = true;
            new GameObject("ASH_PRC").AddComponent<ASHPrefabRenderCheck>();
        }

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            var player = GameObject.FindWithTag("Player"); var t = Terrain.activeTerrain;
            if (!player || !t || Prefabs.Length == 0) { Debug.LogWarning($"[ASH] PRC missing player/terrain/prefabs ({Prefabs.Length})"); Done(); yield break; }
            var root = new GameObject("ASH_PRC_Row");
            var fwd = player.transform.forward; fwd.y = 0; fwd.Normalize(); var right = Vector3.Cross(Vector3.up, fwd);
            var origin = player.transform.position + fwd * 6f; float x = -(Prefabs.Length - 1) * 1.1f;
            foreach (var p in Prefabs)
            {
                if (!p) continue; var pos = origin + right * x; pos.y = t.SampleHeight(pos) + t.GetPosition().y;
                var go = Instantiate(p, pos, Quaternion.LookRotation(-fwd), root.transform);
                // normalise size to ~1.6 m so all fit
                var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); float s = 1.6f / Mathf.Max(0.1f, b.extents.magnitude * 2f); go.transform.localScale *= s; }
                x += 2.2f;
            }
            var cam = new GameObject("ASH_PRC_Cam").AddComponent<Camera>(); cam.depth = 100; cam.fieldOfView = 50f;
            var eye = origin - fwd * 9f + Vector3.up * 3f; eye.y = Mathf.Max(eye.y, t.SampleHeight(eye) + t.GetPosition().y + 1.6f);
            cam.transform.position = eye; cam.transform.LookAt(origin + Vector3.up * 0.6f);
            yield return new WaitForSecondsRealtime(2f);
            Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));
            ScreenCapture.CaptureScreenshot(ShotPath); yield return null; yield return null; yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            Destroy(cam.gameObject); Destroy(root);
            Done();
        }

        void Done() { Application.runInBackground = _prevBg; Debug.Log("[ASH] PRC DONE"); Destroy(gameObject); }
    }
}
