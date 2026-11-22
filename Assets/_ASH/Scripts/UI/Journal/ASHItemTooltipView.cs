using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>The shared item popup (one per game, own overlay canvas above everything). Built in code on first hover.</summary>
    public sealed class ASHItemTooltipView : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHItemTooltipView() { Current = null; }
        public static ASHItemTooltipView Current { get; private set; }

        Canvas _canvas; CanvasScaler _scaler; RectTransform _canvasRt, _panel, _iconBox; CanvasGroup _group;
        Image _bg, _pip, _icon, _frame, _rule; Text _title, _sub, _body, _abbr;
        ASHTooltipTarget _target; bool _focus; float _since, _alpha, _nextRefresh; bool _shown;

        public static ASHItemTooltipView Ensure()
        {
            if (Current) return Current;
            var go = new GameObject("ASH_ItemTooltip"); DontDestroyOnLoad(go);
            Current = go.AddComponent<ASHItemTooltipView>(); Current.Build();
            return Current;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.overrideSorting = true;
            _scaler = gameObject.AddComponent<CanvasScaler>(); _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; _scaler.referenceResolution = new Vector2(1920, 1080); _scaler.matchWidthOrHeight = 0.5f;
            _canvasRt = (RectTransform)transform;
            _panel = K.Rect("Panel", transform); _panel.anchorMin = _panel.anchorMax = new Vector2(0, 0); _panel.pivot = new Vector2(0, 1);
            _group = _panel.gameObject.AddComponent<CanvasGroup>(); _group.blocksRaycasts = false; _group.interactable = false; _group.alpha = 0f;
            _bg = K.Image(_panel, Color.white); _bg.type = Image.Type.Sliced;
            _pip = K.Image(K.Rect("Rarity", _panel), Color.clear);
            _iconBox = K.Rect("IconBox", _panel); _iconBox.anchorMin = _iconBox.anchorMax = _iconBox.pivot = new Vector2(0, 1);
            _icon = K.Image(K.Stretch("Icon", _iconBox, 8, 8, 8, 8), Color.white); _icon.preserveAspect = true;
            _abbr = K.Text(K.Stretch("Abbr", _iconBox), "", 24, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _frame = K.Image(K.Stretch("Frame", _iconBox), Color.white); _frame.preserveAspect = false;
            _title = K.Text(K.Rect("Title", _panel), "", 20, Color.black, TextAnchor.UpperLeft);
            _sub = K.Text(K.Rect("Sub", _panel), "", 14, Color.black, TextAnchor.UpperLeft);
            _rule = K.Image(K.Rect("Rule", _panel), Color.white); _rule.preserveAspect = false;
            _body = K.Text(K.Rect("Body", _panel), "", 15, Color.black, TextAnchor.UpperLeft);
            foreach (var t in new[] { _title, _sub, _body }) { t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.lineSpacing = 1.05f; }
            _panel.gameObject.SetActive(false);
        }

        public void Hover(ASHTooltipTarget t, bool focus)
        {
            if (!t) return;
            if (_target != t) { _target = t; _since = 0f; _shown = false; }
            _focus = focus;
        }

        public void Unhover(ASHTooltipTarget t) { if (_target == t) { _target = null; _shown = false; } }
        public void ForceHide() { _target = null; _shown = false; _alpha = 0f; if (_group) _group.alpha = 0f; if (_panel) _panel.gameObject.SetActive(false); }

        void Update()
        {
            var S = ASHItemTooltipSettings.I;
            _canvas.sortingOrder = S.sortingOrder;
            bool active = S.tooltipsEnabled && _target && _target.isActiveAndEnabled && !ASHItemTooltip.Blocked;
            if (active && S.hideWhileMouseHeld && !_focus) { var m = Mouse.current; if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed)) active = false; }
            if (active && !_focus && Cursor.lockState == CursorLockMode.Locked) active = false; // gameplay view, mouse captured
            bool show = false;
            if (!active) { _since = 0f; _shown = false; }
            else
            {
                _since += Time.unscaledDeltaTime;
                if (_since >= S.hoverDelay)
                {
                    if (!_shown || Time.unscaledTime >= _nextRefresh)
                    {
                        var info = _target.Get();
                        if (info.IsEmpty) _shown = false;
                        else { Fill(info, S); _shown = true; }
                        _nextRefresh = Time.unscaledTime + S.refreshInterval;
                    }
                    show = _shown;
                }
            }
            float goal = show ? 1f : 0f;
            _alpha = S.fadeTime <= 0f ? goal : Mathf.MoveTowards(_alpha, goal, Time.unscaledDeltaTime / S.fadeTime);
            _group.alpha = _alpha;
            bool vis = _alpha > 0.001f;
            if (_panel.gameObject.activeSelf != vis) _panel.gameObject.SetActive(vis);
            if (vis && _target) Place(S);
        }

        void Fill(ASHItemTooltip.Info info, ASHItemTooltipSettings S)
        {
            ASHItemTooltip.Compose(info, out string title, out string sub, out string body);
            float sc = S.scale, W = S.maxWidth * sc;
            var border = ASHJournalArt.TooltipBorder; float bs = S.panelBorderScale * sc;
            float padL = border.x * bs + S.innerPadding * sc, padR = border.z * bs + S.innerPadding * sc;
            float padT = border.w * bs + S.innerPadding * sc, padB = border.y * bs + S.innerPadding * sc;

            _bg.sprite = ASHJournalArt.TooltipPanel; _bg.color = S.panelTint; _bg.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, bs);

            var it = info.item; var r = info.recipe;
            bool icon = S.showIcon && (it || r) && !info.recipeLocked;
            float isz = icon ? S.iconSize * sc : 0f;
            _iconBox.gameObject.SetActive(icon);
            if (icon)
            {
                Sprite sp = r && r.icon ? r.icon : it ? it.icon : null;
                _icon.sprite = sp; _icon.enabled = sp; _icon.color = Color.white;
                _abbr.text = sp ? "" : (it ? it.displayName.Substring(0, Mathf.Min(2, it.displayName.Length)).ToUpperInvariant() : "?");
                _abbr.color = S.ink; _abbr.fontSize = Mathf.RoundToInt(isz * 0.36f);
                var fr = S.useSlotFrame ? ASHJournalArt.SlotFrame : null; _frame.sprite = fr; _frame.enabled = fr;
                _iconBox.anchoredPosition = new Vector2(padL, -padT); _iconBox.sizeDelta = new Vector2(isz, isz);
            }

            float tx = padL + (icon ? isz + 12f * sc : 0f), tw = W - tx - padR;
            SetText(_title, title, Mathf.RoundToInt(S.nameSize * sc), tx, padT, tw, out float th);
            SetText(_sub, sub, Mathf.RoundToInt(S.subSize * sc), tx, padT + th + 2f * sc, tw, out float sh);
            float headH = Mathf.Max(isz, th + sh + 2f * sc);
            float y = padT + headH + 6f * sc;

            bool hasBody = !string.IsNullOrEmpty(body);
            _rule.gameObject.SetActive(hasBody);
            if (hasBody)
            {
                var rs = ASHJournalArt.PageRule; float rw = W - padL - padR; float rh = rs ? Mathf.Max(6f, rw * 64f / 1024f) : Mathf.Max(1.5f, 1.5f * sc);
                _rule.sprite = rs; _rule.type = Image.Type.Simple;
                var c = S.ink; c.a = rs ? Mathf.Clamp01(S.ruleAlpha * 2.2f) : S.ruleAlpha; _rule.color = rs ? new Color(1, 1, 1, c.a) : c;
                var rr = _rule.rectTransform; rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0, 1); rr.anchoredPosition = new Vector2(padL, -y); rr.sizeDelta = new Vector2(rw, rh);
                y += rh + 6f * sc;
                SetText(_body, body, Mathf.RoundToInt(S.bodySize * sc), padL, y, W - padL - padR, out float bh);
                y += bh;
            }
            _body.gameObject.SetActive(hasBody);
            float H = y + padB;
            _panel.sizeDelta = new Vector2(W, H);

            bool pip = S.rarityPip && it;
            _pip.gameObject.SetActive(pip);
            if (pip)
            {
                var pr = _pip.rectTransform; pr.anchorMin = new Vector2(0, 0); pr.anchorMax = new Vector2(0, 1); pr.pivot = new Vector2(0, 0.5f);
                float inset = border.x * bs * 0.55f;
                pr.offsetMin = new Vector2(inset, padB * 0.8f); pr.offsetMax = new Vector2(inset + S.rarityPipWidth * sc, -padT * 0.8f);
                var col = ASHItemTooltip.RarityColor(it.rarity); col.a = 0.9f; _pip.color = col;
            }
        }

        static void SetText(Text t, string s, int size, float x, float y, float w, out float h)
        {
            t.fontSize = Mathf.Max(6, size); t.text = s ?? "";
            var rt = t.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, 10f);
            h = string.IsNullOrEmpty(s) ? 0f : Mathf.Ceil(t.preferredHeight);
            rt.sizeDelta = new Vector2(w, h);
        }

        void Place(ASHItemTooltipSettings S)
        {
            var size = _panel.sizeDelta; var cs = _canvasRt.rect.size; float pad = S.screenPadding;
            Vector2 local;
            if (_focus || Mouse.current == null)
            {
                // beside the focused slot (right, else left)
                var trt = _target.transform as RectTransform; if (!trt) return;
                var cv = _target.GetComponentInParent<Canvas>(); var cam = cv && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
                var wc = new Vector3[4]; trt.GetWorldCorners(wc);
                Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, wc[2]), bl = RectTransformUtility.WorldToScreenPoint(cam, wc[0]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, tr, null, out var lTr);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, bl, null, out var lBl);
                local = new Vector2(lTr.x + 8f, lTr.y);
                if (local.x + cs.x * 0.5f + size.x > cs.x - pad) local.x = lBl.x - 8f - size.x;
            }
            else
            {
                Vector2 mp = Mouse.current.position.ReadValue();
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mp, null, out var lm);
                local = new Vector2(lm.x + S.cursorOffset.x, lm.y - S.cursorOffset.y);
                if (local.x + cs.x * 0.5f + size.x > cs.x - pad) local.x = lm.x - S.cursorOffset.x - size.x; // flip left of the cursor
            }
            // local is relative to the canvas centre; the panel is anchored bottom-left with a top-left pivot
            var p = local + cs * 0.5f;
            p.x = Mathf.Clamp(p.x, pad, Mathf.Max(pad, cs.x - pad - size.x));
            p.y = Mathf.Clamp(p.y, pad + size.y, Mathf.Max(pad + size.y, cs.y - pad));
            _panel.anchoredPosition = p;
        }
    }
}
