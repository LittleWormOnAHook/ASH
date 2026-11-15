using System.Linq;
using System.Text;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Read-only report of the cabin door(s) and openings (bridge: exec ASH.EditorTools.ASHCabinProbe.Run). Changes nothing.</summary>
    public static class ASHCabinProbe
    {
        static string Path(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
        public static void Run()
        {
            var sb = new StringBuilder("[ASH] CABIN PROBE\n");
            var cabin = GameObject.Find("Grandpa Cabin");
            sb.Append($"cabin: {(cabin ? Path(cabin.transform) + " static=" + cabin.isStatic : "none")}\n");
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in all.Where(t => { var n = t.name.ToLowerInvariant(); return n.Contains("door") || n.Contains("window") || n.Contains("glass") || n.Contains("hatch") || n.Contains("shutter"); }))
            {
                var mf = t.GetComponent<MeshFilter>(); var r = t.GetComponent<Renderer>();
                sb.Append($"- {Path(t)} active={t.gameObject.activeInHierarchy} static={t.gameObject.isStatic} pos={t.position:F2} rot={t.eulerAngles:F1} scale={t.lossyScale:F2} children={t.childCount}");
                if (mf && mf.sharedMesh) sb.Append($" mesh={mf.sharedMesh.name} meshBounds c={mf.sharedMesh.bounds.center:F2} s={mf.sharedMesh.bounds.size:F2}");
                if (r) sb.Append($" rendBounds c={r.bounds.center:F2} s={r.bounds.size:F2}");
                sb.Append(" comps=" + string.Join(",", t.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name + (c is Collider cl ? (cl.isTrigger ? "(trig)" : "") + (cl is MeshCollider m ? "(mesh " + (m.sharedMesh ? m.sharedMesh.name : "-") + ")" : "") : ""))));
                sb.Append('\n');
            }
            if (cabin)
            {
                sb.Append("cabin children: " + string.Join(" | ", cabin.GetComponentsInChildren<Transform>(true).Take(80).Select(x => x.name + (x.GetComponent<Collider>() ? "[" + x.GetComponent<Collider>().GetType().Name + "]" : "")))) ;
                sb.Append('\n');
            }
            // openings: from the interior trigger centre, cast outward at 3 heights in 72 directions
            var inter = GameObject.Find("ASH_Phase2A/ASH_CabinInterior");
            var bc = inter ? inter.GetComponent<BoxCollider>() : null;
            if (bc)
            {
                Physics.SyncTransforms();
                var b = bc.bounds; float floor = b.min.y;
                sb.Append($"interior box c={b.center:F2} s={b.size:F2}\n");
                foreach (float h in new[] { 0.4f, 1.0f, 1.5f, 2.0f })
                    for (int i = 0; i < 72; i++)
                    {
                        float a = i * 5f * Mathf.Deg2Rad; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                        var o = new Vector3(b.center.x, floor + h, b.center.z);
                        if (!Physics.Raycast(o, d, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore)) sb.Append($"OPEN h={h} dir={i * 5}deg\n");
                        else if (hit.distance > Mathf.Max(b.extents.x, b.extents.z) + 1.5f) sb.Append($"FAR h={h} dir={i * 5}deg d={hit.distance:F1} hit={Path(hit.collider.transform)}\n");
                    }
                // also sample from several interior points
                for (int gx = -1; gx <= 1; gx++) for (int gz = -1; gz <= 1; gz++)
                {
                    var o = new Vector3(b.center.x + gx * b.extents.x * 0.6f, floor + 1.0f, b.center.z + gz * b.extents.z * 0.6f);
                    for (int i = 0; i < 36; i++) { float a = i * 10f * Mathf.Deg2Rad; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); if (!Physics.Raycast(o, d, 20f, ~0, QueryTriggerInteraction.Ignore)) sb.Append($"OPEN from ({gx},{gz}) dir={i * 10}deg\n"); }
                }
            }
            Debug.Log(sb.ToString());
        }
            public static void Shaders()
        {
            var sb = new StringBuilder("[ASH] SHADER CHECK: ");
            foreach (var n in new[] { "Hidden/ASH/Accumulation Surface", "ASH/CloudLayer", "ASH/Accumulation Lit", "ASH/Terrain Lit Accum" })
            {
                var s = Shader.Find(n); if (!s) { sb.Append($"{n}: MISSING; "); continue; }
                var msgs = UnityEditor.ShaderUtil.GetShaderMessages(s); sb.Append($"{n}: {(UnityEditor.ShaderUtil.ShaderHasError(s) ? "ERROR" : "ok")} ({msgs.Length}); ");
                foreach (var m in msgs) sb.Append($"\n   [{m.severity}] {m.message} ({System.IO.Path.GetFileName(m.file)}:{m.line})");
            }
            Debug.Log(sb.ToString());
        }

        public static void Door()
        {
            var sb = new StringBuilder("[ASH] DOOR PROBE\n");
            var d = GameObject.Find("WoodenDoor-Cabin") ?? GameObject.Find("WoodenDoor"); var inter = GameObject.Find("ASH_Phase2A/ASH_CabinInterior");
            if (!d || !inter) { Debug.Log($"[ASH] DOOR PROBE: missing door={(bool)d} interior={(bool)inter}"); return; }
            sb.Append($"door {d.name} parent {(d.transform.parent ? d.transform.parent.name : "-")} static={d.isStatic} pos {d.transform.position:F2} rot {d.transform.eulerAngles:F1} scale {d.transform.lossyScale:F2} comps {string.Join(",", d.GetComponents<Component>().Select(c => c.GetType().Name))} children {d.transform.childCount}\n");
            Physics.SyncTransforms();
            var b = inter.GetComponent<BoxCollider>().bounds; float y = b.min.y + 1.0f;
            var mf = d.GetComponent<MeshFilter>(); var mb = mf.sharedMesh.bounds; var t = d.transform;
            Vector3 P(Vector3 l) => t.TransformPoint(l);
            sb.Append($"leaf pivot {t.position:F2} localX {t.right:F2} localZ {t.forward:F2} min {P(mb.min):F2} max {P(mb.max):F2} hingeEdge {P(new Vector3(mb.min.x, 0, 0)):F2} freeEdge {P(new Vector3(mb.max.x, 0, 0)):F2}\n");
            // north/south/east/west sweeps from the interior centre line: wall distance per lateral offset
            foreach (var (dir, lat) in new[] { (Vector3.forward, Vector3.right), (Vector3.back, Vector3.right), (Vector3.right, Vector3.forward), (Vector3.left, Vector3.forward) })
            {
                sb.Append($"sweep dir {dir}: ");
                for (float o = -3f; o <= 3.01f; o += 0.2f)
                {
                    var org = new Vector3(b.center.x, y, b.center.z) + lat * o;
                    sb.Append(Physics.Raycast(org, dir, out var h, 10f, ~0, QueryTriggerInteraction.Ignore) ? $"{o:F1}:{h.distance:F1}({h.collider.name.Substring(0, System.Math.Min(6, h.collider.name.Length))}) " : $"{o:F1}:OPEN ");
                }
                sb.Append('\n');
            }
            var cab = GameObject.Find("Grandpa Cabin"); if (cab) { var cmf = cab.GetComponent<MeshFilter>(); var col = cab.GetComponent<MeshCollider>(); sb.Append($"cabin pos {cab.transform.position:F2} rot {cab.transform.eulerAngles:F1} scale {cab.transform.lossyScale:F2} mesh {(cmf && cmf.sharedMesh ? cmf.sharedMesh.name + " b=" + cmf.sharedMesh.bounds : "-")} col {(col && col.sharedMesh ? col.sharedMesh.name + " b=" + col.bounds : "-")}\n"); }
            Debug.Log(sb.ToString());
        }
    }
}