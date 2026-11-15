using System.Collections.Generic;
using ASH.Map;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>
    /// Full-screen damaged-GPS map: hand-drawn region art under a fog-of-war overlay, mouse-wheel zoom toward the
    /// cursor, drag to pan (clamped), approximate position (drifting uncertainty circle, never an exact dot), heading
    /// compass and survey-point markers. Zoom/pan approach follows Genesis MapUI (full-map zoom + viewport pan handler).
    /// </summary>
    public sealed class ASHMapUI : MonoBehaviour
    {
        float _cover, _coverAt;
        [Range(1f, 4f)] public float minZoom = 1f;
        [Range(2f, 20f)] public float maxZoom = 9f;
        [Range(1f, 8f)] public float openZoom = 3f;
        [Range(0.02f, 0.5f)] public float wheelStep = 0.15f;

        ASHGpsMap _gps;
        RectTransform _viewport, _content, _markers, _ring, _ringFill, _needle;
        RawImage _mapImg, _fogImg, _scan;
        Text _header, _status, _here;
        readonly List<(ASHSurveyPointDef def, Text icon)> _surveyIcons = new List<(ASHSurveyPointDef, Text)>();
        float _zoom = 3f; Vector2 _pan; float _baseSize;

        public bool IsOpen => gameObject.activeSelf;
        public float Zoom => _zoom;

        public static ASHMapUI Create(RectTransform parent, ASHGpsMap gps)
        {
            var rt = K.Stretch("GPSMap", parent);
            K.Image(rt, new Color(0, 0, 0, 0.72f), true);
            var ui = rt.gameObject.AddComponent<ASHMapUI>();
            ui._gps = gps;
            ui.Build(rt);
            rt.gameObject.SetActive(false);
            return ui;
        }

        void Build(RectTransform rt)
        {
            // device bezel
            var bezel = K.Stretch("Bezel", rt, 110, 50, 110, 50); K.Image(bezel, new Color(0.13f, 0.13f, 0.12f, 1f));
            var inner = K.Stretch("Inner", bezel, 14, 14, 14, 14); K.Image(inner, new Color(0.05f, 0.055f, 0.05f, 1f));
            _header = K.Text(K.At("Header", bezel, new Vector2(0, 1), new Vector2(26, -2), new Vector2(900, 30)), "", 17, new Color(0.62f, 0.82f, 0.62f), TextAnchor.MiddleLeft, FontStyle.Bold);
            _status = K.Text(K.At("Status", bezel, new Vector2(1, 1), new Vector2(-26, -2), new Vector2(700, 30)), "", 17, new Color(0.62f, 0.82f, 0.62f), TextAnchor.MiddleRight);
            K.Text(K.At("Footer", bezel, new Vector2(0.5f, 0), new Vector2(0, -2), new Vector2(1000, 26)), "Wheel zoom   ·   Drag pan   ·   Space re-centre   ·   M / Esc close", 15, K.Dim, TextAnchor.MiddleCenter);

            _viewport = K.Stretch("Viewport", inner, 6, 24, 6, 24);
            var vpImg = K.Image(_viewport, new Color(0.08f, 0.09f, 0.08f, 1f), true);
            _viewport.gameObject.AddComponent<RectMask2D>();
            _viewport.gameObject.AddComponent<ASHMapPanHandler>().Initialize(d => { _pan += d; Clamp(); Apply(); });

            _content = K.Rect("Content", _viewport); _content.anchorMin = _content.anchorMax = _content.pivot = new Vector2(0.5f, 0.5f);
            _mapImg = K.Stretch("Map", _content).gameObject.AddComponent<RawImage>(); _mapImg.raycastTarget = false;
            if (_gps && _gps.Region) _mapImg.texture = _gps.Region.mapTexture;
            _fogImg = K.Stretch("Fog", _content).gameObject.AddComponent<RawImage>(); _fogImg.raycastTarget = false;
            _markers = K.Stretch("Markers", _content);

            _ringFill = K.At("RingFill", _markers, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40)); var rf = K.Image(_ringFill, new Color(1f, 0.62f, 0.25f, 0.16f)); rf.sprite = K.Dot;
            _ring = K.At("Ring", _markers, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40)); var ri = K.Image(_ring, new Color(1f, 0.62f, 0.25f, 0.95f)); ri.sprite = K.Ring;
            _here = K.Text(K.At("Here", _markers, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260, 24)), "approx. position", 15, new Color(1f, 0.75f, 0.45f), TextAnchor.MiddleLeft, FontStyle.Bold);
            K.Shadow(_here);
            if (_gps && _gps.Region)
                foreach (var s in _gps.Region.surveys)
                {
                    var t = K.Text(K.At("Survey_" + s.surveyId, _markers, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30)), "▲", 22, K.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                    K.Shadow(t); _surveyIcons.Add((s, t));
                }

            // scanlines over the viewport (damaged display)
            _scan = K.Stretch("Scanlines", _viewport).gameObject.AddComponent<RawImage>(); _scan.raycastTarget = false;
            _scan.texture = ScanTexture(); _scan.color = new Color(1, 1, 1, 0.10f);

            // compass (heading)
            var comp = K.At("Compass", inner, new Vector2(1, 0), new Vector2(-30, 40), new Vector2(150, 150));
            var cbg = K.Image(comp, new Color(0, 0, 0, 0.55f)); cbg.sprite = K.Dot;
            var cring = K.Image(K.Stretch("Ring", comp, 4, 4, 4, 4), new Color(0.9f, 0.85f, 0.75f, 0.8f)); cring.sprite = K.Ring;
            string[] dirs = { "N", "E", "S", "W" };
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                var l = K.Text(K.At(dirs[i], comp, new Vector2(0.5f, 0.5f), new Vector2(Mathf.Sin(a) * 52f, Mathf.Cos(a) * 52f), new Vector2(30, 30)), dirs[i], i == 0 ? 22 : 17, i == 0 ? K.Bad : K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            _needle = K.At("Needle", comp, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 84));
            K.Image(K.At("Fwd", _needle, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(8, 42)), K.Accent);
            K.Image(K.At("Back", _needle, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(6, 40)), new Color(0.8f, 0.78f, 0.72f, 0.7f));
            K.Text(K.At("Label", comp, new Vector2(0.5f, 0), new Vector2(0, -24), new Vector2(160, 22)), "HEADING", 13, K.Dim, TextAnchor.MiddleCenter);
        }

        static Texture2D _scanTex;
        static Texture2D ScanTexture()
        {
            if (_scanTex) return _scanTex;
            _scanTex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[16];
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) px[y * 4 + x] = y == 0 ? new Color32(0, 0, 0, 255) : new Color32(0, 0, 0, 0);
            _scanTex.SetPixels32(px); _scanTex.Apply();
            return _scanTex;
        }

        public void Open()
        {
            gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            _baseSize = Mathf.Min(_viewport.rect.width, _viewport.rect.height);
            if (_baseSize < 10f) _baseSize = 900f;
            _zoom = Mathf.Clamp(openZoom, minZoom, maxZoom);
            Recenter();
        }
        public void Close() => gameObject.SetActive(false);

        public void Recenter()
        {
            if (_gps == null || _gps.Region == null) return;
            var uv = _gps.Region.WorldToUV(_gps.ApproxPosition(out _));
            _pan = -(uv - new Vector2(0.5f, 0.5f)) * _baseSize * _zoom;
            Clamp(); Apply();
        }

        /// <summary>Zooms by a factor keeping the content point under 'viewportLocal' fixed.</summary>
        public void ZoomAt(Vector2 viewportLocal, float factor)
        {
            float nz = Mathf.Clamp(_zoom * factor, minZoom, maxZoom);
            if (Mathf.Approximately(nz, _zoom)) return;
            Vector2 p = (viewportLocal - _pan) / _zoom;
            _zoom = nz; _pan = viewportLocal - p * _zoom;
            Clamp(); Apply();
        }

        void Clamp()
        {
            float size = _baseSize * _zoom;
            float mx = Mathf.Max(0f, (size - _viewport.rect.width) * 0.5f), my = Mathf.Max(0f, (size - _viewport.rect.height) * 0.5f);
            _pan.x = Mathf.Clamp(_pan.x, -mx, mx); _pan.y = Mathf.Clamp(_pan.y, -my, my);
        }

        void Apply()
        {
            float size = _baseSize * _zoom;
            _content.sizeDelta = new Vector2(size, size);
            _content.anchoredPosition = _pan;
        }

        Vector2 UVToContent(Vector2 uv) => (uv - new Vector2(0.5f, 0.5f)) * _baseSize * _zoom;

        void Update()
        {
            if (_gps == null || _gps.Region == null) return;
            if (_fogImg.texture == null && _gps.Fog != null) _fogImg.texture = _gps.Fog.Texture;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f && RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, mouse.position.ReadValue(), null, out var local)
                    && _viewport.rect.Contains(local))
                    ZoomAt(local - _viewport.rect.center, wheel > 0 ? 1f + wheelStep : 1f / (1f + wheelStep));
            }
            var kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame) Recenter();

            float size = _baseSize * _zoom;
            var pos = _gps.ApproxPosition(out float unc);
            var uv = _gps.Region.WorldToUV(pos);
            float ringPx = Mathf.Max(18f, unc * 2f / _gps.Region.worldSize.x * size);
            float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 3f);
            _ring.anchoredPosition = _ringFill.anchoredPosition = UVToContent(uv);
            _ring.sizeDelta = Vector2.one * ringPx * pulse; _ringFill.sizeDelta = Vector2.one * ringPx;
            _here.rectTransform.anchoredPosition = UVToContent(uv) + new Vector2(ringPx * 0.5f + 136f, 0f);

            foreach (var (def, icon) in _surveyIcons)
            {
                bool logged = _gps.IsLogged(def);
                bool seen = logged || (_gps.Fog != null && _gps.Fog.IsRevealed(def.worldXZ, 0.5f));
                icon.enabled = seen;
                icon.color = logged ? K.Good : K.Accent;
                icon.rectTransform.anchoredPosition = UVToContent(_gps.Region.WorldToUV(def.worldXZ));
            }

            // fog flicker + scanline crawl
            // flicker only the brightness: unexplored fog stays solid at the region's fogOpacity (default 1)
            float fl = Mathf.PerlinNoise(Time.unscaledTime * 2.3f, 0.7f);
            var reg = _gps.Region; float flick = reg ? reg.fogFlicker : 0.1f, b = 1f - flick + flick * fl;
            _fogImg.color = new Color(b, b, b, reg ? reg.fogOpacity : 1f);
            float lines = _viewport.rect.height / 3f;
            _scan.uvRect = new Rect(0, Time.unscaledTime * 0.6f % 1f, 1, lines / 4f);

            var cam = Camera.main;
            if (cam) _needle.localEulerAngles = new Vector3(0, 0, -cam.transform.eulerAngles.y);

            if (_gps.Fog != null && Time.unscaledTime >= _coverAt) { _cover = _gps.Fog.Coverage01(); _coverAt = Time.unscaledTime + 0.5f; }
            float cover = _cover;
            _header.text = "TRAILMATE 40 · SIGNAL DEGRADED · MAP DATA CORRUPT";
            _status.text = $"SURVEYS {_gps.LoggedCount}/{_gps.SurveyCount}   ·   MAP {cover * 100f:0}%   ·   FIX ±{unc:0} m   ·   x{_zoom:0.0}";
        }
    }
}
