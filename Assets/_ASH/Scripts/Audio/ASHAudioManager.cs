using System.Collections.Generic;
using ASH.Core;
using ASH.Wildlife;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.Audio
{
    /// <summary>
    /// Persistent soundtrack: shuffled background tracks with crossfades and gaps, plus Forward2 combat music
    /// that fades in as soon as a hostile is aggro'd on the player or the dog, and fades out after none remain
    /// for the Play-editable delay. Creates itself at runtime (DontDestroyOnLoad); no scene save needed.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class ASHAudioManager : MonoBehaviour
    {
        public static ASHAudioManager Instance { get; private set; }

        ASHAudioSettings S => ASHAudioSettings.I;
        AudioSource _a, _b, _combat;
        int _fg;
        readonly List<AudioClip> _bag = new List<AudioClip>();
        AudioClip _last;
        float _gapUntil, _combatWant, _combatVol, _bgmMul = 1f, _clearSince = -1f;
        bool _combatOn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance) return;
            var go = new GameObject("ASH_AudioManager");
            DontDestroyOnLoad(go);
            go.AddComponent<ASHAudioManager>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _a = MakeSource("BgmA");
            _b = MakeSource("BgmB");
            _combat = MakeSource("Combat");
            _combat.loop = true;
            SceneManager.sceneLoaded += OnScene;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnScene;
        }

        void OnScene(Scene s, LoadSceneMode m) { /* keep playing across scenes */ }

        AudioSource MakeSource(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            var gain = child.AddComponent<ASHAudioGain>();
            gain.category = ASHAudioGain.Category.Music;
            if (S && S.musicGroup) src.outputAudioMixerGroup = S.musicGroup;
            return src;
        }

        void Update()
        {
            if (S == null) return;
            TickCombat();
            TickBackground();
        }

        void TickBackground()
        {
            var clips = S.background;
            if (clips == null || clips.Count == 0) return;
            var fg = _fg == 0 ? _a : _b;
            var bg = _fg == 0 ? _b : _a;
            float fade = Mathf.Max(0.05f, S.crossfadeSeconds);
            float vol = Mathf.Clamp01(S.backgroundVolume) * _bgmMul;

            if (!fg.isPlaying && Time.unscaledTime >= _gapUntil)
            {
                var clip = NextClip();
                if (!clip) return;
                fg.clip = clip;
                fg.volume = 0f;
                fg.Play();
            }

            bool ending = fg.isPlaying && fg.clip && fg.clip.length > fade && (fg.clip.length - fg.time) <= fade && !bg.isPlaying;
            if (ending)
            {
                float gap = Random.Range(Mathf.Min(S.gapMinSeconds, S.gapMaxSeconds), Mathf.Max(S.gapMinSeconds, S.gapMaxSeconds));
                if (gap <= 0.05f)
                {
                    var clip = NextClip();
                    if (clip)
                    {
                        bg.clip = clip;
                        bg.volume = 0f;
                        bg.Play();
                    }
                    _fg = 1 - _fg;
                }
                else
                {
                    // fade out, then wait the gap before the next track
                    _gapUntil = Time.unscaledTime + fade + gap;
                }
            }

            FadeToward(fg, fg.isPlaying ? vol : 0f, fade);
            FadeToward(bg, (bg.isPlaying && bg != fg) ? vol : 0f, fade);
            if (!fg.isPlaying && !_a.isPlaying && !_b.isPlaying && Time.unscaledTime < _gapUntil)
                FadeToward(_a, 0f, fade);
        }

        AudioClip NextClip()
        {
            var src = S.background;
            if (src == null) return null;
            if (_bag.Count == 0)
            {
                foreach (var c in src) if (c && c != S.combat) _bag.Add(c);
                for (int i = _bag.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    var t = _bag[i]; _bag[i] = _bag[j]; _bag[j] = t;
                }
                if (_bag.Count > 1 && _last && _bag[0] == _last)
                {
                    var t = _bag[0]; _bag[0] = _bag[1]; _bag[1] = t;
                }
            }
            if (_bag.Count == 0) return null;
            _last = _bag[0];
            _bag.RemoveAt(0);
            return _last;
        }

        void TickCombat()
        {
            bool aggro = HostileAggro.Any();
            if (aggro)
            {
                _clearSince = -1f;
                _combatOn = true;
                _combatWant = 1f;
                _bgmMul = Mathf.MoveTowards(_bgmMul, Mathf.Clamp01(S.backgroundDuck), Time.unscaledDeltaTime / Mathf.Max(0.1f, S.combatFadeInSeconds));
                if (!_combat.isPlaying && S.combat)
                {
                    _combat.clip = S.combat;
                    _combat.volume = 0f;
                    _combat.Play();
                }
            }
            else if (_combatOn)
            {
                if (_clearSince < 0f) _clearSince = Time.unscaledTime;
                if (Time.unscaledTime - _clearSince >= Mathf.Max(0f, S.combatEndDelaySeconds))
                    _combatWant = 0f;
            }

            float speed = _combatWant > 0.5f ? S.combatFadeInSeconds : S.combatFadeOutSeconds;
            _combatVol = Mathf.MoveTowards(_combatVol, _combatWant, Time.unscaledDeltaTime / Mathf.Max(0.05f, speed));
            if (_combat)
            {
                _combat.volume = _combatVol * Mathf.Clamp01(S.combatVolume);
                if (_combatVol <= 0.001f && _combat.isPlaying && _combatWant <= 0f)
                {
                    _combat.Stop();
                    _combatOn = false;
                    _clearSince = -1f;
                }
            }
            if (!_combatOn && _combatWant <= 0f)
                _bgmMul = Mathf.MoveTowards(_bgmMul, 1f, Time.unscaledDeltaTime / Mathf.Max(0.1f, S.combatFadeOutSeconds));
        }

        static void FadeToward(AudioSource src, float target, float seconds)
        {
            if (!src) return;
            src.volume = Mathf.MoveTowards(src.volume, target, Time.unscaledDeltaTime / Mathf.Max(0.05f, seconds));
            if (src.volume <= 0.001f && target <= 0f && src.isPlaying && src.time > 0.05f && src.clip && src.time >= src.clip.length - 0.05f)
                src.Stop();
        }
    }

    /// <summary>True when any hostile animal is aggro'd on the player or the pet dog.</summary>
    public static class HostileAggro
    {
        public static bool Any()
        {
            var all = Object.FindObjectsByType<ASHAnimal>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
                if (all[i] && all[i].IsAggroOnPlayerOrDog) return true;
            return false;
        }
    }
}
