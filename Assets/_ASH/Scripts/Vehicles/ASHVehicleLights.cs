using System.Collections.Generic;
using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>Headlights, tail and brake lights (t358-t361). All lights are switched off at load and only run while occupied. Uses the vehicle's own Light objects (the pickup's hand-placed
    /// headlights are kept exactly; only on/off is driven) plus generated ones where a model had none. Emissive headlight material
    /// slots glow with the headlights. Lights need battery power (ASHVehicleCondition) and drain it while on.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHVehicleLights : MonoBehaviour
    {
        [System.Serializable] public struct Slot { public Renderer renderer; public int material; }
        [Header("Lights (found / made by Setup Vehicles)")]
        public List<Light> headLights = new List<Light>();
        public List<Light> brakeLights = new List<Light>();
        public List<Slot> headlightEmissive = new List<Slot>();
        [Header("Headlights")]
        [Tooltip("Keep each headlight's own intensity / range / angle (hand-placed lights). Off = use the values below.")]
        public bool keepAuthoredSettings = true;
        [Range(0f, 50f)] public float intensity = 3f;
        [Range(1f, 200f)] public float range = 35f;
        [Range(1f, 179f)] public float spotAngle = 60f;
        [ColorUsage(false, true)] public Color emissionColor = new Color(2.2f, 2.0f, 1.7f);
        [Tooltip("Headlights come on when the player gets in (always off when nobody is in it).")] public bool lightsOnEnter = true;
        [Tooltip("With lightsOnEnter off: turn them on when getting in at night (ASHDayNightClock).")] public bool autoOnAtNight = true;
        [Header("Tail / brake")]
        [Tooltip("Tail glow (fraction of brakeIntensity) while the headlights are on.")] [Range(0f, 1f)] public float tailFraction = 0.25f;
        [Range(0f, 30f)] public float brakeIntensity = 2.5f;
        [Range(0.5f, 30f)] public float brakeRange = 4f;
        [Tooltip("Brake / tail light colour (t390: red).")] public Color brakeColor = new Color(1f, 0.04f, 0.02f);
        [Tooltip("Tail / brake lamp material slots that glow (auto-found by material or object name when empty).")] public List<Slot> brakeEmissive = new List<Slot>();
        [Tooltip("Comma list of name parts used to auto-find the brake lamp materials / meshes.")] public string brakeMaterialNames = "brake,tail,stop,rear light,rearlight,back light,backlight";
        [Tooltip("Base colour tint of the rear lamp lenses (so they read red even when off).")] public Color brakeLampTint = new Color(1f, 0.22f, 0.18f);
        [Tooltip("Brake lamp glow while braking (HDR multiplier on brakeColor).")] [Range(0f, 50f)] public float brakeEmission = 6f;
        [Tooltip("Tail lamp glow while the headlights are on (fraction of brakeEmission).")] [Range(0f, 1f)] public float tailEmissionFraction = 0.2f;
        public bool On { get; private set; }
        public bool Braking { get; private set; }
        readonly List<float> _authI = new List<float>();
        readonly List<(Material m, Color c, bool kw)> _mats = new List<(Material, Color, bool)>();
        bool _init;

        void Init()
        {
            if (_init) return; _init = true;
            foreach (var l in headLights) _authI.Add(l ? l.intensity : intensity);
            foreach (var s in headlightEmissive)
            {
                if (!s.renderer) continue; var ms = s.renderer.materials; // instance copies (runtime only)
                if (s.material < 0 || s.material >= ms.Length) continue;
                var m = ms[s.material]; _mats.Add((m, m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black, m.IsKeywordEnabled("_EMISSION")));
            }
            if (brakeLights.Count == 0)   // t390: use the lights placed at the back of the vehicle (anything that isn't a headlight, behind the centre)
                foreach (var l in GetComponentsInChildren<Light>(true))
                    if (l && !headLights.Contains(l) && transform.InverseTransformPoint(l.transform.position).z < 0f) { brakeLights.Add(l); if (!l.gameObject.activeSelf) l.gameObject.SetActive(true); }
            // t391: no name-based auto-find any more (it could catch a shared body material); Setup Driver makes ASH_BrakeLamps with its own material
            foreach (var s in brakeEmissive)
            {
                if (!s.renderer) continue; var ms = s.renderer.materials;
                if (s.material < 0 || s.material >= ms.Length) continue;
                var m = ms[s.material]; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * brakeLampTint); else if (m.HasProperty("_Color")) m.SetColor("_Color", m.GetColor("_Color") * brakeLampTint); _bmats.Add((m, m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black, m.IsKeywordEnabled("_EMISSION")));
            }
            Apply();
        }
        readonly List<(Material m, Color c, bool kw)> _bmats = new List<(Material, Color, bool)>();
        public int BrakeSlotCount => _bmats.Count;
        void FindBrakeSlots()
        {
            var keys = new List<string>(); foreach (var k in (brakeMaterialNames ?? "").Split(',')) { var s = k.Trim().ToLowerInvariant(); if (s.Length > 0) keys.Add(s); }
            bool Hit(string n) { n = (n ?? "").ToLowerInvariant(); foreach (var k in keys) if (n.Contains(k)) return true; return false; }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var ms = r.sharedMaterials; bool objHit = Hit(r.name);
                for (int i = 0; i < ms.Length; i++) if (ms[i] && (Hit(ms[i].name) || (objHit && ms.Length == 1))) brakeEmissive.Add(new Slot { renderer = r, material = i });
            }
        }
        void Awake() => Init();

        public void Set(bool on) { Init(); On = on; Apply(); }
        public void Toggle() => Set(!On);
        public void SetBraking(bool b) { Init(); if (b == Braking) return; Braking = b; Apply(); }

        public void Apply()
        {
            for (int i = 0; i < headLights.Count; i++)
            {
                var l = headLights[i]; if (!l) continue;
                l.enabled = On;
                if (!keepAuthoredSettings) { l.intensity = intensity; l.range = range; if (l.type == LightType.Spot) l.spotAngle = spotAngle; }
                else if (i < _authI.Count) l.intensity = _authI[i];
            }
            foreach (var (m, c, kw) in _mats)
            {
                if (!m) continue;
                if (On) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emissionColor); }
                else { m.SetColor("_EmissionColor", c); if (!kw) m.DisableKeyword("_EMISSION"); }
            }
            float b = Braking ? brakeIntensity : (On ? brakeIntensity * tailFraction : 0f);
            foreach (var l in brakeLights) { if (!l) continue; l.enabled = b > 0.001f; l.intensity = b; l.range = brakeRange; l.color = brakeColor; }
            float e = Braking ? brakeEmission : (On ? brakeEmission * tailEmissionFraction : 0f);
            foreach (var (m, c, kw) in _bmats)
            {
                if (!m) continue;
                if (e > 0.001f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", brakeColor * e); }
                else { m.SetColor("_EmissionColor", c); if (!kw) m.DisableKeyword("_EMISSION"); }
            }
        }
    }
}
