using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ASH.UI
{
    /// <summary>Reflection bridge to echo17.EndlessBook.EndlessBook. The vendor pack is git-ignored, so ASH must compile without it;
    /// everything here fails soft (Valid = false) when the type or a member is missing.</summary>
    public sealed class ASHEndlessBookAdapter
    {
        static Type _t, _state, _turnType, _quality, _matEnum, _dtEnum, _pageDataT;
        static MethodInfo _setState, _turnToPage, _setMaterial, _setPageNumber, _setFiller, _setQuality, _setMaxTurning;
        static PropertyInfo _curState, _turning, _left, _last, _deltaTime;
        static FieldInfo _changing, _pageData, _pageDataMat;
        static bool _resolved, _ok;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHEndlessBookAdapter() { _resolved = false; _ok = false; }

        public static Type BookType { get { Resolve(); return _ok ? _t : null; } }
        public static bool Available { get { Resolve(); return _ok; } }

        static void Resolve()
        {
            if (_resolved) return; _resolved = true; _ok = false;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _t = a.GetType("echo17.EndlessBook.EndlessBook", false);
                    if (_t != null) { _pageDataT = a.GetType("echo17.EndlessBook.PageData", false); break; }
                }
                if (_t == null || _pageDataT == null) return;
                const BindingFlags P = BindingFlags.Public | BindingFlags.Instance, N = BindingFlags.NonPublic | BindingFlags.Instance;
                _state = _t.GetNestedType("StateEnum"); _turnType = _t.GetNestedType("PageTurnTimeTypeEnum"); _quality = _t.GetNestedType("StandinQualityEnum");
                _matEnum = _t.GetNestedType("MaterialEnum"); _dtEnum = _t.GetNestedType("DeltaTimeEnum");
                _setState = _t.GetMethod("SetState", P); _turnToPage = _t.GetMethod("TurnToPage", P); _setMaterial = _t.GetMethod("SetMaterial", P);
                _setPageNumber = _t.GetMethod("SetPageNumber", P); _setFiller = _t.GetMethod("SetPageFillerMaterial", P);
                _setQuality = _t.GetMethod("SetStandinQuality", P); _setMaxTurning = _t.GetMethod("SetMaxPagesTurningCount", P);
                _curState = _t.GetProperty("CurrentState", P); _turning = _t.GetProperty("IsTurningPages", P); _left = _t.GetProperty("CurrentLeftPageNumber", P);
                _last = _t.GetProperty("LastPageNumber", P); _deltaTime = _t.GetProperty("DeltaTime", P);
                _changing = _t.GetField("isChangingState", N); _pageData = _t.GetField("pageData", N); _pageDataMat = _pageDataT.GetField("material");
                _ok = _state != null && _turnType != null && _matEnum != null && _setState != null && _turnToPage != null && _setMaterial != null && _setPageNumber != null
                      && _curState != null && _turning != null && _left != null && _last != null && _pageData != null && _pageDataMat != null;
                if (!_ok) Debug.LogWarning("[ASH Journal] EndlessBook found but its API differs from 1.15; using the flat journal.");
            }
            catch (Exception e) { Debug.LogWarning("[ASH Journal] EndlessBook reflection failed: " + e.Message); _ok = false; }
        }

        public enum State { ClosedFront = 0, OpenFront = 1, OpenMiddle = 2, OpenBack = 3, ClosedBack = 4 }

        readonly Component _book;
        public Component Book => _book;
        public bool Valid => _book && _ok;

        ASHEndlessBookAdapter(Component b) { _book = b; }

        public static ASHEndlessBookAdapter From(GameObject go)
        {
            if (!go || !Available) return null;
            var c = go.GetComponentInChildren(_t, true);
            return c ? new ASHEndlessBookAdapter(c) : null;
        }

        object E(Type t, int v) => Enum.ToObject(t, v);

        public State CurrentState => Valid ? (State)Convert.ToInt32(_curState.GetValue(_book)) : State.ClosedFront;
        public bool IsTurning => Valid && (bool)_turning.GetValue(_book);
        public bool IsChanging => Valid && _changing != null && (bool)_changing.GetValue(_book);
        public bool Busy => IsTurning || IsChanging;
        public int LeftPage => Valid ? (int)_left.GetValue(_book) : 1;
        public int LastPage => Valid ? (int)_last.GetValue(_book) : 0;

        public void UseUnscaledTime(bool on) { if (Valid && _deltaTime != null && _dtEnum != null) _deltaTime.SetValue(_book, E(_dtEnum, on ? 1 : 0)); }

        public void SetState(State s, float time, bool stopTurning = false)
        {
            if (!Valid) return;
            Try(() => _setState.Invoke(_book, new object[] { E(_state, (int)s), time, null, stopTurning }));
        }

        /// <summary>Turn to a page. group = TotalTurnTime (a ribbon jump), else TimePerPage (one spread).</summary>
        public void TurnToPage(int page, bool group, float time, float openTime)
        {
            if (!Valid) return;
            Try(() => _turnToPage.Invoke(_book, new object[] { page, E(_turnType, group ? 0 : 1), time, openTime, null, null, null }));
        }

        public void SetMaterial(int matEnum, Material m) { if (Valid && m) Try(() => _setMaterial.Invoke(_book, new object[] { E(_matEnum, matEnum), m })); }
        public void SetPageNumber(int page) { if (Valid) Try(() => _setPageNumber.Invoke(_book, new object[] { page })); }
        public void SetFiller(Material m) { if (Valid && _setFiller != null && m) Try(() => _setFiller.Invoke(_book, new object[] { m })); }
        public void SetStandin(int quality) { if (Valid && _setQuality != null && _quality != null) Try(() => _setQuality.Invoke(_book, new object[] { E(_state, (int)State.OpenMiddle), E(_quality, quality) })); }
        public void SetMaxTurning(int n) { if (Valid && _setMaxTurning != null) Try(() => _setMaxTurning.Invoke(_book, new object[] { n })); }

        /// <summary>Replace all page materials (index 0 = page 1).</summary>
        public void SetPages(System.Collections.Generic.IList<Material> mats)
        {
            if (!Valid) return;
            Try(() =>
            {
                var list = (IList)_pageData.GetValue(_book);
                list.Clear();
                foreach (var m in mats) { var pd = Activator.CreateInstance(_pageDataT); _pageDataMat.SetValue(pd, m); list.Add(pd); }
            });
        }

        /// <summary>Material on a page (1-based), or null.</summary>
        public Material PageMaterial(int page)
        {
            if (!Valid) return null;
            var list = (IList)_pageData.GetValue(_book);
            return page >= 1 && page <= list.Count ? _pageDataMat.GetValue(list[page - 1]) as Material : null;
        }

        static void Try(Action a)
        {
            try { a(); }
            catch (TargetInvocationException e) { Debug.LogWarning("[ASH Journal] EndlessBook call failed: " + (e.InnerException != null ? e.InnerException.Message : e.Message)); }
            catch (Exception e) { Debug.LogWarning("[ASH Journal] EndlessBook call failed: " + e.Message); }
        }
    }
}
