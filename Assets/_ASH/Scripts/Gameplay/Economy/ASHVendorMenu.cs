using ASH.Core;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.Gameplay
{
    /// <summary>Vendor buy / sell window. Code-built uGUI in the tent / dog-menu style (ASHUIKit palette). Left: the vendor's stock
    /// (price, stock left, level locks). Right: your pack (sell one at a time; key items and unsellable items are greyed out).
    /// Prices in Copper / Silver / Gold Bits. E, Esc or walking away closes it.</summary>
    public sealed class ASHVendorMenu : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHVendorMenu() { _i = default; }

        public const string PanelId = "vendor";
        static ASHVendorMenu _i;
        public static bool IsOpen => _i && _i._root && _i._root.gameObject.activeSelf;

        ASHVendor _vendor; ASHInventory _inv; Transform _player; int _openFrame; bool _dirty;
        RectTransform _root, _buyList, _sellList; Text _title, _purse, _msg;

        ASHEconomySettings S => ASHEconomySettings.I;

        public static bool Open(ASHVendor v, ASHInventory inv, Transform player)
        {
            if (!v) return false;
            if (!inv) inv = FindAnyObjectByType<ASHInventory>();
            if (!_i) { var go = new GameObject("ASH_VendorMenu"); _i = go.AddComponent<ASHVendorMenu>(); _i.Build(); }
            var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.CloseAllPanels();
            ASHWallet.Ensure();
            _i._vendor = v; _i._inv = inv; _i._player = player; _i._openFrame = Time.frameCount; _i._msg.text = "";
            _i._root.gameObject.SetActive(true); ASHUIState.SetOpen(PanelId, true);
            _i.Hook(true); _i.Rebuild();
            return true;
        }

        void Hook(bool on)
        {
            ASHWallet.Changed -= MarkDirty; if (_inv) _inv.Changed -= MarkDirty;
            if (on) { ASHWallet.Changed += MarkDirty; if (_inv) _inv.Changed += MarkDirty; }
        }
        void MarkDirty() => _dirty = true;

        static RectTransform TL(string n, Transform p, float x, float y, float w, float h) => K.At(n, p, new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));

        void Build()
        {
            var cv = gameObject.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 60;
            var sc = gameObject.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var ct = (RectTransform)transform;
            _root = K.At("VendorMenu", ct, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1060, 680)); K.Image(_root, K.Panel, true);
            K.Image(K.At("Accent", _root, new Vector2(0.5f, 1), Vector2.zero, new Vector2(1060, 3)), K.Accent);
            _title = K.Text(TL("Title", _root, 28, 14, 700, 40), "", 30, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            K.Button(K.At("Close", _root, new Vector2(1, 1), new Vector2(-20, -16), new Vector2(46, 38)), "X", 20, Close, new Color(1, 1, 1, 0.15f));
            _purse = K.Text(TL("Purse", _root, 28, 56, 1000, 44), "", 18, K.Ink, TextAnchor.MiddleLeft);
            K.Text(TL("BuyHdr", _root, 28, 104, 490, 26), "BUY", 18, K.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            K.Text(TL("SellHdr", _root, 542, 104, 490, 26), "SELL  <color=#a69e91>(one at a time)</color>", 18, K.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _buyList = K.ScrollList(TL("Buy", _root, 22, 134, 500, 474), 3f);
            _sellList = K.ScrollList(TL("Sell", _root, 538, 134, 500, 474), 3f);
            _msg = K.Text(TL("Msg", _root, 28, 620, 1000, 44), "", 16, K.Ink, TextAnchor.MiddleLeft);
            _root.gameObject.SetActive(false);
        }

        void OnDestroy() { if (_i != this) return; Hook(false); _i = null; ASHUIState.SetOpen(PanelId, false); }

        void Close() { Hook(false); if (_root) _root.gameObject.SetActive(false); ASHUIState.SetOpen(PanelId, false); }

        void Update()
        {
            if (!IsOpen) return;
            var kb = Keyboard.current;
            bool far = !_vendor || (_player && (_player.position - _vendor.transform.position).sqrMagnitude > Mathf.Pow(S.useRange + 2.5f, 2f));
            if (far || ASHUIState.IsOpen(ASHPanels.Inventory) || ASHUIState.IsOpen(ASHPanels.Journal) || ASHUIState.IsOpen(ASHPanels.Map) || ASHUIState.IsOpen(ASHPanels.Death)) { Close(); return; }
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) && Time.frameCount > _openFrame + 1) { Close(); return; }
            if (_dirty) { _dirty = false; Rebuild(); }
        }

        void Say(string m) { if (_msg) _msg.text = m ?? ""; }

        void Rebuild()
        {
            var w = ASHWallet.Ensure();
            _title.text = S.vendorName.ToUpperInvariant();
            _purse.text = $"<color=#a69e91>{S.vendorGreeting}</color>\nPurse  {(w ? w.Formatted : "-")}";
            K.Clear(_buyList); K.Clear(_sellList);
            foreach (var e in S.stock)
            {
                if (e == null || !e.item) continue;
                int left = _vendor.Left(e); string lockR = ASHVendor.LockReason(e); int price = ASHEconomy.BuyPrice(e.item);
                bool can = lockR == null && left > 0 && w && w.Balance >= price;
                string sub = lockR != null ? $"<color=#f2806b>{lockR}</color>" : left == int.MaxValue ? "" : left <= 0 ? "<color=#f2806b>sold out</color>" : $"<color=#a69e91>{left} left</color>";
                var entry = e;
                Row(_buyList, e.item, ASHEconomy.Format(price), sub, "Buy", can, () => { _vendor.Buy(entry, _inv, out var m); Say(m); _dirty = true; });
            }
            if (_inv)
            {
                var slots = _inv.Slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    var sl = slots[i]; if (sl.IsEmpty) continue;
                    var it = sl.item; bool sellable = it.CanSell && !ASHInventory.IsKeyItem(it);
                    string price = sellable ? ASHEconomy.Format(ASHEconomy.SellPrice(it, sl.condition)) : "<color=#a69e91>won't buy</color>";
                    string sub = (sl.amount > 1 ? $"x{sl.amount}" : "") + (it.maxStack <= 1 && sl.condition < 0.999f ? $"  {sl.condition * 100f:0}%" : "");
                    int idx = i;
                    Row(_sellList, it, price, $"<color=#a69e91>{sub}</color>", "Sell", sellable, () => { _vendor.SellSlot(_inv, idx, out var m); Say(m); _dirty = true; });
                }
            }
        }

        void Row(RectTransform list, ASHItemDefinition it, string price, string sub, string verb, bool enabled, UnityEngine.Events.UnityAction act)
        {
            var row = K.Rect("Row", list); K.Image(row, K.Row, true); K.Layout(row, 46);
            var icon = K.Image(TL("Icon", row, 6, 4, 38, 38), Color.white);
            if (it.icon) { icon.sprite = it.icon; icon.preserveAspect = true; } else icon.color = ASHClothingSettings.I.RarityColor(it.rarity);
            K.Text(TL("Name", row, 52, 2, 250, 24), it.displayName, 16, K.Ink, TextAnchor.MiddleLeft);
            K.Text(TL("Sub", row, 52, 24, 250, 20), sub ?? "", 13, K.Dim, TextAnchor.MiddleLeft);
            K.Text(TL("Price", row, 300, 0, 110, 46), price, 15, K.Ink, TextAnchor.MiddleRight);
            var b = K.Button(TL("Btn", row, 418, 7, 70, 32), verb, 15, act, enabled ? (Color?)null : new Color(1, 1, 1, 0.12f));
            b.interactable = enabled;
        }
    }
}
