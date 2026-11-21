using System.Collections.Generic;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>Character journal tab: silhouette + wear slots, drag from the pack list, compatibility checks.</summary>
    public static class ASHCharacterTab
    {
        static RectTransform _list;
        static Text _hint;
        static readonly Dictionary<ASHCharacterEquipment.Slot, Image> _icons = new Dictionary<ASHCharacterEquipment.Slot, Image>();
        static readonly Dictionary<ASHCharacterEquipment.Slot, Text> _labels = new Dictionary<ASHCharacterEquipment.Slot, Text>();
        static ASHItemDefinition _picked;
        static int _pickedPack = -1;
        static Image _ghost;

        public static GameObject Build(RectTransform body)
        {
            ASHCharacterEquipment.Ensure();
            var root = K.Stretch("Character", body);
            _list = K.ScrollList(K.Stretch("List", root, 0, 0, 760, 0));
            var stage = K.Stretch("Stage", root, 460, 0, 0, 0);
            K.Image(K.At("Paper", stage, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 620)), new Color(0.1f, 0.09f, 0.08f, 0.55f));

            // crude generic silhouette (no art needed)
            Silhouette(stage);

            Place(stage, ASHCharacterEquipment.Slot.Head, new Vector2(0f, 250), "Head");
            Place(stage, ASHCharacterEquipment.Slot.Face, new Vector2(0f, 175), "Face");
            Place(stage, ASHCharacterEquipment.Slot.Outerwear, new Vector2(-170f, 80), "Outerwear");
            Place(stage, ASHCharacterEquipment.Slot.Torso, new Vector2(0f, 80), "Torso");
            Place(stage, ASHCharacterEquipment.Slot.Backpack, new Vector2(170f, 80), "Backpack");
            Place(stage, ASHCharacterEquipment.Slot.Hands, new Vector2(-170f, -20), "Hands");
            Place(stage, ASHCharacterEquipment.Slot.Accessory0, new Vector2(170f, -20), "Acc 1");
            Place(stage, ASHCharacterEquipment.Slot.Legs, new Vector2(-80f, -130), "Legs");
            Place(stage, ASHCharacterEquipment.Slot.Feet, new Vector2(80f, -130), "Feet");
            Place(stage, ASHCharacterEquipment.Slot.Accessory1, new Vector2(0f, -220), "Acc 2");

            _hint = K.Text(K.At("Hint", stage, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(500, 40)), "Drag a wearable from the list onto a slot, or click one and then a slot.", 14, K.Dim, TextAnchor.MiddleCenter);
            EnsureGhost();
            return root.gameObject;
        }

        static void Silhouette(RectTransform stage)
        {
            void Box(string n, Vector2 pos, Vector2 size, Color c)
            {
                var r = K.At(n, stage, new Vector2(0.5f, 0.5f), pos, size);
                var img = K.Image(r, c);
                img.raycastTarget = false;
            }
            var tone = new Color(0.16f, 0.15f, 0.13f, 0.95f);
            Box("SilHead", new Vector2(0, 210), new Vector2(64, 72), tone);
            Box("SilNeck", new Vector2(0, 168), new Vector2(22, 20), tone);
            Box("SilTorso", new Vector2(0, 80), new Vector2(110, 150), tone);
            Box("SilArmL", new Vector2(-78, 70), new Vector2(28, 140), tone);
            Box("SilArmR", new Vector2(78, 70), new Vector2(28, 140), tone);
            Box("SilLegL", new Vector2(-28, -80), new Vector2(34, 150), tone);
            Box("SilLegR", new Vector2(28, -80), new Vector2(34, 150), tone);
        }

        static void Place(RectTransform stage, ASHCharacterEquipment.Slot slot, Vector2 pos, string caption)
        {
            var r = K.At("Slot" + slot, stage, new Vector2(0.5f, 0.5f), pos, new Vector2(92, 92));
            var bg = K.Image(r, new Color(0.18f, 0.16f, 0.13f, 0.95f), true);
            var icon = K.Image(K.At("Icon", r, new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(56, 56)), Color.white, false);
            icon.enabled = false;
            var lab = K.Text(K.At("Cap", r, new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(90, 22)), caption, 12, K.Dim, TextAnchor.MiddleCenter);
            _icons[slot] = icon;
            _labels[slot] = lab;
            var drop = r.gameObject.AddComponent<DropZone>();
            drop.slot = slot;
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => ClickSlot(slot));
        }

        public static void Rebuild()
        {
            var eq = ASHCharacterEquipment.Ensure();
            var inv = Object.FindAnyObjectByType<ASHInventory>();
            if (_list)
            {
                K.Clear(_list);
                if (inv)
                {
                    for (int i = 0; i < inv.Slots.Count; i++)
                    {
                        var sl = inv.Slots[i];
                        if (sl.IsEmpty || !sl.item) continue;
                        if (sl.item.wearSlot == ASHWearSlot.None && sl.item.itemId != "wet_rag" && sl.item.itemId != "gas_mask") continue;
                        int pack = i;
                        var it = sl.item;
                        var row = K.Rect("Row" + i, _list);
                        K.Layout(row, 52);
                        bool sel = _picked == it && _pickedPack == pack;
                        var bg = K.Image(row, sel ? K.RowSel : K.Row, true);
                        var ic = K.Image(K.At("Ic", row, new Vector2(0, 0.5f), new Vector2(28, 0), new Vector2(40, 40)), Color.white, false);
                        if (it.icon) { ic.sprite = it.icon; ic.enabled = true; } else ic.enabled = false;
                        K.Text(K.At("Nm", row, new Vector2(0, 0.5f), new Vector2(140, 0), new Vector2(240, 40)), it.displayName, 16, K.Ink, TextAnchor.MiddleLeft);
                        var where = it.wearSlot != ASHWearSlot.None ? it.wearSlot.ToString() : "Face";
                        K.Text(K.At("Wh", row, new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(120, 40)), where, 14, K.Dim, TextAnchor.MiddleRight);
                        var btn = row.gameObject.AddComponent<Button>();
                        btn.targetGraphic = bg;
                        btn.onClick.AddListener(() => { _picked = it; _pickedPack = pack; Hint($"Selected {it.displayName}. Click a slot to wear it."); });
                        var drag = row.gameObject.AddComponent<DragItem>();
                        drag.item = it; drag.packSlot = pack;
                    }
                }
            }

            foreach (ASHCharacterEquipment.Slot s in System.Enum.GetValues(typeof(ASHCharacterEquipment.Slot)))
            {
                if (!_icons.TryGetValue(s, out var icon)) continue;
                var w = eq ? eq.View(s) : null;
                if (w != null && w.item)
                {
                    if (w.item.icon) { icon.sprite = w.item.icon; icon.color = Color.white; icon.enabled = true; }
                    else { icon.sprite = null; icon.enabled = false; }
                    if (_labels.TryGetValue(s, out var lab)) lab.text = w.item.displayName;
                }
                else
                {
                    icon.enabled = false;
                    if (_labels.TryGetValue(s, out var lab)) lab.text = Short(s);
                }
            }
            if (_picked == null && string.IsNullOrEmpty(ASHCharacterEquipment.Ensure()?.Message))
                Hint("Drag a wearable from the list onto a slot, or click one and then a slot. Click a filled slot to take it off.");
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
                bool ok = eq.TryEquip(_picked, _pickedPack, slot);
                Hint(ok ? $"Equipped {_picked.displayName}." : (eq.Message ?? "Can't wear that."));
                _picked = null; _pickedPack = -1;
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
            bool ok = eq.TryEquip(item, packSlot, slot);
            Hint(ok ? $"Equipped {item.displayName}." : (eq.Message ?? "Can't wear that."));
            _picked = null; _pickedPack = -1;
        }

        static void Hint(string s) { if (_hint) _hint.text = s ?? ""; }

        static void EnsureGhost()
        {
            if (_ghost) return;
            var go = new GameObject("WearDragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Object.DontDestroyOnLoad(go);
            var c = go.AddComponent<Canvas>();
            c.overrideSorting = true; c.sortingOrder = 4000;
            go.AddComponent<GraphicRaycaster>();
            _ghost = go.GetComponent<Image>();
            _ghost.raycastTarget = false;
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
