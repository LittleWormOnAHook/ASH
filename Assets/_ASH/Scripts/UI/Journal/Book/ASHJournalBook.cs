using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>
    /// B1: the 3D journal book (EndlessBook) shown behind the journal. It lives on an off-screen stage (own layer, own camera, own lights)
    /// rendered to a texture that a screen canvas draws under the HUD, over a dimmed game view. J / Esc / Q still drive ASHHud.SetJournal;
    /// this only follows: open plays ClosedFront → OpenMiddle, then fades the journal content in; close hides the content and plays the
    /// close. The requested state is reconciled every frame, so fast J taps never get stuck mid-animation. Falls back to the flat journal
    /// when the toggle is off, the vendor prefab isn't wired or EndlessBook isn't in the project.
    /// </summary>
    public sealed class ASHJournalBook : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHJournalBook() { Instance = null; }
        public static ASHJournalBook Instance { get; private set; }

        /// <summary>True when the 3D book can be used (toggle on, prefab wired, EndlessBook present).</summary>
        public static bool Usable { get { var S = ASHJournalBookSettings.I; return S.use3DBook && S.bookPrefab && ASHEndlessBookAdapter.Available; } }

        ASHEndlessBookAdapter _book; Transform _stage, _bookRoot; Camera _cam; Light _key, _fill; RenderTexture _rt;
        Canvas _canvas; CanvasGroup _group; RawImage _image; Image _dim; AudioSource _audio;
        CanvasGroup _content; bool _want, _built; float _vis, _contentA; Vector2 _closedSize;
        readonly List<Material> _pageMats = new List<Material>();

        public bool WantOpen => _want;
        public bool AtRest => _book != null && _book.Valid && _book.CurrentState == ASHEndlessBookAdapter.State.OpenMiddle && !_book.Busy;
        public ASHEndlessBookAdapter Book => _book;

        public static ASHJournalBook Ensure()
        {
            if (Instance) return Instance;
            if (!Usable) return null;
            var go = new GameObject("ASH_JournalBook");
            Instance = go.AddComponent<ASHJournalBook>();
            if (!Instance.Build()) { Destroy(go); Instance = null; return null; }
            return Instance;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_rt) { _rt.Release(); Destroy(_rt); }
            foreach (var m in _pageMats) if (m) Destroy(m);
            if (_canvas) Destroy(_canvas.gameObject);
        }

        // ------------------------------------------------------------------ build
        bool Build()
        {
            var S = ASHJournalBookSettings.I;
            _stage = new GameObject("Stage").transform; _stage.SetParent(transform, false); _stage.position = S.stagePosition;
            GameObject inst;
            try { inst = Instantiate(S.bookPrefab, _stage); }
            catch (System.Exception e) { Debug.LogWarning("[ASH Journal] Book prefab failed: " + e.Message); return false; }
            inst.name = "Book";
            _bookRoot = inst.transform;
            _book = ASHEndlessBookAdapter.From(inst);
            if (_book == null || !_book.Valid) { Debug.LogWarning("[ASH Journal] Book prefab has no EndlessBook; using the flat journal."); return false; }
            _book.UseUnscaledTime(true);
            ApplyMaterials(S);
            _book.SetMaxTurning(Mathf.Max(1, S.maxPagesTurning));

            var cgo = new GameObject("BookCamera"); cgo.transform.SetParent(_stage, false);
            _cam = cgo.AddComponent<Camera>(); _cam.enabled = false;
            _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0, 0, 0, 0);
            _cam.nearClipPlane = 0.02f; _cam.farClipPlane = 60f; _cam.allowHDR = false; _cam.allowMSAA = true; _cam.depth = -100;
            _cam.useOcclusionCulling = false;
            var ud = _cam.GetUniversalAdditionalCameraData();
            if (ud) { ud.renderPostProcessing = false; ud.renderShadows = false; ud.antialiasing = AntialiasingMode.None; ud.volumeLayerMask = 0; ud.requiresDepthTexture = false; ud.requiresColorTexture = false; }

            _key = MakeLight("KeyLight", LightType.Spot); _fill = MakeLight("FillLight", LightType.Point);
            _audio = gameObject.AddComponent<AudioSource>(); _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.ignoreListenerPause = true;

            var canvasGo = new GameObject("ASH_JournalBookCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _group = canvasGo.AddComponent<CanvasGroup>(); _group.blocksRaycasts = false; _group.interactable = false; _group.alpha = 0f;
            _dim = K.Image(K.Stretch("Dim", canvasGo.transform), Color.black);
            var imgRt = K.Stretch("Book", canvasGo.transform);
            _image = imgRt.gameObject.AddComponent<RawImage>(); _image.raycastTarget = false;
            canvasGo.SetActive(false);

            SetLayer(_stage, S.bookLayer);
            // closed-book footprint at scale 1 (the open spread is about twice as wide); measured once
            _bookRoot.localRotation = Quaternion.identity; _bookRoot.localScale = Vector3.one;
            var bb = new Bounds(_bookRoot.position, Vector3.zero); bool any = false;
            foreach (var r in _bookRoot.GetComponentsInChildren<Renderer>(false)) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
            _closedSize = any ? new Vector2(Mathf.Max(0.05f, bb.size.x), Mathf.Max(0.05f, bb.size.z)) : new Vector2(1f, 1.3f);
            Frame(S);
            _built = true;
            return true;
        }

        Light MakeLight(string n, LightType t)
        {
            var go = new GameObject(n); go.transform.SetParent(_stage, false);
            var l = go.AddComponent<Light>(); l.type = t; l.shadows = LightShadows.None; l.enabled = false;
            l.cullingMask = 1 << ASHJournalBookSettings.I.bookLayer; l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        void ApplyMaterials(ASHJournalBookSettings S)
        {
            // EndlessBook MaterialEnum: 0 cover, 1 page-block back edge, 2 page-block front edge, 3 left page, 4 right page.
            _book.SetMaterial(0, S.coverMaterial);
            _book.SetMaterial(1, S.pageEdgeBackMaterial);
            _book.SetMaterial(2, S.pageEdgeFrontMaterial);
            foreach (var m in _pageMats) if (m) Destroy(m);
            _pageMats.Clear();
            if (!S.pageMaterial) return;
            int pages = Mathf.Max(2, S.spreads * 2);
            for (int i = 0; i < pages; i++)
            {
                var m = new Material(S.pageMaterial) { name = "ASH_BookPage_" + (i + 1) };
                var tex = (i % 2 == 0) ? S.pagePaperLeft : S.pagePaperRight; // odd page numbers sit on the left
                if (tex) SetTex(m, tex);
                _pageMats.Add(m);
            }
            _book.SetPages(_pageMats);
            _book.SetFiller(_pageMats[_pageMats.Count - 1]);
            _book.SetPageNumber(1);
            _book.SetMaterial(3, _pageMats[0]); _book.SetMaterial(4, _pageMats[1]);
            _book.SetStandin((int)S.standinQuality);
        }

        static void SetTex(Material m, Texture t) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t); }

        /// <summary>Material shown on a page (1-based). B2 sets render textures here.</summary>
        public Material PageMaterial(int page) => page >= 1 && page <= _pageMats.Count ? _pageMats[page - 1] : null;
        public int PageCount => _pageMats.Count;

        static void SetLayer(Transform t, int layer) { t.gameObject.layer = layer; for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer); }

        void Frame(ASHJournalBookSettings S)
        {
            _bookRoot.localPosition = Vector3.zero; _bookRoot.localRotation = Quaternion.Euler(S.bookEuler); _bookRoot.localScale = Vector3.one * S.bookScale;
            var dir = S.cameraOffset.sqrMagnitude > 1e-4f ? S.cameraOffset.normalized : Vector3.up;
            float dist = S.cameraOffset.magnitude;
            if (S.autoFrame)
            {
                float w = _closedSize.x * 2f * S.bookScale, d = _closedSize.y * S.bookScale;
                float aspect = Mathf.Max(0.3f, Screen.width / (float)Mathf.Max(1, Screen.height));
                float v = Mathf.Tan(S.cameraFov * 0.5f * Mathf.Deg2Rad), h = v * aspect, fill = Mathf.Max(0.1f, S.frameFill);
                dist = Mathf.Max(w * 0.5f / (h * fill), d * 0.5f / (v * fill));
            }
            var target = _stage.position + S.cameraLookOffset;
            _cam.transform.position = target + dir * dist;
            _cam.transform.rotation = Quaternion.LookRotation(target - _cam.transform.position, Vector3.forward);
            _cam.fieldOfView = S.cameraFov;
            _cam.cullingMask = 1 << S.bookLayer;

            _key.transform.position = target + Quaternion.Euler(S.keyLightEuler) * Vector3.back * (dist * 0.9f) + Vector3.up * dist * 0.6f;
            _key.transform.rotation = Quaternion.LookRotation(target - _key.transform.position);
            _key.color = S.keyLightColor; _key.intensity = S.keyLightIntensity; _key.range = dist * 4f; _key.spotAngle = 70f;
            _fill.transform.position = _cam.transform.position; _fill.color = S.fillLightColor; _fill.intensity = S.fillLightIntensity; _fill.range = dist * 4f;
            _key.cullingMask = _fill.cullingMask = 1 << S.bookLayer;
        }

        void EnsureRT(ASHJournalBookSettings S)
        {
            int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * S.renderScale)), h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * S.renderScale));
            int aa = S.msaa >= 8 ? 8 : S.msaa >= 4 ? 4 : S.msaa >= 2 ? 2 : 1;
            if (_rt && _rt.width == w && _rt.height == h && _rt.antiAliasing == aa) return;
            if (_rt) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "ASH_JournalBookRT", antiAliasing = aa };
            _rt.Create();
            _cam.targetTexture = _rt; _image.texture = _rt;
        }

        // ------------------------------------------------------------------ open / close (driven by ASHHud.SetJournal)
        /// <summary>Called when the journal opens or closes. content = the journal panel (hidden until the book is open).</summary>
        public void SetOpen(bool open, GameObject content)
        {
            if (!_built) return;
            if (content) { _content = content.GetComponent<CanvasGroup>(); if (!_content) _content = content.AddComponent<CanvasGroup>(); }
            if (open == _want) return;
            _want = open;
            var S = ASHJournalBookSettings.I;
            if (open)
            {
                SetLayer(_stage, S.bookLayer); Frame(S); EnsureRT(S);
                _cam.enabled = true; _key.enabled = _fill.enabled = true; _canvas.gameObject.SetActive(true);
                _contentA = 0f; ApplyContent();
                Play(S.openClip, S.openVolume);
            }
            else
            {
                _contentA = 0f; ApplyContent();
                Play(S.closeClip, S.closeVolume);
            }
            Reconcile(S, true);
        }

        void Play(AudioClip c, float v) { if (c && _audio) _audio.PlayOneShot(c, v); }

        void Reconcile(ASHJournalBookSettings S, bool force)
        {
            if (_book == null || !_book.Valid) return;
            var st = _book.CurrentState;
            if (_want && st != ASHEndlessBookAdapter.State.OpenMiddle && !_book.IsChanging) _book.SetState(ASHEndlessBookAdapter.State.OpenMiddle, S.openTime, force);
            else if (!_want && st != ASHEndlessBookAdapter.State.ClosedFront && !_book.IsChanging) _book.SetState(ASHEndlessBookAdapter.State.ClosedFront, S.closeTime, true);
        }

        void ApplyContent()
        {
            if (!_content) return;
            _content.alpha = _contentA; bool on = _contentA > 0.98f;
            _content.blocksRaycasts = on; _content.interactable = on;
        }

        void Update()
        {
            if (!_built) return;
            var S = ASHJournalBookSettings.I;
            Reconcile(S, false);
            bool atRest = AtRest;
            // content fades in once the book lies open
            float goal = _want && atRest ? 1f : 0f;
            _contentA = goal > _contentA ? (S.contentFadeTime <= 0f ? 1f : Mathf.MoveTowards(_contentA, 1f, Time.unscaledDeltaTime / S.contentFadeTime)) : goal;
            ApplyContent();
            // book image + dim
            bool closedDone = !_want && _book.CurrentState == ASHEndlessBookAdapter.State.ClosedFront && !_book.Busy;
            float vGoal = _want ? 1f : closedDone ? 0f : 1f;
            _vis = Mathf.MoveTowards(_vis, vGoal, Time.unscaledDeltaTime / Mathf.Max(0.05f, _want ? S.openTime * 0.5f : S.closeTime * 0.4f));
            _group.alpha = _vis;
            _dim.color = new Color(S.dimColor.r, S.dimColor.g, S.dimColor.b, S.backgroundDim);
            _canvas.sortingOrder = S.sortingOrder;
            if (!_want && _vis <= 0.001f && closedDone && _canvas.gameObject.activeSelf)
            {
                _canvas.gameObject.SetActive(false); _cam.enabled = false; _key.enabled = _fill.enabled = false;
                if (_rt) { _cam.targetTexture = null; _image.texture = null; _rt.Release(); Destroy(_rt); _rt = null; }
            }
            if (_cam.enabled) { EnsureRT(S); if (!_book.Busy) Frame(S); }
        }
    }
}
