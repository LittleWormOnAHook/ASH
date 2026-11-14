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
        public enum Tab { Crafting, Items, Notes }

        ASHInventory _inv; ASHCrafting _craft; Func<ASHItemDefinition, bool> _useItem;
        readonly Dictionary<Tab, GameObject> _tabs = new Dictionary<Tab, GameObject>();
        readonly Dictionary<Tab, Image> _tabButtons = new Dictionary<Tab, Image>();
        readonly Dictionary<string, Image> _catButtons = new Dictionary<string, Image>();
        RectTransform _recipeList, _itemList, _notesList;
        Image _dIcon, _iIcon; Text _dIconAbbr, _iIconAbbr, _dName, _dDesc, _dReq, _dStatus, _iName, _iDesc, _iInfo, _iUseLabel, _iBreakLabel;
        Button _dCraft, _iUse, _iBreak; RectTransform _dProg, _dProgFill;
        string _cat = "All"; ASHRecipe _selRecipe; ASHItemDefinition _selItem; Tab _tab;
        bool _dirty;

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
            _dirty = true;
        }
        void OnDisable()
        {
            if (_inv) _inv.Changed -= MarkDirty;
            if (_craft) { _craft.Crafted -= OnCraftEvent; _craft.BrokeDown -= OnBreakEvent; }
            var log = ASHJournalLog.Instance; if (log) log.Added -= OnNote;
        }
        void MarkDirty() => _dirty = true;
        void OnCraftEvent(ASHRecipe r) => _dirty = true;
        void OnBreakEvent(ASHItemDefinition i, string s) => _dirty = true;
        void OnNote(ASHJournalLog.Entry e) => _dirty = true;

        // ------------------------------------------------------------------ build
        void Build(RectTransform rt)
        {
            var accent = K.At("Accent", rt, new Vector2(0.5f, 1), Vector2.zero, new Vector2(1200, 3)); K.Image(accent, K.Accent);
            K.Text(K.At("Title", rt, new Vector2(0, 1), new Vector2(28, -18), new Vector2(400, 40)), "FIELD JOURNAL", 30, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            float x = 330;
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                var b = K.At("Tab" + t, rt, new Vector2(0, 1), new Vector2(x, -20), new Vector2(150, 38)); x += 160;
                K.Button(b, t.ToString().ToUpperInvariant(), 18, () => Show(tab), new Color(1, 1, 1, 0.12f));
                _tabButtons[t] = b.GetComponent<Image>();
            }
            K.Text(K.At("Hint", rt, new Vector2(1, 1), new Vector2(-24, -26), new Vector2(360, 26)), "<color=#d9cbb4>J</color> close   <color=#d9cbb4>M</color> GPS map   <color=#d9cbb4>Tab</color> pack", 16, K.Dim, TextAnchor.MiddleRight);

            var body = K.Stretch("Body", rt, 20, 20, 20, 76);
            _tabs[Tab.Crafting] = BuildCrafting(body);
            _tabs[Tab.Items] = BuildItems(body);
            _tabs[Tab.Notes] = BuildNotes(body);
        }

        GameObject BuildCrafting(RectTransform body)
        {
            var root = K.Stretch("Crafting", body);
            var cats = K.Stretch("Categories", root, 0, 0, 1000, 0);
            string[] names = { "All", "Fire", "Medical", "Food", "Tools", "Materials" };
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                var b = K.At("Cat" + n, cats, new Vector2(0, 1), new Vector2(0, -i * 50), new Vector2(150, 42));
                K.Button(b, n, 18, () => { _cat = n; _dirty = true; }, new Color(1, 1, 1, 0.08f));
                b.GetComponentInChildren<Text>().color = K.Ink;
                _catButtons[n] = b.GetComponent<Image>();
            }
            _recipeList = K.ScrollList(K.Stretch("Recipes", root, 170, 0, 560, 0));

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
            return root.gameObject;
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
            _tab = tab;
            foreach (var kv in _tabs) kv.Value.SetActive(kv.Key == tab);
            foreach (var kv in _tabButtons) kv.Value.color = kv.Key == tab ? new Color(0.95f, 0.62f, 0.28f, 0.9f) : new Color(1, 1, 1, 0.12f);
            _dirty = true;
        }

        public void SelectRecipe(ASHRecipe r) { _selRecipe = r; _dirty = true; }
        public void SelectItem(ASHItemDefinition i) { _selItem = i; _dirty = true; }

        void Update()
        {
            if (_dirty) { _dirty = false; Rebuild(); }
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
            else RebuildNotes();
        }

        static void SetIcon(Image img, Text abbr, ASHItemDefinition it)
        {
            img.enabled = it != null && it.icon != null; if (img.enabled) img.sprite = it.icon;
            abbr.text = it != null && it.icon == null ? it.displayName.Substring(0, Mathf.Min(2, it.displayName.Length)).ToUpperInvariant() : "";
            if (it != null) abbr.color = Color.Lerp(it.uiTint, Color.white, 0.3f);
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
            foreach (var kv in _catButtons) kv.Value.color = kv.Key == _cat ? K.RowSel : new Color(1, 1, 1, 0.08f);
            K.Clear(_recipeList);
            if (_craft == null) return;
            var list = _craft.Recipes.Where(r => r != null && (_cat == "All" || r.category.ToString() == _cat)).ToList();
            if (_selRecipe == null || !list.Contains(_selRecipe)) _selRecipe = list.FirstOrDefault();
            foreach (var r in list)
            {
                var rec = r; string why = _craft.MissingReason(r);
                string status = why == null ? "<color=#9fd39b>Ready</color>" : $"<color=#d98b77>{Short(why)}</color>";
                Row(_recipeList, r.outputItem, r.displayName + (r.outputAmount > 1 ? $" x{r.outputAmount}" : ""), status, r == _selRecipe, () => SelectRecipe(rec));
            }
            RefreshRecipeDetail();
        }

        static string Short(string why) => why.Replace(" nearby", "").Replace("Missing materials", "Missing");

        void RefreshRecipeDetail()
        {
            var r = _selRecipe;
            if (r == null) { _dName.text = "No recipes"; _dDesc.text = _dReq.text = _dStatus.text = ""; _dCraft.interactable = false; SetIcon(_dIcon, _dIconAbbr, null); return; }
            SetIcon(_dIcon, _dIconAbbr, r.outputItem);
            _dName.text = r.displayName + (r.outputAmount > 1 ? $"  <size=20>x{r.outputAmount}</size>" : "");
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
            if (it == null) { _iName.text = ""; _iDesc.text = ""; _iInfo.text = ""; SetIcon(_iIcon, _iIconAbbr, null); return; }
            SetIcon(_iIcon, _iIconAbbr, it);
            _iName.text = it.displayName;
            _iDesc.text = it.description;
            var sb = new System.Text.StringBuilder($"<color=#a59e93>{it.category}   ·   carrying {_inv.Count(it)}   ·   stack {it.maxStack}</color>\n");
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
            bool usable = it.IsConsumable || it.IsPlaceable || it.toolType == ASHToolType.GPS;
            _iUse.interactable = usable;
            _iUseLabel.text = it.IsPlaceable ? "PLACE" : it.toolType == ASHToolType.GPS ? "OPEN MAP" : it.IsConsumable ? it.useVerb.ToUpperInvariant() : "USE";
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
