using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Inventory;
using ASH.Map;
using ASH.Survival;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>
    /// Field journal: Crafting (recipes by category with have/need, tool and station requirements, craft button and
    /// progress), Items (carried items with Use / Break Down) and Notes (survey logs, discoveries). Data comes from
    /// ASHRecipe / ASHItemDefinition ScriptableObjects. Layout idea from the Genesis JournalPanelUI tab rail +
    /// CraftingUI detail pane, rebuilt lite in code-built uGUI.
    /// </summary>
    public sealed class ASHJournalUI : MonoBehaviour
    {
        public enum Tab { Crafting, Items, Notes, Objectives, Skills, Pets, Character }

        ASHInventory _inv; ASHCrafting _craft; Func<ASHItemDefinition, bool> _useItem;
        readonly Dictionary<Tab, GameObject> _tabs = new Dictionary<Tab, GameObject>();
        readonly Dictionary<Tab, Image> _tabButtons = new Dictionary<Tab, Image>();
        readonly Dictionary<string, Image> _catButtons = new Dictionary<string, Image>();
        RectTransform _recipeList, _itemList, _notesList, _objList; Text _oTitle, _oLines, _oStatus, _oTrackLabel; Button _oTrack; string _selObj;
        ASH.Gameplay.ASHObjectiveTracker _objectives;
        /// <summary>Objective tracker shown in the Objectives tab (set by the HUD).</summary>
        public ASH.Gameplay.ASHObjectiveTracker Objectives { get => _objectives; set { if (_objectives && isActiveAndEnabled) _objectives.Changed -= MarkDirty; _objectives = value; if (_objectives && isActiveAndEnabled) _objectives.Changed += MarkDirty; } }
        Image _dIcon, _iIcon; Text _dIconAbbr, _iIconAbbr, _dName, _dDesc, _dReq, _dStatus, _iName, _iDesc, _iInfo, _iUseLabel, _iBreakLabel;
        Button _dCraft, _iUse, _iBreak; RectTransform _dProg, _dProgFill;
        string _cat = "All"; ASHRecipe _selRecipe; ASHItemDefinition _selItem; Tab _tab = Tab.Items;
        bool _dirty; float _objRefresh;
        // Grandpa's Field Manual tree (replaces the category list when the manual exists)
        RectTransform _legacyRoot, _treeRoot, _treeContent, _pageBoxes; Text _treeTitle, _pageLabel; ASHFieldManual _manualHooked;
        // torn-page reader (a book over the Survival Guide tab): found pages only, prev/next flips
        Button _gpBtn; Text _gpLbl; string _bookCol, _gpPage; // t443: Grandpa's Journal pages in the same page reader
        RectTransform _book; Text _bookTitle, _bookText, _bookNum, _readLbl; Image _bookSketch; Button _bookPrev, _bookNext, _readBtn; int _bookPage = -1;
        const float NodeW = 128f, NodeH = 52f, ColStep = 142f, RowStep = 68f, TreePad = 14f;

        public Tab Current => _tab;
        public bool IsOpen => gameObject.activeSelf;
        public ASHRecipe SelectedRecipe => _selRecipe;

        public static ASHJournalUI Create(RectTransform parent, ASHInventory inv, ASHCrafting craft, Func<ASHItemDefinition, bool> useItem)
        {
            var rt = K.At("Journal", parent, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1200, 720));
            K.Image(rt, K.Panel, true);
            var ui = rt.gameObject.AddComponent<ASHJournalUI>();
            ui._inv = inv; ui._craft = craft; ui._useItem = useItem;
            ui.Build(rt);
            rt.gameObject.SetActive(false);
            return ui;
        }

        void OnEnable()
        {
            if (_inv) _inv.Changed += MarkDirty;
            if (_craft) { _craft.Crafted += OnCraftEvent; _craft.BrokeDown += OnBreakEvent; }
            var log = ASHJournalLog.Instance; if (log) log.Added += OnNote;
            if (_objectives) _objectives.Changed += MarkDirty;
            ASH.Gameplay.ASHSkills.Changed += MarkDirty;
            ASH.Gameplay.ASHCharacterEquipment.Changed += MarkDirty;
            _dirty = true;
        }
        void OnDisable()
        {
            if (_inv) _inv.Changed -= MarkDirty;
            if (_craft) { _craft.Crafted -= OnCraftEvent; _craft.BrokeDown -= OnBreakEvent; }
            var log = ASHJournalLog.Instance; if (log) log.Added -= OnNote;
            if (_objectives) _objectives.Changed -= MarkDirty;
            ASH.Gameplay.ASHSkills.Changed -= MarkDirty;
            ASH.Gameplay.ASHCharacterEquipment.Changed -= MarkDirty;
        }
        void MarkDirty() => _dirty = true;
        void OnCraftEvent(ASHRecipe r) => _dirty = true;
        void OnBreakEvent(ASHItemDefinition i, string s) => _dirty = true;
        void OnNote(ASHJournalLog.Entry e) => _dirty = true;

        // ------------------------------------------------------------------ build
        void Build(RectTransform rt)
        {
            var accent = K.At("Accent", rt, new Vector2(0.5f, 1), Vector2.zero, new Vector2(1200, 3)); K.Image(accent, K.Accent);
            K.Text(K.At("Title", rt, new Vector2(0, 1), new Vector2(28, -18), new Vector2(400, 40)), "JOURNAL", 30, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            float x = 210;
            foreach (Tab t in new[] { Tab.Items, Tab.Character, Tab.Notes, Tab.Objectives, Tab.Skills, Tab.Pets, Tab.Crafting }) // the Survival Guide tab comes last (hidden until unlocked)
            {
                var tab = t;
                float w = t == Tab.Crafting ? 150 : 96;
                var b = K.At("Tab" + t, rt, new Vector2(0, 1), new Vector2(x, -20), new Vector2(w, 38)); x += w + 4;
                string label = t == Tab.Crafting ? "SURVIVAL GUIDE" : t == Tab.Character ? "ROOK" : t.ToString().ToUpperInvariant();
                K.Button(b, label, 16, () => Show(tab), new Color(1, 1, 1, 0.12f));
                _tabButtons[t] = b.GetComponent<Image>();
            }
            K.Text(K.At("Hint", rt, new Vector2(1, 1), new Vector2(-24, -26), new Vector2(270, 26)), "<color=#d9cbb4>J</color> close  <color=#d9cbb4>M</color> map  <color=#d9cbb4>Tab</color> pack", 14, K.Dim, TextAnchor.MiddleRight);

            var body = K.Stretch("Body", rt, 20, 20, 20, 76);
            _tabs[Tab.Crafting] = BuildCrafting(body);
            _tabs[Tab.Items] = BuildItems(body);
            _tabs[Tab.Notes] = BuildNotes(body);
            _tabs[Tab.Objectives] = BuildObjectives(body);
            _tabs[Tab.Skills] = BuildSkills(body);
            _tabs[Tab.Pets] = BuildPets(body);
            _tabs[Tab.Character] = ASHCharacterTab.Build(body);
        }

        // ------------------------------------------------------------------ pets (ASH.Companion.ASHDogCompanion)
        RectTransform _petList; Text _pTitle, _pLines, _pRight; RectTransform _pXpFill;

        GameObject BuildPets(RectTransform body)
        {
            var root = K.Stretch("Pets", body);
            _petList = K.ScrollList(K.Stretch("List", root, 0, 0, 640, 0));
            var det = K.Stretch("Detail", root, 540, 0, 0, 0);
            K.Image(det, new Color(1, 1, 1, 0.04f));
            _pTitle = K.Text(K.At("Name", det, new Vector2(0, 1), new Vector2(24, -22), new Vector2(590, 40)), "", 28, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            var xbg = K.At("XpBar", det, new Vector2(0, 1), new Vector2(24, -50), new Vector2(330, 8)); K.Image(xbg, new Color(1, 1, 1, 0.1f));
            _pXpFill = K.Stretch("Fill", xbg); K.Image(_pXpFill, K.Accent); _pXpFill.anchorMax = new Vector2(0, 1);
            _pLines = K.Text(K.At("Lines", det, new Vector2(0, 1), new Vector2(24, -64), new Vector2(330, 560)), "", 17, K.Ink, TextAnchor.UpperLeft);
            _pRight = K.Text(K.At("Abilities", det, new Vector2(0, 1), new Vector2(370, -64), new Vector2(250, 560)), "", 15, K.Ink, TextAnchor.UpperLeft);
            K.Button(K.At("Call", det, new Vector2(1, 1), new Vector2(-24, -18), new Vector2(150, 40)), "CALL (C)", 17, () => { var d = ASH.Companion.ASHDogCompanion.Active; if (d) d.Call(); _dirty = true; });
            return root.gameObject;
        }

        void RebuildPets()
        {
            K.Clear(_petList);
            var d = ASH.Companion.ASHDogCompanion.Active; var S = ASH.Companion.ASHCompanionSettings.I;
            if (!d) { K.Layout(K.Text(K.Rect("Empty", _petList), "No companion yet.", 20, K.Dim), 60); _pTitle.text = ""; _pLines.text = ""; _pRight.text = ""; _pXpFill.anchorMax = new Vector2(0, 1); return; }
            Row(_petList, null, d.DogName, "Lv " + d.Level, true, () => { });
            float need = d.XpNeed;
            _pXpFill.anchorMax = new Vector2(need > 0f ? Mathf.Clamp01(d.Xp / need) : 1f, 1); _pXpFill.offsetMin = _pXpFill.offsetMax = Vector2.zero;
            _pTitle.text = $"{d.DogName}  <color=#e8a35a>Lv {d.Level}</color>";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(need > 0f ? $"<color=#a69e91>{d.Xp:0} / {need:0} XP to level {d.Level + 1}</color>" : "<color=#a69e91>Max level</color>");
            sb.AppendLine();
            sb.AppendLine($"Mode  <b>{d.Mode}</b>   <color=#a69e91>{d.Activity}</color>");
            sb.AppendLine(d.Dead ? "<color=#f2806b>Dead</color>" : d.Downed ? "<color=#f2806b>Down, needs help (hold E on it)</color>" : $"Health  {Mathf.CeilToInt(d.Health)} / {Mathf.CeilToInt(d.MaxHealth)}");
            if (S.hunger) sb.AppendLine($"Hunger  {d.Hunger:0}%{(d.Hunger < S.starvingBelow ? "  <color=#f2806b>starving</color>" : "")}");
            if (S.bond) sb.AppendLine($"Bond  {d.Bond:0}%");
            if (d.CallReadyIn > 0f) sb.AppendLine($"<color=#a69e91>Call ready in {d.CallReadyIn:0} s</color>");
            sb.AppendLine($"Finds within {S.DetectRadius(d.Level):0} m, senses danger at {S.DangerRadius(d.Level):0} m, bite {S.Damage(d.Level):0}");
            sb.AppendLine();
            int used = 0; foreach (var p in d.Pack) if (!p.IsEmpty) used++;
            sb.AppendLine($"<b>Pack</b>  <color=#a69e91>{used} / {d.PackSlots}</color>");
            foreach (var p in d.Pack) if (!p.IsEmpty) sb.AppendLine($"  {p.item.displayName}  x{p.amount}");
            if (used == 0) sb.AppendLine("  <color=#857d72>empty</color>");
            _pLines.text = sb.ToString();
            var ab = new System.Text.StringBuilder(); ab.AppendLine("<b>Abilities</b>"); bool next = false;
            foreach (var a in S.Abilities())
            {
                if (d.Level >= a.level) ab.AppendLine($"<color=#9fd39b>√ {a.name}</color>\n<color=#a69e91>  {a.desc}</color>");
                else { ab.AppendLine($"<color={(next ? "#857d72" : "#e8a35a")}>Lv {a.level}  {a.name}</color>\n<color=#857d72>  {a.desc}</color>"); next = true; }
            }
            _pRight.text = ab.ToString();
        }

        // ------------------------------------------------------------------ skills (ASHSkills; Cooking + its combinations)
        RectTransform _skillList; Text _sTitle, _sLines; string _selSkill = ASH.Gameplay.ASHSkills.Cooking;
        GameObject BuildSkills(RectTransform body)
        {
            var root = K.Stretch("Skills", body);
            _skillList = K.ScrollList(K.Stretch("List", root, 0, 0, 640, 0));
            var det = K.Stretch("Detail", root, 540, 0, 0, 0);
            K.Image(det, new Color(1, 1, 1, 0.04f));
            _sTitle = K.Text(K.At("Name", det, new Vector2(0, 1), new Vector2(24, -22), new Vector2(590, 40)), "", 28, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _sLines = K.Text(K.At("Lines", det, new Vector2(0, 1), new Vector2(24, -72), new Vector2(590, 540)), "", 18, K.Ink, TextAnchor.UpperLeft);
            return root.gameObject;
        }

        void RebuildSkills()
        {
            K.Clear(_skillList);
            var ids = new List<string>(ASH.Gameplay.ASHSkills.Defined);
            if (ids.Count == 0) { K.Layout(K.Text(K.Rect("Empty", _skillList), "No skills yet.", 20, K.Dim), 60); _sTitle.text = ""; _sLines.text = ""; return; }
            if (!ids.Contains(_selSkill)) _selSkill = ids[0];
            foreach (var id in ids)
            {
                var sid = id; ASH.Gameplay.ASHSkills.Progress(id, out int l, out _, out _);
                Row(_skillList, null, ASH.Gameplay.ASHSkills.NameOf(id), "Lv " + l, id == _selSkill, () => { _selSkill = sid; _dirty = true; });
            }
            ASH.Gameplay.ASHSkills.Progress(_selSkill, out int lvl, out float into, out float need);
            _sTitle.text = $"{ASH.Gameplay.ASHSkills.NameOf(_selSkill)}  <color=#e8a35a>Lv {lvl}</color>";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(need > 0f ? $"{into:0} / {need:0} XP to level {lvl + 1}" : "Max level");
            if (_selSkill == ASH.Gameplay.ASHSkills.Cooking)
            {
                var cs = ASH.Gameplay.ASHCookingSettings.I;
                sb.AppendLine($"<color=#a69e91>Learn by cooking at a campfire or stove. Cook time -{Mathf.RoundToInt((1f - cs.SkillTimeFactor(lvl)) * 100f)}%, extra-portion chance {cs.SkillBonusChance(lvl) * 100f:0}%.</color>");
                var all = ASH.Gameplay.ASHCookCombo.All; int known = 0; foreach (var c in all) if (c.Known) known++;
                sb.AppendLine(); sb.AppendLine($"<b>Combinations</b>  <color=#a69e91>{known} / {all.Count} learned</color>");
                foreach (var c in all)
                    sb.AppendLine(c.Known ? $"<color=#9fd39b>√ {c.displayName}</color>  <color=#a69e91>{c.ingredientA.displayName} + {c.ingredientB.displayName}</color>"
                                          : $"<color=#857d72>?  ???   {(c.unlockLevel > 0 ? $"(Cooking Lv {c.unlockLevel}, or discover it)" : "(discover it by combining)")}</color>");
            }
            _sLines.text = sb.ToString();
        }

        GameObject BuildObjectives(RectTransform body)
        {
            var root = K.Stretch("Objectives", body);
            _objList = K.ScrollList(K.Stretch("List", root, 0, 0, 640, 0));
            var det = K.Stretch("Detail", root, 540, 0, 0, 0);
            K.Image(det, new Color(1, 1, 1, 0.04f));
            _oTitle = K.Text(K.At("Name", det, new Vector2(0, 1), new Vector2(24, -22), new Vector2(590, 40)), "", 28, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _oStatus = K.Text(K.At("Status", det, new Vector2(0, 1), new Vector2(24, -62), new Vector2(590, 26)), "", 18, K.Dim, TextAnchor.MiddleLeft);
            _oLines = K.Text(K.At("Lines", det, new Vector2(0, 1), new Vector2(24, -104), new Vector2(590, 400)), "", 20, K.Ink, TextAnchor.UpperLeft);
            _oTrack = K.Button(K.At("Track", det, new Vector2(0, 0), new Vector2(24, 20), new Vector2(260, 48)), "TRACK", 22, () => { if (_objectives && _selObj != null) { _objectives.Track(_selObj); _dirty = true; } });
            _oTrackLabel = _oTrack.GetComponentInChildren<Text>();
            return root.gameObject;
        }

        void RebuildObjectives()
        {
            K.Clear(_objList);
            if (!_objectives) { K.Layout(K.Text(K.Rect("Empty", _objList), "No objectives.", 20, K.Dim), 60); return; }
            var tracked = _objectives.Tracked;
            var all = new List<ASH.Gameplay.ASHObjectiveTracker.Objective>(_objectives.Objectives);
            if (_selObj == null || all.Find(o => o.Id == _selObj) == null) _selObj = tracked?.Id;
            bool anyDone = false;
            foreach (var o in all)
            {
                if (o.Completed && !anyDone) { anyDone = true; K.Layout(K.Text(K.Rect("Done", _objList), "  COMPLETED", 15, K.Dim), 28); }
                var obj = o; bool isTracked = tracked == o;
                string title = o.Completed ? $"<color=#857d72>{o.Title}</color>" : isTracked ? $"<color=#e8a35a>◆</color> {o.Title}" : o.Title;
                string right = o.Completed ? "done" : isTracked ? (_objectives.PlayerPicked ? "TRACKED" : "TRACKED (auto)") : Progress(o);
                // click = select + track (click the tracked one again = back to automatic: newest objective)
                Row(_objList, null, title, right, o.Id == _selObj, () => { _selObj = obj.Id; if (!obj.Completed) _objectives.Track(obj.Id); _dirty = true; });
            }
            var sel = all.Find(o => o.Id == _selObj);
            _oTitle.text = sel != null ? sel.Title : "";
            _oStatus.text = sel == null ? "" : sel.Completed ? "Completed" : sel == tracked ? (_objectives.PlayerPicked ? "Tracked on the HUD" : "Tracked automatically (newest)") : "Active - click to track";
            var sb = new System.Text.StringBuilder();
            if (sel != null) foreach (var (text, done) in sel.GetLines()) sb.AppendLine((done ? "<color=#9fd39b>√ " : "• ") + text + (done ? "</color>" : ""));
            _oLines.text = sb.ToString();
            _oTrack.gameObject.SetActive(sel != null && !sel.Completed);
            if (sel != null) _oTrackLabel.text = sel == tracked && _objectives.PlayerPicked ? "STOP TRACKING" : sel == tracked ? "TRACKING" : "TRACK";
        }

        static string Progress(ASH.Gameplay.ASHObjectiveTracker.Objective o)
        {
            int n = 0, d = 0; foreach (var l in o.GetLines()) { n++; if (l.done) d++; }
            return n > 0 ? $"{d}/{n}" : "";
        }

        GameObject BuildCrafting(RectTransform body)
        {
            var root = K.Stretch("Crafting", body);
            _legacyRoot = K.Stretch("Legacy", root);
            var cats = K.Stretch("Categories", _legacyRoot, 0, 0, 1000, 0);
            string[] names = { "All", "Fire", "Medical", "Food", "Tools", "Materials", "Clothing" };
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                var b = K.At("Cat" + n, cats, new Vector2(0, 1), new Vector2(0, -i * 50), new Vector2(150, 42));
                K.Button(b, n, 18, () => { _cat = n; _dirty = true; }, new Color(1, 1, 1, 0.08f));
                b.GetComponentInChildren<Text>().color = K.Ink;
                _catButtons[n] = b.GetComponent<Image>();
            }
            _recipeList = K.ScrollList(K.Stretch("Recipes", _legacyRoot, 170, 0, 560, 0));
            BuildTree(root);

            var det = K.Stretch("Detail", root, 610, 0, 0, 0);
            K.Image(det, new Color(1, 1, 1, 0.04f));
            var iconBox = K.At("IconBox", det, new Vector2(0, 1), new Vector2(20, -20), new Vector2(110, 110)); K.Image(iconBox, new Color(0, 0, 0, 0.35f));
            _dIcon = K.Image(K.Stretch("Icon", iconBox, 8, 8, 8, 8), Color.white); _dIcon.preserveAspect = true;
            _dIconAbbr = K.Text(K.Stretch("Abbr", iconBox), "", 40, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _dName = K.Text(K.At("Name", det, new Vector2(0, 1), new Vector2(150, -22), new Vector2(400, 40)), "", 28, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _dDesc = K.Text(K.At("Desc", det, new Vector2(0, 1), new Vector2(150, -66), new Vector2(400, 70)), "", 18, K.Dim, TextAnchor.UpperLeft);
            _dReq = K.Text(K.At("Req", det, new Vector2(0, 1), new Vector2(24, -150), new Vector2(520, 300)), "", 20, K.Ink, TextAnchor.UpperLeft);
            _dStatus = K.Text(K.At("Status", det, new Vector2(0, 0), new Vector2(24, 96), new Vector2(520, 30)), "", 19, K.Dim, TextAnchor.MiddleLeft);
            _dProg = K.At("Progress", det, new Vector2(0, 0), new Vector2(24, 80), new Vector2(520, 8)); K.Image(_dProg, new Color(0, 0, 0, 0.5f));
            _dProgFill = K.Stretch("Fill", _dProg); K.Image(_dProgFill, K.Accent); _dProgFill.pivot = new Vector2(0, 0.5f);
            _dCraft = K.Button(K.At("Craft", det, new Vector2(0, 0), new Vector2(24, 20), new Vector2(220, 48)), "CRAFT", 22, () => { if (_selRecipe && _craft) { _craft.TryCraft(_selRecipe); _dirty = true; } });
            BuildBook(root);
            return root.gameObject;
        }

        void BuildTree(RectTransform root)
        {
            _treeRoot = K.Stretch("ManualTree", root, 0, 0, 570, 0);
            K.Image(_treeRoot, new Color(0.93f, 0.88f, 0.76f, 0.07f));
            _treeTitle = K.Text(K.At("Title", _treeRoot, new Vector2(0, 1), new Vector2(12, -4), new Vector2(570, 30)), "", 19, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _pageLabel = K.Text(K.At("Pages", _treeRoot, new Vector2(0, 1), new Vector2(12, -34), new Vector2(160, 24)), "", 16, K.Dim, TextAnchor.MiddleLeft);
            _pageBoxes = K.At("PageBoxes", _treeRoot, new Vector2(0, 1), new Vector2(172, -36), new Vector2(400, 20));
            _readBtn = K.Button(K.At("ReadPages", _treeRoot, new Vector2(1, 1), new Vector2(-8, -4), new Vector2(160, 28)), "READ PAGES", 15, () => OpenBook(-1), new Color(1, 1, 1, 0.12f));
            _readLbl = _readBtn.GetComponentInChildren<Text>(); _readLbl.color = K.Ink;
            _gpBtn = K.Button(K.At("GrandpaPages", _treeRoot, new Vector2(1, 1), new Vector2(-176, -4), new Vector2(200, 28)), "GRANDPA'S JOURNAL", 15, () => OpenGrandpa(null), new Color(1, 1, 1, 0.12f));
            _gpLbl = _gpBtn.GetComponentInChildren<Text>(); _gpLbl.color = K.Ink; _gpBtn.gameObject.SetActive(false);
            var area = K.Stretch("Area", _treeRoot, 0, 0, 0, 66);
            K.Image(area, new Color(0, 0, 0, 0.12f), true);
            var vp = K.Stretch("Viewport", area); vp.gameObject.AddComponent<RectMask2D>();
            _treeContent = K.Rect("Content", vp);
            _treeContent.anchorMin = _treeContent.anchorMax = _treeContent.pivot = new Vector2(0, 1);
            _treeContent.anchoredPosition = Vector2.zero;
            var sr = area.gameObject.AddComponent<ScrollRect>();
            sr.viewport = vp; sr.content = _treeContent; sr.horizontal = true; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30f; sr.inertia = false;
        }

        void BuildBook(RectTransform root)
        {
            _book = K.Stretch("PageReader", root);
            K.Image(_book, new Color(0, 0, 0, 0.82f), true); // blocks the tree underneath
            var page = K.At("Page", _book, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 600));
            K.Image(page, K.Paper, true);
            _bookNum = K.Text(K.At("Num", page, new Vector2(0, 1), new Vector2(30, -18), new Vector2(300, 24)), "", 16, new Color(K.PaperInk.r, K.PaperInk.g, K.PaperInk.b, 0.6f), TextAnchor.MiddleLeft);
            _bookTitle = K.Text(K.At("Title", page, new Vector2(0, 1), new Vector2(30, -44), new Vector2(700, 40)), "", 28, K.PaperInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            _bookSketch = K.Image(K.At("Sketch", page, new Vector2(1, 1), new Vector2(-30, -96), new Vector2(260, 260)), new Color(1, 1, 1, 0.9f));
            _bookSketch.preserveAspect = true;
            _bookText = K.Text(K.At("Text", page, new Vector2(0, 1), new Vector2(30, -96), new Vector2(700, 400)), "", 20, K.PaperInk, TextAnchor.UpperLeft, FontStyle.Italic);
            _bookPrev = K.Button(K.At("Prev", page, new Vector2(0, 0), new Vector2(24, 18), new Vector2(150, 42)), "< PREV", 18, () => FlipBook(-1));
            _bookNext = K.Button(K.At("Next", page, new Vector2(1, 0), new Vector2(-24, 18), new Vector2(150, 42)), "NEXT >", 18, () => FlipBook(1));
            K.Button(K.At("Close", page, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(150, 42)), "CLOSE", 18, CloseBook, new Color(0.22f, 0.18f, 0.14f, 0.25f));
            _book.gameObject.SetActive(false);
        }

        /// <summary>Opens the page reader at page n (or the first unread / first found page when n &lt; 1).</summary>
        public void OpenBook(int n)
        {
            var m = ASHFieldManual.Instance; if (!m || !_book) return;
            var found = m.FoundPages; if (found.Count == 0) return; _bookCol = null;
            if (n < 1 || !found.Contains(n)) { n = found[0]; foreach (var p in found) if (m.IsPageUnread(p)) { n = p; break; } }
            _bookPage = n; _book.gameObject.SetActive(true); _book.SetAsLastSibling();
            ShowBookPage();
        }
        static List<ASH.Prologue.ASHJournalCollectionPage> GpFound()
        {
            var c = ASH.Prologue.ASHJournalCollection.Grandpa; var l = new List<ASH.Prologue.ASHJournalCollectionPage>(); if (!c) return l;
            foreach (var p in c.pages) if (p != null && ASH.Prologue.ASHGrandpaJournal.Has(c.collectionId, p.id)) l.Add(p);
            l.Sort((a, b) => a.number.CompareTo(b.number)); return l;
        }
        /// <summary>t443: open the page reader on Grandpa's Journal (id null = first found page).</summary>
        public void OpenGrandpa(string id)
        {
            if (!_book) return; var f = GpFound(); if (f.Count == 0) return;
            _bookCol = "grandpa"; _gpPage = id != null && f.Exists(p => p.id == id) ? id : f[0].id;
            _book.gameObject.SetActive(true); _book.SetAsLastSibling(); ShowGrandpaPage();
        }
        void ShowGrandpaPage()
        {
            var c = ASH.Prologue.ASHJournalCollection.Grandpa; var f = GpFound(); int i = f.FindIndex(p => p.id == _gpPage); if (!c || i < 0) { CloseBook(); return; }
            var pg = f[i];
            _bookNum.text = $"{c.title} - page {pg.number} of {c.pages.Count}  ·  {pg.TypeLabel}{(string.IsNullOrEmpty(pg.area) ? "" : "  ·  " + pg.area)}      ({i + 1}/{f.Count} found)";
            _bookTitle.text = pg.title;
            bool sk = pg.sketch; _bookSketch.gameObject.SetActive(sk); if (sk) _bookSketch.sprite = pg.sketch;
            _bookText.rectTransform.sizeDelta = new Vector2(sk ? 420 : 700, 400);
            _bookText.text = string.IsNullOrEmpty(pg.text) ? "<color=#00000080>The ink has run. Only a few words are left.</color>" : pg.text;
            _bookPrev.interactable = i > 0; _bookNext.interactable = i < f.Count - 1;
        }
        void FlipBook(int dir)
        {
            if (_bookCol != null) { var f = GpFound(); int k = f.FindIndex(p => p.id == _gpPage); if (k < 0) return; k = Mathf.Clamp(k + dir, 0, f.Count - 1); _gpPage = f[k].id; ShowGrandpaPage(); return; }
            var m = ASHFieldManual.Instance; if (!m) return;
            var found = m.FoundPages; int i = found.IndexOf(_bookPage); if (i < 0) return;
            i = Mathf.Clamp(i + dir, 0, found.Count - 1); _bookPage = found[i]; ShowBookPage();
        }
        void ShowBookPage()
        {
            var m = ASHFieldManual.Instance; if (!m || _bookPage < 1) { CloseBook(); return; }
            var found = m.FoundPages; int i = found.IndexOf(_bookPage); if (i < 0) { CloseBook(); return; }
            var d = m.Data; var pg = d ? d.Page(_bookPage) : null;
            _bookNum.text = $"Page {_bookPage} of {(d ? d.totalPages : 12)}      ({i + 1}/{found.Count} found)";
            _bookTitle.text = pg != null ? pg.title : "Torn page";
            bool sk = pg != null && pg.sketch;
            _bookSketch.gameObject.SetActive(sk); if (sk) _bookSketch.sprite = pg.sketch;
            _bookText.rectTransform.sizeDelta = new Vector2(sk ? 420 : 700, 400);
            _bookText.text = pg != null && !string.IsNullOrEmpty(pg.text) ? pg.text : "<color=#00000080>The ink has run. Only a few words are left.</color>";
            _bookPrev.interactable = i > 0; _bookNext.interactable = i < found.Count - 1;
            m.MarkPageRead(_bookPage); // raises Changed: the strip's unread dot and the tab count update
        }
        void CloseBook() { if (_book) _book.gameObject.SetActive(false); _bookPage = -1; _bookCol = null; }

        void HookManual()
        {
            var m = ASHFieldManual.Instance;
            if (m == _manualHooked) return;
            if (_manualHooked) _manualHooked.Changed -= MarkDirty;
            _manualHooked = m;
            if (m) m.Changed += MarkDirty;
            ASH.Prologue.ASHGrandpaJournal.PageFound -= OnGpFound; ASH.Prologue.ASHGrandpaJournal.PageFound += OnGpFound;
        }

        void OnGpFound(string col, string id) { if (this) MarkDirty(); else ASH.Prologue.ASHGrandpaJournal.PageFound -= OnGpFound; }

        static Vector2 NodePos(ASHFieldManualEntry e) => new Vector2(TreePad + e.column * ColStep, -(TreePad + e.row * RowStep));

        void Line(Vector2 a, Vector2 b, Color c)
        {
            var rt = K.Rect("Line", _treeContent);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), y0 = Mathf.Max(a.y, b.y), y1 = Mathf.Min(a.y, b.y);
            rt.anchoredPosition = new Vector2(x0 - 1.5f, y0 + 1.5f); rt.sizeDelta = new Vector2(x1 - x0 + 3f, y0 - y1 + 3f);
            K.Image(rt, c);
        }

        void RebuildTree()
        {
            var m = ASHFieldManual.Instance; var d = m.Data;
            _treeTitle.text = d.tabName;
            // page strip: "Pages 4 of 12" + one box per page (gaps for missing ones)
            int total = Mathf.Max(1, d.totalPages);
            _pageLabel.text = $"Pages {m.PagesFound} of {total}";
            K.Clear(_pageBoxes);
            float bw = Mathf.Min(26f, 400f / total);
            for (int i = 1; i <= total; i++)
            {
                var b = K.At("P" + i, _pageBoxes, new Vector2(0, 0.5f), new Vector2((i - 1) * bw, 0), new Vector2(bw - 4f, 18f));
                bool has = m.HasPage(i);
                if (has) { int pn = i; K.Button(b, "", 11, () => OpenBook(pn), K.Paper); } else K.Image(b, new Color(1, 1, 1, 0.06f));
                K.Text(K.Stretch("N", b), i.ToString(), 11, has ? K.PaperInk : new Color(1, 1, 1, 0.25f), TextAnchor.MiddleCenter);
                if (m.IsPageUnread(i)) K.Image(K.At("New", b, new Vector2(1, 1), new Vector2(2, 2), new Vector2(7, 7)), K.Bad); // unread
            }
            int unread = m.UnreadPages;
            _readBtn.interactable = m.PagesFound > 0;
            _readLbl.text = unread > 0 ? $"READ PAGES <color=#f08a6b>({unread} new)</color>" : "READ PAGES";
            if (_book && _book.gameObject.activeSelf) { if (_bookCol != null) ShowGrandpaPage(); else ShowBookPage(); }
            if (_gpBtn) { var gc = ASH.Prologue.ASHJournalCollection.Grandpa; int gn = GpFound().Count; _gpBtn.gameObject.SetActive(gn > 0); if (gn > 0) _gpLbl.text = $"GRANDPA'S JOURNAL ({gn}/{(gc ? gc.pages.Count : 8)})"; }

            K.Clear(_treeContent);
            // unlocked recipes, plus "?" nodes only for the next step (a parent is unlocked); deeper ones stay hidden
            var entries = d.entries.Where(e => e != null && e.recipe && (m.IsUnlocked(e.recipe) || (e.parents != null && e.parents.Any(p => p && m.IsUnlocked(p))))).ToList();
            if (_selRecipe == null || entries.Find(e => e.recipe == _selRecipe) == null) _selRecipe = entries.Count > 0 ? entries[0].recipe : null;
            float w = 0, h = 0;
            foreach (var e in entries) { var p = NodePos(e); w = Mathf.Max(w, p.x + NodeW + TreePad); h = Mathf.Max(h, -p.y + NodeH + TreePad); }
            _treeContent.sizeDelta = new Vector2(w, h);
            // branch lines first (under the nodes): L-shaped, accent when the child is unlocked, faded when locked
            foreach (var e in entries)
            {
                if (e.parents == null) continue;
                var cp = NodePos(e);
                foreach (var pr in e.parents)
                {
                    var pe = d.Find(pr); if (pe == null || !entries.Contains(pe)) continue;
                    var pp = NodePos(pe);
                    var col = m.IsUnlocked(e.recipe) ? new Color(K.Accent.r, K.Accent.g, K.Accent.b, 0.85f) : new Color(0.85f, 0.8f, 0.7f, 0.18f);
                    if (pe.column < e.column)
                    {
                        var a = new Vector2(pp.x + NodeW, pp.y - NodeH * 0.5f); var b = new Vector2(cp.x, cp.y - NodeH * 0.5f);
                        float mx = (a.x + b.x) * 0.5f;
                        Line(a, new Vector2(mx, a.y), col); Line(new Vector2(mx, a.y), new Vector2(mx, b.y), col); Line(new Vector2(mx, b.y), b, col);
                    }
                    else
                    {
                        var a = new Vector2(pp.x + 14f, pp.y - NodeH); var b = new Vector2(cp.x + 14f, cp.y);
                        Line(a, new Vector2(a.x, b.y + (a.y > b.y ? 0 : 0)), col);
                        if (Mathf.Abs(a.x - b.x) > 1f) Line(new Vector2(a.x, b.y), b, col);
                    }
                }
            }
            foreach (var e in entries) Node(m, e);
            RefreshRecipeDetail();
        }

        void Node(ASHFieldManual m, ASHFieldManualEntry e)
        {
            var r = e.recipe; bool un = m.IsUnlocked(r); bool sel = r == _selRecipe;
            var rt = K.Rect("Node_" + r.name, _treeContent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = NodePos(e); rt.sizeDelta = new Vector2(NodeW, NodeH);
            var img = K.Image(rt, sel ? K.RowSel : un ? K.Row : new Color(0.93f, 0.88f, 0.76f, 0.05f), true);
            var ol = rt.gameObject.AddComponent<Outline>(); ol.effectDistance = new Vector2(1, -1);
            ol.effectColor = sel ? K.Accent : un ? new Color(1, 1, 1, 0.12f) : new Color(0.85f, 0.8f, 0.7f, 0.22f);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img; var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            var rec = r; b.onClick.AddListener(() => { SelectRecipe(rec); m.MarkSeen(rec); });
            var ib = K.At("Icon", rt, new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(NodeH - 8, NodeH - 8)); K.Image(ib, new Color(0, 0, 0, un ? 0.3f : 0.12f));
            var ii = K.Image(K.Stretch("I", ib, 3, 3, 3, 3), Color.white); ii.preserveAspect = true;
            var ab = K.Text(K.Stretch("A", ib), "", 15, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetIcon(ii, ab, r);
            if (!un) { ii.enabled = false; ab.text = "?"; ab.fontSize = 24; ab.color = new Color(0.86f, 0.68f, 0.34f, 0.85f); } // locked: a question mark until revealed
            string why = un && _craft ? _craft.MissingReason(r) : "locked";
            var t = K.Text(K.Stretch("Name", rt, NodeH, 2, 4, 2), un ? r.displayName : "???", 14, un ? K.Ink : new Color(0.85f, 0.8f, 0.7f, 0.55f), TextAnchor.MiddleLeft, un ? FontStyle.Normal : FontStyle.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (un) K.Image(K.At("Ready", rt, new Vector2(1, 0), new Vector2(-6, 6), new Vector2(7, 7)), why == null ? new Color(0.62f, 0.83f, 0.6f) : new Color(0.85f, 0.55f, 0.47f, 0.7f));
            if (m.IsNew(r))
            {
                var nb = K.At("New", rt, new Vector2(1, 1), new Vector2(-2, 8), new Vector2(40, 16)); K.Image(nb, K.Accent);
                K.Text(K.Stretch("T", nb), "NEW", 11, Color.black, TextAnchor.MiddleCenter, FontStyle.Bold);
            }
        }

        GameObject BuildItems(RectTransform body)
        {
            var root = K.Stretch("Items", body);
            _itemList = K.ScrollList(K.Stretch("List", root, 0, 0, 640, 0));
            var det = K.Stretch("Detail", root, 540, 0, 0, 0);
            K.Image(det, new Color(1, 1, 1, 0.04f));
            var iconBox = K.At("IconBox", det, new Vector2(0, 1), new Vector2(20, -20), new Vector2(110, 110)); K.Image(iconBox, new Color(0, 0, 0, 0.35f));
            _iIcon = K.Image(K.Stretch("Icon", iconBox, 8, 8, 8, 8), Color.white); _iIcon.preserveAspect = true;
            _iIconAbbr = K.Text(K.Stretch("Abbr", iconBox), "", 40, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _iName = K.Text(K.At("Name", det, new Vector2(0, 1), new Vector2(150, -22), new Vector2(460, 40)), "", 28, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _iDesc = K.Text(K.At("Desc", det, new Vector2(0, 1), new Vector2(150, -66), new Vector2(460, 70)), "", 18, K.Dim, TextAnchor.UpperLeft);
            _iInfo = K.Text(K.At("Info", det, new Vector2(0, 1), new Vector2(24, -150), new Vector2(590, 300)), "", 20, K.Ink, TextAnchor.UpperLeft);
            _iUse = K.Button(K.At("Use", det, new Vector2(0, 0), new Vector2(24, 20), new Vector2(200, 48)), "USE", 22, () => { if (_selItem != null && _useItem != null) { _useItem(_selItem); _dirty = true; } });
            _iUseLabel = _iUse.GetComponentInChildren<Text>();
            _iBreak = K.Button(K.At("Break", det, new Vector2(0, 0), new Vector2(240, 20), new Vector2(220, 48)), "BREAK DOWN", 20, () => { if (_selItem != null && _craft) { _craft.TryBreakDown(_selItem); _dirty = true; } });
            _iBreakLabel = _iBreak.GetComponentInChildren<Text>();
            return root.gameObject;
        }

        GameObject BuildNotes(RectTransform body)
        {
            var root = K.Stretch("Notes", body);
            _notesList = K.ScrollList(K.Stretch("List", root), 10);
            return root.gameObject;
        }

        // ------------------------------------------------------------------ state
        public void Open(Tab tab) { gameObject.SetActive(true); Show(tab); }
        public void Close() => gameObject.SetActive(false);

        public void Show(Tab tab)
        {
            if (tab == Tab.Crafting && !ASHHud.CanCraftNow) tab = Tab.Items; // no Survival Guide yet: the tab doesn't exist
            CloseBook();
            RefreshTabs();
            _tab = tab;
            foreach (var kv in _tabs) kv.Value.SetActive(kv.Key == tab);
            foreach (var kv in _tabButtons) kv.Value.color = kv.Key == tab ? new Color(0.95f, 0.62f, 0.28f, 0.9f) : new Color(1, 1, 1, 0.12f);
            _dirty = true;
        }

        public void SelectRecipe(ASHRecipe r) { _selRecipe = r; _dirty = true; }

        /// <summary>Crafting tab hidden until crafting is available (Grandpa's journal read); labelled with the manual's name.</summary>
        void RefreshTabs()
        {
            if (!_tabButtons.TryGetValue(Tab.Crafting, out var cb)) return;
            bool on = ASHHud.CanCraftNow;
            if (cb.gameObject.activeSelf != on) cb.gameObject.SetActive(on);
            var lbl = cb.GetComponentInChildren<Text>();
            if (lbl) lbl.text = ASHFieldManualData.I ? "SURVIVAL GUIDE" : "CRAFTING";
            cb.rectTransform.sizeDelta = new Vector2(ASHFieldManualData.I ? 176f : 130f, cb.rectTransform.sizeDelta.y);
            var m = ASHFieldManual.Instance; int n = m ? m.NewCount + m.UnreadPages : 0;
            if (lbl && n > 0) lbl.text += $" <color=#000000><size=13>({n})</size></color>";
        }
        public void SelectItem(ASHItemDefinition i) { _selItem = i; _dirty = true; }

        void Update()
        {
            if ((_tab == Tab.Objectives || _tab == Tab.Pets) && Time.unscaledTime >= _objRefresh) { _objRefresh = Time.unscaledTime + 0.5f; _dirty = true; } // live progress / distances
            HookManual();
            if (_dirty) { _dirty = false; RefreshTabs(); Rebuild(); }
            if (_tab == Tab.Crafting && _craft)
            {
                float p = _craft.IsCrafting ? _craft.CraftProgress01 : 0f;
                _dProgFill.anchorMax = new Vector2(p, 1); _dProgFill.offsetMin = _dProgFill.offsetMax = Vector2.zero;
                _dProg.gameObject.SetActive(_craft.IsCrafting);
                if (_craft.IsCrafting && _selRecipe) _dCraft.interactable = false;
                else if (_selRecipe) _dCraft.interactable = _craft.CanCraft(_selRecipe);
            }
        }

        void Rebuild()
        {
            if (_tab == Tab.Crafting) RebuildCrafting();
            else if (_tab == Tab.Items) RebuildItems();
            else if (_tab == Tab.Objectives) RebuildObjectives();
            else if (_tab == Tab.Skills) RebuildSkills();
            else if (_tab == Tab.Pets) RebuildPets();
            else if (_tab == Tab.Character) ASHCharacterTab.Rebuild();
            else RebuildNotes();
        }

        static void SetIcon(Image img, Text abbr, ASHItemDefinition it)
        {
            img.enabled = it != null && it.icon != null; if (img.enabled) img.sprite = it.icon;
            abbr.text = it != null && it.icon == null ? it.displayName.Substring(0, Mathf.Min(2, it.displayName.Length)).ToUpperInvariant() : "";
            if (it != null) abbr.color = Color.Lerp(it.uiTint, Color.white, 0.3f);
        }

        static void SetIcon(Image img, Text abbr, ASHRecipe r)
        {
            if (r && r.icon) { img.enabled = true; img.sprite = r.icon; img.color = Color.white; abbr.text = ""; return; }
            SetIcon(img, abbr, r ? r.outputItem : null);
        }

        RectTransform Row(RectTransform list, ASHItemDefinition icon, string title, string right, bool selected, UnityEngine.Events.UnityAction onClick, float h = 54f)
        {
            var row = K.Rect("Row", list); K.Layout(row, h);
            var img = K.Image(row, selected ? K.RowSel : K.Row, true);
            var b = row.gameObject.AddComponent<Button>(); b.targetGraphic = img; var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            b.onClick.AddListener(onClick);
            var ib = K.At("Icon", row, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(h - 10, h - 10)); K.Image(ib, new Color(0, 0, 0, 0.3f));
            var ii = K.Image(K.Stretch("I", ib, 3, 3, 3, 3), Color.white); ii.preserveAspect = true;
            var ab = K.Text(K.Stretch("A", ib), "", 18, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetIcon(ii, ab, icon);
            K.Text(K.Stretch("Title", row, h + 6, 0, 150, 0), title, 19, K.Ink);
            K.Text(K.Stretch("Right", row, 0, 0, 10, 0), right, 16, K.Dim, TextAnchor.MiddleRight);
            return row;
        }

        void RebuildCrafting()
        {
            bool tree = ASHFieldManualData.I;
            if (tree && !ASHFieldManual.Instance) { _treeRoot.gameObject.SetActive(false); _legacyRoot.gameObject.SetActive(false); return; }
            _treeRoot.gameObject.SetActive(tree); _legacyRoot.gameObject.SetActive(!tree);
            if (tree) { RebuildTree(); return; }
            foreach (var kv in _catButtons) kv.Value.color = kv.Key == _cat ? K.RowSel : new Color(1, 1, 1, 0.08f);
            K.Clear(_recipeList);
            if (_craft == null) return;
            var list = _craft.Recipes.Where(r => r != null && (_cat == "All" || r.category.ToString() == _cat)).ToList();
            if (_selRecipe == null || !list.Contains(_selRecipe)) _selRecipe = list.FirstOrDefault();
            foreach (var r in list)
            {
                var rec = r; string why = _craft.MissingReason(r);
                string status = why == null ? "<color=#9fd39b>Ready</color>" : $"<color=#d98b77>{Short(why)}</color>";
                Row(_recipeList, r.outputItem, r.displayName + (!r.IsUpgrade && r.outputAmount > 1 ? $" x{r.outputAmount}" : ""), status, r == _selRecipe, () => SelectRecipe(rec));
            }
            RefreshRecipeDetail();
        }

        static string Short(string why) => why.Replace(" nearby", "").Replace("Missing materials", "Missing");

        void RefreshRecipeDetail()
        {
            var r = _selRecipe;
            if (r == null) { _dName.text = "No recipes"; _dDesc.text = _dReq.text = _dStatus.text = ""; _dCraft.interactable = false; SetIcon(_dIcon, _dIconAbbr, (ASHItemDefinition)null); return; }
            SetIcon(_dIcon, _dIconAbbr, r);
            _dName.text = r.displayName + (!r.IsUpgrade && r.outputAmount > 1 ? $"  <size=20>x{r.outputAmount}</size>" : "");
            var man = ASHFieldManual.Instance;
            if (man && man.Data && !man.IsUnlocked(r))
            {
                // locked: a question mark (name hidden) with the hint and what unlocks it
                _dIcon.enabled = false; _dIconAbbr.text = "?"; _dIconAbbr.color = new Color(0.86f, 0.68f, 0.34f, 0.9f);
                _dName.text = "<color=#8c8579>???</color>";
                _dDesc.text = "<i>Not in the guide yet.</i>";
                var e = man.Data.Find(r);
                var hs = new System.Text.StringBuilder("<color=#c9a46a>NOT IN THE MANUAL YET</color>\n");
                if (e != null)
                {
                    if (!string.IsNullOrEmpty(e.hint)) hs.AppendLine($"<i>{e.hint}</i>\n");
                    hs.AppendLine(ASHFieldManual.RequireAll(e) || ASHFieldManual.Conditions(e).Count < 2 ? "Unlocks when you:" : "Unlocks when you do any of:");
                    foreach (var c in ASHFieldManual.Conditions(e)) { bool ok = man.ConditionMet(c); hs.AppendLine($"<color=#{(ok ? "9fd39b" : "a59e93")}>{(ok ? "√" : "•")}</color>  {man.ConditionText(c)}"); }
                }
                _dReq.text = hs.ToString();
                _dStatus.text = "<color=#a59e93>Locked</color>";
                _dCraft.interactable = false;
                return;
            }
            _dIcon.color = Color.white;
            _dDesc.text = string.IsNullOrEmpty(r.description) && r.outputItem ? r.outputItem.description : r.description;
            var sb = new System.Text.StringBuilder("<color=#c9a46a>REQUIRES</color>\n");
            foreach (var ing in r.ingredients)
            {
                if (ing.item == null) continue;
                int have = _inv ? _inv.Count(ing.item) : 0; bool ok = have >= ing.amount;
                sb.AppendLine($"<color=#{(ok ? "9fd39b" : "e08a78")}>{(ok ? "√" : "×")}</color>  {ing.item.displayName}   <color=#{(ok ? "cfc6b6" : "e08a78")}>{have}/{ing.amount}</color>");
            }
            if (r.requiredTool != ASHToolType.None)
            {
                bool ok = _inv && _inv.HasTool(r.requiredTool);
                sb.AppendLine($"<color=#{(ok ? "9fd39b" : "e08a78")}>{(ok ? "√" : "×")}</color>  Tool: {ASHCrafting.ToolName(r.requiredTool)}");
            }
            if (r.station == ASHCraftStation.Campfire)
            {
                bool ok = _craft.NearStation(ASHCraftStation.Campfire);
                sb.AppendLine($"<color=#{(ok ? "9fd39b" : "e08a78")}>{(ok ? "√" : "×")}</color>  At a lit campfire (within 4 m)");
            }
            if (r.waterPercent > 0f)
            {
                bool ok = ASHCrafting.HasWaterFor(r);
                sb.AppendLine($"<color=#{(ok ? "9fd39b" : "e08a78")}>{(ok ? "√" : "×")}</color>  Water from your bottle   <color=#{(ok ? "cfc6b6" : "e08a78")}>{r.waterPercent:0}%</color>");
            }
            if (r.requiredCraftingLevel > 1)
            {
                int have = ASH.Gameplay.ASHSkills.Level(ASH.Gameplay.ASHSkills.Crafting);
                bool ok = have >= r.requiredCraftingLevel;
                sb.AppendLine($"<color=#{(ok ? "9fd39b" : "e08a78")}>{(ok ? "√" : "×")}</color>  Crafting level {r.requiredCraftingLevel}   <color=#{(ok ? "cfc6b6" : "e08a78")}>you: {have}</color>");
            }
            if (r.IsUpgrade)
            {
                int have = ASHUpgrades.Tier(r.upgradeId);
                int cap = r.upgradeId == ASHUpgrades.AmmoPouch ? ASHUpgrades.AmmoPouchMaxTier : Mathf.Max(r.upgradeTier, 1);
                sb.AppendLine($"<color=#c9a46a>Upgrade  ·  {r.upgradeId.Replace("_", " ")}  {have}/{cap}</color>");
                if (r.upgradeId == ASHUpgrades.AmmoPouch)
                    sb.AppendLine($"<color=#a59e93>Ammo stacks +{Mathf.RoundToInt(ASHUpgrades.AmmoPouchBonusPerTier * 100f)}% each tier (up to {ASHUpgrades.AmmoPouchMaxTier}).</color>");
            }
            sb.AppendLine($"\n<color=#a59e93>Time: {r.craftSeconds:0.#} s   ·   Category: {r.category}</color>");
            _dReq.text = sb.ToString();
            string why = _craft.MissingReason(r);
            _dStatus.text = _craft.IsCrafting ? $"Working... {(_craft.CurrentRecipe ? _craft.CurrentRecipe.displayName : _craft.CurrentBreakdown ? "breaking down " + _craft.CurrentBreakdown.displayName : "")}" : why == null ? "<color=#9fd39b>Ready to craft</color>" : $"<color=#e08a78>{why}</color>";
            _dCraft.interactable = why == null;
        }

        void RebuildItems()
        {
            K.Clear(_itemList);
            if (_inv == null) return;
            var items = new List<ASHItemDefinition>();
            foreach (var s in _inv.Slots) if (!s.IsEmpty && !items.Contains(s.item)) items.Add(s.item);
            if (_selItem == null || !items.Contains(_selItem)) _selItem = items.FirstOrDefault();
            foreach (var it in items)
            {
                var item = it;
                string right = (it.CanBreakDown ? "<color=#c9a46a>salvage</color>  " : "") + "x" + _inv.Count(it);
                Row(_itemList, it, it.displayName, right, it == _selItem, () => SelectItem(item));
            }
            if (items.Count == 0) K.Layout(K.Text(K.Rect("Empty", _itemList), "Your pack is empty.", 19, K.Dim), 40);
            RefreshItemDetail();
        }

        void RefreshItemDetail()
        {
            var it = _selItem;
            _iUse.gameObject.SetActive(it != null); _iBreak.gameObject.SetActive(it != null && it.CanBreakDown);
            if (it == null) { _iName.text = ""; _iDesc.text = ""; _iInfo.text = ""; SetIcon(_iIcon, _iIconAbbr, (ASHItemDefinition)null); return; }
            SetIcon(_iIcon, _iIconAbbr, it);
            _iName.text = it.displayName;
            _iDesc.text = it.description;
            var sb = new System.Text.StringBuilder($"<color=#a59e93>{it.category}   ·   carrying {_inv.Count(it)}   ·   stack {ASHInventory.StackLimit(it)}</color>\n");
            if (it.IsConsumable)
            {
                sb.Append("\n");
                if (it.restoreHunger > 0) sb.Append($"+{it.restoreHunger:0} hunger   ");
                if (it.restoreThirst > 0) sb.Append($"+{it.restoreThirst:0} thirst   ");
                if (it.restoreHealth > 0) sb.Append($"+{it.restoreHealth:0} health   ");
                sb.AppendLine();
                if (it.requiresOpener)
                    sb.AppendLine(_inv.HasOpener ? "<color=#9fd39b>You can open this cleanly.</color>" : $"<color=#e08a78>No opener: smashing it open spills about {(1f - it.smashedYield) * 100f:0}%.</color>");
            }
            if (it.toolType != ASHToolType.None) sb.AppendLine($"\nTool: {it.toolType}" + (it.toolType == ASHToolType.Axe ? "  (more, faster wood; breaks crates and furniture)" : it.toolType == ASHToolType.Knife ? "  (cuts cloth, cleans game, opens cans)" : it.toolType == ASHToolType.GPS ? "  (open the map with M; log survey points)" : ""));
            if (it.CanBreakDown)
            {
                float f = _craft ? _craft.BreakdownYieldFactor(it) : 1f;
                var ys = string.Join(", ", it.breakdownYield.Where(y => y.item).Select(y => $"{Mathf.Max(1, Mathf.FloorToInt(y.amount * f))} {y.item.displayName}"));
                sb.AppendLine($"\n<color=#c9a46a>BREAK DOWN</color>  →  {ys}");
                if (it.breakdownTool != ASHToolType.None)
                {
                    bool has = _inv.HasTool(it.breakdownTool);
                    sb.AppendLine(it.breakdownToolRequired
                        ? $"<color=#{(has ? "9fd39b" : "e08a78")}>{(has ? "√" : "×")}  Needs {ASHCrafting.ToolName(it.breakdownTool)}</color>"
                        : $"<color=#{(has ? "9fd39b" : "c9a46a")}>{(has ? "√  Using " + ASHCrafting.ToolName(it.breakdownTool) : "Faster and better with " + ASHCrafting.ToolName(it.breakdownTool))}</color>");
                }
                string why = _craft ? _craft.BreakdownBlockReason(it) : "No crafting";
                _iBreak.interactable = why == null;
            }
            _iInfo.text = sb.ToString();
            string hl = ASHHud.HandlerLabel(it);
            bool usable = hl != null || it.IsConsumable || it.IsPlaceable || it.toolType == ASHToolType.GPS;
            _iUse.interactable = usable;
            _iUseLabel.text = hl != null ? hl.ToUpperInvariant() : it.IsPlaceable ? "PLACE" : it.toolType == ASHToolType.GPS ? "OPEN MAP" : it.IsConsumable ? it.useVerb.ToUpperInvariant() : "USE";
        }

        void RebuildNotes()
        {
            K.Clear(_notesList);
            var log = ASHJournalLog.Instance;
            if (log == null || log.Entries.Count == 0)
            {
                K.Layout(K.Text(K.Rect("Empty", _notesList), "No notes yet. Find a high point with an old survey panel and log it with the GPS.", 20, K.Dim), 60);
                return;
            }
            for (int i = log.Entries.Count - 1; i >= 0; i--)
            {
                var e = log.Entries[i];
                K.Text(K.Rect("Note", _notesList), $"<color=#e8a35a><b>{e.title}</b></color>   <color=#857d72>{e.stamp}</color>\n{e.body}\n", 19, K.Ink, TextAnchor.UpperLeft);
            }
        }
    }
}
