using ASH.Interaction;
using ASH.Inventory;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Axe swing while harvesting wood (ASHChoppable: trees, downed trees, logs, stumps). Plays the Villager Axe-Chopping
    /// clip (from Genesis) on the ASH_Action layer, time-locked to the interact hold so the axe lands exactly when the hit
    /// applies (hold complete = impactNormalized on the clip; the chop audio already plays on that hit). Faces the target,
    /// locks movement during the swing and shows the hatchet in the right hand (restores the previous item afterwards).
    /// swingSpeed scales the hold per hit (ASHChoppable.SwingSpeed).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1660)]
    public sealed class ASHChopAnimator : MonoBehaviour
    {
        [Range(0.25f, 3f)] public float swingSpeed = 1f;
        [Tooltip("Normalized time on the chop clip where the axe hits.")]
        [Range(0f, 1f)] public float impactNormalized = 0.42f;
        public bool faceTarget = true;
        [Range(1f, 30f)] public float faceSpeed = 10f;
        public bool lockMovement = true;
        public bool showAxeInHand = true;
        [Range(0f, 1.5f)] public float releaseDelay = 0.3f;

        public bool Chopping { get; private set; }
        public int Impacts { get; private set; }
        public float ClipTime { get; private set; }
        public bool MovementLocked => _cc && _cc.lockMovement;

        ASHInteractor _ix; ASHInventory _inv; ASHEquipment _eq; vThirdPersonController _cc; ASHActionLayer _layer;
        float _last = -10f, _prevP; bool _active, _savedLockMove, _savedLockRot, _tempEquip; ASHItemDefinition _prevMain; ASHChoppable _target;

        void Awake()
        {
            _ix = GetComponentInParent<ASHInteractor>(); if (!_ix) _ix = FindAnyObjectByType<ASHInteractor>();
            _inv = GetComponentInParent<ASHInventory>(); _eq = GetComponentInParent<ASHEquipment>(); _cc = GetComponentInParent<vThirdPersonController>();
            _layer = GetComponent<ASHActionLayer>(); if (!_layer) _layer = gameObject.AddComponent<ASHActionLayer>();
        }

        void Update()
        {
            ASHChoppable.SwingSpeed = swingSpeed;
            var ch = _ix ? _ix.Target as ASHChoppable : null;
            float p = _ix ? _ix.HoldProgress01 : 0f;
            bool swinging = ch && p > 0f && !ch.Done && _inv && _inv.HasTool(ASHToolType.Axe);
            if (swinging) { _last = Time.time; _target = ch; }
            Chopping = Time.time - _last < releaseDelay;

            if (Chopping && !_active) Begin();
            else if (!Chopping && _active) End();
            if (!_active) { _prevP = 0f; return; }

            if (swinging)
            {
                if (p < _prevP - 0.3f) Impacts++; // hold wrapped = a hit landed
                ClipTime = Mathf.Repeat(impactNormalized + p, 1f);
                _prevP = p;
            }
            else ClipTime = Mathf.Repeat(ClipTime + Time.deltaTime * 0.8f, 1f); // follow-through while releasing
            _layer.PlayAt("Chop", ClipTime);

            if (faceTarget && _target && _cc)
            {
                var d = _target.transform.position - _cc.transform.position; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) _cc.transform.rotation = Quaternion.Slerp(_cc.transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-faceSpeed * Time.deltaTime));
            }
        }

        void Begin()
        {
            _active = true; _prevP = 0f;
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
            _active = false; _layer.Release();
            if (_cc && lockMovement) { _cc.lockMovement = _savedLockMove; _cc.lockRotation = _savedLockRot; }
            if (_tempEquip && _eq)
            {
                if (_prevMain && _inv.Count(_prevMain) > 0) _eq.Equip(_prevMain); else _eq.Unequip(ASHEquipSlot.MainHand);
            }
            _tempEquip = false; _prevMain = null; _target = null;
        }

        void OnDisable() { if (_active) End(); }
    }
}
