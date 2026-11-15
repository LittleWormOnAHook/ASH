using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ASH.Core
{
    /// <summary>Anything that writes its own section into a save. Register in OnEnable, unregister in OnDisable.
    /// Capture returns JSON (JsonUtility of the system's own state class); Restore gets it back after the scene was reloaded.</summary>
    public interface IASHSaveable
    {
        string SaveKey { get; }
        /// <summary>Lower restores first (core: inventory/stats 0, world 10, newer systems 20+).</summary>
        int RestoreOrder { get; }
        string CaptureJson();
        void RestoreJson(string json);
    }

    [Serializable] public sealed class ASHSaveSection { public string key; public string json; }

    [Serializable]
    public sealed class ASHSaveData
    {
        public int version = 1;
        public string slot;
        public string savedAt;          // ISO local time
        public long savedTicks;
        public float playSeconds;
        public string location;
        public string weather;
        public string timeOfDay;
        public string scenePath;
        public List<ASHSaveSection> sections = new List<ASHSaveSection>();
        public string Get(string key) { foreach (var s in sections) if (s.key == key) return s.json; return null; }
        public DateTime SavedAt => savedTicks > 0 ? new DateTime(savedTicks) : DateTime.MinValue;
    }

    /// <summary>
    /// JSON save slots (persistentDataPath/Saves) plus an autosave. A load reloads the game scene and restores every registered
    /// IASHSaveable one frame after the scene's Start calls (so starting kits, the opening and weather init are overwritten).
    /// Created automatically in any scene with a player (no scene edits needed). Autosaves every N real minutes
    /// (ASHSaveSettings) when safe, and when the player enters a shelter.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class ASHSaveSystem : MonoBehaviour
    {
        public const string AutoSlot = "auto";
        public static ASHSaveSystem Instance { get; private set; }
        /// <summary>Save waiting to be applied in the next loaded game scene (null = new game / plain play).</summary>
        public static ASHSaveData PendingLoad { get; private set; }
        public static bool IsRestoring { get; private set; }
        /// <summary>Total play time of the current run (restored from the save).</summary>
        public static float PlaySeconds;
        /// <summary>Extra autosave blockers (exposure warnings, cutscenes...). Return true to skip.</summary>
        public static readonly List<Func<bool>> AutosaveBlockers = new List<Func<bool>>();
        public static event Action<string> Saved;
        public static event Action Restored;

        static readonly List<IASHSaveable> _saveables = new List<IASHSaveable>();
        public static void Register(IASHSaveable s) { if (s != null && !_saveables.Contains(s)) _saveables.Add(s); }
        public static void Unregister(IASHSaveable s) { _saveables.Remove(s); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; PendingLoad = null; IsRestoring = false; PlaySeconds = 0f; _saveables.Clear(); AutosaveBlockers.Clear(); Saved = null; Restored = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single && Instance) return;
            if (FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>() == null) return; // menu or other scene
            if (FindAnyObjectByType<ASHSaveSystem>() != null) return;
            var go = new GameObject("ASH_SaveSystem"); go.AddComponent<ASHSaveSystem>();
        }

        float _nextAuto, _iconUntil, _lastShelterSave = -999f;
        ASHCoreSave _core; ASHWorldSave _world;
        Canvas _canvas; RectTransform _icon; Image _fade; float _fadeT = -1f;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _core = new ASHCoreSave(); _world = new ASHWorldSave(); _world.Snapshot();
            Register(_core); Register(_world);
            BuildUI();
            if (PendingLoad == null && !ASHSceneFlow.ContinuingRun) PlaySeconds = 0f;
            ASHSceneFlow.ContinuingRun = false;
            ResetAutosaveTimer();
        }

        void OnDestroy() { if (Instance == this) Instance = null; Unregister(_core); Unregister(_world); }

        void Start() { if (PendingLoad != null) StartCoroutine(RestoreRoutine(PendingLoad)); }

        IEnumerator RestoreRoutine(ASHSaveData d)
        {
            IsRestoring = true; SetFade(1f);
            yield return null; // every Start has run: overwrite their initial state
            yield return null;
            ApplyNow(d);
            PendingLoad = null; IsRestoring = false;
            _fadeT = 0f;
            ResetAutosaveTimer();
            Restored?.Invoke();
        }

        void ApplyNow(ASHSaveData d)
        {
            PlaySeconds = d.playSeconds;
            var list = new List<IASHSaveable>(_saveables);
            list.Sort((a, b) => a.RestoreOrder.CompareTo(b.RestoreOrder));
            foreach (var s in list)
            {
                string json = d.Get(s.SaveKey); if (json == null) continue;
                try { s.RestoreJson(json); } catch (Exception e) { Debug.LogError($"[ASH] Save: restoring '{s.SaveKey}' failed: {e}"); }
            }
            ASHLog.Info($"Save: restored slot '{d.slot}' ({d.sections.Count} sections)");
        }

        void ResetAutosaveTimer() { float m = ASHSaveSettings.I.autosaveMinutes; _nextAuto = m > 0f ? Time.unscaledTime + m * 60f : float.PositiveInfinity; }

        void Update()
        {
            if (Time.timeScale > 0f && !IsRestoring) PlaySeconds += Time.unscaledDeltaTime;
            var st = ASHSaveSettings.I;
            if (!IsRestoring && Time.unscaledTime >= _nextAuto)
            {
                if (CanAutosave(out _)) { Save(AutoSlot); ResetAutosaveTimer(); }
                else _nextAuto = Time.unscaledTime + st.autosaveRetrySeconds;
            }
            // saving icon
            bool show = Time.unscaledTime < _iconUntil;
            if (_icon) { _icon.gameObject.SetActive(show); if (show) _icon.GetChild(0).localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 240f); }
            if (_fadeT >= 0f)
            {
                _fadeT += Time.unscaledDeltaTime / Mathf.Max(0.05f, st.loadFadeSeconds);
                SetFade(1f - Mathf.Clamp01(_fadeT)); if (_fadeT >= 1f) _fadeT = -1f;
            }
        }

        /// <summary>True when an autosave is safe right now (reason = why not).</summary>
        public bool CanAutosave(out string reason)
        {
            reason = null;
            var stats = FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>();
            if (stats && stats.IsDead) { reason = "dead"; return false; }
            if (ASHUIState.BlocksGameplay) { reason = "menu"; return false; }
            foreach (var q in FindObjectsByType<ASH.Opening.ASHEarthquakeEffects>(FindObjectsSortMode.None)) if (q.Running) { reason = "quake"; return false; }
            var seq = ASH.Opening.ASHEruptionSequence.Instance;
            if (seq && !seq.Suppressed && seq.Current == ASH.Opening.ASHEruptionSequence.Stage.Quake) { reason = "quake"; return false; }
            foreach (var a in FindObjectsByType<ASH.Gameplay.ASHWeaponAim>(FindObjectsSortMode.None)) if (a.Aiming) { reason = "aiming"; return false; }
            var placer = FindAnyObjectByType<ASH.Gameplay.ASHPlacer>(); if (placer && placer.IsPlacing) { reason = "placing"; return false; }
            foreach (var b in AutosaveBlockers) if (b != null && b()) { reason = "blocked"; return false; }
            return true;
        }

        /// <summary>Shelter zones call this when the player walks in.</summary>
        public void OnShelterEntered(float sealing)
        {
            var st = ASHSaveSettings.I;
            if (!st.autosaveOnShelter || sealing < st.shelterAutosaveMinSealing || IsRestoring) return;
            if (Time.unscaledTime - _lastShelterSave < st.shelterAutosaveCooldown) return;
            if (!CanAutosave(out _)) return;
            _lastShelterSave = Time.unscaledTime;
            Save(AutoSlot); ResetAutosaveTimer();
        }

        // ------------------------------------------------------------------ save / load
        public static string Dir => Path.Combine(Application.persistentDataPath, "Saves");
        public static string SlotPath(string slot) => Path.Combine(Dir, $"slot_{slot}.json");

        public bool Save(string slot)
        {
            try
            {
                var d = new ASHSaveData { slot = slot, savedTicks = DateTime.Now.Ticks, savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), playSeconds = PlaySeconds, scenePath = SceneManager.GetActiveScene().path };
                foreach (var s in _saveables)
                {
                    try { d.sections.Add(new ASHSaveSection { key = s.SaveKey, json = s.CaptureJson() }); }
                    catch (Exception e) { Debug.LogError($"[ASH] Save: capturing '{s.SaveKey}' failed: {e}"); }
                }
                d.location = _core.LocationName(); d.weather = _core.WeatherName(); d.timeOfDay = _core.TimeText();
                Directory.CreateDirectory(Dir);
                string tmp = SlotPath(slot) + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(d, false));
                if (File.Exists(SlotPath(slot))) File.Delete(SlotPath(slot));
                File.Move(tmp, SlotPath(slot));
                _iconUntil = Time.unscaledTime + ASHSaveSettings.I.savingIconSeconds;
                ASHLog.Info($"Save: wrote slot '{slot}' ({d.sections.Count} sections)");
                Saved?.Invoke(slot);
                return true;
            }
            catch (Exception e) { Debug.LogError("[ASH] Save failed: " + e); return false; }
        }

        public static ASHSaveData Read(string slot)
        {
            try { var p = SlotPath(slot); return File.Exists(p) ? JsonUtility.FromJson<ASHSaveData>(File.ReadAllText(p)) : null; }
            catch (Exception e) { Debug.LogWarning($"[ASH] Save slot '{slot}' unreadable: {e.Message}"); return null; }
        }

        public static List<ASHSaveData> AllSaves()
        {
            var list = new List<ASHSaveData>();
            if (!Directory.Exists(Dir)) return list;
            foreach (var f in Directory.GetFiles(Dir, "slot_*.json"))
            {
                string slot = Path.GetFileNameWithoutExtension(f).Substring(5);
                var d = Read(slot); if (d != null) { d.slot = slot; list.Add(d); }
            }
            list.Sort((a, b) => b.savedTicks.CompareTo(a.savedTicks));
            return list;
        }

        public static ASHSaveData Newest() { var l = AllSaves(); return l.Count > 0 ? l[0] : null; }
        public static bool Exists(string slot) => File.Exists(SlotPath(slot));
        public static void Delete(string slot) { var p = SlotPath(slot); if (File.Exists(p)) File.Delete(p); }

        /// <summary>Reload the game scene and apply the save.</summary>
        public static void Load(ASHSaveData d)
        {
            if (d == null) return;
            PendingLoad = d;
            ASHSceneFlow.LoadGameScene(string.IsNullOrEmpty(d.scenePath) ? null : d.scenePath);
        }

        // ------------------------------------------------------------------ UI (saving icon + load fade)
        void BuildUI()
        {
            var cgo = new GameObject("ASH_SaveCanvas", typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(transform, false);
            _canvas = cgo.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 300;
            var sc = cgo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)cgo.transform;
            _fade = ASH.UI.ASHUIKit.Image(ASH.UI.ASHUIKit.Stretch("Fade", root), Color.black); _fade.raycastTarget = false; SetFade(0f);
            _icon = ASH.UI.ASHUIKit.At("Saving", root, new Vector2(1, 0), new Vector2(-36, 36), new Vector2(210, 44));
            var ring = ASH.UI.ASHUIKit.At("Ring", _icon, new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(34, 34));
            ring.pivot = new Vector2(0.5f, 0.5f); ring.anchoredPosition = new Vector2(17, 0);
            var ri = ASH.UI.ASHUIKit.Image(ring, ASH.UI.ASHUIKit.Accent); ri.sprite = ASH.UI.ASHUIKit.Ring;
            var dot = ASH.UI.ASHUIKit.At("Dot", ring, new Vector2(0.5f, 1f), new Vector2(0, -2), new Vector2(8, 8)); ASH.UI.ASHUIKit.Image(dot, ASH.UI.ASHUIKit.Ink).sprite = ASH.UI.ASHUIKit.Dot;
            var t = ASH.UI.ASHUIKit.Text(ASH.UI.ASHUIKit.Stretch("Label", _icon, 46, 0, 0, 0), "Saving...", 22, ASH.UI.ASHUIKit.Ink);
            ASH.UI.ASHUIKit.Shadow(t);
            _icon.gameObject.SetActive(false);
        }
        void SetFade(float a) { if (_fade) { _fade.color = new Color(0, 0, 0, a); _fade.enabled = a > 0.001f; } }
    }

    /// <summary>Scene switching that also works in the Editor without the scenes in the build list.</summary>
    public static class ASHSceneFlow
    {
        /// <summary>Set by a load/new game so the next scene keeps play time (not reset to 0).</summary>
        public static bool ContinuingRun;
        /// <summary>True for the next game scene when it was started from the menu as a NEW game.</summary>
        public static bool NewGameRequested;

        public static void NewGame()
        {
            NewGameRequested = true; ASHSaveSystem.PlaySeconds = 0f;
            Time.timeScale = 1f;
            LoadScene(ASHMenuConfig.I.gameScene);
        }

        public static void LoadGameScene(string path)
        {
            ContinuingRun = true; Time.timeScale = 1f;
            LoadScene(string.IsNullOrEmpty(path) ? (string.IsNullOrEmpty(SceneManager.GetActiveScene().path) || SceneManager.GetActiveScene().path == ASHMenuConfig.I.mainMenuScene ? ASHMenuConfig.I.gameScene : SceneManager.GetActiveScene().path) : path);
        }

        public static void MainMenu() { Time.timeScale = 1f; LoadScene(ASHMenuConfig.I.mainMenuScene); }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public static void LoadScene(string path)
        {
            ASHUIState.SetOpen(ASHPanels.Pause, false); ASHUIState.SetOpen(ASHPanels.Death, false);
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            if (SceneUtility.GetBuildIndexByScenePath(path) >= 0) SceneManager.LoadScene(path);
            else { Debug.LogWarning($"[ASH] Scene '{path}' is not in the build scene list; reloading the current scene."); SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().buildIndex : 0); }
        }
    }
}
