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

        CanvasGroup _group; RectTransform _bar, _barBg, _spin, _dot; Text _status, _tip, _pct, _title;
        Image _backdrop, _bgImg, _darkImg, _vigImg, _ringImg, _dotImg; AspectRatioFitter _fit;
        Sprite _ringSprite, _vigSprite; float _ringSpriteThickness = -1f;
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
            if (!S.loadingScreenEnabled) return;
            if (IsGameScene())
            {
                if (!S.coverEditorPlayInGameScene) return;
                var ls = Ensure();
                ls.StartCoroutine(ls.ReadyRoutine(true));
            }
            else if (IsMenuScene() && S.coverEditorPlayInMenu)
            {
                var ls = Ensure();
                ls.StartCoroutine(ls.MenuRoutine(true)); // t555: Play / game start on the main menu: warm the menu + game shaders first
            }
        }

        static ASHLoadingScreen Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("ASH_LoadingScreen");
            DontDestroyOnLoad(go);
            return go.AddComponent<ASHLoadingScreen>();
        }

        static bool IsGameScene() => FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>() != null;
        static bool IsMenuScene() => FindAnyObjectByType<ASH.UI.ASHMainMenu>() != null;

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
            else if (IsMenuScene()) yield return MenuRoutine(false, t0); // t555: back to the menu waits for its shaders too
            else yield return Finish(t0);
        }

        /// <summary>t555: main menu: let it build, then warm the shader variants (game ones too, so the game load is quicker).</summary>
        IEnumerator MenuRoutine(bool coverNow, float t0 = -1f)
        {
            _busy = true;
            if (coverNow) { Show(1f); _progress = 0f; }
            if (t0 < 0f) t0 = Time.unscaledTime;
            float p0 = Mathf.Clamp01(_progress);
            SetStatus("Preparing the menu");
            float st = Time.unscaledTime, dur = Mathf.Max(0.01f, S.menuSettleSeconds);
            while (Time.unscaledTime - st < S.menuSettleSeconds)
            {
                _progress = Mathf.Lerp(p0, p0 + (1f - p0) * 0.2f, (Time.unscaledTime - st) / dur);
                yield return null;
            }
            if (S.warmupOnMenu && S.warmupShaders)
            {
                SetStatus("Preparing shaders");
                yield return WarmVariants(Mathf.Max(_progress, p0 + (1f - p0) * 0.2f), 0.92f);
                yield return WarmGraphicsStates();
            }
            yield return Finish(t0);
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
            if (S.warmupShaders && (!Application.isEditor || S.warmupInEditor))
            {
                SetStatus("Preparing shaders");
                yield return WarmVariants(0.7f, 0.8f);
                yield return WarmGraphicsStates();
            }

            // 3) pre-render around the player + wildlife / bird meshes into an off-screen target
            if (S.prerenderWorld && (!Application.isEditor || S.prerenderInEditor))
            {
                SetStatus("Preparing the world");
                yield return Prerender();
            }

            _progress = 1f;
            Time.timeScale = oldScale;
            yield return Finish(t0);
        }

        /// <summary>Warms the shader variant collection a few variants per frame, progress from..to.</summary>
        IEnumerator WarmVariants(float from, float to)
        {
            var svc = S.shaderVariants;
            if (!svc || svc.isWarmedUp) { _progress = Mathf.Max(_progress, to); yield break; }
            int total = Mathf.Max(1, svc.variantCount), per = Mathf.Max(1, S.variantsPerFrame), done = 0;
            bool finished = false;
            var sw = new System.Diagnostics.Stopwatch();
            while (!finished)
            {
                // one variant at a time inside a per-frame time budget: in the Editor each variant is a synchronous compile,
                // so a fixed batch of 64 could hold a single frame for tens of seconds
                sw.Restart();
                int n = 0;
                try
                {
                    do { finished = svc.WarmUpProgressively(1); n++; }
                    while (!finished && n < per && sw.Elapsed.TotalMilliseconds < S.warmupFrameBudgetMs);
                }
                catch (Exception e) { Debug.LogWarning("[ASH] Loading: shader warmup: " + e.Message); finished = true; }
                done += n;
                _progress = Mathf.Max(_progress, Mathf.Lerp(from, to, Mathf.Clamp01(done / (float)total)));
                yield return null;
            }
            _progress = Mathf.Max(_progress, to);
        }

        IEnumerator Finish(float t0)
        {
            _progress = 1f;
            SetStatus("Ready");
            float wait = Time.unscaledTime;
            while ((Time.unscaledTime - t0 < S.minDisplaySeconds || _shownProgress < 0.999f) && Time.unscaledTime - wait < 5f) yield return null; // the percentage reaches 100% before the fade
            _shownProgress = 1f;
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
            ApplyLook();
            if (_spin) _spin.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * S.ringSpeed);
            float target = Mathf.Clamp01(_progress);
            // smooth: catch up at least progressSmoothing per second, faster when far behind
            float speed = Mathf.Max(S.progressSmoothing, (target - _shownProgress) * 3f);
            _shownProgress = Mathf.MoveTowards(_shownProgress, target, Time.unscaledDeltaTime * speed);
            if (_bar) _bar.anchorMax = new Vector2(Mathf.Clamp01(_shownProgress), 1f);
            int pct = Mathf.FloorToInt(_shownProgress * 100f + 0.001f);
            if (_progress < 1f) pct = Mathf.Min(pct, 99);
            if (_pct) _pct.text = pct + "%";
            if (S.showTips && Time.unscaledTime >= _nextTip) NextTip();
        }

        void BuildUI()
        {
            var cgo = new GameObject("ASH_LoadingCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            var sc = cgo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            _group = cgo.GetComponent<CanvasGroup>(); _group.blocksRaycasts = true; _group.interactable = false; // blocks clicks to the menu underneath
            var root = (RectTransform)cgo.transform;

            // t557/t558: faded background (Grandpa's cabin): tint, image (fills, keeps aspect), dark wash, vignette
            _backdrop = K.Image(K.Stretch("Backdrop", root), S.backgroundTint, true);
            var bgRt = K.At("Background", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
            _bgImg = K.Image(bgRt, Color.white);
            _fit = bgRt.gameObject.AddComponent<AspectRatioFitter>(); _fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; _fit.aspectRatio = 16f / 9f;
            _darkImg = K.Image(K.Stretch("Darken", root), Color.black);
            _vigImg = K.Image(K.Stretch("Vignette", root), Color.black); _vigImg.sprite = VignetteSprite();
            K.Image(K.Stretch("Haze", root), new Color(0.55f, 0.5f, 0.46f, 0.04f));

            _title = K.Text(K.At("Title", root, new Vector2(0, 1), new Vector2(140, -170), new Vector2(600, 140)), "ASH", 120, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            K.Shadow(_title, 2f);

            // t556: big saving-icon circle, centred: ring + dot orbiting it, percentage below
            _spin = K.At("Circle", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 150));
            _ringImg = K.Image(K.Stretch("Ring", _spin), K.Accent);
            _dot = K.At("Dot", _spin, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
            _dotImg = K.Image(_dot, K.Ink); _dotImg.sprite = K.Dot;
            _pct = K.Text(K.At("Percent", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 60)), "0%", 44, K.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            K.Shadow(_pct, 2f);
            _status = K.Text(K.At("Status", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 36)), "Loading", 20, K.Dim, TextAnchor.MiddleCenter);
            K.Shadow(_status);

            // optional progress bar + tip (bottom)
            _barBg = K.At("Bar", root, new Vector2(0.5f, 0), new Vector2(0, 100), new Vector2(1640, 6));
            K.Image(_barBg, new Color(1f, 1f, 1f, 0.08f));
            _bar = K.Rect("Fill", _barBg);
            _bar.anchorMin = Vector2.zero; _bar.anchorMax = new Vector2(0f, 1f); _bar.offsetMin = Vector2.zero; _bar.offsetMax = Vector2.zero;
            K.Image(_bar, K.Accent);
            _tip = K.Text(K.At("Tip", root, new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(1640, 60)), "", 22, K.Dim, TextAnchor.MiddleCenter, FontStyle.Italic);
            K.Shadow(_tip);
            ApplyLook();
        }

        /// <summary>Applies ASH_LoadingSettings' look every frame while shown (edits in Play show live).</summary>
        void ApplyLook()
        {
            var st = S;
            if (!_group || !st) return;
            if (_backdrop) _backdrop.color = st.backgroundTint;
            if (_bgImg)
            {
                var sp = st.background;
                if (_bgImg.sprite != sp) _bgImg.sprite = sp;
                _bgImg.enabled = sp;
                _bgImg.color = new Color(1f, 1f, 1f, st.backgroundOpacity);
                if (sp && _fit) _fit.aspectRatio = sp.rect.width / Mathf.Max(1f, sp.rect.height);
                float z = st.backgroundZoom + st.backgroundPushIn * Mathf.Clamp01((Time.unscaledTime - _shown) / 20f);
                _bgImg.rectTransform.localScale = new Vector3(z, z, 1f);
            }
            if (_darkImg) { var c = st.darkenColor; c.a = st.backgroundDarken; _darkImg.color = c; _darkImg.enabled = c.a > 0.001f; }
            if (_vigImg) { _vigImg.color = new Color(0f, 0f, 0f, st.vignette); _vigImg.enabled = st.vignette > 0.001f; }
            if (_title) _title.gameObject.SetActive(st.showTitle);

            float size = Mathf.Max(8f, st.ringSize), r = size * 0.5f, th = Mathf.Clamp(st.ringThickness, 0.01f, 0.9f);
            if (_spin) { _spin.sizeDelta = new Vector2(size, size); _spin.anchoredPosition = st.circleOffset; }
            if (_ringImg)
            {
                if (!_ringSprite || !Mathf.Approximately(_ringSpriteThickness, th)) { if (_ringSprite) { Destroy(_ringSprite.texture); Destroy(_ringSprite); } _ringSprite = RingSprite(th); _ringSpriteThickness = th; }
                _ringImg.sprite = _ringSprite; _ringImg.color = st.ringColor;
            }
            if (_dot)
            {
                float d = Mathf.Max(2f, size * st.dotSize);
                _dot.sizeDelta = new Vector2(d, d);
                _dot.anchoredPosition = new Vector2(0f, r * (1f - th * 0.5f));
                if (_dotImg) _dotImg.color = st.dotColor;
            }
            float y = st.circleOffset.y - r - st.percentGap - st.percentFontSize * 0.55f;
            if (_pct)
            {
                _pct.fontSize = st.percentFontSize; _pct.color = st.percentColor;
                _pct.rectTransform.sizeDelta = new Vector2(400f, st.percentFontSize * 1.4f);
                _pct.rectTransform.anchoredPosition = new Vector2(st.circleOffset.x, y);
            }
            if (_status)
            {
                _status.gameObject.SetActive(st.showStatusText);
                _status.fontSize = st.statusFontSize;
                _status.rectTransform.anchoredPosition = new Vector2(st.circleOffset.x, y - st.percentFontSize * 0.55f - st.statusFontSize * 0.9f);
            }
            if (_barBg) _barBg.gameObject.SetActive(st.showProgressBar);
            if (_tip) _tip.gameObject.SetActive(st.showTips);
        }

        /// <summary>Anti-aliased white ring, line thickness = fraction of the radius.</summary>
        static Sprite RingSprite(float thickness)
        {
            const int n = 256;
            float px = 2f / n, outer = 1f - px, inner = outer - thickness;
            return MakeSprite(n, d => Mathf.Clamp01(Mathf.Min(d - inner, outer - d) / px + 0.5f));
        }

        Sprite VignetteSprite()
        {
            if (!_vigSprite) _vigSprite = MakeSprite(128, d => { float t = Mathf.Clamp01((d - 0.55f) / 0.85f); return t * t * (3f - 2f * t); });
            return _vigSprite;
        }

        static Sprite MakeSprite(int n, Func<float, float> alpha)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ASH_LoadingUI" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(Mathf.Sqrt(dx * dx + dy * dy))) * 255f));
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
