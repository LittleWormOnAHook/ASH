using System.Collections.Generic;
using ASH.Core;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using ASH.Survival;
using Invector;
using Invector.vCharacterController;
using Invector.vItemManager;
using Invector.vMelee;
using Invector.vShooter;
using UnityEngine;

namespace ASH.Combat
{
    /// <summary>
    /// Glue between ASH and the Invector shooter/melee layer on the player:
    /// - ASHEquipment decides what is in hand (hotbar, pack). When the main item's pre-placed Held_ model carries an Invector
    ///   vShooterWeapon (pistol, hunting rifle, shotgun, wooden bow) or vMeleeWeapon (hatchet, clubs), it is handed to
    ///   vShooterManager / vMeleeManager; otherwise both are cleared. Models and hand placements are never moved.
    /// - Ammo: the ASH pack is the only store. Rounds in the clip stay counted in the pack; Invector's reserve is the pack
    ///   minus the clip, and every shot removes one from the pack. So saves need nothing extra, and old saves load as before.
    /// - Damage numbers come from ASHWeaponData (Genesis tuning); handling (spread, recoil, reload) stays Invector's.
    /// - Damage to the player goes to ASH health (ASHSurvivalStats); Invector's own health is kept immortal so ASH owns death.
    ///   Genesis stagger rules decide whether a hit plays a reaction. Melee hits get Genesis hit stop and hit marks.
    /// - ASHWeaponAim is put in external mode while a gun or the bow is out (it only draws the nocked arrow and string pull).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1590)]
    public sealed class ASHCombatBridge : MonoBehaviour
    {
        ASHBowDrawRig _bowRig;
        public static ASHCombatBridge Player { get; private set; }

        ASHEquipment _eq; ASHInvectorInput _in; vShooterManager _sm; vMeleeManager _mm; vAmmoManager _am; vThirdPersonController _cc;
        ASHSurvivalStats _stats; ASHWeaponAim _aim; ASHChopAnimator _chop; Animator _anim;
        ASHItemDefinition _cur, _offItem; GameObject _curGo, _offGo; vShooterWeapon _gun; vMeleeWeapon _melee; bool _gunLeft, _lastCrit, _wasCharging, _camDone, _torchLit;
        float _staggerImmuneUntil;
        readonly Dictionary<vShooterWeapon, bool> _primed = new Dictionary<vShooterWeapon, bool>();

        public ASHWeaponData CurrentData { get; private set; }
        public ASHItemDefinition CurrentItem => _cur;
        public bool MeleeOut => _melee;
        public bool GunOut => _gun;
        public vShooterWeapon Gun => _gun;

        /// <summary>Invector reserve backed by the ASH pack: pack count minus what is already in the clip. Use() is a no-op
        /// because loaded rounds stay in the pack until fired.</summary>
        sealed class PackAmmo : vAmmo
        {
            public ASHCombatBridge owner; public ASHItemDefinition item;
            public PackAmmo(ASHCombatBridge o, ASHItemDefinition it, int id) : base(it ? it.displayName : "ammo", id, 0) { owner = o; item = it; }
            public override int count => owner ? Mathf.Max(0, owner.PackCount(item) - owner.LoadedOf(item)) : 0;
            public override void Use() { }
            public override void Use(int amount) { }
        }

        int PackCount(ASHItemDefinition it) => _eq ? _eq.AmmoCount(it) : 0;
        int LoadedOf(ASHItemDefinition it) => _gun && _cur && _cur.ammoItem == it ? Mathf.Max(0, _gun.ammo) : 0;

