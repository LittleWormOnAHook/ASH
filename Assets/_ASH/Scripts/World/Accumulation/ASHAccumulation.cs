using System;
using System.Collections.Generic;
using ASH.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.World.Accumulation
{
    /// <summary>
    /// Drop-on: swaps this object's materials (every slot of every Mesh/Skinned renderer) to cached ASH/Accumulation Lit copies so it
    /// collects ash/snow. Maps and settings are carried over (URP Lit property set; other shaders remapped). Transparent, particle and
    /// UI materials are left as they are. NEVER used on pickups: renderers under an ASHItemPickup or any IASHUsable are skipped.
    /// Editor: ASH/World/Apply Accumulation To Selected (prefab assets/instances edit the prefab; copies under Assets/_ASH/Materials/Accumulation).
    /// Play mode: in-memory copies.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ASHAccumulation : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHAccumulation()
        {
            _runtime.Clear();
            All.Clear();
        }

        public const string ShaderName = "ASH/Accumulation Lit";
        public const string SourceShaderName = "Universal Render Pipeline/Lit";

        [Serializable] public sealed class Slot { public Renderer renderer; public Material[] original; }
        public List<Slot> slots = new List<Slot>();
        public bool applied;

        /// <summary>Editor hook: creates persistent variant assets. Null at runtime = in-memory variants.</summary>
        public static Func<Material, Material> VariantProvider;
        static readonly Dictionary<Material, Material> _runtime = new Dictionary<Material, Material>();

        public static readonly List<ASHAccumulation> All = new List<ASHAccumulation>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); if (Application.isPlaying && !applied) Apply(); }
        void OnDisable() { All.Remove(this); }

        public static bool IsExcluded(Transform t)
        {
            for (var p = t; p; p = p.parent)
            {
                if (p.GetComponent<ASHItemPickup>()) return true;
                foreach (var mb in p.GetComponents<MonoBehaviour>()) if (mb is IASHUsable) return true;
            }
            return false;
        }

        public static Material Variant(Material m)
        {
            if (!m || !m.shader) return m;
            if (m.shader.name == ShaderName) return m;
            if (!IsSupported(m)) return m; // transparent / particle / UI: left as is
            if (VariantProvider != null) return VariantProvider(m) ?? m;
            if (_runtime.TryGetValue(m, out var v) && v) return v;
            v = CreateVariant(m); if (!v) return m; _runtime[m] = v; return v;
        }

        public const string PWGeneralShaderName = "Shader Graphs/PW_General_ShaderGraph_URP";

        /// <summary>Why a material is not converted (null = convertible).</summary>
        public static string UnsupportedReason(Material m)
        {
            if (!m || !m.shader) return "missing";
            var n = m.shader.name;
            if (n == ShaderName) return "already ASH";
            if (n.Contains("Particle") || n.StartsWith("UI/") || n.StartsWith("Sprites/") || n.StartsWith("Skybox") || n.StartsWith("Hidden/") || n.Contains("TextMeshPro") || n.Contains("Terrain")) return "particle/UI/sky/terrain shader";
            if (m.renderQueue >= 2900 || m.GetTag("RenderType", false) == "Transparent" || (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f) || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")) return "transparent";
            return null;
        }
        public static bool IsSupported(Material m) => UnsupportedReason(m) == null;

        // t570: HPA Shader Graphs (Shader Graphs/SG_uv0_*) use un-prefixed references: Albedo / Normal / MetallicSmoothness / _Tile
        static readonly string[] BaseTex = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_AlbedoMap", "_BaseAlbedoASmoothness", "_Diffuse", "_DiffuseMap", "_MainTexture", "_Texture", "_Albedo_Texture", "Albedo", "BaseMap", "Base_Map", "MainTex", "Diffuse", "BaseColorMap" };
        static readonly string[] ColorP = { "_BaseColor", "_Color", "_Tint", "_MainColor", "_AlbedoColor" };
        static readonly string[] NormTex = { "_BumpMap", "_NormalMap", "_BaseNormalMap", "_Normal", "_NormalTex", "_MainNormal", "_Normal_Map", "Normal", "NormalMap" };

        /// <summary>True when the source shader has a texture property that holds a texture but none we recognise as the albedo:
        /// converting it would come out white (t570: HPA SG_uv0_Rust cabin props), so it is left on its own shader.</summary>
        static bool HasUnmappedTextures(Material src, string baseName)
        {
            if (baseName != null) return false;
            var sh = src.shader;
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                if (sh.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                var n = sh.GetPropertyName(i);
                if (n.StartsWith("unity_")) continue;
                if (src.GetTexture(n)) return true;
            }
            return false;
        }

        /// <summary>Shader Graph tiling property (_Tile as Vector xy or a single Float), or null.</summary>
        static Vector2? GraphTiling(Material src)
        {
            var sh = src.shader;
            foreach (var n in new[] { "_Tile", "_Tiling", "Tiling", "Tile" })
            {
                int i = sh.FindPropertyIndex(n); if (i < 0) continue;
                var t = sh.GetPropertyType(i);
                if (t == ShaderPropertyType.Vector) { var v = src.GetVector(n); if (v.x > 0f && v.y > 0f) return new Vector2(v.x, v.y); }
                else if (t == ShaderPropertyType.Float || t == ShaderPropertyType.Range) { float f = src.GetFloat(n); if (f > 0f) return new Vector2(f, f); }
            }
            return null;
        }

        /// <summary>New (unsaved) accumulation copy of any opaque/cutout material. URP Lit property sets copy as is; others are remapped
        /// (albedo, color, normal, metallic/smoothness, mask, occlusion, emission, detail, alpha clip, cull, tiling).</summary>
        public static Material CreateVariant(Material src)
        {
            var sh = Shader.Find(ShaderName); if (!sh || !IsSupported(src)) return null;
            var sn = src.shader.name;
            if (sn == SourceShaderName || sn == "Universal Render Pipeline/Complex Lit")
                return new Material(src) { name = src.name + "_Accum", shader = sh };

            Texture T(params string[] ns) { foreach (var n in ns) if (src.HasProperty(n) && src.GetTexture(n)) return src.GetTexture(n); return null; }
            string TN(params string[] ns) { foreach (var n in ns) if (src.HasProperty(n) && src.GetTexture(n)) return n; return null; }
            float F(float d, params string[] ns) { foreach (var n in ns) if (src.HasProperty(n)) { try { return src.GetFloat(n); } catch { } } return d; }
            bool C(out Color c, params string[] ns) { foreach (var n in ns) if (src.HasProperty(n)) { try { c = src.GetColor(n); return true; } catch { } } c = Color.white; return false; }

            var baseName = TN(BaseTex); var baseT = baseName != null ? src.GetTexture(baseName) : null;
            if (HasUnmappedTextures(src, baseName)) return null; // would render white: keep the original material
            var m = new Material(sh) { name = src.name + "_Accum" };
            m.SetTexture("_BaseMap", baseT);
            if (baseName != null)
            {
                var tile = GraphTiling(src);
                m.SetTextureScale("_BaseMap", tile.HasValue ? Vector2.Scale(tile.Value, src.GetTextureScale(baseName)) : src.GetTextureScale(baseName));
                m.SetTextureOffset("_BaseMap", src.GetTextureOffset(baseName));
            }
            if (C(out var col, ColorP)) { col.a = 1f; m.SetColor("_BaseColor", col); }
            var nrm = T(NormTex); if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", F(1f, "_BumpScale", "_BumpMapScale", "_NormalScale", "_NormalStrength", "_Normal_Value")); m.EnableKeyword("_NORMALMAP"); }
            var mask = T("_MaskMap", "_Mask"); var mg = T("_MetallicGlossMap", "_MetallicSmoothness", "_Metallic_Smoothness", "_MetallicMap", "MetallicSmoothness");
            float metal = F(0f, "_Metallic"), smooth = F(0.3f, "_Smoothness", "_Glossiness", "_GlossMapScale", "_BaseSmoothness", "Smoothness_Control");
            if (mask) { m.SetTexture("_MetallicGlossMap", mask); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetTexture("_OcclusionMap", mask); m.EnableKeyword("_OCCLUSIONMAP"); m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 1f); }
            else if (mg) { m.SetTexture("_MetallicGlossMap", mg); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", smooth); }
            else { m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); }
            if (baseName == "_BaseAlbedoASmoothness") { m.SetFloat("_SmoothnessTextureChannel", 1f); m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"); }
            var occ = T("_OcclusionMap", "_AO", "_AOMap", "_Occlusion", "_BaseAOANoiseMask"); if (occ && !mask) { m.SetTexture("_OcclusionMap", occ); m.SetFloat("_OcclusionStrength", F(1f, "_OcclusionStrength", "_AOStrength", "_BaseAOIntensity")); m.EnableKeyword("_OCCLUSIONMAP"); }
            var em = T("_EmissionMap", "_Emission"); if (em && C(out var emC, "_EmissionColor") && emC.maxColorComponent > 0.001f) { m.SetTexture("_EmissionMap", em); m.SetColor("_EmissionColor", emC); m.EnableKeyword("_EMISSION"); }
            var dA = T("_DetailAlbedoMap", "_DetailAlbedo"); var dN = T("_DetailNormalMap", "_DetailNormal");
            if (dA || dN) { m.SetTexture("_DetailAlbedoMap", dA); m.SetTexture("_DetailNormalMap", dN); m.SetFloat("_DetailNormalMapScale", F(1f, "_DetailNormalMapScale", "_DetailNormalScale", "_DetailNormalMapIntensity")); m.EnableKeyword("_DETAIL_MULX2"); }
            bool clip = src.IsKeywordEnabled("_ALPHATEST_ON") || F(0f, "_AlphaClip", "_AlphaTest") > 0.5f || (src.renderQueue >= 2450 && src.renderQueue < 2900);
            if (clip) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", F(0.5f, "_Cutoff", "_AlphaCutoff", "_Cutout")); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; m.SetOverrideTag("RenderType", "TransparentCutout"); }
            float cull = F(2f, "_Cull", "_CullMode"); m.SetFloat("_Cull", cull); if (cull < 0.5f) m.doubleSidedGI = true;
            m.enableInstancing = src.enableInstancing;
            return m;
        }

        /// <summary>Returns renderers changed; skipped = renderers excluded (pickups/interactables) or with nothing convertible.</summary>
        public int Apply() => Apply(out _);
        public int Apply(out int skipped) => Apply(out skipped, out _, out _);
        public int Apply(out int skipped, out int slotsChanged, out int slotsKept)
        {
            skipped = 0; slotsChanged = 0; slotsKept = 0; int changed = 0;
            if (IsExcluded(transform)) { skipped = 1; Debug.LogWarning($"[ASH] Accumulation refused on '{name}': pickup/interactable."); return 0; }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (IsExcluded(r.transform) || slots.Exists(s => s.renderer == r)) { skipped++; continue; }
                var src = r.sharedMaterials; var dst = new Material[src.Length]; bool any = false;
                for (int i = 0; i < src.Length; i++) { dst[i] = Variant(src[i]); if (dst[i] != src[i]) { any = true; slotsChanged++; } else slotsKept++; }
                if (!any) { skipped++; continue; }
                slots.Add(new Slot { renderer = r, original = src }); r.sharedMaterials = dst; changed++;
            }
            applied = true; return changed;
        }

        public void Revert()
        {
            foreach (var s in slots) if (s.renderer) s.renderer.sharedMaterials = s.original;
            slots.Clear(); applied = false;
        }
    }
}
