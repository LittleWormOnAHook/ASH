using System;
using System.Collections.Generic;
using ASH.Interaction;
using UnityEngine;

namespace ASH.World.Accumulation
{
    /// <summary>
    /// Drop-on: swaps this object's URP/Lit (and Gaia PW General) materials to cached ASH/Accumulation Lit variants so it collects ash/snow.
    /// NEVER used on pickups: renderers under an ASHItemPickup or any IASHUsable (interactable) are skipped.
    /// Editor: ASH/World/Apply Accumulation To Selected (creates variant assets). Play mode: in-memory variants.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ASHAccumulation : MonoBehaviour
    {
        public const string ShaderName = "ASH/Accumulation Lit";
        public const string SourceShaderName = "Universal Render Pipeline/Lit";

        [Serializable] public sealed class Slot { public Renderer renderer; public Material[] original; }
        public List<Slot> slots = new List<Slot>();
        public bool applied;

        /// <summary>Editor hook: creates persistent variant assets. Null at runtime = in-memory variants.</summary>
        public static Func<Material, Material> VariantProvider;
        static readonly Dictionary<Material, Material> _runtime = new Dictionary<Material, Material>();

        void OnEnable() { if (Application.isPlaying && !applied) Apply(); }

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
            if (!IsSupported(m)) return m; // unsupported shader: left as is
            if (VariantProvider != null) return VariantProvider(m) ?? m;
            if (_runtime.TryGetValue(m, out var v) && v) return v;
            v = CreateVariant(m); if (!v) return m; _runtime[m] = v; return v;
        }

        public const string PWGeneralShaderName = "Shader Graphs/PW_General_ShaderGraph_URP";
        public static bool IsSupported(Material m) => m && m.shader && (m.shader.name == SourceShaderName || m.shader.name == PWGeneralShaderName);

        /// <summary>New (unsaved) accumulation copy. URP/Lit: same properties. Gaia PW General: albedo/normal/metal-gloss/cutout remapped.</summary>
        public static Material CreateVariant(Material src)
        {
            var sh = Shader.Find(ShaderName); if (!sh || !IsSupported(src)) return null;
            if (src.shader.name == SourceShaderName) return new Material(src) { name = src.name + "_Accum", shader = sh };
            var m = new Material(sh) { name = src.name + "_Accum" };
            Texture T(string n) => src.HasProperty(n) ? src.GetTexture(n) : null;
            float F(string n, float d) => src.HasProperty(n) ? src.GetFloat(n) : d;
            var main = T("_MainTex"); m.SetTexture("_BaseMap", main);
            if (src.HasProperty("_MainTex")) { m.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex")); m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_MainTex")); }
            if (src.HasProperty("_Color")) m.SetColor("_BaseColor", src.GetColor("_Color"));
            var bump = T("_BumpMap"); if (bump) { m.SetTexture("_BumpMap", bump); m.SetFloat("_BumpScale", F("_BumpMapScale", 1f)); m.EnableKeyword("_NORMALMAP"); }
            var mg = T("_MetallicGlossMap"); if (mg) { m.SetTexture("_MetallicGlossMap", mg); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            m.SetFloat("_Metallic", F("_Metallic", 0f)); m.SetFloat("_Smoothness", F("_Glossiness", 0.3f));
            if (F("_AlphaTest", 0f) > 0.5f || src.IsKeywordEnabled("_ALPHATEST_ON")) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", F("_Cutoff", 0.5f)); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; }
            if (src.HasProperty("_CullMode")) m.SetFloat("_Cull", F("_CullMode", 2f));
            m.enableInstancing = src.enableInstancing;
            return m;
        }

        /// <summary>Returns renderers changed; skipped = renderers excluded (pickups/interactables) or unsupported.</summary>
        public int Apply() => Apply(out _);
        public int Apply(out int skipped)
        {
            skipped = 0; int changed = 0;
            if (IsExcluded(transform)) { skipped = 1; Debug.LogWarning($"[ASH] Accumulation refused on '{name}': pickup/interactable."); return 0; }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (IsExcluded(r.transform) || slots.Exists(s => s.renderer == r)) { skipped++; continue; }
                var src = r.sharedMaterials; var dst = new Material[src.Length]; bool any = false;
                for (int i = 0; i < src.Length; i++) { dst[i] = Variant(src[i]); any |= dst[i] != src[i]; }
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