        void Awake()
        {
            Player = this;
            _in = GetComponent<ASHInvectorInput>(); _cc = GetComponent<vThirdPersonController>();
            _sm = GetComponent<vShooterManager>(); _mm = GetComponent<vMeleeManager>(); _am = GetComponent<vAmmoManager>();
            _eq = GetComponentInChildren<ASHEquipment>(true); if (!_eq) _eq = GetComponentInParent<ASHEquipment>();
            _stats = GetComponentInChildren<ASHSurvivalStats>(true); if (!_stats) _stats = GetComponentInParent<ASHSurvivalStats>();
            _anim = GetComponent<Animator>(); if (!_anim) _anim = GetComponentInChildren<Animator>();
            _aim = _anim ? _anim.GetComponent<ASHWeaponAim>() : null; if (!_aim) _aim = GetComponentInChildren<ASHWeaponAim>(true);
            _chop = GetComponentInChildren<ASHChopAnimator>(true);
            if (_in)
            {
                _in.MeleeAllowed = () => _melee && !(_chop && _chop.Chopping) && !ASHUIState.BlocksGameplay;
                _in.ShooterAllowed = () => _gun && !ASHUIState.BlocksGameplay;
            }
            if (_eq) _eq.CombatOwnsMain = it => it != null && it == _cur && (_gun || _melee);
            if (_am) { _am.ammoListData = null; _am.ammos.Clear(); }
            if (_sm) _sm.useAmmoDisplay = false;
            var t = ASHCombatTuning.Live;
            if (t.shooterUiPrefab && !FindAnyObjectByType<vControlAimCanvas>(FindObjectsInactive.Include))
            {
                var ui = Instantiate(t.shooterUiPrefab); ui.name = "ASH_ShooterUI (Invector crosshair)";
                foreach (var d in ui.GetComponentsInChildren<vAmmoDisplay>(true)) d.gameObject.SetActive(false);
            }
        }

        void OnDestroy() { if (Player == this) Player = null; }

        void Start()
        {
            if (_cc)
            {
                _cc.isImmortal = true; // ASH health owns damage and death
                _cc.onStartReceiveDamage.AddListener(OnStartDamage);
                _cc.onReceiveDamage.AddListener(OnDamage);
            }
            if (_mm) _mm.onDamageHit.AddListener(OnMeleeHit);
            if (_sm) { _sm.onShot.AddListener(OnShot); _sm.onEmptyClipShot.AddListener(OnEmpty); _sm.onFinishReloadWeapon.AddListener(OnReloaded); }
        }

        void Update()
        {
            if (!_camDone) _camDone = EnsureCameraStates();
            else BowCameraOffsets();
            if (_eq)
            {
                var main = _eq.Main; var go = _eq.MainVisual;
                bool torchLit = _eq.HandLightOn;
                if (main != _cur || go != _curGo || _eq.Off != _offItem || _eq.OffVisual != _offGo || torchLit != _torchLit)
                    Rebind(main, go);
            }
            if (_gun && _cur && _cur.ammoItem)
            {
                int pack = PackCount(_cur.ammoItem);
                if (_gun.ammo > pack) _gun.ammo = pack; // rounds dropped or used elsewhere
            }
            if (_aim)
            {
                _aim.ExternalControl = _gun;
                if (_gun) { _aim.ExternalAiming = _in && _in.IsAiming; _aim.ExternalDraw01 = _gun.chargeWeapon ? _gun.powerCharge : 0f; }
            }
            if (_bowRig) { _bowRig.draw01 = _gun && _gun.chargeWeapon ? Mathf.Clamp01(_gun.powerCharge) : 0f; _bowRig.Apply(); }
            UpdateAnimatorMode();
            UpdateBowLegs();
            EnsureIdleRunner();
            if (_gun && _gun.chargeWeapon)
            {
                bool ch = _gun.powerCharge > 0.02f;
                if (ch && !_wasCharging) { var b = ASHSoundBank.Instance; if (b) ASHSfx.Play("bowDraw", b.bowDraw, transform.position + Vector3.up * 1.4f, b.bowVolume * b.volume, 1f, 0.04f, 0.6f); }
                _wasCharging = ch;
            }
        }

