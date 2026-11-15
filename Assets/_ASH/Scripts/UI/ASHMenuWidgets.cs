using System;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>Code-built menu widgets in the HUD style (ASHUIKit palette): menu buttons, sliders, steppers, toggles and a
    /// modal confirm box. Shared by the main menu, the pause menu and the death screen.</summary>
    public static class ASHMenuWidgets
    {
        public static readonly Color ButtonBg = new Color(0.12f, 0.11f, 0.10f, 0.92f);
        public static readonly Color ButtonHi = new Color(0.95f, 0.62f, 0.28f, 0.9f);

        /// <summary>Big left-aligned menu button with an accent bar; ink text.</summary>
        public static Button MenuButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Action onClick, Vector2? anchor = null)
        {
            var a = anchor ?? new Vector2(0.5f, 0.5f);
            var rt = K.At("Btn_" + label, parent, a, pos, size);
            var img = K.Image(rt, ButtonBg, true);
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            var cb = b.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.6f, 1.3f, 1f); cb.pressedColor = new Color(0.8f, 0.7f, 0.6f); cb.selectedColor = Color.white; cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.45f); cb.colorMultiplier = 1.4f; b.colors = cb;
            var bar = K.At("Bar", rt, new Vector2(0, 0.5f), Vector2.zero, new Vector2(4, size.y - 12)); K.Image(bar, K.Accent);
            var t = K.Text(K.Stretch("Label", rt, 22, 0, 12, 0), label, Mathf.RoundToInt(Mathf.Clamp(size.y * 0.42f, 16, 30)), K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            K.Shadow(t);
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        public static void SetInteractable(Button b, bool on)
        {
            if (!b) return; b.interactable = on;
            foreach (var t in b.GetComponentsInChildren<Text>()) { var c = t.color; c.a = on ? 1f : 0.4f; t.color = c; }
        }

        public static Text Title(RectTransform parent, string text, Vector2 pos, int size = 44, Vector2? anchor = null)
        {
            var a = anchor ?? new Vector2(0.5f, 1f);
            var t = K.Text(K.At("Title", parent, a, pos, new Vector2(900, size + 16)), text, size, K.Accent, a.x < 0.25f ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, FontStyle.Bold);
            K.Shadow(t); return t;
        }

        /// <summary>Row: label left, horizontal slider right, value text. Returns the slider.</summary>
        public static Slider SliderRow(RectTransform list, string label, float min, float max, float value, Func<float, string> fmt, Action<float> onChange, bool whole = false)
        {
            var row = K.Rect("Row_" + label, list); K.Layout(row, 46);
            K.Text(K.Stretch("Label", row, 6, 0, 420, 0), label, 20, K.Ink);
            var area = K.Stretch("Slider", row, 300, 14, 110, 14);
            var bg = K.Image(area, new Color(0, 0, 0, 0.45f), true);
            var fillArea = K.Stretch("FillArea", area, 0, 0, 0, 0);
            var fill = K.Stretch("Fill", fillArea); K.Image(fill, new Color(0.95f, 0.62f, 0.28f, 0.75f));
            var handleArea = K.Stretch("HandleArea", area, 8, 0, 8, 0);
            var handle = K.Rect("Handle", handleArea); handle.sizeDelta = new Vector2(16, 0); handle.anchorMin = new Vector2(0, 0); handle.anchorMax = new Vector2(0, 1);
            handle.offsetMin = new Vector2(-8, -6); handle.offsetMax = new Vector2(8, 6);
            var himg = K.Image(handle, K.Ink, true);
            var s = area.gameObject.AddComponent<Slider>();
            s.fillRect = fill; s.handleRect = handle; s.targetGraphic = himg; s.direction = Slider.Direction.LeftToRight;
            var nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole; s.value = Mathf.Clamp(value, min, max);
            var vt = K.Text(K.Stretch("Value", row, 0, 0, 6, 0), fmt(s.value), 19, K.Dim, TextAnchor.MiddleRight);
            s.onValueChanged.AddListener(v => { vt.text = fmt(v); onChange?.Invoke(v); });
            return s;
        }

        /// <summary>Row: label left, ◀ value ▶ stepper right. index changes call onChange(newIndex).</summary>
        public static Action<int> StepperRow(RectTransform list, string label, string[] options, int index, Action<int> onChange)
        {
            var row = K.Rect("Row_" + label, list); K.Layout(row, 46);
            K.Text(K.Stretch("Label", row, 6, 0, 420, 0), label, 20, K.Ink);
            int cur = Mathf.Clamp(index, 0, Mathf.Max(0, options.Length - 1));
            var val = K.Text(K.Stretch("Value", row, 350, 0, 60, 0), options.Length > 0 ? options[cur] : "-", 20, K.Ink, TextAnchor.MiddleCenter);
            void Set(int i) { if (options.Length == 0) return; cur = (i + options.Length) % options.Length; val.text = options[cur]; onChange?.Invoke(cur); }
            K.Button(K.At("Prev", row, new Vector2(1, 0.5f), new Vector2(-260, 0), new Vector2(40, 36)), "<", 20, () => Set(cur - 1), new Color(1, 1, 1, 0.12f)).GetComponentInChildren<Text>().color = K.Ink;
            K.Button(K.At("Next", row, new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(40, 36)), ">", 20, () => Set(cur + 1), new Color(1, 1, 1, 0.12f)).GetComponentInChildren<Text>().color = K.Ink;
            return i => { cur = Mathf.Clamp(i, 0, options.Length - 1); val.text = options[cur]; };
        }

        public static void ToggleRow(RectTransform list, string label, bool value, Action<bool> onChange)
        {
            StepperRow(list, label, new[] { "Off", "On" }, value ? 1 : 0, i => onChange?.Invoke(i == 1));
        }

        public static void Header(RectTransform list, string text)
        {
            var row = K.Rect("Hdr_" + text, list); K.Layout(row, 40);
            K.Text(K.Stretch("T", row, 6, 0, 0, 4), text.ToUpperInvariant(), 17, new Color(0.79f, 0.64f, 0.42f), TextAnchor.LowerLeft, FontStyle.Bold);
        }

        /// <summary>Modal yes/no box over the whole parent. Destroys itself when answered.</summary>
        public static void Confirm(RectTransform parent, string message, string yes, string no, Action onYes, Action onNo = null)
        {
            var block = K.Stretch("Confirm", parent); K.Image(block, new Color(0, 0, 0, 0.65f), true);
            var box = K.At("Box", block, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 260)); K.Image(box, K.Panel, true);
            K.Image(K.At("Accent", box, new Vector2(0.5f, 1), Vector2.zero, new Vector2(640, 3)), K.Accent);
            K.Text(K.Stretch("Msg", box, 36, 90, 36, 30), message, 24, K.Ink, TextAnchor.MiddleCenter);
            K.Button(K.At("Yes", box, new Vector2(0.5f, 0), new Vector2(-130, 28), new Vector2(230, 52)), yes, 22, () => { UnityEngine.Object.Destroy(block.gameObject); onYes?.Invoke(); });
            if (!string.IsNullOrEmpty(no)) K.Button(K.At("No", box, new Vector2(0.5f, 0), new Vector2(130, 28), new Vector2(230, 52)), no, 22, () => { UnityEngine.Object.Destroy(block.gameObject); onNo?.Invoke(); }, new Color(1, 1, 1, 0.16f)).GetComponentInChildren<Text>().color = K.Ink;
            block.SetAsLastSibling();
        }

        public static string PlayTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds)); int h = s / 3600, m = (s / 60) % 60;
            return h > 0 ? $"{h} h {m:00} min" : $"{m} min";
        }

        /// <summary>Full-screen canvas (screen-space overlay, 1920x1080 reference) for menus.</summary>
        public static RectTransform Canvas(Transform owner, string name, int order)
        {
            var cgo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(owner, false);
            var c = cgo.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var sc = cgo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            return (RectTransform)cgo.transform;
        }

        public static void EnsureEventSystem(Transform owner)
        {
            if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            es.transform.SetParent(owner, false);
        }
    }
}
