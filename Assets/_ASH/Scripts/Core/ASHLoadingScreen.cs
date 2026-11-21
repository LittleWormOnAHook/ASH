using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.Core
{
    /// <summary>
    /// Loading screen (ASH HUD style, uGUI like every other ASH screen): async scene load with progress, tips and the auto-save spinner,
    /// then, for game scenes, player input locked and time held while the world settles (spawners), shaders warm up
    /// (ShaderVariantCollection + Unity 6 GraphicsStateCollection) and an off-screen camera renders the world around the player and the
    /// wildlife / bird meshes, so the first visible frames don't hitch on shader compiles. Fades out when done.
    /// ASHSceneFlow.LoadScene routes through <see cref="TryLoad"/>; Editor Play directly in a game scene is covered by <see cref="CoverEditorPlay"/>.
    /// Settings: Resources/ASH_LoadingSettings.
    /// </summary>
    public sealed class ASHLoadingScreen : MonoBehaviour
    {
        public static ASHLoadingScreen Instance { get; private set; }
        /// <summary>True while the screen is up (systems can skip heavy work or popups).</summary>
        public static bool IsLoading => Instance && Instance._busy;
        /// <summary>Hook for other systems to add prefabs to pre-render (called once per load).</summary>
        public static event Action<List<GameObject>> CollectPrewarm;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; CollectPrewarm = null; }

        CanvasGroup _group; RectTransform _bar, _spin; Text _status, _tip, _pct;
        bool _busy; float _shown, _progress, _shownProgress; int _tipIdx = -1; float _nextTip;
        readonly List<Behaviour> _lockedInputs = new List<Behaviour>();

        static ASHLoadingSettings S => ASHLoadingSettings.I;

        // ------------------------------------------------------------------ entry points
        /// <summary>Loads the scene behind the loading screen. False = disabled; caller does a plain load.</summary>
        public static bool TryLoad(string path)
        {
            if (!S.loadingScreenEnabled || string.IsNullOrEmpty(path)) return false;
            bool menu = path == ASHMenuConfig.I.mainMenuScene;
            if (menu && !S.coverMainMenuLoads) return false;
            var ls = Ensure();
            if (ls._busy) return true;
            ls.StartCoroutine(ls.LoadRoutine(path));
            return true;
        }

        /// <summary>Editor Play (or a build) that starts directly in a game scene: cover the first frames and warm up.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CoverEditorPlay()
        {
            if (Instance && Instance._busy) return;
            if (!S.loadingScreenEnabled || !S.coverEditorPlayInGameScene) return;
            if (!IsGameScene()) return;
            var ls = Ensure();
            ls.StartCoroutine(ls.ReadyRoutine(true));
        }

        static ASHLoadingScreen Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("ASH_LoadingScreen");
            DontDestroyOnLoad(go);
            return go.AddComponent<ASHLoadingScreen>();
        }

        static bool IsGameScene() => FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>() != null;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            _group.alpha = 0f;
            _group.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ routines
        IEnumerator LoadRoutine(string path)
        {
            _busy = true;
            Show(0f);
            _progress = 0f;
            SetStatus("Loading");
            yield return Fade(0f, 1f, S.fadeInSeconds);
            float t0 = Time.unscaledTime;
            AsyncOperation op = null;
            try
            {
#if UNITY_EDITOR
                if (System.IO.File.Exists(path))
                    op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
                if (op == null)
                {
                    if (SceneUtility.GetBuildIndexByScenePath(path) >= 0) op = SceneManager.LoadSceneAsync(path);
                    else
                    {
                        Debug.LogWarning($"[ASH] Scene '{path}' is not in the build scene list; reloading the current scene.");
                        int idx = SceneManager.GetActiveScene().buildIndex;
                        op = SceneManager.LoadSceneAsync(idx >= 0 ? idx : 0);
                    }
                }
            }
            catch (Exception e) { Debug.LogError("[ASH] Loading: " + e); }
            if (op == null) { yield return Finish(t0); yield break; }
            while (!op.isDone) { _progress = Mathf.Lerp(0f, 0.55f, Mathf.Clamp01(op.progress / 0.9f)); yield return null; }
            _progress = 0.55f;
            yield return null; // scene Awake/Start
            if (IsGameScene()) yield return ReadyRoutine(false, t0);
            else yield return Finish(t0);
        }

        /// <summary>After a game scene is active: lock input, settle, warm shaders, pre-render, fade out.</summary>
        IEnumerator ReadyRoutine(bool coverNow, float t0 = -1f)
        {
            _busy = true;
            if (coverNow) { Show(1f); _progress = 0.55f; _shownProgress = 0.55f; }
            if (t0 < 0f) t0 = Time.unscaledTime;
            float oldScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            LockInput(true);

            // 1) settle: spawners (terrain detail, vegetation, wildlife, birds) run with time going (settleTimeScale)
            SetStatus("Settling the world");
            Time.timeScale = S.settleTimeScale;
            float start = Time.unscaledTime, stableSince = Time.unscaledTime, nextSample = 0f; int lastCount = -1;
            while (true)
            {
                float el = Time.unscaledTime - start;
                if (Time.unscaledTime >= nextSample)
                {
                    nextSample = Time.unscaledTime + 0.25f;
                    int c = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude).Length;
                    if (c != lastCount) { lastCount = c; stableSince = Time.unscaledTime; }
                }
                bool fast = Time.unscaledDeltaTime * 1000f <= S.settleMaxFrameMs;
                if (!fast) stableSince = Mathf.Max(stableSince, Time.unscaledTime - S.settleStableSeconds * 0.5f);
                bool stable = Time.unscaledTime - stableSince >= S.settleStableSeconds && fast;
                _progress = Mathf.Lerp(0.55f, 0.7f, Mathf.Clamp01(el / Mathf.Max(0.1f, S.settleMinSeconds)));
                if ((el >= S.settleMinSeconds && stable) || el >= S.settleMaxSeconds) break;
                yield return null;
            }

            // 2) shader warmup (time held)
            Time.timeScale = S.warmupTimeScale;
            if (S.warmupShaders)
            {
                SetStatus("Preparing shaders");
                var svc = S.shaderVariants;
                if (svc && !svc.isWarmedUp)
                {
                    int total = Mathf.Max(1, svc.variantCount), per = Mathf.Max(1, S.variantsPerFrame), done = 0;
                    bool finished = false;
                    while (!finished)
                    {
                        try { finished = svc.WarmUpProgressively(per); }
                        catch (Exception e) { Debug.LogWarning("[ASH] Loading: shader warmup: " + e.Message); svc.WarmUp(); finished = true; }
                        done += per;
                        _progress = Mathf.Lerp(0.7f, 0.8f, Mathf.Clamp01(done / (float)total));
                        yield return null;
                    }
                }
                yield return WarmGraphicsStates();
            }

            // 3) pre-render around the player + wildlife / bird meshes into an off-screen target
            if (S.prerenderWorld)
            {
                SetStatus("Preparing the world");
                yield return Prerender();
            }

            _progress = 1f;
            Time.timeScale = oldScale;
            yield return Finish(t0);
        }

        IEnumerator Finish(float t0)
        {
            _progress = 1f;
            SetStatus("Ready");
            while (Time.unscaledTime - t0 < S.minDisplaySeconds) yield return null;
            LockInput(false);
            yield return Fade(1f, 0f, S.fadeOutSeconds);
            if (_group) _group.gameObject.SetActive(false);
            _busy = false;
        }

        // ------------------------------------------------------------------ warmup helpers
        IEnumerator WarmGraphicsStates()
        {
            var gsc = S.graphicsStateCollection;
            if (!gsc) yield break;
            var prog = gsc.GetType().GetMethod("WarmUpProgressively", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
            if (prog != null && prog.ReturnType == typeof(bool))
            {
                for (int guard = 0; guard < 100000; guard++)
                {
                    bool fin;
                    try { fin = (bool)prog.Invoke(gsc, new object[] { Mathf.Max(1, S.graphicsStatesPerFrame) }); }
                    catch (Exception e) { Debug.LogWarning("[ASH] Loading: GraphicsStateCollection warmup: " + (e.InnerException ?? e).Message); yield break; }
                    if (fin) yield break;
                    yield return null;
                }
                yield break;
            }
            foreach (var m in gsc.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "WarmUp") continue;
                var ps = m.GetParameters();
                try
                {
                    var args = new object[ps.Length];
                    for (int i = 0; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);
                    var r = m.Invoke(gsc, args);
                    if (r is Unity.Jobs.JobHandle h) h.Complete();
                }
                catch (Exception e) { Debug.LogWarning("[ASH] Loading: GraphicsStateCollection warmup: " + (e.InnerException ?? e).Message); }
                break;
            }
            yield return null;
        }

        IEnumerator Prerender()
        {
            var main = Camera.main;
            if (!main) yield break;
            var rt = new RenderTexture(Mathf.Max(256, Screen.width / 2), Mathf.Max(144, Screen.height / 2), 24) { name = "ASH_LoadingPrewarmRT" };
            var go = new GameObject("ASH_LoadingPrewarmCam");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.targetTexture = rt;
            cam.depth = main.depth - 10f;
            go.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);

            // meshes to draw in front of the camera
            var list = new List<GameObject>();
            if (S.prewarmPrefabs != null) foreach (var p in S.prewarmPrefabs) if (p) list.Add(p);
            try { CollectPrewarm?.Invoke(list); } catch (Exception e) { Debug.LogWarning("[ASH] Loading: prewarm list: " + e.Message); }
            var draws = new List<(Mesh mesh, Material[] mats)>();
            var seen = new HashSet<Mesh>();
            foreach (var p in list)
            {
                if (!p) continue;
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : (r.GetComponent<MeshFilter>() ? r.GetComponent<MeshFilter>().sharedMesh : null);
                    if (!mesh || !seen.Add(mesh)) continue;
                    draws.Add((mesh, r.sharedMaterials));
                }
            }
            Mesh quad = null;
            if (S.prewarmMaterials != null && S.prewarmMaterials.Length > 0)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
                foreach (var m in S.prewarmMaterials) if (m) draws.Add((quad, new[] { m }));
            }

            int dirs = Mathf.Max(1, S.prerenderDirections), fpd = Mathf.Max(1, S.framesPerDirection);
            var pitches = S.prerenderPitches != null && S.prerenderPitches.Length > 0 ? S.prerenderPitches : new[] { 0f };
            int total = dirs * pitches.Length + Mathf.Max(1, S.prewarmDrawFrames), step = 0;
            float yaw0 = main.transform.eulerAngles.y;
            try
            {
                for (int d = 0; d < dirs; d++)
                    foreach (var pitch in pitches)
                    {
                        go.transform.rotation = Quaternion.Euler(pitch, yaw0 + d * 360f / dirs, 0f);
                        for (int f = 0; f < fpd; f++) { DrawPrewarm(cam, draws); yield return null; }
                        _progress = Mathf.Lerp(0.8f, 0.98f, ++step / (float)total);
                    }
                go.transform.rotation = main.transform.rotation;
                for (int f = 0; f < Mathf.Max(1, S.prewarmDrawFrames); f++) { DrawPrewarm(cam, draws); _progress = Mathf.Lerp(0.8f, 0.98f, ++step / (float)total); yield return null; }
            }
            finally
            {
                cam.targetTexture = null;
                Destroy(go);
                rt.Release(); Destroy(rt);
            }
        }

        /// <summary>Draws each prewarm mesh in a row 3 m in front of the off-screen camera (only that camera sees them).</summary>
        static void DrawPrewarm(Camera cam, List<(Mesh mesh, Material[] mats)> draws)
        {
            if (draws.Count == 0) return;
            var t = cam.transform;
            for (int i = 0; i < draws.Count; i++)
            {
                var (mesh, mats) = draws[i];
                if (!mesh) continue;
                float sz = Mathf.Max(0.01f, mesh.bounds.size.magnitude);
                var pos = t.position + t.forward * 3f + t.right * (((i % 7) - 3) * 0.7f) + t.up * (((i / 7) % 5) - 2) * 0.6f;
                var m = Matrix4x4.TRS(pos, t.rotation, Vector3.one * (0.6f / sz));
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                    if (mats[s]) Graphics.DrawMesh(mesh, m, mats[s], 0, cam, s);
            }
        }

        // ------------------------------------------------------------------ player input lock (Invector, by reflection)
        void LockInput(bool on)
        {
            if (!S.lockPlayerInput && on) return;
            if (on)
            {
                _lockedInputs.Clear();
                foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                {
                    if (!b) continue;
                    var m = b.GetType().GetMethod("SetLockAllInput", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
                    if (m == null) continue;
                    try { m.Invoke(b, new object[] { true }); _lockedInputs.Add(b); } catch { }
                }
            }
            else
            {
                foreach (var b in _lockedInputs)
                {
                    if (!b) continue;
                    var m = b.GetType().GetMethod("SetLockAllInput", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
                    try { m?.Invoke(b, new object[] { false }); } catch { }
                }
                _lockedInputs.Clear();
            }
        }

        // ------------------------------------------------------------------ UI
        void Show(float alpha)
        {
            _group.gameObject.SetActive(true);
            _group.alpha = alpha;
            _shown = Time.unscaledTime;
            _shownProgress = 0f;
            NextTip();
        }

        IEnumerator Fade(float from, float to, float seconds)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.01f, seconds);
                _group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t));
                yield return null;
            }
            _group.alpha = to;
        }

        void SetStatus(string s) { if (_status) _status.text = s; }

        void NextTip()
        {
            var tips = S.tips;
            if (_tip == null) return;
            if (tips == null || tips.Length == 0) { _tip.text = ""; return; }
            int i = tips.Length == 1 ? 0 : UnityEngine.Random.Range(0, tips.Length);
            if (i == _tipIdx && tips.Length > 1) i = (i + 1) % tips.Length;
            _tipIdx = i;
            _tip.text = tips[i];
            _nextTip = Time.unscaledTime + S.tipSeconds;
        }

        void Update()
        {
            if (!_busy || !_group) return;
            if (_spin) _spin.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 240f);
            _shownProgress = Mathf.MoveTowards(_shownProgress, _progress, Time.unscaledDeltaTime * 1.5f);
            if (_bar) _bar.anchorMax = new Vector2(Mathf.Clamp01(_shownProgress), 1f);
            if (_pct) _pct.text = Mathf.RoundToInt(_shownProgress * 100f) + "%";
            if (Time.unscaledTime >= _nextTip) NextTip();
        }

        void BuildUI()
        {
            var cgo = new GameObject("ASH_LoadingCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            var sc = cgo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            _group = cgo.GetComponent<CanvasGroup>(); _group.blocksRaycasts = true; _group.interactable = false;
            var root = (RectTransform)cgo.transform;
            var st = S;
            var bg = K.Image(K.Stretch("Background", root), st.background ? Color.white : st.backgroundTint, true);
            if (st.background) { bg.sprite = st.background; bg.preserveAspect = false; }
            K.Image(K.Stretch("Haze", root), new Color(0.55f, 0.5f, 0.46f, 0.06f));
            var title = K.Text(K.At("Title", root, new Vector2(0, 1), new Vector2(140, -170), new Vector2(600, 140)), "ASH", 120, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            K.Shadow(title, 2f);

            // spinner (same ring + dot as the saving icon) + status
            var spinRow = K.At("Spinner", root, new Vector2(0, 0), new Vector2(140, 150), new Vector2(520, 44));
            spinRow.pivot = new Vector2(0, 0.5f);
            _spin = K.At("Ring", spinRow, new Vector2(0, 0.5f), Vector2.zero, new Vector2(34, 34));
            _spin.pivot = new Vector2(0.5f, 0.5f); _spin.anchoredPosition = new Vector2(17, 0);
            K.Image(_spin, K.Accent).sprite = K.Ring;
            var dot = K.At("Dot", _spin, new Vector2(0.5f, 1f), new Vector2(0, -2), new Vector2(8, 8)); K.Image(dot, K.Ink).sprite = K.Dot;
            _status = K.Text(K.Stretch("Status", spinRow, 46, 0, 0, 0), "Loading", 22, K.Ink);
            K.Shadow(_status);

            // progress bar
            var barBg = K.At("Bar", root, new Vector2(0, 0), new Vector2(140, 100), new Vector2(1640, 6));
            barBg.pivot = new Vector2(0, 0.5f);
            K.Image(barBg, new Color(1f, 1f, 1f, 0.08f));
            _bar = K.Rect("Fill", barBg);
            _bar.anchorMin = Vector2.zero; _bar.anchorMax = new Vector2(0f, 1f); _bar.offsetMin = Vector2.zero; _bar.offsetMax = Vector2.zero;
            K.Image(_bar, K.Accent);
            _pct = K.Text(K.At("Pct", root, new Vector2(1, 0), new Vector2(-140, 150), new Vector2(200, 44)), "0%", 20, K.Dim, TextAnchor.MiddleRight);
            _pct.rectTransform.pivot = new Vector2(1, 0.5f);

            // tip
            _tip = K.Text(K.At("Tip", root, new Vector2(0, 0), new Vector2(140, 220), new Vector2(1640, 60)), "", 22, K.Dim, TextAnchor.MiddleLeft, FontStyle.Italic);
            _tip.rectTransform.pivot = new Vector2(0, 0.5f);
            K.Shadow(_tip);
        }
    }
}