        void Rebind(ASHItemDefinition main, GameObject go)
        {
            if (_gun)
            {
                _gun.onInstantiateProjectile.RemoveListener(OnProjectile);
                if (_sm) { if (_gunLeft) _sm.SetLeftWeapon((GameObject)null); else _sm.SetRightWeapon((GameObject)null); }
                _gun.isAiming = false; _gun.powerCharge = 0f;
            }
            if (_melee)
            {
                _melee.onEnableDamage.RemoveListener(RollMelee);
                if (_mm) _mm.SetRightWeapon((vMeleeWeapon)null);
            }
            if (_bowRig) _bowRig.RestoreIdle(_anim);
            _gun = null; _melee = null; CurrentData = null; _wasCharging = false; _bowRig = null;
            _cur = main; _curGo = go;
            _offItem = _eq ? _eq.Off : null; _offGo = _eq ? _eq.OffVisual : null;
            _torchLit = _eq && _eq.HandLightOn;
            if (!main || !go)
            {
                BindOffhandTorch();
                return;
            }
            CurrentData = ASHWeaponData.For(main);
            _gun = go.GetComponent<vShooterWeapon>();
            _melee = _gun ? null : go.GetComponent<vMeleeWeapon>();
            if (_gun && _sm)
            {
                ApplyGun(main);
                _gunLeft = ASHEquipment.InLeftHand(main) || _gun.isLeftWeapon;
                if (_gunLeft) _sm.SetLeftWeapon(go); else _sm.SetRightWeapon(go);
                _gun.onInstantiateProjectile.AddListener(OnProjectile);
                _bowRig = go.GetComponent<ASHBowDrawRig>();
                // bow move set: -1 keeps Invector's bow set (its aim pose needs it); the bent knees are fixed by ASHBowUprightLegs
                if (_bowRig && ASHCombatTuning.Live.bowAimMoveSet >= 0) _gun.moveSetID = ASHCombatTuning.Live.bowAimMoveSet;
            }
            else _gun = null;
            if (_melee && _mm) { ApplyMelee(); _mm.SetRightWeapon(_melee); _melee.onEnableDamage.AddListener(RollMelee); }
            else _melee = null;
            if (!_gun && !_melee) BindOffhandTorch();
        }

        void BindOffhandTorch()
        {
            var off = _eq ? _eq.Off : null;
            var go = _eq ? _eq.OffVisual : null;
            if (!off || !go || off.itemId != "torch") return;
            ASHTorchStrike.Ensure(go);
            var s = ASHTorchSettings.Live;
            if (s && s.requireLitToStrike)
            {
                var light = go.GetComponentInChildren<ASHLightSource>(true);
                if (!light || !light.On) return;
            }
            var w = go.GetComponent<vMeleeWeapon>();
            if (!w || !_mm) return;
            _melee = w;
            CurrentData = ASHWeaponData.For(off);
            ApplyMelee();
            if (!CurrentData && s)
            {
                _melee.distanceToAttack = s.fallbackReach;
                _melee.staminaCost = s.fallbackStamina;
                _melee.meleeType = vMeleeType.OnlyAttack;
            }
            _mm.SetRightWeapon(_melee);
            _melee.onEnableDamage.AddListener(RollMelee);
        }

