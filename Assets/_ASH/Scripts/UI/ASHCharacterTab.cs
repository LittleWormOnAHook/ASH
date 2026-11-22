using System.Collections.Generic;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>Rook journal tab: pack grid on the left, silhouette + wear slots on the right. Drag a pack item onto a slot,
    /// or click it then a slot; click a filled slot to take it off. Wrong items are rejected with a hint.</summary>
    public static class ASHCharacterTab
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHCharacterTab()
        {
            _grid = default;
            _stage = default;
            _sil = default;
            _silImg = default;
            _silBoxes = default;
            _hint = default;
            _slotRects.Clear();
            _icons.Clear();
            _labels.Clear();
            _picked = default;
            _pickedPack = -1;
            _ghost = default;
            _pips.Clear();
            _totals = default;
        }

        static RectTransform _grid, _stage, _sil;
        static Image _silImg;
        static RectTransform[] _silBoxes;
        static Text _hint;
        static readonly Dictionary<ASHCharacterEquipment.Slot, RectTransform> _slotRects = new Dictionary<ASHCharacterEquipment.Slot, RectTransform>();
        static readonly Dictionary<ASHCharacterEquipment.Slot, Image> _icons = new Dictionary<ASHCharacterEquipment.Slot, Image>();
        static readonly Dictionary<ASHCharacterEquipment.Slot, Text> _labels = new Dictionary<ASHCharacterEquipment.Slot, Text>();
        static ASHItemDefinition _picked;
        static int _pickedPack = -1;
        static Image _ghost;
        static readonly Dictionary<ASHCharacterEquipment.Slot, Image> _pips = new Dictionary<ASHCharacterEquipment.Slot, Image>();
        static Text _totals;

        // left column = body parts, right column = carried/extra layers; rows top to bottom
        static readonly ASHCharacterEquipment.Slot[] LeftCol = { ASHCharacterEquipment.Slot.Head, ASHCharacterEquipment.Slot.Face, ASHCharacterEquipment.Slot.Torso, ASHCharacterEquipment.Slot.Hands, ASHCharacterEquipment.Slot.Legs };
        static readonly ASHCharacterEquipment.Slot[] RightCol = { ASHCharacterEquipment.Slot.Backpack, ASHCharacterEquipment.Slot.Outerwear, ASHCharacterEquipment.Slot.Accessory0, ASHCharacterEquipment.Slot.Accessory1, ASHCharacterEquipment.Slot.Feet };

        const string DefaultHint = "Drag a wearable from your pack onto a slot, or click one and then a slot. Click a filled slot to take it off.";

        public static GameObject Build(RectTransform body)
        {
            ASHCharacterEquipment.Ensure();
            _slotRects.Clear(); _icons.Clear(); _labels.Clear(); _pips.Clear();
            var root = K.Stretch("Character", body);

            // left: pack grid
            var left = K.Stretch("Pack", root, 0, 0, 720, 0);
            K.Text(K.At("PackTitle", left, new Vector2(0, 1), new Vector2(6, 0), new Vector2(300, 26)), "PACK", 16, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _grid = K.ScrollList(K.Stretch("List", left, 0, 0, 0, 30), 6f);

            // right: paper-doll stage
            _stage = K.Stretch("Stage", root, 460, 0, 0, 0);
            K.Image(K.Stretch("Paper", _stage), new Color(0.1f, 0.09f, 0.08f, 0.55f));
            _sil = K.At("Silhouette", _stage, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 470));
            _silImg = K.Image(_sil, Color.white);
            _silImg.preserveAspect = true;
            BuildFallbackSilhouette(_sil);

            foreach (var s in LeftCol) Place(s);
            foreach (var s in RightCol) Place(s);

            _totals = K.Text(K.At("Totals", _stage, new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(660, 40)), "", 14, K.Ink, TextAnchor.UpperCenter);
            _totals.supportRichText = true;
            _hint = K.Text(K.At("Hint", _stage, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(640, 36)), DefaultHint, 14, K.Dim, TextAnchor.MiddleCenter);
            ApplyLayout();
            EnsureGhost();
            return root.gameObject;
        }

        /// <summary>Crude block body, only shown if the silhouette sprite is missing.</summary>
        static void BuildFallbackSilhouette(RectTransform sil)
        {
            var tone = new Color(1f, 1f, 1f, 1f);
            Vector4[] boxes = { new Vector4(0.5f, 0.92f, 0.36f, 0.14f), new Vector4(0.5f, 0.66f, 0.62f, 0.34f), new Vector4(0.12f, 0.62f, 0.16f, 0.32f), new Vector4(0.88f, 0.62f, 0.16f, 0.32f), new Vector4(0.36f, 0.25f, 0.2f, 0.46f), new Vector4(0.64f, 0.25f, 0.2f, 0.46f) };
            _silBoxes = new RectTransform[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                var r = K.Rect("Block" + i, sil);
                r.anchorMin = new Vector2(b.x - b.z * 0.5f, b.y - b.w * 0.5f); r.anchorMax = new Vector2(b.x + b.z * 0.5f, b.y + b.w * 0.5f);
                r.offsetMin = r.offsetMax = Vector2.zero;
                K.Image(r, tone);
                _silBoxes[i] = r;
            }
        }

        static void Place(ASHCharacterEquipment.Slot slot)
        {
            var r = K.At("Slot" + slot, _stage, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72, 72));
            var bg = K.Image(r, new Color(0.18f, 0.16f, 0.13f, 0.95f), true);
            var icon = K.Image(K.Stretch("Icon", r, 10, 18, 10, 4), Color.white, false);
            icon.preserveAspect = true;
            icon.enabled = false;
            var pip = K.Image(K.At("Rarity", r, new Vector2(1, 1), new Vector2(-4, -4), new Vector2(9, 9)), Color.clear, false);
            pip.rectTransform.pivot = new Vector2(1, 1); pip.raycastTarget = false; _pips[slot] = pip;
            var lab = K.Text(K.Stretch("Cap", r, 2, 1, 2, 0), Short(slot), 11, K.Dim, TextAnchor.LowerCenter);
            lab.horizontalOverflow = HorizontalWrapMode.Overflow;
            _slotRects[slot] = r;
            _icons[slot] = icon;
            _labels[slot] = lab;
            var drop = r.gameObject.AddComponent<DropZone>();
            drop.slot = slot;
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener(() => ClickSlot(slot));
            ASHItemTooltip.Attach(r, () =>
            {
                var eq = ASHCharacterEquipment.Instance; if (!eq) return default;
                var w = eq[slot]; if (w == null || !w.item) return default;
                var info = ASHItemTooltip.Info.Item(w.item, 1, w.condition); info.note = "Worn"; return info;
            });
        }

        /// <summary>Sizes/positions from ASH_CharacterTabSettings (re-applied on every refresh so Play edits show).</summary>
        static void ApplyLayout()
        {
            var S = ASHCharacterTabSettings.I;
            if (!_stage) return;
            bool hasArt = S.silhouette;
            if (_silImg)
            {
                _silImg.sprite = S.silhouette;
                _silImg.enabled = hasArt;
                _silImg.color = S.silhouetteTint;
                _silImg.raycastTarget = false;
                float aspect = hasArt ? S.silhouette.rect.width / Mathf.Max(1f, S.silhouette.rect.height) : 0.34f;
                _sil.sizeDelta = new Vector2(S.silhouetteHeight * aspect, S.silhouetteHeight);
                _sil.anchoredPosition = new Vector2(0, S.silhouetteOffsetY + 14f);
            }
            if (_silBoxes != null)
                foreach (var b in _silBoxes) if (b) { b.gameObject.SetActive(!hasArt); var im = b.GetComponent<Image>(); im.color = S.silhouetteTint; im.raycastTarget = false; }

            for (int row = 0; row < LeftCol.Length; row++)
            {
                float y = S.slotTopY - row * S.slotRowStep + 14f;
                SetSlot(LeftCol[row], new Vector2(-S.slotColumnX, y), S.slotSize);
                SetSlot(RightCol[row], new Vector2(S.slotColumnX, y), S.slotSize);
            }
        }

        static void SetSlot(ASHCharacterEquipment.Slot s, Vector2 pos, float size)
        {
            if (!_slotRects.TryGetValue(s, out var r) || !r) return;
            r.anchoredPosition = pos; r.sizeDelta = new Vector2(size, size);
        }

        static bool Wearable(ASHItemDefinition it) => it && (it.wearSlot != ASHWearSlot.None || it.itemId == "wet_rag" || it.itemId == "gas_mask");

        public static void Rebuild()
        {
            var eq = ASHCharacterEquipment.Ensure();
            var inv = Object.FindAnyObjectByType<ASHInventory>();
            ApplyLayout();
            if (_grid)
            {
                K.Clear(_grid);
                if (inv)
                {
                    int hot = Mathf.Clamp(inv.HotbarCount, 0, inv.Slots.Count);
                    int pack = Mathf.Clamp(inv.SlotCount, hot, inv.Slots.Count);
                    if (hot > 0) { Header("Hotbar"); Grid("HotbarGrid", inv, 0, hot); }
                    if (pack > hot) { Header("Backpack"); Grid("PackGrid", inv, hot, pack); }
                }
                else K.Layout(K.Text(K.Rect("Empty", _grid), "No pack.", 16, K.Dim), 40);
            }

            foreach (ASHCharacterEquipment.Slot s in System.Enum.GetValues(typeof(ASHCharacterEquipment.Slot)))
            {
                if (!_icons.TryGetValue(s, out var icon)) continue;
                var w = eq ? eq.View(s) : null;
                bool filled = w != null && w.item;
                if (filled && w.item.icon) { icon.sprite = w.item.icon; icon.color = Color.white; icon.enabled = true; }
                else { icon.sprite = null; icon.enabled = false; }
                if (_labels.TryGetValue(s, out var lab))
                {
                    bool low = filled && w.condition < ASHClothingSettings.I.lowConditionThreshold;
                    lab.supportRichText = true;
                    lab.text = filled ? (w.condition < 0.995f ? $"{w.item.displayName} <color={(low ? "#d98a6a" : "#a59e93")}>{w.condition * 100f:0}%</color>" : w.item.displayName) : Short(s);
                }
                if (_pips.TryGetValue(s, out var pip)) { pip.enabled = filled && w.item.IsWearable; if (pip.enabled) pip.color = ASHClothingSettings.I.RarityColor(w.item.rarity); }
            }
            if (_totals) { var w = ASHWallet.Instance; _totals.text = (eq ? ASHClothing.Summary() : "") + (w ? "\nPurse " + w.Formatted : ""); }
            if (_picked == null && string.IsNullOrEmpty(eq ? eq.Message : null)) Hint(DefaultHint);
        }

        static void Header(string title)
        {
            var t = K.Text(K.Rect("H_" + title, _grid), title.ToUpperInvariant(), 13, K.Dim, TextAnchor.LowerLeft, FontStyle.Bold);
            K.Layout(t, 22);
        }

        static void Grid(string name, ASHInventory inv, int from, int to)
        {
            var S = ASHCharacterTabSettings.I;
            var g = K.Rect(name, _grid);
            var gl = g.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(S.cellSize, S.cellSize);
            gl.spacing = new Vector2(S.cellSpacing, S.cellSpacing);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = Mathf.Max(1, S.columns);
            int n = to - from, rows = Mathf.CeilToInt(n / (float)gl.constraintCount);
            K.Layout(g, rows * S.cellSize + Mathf.Max(0, rows - 1) * S.cellSpacing);
            for (int i = from; i < to; i++) Cell(g, inv, i);
        }

        static void Cell(RectTransform parent, ASHInventory inv, int index)
        {
            var sl = inv.Slots[index];
            var it = sl.IsEmpty ? null : sl.item;
            bool sel = it && _picked == it && _pickedPack == index;
            var r = K.Rect("Cell" + index, parent);
            var bg = K.Image(r, sel ? K.RowSel : new Color(1f, 1f, 1f, it ? 0.09f : 0.04f), true);
            if (!it) return;

            bool wear = Wearable(it);
            var icon = K.Image(K.Stretch("Icon", r, 6, 6, 6, 6), new Color(1f, 1f, 1f, wear ? 1f : 0.55f));
            icon.preserveAspect = true;
            if (it.icon) icon.sprite = it.icon; else icon.enabled = false;
            if (!it.icon)
                K.Text(K.Stretch("Abbr", r), it.displayName.Substring(0, Mathf.Min(2, it.displayName.Length)).ToUpperInvariant(), 16, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (sl.amount > 1)
            {
                var a = K.Text(K.Stretch("Amt", r, 2, 1, 4, 2), sl.amount.ToString(), 12, K.Ink, TextAnchor.LowerRight, FontStyle.Bold);
                K.Shadow(a, 1f);
            }
            if (wear) K.Image(K.At("Wear", r, new Vector2(0, 1), new Vector2(3, -3), new Vector2(7, 7)), K.Accent); // small marker: can be worn

            int pack = index;
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener(() => ClickCell(it, pack));
            var drag = r.gameObject.AddComponent<DragItem>();
            drag.item = it; drag.packSlot = pack;
            ASHItemTooltip.Attach(r, () => pack < inv.Slots.Count ? ASHItemTooltip.Info.Slot(inv.Slots[pack]) : default);
        }

        static void ClickCell(ASHItemDefinition it, int pack)
        {
            if (!Wearable(it)) { _picked = null; _pickedPack = -1; Hint($"{it.displayName} can't be worn."); ASHCharacterEquipment.Ensure(); Refresh(); return; }
            if (_picked == it && _pickedPack == pack) { _picked = null; _pickedPack = -1; Hint(DefaultHint); Refresh(); return; }
            _picked = it; _pickedPack = pack;
            Hint($"Selected {it.displayName}. Click a slot to wear it.");
            Refresh();
        }

        static void Refresh()
        {
            // the grid shows the selection highlight; redraw it without clobbering the hint
            string h = _hint ? _hint.text : null;
            Rebuild();
            if (h != null) Hint(h);
        }

        static string Short(ASHCharacterEquipment.Slot s)
        {
            switch (s)
            {
                case ASHCharacterEquipment.Slot.Accessory0: return "Acc 1";
                case ASHCharacterEquipment.Slot.Accessory1: return "Acc 2";
                default: return ASHCharacterEquipment.Label(s);
            }
        }

        static void ClickSlot(ASHCharacterEquipment.Slot slot)
        {
            var eq = ASHCharacterEquipment.Ensure();
            if (!eq) return;
            if (_picked != null)
            {
                var it = _picked;
                bool ok = eq.TryEquip(it, _pickedPack, slot);
                _picked = null; _pickedPack = -1;
                Hint(ok ? $"Equipped {it.displayName}." : (eq.Message ?? "Can't wear that."));
                if (!ok) Refresh();
                return;
            }
            var w = eq.View(slot);
            if (w != null && !w.Empty)
            {
                string name = w.item ? w.item.displayName : "item";
                bool ok = eq.Unequip(slot);
                Hint(ok ? $"Took off {name}." : (eq.Message ?? "Pack is full."));
            }
        }

        public static void DropOn(ASHCharacterEquipment.Slot slot, ASHItemDefinition item, int packSlot)
        {
            var eq = ASHCharacterEquipment.Ensure();
            if (!eq || !item) return;
            _picked = null; _pickedPack = -1;
            if (!Wearable(item)) { Hint($"{item.displayName} can't be worn."); return; }
            bool ok = eq.TryEquip(item, packSlot, slot);
            Hint(ok ? $"Equipped {item.displayName}." : (eq.Message ?? "Can't wear that."));
        }

        static void Hint(string s) { if (_hint) _hint.text = s ?? ""; }

        static void EnsureGhost()
        {
            if (_ghost) return;
            var go = new GameObject("WearDragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Object.DontDestroyOnLoad(go);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.overrideSorting = true; c.sortingOrder = 4000;
            _ghost = go.GetComponent<Image>();
            _ghost.raycastTarget = false;
            _ghost.preserveAspect = true;
            go.SetActive(false);
        }

        public static void DragBegin(Sprite icon, Vector2 pos)
        {
            EnsureGhost();
            if (!_ghost) return;
            _ghost.sprite = icon; _ghost.enabled = icon;
            _ghost.rectTransform.sizeDelta = new Vector2(56, 56);
            _ghost.transform.position = pos;
            _ghost.gameObject.SetActive(true);
        }

        public static void DragMove(Vector2 pos) { if (_ghost && _ghost.gameObject.activeSelf) _ghost.transform.position = pos; }
        public static void DragEnd() { if (_ghost) _ghost.gameObject.SetActive(false); }

        sealed class DragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public ASHItemDefinition item; public int packSlot;
            public void OnBeginDrag(PointerEventData e) { ASHCharacterTab.DragBegin(item ? item.icon : null, e.position); }
            public void OnDrag(PointerEventData e) { ASHCharacterTab.DragMove(e.position); }
            public void OnEndDrag(PointerEventData e) { ASHCharacterTab.DragEnd(); }
        }

        sealed class DropZone : MonoBehaviour, IDropHandler
        {
            public ASHCharacterEquipment.Slot slot;
            public void OnDrop(PointerEventData e)
            {
                var d = e.pointerDrag ? e.pointerDrag.GetComponent<DragItem>() : null;
                if (d != null) ASHCharacterTab.DropOn(slot, d.item, d.packSlot);
            }
        }
    }
}
