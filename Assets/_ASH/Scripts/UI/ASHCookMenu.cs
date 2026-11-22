using System.Collections.Generic;
using System.Text;
using ASH.Core;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Survival;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>
    /// Campfire / stove pop-up (tap Interact at a fire). Fuel: drop or click wood / kindling from the pack, fuel bar,
    /// Light / Put out. Cook: drop a raw item in the slot, see the cooked result and its stats before / after, pick how
    /// many, cook (real seconds per item, ASHFireCooking). Combine: two slots for secret two-ingredient combinations
    /// (ASHCookCombo); a right guess teaches it for good. Header: Cooking skill level / XP. Esc or X closes.
    /// Code-built uGUI in the ASHUIKit style; its own overlay canvas above the HUD.
    /// </summary>
    public sealed class ASHCookMenu : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHCookMenu()
        {
            _i = default;
        }

        public const string PanelId = "cook";
        static ASHCookMenu _i;
        public static bool IsOpen => _i && _i._root && _i._root.gameObject.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() => ASHCampfire.OpenMenu = Open;

        public static bool Open(ASHCampfire fire, ASHInventory inv, Transform player)
        {
            if (!fire) return false;
            var cr = player ? player.GetComponentInParent<ASHCrafting>() : null;
            if (!cr) cr = FindAnyObjectByType<ASHCrafting>();
            if (!inv && cr) inv = cr.Inventory;
            if (!inv) return false;
            if (!_i) { var go = new GameObject("ASH_CookMenu"); _i = go.AddComponent<ASHCookMenu>(); _i.Build(); }
            var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.CloseAllPanels();
            _i.Show(fire, inv, cr, player);
            return true;
        }

        public static void CloseIfOpen() { if (IsOpen) _i.Close(); }

        // ------------------------------------------------------------------ state
        ASHCampfire _fire; ASHInventory _inv; ASHCrafting _cr; Transform _player;
        ASHItemDefinition _cookItem, _a, _b; int _qty = 1, _cqty = 1;
        string _packSig, _jobSig, _comboSig; bool _combosDirty = true;

        // ------------------------------------------------------------------ widgets
        Canvas _canvas; RectTransform _root, _packList, _jobList, _comboList, _ghost;
        Text _title, _skill, _fuelText, _addFuelLbl, _cookName, _cookInfo, _cookStats, _qtyLbl, _cookBtnLbl, _combName, _combStats, _cqtyLbl, _combBtnLbl, _status;
        RectTransform _skillFill, _fuelFill; Button _lightBtn, _outBtn, _addFuelBtn, _cookBtn, _combBtn, _takeBtn;
        Slot _cookSlot, _cookOut, _slotA, _slotB, _combOut;
        readonly List<(ASHFireCooking.Job job, RectTransform fill, Text right)> _jobRows = new List<(ASHFireCooking.Job, RectTransform, Text)>();
        float _statusUntil;

        sealed class Slot { public RectTransform rt; public Image icon; public Text abbr, count, hint; }

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 60;
            var sc = gameObject.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var ct = (RectTransform)transform;

            _root = K.At("CookMenu", ct, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1240, 720)); K.Image(_root, K.Panel, true);
            K.Image(K.At("Accent", _root, new Vector2(0.5f, 1), Vector2.zero, new Vector2(1240, 3)), K.Accent);
            _title = K.Text(TL("Title", _root, 28, 14, 520, 40), "CAMPFIRE", 30, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _skill = K.Text(K.At("Skill", _root, new Vector2(1, 1), new Vector2(-84, -12), new Vector2(560, 26)), "", 18, K.Ink, TextAnchor.MiddleRight);
            var sbg = K.At("SkillBar", _root, new Vector2(1, 1), new Vector2(-84, -42), new Vector2(320, 8)); K.Image(sbg, new Color(1, 1, 1, 0.1f));
            _skillFill = Fill(sbg, K.Good);
            K.Button(K.At("Close", _root, new Vector2(1, 1), new Vector2(-20, -16), new Vector2(46, 38)), "X", 20, Close, new Color(1, 1, 1, 0.15f));

            // ---- pack
            K.Text(TL("PackLbl", _root, 20, 76, 330, 24), "PACK  <size=14><color=#a69e91>drag onto a slot, or click</color></size>", 18, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _packList = K.ScrollList(TL("Pack", _root, 20, 104, 330, 596), 3f);

            // ---- fuel
            var fuel = Box("Fuel", 370, 76, 440, 150, "FUEL  <size=14><color=#a69e91>drop wood / kindling here</color></size>");
            Drop(fuel, AddFuel);
            var fbg = TL("FuelBar", fuel, 16, 40, 408, 18); K.Image(fbg, new Color(0, 0, 0, 0.45f));
            _fuelFill = Fill(fbg, K.Accent);
            _fuelText = K.Text(TL("FuelText", fuel, 16, 62, 408, 24), "", 16, K.Ink);
            _lightBtn = K.Button(TL("Light", fuel, 16, 96, 124, 40), "LIGHT", 18, () => { if (_fire && !_fire.Light()) Status("Needs fuel first"); });
            _outBtn = K.Button(TL("PutOut", fuel, 148, 96, 124, 40), "PUT OUT", 18, () => { if (_fire) _fire.PutOut(); }, new Color(1, 1, 1, 0.18f));
            _addFuelBtn = K.Button(TL("AddFuel", fuel, 280, 96, 144, 40), "ADD FUEL", 15, () => { var it = _fire ? _fire.FindFuelIn(_inv, out _) : null; if (it) AddFuel(it); else Status("No fuel in the pack"); }, new Color(1, 1, 1, 0.18f));
            _addFuelLbl = _addFuelBtn.GetComponentInChildren<Text>(); _addFuelLbl.color = K.Ink;
            _outBtn.GetComponentInChildren<Text>().color = K.Ink;

            // ---- cook
            var cook = Box("Cook", 370, 236, 440, 226, "COOK");
            _cookSlot = MakeSlot(cook, 16, 40, "drop food", it => { _cookItem = it; _qty = 1; }, () => _cookItem = null);
            K.Text(TL("Arrow", cook, 104, 64, 40, 40), "→", 30, K.Dim, TextAnchor.MiddleCenter);
            _cookOut = MakeSlot(cook, 148, 40, "", null, null);
            _cookName = K.Text(TL("Name", cook, 244, 40, 184, 26), "", 18, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _cookInfo = K.Text(TL("Info", cook, 244, 68, 184, 56), "", 15, K.Dim, TextAnchor.UpperLeft);
            _cookStats = K.Text(TL("Stats", cook, 16, 130, 408, 44), "", 15, K.Ink, TextAnchor.UpperLeft);
            QtyRow(cook, 180, () => _qty--, () => _qty++, () => _qty = 999, out _qtyLbl);
            _cookBtn = K.Button(TL("CookBtn", cook, 274, 178, 150, 40), "COOK", 20, DoCook);
            _cookBtnLbl = _cookBtn.GetComponentInChildren<Text>();

            // ---- combine
            var comb = Box("Combine", 370, 472, 440, 228, "COMBINE  <size=14><color=#a69e91>two foods: secret recipes</color></size>");
            _slotA = MakeSlot(comb, 16, 40, "food 1", it => { _a = it; _cqty = 1; }, () => _a = null);
            K.Text(TL("Plus", comb, 102, 64, 30, 40), "+", 30, K.Dim, TextAnchor.MiddleCenter);
            _slotB = MakeSlot(comb, 134, 40, "food 2", it => { _b = it; _cqty = 1; }, () => _b = null);
            K.Text(TL("Arrow", comb, 220, 64, 36, 40), "→", 30, K.Dim, TextAnchor.MiddleCenter);
            _combOut = MakeSlot(comb, 258, 40, "", null, null);
            _combName = K.Text(TL("Name", comb, 16, 128, 408, 24), "", 17, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _combStats = K.Text(TL("Stats", comb, 16, 152, 408, 24), "", 14, K.Ink, TextAnchor.MiddleLeft);
            QtyRow(comb, 184, () => _cqty--, () => _cqty++, () => _cqty = 999, out _cqtyLbl);
            _combBtn = K.Button(TL("CombBtn", comb, 274, 182, 150, 40), "TRY IT", 20, DoCombine);
            _combBtnLbl = _combBtn.GetComponentInChildren<Text>();

            // ---- on the fire + combinations
            K.Text(TL("JobsLbl", _root, 830, 76, 390, 24), "ON THE FIRE", 18, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _jobList = K.ScrollList(TL("Jobs", _root, 830, 104, 390, 300), 3f);
            _takeBtn = K.Button(TL("Take", _root, 830, 412, 190, 38), "TAKE ALL", 18, () => { if (_fire && _fire.Cooking) _fire.Cooking.Collect(_inv, _player); });
            _status = K.Text(TL("Status", _root, 1030, 412, 190, 38), "", 14, K.Bad, TextAnchor.MiddleRight);
            K.Text(TL("CombosLbl", _root, 830, 462, 390, 24), "COMBINATIONS", 18, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _comboList = K.ScrollList(TL("Combos", _root, 830, 490, 390, 210), 2f);

            ASHSkills.Changed += MarkCombos;
            _root.gameObject.SetActive(false);
        }

        void OnDestroy() { ASHSkills.Changed -= MarkCombos; if (_i == this) { _i = null; ASHUIState.SetOpen(PanelId, false); } }
        void MarkCombos() => _combosDirty = true;

        // ------------------------------------------------------------------ helpers
        static RectTransform TL(string n, Transform p, float x, float y, float w, float h) => K.At(n, p, new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));

        static RectTransform Fill(RectTransform bg, Color c)
        {
            var f = K.Stretch("Fill", bg); K.Image(f, c);
            f.anchorMax = new Vector2(0, 1); f.offsetMin = f.offsetMax = Vector2.zero; return f;
        }
        static void SetFill(RectTransform f, float v) { f.anchorMax = new Vector2(Mathf.Clamp01(v), 1); f.offsetMin = f.offsetMax = Vector2.zero; }

        RectTransform Box(string n, float x, float y, float w, float h, string title)
        {
            var b = TL(n, _root, x, y, w, h); K.Image(b, K.Row, true);
            K.Text(TL("Title", b, 16, 8, w - 32, 26), title, 18, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            return b;
        }

        void Drop(RectTransform rt, System.Action<ASHItemDefinition> onDrop) => rt.gameObject.AddComponent<CookDrop>().onDrop = onDrop;

        Slot MakeSlot(RectTransform parent, float x, float y, string hint, System.Action<ASHItemDefinition> onDrop, System.Action onClear)
        {
            var s = new Slot { rt = TL("Slot", parent, x, y, 84, 84) };
            var bg = K.Image(s.rt, new Color(0, 0, 0, 0.4f), onDrop != null);
            var ring = K.Stretch("Edge", s.rt, 0, 0, 0, 0); var ri = K.Image(ring, new Color(1, 1, 1, 0.12f)); ri.type = Image.Type.Simple;
            var inner = K.Stretch("Inner", s.rt, 2, 2, 2, 2); K.Image(inner, new Color(0.08f, 0.07f, 0.06f, 0.95f));
            s.icon = K.Image(K.Stretch("Icon", s.rt, 8, 8, 8, 8), Color.white); s.icon.preserveAspect = true; s.icon.enabled = false;
            s.abbr = K.Text(K.Stretch("Abbr", s.rt), "", 26, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            s.hint = K.Text(K.Stretch("Hint", s.rt, 4, 4, 4, 4), hint, 13, K.Dim, TextAnchor.MiddleCenter);
            s.count = K.Text(K.Stretch("Count", s.rt, 4, 2, 6, 4), "", 15, K.Ink, TextAnchor.LowerRight, FontStyle.Bold);
            if (onDrop != null)
            {
                Drop(s.rt, it => { onDrop(it); });
                var b = s.rt.gameObject.AddComponent<Button>(); b.targetGraphic = bg; var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
                b.onClick.AddListener(() => onClear?.Invoke());
            }
            return s;
        }

        static void SetSlot(Slot s, ASHItemDefinition it, string count, bool unknown = false)
        {
            s.hint.enabled = !it && !unknown;
            s.icon.enabled = it && it.icon && !unknown; if (s.icon.enabled) s.icon.sprite = it.icon;
            s.abbr.text = unknown ? "?" : it && !it.icon ? it.displayName.Substring(0, Mathf.Min(2, it.displayName.Length)).ToUpperInvariant() : "";
            s.abbr.color = unknown ? K.Accent : it ? Color.Lerp(it.uiTint, Color.white, 0.3f) : K.Ink;
            s.count.text = it || unknown ? count : "";
        }

        void QtyRow(RectTransform parent, float y, System.Action minus, System.Action plus, System.Action max, out Text label)
        {
            K.Button(TL("Minus", parent, 16, y, 40, 36), "-", 22, () => minus(), new Color(1, 1, 1, 0.18f)).GetComponentInChildren<Text>().color = K.Ink;
            label = K.Text(TL("Qty", parent, 58, y, 72, 36), "x1", 20, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            K.Button(TL("Plus", parent, 132, y, 40, 36), "+", 22, () => plus(), new Color(1, 1, 1, 0.18f)).GetComponentInChildren<Text>().color = K.Ink;
            K.Button(TL("Max", parent, 178, y, 70, 36), "MAX", 16, () => max(), new Color(1, 1, 1, 0.18f)).GetComponentInChildren<Text>().color = K.Ink;
        }

        void Status(string m) { _status.text = m; _statusUntil = Time.unscaledTime + 3f; }

        // ------------------------------------------------------------------ open / close
        void Show(ASHCampfire fire, ASHInventory inv, ASHCrafting cr, Transform player)
        {
            if (_fire != fire) { _cookItem = _a = _b = null; _qty = _cqty = 1; }
            _fire = fire; _inv = inv; _cr = cr; _player = player;
            _packSig = _jobSig = null; _combosDirty = true;
            _root.gameObject.SetActive(true);
            ASHUIState.SetOpen(PanelId, true);
            Refresh();
        }

        void Close()
        {
            EndDrag();
            if (_root) _root.gameObject.SetActive(false);
            ASHUIState.SetOpen(PanelId, false);
        }

        void Update()
        {
            if (!IsOpen) return;
            bool far = !_fire || !_fire.isActiveAndEnabled || (_player && (_player.position - _fire.transform.position).sqrMagnitude > 64f);
            var stats = _player ? _player.GetComponentInParent<ASHSurvivalStats>() : null;
            if (far || (stats && stats.IsDead) || ASHUIState.IsOpen(ASHPanels.Inventory) || ASHUIState.IsOpen(ASHPanels.Journal) || ASHUIState.IsOpen(ASHPanels.Map) || ASHUIState.IsOpen(ASHPanels.Death))
            { Close(); return; }
            Refresh();
        }

        // ------------------------------------------------------------------ actions
        void AddFuel(ASHItemDefinition it)
        {
            if (!_fire || !it) return;
            if (ASHCampfire.FuelHoursOf(it) <= 0f) { Status($"{it.displayName} won't burn"); return; }
            if (!_fire.HasRoomForFuel) { Status("Fuel is full"); return; }
            if (!_fire.TryAddFuel(_inv, it)) Status("Couldn't add that");
        }

        void Quick(ASHItemDefinition it)
        {
            if (!it) return;
            if (ASHCampfire.FuelHoursOf(it) > 0f) { AddFuel(it); return; }
            if (ASHFireCooking.IsWaterContainer(it) && (_a != null) != (_b != null)) { if (!_a) _a = it; else _b = it; _cqty = 1; return; }
            if (RecipeFor(it)) { _cookItem = it; _qty = 1; return; }
            if (!_a) { _a = it; _cqty = 1; } else if (!_b) { _b = it; _cqty = 1; } else { _a = _b; _b = it; _cqty = 1; }
        }

        ASHRecipe RecipeFor(ASHItemDefinition it)
        {
            if (!it || !_cr) return null;
            if (ASHFireCooking.IsWaterContainer(it)) return ASHFireCooking.ContainerRecipe(_cr);
            foreach (var r in _cr.Recipes)
            {
                if (!r || !r.outputItem || r.station != ASHCraftStation.Campfire || !r.CooksOnFire) continue;
                foreach (var g in r.ingredients) if (g != null && g.item == it) return r;
            }
            return null;
        }

        void DoCook()
        {
            var r = RecipeFor(_cookItem); var k = Cook(); if (!r || !k) return;
            var why = CookBlock(r); if (why != null) { Status(why); return; }
            bool bottle = ASHFireCooking.IsWaterContainer(_cookItem);
            if (k.Begin(_cr, r, Mathf.Clamp(_qty, 1, ASHFireCooking.MaxFor(_cr, r, bottle)), bottle)) { _qty = 1; if (!bottle && ASHFireCooking.MaxFor(_cr, r) <= 0) _cookItem = null; }
        }

        string CookBlock(ASHRecipe r)
        {
            if (!r) return "Can't be cooked alone";
            var gate = ASHCrafting.RecipeGate?.Invoke(r); if (gate != null) return gate;
            var b = Cook() ? Cook().Block() : "No fire"; if (b != null) return b;
            if (ASHFireCooking.IsWaterContainer(_cookItem)) { if (ASHFireCooking.MaxFor(_cr, r, true) <= 0) return $"Not enough water in the bottle ({ASHFireCooking.PercentPerItem:0}% per item)"; return null; }
            if (ASHFireCooking.MaxFor(_cr, r) <= 0) return r.waterPercent > 0f ? "Not enough water" : "Not enough in the pack";
            return null;
        }

        void DoCombine()
        {
            var k = Cook(); if (!k || !_a || !_b) return;
            var b = k.Block(); if (b != null) { Status(b); return; }
            if (ASHFireCooking.MaxFor(_inv, _a, _b) <= 0) { Status(HasContainer ? $"Not enough water in the bottle ({ASHFireCooking.PercentPerItem:0}% per item)" : "Not enough in the pack"); return; }
            var c = ASHFireCooking.FindCombo(_a, _b, out var waterFor);
            if (c && waterFor && !ASHFireCooking.ContainerCanBe(waterFor)) { Status("The bottle's water is untreated: boil it first"); return; }
            var hud = FindAnyObjectByType<ASHHud>();
            if (!c)
            {
                if (ASHCookingSettings.I.wrongGuessWastesItems) { Waste(_a); Waste(_b); if (hud) hud.Toast($"{_a.displayName} and {_b.displayName} don't go together: both ruined."); }
                else if (hud) hud.Toast($"{_a.displayName} and {_b.displayName} don't go together. Try another pair.");
                Status("Nothing came of that");
                return;
            }
            if (!c.Known && ASHSkills.Learn(c.LearnKey))
            {
                ASHSkills.AddXp(ASHSkills.Cooking, ASHCookingSettings.I.xpDiscover);
                if (hud) hud.Toast($"Discovered a recipe: {c.displayName}!");
            }
            if (k.BeginCombo(_cr, c, Mathf.Max(1, _cqty), waterFor)) { _cqty = 1; if (ASHFireCooking.MaxFor(_inv, _a, _b) <= 0) { if (!ASHFireCooking.IsWaterContainer(_a)) _a = null; if (!ASHFireCooking.IsWaterContainer(_b)) _b = null; } }
        }

        bool HasContainer => ASHFireCooking.IsWaterContainer(_a) || ASHFireCooking.IsWaterContainer(_b);
        /// <summary>A wrong guess wastes one item, or one item's worth of water from a container (the bottle itself is kept).</summary>
        void Waste(ASHItemDefinition it)
        {
            if (!it) return;
            if (ASHFireCooking.IsWaterContainer(it)) ASHCrafting.ConsumeWater?.Invoke(Mathf.Min(ASHFireCooking.PercentPerItem, ASHFireCooking.ContainerPercent));
            else _inv.Remove(it, 1);
        }
        /// <summary>Slot count text: stack count, or the container's water % (and untreated).</summary>
        string CountText(ASHItemDefinition it)
        {
            if (!it) return "";
            if (ASHFireCooking.IsWaterContainer(it)) return $"{ASHFireCooking.ContainerPercent:0}%{(ASHFireCooking.ContainerDirty ? "!" : "")}";
            return "x" + _inv.Count(it);
        }

        ASHFireCooking Cook() => !_fire ? null : _fire.Cooking ? _fire.Cooking : _fire.gameObject.AddComponent<ASHFireCooking>();

        // ------------------------------------------------------------------ refresh
        void Refresh()
        {
            var s = ASHCookingSettings.I;
            _title.text = _fire ? _fire.StationName.ToUpperInvariant() : "FIRE";
            ASHSkills.Progress(ASHSkills.Cooking, out int lvl, out float into, out float need);
            int cut = Mathf.RoundToInt((1f - s.SkillTimeFactor(lvl)) * 100f);
            _skill.text = $"COOKING  <b>Lv {lvl}</b>   <color=#a69e91>{(need > 0f ? $"{into:0}/{need:0} XP" : "max level")}{(cut > 0 ? $"   -{cut}% cook time" : "")}</color>";
            SetFill(_skillFill, need > 0f ? into / need : 1f);
            if (Time.unscaledTime > _statusUntil) _status.text = "";

            // fuel
            bool lit = _fire && _fire.Lit;
            SetFill(_fuelFill, _fire ? _fire.Fuel01 : 0f);
            _fuelText.text = _fire ? $"{ASHGameTime.Format(_fire.FuelHours)} / {ASHGameTime.Format(_fire.MaxFuelHours)}   {(lit ? "<color=#9fd39b>LIT</color>" : _fire.FuelHours > 0f ? "<color=#a69e91>OUT</color>" : "<color=#f2806b>OUT: add fuel</color>")}" : "";
            _lightBtn.interactable = _fire && !lit && _fire.FuelHours > 0f;
            _outBtn.interactable = lit;
            var nf = _fire ? _fire.FindFuelIn(_inv, out _) : null;
            _addFuelBtn.interactable = nf;
            _addFuelLbl.text = nf ? $"ADD {nf.displayName.ToUpperInvariant()}" : _fire && !_fire.HasRoomForFuel ? "FUEL FULL" : "NO FUEL";

            RefreshCook(); RefreshCombine(); RefreshPack(); RefreshJobs();
            if (_combosDirty) { _combosDirty = false; RebuildCombos(); }
        }

        void RefreshCook()
        {
            if (_cookItem && _inv.Count(_cookItem) <= 0) _cookItem = null;
            var r = RecipeFor(_cookItem);
            bool bottle = ASHFireCooking.IsWaterContainer(_cookItem);
            SetSlot(_cookSlot, _cookItem, CountText(_cookItem));
            int max = r ? ASHFireCooking.MaxFor(_cr, r, bottle) : 0;
            _qty = Mathf.Clamp(_qty, 1, Mathf.Max(1, max));
            _qtyLbl.text = "x" + _qty;
            if (!_cookItem) { SetSlot(_cookOut, null, ""); _cookName.text = ""; _cookInfo.text = "Drop raw meat, fish, creek water or berries in the slot."; _cookStats.text = ""; _cookBtn.interactable = false; return; }
            if (!r) { SetSlot(_cookOut, null, "", true); _cookName.text = "Can't cook alone"; _cookInfo.text = "Try it in COMBINE with another food."; _cookStats.text = Stats(_cookItem); _cookBtn.interactable = false; return; }
            int per = Mathf.Max(1, r.outputAmount);
            SetSlot(_cookOut, r.outputItem, "x" + per * _qty);
            _cookName.text = r.outputItem.displayName;
            float secs = ASHFireCooking.FinalSeconds(ASHFireCooking.BaseSeconds(r));
            var why = CookBlock(r);
            string need = bottle ? $"\nUses {ASHFireCooking.PercentPerItem:0}% of the bottle each · bottle {ASHFireCooking.ContainerPercent:0}%{(ASHFireCooking.ContainerDirty ? " (untreated)" : "")}"
                : r.ingredients.Count > 1 || (r.ingredients.Count == 1 && r.ingredients[0].amount > 1) ? "\nUses " + Needs(r) : "";
            _cookInfo.text = (why != null ? $"<color=#f2806b>{why}</color>\n" : "") + $"{secs:0.#}s each · {secs * _qty:0.#}s total{need}";
            _cookStats.text = $"{_cookItem.displayName}: {Stats(_cookItem)}\n→ {r.outputItem.displayName}{(per > 1 ? " x" + per : "")}: {Stats(r.outputItem)}";
            _cookBtn.interactable = why == null;
            _cookBtnLbl.text = r.outputItem.itemId.Contains("water") ? "BOIL" : "COOK";
        }

        static string Needs(ASHRecipe r)
        {
            var sb = new StringBuilder();
            foreach (var g in r.ingredients) { if (g == null || !g.item) continue; if (sb.Length > 0) sb.Append(" + "); sb.Append(g.amount).Append(' ').Append(g.item.displayName); }
            if (r.waterPercent > 0f) sb.Append($" + {r.waterPercent:0}% water");
            return sb.ToString();
        }

        void RefreshCombine()
        {
            if (_a && _inv.Count(_a) <= 0) _a = null;
            if (_b && _inv.Count(_b) <= 0) _b = null;
            SetSlot(_slotA, _a, CountText(_a));
            SetSlot(_slotB, _b, CountText(_b));
            int max = ASHFireCooking.MaxFor(_inv, _a, _b);
            _cqty = Mathf.Clamp(_cqty, 1, Mathf.Max(1, max));
            _cqtyLbl.text = "x" + _cqty;
            if (!_a || !_b) { SetSlot(_combOut, null, ""); _combName.text = ""; _combStats.text = "<color=#a69e91>Put two foods in to try a combination. A right guess teaches it.</color>"; _combBtn.interactable = false; _combBtnLbl.text = "TRY IT"; return; }
            var c = ASHFireCooking.FindCombo(_a, _b, out var waterFor);
            var k = Cook(); var why = k ? k.Block() : "No fire";
            if (why == null && max <= 0) why = HasContainer ? $"Not enough water in the bottle ({ASHFireCooking.PercentPerItem:0}% per item)" : "Not enough in the pack";
            if (why == null && c && c.Known && waterFor && !ASHFireCooking.ContainerCanBe(waterFor)) why = "The bottle's water is untreated: boil it first";
            if (c && c.Known)
            {
                SetSlot(_combOut, c.output, "x" + c.outputAmount * _cqty);
                float secs = ASHFireCooking.FinalSeconds(ASHFireCooking.BaseSeconds(c));
                _combName.text = $"{c.displayName}  <size=14><color=#a69e91>{secs:0.#}s each</color></size>";
                _combStats.text = why != null ? $"<color=#f2806b>{why}</color>" : Stats(c.output) + (waterFor ? $"\n<color=#a69e91>{ASHFireCooking.PercentPerItem:0}% of the bottle each · bottle {ASHFireCooking.ContainerPercent:0}%</color>" : "");
                _combBtnLbl.text = "COOK";
            }
            else
            {
                SetSlot(_combOut, null, "", true);
                _combName.text = "???";
                _combStats.text = why != null ? $"<color=#f2806b>{why}</color>" : "<color=#a69e91>Unknown result. Try it to find out.</color>";
                _combBtnLbl.text = "TRY IT";
            }
            _combBtn.interactable = why == null;
        }

        void RefreshPack()
        {
            var counts = new Dictionary<ASHItemDefinition, int>(); var order = new List<ASHItemDefinition>();
            foreach (var sl in _inv.Slots)
            {
                if (sl == null || sl.IsEmpty || ASHInventory.IsKeyItem(sl.item) || !Useful(sl.item)) continue;
                if (!counts.ContainsKey(sl.item)) { counts[sl.item] = 0; order.Add(sl.item); }
                counts[sl.item] += sl.amount;
            }
            order.Sort((x, y) => { int fx = ASHCampfire.FuelHoursOf(x) > 0f ? 0 : 1, fy = ASHCampfire.FuelHoursOf(y) > 0f ? 0 : 1; return fx != fy ? fx.CompareTo(fy) : string.CompareOrdinal(x.displayName, y.displayName); });
            var sb = new StringBuilder(); foreach (var it in order) sb.Append(it.itemId).Append(':').Append(counts[it]).Append(';');
            sb.Append(Mathf.RoundToInt(ASHFireCooking.ContainerPercent)).Append(ASHFireCooking.ContainerDirty ? 'd' : 'c');
            string sig = sb.ToString(); if (sig == _packSig) return; _packSig = sig;
            K.Clear(_packList);
            if (order.Count == 0) { K.Layout(K.Text(K.Rect("Empty", _packList), "  Nothing to burn or cook in the pack.", 16, K.Dim), 40); return; }
            foreach (var it in order)
            {
                var row = K.Rect("Row", _packList); K.Layout(row, 50); K.Image(row, K.Row, true);
                var tile = row.gameObject.AddComponent<CookTile>(); tile.item = it; tile.menu = this;
                var ic = K.Image(K.At("Icon", row, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(40, 40)), it.icon ? Color.white : it.uiTint); if (it.icon) { ic.sprite = it.icon; ic.preserveAspect = true; }
                K.Text(K.Stretch("Name", row, 54, 0, 96, 0), it.displayName, 16, K.Ink);
                float fh = ASHCampfire.FuelHoursOf(it);
                bool wc = ASHFireCooking.IsWaterContainer(it);
                string tag = fh > 0f ? $"fuel {ASHGameTime.Format(fh)}" : wc ? $"water {ASHFireCooking.ContainerPercent:0}%{(ASHFireCooking.ContainerDirty ? " untreated" : "")}" : RecipeFor(it) ? "cook" : "combine";
                K.Text(K.Stretch("Right", row, 0, 0, 8, 0), wc ? $"<size=12><color=#a69e91>{tag}</color></size>" : $"x{counts[it]}  <size=12><color=#a69e91>{tag}</color></size>", 15, K.Ink, TextAnchor.MiddleRight);
            }
        }

        bool Useful(ASHItemDefinition it) =>
            ASHCampfire.FuelHoursOf(it) > 0f || RecipeFor(it) || ASHCookCombo.IsIngredient(it) || ASHFireCooking.IsWaterContainer(it) ||
            (it.IsConsumable && it.category != ASHItemCategory.Medical && (it.restoreHunger > 0f || it.restoreThirst > 0f));

        void RefreshJobs()
        {
            var k = _fire ? _fire.Cooking : null;
            var sb = new StringBuilder();
            if (k) foreach (var j in k.Jobs) sb.Append(j.GetHashCode()).Append(j.count).Append('/').Append(j.cooked).Append(';');
            string sig = sb.ToString();
            if (sig != _jobSig)
            {
                _jobSig = sig; K.Clear(_jobList); _jobRows.Clear();
                if (!k || k.Jobs.Count == 0) K.Layout(K.Text(K.Rect("Empty", _jobList), "  Nothing cooking.", 16, K.Dim), 40);
                else foreach (var j in k.Jobs)
                {
                    var job = j;
                    var row = K.Rect("Job", _jobList); K.Layout(row, 50); K.Image(row, K.Row);
                    var bar = K.At("Bar", row, new Vector2(0, 0), new Vector2(8, 6), new Vector2(290, 5)); K.Image(bar, new Color(1, 1, 1, 0.08f));
                    var fill = Fill(bar, j.boil ? new Color(0.55f, 0.75f, 0.95f) : K.Accent);
                    K.Text(K.Stretch("Name", row, 10, 10, 120, 0), $"{j.label} <size=13><color=#a69e91>{(j.count > 0 ? $"{j.count} left" : "done")}{(j.cooked > 0 ? $" · <color=#9fd39b>{j.cooked} ready</color>" : "")}</color></size>", 16, K.Ink);
                    var right = K.Text(K.Stretch("Right", row, 0, 10, 84, 0), "", 14, K.Dim, TextAnchor.MiddleRight);
                    if (j.count > 0) { var cb = K.Button(K.At("Cancel", row, new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(72, 30)), "TAKE OFF", 12, () => { if (k.Cancel(job, _cr)) _jobSig = null; }, new Color(1, 1, 1, 0.16f)); cb.GetComponentInChildren<Text>().color = K.Ink; }
                    _jobRows.Add((j, fill, right));
                }
            }
            int ready = k ? k.ReadyCount : 0;
            _takeBtn.interactable = ready > 0;
            _takeBtn.GetComponentInChildren<Text>().text = ready > 0 ? $"TAKE ALL ({ready})" : "TAKE ALL";
            foreach (var (job, fill, right) in _jobRows)
            {
                bool active = k && job.count > 0 && k.IsActive(job);
                SetFill(fill, job.count > 0 ? ASHFireCooking.Progress01(job) : 1f);
                right.text = job.count <= 0 ? "" : !_fire.Lit ? "fire out" : active ? $"{Mathf.Max(0f, job.per - job.done):0.0}s" : "queued";
            }
        }

        void RebuildCombos()
        {
            K.Clear(_comboList);
            var all = ASHCookCombo.All;
            if (all.Count == 0) { K.Layout(K.Text(K.Rect("Empty", _comboList), "  No combinations installed.", 15, K.Dim), 32); return; }
            int known = 0; foreach (var c in all) if (c.Known) known++;
            K.Layout(K.Text(K.Rect("Count", _comboList), $"  {known} / {all.Count} learned", 14, K.Dim), 22);
            foreach (var c in all)
            {
                var row = K.Rect("Combo", _comboList); K.Layout(row, 34); K.Image(row, K.Row);
                string txt = c.Known
                    ? $"<color=#9fd39b>{c.displayName}</color>  <size=13><color=#a69e91>{c.ingredientA.displayName} + {c.ingredientB.displayName}</color></size>"
                    : $"<color=#a69e91>???   <size=13>{(c.unlockLevel > 0 ? $"Cooking Lv {c.unlockLevel}, or discover it" : "discover it by combining")}</size></color>";
                K.Text(K.Stretch("T", row, 10, 0, 6, 0), txt, 15, K.Ink);
            }
        }

        static string Stats(ASHItemDefinition it)
        {
            if (!it) return "";
            var sb = new StringBuilder();
            void Add(string n, float v) { if (Mathf.Abs(v) < 0.01f) return; if (sb.Length > 0) sb.Append("  "); sb.Append(v > 0 ? "<color=#9fd39b>" : "<color=#f2806b>").Append(n).Append(' ').Append(v > 0 ? "+" : "").Append(v.ToString("0")).Append("</color>"); }
            Add("Hunger", it.restoreHunger); Add("Thirst", it.restoreThirst); Add("Health", it.restoreHealth); Add("Stamina", it.restoreStamina);
            float sick = Mathf.Clamp01(it.sicknessChance * ASHCookingSettings.I.sicknessChanceMultiplier);
            if (sick > 0f) { if (sb.Length > 0) sb.Append("  "); sb.Append($"<color=#f2806b>Sickness {sick * 100f:0}%</color>"); }
            else if (sb.Length > 0 && (it.restoreHunger > 0f || it.restoreThirst > 0f)) sb.Append("  <color=#a69e91>safe</color>");
            return sb.Length > 0 ? sb.ToString() : "<color=#a69e91>no food value</color>";
        }

        // ------------------------------------------------------------------ drag
        internal void BeginDrag(ASHItemDefinition it)
        {
            EndDrag();
            _ghost = K.At("DragGhost", transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64, 64));
            var img = K.Image(_ghost, it.icon ? Color.white : it.uiTint); if (it.icon) { img.sprite = it.icon; img.preserveAspect = true; }
            img.raycastTarget = false;
            var cg = _ghost.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.alpha = 0.85f;
        }
        internal void Drag(Vector2 screen) { if (_ghost) _ghost.position = screen; }
        internal void EndDrag() { if (_ghost) Destroy(_ghost.gameObject); _ghost = null; }
        internal void Click(ASHItemDefinition it) => Quick(it);

        sealed class CookTile : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
        {
            public ASHItemDefinition item; public ASHCookMenu menu;
            public void OnBeginDrag(PointerEventData e) { if (menu && item) { menu.BeginDrag(item); menu.Drag(e.position); } }
            public void OnDrag(PointerEventData e) { if (menu) menu.Drag(e.position); }
            public void OnEndDrag(PointerEventData e) { if (menu) menu.EndDrag(); }
            public void OnPointerClick(PointerEventData e) { if (menu && item && !e.dragging) menu.Click(item); }
        }

        sealed class CookDrop : MonoBehaviour, IDropHandler
        {
            public System.Action<ASHItemDefinition> onDrop;
            public void OnDrop(PointerEventData e)
            {
                var t = e.pointerDrag ? e.pointerDrag.GetComponent<CookTile>() : null;
                if (t && t.item) onDrop?.Invoke(t.item);
            }
        }
    }
}