        void ApplyGun(ASHItemDefinition main)
        {
            var d = CurrentData; var t = ASHCombatTuning.Live; var ammo = main.ammoItem;
            if (d && !d.useInvectorDamage)
            {
                _gun.maxDamage = Mathf.RoundToInt((d.damage + d.damageRandom) * t.outgoingDamage);
                _gun.minDamage = Mathf.RoundToInt(Mathf.Max(0f, d.damage - d.damageRandom) * t.outgoingDamage);
                _gun.damageByDistance = true; _gun.minDamageDistance = d.range * 0.25f; _gun.maxDamageDistance = d.range;
                if (d.kind == ASHWeaponKind.Bow) { _gun.chargeDamageMultiplier = d.fullDrawDamageMultiplier; _gun.minDamageDistance = d.range * 0.5f; }
            }
            if (d && d.kind == ASHWeaponKind.Bow)
            {
                _gun.chargeWeapon = true; _gun.chargeSpeed = 1f / Mathf.Max(0.1f, d.drawSeconds);
                _gun.changeVelocityByCharge = true; _gun.chargeVelocityMultiplier = d.fullDrawSpeedMultiplier;
                _gun.velocity = d.projectileSpeed / Mathf.Max(1f, d.fullDrawSpeedMultiplier); _gun.autoShotOnFinishCharge = d.autoLooseAtFullDraw;
            }
            if (d && d.overrideInvectorHandling && d.kind == ASHWeaponKind.Gun)
            { _gun.velocity = d.projectileSpeed; _gun.dispersion = d.dispersion; _gun.shootFrequency = d.shotInterval; }
            _gun.isInfinityAmmo = false; _gun.checkAmmoHandle = null; _gun.changeAmmoHandle = null;
            if (ammo && _am)
            {
                if (_am.ammos.Find(a => a.ammoID == _gun.ammoID) is PackAmmo pa) pa.item = ammo;
                else { _am.ammos.RemoveAll(a => a.ammoID == _gun.ammoID); _am.ammos.Add(new PackAmmo(this, ammo, _gun.ammoID)); }
            }
            // first draw this session: the gun comes out loaded from the pack (a save stores only the pack)
            if (!_primed.ContainsKey(_gun)) { _primed[_gun] = true; _gun.ammo = Mathf.Min(_gun.clipSize, ammo ? PackCount(ammo) : 0); }
            if (ammo) _gun.ammo = Mathf.Min(_gun.ammo, PackCount(ammo));
        }

        void ApplyMelee()
        {
            var d = CurrentData; if (!d) return;
            _melee.distanceToAttack = d.reach; _melee.staminaCost = d.staminaCost; _melee.staminaRecoveryDelay = d.staminaRecoveryDelay;
            _melee.attackID = d.attackID; _melee.movesetID = d.movesetID; _melee.meleeType = vMeleeType.OnlyAttack; // no blocking
        }

        void RollMelee()
        {
            if (!_melee) return;
            var d = CurrentData; bool crit = false;
            float dmg = d ? d.RollDamage(out crit) : _melee.damage.damageValue;
            _melee.damage.damageValue = dmg * ASHCombatTuning.Live.outgoingDamage;
            _melee.damage.damageType = crit ? "ASH_MeleeCrit" : "ASH_Melee";
            _lastCrit = crit;
        }

        void OnMeleeHit(vHitInfo hi)
        {
            if (hi == null || !_melee || hi.attackObject != _melee) return;
            ASHHitStop.OnMeleeHit(_anim, hi.targetCollider, CurrentData, _lastCrit);
            var n = (transform.position + Vector3.up * 1.2f - hi.hitPoint); n.y *= 0.3f;
            ASHHitMarks.Spawn(hi.hitPoint, n.normalized, hi.targetCollider, ASHHitKind.Melee);
            var rb = hi.targetCollider ? hi.targetCollider.attachedRigidbody : null;
            if (rb && !rb.isKinematic && CurrentData) rb.AddForceAtPosition(-n.normalized * CurrentData.knockback, hi.hitPoint, ForceMode.Impulse);
            var b = ASHSoundBank.Instance; if (b && b.axeChop != null) ASHSfx.Play("meleeHit", b.axeChop, hi.hitPoint, b.volume, Random.Range(0.85f, 1.05f), 0.05f, 1f, 30f);
            ASHTorchStrike.NotifyHit(hi);
        }

