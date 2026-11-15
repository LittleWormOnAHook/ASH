using ASH.Interaction;
using ASH.World.Weather;
using UnityEngine;

namespace ASH.Opening
{
    /// <summary>
    /// Grandpa's radio: low music, the emergency alert, the announcement (narration clips when present, otherwise subtitles only)
    /// over a static bed with random signal dropouts, and static interruptions while the cabin shakes. All sound is 3D from the
    /// radio, band-limited like a small speaker. Subtitles go through ASHHud.ShowSubtitle. Usable with E (turn on/off) outside the
    /// broadcast. Generated audio fills in for a missing alert tone / static loop (original audio, no license needed).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHRadioController : MonoBehaviour, IASHUsable
    {
        public enum Mode { Off, Music, Alert, Voice, StaticOnly }
        public ASHPhase2AAudio audioBank;
        public ASHOpeningProfile profile;
        [Range(0.5f, 20f)] public float minDistance = 1.2f;
        [Range(2f, 80f)] public float maxDistance = 22f;
        [Tooltip("Small-speaker band: low / high cut (Hz).")] public Vector2 band = new Vector2(320f, 4800f);
        [Range(0f, 1f)] public float distortion = 0.35f;

        AudioSource _main, _static; AudioLowPassFilter _lp; AudioHighPassFilter _hp; AudioDistortionFilter _dist;
        Mode _mode = Mode.Off; float _modeT, _lineT, _dropUntil, _interrupt, _musicVol; int _line; ASHOpeningProfile.Line[] _lines; bool _userOff;
        UI.ASHHud _hud;
        public Mode Current => _mode;
        public int LineIndex => _line;
        public float MusicVolume => _mode == Mode.Music && _main ? _main.volume : 0f;
        public bool Playing => _main && _main.isPlaying;
        public string LastSubtitle { get; private set; } = "";
        public int SubtitlesShown { get; private set; }

        void Awake()
        {
            _main = Src("Speaker"); _static = Src("Static");
            _lp = _main.gameObject.AddComponent<AudioLowPassFilter>(); _hp = _main.gameObject.AddComponent<AudioHighPassFilter>(); _dist = _main.gameObject.AddComponent<AudioDistortionFilter>();
            _static.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 6000f;
            _static.clip = audioBank && audioBank.staticLoop ? audioBank.staticLoop : GenStatic(); _static.loop = true; _static.volume = 0f; _static.Play();
        }

        AudioSource Src(string n)
        {
            var go = new GameObject(n); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); go.AddComponent<ASH.Core.ASHAudioGain>().category = ASH.Core.ASHAudioGain.Category.Music; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = minDistance; s.maxDistance = maxDistance; s.dopplerLevel = 0f; s.playOnAwake = false;
            return s;
        }

        UI.ASHHud Hud { get { if (!_hud) _hud = FindAnyObjectByType<UI.ASHHud>(); return _hud; } }

        // ---------------------------------------------------------------- control (ASHEruptionSequence)
        public void PlayMusic()
        {
            _userOff = false; var clip = audioBank ? ASH.Gameplay.ASHSfx.Pick(audioBank.radioMusic) : null;
            _musicVol = profile ? profile.musicVolume : 0.28f; SetMode(Mode.Music);
            if (clip) { _main.clip = clip; _main.loop = true; _main.volume = _musicVol; _main.time = Random.Range(0f, clip.length * 0.3f); _main.Play(); }
        }
        public void StartBroadcast() { SetMode(Mode.Alert); _userOff = false; }
        public void StaticFragments() { _lines = profile ? profile.afterQuake : null; _line = -1; _lineT = 0f; SetMode(Mode.StaticOnly); }
        public void Stop() { SetMode(Mode.Off); _main.Stop(); _static.volume = 0f; }
        /// <summary>0-1 from the shaking: static swamps the speaker.</summary>
        public void Interrupt(float amount) { _interrupt = Mathf.Max(_interrupt, Mathf.Clamp01(amount)); }

        void SetMode(Mode m) { _mode = m; _modeT = 0f; }

