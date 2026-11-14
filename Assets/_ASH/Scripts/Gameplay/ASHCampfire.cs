using ASH.Interaction;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Placed campfire: flickering light, fire/smoke particles, toggle lit by interaction.</summary>
    public sealed class ASHCampfire : MonoBehaviour, IASHUsable, IASHCraftStation
    {
        [SerializeField] private Light fireLight;
        [SerializeField] private ParticleSystem[] effects;
        [SerializeField] private AudioSource crackle;
        [Range(0f, 20f)] public float baseIntensity = 3.2f;
        [Range(0f, 1f)] public float flicker = 0.35f;
        [Range(0.1f, 20f)] public float flickerSpeed = 7f;
        [SerializeField] private bool lit = true;

        public bool Lit => lit;
        public Transform UseTransform => transform;
        public static event System.Action<ASHCampfire> Placed;

        private float _seed;

        public ASHCraftStation StationType => ASHCraftStation.Campfire;
        public bool StationActive => lit && isActiveAndEnabled;
        public Vector3 StationPosition => transform.position;
        private void OnEnable() { if (!ASHCrafting.Stations.Contains(this)) ASHCrafting.Stations.Add(this); }
        private void OnDisable() { ASHCrafting.Stations.Remove(this); }

        private void Start() { _seed = Random.value * 100f; SetLit(lit); Placed?.Invoke(this); }

        private void Update()
        {
            if (!lit || fireLight == null) return;
            float n = Mathf.PerlinNoise(_seed, Time.time * flickerSpeed);
            fireLight.intensity = baseIntensity * (1f - flicker * 0.5f + flicker * n);
        }

        public void SetLit(bool on)
        {
            lit = on;
            if (fireLight) fireLight.enabled = on;
            if (effects != null) foreach (var e in effects) if (e) { if (on) e.Play(); else e.Stop(); }
            if (crackle) { if (on) crackle.Play(); else crackle.Stop(); }
        }

        public float GetUsePriority(in ASHUseContext c) => 30f;
        public float GetHoldDuration(in ASHUseContext c) => lit ? 0.6f : 0f;
        public string GetPrompt(in ASHUseContext c) => lit ? "Put out fire" : "Light fire";
        public bool TryUse(in ASHUseContext c) { SetLit(!lit); return true; }
    }
}