        void OnShot(vShooterWeapon w)
        {
            if (!w || w != _gun || !_cur) return;
            if (_cur.ammoItem) _eq.SpendAmmo(_cur.ammoItem, 1);
            var kind = CurrentData ? CurrentData.kind : (w.chargeWeapon ? ASHWeaponKind.Bow : ASHWeaponKind.Gun);
            var b = ASHSoundBank.Instance; var at = w.muzzle ? w.muzzle.position : transform.position + Vector3.up * 1.4f;
            if (kind == ASHWeaponKind.Bow)
            {
                if (_aim) _aim.ExternalShot();
                if (b) { ASHSfx.Play("bowRelease", b.bowRelease, at, b.bowVolume * b.volume, 1f, 0.05f, 0.6f); ASHSfx.Play("arrowFlight", b.arrowFlight, at, b.bowVolume * b.volume * 0.45f, 1.1f, 0.08f, 0.8f); }
            }
            else
            {
                // Invector's own fire clip plays if the template had one; ASH sounds only fill in when it has none
                if (b && !w.fireClip)
                {
                    if (_cur.gripStyle == ASHGripStyle.PistolGrip) ASHSfx.Play("pistolShot", b.pistolShot, at, b.gunVolume * b.volume, b.pistolPitch, 0.04f, 0.6f, 250f);
                    else ASHSfx.Play("rifleShot", b.rifleShot, at, b.gunVolume * b.volume, 1f, 0.03f, 0.6f, 400f);
                }
                // one flash per shot: Invector's own muzzle particles when the weapon has them (shotgun), else the ASH flash
                bool invFx = w.emittShurykenParticle != null && System.Array.Exists(w.emittShurykenParticle, e => e);
                if (!invFx && w.muzzle) MuzzleFlash(ASHCombatTuning.Live.muzzleFlash, w.muzzle);
                if (CurrentData) ASHCameraKick.Kick(CurrentData.cameraKick, Random.Range(-0.4f, 0.4f));
            }
        }

        void OnEmpty(vShooterWeapon w)
        {
            if (!w || w != _gun) return;
            var b = ASHSoundBank.Instance;
            if (b && !w.emptyClip) ASHSfx.Play("dryFire", b.dryFire, w.muzzle ? w.muzzle.position : transform.position, b.gunVolume * b.volume, 1f, 0.05f, 0.6f);
            if (_cur && _cur.ammoItem && PackCount(_cur.ammoItem) <= 0) _eq.Say("No " + _cur.ammoItem.displayName);
        }

        void OnReloaded(vShooterWeapon w) { if (w && w == _gun && _cur && _cur.ammoItem) _eq.Say($"{_cur.displayName}: {w.ammo} loaded, {PackCount(_cur.ammoItem)} {_cur.ammoItem.displayName} in pack"); }

        void OnProjectile(vProjectileControl p)
        {
            if (!p || !_gun) return;
            StripTracers(p);
            bool bow = CurrentData ? CurrentData.kind == ASHWeaponKind.Bow : _gun.chargeWeapon;
            p.onCastCollider.AddListener(hit => ASHHitMarks.Spawn(hit.point, hit.normal, hit.collider, bow ? ASHHitKind.Arrow : ASHHitKind.Bullet));
            if (!bow) return;
            var arrow = p.GetComponent<vArrow>();
            float pen = CurrentData ? Mathf.Lerp(CurrentData.minPenetration, CurrentData.maxPenetration, _gun.powerCharge) : 0.08f;
            if (arrow) arrow.penetration = pen;
            var rec = p.GetComponentInChildren<ASHArrowRecover>(true); var item = _cur ? _cur.ammoItem : null;
            if (rec) rec.arrowItem = item;
            // ASH sticks the arrow itself (parented to the hit collider, then a pickup); Invector's vArrow detach is switched off
            var ev = p.onDestroyProjectile;
            for (int i = 0; i < ev.GetPersistentEventCount(); i++) if (ev.GetPersistentTarget(i) is vArrow) ev.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            var vis = arrow && arrow.detachObject ? arrow.detachObject : rec ? rec.transform : null;
            // the Invector projectile root is scaled 0.1, which shrank the ASH arrow model to ~8 cm (invisible in flight and stuck):
            // the visual is brought back to real size (ASHCombatTuning.arrowVisualScale x the model's ~0.83 m)
            if (vis) { float rs = Mathf.Max(1e-4f, vis.parent ? vis.parent.lossyScale.x : 1f); vis.localScale = Vector3.one * (ASHCombatTuning.Live.arrowVisualScale / rs); }
            ev.AddListener(hit => StickArrow(p, vis, rec, item, pen, hit));
        }

