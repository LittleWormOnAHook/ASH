using System.Linq;
using System.Text;
using ASH.Gameplay;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>Logs the arrow prefab's parts along its local axes (which end is the head). "[ASH] ARROWPROBE".</summary>
    public static class ASHArrowProbe
    {
        public static void Run()
        {
            var guid = AssetDatabase.FindAssets("ASH_Player t:Prefab").FirstOrDefault();
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var wa = player.GetComponentInChildren<ASHWeaponAim>(true);
            var sb = new StringBuilder();
            void Dump(string label, GameObject root)
            {
                sb.Append($"\n{label} {(root ? AssetDatabase.GetAssetPath(root) + " / " + root.name : "null")}");
                if (!root) return;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = mf.sharedMesh; if (!m) continue;
                    var b = m.bounds; var mn = root.transform.InverseTransformPoint(mf.transform.TransformPoint(b.min)); var mx = root.transform.InverseTransformPoint(mf.transform.TransformPoint(b.max));
                    var mr = mf.GetComponent<Renderer>();
                    sb.Append($"\n  {mf.name} mesh={m.name} mats=[{(mr ? string.Join(",", mr.sharedMaterials.Where(x => x).Select(x => x.name)) : "")}] rootMin={mn:F3} rootMax={mx:F3} sub={m.subMeshCount} readable={m.isReadable}");
                    if (m.isReadable)
                    {
                        var v = m.vertices.Select(p => root.transform.InverseTransformPoint(mf.transform.TransformPoint(p))).ToArray();
                        float zmin = v.Min(p => p.z), zmax = v.Max(p => p.z), len = zmax - zmin;
                        float R(float lo, float hi) { var s = v.Where(p => p.z >= lo && p.z <= hi).ToArray(); return s.Length == 0 ? 0 : s.Max(p => new Vector2(p.x, p.y).magnitude); }
                        int C(float lo, float hi) => v.Count(p => p.z >= lo && p.z <= hi);
                        var prof = new StringBuilder(" profile(z:xw/yw cm):");
                        for (int i = 0; i < 40; i++)
                        {
                            float lo = zmin + len * i / 40f, hi = zmin + len * (i + 1) / 40f; var s2 = v.Where(p => p.z >= lo && p.z <= hi).ToArray();
                            if (s2.Length == 0) { prof.Append(" -"); continue; }
                            prof.Append($" {i}:{(s2.Max(p => p.x) - s2.Min(p => p.x)) * 100f:F1}/{(s2.Max(p => p.y) - s2.Min(p => p.y)) * 100f:F1}");
                        }
                        sb.Append(prof);
                        sb.Append($" z {zmin:F3}..{zmax:F3}; radius/verts at -Z end(5%) {R(zmin, zmin + len * .05f):F3}/{C(zmin, zmin + len * .05f)} (15%) {R(zmin, zmin + len * .15f):F3}/{C(zmin, zmin + len * .15f)}; +Z end(5%) {R(zmax - len * .05f, zmax):F3}/{C(zmax - len * .05f, zmax)} (15%) {R(zmax - len * .15f, zmax):F3}/{C(zmax - len * .15f, zmax)}");
                    }
                }
            }
            Dump("arrowPrefab", wa ? wa.arrowPrefab : null);
            var held = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Contains("HandArrow"));
            Dump("held", held ? held.gameObject : null);
            Debug.Log("[ASH] ARROWPROBE flip=" + (wa && wa.flipArrowVisual) + sb);
        }
    }
}
