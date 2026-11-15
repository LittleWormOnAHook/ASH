using System.Collections.Generic;
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
    ///   ASH_Action layer plays the aim pose (Invector Aiming@Rifle / Aiming@Pistol; bow: Mixamo longbow draw / aim-idle / recoil from Genesis).
    /// - Rifle/pistol: LMB fires (hip fire optional): one round from the pack, camera ray hit (nudges rigidbodies),
    ///   muzzle flash, Shot_* pose, camera kick + weapon kick.
    /// - Bow (left hand) + arrow (right hand): RMB plays the archery draw (clip scrubbed by the draw: left arm out, right hand
    ///   to the cheek), then the aim-idle clip loops at full draw until RMB is released (no arm IK). ONLY an LMB click while aiming looses the arrow (once drawn past
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

        [Header("Bow body aim (the archery clip aims to the archer's side: turn the body so its arrow line meets the crosshair)")]
        public bool bowBodyAim = true;
        [Tooltip("Share of the residual pitch put on the spine (0 = arms level, 1 = full pitch to the aim point).")]
        [Range(0f, 1f)] public float bowSpinePitch = 1f;
        [Range(0f, 150f)] public float bowMaxBodyYaw = 120f;
        [Header("Bow arm while aiming (aim-only delta; rest placement untouched)")]
        [Tooltip("Turns the bow arm (shoulder) to the right while aiming (deg): rotates the bow right and moves it right.")]
        [Range(-30f, 30f)] public float bowArmYaw = 6f;
        [Tooltip("Extra turn of the bow hand only (deg, + = right): rotates the bow without moving the arm.")]
        [Range(-30f, 30f)] public float bowHandYaw = 4f;
        [Tooltip("Extra shift of the bow hand to the right (metres), done by the shoulder turn.")]
        [Range(-0.2f, 0.2f)] public float bowArmShiftRight = 0.04f;
        [Range(1f, 20f)] public float bowBodyBlendSpeed = 6f;

        [Header("Head look at the crosshair while aiming (bow, rifle, pistol)")]
        public bool headLookAtAim = true;
        [Tooltip("How much of the turn toward the crosshair is applied (1 = full).")]
        [Range(0f, 1f)] public float headLookWeight = 0.6f;
        [Tooltip("Yaw offset of the look target (deg, + = right, - = left).")]
        [Range(-30f, 30f)] public float headLookYawOffset = 4f;
        [Tooltip("Largest total turn toward the aim point (deg), shared over spine, chest, neck and head.")]
        [Range(0f, 90f)] public float headLookClamp = 35f;
        [Tooltip("Largest up/down part of the turn (deg).")]
        [Range(0f, 60f)] public float headLookPitchClamp = 15f;
        [Tooltip("Shares of the turn done by the spine / chest / neck (the head does the rest). Small spine and chest shares spread the twist so the neck and shoulder skin does not crease.")]
        [Range(0f, 0.5f)] public float spineLookShare = 0.08f;
        [Range(0f, 0.5f)] public float chestLookShare = 0.17f;
        [Range(0f, 0.6f)] public float neckLookShare = 0.3f;
        [Range(1f, 20f)] public float headLookBlendSpeed = 6f;
        public bool headLookOnGuns = true;

        [Header("Arm IK (rifle/pistol support hand)")]
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

        [Header("Rifle: butt seated in the right shoulder pocket while aiming (delta on the edit-mode Held_Rifle placement)")]
        public bool rifleSeatInShoulder = true;
        [Tooltip("Shoulder pocket from the right shoulder joint, character space (x right, y up, z forward; metres). The rifle butt is moved here.")]
        public Vector3 rifleShoulderPocket = new Vector3(-0.05f, -0.03f, 0.05f);
        [Tooltip("Chest turn (deg) toward the right while aiming the rifle (bladed stance) so the support hand reaches the forend.")]
        [Range(0f, 60f)] public float rifleBladeYaw = 35f;
        [Tooltip("Most the rifle may slide back off the shoulder to let the support hand reach (metres).")]
        [Range(0f, 0.2f)] public float rifleMaxPull = 0.05f;
        [Tooltip("If the support hand can't reach LeftHandIK, it grips further back along the forend by up to this much (metres) instead of pulling the rifle off the shoulder.")]
        [Range(0f, 0.4f)] public float rifleGripSlideMax = 0.3f;

        [Header("Aim camera (over the right shoulder while aiming a weapon or a light, like Genesis Aiming)")]
        public bool aimCamera = true;
        [Tooltip("Camera offset to the right of the player while aiming, metres (Genesis Aiming state uses ~0.3-0.4).")]
        [Range(-1f, 1.5f)] public float aimCameraRight = 0.38f;
        [Range(0.5f, 2.5f)] public float aimCameraHeight = 1.55f;
        [Range(0.4f, 6f)] public float aimCameraDistance = 1.6f;
        [Range(20f, 90f)] public float aimCameraFov = 52f;
        [Tooltip("Blend speed into and out of the aim camera (higher = faster).")]
        [Range(0.5f, 30f)] public float aimCameraBlendSpeed = 4f;
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
        [Tooltip("BowDraw clip time at no draw / at full draw; at full draw the BowHold (aim idle) clip loops until RMB is released.")]
        [Range(0f, 1f)] public float bowAimStartNormalized = 0f;
        [Range(0f, 1f)] public float bowAimHoldNormalized = 1f;
        [Tooltip("Cross-fade from the end of the draw into the looping hold, seconds.")]
        [Range(0f, 0.5f)] public float bowHoldBlend = 0.15f;
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
        /// <summary>Body yaw (deg) applied this frame so the archery pose's arrow line points at the aim point.</summary>
        public float BowBodyYaw { get; private set; }
        /// <summary>Angle (deg) between the face direction and the line from the head to the aim point (-1 when not looking).</summary>
        public float HeadLookError { get; private set; } = -1f;
        public float HeadLookBlend => _lookW;
        /// <summary>Face-to-target angle before / turn applied this frame (deg).</summary>
        public float HeadLookRaw { get; private set; } = -1f;
        public float HeadLookTurn { get; private set; }
        /// <summary>Angle (deg) between the pose's arrow line (draw hand to bow hand) and the line to the aim point, after body aim.</summary>
        public float BowArrowLineError { get; private set; } = -1f;
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
        /// <summary>Invector camera target state and current (blended) height, for tests.</summary>
        public string CameraStateNow { get; private set; } = "";
        public float CameraHeightNow { get; private set; }
        /// <summary>How far the gun was pulled back toward the shoulder this frame (m).</summary>
        public float GunPull { get; private set; }
        /// <summary>Rifle aim: distance (m) from the rifle butt to the shoulder pocket after seating (-1 when not aiming the rifle).</summary>
        public float RifleButtToPocket { get; private set; } = -1f;
        /// <summary>Rifle aim: how far (m) the support grip slid back along the forend from LeftHandIK so the hand reaches.</summary>
        public float GunGripSlide { get; private set; }
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
        GameObject _restEndsFor; bool _restEndsOk; Vector3 _restTipL, _restTailL;
        float _bodyW, _bladeW, _lookW; bool _faceAxisOk; Vector3 _faceLocal; GameObject _buttFor; Vector3 _buttLocal;
        float _kick, _nextFire, _shotUntil, _aimReleasedAt = -10f, _bowBlend, _gunIK;
        Vector3 _restRootLocal, _nockHandLocal; bool _restValid;
        vThirdPersonInput _tpIn; vThirdPersonCameraState _aimCamState; ASHItemDefinition _camLastMain; bool _camAiming; float _savedCamSmooth = -1f, _restoreCamSmoothAt; ASHLightAim _lightAim;

        // bow mesh analysis, in the held bow's local space (done once per bow visual)
        struct BowGeo
        {
            public bool ok; public GameObject src; public float stringSign, riserX, zMin, zMax, gripZ; public int stringVerts;
            public float lineA, lineB; // string line: x = lineA + lineB * z (bow local)
            public System.Collections.Generic.List<StringMesh> meshes;
        }
        sealed class StringMesh { public MeshFilter mf; public Mesh orig, work; public Vector3[] baseV, curV; public int[] idx; public float[] zs; }
        BowGeo _bg;

        void Awake()
        {
            _anim = GetComponent<Animator>(); _eq = GetComponentInParent<ASHEquipment>(); _in = GetComponentInParent<ASHInput>();
            _inv = GetComponentInParent<ASHInventory>(); _cc = GetComponentInParent<vThirdPersonController>();
            _layer = GetComponent<ASHActionLayer>(); if (!_layer) _layer = gameObject.AddComponent<ASHActionLayer>();
            if (!GetComponent<ASHPoseGuard>()) gameObject.AddComponent<ASHPoseGuard>();
            if (!GetComponent<ASHHeldRig>()) gameObject.AddComponent<ASHHeldRig>();
            _wakeFrame = Time.frameCount;
        }

        Transform Root => _cc ? _cc.transform : transform;
        Camera Cam { get { if (!_cam) _cam = Camera.main; return _cam; } }

        public static Kind KindOf(ASHItemDefinition it)
        {
            if (it == null || it.ammoItem == null || !it.IsWeapon) return Kind.None;
            if (it.ammoItem.itemId != null && it.ammoItem.itemId.ToLowerInvariant().Contains("arrow")) return Kind.Bow;
            return it.gripStyle == ASHGripStyle.PistolGrip ? Kind.Pistol : Kind.Rifle;
        }

        static bool IsBowState(string s) => s == "BowDraw" || s == "BowHold" || s == "BowShot";

        void Update()
        {
            var kindNow = _eq ? KindOf(_eq.Main) : Kind.None;
            if (kindNow != Current) _nextFire = 0f; // cooldown is per weapon
            Current = kindNow;
            bool want = Current != Kind.None && _in && _in.SecondaryHeld && !ASHUIState.BlocksGameplay;
            if (want != Aiming) { SetAim(want); if (!want) _aimReleasedAt = Time.time; }
            Draw01 = Aiming && Current == Kind.Bow && HasArrows ? Mathf.Min(1f, Draw01 + Time.deltaTime / Mathf.Max(0.05f, drawSeconds)) : 0f;
            if (Draw01 > 0f && !_drawSfx) { _drawSfx = true; var sb = ASHSoundBank.Instance; if (sb) ASHSfx.Play("bowDraw", sb.bowDraw, Root.position + Vector3.up * 1.4f, sb.bowVolume * sb.volume, 1f, 0.04f, 0.6f); }
            else if (Draw01 <= 0f) _drawSfx = false;

            if (Time.time < _shotUntil) { }
            else if (Aiming && Current == Kind.Bow)
            {
                // archery animation: the draw clip is scrubbed by the draw, then the aim-idle clip loops at full draw until RMB is released
                if (Draw01 < 1f || !_layer.Has("BowHold")) _layer.PlayAt("BowDraw", Mathf.Lerp(bowAimStartNormalized, bowAimHoldNormalized, Draw01));
                else _layer.Play("BowHold", bowHoldBlend);
            }
            else if (Aiming) _layer.Play(Current == Kind.Pistol ? "PistolAim" : "RifleAim", 0.2f);
            else if (IsBowState(_layer.State) || _layer.State == "RifleAim" || _layer.State == "PistolAim" || _layer.State == "BowShot" || _layer.State == "RifleShot" || _layer.State == "PistolShot") _layer.Release();

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
                // ASH has one base locomotion camera: Invector's "Strafing" state (higher than Default) must never change the
                // framing, so in our runtime copy it is a clone of Default (Crouch keeps its own values)
                var strafe = list.tpCameraStates.Find(st => st.Name == "Strafing");
                if (strafe != null && strafe != def) { JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(def), strafe); strafe.Name = "Strafing"; }
                cam.ChangeStateList(list);
            }
            _aimCamState.right = aimCameraRight; _aimCamState.height = aimCameraHeight; _aimCamState.defaultDistance = aimCameraDistance;
            _aimCamState.maxDistance = Mathf.Max(aimCameraDistance + 0.5f, _aimCamState.maxDistance); _aimCamState.minDistance = Mathf.Min(0.3f, aimCameraDistance); _aimCamState.fov = aimCameraFov;
            if (!_lightAim) _lightAim = _cc ? _cc.GetComponentInChildren<ASHLightAim>() : GetComponentInParent<ASHLightAim>();
            bool want = aimCamera && (Aiming || (_lightAim && _lightAim.Aiming)) && !ASHUIState.BlocksGameplay;
            var mainNow = _eq ? _eq.Main : null;
            bool switched = mainNow != _camLastMain; _camLastMain = mainNow;
            if ((switched || (!want && _camAiming)) && !want) ResetToBaseCamera(cam);
            if (want != _camAiming)
            {
                _camAiming = want;
                if (_savedCamSmooth < 0f) _savedCamSmooth = cam.smoothBetweenState;
                cam.smoothBetweenState = aimCameraBlendSpeed; _restoreCamSmoothAt = want ? float.MaxValue : Time.time + 3f / Mathf.Max(0.5f, aimCameraBlendSpeed);
                if (want) _tpIn.ChangeCameraState(AimCameraState, true); else _tpIn.ResetCameraState();
            }
            if (!_camAiming && _savedCamSmooth >= 0f && Time.time >= _restoreCamSmoothAt) { cam.smoothBetweenState = _savedCamSmooth; _savedCamSmooth = -1f; }
            // never left on the aim state once nothing aims (absolute states only: Invector's own Default/Strafing/Crouch)
            if (!_camAiming && !_tpIn.changeCameraState && cam.lerpState != null && cam.lerpState.Name == AimCameraState)
                cam.ChangeState(_cc && _cc.isCrouching ? "Crouch" : _cc && _cc.isStrafing ? "Strafing" : "Default", true);
            // not aiming: the target is always the base state (never Strafing / Aim), whatever Invector's input picked
            if (!_camAiming && !_tpIn.changeCameraState && cam.lerpState != null && cam.lerpState.Name != BaseCameraState) cam.ChangeState(BaseCameraState, true);
            CameraStateNow = cam.lerpState != null ? cam.lerpState.Name : ""; CameraHeightNow = cam.currentState != null ? cam.currentState.height : 0f;
        }

        string BaseCameraState => _cc && _cc.isCrouching ? "Crouch" : "Default";

        /// <summary>Item switched or aim ended: drop any custom camera state, clear a strafe nobody owns (a stuck strafe is what kept
        /// Invector on its higher Strafing camera) and blend back to the base locomotion camera state. Absolute, so nothing accumulates.</summary>
        void ResetToBaseCamera(Invector.vCamera.vThirdPersonCamera cam)
        {
            _tpIn.ResetCameraState();
            if (_cc && ASHStrafeLock.Owners(_cc) == 0 && _cc.isStrafing && !(_lightAim && _lightAim.Aiming)) _cc.isStrafing = false;
            if (cam.lerpState == null || cam.lerpState.Name != BaseCameraState) cam.ChangeState(BaseCameraState, true);
        }

        bool HasArrows => _inv && _eq && _eq.Main && _eq.Main.ammoItem && _inv.Count(_eq.Main.ammoItem) > 0;

        void SetAim(bool on)
        {
            Aiming = on;
            if (!_cc) return;
            if (on) ASHStrafeLock.Acquire(_cc, this, aimRotationSpeed); else ASHStrafeLock.Release(_cc, this);
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
            var bank = ASHSoundBank.Instance; var muzzle = _eq.MainVisual ? _eq.MainVisual.transform.position : Root.position + Vector3.up * 1.4f;
            bool empty = _eq.Main.ammoItem && _inv && _inv.Count(_eq.Main.ammoItem) <= 0;
            if (!_eq.UseMain()) { if (empty && bank) { ASHSfx.Play("dryFire", bank.dryFire, muzzle, bank.gunVolume * bank.volume, 1f, 0.05f, 0.6f); _nextFire = Time.time + 0.25f; } return false; } // spends one round, muzzle flash; "Click" when empty
            if (bank)
            {
                if (kind == Kind.Pistol) ASHSfx.Play("pistolShot", bank.pistolShot, muzzle, bank.gunVolume * bank.volume, bank.pistolPitch, 0.04f, 0.6f, 250f);
                else { ASHSfx.Play("rifleShot", bank.rifleShot, muzzle, bank.gunVolume * bank.volume, 1f, 0.03f, 0.6f, 400f); if (bank.rifleBolt != null && bank.rifleBolt.Length > 0) StartCoroutine(Bolt(bank, rifleCooldown)); }
            }
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

        bool _drawSfx; int _wakeFrame;
        System.Collections.IEnumerator Bolt(ASHSoundBank bank, float cooldown)
        {
            yield return new WaitForSeconds(Mathf.Clamp(cooldown * 0.45f, 0.25f, 0.8f));
            if (Current == Kind.Rifle) ASHSfx.Play("rifleBolt", bank.rifleBolt, _eq.MainVisual ? _eq.MainVisual.transform.position : Root.position + Vector3.up * 1.4f, bank.gunVolume * bank.volume * 0.7f, 1f, 0.03f, 0.6f);
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
            var bk = ASHSoundBank.Instance;
            if (bk) { ASHSfx.Play("bowRelease", bk.bowRelease, origin, bk.bowVolume * bk.volume, 1f, 0.05f, 0.6f); ASHSfx.Play("arrowFlight", bk.arrowFlight, origin + dir * 1.5f, bk.bowVolume * bk.volume * 0.45f, 1.1f, 0.08f, 0.8f); }
            _layer.Restart("BowShot"); _shotUntil = Time.time + bowShotHoldSeconds;
            return true;
        }

        [Tooltip("Fired arrow: how far ahead of the arrow's origin the tip sits (matches ASHArrow.tipLength, used for hits and sticking).")]
        public float arrowTipAhead = 0.35f;

        /// <summary>Runtime arrow visual with the TIP on local +Z (the mesh's pointed end is detected; flipArrowVisual is the
        /// fallback for unreadable meshes). originAtNock: origin at the nock (nocked arrow); otherwise the tip sits arrowTipAhead in
        /// front of the origin (fired arrow, matches ASHArrow's hit/stick maths).</summary>
        GameObject MakeArrowVisual(string name, bool originAtNock = false)
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
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var pivot = new GameObject("ArrowPivot").transform; pivot.SetParent(go.transform, false);
            for (int i = go.transform.childCount - 1; i >= 0; i--) { var ch = go.transform.GetChild(i); if (ch != pivot) ch.SetParent(pivot, false); }
            Vector3 tip, tail;
            if (!ArrowEndsLocal(go, out tip, out tail)) { tip = new Vector3(0, 0, flipArrowVisual ? -0.8f : 0.8f); tail = Vector3.zero; }
            var rot = Quaternion.FromToRotation(tip - tail, Vector3.forward);
            pivot.localRotation = rot; tip = rot * tip; tail = rot * tail;
            pivot.localPosition = originAtNock ? -tail : Vector3.forward * arrowTipAhead - tip;
            return go;
        }

        /// <summary>World tip and tail of an arrow visual (pointed end = tip). False if no readable mesh.</summary>
        public static bool ArrowEndsWorld(GameObject arrow, out Vector3 tip, out Vector3 tail)
        {
            tip = tail = Vector3.zero; if (!arrow) return false;
            if (!ArrowEndsLocal(arrow, out var t, out var b)) return false;
            tip = arrow.transform.TransformPoint(t); tail = arrow.transform.TransformPoint(b); return true;
        }

        /// <summary>Arrow ends in the root's local space: long axis of the mesh vertices; the pointed end (narrower at the very end) is the tip.</summary>
        static bool ArrowEndsLocal(GameObject root, out Vector3 tip, out Vector3 tail)
        {
            tip = tail = Vector3.zero; var pts = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m || !m.isReadable) continue;
                foreach (var v in m.vertices) pts.Add(root.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            if (pts.Count < 8) return false;
            Vector3 mn = pts[0], mx = pts[0]; foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            var ext = mx - mn; int ax = ext.x >= ext.y && ext.x >= ext.z ? 0 : ext.y >= ext.z ? 1 : 2; float len = ext[ax]; if (len < 1e-4f) return false;
            float Width(float lo, float hi)
            {
                Vector3 a = Vector3.positiveInfinity, b = Vector3.negativeInfinity; int n = 0;
                foreach (var p in pts) if (p[ax] >= lo && p[ax] <= hi) { a = Vector3.Min(a, p); b = Vector3.Max(b, p); n++; }
                if (n == 0) return 0f; var e = b - a; e[ax] = 0f; return Mathf.Max(e.x, e.y, e.z);
            }
            float wMin = Width(mn[ax], mn[ax] + len * 0.025f), wMax = Width(mx[ax] - len * 0.025f, mx[ax]);
            var centre = (mn + mx) * 0.5f; Vector3 lowEnd = centre, highEnd = centre; lowEnd[ax] = mn[ax]; highEnd[ax] = mx[ax];
            if (wMin <= wMax) { tip = lowEnd; tail = highEnd; } else { tip = highEnd; tail = lowEnd; }
            return true;
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
            _bowBlend = bowOut && (Aiming || _layer == null || _layer.State == "BowDraw" || _layer.State == "BowHold") ? Mathf.Clamp01(w) : 0f;
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
            NockWorld = FingerWorld;   // the archery animation brings the drawing fingers to the cheek; the nock sits there
            float n = _bowBlend;

            // arrow: authored rest arrow while idle; while drawing a runtime copy is nocked on the string and rests on the bow
            if (_arrowShown && _restArrow)
            {
                if (n > 0.01f && rh && _restValid)
                {
                    if (!_nockArrow) _nockArrow = MakeArrowVisual("ASH_NockArrow", true);
                    var rest = _restArrow.transform;
                    if (_restArrow.activeSelf) _restArrow.SetActive(false);
                    _nockArrow.SetActive(true);
                    var shaft = _restValid ? ArrowRestWorld - NockWorld : aimL; if (shaft.sqrMagnitude < 0.01f) shaft = aimL;
                    var nockRot = Quaternion.LookRotation(shaft.normalized, Vector3.up);
                    _nockArrow.transform.localScale = rest.lossyScale;
                    float k = Mathf.SmoothStep(0f, 1f, n);
                    // start from the rest arrow's pose (nock end, tip direction) so the blend never spins the arrow end over end
                    Vector3 rTail = rest.position, rFwd = rest.forward;
                    if (_restEndsFor != _restArrow) { _restEndsFor = _restArrow; _restEndsOk = ArrowEndsLocal(_restArrow, out _restTipL, out _restTailL); }
                    if (_restEndsOk) { var tw = rest.TransformPoint(_restTipL); rTail = rest.TransformPoint(_restTailL); rFwd = (tw - rTail).normalized; }
                    var rRot = Quaternion.LookRotation(rFwd, rest.up);
                    _nockArrow.transform.SetPositionAndRotation(Vector3.Lerp(rTail, NockWorld, k), Quaternion.Slerp(rRot, nockRot, k));
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
            BowBodyAim();
            BowArmDelta();
            RifleBlade();
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
            UpdateGunSupportHand(v, w);
            HeadLook();
            UpdateNockAndString(v, w);
        }


        /// <summary>Bow: the Mixamo archery pose (from Genesis) has the archer side-on with the arrow line along the character's left.
        /// Turn the whole body (hips, so legs follow into a side-on stance) about world up until the arrow line (draw hand to bow hand)
        /// points at the aim point horizontally, then pitch the spine for the vertical part. Bones only, after animation (the Animator
        /// re-poses next frame). No arm IK.</summary>
        void BowBodyAim()
        {
            BowBodyYaw = 0f; BowArrowLineError = -1f;
            string st = _layer && _layer.Valid ? _layer.State : "";
            float want = 0f;
            if (bowBodyAim && Current == Kind.Bow && _anim && _anim.isHuman && IsBowState(st))
                want = Mathf.Clamp01(_layer.Weight) * (st == "BowDraw" ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.7f, Draw01)) : 1f);
            _bodyW = Mathf.MoveTowards(_bodyW, want, bowBodyBlendSpeed * Time.deltaTime);
            if (_bodyW <= 0.001f) return;
            var hips = _anim.GetBoneTransform(HumanBodyBones.Hips); var spine = _anim.GetBoneTransform(HumanBodyBones.Spine);
            var chest = _anim.GetBoneTransform(HumanBodyBones.Chest);
            var rh = _anim.GetBoneTransform(HumanBodyBones.RightHand); var lh = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            if (!hips || !spine || !rh || !lh) return;
            var aimPt = AimPoint(out _);
            var line = lh.position - rh.position; if (line.sqrMagnitude < 0.04f) return;
            var flatLine = Vector3.ProjectOnPlane(line, Vector3.up); var flatAim = Vector3.ProjectOnPlane(aimPt - rh.position, Vector3.up);
            if (flatLine.sqrMagnitude < 1e-4f || flatAim.sqrMagnitude < 1e-4f) return;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(flatLine, flatAim, Vector3.up), -bowMaxBodyYaw, bowMaxBodyYaw) * _bodyW;
            hips.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * hips.rotation; BowBodyYaw = yaw;
            if (bowSpinePitch > 0f)
            {
                var piv = chest ? chest : spine;
                for (int i = 0; i < 2; i++)
                {
                    line = lh.position - rh.position; var aim = aimPt - rh.position;
                    var axis = Vector3.Cross(line, aim); if (axis.sqrMagnitude < 1e-8f) break;
                    // pitch only: keep the rotation axis horizontal so the stance yaw stays on the hips
                    var hAxis = Vector3.ProjectOnPlane(axis, Vector3.up).normalized; if (hAxis.sqrMagnitude < 0.5f) break;
                    float a = Vector3.SignedAngle(Vector3.ProjectOnPlane(line, hAxis), Vector3.ProjectOnPlane(aim, hAxis), hAxis);
                    piv.rotation = Quaternion.AngleAxis(Mathf.Clamp(a, -60f, 60f) * bowSpinePitch * _bodyW, hAxis) * piv.rotation;
                }
            }
            BowArrowLineError = Vector3.Angle(lh.position - rh.position, aimPt - rh.position);
        }

        /// <summary>Aim-only nudge of the bow arm: shoulder yaw (moves + turns the bow) and hand yaw (turns the bow), right = +.</summary>
        void BowArmDelta()
        {
            if (_bodyW <= 0.001f || !_anim || !_anim.isHuman) return;
            var ua = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var hand = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            if (!ua || !hand) return;
            float w = Mathf.SmoothStep(0f, 1f, _bodyW);
            float reach = Mathf.Max(0.2f, Vector3.Distance(ua.position, hand.position));
            float yaw = bowArmYaw + Mathf.Rad2Deg * Mathf.Atan2(bowArmShiftRight, reach);
            if (Mathf.Abs(yaw) > 0.01f) ua.rotation = Quaternion.AngleAxis(yaw * w, Vector3.up) * ua.rotation;
            if (Mathf.Abs(bowHandYaw) > 0.01f) hand.rotation = Quaternion.AngleAxis(bowHandYaw * w, Vector3.up) * hand.rotation;
        }

        /// <summary>Head (and a share on the neck) turns to look at the aim point (crosshair hit) while aiming. Runs after the
        /// archery body turn / rifle blade so it never fights the pose; bones only, after animation; clamped and blended.</summary>
        void HeadLook()
        {
            HeadLookError = -1f; HeadLookRaw = -1f; HeadLookTurn = 0f;
            if (!_anim || !_anim.isHuman) return;
            var head = _anim.GetBoneTransform(HumanBodyBones.Head);
            if (!head) return;
            string st = _layer && _layer.Valid ? _layer.State : "";
            bool bow = Current == Kind.Bow && IsBowState(st) && _layer.Weight > 0.05f;
            bool gun = headLookOnGuns && (Current == Kind.Rifle || Current == Kind.Pistol) && Aiming;
            if (!_faceAxisOk && !Aiming && !bow && Time.frameCount > _wakeFrame + 30) { _faceLocal = Quaternion.Inverse(head.rotation) * Vector3.ProjectOnPlane(Root.forward, Vector3.up).normalized; _faceAxisOk = true; }
            float want = headLookAtAim && _faceAxisOk && (bow || gun) ? headLookWeight * (bow ? Mathf.Clamp01(_layer.Weight) : 1f) : 0f;
            _lookW = Mathf.MoveTowards(_lookW, want, headLookBlendSpeed * Time.deltaTime);
            if (_lookW <= 0.001f) return;
            var aimPt = AimPoint(out _);
            var to = Quaternion.AngleAxis(headLookYawOffset, Vector3.up) * (aimPt - head.position).normalized;
            var face = FaceDir(head);
            HeadLookRaw = Vector3.Angle(face, to);
            if (HeadLookRaw < 0.01f) { HeadLookError = 0f; return; }
            // yaw and pitch clamped separately (looking up/down twists the neck/shoulder skin fastest)
            var fFlat = Vector3.ProjectOnPlane(face, Vector3.up); var tFlat = Vector3.ProjectOnPlane(to, Vector3.up);
            float dYaw = fFlat.sqrMagnitude > 1e-6f && tFlat.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(fFlat, tFlat, Vector3.up) : 0f;
            float dPitch = Mathf.Asin(Mathf.Clamp(to.y, -1f, 1f)) * Mathf.Rad2Deg - Mathf.Asin(Mathf.Clamp(face.y, -1f, 1f)) * Mathf.Rad2Deg;
            dYaw = Mathf.Clamp(dYaw, -headLookClamp, headLookClamp); dPitch = Mathf.Clamp(dPitch, -headLookPitchClamp, headLookPitchClamp);
            var side = Vector3.Cross(Vector3.up, fFlat.sqrMagnitude > 1e-6f ? fFlat.normalized : Root.forward).normalized;
            var turnQ = Quaternion.AngleAxis(dYaw * _lookW, Vector3.up) * Quaternion.AngleAxis(-dPitch * _lookW, side);
            turnQ.ToAngleAxis(out float a, out var axis); if (a > 180f) a -= 360f;
            HeadLookTurn = Mathf.Abs(a);
            // one world axis split over the chain: the rotations compose to exactly 'a' at the head, each bone only turns a little
            float sSp = spineLookShare, sCh = chestLookShare, sNk = neckLookShare, sHd = Mathf.Max(0f, 1f - sSp - sCh - sNk);
            var chest = _anim.GetBoneTransform(HumanBodyBones.UpperChest); if (!chest) chest = _anim.GetBoneTransform(HumanBodyBones.Chest);
            void Turn(Transform t, float share) { if (t && share > 0f) t.rotation = Quaternion.AngleAxis(a * share, axis) * t.rotation; else sHd += share; }
            Turn(_anim.GetBoneTransform(HumanBodyBones.Spine), sSp); Turn(chest, sCh); Turn(_anim.GetBoneTransform(HumanBodyBones.Neck), sNk);
            head.rotation = Quaternion.AngleAxis(a * sHd, axis) * head.rotation;
            HeadLookError = Vector3.Angle(Quaternion.AngleAxis(a, axis) * face, to);
        }

        /// <summary>Where the face points: from the eyes (mid-eye minus head, perpendicular to the neck) when the avatar maps them,
        /// else the head axis learned at idle.</summary>
        public string FaceSource { get; private set; } = "";
        Vector3 FaceDir(Transform head)
        {
            var le = _anim.GetBoneTransform(HumanBodyBones.LeftEye); var re = _anim.GetBoneTransform(HumanBodyBones.RightEye);
            var neck = _anim.GetBoneTransform(HumanBodyBones.Neck);
            if (le && re && neck)
            {
                var up = (head.position - neck.position).normalized;
                var f = Vector3.ProjectOnPlane((le.position + re.position) * 0.5f - head.position, up);
                if (f.sqrMagnitude > 1e-6f) { FaceSource = "eyes"; return f.normalized; }
            }
            if (le && re)
            {
                // eye line: face = up x (left->right) ... right-handed: forward = cross(right, up)
                var right = (re.position - le.position).normalized; FaceSource = "eyeline";
                return Vector3.Cross(right, head.up).normalized;
            }
            FaceSource = "idle axis"; return head.rotation * _faceLocal;
        }

        /// <summary>Rifle aim: turn the chest toward the right (left shoulder forward, bladed stance) so the support hand reaches.</summary>
        void RifleBlade()
        {
            bool on = rifleSeatInShoulder && Current == Kind.Rifle && Aiming && _anim && _anim.isHuman;
            _bladeW = Mathf.MoveTowards(_bladeW, on ? 1f : 0f, armIKBlendSpeed * Time.deltaTime);
            if (_bladeW <= 0.001f || rifleBladeYaw <= 0f || !_anim) return;
            var spine = _anim.GetBoneTransform(HumanBodyBones.Spine); if (!spine) return;
            spine.rotation = Quaternion.AngleAxis(rifleBladeYaw * Mathf.SmoothStep(0f, 1f, _bladeW), Vector3.up) * spine.rotation;
        }

        /// <summary>Rifle butt point in the visual's local space: back end (min local Z, local Z = muzzle) of the mesh bounds.</summary>
        /// <summary>Called by ASHHeldRig after it moved the hands onto the item grips: re-seat the nocked arrow and the string.</summary>
        public void RefreshAfterRig()
        {
            var v = _eq ? _eq.MainVisual : null; if (!v) return;
            float w = _layer && _layer.Valid ? _layer.Weight : (Aiming ? 1f : 0f);
            UpdateNockAndString(v, w);
        }
        /// <summary>Rifle butt point (world).</summary>
        public Vector3 ButtWorld(GameObject v) => v ? v.transform.TransformPoint(ButtLocal(v)) : Vector3.zero;

        Vector3 ButtLocal(GameObject v)
        {
            if (_buttFor == v) return _buttLocal;
            _buttFor = v; float minZ = float.MaxValue; Vector3 sum = Vector3.zero; int n = 0; var corners = new List<Vector3>();
            foreach (var mf in v.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.name == LeftHandIKName) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    corners.Add(v.transform.InverseTransformPoint(mf.transform.TransformPoint(c)));
                }
            }
            foreach (var c in corners) minZ = Mathf.Min(minZ, c.z);
            foreach (var c in corners) if (c.z < minZ + 0.02f) { sum += c; n++; }
            _buttLocal = n > 0 ? sum / n : Vector3.zero; return _buttLocal;
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
            GunPull = 0f; RifleButtToPocket = -1f;
            var rua = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var rla = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm); var rh = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            bool seat = rifleSeatInShoulder && Current == Kind.Rifle && Aiming && rua && rla && rh && !ASHEquipment.InLeftHand(_eq.Main);
            Vector3 pocket = Vector3.zero;
            if (seat)
            {
                // butt into the shoulder pocket: move the gun hand (gun follows, aim direction kept) so the stock's back end sits there
                pocket = rua.position + Root.TransformDirection(rifleShoulderPocket);
                var butt = v.transform.TransformPoint(ButtLocal(v));
                var keep = rh.rotation;
                TwoBoneIK(rua, rla, rh, rh.position + (pocket - butt), rua.position + Root.TransformDirection(gunRightElbowHint), k);
                rh.rotation = keep;
            }
            if (gunShoulderPull > 0f && rua && rla && rh && !ASHEquipment.InLeftHand(_eq.Main))
            {
                float maxR = (Vector3.Distance(ua.position, la.position) + Vector3.Distance(la.position, lh.position)) * 0.97f;
                var a = _aimDir.sqrMagnitude > 0.5f ? _aimDir : v.transform.forward;
                var rel = target.position - ua.position; float b = Vector3.Dot(rel, a), c = rel.sqrMagnitude - maxR * maxR;
                if (c > 0f)
                {
                    float disc = b * b - c; float d = disc >= 0f ? b - Mathf.Sqrt(disc) : b;
                    d = Mathf.Clamp(d, 0f, seat ? rifleMaxPull : gunMaxPull) * gunShoulderPull * k;
                    if (d > 0.001f)
                    {
                        var keep = rh.rotation;
                        TwoBoneIK(rua, rla, rh, rh.position - a * d, rua.position + Root.TransformDirection(gunRightElbowHint), 1f);
                        rh.rotation = keep; GunPull = d;
                    }
                }
            }
            var grip = target.position; GunGripSlide = 0f;
            if (seat)
            {
                RifleButtToPocket = Vector3.Distance(v.transform.TransformPoint(ButtLocal(v)), pocket);
                // support hand out of reach: grip further back along the forend (toward the butt) instead of moving the rifle
                float maxR = (Vector3.Distance(ua.position, la.position) + Vector3.Distance(la.position, lh.position)) * 0.97f;
                var ax = v.transform.forward; var rel = grip - ua.position; float c = rel.sqrMagnitude - maxR * maxR;
                if (c > 0f && rifleGripSlideMax > 0f)
                {
                    float b = Vector3.Dot(rel, ax), disc = b * b - c;
                    float t = disc >= 0f ? b - Mathf.Sqrt(disc) : b;   // smallest slide back along -ax that reaches (or the closest point)
                    t = Mathf.Clamp(t, 0f, rifleGripSlideMax); grip -= ax * t; GunGripSlide = t;
                }
            }
            TwoBoneIK(ua, la, lh, grip, ua.position + Root.TransformDirection(gunLeftElbowHint), k);
            lh.rotation = Quaternion.Slerp(lh.rotation, target.rotation, k);
            GunLeftHandError = Vector3.Distance(lh.position, grip);
            GunLeftReach = Vector3.Distance(ua.position, grip); GunLeftArmLength = Vector3.Distance(ua.position, la.position) + Vector3.Distance(la.position, lh.position);
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
