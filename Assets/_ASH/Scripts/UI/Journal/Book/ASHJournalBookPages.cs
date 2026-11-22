using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;
using Tab = ASH.UI.ASHJournalUI.Tab;

namespace ASH.UI
{
    /// <summary>
    /// B2: journal tabs on the book's pages. Each tab owns one spread (Items 1-2, Rook 3-4, Notes 5-6, Objectives 7-8, Skills 9-10,
    /// Pets 11-12, Survival Guide 13-14). The live journal panel is snapshotted (off-screen world canvas + ortho camera, one frame)
    /// onto Grandpa-paper pages so flips show real content; at rest the live panel sits over the open spread, scaled to the pages.
    /// Side tabs (sheet-2 paper tabs or tinted ribbons) jump with TurnToPage; the Survival Guide tab is hidden until the journal is read.
    /// Flip one spread: Left/Right arrows, d-pad, a click on the paper margin, or the on-page arrow buttons. No drag-to-flip.
    /// </summary>
    public sealed class ASHJournalBookPages : MonoBehaviour
    {
        public static readonly Tab[] Order = { Tab.Items, Tab.Character, Tab.Notes, Tab.Objectives, Tab.Skills, Tab.Pets, Tab.Crafting };
        static int SpreadOf(Tab t) { int i = System.Array.IndexOf(Order, t); return i < 0 ? 0 : i; }
        public static int LeftPageOf(Tab t) => SpreadOf(t) * 2 + 1;

        ASHJournalBook _book; ASHJournalUI _journal; RectTransform _panel; Image _panelBg; Color _panelBgCol; bool _panelBgSaved;
        Transform _panelParent; int _panelSibling; Vector2 _panelPos, _panelAnchorMin, _panelAnchorMax, _panelPivot; Vector3 _panelScale; bool _panelSaved;

        // capture rig
        Transform _rig; Camera _capCam; RectTransform _capCanvas; RawImage _paperL, _paperR; float _capW = 1300f, _capH = 840f;
        readonly RenderTexture[] _rts = new RenderTexture[2]; readonly int[] _rtSpread = { -1, -1 };

        // overlay (side tabs + arrows + edge zones)
        Canvas _overlay; CanvasGroup _overlayGroup; RectTransform _overlayRt;
        readonly Dictionary<Tab, RectTransform> _ribbons = new Dictionary<Tab, RectTransform>();
        RectTransform _arrowL, _arrowR, _edgeL, _edgeR;

        // state
        enum Step { Idle, PreCapture, CaptureOld, ShowNew, CaptureNew, Turning }
        Step _step = Step.Idle; int _wait; bool _capturing; Tab _shown = Tab.Items, _target; bool _hasShown; bool _openPending; bool _readyToOpen = true;
        Rect _spreadScreen; bool _haveSpread; int _turnTarget; bool _groupTurn;

        public bool HoldContent => _openPending || _step != Step.Idle || _capturing;
        public bool ReadyToOpen => _readyToOpen;
        public bool Capturing => _capturing;

        public void Init(ASHJournalBook book) { _book = book; BuildRig(); BuildOverlay(); }

        void OnDestroy()
        {
            RestorePanel(true);
            for (int i = 0; i < 2; i++) if (_rts[i]) { _rts[i].Release(); Destroy(_rts[i]); }
            if (_overlay) Destroy(_overlay.gameObject);
        }

        // ------------------------------------------------------------------ rig
        void BuildRig()
        {
            var S = ASHJournalBookSettings.I;
            _rig = new GameObject("PageCapture").transform; _rig.SetParent(transform, false);
            _rig.position = S.stagePosition + new Vector3(3000f, 0f, 0f);
            var cgo = new GameObject("CaptureCamera"); cgo.transform.SetParent(_rig, false);
            _capCam = cgo.AddComponent<Camera>(); _capCam.enabled = false; _capCam.orthographic = true;
            _capCam.clearFlags = CameraClearFlags.SolidColor; _capCam.backgroundColor = new Color(0.9f, 0.72f, 0.5f, 1f);
            _capCam.nearClipPlane = 0.1f; _capCam.farClipPlane = 40f; _capCam.allowHDR = false; _capCam.allowMSAA = false; _capCam.depth = -101;
            var ud = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_capCam);
            if (ud) { ud.renderPostProcessing = false; ud.renderShadows = false; ud.volumeLayerMask = 0; }
            var canvasGo = new GameObject("CaptureCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(_rig, false);
            var c = canvasGo.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace; c.worldCamera = _capCam;
            _capCanvas = (RectTransform)canvasGo.transform;
            _capCanvas.localPosition = new Vector3(0f, 0f, 10f); _capCanvas.localRotation = Quaternion.identity;
            _paperL = MakePaper("PaperL", 0f); _paperR = MakePaper("PaperR", 0.5f);
            SetLayer(_rig, S.captureLayer);
            _capCam.cullingMask = 1 << S.captureLayer;
            ResizeCapture(1300f, 840f);
        }

