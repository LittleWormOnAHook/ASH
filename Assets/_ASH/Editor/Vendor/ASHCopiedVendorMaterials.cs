using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.EditorTools.Vendor
{
    /// <summary>
    /// Batch URP conversion for ASH's copies of Invector, Malbers and JMO WarFX (Genesis is HDRP; Genesis itself is never touched).
    /// HDRP materials lose their shader in URP, so their saved properties are read straight from the .mat YAML.
    /// HDRP/Lit + built-in Standard/legacy lit -> URP/Lit; HDRP/Unlit -> URP/Unlit; legacy particles -> URP/Particles/Unlit;
    /// WarFX lit bullet-hole shaders -> URP/Lit transparent; Malbers custom shaders -> Malbers' own URP shader package (same GUIDs).
    /// Keeps base map + tiling, color, normal, mask (metal R / AO G / smooth A), metallic/smoothness, emission, alpha mode, culling.
    /// Idempotent: anything already on a URP shader is skipped. Report: AshBridge/materials_urp_report.txt
    /// </summary>
    public static class ASHCopiedVendorMaterials
    {
        public static readonly string[] Roots = { "Assets/Invector-3rdPersonController", "Assets/Malbers Animations", "Assets/JMO Assets" };
        const string HdrpLit = "6e4ae4064600d784cac1e41a9e6f2e59", HdrpUnlit = "c4edd00ff2db5b24391a4fcb1762e459";
        const string MalbersUrpPkg = "Assets/Malbers Animations/Common/Shaders/URP_Malbers Shaders 2023.unitypackage";
        const string MalbersProbe = "Assets/Malbers Animations/Common/Shaders/Color4x4.shader";
        const string ReportPath = "AshBridge/materials_urp_report.txt";

        [MenuItem("ASH/Tools/Materials/Report Copied Vendor Materials (dry run)")] public static void DryRun() => Run(false);
        [MenuItem("ASH/Tools/Materials/Convert Copied Vendor Materials to URP")] public static void Convert() => Run(true);
        [MenuItem("ASH/Tools/Materials/Import Malbers URP Shaders")]
        public static void ImportMalbersUrp()
        {
            if (Blocked()) return;
            if (MalbersIsUrp()) { Debug.Log("[ASH] Materials: Malbers shaders already URP."); return; }
            if (!File.Exists(MalbersUrpPkg)) { Debug.LogWarning("[ASH] Materials: Malbers URP package not found: " + MalbersUrpPkg); return; }
            AssetDatabase.ImportPackage(MalbersUrpPkg, false);
            Debug.Log("[ASH] Materials: imported Malbers URP shaders (replaces the built-in Malbers shaders in ASH's copy, same GUIDs).");
        }

        static bool MalbersIsUrp() => File.Exists(MalbersProbe) && File.ReadAllText(MalbersProbe).Contains("UniversalPipeline");
        static bool Blocked()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) { Debug.LogWarning("[ASH] Materials: stop Play mode / wait for compile first."); return true; }
            return false;
        }

        sealed class Props
        {
            public readonly Dictionary<string, (Texture t, Vector2 s, Vector2 o)> Tex = new Dictionary<string, (Texture, Vector2, Vector2)>();
            public readonly Dictionary<string, float> Fl = new Dictionary<string, float>();
            public readonly Dictionary<string, Color> Col = new Dictionary<string, Color>();
            public Texture T(string n) => Tex.TryGetValue(n, out var v) ? v.t : null;
            public bool HasT(string n) => T(n) != null;
            public float F(string n, float d) => Fl.TryGetValue(n, out var v) ? v : d;
            public Color C(string n, Color d) => Col.TryGetValue(n, out var v) ? v : d;
            public bool HasC(string n) => Col.ContainsKey(n);
            public (Vector2 s, Vector2 o) ST(string n) => Tex.TryGetValue(n, out var v) ? (v.s, v.o) : (Vector2.one, Vector2.zero);
        }

        static float P(string s) => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

        static Props Parse(string y)
        {
            var p = new Props();
            foreach (Match m in Regex.Matches(y, @"- (\w+):\s*\r?\n\s*m_Texture: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d+)?\}\s*\r?\n\s*m_Scale: \{x: ([^,]+), y: ([^}]+)\}\s*\r?\n\s*m_Offset: \{x: ([^,]+), y: ([^}]+)\}"))
            {
                Texture t = null; var g = m.Groups[3].Value;
                if (g.Length == 32) { var ap = AssetDatabase.GUIDToAssetPath(g); if (!string.IsNullOrEmpty(ap)) t = AssetDatabase.LoadAssetAtPath<Texture>(ap); }
                p.Tex[m.Groups[1].Value] = (t, new Vector2(P(m.Groups[4].Value), P(m.Groups[5].Value)), new Vector2(P(m.Groups[6].Value), P(m.Groups[7].Value)));
            }
            int fi = y.IndexOf("m_Floats:", StringComparison.Ordinal), ci = y.IndexOf("m_Colors:", StringComparison.Ordinal);
            if (fi >= 0)
            {
                var sec = ci > fi ? y.Substring(fi, ci - fi) : y.Substring(fi);
                foreach (Match m in Regex.Matches(sec, @"^\s*- (\w+): (-?[0-9.eE+\-]+)\s*$", RegexOptions.Multiline)) p.Fl[m.Groups[1].Value] = P(m.Groups[2].Value);
            }
            foreach (Match m in Regex.Matches(y, @"- (\w+): \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}"))
                p.Col[m.Groups[1].Value] = new Color(P(m.Groups[2].Value), P(m.Groups[3].Value), P(m.Groups[4].Value), P(m.Groups[5].Value));
            return p;
        }

        static string Classify(Material m, string yaml, out string from)
        {
            var guid = Regex.Match(yaml, @"m_Shader: \{fileID: -?\d+, guid: ([0-9a-f]{32})").Groups[1].Value;
            var s = m.shader; var sn = s ? s.name : "";
            from = sn;
            if (guid == HdrpLit) { from = "HDRP/Lit"; return "hdrp-lit"; }
            if (guid == HdrpUnlit) { from = "HDRP/Unlit"; return "hdrp-unlit"; }
            if (sn.StartsWith("Universal Render Pipeline/") || sn.StartsWith("Shader Graphs/") || sn.StartsWith("ASH/")) return "urp";
            if (!s || sn == "Hidden/InternalErrorShader")
            {
                if (yaml.Contains("_UnlitColorMap")) { from = "HDRP/Unlit?"; return "hdrp-unlit"; }
                if (yaml.Contains("_BaseColorMap") || yaml.Contains("_MaskMap")) { from = "HDRP/Lit?"; return "hdrp-lit"; }
                from = "missing shader " + guid; return "left";
            }
            var sp = AssetDatabase.GetAssetPath(s);
            if (sp.EndsWith(".shader") || sp.EndsWith(".shadergraph"))
            {
                if (sp.EndsWith(".shadergraph")) return "urp";
                var src = File.ReadAllText(sp);
                if (src.Contains("UniversalPipeline") || src.Contains("UniversalForward")) return "urp";
                bool lit = src.Contains("#pragma surface") || src.Contains("ForwardBase") || src.Contains("\"LightMode\"=\"Vertex\"");
                if (!lit) return "keep";
                if (sn.StartsWith("WFX") || sp.Contains("WarFX")) return "wfx-lit";
                return "left";
            }
            // built-in
            if (sn.Contains("Particles/")) return "particle";
            if (sn == "Standard" || sn == "Standard (Specular setup)" || sn == "Autodesk Interactive" || sn.StartsWith("Legacy Shaders/") || sn.StartsWith("Mobile/") || sn.StartsWith("Nature/")) return "std-lit";
            return "keep"; // Skybox/, UI/, Sprites/, Unlit/, GUI/, Hidden/ render in URP
        }

        static void Run(bool write)
        {
            if (Blocked()) return;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit"), unlit = Shader.Find("Universal Render Pipeline/Unlit"), part = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!lit || !unlit || !part) { Debug.LogError("[ASH] Materials: URP shaders not found."); return; }
            var roots = Roots.Where(AssetDatabase.IsValidFolder).ToArray();
            var paths = AssetDatabase.FindAssets("t:Material", roots).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".mat")).Distinct().OrderBy(p => p).ToList();
            var dirty = new List<Material>(); var tally = new SortedDictionary<string, int>(); var leftList = new List<string>(); var rows = new List<string>();
            int converted = 0, already = 0, kept = 0, left = 0;
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    var p = paths[i];
                    if (i % 25 == 0 && EditorUtility.DisplayCancelableProgressBar("ASH materials", p, i / (float)paths.Count)) break;
                    var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) continue;
                    var yaml = File.ReadAllText(p);
                    var kind = Classify(m, yaml, out var from);
                    tally[kind + "  (" + from + ")"] = tally.TryGetValue(kind + "  (" + from + ")", out var n) ? n + 1 : 1;
                    if (kind == "urp") { already++; continue; }
                    if (kind == "keep") { kept++; continue; }
                    if (kind == "left") { left++; leftList.Add(p + "  [" + from + "]"); continue; }
                    if (!write) { rows.Add($"would convert ({kind}): {p}"); converted++; continue; }
                    var pr = Parse(yaml);
                    switch (kind)
                    {
                        case "hdrp-lit": ToLit(m, lit, pr, true, from); break;
                        case "std-lit": ToLit(m, lit, pr, false, from); break;
                        case "wfx-lit": ToLit(m, lit, pr, false, from, wfx: true); break;
                        case "hdrp-unlit": ToUnlit(m, unlit, pr); break;
                        case "particle": ToParticle(m, part, pr, from); break;
                    }
                    EditorUtility.SetDirty(m); dirty.Add(m); converted++;
                    rows.Add($"converted ({kind}) {from} -> {m.shader.name}: {p}");
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            // per-asset save only: AssetDatabase.SaveAssets() would also flush any dirty ProjectSettings managers
            foreach (var d in dirty) AssetDatabase.SaveAssetIfDirty(d);

            var sb = new StringBuilder();
            sb.AppendLine($"[ASH] Copied vendor materials {(write ? "CONVERT" : "DRY RUN")} {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"materials={paths.Count} {(write ? "converted" : "to convert")}={converted} alreadyURP={already} keptBuiltinOK={kept} left={left} malbersUrpShaders={MalbersIsUrp()}");
            sb.AppendLine("-- by kind (shader before)"); foreach (var kv in tally) sb.AppendLine($"{kv.Value,5}  {kv.Key}");
            sb.AppendLine("-- left (not converted)"); foreach (var l in leftList) sb.AppendLine(l);
            sb.AppendLine("-- rows"); foreach (var r in rows) sb.AppendLine(r);
            Directory.CreateDirectory("AshBridge"); File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log($"[ASH] Materials {(write ? "converted" : "dry run")}: {converted} of {paths.Count}, already URP {already}, built-in OK {kept}, left {left}. Report: {ReportPath}");
        }

        static void Tex(Material m, string prop, Texture t, (Vector2 s, Vector2 o) st)
        {
            if (!m.HasProperty(prop)) return;
            m.SetTexture(prop, t); m.SetTextureScale(prop, st.s); m.SetTextureOffset(prop, st.o);
        }

        static void ToLit(Material m, Shader lit, Props p, bool hdrp, string from, bool wfx = false)
        {
            string baseN = hdrp ? "_BaseColorMap" : "_MainTex";
            var baseT = p.T(baseN); var st = p.ST(baseN);
            Color col = hdrp ? p.C("_BaseColor", Color.white) : wfx ? p.C("_TintColor", p.C("_Color", Color.white)) : p.C("_Color", Color.white);
            var nrm = hdrp ? p.T("_NormalMap") : p.T("_BumpMap"); float nS = hdrp ? p.F("_NormalScale", 1f) : p.F("_BumpScale", 1f);
            var mask = hdrp ? p.T("_MaskMap") : null; var mg = hdrp ? null : p.T("_MetallicGlossMap"); var occ = hdrp ? null : p.T("_OcclusionMap");
            float metal = p.F("_Metallic", 0f);
            float smooth = hdrp ? p.F("_Smoothness", 0.5f) : p.F("_Glossiness", p.F("_Smoothness", wfx ? 0.5f : 0.5f));
            if (from.Contains("Specular") && !hdrp) smooth = Mathf.Max(smooth, Mathf.Clamp01(p.F("_Shininess", 0.4f) * 1.2f));
            var emT = hdrp ? p.T("_EmissiveColorMap") : p.T("_EmissionMap");
            Color emC;
            if (hdrp)
            {
                emC = p.HasC("_EmissiveColorLDR") && p.F("_UseEmissiveIntensity", 0f) > 0.5f ? p.C("_EmissiveColorLDR", Color.black) * 2f : p.C("_EmissiveColor", Color.black);
                float mx = emC.maxColorComponent; if (mx > 8f) emC *= 8f / mx;
            }
            else emC = p.C("_EmissionColor", Color.black);

            bool transparent, clip, twoSided; int blend; float cutoff;
            if (hdrp)
            {
                transparent = p.F("_SurfaceType", 0f) > 0.5f; int hb = Mathf.RoundToInt(p.F("_BlendMode", 0f));
                blend = hb == 1 ? 2 : hb == 4 ? 1 : 0; clip = p.F("_AlphaCutoffEnable", 0f) > 0.5f; cutoff = p.F("_AlphaCutoff", 0.5f);
                twoSided = p.F("_DoubleSidedEnable", 0f) > 0.5f;
            }
            else if (wfx) { transparent = true; blend = 0; clip = false; cutoff = 0.5f; twoSided = false; }
            else
            {
                int mode = Mathf.RoundToInt(p.F("_Mode", 0f));
                bool legacyAlpha = from.Contains("Transparent/") && !from.Contains("Cutout"), legacyCut = from.Contains("Cutout");
                transparent = mode >= 2 || legacyAlpha; blend = mode == 3 ? 1 : 0; clip = mode == 1 || legacyCut; cutoff = p.F("_Cutoff", 0.5f); twoSided = false;
            }

            m.shader = lit; m.shaderKeywords = new string[0];
            Tex(m, "_BaseMap", baseT, st); Tex(m, "_MainTex", baseT, st);
            m.SetColor("_BaseColor", col); m.SetColor("_Color", col);
            if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", nS); m.EnableKeyword("_NORMALMAP"); } else m.SetTexture("_BumpMap", null);
            m.SetFloat("_WorkflowMode", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f);
            if (mask)
            {
                m.SetTexture("_MetallicGlossMap", mask); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", Mathf.Clamp01(p.F("_SmoothnessRemapMax", 1f))); m.SetFloat("_Metallic", 1f);
                m.SetTexture("_OcclusionMap", mask); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP");
            }
            else if (mg) { m.SetTexture("_MetallicGlossMap", mg); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", p.F("_GlossMapScale", 1f)); m.SetFloat("_Metallic", 1f); }
            else { m.SetTexture("_MetallicGlossMap", null); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); }
            if (occ) { m.SetTexture("_OcclusionMap", occ); m.SetFloat("_OcclusionStrength", p.F("_OcclusionStrength", 1f)); m.EnableKeyword("_OCCLUSIONMAP"); }
            if (emC.maxColorComponent > 0.001f)
            {
                m.SetColor("_EmissionColor", emC); m.SetTexture("_EmissionMap", emT); m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else { m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; }
            Surface(m, transparent, blend, clip, cutoff, twoSided);
            UrpValidate(m, true);
        }

        static void ToUnlit(Material m, Shader unlit, Props p)
        {
            var t = p.T("_UnlitColorMap"); var st = p.ST("_UnlitColorMap");
            var col = p.C("_UnlitColor", Color.white);
            bool transparent = p.F("_SurfaceType", 0f) > 0.5f; int hb = Mathf.RoundToInt(p.F("_BlendMode", 0f));
            int blend = hb == 1 ? 2 : hb == 4 ? 1 : 0;
            m.shader = unlit; m.shaderKeywords = new string[0];
            Tex(m, "_BaseMap", t, st); Tex(m, "_MainTex", t, st); m.SetColor("_BaseColor", col); m.SetColor("_Color", col);
            Surface(m, transparent, blend, p.F("_AlphaCutoffEnable", 0f) > 0.5f, p.F("_AlphaCutoff", 0.5f), p.F("_DoubleSidedEnable", 0f) > 0.5f);
            UrpValidate(m, false);
        }

        static void ToParticle(Material m, Shader part, Props p, string from)
        {
            var t = p.T("_MainTex"); var st = p.ST("_MainTex");
            Color col = p.HasC("_TintColor") ? p.C("_TintColor", Color.gray) * 2f : p.C("_Color", Color.white);
            var n = from.ToLowerInvariant();
            int blend = n.Contains("premultipl") ? 1 : n.Contains("multiply") ? 3 : n.Contains("add") ? 2 : 0;
            m.shader = part; m.shaderKeywords = new string[0];
            Tex(m, "_BaseMap", t, st); Tex(m, "_MainTex", t, st); m.SetColor("_BaseColor", col);
            Surface(m, true, blend, false, 0.5f, true);
            UrpValidate(m, false);
        }

        static void Surface(Material m, bool transparent, int blend, bool clip, float cutoff, bool twoSided)
        {
            m.SetFloat("_Surface", transparent ? 1f : 0f); m.SetFloat("_Blend", blend);
            m.SetFloat("_AlphaClip", clip ? 1f : 0f); m.SetFloat("_Cutoff", cutoff); m.SetFloat("_Cull", twoSided ? 0f : 2f);
            if (clip) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            if (transparent)
            {
                m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetFloat("_ZWrite", 0f); m.renderQueue = (int)RenderQueue.Transparent;
                BlendMode src = BlendMode.SrcAlpha, dst = BlendMode.OneMinusSrcAlpha;
                if (blend == 1) { src = BlendMode.One; m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); }
                else if (blend == 2) { dst = BlendMode.One; }
                else if (blend == 3) { src = BlendMode.DstColor; dst = BlendMode.Zero; m.EnableKeyword("_ALPHAMODULATE_ON"); }
                m.SetFloat("_SrcBlend", (float)src); m.SetFloat("_DstBlend", (float)dst);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetShaderPassEnabled("DepthOnly", false);
            }
            else
            {
                m.SetOverrideTag("RenderType", clip ? "TransparentCutout" : "Opaque"); m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetFloat("_ZWrite", 1f); m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 0f);
                m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", 0f);
                m.renderQueue = clip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
                m.SetShaderPassEnabled("DepthOnly", true);
            }
        }

        // URP's own keyword/render-state validation (same code the material inspector runs), if the editor API is reachable.
        static MethodInfo _baseKw, _litKw; static bool _looked;
        static void UrpValidate(Material m, bool isLit)
        {
            if (!_looked)
            {
                _looked = true;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!a.GetName().Name.StartsWith("Unity.RenderPipelines.Universal.Editor")) continue;
                    var bt = a.GetType("UnityEditor.BaseShaderGUI");
                    _baseKw = bt?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == "SetMaterialKeywords" && x.GetParameters().Length == 3);
                    var lt = a.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitGUI");
                    _litKw = lt?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == "SetMaterialKeywords" && x.GetParameters().Length == 1);
                }
            }
            try
            {
                Action<Material> lk = null;
                if (isLit && _litKw != null) lk = mm => _litKw.Invoke(null, new object[] { mm });
                _baseKw?.Invoke(null, new object[] { m, lk, null });
            }
            catch (Exception e) { Debug.LogWarning("[ASH] Materials: URP validate skipped for " + m.name + ": " + e.GetBaseException().Message); }
        }
    }
}
