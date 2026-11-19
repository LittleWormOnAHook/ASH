using System.Collections.Generic;
using ASH.Core;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Map;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    public enum ASHCompassMarkerKind { Poi, Survey, Campfire, Custom, Enemy }

    /// <summary>A registered compass marker. Call <see cref="Unregister"/> (or <see cref="ASHCompass.Unregister"/>) to remove it.</summary>
    public sealed class ASHCompassMarker
    {
        public string Id;
        public ASHCompassMarkerKind Kind;
        public Color Color;
        public Transform Follow;
        public Vector3 WorldPosition;
        public bool Visible = true;
        internal bool Auto;
        public Vector3 Position => Follow ? Follow.position : WorldPosition;
        public void Unregister() => ASHCompass.Unregister(this);
    }

    /// <summary>
    /// Top-centre scrolling compass (uGUI, built at runtime by the HUD). Cardinals and ticks follow camera yaw
    /// (+Z = north, +X = east, heading clockwise — same as the GPS map needle). Known POIs, revealed surveys and
    /// campfires show as blue dots when the GPS is carried (toggle on ASH_CompassSettings). Enemies and other
    /// markers register through <see cref="Register(Transform, ASHCompassMarkerKind, Color, string)"/>.
    /// </summary>
    public sealed class ASHCompass : MonoBehaviour
    {
        public static ASHCompass Instance { get; private set; }

        const float PointerWidth = 10f;
        const string AutoPoi = "auto:poi:";
        const string AutoSurvey = "auto:survey:";
        const string AutoFire = "auto:campfire:";

        ASHInventory _inventory;
        ASHGpsMap _gps;
        RectTransform _root, _strip, _tickLayer, _markerLayer;
        CanvasGroup _group;
        bool _visible = true;
        Text _heading;
        readonly List<TickEntry> _ticks = new List<TickEntry>();
        readonly Dictionary<string, ASHCompassMarker> _markers = new Dictionary<string, ASHCompassMarker>();
        readonly Dictionary<string, MarkerView> _views = new Dictionary<string, MarkerView>();
        readonly HashSet<string> _seen = new HashSet<string>();
        readonly List<string> _stale = new List<string>();
        bool _built;
        float _syncAt;
        int _autoSerial;

        struct TickEntry
        {
            public float Angle;
            public RectTransform Rect;
            public Text Label;
            public Image Line;
            public bool Cardinal;
            public bool Intercardinal;
        }

        struct MarkerView
        {
            public RectTransform Rect;
            public Image Icon;
            public Text Distance;
        }

        static ASHCompassSettings S => ASHCompassSettings.I;

        public static ASHCompass Ensure(RectTransform hudRoot, ASHInventory inventory = null, ASHGpsMap gps = null)
        {
            if (!hudRoot) return Instance;
            ASHCompass c = Instance;
            if (c && c._built && c.transform.parent == hudRoot)
            {
                if (inventory) c._inventory = inventory;
                if (gps) c._gps = gps;
                return c;
            }
            var existing = hudRoot.Find("CompassHud");
            if (existing)
            {
                c = existing.GetComponent<ASHCompass>();
                if (!c) c = existing.gameObject.AddComponent<ASHCompass>();
            }
            else
            {
                var go = new GameObject("CompassHud", typeof(RectTransform));
                c = go.AddComponent<ASHCompass>();
            }
            c._inventory = inventory;
            c._gps = gps ? gps : ASHGpsMap.Instance;
            c.Build(hudRoot);
            c.gameObject.SetActive(true);
            return c;
        }

        /// <summary>Follow a transform (enemies, moving targets). Color default uses the kind's settings colour.</summary>
        public static ASHCompassMarker Register(Transform follow, ASHCompassMarkerKind kind, Color color = default, string id = null)
        {
            var m = Upsert(id, kind, color);
            m.Follow = follow;
            if (follow) m.WorldPosition = follow.position;
            return m;
        }

        /// <summary>A fixed world position (POIs, waypoints).</summary>
        public static ASHCompassMarker Register(Vector3 worldPosition, ASHCompassMarkerKind kind, Color color = default, string id = null)
        {
            var m = Upsert(id, kind, color);
            m.Follow = null;
            m.WorldPosition = worldPosition;
            return m;
        }

        public static void Unregister(ASHCompassMarker marker)
        {
            if (marker == null) return;
            if (!string.IsNullOrEmpty(marker.Id)) Unregister(marker.Id);
            else if (Instance) Instance.RemoveMarker(marker);
        }

        public static void Unregister(string id)
        {
            if (Instance == null || string.IsNullOrEmpty(id)) return;
            if (!Instance._markers.TryGetValue(id, out var m)) return;
            Instance.RemoveMarker(m);
        }

        static ASHCompassMarker Upsert(string id, ASHCompassMarkerKind kind, Color color)
        {
            var c = Instance;
            if (c == null)
            {
                var hud = Object.FindAnyObjectByType<ASHHud>();
                if (hud && hud.Root) c = Ensure(hud.Root);
            }
            if (c == null)
            {
                var go = new GameObject("CompassHud", typeof(RectTransform));
                c = go.AddComponent<ASHCompass>();
            }
            if (string.IsNullOrEmpty(id)) id = "reg:" + (++c._autoSerial);
            if (!c._markers.TryGetValue(id, out var m))
            {
                m = new ASHCompassMarker { Id = id };
                c._markers[id] = m;
            }
            m.Kind = kind;
            m.Color = color.a > 0f ? color : c.ColorFor(kind);
            m.Visible = true;
            m.Auto = false;
            return m;
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (!_built) return;
            if (_gps == null) _gps = ASHGpsMap.Instance;
            ApplyVisibility();
            if (!_visible) return;
            ApplyLayout();
            float heading = HeadingDegrees();
            RefreshTicks(heading);
            if (_heading)
            {
                _heading.enabled = S.showHeadingDegrees;
                if (S.showHeadingDegrees) _heading.text = Mathf.RoundToInt(Normalize(heading)).ToString("000");
            }
            if (Time.unscaledTime >= _syncAt)
            {
                SyncAutoMarkers();
                _syncAt = Time.unscaledTime + 0.2f;
            }
            RefreshMarkers(heading, PlayerPosition());
        }

        void ApplyVisibility()
        {
            // Never SetActive(false) on this GameObject: that would stop LateUpdate and the compass
            // would never come back after the intro / a journal / the pause menu. Hide with a CanvasGroup.
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            bool panels = S.hideWhenPanelsOpen && ASHUIState.BlocksGameplay;
            bool show = !panels && (S.compassAlwaysVisible || HasGps);
            if (!_group) _group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            if (_visible == show && _group.alpha == (show ? 1f : 0f)) return;
            _visible = show;
            _group.alpha = show ? 1f : 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            if (show && _root) _root.SetAsLastSibling();
        }

        bool HasGps => _inventory && _inventory.HasTool(ASHToolType.GPS);

        bool KindAllowed(ASHCompassMarkerKind kind)
        {
            bool needGps = kind == ASHCompassMarkerKind.Enemy ? S.enemyMarkersRequireGps : S.poiMarkersRequireGps;
            return !needGps || HasGps;
        }

        Color ColorFor(ASHCompassMarkerKind kind) =>
            kind == ASHCompassMarkerKind.Enemy ? S.enemyColor : S.poiColor;

        static float HeadingDegrees()
        {
            var cam = Camera.main;
            return cam ? cam.transform.eulerAngles.y : 0f;
        }

        Vector3 PlayerPosition()
        {
            if (_gps && _gps.Player) return _gps.Player.position;
            var cam = Camera.main;
            return cam ? cam.transform.position : Vector3.zero;
        }

        static float BearingTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) return 0f;
            return Normalize(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }

        static float Normalize(float degrees)
        {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        // ---------------------------------------------------------------- build
        void Build(RectTransform hudRoot)
        {
            _root = (RectTransform)transform;
            _root.SetParent(hudRoot, false);
            gameObject.name = "CompassHud";
            gameObject.SetActive(true);
            _group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 1f; _group.blocksRaycasts = false; _group.interactable = false;
            _visible = true;
            _root.SetAsLastSibling();
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            if (_root.GetComponent<CanvasRenderer>() == null && _ticks.Count > 0)
                ClearBuilt();
            if (!_built) BuildStrip();
            _built = true;
            ApplyLayout();
            if (_ticks.Count == 0) BuildTicks();
        }

        void ClearBuilt()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            _ticks.Clear();
            _views.Clear();
            _built = false;
        }

        void ApplyLayout()
        {
            float extra = PointerWidth * 0.2f + (S.showHeadingDegrees ? S.headingLabelHeight : 0f);
            _root.sizeDelta = new Vector2(S.width, S.stripHeight + S.pointerHeight + extra);
            _root.anchoredPosition = new Vector2(0f, -S.topInset);
            if (_strip) _strip.sizeDelta = new Vector2(0f, S.stripHeight);
        }

        void BuildStrip()
        {
            _strip = K.Rect("StripViewport", _root);
            _strip.anchorMin = new Vector2(0f, 1f);
            _strip.anchorMax = new Vector2(1f, 1f);
            _strip.pivot = new Vector2(0.5f, 1f);
            _strip.sizeDelta = new Vector2(0f, S.stripHeight);
            _strip.anchoredPosition = Vector2.zero;
            _strip.gameObject.AddComponent<RectMask2D>();

            // Four thin bars — border only, no fill over the world.
            float t = Mathf.Max(1f, S.borderThickness);
            Bar("BorderT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, t));
            Bar("BorderB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, t));
            Bar("BorderL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(t, 0f));
            Bar("BorderR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(t, 0f));

            _markerLayer = K.Rect("MarkerLayer", _strip);
            _markerLayer.anchorMin = _markerLayer.anchorMax = _markerLayer.pivot = new Vector2(0.5f, 1f);
            _markerLayer.sizeDelta = Vector2.zero;
            _markerLayer.anchoredPosition = new Vector2(0f, -3f);

            _tickLayer = K.Rect("TickLayer", _strip);
            _tickLayer.anchorMin = _tickLayer.anchorMax = new Vector2(0.5f, 0f);
            _tickLayer.pivot = new Vector2(0.5f, 0f);
            _tickLayer.sizeDelta = Vector2.zero;
            _tickLayer.anchoredPosition = new Vector2(0f, 3f);

            var pointer = K.At("Pointer", _root, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(PointerWidth, S.pointerHeight));
            pointer.pivot = new Vector2(0.5f, 1f);
            // Sit just under the strip; ApplyLayout may change strip height, so parent under root and offset in LateUpdate via this name.
            pointer.anchoredPosition = new Vector2(0f, -S.stripHeight);
            var pImg = K.Image(pointer, S.cardinalColor);
            pImg.sprite = TriangleSprite();
            pImg.raycastTarget = false;

            var head = K.At("Heading", _root, new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(64f, S.headingLabelHeight));
            head.pivot = new Vector2(0.5f, 1f);
            head.anchoredPosition = new Vector2(0f, -(S.stripHeight + S.pointerHeight));
            _heading = K.Text(head, "000", S.cardinalFontSize, S.headingColor, TextAnchor.UpperCenter, FontStyle.Bold);
            AddShadow(_heading);
        }

        void Bar(string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size)
        {
            var rt = K.Rect(name, _strip);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
            var img = K.Image(rt, S.borderColor);
            img.raycastTarget = false;
        }

        void BuildTicks()
        {
            var angles = new SortedSet<float>();
            float step = Mathf.Max(5f, S.tickIntervalDegrees);
            for (float a = 0f; a < 360f; a += step) angles.Add(Normalize(a));
            for (int i = 0; i < 8; i++) angles.Add(i * 45f);

            foreach (float angle in angles)
            {
                bool cardinal = Nearly(angle, 0f) || Nearly(angle, 90f) || Nearly(angle, 180f) || Nearly(angle, 270f);
                bool inter = !cardinal && (Nearly(angle, 45f) || Nearly(angle, 135f) || Nearly(angle, 225f) || Nearly(angle, 315f));
                var tick = K.Rect($"Tick_{angle:000}", _tickLayer);
                tick.anchorMin = tick.anchorMax = tick.pivot = new Vector2(0.5f, 0f);
                tick.sizeDelta = new Vector2(40f, S.stripHeight - 6f);
                tick.anchoredPosition = Vector2.zero;

                var lineRt = K.At("Line", tick, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(2f, cardinal ? S.cardinalTickHeight : S.minorTickHeight));
                lineRt.pivot = new Vector2(0.5f, 0f);
                var line = K.Image(lineRt, cardinal ? S.cardinalColor : S.tickColor);

                Text label = null;
                if (cardinal || inter)
                {
                    var labRt = K.At("Label", tick, new Vector2(0.5f, 0f), new Vector2(0f, (cardinal ? S.cardinalTickHeight : S.minorTickHeight) + 2f), new Vector2(40f, 18f));
                    labRt.pivot = new Vector2(0.5f, 0f);
                    var color = Nearly(angle, 0f) ? S.northColor : (cardinal ? S.cardinalColor : S.intercardinalColor);
                    label = K.Text(labRt, CompassLabel(angle), cardinal ? S.cardinalFontSize : S.intercardinalFontSize, color, TextAnchor.LowerCenter, cardinal ? FontStyle.Bold : FontStyle.Normal);
                    AddShadow(label);
                }

                _ticks.Add(new TickEntry
                {
                    Angle = angle,
                    Rect = tick,
                    Label = label,
                    Line = line,
                    Cardinal = cardinal,
                    Intercardinal = inter
                });
            }
        }

        static bool Nearly(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a, b)) < 0.01f;

        static string CompassLabel(float angle)
        {
            int a = Mathf.RoundToInt(Normalize(angle));
            switch (a)
            {
                case 0: return "N";
                case 45: return "NE";
                case 90: return "E";
                case 135: return "SE";
                case 180: return "S";
                case 225: return "SW";
                case 270: return "W";
                case 315: return "NW";
                default: return "";
            }
        }

        void RefreshTicks(float heading)
        {
            float halfFov = Mathf.Max(1f, S.fieldOfViewDegrees * 0.5f);
            float halfWidth = _strip.rect.width * 0.5f;
            var pointer = _root.Find("Pointer") as RectTransform;
            if (pointer)
            {
                pointer.sizeDelta = new Vector2(PointerWidth, S.pointerHeight);
                pointer.anchoredPosition = new Vector2(0f, -S.stripHeight);
            }
            var head = _heading ? _heading.rectTransform : null;
            if (head) head.anchoredPosition = new Vector2(0f, -(S.stripHeight + S.pointerHeight));

            for (int i = 0; i < _ticks.Count; i++)
            {
                var tick = _ticks[i];
                float delta = Mathf.DeltaAngle(heading, tick.Angle);
                bool vis = Mathf.Abs(delta) <= halfFov;
                if (tick.Rect.gameObject.activeSelf != vis) tick.Rect.gameObject.SetActive(vis);
                if (!vis) continue;
                tick.Rect.anchoredPosition = new Vector2((delta / halfFov) * halfWidth, tick.Rect.anchoredPosition.y);
            }
        }

        // ---------------------------------------------------------------- markers
        void SyncAutoMarkers()
        {
            _seen.Clear();
            var gps = _gps;
            var region = gps ? gps.Region : null;

            if (S.showKnownPois && region != null && region.pois != null)
            {
                for (int i = 0; i < region.pois.Count; i++)
                {
                    var p = region.pois[i];
                    if (p == null || string.IsNullOrEmpty(p.id) || !gps.IsPoiKnown(p.id)) continue;
                    PlaceAuto(AutoPoi + p.id, new Vector3(p.worldXZ.x, 0f, p.worldXZ.y), ASHCompassMarkerKind.Poi);
                }
            }

            if (S.showSurveys && region != null && region.surveys != null)
            {
                for (int i = 0; i < region.surveys.Count; i++)
                {
                    var s = region.surveys[i];
                    if (s == null) continue;
                    bool known = gps.IsLogged(s) || (gps.Fog != null && gps.Fog.IsRevealed(s.worldXZ, 0.5f));
                    if (!known) continue;
                    PlaceAuto(AutoSurvey + s.surveyId, new Vector3(s.worldXZ.x, 0f, s.worldXZ.y), ASHCompassMarkerKind.Survey);
                }
            }

            if (S.showCampfires)
            {
                var fires = ASHCampfire.All;
                for (int i = 0; i < fires.Count; i++)
                {
                    var f = fires[i];
                    if (!f) continue;
                    PlaceAuto(AutoFire + f.gameObject.GetHashCode().ToString("X8"), f.transform.position, ASHCompassMarkerKind.Campfire);
                }
            }

            _stale.Clear();
            foreach (var pair in _markers)
            {
                if (!pair.Value.Auto) continue;
                if (!_seen.Contains(pair.Key)) _stale.Add(pair.Key);
            }
            for (int i = 0; i < _stale.Count; i++)
            {
                if (_markers.TryGetValue(_stale[i], out var m)) RemoveMarker(m);
            }
        }

        void PlaceAuto(string id, Vector3 pos, ASHCompassMarkerKind kind)
        {
            _seen.Add(id);
            if (!_markers.TryGetValue(id, out var m))
            {
                m = new ASHCompassMarker { Id = id, Auto = true };
                _markers[id] = m;
            }
            m.Kind = kind;
            m.Color = ColorFor(kind);
            m.Follow = null;
            m.WorldPosition = pos;
            m.Visible = true;
            m.Auto = true;
        }

        void RefreshMarkers(float heading, Vector3 playerPos)
        {
            float halfFov = Mathf.Max(1f, S.fieldOfViewDegrees * 0.5f);
            float halfWidth = _strip.rect.width * 0.5f;
            float maxDist = Mathf.Max(1f, S.maxMarkerDistance);
            int shown = 0;
            _seen.Clear();

            foreach (var pair in _markers)
            {
                var m = pair.Value;
                if (m == null || !m.Visible || !KindAllowed(m.Kind)) continue;
                Vector3 pos = m.Position;
                Vector3 flat = pos - playerPos; flat.y = 0f;
                float dist = flat.magnitude;
                if (dist > maxDist) continue;

                float bearing = BearingTo(playerPos, pos);
                float delta = Mathf.DeltaAngle(heading, bearing);
                bool off = Mathf.Abs(delta) > halfFov;
                if (off && !S.clampOffArcToEdge) continue;
                if (off) delta = Mathf.Sign(delta) * halfFov;
                if (shown >= S.maxVisibleMarkers) continue;
                shown++;
                _seen.Add(m.Id);

                if (!_views.TryGetValue(m.Id, out var view))
                {
                    view = CreateView(m.Id);
                    _views[m.Id] = view;
                }

                Color col = m.Color.a > 0f ? m.Color : ColorFor(m.Kind);
                if (S.fadeByDistance)
                {
                    float t = dist / maxDist;
                    float fade = t <= S.fadeStartNormalized ? 1f
                        : Mathf.Lerp(1f, S.minFadeAlpha, (t - S.fadeStartNormalized) / Mathf.Max(0.001f, 1f - S.fadeStartNormalized));
                    col.a *= fade;
                }
                view.Icon.color = col;
                view.Icon.rectTransform.sizeDelta = new Vector2(S.markerSize, S.markerSize);
                if (view.Distance)
                {
                    bool show = S.showDistanceText;
                    if (view.Distance.enabled != show) view.Distance.enabled = show;
                    if (show)
                    {
                        view.Distance.text = Mathf.RoundToInt(dist) + "m";
                        view.Distance.color = S.distanceColor;
                        view.Distance.rectTransform.anchoredPosition = new Vector2(0f, -S.markerSize);
                    }
                }
                view.Rect.sizeDelta = new Vector2(S.markerSize + 4f, S.markerSize + (S.showDistanceText ? S.distanceFontSize + 4f : 2f));
                view.Rect.anchoredPosition = new Vector2((delta / halfFov) * halfWidth, view.Rect.anchoredPosition.y);
            }

            _stale.Clear();
            foreach (var pair in _views)
            {
                if (!_seen.Contains(pair.Key)) _stale.Add(pair.Key);
            }
            for (int i = 0; i < _stale.Count; i++)
            {
                if (_views.TryGetValue(_stale[i], out var v) && v.Rect) Destroy(v.Rect.gameObject);
                _views.Remove(_stale[i]);
            }
        }

        MarkerView CreateView(string id)
        {
            var rt = K.Rect("Mark_" + id, _markerLayer);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(S.markerSize, S.markerSize + S.distanceFontSize + 2f);
            rt.anchoredPosition = Vector2.zero;

            var iconRt = K.At("Icon", rt, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(S.markerSize, S.markerSize));
            iconRt.pivot = new Vector2(0.5f, 1f);
            var icon = K.Image(iconRt, S.poiColor);
            icon.sprite = K.Dot;
            icon.raycastTarget = false;

            var distRt = K.At("Distance", rt, new Vector2(0.5f, 1f), new Vector2(0f, -S.markerSize), new Vector2(48f, S.distanceFontSize + 2f));
            distRt.pivot = new Vector2(0.5f, 1f);
            var dist = K.Text(distRt, "", S.distanceFontSize, S.distanceColor, TextAnchor.UpperCenter);
            AddShadow(dist);

            return new MarkerView { Rect = rt, Icon = icon, Distance = dist };
        }

        void RemoveMarker(ASHCompassMarker m)
        {
            if (m == null) return;
            if (!string.IsNullOrEmpty(m.Id))
            {
                _markers.Remove(m.Id);
                if (_views.TryGetValue(m.Id, out var v))
                {
                    if (v.Rect) Destroy(v.Rect.gameObject);
                    _views.Remove(m.Id);
                }
            }
        }

        static Sprite _tri;
        static Sprite TriangleSprite()
        {
            if (_tri) return _tri;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    // Pointing down: apex at the bottom centre.
                    float half = v * 0.5f + 0.02f;
                    float a = Mathf.Clamp01((half - Mathf.Abs(u - 0.5f)) * n * 0.6f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            _tri = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 1f));
            return _tri;
        }

        static void AddShadow(Text t)
        {
            if (!t || t.GetComponent<Shadow>()) return;
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.8f);
            s.effectDistance = new Vector2(1.2f, -1.2f);
        }
    }
}
