using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Inventory;
using ASH.Player;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// What the player carries ready: a main-hand item (rifle, pistol, hatchet; the bow sits in the LEFT hand), an off-hand light
    /// in the LEFT hand (torch, flashlight) and a lantern hung from the LEFT HIP.
    /// Held visuals are PRE-PLACED on ASH_Player in edit mode (like Genesis Drawn_ slots): "Held_&lt;Item&gt;" children under the hand
    /// bones and "Holder_Hip/Held_Lantern" under Hips (ASH/Build/Held Items On Player adds missing ones, never moves placed ones).
    /// At runtime they are only switched on/off (all forced off at Start); their transforms are never written. An item with no
    /// pre-placed child falls back to spawning at its item grip (with a warning). Runs light
    /// fuel/burn time and a stub weapon use (Primary spends one round/arrow; no combat system). Fuel is remembered per
    /// item type across unequips. Light key (L): toggles the hand light, else the hip lantern, else readies the first carried light.
    /// Arm raise/aim (RMB) for hand lights: ASHLightAim.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1600)]
    public sealed class ASHEquipment : MonoBehaviour
    {
        [SerializeField] private ASHInventory inventory;
        [SerializeField] private ASHInput input;
        [SerializeField] private Animator animator;
        [Tooltip("Muzzle flash light length for the stub weapon use (seconds).")]
        [Range(0f, 0.3f)] public float muzzleFlashSeconds = 0.06f;
        [Tooltip("Muzzle flash light intensity (kept low so bloom doesn't blow out).")]
        [Range(0f, 8f)] public float muzzleFlashIntensity = 1.2f;

        [Header("Lantern on the left hip (fallback spawn uses hipOffset/hipScale; pre-placed Holder_Hip is authored by hand)")]
        [Tooltip("Pre-placed lantern: swing by rotating Holder_Hip around its authored pose (the lantern itself is never moved).")]
        public bool hipSwing = true;
        [Tooltip("Hook position from the hips bone in character space (x right, y up, z forward; metres).")]
        public Vector3 hipOffset = new Vector3(-0.19f, -0.06f, 0.03f);
        [Range(0.2f, 1.5f)] public float hipScale = 0.6f;
        [Tooltip("Pendulum spring toward hanging straight down.")]
        [Range(1f, 200f)] public float swingStiffness = 45f;
        [Range(0f, 30f)] public float swingDamping = 5f;
        [Tooltip("How much the hip's acceleration pushes the lantern.")]
        [Range(0f, 0.2f)] public float swingResponse = 0.035f;
        [Range(0f, 80f)] public float swingMaxAngle = 35f;

        public ASHItemDefinition Main { get; private set; }
        public ASHItemDefinition Off { get; private set; }
        public ASHItemDefinition Hip { get; private set; }
        public GameObject MainVisual => _mainGo;
        public GameObject OffVisual => _offGo;
        public GameObject HipVisual => _hipGo;
        public bool HandLightOn => _offLight && _offLight.On;
        public bool HipLightOn => _hipLight && _hipLight.On;
        public bool LightOn => HandLightOn || HipLightOn;
        public int ShotsFired { get; private set; }
        public Animator Animator => animator;
        /// <summary>Short player-facing messages (HUD toasts).</summary>
        public event Action<string> Message;
        public event Action Changed;

        public const string HeldPrefix = "Held_", HipHolderName = "Holder_Hip", HandArrowName = "Held_HandArrow";
        /// <summary>Name of an item's pre-placed child: Held_ + item asset name without "ASH_Item_" (Held_WoodenBow, Held_Hatchet...).</summary>
        public static string HeldName(ASHItemDefinition item) => item == null ? "" : HeldPrefix + (item.name.StartsWith("ASH_Item_") ? item.name.Substring(9) : item.name);
        readonly Dictionary<string, GameObject> _placedByName = new Dictionary<string, GameObject>();
        Transform _hipHolder; Quaternion _hipHolderRot; bool _mainPlaced, _offPlaced, _hipPlaced;
        /// <summary>Pre-placed held child by name (e.g. Held_HandArrow), or null.</summary>
        public GameObject Placed(string name) => name != null && _placedByName.TryGetValue(name.ToLowerInvariant(), out var g) && g ? g : null;
        public GameObject Placed(ASHItemDefinition item) => Placed(HeldName(item)) ?? (item ? Placed(HeldPrefix + item.itemId) : null);
        public int PlacedCount => _placedByName.Count;
        public IEnumerable<GameObject> AllPlaced => _placedByName.Values;
        GameObject _mainGo, _offGo, _hipGo; Transform _hipPivot; ASHLightSource _offLight, _hipLight; ASHPlacer _placer;
        Light _flash; float _flashUntil;
        Vector3 _hipLastPos, _hipVel, _hipDown = Vector3.down, _hipAngVel; bool _hipInit;
        readonly Dictionary<ASHItemDefinition, float> _fuel = new Dictionary<ASHItemDefinition, float>();

        void Awake()
        {
            if (!inventory) inventory = GetComponent<ASHInventory>();
            if (inventory) inventory.IsHeld = x => x != null && (x == Main || x == Off || x == Hip);
            if (!input) input = GetComponent<ASHInput>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            _placer = GetComponent<ASHPlacer>();
            if (animator && !animator.GetComponent<ASHLightAim>()) animator.gameObject.AddComponent<ASHLightAim>();
            if (animator && !animator.GetComponent<ASHActionLayer>()) animator.gameObject.AddComponent<ASHActionLayer>();
            if (animator && !animator.GetComponent<ASHWeaponAim>()) animator.gameObject.AddComponent<ASHWeaponAim>();
            if (animator && !animator.GetComponent<ASHChopAnimator>()) animator.gameObject.AddComponent<ASHChopAnimator>();
            _weaponAim = animator ? animator.GetComponent<ASHWeaponAim>() : null;
            IndexPlaced();
        }

        void IndexPlaced()
        {
            _placedByName.Clear();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == HipHolderName) { _hipHolder = t; _hipHolderRot = t.localRotation; continue; }
                if (!t.name.StartsWith(HeldPrefix)) continue;
                _placedByName[t.name.ToLowerInvariant()] = t.gameObject;
                foreach (var c in t.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }
        }

        /// <summary>Everything pre-placed starts hidden, even if left enabled in the saved prefab; equipped ones stay on.</summary>
        void Start()
        {
            foreach (var g in _placedByName.Values) if (g && g != _mainGo && g != _offGo && g != _hipGo && !(_weaponAim && g == _weaponAim.HandArrow)) g.SetActive(false);
        }
        void OnEnable() { if (inventory) inventory.Changed += Validate; }
        void OnDisable() { if (inventory) inventory.Changed -= Validate; }

        public bool IsEquipped(ASHItemDefinition item) => item != null && (item == Main || item == Off || item == Hip);

        public bool ToggleEquip(ASHItemDefinition item)
        {
            if (item == null || !item.IsEquippable) return false;
            if (IsEquipped(item)) { Unequip(item.equipSlot); Say("Put away " + item.displayName); return true; }
            return Equip(item);
        }

        public bool Equip(ASHItemDefinition item)
        {
            if (item == null || !item.IsEquippable || inventory == null || inventory.Count(item) <= 0) return false;
            Unequip(item.equipSlot);
            // bow takes both hands: bow in the LEFT hand, arrow in the RIGHT (the bow still lives in the main/weapon slot)
            if (item.equipSlot == ASHEquipSlot.MainHand && IsBow(item) && Off) Unequip(ASHEquipSlot.OffHand);
            if (item.equipSlot == ASHEquipSlot.OffHand && IsBow(Main)) Unequip(ASHEquipSlot.MainHand);
            switch (item.equipSlot)
            {
                case ASHEquipSlot.MainHand: Main = item; _mainGo = Show(item, out _mainPlaced); break;
                case ASHEquipSlot.OffHand: Off = item; _offGo = Show(item, out _offPlaced); _offLight = _offGo ? _offGo.GetComponentInChildren<ASHLightSource>(true) : null; break;
                case ASHEquipSlot.Hip: Hip = item; _hipGo = ShowHip(item); _hipLight = _hipGo ? _hipGo.GetComponentInChildren<ASHLightSource>(true) : null; break;
            }
            Say((item.equipSlot == ASHEquipSlot.Hip ? "Hung " : "Holding ") + item.displayName);
            if (item.IsLightSource) SetLight(item, true);
            Changed?.Invoke();
            return true;
        }

        public void Unequip(ASHEquipSlot slot)
        {
            switch (slot)
            {
                case ASHEquipSlot.MainHand: Hide(_mainGo, _mainPlaced); _mainGo = null; _mainPlaced = false; Main = null; break;
                case ASHEquipSlot.OffHand: if (_offLight && _offPlaced) _offLight.SetOn(false); Hide(_offGo, _offPlaced); _offGo = null; _offPlaced = false; _offLight = null; Off = null; break;
                case ASHEquipSlot.Hip:
                    if (_hipPlaced) { if (_hipLight) _hipLight.SetOn(false); Hide(_hipGo, true); if (_hipHolder) _hipHolder.localRotation = _hipHolderRot; }
                    else if (_hipPivot) Destroy(_hipPivot.gameObject);
                    _hipPivot = null; _hipGo = null; _hipPlaced = false; _hipLight = null; Hip = null; break;
            }
            Changed?.Invoke();
        }

        GameObject Show(ASHItemDefinition item, out bool placed)
        {
            var g = Placed(item); placed = g != null;
            if (g) { g.SetActive(true); return g; }
            Debug.LogWarning($"[ASH] Equipment: no pre-placed {HeldName(item)} on the player; spawning {item.displayName} at its item grip. Run ASH/Build/Held Items On Player to add it, then place it by hand.");
            return SpawnInHand(item);
        }

        GameObject ShowHip(ASHItemDefinition item)
        {
            var g = Placed(item);
            if (g) { _hipPlaced = true; g.SetActive(true); _hipInit = false; return g; }
            _hipPlaced = false;
            Debug.LogWarning($"[ASH] Equipment: no pre-placed {HeldName(item)} on the player; spawning {item.displayName} on the hip. Run ASH/Build/Held Items On Player to add it, then place it by hand.");
            return SpawnOnHip(item);
        }

        static void Hide(GameObject g, bool placed) { if (!g) return; if (placed) g.SetActive(false); else Destroy(g); }

        GameObject SpawnInHand(ASHItemDefinition item)
        {
            if (!item.heldPrefab) return null;
            Transform bone = null;
            if (animator && animator.isHuman) bone = animator.GetBoneTransform(InLeftHand(item) ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (!bone) bone = transform;
            var go = Instantiate(item.heldPrefab);
            go.name = "Held_" + item.itemId;
            if (GripFrame(item, out var pos, out var rot))
            {
                go.transform.SetPositionAndRotation(pos + rot * item.holdPosition, rot * Quaternion.Euler(item.holdEuler));
                go.transform.SetParent(bone, true);
            }
            else
            {
                go.transform.SetParent(bone, false);
                go.transform.localPosition = item.holdPosition; go.transform.localRotation = Quaternion.Euler(item.holdEuler);
            }
            UnitScale(go.transform, item.heldScale > 0f ? item.heldScale : 1f);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            return go;
        }

        GameObject SpawnOnHip(ASHItemDefinition item)
        {
            if (!item.heldPrefab) return null;
            _hipPivot = new GameObject("LanternHook").transform; _hipPivot.SetParent(transform, false);
            var go = Instantiate(item.heldPrefab, _hipPivot, false);
            go.name = "Hip_" + item.itemId;
            go.transform.localPosition = item.holdPosition; go.transform.localRotation = Quaternion.Euler(item.holdEuler);
            _hipInit = false; HipUpdate(0f);
            UnitScale(go.transform, hipScale);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            return go;
        }

        static void UnitScale(Transform t, float s)
        {
            t.localScale = Vector3.one; var ls = t.lossyScale;
            t.localScale = new Vector3(s / Mathf.Max(ls.x, 1e-4f), s / Mathf.Max(ls.y, 1e-4f), s / Mathf.Max(ls.z, 1e-4f));
        }

        Vector3 HipAnchor()
        {
            var hips = animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            var basePos = hips ? hips.position : transform.position + Vector3.up;
            return basePos + transform.TransformDirection(hipOffset);
        }

        /// <summary>Hook follows the hip; the lantern hangs world-down on a damped spring pushed by the hip's acceleration.</summary>
        void HipUpdate(float dt)
        {
            bool placedSwing = _hipPlaced && hipSwing && _hipHolder && _hipHolder.parent && _hipGo && _hipGo.transform.IsChildOf(_hipHolder);
            if (!_hipPivot && !placedSwing) return;
            var p = placedSwing ? _hipHolder.position : HipAnchor();
            if (!_hipInit || dt <= 0f) { _hipLastPos = p; _hipVel = Vector3.zero; _hipDown = Vector3.down; _hipAngVel = Vector3.zero; _hipInit = true; }
            else
            {
                var vel = (p - _hipLastPos) / dt; var acc = (vel - _hipVel) / dt; _hipVel = vel; _hipLastPos = p;
                var desired = (Vector3.down - Vector3.ClampMagnitude(acc * swingResponse, 1.5f)).normalized;
                _hipAngVel += (desired - _hipDown) * swingStiffness * dt;
                _hipAngVel *= Mathf.Exp(-swingDamping * dt);
                _hipDown = (_hipDown + _hipAngVel * dt).normalized;
                float ang = Vector3.Angle(Vector3.down, _hipDown);
                if (ang > swingMaxAngle) _hipDown = Vector3.Slerp(Vector3.down, _hipDown, swingMaxAngle / ang).normalized;
            }
            if (placedSwing) { _hipHolder.rotation = Quaternion.FromToRotation(Vector3.down, _hipDown) * (_hipHolder.parent.rotation * _hipHolderRot); return; }
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            _hipPivot.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.down, _hipDown) * Quaternion.LookRotation(fwd, Vector3.up));
        }

        void LateUpdate() => HipUpdate(Time.deltaTime);

        /// <summary>World grip pose from the humanoid hand ('along' = wrist -> middle knuckle) and the player's facing.
        /// Held prefabs have their grip at the origin with the long axis on +Z (head end forward), Hang items the handle at the origin, +Y up.</summary>
        bool GripFrame(ASHItemDefinition item, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (!animator || !animator.isHuman) return false;
            bool left = InLeftHand(item);
            var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            var middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (!hand || !index || !little || !middle) return false;
            Vector3 across = (index.position - little.position).normalized, along = (middle.position - hand.position).normalized;
            if (across.sqrMagnitude < 0.5f || along.sqrMagnitude < 0.5f) return false;
            Vector3 palm = Vector3.Cross(along, across).normalized * (left ? -1f : 1f);
            pos = Vector3.Lerp(hand.position, middle.position, 0.7f) + palm * 0.03f;
            Vector3 fwd = transform.forward, up = -along;
            rot = item.gripStyle == ASHGripStyle.Handle ? Quaternion.LookRotation(up, fwd) : Quaternion.LookRotation(fwd, up);
            return true;
        }

        // ---------------------------------------------------------------- light + fuel
        public float Capacity(ASHItemDefinition item) => item == null ? 0f : item.fuelItem ? item.fuelSecondsPerUnit * item.fuelCapacityUnits : item.fuelSecondsPerUnit;
        /// <summary>Seconds of light left. A fresh lantern/flashlight has one unit in it; a fresh torch is a full burn.</summary>
        public float Fuel(ASHItemDefinition item) => item == null ? 0f : _fuel.TryGetValue(item, out var f) ? f : item.fuelSecondsPerUnit;
        public float Fuel01(ASHItemDefinition item) { float c = Capacity(item); return c > 0f ? Mathf.Clamp01(Fuel(item) / c) : 0f; }
        public void SetFuel(ASHItemDefinition item, float seconds) { if (item) _fuel[item] = Mathf.Clamp(seconds, 0f, Capacity(item)); }

        ASHLightSource LightOf(ASHItemDefinition item) => item == null ? null : item == Off ? _offLight : item == Hip ? _hipLight : null;
        public bool IsLit(ASHItemDefinition item) { var l = LightOf(item); return l && l.On; }

        /// <summary>L: hand light first, then the hip lantern; with neither ready, readies the first carried light (lantern to the hip).</summary>
        public bool ToggleLight()
        {
            if (Off && Off.IsLightSource) return SetLight(Off, !HandLightOn);
            if (Hip && Hip.IsLightSource) return SetLight(Hip, !HipLightOn);
            if (inventory == null) return false;
            var cross = inventory.CrossSlot(ASHInventory.CrossQuad.Light);
            if (!cross.IsEmpty && cross.item.IsEquippable) return Equip(cross.item);
            foreach (var s in inventory.Slots) if (!s.IsEmpty && s.item.IsLightSource && s.item.IsEquippable) return Equip(s.item);
            Say("No light to turn on"); return false;
        }

        public bool SetLight(bool on) => Off && Off.IsLightSource ? SetLight(Off, on) : Hip && SetLight(Hip, on);

        public bool SetLight(ASHItemDefinition item, bool on)
        {
            var l = LightOf(item); if (item == null || !item.IsLightSource) return false;
            if (on && Fuel(item) <= 0.01f && !Refuel(item, true)) { Say($"{item.displayName}: no {(item.fuelItem ? item.fuelItem.displayName : "fuel")}"); on = false; }
            if (l) l.SetOn(on);
            Say(item.displayName + (on ? " on" : " off"));
            Changed?.Invoke();
            return on;
        }

        /// <summary>Pours one fuel unit from the pack into a light (lantern: rubbing alcohol; flashlight: battery).</summary>
        public bool Refuel(ASHItemDefinition light, bool quiet = false)
        {
            if (light == null || light.fuelItem == null || inventory == null) return false;
            float f = Fuel(light);
            if (f > Capacity(light) - light.fuelSecondsPerUnit + 0.5f) { if (!quiet) Say(light.displayName + " is full"); return false; }
            if (!inventory.Remove(light.fuelItem, 1)) { if (!quiet) Say("No " + light.fuelItem.displayName); return false; }
            _fuel[light] = f + light.fuelSecondsPerUnit;
            Say($"Filled {light.displayName} with {light.fuelItem.displayName}");
            return true;
        }

        /// <summary>Using a fuel item from the pack refuels a carried light that takes it (readied ones first).</summary>
        public bool TryRefuelWith(ASHItemDefinition fuel)
        {
            if (fuel == null || inventory == null) return false;
            if (Off && Off.fuelItem == fuel) return Refuel(Off);
            if (Hip && Hip.fuelItem == fuel) return Refuel(Hip);
            foreach (var s in inventory.Slots) if (!s.IsEmpty && s.item.fuelItem == fuel) return Refuel(s.item);
            return false;
        }

        void Burn(ASHItemDefinition item, ASHEquipSlot slot)
        {
            var l = LightOf(item); if (item == null || !l || !l.On) return;
            float f = Fuel(item) - Time.deltaTime;
            if (f > 0f) { _fuel[item] = f; return; }
            if (item.fuelItem)
            {
                _fuel[item] = 0f;
                if (!Refuel(item, true)) { l.SetOn(false); Say($"{item.displayName} ran dry: needs {item.fuelItem.displayName}"); Changed?.Invoke(); }
                return;
            }
            _fuel[item] = item.fuelSecondsPerUnit; // torch: burns itself
            inventory.Remove(item, 1); Say(item.displayName + " burned out");
            if (inventory.Count(item) <= 0) Unequip(slot); else { var l2 = LightOf(item); if (l2) l2.SetOn(true); }
        }

        void Update()
        {
            Burn(Off, ASHEquipSlot.OffHand);
            Burn(Hip, ASHEquipSlot.Hip);
            if (_flash && Time.time > _flashUntil) _flash.enabled = false;

            if (input == null || ASHUIState.BlocksGameplay || (_placer && _placer.IsPlacing)) return;
            if (input.LightPressed) ToggleLight();
            if (input.PrimaryPressed && Main && !(CombatOwnsMain != null && CombatOwnsMain(Main))) { if (_weaponAim) _weaponAim.Fire(); else UseMain(); }
        }

        /// <summary>Stub weapon use: spends one round/arrow with a muzzle flash. No damage, no combat system.</summary>
        public bool UseMain()
        {
            if (Main == null || Main.ammoItem == null || inventory == null) return false;
            bool arrow = Main.ammoItem.itemId.Contains("arrow");
            if (!inventory.Remove(Main.ammoItem, 1)) { Say(arrow ? "No arrows" : "Click. No " + Main.ammoItem.displayName); return false; }
            ShotsFired++;
            if (_mainGo && muzzleFlashSeconds > 0f && !arrow)
            {
                if (!_flash) { var g = new GameObject("MuzzleFlash"); _flash = g.AddComponent<Light>(); _flash.type = LightType.Point; _flash.range = 5f; _flash.color = new Color(1f, 0.8f, 0.5f); }
                _flash.transform.SetParent(_mainGo.transform, false); _flash.transform.localPosition = Vector3.forward * 0.5f;
                _flash.intensity = muzzleFlashIntensity; _flash.enabled = true; _flashUntil = Time.time + muzzleFlashSeconds;
            }
            Say($"{Main.displayName}: {inventory.Count(Main.ammoItem)} {Main.ammoItem.displayName} left");
            return true;
        }

        void Validate()
        {
            if (inventory == null) return;
            if (Main && inventory.Count(Main) <= 0) Unequip(ASHEquipSlot.MainHand);
            if (Off && inventory.Count(Off) <= 0) Unequip(ASHEquipSlot.OffHand);
            if (Hip && inventory.Count(Hip) <= 0) Unequip(ASHEquipSlot.Hip);
        }

        /// <summary>Set by ASH.Combat.ASHCombatBridge: true when the Invector combat layer handles LMB for this item (melee weapon,
        /// Invector gun or bow), so the old stub fire path is skipped.</summary>
        public Func<ASHItemDefinition, bool> CombatOwnsMain;
        /// <summary>Spends ammo from the pack for the combat layer (one shot / arrow).</summary>
        public bool SpendAmmo(ASHItemDefinition ammo, int n = 1) { if (ammo == null || inventory == null || n <= 0) return false; bool ok = inventory.Remove(ammo, n); if (ok) ShotsFired += n; return ok; }
        public int AmmoCount(ASHItemDefinition ammo) => ammo == null || inventory == null ? 0 : inventory.Count(ammo);
        public ASHInventory Inventory => inventory;

        public void Say(string m) => Message?.Invoke(m);
        ASHWeaponAim _weaponAim;
        public static bool IsBow(ASHItemDefinition it) => ASHWeaponAim.KindOf(it) == ASHWeaponAim.Kind.Bow;
        /// <summary>Off-hand items and the bow are held in the left hand; everything else in the right.</summary>
        public static bool InLeftHand(ASHItemDefinition it) => it != null && (it.equipSlot == ASHEquipSlot.OffHand || IsBow(it));
    }
}
