using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>On/off presentation of a held light (lantern, torch, flashlight): light, flicker, on/off visuals, particles.
    /// Fuel and burn time are tracked by ASHEquipment (survives unequip).</summary>
    [DisallowMultipleComponent]
    public sealed class ASHLightSource : MonoBehaviour
    {
        [SerializeField] private Light lightSource;
        [SerializeField] private GameObject[] onObjects;
        [SerializeField] private GameObject[] offObjects;
        [SerializeField] private ParticleSystem[] effects;
        [Tooltip("Kept at this world rotation (torch flame rises whatever the hand does).")]
        [SerializeField] private Transform keepUpright;
        [SerializeField] private Quaternion uprightRotation = Quaternion.identity;
        [Range(0f, 20f)] public float intensity = 1.6f;
        [Range(0f, 1f)] public float flicker = 0.15f;
        [Range(0.1f, 20f)] public float flickerSpeed = 6f;
        bool _on; float _seed;

        public bool On => _on;
        public Light Light => lightSource;

        public void Configure(Light l, GameObject[] on, GameObject[] off, ParticleSystem[] fx, float lightIntensity, float flick)
        { lightSource = l; onObjects = on; offObjects = off; effects = fx; intensity = lightIntensity; flicker = flick; }

        public void KeepUpright(Transform t, Quaternion worldRot) { keepUpright = t; uprightRotation = worldRot; }

        void LateUpdate() { if (keepUpright) keepUpright.rotation = uprightRotation; }

        void Awake() { _seed = Random.value * 100f; Apply(); }

        public void SetOn(bool on)
        {
            bool changed = on != _on; _on = on; Apply();
            if (changed && playToggleSound && isActiveAndEnabled) PlayToggle(on);
        }

        [Tooltip("Click/ignite sound on toggle (clips from the ASH sound bank by light kind).")]
        public bool playToggleSound = true;
        /// <summary>torch / flashlight / lantern, from the object name.</summary>
        public string Kind { get { var n = transform.root.name.ToLowerInvariant() + " " + name.ToLowerInvariant(); return n.Contains("torch") ? "torch" : n.Contains("flash") ? "flashlight" : "lantern"; } }

        void PlayToggle(bool on)
        {
            var b = ASHSoundBank.Instance; if (!b) return;
            string k = Kind; AudioClip[] set; float pitch = 1f;
            if (k == "torch") { set = on ? b.torchOn : (b.torchOff != null && b.torchOff.Length > 0 ? b.torchOff : b.torchOn); if (!on && (b.torchOff == null || b.torchOff.Length == 0)) pitch = 0.7f; }
            else if (k == "flashlight") set = on ? b.flashlightOn : b.flashlightOff;
            else set = on ? b.lanternOn : b.lanternOff;
            ASHSfx.Play(k + (on ? "On" : "Off"), set, transform.position, b.lightVolume * b.volume * (k == "torch" && !on ? 0.5f : 1f), pitch, 0.04f, 0.7f, 30f);
        }

        void Apply()
        {
            if (lightSource) lightSource.enabled = _on;
            if (onObjects != null) foreach (var g in onObjects) if (g) g.SetActive(_on);
            if (offObjects != null) foreach (var g in offObjects) if (g) g.SetActive(!_on);
            if (effects != null) foreach (var e in effects) if (e) { if (_on) e.Play(true); else e.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        }

        void Update()
        {
            if (!_on || !lightSource || flicker <= 0f) return;
            float n = Mathf.PerlinNoise(_seed, Time.time * flickerSpeed);
            lightSource.intensity = intensity * (1f - flicker * 0.5f + flicker * n);
        }
    }
}
