using ASH.Core;
using ASH.Inventory;
using ASH.Player;
using Invector;
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
    ///   bowDrawLength) and HOLDS full draw until RMB is released. ONLY an LMB click while aiming looses the arrow (once drawn past
    ///   minDraw01); letting go of RMB just lowers the bow, never fires. While drawing, the bow is turned string-toward-the-archer
    ///   (side found from the bow mesh), the runtime nock arrow sits nock-on-string at the right hand and rests on the riser pointing
    ///   along the aim, the bow string is pulled back to the right hand (mesh deform of the string vertices on a runtime mesh copy),
    ///   and the arrow is launched from the arrow rest on the bow toward the crosshair hit. Launch speed scales with draw time; the arrow flies under gravity (ASHArrow), sticks and can be recovered.
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
        [Tooltip("Extra trim on the bow aim pose (degrees, bow local). The base pose comes from the bow mesh: limbs vertical, string toward the archer.")]
        public Vector3 bowAimTrimEuler = Vector3.zero;
        [Range(-30f, 30f)] public float bowTiltDeg = 8f;

        [Header("Bow arm (left-arm IK while aiming)")]
        [Tooltip("0 = animation only. Pulls the bow hand in toward the chest centre line, in front of the chest, on the aim line.")]
        [Range(0f, 1f)] public float bowArmIKWeight = 1f;
        [Tooltip("Bow hand distance in front of the shoulders' midpoint along the aim (smaller = more elbow bend).")]
        [Range(0.2f, 0.8f)] public float bowArmReach = 0.5f;
        [Tooltip("Bow hand sideways from the chest centre line, metres (negative = left).")]
        [Range(-0.4f, 0.4f)] public float bowArmLateral = -0.06f;
        [Range(-0.3f, 0.3f)] public float bowArmHeight = 0.1f;
        [Tooltip("Left elbow pole from the left shoulder, character space (x right, y up, z forward): out and down gives a natural bend.")]
        public Vector3 bowArmElbowHint = new Vector3(-0.5f, -0.4f, 0.05f);
        [Range(1f, 30f)] public float armIKBlendSpeed = 8f;

        [Header("Guns: support hand (LeftHandIK child on each held gun, placed in edit mode)")]
        [Range(0f, 1f)] public float gunLeftHandAimWeight = 1f;
        [Tooltip("Support hand on the gun while just holding it (not aiming).")]
        [Range(0f, 1f)] public float gunLeftHandIdleWeight = 0f;
        public Vector3 gunLeftElbowHint = new Vector3(-0.4f, -0.5f, 0f);
        [Tooltip("Rifle/pistol held out further than the left arm reaches: pull it back along the aim line toward the shoulder until the left hand reaches LeftHandIK (0 = off).")]
        [Range(0f, 1f)] public float gunShoulderPull = 1f;
        [Tooltip("Most the gun is pulled back toward the shoulder, metres.")]
        [Range(0f, 0.6f)] public float gunMaxPull = 0.4f;
        [Tooltip("Right elbow direction (root space) while the gun is pulled back.")]
        public Vector3 gunRightElbowHint = new Vector3(0.4f, -0.5f, -0.1f);

        [Header("Aim camera (over the right shoulder while aiming a weapon or a light, like Genesis Aiming)")]
        public bool aimCamera = true;
        [Tooltip("Camera offset to the right of the player while aiming, metres (Genesis Aiming state uses ~0.3-0.4).")]
        [Range(-1f, 1.5f)] public float aimCameraRight = 0.38f;
        [Range(0.5f, 2.5f)] public float aimCameraHeight = 1.55f;
        [Range(0.4f, 6f)] public float aimCameraDistance = 1.6f;
        [Range(20f, 90f)] public float aimCameraFov = 52f;
        [Tooltip("Blend speed into and out of the aim camera (higher = faster).")]
        [Range(0.5f, 30f)] public float aimCameraBlendSpeed = 8f;
        public const string AimCameraState = "ASH_Aim";

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
        public Vector3 bowDrawElbowHint = new Vector3(0.45f, 0.25f, -0.4f);
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
        [Tooltip("Arrow rest height above the bow-hand grip, metres along the bow.")]
        [Range(-0.1f, 0.2f)] public float arrowRestAboveGrip = 0.045f;
        [Tooltip("Arrow rest offset to the archer's left of the riser centre (right-handed draw), metres.")]
        [Range(-0.08f, 0.08f)] public float arrowRestSide = 0.02f;
        [Tooltip("Pull the bow string back to the drawing hand (runtime copy of the bow mesh; the authored mesh is never changed).")]
        public bool pullBowString = true;
        [Tooltip("LMB is ignored this long after RMB was let go (no accidental shot while lowering).")]
        [Range(0f, 1f)] public float fireLockAfterAimRelease = 0.2f;

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
        // bow geometry / draw diagnostics (read by the weapon test and the held audit)
        public bool BowGeometryOk => _bg.ok;
        public float BowStringSign => _bg.stringSign;
        public int BowStringVerts => _bg.stringVerts;
        public Vector3 ArrowRestWorld { get; private set; }
        public Vector3 NockWorld { get; private set; }
        public Vector3 FingerWorld { get; private set; }
        public float GunLeftReach { get; private set; }
        /// <summary>How far the gun was pulled back toward the shoulder this frame (m).</summary>
        public float GunPull { get; private set; }
        public float GunLeftArmLength { get; private set; }
        /// <summary>Left hand to its gun LeftHandIK target after the IK this frame (metres; -1 = no target / not applied).</summary>
        public float GunLeftHandError { get; private set; } = -1f;
        public float GunLeftHandWeight => _gunIK;
        public const string LeftHandIKName = "LeftHandIK";
        public bool CameraAiming => _camAiming;
        /// <summary>Live camera state values (current, blended).</summary>
        public float CameraRightNow => _tpIn && _tpIn.tpCamera && _tpIn.tpCamera.currentState != null ? _tpIn.tpCamera.currentState.right : 0f;
        public string CameraTargetState => _tpIn && _tpIn.tpCamera && _tpIn.tpCamera.lerpState != null ? _tpIn.tpCamera.lerpState.Name : "";
        public Vector3 LastArrowSpawn { get; private set; }
        public float StringPullMetres { get; private set; }
        /// <summary>World direction from the riser to the string (bow local +X x sign).</summary>
        public Vector3 BowStringDirWorld { get { var v = _eq ? _eq.MainVisual : null; return v ? v.transform.TransformDirection(Vector3.right * _bg.stringSign).normalized : Vector3.zero; } }

        Animator _anim; ASHEquipment _eq; ASHInput _in; ASHInventory _inv; vThirdPersonController _cc; ASHActionLayer _layer; Camera _cam;
        GameObject _restArrow, _nockArrow; bool _restPlaced, _arrowShown, _warnedArrow; Vector3 _aimDir = Vector3.forward;
        float _kick, _nextFire, _shotUntil, _aimReleasedAt = -10f, _bowBlend, _bowArm, _gunIK;
        Vector3 _restRootLocal, _nockHandLocal; bool _restValid;
        vThirdPersonInput _tpIn; vThirdPersonCameraState _aimCamState; bool _camAiming; float _savedCamSmooth = -1f, _restoreCamSmoothAt; ASHLightAim _lightAim;

        // bow mesh analysis, in the held bow's local space (done once per bow visual)
        struct BowGeo
        {
            public bool ok; public GameObject src; public float stringSign, riserX, zMin, zMax, gripZ; public int stringVerts;
            public float lineA, lineB; // string line: x = lineA + lineB * z (bow local)
            public System.Collections.Generic.List<StringMesh> meshes;
        }
        sealed class StringMesh { public MeshFilter mf; public Mesh orig, work; public Vector3[] baseV, curV; public int[] idx; public float[] zs; }
        BowGeo _bg;
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
            if (want != Aiming) { SetAim(want); if (!want) _aimReleasedAt = Time.time; }
            Draw01 = Aiming && Current == Kind.Bow && HasArrows ? Mathf.Min(1f, Draw01 + Time.deltaTime / Mathf.Max(0.05f, drawSeconds)) : 0f;

            if (Time.time < _shotUntil) { }
            else if (Aiming && Current == Kind.Bow) _layer.PlayAt("BowAim", Mathf.Lerp(bowAimStartNormalized, bowAimHoldNormalized, Draw01)); // holds full draw
            else if (Aiming) _layer.Play(Current == Kind.Pistol ? "PistolAim" : "RifleAim", 0.2f);
            else if (_layer.State == "BowAim" || _layer.State == "RifleAim" || _layer.State == "PistolAim" || _layer.State == "BowShot" || _layer.State == "RifleShot" || _layer.State == "PistolShot") _layer.Release();

            _kick = Mathf.Lerp(_kick, 0f, 1f - Mathf.Exp(-weaponKickRecover * Time.deltaTime));
            UpdateHandArrow();
            UpdateAimCamera();
        }

        /// <summary>Over-the-shoulder aim camera: a runtime copy of the camera's state list (the Invector asset is never touched)
        /// gets an ASH_Aim state (Default + the aim offsets above), entered while aiming a weapon or a light. The crosshair stays the
        /// screen centre, and shots/arrows aim at the camera-centre ray hit, so they land where it points.</summary>
        void UpdateAimCamera()
        {
            if (!_tpIn) _tpIn = _cc ? _cc.GetComponent<vThirdPersonInput>() : null;
            var cam = _tpIn ? _tpIn.tpCamera : null; if (!cam || !cam.CameraStateList || cam.lerpState == null) return;
            if (_aimCamState == null)
            {
                var list = Instantiate(cam.CameraStateList); list.name = cam.CameraStateList.name + " (ASH runtime)";
                var def = list.tpCameraStates.Find(st => st.Name == "Default") ?? list.tpCameraStates[0];
                _aimCamState = new vThirdPersonCameraState(AimCameraState); JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(def), _aimCamState); _aimCamState.Name = AimCameraState;
                list.tpCameraStates.RemoveAll(st => st.Name == AimCameraState); list.tpCameraStates.Add(_aimCamState);
                cam.ChangeStateList(list);
            }
            _aimCamState.right = aimCameraRight; _aimCamState.height = aimCameraHeight; _aimCamState.defaultDistance = aimCameraDistance;
            _aimCamState.maxDistance = Mathf.Max(aimCameraDistance + 0.5f, _aimCamState.maxDistance); _aimCamState.minDistance = Mathf.Min(0.3f, aimCameraDistance); _aimCamState.fov = aimCameraFov;
            if (!_lightAim) _lightAim = _cc ? _cc.GetComponentInChildren<ASHLightAim>() : GetComponentInParent<ASHLightAim>();
            bool want = aimCamera && (Aiming || (_lightAim && _lightAim.Aiming)) && !ASHUIState.BlocksGameplay;
            if (want != _camAiming)
            {
                _camAiming = want;
                if (_savedCamSmooth < 0f) _savedCamSmooth = cam.smoothBetweenState;
                cam.smoothBetweenState = aimCameraBlendSpeed; _restoreCamSmoothAt = want ? float.MaxValue : Time.time + 3f / Mathf.Max(0.5f, aimCameraBlendSpeed);
                if (want) _tpIn.ChangeCameraState(AimCameraState, true); else _tpIn.ResetCameraState();
            }
            if (!_camAiming && _savedCamSmooth >= 0f && Time.time >= _restoreCamSmoothAt) { cam.smoothBetweenState = _savedCamSmooth; _savedCamSmooth = -1f; }
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
            RestoreString(); _bg = default;
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
            // LMB only, and only while RMB is actually held: never on RMB release, never right after letting go
            if (!Aiming || (_in && !_in.SecondaryHeld)) { _eq.Say("Hold RMB to draw the bow"); return false; }
            if (Time.time - _aimReleasedAt < fireLockAfterAimRelease) return false;
            if (!HasArrows) { _eq.Say("No arrows"); return false; }
            if (Draw01 < minDraw01) return false;
            if (!_inv.Remove(ammo, 1)) return false;
            // launched from the arrow rest on the bow (where the nocked arrow crosses the riser), toward the crosshair hit
            var origin = _restValid ? ArrowRestWorld : (_eq.MainVisual ? _eq.MainVisual.transform.position : _anim.GetBoneTransform(HumanBodyBones.LeftHand).position);
            var target = AimPoint(out var hit); LastHit = hit;
            var dir = (target - origin); if (dir.sqrMagnitude < 0.01f) dir = Cam ? Cam.transform.forward : Root.forward; dir.Normalize();
            float speed = Mathf.Lerp(arrowSpeedMin, arrowSpeedMax, Draw01);
            var go = MakeArrowVisual("ASH_Arrow");
            go.transform.position = origin; go.transform.rotation = Quaternion.LookRotation(dir); LastArrowSpawn = origin;
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
            if (layerIndex != 0 || !_anim || !_anim.isHuman || Current != Kind.Bow || !(Aiming || _bowBlend > 0.01f)) return;
            float w = (_layer && _layer.Valid ? _layer.Weight : 1f) * bowDrawIKWeight;
            if (w <= 0.001f) return;
            var bowHand = _anim.GetBoneTransform(HumanBodyBones.LeftHand); var sh = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var rhb = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (!bowHand || !sh || !rhb) return;
            var aim = _aimDir.sqrMagnitude > 0.5f ? _aimDir : (Cam ? Cam.transform.forward : Root.forward);
            var rest = _restValid ? Root.TransformPoint(_restRootLocal) : bowHand.position;
            // the string fingers (not the wrist) land on the aim line behind the arrow rest
            var nockGoal = rest - aim * (bowDrawLength * Mathf.Lerp(0.15f, 1f, Draw01)) + Vector3.up * bowDrawRaise;
            var goal = nockGoal - rhb.rotation * _nockHandLocal;
            _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, w); _anim.SetIKPosition(AvatarIKGoal.RightHand, goal);
            _anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, w); _anim.SetIKHintPosition(AvatarIKHint.RightElbow, sh.position + Root.TransformDirection(bowDrawElbowHint));
        }

        /// <summary>Reads the bow mesh once: which side the string is on (limb tips sit near the string, the riser far from it),
        /// the riser line, the string line and the string vertices (for the draw pull), and the grip height from the left palm.</summary>
        void EnsureBowGeometry(GameObject v, Transform bowHand)
        {
            if (_bg.src == v && _bg.meshes != null) return;
            RestoreString(); _bg = new BowGeo { src = v, stringSign = 1f, meshes = new System.Collections.Generic.List<StringMesh>() };
            var vt = v.transform; var pts = new System.Collections.Generic.List<Vector3>();
            var mfs = System.Array.FindAll(v.GetComponentsInChildren<MeshFilter>(true), f => f.name.IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) < 0); // the pack bow carries its own arrow mesh
            foreach (var mf in mfs) { var m = mf.sharedMesh; if (!Readable(m)) continue; var vs = m.vertices; foreach (var p in vs) pts.Add(vt.InverseTransformPoint(mf.transform.TransformPoint(p))); }
            if (pts.Count < 20) { Fallback(v, bowHand); return; }
            float zMin = float.MaxValue, zMax = float.MinValue; foreach (var p in pts) { zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z); }
            float half = (zMax - zMin) * 0.5f, zc = (zMax + zMin) * 0.5f;
            double tipX = 0, midX = 0; int nt = 0, nm = 0;
            foreach (var p in pts) { float u = Mathf.Abs(p.z - zc) / Mathf.Max(1e-4f, half); if (u > 0.9f) { tipX += p.x; nt++; } else if (u < 0.08f) { midX += p.x; nm++; } }
            if (nt == 0 || nm == 0) { Fallback(v, bowHand); return; }
            float sign = tipX / nt >= midX / nm ? 1f : -1f;
            // string: the string-side vertices of the middle 60% (the pack string is a strip with vertices only at the tips and the
            // nocking point, so its middle vertices are the ones that move); limbs there sit well inside the string line
            float smax = float.MinValue; foreach (var p in pts) if (Mathf.Abs(p.z - zc) / half < 0.6f) smax = Mathf.Max(smax, p.x * sign);
            float a = smax, b = 0f;
            // riser: mean of the middle slice without the string
            double rx = 0; int rn = 0; foreach (var p in pts) { if (Mathf.Abs(p.z - zc) / half < 0.08f && p.x * sign < smax - 0.06f) { rx += p.x; rn++; } }
            _bg.stringSign = sign; _bg.lineA = a; _bg.lineB = b; _bg.zMin = zMin; _bg.zMax = zMax; _bg.riserX = rn > 0 ? (float)(rx / rn) : (float)(midX / nm);
            // grip height: left palm centre on the bow
            var mid = _anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            var palm = mid ? Vector3.Lerp(bowHand.position, mid.position, 0.6f) : bowHand.position;
            _bg.gripZ = Mathf.Clamp(vt.InverseTransformPoint(palm).z, zMin + half * 0.5f, zMax - half * 0.5f);
            // string vertices: within a few mm of the string line, along the whole string
            foreach (var mf in mfs)
            {
                var m = mf.sharedMesh; if (!Readable(m)) continue; var vs = m.vertices;
                var idx = new System.Collections.Generic.List<int>(); var zs = new System.Collections.Generic.List<float>();
                for (int i = 0; i < vs.Length; i++)
                {
                    var p = vt.InverseTransformPoint(mf.transform.TransformPoint(vs[i]));
                    if (Mathf.Abs(p.z - zc) / half > 0.6f) continue;
                    if (p.x * sign > smax - 0.025f) { idx.Add(i); zs.Add(p.z); }
                }
                if (idx.Count < 3) continue;
                var sm = new StringMesh { mf = mf, orig = m, baseV = vs, curV = (Vector3[])vs.Clone(), idx = idx.ToArray(), zs = zs.ToArray() };
                _bg.meshes.Add(sm); _bg.stringVerts += idx.Count;
            }
            _bg.ok = true;
        }

        static bool Readable(Mesh m)
        {
#if UNITY_EDITOR
            return m;
#else
            return m && m.isReadable;
#endif
        }

        /// <summary>No readable mesh: Survival Pack wooden bow layout (string on +X, riser at x -0.16, limbs along Z), no string pull.</summary>
        void Fallback(GameObject v, Transform bowHand)
        {
            _bg.ok = true; _bg.stringSign = 1f; _bg.riserX = -0.16f; _bg.lineA = 0.15f; _bg.lineB = 0f; _bg.zMin = -0.74f; _bg.zMax = 0.74f;
            var mid = _anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _bg.gripZ = v.transform.InverseTransformPoint(mid ? Vector3.Lerp(bowHand.position, mid.position, 0.6f) : bowHand.position).z;
        }

        void RestoreString()
        {
            if (_bg.meshes == null) return;
            foreach (var sm in _bg.meshes) { if (sm.mf && sm.work && sm.mf.sharedMesh == sm.work) sm.mf.sharedMesh = sm.orig; if (sm.work) Destroy(sm.work); sm.work = null; }
            StringPullMetres = 0f;
        }

        /// <summary>Nock arrow on the string at the drawing fingers, resting on the riser, pointing along the aim; the string follows the fingers.</summary>
        void UpdateNockAndString(GameObject v, float w)
        {
            bool bowOut = Current == Kind.Bow && v && v.activeInHierarchy;
            _bowBlend = bowOut && (Aiming || _layer == null || _layer.State == "BowAim") ? Mathf.Clamp01(w) : 0f;
            var rh = _anim ? _anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            var rmid = _anim ? _anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal) : null;
            if (rh) _nockHandLocal = rmid ? rh.InverseTransformPoint(Vector3.Lerp(rh.position, rmid.position, 1.15f)) : new Vector3(0f, 0.09f, 0f);
            if (bowOut && _bg.src == v && _bg.ok)
            {
                var vt = v.transform; float zr = _bg.gripZ + arrowRestAboveGrip;
                ArrowRestWorld = vt.TransformPoint(new Vector3(_bg.riserX, 0f, zr)) - Root.right * arrowRestSide;
                _restRootLocal = Root.InverseTransformPoint(ArrowRestWorld); _restValid = true;
            }
            else if (!bowOut) _restValid = false;
            if (rh) FingerWorld = rh.TransformPoint(_nockHandLocal);
            // the nocked arrow lies on the aim line through the arrow rest; its nock is where the string fingers project onto that line
            var aimL = _aimDir.sqrMagnitude > 0.5f ? _aimDir : Root.forward;
            NockWorld = _restValid ? ArrowRestWorld - aimL * Mathf.Max(0.06f, Vector3.Dot(ArrowRestWorld - FingerWorld, aimL)) : FingerWorld;
            float n = _bowBlend;

            // arrow: authored rest arrow while idle; while drawing a runtime copy is nocked on the string and rests on the bow
            if (_arrowShown && _restArrow)
            {
                if (n > 0.01f && rh && _restValid)
                {
                    if (!_nockArrow) _nockArrow = MakeArrowVisual("ASH_NockArrow");
                    var rest = _restArrow.transform;
                    if (_restArrow.activeSelf) _restArrow.SetActive(false);
                    _nockArrow.SetActive(true);
                    var nockRot = Quaternion.LookRotation(aimL, Vector3.up);
                    _nockArrow.transform.localScale = rest.lossyScale;
                    float k = Mathf.SmoothStep(0f, 1f, n);
                    _nockArrow.transform.SetPositionAndRotation(Vector3.Lerp(rest.position, NockWorld, k), Quaternion.Slerp(rest.rotation, nockRot, k));
                }
                else
                {
                    if (_nockArrow && _nockArrow.activeSelf) _nockArrow.SetActive(false);
                    if (!_restArrow.activeSelf) _restArrow.SetActive(true);
                }
            }

            // string pull: V from both string ends to the drawing fingers (runtime mesh copy only)
            if (pullBowString && bowOut && _bg.ok && _bg.meshes != null && _bg.meshes.Count > 0 && n > 0.01f && rh)
            {
                var vt = v.transform; var nockL = vt.InverseTransformPoint(NockWorld);
                float sEnd = _bg.zMax - (_bg.zMax - _bg.zMin) * 0.0075f, sBeg = _bg.zMin + (_bg.zMax - _bg.zMin) * 0.0075f;
                float nz = Mathf.Clamp(nockL.z, sBeg + 0.05f, sEnd - 0.05f);
                var onLine = new Vector3((_bg.lineA + _bg.lineB * nz) * _bg.stringSign, 0f, nz);
                var pull = (nockL - onLine) * n; pull.z = 0f; // pull straight back from the string, the string stays its length visually
                StringPullMetres = pull.magnitude * vt.lossyScale.x;
                foreach (var sm in _bg.meshes)
                {
                    if (!sm.mf) continue;
                    if (!sm.work) { sm.work = Instantiate(sm.orig); sm.work.name = sm.orig.name + " (string draw)"; sm.work.MarkDynamic(); }
                    if (sm.mf.sharedMesh != sm.work) sm.mf.sharedMesh = sm.work;
                    var toMf = sm.mf.transform.worldToLocalMatrix * vt.localToWorldMatrix;
                    for (int j = 0; j < sm.idx.Length; j++)
                    {
                        float z = sm.zs[j]; float f = z >= nz ? (sEnd - z) / Mathf.Max(1e-3f, sEnd - nz) : (z - sBeg) / Mathf.Max(1e-3f, nz - sBeg);
                        sm.curV[sm.idx[j]] = sm.baseV[sm.idx[j]] + toMf.MultiplyVector(pull * Mathf.Clamp01(f));
                    }
                    sm.work.vertices = sm.curV; sm.work.RecalculateBounds();
                }
            }
            else if (_bg.meshes != null)
            {
                foreach (var sm in _bg.meshes) if (sm.mf && sm.work && sm.mf.sharedMesh == sm.work) sm.mf.sharedMesh = sm.orig;
                StringPullMetres = 0f;
            }
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
                // bow arm: left hand in toward the chest centre line on the aim line, elbow slightly bent (before the bow is aligned)
                _bowArm = Mathf.MoveTowards(_bowArm, Current == Kind.Bow && Aiming ? 1f : 0f, armIKBlendSpeed * Time.deltaTime);
                if (Current == Kind.Bow && left && _bowArm > 0.001f && bowArmIKWeight > 0f)
                {
                    var ua = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var ra = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    if (ua && ra && fore && hand)
                    {
                        var aim = _aimDir.sqrMagnitude > 0.5f ? _aimDir : Root.forward;
                        var mid = (ua.position + ra.position) * 0.5f;
                        var goal = mid + aim * bowArmReach + Root.right * bowArmLateral + Vector3.up * bowArmHeight;
                        var keep = hand.rotation;
                        TwoBoneIK(ua, fore, hand, goal, ua.position + Root.TransformDirection(bowArmElbowHint), Mathf.SmoothStep(0f, 1f, _bowArm) * bowArmIKWeight * Mathf.Max(w, Aiming ? 0f : 1f));
                        hand.rotation = keep;
                    }
                }
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
                    float k = ((Aiming || (Current == Kind.Bow && _bowBlend > 0.01f)) ? w : 0f) * itemAlignWeight;
                    if (k > 0.001f)
                    {
                        var cur = v.transform.rotation; var pos = v.transform.position;
                        _aimDir = (AimPoint(out _) - pos).normalized;
                        Quaternion desired;
                        if (Current == Kind.Bow)
                        {
                            EnsureBowGeometry(v, hand);
                            // limbs (bow local Z) vertical with a little cant, string side (local +X * sign) back toward the archer
                            var up = Quaternion.AngleAxis(-bowTiltDeg, Vector3.Cross(Vector3.up, _aimDir).normalized) * Vector3.up;
                            var back = Vector3.ProjectOnPlane(-_aimDir, up).normalized;
                            var xw = back * _bg.stringSign;
                            desired = Quaternion.LookRotation(up, Vector3.Cross(up, xw)) * Quaternion.Euler(bowAimTrimEuler);
                        }
                        else desired = Quaternion.LookRotation(_aimDir, Vector3.up) * Quaternion.Euler(gunAimEuler);
                        hand.rotation = Quaternion.Slerp(Quaternion.identity, desired * Quaternion.Inverse(cur), k) * hand.rotation;
                    }
                }
            }
            DrawHandLate(v, w);
            UpdateGunSupportHand(v, w);
            UpdateNockAndString(v, w);
        }

        /// <summary>Right hand draws the string after the bow arm and bow are posed this frame (analytic, exact): the string fingers land
        /// on the aim line through the arrow rest, bowDrawLength x draw behind it. OnAnimatorIK only pre-poses the arm.</summary>
        void DrawHandLate(GameObject v, float w)
        {
            if (Current != Kind.Bow || !v || !v.activeInHierarchy || !_anim || !_anim.isHuman || !(Aiming || _bowBlend > 0.01f)) return;
            if (_bg.src != v || !_bg.ok) return;
            float k = (_layer && _layer.Valid ? _layer.Weight : 1f) * bowDrawIKWeight;
            if (k <= 0.001f) return;
            var ua = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var la = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var rh = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (!ua || !la || !rh) return;
            var vt = v.transform;
            var rest = vt.TransformPoint(new Vector3(_bg.riserX, 0f, _bg.gripZ + arrowRestAboveGrip)) - Root.right * arrowRestSide;
            var aim = _aimDir.sqrMagnitude > 0.5f ? _aimDir : Root.forward;
            var nockGoal = rest - aim * (bowDrawLength * Mathf.Lerp(0.15f, 1f, Draw01));
            var keep = rh.rotation;
            var off = rh.TransformPoint(_nockHandLocal) - rh.position;   // finger offset at the kept (animated) hand rotation
            TwoBoneIK(ua, la, rh, nockGoal - off, ua.position + Root.TransformDirection(bowDrawElbowHint), k);
            rh.rotation = keep;
        }

        /// <summary>Rifle/pistol: the left hand goes to the gun's LeftHandIK child (wrist pose, placed by hand in edit mode), full
        /// weight while aiming, gunLeftHandIdleWeight while just holding. Runs after recoil and aim alignment so it follows the gun.</summary>
        void UpdateGunSupportHand(GameObject v, float w)
        {
            bool gun = (Current == Kind.Rifle || Current == Kind.Pistol) && v && v.activeInHierarchy && _anim && _anim.isHuman;
            var target = gun ? v.transform.Find(LeftHandIKName) : null;
            float want = target ? Mathf.Lerp(gunLeftHandIdleWeight, gunLeftHandAimWeight, Aiming ? Mathf.Clamp01(w) : 0f) : 0f;
            _gunIK = Mathf.MoveTowards(_gunIK, want, armIKBlendSpeed * Time.deltaTime);
            GunLeftHandError = -1f;
            if (!target || _gunIK <= 0.001f) return;
            var ua = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var la = _anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); var lh = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            if (!ua || !la || !lh) return;
            float k = Mathf.SmoothStep(0f, 1f, _gunIK);
            // gun held out past the support arm's reach: bring it back toward the shoulder along its own aim line (keeps it on the
            // crosshair) just far enough that the left hand reaches LeftHandIK
            GunPull = 0f;
            var rua = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var rla = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm); var rh = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (gunShoulderPull > 0f && rua && rla && rh && !ASHEquipment.InLeftHand(_eq.Main))
            {
                float maxR = (Vector3.Distance(ua.position, la.position) + Vector3.Distance(la.position, lh.position)) * 0.97f;
                var a = _aimDir.sqrMagnitude > 0.5f ? _aimDir : v.transform.forward;
                var rel = target.position - ua.position; float b = Vector3.Dot(rel, a), c = rel.sqrMagnitude - maxR * maxR;
                if (c > 0f)
                {
                    float disc = b * b - c; float d = disc >= 0f ? b - Mathf.Sqrt(disc) : b;
                    d = Mathf.Clamp(d, 0f, gunMaxPull) * gunShoulderPull * k;
                    if (d > 0.001f)
                    {
                        var keep = rh.rotation;
                        TwoBoneIK(rua, rla, rh, rh.position - a * d, rua.position + Root.TransformDirection(gunRightElbowHint), 1f);
                        rh.rotation = keep; GunPull = d;
                    }
                }
            }
            TwoBoneIK(ua, la, lh, target.position, ua.position + Root.TransformDirection(gunLeftElbowHint), k);
            lh.rotation = Quaternion.Slerp(lh.rotation, target.rotation, k);
            GunLeftHandError = Vector3.Distance(lh.position, target.position);
            GunLeftReach = Vector3.Distance(ua.position, target.position); GunLeftArmLength = Vector3.Distance(ua.position, la.position) + Vector3.Distance(la.position, lh.position);
        }

        /// <summary>Analytic two-bone IK on the bones (after animation): moves c (hand) toward target, elbow bent toward the hint.</summary>
        static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float weight)
        {
            if (weight <= 0f) return;
            Vector3 ap = a.position; float la = Vector3.Distance(ap, b.position), lb = Vector3.Distance(b.position, c.position);
            var t = Vector3.Lerp(c.position, target, weight);
            var d = t - ap; float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.999f);
            var dir = d.sqrMagnitude > 1e-8f ? d.normalized : (c.position - ap).normalized; t = ap + dir * dist;
            float cosA = Mathf.Clamp((la * la + dist * dist - lb * lb) / (2f * la * dist), -1f, 1f); float ang = Mathf.Acos(cosA) * Mathf.Rad2Deg;
            var pole = Vector3.ProjectOnPlane(hint - ap, dir); if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(b.position - ap, dir);
            var axis = Vector3.Cross(dir, pole).normalized; if (axis.sqrMagnitude < 0.5f) return;
            var e1 = ap + (Quaternion.AngleAxis(ang, axis) * dir) * la; var e2 = ap + (Quaternion.AngleAxis(-ang, axis) * dir) * la;
            var elbow = (e1 - hint).sqrMagnitude <= (e2 - hint).sqrMagnitude ? e1 : e2;
            a.rotation = Quaternion.FromToRotation(b.position - ap, elbow - ap) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, t - b.position) * b.rotation;
        }
    }
}
