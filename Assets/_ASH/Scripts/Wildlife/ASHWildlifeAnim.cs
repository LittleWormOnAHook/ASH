using UnityEngine;

namespace ASH.Wildlife
{
    /// <summary>
    /// Drives a species' own Malbers clips with the dog's animation setup: Speed/Turn floats when the
    /// ASH locomotion controller is present, otherwise CrossFade of classified walk/trot/run/turn states.
    /// Playback rate is applied by ASHGroundLocomotion so the feet stay planted. Also plays vendor audio.
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
        bool _hasSpeed;

        public void Bind(Animator anim)
        {
            _anim = anim ? anim : GetComponentInChildren<Animator>();
            if (_anim) _anim.applyRootMotion = false;
            _hasSpeed = HasParam("Speed");
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
            if (_hasSpeed)
            {
                if (turningInPlace && !string.IsNullOrEmpty(turnLState) && !string.IsNullOrEmpty(turnRState))
                {
                    var turn = turnRate >= 0f ? turnRState : turnLState;
                    Fade(turn);
                }
                else Fade(locoState);
            }
            else
            {
                string want = idleState;
                if (turningInPlace && (!string.IsNullOrEmpty(turnLState) || !string.IsNullOrEmpty(turnRState)))
                    want = turnRate >= 0f && !string.IsNullOrEmpty(turnRState) ? turnRState : (string.IsNullOrEmpty(turnLState) ? turnRState : turnLState);
                else if (actualSpeed > run * 0.85f && !string.IsNullOrEmpty(runState)) want = runState;
                else if (actualSpeed > trot * 0.85f && !string.IsNullOrEmpty(trotState)) want = trotState;
                else if (actualSpeed > 0.2f && !string.IsNullOrEmpty(walkState)) want = walkState;
                Fade(want);
            }
            Steps(actualSpeed, run, dt);
        }

        public void PlayAttack() { Fade(attackState); Play(attackClips, 0.9f); }
        public void PlayHit() { Fade(hitState); Play(hurtClips, 0.85f); }
        public void PlayDeath() { Fade(!string.IsNullOrEmpty(deathState) ? deathState : sleepState); Play(deathClips, 1f); }
        public void PlayCall() { Fade(callState); Play(callClips, 0.8f); }
        public void PlayEat() => Fade(eatState);
        public void PlaySleep() => Fade(sleepState);
        public void PlayIdle() => Fade(!string.IsNullOrEmpty(idleState) ? idleState : locoState);

        void Fade(string state)
        {
            if (string.IsNullOrEmpty(state) || !_anim || state == _state) return;
            if (!_anim.runtimeAnimatorController) return;
            _anim.CrossFade(state, 0.12f, 0, 0f);
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

        bool HasParam(string name)
        {
            if (!_anim) return false;
            var ps = _anim.parameters;
            for (int i = 0; i < ps.Length; i++) if (ps[i].name == name) return true;
            return false;
        }
    }
}
