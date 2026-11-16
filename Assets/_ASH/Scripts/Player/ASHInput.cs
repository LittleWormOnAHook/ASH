using ASH.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ASH.Player
{
    /// <summary>
    /// Thin Input System layer for ASH. Owns the ASH_InputActions asset (bindings reused from the Genesis
    /// InputSystem_Actions "Player" map) and exposes the few actions ASH needs. Every gameplay reader goes
    /// through here, so UI gating and automated test overrides live in one place.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class ASHInput : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [Tooltip("Mouse delta (pixels) to Invector camera units. 0.1 matches the legacy 'Mouse X' axis.")]
        [Range(0.01f, 1f)] [SerializeField] private float mouseLookScale = 0.1f;
        [Tooltip("Gamepad right stick look speed (camera units per 60 fps frame).")]
        [Range(0.1f, 10f)] [SerializeField] private float gamepadLookSpeed = 2.2f;
        [SerializeField] private bool invertY;

        private InputAction _journal, _map, _breakDown, _lightToggle;
        private InputAction _move, _look, _jump, _sprint, _crouch, _walk, _interact, _inventory, _craft, _pause, _primary, _secondary, _zoom;

        // Automated test overrides (ASH editor play tests). Null = use real input.
        public Vector2? SimMove; public bool? SimSprint; public bool SimJump; public bool SimCrouch; public bool? SimInteractHeld; public bool SimInteractPressed; public bool SimPrimary; public bool SimSecondary; public bool SimInventory; public bool SimCraft; public int SimHotbar = -1;

        public InputActionAsset Actions => actions;
        /// <summary>Test harness: ignore the real keyboard/mouse for gameplay input (Sim* values still apply).</summary>
        public static bool IgnoreDevices;
        public bool GameplayBlocked => ASHUIState.BlocksGameplay || IgnoreDevices;

        private void Awake()
        {
            if (actions == null) actions = CreateDefaultActions();
            var map = actions.FindActionMap("Player", true);
            _move = map.FindAction("Move", true);
            _look = map.FindAction("Look", true);
            _jump = map.FindAction("Jump", true);
            _sprint = map.FindAction("Sprint", true);
            _crouch = map.FindAction("Crouch", true);
            _walk = map.FindAction("WalkToggle", true);
            _interact = map.FindAction("Interact", true);
            _inventory = map.FindAction("Inventory", true);
            _craft = map.FindAction("Craft", true);
            _pause = map.FindAction("Pause", true);
            _primary = map.FindAction("Primary", true);
            _secondary = map.FindAction("Secondary", true);
            _zoom = map.FindAction("Zoom", true);
            _journal = map.FindAction("Journal", false); _map = map.FindAction("Map", false); _breakDown = map.FindAction("BreakDown", false); _lightToggle = map.FindAction("Light", false);
        }

        private void OnEnable() { if (actions != null) actions.Enable(); EnsureCrossMap(); _cross.Enable(); }
        private void OnDisable() { if (actions != null) actions.Disable(); if (_cross != null) _cross.Disable(); }

        // ---------------------------------------------------------------- cross bar (Genesis Hot Cross style)
        // Runtime-only map so the saved ASH_InputActions asset is never modified. Keys Z/X/G/V, gamepad d-pad
        // up/right/left/down (as in Genesis); Craft and Map move from the d-pad to the shoulders at runtime.
        InputActionMap _cross; InputAction _crossWeapon, _crossAmmo, _crossLight, _crossFood, _aimAlt;
        public int SimCross = -1;
        void EnsureCrossMap()
        {
            if (_cross != null) return;
            _cross = new InputActionMap("ASH_Cross");
            _crossWeapon = _cross.AddAction("CrossWeapon", InputActionType.Button, "<Keyboard>/z"); _crossWeapon.AddBinding("<Gamepad>/dpad/up");
            _crossAmmo = _cross.AddAction("CrossAmmo", InputActionType.Button, "<Keyboard>/x"); _crossAmmo.AddBinding("<Gamepad>/dpad/right");
            _crossLight = _cross.AddAction("CrossLight", InputActionType.Button, "<Keyboard>/g"); _crossLight.AddBinding("<Gamepad>/dpad/down"); // BR quadrant
            _crossFood = _cross.AddAction("CrossFood", InputActionType.Button, "<Keyboard>/v"); _crossFood.AddBinding("<Gamepad>/dpad/left"); // BL quadrant
            _aimAlt = _cross.AddAction("AimAlt", InputActionType.Button, "<Gamepad>/leftTrigger");
            MoveOffDpad(_craft, "<Gamepad>/dpad/up", "<Gamepad>/leftShoulder");
            MoveOffDpad(_map, "<Gamepad>/dpad/right", "<Gamepad>/rightShoulder");
        }
        static void MoveOffDpad(InputAction a, string from, string to)
        {
            if (a == null) return;
            for (int i = 0; i < a.bindings.Count; i++) if (a.bindings[i].path == from) a.ApplyBindingOverride(i, to);
        }
        /// <summary>Cross quadrant pressed this frame (0 weapon, 1 ammo, 2 light, 3 food) or -1.</summary>
        public int CrossPressed
        {
            get
            {
                if (SimCross >= 0) { int s = SimCross; SimCross = -1; return s; }
                if (_cross == null || GameplayBlocked) return -1;
                if (_crossWeapon.WasPressedThisFrame()) return 0;
                if (_crossAmmo.WasPressedThisFrame()) return 1;
                if (_crossLight.WasPressedThisFrame()) return 2;
                if (_crossFood.WasPressedThisFrame()) return 3;
                return -1;
            }
        }
        public static readonly string[] CrossKeys = { "Z", "X", "G", "V" };

        public Vector2 Move => SimMove ?? (GameplayBlocked ? Vector2.zero : Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f));

        /// <summary>Camera rotation delta for this frame in Invector camera units.</summary>
        public Vector2 Look
        {
            get
            {
                if (GameplayBlocked) return Vector2.zero;
                Vector2 v = _look.ReadValue<Vector2>();
                bool pointer = _look.activeControl != null && _look.activeControl.device is Pointer;
                v = pointer ? v * mouseLookScale : v * gamepadLookSpeed * (Time.unscaledDeltaTime * 60f);
                v *= ASH.Core.ASHGameSettings.CachedSensitivity;
                if (invertY != ASH.Core.ASHGameSettings.CachedInvertY) v.y = -v.y;
                return v;
            }
        }

        public bool SuppressZoom { get; set; }
        public float Zoom => GameplayBlocked || SuppressZoom ? 0f : _zoom.ReadValue<Vector2>().y / 1200f;
        public bool SprintHeld => SimSprint ?? (!GameplayBlocked && _sprint.IsPressed());
        public bool JumpPressed => Consume(ref SimJump) || (!GameplayBlocked && _jump.WasPressedThisFrame());
        public bool CrouchPressed => Consume(ref SimCrouch) || (!GameplayBlocked && _crouch.WasPressedThisFrame());
        public bool WalkTogglePressed => !GameplayBlocked && _walk.WasPressedThisFrame();
        public bool InteractPressed => Consume(ref SimInteractPressed) || (!GameplayBlocked && _interact.WasPressedThisFrame());
        public bool InteractHeld => SimInteractHeld ?? (!GameplayBlocked && _interact.IsPressed());
        public bool InventoryPressed => Consume(ref SimInventory) || _inventory.WasPressedThisFrame();
        public bool CraftPressed => Consume(ref SimCraft) || _craft.WasPressedThisFrame();
        /// <summary>Test hook: Esc counts as pressed on this frame number (every reader sees it that frame).</summary>
        public int SimPauseFrame = -1;
        public bool PausePressed => SimPauseFrame == Time.frameCount || _pause.WasPressedThisFrame();
        public bool PrimaryPressed => Consume(ref SimPrimary) || (!IgnoreDevices && _primary.WasPressedThisFrame());
        public bool SecondaryPressed => Consume(ref SimSecondary) || (!IgnoreDevices && _secondary.WasPressedThisFrame());
        public bool? SimSecondaryHeld;
        /// <summary>Held Secondary (RMB): aim a torch/flashlight.</summary>
        public bool SecondaryHeld => SimSecondaryHeld ?? (!GameplayBlocked && (_secondary.IsPressed() || (_aimAlt != null && _aimAlt.IsPressed())));
        /// <summary>Index of a hotbar key (1..9 -> 0..8) pressed this frame, or -1.</summary>
        public int HotbarPressed
        {
            get
            {
                if (SimHotbar >= 0) { int s = SimHotbar; SimHotbar = -1; return s; }
                var kb = Keyboard.current; if (kb == null || ASHUIState.IsOpen(ASHPanels.Pause)) return -1;
                for (int i = 0; i < 9; i++) if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) return i;
                return -1;
            }
        }
        public bool SimJournal, SimMap, SimBreakDown, SimLight;
        /// <summary>Toggle the held light (lantern/torch/flashlight). L by default.</summary>
        public bool LightPressed => Consume(ref SimLight) || (!GameplayBlocked && KeyHit(_lightToggle, UnityEngine.InputSystem.Key.L));
        public bool JournalPressed => Consume(ref SimJournal) || KeyHit(_journal, UnityEngine.InputSystem.Key.J);
        public bool MapPressed => Consume(ref SimMap) || KeyHit(_map, UnityEngine.InputSystem.Key.M);
        public bool BreakDownPressed => Consume(ref SimBreakDown) || KeyHit(_breakDown, UnityEngine.InputSystem.Key.B);
        static bool KeyHit(InputAction a, UnityEngine.InputSystem.Key fallback)
        {
            if (a != null) return a.WasPressedThisFrame();
            var kb = Keyboard.current; return kb != null && kb[fallback].wasPressedThisFrame;
        }
        public float RawZoom => _zoom.ReadValue<Vector2>().y;

        /// <summary>Short display name of the first binding of an action, e.g. "E".</summary>
        public string BindingLabel(string actionName)
        {
            var a = actions != null ? actions.FindAction(actionName) : null;
            return a == null ? "?" : a.GetBindingDisplayString(0, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }

        /// <summary>
        /// ASH default bindings (keyboard/mouse + gamepad), reused from the Genesis InputSystem_Actions "Player" map
        /// where actions overlap. The editor saves this as Assets/_ASH/Settings/Input/ASH_InputActions.inputactions.
        /// </summary>
        public static InputActionAsset CreateDefaultActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "ASH_InputActions";
            var map = asset.AddActionMap("Player");
            var move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/leftStick");
            var look = map.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
            look.AddBinding("<Pointer>/delta"); look.AddBinding("<Gamepad>/rightStick");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space").AddBinding("<Gamepad>/buttonSouth");
            map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift").AddBinding("<Gamepad>/leftStickPress");
            var crouch = map.AddAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl"); crouch.AddBinding("<Keyboard>/c"); crouch.AddBinding("<Gamepad>/rightStickPress");
            map.AddAction("WalkToggle", InputActionType.Button, "<Keyboard>/capsLock");
            map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e").AddBinding("<Gamepad>/buttonWest");
            var inv = map.AddAction("Inventory", InputActionType.Button, "<Keyboard>/tab"); inv.AddBinding("<Keyboard>/i"); inv.AddBinding("<Gamepad>/buttonNorth");
            map.AddAction("Craft", InputActionType.Button, "<Keyboard>/q").AddBinding("<Gamepad>/leftShoulder");
            map.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape").AddBinding("<Gamepad>/start");
            map.AddAction("Journal", InputActionType.Button, "<Keyboard>/j").AddBinding("<Gamepad>/select");
            map.AddAction("Map", InputActionType.Button, "<Keyboard>/m").AddBinding("<Gamepad>/rightShoulder");
            map.AddAction("BreakDown", InputActionType.Button, "<Keyboard>/b");
            map.AddAction("Light", InputActionType.Button, "<Keyboard>/l");
            map.AddAction("Primary", InputActionType.Button, "<Mouse>/leftButton").AddBinding("<Gamepad>/rightTrigger");
            map.AddAction("Secondary", InputActionType.Button, "<Mouse>/rightButton").AddBinding("<Gamepad>/buttonEast");
            var zoom = map.AddAction("Zoom", InputActionType.Value, expectedControlLayout: "Vector2"); zoom.AddBinding("<Mouse>/scroll");
            return asset;
        }

        private static bool Consume(ref bool flag) { bool v = flag; flag = false; return v; }
    }
}
