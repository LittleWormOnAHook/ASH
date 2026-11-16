using ASH.Interaction;
using ASH.Inventory;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Axe swing while harvesting wood (ASHChoppable: trees, downed trees, logs, stumps). Plays an axe clip copied from Genesis
    /// (overhead Villager Axe-Chopping02 on low wood, level Malbers Axe_2Hand Swing_Right on standing trees) full body on the ASH_ActionFull layer, time-locked to the interact hold so the axe lands exactly when the hit
    /// applies (hold complete = impactNormalized on the clip; the chop audio already plays on that hit). Faces the target,
    /// snaps once (smoothly, snapSeconds) to a chop position chopDistance from the wood's surface and holds him there for every
    /// swing (no root motion, no rubberbanding, no return afterward), locks movement during the swing and shows the hatchet in the right hand (restores the previous item afterwards).
    /// swingSpeed scales the hold per hit (ASHChoppable.SwingSpeed).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1660)]
    public sealed class ASHChopAnimator : MonoBehaviour
    {
        [Range(0.25f, 3f)] public float swingSpeed = 1f;
        [Tooltip("Chop effort: hits needed per log / stump / tree x this (2 = felling takes twice as long; swing speed unchanged). Applies to trees as they load.")]
        [Range(0.25f, 6f)] public float chopEffortMultiplier = 2f;
        [Tooltip("Normalized time on the low chop clip (Villager Axe-Chopping02: logs, stumps, downed trees) where the axe hits.")]
        [Range(0f, 1f)] public float impactNormalized = 0.51f;
        [Tooltip("Use the level swing (Malbers Axe_2Hand Swing_Right) on standing trees.")]
        public bool levelSwingOnStandingTrees = true;
        [Tooltip("Normalized time on the level swing clip where the axe hits the trunk.")]
        [Range(0f, 1f)] public float impactNormalizedHigh = 0.506f;
        [Header("Felling (standing trees)")]
        [Tooltip("Felling: facing offset from the trunk centre (deg, + = right).")]
        [Range(-45f, 45f)] public float fellFacingYaw = 0f;
        [Tooltip("Felling: distance from the trunk's bark to the player's feet (m). The level swing's axe head reaches ~0.96 m.")]
        [Range(0.4f, 1.6f)] public float fellDistance = 0.85f;
        [Tooltip("Felling: time of the one move + turn into the set spot before the first swing (s).")]
        [Range(0.1f, 1f)] public float fellSnapSeconds = 0.4f;
        public bool faceTarget = true;
        [Range(1f, 30f)] public float faceSpeed = 10f;
        public bool lockMovement = true;
        [Header("Swing aim")]
        [Tooltip("Extra turn of the chop facing (deg, + = right). The clips swing slightly off their own forward.")]
        [Range(-45f, 45f)] public float chopFacingYaw = 10f;
        [Tooltip("Learns a small extra facing correction from where the blade actually lands at each impact, so it hits the trunk centre square.")]
        public bool autoSquareSwing = false; // experimental: learn a facing correction from where the blade lands (off: the blade-point estimate is not reliable for every axe mesh)
        [Range(0f, 30f)] public float autoSquareMax = 20f;
        [Tooltip("Roll of the held axe about its handle while chopping (deg) - aim-only delta so the blade edge, not the back of the head, leads into the wood. Rest placement untouched.")]
        [Range(-180f, 180f)] public float chopAxeRoll = 0f;
        /// <summary>Blade sideways miss from the trunk centre line at the last impact (m, + = right of centre) / learned yaw.</summary>
        public float BladeMiss { get; private set; } = float.NaN;
        public float BladeMissDeg { get; private set; } = float.NaN;
        public float LearnedYaw => _yawCorr;
        public Vector3 BladePoint { get; private set; }
        float _yawCorr;
        [Tooltip("Blend from the last swing's follow-through back to idle (s).")]
        [Range(0.05f, 0.6f)] public float chopExitSeconds = 0.3f;
        [Header("Chop position (snap once, then hold still for every swing)")]
        public bool snapToChopPosition = true;
        [Tooltip("Distance from the wood's surface (trunk/log) to the player's feet while chopping (metres).")]
        [Range(0.4f, 1.6f)] public float chopDistance = 0.9f;
        [Tooltip("Time of the one smooth move into the chop position.")]
        [Range(0.05f, 0.5f)] public float snapSeconds = 0.2f;
        [Tooltip("Furthest the snap may move the player (beyond this he chops from where he stands).")]
        [Range(0.5f, 4f)] public float maxSnapDistance = 2.5f;
        public bool showAxeInHand = true;
        [Range(0f, 1.5f)] public float releaseDelay = 0.3f;

        public bool Chopping { get; private set; }
        public int Impacts { get; private set; }
        public float ClipTime { get; private set; }
        /// <summary>Action layer state used for the current swing ("Chop" or "ChopHigh").</summary>
        public string SwingState { get; private set; } = "Chop";
        public bool MovementLocked => _cc && _cc.lockMovement;
        /// <summary>Where the player is held while chopping (valid while Chopping and Snapped).</summary>
        public Vector3 ChopPosition => _snapTo;
        public bool Snapped { get; private set; }
        /// <summary>Distance from the chop position to the wood's surface (m), -1 if not snapped.</summary>
        public float SnapSurfaceDistance { get; private set; } = -1f;

        ASHInteractor _ix; ASHInventory _inv; ASHEquipment _eq; vThirdPersonController _cc; ASHActionLayer _layer;
        float _last = -10f, _prevP; bool _active, _savedLockMove, _savedLockRot, _tempEquip; ASHItemDefinition _prevMain; ASHChoppable _target;
        Vector3 _snapFrom, _snapTo; float _snapT; Rigidbody _rb; Animator _anim; Quaternion _snapRotFrom; bool _fell;
        bool SnapDone => !_locked || _snapT >= 1f;
        // hard lock (whole chop sequence): saved rigidbody / animator settings, restored on release
        bool _locked, _savedRootMotion; RigidbodyInterpolation _savedInterp; RigidbodyConstraints _savedCons; Quaternion _lockRot; bool _haveLockRot;
        /// <summary>Position the player is pinned at for the whole chop sequence (valid while HardLocked).</summary>
        public Vector3 LockPosition { get; private set; }
        public bool HardLocked => _locked;

        void Awake()
        {
            _ix = GetComponentInParent<ASHInteractor>(); if (!_ix) _ix = FindAnyObjectByType<ASHInteractor>();
            _inv = GetComponentInParent<ASHInventory>(); _eq = GetComponentInParent<ASHEquipment>(); _cc = GetComponentInParent<vThirdPersonController>();
            _layer = GetComponent<ASHActionLayer>(); if (!_layer) _layer = gameObject.AddComponent<ASHActionLayer>();
            _rb = _cc ? _cc.GetComponent<Rigidbody>() : null; _anim = GetComponent<Animator>();
        }

        void Update()
        {
            ASHChoppable.SwingSpeed = swingSpeed; ASHChoppable.HitsMultiplier = chopEffortMultiplier;
            var ch = _ix ? _ix.Target as ASHChoppable : null;
            float p = _ix ? _ix.HoldProgress01 : 0f;
            bool swinging = ch && p > 0f && !ch.Done && _inv && _inv.HasTool(ASHToolType.Axe);
            if (swinging) { _last = Time.time; _target = ch; }
            Chopping = Time.time - _last < releaseDelay;

            if (Chopping && !_active) Begin(ch);
            else if (!Chopping && _active) End();
            if (!_active)
            {
                _prevP = 0f;
                if (_locked)
                {   // exit blend: stay put (the clip's recovery step is root motion, discarded) until it finishes or he moves
                    bool moving = _cc && _cc.input.sqrMagnitude > 0.01f;
                    if (moving || Time.time >= _exitUntil) Lock(false); else HoldPosition(false);
                }
                if (Time.time < _exitUntil && !string.IsNullOrEmpty(SwingState) && _layer.Weight > 0f)
                { ClipTime = Mathf.Min(0.999f, ClipTime + Time.deltaTime * 0.8f); _layer.PlayAt(SwingState, ClipTime, false); } // follow-through keeps moving while it blends out
                if (_reequip && Time.time >= _exitUntil) Reequip();
                return;
            }

            if (swinging)
            {
                if (p < _prevP - 0.3f) { Impacts++; MeasureBlade(ch); } // hold wrapped = a hit landed
                bool high = levelSwingOnStandingTrees && ch.Kind == ASHChopKind.StandingTree && _layer.Has("ChopHigh");
                SwingState = high ? "ChopHigh" : "Chop";
                ClipTime = Mathf.Repeat((high ? impactNormalizedHigh : impactNormalized) + p, 1f);
                _prevP = p;
            }
            else ClipTime = Mathf.Repeat(ClipTime + Time.deltaTime * 0.8f, 1f); // follow-through while releasing
            if (!_fell || SnapDone) _layer.PlayAt(SwingState, ClipTime); // felling: swings start once he is in the spot, facing the trunk

            if (_fell && _target && _cc)
            {   // felling: one smooth move + turn into the set spot (face the trunk centre), then locked for every swing
                var c = TrunkCentre(_target); var d = c - _snapTo; d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) { var want = Quaternion.LookRotation(d) * Quaternion.Euler(0f, fellFacingYaw, 0f); _lockRot = Quaternion.Slerp(_snapRotFrom, want, Mathf.SmoothStep(0f, 1f, _snapT)); _haveLockRot = true; }
            }
            else if (faceTarget && _target && _cc)
            {
                var d = SurfacePoint(_target, _cc.transform.position) - _cc.transform.position; d.y = 0f;
                if (d.sqrMagnitude < 0.01f) { d = _target.transform.position - _cc.transform.position; d.y = 0f; }
                if (d.sqrMagnitude > 0.01f) { _lockRot = Quaternion.Slerp(_haveLockRot ? _lockRot : _cc.transform.rotation, Quaternion.LookRotation(d) * Quaternion.Euler(0f, chopFacingYaw + _yawCorr, 0f), 1f - Mathf.Exp(-faceSpeed * Time.deltaTime)); _haveLockRot = true; }
            }
            HoldPosition();
            RollAxe(true);
        }

        void FixedUpdate() { if (_active || _locked) HoldPosition(false); } // runs after Invector's FixedUpdate (order 1660): overrides its locomotion/ground correction

        /// <summary>Axe head point: the far end of the held axe's mesh from the hand (oriented mesh bounds).</summary>
        public bool BladeWorld(out Vector3 p)
        {
            p = default; var v = _eq ? _eq.MainVisual : null; var a = GetComponent<Animator>();
            var hand = a && a.isHuman ? a.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (!v || !hand) return false;
            float best = -1f;
            foreach (var mf in v.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || !mf.gameObject.activeInHierarchy) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mf.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    float d = (c - hand.position).sqrMagnitude; if (d > best) { best = d; p = c; }
                }
            }
            return best > 0f;
        }

        void MeasureBlade(ASHChoppable ch)
        {
            if (!ch || !_cc || !BladeWorld(out var blade)) return;
            BladePoint = blade;
            var me = _cc.transform.position; var centre = ch.Kind == ASHChopKind.StandingTree ? ch.transform.position : SurfacePoint(ch, me);
            var toC = centre - me; toC.y = 0f; var toB = blade - me; toB.y = 0f;
            if (toC.sqrMagnitude < 1e-4f || toB.sqrMagnitude < 1e-4f) return;
            BladeMissDeg = Vector3.SignedAngle(toC, toB, Vector3.up); // + = blade lands right of the trunk centre
            BladeMiss = Vector3.Dot(blade - centre, Vector3.Cross(Vector3.up, toC.normalized));
            if (autoSquareSwing) _yawCorr = Mathf.Clamp(_yawCorr - BladeMissDeg * 0.6f, -autoSquareMax, autoSquareMax);
        }

        Quaternion _axeRest; Transform _axeT; bool _axeRolled;
        void RollAxe(bool on)
        {
            var v = _eq ? _eq.MainVisual : null;
            if (_axeRolled && (!on || !v || v.transform != _axeT)) { if (_axeT) _axeT.localRotation = _axeRest; _axeRolled = false; _axeT = null; }
            if (!on || !v || Mathf.Abs(chopAxeRoll) < 0.01f || _axeRolled) return;
            _axeT = v.transform; _axeRest = _axeT.localRotation; _axeRolled = true;
            // roll about the handle = the longest axis of the axe mesh
            var mf = v.GetComponentInChildren<MeshFilter>(); var e = mf && mf.sharedMesh ? mf.sharedMesh.bounds.extents : Vector3.up;
            var axisLocal = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
            var axisWorld = mf ? mf.transform.TransformDirection(axisLocal) : _axeT.up;
            _axeT.rotation = Quaternion.AngleAxis(chopAxeRoll, axisWorld) * _axeT.rotation;
        }
        void LateUpdate() { if (_active || _locked) HoldPosition(false); }

        /// <summary>Hard lock for the whole chop sequence: one smooth move into the chop position, then pinned there every Update,
        /// FixedUpdate and LateUpdate (zero velocity, no movement input, no root motion, rigidbody frozen and not interpolated so
        /// nothing can drag him back), until the tree falls or he lets go. Releasing leaves him where he chopped.</summary>
        void HoldPosition(bool advance = true)
        {
            if (!_cc || !_locked) return;
            Vector3 p;
            if (Snapped)
            {
                if (advance) _snapT = Mathf.Min(1f, _snapT + Time.deltaTime / Mathf.Max(0.01f, _fell ? fellSnapSeconds : snapSeconds));
                p = Vector3.Lerp(_snapFrom, _snapTo, Mathf.SmoothStep(0f, 1f, _snapT));
            }
            else p = _snapFrom; // no snap spot: pinned where he stood
            LockPosition = p;
            _cc.transform.position = p;
            if (_haveLockRot) _cc.transform.rotation = _lockRot;
            _cc.input = Vector3.zero;
            if (_rb) { _rb.position = p; if (_haveLockRot) _rb.rotation = _lockRot; if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; } }
        }

        void Lock(bool on)
        {
            if (on == _locked || !_cc) return;
            _locked = on;
            if (on)
            {
                _haveLockRot = false;
                if (_rb) { _savedInterp = _rb.interpolation; _savedCons = _rb.constraints; _rb.interpolation = RigidbodyInterpolation.None; _rb.constraints = RigidbodyConstraints.FreezeAll; }
                if (_anim) { _savedRootMotion = _anim.applyRootMotion; _anim.applyRootMotion = false; }
            }
            else
            {
                if (_rb) { _rb.constraints = _savedCons; _rb.linearVelocity = Vector3.zero; _rb.position = _cc.transform.position; _rb.interpolation = _savedInterp; }
                if (_anim) _anim.applyRootMotion = _savedRootMotion;
                _cc.input = Vector3.zero;
            }
        }

        /// <summary>Trunk centre on the ground plane: the standing tree's pivot (trunk base).</summary>
        public static Vector3 TrunkCentre(ASHChoppable c) => c.transform.position;

        static Vector3 SurfacePoint(ASHChoppable c, Vector3 from)
        {
            Vector3 best = c.transform.position; float bd = float.MaxValue;
            foreach (var col in c.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger) continue;
                var q = new Vector3(from.x, Mathf.Clamp(from.y + 0.9f, col.bounds.min.y, col.bounds.max.y), from.z);
                var cp = col is MeshCollider mc && !mc.convex ? col.ClosestPointOnBounds(q) : col.ClosestPoint(q);
                float d = (cp - q).sqrMagnitude; if (d < bd) { bd = d; best = cp; }
            }
            return best;
        }

        /// <summary>Chop position: chopDistance out from the wood's surface on the side the player came from, on the ground,
        /// with room for the player's capsule. Tries a few angles around that side; false = chop from where he stands.</summary>
        bool FindChopPosition(ASHChoppable c, out Vector3 pos, out float surfDist)
        {
            pos = _cc.transform.position; surfDist = -1f;
            var surf = SurfacePoint(c, pos); var outDir = pos - surf; outDir.y = 0f;
            if (outDir.sqrMagnitude < 1e-4f) { outDir = pos - c.transform.position; outDir.y = 0f; }
            if (outDir.sqrMagnitude < 1e-4f) outDir = -_cc.transform.forward;
            outDir.Normalize();
            var cap = _cc.GetComponent<CapsuleCollider>(); float r = cap ? cap.radius : 0.3f, h = cap ? cap.height : 1.8f;
            foreach (float ang in new[] { 0f, 25f, -25f, 50f, -50f })
            {
                var dir = Quaternion.AngleAxis(ang, Vector3.up) * outDir;
                var s2 = SurfacePoint(c, surf + dir * 0.5f);
                var cand = new Vector3(s2.x, pos.y, s2.z) + dir * (c.Kind == ASHChopKind.StandingTree ? fellDistance : chopDistance);
                if (!Physics.Raycast(cand + Vector3.up * 1.5f, Vector3.down, out var g, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (g.collider.GetComponentInParent<ASHChoppable>() == c || g.collider.transform.IsChildOf(_cc.transform)) continue;
                cand.y = g.point.y;
                if ((cand - pos).magnitude > maxSnapDistance) continue;
                bool blocked = false;
                foreach (var o in Physics.OverlapCapsule(cand + Vector3.up * (r + 0.05f), cand + Vector3.up * (h - r), r * 0.95f, ~0, QueryTriggerInteraction.Ignore))
                    if (!o.transform.IsChildOf(_cc.transform) && o.GetComponentInParent<ASHChoppable>() != c) { blocked = true; break; }
                if (blocked) continue;
                pos = cand; var fs = new Vector3(s2.x, 0f, s2.z); surfDist = Vector3.Distance(new Vector3(cand.x, 0f, cand.z), fs); return true;
            }
            return false;
        }

        void Begin(ASHChoppable ch)
        {
            if (_locked) Lock(false); // still easing out of the last chop
            _active = true; _prevP = 0f; Snapped = false; SnapSurfaceDistance = -1f; _reequip = false;
            _fell = ch && ch.Kind == ASHChopKind.StandingTree; _snapRotFrom = _cc ? _cc.transform.rotation : Quaternion.identity;
            if (snapToChopPosition && _cc && ch && FindChopPosition(ch, out var to, out var sd))
            { _snapFrom = _cc.transform.position; _snapTo = to; _snapT = 0f; Snapped = true; SnapSurfaceDistance = sd; }
            else if (_cc) { _snapFrom = _snapTo = _cc.transform.position; _snapT = 1f; }
            Lock(true);
            if (_cc && lockMovement) { _savedLockMove = _cc.lockMovement; _savedLockRot = _cc.lockRotation; _cc.lockMovement = true; _cc.lockRotation = true; }
            if (showAxeInHand && _eq && _inv && !(_eq.Main && _eq.Main.toolType == ASHToolType.Axe))
            {
                ASHItemDefinition axe = null;
                var s = _inv.Slots; for (int i = 0; i < s.Count && !axe; i++) if (!s[i].IsEmpty && s[i].item.toolType == ASHToolType.Axe && s[i].item.IsEquippable) axe = s[i].item;
                if (axe) { _prevMain = _eq.Main; _tempEquip = _eq.Equip(axe); }
            }
        }

        void End()
        {
            RollAxe(false);
            // stays where he chopped (no return to the start point); the lock lifts when the exit blend ends or he moves
            if (_locked) { _snapFrom = _snapTo = LockPosition; _snapT = 1f; } // pinned where he chopped while easing out - never back to the start
            _active = false; _layer.Release(chopExitSeconds); _exitUntil = Time.time + chopExitSeconds; Snapped = false;
            if (_cc && lockMovement) { _cc.lockMovement = _savedLockMove; _cc.lockRotation = _savedLockRot; }
            _reequip = _tempEquip && _eq; if (!_reequip) { _tempEquip = false; _prevMain = null; }
            _target = null;
        }

        void Reequip()
        {
            _reequip = false;
            if (_tempEquip && _eq) { if (_prevMain && _inv.Count(_prevMain) > 0) _eq.Equip(_prevMain); else _eq.Unequip(ASHEquipSlot.MainHand); }
            _tempEquip = false; _prevMain = null;
        }
        float _exitUntil; bool _reequip;

        void OnDisable() { if (_active) End(); Lock(false); }
    }
}
