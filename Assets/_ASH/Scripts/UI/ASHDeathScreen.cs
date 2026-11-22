using ASH.Core;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;
using W = ASH.UI.ASHMenuWidgets;

namespace ASH.UI
{
    /// <summary>Death with saves: slow fade, "You didn't make it", then Load Last Save (newest save, manual or auto) or Main Menu.
    /// With no save yet the first button restarts the game.</summary>
    public sealed class ASHDeathScreen : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHDeathScreen()
        {
            Current = default;
            RetryAvailable = null;
            Retry = null;
            RetryLabel = "Retry from Checkpoint";
        }

        public static ASHDeathScreen Current { get; private set; }
        public static bool Active => Current != null;
        /// <summary>Optional first button (M1 prologue: "Retry from Checkpoint" while the quake checkpoint applies).</summary>
        public static System.Func<bool> RetryAvailable;
        public static System.Action Retry;
        public static string RetryLabel = "Retry from Checkpoint";
        [Range(0.2f, 10f)] public float fadeSeconds = 2.5f;

        RectTransform _root, _content; Image _fade; Text _title; float _t;

        public static void Show()
        {
            if (Current) return;
            var go = new GameObject("ASH_DeathScreen"); Current = go.AddComponent<ASHDeathScreen>();
        }

        void Awake()
        {
            ASHUIState.SetOpen(ASHPanels.Death, true);
            W.EnsureEventSystem(transform);
            _root = W.Canvas(transform, "ASH_DeathCanvas", 220);
            _fade = K.Image(K.Stretch("Fade", _root), new Color(0.05f, 0.01f, 0.01f, 0f), true);
            _content = K.Stretch("Content", _root);
            _title = K.Text(K.At("Title", _content, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1200, 110)), "You didn't make it.", 64, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            K.Shadow(_title, 2f);
            var newest = ASHSaveSystem.Newest();
            string sub = newest != null ? $"Last save: {newest.location} · {newest.savedAt}" : "No save yet.";
            K.Text(K.At("Sub", _content, new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(1000, 40)), sub, 22, K.Dim, TextAnchor.MiddleCenter);
            bool retry = Retry != null && RetryAvailable != null && RetryAvailable();
            float dy = retry ? -74f : 0f;
            if (retry) { var r = Retry; W.MenuButton(_content, RetryLabel, new Vector2(0, -50), new Vector2(380, 62), () => r()); }
            W.MenuButton(_content, newest != null ? "Load Last Save" : "Restart", new Vector2(0, -50 + dy), new Vector2(380, 62), () =>
            {
                var d = ASHSaveSystem.Newest(); if (d != null) ASHSaveSystem.Load(d); else ASHSceneFlow.NewGame();
            });
            W.MenuButton(_content, "Main Menu", new Vector2(0, -124 + dy), new Vector2(380, 62), ASHSceneFlow.MainMenu);
            _content.gameObject.SetActive(false);
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / Mathf.Max(0.1f, fadeSeconds));
            _fade.color = new Color(0.05f, 0.01f, 0.01f, Mathf.SmoothStep(0f, 0.92f, k));
            if (k >= 0.6f && !_content.gameObject.activeSelf) _content.gameObject.SetActive(true);
            if (_content.gameObject.activeSelf) { var c = _title.color; c.a = Mathf.Clamp01((k - 0.6f) / 0.4f); _title.color = c; }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        void OnDestroy() { if (Current == this) Current = null; ASHUIState.SetOpen(ASHPanels.Death, false); }
    }
}
