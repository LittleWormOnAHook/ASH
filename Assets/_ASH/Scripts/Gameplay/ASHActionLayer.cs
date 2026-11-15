using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Drives the "ASH_Action" upper-body override layer on the ASH animator (built by ASH/Build/Action Animations):
    /// axe chop, bow aim/shot (bow in the left hand), rifle and pistol aim/shot.
    /// One owner at a time; weight blends in/out. Chop states live on the unmasked "ASH_ActionFull" layer (full body).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1640)]
    public sealed class ASHActionLayer : MonoBehaviour
    {
        public const string LayerName = "ASH_Action";
        /// <summary>Unmasked full-body override layer (axe chop: the clips bend the knees and hips into the swing).</summary>
        public const string FullLayerName = "ASH_ActionFull";
        [Range(1f, 40f)] public float weightBlendSpeed = 5f;

        Animator _a; int _layer = -1, _full = -1; float _w, _target, _fw, _ftarget; string _state = "Empty", _fstate = "Empty"; bool _onFull;
        public bool Valid => _a && _layer >= 0;
        /// <summary>Weight of the layer the current state lives on.</summary>
        public float Weight => _onFull ? _fw : _w;
        public string State => _onFull ? _fstate : _state;
        public int Layer => _onFull ? _full : _layer;
        public bool OnFullBody => _onFull;

        void Awake() { _a = GetComponent<Animator>(); Resolve(); }
        void Resolve()
        {
            bool ok = _a && _a.runtimeAnimatorController;
            _layer = ok ? _a.GetLayerIndex(LayerName) : -1; _full = ok ? _a.GetLayerIndex(FullLayerName) : -1;
        }

        bool OnFull(string state) => _full >= 0 && _a.HasState(_full, Animator.StringToHash(state));
        public bool Has(string state) => Valid && (_a.HasState(_layer, Animator.StringToHash(state)) || OnFull(state));

        /// <summary>Picks the layer for a state (full-body states take over from the upper-body layer and vice versa).</summary>
        int Use(string state)
        {
            _onFull = OnFull(state); _relSpeed = 0f;
            if (_onFull) { _ftarget = 1f; _target = 0f; return _full; }
            _target = 1f; _ftarget = 0f; return _layer;
        }

        /// <summary>Cross-fade to a state (no-op if already there) and blend the layer in.</summary>
        public void Play(string state, float fade = 0.15f)
        {
            if (!Valid) Resolve(); if (!Valid || !Has(state)) return;
            int l = Use(state);
            if (_onFull) { if (_fstate == state) return; _fstate = state; }
            else { if (_state == state) return; _state = state; }
            _a.CrossFadeInFixedTime(state, fade, l);
        }

        /// <summary>Restart a one-shot state from the beginning.</summary>
        public void Restart(string state, float fade = 0.05f)
        {
            if (!Valid) Resolve(); if (!Valid || !Has(state)) return;
            int l = Use(state); if (_onFull) _fstate = state; else _state = state;
            _a.CrossFadeInFixedTime(state, fade, l, 0f);
        }

        /// <summary>Hold a state at an exact normalized time (states built with speed 0 for manual drive).</summary>
        public void PlayAt(string state, float normalizedTime, bool blendIn = true)
        {
            if (!Valid) Resolve(); if (!Valid || !Has(state)) return;
            int l; if (blendIn) l = Use(state); else { _onFull = OnFull(state); l = _onFull ? _full : _layer; }
            if (_onFull) _fstate = state; else _state = state;
            _a.Play(state, l, normalizedTime);
        }

        public void Release() { _target = 0f; _ftarget = 0f; }
        /// <summary>Blend out over 'seconds' (exit transition, e.g. chop follow-through back to idle).</summary>
        public void Release(float seconds) { _target = 0f; _ftarget = 0f; _relSpeed = 1f / Mathf.Max(0.01f, seconds); }
        float _relSpeed;
        public bool IsIn(string state) => Valid && _a.GetCurrentAnimatorStateInfo(Layer).IsName(state);
        public float NormalizedTime => Valid ? _a.GetCurrentAnimatorStateInfo(Layer).normalizedTime : 0f;

        void Update()
        {
            if (!Valid) return;
            float sp = _relSpeed > 0f ? _relSpeed : weightBlendSpeed;
            _w = Mathf.MoveTowards(_w, _target, sp * Time.deltaTime);
            _a.SetLayerWeight(_layer, Mathf.SmoothStep(0f, 1f, _w)); // eased in/out (no snap into or out of aim)
            if (_w <= 0f && _target <= 0f && _state != "Empty") { _state = "Empty"; _a.Play("Empty", _layer, 0f); }
            if (_full >= 0)
            {
                _fw = Mathf.MoveTowards(_fw, _ftarget, sp * Time.deltaTime);
                _a.SetLayerWeight(_full, Mathf.SmoothStep(0f, 1f, _fw));
                if (_fw <= 0f && _ftarget <= 0f && _fstate != "Empty") { _fstate = "Empty"; _a.Play("Empty", _full, 0f); }
            }
        }
    }
}
