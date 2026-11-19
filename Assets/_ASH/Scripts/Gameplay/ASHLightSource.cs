using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>On/off presentation of a held light (lantern, torch, flashlight): light, flicker, on/off visuals, particles.
    /// Fuel and burn time are tracked by ASHEquipment (survives unequip).
    /// Torch flame and point light are anchored to a FlameTip child at the mesh bounds tip (not the grip / hand),
    /// with an adjustable tipOffset. The held pose (this transform) is never moved.</summary>
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

        [Header("Torch flame tip")]
        [Tooltip("Created or reused under this object, at the top of the torch mesh. The held pose is not changed.")]
        [SerializeField] Transform flameTip;
        [Tooltip("Added in this object's local space after the mesh-bounds tip.")]
        [SerializeField] Vector3 tipOffset = Vector3.zero;
        [Tooltip("Extra push along the mesh tip axis, in metres (sits the flame just past the wrapping).")]
        [Range(-0.2f, 0.2f)] public float tipPadding = 0.015f;
        [SerializeField] bool anchorFlameToMeshTip = true;
        [Tooltip("Flame particle space. Local = the flame stays on the tip while the torch moves (World left the flame behind the moving torch, so it drifted off the top). Custom/World = old look.")]
        [SerializeField] ParticleSystemSimulationSpace flameSimulationSpace = ParticleSystemSimulationSpace.Local;

        bool _on; float _seed; bool _anchored;

        public bool On => _on;
        public Light Light => lightSource;
        public Transform FlameTip => flameTip;

        public void Configure(Light l, GameObject[] on, GameObject[] off, ParticleSystem[] fx, float lightIntensity, float flick)
        { lightSource = l; onObjects = on; offObjects = off; effects = fx; intensity = lightIntensity; flicker = flick; _anchored = false; }

        public void KeepUpright(Transform t, Quaternion worldRot) { keepUpright = t; uprightRotation = worldRot; }

        void LateUpdate() { if (keepUpright) keepUpright.rotation = uprightRotation; }

        void Awake() { _seed = Random.value * 100f; AnchorFlameToTip(); ApplyFlameSpace(); Apply(); }

        void OnEnable() { AnchorFlameToTip(); ApplyFlameSpace(); }

        /// <summary>Torch flame particles follow the tip (Local) unless flameSimulationSpace says otherwise. Lights other than torches are untouched.</summary>
        void ApplyFlameSpace()
        {
            if (Kind != "torch" && keepUpright == null) return;
            if (effects != null) foreach (var e in effects) SetSpace(e);
            if (keepUpright) foreach (var e in keepUpright.GetComponentsInChildren<ParticleSystem>(true)) SetSpace(e);
            if (flameTip) foreach (var e in flameTip.GetComponentsInChildren<ParticleSystem>(true)) SetSpace(e);
        }
        void SetSpace(ParticleSystem e) { if (!e) return; var m = e.main; if (m.simulationSpace != flameSimulationSpace && flameSimulationSpace != ParticleSystemSimulationSpace.Custom) m.simulationSpace = flameSimulationSpace; }

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

        /// <summary>
        /// Parents the flame particles and point light to a FlameTip at the top of the torch mesh.
        /// Does not move this transform (the hand placement).
        /// </summary>
        public void AnchorFlameToTip()
        {
            if (_anchored || !anchorFlameToMeshTip) return;
            if (Kind != "torch" && keepUpright == null) return;
            if (!TryMeshTip(transform, out var tip, out var axis)) return;

            var tipTf = flameTip;
            if (!tipTf)
            {
                var found = transform.Find("FlameTip");
                tipTf = found ? found : null;
            }
            if (!tipTf)
            {
                var go = new GameObject("FlameTip");
                tipTf = go.transform;
                tipTf.SetParent(transform, false);
            }
            tipTf.localPosition = tip + tipOffset + axis * tipPadding;
            tipTf.localRotation = Quaternion.identity;
            flameTip = tipTf;

            ParentAtTip(keepUpright, tipTf);
            if (effects != null)
                for (int i = 0; i < effects.Length; i++)
                    if (effects[i]) ParentAtTip(effects[i].transform, tipTf);
            if (onObjects != null)
                for (int i = 0; i < onObjects.Length; i++)
                    if (onObjects[i] && onObjects[i].GetComponent<ParticleSystem>())
                        ParentAtTip(onObjects[i].transform, tipTf);
            if (lightSource && lightSource.transform != transform)
                ParentAtTip(lightSource.transform, tipTf);

            _anchored = true;
        }

        static void ParentAtTip(Transform t, Transform tip)
        {
            if (!t || t == tip || t.IsChildOf(tip) && t.parent == tip) return;
            if (t == tip.parent || tip.IsChildOf(t) && t != tip) return;
            var scale = t.localScale;
            t.SetParent(tip, false);
            t.localPosition = Vector3.zero;
            t.localScale = scale;
        }

        /// <summary>Local-space tip of the first mesh renderers under root (ignores particle renderers). The end of the longest bounds axis that is farther from the grip (origin).</summary>
        public static bool TryMeshTip(Transform root, out Vector3 localTip, out Vector3 localAxis)
        {
            localTip = Vector3.zero;
            localAxis = Vector3.forward;
            if (!root) return false;
            bool any = false;
            var b = new Bounds(Vector3.zero, Vector3.zero);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (!r || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf) mesh = mf.sharedMesh;
                if (!mesh)
                {
                    var smr = r as SkinnedMeshRenderer;
                    if (smr) mesh = smr.sharedMesh;
                }
                if (!mesh) continue;
                var mb = mesh.bounds;
                var c = mb.center;
                var e = mb.extents;
                for (int k = 0; k < 8; k++)
                {
                    var p = c + Vector3.Scale(e, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                    var lp = root.InverseTransformPoint(r.transform.TransformPoint(p));
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; }
                    else b.Encapsulate(lp);
                }
            }
            if (!any) return false;
            var size = b.size;
            int ax = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var axis = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            float ext = ax == 0 ? b.extents.x : ax == 1 ? b.extents.y : b.extents.z;
            var a = b.center + axis * ext;
            var c2 = b.center - axis * ext;
            if (a.sqrMagnitude >= c2.sqrMagnitude) { localTip = a; localAxis = axis; }
            else { localTip = c2; localAxis = -axis; }
            if (localAxis.sqrMagnitude < 0.0001f) localAxis = Vector3.forward;
            else localAxis.Normalize();
            return true;
        }
    }
}
