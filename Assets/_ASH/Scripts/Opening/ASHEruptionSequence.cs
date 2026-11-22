using ASH.Gameplay;
using ASH.Interaction;
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
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHEruptionSequence()
        {
            Instance = default;
        }

        public enum Stage { Inactive, Morning, Broadcast, Quake, Quiet, FirstAsh, HeavyWave, Easing, Done }
        public ASHOpeningProfile profile;
        [Tooltip("Run the opening when Play starts. Off = the scene starts in the director's initial weather.")]
        public bool playOnStart = true;
        [Tooltip("Testing: NEW games skip the radio dialogue and the earthquake and start just as the cabin door unlocks " +
                 "(broadcast journal note written, quake over, door unlocked, 'Secure the cabin' active, aftershocks as normal). " +
                 "Loaded saves are not affected. Off by default.")]
        public bool skipStartSequence = false;
        [Tooltip("Player is moved here when the opening starts (inside the cabin). The scene spawn is left untouched.")]
        public Transform openingSpawn;
        public ASHWeatherDirector director;
        public ASHRadioController radio;
        public ASHEarthquakeEffects quake;
        public ASHOpeningObjective objective;
        public ASHDayNightClock clock;
        [Tooltip("Cabin door(s) locked until Profile > Lock Doors Until.")] public ASHDoor[] cabinDoors;
        [Tooltip("Invisible colliders over other cabin exits (windows/gaps), active while the doors are locked.")] public GameObject exitBlockers;
        [Header("Debug")]
        [Tooltip("Time multiplier for the whole opening (testing).")] [Range(0.1f, 50f)] public float debugSpeed = 1f;
        public bool logStages = true;

        public static ASHEruptionSequence Instance { get; private set; }
        public Stage Current { get; private set; } = Stage.Inactive;
        public float StageTime => _t;
        public bool Suppressed { get; private set; }
        public event System.Action<Stage> StageChanged;

        // opening-owned fog Density Multiplier (own runtime volume above the weather volume, so nothing blends against it)
        Volume _fogVol; VolumeProfile _fogProf; ASHVolumetricFog _fogOv;
        float _fogFrom, _fogTo, _fogT = 1f, _fogDur = 1f, _fogW = 1f, _fogWTarget = 1f, _fogWSpeed; AnimationCurve _fogCurve;
        public float FogMultiplier => _fogOv != null ? _fogOv.densityMultiplier.value : -1f;

        float _unlockTimer = -1f; public bool CabinLocked { get; private set; }
        float _t; bool _quakeStarted, _ashStarted; Vector3 _origPos; Quaternion _origRot; bool _haveOrig; vThirdPersonController _cc;

        void Awake() { Instance = this; if (playOnStart) { ASHObjectiveTracker.HoldIntroMessage = true; ASHObjectiveTracker.HoldSlice = true; } } // the slice ("Get your bearings") waits for the opening
        void OnDestroy() { if (Instance == this) Instance = null; ASHObjectiveTracker.HoldIntroMessage = false; ASHObjectiveTracker.HoldSlice = false; }

        /// <summary>Start the slice objectives ("Get your bearings" ...) now: opening Done (Secure the cabin has ended), suppressed, or not running.</summary>
        void ReleaseSlice()
        {
            if (ASH.Prologue.ASHPrologueDirector.BlocksLegacySlice) { ASHObjectiveTracker.HoldIntroMessage = false; return; } // M1: legacy slice is debug-only
            var tr = FindAnyObjectByType<ASHObjectiveTracker>();
            if (tr) tr.StartSlice(); else { ASHObjectiveTracker.HoldSlice = false; ASHObjectiveTracker.HoldIntroMessage = false; }
        }

        void Start()
        {
            if (!director) director = ASHWeatherDirector.Instance;
            if (!clock) clock = FindAnyObjectByType<ASHDayNightClock>();
            _cc = FindAnyObjectByType<vThirdPersonController>();
            if (_cc) { _origPos = _cc.transform.position; _origRot = _cc.transform.rotation; _haveOrig = true; }
            if (ASH.Core.ASHSaveSystem.PendingLoad != null) return; // loading a save: RestoreFromSave sets the stage (no replay / skip)
            if (ASH.Prologue.ASHPrologueDirector.TakesOverOpening) { if (profile) PrepareWorld(); if (logStages) Debug.Log("[ASH] Opening: prologue car start (cabin opening not run; the warning starts it later)"); return; }
            if (playOnStart && profile) { if (skipStartSequence) SkipStartSequence(); else Begin(); } else ReleaseSlice();
        }

        /// <summary>New game with Skip Start Sequence: jump to the stage where the cabin door unlocks and unlock it now.
        /// Uses SkipTo, so the world gets exactly what the skipped stages would have produced (broadcast journal note, quake
        /// stopped, haze weather, fog, "Secure the cabin" objective) and saves / later objectives see a normal run.</summary>
        void SkipStartSequence()
        {
            var us = UnlockStage;
            var target = profile.lockDoorsUntil == DoorUnlockAt.Never || us <= Stage.Quiet ? Stage.Quiet : us;
            SkipTo(target);
            if (radio) radio.StaticFragments(); // radio after the broadcast: static only (no music / dialogue)
            UnlockCabin(true);
            if (logStages) Debug.Log($"[ASH] Opening: start sequence skipped -> {target}, cabin unlocked");
        }

        /// <summary>True when "Secure the cabin" was finished early (the stage timers still run until Done).</summary>
        public bool ObjectiveFinishedEarly => Current >= Stage.Quiet && Current < Stage.Done && objective && !objective.Active;

        /// <summary>Save restore. Done/Inactive: the world is left as after the opening (doors unlocked, no objective, aftershock
        /// state kept). Earlier stages restart at the start of the saved stage (same as Debug > Skip To), so loading can neither
        /// replay nor skip the opening.</summary>
        public void RestoreFromSave(Stage s, bool aftershocksOver, bool suppressed, bool objectiveFinished = false)
        {
            if (!profile) return;
            _aftershocksOver = aftershocksOver; _nextAftershock = -1f;
            if (suppressed || s == Stage.Inactive || s == Stage.Done)
            {
                if (quake) quake.Stop();
                if (radio) { if (s == Stage.Done && !suppressed) radio.StaticFragments(); else radio.Stop(); } // after the broadcast: static only, inside the cabin
                EndObjective(); ASHQuakeProp.ResetAll();
                UnlockCabin(false);
                _journalBroadcast = true; _quakeStarted = true; _ashStarted = true;
                FogSet(profile.ashFogMultiplier); FogWeight(0f, 0f);
                Suppressed = suppressed;
                Current = suppressed ? Stage.Inactive : s; _t = 0f;
                StageChanged?.Invoke(Current);
                if (logStages) Debug.Log($"[ASH] Opening restored at {Current}");
                return;
            }
            SkipTo(s); // from Inactive this runs Begin() first (doors, spawn, start state); the core save then restores pose/time/weather
            if (objectiveFinished && objective && objective.Active) { objective.Completed -= OnObjectiveComplete; objective.End(); }
            _aftershocksOver = aftershocksOver;
            if (logStages) Debug.Log($"[ASH] Opening restored at {s}");
        }

        // ------------------------------------------------------------------ control
        public void Begin()
        {
            if (!profile) { Debug.LogWarning("[ASH] Opening: no profile"); ReleaseSlice(); return; }
            ASHObjectiveTracker.HoldSlice = true; // (re)running the opening: the slice waits for Done again
            _aftershocksOver = false; _nextAftershock = -1f;
            Suppressed = false; _journalBroadcast = false; _quakeStarted = false; _ashStarted = false;
            ASHQuakeProp.ResetAll();
            if (quake) quake.Stop();
            EndObjective();
            if (director) { director.SetScheduler(false); director.Snap(director.Profile(profile.startState) ? profile.startState : ASHWeatherKind.Clear); }
            FogSet(profile.startFogMultiplier); FogWeight(1f, 0f);
            var acc = ASHAccumulationController.Instance;
            if (acc) { if (profile.groundAshAtStart >= 0f) acc.ashAmount = profile.groundAshAtStart; if (profile.groundSnowAtStart >= 0f) acc.snowAmount = profile.groundSnowAtStart; }
            if (clock && profile.startTimeOfDay >= 0f) clock.timeOfDay = profile.startTimeOfDay;
            var ash = FindAnyObjectByType<ASHAshFall>(); if (ash) ash.SetWeather(0f, Vector3.zero, 1f, true);
            if (openingSpawn) TeleportPlayer(openingSpawn.position, openingSpawn.rotation);
            if (profile.lockDoorsUntil != DoorUnlockAt.Never) LockCabin(); else UnlockCabin(false);
            Enter(Stage.Morning);
            if (radio) radio.PlayMusic();
        }

        public void Replay() => Begin();

        /// <summary>M1 prologue new game: the opening's start state (weather, fog, ground cover, time) without the cabin spawn,
        /// door lock, music or stages. The prologue later calls BeginFromWarning.</summary>
        public void PrepareWorld()
        {
            if (!profile) return;
            ASHObjectiveTracker.HoldSlice = true;
            _aftershocksOver = false; _nextAftershock = -1f;
            Suppressed = false; _journalBroadcast = false; _quakeStarted = false; _ashStarted = false;
            ASHQuakeProp.ResetAll();
            if (quake) quake.Stop();
            EndObjective();
            if (director) { director.SetScheduler(false); director.Snap(director.Profile(profile.startState) ? profile.startState : ASHWeatherKind.Clear); }
            FogSet(profile.startFogMultiplier); FogWeight(1f, 0f);
            var acc = ASHAccumulationController.Instance;
            if (acc) { if (profile.groundAshAtStart >= 0f) acc.ashAmount = profile.groundAshAtStart; if (profile.groundSnowAtStart >= 0f) acc.snowAmount = profile.groundSnowAtStart; }
            if (clock && profile.startTimeOfDay >= 0f) clock.timeOfDay = profile.startTimeOfDay;
            var ash = FindAnyObjectByType<ASHAshFall>(); if (ash) ash.SetWeather(0f, Vector3.zero, 1f, true);
            UnlockCabin(false);
            Current = Stage.Inactive; _t = 0f;
        }

        /// <summary>M1 prologue P02: start at the broadcast (alert + announcement) with the quake exactly graceSeconds (real time) away.
        /// No teleport, no door lock. Safe to call again on a checkpoint retry (resets the opening flags first, so nothing doubles).</summary>
        public void BeginFromWarning(float graceSeconds)
        {
            if (!profile) return;
            ASHObjectiveTracker.HoldSlice = true;
            _aftershocksOver = false; _nextAftershock = -1f;
            Suppressed = false; _journalBroadcast = false; _quakeStarted = false; _ashStarted = false;
            ASHQuakeProp.ResetAll();
            if (quake) quake.Stop();
            EndObjective();
            UnlockCabin(false);
            if (director) director.SetScheduler(false);
            Enter(Stage.Broadcast);
            float sp = Mathf.Max(0.01f, debugSpeed);
            _t = profile.musicFadeSeconds + profile.alertSeconds + profile.quakeDelay - Mathf.Max(0f, graceSeconds) * sp;
            if (logStages) Debug.Log($"[ASH] Opening: warning started, quake in {graceSeconds:F0} s");
        }

        /// <summary>Stop aftershocks for good (M1: once the truck objective starts).</summary>
        public void EndAftershocks() { _aftershocksOver = true; _nextAftershock = -1f; }

        public void SkipTo(Stage s)
        {
            if (Current == Stage.Inactive || s <= Stage.Morning) { Begin(); if (s <= Stage.Morning) return; }
            if (s < Stage.Done) { ASHObjectiveTracker.HoldSlice = true; EndObjective(); } // skipping before Done: "Secure the cabin" restarts at Quiet and the slice waits for it again
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
            FogSet(s <= Stage.FirstAsh ? profile.startFogMultiplier : profile.ashFogMultiplier);
            FogWeight(1f, 0f);
            Enter(s);
            if (s == Stage.Quake && quake) { quake.Begin(); _quakeStarted = true; }
            var us = UnlockStage;
            if (profile.lockDoorsUntil == DoorUnlockAt.Never || s > us) UnlockCabin(false); else { LockCabin(); if (s == us) _unlockTimer = profile.doorUnlockDelay; }
        }

        // ------------------------------------------------------------------ cabin doors
        Stage UnlockStage => profile.lockDoorsUntil switch
        {
            DoorUnlockAt.QuakeStart => Stage.Quake, DoorUnlockAt.QuakeEnd => Stage.Quiet,
            DoorUnlockAt.FirstAsh => Stage.FirstAsh, DoorUnlockAt.OpeningDone => Stage.Done, DoorUnlockAt.AfterObjective => Stage.Quiet, _ => Stage.Inactive
        };

        void LockCabin()
        {
            _unlockTimer = -1f; CabinLocked = true;
            if (cabinDoors != null) foreach (var d in cabinDoors) if (d) { d.SnapClosed(); d.Lock(); if (profile) d.lockedMessage = profile.lockedDoorMessage; }
            if (exitBlockers) exitBlockers.SetActive(true);
        }

        void UnlockCabin(bool ajar)
        {
            _unlockTimer = -1f; CabinLocked = false;
            if (cabinDoors != null) foreach (var d in cabinDoors) if (d && d.Locked)
            {
                d.Unlock(ajar);
                if (ajar && profile && profile.doorAjarOnUnlock && profile.doorAjarAngle > 0f) d.Ajar(profile.doorAjarAngle, openingSpawn ? openingSpawn.position : transform.position);
            }
            if (exitBlockers) exitBlockers.SetActive(false);
        }

        /// <summary>Automated tests (ASHPlayTest) need the regular slice start: stop everything and return the player
        /// to the scene spawn position.</summary>
        public void SuppressForTests()
        {
            Suppressed = true;
            if (quake) quake.Stop();
            if (radio) radio.Stop();
            EndObjective();
            ASHQuakeProp.ResetAll();
            if (director) { director.SetScheduler(false); director.debugOverride = false; director.Snap(ASHWeatherKind.Clear); }
            FogWeight(0f, 0f);
            UnlockCabin(false);
            var ash = FindAnyObjectByType<ASHAshFall>(); if (ash) ash.SetWeather(0f, Vector3.zero, 1f, true);
            if (_haveOrig) TeleportPlayer(_origPos, _origRot);
            var hud = FindAnyObjectByType<UI.ASHHud>(); if (hud) hud.ShowSubtitle(null, 0f);
            ReleaseSlice();
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
            if (profile && CabinLocked && profile.lockDoorsUntil != DoorUnlockAt.Never && s == UnlockStage && _unlockTimer < 0f) _unlockTimer = profile.doorUnlockDelay;
            if (logStages) Debug.Log($"[ASH] Opening stage -> {s}");
            StageChanged?.Invoke(s);
            float sp = Mathf.Max(0.01f, debugSpeed);
            switch (s)
            {
                case Stage.Broadcast: if (radio) radio.StartBroadcast(); break;
                case Stage.Quiet: if (director) director.TransitionTo(ASHWeatherKind.CalmHazy, profile.hazeSeconds / sp); break; // objective pops up below, the door unlocks doorUnlockDelay later
                case Stage.FirstAsh: break; // ramp starts after ashOnset (see Tick)
                case Stage.HeavyWave:
                    if (director) director.TransitionTo(ASHWeatherKind.HeavyAsh, profile.heavyRampSeconds / sp);
                    ASHJournalLog.Add(profile.journalWaveTitle, profile.journalWaveBody, "eruption_wave");
                    break;
                case Stage.Easing:
                    if (director) director.TransitionTo(profile.afterState, profile.easeSeconds / sp);
                    if (objective) objective.MarkEased();
                    break;
                case Stage.Done:
                    if (objective && objective.Active) objective.End(objective.AllDone ? "The first wave has passed." : "The first wave has passed. Keep preparing - more will come.");
                    if (director && profile.startSchedulerAfter) director.SetScheduler(true);
                    FogWeight(0f, profile.fogHandoffSeconds / sp);
                    ReleaseSlice(); // after objective.End above: "Secure the cabin" is over, the slice begins
                    break;
            }
            if (s >= Stage.Quiet && s < Stage.Done) StartObjective(); // also covers SkipTo past Quiet
        }

        // ------------------------------------------------------------------ "Secure the cabin"
        bool _objStarted;
        /// <summary>Right after the quake (start of Quiet, before the door unlocks): pop up "Secure the cabin". Completing every
        /// step ends it at once and starts the slice ("Get your bearings"); otherwise it runs until Done as before.</summary>
        void StartObjective()
        {
            if (_objStarted || !objective || !profile) return;
            if (ASH.Prologue.ASHPrologueDirector.BlocksLegacySlice && !ASH.Prologue.ASHPrologueSettings.I.legacySecureCabin) return; // M1: prologue objectives own the post-quake flow
            _objStarted = true;
            objective.Completed -= OnObjectiveComplete; objective.Completed += OnObjectiveComplete;
            objective.Begin(profile.objectiveTitle, profile.objectiveMessage);
            if (logStages) Debug.Log("[ASH] Opening objective -> Secure the cabin");
        }
        void EndObjective() { _objStarted = false; if (objective) { objective.Completed -= OnObjectiveComplete; objective.End(); } }
        void OnObjectiveComplete()
        {
            if (!objective || !objective.Active || Suppressed) return;
            objective.Completed -= OnObjectiveComplete;
            objective.End(profile ? profile.objectiveCompleteMessage : null);
            if (logStages) Debug.Log("[ASH] Opening objective complete -> next objective");
            ReleaseSlice(); // next objective now, not when the opening timers reach Done
        }

        // ------------------------------------------------------------------ aftershocks
        float _nextAftershock = -1f; bool _aftershocksOver; ASHObjectiveTracker _tracker;
        public bool AftershocksOver => _aftershocksOver;
        public float NextAftershockIn => _nextAftershock;
        /// <summary>Every aftershockMin-MaxInterval s (after the main quake, once the door has unlocked; never overlapping) until the
        /// player reaches the "Get your bearings" area, then never again. Keeps running after Done.</summary>
        void TickAftershocks()
        {
            if (!profile || !profile.aftershocks || _aftershocksOver || Suppressed || !quake || Current == Stage.Inactive) return;
            if (Current <= Stage.Quake || CabinLocked || _unlockTimer >= 0f) { _nextAftershock = -1f; return; }
            if (ReachedBearingsArea())
            {
                _aftershocksOver = true;
                if (logStages) Debug.Log("[ASH] Aftershocks over (player reached the 'Get your bearings' area)");
                return; // a running aftershock just finishes (its shake fades to zero and clears itself)
            }
            if (quake.Running) return; // no overlap; the gap is counted from the end
            if (_nextAftershock < 0f) { _nextAftershock = Random.Range(Mathf.Min(profile.aftershockMinInterval, profile.aftershockMaxInterval), Mathf.Max(profile.aftershockMinInterval, profile.aftershockMaxInterval)); return; }
            _nextAftershock -= Time.deltaTime * Mathf.Max(0.01f, debugSpeed);
            if (_nextAftershock <= 0f) ForceAftershock();
        }

        /// <summary>Start one aftershock now (also Debug > Force Aftershock). False while a quake is already shaking.</summary>
        public bool ForceAftershock()
        {
            if (!quake || !profile || quake.Running) return false;
            float d = Random.Range(Mathf.Min(profile.aftershockMinDuration, profile.aftershockMaxDuration), Mathf.Max(profile.aftershockMinDuration, profile.aftershockMaxDuration));
            float k = Random.Range(Mathf.Min(profile.aftershockMinIntensity, profile.aftershockMaxIntensity), Mathf.Max(profile.aftershockMinIntensity, profile.aftershockMaxIntensity));
            quake.BeginAftershock(d / Mathf.Max(0.01f, debugSpeed), k);
            _nextAftershock = -1f; // next gap is rolled once this one ends
            Debug.Log($"[ASH] Aftershock {d:F1} s at {k:F2}x");
            return true;
        }

        bool ReachedBearingsArea()
        {
            if (!_tracker) _tracker = FindAnyObjectByType<ASHObjectiveTracker>(); if (!_tracker) return false;
            if (_tracker.SliceStarted && _tracker.Current != ASHObjectiveTracker.Stage.Survey) return true; // lookout survey logged
            var pt = _tracker.FirstSurveyPoint; if (!pt) return false;
            if (!_cc) _cc = FindAnyObjectByType<vThirdPersonController>(); if (!_cc) return false;
            var a = _cc.transform.position; var b = pt.position; a.y = b.y = 0f;
            return (a - b).sqrMagnitude <= profile.aftershockStopRadius * profile.aftershockStopRadius;
        }

        bool _journalBroadcast;
        void JournalBroadcast() { if (_journalBroadcast) return; _journalBroadcast = true; ASHJournalLog.Add(profile.journalBroadcastTitle, profile.journalBroadcastBody, "eruption_broadcast"); }

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
            if (!profile || !profile.driveFogMultiplier) { if (_fogVol) _fogVol.weight = 0f; return; }
            EnsureFogVolume();
            _fogOv.densityMultiplier.Override(Mathf.Clamp(CurrentFog(), 0f, 10f)); // only parameter overridden
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
            if (_unlockTimer >= 0f && !Suppressed) { _unlockTimer -= Time.deltaTime * Mathf.Max(0.01f, debugSpeed); if (_unlockTimer < 0f) UnlockCabin(true); }
            TickAftershocks();
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
                    if (!_ashStarted && _t >= profile.ashOnsetSeconds) { _ashStarted = true; if (director) director.TransitionTo(ASHWeatherKind.LightAsh, profile.ashRampSeconds / sp); FogRamp(profile.ashFogMultiplier, profile.fogRampSeconds / sp, profile.fogRampCurve); }
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
