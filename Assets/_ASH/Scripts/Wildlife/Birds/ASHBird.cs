using System;
using ASH.Interaction;
using ASH.Inventory;
using Invector;
using UnityEngine;

namespace ASH.Wildlife.Birds
{
    public enum ASHBirdState { Pooled, Fly, Approach, Landed, Falling, Dead, Attack }

    /// <summary>
    /// One pooled bird. ASHBirdManager moves it (boids, landing, pecking). Any hit kills it (one shot from any weapon);
    /// it tumbles to the ground and can be harvested with a knife for feathers and small meat.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHBird : MonoBehaviour, vIDamageReceiver, IASHUsable
    {
        /// <summary>Per-model animation names (filled by Setup Birds from the vendor clips). Empty = the global names in ASH_BirdSettings.</summary>
        [Serializable]
        public sealed class AnimSet
        {
            public bool use;
            [Tooltip("Clips don't loop on import (Birds Misc): loop them by hand.")]
            public bool manualLoop;
            public string[] idle = new string[0];
            public string[] peck = new string[0];
            public string walk = "", fly = "", glide = "", takeOff = "", land = "", death = "";
        }

        public Animator animator;
        [Tooltip("Legacy Animation (Bird Flocks crow / seagull models). Used instead of the Animator when set.")]
        public Animation legacy;
        public AnimSet anims = new AnimSet();
        [Tooltip("Type id used when this bird is placed by hand in a scene (it registers itself with the bird manager).")]
        public string placedTypeId = "crow";
        public SphereCollider body;
        [Tooltip("Radius of the alive hit sphere at scale 1 (set by Setup Birds from the mesh).")]
        public float bodyRadius = 0.16f;
        [Tooltip("Radius of the dead bird's interaction trigger at scale 1.")]
        public float harvestRadius = 0.35f;

        internal ASHBirdManager mgr;
        internal ASHBirdManager.Flock flock;
        internal ASHBirdSettings.BirdType type;
        internal ASHBirdState state = ASHBirdState.Pooled;
        internal Vector3 vel, perch, perchFwd, avoidDir;
        internal bool perchGround, gliding;
        internal float speedMul = 1f, stateAt, launchAt, nextAnimSwap, deadAt, groundY, groundCheckAt, avoidAt, tickAcc, targetYaw;
        internal int perchFails;
        // attack birds
        internal bool aggressive;
        internal Transform atkTarget;
        internal Vector3 atkPoint;
        internal int atkPasses, atkPhase;
        internal float atkPhaseAt, attackReadyAt;
        public bool Aggressive => aggressive;
        // pooling / placement / swarm
        internal GameObject poolKey;
        internal bool placed, swarm, fromGround;
        internal int idleIdx, peckIdx;
        internal float placedScale = 1f;
        int _animHash;
        bool _loop;
        // ground behaviour: 0 idle, 1 peck, 2 walk
        internal int gMode;
        internal Vector3 groundHome, walkTo;
        internal float walkPhase, alertW, alertTarget, alertFrom;
        internal Collider surface;
        Transform _head, _neck;
        Quaternion _headBase, _headSet, _neckBase, _neckSet;
        bool _headApplied;

        string _anim;
        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        SphereCollider _harvestTrigger;
        readonly OnReceiveDamage _onStart = new OnReceiveDamage();
        readonly OnReceiveDamage _onRecv = new OnReceiveDamage();

        public OnReceiveDamage onStartReceiveDamage => _onStart;
        public OnReceiveDamage onReceiveDamage => _onRecv;
        public Transform UseTransform => transform;
        public ASHBirdState State => state;
        public bool IsDead => state == ASHBirdState.Falling || state == ASHBirdState.Dead;
        public string DisplayName => type != null && !string.IsNullOrEmpty(type.displayName) ? type.displayName : "Bird";

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!legacy) legacy = GetComponentInChildren<Animation>();
            if (legacy) { legacy.playAutomatically = false; legacy.cullingType = AnimationCullingType.BasedOnRenderers; if (animator && !animator.runtimeAnimatorController) animator = null; }
            if (animator) { animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.CullCompletely; }
            if (!body) body = GetComponent<SphereCollider>();
            if (!body) { body = gameObject.AddComponent<SphereCollider>(); body.radius = bodyRadius; }
            _renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLowerInvariant();
                if (!_head && n == "head") _head = t;
                else if (!_neck && n == "neck") _neck = t;
            }
            var rb = GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>Applies a type's look (tint, scale) and resets per-life state.</summary>
        internal void Apply(ASHBirdSettings.BirdType t, System.Random rng, float aggressiveShare = -1f)
        {
            type = t;
            float share = aggressiveShare >= 0f ? aggressiveShare : (t != null ? t.attackerChance : 0f);
            aggressive = t != null && t.canAttack && rng.NextDouble() < share;
            atkTarget = null; attackReadyAt = 0f;
            float sc = t != null ? t.scale : 1f;
            sc *= 1f + ((float)rng.NextDouble() - 0.5f) * 0.12f;
            transform.localScale = Vector3.one * Mathf.Max(0.05f, sc * placedScale);
            speedMul = 1f + ((float)rng.NextDouble() * 2f - 1f) * (t != null ? t.speedJitter : 0.1f);
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            var c = t != null ? t.tint : Color.white;
            foreach (var r in _renderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_Color", c);
                r.SetPropertyBlock(_mpb);
            }
            if (body) { body.enabled = true; body.isTrigger = false; body.radius = bodyRadius; }
            if (_harvestTrigger) _harvestTrigger.enabled = false;
            if (animator) { animator.enabled = true; animator.speed = 1f; }
            if (legacy) legacy.enabled = true;
            swarm = false; fromGround = false;
            idleIdx = rng.Next(16); peckIdx = rng.Next(16);
            _anim = null;
            perchFails = 0;
            gliding = false;
            vel = Vector3.zero;
            tickAcc = 0f;
            gMode = 0; alertW = alertTarget = 0f; surface = null; walkPhase = 0f;
            _headApplied = false;
        }

        /// <summary>Alert head-up: lifts head and neck after the Animator has posed them. Detects whether the Animator wrote this
        /// frame (culled / disabled animators don't), so the offset never accumulates.</summary>
        void LateUpdate()
        {
            var s = ASHBirdSettings.I;
            float ang = s ? s.alertHeadPitch * alertW : 0f;
            if (ang < 0.05f && !_headApplied) return;
            var right = transform.right;
            if (_head) ApplyLift(_head, ref _headBase, ref _headSet, right, ang * 0.6f);
            if (_neck) ApplyLift(_neck, ref _neckBase, ref _neckSet, right, ang * 0.4f);
            _headApplied = ang >= 0.05f;
        }

        void ApplyLift(Transform bone, ref Quaternion baseRot, ref Quaternion setRot, Vector3 axis, float ang)
        {
            var cur = bone.localRotation;
            // unchanged since we set it = the Animator didn't pose this frame, so start from the stored base
            var start = _headApplied && Quaternion.Dot(cur, setRot) > 0.999999f ? baseRot : cur;
            baseRot = start;
            bone.localRotation = start;
            if (ang >= 0.05f) bone.rotation = Quaternion.AngleAxis(-ang, axis) * bone.rotation;
            setRot = bone.localRotation;
        }

        internal void Play(string st, float speed, bool loop = true)
        {
            if (string.IsNullOrEmpty(st)) return;
            if (legacy)
            {
                if (!legacy.enabled) return;
                var a = legacy[st];
                if (!a) return;
                if (_anim != st)
                {
                    a.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
                    legacy.CrossFade(st, 0.18f);
                    _anim = st;
                }
                a.speed = speed;
                return;
            }
            if (!animator || !animator.isActiveAndEnabled) return;
            if (_anim != st)
            {
                int h = Animator.StringToHash(st);
                if (!animator.HasState(0, h)) return;
                animator.CrossFadeInFixedTime(h, 0.18f);
                _anim = st; _animHash = h;
            }
            _loop = loop && anims != null && anims.manualLoop;
            animator.speed = speed;
        }

        /// <summary>True if this bird's rig has the named state / clip.</summary>
        internal bool Has(string st)
        {
            if (string.IsNullOrEmpty(st)) return false;
            if (legacy) return legacy[st] != null;
            return animator && animator.runtimeAnimatorController && animator.HasState(0, Animator.StringToHash(st));
        }

        void Update()
        {
            // Birds Misc clips import without loop time (vendor files stay untouched): restart looping states by hand
            if (!_loop || !animator || !animator.isActiveAndEnabled || animator.IsInTransition(0)) return;
            var i = animator.GetCurrentAnimatorStateInfo(0);
            if (i.shortNameHash == _animHash && i.normalizedTime >= 0.985f) animator.Play(_animHash, 0, 0f);
        }

        internal void SetAnimatorOn(bool on)
        {
            if (animator && animator.enabled != on) { animator.enabled = on; if (on) _anim = null; }
            if (legacy && legacy.enabled != on) { legacy.enabled = on; if (on) _anim = null; }
        }

        // ------------------------------------------------------------------ hand-placed birds register themselves (no manager wiring needed)
        void Start()
        {
            if (mgr || state != ASHBirdState.Pooled || !Application.isPlaying) return;
            var m = ASHBirdManager.EnsureActive();
            if (m) m.AdoptPlaced(this);
        }

        internal void BecomeCorpse()
        {
            if (body) body.enabled = false;
            if (!_harvestTrigger)
            {
                _harvestTrigger = gameObject.AddComponent<SphereCollider>();
                _harvestTrigger.isTrigger = true;
            }
            _harvestTrigger.radius = harvestRadius;
            _harvestTrigger.enabled = true;
        }

        public void TakeDamage(vDamage damage)
        {
            if (damage == null || IsDead) return;
            if (!mgr || state == ASHBirdState.Pooled)
            {
                // placed bird hit before its first tick: register now so the hit still counts
                var m = ASHBirdManager.EnsureActive();
                if (!m) return;
                m.AdoptPlaced(this);
                if (!mgr) return;
            }
            _onStart.Invoke(damage);
            Vector3 push = transform.position - damage.hitPosition;
            if (damage.hitPosition.sqrMagnitude < 0.0001f || push.sqrMagnitude < 0.0001f)
                push = damage.sender ? transform.position - damage.sender.position : -transform.forward;
            mgr.Kill(this, push.normalized);
            _onRecv.Invoke(damage);
        }

        // ------------------------------------------------------------------ knife harvest
        public float GetUsePriority(in ASHUseContext c)
        {
            if (state != ASHBirdState.Dead) return -1f;
            if (c.Inventory == null || !c.Inventory.HasTool(ASHToolType.Knife)) return 6f;
            return 38f;
        }

        public float GetHoldDuration(in ASHUseContext c)
        {
            var s = ASHBirdSettings.I;
            return state == ASHBirdState.Dead ? (s ? s.harvestHold : 1.4f) : 0f;
        }

        public string GetPrompt(in ASHUseContext c)
        {
            if (state != ASHBirdState.Dead) return null;
            if (c.Inventory == null || !c.Inventory.HasTool(ASHToolType.Knife)) return "Need a knife to harvest the " + DisplayName.ToLowerInvariant();
            return "[Hold E] Harvest " + DisplayName;
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (state != ASHBirdState.Dead || c.Inventory == null || !c.Inventory.HasTool(ASHToolType.Knife) || !mgr) return false;
            mgr.Harvest(this, c.Inventory);
            return true;
        }
    }
}
