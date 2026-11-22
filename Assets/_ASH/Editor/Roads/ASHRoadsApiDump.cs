using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Roads
{
    /// <summary>Dev: logs the EasyRoads3D API surface (reflection; the package is git-ignored).</summary>
    public static class ASHRoadsApiDump
    {
        [MenuItem("ASH/Dev/Roads/Dump EasyRoads API (log)")]
        static void Dump()
        {
            var sb = new System.Text.StringBuilder("[ASH Roads] API dump\n");
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "EasyRoads3Dv3");
            if (asm == null) { Debug.Log("[ASH Roads] EasyRoads3Dv3 not loaded"); return; }
            foreach (var n in new[] { "ERRoadNetwork", "ERRoad", "ERRoadType", "ERConnection", "ERModularBase", "ERMarkerControlType", "ERCrossings", "ERSideObject", "ERModularRoad" })
            {
                var t = asm.GetTypes().FirstOrDefault(x => x.Name == n); if (t == null) { sb.AppendLine("no " + n); continue; }
                sb.AppendLine("== " + t.FullName + (t.IsEnum ? " enum: " + string.Join(",", Enum.GetNames(t)) : ""));
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
                    sb.AppendLine("  " + (m.IsStatic ? "static " : "") + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) sb.AppendLine("  F " + f.FieldType.Name + " " + f.Name);
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) sb.AppendLine("  P " + p.PropertyType.Name + " " + p.Name);
            }
            System.IO.File.WriteAllText("AshBridge/er_api.txt", sb.ToString());
            Debug.Log("[ASH Roads] API dump written to AshBridge/er_api.txt");
        }
    }
}
