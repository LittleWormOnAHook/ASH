using System.Text;
using ASH.Core;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.Companion
{
    /// <summary>
    /// The dog menu (E on the dog): Follow / Stay / Defend / Call / Pet / Feed, its status (level, XP, health, hunger, bond)
    /// and its pack: click one of your items to give it, click a dog pack slot to take it back. Esc, X or walking away closes.
    /// Code-built uGUI in the ASHUIKit style (same as the campfire menu).
    /// </summary>
    public sealed class ASHDogMenu : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHDogMenu()
        {
            _i = default;
        }

        public const string PanelId = "dog";
        static ASHDogMenu _i;
        public static bool IsOpen => _i && _i._root && _i._root.gameObject.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() => ASHDogCompanion.OpenMenu = Open;

        public static bool Open(ASHDogCompanion dog, ASHInventory inv, Transform player)
        {
            if (!dog) return false;
            if (!inv) inv = FindAnyObjectByType<ASHInventory>(); if (!inv) return false;
            if (!_i) { var go = new GameObject("ASH_DogMenu"); _i = go.AddComponent<ASHDogMenu>(); _i.Build(); }
            var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.CloseAllPanels();
            _i._dog = dog; _i._inv = inv; _i._player = player; _i._sig = null;
            _i._root.gameObject.SetActive(true); ASHUIState.SetOpen(PanelId, true); _i.Refresh();
            return true;
        }
        public static void CloseIfOpen() { if (IsOpen) _i.Close(); }

        ASHDogCompanion _dog; ASHInventory _inv; Transform _player; string _sig;
        RectTransform _root, _dogList, _invList, _xpFill; Text _title, _status, _packLbl; Button _bFollow, _bStay, _bDefend;

        static RectTransform TL(string n, Transform p, float x, float y, float w, float h) => K.At(n, p, new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));

        void Build()
        {
            var cv = gameObject.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 60;
            var sc = gameObject.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var ct = (RectTransform)transform;
            _root = K.At("DogMenu", ct, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1060, 620)); K.Image(_root, K.Panel, true);
            K.Image(K.At("Accent", _root, new Vector2(0.5f, 1), Vector2.zero, new Vector2(1060, 3)), K.Accent);
            _title = K.Text(TL("Title", _root, 28, 14, 700, 40), "", 30, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            K.Button(K.At("Close", _root, new Vector2(1, 1), new Vector2(-20, -16), new Vector2(46, 38)), "X", 20, Close, new Color(1, 1, 1, 0.15f));

            // status + commands
            var xbg = TL("XpBar", _root, 28, 62, 300, 8); K.Image(xbg, new Color(1, 1, 1, 0.1f));
            _xpFill = K.Stretch("Fill", xbg); K.Image(_xpFill, K.Accent); _xpFill.anchorMax = new Vector2(0, 1);
            _status = K.Text(TL("Status", _root, 28, 78, 300, 190), "", 17, K.Ink, TextAnchor.UpperLeft);
            float y = 278;
            _bFollow = K.Button(TL("Follow", _root, 28, y, 145, 44), "FOLLOW", 18, () => Cmd(ASHDogMode.Follow)); _bStay = K.Button(TL("Stay", _root, 183, y, 145, 44), "STAY", 18, () => Cmd(ASHDogMode.Stay)); y += 52;
            _bDefend = K.Button(TL("Defend", _root, 28, y, 145, 44), "DEFEND", 18, () => Cmd(ASHDogMode.Defend));
            K.Button(TL("Call", _root, 183, y, 145, 44), "CALL", 18, () => { if (_dog) _dog.Call(); _sig = null; }, new Color(1, 1, 1, 0.18f)); y += 52;
            K.Button(TL("Pet", _root, 28, y, 145, 44), "PET", 18, () => { if (_dog) _dog.Pet(); _sig = null; }, new Color(1, 1, 1, 0.18f));
            K.Button(TL("Feed", _root, 183, y, 145, 44), "FEED", 18, () => { if (_dog) _dog.Feed(_inv); _sig = null; }, new Color(1, 1, 1, 0.18f)); y += 60;
            K.Text(TL("Hint", _root, 28, y, 300, 60), "<color=#a69e91>C calls the dog from anywhere. The journal Pets tab shows its levels.</color>", 14, K.Dim, TextAnchor.UpperLeft);

            _packLbl = K.Text(TL("DogLbl", _root, 360, 64, 320, 24), "", 18, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _dogList = K.ScrollList(TL("DogPack", _root, 360, 92, 320, 508), 3f);
            K.Text(TL("InvLbl", _root, 710, 64, 330, 24), "YOUR PACK  <size=14><color=#a69e91>click to give</color></size>", 18, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _invList = K.ScrollList(TL("Inv", _root, 710, 92, 330, 508), 3f);
            _root.gameObject.SetActive(false);
        }

        void OnDestroy() { if (_i == this) { _i = null; ASHUIState.SetOpen(PanelId, false); } }

        void Cmd(ASHDogMode m) { if (_dog) _dog.SetMode(m); _sig = null; }

        void Close() { if (_root) _root.gameObject.SetActive(false); ASHUIState.SetOpen(PanelId, false); }

        void Update()
        {
            if (!IsOpen) return;
            var stats = _player ? _player.GetComponentInParent<ASH.Survival.ASHSurvivalStats>() : null;
            bool far = !_dog || !_dog.isActiveAndEnabled || _dog.Dead || (_player && (_player.position - _dog.transform.position).sqrMagnitude > 64f);
            if (far || (stats && stats.IsDead) || ASHUIState.IsOpen(ASHPanels.Inventory) || ASHUIState.IsOpen(ASHPanels.Journal) || ASHUIState.IsOpen(ASHPanels.Map) || ASHUIState.IsOpen(ASHPanels.Death)) { Close(); return; }
            Refresh();
        }

        static Color Sel(bool on) => on ? new Color(0.95f, 0.62f, 0.28f, 0.9f) : new Color(1, 1, 1, 0.12f);

        void Refresh()
        {
            if (!_dog || !_inv) return;
            var S = ASHCompanionSettings.I; var d = _dog;
            _title.text = $"{d.DogName.ToUpperInvariant()}  <size=20><color=#e8dfd0>Lv {d.Level}</color></size>";
            float need = d.XpNeed; _xpFill.anchorMax = new Vector2(need > 0f ? Mathf.Clamp01(d.Xp / need) : 1f, 1); _xpFill.offsetMin = _xpFill.offsetMax = Vector2.zero;
            var sb = new StringBuilder();
            sb.AppendLine(need > 0f ? $"<color=#a69e91>{d.Xp:0} / {need:0} XP to level {d.Level + 1}</color>" : "<color=#a69e91>Max level</color>");
            sb.AppendLine($"Mode  <b>{d.Mode}</b>");
            sb.AppendLine($"<color=#a69e91>{d.Activity}</color>");
            sb.AppendLine($"Health  {Mathf.CeilToInt(d.Health)} / {Mathf.CeilToInt(d.MaxHealth)}");
            if (S.hunger) sb.AppendLine($"Hunger  {(d.Hunger < S.starvingBelow ? "<color=#f2806b>" : "")}{d.Hunger:0}%{(d.Hunger < S.starvingBelow ? " starving</color>" : "")}");
            if (S.bond) sb.AppendLine($"Bond  {d.Bond:0}%");
            if (d.CallReadyIn > 0f) sb.AppendLine($"<color=#a69e91>Call ready in {d.CallReadyIn:0} s</color>");
            _status.text = sb.ToString();
            _bFollow.GetComponent<Image>().color = Sel(d.Mode == ASHDogMode.Follow); _bStay.GetComponent<Image>().color = Sel(d.Mode == ASHDogMode.Stay); _bDefend.GetComponent<Image>().color = Sel(d.Mode == ASHDogMode.Defend);

            var sig = new StringBuilder();
            foreach (var p in d.Pack) sig.Append(p.IsEmpty ? "-" : p.item.itemId + p.amount).Append(';');
            sig.Append('|'); foreach (var s in _inv.Slots) sig.Append(s == null || s.IsEmpty ? "-" : s.item.itemId + s.amount).Append(';');
            string sg = sig.ToString(); if (sg == _sig) return; _sig = sg;

            int used = 0; foreach (var p in d.Pack) if (!p.IsEmpty) used++;
            _packLbl.text = $"{d.DogName.ToUpperInvariant()}'S PACK  <size=14><color=#a69e91>{used}/{d.PackSlots} - click to take</color></size>";
            K.Clear(_dogList);
            for (int i = 0; i < d.Pack.Count; i++)
            {
                var p = d.Pack[i]; int idx = i;
                Row(_dogList, p.IsEmpty ? null : p.item, p.IsEmpty ? "<color=#857d72>empty</color>" : p.item.displayName, p.IsEmpty ? "" : "x" + p.amount,
                    p.IsEmpty ? (System.Action)null : () => { int n = _dog.TakeSlot(_inv, idx); if (n == 0) Toast("Your pack is full."); _sig = null; });
            }
            K.Clear(_invList); int shown = 0;
            for (int i = 0; i < _inv.Slots.Count; i++)
            {
                var s = _inv.Slots[i]; if (s == null || s.IsEmpty || ASHInventory.IsKeyItem(s.item)) continue; int idx = i; shown++;
                bool food = ASHDogCompanion.IsDogFood(s.item);
                Row(_invList, s.item, s.item.displayName + (food ? "  <size=12><color=#9fd39b>food</color></size>" : ""), "x" + s.amount,
                    () => { int n = _dog.GiveFromSlot(_inv, idx); if (n == 0) Toast($"{_dog.DogName}'s pack is full."); _sig = null; });
            }
            if (shown == 0) K.Layout(K.Text(K.Rect("Empty", _invList), "  Nothing to give.", 16, K.Dim), 40);
        }

        void Toast(string m) { var h = FindAnyObjectByType<ASHHud>(); if (h) h.Toast(m); }

        static void Row(RectTransform list, ASHItemDefinition it, string name, string right, System.Action onClick)
        {
            var row = K.Rect("Row", list); K.Layout(row, 46); var img = K.Image(row, K.Row, true);
            if (onClick != null) { var b = row.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(() => onClick()); }
            if (it) { var ic = K.Image(K.At("Icon", row, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(36, 36)), it.icon ? Color.white : it.uiTint); if (it.icon) { ic.sprite = it.icon; ic.preserveAspect = true; } }
            K.Text(K.Stretch("Name", row, 50, 0, 60, 0), name, 16, K.Ink);
            K.Text(K.Stretch("Right", row, 0, 0, 10, 0), right, 15, K.Ink, TextAnchor.MiddleRight);
        }
    }
}