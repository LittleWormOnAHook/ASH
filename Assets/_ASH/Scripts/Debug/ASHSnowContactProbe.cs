using System.IO;
using System.Text;
using ASH.World.Accumulation;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>t345 snow contact probe (exec ASH.DebugTools.ASHSnowContactProbe.Run): colliders near the player and how the
    /// accumulation surface treats them (clear / bank), collider footprint vs visible mesh size, material shader; then a
    /// top-down shot at the nearest rock to C:\Users\Teabagger\ashx\snow_top.png. Logs "[ASH] SNOWC ...".</summary>
    public sealed class ASHSnowContactProbe : MonoBehaviour
    {
        public static string ShotPath = @"C:\Users\Teabagger\ashx\snow_top.png";
        public static float Height = 7f;
        public static void RunSide() { if (Application.isPlaying) new GameObject("ASH_SnowContactProbe").AddComponent<ASHSnowContactProbe>()._side = true; }
        bool _side;
        System.Collections.IEnumerator SideShots(Collider rock, ASHAccumulationSurface surf)
        {
            var b = rock.bounds; var dir = Quaternion.Euler(0f, 30f, 0f) * Vector3.forward;
            float d = Mathf.Max(b.extents.x, b.extents.z) + 1.6f;
            var go = new GameObject("ASH_SideCam"); var cam = go.AddComponent<Camera>(); var main = Camera.main; if (main) cam.CopyFrom(main);
            cam.fieldOfView = 50f;
            var basePt = new Vector3(b.center.x, b.min.y + 0.1f, b.center.z);
            go.transform.position = basePt - dir * d + Vector3.up * 0.9f; go.transform.LookAt(basePt);
            var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt; cam.enabled = false;
            float keep = surf ? surf.contactCurveHeight : 0f;
            foreach (var on in new[] { true, false })
            {
                if (surf) surf.contactCurveHeight = on ? keep : 0f;
                yield return null; yield return new WaitForEndOfFrame();
                cam.Render(); RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
                var p = ShotPath.Replace("snow_top", on ? "snow_side_on" : "snow_side_off");
                File.WriteAllBytes(p, tex.EncodeToPNG()); Destroy(tex); Debug.Log($"[ASH] SNOWC side shot {p} at {rock.name} size {b.size} curve {(surf ? surf.contactCurveHeight : -1f)}");
            }
            if (surf) surf.contactCurveHeight = keep;
            cam.targetTexture = null; Destroy(rt); Destroy(go);
        }
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_SnowContactProbe").AddComponent<ASHSnowContactProbe>(); }

        System.Collections.IEnumerator Start()
        {
            var sb = new StringBuilder("[ASH] SNOWC");
            var surf = FindAnyObjectByType<ASHAccumulationSurface>();
            var pl = GameObject.FindWithTag("Player"); var P = pl ? pl.transform.position : Camera.main.transform.position;
            sb.Append(surf ? $" surface: fade {surf.colliderFade} pad {surf.colliderFadePadding} soft {surf.colliderFadeSoftness} bank {surf.banking} h {surf.bankHeight} reach {surf.bankReach} minSize {surf.bankMinColliderSize} footH {surf.colliderFootprintHeight}" : " no surface");
            Collider rock = null; float best = 1e9f;
            foreach (var c in Physics.OverlapSphere(P, 14f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c is TerrainCollider || (pl && c.transform.IsChildOf(pl.transform))) continue;
                var b = c.bounds; float sz = Mathf.Max(b.size.x, b.size.y, b.size.z);
                var r = c.GetComponentInParent<Renderer>(); if (!r) r = c.GetComponentInChildren<Renderer>();
                string sh = r && r.sharedMaterial ? r.sharedMaterial.shader.name : "-";
                Vector3 rs = r ? r.bounds.size : Vector3.zero;
                bool banks = surf && surf.banking && sz >= surf.bankMinColliderSize && r && c.gameObject.layer != 2;
                sb.Append($"\n  {c.name} [{c.GetType().Name}] col {b.size.x:F2}x{b.size.z:F2} h{b.size.y:F2} vis {rs.x:F2}x{rs.z:F2} -> {(banks ? "BANK" : "CLEAR")} shader {sh} d {Vector3.Distance(P, b.center):F1}");
                string n = c.name.ToLowerInvariant();
                if ((n.Contains("rock") || n.Contains("boulder") || n.Contains("stone")) && (!_side || banks) && Vector3.Distance(P, b.center) < best) { best = Vector3.Distance(P, b.center); rock = c; }
            }
            Debug.Log(sb.ToString());
            yield return new WaitForSeconds(0.5f);
            if (_side) { if (rock) yield return SideShots(rock, surf); else Debug.Log("[ASH] SNOWC side: no banking rock within 14 m"); Debug.Log("[ASH] SNOWC SUMMARY"); Destroy(gameObject); yield break; }
            var at = rock ? rock.bounds.center : P;
            var go = new GameObject("ASH_TopCam"); var cam = go.AddComponent<Camera>(); var main = Camera.main;
            if (main) cam.CopyFrom(main);
            cam.fieldOfView = 60f; go.transform.position = at + Vector3.up * Height + Vector3.back * 0.01f; go.transform.rotation = Quaternion.Euler(80f, 0f, 0f);
            go.transform.position = at - go.transform.forward * (Height / Mathf.Sin(80f * Mathf.Deg2Rad));
            var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt; cam.enabled = false;
            yield return new WaitForEndOfFrame();
            cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
            try { File.WriteAllBytes(ShotPath, tex.EncodeToPNG()); Debug.Log($"[ASH] SNOWC shot {ShotPath} at {(rock ? rock.name : "player")}"); } catch (System.Exception e) { Debug.Log("[ASH] SNOWC shot failed " + e.Message); }
            cam.targetTexture = null; Destroy(rt); Destroy(tex); Destroy(go);
            Debug.Log("[ASH] SNOWC SUMMARY"); Destroy(gameObject);
        }
    }
}
