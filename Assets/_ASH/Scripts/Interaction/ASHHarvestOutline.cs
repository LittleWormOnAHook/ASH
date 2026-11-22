using System.Collections.Generic;
using ASH.Wildlife;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.Interaction
{
    public enum ASHOutlineMode { Auto, Outline, Glow, Off }

    /// <summary>t478/t484: draws a light-gold highlight around the harvestable resource the player is targeting (ASHInteractor.Target,
    /// already limited to the harvest distance). Hidden when nothing / a non-resource is targeted, or when depleted / felled.
    /// Solid submeshes use the inverted-hull outline; foliage submeshes use a clipped overlay (no filled quads).
    /// One extra draw per mesh part of ONE object; no material or object changes.</summary>
    public sealed class ASHHarvestOutline : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<ASHHarvestOutline>()) return;
            var go = new GameObject("ASH_HarvestOutline"); DontDestroyOnLoad(go); go.AddComponent<ASHHarvestOutline>();
        }

        static readonly int ID_Col = Shader.PropertyToID("_OutlineColor"), ID_W = Shader.PropertyToID("_OutlineWidth");
        static readonly int ID_Map = Shader.PropertyToID("_BaseMap"), ID_ST = Shader.PropertyToID("_BaseMap_ST");
        static readonly int ID_Cut = Shader.PropertyToID("_Cutoff"), ID_Tint = Shader.PropertyToID("_TintStrength");
        static readonly int ID_Rim = Shader.PropertyToID("_RimPower"), ID_Pulse = Shader.PropertyToID("_Pulse");
        static readonly string[] MapNames = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_Diffuse", "_Texture" };
        static readonly string[] CutNames = { "_Cutoff", "_AlphaCutoff", "_CutoffValue", "_AlphaClip", "_Cutout", "_AlphaCutoff" };
        static readonly string[] FolioHints = { "leaf", "leaves", "foliage", "grass", "vegetation", "plant", "fern", "bush", "twig", "cross", "billboard", "cutout", "alphatest", "alpha test" };
        static readonly string[] FolioKw = { "_ALPHATEST_ON", "ALPHATEST_ON", "_ALPHA_CLIP", "_ALPHACLIP_ON", "_ALPHATEST", "ALPHA_TEST", "_CUTOFF_ON" };

        struct Part { public MeshFilter mf; public int sub; public bool foliage; public Material src; }

        ASHInteractor _ia; Material _matOutline, _matGlow; Component _cur; float _findT;
        readonly List<Part> _parts = new List<Part>();
        MaterialPropertyBlock _block;

        public static bool Harvestable(Component t)
        {
            if (!t || !t.gameObject.activeInHierarchy) return false;
            if (t is ASHResourceNode n) return !n.Depleted && n.Item;
            if (t is ASHChoppable c) return !c.Done && !c.Busy;
            var animal = t as ASHAnimal ?? t.GetComponentInParent<ASHAnimal>();
            if (animal) return animal.IsDead && !animal.Harvested;
            return false;
        }

        void Awake() { _block = new MaterialPropertyBlock(); }

        void LateUpdate()
        {
            var S = ASHOutlineSettings.I; if (!S || !S.outlineEnabled) return;
            if (!_ia) { if (Time.unscaledTime < _findT) return; _findT = Time.unscaledTime + 1f; _ia = FindAnyObjectByType<ASHInteractor>(); if (!_ia) return; }
            var t = _ia.Target as Component;
            if (!Harvestable(t)) { _cur = null; return; }
            if (t != _cur) { _cur = t; Collect(t, S.maxParts); }
            EnsureMats(S);
            foreach (var p in _parts)
            {
                if (p.mf == null || !p.mf.gameObject.activeInHierarchy) continue;
                var r = p.mf.GetComponent<MeshRenderer>(); if (!r || !r.enabled) continue;
                var mesh = p.mf.sharedMesh; if (!mesh || p.sub >= mesh.subMeshCount) continue;
                var mode = Resolve(p.foliage, S);
                if (mode == ASHOutlineMode.Off) continue;
                var mtx = p.mf.transform.localToWorldMatrix;
                if (mode == ASHOutlineMode.Glow) DrawGlow(S, p, mesh, mtx);
                else DrawOutline(S, mesh, p.sub, mtx);
            }
        }

        static ASHOutlineMode Resolve(bool foliage, ASHOutlineSettings S)
        {
            var mode = foliage ? S.foliageMode : S.solidMode;
            if (mode == ASHOutlineMode.Auto) return foliage ? ASHOutlineMode.Glow : ASHOutlineMode.Outline;
            return mode;
        }

        void EnsureMats(ASHOutlineSettings S)
        {
            var shO = S.shader ? S.shader : Shader.Find("ASH/Harvest Outline");
            if (shO && (!_matOutline || _matOutline.shader != shO))
            {
                if (_matOutline) Destroy(_matOutline);
                _matOutline = new Material(shO) { hideFlags = HideFlags.HideAndDontSave };
            }
            var shG = S.foliageShader ? S.foliageShader : Shader.Find("ASH/Harvest Foliage Glow");
            if (shG && (!_matGlow || _matGlow.shader != shG))
            {
                if (_matGlow) Destroy(_matGlow);
                _matGlow = new Material(shG) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        void DrawOutline(ASHOutlineSettings S, Mesh mesh, int sub, Matrix4x4 mtx)
        {
            if (!_matOutline) return;
            _matOutline.SetColor(ID_Col, S.color); _matOutline.SetFloat(ID_W, S.widthPixels);
            var rp = new RenderParams(_matOutline) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMesh(rp, mesh, sub, mtx);
        }

        void DrawGlow(ASHOutlineSettings S, Part p, Mesh mesh, Matrix4x4 mtx)
        {
            if (!_matGlow) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            var tex = BaseMap(p.src);
            if (!tex) return; // no alpha source: skip rather than filling the card
            _matGlow.SetColor(ID_Col, S.color);
            _block.Clear();
            _block.SetTexture(ID_Map, tex);
            _block.SetVector(ID_ST, Tile(p.src, tex));
            _block.SetFloat(ID_Cut, Cutoff(p.src));
            _block.SetFloat(ID_Tint, S.foliageTintStrength);
            _block.SetFloat(ID_Rim, S.foliageRimPower);
            _block.SetFloat(ID_Pulse, S.foliagePulse);
            var rp = new RenderParams(_matGlow) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, matProps = _block };
            Graphics.RenderMesh(rp, mesh, p.sub, mtx);
        }

        void Collect(Component t, int max)
        {
            _parts.Clear();
            var lod = t.GetComponentInChildren<LODGroup>(); if (!lod) lod = t.GetComponentInParent<LODGroup>();
            if (lod)
            {
                var lods = lod.GetLODs();
                if (lods.Length > 0)
                {
                    foreach (var r in lods[0].renderers) AddRenderer(r, max);
                    if (_parts.Count > 0) return;
                }
            }
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>()) AddFilter(mf, max);
        }

        void AddRenderer(Renderer r, int max)
        {
            if (!r || _parts.Count >= max) return;
            if (r.TryGetComponent(out MeshFilter mf)) AddFilter(mf, max);
        }

        void AddFilter(MeshFilter mf, int max)
        {
            if (!mf || !mf.sharedMesh || _parts.Count >= max) return;
            var r = mf.GetComponent<MeshRenderer>();
            var mats = r ? r.sharedMaterials : null;
            int subs = mf.sharedMesh.subMeshCount;
            for (int s = 0; s < subs && _parts.Count < max; s++)
            {
                var mat = mats != null && s < mats.Length ? mats[s] : (mats != null && mats.Length > 0 ? mats[0] : null);
                _parts.Add(new Part { mf = mf, sub = s, foliage = IsFoliage(mat), src = mat });
            }
        }

        static bool IsFoliage(Material mat)
        {
            if (!mat) return false;
            int q = mat.renderQueue;
            if (q >= 2225 && q < 4000) return true;
            for (int i = 0; i < FolioKw.Length; i++) if (mat.IsKeywordEnabled(FolioKw[i])) return true;
            if (mat.HasProperty("_AlphaClip") && mat.GetFloat("_AlphaClip") > 0.5f) return true;
            string n = ((mat.shader ? mat.shader.name : "") + " " + mat.name).ToLowerInvariant();
            if (n.Contains("bark") && !n.Contains("leaf") && !n.Contains("cross") && !n.Contains("grass")) return false;
            for (int i = 0; i < FolioHints.Length; i++) if (n.Contains(FolioHints[i])) return true;
            return false;
        }

        static Texture BaseMap(Material m)
        {
            if (!m) return null;
            for (int i = 0; i < MapNames.Length; i++)
                if (m.HasProperty(MapNames[i])) { var t = m.GetTexture(MapNames[i]); if (t) return t; }
            return m.mainTexture;
        }

        static float Cutoff(Material m)
        {
            if (!m) return 0.5f;
            for (int i = 0; i < CutNames.Length; i++)
                if (m.HasProperty(CutNames[i]) && CutNames[i] != "_AlphaClip") return m.GetFloat(CutNames[i]);
            return 0.5f;
        }

        static Vector4 Tile(Material m, Texture tex)
        {
            if (m)
            {
                for (int i = 0; i < MapNames.Length; i++)
                    if (m.HasProperty(MapNames[i])) { var s = m.GetTextureScale(MapNames[i]); var o = m.GetTextureOffset(MapNames[i]); return new Vector4(s.x, s.y, o.x, o.y); }
            }
            return new Vector4(1f, 1f, 0f, 0f);
        }

        void OnDestroy() { if (_matOutline) Destroy(_matOutline); if (_matGlow) Destroy(_matGlow); }
    }
}
