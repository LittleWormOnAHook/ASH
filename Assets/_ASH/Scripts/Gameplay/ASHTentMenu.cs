using System.Collections;
using ASH.Core;
using ASH.Inventory;
using ASH.Survival;
using ASH.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;
using W = ASH.UI.ASHMenuWidgets;

namespace ASH.Gameplay
{
    /// <summary>t565 tent menu: E on a pitched tent opens it. Pack up tent, or Rest / Sleep for a chosen number of hours.
    /// Sleep fades to black (the save system's load-fade overlay), advances the game clock, drains hunger and thirst for the time
    /// slept (ASHTentSettings.sleepNeedsMultiplier) and regenerates some health; it wakes early with a toast when hunger or thirst
    /// gets critical or you take damage, then fades back. Code-built uGUI in the vehicle / dog-menu style (ASHUIKit palette).
    /// All numbers live in Resources/ASH_TentSettings (editable in Play, edits kept).</summary>
    public sealed class ASHTentMenu : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHTentMenu()
        {
            _i = default;
        }

        public const string PanelId = "tent", RestId = "tentRest";
        static ASHTentMenu _i;
        public static bool IsOpen => _i && _i._root && _i._root.gameObject.activeSelf;
        public static bool Resting => _i && _i._resting;

        ASHTent _tent; ASHInventory _inv; ASHSurvivalStats _stats; Transform _player; int _openFrame; int _hours = -1; bool _resting;
        RectTransform _root; Text _title, _status; Slider _slider; Image _black;

        ASHTentSettings S => ASHTentSettings.I;

        public static bool Open(ASHTent tent, ASHInventory inv, ASHSurvivalStats stats, Transform player)
        {
            if (!tent || Resting) return false;
            if (!inv) inv = FindAnyObjectByType<ASHInventory>();
            if (!stats && player) stats = player.GetComponentInParent<ASHSurvivalStats>();
            if (!stats) stats = FindAnyObjectByType<ASHSurvivalStats>();
            if (!_i) { var go = new GameObject("ASH_TentMenu"); _i = go.AddComponent<ASHTentMenu>(); _i.Build(); }
            var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.CloseAllPanels();
            _i._tent = tent; _i._inv = inv; _i._stats = stats; _i._player = player ? player : (stats ? stats.transform : null); _i._openFrame = Time.frameCount;
            var s = ASHTentSettings.I;
            _i._hours = Mathf.Clamp(_i._hours <= 0 ? s.defaultSleepHours : _i._hours, 1, Mathf.Max(1, s.maxSleepHours));
            _i._slider.maxValue = Mathf.Max(1, s.maxSleepHours); _i._slider.value = _i._hours;
            _i._root.gameObject.SetActive(true); ASHUIState.SetOpen(PanelId, true); _i.Refresh();
            return true;
        }

        public static void CloseIfOpen() { if (IsOpen) _i.Close(); }