        void Update()
        {
            float dt = Time.deltaTime; _modeT += dt;
            float bv = profile ? profile.broadcastVolume : 0.75f, sv = profile ? profile.staticVolume : 0.12f;
            _lp.cutoffFrequency = band.y; _hp.cutoffFrequency = band.x;
            float drop = Time.time < _dropUntil ? 1f : 0f;
            switch (_mode)
            {
                case Mode.Off: _static.volume = Mathf.MoveTowards(_static.volume, 0f, dt); break;
                case Mode.Music:
                    _main.volume = Mathf.MoveTowards(_main.volume, _userOff ? 0f : _musicVol, dt * 0.5f);
                    _static.volume = Mathf.MoveTowards(_static.volume, _userOff ? 0f : sv * 0.15f, dt); _dist.distortionLevel = distortion * 0.3f;
                    break;
                case Mode.Alert:
                    float fade = profile ? profile.musicFadeSeconds : 1.5f;
                    if (_modeT < fade) { _main.volume = Mathf.Lerp(_musicVol, 0f, _modeT / fade); break; }
                    if (_main.clip == null || _main.loop) { _main.loop = false; _main.clip = audioBank && audioBank.alertTone ? audioBank.alertTone : GenAlert(profile ? profile.alertSeconds : 7f); _main.volume = bv * 0.8f; _main.Play(); _dist.distortionLevel = distortion * 0.5f; Subtitle("Radio", "[Emergency alert tone]", 4f); }
                    _static.volume = Mathf.MoveTowards(_static.volume, sv * 0.5f, dt);
                    if (_modeT > fade + (profile ? profile.alertSeconds : 7f)) { _main.Stop(); _main.clip = null; _lines = profile ? profile.broadcast : null; _line = -1; _lineT = 0f; SetMode(Mode.Voice); }
                    break;
                case Mode.Voice:
                case Mode.StaticOnly:
                    bool voice = _mode == Mode.Voice;
                    _lineT -= dt;
                    if (_lineT <= 0f) NextLine(voice);
                    if (voice && profile && Random.value < profile.dropoutsPerMinute / 60f * dt) _dropUntil = Time.time + Random.Range(0.15f, 0.6f);
                    float sTarget = voice ? sv : sv * 2.2f;
                    _static.volume = Mathf.MoveTowards(_static.volume, Mathf.Clamp01(sTarget + drop * 0.25f + _interrupt * 0.6f), dt * 3f);
                    _main.volume = bv * (1f - 0.85f * drop) * (1f - 0.8f * _interrupt);
                    _dist.distortionLevel = Mathf.Clamp01(distortion * 0.6f + drop * 0.3f + _interrupt * 0.4f);
                    break;
            }
            _interrupt = Mathf.MoveTowards(_interrupt, 0f, dt * 0.8f);
        }

        void NextLine(bool voice)
        {
            if (_lines == null || _lines.Length == 0) { if (voice) SetMode(Mode.StaticOnly); return; }
            _line++;
            if (_line >= _lines.Length)
            {
                if (voice) { StaticFragments(); return; }
                _line = 0; _lineT = Random.Range(6f, 14f); return; // fragments repeat with long static gaps
            }
            var l = _lines[_line]; _lineT = l.seconds + (voice ? 0.6f : Random.Range(4f, 9f));
            var vc = voice && audioBank != null && audioBank.broadcastVoice != null && _line < audioBank.broadcastVoice.Length ? audioBank.broadcastVoice[_line] : null;
            if (vc) { _main.loop = false; _main.clip = vc; _main.Play(); }
            if (!voice && audioBank) ASH.Gameplay.ASHSfx.Play("radioStatic", audioBank.staticBursts, transform.position, 0.5f, 1f, 0.1f, 1f, maxDistance);
            Subtitle(l.speaker, l.text, l.seconds);
        }

        void Subtitle(string who, string text, float seconds)
        {
            LastSubtitle = text; SubtitlesShown++;
            var h = Hud; if (h) h.ShowSubtitle(string.IsNullOrEmpty(who) ? text : $"<color=#d8c48a>{who}:</color> {text}", seconds);
        }

        // ---------------------------------------------------------------- generated audio (original)
        static AudioClip _genStatic, _genAlert;
        static AudioClip GenStatic()
        {
            if (_genStatic) return _genStatic;
            int sr = 22050, n = sr * 4; var d = new float[n]; float b = 0f, crackle = 0f; var r = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float w = (float)(r.NextDouble() * 2 - 1); b = b * 0.6f + w * 0.4f; // softened hiss
                if (r.NextDouble() < 0.0008) crackle = (float)(r.NextDouble() * 0.9);
                crackle *= 0.97f; d[i] = (b * 0.35f + crackle * w) * (0.85f + 0.15f * Mathf.Sin(i * 0.0007f));
            }
            for (int i = 0; i < 400; i++) { float k = i / 400f; d[i] *= k; d[n - 1 - i] *= k; }
            _genStatic = AudioClip.Create("ASH_GenStatic", n, 1, sr, false); _genStatic.SetData(d, 0); return _genStatic;
        }
        static AudioClip GenAlert(float seconds)
        {
            if (_genAlert && Mathf.Abs(_genAlert.length - seconds) < 0.1f) return _genAlert;
            int sr = 22050, n = Mathf.Max(1, (int)(sr * seconds)); var d = new float[n];
            for (int i = 0; i < n; i++)
            {   // two-tone attention signal (853 Hz + 960 Hz), short ramps
                float t = i / (float)sr; float env = Mathf.Clamp01(t * 20f) * Mathf.Clamp01((seconds - t) * 20f);
                d[i] = 0.35f * env * (Mathf.Sin(2f * Mathf.PI * 853f * t) + Mathf.Sin(2f * Mathf.PI * 960f * t));
            }
            _genAlert = AudioClip.Create("ASH_GenAlert", n, 1, sr, false); _genAlert.SetData(d, 0); return _genAlert;
        }

        // ---------------------------------------------------------------- IASHUsable (turn the radio on/off while it only plays music)
        public Transform UseTransform => transform;
        public float GetUsePriority(in ASHUseContext c) => _mode == Mode.Music || _mode == Mode.Off ? 1f : -1f;
        public string GetPrompt(in ASHUseContext c) => _mode == Mode.Music && !_userOff ? "Turn off Grandpa's radio" : "Turn on Grandpa's radio";
        public float GetHoldDuration(in ASHUseContext c) => 0f;
        public bool TryUse(in ASHUseContext c)
        {
            if (_mode == Mode.Off) { PlayMusic(); return true; }
            if (_mode != Mode.Music) return false;
            _userOff = !_userOff; return true;
        }
    }
}
