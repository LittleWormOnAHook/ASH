using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Interaction;
using ASH.Survival;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Press E to turn a fuel-free world light on or off (lanterns, lamps, table flashlights).
    /// Toggles lights, emission, flicker, a click, and warmth (only while on). Drop it on a prop, or let
    /// <see cref="EnsureAll"/> attach it at runtime (no scene save). State is saved in the "worldLights" section.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public sealed class ASHLightToggle : MonoBehaviour, IASHUsable
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _all.Clear();
            _stoveApplied = false;
            _ensured = false;
        }

        [Tooltip("Stable id in the save. Empty = scene path.")]
        [SerializeField] string saveId;
        [Tooltip("Override the prompt name (Lantern, Lamp). Empty = a cleaned object name.")]
        [SerializeField] string displayName;
        [Tooltip("Start on for this one light. The settings default is off.")]
        [SerializeField] bool startOn;
        [SerializeField] Light[] lights;
        [SerializeField] ParticleSystem[] effects;
        [SerializeField] GameObject[] onObjects;
        [SerializeField] GameObject[] offObjects;
        [SerializeField] Renderer[] emissiveRenderers;

        public bool On => _on;
        public string SaveId => string.IsNullOrEmpty(saveId) ? ASHWorldSave.IdOf(transform) : saveId;
        public Transform UseTransform => transform;
        public static IReadOnlyList<ASHLightToggle> All => _all;

        static readonly List<ASHLightToggle> _all = new List<ASHLightToggle>();
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static bool _stoveApplied, _ensured;

        bool _on, _bound;
        float _seed;
        float[] _baseIntensity;
        Color[] _emission;
        bool _emissionCached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad() => EnsureAll();

        /// <summary>Idempotent: attach to the cabin hanging lantern (and any name-pattern matches), force the cabin stove off on a new game, host the save.</summary>
        public static void EnsureAll()
        {
            if (!Application.isPlaying) return;
            var s = ASHLightToggleSettings.I;
            ASHLightToggleHost.Ensure();
            int attached = 0;
            if (s.autoAttachExactNames != null)
            {
                foreach (var n in s.autoAttachExactNames)
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    var go = GameObject.Find(n);
                    if (go && AttachIfEligible(go.transform)) attached++;
                }
            }
            var found = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            foreach (var l in found)
            {
                if (!l) continue;
                var root = GuessRoot(l.transform);
                if (root && NameMatches(root, s.autoAttachNamePatterns) && AttachIfEligible(root)) attached++;
            }
            if (!_ensured)
            {
                _ensured = true;
                ASHLog.Info($"Light toggle: ready ({attached} attached, { _all.Count } in scene). Cabin stove starts off on a new game. E toggles lanterns.");
            }
        }

        public static bool AttachIfEligible(Transform root)
        {
            if (!root) return false;
            if (IsHeldOrPlayer(root) || root.GetComponentInParent<ASHCampfire>() || root.GetComponentInParent<ASHCookStove>()) return false;
            if (root.GetComponentInParent<ASHLightSource>()) return false;
            var existing = root.GetComponent<ASHLightToggle>();
            if (!existing) existing = root.GetComponentInParent<ASHLightToggle>();
            if (existing) { existing.Bind(); return false; }
            var t = root.gameObject.AddComponent<ASHLightToggle>();
            t.Bind();
            t.EnsureInteractCollider();
            t.EnsureWarmth();
            t.Apply(t.startOn || ASHLightToggleSettings.I.defaultStartOn, playSfx: false);
            return true;
        }

        void Awake()
        {
            _seed = UnityEngine.Random.value * 100f;
            if (!startOn && !ASHLightToggleSettings.I.defaultStartOn) _on = false;
            else _on = startOn || ASHLightToggleSettings.I.defaultStartOn;
            Bind();
            EnsureInteractCollider();
            EnsureWarmth();
            Apply(_on, playSfx: false);
        }

        void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        void OnDisable() { _all.Remove(this); }

        void Update()
        {
            if (!_on || _baseIntensity == null) return;
            var s = ASHLightToggleSettings.I;
            if (s.flicker <= 0f || lights == null) return;
            float n = Mathf.PerlinNoise(_seed, Time.time * s.flickerSpeed);
            float k = 1f - s.flicker * 0.5f + s.flicker * n;
            for (int i = 0; i < lights.Length && i < _baseIntensity.Length; i++)
                if (lights[i]) lights[i].intensity = _baseIntensity[i] * k;
        }

        public float GetUsePriority(in ASHUseContext c)
        {
            var s = ASHLightToggleSettings.I;
            if (c.PlayerTransform && Vector3.Distance(UsePoint(), c.PlayerPosition + Vector3.up) > s.useRange + 0.25f) return -1f;
            return s.usePriority;
        }

        public float GetHoldDuration(in ASHUseContext c) => 0f;

        public string GetPrompt(in ASHUseContext c)
        {
            var s = ASHLightToggleSettings.I;
            string n = DisplayName();
            string fmt = _on ? s.promptOff : s.promptOn;
            if (string.IsNullOrEmpty(fmt)) fmt = _on ? "Turn off {0}" : "Turn on {0}";
            return fmt.Contains("{0}") ? string.Format(fmt, n) : fmt + " " + n;
        }

        public bool TryUse(in ASHUseContext c)
        {
            Apply(!_on, playSfx: true);
            return true;
        }

        /// <summary>Set on/off. Does not change light colour, range, or placement. Intensity is only flickered from the authored value.</summary>
        public void Apply(bool on, bool playSfx)
        {
            Bind();
            bool changed = on != _on;
            _on = on;
            if (lights != null)
                for (int i = 0; i < lights.Length; i++)
                {
                    if (!lights[i]) continue;
                    lights[i].enabled = _on;
                    if (_baseIntensity != null && i < _baseIntensity.Length)
                        lights[i].intensity = _on ? _baseIntensity[i] : _baseIntensity[i];
                }
            if (effects != null)
                foreach (var e in effects)
                    if (e) { if (_on) e.Play(true); else e.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            if (onObjects != null) foreach (var g in onObjects) if (g) g.SetActive(_on);
            if (offObjects != null) foreach (var g in offObjects) if (g) g.SetActive(!_on);
            ApplyEmission();
            if (changed && playSfx) PlayClick();
        }

        public void Restore(bool on) => Apply(on, playSfx: false);

        void Bind()
        {
            if (_bound) return;
            if (lights == null || lights.Length == 0) lights = CollectLights();
            if (effects == null || effects.Length == 0) effects = CollectEffects();
            CacheIntensities();
            CacheEmission();
            _bound = true;
        }

        Light[] CollectLights()
        {
            var list = new List<Light>();
            foreach (var l in GetComponentsInChildren<Light>(true))
            {
                if (!l) continue;
                if (l.GetComponentInParent<ASHCampfire>() && l.GetComponentInParent<ASHCampfire>().transform != transform) continue;
                if (l.GetComponentInParent<ASHLightSource>()) continue;
                list.Add(l);
            }
            return list.ToArray();
        }

        ParticleSystem[] CollectEffects()
        {
            var list = new List<ParticleSystem>();
            foreach (var e in GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!e) continue;
                if (e.GetComponentInParent<ASHCampfire>() && e.GetComponentInParent<ASHCampfire>().transform != transform) continue;
                if (e.GetComponentInParent<ASHLightSource>()) continue;
                list.Add(e);
            }
            return list.ToArray();
        }

        void CacheIntensities()
        {
            if (lights == null) { _baseIntensity = Array.Empty<float>(); return; }
            _baseIntensity = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) _baseIntensity[i] = lights[i] ? lights[i].intensity : 0f;
        }

        void CacheEmission()
        {
            if (_emissionCached) return;
            var rends = emissiveRenderers != null && emissiveRenderers.Length > 0
                ? emissiveRenderers
                : GetComponentsInChildren<Renderer>(true);
            var colors = new List<Color>();
            var kept = new List<Renderer>();
            foreach (var r in rends)
            {
                if (!r || r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                bool any = false;
                Color first = Color.black;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m || !m.HasProperty(EmissionId)) continue;
                    var c = m.GetColor(EmissionId);
                    if (c.maxColorComponent < 0.01f && c.a < 0.01f) continue;
                    if (!any) first = c;
                    any = true;
                }
                if (!any) continue;
                kept.Add(r);
                colors.Add(first);
            }
            emissiveRenderers = kept.ToArray();
            _emission = colors.ToArray();
            _emissionCached = true;
        }

        void ApplyEmission()
        {
            if (emissiveRenderers == null || _emission == null) return;
            var s = ASHLightToggleSettings.I;
            float k = _on ? s.emissionWhenOn : 0f;
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < emissiveRenderers.Length && i < _emission.Length; i++)
            {
                var r = emissiveRenderers[i]; if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionId, _emission[i] * k);
                r.SetPropertyBlock(block);
            }
        }

        void PlayClick()
        {
            var s = ASHLightToggleSettings.I;
            var clips = _on ? s.onClips : s.offClips;
            if (clips == null || clips.Length == 0)
            {
                var b = ASHSoundBank.Instance;
                if (b)
                {
                    string n = (name + " " + transform.root.name).ToLowerInvariant();
                    bool flash = n.Contains("flash");
                    clips = flash
                        ? (_on ? b.flashlightOn : b.flashlightOff)
                        : (_on ? b.lanternOn : b.lanternOff);
                }
            }
            float vol = s.sfxVolume;
            var bank = ASHSoundBank.Instance;
            if (bank) vol *= bank.lightVolume * bank.volume;
            ASHSfx.Play(_on ? "lanternOn" : "lanternOff", clips, transform.position, vol, 1f, 0.04f, 0.7f, 30f);
        }

        void EnsureInteractCollider()
        {
            if (GetComponentInChildren<Collider>(true)) return;
            var rend = GetComponentInChildren<Renderer>();
            var go = rend ? rend.gameObject : gameObject;
            var box = go.GetComponent<BoxCollider>();
            if (!box) box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (!rend) box.size = ASHLightToggleSettings.I.fallbackColliderSize;
        }

        void EnsureWarmth()
        {
            var s = ASHLightToggleSettings.I;
            string n = (name + " " + (transform.parent ? transform.parent.name : "")).ToLowerInvariant();
            bool flash = n.Contains("flash");
            var heat = GetComponent<ASHHeatSource>() ?? GetComponentInChildren<ASHHeatSource>() ?? GetComponentInParent<ASHHeatSource>();
            if (heat)
            {
                // Warmth only while the light is on. Do not change his radius / rate.
                heat.requireLight = true;
                return;
            }
            if (!s.addWarmth || (flash && !s.warmthOnFlashlights)) return;
            heat = gameObject.AddComponent<ASHHeatSource>();
            heat.ApplyPreset(ASHHeatSource.Preset.Lantern);
            heat.requireLight = true;
        }

        string DisplayName()
        {
            if (!string.IsNullOrEmpty(displayName)) return displayName;
            string n = name.Replace("(swings)", "").Replace("ASH_", "").Replace("_", " ").Trim();
            if (n.EndsWith(" (swings)", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 9).Trim();
            if (string.Equals(n, "Lantern ON", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "Lantern_ON", StringComparison.OrdinalIgnoreCase)) n = "Lantern";
            if (n.StartsWith("Hanging Lantern", StringComparison.OrdinalIgnoreCase) || n.IndexOf("HangingLantern", StringComparison.OrdinalIgnoreCase) >= 0) n = "Lantern";
            return string.IsNullOrEmpty(n) ? "Light" : n;
        }

        Vector3 UsePoint()
        {
            var col = GetComponentInChildren<Collider>();
            return col ? col.bounds.center : transform.position;
        }

        static Transform GuessRoot(Transform t)
        {
            for (var x = t; x; x = x.parent)
            {
                if (NameMatches(x, ASHLightToggleSettings.I.autoAttachNamePatterns)) return x;
                if (ExactName(x, ASHLightToggleSettings.I.autoAttachExactNames)) return x;
            }
            return t;
        }

        static bool ExactName(Transform t, string[] names)
        {
            if (!t || names == null) return false;
            foreach (var n in names) if (!string.IsNullOrEmpty(n) && t.name == n) return true;
            return false;
        }

        internal static bool NameMatches(Transform t, string[] patterns)
        {
            if (!t || patterns == null) return false;
            return PathMatches(t, patterns);
        }

        internal static bool PathMatches(Transform t, string[] patterns)
        {
            if (!t || patterns == null) return false;
            var sb = new System.Text.StringBuilder();
            for (var x = t; x; x = x.parent) sb.Append('/').Append(x.name);
            string path = sb.ToString();
            return Matches(path, patterns) || Matches(t.name, patterns);
        }

        internal static bool Matches(string text, string[] patterns)
        {
            if (string.IsNullOrEmpty(text) || patterns == null) return false;
            string n = Normalize(text);
            foreach (var p in patterns)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (n.Contains(Normalize(p))) return true;
            }
            return false;
        }

        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s) if (!char.IsWhiteSpace(c) && c != '_' && c != '-') sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        static bool IsHeldOrPlayer(Transform t)
        {
            for (var x = t; x; x = x.parent)
            {
                if (x.name.StartsWith("Held_", StringComparison.Ordinal) || x.name.StartsWith("ASH_Player", StringComparison.Ordinal)) return true;
                if (x.GetComponent<ASHLightSource>()) return true;
            }
            return false;
        }

        /// <summary>New game only: cabin furnace out and empty, leftover flame/emission off. Loaded saves keep their own state.</summary>
        public static void ApplyCabinStoveStart()
        {
            if (_stoveApplied) return;
            if (ASHSaveSystem.PendingLoad != null || ASHSaveSystem.IsRestoring) { _stoveApplied = true; return; }
            _stoveApplied = true;
            var s = ASHLightToggleSettings.I;
            if (!s.cabinStoveStartsEmpty) return;
            Vector3? cabin = CabinAnchor();
            int n = 0;
            foreach (var f in ASHCampfire.All)
            {
                if (!f || f.GetComponent<ASHPlacedObject>()) continue;
                if (!IsCabinStove(f, cabin, s)) continue;
                f.RestoreState(false, 0f);
                SyncStoveVisuals(f, false);
                f.LitChanged -= OnCabinFireLit;
                f.LitChanged += OnCabinFireLit;
                n++;
            }
            if (n > 0) ASHLog.Info($"Light toggle: cabin stove starts off ({n}). Add wood or kindling to light it.");
        }

        static void OnCabinFireLit(ASHCampfire f, bool on) { if (f) SyncStoveVisuals(f, on); }

        static void SyncStoveVisuals(ASHCampfire f, bool on)
        {
            if (!f) return;
            foreach (var l in f.GetComponentsInChildren<Light>(true))
                if (l) l.enabled = on;
            foreach (var e in f.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!e) continue;
                if (on) e.Play(true);
                else e.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            // Emission on coals / firebox: scale to 0 when out, without changing the authored material colour.
            var block = new MaterialPropertyBlock();
            foreach (var r in f.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                bool any = false;
                Color c = Color.black;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m || !m.HasProperty(EmissionId)) continue;
                    var col = m.GetColor(EmissionId);
                    if (col.maxColorComponent < 0.01f) continue;
                    c = col; any = true; break;
                }
                if (!any) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionId, on ? c : Color.black);
                r.SetPropertyBlock(block);
            }
        }

        static bool IsCabinStove(ASHCampfire f, Vector3? cabin, ASHLightToggleSettings s)
        {
            if (!Matches(f.name, s.cabinStoveNamePatterns) && !PathMatches(f.transform, s.cabinStoveNamePatterns)) return false;
            if (PathMatches(f.transform, s.cabinNamePatterns)) return true;
            if (cabin.HasValue && Vector3.Distance(f.transform.position, cabin.Value) <= s.cabinStoveRadius) return true;
            return false;
        }

        static Vector3? CabinAnchor()
        {
            var s = ASHLightToggleSettings.I;
            if (s.autoAttachExactNames != null)
                foreach (var n in s.autoAttachExactNames)
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    var go = GameObject.Find(n);
                    if (go) return go.transform.position;
                }
            foreach (var t in _all) if (t) return t.transform.position;
            return null;
        }
    }

    /// <summary>Runtime host: save section plus the late cabin-stove start (after ASHCampfire.Start). Not saved into the scene.</summary>
    [DefaultExecutionOrder(250)]
    public sealed class ASHLightToggleHost : MonoBehaviour, IASHSaveable
    {
        const string HostName = "ASH_LightToggleHost";

        [Serializable] sealed class Entry { public string id; public bool on; }
        [Serializable] sealed class State { public List<Entry> lights = new List<Entry>(); }

        public string SaveKey => "worldLights";
        public int RestoreOrder => 21;

        public static ASHLightToggleHost Ensure()
        {
            var h = FindAnyObjectByType<ASHLightToggleHost>();
            if (h) return h;
            var go = new GameObject(HostName);
            return go.AddComponent<ASHLightToggleHost>();
        }

        void OnEnable() => ASHSaveSystem.Register(this);
        void OnDisable() => ASHSaveSystem.Unregister(this);
        void Start() => ASHLightToggle.ApplyCabinStoveStart();

        public string CaptureJson()
        {
            var s = new State();
            foreach (var t in ASHLightToggle.All)
                if (t) s.lights.Add(new Entry { id = t.SaveId, on = t.On });
            return JsonUtility.ToJson(s);
        }

        public void RestoreJson(string json)
        {
            var s = JsonUtility.FromJson<State>(json);
            if (s == null || s.lights == null) return;
            var map = new Dictionary<string, ASHLightToggle>();
            foreach (var t in ASHLightToggle.All) if (t && !map.ContainsKey(t.SaveId)) map[t.SaveId] = t;
            foreach (var e in s.lights)
                if (e != null && !string.IsNullOrEmpty(e.id) && map.TryGetValue(e.id, out var t) && t) t.Restore(e.on);
        }
    }
}
