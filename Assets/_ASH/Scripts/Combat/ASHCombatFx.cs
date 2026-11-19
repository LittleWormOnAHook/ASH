using System.Collections.Generic;
using UnityEngine;

namespace ASH.Combat
{
    public enum ASHHitKind { Melee, Bullet, Arrow }
    public enum ASHSurface { Default, Flesh, Wood, Stone }

    /// <summary>Local hit stop (port of Genesis DMMeleeHitStop): the hit target's Animator, and optionally the attacker's,
    /// dip to the hold speed for a few frames. Never touches Time.timeScale.</summary>
    public static class ASHHitStop
    {
        const float Frame = 1f / 60f;

        public static void OnMeleeHit(Animator attacker, Component target, ASHWeaponData w, bool crit)
        {
            var t = ASHCombatTuning.Live; if (!t.hitStop) return;
            bool heavy = w && w.bodyWeight >= t.heavyWeight;
            int frames = heavy ? t.hitStopHeavyFrames : t.hitStopLightFrames;
            if (crit) frames += t.hitStopCritBonusFrames;
            if (w && w.bodyWeight > 1f) frames += Mathf.RoundToInt(t.hitStopWeightBonusFrames * Mathf.Clamp01((w.bodyWeight - 1f) / Mathf.Max(0.01f, t.heavyWeight - 1f)));
            if (frames <= 0) return;
            float s = frames * Frame;
            var ta = target ? target.GetComponentInParent<Animator>() : null;
            if (ta && ta != attacker) Hold.Begin(ta, s, t.hitStopHoldSpeed);
            if (attacker && t.hitStopFreezesAttacker) Hold.Begin(attacker, s, t.hitStopHoldSpeed);
        }

        [DisallowMultipleComponent]
        public sealed class Hold : MonoBehaviour
        {
            Animator _a; float _until, _speed, _saved = 1f; bool _on;
            public static void Begin(Animator a, float seconds, float speed)
            {
                var h = a.GetComponent<Hold>(); if (!h) h = a.gameObject.AddComponent<Hold>();
                h._a = a; h._speed = speed;
                if (!h._on) { h._saved = a.speed > 0.01f ? a.speed : 1f; h._on = true; }
                h._until = Mathf.Max(h._until, Time.unscaledTime + Mathf.Min(seconds, 0.5f));
                a.speed = speed;
            }
            void LateUpdate()
            {
                if (!_on || !_a) return;
                float seen = _a.speed; if (Mathf.Abs(seen - _speed) > 0.001f && seen > 0.01f) _saved = seen;
                if (Time.unscaledTime >= _until) { _a.speed = _saved; _on = false; return; }
                _a.speed = _speed;
            }
            void OnDisable() { if (_on && _a) _a.speed = _saved; _on = false; }
        }
    }

    /// <summary>Light hit-mark layer (after Genesis DMEnemyHitMarks, URP-safe): a surface burst at the impact and a small mark
    /// parented to what was hit, up to N per target (oldest goes first), fading after a while.</summary>
    public static class ASHHitMarks
    {
        static readonly Dictionary<int, Queue<GameObject>> _perTarget = new Dictionary<int, Queue<GameObject>>();
        static Material _fallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _perTarget.Clear(); }

        public static ASHSurface SurfaceOf(Collider c)
        {
            if (!c) return ASHSurface.Default;
            if (c.GetComponentInParent<ASHTrainingDummy>()) return ASHSurface.Wood;
            if (c.CompareTag("Enemy") || c.CompareTag("Animal") || c.CompareTag("Player")) return ASHSurface.Flesh;
            var a = c.GetComponentInParent<Animator>(); if (a && a.isHuman) return ASHSurface.Flesh;
            string n = c.name.ToLowerInvariant();
            if (n.Contains("tree") || n.Contains("log") || n.Contains("wood") || n.Contains("stump") || n.Contains("plank") || n.Contains("cabin")) return ASHSurface.Wood;
            if (n.Contains("rock") || n.Contains("stone") || n.Contains("cliff") || n.Contains("boulder")) return ASHSurface.Stone;
            return ASHSurface.Default;
        }

