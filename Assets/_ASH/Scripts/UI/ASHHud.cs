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
        Text _prompt, _objTitle, _objLines, _clock, _placeHint, _deathText, _invDesc;
        RectTransform _holdBar, _holdFill, _reticle;
        RectTransform _toastRoot;
        GameObject _invPanel, _pausePanel, _deathPanel;
        ASHJournalUI _journal; ASHMapUI _map; int _hoverSlot = -1;
        readonly List<SlotUI> _slots = new List<SlotUI>();
        readonly List<(Text text, float until)> _toasts = new List<(Text, float)>();
        float _deathTimer;

        sealed class Bar { public RectTransform fill; public Image fillImg; public Text label; public Color color; }
        sealed class SlotUI { public int index; public RectTransform rt; public Button button; public Image bg, icon, strip; public Text abbr, count, key; public float pulse; }
        readonly List<SlotUI> _hotbar = new List<SlotUI>();
        RectTransform _hotbarRoot;
        Image _dragGhost; int _dragFrom = -1;
        Text _tipTitle, _tipBody;
        static readonly Color SlotEmpty = new Color(0.10f, 0.095f, 0.085f, 0.55f);
        static readonly Color SlotFull = new Color(0.17f, 0.155f, 0.14f, 0.88f);
        const string PackHint = "<color=#d9cbb4>LMB</color> use   <color=#d9cbb4>RMB</color> split   <color=#d9cbb4>B</color> break down   <color=#d9cbb4>Drag</color> move   <color=#d9cbb4>J</color> journal   <color=#d9cbb4>Tab</color> close";

        void Awake()
        {
            if (gps == null) gps = FindAnyObjectByType<ASHGpsMap>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
        }

        void OnEnable()
        {
            if (inventory) { inventory.Changed += RefreshInventory; inventory.ItemAdded += OnItemAdded; }
            if (crafting) { crafting.Crafted += OnCrafted; crafting.CraftFailed += OnCraftFailed; crafting.BrokeDown += OnBrokeDown; crafting.BreakdownFailed += OnBreakFailed; }
            if (gps) gps.SurveyLogged += OnSurvey;
            if (objective) { objective.Message += Toast; }
            if (stats) { stats.Died += OnDied; stats.Consumed += OnConsumed; }
        }

        void OnDisable()
        {
            if (inventory) { inventory.Changed -= RefreshInventory; inventory.ItemAdded -= OnItemAdded; }
            if (crafting) { crafting.Crafted -= OnCrafted; crafting.CraftFailed -= OnCraftFailed; crafting.BrokeDown -= OnBrokeDown; crafting.BreakdownFailed -= OnBreakFailed; }
            if (gps) gps.SurveyLogged -= OnSurvey;
            if (objective) { objective.Message -= Toast; }
            if (stats) { stats.Died -= OnDied; stats.Consumed -= OnConsumed; }
        }

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

            BuildHotbar(); BuildInventory();
            _journal = ASHJournalUI.Create(_root, inventory, crafting, UseItem);
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
            On(EventTriggerType.PointerClick, d => { if (d.button == PointerEventData.InputButton.Right && inventory) inventory.Split(idx); });
            On(EventTriggerType.BeginDrag, d => BeginSlotDrag(idx, d));
            On(EventTriggerType.Drag, d => { if (_dragGhost) _dragGhost.rectTransform.position = d.position; });
            On(EventTriggerType.EndDrag, _ => EndSlotDrag());
            On(EventTriggerType.Drop, _ => { if (_dragFrom >= 0 && inventory) inventory.Move(_dragFrom, idx); });
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

        void BuildInventory()
        {
            int n = inventory ? inventory.SlotCount : 12; const int cols = 6; const float cell = 96f, gap = 10f;
            int rows = Mathf.CeilToInt(n / (float)cols);
            float gridW = cols * cell + (cols - 1) * gap, gridH = rows * cell + (rows - 1) * gap;
            _invPanel = Img("Inventory", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(gridW + 80, gridH + 270), Panel).gameObject;
            var rt = (RectTransform)_invPanel.transform;
            var accent = Img("Accent", rt, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, new Vector2(0, 3), new Color(Accent.r, Accent.g, Accent.b, 0.8f)).rectTransform; accent.pivot = new Vector2(0.5f, 1); accent.anchoredPosition = Vector2.zero; accent.sizeDelta = new Vector2(0, 3);
            Label("Title", rt, new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(gridW, 40), 30, TextAnchor.MiddleCenter, Accent).text = "Pack";
            Label("Sub", rt, new Vector2(0.5f, 1), new Vector2(0, -64), new Vector2(gridW, 24), 16, TextAnchor.MiddleCenter, Dim).text = $"{n} slots  ·  top row is the hotbar";
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
        }

        void BuildPause()
        {
            _pausePanel = Img("Pause", _root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.6f)).gameObject;
            var rt = (RectTransform)_pausePanel.transform; rt.offsetMin = rt.offsetMax = Vector2.zero;
            Label("Title", rt, new Vector2(0.5f, 0.5f), new Vector2(0, 140), new Vector2(600, 70), 52, TextAnchor.MiddleCenter, Ink).text = "ASH";
            MakeButton(rt, "Resume", new Vector2(0, 30), () => SetPause(false));
            MakeButton(rt, "Restart", new Vector2(0, -40), () => { SetPause(false); UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex); });
            MakeButton(rt, "Quit", new Vector2(0, -110), () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            Label("Controls", rt, new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(1500, 80), 19, TextAnchor.MiddleCenter, Dim).text =
                "WASD move   Mouse look   Shift sprint   Space jump   Ctrl/C crouch   CapsLock walk   E interact (hold to gather)   Tab pack   J journal   Q crafting   M GPS map   Esc pause";
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
                if (input.PausePressed && !(placer && placer.LastCancelFrame == Time.frameCount) && !placing)
                {
                    if (AnyPanelOpen) { SetInv(false); SetCraft(false); SetMap(false); }
                    else SetPause(!ASHUIState.IsOpen(ASHPanels.Pause));
                }
                if (!ASHUIState.IsOpen(ASHPanels.Pause))
                {
                    if (input.InventoryPressed) { if (placing) placer.Cancel(); SetCraft(false); SetMap(false); SetInv(!ASHUIState.IsOpen(ASHPanels.Inventory)); }
                    if (input.CraftPressed) { if (placing) placer.Cancel(); SetInv(false); SetMap(false); bool open = !(_journal.IsOpen && _journal.Current == ASHJournalUI.Tab.Crafting); SetJournal(open, ASHJournalUI.Tab.Crafting); }
                    if (input.JournalPressed) { if (placing) placer.Cancel(); SetInv(false); SetMap(false); SetJournal(!_journal.IsOpen, _journal.Current); }
                    if (input.MapPressed) { if (placing) placer.Cancel(); SetInv(false); SetCraft(false); SetMap(!(_map && _map.IsOpen)); }
                    if (input.BreakDownPressed && _invPanel.activeSelf && _hoverSlot >= 0 && inventory && _hoverSlot < inventory.Slots.Count && !inventory.Slots[_hoverSlot].IsEmpty && crafting)
                        crafting.TryBreakDown(inventory.Slots[_hoverSlot].item);
                    int hk = input.HotbarPressed;
                    if (hk >= 0 && hk < _hotbar.Count) { _hotbar[hk].pulse = 1f; UseSlot(hk); }
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
            if (objective)
            {
                _objTitle.text = objective.Title();
                var sb = new System.Text.StringBuilder();
                foreach (var (text, done) in objective.Lines()) sb.AppendLine((done ? "<color=#9fd39b>√ " : "• ") + text + (done ? "</color>" : ""));
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
            AnimateSlots(_hotbar); AnimateSlots(_slots);
            if (_dragFrom >= 0 && !_invPanel.activeSelf) EndSlotDrag();

            // crafting progress

            // toasts
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var (t, until) = _toasts[i];
                float left = until - Time.unscaledTime;
                if (left <= 0f) { Destroy(t.gameObject); _toasts.RemoveAt(i); continue; }
                var c = t.color; c.a = Mathf.Clamp01(left / 0.6f); t.color = c;
            }

            // death
            _deathPanel.SetActive(dead || ASHUIState.IsOpen(ASHPanels.Death));
            if (_deathPanel.activeSelf) { _deathTimer = Mathf.Max(0f, _deathTimer - Time.deltaTime); _deathText.text = $"You didn't make it.\n<size=26>Back at camp in {Mathf.CeilToInt(_deathTimer)}...</size>"; }
        }

        public void Toast(string msg)
        {
            if (string.IsNullOrEmpty(msg) || _toastRoot == null) return;
            var t = Label("Toast", _toastRoot, new Vector2(1, 1), Vector2.zero, new Vector2(520, 30), 22, TextAnchor.MiddleRight, Ink);
            t.text = msg; AddShadow(t);
            _toasts.Add((t, Time.unscaledTime + toastSeconds));
            if (_toasts.Count > 6) { Destroy(_toasts[0].text.gameObject); _toasts.RemoveAt(0); }
        }

        void SetInv(bool open) { _invPanel.SetActive(open); ASHUIState.SetOpen(ASHPanels.Inventory, open); if (open) { RefreshInventory(); HideTip(); } else EndSlotDrag(); }
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
        }

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
            if (item.toolType == ASHToolType.GPS) { SetInv(false); SetJournal(false, _journal.Current); SetMap(true); return true; }
            if (item.IsConsumable && stats) return inventory.TryConsume(item, stats);
            if (item.IsPlaceable && placer)
            {
                if (placer.IsPlacing) placer.Cancel();
                SetInv(false); SetCraft(false); placer.Begin(item); return true;
            }
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
