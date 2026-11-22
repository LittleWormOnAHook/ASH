using UnityEngine;
using UnityEngine.UI;

namespace ASH.UI
{
    /// <summary>Shared code-built uGUI helpers and palette for ASH panels (journal, GPS map).</summary>
    public static class ASHUIKit
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHUIKit()
        {
            _font = default;
            _ring = default;
            _dot = default;
        }

        public static readonly Color Panel = new Color(0.06f, 0.055f, 0.05f, 0.92f);
        public static readonly Color Paper = new Color(0.86f, 0.81f, 0.70f, 1f);
        public static readonly Color PaperInk = new Color(0.22f, 0.18f, 0.14f, 1f);
        public static readonly Color Ink = new Color(0.93f, 0.9f, 0.84f);
        public static readonly Color Dim = new Color(0.65f, 0.62f, 0.57f);
        public static readonly Color Accent = new Color(0.95f, 0.62f, 0.28f);
        public static readonly Color Good = new Color(0.62f, 0.83f, 0.6f);
        public static readonly Color Bad = new Color(0.95f, 0.5f, 0.42f);
        public static readonly Color Row = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color RowSel = new Color(0.95f, 0.62f, 0.28f, 0.28f);

        static Font _font;
        public static Font Font => _font ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false); return rt;
        }

        /// <summary>Rect anchored to the parent with pixel offsets from each edge (l, b, r, t).</summary>
        public static RectTransform Stretch(string name, Transform parent, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            var rt = Rect(name, parent); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t); return rt;
        }

        /// <summary>Rect with anchor+pivot at a normalized point, positioned and sized in pixels.</summary>
        public static RectTransform At(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = Rect(name, parent); rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
        }

        public static Image Image(RectTransform rt, Color c, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>(); img.color = c; img.raycastTarget = raycast; return img;
        }

        public static Text Text(RectTransform rt, string text, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var t = rt.gameObject.AddComponent<Text>(); t.font = Font; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = style;
            t.text = text; t.supportRichText = true; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(RectTransform rt, string label, int size, UnityEngine.Events.UnityAction onClick, Color? bg = null)
        {
            var img = Image(rt, bg ?? new Color(0.95f, 0.62f, 0.28f, 0.85f), true);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            var cb = b.colors; cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.5f); cb.highlightedColor = new Color(1f, 0.92f, 0.8f); b.colors = cb;
            if (onClick != null) b.onClick.AddListener(onClick);
            var t = Text(Stretch("Label", rt), label, size, new Color(0.1f, 0.08f, 0.06f), TextAnchor.MiddleCenter, FontStyle.Bold);
            return b;
        }

        public static void Shadow(Graphic g, float d = 1.5f) { var s = g.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.75f); s.effectDistance = new Vector2(d, -d); }

        /// <summary>Vertical scroll list: returns the content transform (VerticalLayoutGroup + ContentSizeFitter).</summary>
        public static RectTransform ScrollList(RectTransform area, float spacing = 4f)
        {
            Image(area, new Color(0, 0, 0, 0.18f), true);
            var sr = area.gameObject.AddComponent<ScrollRect>(); sr.horizontal = false; sr.scrollSensitivity = 30f; sr.movementType = ScrollRect.MovementType.Clamped;
            var vp = Stretch("Viewport", area, 4, 4, 4, 4); vp.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", vp); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>(); vl.spacing = spacing; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp; sr.content = content;
            return content;
        }

        public static LayoutElement Layout(Component c, float height) { var le = c.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = height; le.minHeight = height; return le; }

        public static void Clear(Transform t) { for (int i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject); }

        static Sprite _ring, _dot;
        /// <summary>Generated anti-aliased ring sprite (white).</summary>
        public static Sprite Ring => _ring ? _ring : (_ring = Generated(128, (d) => Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) * 18f)));
        public static Sprite Dot => _dot ? _dot : (_dot = Generated(64, (d) => Mathf.Clamp01((1f - d) * 8f)));

        static Sprite Generated(int n, System.Func<float, float> alpha)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(alpha(Mathf.Sqrt(dx * dx + dy * dy)) * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