        public static void Spawn(Vector3 point, Vector3 normal, Collider target, ASHHitKind kind)
        {
            var t = ASHCombatTuning.Live; var surf = SurfaceOf(target);
            if (normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
            var fx = surf == ASHSurface.Flesh ? t.fleshHit : surf == ASHSurface.Wood ? t.woodHit : surf == ASHSurface.Stone ? t.stoneHit : t.defaultHit;
            if (!fx) fx = t.defaultHit;
            if (fx) { var g = Object.Instantiate(fx, point + normal * 0.01f, Quaternion.LookRotation(normal)); Object.Destroy(g, 4f); }
            if (kind == ASHHitKind.Arrow || t.maxMarksPerTarget <= 0 || !target || target is TerrainCollider) return; // arrows leave themselves as the mark
            var mark = GameObject.CreatePrimitive(PrimitiveType.Quad); mark.name = "ASH_HitMark";
            Object.Destroy(mark.GetComponent<Collider>());
            float size = t.markSize * (kind == ASHHitKind.Melee ? 1.6f : 1f) * Random.Range(0.8f, 1.2f);
            mark.transform.SetPositionAndRotation(point + normal * 0.004f, Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            mark.transform.localScale = new Vector3(kind == ASHHitKind.Melee ? size * 2.2f : size, kind == ASHHitKind.Melee ? size * 0.45f : size, 1f);
            var r = mark.GetComponent<MeshRenderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.sharedMaterial = t.markMaterial ? t.markMaterial : Fallback();
            var mpb = new MaterialPropertyBlock(); var col = surf == ASHSurface.Flesh ? t.fleshMarkColour : surf == ASHSurface.Wood ? t.woodMarkColour : new Color(0.1f, 0.1f, 0.1f, 0.85f);
            mpb.SetColor("_BaseColor", col); mpb.SetColor("_Color", col); r.SetPropertyBlock(mpb);
            mark.transform.SetParent(target.transform, true);
            Object.Destroy(mark, t.markLifetime);
            int id = target.transform.GetInstanceID();
            if (!_perTarget.TryGetValue(id, out var q)) { q = new Queue<GameObject>(); _perTarget[id] = q; }
            q.Enqueue(mark);
            while (q.Count > t.maxMarksPerTarget) { var old = q.Dequeue(); if (old) Object.Destroy(old); }
        }

        /// <summary>Removes every mark on a target (dummy reset).</summary>
        public static void Clear(Transform target)
        {
            if (!target) return;
            if (_perTarget.TryGetValue(target.GetInstanceID(), out var q)) { foreach (var g in q) if (g) Object.Destroy(g); q.Clear(); }
        }

        static Material Fallback()
        {
            if (_fallback) return _fallback;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _fallback = new Material(sh) { name = "ASH_HitMark (runtime)" };
            if (_fallback.HasProperty("_Surface")) { _fallback.SetFloat("_Surface", 1f); _fallback.SetOverrideTag("RenderType", "Transparent"); _fallback.renderQueue = 3000;
                _fallback.SetFloat("_Blend", 0f); _fallback.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); _fallback.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); _fallback.SetInt("_ZWrite", 0);
                _fallback.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
            return _fallback;
        }
    }

    /// <summary>Floating damage number (world-space TextMesh, rises and fades).</summary>
    public sealed class ASHDamageNumber : MonoBehaviour
    {
        TextMesh _t; float _age; Color _c; Vector3 _vel;
        static Font _font;
        public static void Show(Vector3 at, float damage, bool crit)
        {
            if (!ASHCombatTuning.Live.damageNumbers || damage <= 0f) return;
            var g = new GameObject("ASH_DamageNumber"); g.transform.position = at + Random.insideUnitSphere * 0.08f;
            var n = g.AddComponent<ASHDamageNumber>(); n._t = g.AddComponent<TextMesh>();
            if (!_font) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            n._t.font = _font; g.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
            n._t.text = Mathf.RoundToInt(damage).ToString() + (crit ? "!" : ""); n._t.anchor = TextAnchor.MiddleCenter; n._t.alignment = TextAlignment.Center;
            n._t.characterSize = crit ? 0.05f : 0.035f; n._t.fontSize = 64; n._c = crit ? new Color(1f, 0.82f, 0.2f) : Color.white; n._t.color = n._c;
            n._vel = new Vector3(Random.Range(-0.15f, 0.15f), 0.9f, 0f);
        }
        void LateUpdate()
        {
            _age += Time.deltaTime; transform.position += _vel * Time.deltaTime; _vel.y = Mathf.Max(0.15f, _vel.y - Time.deltaTime * 1.2f);
            var cam = Camera.main; if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            var c = _c; c.a = Mathf.Clamp01(1.4f - _age); _t.color = c;
            if (_age > 1.4f) Destroy(gameObject);
        }
    }
}
