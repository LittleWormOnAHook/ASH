using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Vendor
{
    /// <summary>
    /// Rewrites layer numbers in ASH's copies of Invector / Malbers / WarFX from Genesis numbering (= Invector defaults) to ASH's,
    /// matching by layer NAME: GameObject m_Layer and every serialized LayerMask (m_Bits). Dry run by default.
    /// The write pass refuses to run until every target layer exists in ASH (add them in Project Settings > Tags and Layers first),
    /// and records each rewritten file in Docs/LayerRemap_Applied.txt so it is never remapped twice. Never touches Genesis or ProjectSettings.
    /// </summary>
    public static class ASHVendorLayerRemap
    {
        public static readonly string[] Roots = { "Assets/Invector-3rdPersonController", "Assets/Malbers Animations", "Assets/JMO Assets" };
        const string AppliedPath = "Docs/LayerRemap_Applied.txt";
        const string ReportPath = "AshBridge/layer_remap_report.txt";

        /// <summary>Genesis TagManager layer names (read once from A:\Dark Matter Genesis, hard-coded so Genesis is never opened again).</summary>
        static readonly Dictionary<int, string> Genesis = new Dictionary<int, string>
        {
            {0,"Default"},{1,"TransparentFX"},{2,"Ignore Raycast"},{4,"Water"},{5,"UI"},{6,"Resource"},{7,"Item"},
            {8,"Player"},{9,"Enemy"},{10,"CompanionAI"},{11,"Triggers"},{12,"StopMove"},{13,"PW_VFX"},{14,"HeadTrack"},{15,"BodyPart"},
            {16,"BlockAIRayCast"},{17,"Pushable"},{18,"CoverPoint"},{19,"PW_Object_Small"},{20,"Animal"},{21,"PW_Object_Medium"},
            {22,"PW_Object_Large"},{23,"Climbable"},{24,"Building"},{25,"DistantSky"},{26,"DMHitbox"},{31,"PostProcess"},
        };
        /// <summary>Genesis-only layers with no ASH equivalent: objects go to Default, masks drop the bit.</summary>
        static readonly HashSet<string> GenesisOnly = new HashSet<string> { "DistantSky", "DMHitbox", "PostProcess" };

        [MenuItem("ASH/Tools/Layers/Remap Copied Vendor Layers (dry run)")] public static void DryRun() => Run(false);
        [MenuItem("ASH/Tools/Layers/Remap Copied Vendor Layers (WRITE)")]
        public static void Write()
        {
            if (!EditorUtility.DisplayDialog("ASH layer remap", "Rewrite layer numbers in ASH's Invector / Malbers / WarFX copies from Genesis to ASH numbering?\nRun the dry run first.", "Rewrite", "Cancel")) return;
            Run(true);
        }

        /// <summary>Genesis index -> ASH index; -1 = keep the same number (unnamed slot), -2 = missing in ASH.</summary>
        static int[] BuildMap(List<string> missing)
        {
            var map = new int[32];
            for (int i = 0; i < 32; i++)
            {
                if (!Genesis.TryGetValue(i, out var name)) { map[i] = -1; continue; }
                if (GenesisOnly.Contains(name)) { map[i] = i == 0 ? 0 : -3; continue; }
                int a = LayerMask.NameToLayer(name);
                if (a < 0) { map[i] = -2; missing.Add($"{name} (Genesis {i})"); } else map[i] = a;
            }
            return map;
        }

        static int MapLayer(int[] map, int l) { int v = map[l]; return v >= 0 ? v : v == -1 ? l : 0; }
        static uint MapMask(int[] map, uint bits)
        {
            if (bits == 0u || bits == uint.MaxValue) return bits;
            uint r = 0u;
            for (int i = 0; i < 32; i++)
            {
                if ((bits & (1u << i)) == 0) continue;
                int v = map[i];
                if (v >= 0) r |= 1u << v; else if (v == -1) r |= 1u << i; // -2 missing / -3 Genesis-only: dropped
            }
            return r;
        }

        static void Run(bool write)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { Debug.LogWarning("[ASH] Layers: stop Play mode / wait for compile first."); return; }
            var missing = new List<string>(); var map = BuildMap(missing);
            if (write && missing.Count > 0) { Debug.LogError("[ASH] Layers: add these layers in ASH first, then rerun: " + string.Join(", ", missing)); return; }
            var applied = File.Exists(AppliedPath) ? new HashSet<string>(File.ReadAllLines(AppliedPath)) : new HashSet<string>();
            var files = Roots.Where(Directory.Exists).SelectMany(r => Directory.GetFiles(r, "*.*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".prefab") || f.EndsWith(".unity") || f.EndsWith(".asset")).Select(f => f.Replace('\\', '/')).OrderBy(f => f).ToList();
            var pairs = new SortedDictionary<string, int>(); var changedFiles = new List<string>(); var changedGuids = new HashSet<string>();
            int layerEdits = 0, maskEdits = 0, skipped = 0;
            var rxL = new Regex(@"(m_Layer: )(\d+)"); var rxB = new Regex(@"(m_Bits: )(\d+)");
            if (write) AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var f = files[i];
                    if (applied.Contains(f)) { skipped++; continue; }
                    if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("ASH layer remap", f, i / (float)files.Count)) break;
                    string txt;
                    using (var sr = new StreamReader(f)) { var head = new char[5]; sr.Read(head, 0, 5); if (new string(head) != "%YAML") continue; txt = (new string(head)) + sr.ReadToEnd(); }
                    if (!txt.Contains("m_Layer: ") && !txt.Contains("m_Bits: ")) continue;
                    int le = 0, me = 0;
                    var outTxt = rxL.Replace(txt, m => { int o = int.Parse(m.Groups[2].Value); if (o < 0 || o > 31) return m.Value; int n = MapLayer(map, o); if (n == o) return m.Value; le++; Count(pairs, $"layer {o} {Name(o)} -> {n}"); return m.Groups[1].Value + n; });
                    outTxt = rxB.Replace(outTxt, m => { if (!uint.TryParse(m.Groups[2].Value, out var o)) return m.Value; uint n = MapMask(map, o); if (n == o) return m.Value; me++; return m.Groups[1].Value + n; });
                    if (le + me == 0) continue;
                    layerEdits += le; maskEdits += me; changedFiles.Add($"{f}  layers={le} masks={me}");
                    var g = AssetDatabase.AssetPathToGUID(f); if (!string.IsNullOrEmpty(g)) changedGuids.Add(g);
                    if (write) { File.WriteAllText(f, outTxt); applied.Add(f); }
                }
            }
            finally { if (write) AssetDatabase.StopAssetEditing(); EditorUtility.ClearProgressBar(); }
            if (write) { Directory.CreateDirectory("Docs"); File.WriteAllLines(AppliedPath, applied.OrderBy(x => x)); AssetDatabase.Refresh(); }

            // ASH-owned prefabs/scenes that nest or derive from a remapped vendor asset (their layers change through the prefab link).
            var ashRefs = new List<string>();
            if (changedGuids.Count > 0)
                foreach (var f in Directory.GetFiles("Assets/_ASH", "*.*", SearchOption.AllDirectories).Where(x => x.EndsWith(".prefab") || x.EndsWith(".unity")))
                {
                    var t = File.ReadAllText(f);
                    int hits = changedGuids.Count(g => t.Contains(g)); if (hits > 0) ashRefs.Add($"{f.Replace('\\', '/')}  refs={hits}");
                }

            var sb = new StringBuilder();
            sb.AppendLine($"[ASH] Vendor layer remap {(write ? "WRITE" : "DRY RUN")} {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"files scanned={files.Count} {(write ? "rewritten" : "would change")}={changedFiles.Count} objectLayerEdits={layerEdits} maskEdits={maskEdits} alreadyApplied={skipped}");
            sb.AppendLine("missing ASH layers (write pass blocked until added): " + (missing.Count == 0 ? "none" : string.Join(", ", missing)));
            sb.AppendLine("-- map (Genesis -> ASH)");
            for (int i = 0; i < 32; i++) if (Genesis.TryGetValue(i, out var n)) sb.AppendLine($"{i,2} {n,-18} -> {(map[i] >= 0 ? map[i].ToString() : map[i] == -3 ? "0 (Genesis-only)" : "MISSING")}");
            sb.AppendLine("-- object layer changes"); foreach (var kv in pairs) sb.AppendLine($"{kv.Value,6}  {kv.Key}");
            sb.AppendLine("-- ASH prefabs/scenes referencing changed vendor assets"); foreach (var r in ashRefs) sb.AppendLine(r);
            sb.AppendLine("-- files"); foreach (var c in changedFiles) sb.AppendLine(c);
            Directory.CreateDirectory("AshBridge"); File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log($"[ASH] Layer remap {(write ? "written" : "dry run")}: {changedFiles.Count} files, {layerEdits} object layers, {maskEdits} masks, missing: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}. Report: {ReportPath}");
        }

        static string Name(int i) => Genesis.TryGetValue(i, out var n) ? n : "(unnamed)";
        static void Count(SortedDictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var v) ? v + 1 : 1;
    }
}