        static RectTransform TL(string n, Transform p, float x, float y, float w, float h) => K.At(n, p, new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));

        void Build()
        {
            var cv = gameObject.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 60;
            var sc = gameObject.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var ct = (RectTransform)transform;
            // fallback black overlay (used only when there is no save system overlay in the scene)
            _black = K.Image(K.Stretch("Black", ct), new Color(0, 0, 0, 0)); _black.raycastTarget = false; _black.enabled = false;

            _root = K.At("TentMenu", ct, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(600, 330)); K.Image(_root, K.Panel, true);
            K.Image(K.At("Accent", _root, new Vector2(0.5f, 1), Vector2.zero, new Vector2(600, 3)), K.Accent);
            _title = K.Text(TL("Title", _root, 28, 14, 460, 40), "", 30, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            K.Button(K.At("Close", _root, new Vector2(1, 1), new Vector2(-20, -16), new Vector2(46, 38)), "X", 20, Close, new Color(1, 1, 1, 0.15f));
            _status = K.Text(TL("Status", _root, 28, 62, 544, 50), "", 16, K.Ink, TextAnchor.UpperLeft);

            var list = TL("Rows", _root, 22, 116, 556, 50);
            var vl = list.gameObject.AddComponent<VerticalLayoutGroup>(); vl.childControlWidth = true; vl.childForceExpandWidth = true; vl.childControlHeight = true; vl.childForceExpandHeight = false;
            _slider = W.SliderRow(list, "Sleep for", 1, Mathf.Max(1, S.maxSleepHours), Mathf.Max(1, S.defaultSleepHours), v => $"{Mathf.RoundToInt(v)} h", v => { _hours = Mathf.RoundToInt(v); Refresh(); }, true);

            K.Button(TL("Sleep", _root, 28, 196, 260, 52), S.sleepButton, 19, Sleep);
            K.Button(TL("Pack", _root, 312, 196, 260, 52), S.packButton, 19, Pack, new Color(1, 1, 1, 0.18f));
            K.Text(TL("Hint", _root, 28, 262, 544, 50), "<color=#a69e91>Hunger and thirst keep dropping while you sleep. You wake early if either gets critical.</color>", 14, K.Dim, TextAnchor.UpperLeft);
            _root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_i != this) return;
            _i = null;
            ASHUIState.SetOpen(PanelId, false);
            if (_resting) { ASHUIState.SetOpen(RestId, false); if (_stats) _stats.Paused = false; SetBlack(0f); }
        }

        void Close() { if (_root) _root.gameObject.SetActive(false); ASHUIState.SetOpen(PanelId, false); }

        void Pack()
        {
            var t = _tent; var inv = _inv; Close();
            if (t && t.PackUp(inv)) Toast("Tent packed up.");
        }

        void Sleep()
        {
            if (!_tent || !_stats || _resting) return;
            var s = S;
            if (_stats.Hunger <= _stats.MaxHunger * s.wakeHungerFraction) { Toast(s.tooHungryMessage); return; }
            if (_stats.Thirst <= _stats.MaxThirst * s.wakeThirstFraction) { Toast(s.tooThirstyMessage); return; }
            Close();
            StartCoroutine(RestRoutine(Mathf.Clamp(_hours, 1, Mathf.Max(1, s.maxSleepHours))));
        }

        IEnumerator RestRoutine(int hours)
        {
            var s = S; var st = _stats;
            _resting = true;
            ASHUIState.SetOpen(RestId, true); // blocks movement, look and interaction while asleep
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;

            yield return FadeBlack(0f, 1f, s.fadeToBlackSeconds);

            st.Paused = true; // the rest step below drains needs instead of the awake Update
            var clock = ASHGameTime.Clock;
            float left = hours, slept = 0f, lastHealth = st.Health;
            string woke = null;
            while (left > 1e-4f && st)
            {
                if (st.IsDead) break;
                if (s.wakeOnDamage && st.Health < lastHealth - 0.01f) { woke = s.wokeHurtMessage; break; }
                // <= 0.4 h per frame so ASHGameTime.DeltaHours (max 0.5 h jump) still feeds weather, fires and exposure the time slept
                float step = Mathf.Min(left, Mathf.Max(0.01f, s.sleepHoursPerSecond * Time.unscaledDeltaTime), 0.4f);
                if (clock) clock.timeOfDay = Mathf.Repeat(clock.timeOfDay + step, 24f);
                st.SimulateRest(step, s.sleepNeedsMultiplier, s.restHealthPerHour, s.wakeHungerFraction, s.wakeThirstFraction);
                left -= step; slept += step; lastHealth = st.Health;
                if (st.Hunger <= st.MaxHunger * s.wakeHungerFraction) { woke = s.wokeHungryMessage; break; }
                if (st.Thirst <= st.MaxThirst * s.wakeThirstFraction) { woke = s.wokeThirstyMessage; break; }
                yield return null;
            }
            if (st) st.Paused = false;

            float hold = Time.unscaledTime + s.blackHoldSeconds;
            while (Time.unscaledTime < hold) yield return null;
            yield return FadeBlack(1f, 0f, s.fadeBackSeconds);

            ASHUIState.SetOpen(RestId, false);
            _resting = false;
            if (st && !st.IsDead) Toast(!string.IsNullOrEmpty(woke) ? woke + " (" + Dur(slept) + ")" : string.Format(s.sleptMessage, Dur(slept)));
        }

        static string Dur(float h)
        {
            int m = Mathf.RoundToInt(h * 60f);
            return m % 60 == 0 ? $"{m / 60} h" : (m >= 60 ? $"{m / 60} h {m % 60} min" : $"{m} min");
        }

        IEnumerator FadeBlack(float from, float to, float seconds)
        {
            float t = 0f, dur = Mathf.Max(0.01f, seconds);
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / dur;
                SetBlack(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t))));
                yield return null;
            }
            SetBlack(to);
        }

        /// <summary>The existing black load-fade overlay (ASHSaveSystem); our own overlay only when there is none.</summary>
        void SetBlack(float a)
        {
            if (ASHSaveSystem.SetScreenFade(a)) { if (_black) _black.enabled = false; return; }
            if (_black) { _black.color = new Color(0, 0, 0, a); _black.enabled = a > 0.001f; }
        }

        void Update()
        {
            if (!IsOpen) return;
            var kb = Keyboard.current;
            bool far = !_tent || (_player && (_player.position - _tent.transform.position).sqrMagnitude > Mathf.Pow(S.packUseRange + 3f, 2f));
            if (far || (_stats && _stats.IsDead) || ASHUIState.IsOpen(ASHPanels.Inventory) || ASHUIState.IsOpen(ASHPanels.Journal) || ASHUIState.IsOpen(ASHPanels.Map) || ASHUIState.IsOpen(ASHPanels.Death)) { Close(); return; }
            if (kb != null && kb.eKey.wasPressedThisFrame && Time.frameCount > _openFrame + 1) { Close(); return; }
            if (Time.frameCount % 15 == 0) Refresh();
        }

        void Refresh()
        {
            if (!_tent) return;
            var s = S;
            _title.text = s.MenuPrompt(_tent.kind).ToUpperInvariant();
            var clock = ASHGameTime.Clock;
            string now = clock ? Clock(clock.timeOfDay) : "--:--";
            string wake = clock ? Clock(clock.timeOfDay + _hours) : "--:--";
            string needs = "";
            if (_stats)
            {
                float h = _stats.Hunger / Mathf.Max(1f, _stats.MaxHunger), t = _stats.Thirst / Mathf.Max(1f, _stats.MaxThirst);
                needs = $"   Hunger {Col(h, s.wakeHungerFraction)}{h * 100f:0}%</color>   Thirst {Col(t, s.wakeThirstFraction)}{t * 100f:0}%</color>";
            }
            _status.text = $"Time {now}{needs}\n<color=#a69e91>Sleep {_hours} h: wake around {wake}</color>";
        }

        static string Col(float v, float crit) => v <= crit * 2f ? "<color=#f2806b>" : "<color=#9fd39b>";
        static string Clock(float h) { h = Mathf.Repeat(h, 24f); int m = Mathf.FloorToInt(h * 60f); return $"{m / 60:00}:{m % 60:00}"; }

        void Toast(string m) { if (string.IsNullOrEmpty(m)) return; var h = FindAnyObjectByType<ASHHud>(); if (h) h.Toast(m); }
    }
}
