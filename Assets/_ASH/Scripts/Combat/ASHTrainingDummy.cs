using System;
using Invector;
using UnityEngine;

namespace ASH.Combat
{
    /// <summary>
    /// Practice target, copied from the Genesis TrainingDummy (Blink DummyTarget model, health, reset on death, poise,
    /// base-pivot tilt kick with the Genesis spring numbers from ASHCombatTuning). Takes Invector damage (axe hit boxes,
    /// bullets, arrows) through vIDamageReceiver, shows damage numbers and hit marks. Tagged Enemy so Invector melee hits it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHTrainingDummy : MonoBehaviour, vIDamageReceiver
    {
        [Tooltip("Model root that tilts (child). Found as 'Visual' / 'DummyTarget' if empty.")]
        [SerializeField] private Transform visualRoot;
        [Tooltip("0 = use ASHCombatTuning.dummyMaxHealth.")]
        [Range(0f, 100000f)] [SerializeField] private float maxHealthOverride = 0f;

        [SerializeField] private OnReceiveDamage _onStartReceiveDamage = new OnReceiveDamage();
        [SerializeField] private OnReceiveDamage _onReceiveDamage = new OnReceiveDamage();
        public OnReceiveDamage onStartReceiveDamage => _onStartReceiveDamage;
        public OnReceiveDamage onReceiveDamage => _onReceiveDamage;

        public float MaxHealth => maxHealthOverride > 0f ? maxHealthOverride : ASHCombatTuning.Live.dummyMaxHealth;
        public float Health { get; private set; }
        public float Poise { get; private set; }
        public bool IsDead { get; private set; }
        public float TotalDamage { get; private set; }
        public int Hits { get; private set; }
        public event Action<float, float> HealthChanged;

        Vector2 _tilt, _tiltVel; float _kickWindowStart = -10f, _kickUsed, _deathAt, _lastHit = -10f;
        Vector3 _pivotLocal; bool _pivotOk;

        void Awake()
        {
            if (!visualRoot) { visualRoot = transform.Find("Visual") ?? transform.Find("DummyTarget"); }
            if (CompareTag("Untagged")) gameObject.tag = "Enemy";
            if (!GetComponent<Collider>()) FitBox(gameObject.AddComponent<BoxCollider>());
        }

        void OnEnable() { ResetDummy(); }

        public void ResetDummy()
        {
            IsDead = false; Health = MaxHealth; Poise = ASHCombatTuning.Live.dummyPoiseMax; _tilt = _tiltVel = Vector2.zero;
            HealthChanged?.Invoke(Health, MaxHealth);
        }

        public void TakeDamage(vDamage damage)
        {
            if (damage == null) return;
            _onStartReceiveDamage.Invoke(damage);
            float d = damage.damageValue; if (d <= 0f || IsDead) return;
            var t = ASHCombatTuning.Live;
            Health = Mathf.Max(0f, Health - d); TotalDamage += d; Hits++; _lastHit = Time.time;
            bool crit = !string.IsNullOrEmpty(damage.damageType) && damage.damageType.Contains("Crit");
            var src = damage.sender ? damage.sender.position : transform.position - transform.forward;
            // poise (Genesis): a share of the damage, capped per hit; breaking it gives an extra kick
            var w = ASHCombatBridge.Player ? ASHCombatBridge.Player.CurrentData : null;
            float poiseHit = Mathf.Min(d * (w ? w.poiseFromDamage : 0.44f), t.dummyPoiseMax * t.maxPoiseFractionPerHit);
            Poise -= poiseHit; float scale = 1f;
            if (Poise <= 0f) { Poise = t.dummyPoiseMax; scale += t.poiseBreakKickScale; }
            if (w) scale *= Mathf.Lerp(1f, w.bodyWeight, 0.5f);
            Kick(src, scale);
            HealthChanged?.Invoke(Health, MaxHealth);
            var top = Top(); ASHDamageNumber.Show(top, d, crit);
            _onReceiveDamage.Invoke(damage);
            if (Health <= 0f) { IsDead = true; _deathAt = Time.time; }
        }

        void Kick(Vector3 from, float scale)
        {
            var t = ASHCombatTuning.Live;
            Vector3 dir = transform.position - from; dir.y = 0f;
            bool melee = dir.magnitude <= t.dummyMeleeDistance;
            if (dir.sqrMagnitude < 1e-4f) dir = -transform.forward; dir.Normalize();
            float kick = (melee ? t.dummyMeleeKick : t.dummyRangedKick) * scale;
            if (t.dummyKickWindowSeconds > 0f)
            {
                if (Time.time - _kickWindowStart > t.dummyKickWindowSeconds) { _kickWindowStart = Time.time; _kickUsed = 0f; }
                kick = Mathf.Min(kick, Mathf.Max(0f, t.dummyKickWindowCap - _kickUsed)); _kickUsed += kick;
            }
            if (kick <= 0f) return;
            var axisL = transform.InverseTransformDirection(Vector3.Cross(Vector3.up, dir));
            _tiltVel += new Vector2(axisL.x, axisL.z) * kick;
        }

        void Update()
        {
            var t = ASHCombatTuning.Live;
            if (IsDead && Time.time >= _deathAt + t.dummyResetDelay) { ResetDummy(); ASHHitMarks.Clear(transform); }
            if (!IsDead && Time.time > _lastHit + t.poiseRegenDelay) Poise = Mathf.Min(t.dummyPoiseMax, Poise + t.poiseRegenPerSecond * Time.deltaTime);
        }

        void LateUpdate()
        {
            if (!visualRoot) return;
            var t = ASHCombatTuning.Live;
            if (!_pivotOk) ResolvePivot();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float w = 2f * Mathf.PI * Mathf.Max(0.01f, t.dummyTiltFrequency), z = Mathf.Max(0f, t.dummyTiltDampingRatio);
            _tiltVel += (-w * w * _tilt - 2f * z * w * _tiltVel) * dt; _tilt += _tiltVel * dt;
            float max = t.dummyMaxTiltDegrees;
            if (_tilt.magnitude > max)
            {
                _tilt = max > 0f ? _tilt.normalized * max : Vector2.zero;
                var n = _tilt.sqrMagnitude > 1e-6f ? _tilt.normalized : Vector2.zero; float outward = Vector2.Dot(_tiltVel, n);
                if (outward > 0f) _tiltVel -= n * outward;
            }
            var q = Quaternion.Euler(_tilt.x, 0f, _tilt.y);
            visualRoot.localRotation = q; visualRoot.localPosition = _pivotLocal - q * _pivotLocal; // rotate around the base only
        }

        void ResolvePivot()
        {
            _pivotOk = true; _pivotLocal = Vector3.zero;
            var r0 = visualRoot.localRotation; var p0 = visualRoot.localPosition; visualRoot.localRotation = Quaternion.identity; visualRoot.localPosition = Vector3.zero;
            if (Bounds(visualRoot, out var b)) _pivotLocal = transform.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
            visualRoot.localRotation = r0; visualRoot.localPosition = p0;
        }

        Vector3 Top() { return Bounds(transform, out var b) ? new Vector3(b.center.x, b.max.y + 0.15f, b.center.z) : transform.position + Vector3.up * 1.9f; }

        static bool Bounds(Transform root, out Bounds b)
        {
            b = default; bool has = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.name == "ASH_HitMark" || r.GetComponent<TextMesh>()) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            return has;
        }

        public static void FitBox(BoxCollider box)
        {
            if (!box || !Bounds(box.transform, out var b)) return;
            var tr = box.transform; box.center = tr.InverseTransformPoint(b.center);
            var s = tr.lossyScale; box.size = new Vector3(b.size.x / Mathf.Max(1e-4f, Mathf.Abs(s.x)), b.size.y / Mathf.Max(1e-4f, Mathf.Abs(s.y)), b.size.z / Mathf.Max(1e-4f, Mathf.Abs(s.z)));
        }
    }
}
