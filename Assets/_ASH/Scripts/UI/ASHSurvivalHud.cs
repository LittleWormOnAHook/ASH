using ASH.Core;
using ASH.Inventory;
using ASH.Survival;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>
    /// Survival HUD additions (code-built uGUI like the main HUD): a bottom-left column above the bars (temperature, worn
    /// rag/mask durability, air/shelter status; the bottle fill shows only in the inventory panels), the worn rag / filter countdown in game time above the hotbar (icon + h:mm; amber in the
    /// rag's last 30 min / low filter, red near empty; hidden when nothing is worn) and the pulsing red screen edge at low health
    /// while exposed. Positions: ASHExposureSettings (HUD).
    /// </summary>
    public sealed class ASHSurvivalHud : MonoBehaviour
    {
        static readonly Color Amber = new Color(1f, 0.72f, 0.3f), Red = new Color(1f, 0.36f, 0.3f), Green = new Color(0.62f, 0.83f, 0.6f), Water = new Color(0.4f, 0.66f, 0.9f);

        ASHExposure _exp; ASHFaceProtection _face; ASHHud _hud;
        const float StatusW = 170f, RowH = 34f, RowGap = 6f, TimerH = 42f;
        RectTransform _root, _status, _timer, _protW, _airW;
        Text _airText, _protText, _timerText, _timerLabel, _timerAbbr; Image _protFill, _timerIcon, _timerBack, _edge, _airDot;
        RectTransform _tempW; Text _tempText; Image _tempFill, _tempBack, _frost; float _frostA;
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

        static Sprite _frostSprite; static Texture2D _frostTex; static int _frostW, _frostH; static float _frostEdge;

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++) { sum += amp * Mathf.PerlinNoise(x * freq, y * freq); norm += amp; amp *= 0.5f; freq *= 2.03f; }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Fullscreen organic frost (no 9-slice — slicing a noisy edge stretches it into streaks).</summary>
        static Sprite FrostSprite(float edgeWidth)
        {
            const int w = 1280, h = 720;
            if (_frostSprite && _frostW == w && _frostH == h && Mathf.Abs(_frostEdge - edgeWidth) < 0.5f) return _frostSprite;
            _frostW = w; _frostH = h; _frostEdge = edgeWidth;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ASH_FrostVignette" };
            var px = new Color32[w * h];
            float edgePx = Mathf.Max(24f, edgeWidth * (h / 1080f));
            float aspect = w / (float)h;
            float corner = edgePx * 0.65f;
            for (int y = 0; y < h; y++)
            {
                float ny = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w;
                    float dx = Mathf.Min(x + 0.5f, w - 0.5f - x);
                    float dy = Mathf.Min(y + 0.5f, h - 0.5f - y);
                    // rounded-rect distance from the frame (circular in the corners, no hard 45° ridges)
                    float dEdge;
                    if (dx < corner && dy < corner)
                    {
                        float cx = corner - dx, cy = corner - dy;
                        dEdge = corner - Mathf.Sqrt(cx * cx + cy * cy);
                    }
                    else dEdge = Mathf.Min(dx, dy);
                    float warp = (Fbm(nx * 5.2f + 2.1f, ny * 5.2f * aspect + 0.7f, 4) - 0.5f) * edgePx * 0.5f;
                    float d = dEdge + warp;
                    float fall = 1f - Mathf.SmoothStep(0f, edgePx, Mathf.Max(0f, d));
                    float grain = Fbm(nx * 22f, ny * 22f * aspect, 3);
                    float crystals = Mathf.Pow(Mathf.Clamp01(Fbm(nx * 36f + 3.4f, ny * 36f * aspect + 8.1f, 2)), 8f) * fall;
                    float a = Mathf.Clamp01(fall * (0.4f + 0.6f * grain) + crystals * 0.7f);
                    a = Mathf.SmoothStep(0f, 1f, a);
                    byte tint = (byte)(198 + 42 * grain);
                    px[y * w + x] = new Color32((byte)(tint - 20), (byte)(tint - 4), 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px); tex.Apply(false, false);
            if (_frostTex) Object.Destroy(_frostTex);
            if (_frostSprite) Object.Destroy(_frostSprite);
            _frostTex = tex;
            _frostSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return _frostSprite;
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
            var fr = K.Stretch("FrostEdge", _root); _frost = K.Image(fr, new Color(1, 1, 1, 0f));
            _frost.sprite = FrostSprite(ASHTemperatureSettings.I.frostEdgeWidth); _frost.type = Image.Type.Simple; _frost.preserveAspect = false; _frost.raycastTarget = false;

            // status: a column bottom-left above the bars. Temperature stays at the bottom (its old spot); the worn rag/mask and
            // the air/shelter indicator stack above it (hidden rows collapse). The bottle is shown only in the inventory panels.
            _status = K.At("Status", _root, new Vector2(0, 0), S.statusOffset, new Vector2(StatusW, 120));
            _tempW = K.At("Temperature", _status, new Vector2(0, 0), Vector2.zero, new Vector2(StatusW, RowH)); _tempBack = K.Image(_tempW, new Color(0, 0, 0, 0.35f));
            _tempText = K.Text(K.Stretch("T", _tempW, 10, 10, 6, 0), "", 15, K.Ink); K.Shadow(_tempText);
            var tb = K.At("Bar", _tempW, new Vector2(0, 0), new Vector2(10, 5), new Vector2(StatusW - 20, 5)); K.Image(tb, new Color(0, 0, 0, 0.5f));
            _tempFill = K.Image(K.Stretch("Fill", tb), Green);

            _protW = K.At("Protection", _status, new Vector2(0, 0), new Vector2(0, RowH + RowGap), new Vector2(StatusW, RowH)); K.Image(_protW, new Color(0, 0, 0, 0.35f));
            _protText = K.Text(K.Stretch("T", _protW, 10, 10, 6, 0), "", 15, K.Ink); K.Shadow(_protText);
            var pb = K.At("Bar", _protW, new Vector2(0, 0), new Vector2(10, 5), new Vector2(StatusW - 20, 5)); K.Image(pb, new Color(0, 0, 0, 0.5f));
            _protFill = K.Image(K.Stretch("Fill", pb), Green); _protFill.rectTransform.pivot = new Vector2(0, 0.5f);

            _airW = K.At("Air", _status, new Vector2(0, 0), new Vector2(0, (RowH + RowGap) * 2), new Vector2(StatusW, RowH)); K.Image(_airW, new Color(0, 0, 0, 0.35f));
            _airDot = K.Image(K.At("Dot", _airW, new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(12, 12)), Green);
            _airText = K.Text(K.Stretch("T", _airW, 30, 0, 6, 0), "", 16, K.Ink); K.Shadow(_airText);

            // worn rag / mask countdown: a row of the status column, stacked right above the air / SHELTERED row (t346: no panel,
            // icon left, time on top, label on its own line underneath)
            _timer = K.At("WornTimer", _status, new Vector2(0, 0), new Vector2(0, (RowH + RowGap) * 3), new Vector2(StatusW, TimerH));
            _timerBack = K.Image(_timer, Color.clear); _timerBack.raycastTarget = false;
            var ib = K.At("IconBox", _timer, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(36, 36));
            _timerIcon = K.Image(K.Stretch("Icon", ib, 2, 2, 2, 2), Color.white); _timerIcon.preserveAspect = true;
            _timerAbbr = K.Text(K.Stretch("Abbr", ib), "", 14, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _timerText = K.Text(K.Stretch("Time", _timer, 50, 15, 4, 0), "", 22, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); K.Shadow(_timerText);
            _timerText.horizontalOverflow = HorizontalWrapMode.Overflow; _timerText.verticalOverflow = VerticalWrapMode.Overflow;
            _timerLabel = K.Text(K.Stretch("Label", _timer, 50, 0, 4, TimerH - 15), "", 13, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); K.Shadow(_timerLabel);
            _timerLabel.horizontalOverflow = HorizontalWrapMode.Overflow; _timerLabel.verticalOverflow = VerticalWrapMode.Overflow;
            { var o = _timerLabel.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.85f); o.effectDistance = new Vector2(1f, -1f); }
            { var o = _timerText.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.7f); o.effectDistance = new Vector2(1f, -1f); }
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
            _status.anchoredPosition = S.statusOffset;

            // red edge
            float target = _exp.Warning ? S.edgeMaxAlpha * (0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * S.edgePulseSpeed * Mathf.PI)) : 0f;
            _edgeA = _exp.Warning ? target : Mathf.MoveTowards(_edgeA, 0f, Time.unscaledDeltaTime * 0.6f);
            _edge.color = new Color(0.85f, 0.05f, 0.03f, hidden ? 0f : _edgeA);
            _edge.pixelsPerUnitMultiplier = 63f / Mathf.Max(10f, S.edgeWidth);

            // frost (body temperature)
            var bt = ASHBodyTemperature.Instance; var ts = ASHTemperatureSettings.I;
            float ft = bt ? bt.Frost * ts.frostMaxAlpha : 0f;
            _frostA = Mathf.MoveTowards(_frostA, ft, Time.unscaledDeltaTime * 0.35f);
            if (_frost.sprite != FrostSprite(ts.frostEdgeWidth)) _frost.sprite = FrostSprite(ts.frostEdgeWidth);
            _frost.color = new Color(1, 1, 1, hidden ? 0f : _frostA);

            _status.gameObject.SetActive(!hidden);
            if (!hidden) UpdateStatus();
            UpdateTimer(hidden);
        }

        void UpdateStatus()
        {
            UpdateTemperature();
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
            // stack: temperature (bottom), protection (if worn), air/shelter
            float y = _tempW.gameObject.activeSelf ? RowH + RowGap : 0f;
            if (worn) { _protW.anchoredPosition = new Vector2(0, y); y += RowH + RowGap; }
            _airW.anchoredPosition = new Vector2(0, y);
            _timer.anchoredPosition = new Vector2(0, y + RowH + RowGap); // directly above the air / SHELTERED row
        }

        static readonly Color Cold = new Color(0.55f, 0.78f, 1f), Freezing = new Color(0.4f, 0.6f, 1f);
        void UpdateTemperature()
        {
            var bt = ASHBodyTemperature.Instance; var ts = ASHTemperatureSettings.I;
            _tempW.gameObject.SetActive(bt);
            if (!bt) return;
            float w = bt.Warmth, tr = bt.Trend, dz = ts.warmthTrendDeadzone;
            string arrow = tr > dz * 10f ? " <color=#ffb070>▲▲</color>" : tr > dz ? " <color=#ffb070>▲</color>" : tr < -dz * 10f ? " <color=#8cc4ff>▼▼</color>" : tr < -dz ? " <color=#8cc4ff>▼</color>" : "";
            bool cold = bt.Cold;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * ts.pulseSpeed);
            var col = cold ? Color.Lerp(Freezing, Color.white, pulse) : w < ts.normalWarmth - 0.5f ? Cold : K.Ink;
            _tempText.text = $"TEMP  <color=#{ColorUtility.ToHtmlStringRGB(col)}>{ts.Format(w)}</color>{arrow}";
            float f = Mathf.InverseLerp(ts.minWarmth, ts.normalWarmth, w);
            _tempFill.rectTransform.anchorMax = new Vector2(f, 1); _tempFill.rectTransform.offsetMin = _tempFill.rectTransform.offsetMax = Vector2.zero;
            _tempFill.color = cold ? Color.Lerp(Freezing, Color.white, pulse * 0.6f) : w < ts.normalWarmth - 0.5f ? Cold : new Color(0.95f, 0.62f, 0.28f);
            // the whole meter pulses at/below the cold threshold
            _tempBack.color = cold ? new Color(0.1f, 0.2f, 0.45f, 0.35f + 0.3f * pulse) : new Color(0, 0, 0, 0.35f);
            _tempW.localScale = Vector3.one * (cold ? 1f + 0.04f * pulse : 1f);
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
            _timerLabel.color = col == K.Ink ? new Color(0.85f, 0.85f, 0.85f) : col;
            _timerBack.color = Color.clear; // no panel for now (t346)
        }
    }
}