        static void StickArrow(vProjectileControl p, Transform vis, ASHArrowRecover rec, ASHItemDefinition item, float pen, RaycastHit hit)
        {
            if (!p || !vis || !hit.collider) return;
            var rb = p.GetComponent<Rigidbody>();
            var dir = rb && rb.linearVelocity.sqrMagnitude > 1f ? rb.linearVelocity.normalized : p.transform.forward;
            var turn = Quaternion.FromToRotation(p.transform.forward, dir);
            // moving / physics targets carry the arrow; static scenery does not (a non-uniformly scaled parent would shear the arrow mesh)
            var col = hit.collider; var hitRb = col.attachedRigidbody; Transform parent = hitRb ? hitRb.transform : col.transform;
            if (!hitRb && (col.gameObject.isStatic || !UniformScale(parent.lossyScale))) parent = null;
            vis.SetParent(parent, true);
            vis.SetPositionAndRotation(hit.point + dir * pen, turn * vis.rotation);   // ASH arrow visual: tip at its origin, pointing +Z
            if (!rec) rec = vis.GetComponentInChildren<ASHArrowRecover>(true);
            if (!rec) rec = vis.gameObject.AddComponent<ASHArrowRecover>();
            rec.Stick(item);
        }

        /// <summary>No tracer lines: trails, line renderers and particle streams on Invector projectiles are removed at spawn.</summary>
        static void StripTracers(vProjectileControl p)
        {
            p.trail = null;
            foreach (var tr in p.GetComponentsInChildren<TrailRenderer>(true)) { tr.emitting = false; tr.Clear(); Destroy(tr); }
            foreach (var lr in p.GetComponentsInChildren<LineRenderer>(true)) Destroy(lr);
            foreach (var ps in p.GetComponentsInChildren<ParticleSystem>(true)) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); var r = ps.GetComponent<ParticleSystemRenderer>(); if (r) r.enabled = false; }
        }

        /// <summary>One short muzzle flash: the prefab's particles are emitted once (never looped), its light shows for muzzleFlashSeconds.</summary>
        static void MuzzleFlash(GameObject fx, Transform muzzle)
        {
            if (!fx || !muzzle) return;
            var t = ASHCombatTuning.Live;
            var g = Instantiate(fx, muzzle.position, muzzle.rotation, muzzle);
            // WFX flicker / auto-loop helpers go; URP's light data stays (it requires the Light, so removing either throws)
            foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true)) { var ns = mb ? mb.GetType().Namespace : null; if (mb && (ns == null || !ns.StartsWith("UnityEngine.Rendering"))) Destroy(mb); }
            float life = 0.05f;
            foreach (var ps in g.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var m = ps.main; m.loop = false; m.playOnAwake = false;
                var em = ps.emission; em.enabled = false;
                ps.Play(false); ps.Emit(Mathf.Max(1, t.muzzleFlashParticles));
                life = Mathf.Max(life, m.startLifetime.constantMax);
            }
            // the flash light is switched off after muzzleFlashSeconds (not destroyed: "Can't remove Light because UniversalAdditionalLightData depends on it")
            var lights = g.GetComponentsInChildren<Light>(true);
            if (lights.Length > 0) { foreach (var l in lights) l.enabled = true; g.AddComponent<ASHFlashLightOff>().Init(lights, t.muzzleFlashSeconds); }
            Destroy(g, Mathf.Clamp(life + 0.05f, 0.1f, 0.6f));
        }

        void OnStartDamage(vDamage d)
        {
            if (d == null) return;
            var t = ASHCombatTuning.Live;
            float h01 = _stats && _stats.MaxHealth > 0f ? _stats.Health / _stats.MaxHealth : 1f;
            float chance = Mathf.Lerp(t.playerStaggerChanceMax, t.playerStaggerChanceMin, h01);
            bool stagger = Time.time >= _staggerImmuneUntil && Random.value < chance;
            d.hitReaction = d.hitReaction && stagger;
            if (stagger) { _staggerImmuneUntil = Time.time + t.playerStaggerImmunitySeconds; if (_cc && t.playerStaggerStaminaCost > 0f) _cc.ReduceStamina(t.playerStaggerStaminaCost, false); }
        }

        void OnDamage(vDamage d)
        {
            if (d == null || !_stats) return;
            _stats.Damage(d.damageValue * ASHCombatTuning.Live.incomingDamage);
        }

        /// <summary>Adds Invector's Aiming / CrouchingAiming / AimingScopeView camera states to a runtime copy of the player's
        /// camera list (framed like ASH's over-the-shoulder aim). The Invector assets are never edited.</summary>
        Invector.vThirdPersonCameraState _camAimSt, _camCrouchAimSt; Invector.vThirdPersonCameraListData _camList;
        /// <summary>Bow aim camera: aimCameraRight / aimCameraHeight minus the bow offsets (left / down) while the bow is held; guns keep the base.</summary>
        void BowCameraOffsets()
        {
            var cam = _in ? _in.tpCamera : null; if (!cam || !cam.CameraStateList || !_aim) return;
            if (_camList != cam.CameraStateList) { _camList = cam.CameraStateList; _camAimSt = _camList.tpCameraStates.Find(s => s.Name == "Aiming"); _camCrouchAimSt = _camList.tpCameraStates.Find(s => s.Name == "CrouchingAiming"); }
            bool bow = _bowRig && _gun;
            float r = _aim.aimCameraRight - (bow ? _aim.bowCameraLeft : 0f), h = _aim.aimCameraHeight - (bow ? _aim.bowCameraDown : 0f);
            if (_camAimSt != null) { _camAimSt.right = r; _camAimSt.height = h; }
            if (_camCrouchAimSt != null) { _camCrouchAimSt.right = r; _camCrouchAimSt.height = h * 0.7f; }
        }
        bool EnsureCameraStates()
        {
            var cam = _in ? _in.tpCamera : null; if (!cam || !cam.CameraStateList) return false;
            var src = ASHCombatTuning.Live.shooterCameraStates;
            var list = cam.CameraStateList;
            if (list.tpCameraStates.Exists(s => s.Name == "Aiming")) return true;
            if (!list.name.Contains("(ASH runtime)")) { list = Instantiate(list); list.name = cam.CameraStateList.name + " (ASH runtime)"; }
            var def = list.tpCameraStates.Find(s => s.Name == "Default") ?? list.tpCameraStates[0];
            foreach (var n in new[] { "Aiming", "CrouchingAiming", "AimingScopeView" })
            {
                var from = src ? src.tpCameraStates.Find(s => s.Name == n) : null;
                var st = new vThirdPersonCameraState(n);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(from ?? def), st); st.Name = n;
                if (_aim && n != "AimingScopeView") { st.right = _aim.aimCameraRight; st.height = n == "CrouchingAiming" ? _aim.aimCameraHeight * 0.7f : _aim.aimCameraHeight; st.defaultDistance = _aim.aimCameraDistance; st.fov = _aim.aimCameraFov; st.maxDistance = Mathf.Max(st.maxDistance, _aim.aimCameraDistance + 0.5f); st.minDistance = Mathf.Min(st.minDistance, 0.3f); }
                list.tpCameraStates.Add(st);
            }
            if (list != cam.CameraStateList) cam.ChangeStateList(list);
            return true;
        }

        static bool UniformScale(Vector3 s) { float a = Mathf.Abs(s.x), b = Mathf.Abs(s.y), c = Mathf.Abs(s.z), mx = Mathf.Max(a, Mathf.Max(b, c)); return mx > 1e-5f && Mathf.Min(a, Mathf.Min(b, c)) / mx > 0.98f; }

        // Aim jitter fix: Invector runs the player Animator in the physics step (AnimatorUpdateMode.Fixed) and vShooterMeleeInput
        // only updates its aim IK on frames that had a physics step, so at render rates above 50 Hz the aimed arms and weapon
        // stepped against the per-frame camera and interpolated body. While aiming (ASHCombatTuning) the Animator runs per frame
        // (Normal), which also makes Invector's aim IK run every frame; the old mode comes back when aiming ends (Genesis fix).
        ASH.Player.ASHBowUprightLegs _legs;
        /// <summary>Stands the legs up while the bow is aimed and the player is still (Invector's bow aim idle bends the knees).</summary>
        void UpdateBowLegs()
        {
            if (!_anim) return;
            if (!_legs) { _legs = _anim.GetComponent<ASH.Player.ASHBowUprightLegs>(); if (!_legs) _legs = _anim.gameObject.AddComponent<ASH.Player.ASHBowUprightLegs>(); }
            var cc = _cc;
            bool still = cc && cc.isGrounded && !cc.isCrouching && cc.input.sqrMagnitude < 0.01f;
            _legs.standingStill = still;
            _legs.targetWeight = _bowRig && _gun && _in && _in.IsAiming && still ? ASHCombatTuning.Live.bowUprightLegs : 0f;
        }

        bool _perFrameAnim; AnimatorUpdateMode _animModeBefore = AnimatorUpdateMode.Fixed;
        void UpdateAnimatorMode()
        {
            if (!_anim) return;
            var t = ASHCombatTuning.Live;
            bool want = _gun && (t.perFrameAnimatorWhileRangedOut || (t.perFrameAnimatorWhileAiming && _in && _in.IsAiming));
            if (want)
            {
                if (!_perFrameAnim) { _animModeBefore = _anim.updateMode == AnimatorUpdateMode.Normal ? AnimatorUpdateMode.Fixed : _anim.updateMode; _perFrameAnim = true; }
                if (_anim.updateMode != AnimatorUpdateMode.Normal) _anim.updateMode = AnimatorUpdateMode.Normal;
            }
            else if (_perFrameAnim)
            {
                _perFrameAnim = false;
                if (_anim.updateMode == AnimatorUpdateMode.Normal) _anim.updateMode = _animModeBefore;
            }
        }

        ASHBowIdleRunner _idleRunner;
        void EnsureIdleRunner()
        {
            if (_idleRunner) return;
            _idleRunner = GetComponent<ASHBowIdleRunner>();
            if (!_idleRunner) _idleRunner = gameObject.AddComponent<ASHBowIdleRunner>();
            _idleRunner.bridge = this;
        }

        public void ApplyBowIdle()
        {
            if (!_bowRig) return;
            bool aiming = _in && _in.IsAiming;
            _bowRig.ApplyIdle(_anim, aiming);
        }
    }

    /// <summary>Switches a muzzle flash's lights off after a delay (the flash object itself is destroyed shortly after).</summary>
    public sealed class ASHFlashLightOff : MonoBehaviour
    {
        Light[] _l; float _until;
        public void Init(Light[] lights, float seconds) { _l = lights; _until = Time.time + seconds; }
        void Update() { if (Time.time < _until || _l == null) return; foreach (var l in _l) if (l) l.enabled = false; _l = null; enabled = false; }
    }

    /// <summary>Runs after Invector's animator/IK so a relaxed bow idle can override the raised-arm equipped pose.</summary>
    [DefaultExecutionOrder(3000)]
    public sealed class ASHBowIdleRunner : MonoBehaviour
    {
        public ASHCombatBridge bridge;
        void LateUpdate() { if (bridge) bridge.ApplyBowIdle(); }
    }
}
