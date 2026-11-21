using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.Interaction
{
    /// <summary>t478: harvest outline look. Resources/ASH_OutlineSettings (edits in Play are kept). The range is NOT set here:
    /// the outline follows the interactor's own harvest/use distance (only the current target inside that range lights up).</summary>
    [CreateAssetMenu(menuName = "ASH/Harvest Outline Settings", fileName = "ASH_OutlineSettings")]
    public sealed class ASHOutlineSettings : ScriptableObject
    {
        static ASHOutlineSettings _i;
        public static ASHOutlineSettings I => _i ? _i : (_i = Resources.Load<ASHOutlineSettings>("ASH_OutlineSettings"));
        [Tooltip("Outline on harvestable resources (rocks, trees, logs, bushes, plants) while they are targeted within harvest distance.")]
        public bool outlineEnabled = true;
        public Color color = new Color(1f, 0.85f, 0.45f, 1f);
        [Tooltip("Outline width in screen pixels.")] [Range(0.25f, 16f)] public float widthPixels = 2.5f;
        [Tooltip("Outline shader (kept referenced so it is in builds).")] public Shader shader;
        [Tooltip("Max mesh parts outlined per resource (big prefabs).")] [Range(1, 64)] public int maxParts = 16;
    }

    /// <summary>t478: draws a light-gold hull outline around the harvestable resource the player is targeting (ASHInteractor.Target,
    /// which is already limited to the harvest distance). Hidden when nothing / a non-resource is targeted, or when depleted / felled.
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
        ASHInteractor _ia; Material _mat; Component _cur; float _findT;
        readonly List<MeshFilter> _parts = new List<MeshFilter>();

        public static bool Harvestable(Component t)
        {
            if (!t || !t.gameObject.activeInHierarchy) return false;
            if (t is ASHResourceNode n) return !n.Depleted && n.Item;
            if (t is ASHChoppable c) return !c.Done && !c.Busy;
            return false;
        }

        void LateUpdate()
        {
            var S = ASHOutlineSettings.I; if (!S || !S.outlineEnabled) return;
            if (!_ia) { if (Time.unscaledTime < _findT) return; _findT = Time.unscaledTime + 1f; _ia = FindAnyObjectByType<ASHInteractor>(); if (!_ia) return; }
            var t = _ia.Target as Component;
            if (!Harvestable(t)) { _cur = null; return; }
            if (t != _cur) { _cur = t; Collect(t, S.maxParts); }
            if (!_mat)
            {
                var sh = S.shader ? S.shader : Shader.Find("ASH/Harvest Outline"); if (!sh) return;
                _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            _mat.SetColor(ID_Col, S.color); _mat.SetFloat(ID_W, S.widthPixels);
            var rp = new RenderParams(_mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            foreach (var mf in _parts)
            {
                if (!mf || !mf.gameObject.activeInHierarchy) continue;
                var r = mf.GetComponent<MeshRenderer>(); if (!r || !r.enabled) continue;
                var m = mf.sharedMesh; if (!m) continue;
                var mtx = mf.transform.localToWorldMatrix;
                for (int s = 0; s < m.subMeshCount; s++) Graphics.RenderMesh(rp, m, s, mtx);
            }
        }

        void Collect(Component t, int max)
        {
            _parts.Clear();
            var lod = t.GetComponentInChildren<LODGroup>(); if (!lod) lod = t.GetComponentInParent<LODGroup>();
            if (lod)
            {
                var lods = lod.GetLODs();
                if (lods.Length > 0) foreach (var r in lods[0].renderers) if (r && r.TryGetComponent(out MeshFilter mf) && _parts.Count < max) _parts.Add(mf);
                if (_parts.Count > 0) return;
            }
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>()) { if (_parts.Count >= max) break; _parts.Add(mf); }
        }

        void OnDestroy() { if (_mat) Destroy(_mat); }
    }
}