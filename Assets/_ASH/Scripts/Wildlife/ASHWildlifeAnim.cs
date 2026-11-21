using UnityEngine;

namespace ASH.Wildlife
{
    /// <summary>
    /// Drives a species' own Malbers clips with the dog's animation setup: Speed/Turn floats when the
    /// ASH locomotion controller is present, otherwise CrossFade of classified walk/trot/run/turn states.
    /// Playback rate is applied by ASHGroundLocomotion so the feet stay planted. Also plays vendor audio.
    /// CrossFade is skipped when the state or controller is missing (no Animator warnings).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHWildlifeAnim : MonoBehaviour
    {
        public string idleState = "Locomotion";
        public string locoState = "Locomotion";
        public string walkState, trotState, runState, turnLState, turnRState;
        public string attackState, hitState, deathState, eatState, sleepState, callState;
        public AudioClip[] stepClips, callClips, attackClips, hurtClips, deathClips;
        public float stepIntervalWalk = 0.42f, stepIntervalRun = 0.28f;

        Animator _anim;
        AudioSource _audio;
        string _state;
        float _stepT;
        bool _deathLock;
        int _hasSpeed = -1; // -1 = not resolved yet (Animator had no controller / wasn't active at Bind)

        public bool HasEat => Has(eatState);
        public bool HasSleep => Has(sleepState);
        public bool HasCall => Has(callState);
        public bool HasAttack => Has(attackState);
        public bool HasDeath => Has(deathState) || Has(sleepState);
        public string Current => _state;
        public float NormalizedTime
        {
            get
            {
                if (!_anim || !_anim.runtimeAnimatorController) return 0f;
                return _anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
            }
        }

        /// <summary>The Animator that actually drives the rig: prefers one with a controller that is active, so a stray
        /// controller-less Animator on a child (vendor model import) isn't picked.</summary>
        public static Animator PickAnimator(GameObject go, Animator fallback = null)
        {
            if (!go) return fallback;
            Animator withCtrl = null;
            foreach (var a in go.GetComponentsInChildren<Animator>(true))
            {
                if (!a || !a.runtimeAnimatorController) continue;
                if (a.isActiveAndEnabled) return a;
                if (!withCtrl) withCtrl = a;
            }
            return withCtrl ? withCtrl : (fallback ? fallback : go.GetComponentInChildren<Animator>());
        }

        static readonly System.Collections.Generic.HashSet<string> _notReadyLogged = new System.Collections.Generic.HashSet<string>();
        void LogNotReady()
        {
            string n = gameObject.name.Replace("(Clone)", "").Trim();
            if (!_notReadyLogged.Add(n)) return;
            string why = !_anim.runtimeAnimatorController ? "has no AnimatorController" : (!_anim.isActiveAndEnabled ? "is inactive/disabled" : "is not initialised yet");
            Debug.Log($"[ASH] Wildlife: '{n}' Animator on '{_anim.gameObject.name}' {why} at Awake; parameters are read once it plays (no warning).");
        }

        public void Bind(Animator anim)
        {
            _anim = anim && anim.runtimeAnimatorController ? anim : PickAnimator(gameObject, anim);
            if (_anim && !AnimReady()) LogNotReady();
            if (_anim) _anim.applyRootMotion = false;
            _hasSpeed = -1; // resolved lazily once the Animator is actually playing its controller
            _deathLock = false;
            // vendor clips (e.g. bear 'Be_Stand Roar') fire Malbers 'PlaySound' events: give them a receiver on the Animator object
            if (_anim && _anim.gameObject != gameObject && !_anim.GetComponent<ASHWildlifeAnimEvents>()) _anim.gameObject.AddComponent<ASHWildlifeAnimEvents>();
            if (!_audio)
            {
                _audio = gameObject.GetComponent<AudioSource>();
                if (!_audio) _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
                _audio.minDistance = 2f;
                _audio.maxDistance = 28f;
                _audio.rolloffMode = AudioRolloffMode.Linear;
            }
        }

        public void TickLoco(float actualSpeed, float trot, float run, bool turningInPlace, float turnRate, float dt)
        {
            if (!_anim || !_anim.runtimeAnimatorController) return;
            if (IsLocked()) return;
            if (_hasSpeed < 0 && AnimReady()) _hasSpeed = HasParam("Speed") ? 1 : 0;
            if (_hasSpeed == 1)
            {
                if (turningInPlace && Has(turnLState) && Has(turnRState))
                    Fade(turnRate >= 0f ? turnRState : turnLState);
                else Fade(locoState);
            }
            else
            {
                string want = Has(idleState) ? idleState : locoState;
                if (turningInPlace && (Has(turnLState) || Has(turnRState)))
                    want = turnRate >= 0f && Has(turnRState) ? turnRState : (Has(turnLState) ? turnLState : turnRState);
                else if (actualSpeed > run * 0.85f && Has(runState)) want = runState;
                else if (actualSpeed > trot * 0.85f && Has(trotState)) want = trotState;
                else if (actualSpeed > 0.2f && Has(walkState)) want = walkState;
                Fade(want);
            }
            Steps(actualSpeed, run, dt);
        }

        public void PlayAttack() { Fade(attackState); Play(attackClips, 0.9f); }
        public void PlayHit() { Fade(hitState); Play(hurtClips, 0.85f); }
        /// <summary>Real death: always restarts the clip (even if a play-dead pose already used it) and locks the Animator there.</summary>
        public void PlayDeath() { Fade(Has(deathState) ? deathState : sleepState, true); _deathLock = true; Play(deathClips, 1f); }
        /// <summary>Play-dead pose (raccoon surprise): same clip, no lock, so it can get up again.</summary>
        public void PlayFakeDeath() { Fade(Has(deathState) ? deathState : sleepState, true); }
        /// <summary>Malbers animation event receiver (animator on the root object).</summary>
        public void PlaySound(AnimationEvent e) { if (e != null && e.objectReferenceParameter is AudioClip c && _audio) _audio.PlayOneShot(c, 0.9f); }
        public void PlayCall() { Fade(callState); Play(callClips, 0.8f); }
        public void PlayEat() => Fade(eatState);
        public void PlaySleep() => Fade(sleepState);
        public void PlayIdle() => Fade(Has(idleState) ? idleState : locoState);

        public bool InState(string state)
        {
            if (string.IsNullOrEmpty(state) || !_anim || !_anim.runtimeAnimatorController) return false;
            var info = _anim.GetCurrentAnimatorStateInfo(0);
            return info.IsName(state);
        }

        public bool Has(string state)
        {
            if (string.IsNullOrEmpty(state) || !_anim || !_anim.runtimeAnimatorController) return false;
            return _anim.HasState(0, Animator.StringToHash(state));
        }

        bool IsLocked()
        {
            if (_deathLock) return true;
            if (InState(attackState) && NormalizedTime < 0.95f) return true;
            if (InState(hitState) && NormalizedTime < 0.85f) return true;
            return false;
        }

        void Fade(string state, bool force = false)
        {
            if (string.IsNullOrEmpty(state) || !_anim || (state == _state && !force)) return;
            if (!_anim.runtimeAnimatorController) return;
            int hash = Animator.StringToHash(state);
            if (!_anim.HasState(0, hash)) return;
            _anim.CrossFade(hash, 0.12f, 0, 0f);
            _state = state;
        }

        void Steps(float speed, float run, float dt)
        {
            if (stepClips == null || stepClips.Length == 0 || speed < 0.2f) { _stepT = 0f; return; }
            float interval = Mathf.Lerp(stepIntervalWalk, stepIntervalRun, Mathf.InverseLerp(0.5f, Mathf.Max(0.6f, run), speed));
            _stepT += dt;
            if (_stepT >= interval)
            {
                _stepT = 0f;
                Play(stepClips, 0.45f);
            }
        }

        void Play(AudioClip[] clips, float vol)
        {
            if (!_audio || clips == null || clips.Length == 0) return;
            var c = clips[Random.Range(0, clips.Length)];
            if (c) _audio.PlayOneShot(c, vol);
        }

        bool AnimReady() => _anim && _anim.runtimeAnimatorController && _anim.isActiveAndEnabled && _anim.isInitialized;

        bool HasParam(string name)
        {
            // Animator.parameters warns "Animator is not playing an AnimatorController" on inactive / uninitialised animators
            if (!AnimReady()) return false;
            var ps = _anim.parameters;
            for (int i = 0; i < ps.Length; i++) if (ps[i].name == name) return true;
            return false;
        }
    }
}
