using ASH.Core;
using ASH.Inventory;
using ASH.Player;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Basic shooting (no damage, no combat systems), adapted from the Genesis/Invector shooter + bow flow:
    /// - Hold RMB with a rifle, pistol or bow: aim. The body faces the camera (Invector strafe, restored on release) and the
    ///   ASH_Action layer plays the aim pose (Invector Aiming@Rifle / Aiming@Pistol / Bow_Aiming, unmirrored: bow in the left hand).
    /// - Rifle/pistol: LMB fires (hip fire optional): one round from the pack, camera ray hit (nudges rigidbodies),
    ///   muzzle flash, Shot_* pose, camera kick + weapon kick.
    /// - Bow (left hand) + arrow (right hand): RMB brings the hands together, the right hand nocks and draws back (right-arm IK,
    ///   bowDrawLength) and HOLDS full draw until RMB is released; LMB releases once drawn past minDraw01. Launch speed scales with draw time; the arrow flies under gravity (ASHArrow), sticks and can be recovered.
    /// Held visuals are hand-placed on the player prefab and never moved: aim alignment and recoil rotate the hand/forearm BONES
    /// after animation (re-posed by the Animator every frame, so nothing accumulates); the drawn arrow is a separate runtime copy
    /// (the authored Held_HandArrow is only shown/hidden).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1650)]
    public sealed class ASHWeaponAim : MonoBehaviour
    {
        public enum Kind { None, Rifle, Pistol, Bow }

        [Header("Aim")]
        [Range(1f, 60f)] public float aimRotationSpeed = 18f;
        [Range(0f, 1f)] public float itemAlignWeight = 1f;
        public Vector3 gunAimEuler = Vector3.zero;
        public Vector3 bowAimEuler = Vector3.zero;
        [Range(-30f, 30f)] public float bowTiltDeg = 8f;

        [Header("Guns")]
        public bool allowHipFire = true;
        [Range(10f, 600f)] public float range = 150f;
        [Range(0f, 50f)] public float hitImpulse = 4f;
        [Range(0.05f, 3f)] public float rifleCooldown = 0.9f;
        [Range(0.05f, 3f)] public float pistolCooldown = 0.3f;
        [Range(0f, 10f)] public float rifleCameraKick = 2.4f;
        [Range(0f, 10f)] public float pistolCameraKick = 1.3f;
        [Range(0f, 3f)] public float cameraKickYawJitter = 0.5f;
        [Tooltip("Recoil: hand pitch in degrees (applied to the hand bone, the weapon follows).")]
        [Range(0f, 30f)] public float weaponKickPitch = 9f;
        [Tooltip("Recoil share applied to the forearm bone (lifts the hand back).")]
        [Range(0f, 1f)] public float forearmKick = 0.4f;
        [Range(1f, 40f)] public float weaponKickRecover = 12f;

        [Header("Bow")]
        [Range(0.1f, 4f)] public float drawSeconds = 0.9f;
        [Tooltip("How far the right (string) hand pulls back from the bow hand at full draw, metres along the aim.")]
        [Range(0.1f, 1f)] public float bowDrawLength = 0.62f;
        [Tooltip("Right-arm draw IK weight (0 = clip only).")]
        [Range(0f, 1f)] public float bowDrawIKWeight = 1f;
        [Range(-0.3f, 0.3f)] public float bowDrawRaise = 0.04f;
        [Tooltip("Right elbow hint from the right shoulder, character space (x right, y up, z forward).")]
        public Vector3 bowDrawElbowHint = new Vector3(0.45f, 0.05f, -0.4f);
        [Tooltip("BowAim clip time at no draw / at full draw (held there until RMB is released).")]
        [Range(0f, 1f)] public float bowAimStartNormalized = 0f;
        [Range(0f, 1f)] public float bowAimHoldNormalized = 0.95f;
        [Range(0f, 1f)] public float minDraw01 = 0.2f;
        [Range(1f, 120f)] public float arrowSpeedMin = 16f;
        [Range(1f, 150f)] public float arrowSpeedMax = 55f;
        [Range(0f, 4f)] public float arrowGravityScale = 1f;
        [Range(0f, 2f)] public float arrowDrag = 0.02f;
        [Range(0f, 900f)] public float arrowStuckLifeSeconds = 120f;
        public bool recoverArrows = true;
        [Range(0f, 1f)] public float arrowRecoverChance = 0.85f;
        [Range(1, 200)] public int maxStuckArrows = 30;
        [Tooltip("Arrow visual: nock at the origin, tip along +Z (built by Survival Pack tools).")]
        public GameObject arrowPrefab;
        public bool flipArrowVisual;
        [Tooltip("Fallback only (no pre-placed Held_HandArrow): arrow rest pose relative to the right hand bone.")]
        public Vector3 arrowIdleOffset = new Vector3(0f, 0.02f, 0.03f);
        public Vector3 arrowIdleEuler = new Vector3(0f, 0f, 0f);
        [Range(0f, 1f)] public float bowShotHoldSeconds = 0.35f;

        public bool Aiming { get; private set; }
        public float Draw01 { get; private set; }
        public Kind Current { get; private set; }
        public int Shots { get; private set; }
        public RaycastHit? LastHit { get; private set; }
        /// <summary>The arrow currently shown in the right hand: the runtime nock arrow while drawing, else the authored rest arrow.</summary>
        public GameObject HandArrow => _nockArrow && _nockArrow.activeSelf ? _nockArrow : _restArrow;
        public GameObject RestArrow => _restArrow;
        public bool RestArrowPlaced => _restPlaced;
        public float KickAmount => _kick;

        Animator _anim; ASHEquipment _eq; ASHInput _in; ASHInventory _inv; vThirdPersonController _cc; ASHActionLayer _layer; Camera _cam;
        GameObject _restArrow, _nockArrow; bool _restPlaced, _arrowShown, _warnedArrow; Vector3 _aimDir = Vector3.forward;
        float _kick, _nextFire, _shotUntil;
        vThirdPersonMotor.LocomotionType _sLoco; bool _sStrafe, _sRotCam; float _sRotSpeed; bool _saved;

        void Awake()
        {
            _anim = GetComponent<Animator>(); _eq = GetComponentInParent<ASHEquipment>(); _in = GetComponentInParent<ASHInput>();
            _inv = GetComponentInParent<ASHInventory>(); _cc = GetComponentInParent<vThirdPersonController>();
            _layer = GetComponent<ASHActionLayer>(); if (!_layer) _layer = gameObject.AddComponent<ASHActionLayer>();
        }

        Transform Root => _cc ? _cc.transform : transform;
        Camera Cam { get { if (!_cam) _cam = Camera.main; return _cam; } }

        public static Kind KindOf(ASHItemDefinition it)
        {
            if (it == null || it.ammoItem == null || !it.IsWeapon) return Kind.None;
            if (it.ammoItem.itemId != null && it.ammoItem.itemId.ToLowerInvariant().Contains("arrow")) return Kind.Bow;
            return it.gripStyle == ASHGripStyle.PistolGrip ? Kind.Pistol : Kind.Rifle;
        }

        void Update()
        {
            var kindNow = _eq ? KindOf(_eq.Main) : Kind.None;
            if (kindNow != Current) _nextFire = 0f; // cooldown is per weapon
            Current = kindNow;
            bool want = Current != Kind.None && _in && _in.SecondaryHeld && !ASHUIState.BlocksGameplay;
            if (want != Aiming) SetAim(want);
            Draw01 = Aiming && Current == Kind.Bow && HasArrows ? Mathf.Min(1f, Draw01 + Time.deltaTime / Mathf.Max(0.05f, drawSeconds)) : 0f;

            if (Time.time < _shotUntil) { }
            else if (Aiming && Current == Kind.Bow) _layer.PlayAt("BowAim", Mathf.Lerp(bowAimStartNormalized, bowAimHoldNormalized, Draw01)); // holds full draw
            else if (Aiming) _layer.Play(Current == Kind.Pistol ? "PistolAim" : "RifleAim", 0.2f);
            else if (_layer.State == "BowAim" || _layer.State == "RifleAim" || _layer.State == "PistolAim" || _layer.State == "BowShot" || _layer.State == "RifleShot" || _layer.State == "PistolShot") _layer.Release();

            _kick = Mathf.Lerp(_kick, 0f, 1f - Mathf.Exp(-weaponKickRecover * Time.deltaTime));
            UpdateHandArrow();
        }

        bool HasArrows => _inv && _eq && _eq.Main && _eq.Main.ammoItem && _inv.Count(_eq.Main.ammoItem) > 0;

        void SetAim(bool on)
        {
            Aiming = on;
            if (!_cc) return;
            if (on)
            {
                _sLoco = _cc.locomotionType; _sStrafe = _cc.isStrafing; _sRotCam = _cc.strafeSpeed.rotateWithCamera; _sRotSpeed = _cc.strafeSpeed.rotationSpeed; _saved = true;
                _cc.locomotionType = vThirdPersonMotor.LocomotionType.FreeWithStrafe; _cc.isStrafing = true;
                _cc.strafeSpeed.rotateWithCamera = true; _cc.strafeSpeed.rotationSpeed = aimRotationSpeed;
            }
            else if (_saved)
            {
                _cc.locomotionType = _sLoco; _cc.isStrafing = _sStrafe; _cc.strafeSpeed.rotateWithCamera = _sRotCam; _cc.strafeSpeed.rotationSpeed = _sRotSpeed; _saved = false;
            }
        }

        void OnDisable()
        {
            if (Aiming) SetAim(false);
            if (_nockArrow) Destroy(_nockArrow);
            if (_restArrow) { if (_restPlaced) _restArrow.SetActive(false); else Destroy(_restArrow); }
            _restArrow = null; _restPlaced = false;
        }

        /// <summary>Crosshair target: camera ray against the world (player ignored).</summary>
        public Vector3 AimPoint(out RaycastHit? hit)
        {
            hit = null; var cam = Cam;
            var ray = cam ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : new Ray(Root.position + Vector3.up * 1.5f, Root.forward);
            var hits = Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits) { if (h.collider.transform.IsChildOf(Root)) continue; if (h.distance < best) { best = h.distance; hit = h; } }
            return hit.HasValue ? hit.Value.point : ray.GetPoint(range);
        }

        /// <summary>LMB with a held weapon (called by ASHEquipment).</summary>
        public bool Fire()
        {
            if (_eq == null || _eq.Main == null) return false;
            var kind = KindOf(_eq.Main);
            if (kind == Kind.None) return _eq.UseMain();
            if (kind == Kind.Bow) return ReleaseArrow();
            if (Time.time < _nextFire) return false;
            if (!allowHipFire && !Aiming) { _eq.Say("Hold RMB to aim"); return false; }
            if (!_eq.UseMain()) return false; // spends one round, muzzle flash; "Click" when empty
            _nextFire = Time.time + (kind == Kind.Pistol ? pistolCooldown : rifleCooldown);
            var target = AimPoint(out var hit); LastHit = hit; Shots++;
            if (hit.HasValue && hit.Value.rigidbody && !hit.Value.rigidbody.isKinematic)
                hit.Value.rigidbody.AddForceAtPosition((target - Root.position).normalized * hitImpulse, hit.Value.point, ForceMode.Impulse);
            float k = kind == Kind.Pistol ? pistolCameraKick : rifleCameraKick;
            ASHCameraKick.Kick(k, Random.Range(-cameraKickYawJitter, cameraKickYawJitter));
            _kick = 1f;
            _layer.Restart(kind == Kind.Pistol ? "PistolShot" : "RifleShot"); _shotUntil = Time.time + 0.18f;
            return true;
        }

        bool ReleaseArrow()
        {
            var ammo = _eq.Main.ammoItem;
            if (!Aiming) { _eq.Say("Hold RMB to draw the bow"); return false; }
            if (!HasArrows) { _eq.Say("No arrows"); return false; }
            if (Draw01 < minDraw01) return false;
            if (!_inv.Remove(ammo, 1)) return false;
            var ha = HandArrow; var origin = ha ? ha.transform.position : _anim.GetBoneTransform(HumanBodyBones.RightHand).position;
            var target = AimPoint(out var hit); LastHit = hit;
            var dir = (target - origin); if (dir.sqrMagnitude < 0.01f) dir = Cam ? Cam.transform.forward : Root.forward; dir.Normalize();
            float speed = Mathf.Lerp(arrowSpeedMin, arrowSpeedMax, Draw01);
            var go = MakeArrowVisual("ASH_Arrow");
            go.transform.position = origin + dir * 0.2f; go.transform.rotation = Quaternion.LookRotation(dir);
            go.AddComponent<ASHArrow>().Launch(dir * speed, Root, arrowGravityScale, arrowDrag, arrowStuckLifeSeconds, ammo, recoverArrows, arrowRecoverChance, maxStuckArrows);
            Shots++; Draw01 = 0f; _kick = 0.5f;
            _layer.Restart("BowShot"); _shotUntil = Time.time + bowShotHoldSeconds;
            return true;
        }

        GameObject MakeArrowVisual(string name)
        {
            GameObject go;
            if (arrowPrefab) { go = Instantiate(arrowPrefab); }
            else
            {
                go = new GameObject(); var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder); shaft.transform.SetParent(go.transform, false);
                shaft.transform.localScale = new Vector3(0.012f, 0.38f, 0.012f); shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); shaft.transform.localPosition = new Vector3(0, 0, 0.38f);
            }
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            if (flipArrowVisual && go.transform.childCount > 0) { var ch = go.transform.GetChild(0); ch.localRotation = Quaternion.Euler(0, 180f, 0) * ch.localRotation; ch.localPosition = Quaternion.Euler(0, 180f, 0) * ch.localPosition; }
            return go;
        }

        void UpdateHandArrow()
        {
            _arrowShown = Current == Kind.Bow && HasArrows && !(Time.time < _shotUntil && Draw01 < 0.05f);
            var rh = _anim ? _anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (!_restArrow && _eq) { var placed = _eq.Placed(ASHEquipment.HandArrowName); if (placed) { _restArrow = placed; _restPlaced = true; } }
            if (_arrowShown && !_restArrow && rh)
            {
                if (!_warnedArrow) { _warnedArrow = true; Debug.LogWarning("[ASH] WeaponAim: no pre-placed Held_HandArrow on the player; spawning one. Run ASH/Build/Held Items On Player, then place it by hand."); }
                _restArrow = MakeArrowVisual("ASH_HandArrow"); _restArrow.transform.SetParent(rh, false);
                _restArrow.transform.localPosition = arrowIdleOffset; _restArrow.transform.localRotation = Quaternion.Euler(arrowIdleEuler); _restPlaced = false;
            }
            if (!_arrowShown) { if (_restArrow && _restArrow.activeSelf) _restArrow.SetActive(false); if (_nockArrow) _nockArrow.SetActive(false); }
        }

        /// <summary>Right arm draws the string: hand goes from the bow hand back along the aim by bowDrawLength x draw.</summary>
        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || !_anim || !_anim.isHuman || !Aiming || Current != Kind.Bow) return;
            float w = (_layer && _layer.Valid ? _layer.Weight : 1f) * bowDrawIKWeight;
            if (w <= 0.001f) return;
            var bowHand = _anim.GetBoneTransform(HumanBodyBones.LeftHand); var sh = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (!bowHand || !sh) return;
            var aim = Cam ? Cam.transform.forward : Root.forward;
            var goal = bowHand.position - aim * (bowDrawLength * Mathf.Lerp(0.15f, 1f, Draw01)) + Vector3.up * bowDrawRaise;
            _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, w); _anim.SetIKPosition(AvatarIKGoal.RightHand, goal);
            _anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, w); _anim.SetIKHintPosition(AvatarIKHint.RightElbow, sh.position + Root.TransformDirection(bowDrawElbowHint));
        }

        void LateUpdate()
        {
            var v = _eq ? _eq.MainVisual : null;
            float w = _layer && _layer.Valid ? _layer.Weight : (Aiming ? 1f : 0f);
            if (v && v.activeInHierarchy && _anim && _anim.isHuman)
            {
                bool left = ASHEquipment.InLeftHand(_eq.Main);
                var hand = _anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var fore = _anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                if (hand && v.transform.IsChildOf(hand))
                {
                    // recoil: forearm then hand pitch (bones only; the Animator re-poses them next frame)
                    if (_kick > 0.001f)
                    {
                        var axis = Root.right;
                        if (fore && forearmKick > 0f) fore.rotation = Quaternion.AngleAxis(-weaponKickPitch * forearmKick * _kick, axis) * fore.rotation;
                        hand.rotation = Quaternion.AngleAxis(-weaponKickPitch * _kick, axis) * hand.rotation;
                    }
                    // aim alignment: turn the hand so the hand-placed weapon lines up on the crosshair (delta on the authored pose)
                    float k = (Aiming ? w : 0f) * itemAlignWeight;
                    if (k > 0.001f)
                    {
                        var cur = v.transform.rotation; var pos = v.transform.position;
                        _aimDir = (AimPoint(out _) - pos).normalized;
                        Quaternion desired = Current == Kind.Bow
                            ? Quaternion.LookRotation(Quaternion.AngleAxis(-bowTiltDeg, Vector3.Cross(Vector3.up, _aimDir).normalized) * Vector3.up, _aimDir) * Quaternion.Euler(bowAimEuler)
                            : Quaternion.LookRotation(_aimDir, Vector3.up) * Quaternion.Euler(gunAimEuler);
                        hand.rotation = Quaternion.Slerp(Quaternion.identity, desired * Quaternion.Inverse(cur), k) * hand.rotation;
                    }
                }
            }
            // arrow: authored rest arrow while idle; while drawing a runtime copy rides the right (string) hand, tip at the bow
            if (_arrowShown && _restArrow)
            {
                float n = Aiming && v ? Mathf.Clamp01(w) : 0f;
                var rh = _anim ? _anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
                if (n > 0.01f && rh)
                {
                    if (!_nockArrow) _nockArrow = MakeArrowVisual("ASH_NockArrow");
                    var rest = _restArrow.transform;
                    if (_restArrow.activeSelf) _restArrow.SetActive(false);
                    _nockArrow.SetActive(true);
                    var bow = v.transform.position;
                    var nockRot = Quaternion.LookRotation((bow - rh.position).sqrMagnitude > 1e-4f ? bow - rh.position : Root.forward, Vector3.up);
                    _nockArrow.transform.localScale = rest.lossyScale;
                    _nockArrow.transform.SetPositionAndRotation(Vector3.Lerp(rest.position, rh.position, n), Quaternion.Slerp(rest.rotation, nockRot, n));
                }
                else
                {
                    if (_nockArrow && _nockArrow.activeSelf) _nockArrow.SetActive(false);
                    if (!_restArrow.activeSelf) _restArrow.SetActive(true);
                }
            }
        }
    }
}
