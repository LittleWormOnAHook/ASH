using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.EditorTools.Vendor
{
    /// <summary>
    /// Vendor pack material audit + URP fix-up (ANGRY MESH first; reusable for other packs via Root).
    /// exec ASH.EditorTools.Vendor.ASHVendorMaterialTools.Inventory / ImportAngryMeshUrp / ConvertRemaining / WriteReport
    /// </summary>
    public static class ASHVendorMaterialTools
    {
        public static string Root = "Assets/ANGRY MESH";
        public const string UrpTemplate = "Assets/ANGRY MESH/Nature Pack - PBR Rocks/SRP/URP Template 12.1.0.unitypackage";
        const string BeforeCsv = "AshBridge/vendor_angrymesh_before.csv";
        const string LogCsv = "AshBridge/vendor_angrymesh_changes.csv";
        public const string DocPath = "Docs/Vendor_AngryMesh_Materials.md";

        static string[] Mats() => AssetDatabase.FindAssets("t:Material", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p).ToArray();

        public static string Status(Material m)
        {
            var s = m.shader;
            if (!s || s.name == "Hidden/InternalErrorShader") return "MISSING";
            if (ShaderUtil.ShaderHasError(s)) return "ERROR";
            if (!s.isSupported) return "UNSUPPORTED";
            var n = s.name;
            if (n.StartsWith("Universal Render Pipeline/") || n.StartsWith("Shader Graphs/") || n.Contains("/URP/") || n.StartsWith("Skybox/") || n.StartsWith("Hidden/ASH") || n.StartsWith("ASH/")) return "OK";
            if (n.StartsWith("HDRP/") || n.StartsWith("HDRenderPipeline")) return "HDRP";
            if (!HasUrpPass(s)) return "BUILTIN";
            return "OK";
        }

        // Built-in surface/legacy shaders have no UniversalForward/SRPDefaultUnlit pass usable by URP
        static bool HasUrpPass(Shader s)
        {
            var path = AssetDatabase.GetAssetPath(s);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".shader")) // built-in resources
                return s.name.StartsWith("Unlit/") || s.name.StartsWith("UI/") || s.name.StartsWith("Sprites/");
            var src = File.ReadAllText(path);
            if (src.Contains("UniversalForward") || src.Contains("UniversalPipeline")) return true;
            if (src.Contains("#pragma surface") || src.Contains("ForwardBase")) return false;
            return true;
        }

        [MenuItem("ASH/Vendor/Material Inventory (ANGRY MESH)")]
        public static void Inventory()
        {
            var sb = new StringBuilder("path,shader,status\n"); var counts = new Dictionary<string, int>(); var byShader = new Dictionary<string, int>();
            foreach (var p in Mats())
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) continue;
                var st = Status(m); var sn = m.shader ? m.shader.name : "(none)";
                counts[st] = counts.TryGetValue(st, out var c) ? c + 1 : 1; byShader[sn + " [" + st + "]"] = byShader.TryGetValue(sn + " [" + st + "]", out var c2) ? c2 + 1 : 1;
                sb.Append($"\"{p}\",\"{sn}\",{st}\n");
            }
            Directory.CreateDirectory("AshBridge");
            if (!File.Exists(BeforeCsv)) File.WriteAllText(BeforeCsv, sb.ToString());
            File.WriteAllText("AshBridge/vendor_angrymesh_now.csv", sb.ToString());
            Debug.Log($"[ASH] VENDOR inventory {Root}: {Mats().Length} materials; " + string.Join(", ", counts.Select(k => $"{k.Key} {k.Value}")));
            foreach (var k in byShader.OrderByDescending(k => k.Value)) Debug.Log($"[ASH] VENDOR   {k.Value,3} x {k.Key}");
            foreach (var s in AssetDatabase.FindAssets("t:Shader", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var sh = AssetDatabase.LoadAssetAtPath<Shader>(s);
                if (sh && ShaderUtil.ShaderHasError(sh))
                {
                    var msgs = ShaderUtil.GetShaderMessages(sh).Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Take(2).Select(x => $"{x.message} (line {x.line})");
                    Debug.Log($"[ASH] VENDOR shader ERROR {s}: {string.Join(" | ", msgs)}");
                }
            }
        }

        [MenuItem("ASH/Vendor/Import ANGRY MESH URP Template")]
        public static void ImportAngryMeshUrp()
        {
            if (!File.Exists(UrpTemplate)) { Debug.LogError("[ASH] URP template not found: " + UrpTemplate); return; }
            if (!File.Exists(BeforeCsv)) Inventory();
            AssetDatabase.ImportPackage(UrpTemplate, false);
            Debug.Log("[ASH] VENDOR imported " + UrpTemplate);
        }

        static Texture FirstTex(Material m, params string[] names) { foreach (var n in names) if (m.HasProperty(n) && m.GetTexture(n)) return m.GetTexture(n); return null; }
        static string FirstTexName(Material m, params string[] names) { foreach (var n in names) if (m.HasProperty(n) && m.GetTexture(n)) return n; return null; }
        static bool TryColor(Material m, out Color c, params string[] names) { foreach (var n in names) if (m.HasProperty(n)) { try { c = m.GetColor(n); return true; } catch { } } c = Color.white; return false; }
        static float F(Material m, float def, params string[] names) { foreach (var n in names) if (m.HasProperty(n)) { try { return m.GetFloat(n); } catch { } } return def; }

        /// <summary>Remap every material that still can't render in URP to URP/Lit (or URP particles), preserving maps.</summary>
        [MenuItem("ASH/Vendor/Convert Remaining to URP (ANGRY MESH)")]
        public static void ConvertRemaining()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit"); var pUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var log = File.Exists(LogCsv) ? File.ReadAllText(LogCsv) : "path,old shader,new shader,method,notes\n"; int n = 0;
            foreach (var p in Mats())
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) continue;
                var st = Status(m); if (st == "OK") continue;
                string old = m.shader ? m.shader.name : "(missing)"; string notes;
                bool particle = old.StartsWith("Particles/") || old.StartsWith("Legacy Shaders/Particles") || p.Contains("/Particles/");
                if (particle) notes = ToParticle(m, pUnlit); else notes = ToLit(m, lit, old);
                EditorUtility.SetDirty(m); n++;
                log += $"\"{p}\",\"{old}\",\"{m.shader.name}\",remap,\"{notes}\"\n";
            }
            AssetDatabase.SaveAssets(); File.WriteAllText(LogCsv, log);
            Debug.Log($"[ASH] VENDOR converted {n} materials to URP");
        }

        static string ToLit(Material m, Shader lit, string old)
        {
            // gather before switching (properties vanish after the shader change)
            var baseT = FirstTex(m, "_BaseMap", "_MainTex", "_Albedo", "_AlbedoMap", "_BaseColorMap", "_Diffuse", "_MainTexture", "_Texture");
            TryColor(m, out var col, "_BaseColor", "_Color", "_Tint", "_MainColor", "_AlbedoColor");
            var nrm = FirstTex(m, "_BumpMap", "_NormalMap", "_Normal", "_NormalTex", "_MainNormal"); float nScale = F(m, 1f, "_BumpScale", "_NormalScale", "_NormalStrength");
            string maskName = FirstTexName(m, "_MaskMap", "_Mask", "_MetallicSmoothness");
            var mask = maskName != null ? m.GetTexture(maskName) : null;
            var mg = FirstTex(m, "_MetallicGlossMap", "_Metallic_Smoothness", "_MetallicMap");
            var occ = FirstTex(m, "_OcclusionMap", "_AO", "_AOMap", "_Occlusion"); float occS = F(m, 1f, "_OcclusionStrength", "_AOStrength");
            var em = FirstTex(m, "_EmissionMap", "_Emission"); TryColor(m, out var emC, "_EmissionColor");
            var dA = FirstTex(m, "_DetailAlbedoMap", "_DetailAlbedo"); var dN = FirstTex(m, "_DetailNormalMap", "_DetailNormal"); var dM = FirstTex(m, "_DetailMask");
            float dNS = F(m, 1f, "_DetailNormalMapScale", "_DetailNormalScale");
            float metal = F(m, 0f, "_Metallic"), smooth = F(m, 0.5f, "_Glossiness", "_Smoothness", "_GlossMapScale");
            float cutoff = F(m, 0.5f, "_Cutoff", "_AlphaCutoff", "_Cutout");
            var tiling = baseT && m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one; var offs = baseT && m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            bool clip = m.IsKeywordEnabled("_ALPHATEST_ON") || m.renderQueue >= 2450 && m.renderQueue < 3000 || old.Contains("Leaf") || old.Contains("Grass") || old.Contains("Cross") || old.Contains("Alpha") || old.Contains("Cutout");
            bool twoSided = old.Contains("Leaf") || old.Contains("Grass") || old.Contains("Cross") || old.Contains("2Sided");
            int q = m.renderQueue;

            m.shader = lit; m.shaderKeywords = new string[0];
            var notes = new List<string>();
            m.SetTexture("_BaseMap", baseT); m.SetTextureScale("_BaseMap", tiling); m.SetTextureOffset("_BaseMap", offs); m.SetColor("_BaseColor", col);
            if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", nScale); m.EnableKeyword("_NORMALMAP"); }
            if (mask) // HDRP-style mask: R metal, G AO, B detail mask, A smoothness -> URP metallic (R) + smoothness (A) + occlusion (G)
            {
                m.SetTexture("_MetallicGlossMap", mask); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 1f);
                m.SetTexture("_OcclusionMap", mask); m.SetFloat("_OcclusionStrength", occS); m.EnableKeyword("_OCCLUSIONMAP"); notes.Add($"mask {maskName}: R metal, A smooth, G AO");
            }
            else if (mg) { m.SetTexture("_MetallicGlossMap", mg); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", smooth); }
            else { m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); }
            if (occ) { m.SetTexture("_OcclusionMap", occ); m.SetFloat("_OcclusionStrength", occS); m.EnableKeyword("_OCCLUSIONMAP"); }
            if (em && emC.maxColorComponent > 0.001f) { m.SetTexture("_EmissionMap", em); m.SetColor("_EmissionColor", emC); m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; }
            if (dA || dN) { m.SetTexture("_DetailAlbedoMap", dA); m.SetTexture("_DetailNormalMap", dN); m.SetTexture("_DetailMask", dM); m.SetFloat("_DetailNormalMapScale", dNS); m.EnableKeyword("_DETAIL_MULX2"); notes.Add("detail"); }
            if (clip) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", cutoff); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; notes.Add("alpha clip"); }
            else m.renderQueue = -1;
            if (twoSided) { m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; notes.Add("two-sided"); }
            m.SetFloat("_Surface", 0f); m.SetOverrideTag("RenderType", clip ? "TransparentCutout" : "Opaque");
            if (old.Contains("Snow") || old.Contains("BlendTop")) notes.Add("top snow/moss layer dropped (URP/Lit has none; ASH accumulation will cover rocks)");
            return string.Join("; ", notes);
        }

        static string ToParticle(Material m, Shader p)
        {
            var t = FirstTex(m, "_MainTex", "_BaseMap"); TryColor(m, out var c, "_TintColor", "_Color", "_BaseColor");
            m.shader = p; m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent; m.SetOverrideTag("RenderType", "Transparent");
            return "particles: transparent alpha blend";
        }

        /// <summary>Docs/Vendor_AngryMesh_Materials.md: before vs now for every material + change method.</summary>
        [MenuItem("ASH/Vendor/Write ANGRY MESH Material Report")]
        public static void WriteReport()
        {
            var before = new Dictionary<string, string[]>();
            if (File.Exists(BeforeCsv)) foreach (var l in File.ReadAllLines(BeforeCsv).Skip(1)) { var c = Split(l); if (c.Length >= 3) before[c[0]] = c; }
            var remap = new Dictionary<string, string[]>();
            if (File.Exists(LogCsv)) foreach (var l in File.ReadAllLines(LogCsv).Skip(1)) { var c = Split(l); if (c.Length >= 5) remap[c[0]] = c; }
            var sb = new StringBuilder();
            sb.AppendLine("# ANGRY MESH (Nature Pack - PBR Rocks / Common): URP material fix-up");
            sb.AppendLine();
            sb.AppendLine("Vendor pack stays out of git (`/Assets/ANGRY MESH/` in .gitignore); this log is ours. Tool: `Assets/_ASH/Editor/Vendor/ASHVendorMaterialTools.cs`.");
            sb.AppendLine("Method `urp-template` = vendor's own `SRP/URP Template 12.1.0.unitypackage` (URP shaders + URP materials); `remap` = ASH remap to URP/Lit or URP Particles; `unchanged` = already URP-compatible.");
            sb.AppendLine();
            sb.AppendLine("```csv"); sb.AppendLine("path,old shader,old status,new shader,new status,method,notes");
            int ok = 0, bad = 0;
            foreach (var p in Mats())
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) continue;
                var st = Status(m); if (st == "OK") ok++; else bad++;
                before.TryGetValue(p, out var b); remap.TryGetValue(p, out var r);
                string oldS = r != null ? r[1] : b != null ? b[1] : "(new from template)"; string oldSt = b != null ? b[2] : "-";
                string method = r != null ? "remap" : b == null ? "urp-template" : b[1] != m.shader.name ? "urp-template" : "unchanged";
                sb.AppendLine($"\"{p.Substring(Root.Length + 1)}\",\"{(b != null ? b[1] : oldS)}\",{oldSt},\"{m.shader.name}\",{st},{method},\"{(r != null ? r[4] : "")}\"");
            }
            sb.AppendLine("```"); sb.AppendLine(); sb.AppendLine($"Result: {ok} OK, {bad} not URP-compatible.");
            File.WriteAllText(DocPath, sb.ToString());
            Debug.Log($"[ASH] VENDOR report {DocPath}: {ok} OK, {bad} bad");
        }

        /// <summary>Render a few pack prefabs in a preview scene (sync shader compile) and count magenta pixels.</summary>
        public static void RenderCheck()
        {
            var picks = new[] { "Rock_01_Moss", "Rock_01_Snow", "Rock_01_Sand", "Rock_13_Moss", "Grass_Group_01", "Fern_01", "Root_01", "Snow" };
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            bool prevAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            Directory.CreateDirectory("AshBridge/shots/vendor");
            try
            {
                var lgo = new GameObject("L"); var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f; lgo.transform.rotation = Quaternion.Euler(50, -30, 0);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lgo, scene);
                var cgo = new GameObject("C"); var cam = cgo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.35f, 0.4f, 0.45f); cam.cameraType = CameraType.Game;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cgo, scene);
                var rt = new RenderTexture(512, 512, 24); cam.targetTexture = rt; var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
                int total = 0, pinkShots = 0;
                foreach (var name in picks)
                {
                    var path = prefabs.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == name); if (path == null) { Debug.Log($"[ASH] VENDOR render: {name} not found"); continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                    var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { Object.DestroyImmediate(go); continue; }
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    float d = Mathf.Max(b.extents.magnitude, 0.5f) * 2.4f; cam.transform.position = b.center + new Vector3(0.6f, 0.45f, -1f).normalized * d; cam.transform.LookAt(b.center);
                    cam.nearClipPlane = 0.01f; cam.farClipPlane = d * 4f;
                    cam.Render(); cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); tex.Apply(); RenderTexture.active = null;
                    int pink = 0; foreach (var c in tex.GetPixels32()) if (c.r > 200 && c.b > 200 && c.g < 60) pink++;
                    File.WriteAllBytes($"AshBridge/shots/vendor/{name}.png", tex.EncodeToPNG());
                    var mats = string.Join(", ", rs.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.shader.name).Distinct());
                    Debug.Log($"[ASH] VENDOR render {name}: magenta {100f * pink / (512 * 512):F2}% ({mats})");
                    total++; if (pink > 500) pinkShots++;
                    Object.DestroyImmediate(go);
                }
                Debug.Log($"[ASH] VENDOR render check: {total} prefabs, {pinkShots} with magenta");
                cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            finally { ShaderUtil.allowAsyncCompilation = prevAsync; UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>Count materials anywhere under Root whose renderers reference missing materials (null slots) in prefabs.</summary>
        public static void PrefabSlotCheck()
        {
            int prefabs = 0, empty = 0, bad = 0;
            foreach (var p in AssetDatabase.FindAssets("t:Prefab", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p); if (!go) continue; prefabs++;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) { if (!m) { empty++; if (empty <= 5) Debug.Log($"[ASH] VENDOR empty slot {p} / {r.name}"); } else if (Status(m) != "OK") { bad++; if (bad <= 10) Debug.Log($"[ASH] VENDOR non-URP {p}: {m.name} ({m.shader.name})"); } }
            }
            Debug.Log($"[ASH] VENDOR prefab slots: {prefabs} prefabs, {empty} empty slots, {bad} non-URP material refs");
        }

        /// <summary>Play Mode: spawn a row of pack prefabs under the scene's real lighting (nothing saved).</summary>
        public static void PlayRenderCheck()
        {
            var names = new[] { "Rock_01_Moss", "Rock_01_Snow", "Rock_01_Sand", "Rock_07_Moss", "Grass_Group_01", "Fern_01", "Root_01" };
            var all = AssetDatabase.FindAssets("t:Prefab", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            ASH.DebugTools.ASHPrefabRenderCheck.Prefabs = names.Select(n => all.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == n)).Where(x => x != null).Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
            ASH.DebugTools.ASHPrefabRenderCheck.Run();
        }

        static string[] Split(string line)
        {
            var res = new List<string>(); var cur = new StringBuilder(); bool q = false;
            foreach (var ch in line) { if (ch == '"') q = !q; else if (ch == ',' && !q) { res.Add(cur.ToString()); cur.Clear(); } else cur.Append(ch); }
            res.Add(cur.ToString()); return res.ToArray();
        }
    }
}
