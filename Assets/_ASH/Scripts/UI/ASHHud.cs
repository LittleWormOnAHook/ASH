using System.Collections.Generic;
using ASH.Core;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Map;
using ASH.Inventory;
using ASH.Player;
using ASH.Survival;
using ASH.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ASH.UI
{
    /// <summary>
    /// Slice HUD built in code (no prefab/TMP dependency): survival bars, reticle + prompt with hold progress,
    /// objective, toasts, clock, inventory (Tab), crafting (Q), pause (Esc), death and placement hints.
    /// Reuses the Genesis HUD layout idea (bars bottom-left, objective top-left, prompt under the reticle).
    /// </summary>
    public sealed class ASHHud : MonoBehaviour
    {
        [SerializeField] private ASHInput input;
        [SerializeField] private ASHSurvivalStats stats;
        [SerializeField] private ASHInventory inventory;
        [SerializeField] private ASHCrafting crafting;
        [SerializeField] private ASHInteractor interactor;
        [SerializeField] private ASHPlacer placer;
        [SerializeField] private ASHObjectiveTracker objective;
        [SerializeField] private ASHDayNightClock clock;
        [SerializeField] private ASHGpsMap gps;
        [Range(0.5f, 2f)] public float uiScale = 1f;
        [Range(1f, 15f)] public float toastSeconds = 5f;

        static readonly Color Panel = new Color(0.06f, 0.055f, 0.05f, 0.82f);
        static readonly Color Ink = new Color(0.93f, 0.9f, 0.84f);
        static readonly Color Dim = new Color(0.65f, 0.62f, 0.57f);
        static readonly Color Accent = new Color(0.95f, 0.62f, 0.28f);

        Font _font;
        Canvas _canvas;
        RectTransform _root;
        Bar _health, _stamina, _hunger, _thirst;
        float _objRefreshAt;
        Text _prompt, _objTitle, _objLines, _clock, _placeHint, _deathText, _invDesc;
        RectTransform _holdBar, _holdFill, _reticle;
        RectTransform _toastRoot;
        Text _subtitle; float _subUntil = -1f;
        GameObject _invPanel, _pausePanel, _deathPanel;
        RectTransform _pauseMenu; ASHSettingsPanel _settingsPanel; ASHSaveSlotsPanel _slotsPanel; Button _pauseSave;

        /// <summary>Item use hooks (water bottle, wet rag, gas mask, filters, Grandpa's journal): checked before the built-in uses.</summary>
        public interface IItemUseHandler
        {
            bool Handles(ASHItemDefinition item);
            string UseLabel(ASHItemDefinition item);
            /// <summary>slot = inventory slot index (-1 when used from the journal).</summary>
            bool Use(ASHItemDefinition item, int slot);
        }
        public static readonly List<IItemUseHandler> UseHandlers = new List<IItemUseHandler>();
        static IItemUseHandler HandlerFor(ASHItemDefinition it) { if (it == null) return null; foreach (var h in UseHandlers) if (h != null && h.Handles(it)) return h; return null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetHooks() { UseHandlers.Clear(); CraftingAvailable = null; CraftingLockedMessage = null; CraftTabName = null; }
        /// <summary>Crafting gate (Grandpa's Field Manual): null = always available.</summary>
        public static System.Func<bool> CraftingAvailable;
        public static string CraftingLockedMessage;
        /// <summary>Tab label for the crafting tab (null = "CRAFTING").</summary>
        public static string CraftTabName;
        public static bool CanCraftNow => CraftingAvailable == null || CraftingAvailable();
        /// <summary>Frame on which an overlay (cover view...) used Esc, so the pause menu doesn't open with the same press.</summary>
        public static int EscConsumedFrame = -1;
        /// <summary>Use label from a registered handler (null when none handles the item).</summary>
        public static string HandlerLabel(ASHItemDefinition it) { var h = HandlerFor(it); return h != null ? h.UseLabel(it) : null; }
        /// <summary>The HUD canvas root, for systems that add their own HUD elements.</summary>
        public RectTransform Root => _root;
        public bool PauseOpen => _pausePanel && _pausePanel.activeSelf;
        ASHJournalUI _journal; ASHMapUI _map; int _hoverSlot = -1;
        readonly List<SlotUI> _slots = new List<SlotUI>();
        readonly List<(Text text, float until)> _toasts = new List<(Text, float)>();
        float _deathTimer;
        ASHEquipment _equip;

        sealed class Bar { public RectTransform fill; public Image fillImg; public Text label; public Color color; }
        sealed class SlotUI { public int index; public RectTransform rt; public Button button; public Image bg, icon, strip; public Text abbr, count, key; public float pulse; }
        readonly List<SlotUI> _hotbar = new List<SlotUI>();
        readonly List<SlotUI> _cross = new List<SlotUI>();
        RectTransform _crossRoot; Text _crossCenter;
        static readonly Color Gold = new Color(0.86f, 0.68f, 0.34f, 0.9f);
        RectTransform _hotbarRoot;
        Image _dragGhost; int _dragFrom = -1;
        Text _tipTitle, _tipBody;
        static readonly Color SlotEmpty = new Color(0.10f, 0.095f, 0.085f, 0.55f);
        static readonly Color SlotFull = new Color(0.17f, 0.155f, 0.14f, 0.88f);
        const string PackHint = "<color=#d9cbb4>LMB</color> use   <color=#d9cbb4>RMB</color> menu (use, drop, split)   <color=#d9cbb4>B</color> break down   <color=#d9cbb4>Drag</color> move (pack, hotbar, cross bar)   <color=#d9cbb4>J</color> journal   <color=#d9cbb4>Tab</color> close";

        void Awake()
        {
            if (gps == null) gps = FindAnyObjectByType<ASHGpsMap>();
            _equip = inventory ? inventory.GetComponent<ASHEquipment>() : null; if (!_equip) _equip = FindAnyObjectByType<ASHEquipment>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
        }

        void OnEnable()
        {
            if (inventory) { inventory.Changed += RefreshInventory; inventory.ItemAdded += OnItemAdded; }
            if (crafting) { crafting.Crafted += OnCrafted; crafting.CraftFailed += OnCraftFailed; crafting.BrokeDown += OnBrokeDown; crafting.BreakdownFailed += OnBreakFailed; }
            if (gps) gps.SurveyLogged += OnSurvey;
            if (objective) { objective.Message += Toast; objective.Changed += ObjDirty; }
            if (stats) { stats.Died += OnDied; stats.Consumed += OnConsumed; }
            if (_equip) _equip.Message += Toast;
        }

        void OnDisable()
        {
            if (inventory) { inventory.Changed -= RefreshInventory; inventory.ItemAdded -= OnItemAdded; }
            if (crafting) { crafting.Crafted -= OnCrafted; crafting.CraftFailed -= OnCraftFailed; crafting.BrokeDown -= OnBrokeDown; crafting.BreakdownFailed -= OnBreakFailed; }
            if (gps) gps.SurveyLogged -= OnSurvey;
            if (objective) { objective.Message -= Toast; objective.Changed -= ObjDirty; }
            if (stats) { stats.Died -= OnDied; stats.Consumed -= OnConsumed; }
            if (_equip) _equip.Message -= Toast;
        }

        void ObjDirty() => _objRefreshAt = 0f;
        void OnItemAdded(ASHItemDefinition i, int n) { Toast($"+{n} {i.displayName}"); PulseItem(i); }
        void OnCrafted(ASHRecipe r) { Toast("Crafted " + r.displayName + (r.outputAmount > 1 ? $" x{r.outputAmount}" : "")); }
        void OnBrokeDown(ASHItemDefinition i, string res) => Toast($"Broke down {i.displayName}: {res}");
        void OnBreakFailed(ASHItemDefinition i, string why) => Toast($"{(i ? i.displayName : "Item")}: {why}");
        void OnSurvey(ASHSurveyPointDef d) { Toast($"Survey logged: {d.displayName}. GPS map updated [M]"); if (!string.IsNullOrEmpty(d.nextHint)) Toast("Next: " + d.nextHint); }
        void OnCraftFailed(ASHRecipe r, string why) => Toast(why);
        void OnConsumed(ASHItemDefinition i) => Toast((inventory && inventory.LastConsumeSmashed ? "Smashed it open and lost some: " : (string.IsNullOrEmpty(i.useVerb) || i.useVerb == "Use" ? "Used " : PastTense(i.useVerb) + " ")) + i.displayName);
        static string PastTense(string v) { switch (v) { case "Eat": return "Ate"; case "Drink": return "Drank"; case "Apply": return "Applied"; default: return v + "ed"; } }
        void OnDied() { _deathTimer = 5f; }

        // ---------------------------------------------------------------- build
        void Build()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
            var cgo = new GameObject("ASH_HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            _canvas = cgo.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 50;
            var sc = cgo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080) / uiScale; sc.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)cgo.transform;

            // survival bars, bottom-left
            var bars = Rect("Bars", _root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 40), new Vector2(360, 150));
            _health = MakeBar(bars, "Health", 3, new Color(0.78f, 0.22f, 0.2f));
            _stamina = MakeBar(bars, "Stamina", 2, new Color(0.86f, 0.78f, 0.36f));
            _hunger = MakeBar(bars, "Hunger", 1, new Color(0.85f, 0.5f, 0.22f));
            _thirst = MakeBar(bars, "Thirst", 0, new Color(0.35f, 0.62f, 0.85f));

            // reticle + prompt
            _reticle = Img("Reticle", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 6), new Color(1, 1, 1, 0.85f)).rectTransform;
            _prompt = Label("Prompt", _root, new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(700, 40), 26, TextAnchor.MiddleCenter, Ink);
            AddShadow(_prompt);
            _holdBar = Img("HoldBar", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(220, 8), new Color(0, 0, 0, 0.55f)).rectTransform;
            _holdFill = Img("Fill", _holdBar, new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, Vector2.zero, Accent).rectTransform;
            _holdFill.pivot = new Vector2(0, 0.5f);

            // objective, top-left
            var obj = Img("Objective", _root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -40), new Vector2(520, 150), new Color(0, 0, 0, 0.28f)).rectTransform;
            obj.pivot = new Vector2(0, 1);
            _objTitle = Label("Title", obj, new Vector2(0, 1), new Vector2(16, -12), new Vector2(490, 34), 26, TextAnchor.UpperLeft, Accent, pivot: new Vector2(0, 1));
            _objLines = Label("Lines", obj, new Vector2(0, 1), new Vector2(16, -50), new Vector2(490, 100), 21, TextAnchor.UpperLeft, Ink, pivot: new Vector2(0, 1));
            AddShadow(_objTitle); AddShadow(_objLines);

            _clock = Label("Clock", _root, new Vector2(1, 1), new Vector2(-40, -40), new Vector2(240, 34), 24, TextAnchor.UpperRight, Ink, pivot: new Vector2(1, 1));
            AddShadow(_clock);
            _toastRoot = Rect("Toasts", _root, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-40, 120), new Vector2(520, 300));
            _toastRoot.pivot = new Vector2(1, 1);
            var vl = _toastRoot.gameObject.AddComponent<VerticalLayoutGroup>(); vl.childAlignment = TextAnchor.UpperRight; vl.spacing = 6; vl.childControlHeight = false; vl.childControlWidth = true; vl.childForceExpandHeight = false;

            _placeHint = Label("PlaceHint", _root, new Vector2(0.5f, 0), new Vector2(0, 185), new Vector2(900, 60), 24, TextAnchor.MiddleCenter, Ink);
            AddShadow(_placeHint);
            _subtitle = Label("Subtitle", _root, new Vector2(0.5f, 0), new Vector2(0, 250), new Vector2(1300, 96), 27, TextAnchor.LowerCenter, Ink);
            AddShadow(_subtitle); _subtitle.enabled = false;

            BuildHotbar(); BuildCross(); BuildInventory();
            _journal = ASHJournalUI.Create(_root, inventory, crafting, UseItem);
            _journal.Objectives = objective;
            if (gps) _map = ASHMapUI.Create(_root, gps);
            BuildPause(); BuildDeath();
            RefreshInventory();
        }

        // ---------------------------------------------------------------- slots
        SlotUI MakeSlot(Transform parent, int idx, float size, bool showKey)
        {
            var bg = Img("Slot" + idx, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size), SlotEmpty);
            bg.raycastTarget = true;
            var ol = bg.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0.92f, 0.82f, 0.66f, 0.22f); ol.effectDistance = new Vector2(1.5f, -1.5f);
            var s = new SlotUI { index = idx, rt = bg.rectTransform, bg = bg };
            s.icon = Img("Icon", bg.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white);
            s.icon.rectTransform.offsetMin = new Vector2(7, 9); s.icon.rectTransform.offsetMax = new Vector2(-7, -7); s.icon.preserveAspect = true;
            s.strip = Img("Strip", bg.rectTransform, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 3), Accent);
            s.strip.rectTransform.pivot = new Vector2(0.5f, 0); s.strip.rectTransform.anchoredPosition = Vector2.zero; s.strip.rectTransform.sizeDelta = new Vector2(-6, 3);
            s.abbr = Label("Abbr", bg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size), Mathf.RoundToInt(size * 0.34f), TextAnchor.MiddleCenter, Ink);
            s.abbr.fontStyle = FontStyle.Bold; AddShadow(s.abbr);
            s.count = Label("Count", bg.rectTransform, new Vector2(1, 0), new Vector2(-6, 5), new Vector2(size, 26), Mathf.RoundToInt(Mathf.Clamp(size * 0.24f, 15, 24)), TextAnchor.LowerRight, Ink, pivot: new Vector2(1, 0));
            s.count.fontStyle = FontStyle.Bold; AddShadow(s.count);
            s.key = Label("Key", bg.rectTransform, new Vector2(0, 1), new Vector2(6, -3), new Vector2(30, 22), 15, TextAnchor.UpperLeft, Dim, pivot: new Vector2(0, 1));
            s.key.text = showKey ? (idx + 1).ToString() : ""; AddShadow(s.key);

            s.button = bg.gameObject.AddComponent<Button>(); s.button.targetGraphic = bg;
            var cb = s.button.colors; cb.colorMultiplier = 1.6f; cb.normalColor = new Color(0.625f, 0.625f, 0.625f, 1f); cb.selectedColor = cb.normalColor; cb.highlightedColor = new Color(1f, 0.9f, 0.74f, 1f); cb.pressedColor = new Color(0.5f, 0.47f, 0.44f, 1f); cb.fadeDuration = 0.06f; s.button.colors = cb;
            var nav = s.button.navigation; nav.mode = Navigation.Mode.None; s.button.navigation = nav;
            s.button.onClick.AddListener(() => UseSlot(idx));
            var trig = bg.gameObject.AddComponent<EventTrigger>();
            void On(EventTriggerType type, System.Action<PointerEventData> a) { var e = new EventTrigger.Entry { eventID = type }; e.callback.AddListener(d => a((PointerEventData)d)); trig.triggers.Add(e); }
            On(EventTriggerType.PointerEnter, _ => { _hoverSlot = idx; ShowTip(idx); });
            On(EventTriggerType.PointerExit, _ => { if (_hoverSlot == idx) _hoverSlot = -1; HideTip(); });
            On(EventTriggerType.PointerClick, d => { if (d.button == PointerEventData.InputButton.Right && inventory && _invPanel && _invPanel.activeSelf) OpenContext(idx, d.position); });
            On(EventTriggerType.BeginDrag, d => BeginSlotDrag(idx, d));
            On(EventTriggerType.Drag, d => { if (_dragGhost) _dragGhost.rectTransform.position = d.position; });
            On(EventTriggerType.EndDrag, _ => EndSlotDrag());
            On(EventTriggerType.Drop, _ => DropOn(idx));
            return s;
        }

        void BuildHotbar()
        {
            int n = inventory ? Mathf.Min(inventory.hotbarSize, inventory.SlotCount) : 6;
            const float size = 78f, gap = 8f;
            _hotbarRoot = Rect("Hotbar", _root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(n * size + (n - 1) * gap + 16, size + 16));
            _hotbarRoot.pivot = new Vector2(0.5f, 0);
            var back = _hotbarRoot.gameObject.AddComponent<Image>(); back.color = new Color(0, 0, 0, 0.32f); back.raycastTarget = false;
            var hl = _hotbarRoot.gameObject.AddComponent<HorizontalLayoutGroup>(); hl.spacing = gap; hl.padding = new RectOffset(8, 8, 8, 8);
            hl.childControlWidth = hl.childControlHeight = false; hl.childForceExpandWidth = hl.childForceExpandHeight = false; hl.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < n; i++) _hotbar.Add(MakeSlot(_hotbarRoot, i, size, true));
        }

        /// <summary>Drops the dragged stack onto a slot (pack, hotbar or cross bar); cross quadrants enforce their category.</summary>
        public bool DropOn(int idx)
        {
            if (_dragFrom < 0 || inventory == null) return false;
            int from = _dragFrom;
            bool ok = inventory.Move(from, idx);
            if (!ok && inventory.LastMoveRefused >= 0 && inventory.IsCross(inventory.LastMoveRefused))
            {
                var q = (ASHInventory.CrossQuad)(inventory.LastMoveRefused - inventory.CrossStart);
                Toast($"The {ASHInventory.CrossLabel(q).ToLowerInvariant()} slot only holds {CrossHint(q)}.");
            }
            return ok;
        }
        static string CrossHint(ASHInventory.CrossQuad q) => q == ASHInventory.CrossQuad.Weapon ? "weapons and the hatchet" : q == ASHInventory.CrossQuad.Ammo ? "ammo" : q == ASHInventory.CrossQuad.Light ? "lanterns, torches and flashlights" : "food, water and meds";

        /// <summary>Test hook: drag slot a onto slot b through the same path as the mouse.</summary>
        public bool SimDrag(int a, int b) { if (inventory == null || a < 0 || a >= inventory.Slots.Count || inventory.Slots[a].IsEmpty) return false; _dragFrom = a; bool ok = DropOn(b); _dragFrom = -1; return ok; }

        // Genesis Hot Cross, adapted to uGUI: four quadrants split by a thin gold cross with a ring in the middle.
        // TL weapon (Z / d-pad up), TR ammo (X / right), BL food (V / down), BR light (G / left). Centre: rounds for the held weapon.
        void BuildCross()
        {
            if (inventory == null) return;
            const float q = 84f, gap = 14f; float span = q * 2 + gap;
            _crossRoot = Rect("HotCross", _root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 30), new Vector2(span + 16, span + 16));
            _crossRoot.pivot = new Vector2(1, 0);
            var back = _crossRoot.gameObject.AddComponent<Image>(); back.color = new Color(0, 0, 0, 0.28f); back.raycastTarget = false;
            Img("HBar", _crossRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(span, 2), Gold).raycastTarget = false;
            Img("VBar", _crossRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, span), Gold).raycastTarget = false;
            var pos = new[] { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, -1) }; // weapon TL, ammo TR, light BR, food BL
            for (int i = 0; i < ASHInventory.CrossCount; i++)
            {
                var s = MakeSlot(_crossRoot, inventory.CrossStart + i, q, false);
                s.rt.anchoredPosition = pos[i] * ((q + gap) * 0.5f);
                s.key.text = ASHInput.CrossKeys[i]; s.key.color = Gold;
                _cross.Add(s);
            }
            var ring = Img("Ring", _crossRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36, 36), new Color(0.05f, 0.045f, 0.04f, 0.95f));
            ring.raycastTarget = false; var ro = ring.gameObject.AddComponent<Outline>(); ro.effectColor = Gold; ro.effectDistance = new Vector2(2, -2);
            _crossCenter = Label("Rounds", ring.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36, 36), 17, TextAnchor.MiddleCenter, Ink);
            _crossCenter.fontStyle = FontStyle.Bold; AddShadow(_crossCenter);
        }

        void RefreshCross()
        {
            if (inventory == null) return;
            foreach (var ui in _cross)
            {
                FillSlot(ui);
                var s = inventory.Slots[ui.index]; var quad = (ASHInventory.CrossQuad)(ui.index - inventory.CrossStart);
                if (s.IsEmpty) { ui.abbr.text = $"<size=13>{ASHInventory.CrossLabel(quad).ToUpperInvariant()}</size>"; ui.abbr.color = new Color(Gold.r, Gold.g, Gold.b, 0.55f); }
                bool held = !s.IsEmpty && _equip && (s.item == _equip.Main || s.item == _equip.Off || s.item == _equip.Hip);
                ui.bg.color = held ? new Color(0.32f, 0.24f, 0.12f, 0.92f) : ui.bg.color;
            }
            if (_crossCenter)
            {
                var w = _equip ? _equip.Main : null;
                _crossCenter.text = w && w.ammoItem ? inventory.Count(w.ammoItem).ToString() : "";
            }
        }

        /// <summary>Cross bar key/d-pad: weapon holds/holsters, ammo shows the count (reload stub), light readies or toggles, food eats.</summary>
        public bool UseCross(int quad)
        {
            if (inventory == null || quad < 0 || quad >= ASHInventory.CrossCount) return false;
            int idx = inventory.CrossStart + quad; var s = inventory.Slots[idx];
            if (quad < _cross.Count) _cross[quad].pulse = 1f;
            var q = (ASHInventory.CrossQuad)quad;
            if (s.IsEmpty) { Toast($"Cross bar: drag {CrossHint(q)} onto the {ASHInventory.CrossLabel(q).ToLowerInvariant()} slot [{ASHInput.CrossKeys[quad]}]."); return false; }
            switch (q)
            {
                case ASHInventory.CrossQuad.Ammo:
                    var w = _equip ? _equip.Main : null;
                    Toast(w && w.ammoItem == s.item ? $"{w.displayName}: {inventory.Count(s.item)} {s.item.displayName} ready." : $"{s.item.displayName}: {inventory.Count(s.item)} carried.");
                    return true;
                case ASHInventory.CrossQuad.Light:
                    if (_equip && (s.item == _equip.Off || s.item == _equip.Hip)) return _equip.ToggleLight();
                    return UseSlot(idx);
                default: return UseSlot(idx);
            }
        }
        public string CrossText(int quad) { if (inventory == null || quad < 0 || quad >= ASHInventory.CrossCount) return null; var s = inventory.Slots[inventory.CrossStart + quad]; return s.IsEmpty ? "" : s.item.displayName + "|" + s.amount; }
        public int CrossSlotCount => _cross.Count;

        void BuildInventory()
        {
            int n = inventory ? inventory.SlotCount : 12; const int cols = 6; const float cell = 96f, gap = 10f;
            int rows = Mathf.CeilToInt(n / (float)cols);
            float gridW = cols * cell + (cols - 1) * gap, gridH = rows * cell + (rows - 1) * gap;
            _invPanel = Img("Inventory", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(gridW + 80, gridH + 270), Panel).gameObject;
            var rt = (RectTransform)_invPanel.transform;
            var accent = Img("Accent", rt, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 3), new Color(Accent.r, Accent.g, Accent.b, 0.8f)).rectTransform; accent.pivot = new Vector2(0.5f, 1); accent.anchoredPosition = Vector2.zero; accent.sizeDelta = new Vector2(0, 3);
            Label("Title", rt, new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(gridW, 40), 30, TextAnchor.MiddleCenter, Accent).text = "Pack";
            Label("Sub", rt, new Vector2(0.5f, 1), new Vector2(0, -64), new Vector2(gridW, 24), 16, TextAnchor.MiddleCenter, Dim).text = $"{n} slots  ·  top row is the hotbar  ·  cross bar bottom-right";
            var grid = Rect("Grid", rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(gridW, gridH));
            grid.pivot = new Vector2(0.5f, 1);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>(); g.cellSize = new Vector2(cell, cell); g.spacing = new Vector2(gap, gap);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g.constraintCount = cols; g.childAlignment = TextAnchor.UpperCenter;
            int hb = inventory ? inventory.hotbarSize : 6;
            for (int i = 0; i < n; i++) _slots.Add(MakeSlot(grid, i, cell, i < hb));
            _tipTitle = Label("TipTitle", rt, new Vector2(0.5f, 0), new Vector2(0, 140), new Vector2(gridW, 32), 23, TextAnchor.MiddleLeft, Ink);
            _tipBody = Label("TipBody", rt, new Vector2(0.5f, 0), new Vector2(0, 82), new Vector2(gridW, 80), 18, TextAnchor.UpperLeft, Dim);
            _invDesc = Label("Hint", rt, new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(gridW, 26), 16, TextAnchor.MiddleCenter, Dim);
            _invDesc.text = PackHint;
            HideTip();
            _invPanel.SetActive(false);
            BuildContextMenu();
        }

        // ---------------------------------------------------------------- item context menu (RMB on a pack slot)
        GameObject _ctxBlocker; RectTransform _ctxPanel; int _ctxSlot = -1;
        sealed class CtxEntry { public string id; public Image bg; public Button button; public Text text; }
        readonly List<CtxEntry> _ctx = new List<CtxEntry>();
        const float CtxW = 230f, CtxRow = 40f, CtxPad = 8f;

        void BuildContextMenu()
        {
            // full-screen click catcher: clicking anywhere outside the menu closes it
            var blk = Img("ItemMenuBlocker", _root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0));
            blk.rectTransform.offsetMin = blk.rectTransform.offsetMax = Vector2.zero; blk.raycastTarget = true;
            var bt = blk.gameObject.AddComponent<EventTrigger>();
            var e = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown }; e.callback.AddListener(_ => CloseContext()); bt.triggers.Add(e);
            _ctxBlocker = blk.gameObject;
            var panel = Img("ItemMenu", blk.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CtxW, 100), new Color(Panel.r, Panel.g, Panel.b, 0.96f));
            panel.raycastTarget = true; _ctxPanel = panel.rectTransform; _ctxPanel.pivot = new Vector2(0, 1);
            var ol = panel.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0.92f, 0.82f, 0.66f, 0.22f); ol.effectDistance = new Vector2(1.5f, -1.5f);
            var acc = Img("Accent", _ctxPanel, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 3), new Color(Accent.r, Accent.g, Accent.b, 0.8f)).rectTransform;
            acc.pivot = new Vector2(0.5f, 1); acc.anchoredPosition = Vector2.zero; acc.sizeDelta = new Vector2(0, 3);
            foreach (var id in new[] { "use", "drop1", "dropall", "split" })
            {
                var bg = Img("Item_" + id, _ctxPanel, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(CtxW - CtxPad * 2, CtxRow - 4), new Color(1, 1, 1, 0.05f));
                bg.raycastTarget = true; bg.rectTransform.pivot = new Vector2(0, 1);
                var b = bg.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
                var cb = b.colors; cb.colorMultiplier = 1.6f; cb.normalColor = new Color(0.625f, 0.625f, 0.625f, 1f); cb.highlightedColor = new Color(1f, 0.9f, 0.74f, 1f); cb.pressedColor = new Color(0.5f, 0.47f, 0.44f, 1f); cb.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f); cb.selectedColor = cb.normalColor; cb.fadeDuration = 0.06f; b.colors = cb;
                var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
                var t = Label("Text", bg.rectTransform, new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(CtxW - CtxPad * 2 - 16, CtxRow - 4), 20, TextAnchor.MiddleLeft, Ink, pivot: new Vector2(0, 0.5f));
                var entry = new CtxEntry { id = id, bg = bg, button = b, text = t };
                b.onClick.AddListener(() => ContextPick(entry.id));
                _ctx.Add(entry);
            }
            _ctxBlocker.SetActive(false);
        }

        public bool ContextOpen => _ctxSlot >= 0 && _ctxBlocker && _ctxBlocker.activeSelf;
        public int ContextSlot => ContextOpen ? _ctxSlot : -1;

        /// <summary>Opens the Use / Drop / Split menu for a pack, hotbar or cross slot at a screen position (defaults to the slot).</summary>
        public bool OpenContext(int slot, Vector2? screenPos = null)
        {
            if (inventory == null || _ctxBlocker == null || slot < 0 || slot >= inventory.Slots.Count || inventory.Slots[slot].IsEmpty) { CloseContext(); return false; }
            EndSlotDrag(); _ctxSlot = slot;
            var s = inventory.Slots[slot]; var it = s.item;
            int rows = 0;
            foreach (var e in _ctx)
            {
                bool show = true, enabled = true; string label = "";
                switch (e.id)
                {
                    case "use": label = UseLabel(it); enabled = CanUse(it); break;
                    case "drop1": label = s.amount > 1 ? "Drop 1" : "Drop"; break;
                    case "dropall": show = s.amount > 1; label = $"Drop all  <color=#a59e93>x{s.amount}</color>"; break;
                    case "split": show = s.amount > 1; label = "Split stack"; break;
                }
                e.bg.gameObject.SetActive(show);
                if (!show) continue;
                e.button.interactable = enabled;
                e.text.text = label; e.text.color = enabled ? Ink : new Color(Dim.r, Dim.g, Dim.b, 0.55f);
                e.bg.rectTransform.anchoredPosition = new Vector2(CtxPad, -CtxPad - 3 - rows * CtxRow);
                rows++;
            }
            _ctxPanel.sizeDelta = new Vector2(CtxW, rows * CtxRow + CtxPad * 2 + 3);
            // place at the cursor (or the slot), kept on screen
            Vector2 sp = screenPos ?? (Vector2)RectTransformUtility.WorldToScreenPoint(null, SlotRect(slot) ? SlotRect(slot).position : _root.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, null, out var local);
            var half = _root.rect.size * 0.5f;
            local.x = Mathf.Clamp(local.x + 6f, -half.x + 4f, half.x - CtxW - 4f);
            local.y = Mathf.Clamp(local.y - 6f, -half.y + _ctxPanel.sizeDelta.y + 4f, half.y - 4f);
            _ctxPanel.anchoredPosition = local;
            _ctxBlocker.transform.SetAsLastSibling(); _ctxBlocker.SetActive(true);
            HideTip();
            return true;
        }

        RectTransform SlotRect(int slot)
        {
            foreach (var ui in _slots) if (ui.index == slot) return ui.rt;
            foreach (var ui in _cross) if (ui.index == slot) return ui.rt;
            foreach (var ui in _hotbar) if (ui.index == slot) return ui.rt;
            return null;
        }

        public void CloseContext() { _ctxSlot = -1; if (_ctxBlocker) _ctxBlocker.SetActive(false); }

        /// <summary>Test hook: visible menu rows as "label:enabled|...".</summary>
        public string ContextText()
        {
            if (!ContextOpen) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var e in _ctx) if (e.bg.gameObject.activeSelf) sb.Append(System.Text.RegularExpressions.Regex.Replace(e.text.text, "<.*?>", "")).Append(':').Append(e.button.interactable ? 1 : 0).Append('|');
            return sb.ToString();
        }

        /// <summary>Runs a menu row by id ("use", "drop1", "dropall", "split") as a click would. False when hidden/disabled.</summary>
        public bool ContextPick(string id)
        {
            if (!ContextOpen) return false;
            var e = _ctx.Find(x => x.id == id); if (e == null || !e.bg.gameObject.activeSelf || !e.button.interactable) return false;
            int i = _ctxSlot; CloseContext();
            switch (id)
            {
                case "use": return UseSlot(i);
                case "drop1": return DropSlot(i, 1) != null;
                case "dropall": return DropSlot(i, int.MaxValue) != null;
                case "split": return inventory.Split(i);
            }
            return false;
        }

        string UseLabel(ASHItemDefinition it)
        {
            var hk = HandlerFor(it); if (hk != null) return hk.UseLabel(it);
            if (it.toolType == ASHToolType.GPS) return "Open map";
            if (it.IsEquippable) return _equip && _equip.IsEquipped(it) ? "Unequip" : "Equip";
            if (it.IsPlaceable) return "Place";
            if (it.IsConsumable) return string.IsNullOrEmpty(it.useVerb) || it.useVerb == "Use" ? (it.restoreThirst > it.restoreHunger ? "Drink" : it.category == ASHItemCategory.Medical ? "Apply" : "Eat") : it.useVerb;
            if (IsFuelForEquipped(it)) return "Refuel";
            return "Use";
        }

        /// <summary>True when UseSlot would do something with this item (eat, place, equip, refuel, open the map).</summary>
        public bool CanUse(ASHItemDefinition it)
        {
            if (it == null) return false;
            if (HandlerFor(it) != null) return true;
            if (it.toolType == ASHToolType.GPS) return _map != null;
            if (it.IsConsumable) return stats != null;
            if (it.IsPlaceable) return placer != null;
            if (it.IsEquippable) return _equip != null;
            return IsFuelForEquipped(it);
        }

        bool IsFuelForEquipped(ASHItemDefinition it) =>
            _equip && it && ((_equip.Main && _equip.Main.fuelItem == it) || (_equip.Off && _equip.Off.fuelItem == it) || (_equip.Hip && _equip.Hip.fuelItem == it));

        /// <summary>Drops up to amount from slot i as a world pickup on the ground in front of the player. Returns the pickup.</summary>
        public ASHItemPickup DropSlot(int i, int amount)
        {
            if (inventory == null || i < 0 || i >= inventory.Slots.Count || inventory.Slots[i].IsEmpty) return null;
            var who = _equip && _equip.Animator ? _equip.Animator.transform : inventory.transform;
            int n = inventory.TakeFromSlot(i, amount, out var item);
            if (n <= 0) return null;
            var pk = ASHItemDrop.Spawn(item, n, who);
            if (pk && item.maxStack == 1) pk.SetCondition(inventory.LastTakenCondition);
            Toast(n > 1 ? $"Dropped {item.displayName} x{n}" : $"Dropped {item.displayName}");
            return pk;
        }

        void BuildPause()
        {
            _pausePanel = Img("Pause", _root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.6f)).gameObject;
            var rt = (RectTransform)_pausePanel.transform; rt.offsetMin = rt.offsetMax = Vector2.zero;
            _pauseMenu = ASHUIKit.Stretch("Menu", rt);
            ASHMenuWidgets.Title(_pauseMenu, "ASH", new Vector2(0, 230), 64, new Vector2(0.5f, 0.5f));
            var bs = new Vector2(360, 56); float y = 140;
            ASHMenuWidgets.MenuButton(_pauseMenu, "Resume", new Vector2(0, y), bs, () => SetPause(false)); y -= 66;
            _pauseSave = ASHMenuWidgets.MenuButton(_pauseMenu, "Save", new Vector2(0, y), bs, () => { _pauseMenu.gameObject.SetActive(false); _slotsPanel.Show(ASHSaveSlotsPanel.Mode.Save); }); y -= 66;
            ASHMenuWidgets.MenuButton(_pauseMenu, "Load", new Vector2(0, y), bs, () => { _pauseMenu.gameObject.SetActive(false); _slotsPanel.Show(ASHSaveSlotsPanel.Mode.Load); }); y -= 66;
            ASHMenuWidgets.MenuButton(_pauseMenu, "Settings", new Vector2(0, y), bs, () => { _pauseMenu.gameObject.SetActive(false); _settingsPanel.Show(); }); y -= 66;
            ASHMenuWidgets.MenuButton(_pauseMenu, "Main Menu", new Vector2(0, y), bs, () => ASHMenuWidgets.Confirm(rt, "Return to the main menu?\n<size=19><color=#a59e93>Progress since your last save will be lost.</color></size>", "MAIN MENU", "CANCEL", ASH.Core.ASHSceneFlow.MainMenu)); y -= 66;
            ASHMenuWidgets.MenuButton(_pauseMenu, "Exit", new Vector2(0, y), bs, () => ASHMenuWidgets.Confirm(rt, "Exit the game?\n<size=19><color=#a59e93>Progress since your last save will be lost.</color></size>", "EXIT", "CANCEL", ASH.Core.ASHSceneFlow.Quit));
            _settingsPanel = ASHSettingsPanel.Create(rt, () => _pauseMenu.gameObject.SetActive(true));
            _slotsPanel = ASHSaveSlotsPanel.Create(rt, () => _pauseMenu.gameObject.SetActive(true));
            Label("Controls", rt, new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(1500, 80), 19, TextAnchor.MiddleCenter, Dim).text =
                "WASD move   Mouse look   Shift sprint   Space jump   Ctrl/C crouch   CapsLock walk   E interact (hold to gather)   L light   LMB use held item   RMB aim light   Z/X/G/V cross bar (weapon/ammo/light/food; d-pad on gamepad)   Tab pack   J journal   Q crafting   M GPS map   Esc pause";
            _pausePanel.SetActive(false);
        }

        void BuildDeath()
        {
            _deathPanel = Img("Death", _root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.08f, 0.02f, 0.02f, 0.7f)).gameObject;
            var rt = (RectTransform)_deathPanel.transform; rt.offsetMin = rt.offsetMax = Vector2.zero;
            _deathText = Label("Text", rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 120), 40, TextAnchor.MiddleCenter, Ink);
            _deathPanel.SetActive(false);
        }

        // ---------------------------------------------------------------- update
        void Update()
        {
            if (input == null) return;
            bool dead = stats != null && stats.IsDead;

            if (!dead)
            {
                bool placing = placer && placer.IsPlacing;
                bool ctxEsc = ContextOpen && input.PausePressed;   // Esc closes the item menu first
                if (ctxEsc) CloseContext();
                bool subEsc = !ctxEsc && input.PausePressed && PauseOpen && ((_settingsPanel && _settingsPanel.gameObject.activeSelf) || (_slotsPanel && _slotsPanel.gameObject.activeSelf));
                if (subEsc) { ASH.Core.ASHGameSettings.Apply(false); _settingsPanel.gameObject.SetActive(false); _slotsPanel.gameObject.SetActive(false); _pauseMenu.gameObject.SetActive(true); }
                if (!ctxEsc && !subEsc && input.PausePressed && !(placer && placer.LastCancelFrame == Time.frameCount) && !placing && !ASHDeathScreen.Active && EscConsumedFrame != Time.frameCount && !ASHUIState.IsOpen("manualCover"))
                {
                    if (AnyPanelOpen) { SetInv(false); SetCraft(false); SetMap(false); }
                    else SetPause(!ASHUIState.IsOpen(ASHPanels.Pause));
                }
                if (!ASHUIState.IsOpen(ASHPanels.Pause) && !ASHUIState.IsOpen("manualCover"))
                {
                    if (input.InventoryPressed) { if (placing) placer.Cancel(); SetCraft(false); SetMap(false); SetInv(!ASHUIState.IsOpen(ASHPanels.Inventory)); }
                    if (input.CraftPressed)
                    {
                        if (!CanCraftNow) Toast(CraftingLockedMessage ?? "You have nothing to craft from yet.");
                        else { if (placing) placer.Cancel(); SetInv(false); SetMap(false); bool open = !(_journal.IsOpen && _journal.Current == ASHJournalUI.Tab.Crafting); SetJournal(open, ASHJournalUI.Tab.Crafting); }
                    }
                    if (input.JournalPressed) { if (placing) placer.Cancel(); SetInv(false); SetMap(false); SetJournal(!_journal.IsOpen, _journal.Current); }
                    if (input.MapPressed) { if (placing) placer.Cancel(); SetInv(false); SetCraft(false); SetMap(!(_map && _map.IsOpen)); }
                    if (input.BreakDownPressed && _invPanel.activeSelf && _hoverSlot >= 0 && inventory && _hoverSlot < inventory.Slots.Count && !inventory.Slots[_hoverSlot].IsEmpty && crafting)
                        crafting.TryBreakDown(inventory.Slots[_hoverSlot].item);
                    int hk = input.HotbarPressed;
                    if (hk >= 0 && hk < _hotbar.Count) { _hotbar[hk].pulse = 1f; UseSlot(hk); }
                    int cq = input.CrossPressed; if (cq >= 0) UseCross(cq);
                }
            }

            if (stats)
            {
                SetBar(_health, stats.Health, stats.MaxHealth, stats.Health < stats.MaxHealth * 0.25f);
                SetBar(_stamina, stats.Stamina, stats.MaxStamina, false);
                SetBar(_hunger, stats.Hunger, stats.MaxHunger, stats.HungerLow);
                SetBar(_thirst, stats.Thirst, stats.MaxThirst, stats.ThirstLow);
            }

            // prompt
            string p = interactor ? interactor.Prompt : null;
            bool showPrompt = !string.IsNullOrEmpty(p) && !ASHUIState.BlocksGameplay && !(placer && placer.IsPlacing);
            if (showPrompt)
            {
                float hold = interactor.Target != null ? interactor.Target.GetHoldDuration(default) : 0f;
                _prompt.text = $"[{input.BindingLabel("Interact")}]  {p}{(hold > 0f ? "  (hold)" : "")}";
            }
            _prompt.enabled = showPrompt;
            float hp = interactor ? interactor.HoldProgress01 : 0f;
            _holdBar.gameObject.SetActive(showPrompt && hp > 0f);
            _holdFill.anchorMax = new Vector2(hp, 1); _holdFill.offsetMax = Vector2.zero; _holdFill.offsetMin = Vector2.zero;
            _reticle.gameObject.SetActive(!ASHUIState.BlocksGameplay);
            _reticle.localScale = Vector3.one * (showPrompt ? 1.6f : 1f);

            // objective
            if (objective && Time.unscaledTime >= _objRefreshAt) // 5 Hz: the objective text allocates strings (was a per-frame GC source)
            {
                _objRefreshAt = Time.unscaledTime + 0.2f;
                string ot = objective.Title();
                _objTitle.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(ot)); // nothing active yet (slice waits for the opening)
                _objTitle.text = ot;
                var sb = new System.Text.StringBuilder();
                foreach (var (text, done) in objective.Lines()) sb.AppendLine((done ? "<color=#9fd39b>√ " : "• ") + text + (done ? "</color>" : ""));
                int others = objective.OtherActiveCount;
                if (others > 0) sb.AppendLine($"<size=16><color=#857d72>+{others} more objective{(others > 1 ? "s" : "")}  ·  journal [J]</color></size>");
                _objLines.text = sb.ToString();
            }
            if (clock) { int h = (int)clock.timeOfDay; int m = (int)((clock.timeOfDay - h) * 60f); _clock.text = $"{h:00}:{m:00}"; }

            // placement hint
            if (placer && placer.IsPlacing)
            {
                _placeHint.enabled = true;
                _placeHint.text = (placer.IsValid ? "" : $"<color=#ff8a70>{placer.InvalidReason}</color>\n") + "[LMB/E] Place    [RMB/Esc] Cancel    [Scroll] Rotate";
            }
            else _placeHint.enabled = false;

            // hotbar
            if (_hotbarRoot) _hotbarRoot.gameObject.SetActive(!dead && !ASHUIState.IsOpen(ASHPanels.Pause) && !(_map && _map.IsOpen) && !_journal.IsOpen);
            if (_crossRoot) _crossRoot.gameObject.SetActive(_hotbarRoot && _hotbarRoot.gameObject.activeSelf);
            AnimateSlots(_hotbar); AnimateSlots(_slots); AnimateSlots(_cross);
            if (_equip && (_equip.Main != _lastMain || _equip.Off != _lastOff || _equip.Hip != _lastHip)) { _lastMain = _equip.Main; _lastOff = _equip.Off; _lastHip = _equip.Hip; RefreshCross(); }
            if (_dragFrom >= 0 && !_invPanel.activeSelf) EndSlotDrag();
            if (ContextOpen && (!_invPanel.activeSelf || inventory == null || _ctxSlot >= inventory.Slots.Count || inventory.Slots[_ctxSlot].IsEmpty)) CloseContext();

            // crafting progress

            // toasts
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var (t, until) = _toasts[i];
                float left = until - Time.unscaledTime;
                if (left <= 0f) { Destroy(t.gameObject); _toasts.RemoveAt(i); continue; }
                var c = t.color; c.a = Mathf.Clamp01(left / 0.6f); t.color = c;
            }

            // subtitles
            if (_subtitle && _subtitle.enabled)
            {
                float sl = _subUntil - Time.unscaledTime;
                if (sl <= 0f) _subtitle.enabled = false; else { var c = _subtitle.color; c.a = Mathf.Clamp01(sl / 0.4f); _subtitle.color = c; }
            }

            // death
            _deathPanel.SetActive((dead || ASHUIState.IsOpen(ASHPanels.Death)) && !ASHDeathScreen.Active);
            if (_deathPanel.activeSelf) { _deathTimer = Mathf.Max(0f, _deathTimer - Time.deltaTime); _deathText.text = $"You didn't make it.\n<size=26>Back at camp in {Mathf.CeilToInt(_deathTimer)}...</size>"; }
        }

        /// <summary>Bottom-centre subtitle (radio, narration). Replaces the current one.</summary>
        public void ShowSubtitle(string text, float seconds)
        {
            if (_subtitle == null) return;
            if (string.IsNullOrEmpty(text)) { _subtitle.enabled = false; return; }
            _subtitle.text = text; _subtitle.enabled = true; var c = _subtitle.color; c.a = 1f; _subtitle.color = c;
            _subUntil = Time.unscaledTime + Mathf.Max(0.5f, seconds);
        }
        public string SubtitleText => _subtitle && _subtitle.enabled ? _subtitle.text : "";

        public void Toast(string msg)
        {
            if (string.IsNullOrEmpty(msg) || _toastRoot == null) return;
            var t = Label("Toast", _toastRoot, new Vector2(1, 1), Vector2.zero, new Vector2(520, 30), 22, TextAnchor.MiddleRight, Ink);
            t.text = msg; AddShadow(t);
            _toasts.Add((t, Time.unscaledTime + toastSeconds));
            if (_toasts.Count > 6) { Destroy(_toasts[0].text.gameObject); _toasts.RemoveAt(0); }
        }

        void SetInv(bool open) { _invPanel.SetActive(open); ASHUIState.SetOpen(ASHPanels.Inventory, open); if (open) { RefreshInventory(); HideTip(); } else { EndSlotDrag(); CloseContext(); } }
        void SetCraft(bool open) { if (open) SetJournal(true, ASHJournalUI.Tab.Crafting); else SetJournal(false, ASHJournalUI.Tab.Crafting); }
        void SetJournal(bool open, ASHJournalUI.Tab tab)
        {
            if (open) _journal.Open(tab); else _journal.Close();
            ASHUIState.SetOpen(ASHPanels.Journal, open);
        }
        void SetMap(bool open)
        {
            if (_map == null) { if (open) Toast("No GPS map data in this scene."); return; }
            if (open && inventory && !inventory.HasTool(ASHToolType.GPS)) { Toast("You need the GPS to see the map."); return; }
            if (open) _map.Open(); else _map.Close();
            ASHUIState.SetOpen(ASHPanels.Map, open);
        }
        bool AnyPanelOpen => ASHUIState.IsOpen(ASHPanels.Inventory) || ASHUIState.IsOpen(ASHPanels.Journal) || ASHUIState.IsOpen(ASHPanels.Map);
        public ASHJournalUI Journal => _journal;
        public ASHMapUI MapUI => _map;
        public void OpenJournal(ASHJournalUI.Tab tab) => SetJournal(true, tab);
        public void CloseAllPanels() { SetInv(false); SetJournal(false, _journal.Current); SetMap(false); }
        public void OpenMap(bool open) => SetMap(open);
        void SetPause(bool open)
        {
            _pausePanel.SetActive(open); ASHUIState.SetOpen(ASHPanels.Pause, open);
            if (_pauseMenu) { _pauseMenu.gameObject.SetActive(true); if (_settingsPanel) _settingsPanel.gameObject.SetActive(false); if (_slotsPanel) _slotsPanel.gameObject.SetActive(false); }
            if (open && _pauseSave) ASHMenuWidgets.SetInteractable(_pauseSave, ASH.Core.ASHSaveSystem.Instance != null);
            Time.timeScale = open ? 0f : 1f;
            if (stats) stats.Paused = open;
        }
        public bool InventoryOpen => _invPanel && _invPanel.activeSelf;
        public bool CraftOpen => _journal && _journal.IsOpen && _journal.Current == ASHJournalUI.Tab.Crafting;

        void RefreshInventory()
        {
            if (inventory == null) return;
            foreach (var ui in _slots) FillSlot(ui);
            foreach (var ui in _hotbar) FillSlot(ui);
            RefreshCross();
        }
        ASHItemDefinition _lastMain, _lastOff, _lastHip;

        void FillSlot(SlotUI ui)
        {
            if (ui.index >= inventory.Slots.Count) return;
            var s = inventory.Slots[ui.index];
            bool empty = s.IsEmpty;
            ui.bg.color = empty ? SlotEmpty : SlotFull;
            ui.icon.enabled = !empty && s.item.icon != null;
            if (ui.icon.enabled) ui.icon.sprite = s.item.icon;
            ui.abbr.text = empty || s.item.icon != null ? "" : s.item.displayName.Substring(0, Mathf.Min(2, s.item.displayName.Length)).ToUpperInvariant();
            if (!empty) ui.abbr.color = Color.Lerp(s.item.uiTint, Color.white, 0.25f);
            ui.count.text = empty || s.item.maxStack <= 1 ? "" : s.amount.ToString();
            ui.strip.enabled = !empty;
            if (!empty) { var c = s.item.uiTint; c.a = 0.9f; ui.strip.color = c; }
        }

        void PulseItem(ASHItemDefinition item)
        {
            if (inventory == null) return;
            foreach (var ui in _hotbar) if (ui.index < inventory.Slots.Count && inventory.Slots[ui.index].item == item) ui.pulse = 1f;
            foreach (var ui in _slots) if (ui.index < inventory.Slots.Count && inventory.Slots[ui.index].item == item) ui.pulse = 1f;
            foreach (var ui in _cross) if (ui.index < inventory.Slots.Count && inventory.Slots[ui.index].item == item) ui.pulse = 1f;
        }

        void AnimateSlots(List<SlotUI> list)
        {
            foreach (var ui in list)
            {
                if (ui.pulse <= 0f) { if (ui.rt.localScale.x != 1f) ui.rt.localScale = Vector3.one; continue; }
                ui.pulse = Mathf.Max(0f, ui.pulse - Time.unscaledDeltaTime * 3.5f);
                ui.rt.localScale = Vector3.one * (1f + 0.16f * Mathf.Sin(ui.pulse * Mathf.PI));
            }
        }

        void ShowTip(int i)
        {
            if (inventory == null || i >= inventory.Slots.Count || _tipTitle == null) return;
            var s = inventory.Slots[i];
            if (s.IsEmpty) { HideTip(); return; }
            var it = s.item;
            string hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(it.uiTint, Color.white, 0.35f));
            _tipTitle.text = $"<color=#{hex}><b>{it.displayName}</b></color>  <size=16><color=#a59e93>{it.category}  ·  {s.amount}/{it.maxStack}</color></size>";
            var sb = new System.Text.StringBuilder(it.description);
            if (it.IsConsumable)
            {
                sb.Append("\n<color=#cfe3b0>");
                if (it.restoreHunger > 0) sb.Append($"+{it.restoreHunger:0} hunger   ");
                if (it.restoreThirst > 0) sb.Append($"+{it.restoreThirst:0} thirst   ");
                if (it.restoreHealth > 0) sb.Append($"+{it.restoreHealth:0} health   ");
                if (it.restoreStamina > 0) sb.Append($"+{it.restoreStamina:0} stamina");
                sb.Append("</color>  <color=#a59e93>LMB to eat</color>");
            }
            else if (it.IsPlaceable) sb.Append("\n<color=#a59e93>LMB to place</color>");
            else if (it.toolType == ASHToolType.GPS) sb.Append("\n<color=#a59e93>LMB or [M] to open the map</color>");
            else if (it.toolType != ASHToolType.None) sb.Append("\n<color=#a59e93>Tool  ·  used automatically</color>");
            else if (it.CanBreakDown) sb.Append("\n<color=#c9a46a>[B] break down</color>");
            else sb.Append("\n<color=#a59e93>Crafting material  ·  [Q] craft</color>");
            _tipBody.text = sb.ToString();
        }

        void HideTip()
        {
            if (_tipTitle == null) return;
            _tipTitle.text = "";
            _tipBody.text = "Gather deadwood, stone and chokecherries. Hover a slot for details.";
        }

        void BeginSlotDrag(int idx, PointerEventData d)
        {
            if (inventory == null || idx >= inventory.Slots.Count || inventory.Slots[idx].IsEmpty) { _dragFrom = -1; return; }
            _dragFrom = idx;
            var s = inventory.Slots[idx];
            _dragGhost = Img("DragGhost", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72, 72), s.item.icon ? Color.white : s.item.uiTint);
            _dragGhost.sprite = s.item.icon; _dragGhost.preserveAspect = true; _dragGhost.raycastTarget = false;
            var c = _dragGhost.color; c.a = 0.85f; _dragGhost.color = c;
            _dragGhost.rectTransform.position = d.position;
        }

        void EndSlotDrag()
        {
            if (_dragGhost) Destroy(_dragGhost.gameObject);
            _dragGhost = null; _dragFrom = -1;
        }

        /// <summary>Uses the item in a slot: eat consumables, place placeables, hint for materials.</summary>
        public bool UseSlot(int i)
        {
            if (inventory == null || i < 0 || i >= inventory.Slots.Count) return false;
            var s = inventory.Slots[i]; if (s.IsEmpty) return false;
            var item = s.item;
            var hk = HandlerFor(item); if (hk != null) return hk.Use(item, i);
            if (item.toolType == ASHToolType.GPS) { SetInv(false); SetJournal(false, _journal.Current); SetMap(true); return true; }
            if (item.IsConsumable && stats) return inventory.TryConsume(item, stats);
            if (item.IsPlaceable && placer)
            {
                if (placer.IsPlacing) placer.Cancel();
                SetInv(false); SetCraft(false); placer.Begin(item); return true;
            }
            if (item.IsEquippable && _equip) { _equip.ToggleEquip(item); return true; }
            if (_equip && _equip.TryRefuelWith(item)) return true;
            if (item.CanBreakDown) { Toast($"{item.displayName}: break it down with [B] in the pack or from the journal."); return false; }
            if (item.toolType != ASHToolType.None) { Toast($"{item.displayName}: used automatically when you harvest, cut or open things."); return false; }
            Toast($"{item.displayName}: crafting material. Press [{(input ? input.BindingLabel("Craft") : "Q")}] for the journal.");
            return false;
        }

        /// <summary>Uses the first stack of an item (journal Use button).</summary>
        public bool UseItem(ASHItemDefinition item)
        {
            if (inventory == null || item == null) return false;
            for (int i = 0; i < inventory.Slots.Count; i++) if (inventory.Slots[i].item == item && !inventory.Slots[i].IsEmpty) return UseSlot(i);
            return false;
        }

        /// <summary>Test hook: "name|count|icon" as displayed by a hotbar slot (or pack slot when hotbar=false).</summary>
        public string SlotText(int i, bool hotbar = true)
        {
            var list = hotbar ? _hotbar : _slots;
            if (i < 0 || i >= list.Count) return null;
            var ui = list[i];
            string name = inventory && ui.index < inventory.Slots.Count && !inventory.Slots[ui.index].IsEmpty ? inventory.Slots[ui.index].item.displayName : "";
            return $"{name}|{ui.count.text}|{(ui.icon.enabled ? "icon" : (ui.abbr.text.Length > 0 ? "abbr" : "-"))}";
        }
        public int HotbarSlotCount => _hotbar.Count;
        public int PackSlotCount => _slots.Count;

        void SetBar(Bar b, float v, float max, bool warn)
        {
            float f = max > 0 ? Mathf.Clamp01(v / max) : 0f;
            b.fill.anchorMax = new Vector2(f, 1);
            float pulse = warn ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) : 1f;
            b.fillImg.color = b.color * new Color(pulse, pulse, pulse, 1f);
            b.label.text = $"{b.label.name}  {Mathf.CeilToInt(v)}";
        }

        // ---------------------------------------------------------------- ui helpers
        Bar MakeBar(RectTransform parent, string name, int row, Color color)
        {
            var bg = Img(name, parent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, row * 36), new Vector2(320, 14), new Color(0, 0, 0, 0.5f)).rectTransform;
            bg.pivot = new Vector2(0, 0);
            var fill = Img("Fill", bg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, color);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var label = Label(name, bg, new Vector2(0, 1), new Vector2(0, 2), new Vector2(320, 20), 16, TextAnchor.LowerLeft, Ink, pivot: new Vector2(0, 0));
            AddShadow(label);
            return new Bar { fill = fill.rectTransform, fillImg = fill, label = label, color = color };
        }

        void MakeButton(RectTransform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var img = Img(text, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(300, 56), new Color(1, 1, 1, 0.1f));
            img.raycastTarget = true; var b = img.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(onClick);
            Label("Text", img.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 56), 26, TextAnchor.MiddleCenter, Ink).text = text;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false); rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos; rt.sizeDelta = size; rt.pivot = aMin == aMax ? aMin : new Vector2(0.5f, 0.5f);
            if (aMin == new Vector2(0.5f, 0.5f)) rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        static Image Img(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, Color c)
        {
            var rt = Rect(name, parent, aMin, aMax, pos, size);
            var img = rt.gameObject.AddComponent<Image>(); img.color = c; img.raycastTarget = false;
            return img;
        }

        Text Label(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, Color c, Vector2? pivot = null)
        {
            var rt = Rect(name, parent, anchor, anchor, pos, size);
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            var t = rt.gameObject.AddComponent<Text>(); t.font = _font; t.fontSize = fontSize; t.alignment = align; t.color = c;
            t.supportRichText = true; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void AddShadow(Text t) { var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.75f); s.effectDistance = new Vector2(1.5f, -1.5f); }
    }
}
