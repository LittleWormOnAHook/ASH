using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Drives the "ASH_Action" upper-body override layer on the ASH animator (built by ASH/Build/Action Animations):
    /// axe chop, bow aim/shot (bow in the left hand), rifle and pistol aim/shot.
    /// One owner at a time; weight blends in/out.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1640)]
    public sealed class ASHActionLayer : MonoBehaviour
    {
        public const string LayerName = "ASH_Action";
        [Range(1f, 40f)] public float weightBlendSpeed = 8f;

        Animator _a; int _layer = -1; float _w, _target; string _state = "Empty";
        public bool Valid => _a && _layer >= 0;
        public float Weight => _w;
        public string State => _state;
        public int Layer => _layer;

        void Awake() { _a = GetComponent<Animator>(); Resolve(); }
        void Resolve() { _layer = _a && _a.runtimeAnimatorController ? _a.GetLayerIndex(LayerName) : -1; }

        public bool Has(string state) => Valid && _a.HasState(_layer, Animator.StringToHash(state));

        /// <summary>Cross-fade to a state (no-op if already there) and blend the layer in.</summary>
        public void Play(string state, float fade = 0.15f)
        {
            if (!Valid) Resolve(); if (!Valid || !Has(state)) return;
            _target = 1f;
            if (_state == state) return;
            _state = state; _a.CrossFadeInFixedTime(state, fade, _layer);
        }

        /// <summary>Restart a one-shot state from the beginning.</summary>
        public void Restart(string state, float fade = 0.05f)
        {
            if (!Valid) Resolve(); if (!Valid || !Has(state)) return;
            _target = 1f; _state = state; _a.CrossFadeInFixedTime(state, fade, _layer, 0f);
        }

        /// <summary>Hold a state at an exact normalized time (states built with speed 0 for manual drive).</summary>
        public void PlayAt(string state, float normalizedTime)
        {
            if (!Valid) Resolve(); if (!Valid || !Has(state)) return;
            _target = 1f; _state = state; _a.Play(state, _layer, normalizedTime);
        }

        public void Release() { _target = 0f; }
        public bool IsIn(string state) => Valid && _a.GetCurrentAnimatorStateInfo(_layer).IsName(state);
        public float NormalizedTime => Valid ? _a.GetCurrentAnimatorStateInfo(_layer).normalizedTime : 0f;

        void Update()
        {
            if (!Valid) return;
            _w = Mathf.MoveTowards(_w, _target, weightBlendSpeed * Time.deltaTime);
            _a.SetLayerWeight(_layer, _w);
            if (_w <= 0f && _target <= 0f && _state != "Empty") { _state = "Empty"; _a.Play("Empty", _layer, 0f); }
        }
    }
}
