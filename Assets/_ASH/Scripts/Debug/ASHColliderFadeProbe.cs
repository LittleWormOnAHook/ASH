using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ASH.World.Accumulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.DebugTools
{
    /// <summary>Debug: measures how far the deformable layer's collider fade reaches past real collider footprints.
    /// Reads the collider fade mask back, walks inward from outside each nearby collider in 8 directions until the fade
    /// reaches 0.5, and logs the horizontal gap to the collider. Logs "[ASH] CFADE SUMMARY".</summary>
    public sealed class ASHColliderFadeProbe : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_ColliderFadeProbe").AddComponent<ASHColliderFadeProbe>(); }

        IEnumerator Start()
        {
            var s = FindAnyObjectByType<ASHAccumulationSurface>(); var c = ASHAccumulationController.Instance;
            if (!s || !c || !c.terrain) { Debug.Log("[ASH] CFADE SUMMARY 0/1 no surface"); Destroy(gameObject); yield break; }
            s.blockerRefresh = Mathf.Min(s.blockerRefresh, 1f);
            yield return new WaitForSeconds(1.5f);
            var rt = s.ColliderMask; if (!rt) { Debug.Log("[ASH] CFADE SUMMARY 0/1 no mask"); Destroy(gameObject); yield break; }
            var rect = s.ColliderMaskRect; int res = (int)rect.w; float size = rect.z; Vector2 min = new Vector2(rect.x, rect.y); float tx = size / res;
            float[] px = null; bool done = false;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RFloat, r => { if (!r.hasError) px = r.GetData<float>().ToArray(); done = true; });
            while (!done) yield return null;
            if (px == null) { Debug.Log("[ASH] CFADE SUMMARY 0/1 readback failed"); Destroy(gameObject); yield break; }
            float Fade(Vector2 xz)
            {   // bilinear, same as the shader
                Vector2 uv = (xz - min) / size * res - Vector2.one * 0.5f; int x0 = Mathf.FloorToInt(uv.x), y0 = Mathf.FloorToInt(uv.y); float fx = uv.x - x0, fy = uv.y - y0;
                float P(int x, int y) => x < 0 || y < 0 || x >= res || y >= res ? 0f : px[y * res + x];
                return Mathf.Lerp(Mathf.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Mathf.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy);
            }
            var t = c.terrain; var player = GameObject.FindWithTag("Player"); Vector3 pp = player ? player.transform.position : Camera.main.transform.position;
            var cols = Physics.OverlapSphere(pp, 20f, ~0, QueryTriggerInteraction.Ignore)
                .Where(k => !(k is TerrainCollider) && !(k is CharacterController) && !(k is MeshCollider mc && !mc.convex) && (!player || !k.transform.IsChildOf(player.transform.root)))
                .Where(k => { var an = k.GetComponentInParent<Animator>(); return !(an && an.isHuman); })
                .Where(k => k.bounds.min.y <= t.SampleHeight(k.bounds.center) + t.GetPosition().y + s.blockerGroundTolerance && k.bounds.size.x < 20f)
                .OrderBy(k => (k.bounds.center - pp).sqrMagnitude).Take(12).ToList();
            var gaps = new List<float>(); var lines = new List<string>(); float limit = s.colliderFadePadding + s.colliderFadeSoftness * 0.5f + 2f * tx + 0.01f; int fails = 0, checkedCols = 0;
            foreach (var col in cols)
            {
                var b = col.bounds; float g = t.SampleHeight(b.center) + t.GetPosition().y; var cg = new List<float>();
                for (int d = 0; d < 8; d++)
                {
                    float a = d * Mathf.PI / 4f; var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var c2 = new Vector2(b.center.x, b.center.z);
                    float far = Mathf.Max(b.extents.x, b.extents.z) * 1.5f + 1f; Vector2 hit = Vector2.zero; bool found = false;
                    for (float r = far; r > 0f; r -= 0.004f) { var q = c2 + dir * r; if (Fade(q) >= 0.5f) { hit = q; found = true; break; } }
                    if (!found) continue;
                    float best = float.MaxValue; // horizontal distance from the fade edge to the collider's footprint (slices up to the footprint height)
                    for (float y = Mathf.Max(b.min.y, g - 0.2f); y <= Mathf.Min(b.max.y, g + s.colliderFootprintHeight); y += 0.05f)
                    { var q3 = new Vector3(hit.x, y, hit.y); var cp = col.ClosestPoint(q3); if (Mathf.Abs(cp.y - y) > 0.03f) continue; best = Mathf.Min(best, Vector2.Distance(hit, new Vector2(cp.x, cp.z))); }
                    if (best < float.MaxValue) cg.Add(best);
                }
                if (cg.Count == 0) continue;
                checkedCols++; float mx = cg.Max(); gaps.AddRange(cg); bool ok = mx <= limit; if (!ok) fails++;
                lines.Add($"{col.name}({col.GetType().Name}) maxGap={mx * 100f:F1}cm mean={cg.Average() * 100f:F1}cm {(ok ? "ok" : "WIDE")}");
            }
            foreach (var l in lines) Debug.Log("[ASH] CFADE " + l);
            Debug.Log($"[ASH] CFADE SUMMARY {checkedCols - fails}/{fails} colliders={s.ColliderFadeCount} texel={tx * 100f:F2}cm pad={s.colliderFadePadding} soft={s.colliderFadeSoftness} limit={limit * 100f:F1}cm overallMax={(gaps.Count > 0 ? gaps.Max() * 100f : -1):F1}cm");
            Destroy(gameObject);
        }
    }
}
