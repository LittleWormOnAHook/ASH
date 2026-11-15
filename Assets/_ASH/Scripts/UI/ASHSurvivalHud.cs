using ASH.Core;
using ASH.Inventory;
using ASH.Survival;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>
    /// Survival HUD additions (code-built uGUI like the main HUD): air/exposure status, worn rag/mask durability, water bottle fill
    /// (bottom-left, above the bars), the worn rag / filter countdown in game time above the hotbar (icon + h:mm; amber in the
    /// rag's last 30 min / low filter, red near empty; hidden when nothing is worn) and the pulsing red screen edge at low health
    /// while exposed. Positions: ASHExposureSettings (HUD).
    /// </summary>
    public sealed class ASHSurvivalHud : MonoBehaviour
    {
        static readonly Color Amber = new Color(1f, 0.72f, 0.3f), Red = new Color(1f, 0.36f, 0.3f), Green = new Color(0.62f, 0.83f, 0.6f), Water = new Color(0.4f, 0.66f, 0.9f);

        ASHExposure _exp; ASHFaceProtection _face; ASHHud _hud;
        RectTransform _root, _status, _timer, _protW, _bottleW;
        Text _airText, _protText, _bottleText, _timerText, _timerLabel, _timerAbbr; Image _protFill, _bottleFill, _timerIcon, _timerBack, _edge, _airDot;
        float _edgeA; ASHFaceProtection.Kind _iconKind = (ASHFaceProtection.Kind)(-1);
        ASHExposureSettings S => ASHExposureSettings.I;

        public static void Ensure(ASHExposure e)
        {
            if (FindAnyObjectByType<ASHSurvivalHud>()) return;
            var go = new GameObject("ASH_SurvivalHUD", typeof(Canvas), typeof(CanvasScaler));
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 49;
            var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            var h = go.AddComponent<ASHSurvivalHud>(); h._exp = e; h._face = e.GetComponent<ASHFaceProtection>(); h.Build();
        }

        static Sprite EdgeSprite()
        {
            const int n = 128, b = 63;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int d = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y));
                float a = Mathf.Clamp01(1f - d / (float)b); a = a * a * (3f - 2f * a); a *= a;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        void Build()
        {
            _root = (RectTransform)transform;
            // red edge (under everything else here)
            var er = K.Stretch("LowHealthEdge", _root); _edge = K.Image(er, new Color(0.85f, 0.05f, 0.03f, 0f));
            _edge.sprite = EdgeSprite(); _edge.type = Image.Type.Sliced; _edge.fillCenter = false;

            // status (bottom-left above the bars)
            _status = K.At("Status", _root, new Vector2(0, 0), S.statusOffset, new Vector2(520, 40));
            var air = K.At("Air", _status, new Vector2(0, 0), Vector2.zero, new Vector2(170, 34)); K.Image(air, new Color(0, 0, 0, 0.35f));
            _airDot = K.Image(K.At("Dot", air, new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(12, 12)), Green);
            _airText = K.Text(K.Stretch("T", air, 30, 0, 6, 0), "", 16, K.Ink); K.Shadow(_airText);

            _protW = K.At("Protection", _status, new Vector2(0, 0), new Vector2(178, 0), new Vector2(160, 34)); K.Image(_protW, new Color(0, 0, 0, 0.35f));
            _protText = K.Text(K.Stretch("T", _protW, 10, 10, 6, 0), "", 15, K.Ink); K.Shadow(_protText);
            var pb = K.At("Bar", _protW, new Vector2(0, 0), new Vector2(10, 5), new Vector2(140, 5)); K.Image(pb, new Color(0, 0, 0, 0.5f));
            _protFill = K.Image(K.Stretch("Fill", pb), Green); _protFill.rectTransform.pivot = new Vector2(0, 0.5f);

            _bottleW = K.At("Bottle", _status, new Vector2(0, 0), new Vector2(346, 0), new Vector2(150, 34)); K.Image(_bottleW, new Color(0, 0, 0, 0.35f));
            _bottleText = K.Text(K.Stretch("T", _bottleW, 10, 10, 6, 0), "", 15, K.Ink); K.Shadow(_bottleText);
            var bb = K.At("Bar", _bottleW, new Vector2(0, 0), new Vector2(10, 5), new Vector2(130, 5)); K.Image(bb, new Color(0, 0, 0, 0.5f));
            _bottleFill = K.Image(K.Stretch("Fill", bb), Water);

            // countdown above the hotbar
            _timer = K.At("WornTimer", _root, new Vector2(0.5f, 0), S.timerOffset, new Vector2(196, 46));
            _timerBack = K.Image(_timer, new Color(0, 0, 0, 0.42f));
            var ib = K.At("IconBox", _timer, new Vector2(0, 0.5f), new Vector2(5, 0), new Vector2(36, 36)); K.Image(ib, new Color(0, 0, 0, 0.35f));
            _timerIcon = K.Image(K.Stretch("Icon", ib, 2, 2, 2, 2), Color.white); _timerIcon.preserveAspect = true;
            _timerAbbr = K.Text(K.Stretch("Abbr", ib), "", 14, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _timerText = K.Text(K.Stretch("Time", _timer, 48, 0, 8, 12), "", 24, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); K.Shadow(_timerText);
            _timerLabel = K.Text(K.Stretch("Label", _timer, 48, 2, 8, 28), "", 12, K.Dim, TextAnchor.LowerLeft); K.Shadow(_timerLabel);
            _timer.gameObject.SetActive(false);
        }

        bool HudHidden()
        {
            if (!_hud) _hud = FindAnyObjectByType<ASHHud>();
            if (ASHDeathScreen.Active || ASHUIState.IsOpen(ASHPanels.Pause) || ASHUIState.IsOpen(ASHPanels.Map) || ASHUIState.IsOpen(ASHPanels.Death) || ASHUIState.IsOpen("manualCover")) return true;
            if (_hud && _hud.Journal && _hud.Journal.IsOpen) return true;
            if (_hud && _hud.Root && !_hud.Root.gameObject.activeInHierarchy) return true;
            return false;
        }

        void Update()
        {
            if (!_exp) { Destroy(gameObject); return; }
            if (!_face) _face = _exp.GetComponent<ASHFaceProtection>();
            bool hidden = HudHidden();
            _status.anchoredPosition = S.statusOffset; _timer.anchoredPosition = S.timerOffset;

            // red edge
            float target = _exp.Warning ? S.edgeMaxAlpha * (0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * S.edgePulseSpeed * Mathf.PI)) : 0f;
            _edgeA = _exp.Warning ? target : Mathf.MoveTowards(_edgeA, 0f, Time.unscaledDeltaTime * 0.6f);
            _edge.color = new Color(0.85f, 0.05f, 0.03f, hidden ? 0f : _edgeA);
            _edge.pixelsPerUnitMultiplier = 63f / Mathf.Max(10f, S.edgeWidth);

            _status.gameObject.SetActive(!hidden);
            if (!hidden) UpdateStatus();
            UpdateTimer(hidden);
        }

        void UpdateStatus()
        {
            float e = _exp.Exposure;
            if (_exp.Sealing >= 0.99f) { _airDot.color = Green; _airText.text = "SHELTERED"; }
            else if (!_exp.AirBad) { _airDot.color = K.Dim; _airText.text = _exp.Sealing > 0f ? "SHELTER  air ok" : "AIR  clear"; }
            else
            {
                string arrows = e >= 0.66f ? "▼▼▼" : e >= 0.33f ? "▼▼" : e > S.exposureDeadzone ? "▼" : "";
                var col = e >= 0.66f ? Red : e >= 0.2f ? Amber : new Color(0.95f, 0.85f, 0.5f);
                if (!_exp.Draining) col = Green;
                _airDot.color = col;
                string where = _exp.Sealing > 0f ? "LEAKY" : "ASH";
                _airText.text = _exp.Draining ? $"{where}  <color=#{ColorUtility.ToHtmlStringRGB(col)}>{arrows}</color>" : $"{where}  filtered";
            }
            if (_exp.Warning) _airDot.color = Color.Lerp(Red, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));

            bool worn = _face && _face.Worn != ASHFaceProtection.Kind.None;
            _protW.gameObject.SetActive(worn);
            if (worn)
            {
                bool mask = _face.Worn == ASHFaceProtection.Kind.Mask;
                _protText.text = mask ? (_face.FilterHours > 0f ? "GAS MASK" : "GAS MASK  <color=#ff5c4d>no filter</color>") : "WET RAG";
                float d = _face.Durability01;
                _protFill.rectTransform.anchorMax = new Vector2(d, 1); _protFill.rectTransform.offsetMin = _protFill.rectTransform.offsetMax = Vector2.zero;
                _protFill.color = TimerColor(Green);
            }
            bool bottle = _face && _face.HasBottle;
            _bottleW.gameObject.SetActive(bottle);
            if (bottle)
            {
                float f = _face.BottleFill;
                _bottleText.text = $"WATER  {Mathf.RoundToInt(f * 100f)}%" + (_face.BottleDirty ? "  <color=#c9a46a>dirty</color>" : "");
                _bottleFill.rectTransform.anchorMax = new Vector2(f, 1); _bottleFill.rectTransform.offsetMin = _bottleFill.rectTransform.offsetMax = Vector2.zero;
                _bottleFill.color = f * 100f + 0.01f < S.sipPercent ? Red : Water;
            }
        }

        Color TimerColor(Color normal)
        {
            if (!_face || _face.Worn == ASHFaceProtection.Kind.None) return normal;
            float rem = _face.RemainingHours;
            if (_face.Worn == ASHFaceProtection.Kind.Mask && rem <= 0f) return Red;
            if (rem * 60f <= S.timerRedMinutes) return Red;
            if (_face.Low) return Amber;
            return normal;
        }

        void UpdateTimer(bool hidden)
        {
            bool show = !hidden && _face && _face.Worn != ASHFaceProtection.Kind.None;
            if (_timer.gameObject.activeSelf != show) _timer.gameObject.SetActive(show);
            if (!show) return;
            bool mask = _face.Worn == ASHFaceProtection.Kind.Mask;
            if (_iconKind != _face.Worn)
            {
                _iconKind = _face.Worn;
                var it = ASHItemRegistry.I.Find(mask ? ASHFaceProtection.FilterId : ASHFaceProtection.WetRagId);
                if (mask && (!it || !it.icon)) { var m = ASHItemRegistry.I.Find(ASHFaceProtection.MaskId); if (m && m.icon) it = m; }
                _timerIcon.enabled = it && it.icon; if (_timerIcon.enabled) _timerIcon.sprite = it.icon;
                _timerAbbr.text = _timerIcon.enabled ? "" : mask ? "GM" : "WR";
            }
            var col = TimerColor(K.Ink);
            float rem = _face.RemainingHours;
            _timerText.text = mask && rem <= 0f ? "NO FILTER" : ASHGameTime.Format(rem);
            _timerText.fontSize = mask && rem <= 0f ? 18 : 24;
            _timerText.color = col;
            _timerLabel.text = mask ? "FILTER" : "WET RAG";
            _timerLabel.color = col == K.Ink ? K.Dim : col;
            bool urgent = col == Red;
            _timerBack.color = urgent ? new Color(0.35f, 0.04f, 0.03f, 0.45f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f)) : new Color(0, 0, 0, 0.42f);
        }
    }
}
