using ASH.Gameplay;
using ASH.Map;
using ASH.World;
using ASH.World.Accumulation;
using ASH.World.Weather;
using Invector.vCharacterController;
using ASH.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.Opening
{
    /// <summary>
    /// Phase 2A "The Last Normal Morning": Morning (radio music) -> Broadcast (alert + announcement) -> Quake -> Quiet ->
    /// FirstAsh (slow ramp) -> HeavyWave (objective) -> Easing -> Done (weather scheduler takes over). Owns only timing;
    /// the weather look/sound comes from ASHWeatherDirector + ASHWeatherOutputs, the shaking from ASHEarthquakeEffects,
    /// the radio from ASHRadioController, the objective from ASHOpeningObjective on the existing tracker.
    /// Player control is never taken away. Debug: Replay(), SkipTo(stage), debugSpeed.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-30)]
    public sealed class ASHEruptionSequence : MonoBehaviour
    {
        public enum Stage { Inactive, Morning, Broadcast, Quake, Quiet, FirstAsh, HeavyWave, Easing, Done }
        public ASHOpeningProfile profile;
        [Tooltip("Run the opening when Play starts. Off = the scene starts in the director's initial weather.")]
        public bool playOnStart = true;
        [Tooltip("Player is moved here when the opening starts (inside the cabin). The scene spawn is left untouched.")]
        public Transform openingSpawn;
        public ASHWeatherDirector director;
        public ASHRadioController radio;
        public ASHEarthquakeEffects quake;
        public ASHOpeningObjective objective;
        public ASHDayNightClock clock;
        [Header("Debug")]
        [Tooltip("Time multiplier for the whole opening (testing).")] [Range(0.1f, 50f)] public float debugSpeed = 1f;
        public bool logStages = true;

        public static ASHEruptionSequence Instance { get; private set; }
        public Stage Current { get; private set; } = Stage.Inactive;
        public float StageTime => _t;
        public bool Suppressed { get; private set; }
        public event System.Action<Stage> StageChanged;

        // opening-owned fog density (own runtime volume above the weather volume, so nothing blends against it)
        Volume _fogVol; VolumeProfile _fogProf; ASHVolumetricFog _fogOv;
        float _fogFrom, _fogTo, _fogT = 1f, _fogDur = 1f, _fogW = 1f, _fogWTarget = 1f, _fogWSpeed; AnimationCurve _fogCurve;
        public float FogDensity => _fogOv != null ? _fogOv.density.value : -1f;

        float _t; bool _quakeStarted, _ashStarted; Vector3 _origPos; Quaternion _origRot; bool _haveOrig; vThirdPersonController _cc;

        void Awake() { Instance = this; if (playOnStart) ASHObjectiveTracker.HoldIntroMessage = true; }
        void OnDestroy() { if (Instance == this) Instance = null; ASHObjectiveTracker.HoldIntroMessage = false; }

        void Start()
        {
            if (!director) director = ASHWeatherDirector.Instance;
            if (!clock) clock = FindAnyObjectByType<ASHDayNightClock>();
            _cc = FindAnyObjectByType<vThirdPersonController>();
            if (_cc) { _origPos = _cc.transform.position; _origRot = _cc.transform.rotation; _haveOrig = true; }
            if (playOnStart && profile) Begin();
        }

        // ------------------------------------------------------------------ control
        public void Begin()
        {
            if (!profile) { Debug.LogWarning("[ASH] Opening: no profile"); return; }
            Suppressed = false; _journalBroadcast = false; _quakeStarted = false; _ashStarted = false;
            ASHQuakeProp.ResetAll();
            if (quake) quake.Stop();
            if (objective) objective.End();
            if (director) { director.SetScheduler(false); director.Snap(director.Profile(profile.startState) ? profile.startState : ASHWeatherKind.Clear); }
            FogSet(profile.startFogDensity); FogWeight(1f, 0f);
            var acc = ASHAccumulationController.Instance;
            if (acc) { if (profile.groundAshAtStart >= 0f) acc.ashAmount = profile.groundAshAtStart; if (profile.groundSnowAtStart >= 0f) acc.snowAmount = profile.groundSnowAtStart; }
            if (clock && profile.startTimeOfDay >= 0f) clock.timeOfDay = profile.startTimeOfDay;
            var ash = FindAnyObjectByType<ASHAshFall>(); if (ash) ash.SetWeather(0f, Vector3.zero, 1f, true);
            if (openingSpawn) TeleportPlayer(openingSpawn.position, openingSpawn.rotation);
            Enter(Stage.Morning);
            if (radio) radio.PlayMusic();
        }

        public void Replay() => Begin();

        public void SkipTo(Stage s)
        {
            if (Current == Stage.Inactive || s <= Stage.Morning) { Begin(); if (s <= Stage.Morning) return; }
            // bring the world into the state the skipped stages would have produced
            if (s >= Stage.Broadcast && radio && s == Stage.Broadcast) radio.StartBroadcast();
            if (s > Stage.Broadcast) { JournalBroadcast(); if (radio) radio.StaticFragments(); }
            if (s > Stage.Quake && quake) quake.Stop();
            if (director)
            {
                if (s == Stage.FirstAsh) director.Snap(ASHWeatherKind.CalmHazy);
                if (s == Stage.HeavyWave) director.Snap(ASHWeatherKind.LightAsh);
                if (s == Stage.Easing) director.Snap(ASHWeatherKind.HeavyAsh);
                if (s == Stage.Done) director.Snap(profile.afterState);
            }
            _quakeStarted = s > Stage.Quake; _ashStarted = s > Stage.FirstAsh;
            if (s <= Stage.FirstAsh) FogSet(profile.startFogDensity); else if (s <= Stage.Easing) FogSet(profile.ashFogDensity); else FogSet(profile.easedFogDensity);
            FogWeight(1f, 0f);
            Enter(s);
            if (s == Stage.Quake && quake) { quake.Begin(); _quakeStarted = true; }
        }

        /// <summary>Automated tests (ASHPlayTest) need the regular slice start: stop everything and return the player
        /// to the scene spawn position.</summary>
        public void SuppressForTests()
        {
            Suppressed = true;
            if (quake) quake.Stop();
            if (radio) radio.Stop();
            if (objective) objective.End();
            ASHQuakeProp.ResetAll();
            if (director) { director.SetScheduler(false); director.debugOverride = false; director.Snap(ASHWeatherKind.Clear); }
            FogWeight(0f, 0f);
            var ash = FindAnyObjectByType<ASHAshFall>(); if (ash) ash.SetWeather(0f, Vector3.zero, 1f, true);
            if (_haveOrig) TeleportPlayer(_origPos, _origRot);
            var hud = FindAnyObjectByType<UI.ASHHud>(); if (hud) hud.ShowSubtitle(null, 0f);
            ASHObjectiveTracker.HoldIntroMessage = false;
            Current = Stage.Inactive; StageChanged?.Invoke(Current);
            Debug.Log("[ASH] Opening suppressed for tests");
        }

        /// <summary>Called by ASHPlayTest.Run (no-op when no opening is in the scene).</summary>
        public static void SuppressIfPresent() { var s = Instance ? Instance : FindAnyObjectByType<ASHEruptionSequence>(); if (s) s.SuppressForTests(); }

        void TeleportPlayer(Vector3 p, Quaternion r)
        {
            if (!_cc) _cc = FindAnyObjectByType<vThirdPersonController>(); if (!_cc) return;
            var rb = _cc.GetComponent<Rigidbody>(); if (rb) { rb.linearVelocity = Vector3.zero; rb.position = p + Vector3.up * 0.05f; rb.rotation = r; }
            _cc.transform.SetPositionAndRotation(p + Vector3.up * 0.05f, r);
        }

        void Enter(Stage s)
        {
            Current = s; _t = 0f;
            if (logStages) Debug.Log($"[ASH] Opening stage -> {s}");
            StageChanged?.Invoke(s);
            float sp = Mathf.Max(0.01f, debugSpeed);
            switch (s)
            {
                case Stage.Broadcast: if (radio) radio.StartBroadcast(); break;
                case Stage.Quiet: if (director) director.TransitionTo(ASHWeatherKind.CalmHazy, profile.hazeSeconds / sp); break;
                case Stage.FirstAsh: break; // ramp starts after ashOnset (see Tick)
                case Stage.HeavyWave:
                    if (director) director.TransitionTo(ASHWeatherKind.HeavyAsh, profile.heavyRampSeconds / sp);
                    if (objective) objective.Begin(profile.objectiveTitle, profile.objectiveMessage);
                    ASHJournalLog.Add(profile.journalWaveTitle, profile.journalWaveBody);
                    break;
                case Stage.Easing:
                    if (director) director.TransitionTo(profile.afterState, profile.easeSeconds / sp);
                    if (objective) objective.MarkEased();
                    FogRamp(profile.easedFogDensity, profile.easeSeconds / sp, null);
                    break;
                case Stage.Done:
                    if (objective) objective.End(objective.AllDone ? "The first wave has passed." : "The first wave has passed. Keep preparing - more will come.");
                    if (director && profile.startSchedulerAfter) director.SetScheduler(true);
                    FogWeight(0f, profile.fogHandoffSeconds / sp);
                    var tr = FindAnyObjectByType<ASHObjectiveTracker>(); if (tr && ASHObjectiveTracker.HoldIntroMessage) tr.ReplayIntro();
                    ASHObjectiveTracker.HoldIntroMessage = false;
                    break;
            }
        }

        bool _journalBroadcast;
        void JournalBroadcast() { if (_journalBroadcast) return; _journalBroadcast = true; ASHJournalLog.Add(profile.journalBroadcastTitle, profile.journalBroadcastBody); }

        // ------------------------------------------------------------------ fog density
        void EnsureFogVolume()
        {
            if (_fogVol) return;
            var go = new GameObject("ASH_OpeningFogVolume") { hideFlags = HideFlags.DontSave }; go.transform.SetParent(transform, false);
            _fogVol = go.AddComponent<Volume>(); _fogVol.isGlobal = true; _fogVol.priority = 60f; _fogVol.weight = 0f;
            _fogProf = ScriptableObject.CreateInstance<VolumeProfile>(); _fogProf.name = "ASH_OpeningFog (runtime)"; _fogProf.hideFlags = HideFlags.DontSave;
            _fogOv = _fogProf.Add<ASHVolumetricFog>(false); _fogVol.sharedProfile = _fogProf;
        }
        void FogSet(float d) { _fogFrom = _fogTo = d; _fogT = 1f; ApplyFog(); }
        void FogRamp(float to, float seconds, AnimationCurve curve) { _fogFrom = CurrentFog(); _fogTo = to; _fogDur = Mathf.Max(0.05f, seconds); _fogT = 0f; _fogCurve = curve; }
        void FogWeight(float w, float seconds) { _fogWTarget = w; if (seconds <= 0f) _fogW = w; else _fogWSpeed = Mathf.Abs(w - _fogW) / seconds; ApplyFog(); }
        float CurrentFog() { float k = _fogCurve != null && _fogCurve.length > 1 ? Mathf.Clamp01(_fogCurve.Evaluate(_fogT)) : Mathf.SmoothStep(0f, 1f, _fogT); return Mathf.LerpUnclamped(_fogFrom, _fogTo, k); }
        void ApplyFog()
        {
            if (!profile || !profile.driveFogDensity) { if (_fogVol) _fogVol.weight = 0f; return; }
            EnsureFogVolume();
            _fogOv.enabled.Override(true); _fogOv.density.Override(Mathf.Max(0f, CurrentFog())); _fogOv.densityMultiplier.Override(profile.fogDensityMultiplier);
            _fogVol.weight = _fogW;
        }
        void TickFog(float dt)
        {
            if (_fogT < 1f) _fogT = Mathf.Min(1f, _fogT + dt / _fogDur);
            if (_fogW != _fogWTarget) _fogW = Mathf.MoveTowards(_fogW, _fogWTarget, Mathf.Max(1e-4f, _fogWSpeed) * dt);
            ApplyFog();
        }
        void OnDisable() { if (_fogVol) Destroy(_fogVol.gameObject); if (_fogProf) Destroy(_fogProf); _fogVol = null; _fogProf = null; _fogOv = null; }

        void Update()
        {
            if (_fogVol) TickFog(Time.deltaTime);
            if (Suppressed || !profile || Current == Stage.Inactive || Current == Stage.Done) return;
            float sp = Mathf.Max(0.01f, debugSpeed);
            _t += Time.deltaTime * sp;
            switch (Current)
            {
                case Stage.Morning:
                    if (_t >= profile.peacefulSeconds) Enter(Stage.Broadcast);
                    break;
                case Stage.Broadcast:
                    if (_t >= profile.musicFadeSeconds + profile.alertSeconds + 4f) JournalBroadcast();
                    if (_t >= profile.musicFadeSeconds + profile.alertSeconds + profile.quakeDelay) { Enter(Stage.Quake); if (quake) quake.Begin(); _quakeStarted = true; }
                    break;
                case Stage.Quake:
                    if (!quake || quake.Done || (_t > 1f && !quake.Running)) { if (radio) radio.StaticFragments(); Enter(Stage.Quiet); }
                    break;
                case Stage.Quiet:
                    if (_t >= profile.quietSeconds) Enter(Stage.FirstAsh);
                    break;
                case Stage.FirstAsh:
                    if (!_ashStarted && _t >= profile.ashOnsetSeconds) { _ashStarted = true; if (director) director.TransitionTo(ASHWeatherKind.LightAsh, profile.ashRampSeconds / sp); FogRamp(profile.ashFogDensity, profile.fogRampSeconds / sp, profile.fogRampCurve); }
                    if (_t >= profile.ashOnsetSeconds + profile.ashRampSeconds) Enter(Stage.HeavyWave);
                    break;
                case Stage.HeavyWave:
                    if (_t >= profile.heavyRampSeconds + profile.heavyWaveSeconds) Enter(Stage.Easing);
                    break;
                case Stage.Easing:
                    if (_t >= profile.easeSeconds) Enter(Stage.Done);
                    break;
            }
        }

        public string Describe() => $"stage={Current} t={_t:F1}s speed={debugSpeed:F1} weather=({(director ? director.Describe() : "none")})";
    }
}
