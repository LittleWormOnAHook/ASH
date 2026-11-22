using System.Collections;
using ASH.FX;
using ASH.Player;
using ASH.PostFX;
using ASH.World.Accumulation;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>
    /// Play Mode check for the footstep/post batch: FPS at spawn (no post / scene post / ASH stack), surface resolution at a few
    /// spots, forced steps (audio + puffs). TEST HARNESS ONLY: holds the player's input still while the editor is unfocused.
    /// Logs "[ASH] BATCH DONE".
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class ASHBatchCheck : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHBatchCheck()
        {
            _prevBg = default;
        }

        static bool _prevBg;
        Invector.vCharacterController.vThirdPersonMotor _mot;

        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] BATCH requires Play Mode"); return; }
            _prevBg = Application.runInBackground; Application.runInBackground = true;
            new GameObject("ASH_BatchCheck").AddComponent<ASHBatchCheck>();
        }

        // test harness only: no input while the editor window is not focused (stuck-input self-running player)
        void FixedUpdate() { if (_mot && !Application.isFocused) _mot.input = Vector3.zero; }
        void Update() { if (_mot && !Application.isFocused) _mot.input = Vector3.zero; }

        static bool VfsOn(Component c) { var v = c.GetComponent<Invector.vFootStep>(); return v && v.enabled; }

        IEnumerator Fps(string label, float secs)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            int f = 0; float t0 = Time.realtimeSinceStartup, worst = 0f;
            while (Time.realtimeSinceStartup - t0 < secs) { yield return null; f++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
            Debug.Log($"[ASH] BATCH FPS {label}: {f / (Time.realtimeSinceStartup - t0):F1} (worst {worst * 1000f:F0} ms)");
        }

        IEnumerator Start()
        {
            var ctl0 = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>(); var start = ctl0 ? ctl0.transform.position : Vector3.zero;
            var ctl = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>();
            if (!ctl) { Debug.LogWarning("[ASH] BATCH no player"); Done(); yield break; }
            _mot = ctl.GetComponent<Invector.vCharacterController.vThirdPersonMotor>();
            yield return new WaitForSecondsRealtime(3f);
            var post = ASHPostProcessing.Instance; var fs = ctl.GetComponent<ASHFootsteps>(); var pf = ctl.GetComponent<ASHFootstepPuffs>();
            Debug.Log($"[ASH] BATCH components: post {(bool)post}, footsteps {(bool)fs} (map {(fs && fs.map ? fs.map.name : "-")}), puffs {(bool)pf}, vFootStep enabled {VfsOn(ctl)}");
            foreach (var mr in FindObjectsByType<MeshRenderer>()) if (mr.name.Contains("ASH") && mr.name.Contains("Surface")) Debug.Log($"[ASH] BATCH '{mr.name}' shadows {mr.shadowCastingMode}, receive {mr.receiveShadows}");

            // FPS: before (scene post only) / after (ASH stack) / no post
            if (post) post.SetActive(false);
            yield return Fps("spawn BEFORE (scene volume only)", 6f);
            if (post) post.SetActive(true);
            yield return Fps("spawn AFTER (ASH post stack)", 6f);
            if (post) { post.SetActive(false); ASHPostProcessing.SetCameraPost(false); }
            yield return Fps("spawn no post at all", 6f);
            if (post) post.SetActive(true);

            // surfaces
            if (fs)
            {
                var p0 = ctl.transform.position;
                void Probe(string label, Vector3 p) { var s = fs.Resolve(p, out var d, out var onT, out var road); Debug.Log($"[ASH] BATCH surface {label} @ {p.x:F0},{p.z:F0}: {(s != null ? s.id : "none")} depth {d:F3} m terrain {onT} road {road} puffs {(s != null && s.puffs && !road && d >= (pf ? pf.minDepth : 0f))}"); }
                Probe("spawn", p0);
                for (int i = 0; i < 6; i++) { var a = i * 60f * Mathf.Deg2Rad; Probe("ring" + i, p0 + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 12f); }
                // a structure top (wood) if any collider named like a cabin/deck is near
                foreach (var col in FindObjectsByType<Collider>())
                {
                    if (col.isTrigger || col is TerrainCollider) continue; var n = col.name.ToLowerInvariant();
                    if ((n.Contains("cabin") || n.Contains("floor") || n.Contains("deck") || n.Contains("porch") || n.Contains("rock")) && (col.bounds.center - p0).magnitude < 120f)
                    { var b = col.bounds; if (col.Raycast(new Ray(new Vector3(b.center.x, b.max.y + 2f, b.center.z), Vector3.down), out var h, b.size.y + 4f)) Probe("collider '" + col.name + "' top", h.point + Vector3.up * 0.02f); }
                }
                var ac = ASHAccumulationController.Instance; bool foundRoad = false;
                if (ac) for (float rad = 5f; rad < 400f && !foundRoad; rad += 5f) for (int k = 0; k < 24 && !foundRoad; k++)
                { var a = k * 15f * Mathf.Deg2Rad; var q = p0 + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rad; if (ac.GetPathFactor(q) > 0.9f) { foundRoad = true; Probe("road (" + rad + " m)", q); } }
                int e0 = pf ? pf.Emitted : 0; var lf = ctl.transform.position + ctl.transform.right * -0.12f;
                for (int i = 0; i < 4; i++) { fs.Step(lf + ctl.transform.forward * (0.4f * i), i < 2 ? 0.3f : 1f); yield return new WaitForSecondsRealtime(0.3f); }
                Debug.Log($"[ASH] BATCH forced 4 steps: last surface {fs.LastSurface}, depth {fs.LastDepth:F3}, puff particles emitted {(pf ? pf.Emitted - e0 : 0)}, alive {(pf && pf.System ? pf.System.particleCount : 0)}");
                yield return new WaitForSecondsRealtime(3.5f);
                Debug.Log($"[ASH] BATCH puffs alive after 3.5 s: {(pf && pf.System ? pf.System.particleCount : 0)}");
            }
            Debug.Log($"[ASH] BATCH player moved {(ctl.transform.position - start).magnitude:F2} m during the check (focused {Application.isFocused})");
            Done();
        }

        void Done() { Application.runInBackground = _prevBg; Debug.Log("[ASH] BATCH DONE"); Destroy(gameObject); }
    }
}
