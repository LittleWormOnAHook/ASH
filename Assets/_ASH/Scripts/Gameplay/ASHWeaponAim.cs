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
    ///   ASH_Action layer plays the aim pose (Invector Aiming@Rifle / Aiming@Pistol / Bow_Aiming mirrored).
    /// - Rifle/pistol: LMB fires (hip fire optional): one round from the pack, camera ray hit (nudges rigidbodies),
    ///   muzzle flash, Shot_* pose, camera kick + weapon kick.
    /// - Bow (right hand) + arrow (left hand): RMB brings the hands together (nock and draw); LMB releases once drawn past
    ///   minDraw01. Launch speed scales with draw time; the arrow flies under gravity (ASHArrow), sticks and can be recovered.
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
        [Range(0f, 0.3f)] public float weaponKickBack = 0.06f;
        [Range(0f, 30f)] public float weaponKickPitch = 9f;
        [Range(1f, 40f)] public float weaponKickRecover = 12f;

        [Header("Bow")]
        [Range(0.1f, 4f)] public float drawSeconds = 0.9f;
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
        [Tooltip("Arrow in the left hand while not aiming, relative to the hand bone.")]
        public Vector3 arrowIdleOffset = new Vector3(0f, 0.02f, 0.03f);
        public Vector3 arrowIdleEuler = new Vector3(0f, 0f, 0f);
        [Range(0f, 1f)] public float bowShotHoldSeconds = 0.35f;

        public bool Aiming { get; private set; }
        public float Draw01 { get; private set; }
        public Kind Current { get; private set; }
        public int Shots { get; private set; }
        public RaycastHit? LastHit { get; private set; }
        public GameObject HandArrow => _handArrow;
        public float KickAmount => _kick;

        Animator _anim; ASHEquipment _eq; ASHInput _in; ASHInventory _inv; vThirdPersonController _cc; ASHActionLayer _layer; Camera _cam;
        GameObject _handArrow; GameObject _mainVisual; Quaternion _mainLocalRot; Vector3 _mainLocalPos;
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
            else if (Aiming) _layer.Play(Current == Kind.Bow ? "BowAim" : Current == Kind.Pistol ? "PistolAim" : "RifleAim", 0.2f);
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

        void OnDisable() { if (Aiming) SetAim(false); if (_handArrow) Destroy(_handArrow); }

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
            var origin = _handArrow ? _handArrow.transform.position : _anim.GetBoneTransform(HumanBodyBones.LeftHand).position;
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
            bool show = Current == Kind.Bow && HasArrows && !(Time.time < _shotUntil && Current == Kind.Bow && Draw01 < 0.05f);
            var lh = _anim ? _anim.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            if (!show || !lh) { if (_handArrow) _handArrow.SetActive(false); return; }
            if (!_handArrow) { _handArrow = MakeArrowVisual("ASH_HandArrow"); _handArrow.transform.SetParent(lh, false); }
            _handArrow.SetActive(true);
        }

        void LateUpdate()
        {
            var v = _eq ? _eq.MainVisual : null;
            if (v != _mainVisual) { _mainVisual = v; if (v) { _mainLocalRot = v.transform.localRotation; _mainLocalPos = v.transform.localPosition; } }
            float w = _layer && _layer.Valid ? _layer.Weight : (Aiming ? 1f : 0f);
            if (v && v.transform.parent)
            {
                var t = v.transform; var p = t.parent;
                Quaternion attached = p.rotation * _mainLocalRot; Vector3 attachedPos = p.TransformPoint(_mainLocalPos);
                Quaternion rot = attached;
                float k = (Aiming ? w : 0f) * itemAlignWeight;
                if (k > 0.001f)
                {
                    var aim = (AimPoint(out _) - attachedPos).normalized;
                    Quaternion desired = Current == Kind.Bow
                        ? Quaternion.LookRotation(Quaternion.AngleAxis(-bowTiltDeg, Vector3.Cross(Vector3.up, aim).normalized) * Vector3.up, aim) * Quaternion.Euler(bowAimEuler)
                        : Quaternion.LookRotation(aim, Vector3.up) * Quaternion.Euler(gunAimEuler);
                    rot = Quaternion.Slerp(attached, desired, k);
                }
                if (_kick > 0.001f) rot = rot * Quaternion.Euler(-weaponKickPitch * _kick, 0f, 0f);
                t.rotation = rot;
                t.position = attachedPos - (rot * Vector3.forward) * (weaponKickBack * _kick);
            }
            // arrow: idle on the left hand; while aiming the nock rides the left (string) hand and the tip points at the bow
            if (_handArrow && _handArrow.activeSelf)
            {
                var lh = _handArrow.transform.parent;
                if (Aiming && v)
                {
                    var bow = v.transform.position;
                    float n = Mathf.Clamp01(w);
                    var idleRot = lh.rotation * Quaternion.Euler(arrowIdleEuler); var idlePos = lh.TransformPoint(arrowIdleOffset);
                    var nockRot = Quaternion.LookRotation((bow - lh.position).sqrMagnitude > 1e-4f ? bow - lh.position : Root.forward, Vector3.up);
                    _handArrow.transform.SetPositionAndRotation(Vector3.Lerp(idlePos, lh.position, n), Quaternion.Slerp(idleRot, nockRot, n));
                }
                else { _handArrow.transform.localPosition = arrowIdleOffset; _handArrow.transform.localRotation = Quaternion.Euler(arrowIdleEuler); }
            }
        }
    }
}