        RawImage MakePaper(string n, float x0)
        {
            var rt = K.Rect(n, _capCanvas); rt.anchorMin = new Vector2(x0, 0f); rt.anchorMax = new Vector2(x0 + 0.5f, 1f); rt.offsetMin = rt.offsetMax = Vector2.zero;
            var r = rt.gameObject.AddComponent<RawImage>(); r.raycastTarget = false; return r;
        }

        void ResizeCapture(float w, float h)
        {
            _capW = w; _capH = h;
            _capCanvas.sizeDelta = new Vector2(w, h); _capCanvas.localScale = Vector3.one;
            _capCam.orthographicSize = h * 0.5f; _capCam.transform.localPosition = Vector3.zero; _capCam.transform.localRotation = Quaternion.identity;
        }

        static void SetLayer(Transform t, int layer) { t.gameObject.layer = layer; for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer); }

        RenderTexture RT(int i)
        {
            var S = ASHJournalBookSettings.I;
            int w = Mathf.Clamp(S.captureWidth, 256, 4096), h = Mathf.Max(64, Mathf.RoundToInt(w * _capH / _capW));
            var rt = _rts[i];
            if (rt && rt.width == w && rt.height == h) return rt;
            if (rt) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "ASH_BookSpread" + i, antiAliasing = 2 };
            rt.Create(); _rts[i] = rt; _rtSpread[i] = -1;
            return rt;
        }

        // ------------------------------------------------------------------ overlay
        void BuildOverlay()
        {
            var go = new GameObject("ASH_JournalBookOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _overlay = go.GetComponent<Canvas>(); _overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            _overlayRt = (RectTransform)go.transform;
            _overlayGroup = go.AddComponent<CanvasGroup>(); _overlayGroup.alpha = 0f; _overlayGroup.blocksRaycasts = false;
            foreach (var t in Order)
            {
                var tab = t;
                var r = K.Rect("Tab_" + t, _overlayRt); r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = new Vector2(0f, 0.5f);
                var img = K.Image(r, Color.white, true); img.preserveAspect = false;
                var mot = K.Image(K.Stretch("Motif", r, 30, 6, 10, 6), Color.white); mot.preserveAspect = true;
                var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = img; var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
                b.onClick.AddListener(() => RequestTab(tab));
                _ribbons[t] = r;
            }
            _edgeL = Zone("EdgeL", () => Flip(-1)); _edgeR = Zone("EdgeR", () => Flip(1));
            _arrowL = Arrow("ArrowL", () => Flip(-1)); _arrowR = Arrow("ArrowR", () => Flip(1));
            go.SetActive(false);
        }

        RectTransform Zone(string n, UnityEngine.Events.UnityAction a)
        {
            var r = K.Rect(n, _overlayRt); r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = Vector2.zero;
            var img = K.Image(r, new Color(0, 0, 0, 0), true);
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.transition = Selectable.Transition.None;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav; b.onClick.AddListener(a);
            return r;
        }

        RectTransform Arrow(string n, UnityEngine.Events.UnityAction a)
        {
            var r = K.Rect(n, _overlayRt); r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = new Vector2(0.5f, 0.5f);
            var img = K.Image(r, Color.white, true); img.preserveAspect = true;
            var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = img;
            var cb = b.colors; cb.highlightedColor = new Color(1f, 0.9f, 0.75f); cb.pressedColor = new Color(0.7f, 0.6f, 0.5f); b.colors = cb;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav; b.onClick.AddListener(a);
            return r;
        }

        // ------------------------------------------------------------------ journal panel placement
        void SavePanel()
        {
            if (_panelSaved || !_panel) return;
            _panelParent = _panel.parent; _panelSibling = _panel.GetSiblingIndex();
            _panelPos = _panel.anchoredPosition; _panelAnchorMin = _panel.anchorMin; _panelAnchorMax = _panel.anchorMax; _panelPivot = _panel.pivot; _panelScale = _panel.localScale;
            _panelBg = _panel.GetComponent<Image>(); if (_panelBg && !_panelBgSaved) { _panelBgCol = _panelBg.color; _panelBgSaved = true; }
            _panelSaved = true;
        }

        /// <summary>Put the journal panel back exactly as the flat journal had it.</summary>
        public void RestorePanel(bool full)
        {
            if (!_panelSaved || !_panel) return;
            if (_panel.parent != _panelParent) { _panel.SetParent(_panelParent, false); _panel.SetSiblingIndex(_panelSibling); }
            _panel.anchorMin = _panelAnchorMin; _panel.anchorMax = _panelAnchorMax; _panel.pivot = _panelPivot;
            _panel.anchoredPosition = _panelPos; _panel.localScale = _panelScale; _panel.localRotation = Quaternion.identity;
            if (full) { if (_panelBg && _panelBgSaved) _panelBg.color = _panelBgCol; _panelSaved = false; SetChrome(true); }
        }

        readonly List<CanvasGroup> _chromeHidden = new List<CanvasGroup>();
        /// <summary>Flat-journal header (title, accent bar, top tab buttons, key hint): hidden in the book, the side tabs replace it.
        /// Uses a CanvasGroup (not SetActive) so the journal's own tab toggles can't bring it back.</summary>
        void SetChrome(bool on)
        {
            if (on) { foreach (var g in _chromeHidden) if (g) { g.alpha = 1f; g.blocksRaycasts = true; g.interactable = true; } _chromeHidden.Clear(); return; }
            if (!_panel) return;
            for (int i = 0; i < _panel.childCount; i++)
            {
                var c = _panel.GetChild(i).gameObject; string n = c.name;
                if (!(n == "Accent" || n == "Title" || n == "Hint" || n.StartsWith("Tab"))) continue;
                var g = c.GetComponent<CanvasGroup>(); if (!g) g = c.AddComponent<CanvasGroup>();
                g.alpha = 0f; g.blocksRaycasts = false; g.interactable = false; _chromeHidden.Add(g);
            }
        }

        float PanelScaleInCapture()
        {
            var S = ASHJournalBookSettings.I; var size = _panelSaved ? _panel.sizeDelta : new Vector2(1200, 720);
            return Mathf.Min((_capW - 2f * S.pageMargin) / size.x, (_capH - 2f * S.pageMargin) / size.y);
        }

        void PlaceLive()
        {
            if (!_panel || !_haveSpread || _capturing) return;
            if (_panel.parent != _panelParent) return;
            var canvas = _panelParent as RectTransform; if (!canvas) return;
            var cs = canvas.rect.size;
            // spread rect (screen fraction) → HUD canvas units (anchored at the centre)
            var r = _spreadScreen;
            float sx = r.width * cs.x / _capW;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f); _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.localScale = Vector3.one * (PanelScaleInCapture() * sx);
            _panel.anchoredPosition = new Vector2((r.center.x - 0.5f) * cs.x, (r.center.y - 0.5f) * cs.y);
        }

        // ------------------------------------------------------------------ spread geometry
        /// <summary>The book's on-screen rect (viewport 0..1) in whatever state it is in (closed cover, opening, open).</summary>
        static bool ScreenRect(Camera cam, Transform root, out Rect rect)
        {
            rect = default; if (!cam || !root) return false;
            var b = new Bounds(); bool any = false;
            foreach (var rd in root.GetComponentsInChildren<Renderer>(false)) { if (!rd.enabled) continue; if (!any) { b = rd.bounds; any = true; } else b.Encapsulate(rd.bounds); }
            if (!any) return false;
            float x0 = 1, y0 = 1, x1 = 0, y1 = 0;
            for (int i = 0; i < 8; i++)
            {
                var v = cam.WorldToViewportPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
            }
            rect = Rect.MinMaxRect(x0, y0, x1, y1); return rect.width > 0.01f && rect.height > 0.01f;
        }

        bool MeasureSpread(Camera bookCam, Transform bookRoot)
        {
            var b = new Bounds(); bool any = false;
            foreach (var rd in bookRoot.GetComponentsInChildren<Renderer>(false)) { if (!any) { b = rd.bounds; any = true; } else b.Encapsulate(rd.bounds); }
            if (!any) return false;
            float x0 = 1, y0 = 1, x1 = 0, y1 = 0;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var v = bookCam.WorldToViewportPoint(c);
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
            }
            var S = ASHJournalBookSettings.I; var ins = S.pageRectInset; float w = x1 - x0, h = y1 - y0;
            var r = Rect.MinMaxRect(x0 + w * ins.x, y0 + h * ins.y, x1 - w * ins.z, y1 - h * ins.w);
            if (r.width <= 0.01f || r.height <= 0.01f) return false;
            _spreadScreen = r; _haveSpread = true;
            // capture canvas follows the spread's on-screen shape
            float aspect = r.width * Screen.width / Mathf.Max(1f, r.height * Screen.height);
            float cw = 1300f, ch = Mathf.Clamp(cw / Mathf.Max(0.3f, aspect), 300f, 2600f);
            if (Mathf.Abs(ch - _capH) > 1f) ResizeCapture(cw, ch);
            return true;
        }

        // ------------------------------------------------------------------ capture
        /// <summary>Render the live panel (current tab) onto spread s. Takes effect at the end of this frame (camera renders once).</summary>
        void BeginCapture(int spread)
        {
            if (!_panel) return;
            var S = ASHJournalBookSettings.I;
            int slot = _rtSpread[0] == spread ? 0 : _rtSpread[1] == spread ? 1 : (_rtSpread[0] == SpreadOf(_shown) && _hasShown ? 1 : 0);
            var rt = RT(slot);
            // free the slot's previous spread back to paper
            if (_rtSpread[slot] >= 0 && _rtSpread[slot] != spread) SetSpreadPaper(_rtSpread[slot]);
            _rtSpread[slot] = spread;

            int kind = S.pagePaper != null && spread < S.pagePaper.Length ? S.pagePaper[spread] : 0;
            _paperL.texture = ASHJournalArt.PageBackground(kind, false) ?? (Texture)ASHJournalArt.Paper;
            _paperR.texture = ASHJournalArt.PageBackground(kind, true) ?? (Texture)ASHJournalArt.Paper;

            SavePanel();
            _panel.SetParent(_capCanvas, false);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.anchoredPosition3D = Vector3.zero; _panel.localRotation = Quaternion.identity;
            _panel.localScale = Vector3.one * PanelScaleInCapture();
            SetLayer(_panel, S.captureLayer);
            var cg = _panel.GetComponent<CanvasGroup>(); if (cg) cg.alpha = 1f;
            Canvas.ForceUpdateCanvases();
            _capCam.targetTexture = rt; _capCam.enabled = true; _capturing = true;
            AssignSpread(spread, rt);
        }

        void EndCapture()
        {
            if (!_capturing) return;
            _capCam.enabled = false; _capturing = false;
            if (_panel) { SetLayer(_panel, _panelParent ? _panelParent.gameObject.layer : 5); RestorePanel(false); var cg = _panel.GetComponent<CanvasGroup>(); if (cg) cg.alpha = 0f; }
        }

        void AssignSpread(int spread, Texture rt)
        {
            var S = ASHJournalBookSettings.I;
            var l = _book.PageMaterial(spread * 2 + 1); var r = _book.PageMaterial(spread * 2 + 2);
            float u = S.flipPagesU ? -0.5f : 0.5f, v = S.flipPagesV ? -1f : 1f, oy = S.flipPagesV ? 1f : 0f;
            float oL = S.swapPageHalves ? 0.5f : 0f, oR = S.swapPageHalves ? 0f : 0.5f;
            if (S.flipPagesU) { oL += 0.5f; oR += 0.5f; }
            SetPageTex(l, rt, new Vector2(u, v), new Vector2(oL, oy));
            SetPageTex(r, rt, new Vector2(u, v), new Vector2(oR, oy));
            _book.RefreshVisiblePages();
        }

        void SetSpreadPaper(int spread)
        {
            var S = ASHJournalBookSettings.I; int kind = S.pagePaper != null && spread < S.pagePaper.Length ? S.pagePaper[spread] : 0;
            SetPageTex(_book.PageMaterial(spread * 2 + 1), ASHJournalArt.PageBackground(kind, false), Vector2.one, Vector2.zero);
            SetPageTex(_book.PageMaterial(spread * 2 + 2), ASHJournalArt.PageBackground(kind, true), Vector2.one, Vector2.zero);
        }

        /// <summary>Plain paper on every spread (sheet-2 page backgrounds per tab).</summary>
        public void PaperAll() { for (int s = 0; s < Order.Length; s++) SetSpreadPaper(s); _rtSpread[0] = _rtSpread[1] = -1; }

        static void SetPageTex(Material m, Texture t, Vector2 scale, Vector2 offset)
        {
            if (!m) return;
            foreach (var p in new[] { "_BaseMap", "_MainTex" })
                if (m.HasProperty(p)) { if (t) m.SetTexture(p, t); m.SetTextureScale(p, scale); m.SetTextureOffset(p, offset); }
        }

        // ------------------------------------------------------------------ requests
        /// <summary>Called by ASHJournalBook when the journal opens: snapshot the current tab, then let the book open on its spread.</summary>
        public void OnOpening(ASHJournalUI journal)
        {
            _journal = journal; _panel = journal ? (RectTransform)journal.transform : null;
            SavePanel();
            if (_panelBg) { var c = _panelBgCol; c.a *= ASHJournalBookSettings.I.contentCardAlpha; _panelBg.color = c; }
            SetChrome(!ASHJournalBookSettings.I.hideFlatHeader);
            _shown = journal ? journal.Current : Tab.Items; _hasShown = true;
            _book.Book.SetPageNumber(LeftPageOf(_shown));
            _openPending = true; _readyToOpen = false; _wait = 2; _step = Step.Idle;
        }

        public void OnClosing()
        {
            EndCapture(); _step = Step.Idle; _openPending = false; _readyToOpen = true;
            if (_book.Book != null && _book.Book.IsTurning) StopFlipSound();
        }

        /// <summary>Book fully closed: hand the panel back to the flat journal layout.</summary>
        public void OnClosed() { EndCapture(); RestorePanel(true); _overlay.gameObject.SetActive(false); _haveSpread = false; }

        bool Visible(Tab t) => t != Tab.Crafting || ASHHud.CanCraftNow;

        /// <summary>Side tab / flip: capture the current tab, switch, capture the new one, turn.</summary>
        public void RequestTab(Tab t)
        {
            if (!Visible(t) || !_journal || _step != Step.Idle || _openPending || !_book.AtRest) return;
            if (t == _journal.Current && t == _shown) return;
            _target = t; _step = Step.PreCapture; _wait = 0;
        }

        public void Flip(int dir)
        {
            if (!_journal) return;
            int i = System.Array.IndexOf(Order, _journal.Current);
            for (int k = i + dir; k >= 0 && k < Order.Length; k += dir) if (Visible(Order[k])) { RequestTab(Order[k]); return; }
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (!_book || _book.Book == null || !_book.Book.Valid) return;
            var S = ASHJournalBookSettings.I;
            var bk = _book.Book;

            // a capture camera renders once, then the panel goes home
            if (_capturing) { EndCapture(); return; }

            if (_openPending)
            {
                if (_wait-- > 0) return;
                BeginCapture(SpreadOf(_shown));
                _openPending = false; _readyToOpen = true;
                return;
            }

            bool rest = _book.AtRest && _book.WantOpen;
            if (rest && MeasureSpread(_book.BookCamera, _book.BookRoot)) { }

            switch (_step)
            {
                case Step.Idle:
                    // header-button tab change inside the journal: snapshot the new tab and turn (the old spread keeps its last snapshot)
                    if (rest && _journal && _journal.Current != _shown) { _target = _journal.Current; _step = Step.ShowNew; _wait = 2; }
                    break;
                case Step.PreCapture:
                    BeginCapture(SpreadOf(_shown)); _step = Step.CaptureOld; break;
                case Step.CaptureOld:
                    if (_journal.Current != _target) _journal.Show(_target);
                    _step = Step.ShowNew; _wait = 2; break;
                case Step.ShowNew:
                    if (_wait-- > 0) break;
                    BeginCapture(SpreadOf(_target)); _step = Step.CaptureNew; break;
                case Step.CaptureNew:
                {
                    int from = SpreadOf(_shown), to = SpreadOf(_target);
                    _turnTarget = to * 2 + 1; _groupTurn = Mathf.Abs(to - from) > 1;
                    if (from == to) { _shown = _target; _step = Step.Idle; break; }
                    bk.TurnToPage(_turnTarget, _groupTurn, _groupTurn ? S.groupTurnTime : S.singleTurnTime, S.openTime);
                    _book.PlayTurn(_groupTurn);
                    _step = Step.Turning; break;
                }
                case Step.Turning:
                    if (!bk.Busy && bk.CurrentState == ASHEndlessBookAdapter.State.OpenMiddle)
                    {
                        if (bk.LeftPage != _turnTarget) bk.SetPageNumber(_turnTarget);
                        StopFlipSound(); _shown = _target; _step = Step.Idle;
                    }
                    break;
            }

            // input flips (at rest only)
            if (rest && _step == Step.Idle && !TypingInField())
            {
                var kb = Keyboard.current; var gp = Gamepad.current;
                if (S.arrowKeysFlip && kb != null) { if (kb.leftArrowKey.wasPressedThisFrame) Flip(-1); else if (kb.rightArrowKey.wasPressedThisFrame) Flip(1); }
                if (S.dpadFlip && gp != null) { if (gp.dpad.left.wasPressedThisFrame) Flip(-1); else if (gp.dpad.right.wasPressedThisFrame) Flip(1); }
            }

            if (rest && _step == Step.Idle) PlaceLive();
            LayoutOverlay(S, rest);
        }

        void StopFlipSound() => _book.StopTurnSound();

        static bool TypingInField()
        {
            var es = EventSystem.current; var go = es ? es.currentSelectedGameObject : null;
            return go && (go.GetComponent<InputField>() || go.GetComponent("TMP_InputField"));
        }

        void LayoutOverlay(ASHJournalBookSettings S, bool rest)
        {
            bool haveBook = ScreenRect(_book.BookCamera, _book.BookRoot, out var bookR);
            bool show = _book.WantOpen && haveBook;
            if (_overlay.gameObject.activeSelf != show) _overlay.gameObject.SetActive(show);
            if (!show) return;
            _overlay.sortingOrder = S.overlaySortingOrder;
            bool live = rest && _step == Step.Idle && !_openPending;
            _overlayGroup.alpha = Mathf.MoveTowards(_overlayGroup.alpha, rest ? 1f : 0.8f, Time.unscaledDeltaTime * 6f);
            _overlayGroup.blocksRaycasts = live; _overlayGroup.interactable = live;

            var cs = _overlayRt.rect.size; var r = _spreadScreen;
            float x0 = r.xMin * cs.x, x1 = r.xMax * cs.x, y0 = r.yMin * cs.y, y1 = r.yMax * cs.y;
            // side tabs along the book's edge (left by default), following the book closed, opening and open
            float bx0 = bookR.xMin * cs.x, bx1 = bookR.xMax * cs.x, by1 = bookR.yMax * cs.y;
            bool left = S.tabSide == ASHJournalBookSettings.TabSide.Left;
            bool sheet2 = ASHJournalArt.I.tabStyle == ASHJournalArt.TabStyle.Sheet2PaperTabs;
            float y = by1 - S.ribbonSize.y * 0.5f - 8f; int idx = 0;
            var cur = _journal ? _journal.Current : Tab.Items;
            foreach (var t in Order)
            {
                var rt = _ribbons[t]; bool vis = Visible(t);
                if (rt.gameObject.activeSelf != vis) rt.gameObject.SetActive(vis);
                int ci = idx++;
                if (!vis) continue;
                bool sel = t == cur;
                rt.sizeDelta = S.ribbonSize * (sel ? 1.08f : 1f);
                float stick = (sel ? S.ribbonOut + 10f : S.ribbonOut) - S.ribbonSize.x * 0.35f - 6f;
                rt.pivot = new Vector2(left ? 1f : 0f, 0.5f);
                rt.anchoredPosition = new Vector2(left ? bx0 - stick : bx1 + stick, y);
                rt.localScale = new Vector3(S.mirrorTabArt ? -1f : 1f, 1f, 1f);
                var img = rt.GetComponent<Image>(); var mot = rt.Find("Motif").GetComponent<Image>();
                var paperTab = sheet2 ? ASHJournalArt.PaperTab(t.ToString()) : null;
                if (paperTab) { img.sprite = paperTab; img.color = sel ? Color.white : new Color(0.86f, 0.82f, 0.76f, 1f); mot.enabled = false; }
                else
                {
                    img.sprite = ASHJournalArt.Ribbon; var col = S.ribbonColors != null && ci < S.ribbonColors.Length ? S.ribbonColors[ci] : new Color(0.5f, 0.3f, 0.2f);
                    img.color = sel ? Color.Lerp(col, Color.white, 0.15f) : col;
                    var m = ASHJournalArt.Motif(t.ToString()); mot.sprite = m; mot.enabled = m; mot.color = new Color(1f, 0.95f, 0.85f, 0.95f);
                    rt.localRotation = Quaternion.identity;
                }
                y -= S.ribbonSize.y + S.ribbonSpacing;
            }
            // page-edge zones (paper margin) and the on-page arrows
            float marginPx = S.pageMargin * (r.width * cs.x / _capW);
            int i0 = System.Array.IndexOf(Order, cur);
            bool canBack = false, canFwd = false;
            for (int k = i0 - 1; k >= 0; k--) if (Visible(Order[k])) { canBack = true; break; }
            for (int k = i0 + 1; k < Order.Length; k++) if (Visible(Order[k])) { canFwd = true; break; }
            bool spread = _haveSpread && rest; canBack &= spread; canFwd &= spread;
            _edgeL.gameObject.SetActive(S.edgeClickFlip && canBack); _edgeR.gameObject.SetActive(S.edgeClickFlip && canFwd);
            _edgeL.anchoredPosition = new Vector2(x0, y0); _edgeL.sizeDelta = new Vector2(Mathf.Max(12f, marginPx), y1 - y0);
            _edgeR.anchoredPosition = new Vector2(x1 - Mathf.Max(12f, marginPx), y0); _edgeR.sizeDelta = new Vector2(Mathf.Max(12f, marginPx), y1 - y0);
            var aL = ASHJournalArt.ArrowLeft; var aR = ASHJournalArt.ArrowRight;
            _arrowL.gameObject.SetActive(S.arrowButtons && canBack); _arrowR.gameObject.SetActive(S.arrowButtons && canFwd);
            SetArrow(_arrowL, aL, "<", new Vector2(x0 + Mathf.Max(S.arrowButtonSize.x * 0.5f, marginPx * 0.5f) + 4f, y0 + S.arrowButtonSize.y * 0.5f + 6f), S.arrowButtonSize);
            SetArrow(_arrowR, aR, ">", new Vector2(x1 - Mathf.Max(S.arrowButtonSize.x * 0.5f, marginPx * 0.5f) - 4f, y0 + S.arrowButtonSize.y * 0.5f + 6f), S.arrowButtonSize);
        }

        static void SetArrow(RectTransform r, Sprite s, string fallback, Vector2 pos, Vector2 size)
        {
            r.anchoredPosition = pos; r.sizeDelta = size;
            var img = r.GetComponent<Image>(); img.sprite = s; img.color = s ? Color.white : new Color(0.22f, 0.18f, 0.14f, 0.85f);
            var tx = r.Find("T") ? r.Find("T").GetComponent<Text>() : null;
            if (!s && !tx) tx = K.Text(K.Stretch("T", r), fallback, 22, new Color(0.95f, 0.88f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Bold);
            if (tx) tx.enabled = !s;
        }
    }
}
