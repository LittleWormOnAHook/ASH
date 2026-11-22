using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using ASH.UI;
using ASH.Wildlife;
using Invector;
using UnityEngine;

namespace ASH.Companion
{
    public enum ASHDogMode { Follow, Stay, Defend }

    /// <summary>
    /// The dog companion (the scene's RedDeer Shepherd, set up by ASH/Systems/Companion/Setup Dog). Its own ground-following
    /// mover (no NavMesh): follows you, wanders and sniffs around when you stand still, sits / lies down when idle, Stay holds a
    /// spot, Defend bites enemies near you (Invector vIDamageReceiver) and backs off when hurt. Points out loot pickups and
    /// unopened supply crates (bark + world marker + compass dot), growls at enemies (IASHDogThreat, or tag / layer "Enemy").
    /// E on the dog opens its menu (ASHDogMenu); C (or the menu / journal) calls it to you. Levels, bond, hunger, a pack of
    /// 6+ slots, goes down instead of dying (hold E to revive). Everything saved with the game. Tunables: ASH_CompanionSettings.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class ASHDogCompanion : MonoBehaviour, IASHUsable, IASHSaveable, vIDamageReceiver
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHDogCompanion()
        {
            OpenMenu = null;
            LearnedLegRight = default;
            ForceDigFindChance = -1f;
            ForceNextAct = default;
            ForceDigGrowlChance = -1f;
        }

        public static ASHDogCompanion Active { get; private set; }
        /// <summary>True when the dog can be targeted or fought. Downed or dead dogs are ignored so hostiles go back to the player.</summary>
        public static bool IsValidTarget => Active && Active.isActiveAndEnabled && !Active.Downed && !Active.Dead;
        public static event Action Changed;
        /// <summary>Set by ASHDogMenu: opens the dog menu.</summary>
        public static Func<ASHDogCompanion, ASHInventory, Transform, bool> OpenMenu;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { Active = null; Changed = null; }

        enum Pose { None = 0, Sit = 1, Lie = 2, Alert = 3, Eat = 4, Down = 5, Dig = 6, Sleep = 7, Look = 8 }
        enum Act { None, Settle, Patrol, Sniff, Look, Sit, Lie, Sleep, Dig, Fidget, Pee }
        static readonly int HSpeed = Animator.StringToHash("Speed"), HPose = Animator.StringToHash("Pose"), HAttack = Animator.StringToHash("Attack"),
            HHit = Animator.StringToHash("Hit"), HHappy = Animator.StringToHash("Happy"), HTurn = Animator.StringToHash("Turn"),
            HFidget = Animator.StringToHash("Fidget"), HFidgetId = Animator.StringToHash("FidgetId");

        [SerializeField] Animator animator;
        public Animator Anim => animator;
        [SerializeField] AudioClip barkClip, barkDoubleClip, growlClip, whineClip, whistleClip;
        [SerializeField] OnReceiveDamage _onStart = new OnReceiveDamage(), _onRecv = new OnReceiveDamage();
        public OnReceiveDamage onStartReceiveDamage => _onStart;
        public OnReceiveDamage onReceiveDamage => _onRecv;

        static ASHCompanionSettings S => ASHCompanionSettings.I;

        // ------------------------------------------------------------------ state (saved)
        string _name;
        public ASHDogMode Mode { get; private set; } = ASHDogMode.Follow;
        public string DogName => string.IsNullOrEmpty(_name) ? S.dogName : _name;
        public int Level { get; private set; } = 1;
        public float Xp { get; private set; }
        public float XpNeed => S.XpToNext(Level);
        public float Health { get; private set; } = -1f;
        public float MaxHealth => S.MaxHealth(Level);
        public float Hunger { get; private set; } = 80f;
        public float Bond { get; private set; } = 20f;
        public bool Downed { get; private set; }
        public bool Dead { get; private set; }
        public int PackSlots => Mathf.Max(S.PackSlots(Level), _pack.Count);
        /// <summary>What the dog is doing right now (journal / menu).</summary>
        public string Activity { get; private set; } = "";
        public float CallReadyIn => Mathf.Max(0f, _nextCall - Time.time);

        public sealed class PackSlot { public ASHItemDefinition item; public int amount; public float condition = 1f; public bool IsEmpty => item == null || amount <= 0; public void Clear() { item = null; amount = 0; condition = 1f; } }
        readonly List<PackSlot> _pack = new List<PackSlot>();
        public IReadOnlyList<PackSlot> Pack { get { EnsurePack(); return _pack; } }

        // ------------------------------------------------------------------ runtime
        Transform _player; ASHInput _input; ASHHud _hud; AudioSource _audio;
        Vector3 _lastPlayerPos; float _playerSpeed, _playerIdleT, _speed, _stuckT, _yaw; Vector3 _up = Vector3.up;
        Pose _pose; float _poseUntil; Vector3 _stayPos; float _stayT, _idleT;
        Vector3 _wander; float _wanderWait; bool _moving;
        Transform _threat; float _threatScanT, _nextWarn, _warnUntil, _nextAttack, _pendingHit = -1f; Transform _pendingTarget; bool _retreat; float _lastHurt = -99f;
        Transform _assistTarget; float _distractUntil;
        Transform _find; bool _findIsCrate; float _findScanT, _nextFind, _findGiveUp, _pointUntil = -1f; string _findLabel;
        readonly Dictionary<int, float> _seen = new Dictionary<int, float>();
        readonly List<(string id, float until)> _markers = new List<(string, float)>();
        float _nextPet, _nextCall, _callRunUntil, _downT, _followXp, _whimperT; bool _compassReg; string _threatMarker;
        float _yawVel, _animYaw, _turnRate, _actualSpeed, _animRate = 1f; bool _turningInPlace, _teleported; Vector3 _lastPos;
        sealed class Paw { public Transform t; public float min = 99f; public bool lifted; }
        readonly List<Paw> _paws = new List<Paw>(); AudioSource _stepAudio; int _lastStepClip = -1;
        public bool TurningInPlace => _turningInPlace; public float ActualSpeed => _actualSpeed; public int PawSteps { get; private set; }
        static readonly Collider[] Cols = new Collider[256];
        static readonly RaycastHit[] Hits = new RaycastHit[32];

        public Transform UseTransform => transform;
        public bool HasAbility(int level) => Level >= level;

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animator) { animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; } // paws step / print behind the camera too
            _audio = GetComponent<AudioSource>(); if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 1f; _audio.minDistance = 2f; _audio.maxDistance = 45f; _audio.rolloffMode = AudioRolloffMode.Linear;
            _yaw = transform.eulerAngles.y; _animYaw = _yaw; _stayPos = transform.position; _lastPos = transform.position; SyncBody();
            SetupPaws();
        }

        /// <summary>Finds the four foot bones, adds an ASHDeformer (paw prints in accumulated snow/ash) and a step audio source.</summary>
        void SetupPaws()
        {
            _paws.Clear();
            foreach (var n in new[] { "foot_f.L", "foot_f.R", "foot_b.L", "foot_b.R" })
            {
                Transform f = null; foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) { f = t; break; }
                if (f) _paws.Add(new Paw { t = f });
            }
            _stepAudio = gameObject.AddComponent<AudioSource>();
            _stepAudio.playOnAwake = false; _stepAudio.spatialBlend = 1f; _stepAudio.minDistance = 1.5f; _stepAudio.maxDistance = 20f; _stepAudio.rolloffMode = AudioRolloffMode.Linear;
            if (!S.pawPrints || _paws.Count == 0) return;
            var def = GetComponent<ASH.World.Accumulation.ASHDeformer>();
            if (def == null) def = gameObject.AddComponent<ASH.World.Accumulation.ASHDeformer>();
            def.autoHumanoidFeet = false; def.wadingTrench = false; def.points.Clear();
            foreach (var p in _paws) def.points.Add(new ASH.World.Accumulation.ASHDeformer.Point { transform = p.t, radius = S.pawPrintRadius, strength = S.pawPrintDepth, bottomOffset = S.pawBottomOffset });
        }

        /// <summary>Per-paw footfall (foot bone dips to its lowest height relative to the body) -> paw step sound.</summary>
        void PawSteps_(float dt)
        {
            if (_paws.Count == 0) return;
            float sc = Mathf.Max(1e-4f, transform.lossyScale.y);
            bool moving = _actualSpeed > 0.2f || Mathf.Abs(_turnRate) > 40f;
            foreach (var p in _paws)
            {
                if (!p.t) continue;
                float h = transform.InverseTransformPoint(p.t.position).y * sc;
                p.min = Mathf.Min(p.min + dt * 0.03f, h);
                if (h > p.min + S.pawLiftHeight) p.lifted = true;
                else if (p.lifted && h < p.min + S.pawPlantHeight)
                {
                    p.lifted = false;
                    if (moving) { PawSteps++; PlayStep(p.t.position); }
                }
            }
        }

        void PlayStep(Vector3 pos)
        {
            var c = S.pawStepClips; if (!S.pawStepSounds || c == null || c.Length == 0 || !_stepAudio) return;
            int i = UnityEngine.Random.Range(0, c.Length); if (c.Length > 1 && i == _lastStepClip) i = (i + 1) % c.Length; _lastStepClip = i;
            if (!c[i]) return;
            _stepAudio.pitch = S.pawStepPitch * UnityEngine.Random.Range(0.92f, 1.08f);
            _stepAudio.PlayOneShot(c[i], S.pawStepVolume * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(_actualSpeed / Mathf.Max(0.1f, S.runSpeed))));
        }

        void OnEnable() { Active = this; ASHSaveSystem.Register(this); }
        void OnDisable()
        {
            ASHSaveSystem.Unregister(this); if (Active == this) Active = null;
            ASHCompass.Unregister("dog"); if (_threatMarker != null) ASHCompass.Unregister(_threatMarker);
            foreach (var m in _markers) ASHCompass.Unregister(m.id); _markers.Clear(); _compassReg = false;
        }

        void EnsurePack() { int n = S.PackSlots(Level); while (_pack.Count < n) _pack.Add(new PackSlot()); }

        bool FindPlayer()
        {
            if (_player) return true;
            var st = FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>(); if (!st) return false;
            _player = st.transform; _lastPlayerPos = _player.position; _input = FindAnyObjectByType<ASHInput>(); _hud = FindAnyObjectByType<ASHHud>();
            return true;
        }

        void Toast(string m) { if (!_hud) _hud = FindAnyObjectByType<ASHHud>(); if (_hud) _hud.Toast(m); }
        float _nextGrowl, _nextDigGrowl; int _lastGrowl = -1; Transform _growledAt;
        /// <summary>Plays a random growl (warning or playful set). Falls back to the double bark when no growl clips are set. Returns false on cooldown.</summary>
        bool Growl(bool playful = false, float volMul = 1f)
        {
            if (!_audio || Time.time < _nextGrowl) return false;
            var set = playful && S.playGrowlClips != null && S.playGrowlClips.Length > 0 ? S.playGrowlClips : S.growlClips;
            AudioClip c = null;
            if (set != null && set.Length > 0)
            {
                int i = UnityEngine.Random.Range(0, set.Length); if (set.Length > 1 && i == _lastGrowl) i = (i + 1) % set.Length;
                _lastGrowl = i; c = set[i];
            }
            if (!c) { if (playful) return false; c = growlClip ? growlClip : barkDoubleClip; }
            if (!c) return false;
            _nextGrowl = Time.time + S.growlCooldown;
            _audio.pitch = 1f + UnityEngine.Random.Range(-S.growlPitchVariance, S.growlPitchVariance);
            _audio.PlayOneShot(c, (playful ? S.digGrowlVolume : S.growlVolume) * volMul);
            GrowlCount++;
            return true;
        }
        /// <summary>Test hook: growls played this session.</summary>
        public int GrowlCount { get; private set; }
        public bool TestGrowl(bool playful) { _nextGrowl = 0f; return Growl(playful); }

        void Play(AudioClip c, float vol = 1f) { if (c && _audio) { _audio.pitch = UnityEngine.Random.Range(0.94f, 1.06f); _audio.PlayOneShot(c, vol); } }
        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
        void Notify() => Changed?.Invoke();

        // ================================================================== update
        void Update()
        {
            if (Health < 0f) Health = MaxHealth;
            EnsurePack();
            if (!FindPlayer()) return;
            float dt = Time.deltaTime; if (dt <= 0f) return;
            var P = _player.position;
            float ps = Flat(P, _lastPlayerPos) / dt; _lastPlayerPos = P; _playerSpeed = Mathf.Lerp(_playerSpeed, ps, 1f - Mathf.Exp(-dt * 6f));
            _playerIdleT = _playerSpeed < 0.3f ? _playerIdleT + dt : 0f;

            { var pos = transform.position; float v = _teleported ? 0f : Mathf.Min(Flat(pos, _lastPos) / dt, S.runSpeed * 1.5f); _teleported = false; _lastPos = pos;
              _actualSpeed = Mathf.Lerp(_actualSpeed, v, 1f - Mathf.Exp(-dt * 12f)); }
            if (_input && _input.CallPetPressed) Call();
            Needs(dt);
            Markers();
            if (Dead || Downed) { DownedUpdate(dt); Animate(dt); return; }
            if (RideUpdate(dt)) { Animate(dt); return; }
            if (VehicleWait()) { Animate(dt); return; }

            if ((_threatScanT -= dt) <= 0f) { _threatScanT = 0.4f; ScanThreats(); }
            if ((_findScanT -= dt) <= 0f) { _findScanT = S.scanInterval; ScanFinds(); }
            Think(dt, P);
            if (_pendingHit > 0f && Time.time >= _pendingHit) { _pendingHit = -1f; ApplyBite(); }
            Animate(dt);
        }

        void Think(float dt, Vector3 P)
        {
            float dP = Flat(transform.position, P);
            if (S.autoTeleportWhenFar && Mode != ASHDogMode.Stay && (dP > S.teleportDistance || (_stuckT > S.stuckTeleportSeconds && dP > S.followDistance * 3f)))
            { TeleportNearPlayer(); Activity = "Caught up with you"; return; }

            if (Mode != ASHDogMode.Stay && dP < S.followDistance * 4f && _playerSpeed > 0.5f)
            { _followXp += dt / 60f * S.xpPerFollowMinute; if (_followXp >= 1f) { float w = Mathf.Floor(_followXp); _followXp -= w; AddXp(w, false); } }

            bool hurt = Health < MaxHealth * S.retreatAt; if (hurt) _retreat = true; else if (Health > MaxHealth * Mathf.Min(1f, S.retreatAt + 0.15f)) _retreat = false;

            // called: run to you
            if (Time.time < _callRunUntil && Mode != ASHDogMode.Stay)
            {
                Activity = "Coming to you";
                if (MoveTo(P, S.runSpeed, S.followDistance)) _callRunUntil = 0f;
                return;
            }
            // combat assist in Follow or Defend (never Stay); wolf = engage, bear = warn/distract/retreat
            if (_assistTarget && Mode != ASHDogMode.Stay && !_retreat && AssistStep(P)) return;
            // defend nearby threats
            if (_threat && Mode == ASHDogMode.Defend && !_retreat && Engage(P)) return;
            // warn
            if (_threat && Time.time < _warnUntil) { Activity = "Growling at danger"; Stop(); Face(_threat.position, dt); SetPose(Pose.Alert); return; }

            if (Mode == ASHDogMode.Stay)
            {
                Activity = "Staying";
                if (Flat(transform.position, _stayPos) > 0.6f) { MoveTo(_stayPos, S.walkSpeed * 1.4f, 0.4f); _stayT = 0f; return; }
                _stayT += dt;
                IdleBrain(dt, P, true);
                return;
            }

            // show a find
            if (_find && Mode == ASHDogMode.Follow && !_retreat && FindStep(dt, P)) return;

            float fd = Mode == ASHDogMode.Defend || _retreat ? S.defendFollowDistance : S.followDistance;
            bool far = dP > fd + S.followSlack;
            bool idleMode = Mode == ASHDogMode.Follow && !_retreat && _playerIdleT > S.wanderAfterIdle && dP < S.idleRadius + 2.5f + (_act == Act.Pee ? 2f : 0f);
            if (_playerIdleT < 0.3f && _act != Act.None) EndAct(); // you moved: drop the activity and come along
            if (!idleMode && (far || (_moving && dP > fd)))
            {
                _idleT = 0f;
                float want = dP > S.runDistance ? S.runSpeed : dP > S.trotDistance ? S.trotSpeed : Mathf.Clamp(_playerSpeed * 1.1f, S.walkSpeed, S.runSpeed);
                if (_retreat) want = S.runSpeed;
                want *= 1f + S.bondBonus * Bond / 100f;
                var dir = (transform.position - P); dir.y = 0f; if (dir.sqrMagnitude < 0.01f) dir = -_player.forward;
                MoveTo(P + dir.normalized * fd, want, 0.3f);
                Activity = _retreat ? "Hurt, staying close" : Mode == ASHDogMode.Defend ? "Guarding you" : "Following";
                return;
            }
            // near you
            if (idleMode) { IdleBrain(dt, P, false); return; }
            Stop(); _idleT = 0f; FaceLoose(P, dt);
            Activity = Mode == ASHDogMode.Defend ? "Guarding you" : "By your side";
            if (_playerIdleT > 1.5f && Mode == ASHDogMode.Defend) SetPose(Pose.Alert); else SetPose(Pose.None);
        }

        // ================================================================== idle brain (t338)
        Act _act; float _actUntil, _actStart, _nextDigFind; Act _lastAct; int _fidgetId;
        readonly List<Vector3> _patrol = new List<Vector3>(); int _patrolI, _patrolDir = 1; float _patrolWait; Vector3 _actSpot; bool _atSpot, _facing;
        public string IdleActivity => _act.ToString();
        public int DigFinds { get; private set; }
        // ---------------------------------------------------------- leg-lift (t343): clip Arm_Dog|Pissing (FidgetId 5) + ASHDogPee effects
        ASHDogPee _peeFx; ASHDogPee.Target _peeT; float _peeStart = -1f, _nextPee = 60f; bool _peeStreaming, _peeLegChecked;
        public int Pees { get; private set; } public string LastPeeTarget { get; private set; } = "";
        public static bool LearnedLegRight { get; private set; }
        public float PlayerIdleSeconds => _playerIdleT; public Vector3 PeeTargetPos => _peeT.pos; public Vector3 ActSpot => _actSpot;
        void PeeStep(float dt)
        {
            if (!GoToSpot()) { Activity = "Sniffing for a good tree"; return; }
            var to = _peeT.pos - transform.position; to.y = 0f; if (to.sqrMagnitude < 0.001f) to = transform.right;
            bool right = _peeLegChecked ? LearnedLegRight : S.peeLegRight;
            var fwd = right ? Vector3.Cross(to.normalized, Vector3.up) : Vector3.Cross(Vector3.up, to.normalized); // target on the lifted-leg side
            if (_peeStart < 0f)
            {
                Activity = "Sniffing the " + _peeT.what; _pose = Pose.None;
                Face(transform.position + fwd * 2f, dt);
                float yawTo = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, yawTo)) < 12f || Time.time > _actUntil - 18f)
                {
                    _peeStart = Time.time; _actUntil = Time.time + S.peeSeconds;
                    if (animator && animator.runtimeAnimatorController) { animator.SetInteger(HFidgetId, 5); animator.SetTrigger(HFidget); }
                }
                return;
            }
            Activity = "Marking the " + _peeT.what; Stop();
            float t = Time.time - _peeStart;
            if (!_peeLegChecked && t > 1.2f && _peeFx) { int side = _peeFx.LiftedSide(); if (side != 0) { LearnedLegRight = side > 0; _peeLegChecked = true; } }
            if (!_peeStreaming && t > 1.0f && t < S.peeSeconds - 0.8f && _peeFx) { _peeFx.StartStream(_peeT.pos, right); _peeStreaming = true; }
            if (_peeStreaming && t >= S.peeSeconds - 0.8f) { _peeFx.Finish(_peeT, _audio); _peeStreaming = false; Pees++; LastPeeTarget = _peeT.what; }
        }
        void PeeAbort() { if (_peeStreaming && _peeFx) _peeFx.StopStream(); _peeStreaming = false; _peeStart = -1f; }
        float _digTime;
        public static float ForceDigFindChance = -1f; // tests
        public static string ForceNextAct; // tests: next picked idle activity (Patrol, Sniff, Look, Sit, Lie, Sleep, Dig, Fidget)
        public static float ForceDigGrowlChance = -1f; // tests

        static float Rand(Vector2 r) => UnityEngine.Random.Range(Mathf.Min(r.x, r.y), Mathf.Max(r.x, r.y));

        void EndAct()
        {
            if (_act == Act.Dig && _digTime >= 1.5f) DigDone(); // however the dig ends (time up, player moved, threat)
            _digTime = 0f; if (_act == Act.Pee) PeeAbort();
            _act = Act.None; _patrol.Clear(); _atSpot = false;
            if (_pose != Pose.Down) { _pose = Pose.None; _poseUntil = 0f; }
        }

        /// <summary>Weighted activities while you wait. Every activity lasts at least minActivitySeconds and is followed by a short settle.</summary>
        void IdleBrain(float dt, Vector3 P, bool stay)
        {
            _idleT += dt;
            if (_act == Act.None || Time.time >= _actUntil) PickAct(P, stay);
            switch (_act)
            {
                case Act.Settle: Stop(); _pose = Pose.None; FaceLoose(P, dt); Activity = stay ? "Staying" : "Waiting for you"; break;
                case Act.Patrol: PatrolStep(dt, P); break;
                case Act.Sniff: if (GoToSpot()) { _pose = Pose.Eat; Activity = "Sniffing the ground"; } break;
                case Act.Look: Stop(); _pose = Pose.Look; Activity = "Looking around"; break;
                case Act.Sit: Stop(); _pose = Pose.Sit; Activity = "Sitting"; break;
                case Act.Lie: Stop(); _pose = Pose.Lie; Activity = "Resting"; break;
                case Act.Sleep: Stop(); _pose = Pose.Sleep; Activity = "Napping"; break;
                case Act.Dig: if (GoToSpot()) { _pose = Pose.Dig; Activity = "Digging"; _digTime += dt;
                        if (Time.time >= _nextDigGrowl && UnityEngine.Random.value < (ForceDigGrowlChance >= 0f ? ForceDigGrowlChance : S.digGrowlChance) * dt) { _nextDigGrowl = Time.time + S.digGrowlCooldown; Growl(true); } } else _actUntil = Mathf.Max(_actUntil, Time.time + 0.5f); break;
                case Act.Pee: PeeStep(dt); break;
                case Act.Fidget: Stop(); _pose = Pose.None; Activity = stay ? "Staying" : "Waiting for you"; break;
            }
        }

        bool GoToSpot()
        {
            if (_atSpot) { Stop(); return true; }
            if (Flat(transform.position, _actSpot) < 0.5f || MoveTo(_actSpot, S.walkSpeed, 0.45f)) { _atSpot = true; _speed = 0f; return true; }
            _pose = Pose.None; return false;
        }

        void PickAct(Vector3 P, bool stay)
        {
            var prev = _act; EndAct();
            if (prev != Act.Settle && prev != Act.None) { _act = Act.Settle; _actUntil = Time.time + Rand(S.settleSeconds); _lastAct = prev; return; }
            bool longIdle = S.lieAfterIdle > 0f && _idleT > S.lieAfterIdle;
            var acts = new List<(Act a, float w)>
            {
                (Act.Sniff, S.wSniff), (Act.Look, S.wLook), (Act.Sit, S.wSit * (stay ? 2f : 1f)),
                (Act.Lie, S.wLie * (longIdle ? 3f : 1f) * (stay ? 2f : 1f)), (Act.Sleep, S.lieAfterIdle > 0f && _idleT > S.lieAfterIdle * 2f ? S.wSleep : 0f),
                (Act.Fidget, S.wFidget)
            };
            if (!stay) { acts.Add((Act.Patrol, S.wPatrol)); acts.Add((Act.Dig, S.wDig)); if (S.peeEnabled && Time.time >= _nextPee) acts.Add((Act.Pee, S.wPee)); }
            float tot = 0f; foreach (var x in acts) if (x.a != _lastAct) tot += Mathf.Max(0f, x.w);
            if (tot <= 0f) { _act = Act.Settle; _actUntil = Time.time + 2f; return; }
            float r = UnityEngine.Random.value * tot; Act pick = Act.Look;
            foreach (var x in acts) { if (x.a == _lastAct) continue; r -= Mathf.Max(0f, x.w); if (r <= 0f) { pick = x.a; break; } }
            if (ForceNextAct != null && System.Enum.TryParse<Act>(ForceNextAct, out var fa)) { pick = fa; ForceNextAct = null; }
            _act = pick; _actStart = Time.time;
            float dur = pick switch
            {
                Act.Patrol => Rand(S.patrolSeconds), Act.Sniff => Rand(S.sniffSeconds), Act.Look => Rand(S.lookSeconds), Act.Sit => Rand(S.sitSeconds),
                Act.Lie => Rand(S.lieSeconds), Act.Sleep => Rand(S.sleepSeconds), Act.Dig => Rand(S.digSeconds), _ => 3.5f
            };
            _actUntil = Time.time + Mathf.Max(S.minActivitySeconds, dur);
            if (pick == Act.Sniff) _actSpot = stay ? transform.position : SpotNear(P, transform.position, 2.5f);
            if (pick == Act.Dig) { _actSpot = SpotNear(P, transform.position, 4f); _actUntil += 6f; } // walking there does not eat the dig time (see DigDone)
            if (pick == Act.Patrol) BuildPatrol(P);
            if (pick == Act.Pee)
            {
                if (!_peeFx) { _peeFx = GetComponent<ASHDogPee>(); if (_peeFx == null) _peeFx = gameObject.AddComponent<ASHDogPee>(); }
                if (ASHDogPee.FindTarget(transform.position, P, Mathf.Max(2f, S.idleRadius - 0.8f), out _peeT)) { _actSpot = ASHDogPee.StandSpot(_peeT, transform.position); _peeStart = -1f; _actUntil = Time.time + 25f; _nextPee = Time.time + S.peeCooldown; }
                else { _act = Act.Look; _actUntil = Time.time + Rand(S.lookSeconds); }
            }
            if (pick == Act.Fidget && animator && animator.runtimeAnimatorController)
            {
                _fidgetId = UnityEngine.Random.Range(2, 5);
                animator.SetInteger(HFidgetId, _fidgetId); animator.SetTrigger(HFidget);
            }
        }

        /// <summary>A walkable point near 'from', inside the idle area around P, visible from P.</summary>
        Vector3 SpotNear(Vector3 P, Vector3 from, float reach)
        {
            for (int k = 0; k < 10; k++)
            {
                var c = UnityEngine.Random.insideUnitCircle * reach; var p = from + new Vector3(c.x, 0f, c.y);
                var off = p - P; off.y = 0f; float d = off.magnitude;
                if (d > S.idleRadius) p = P + off / d * S.idleRadius; else if (d < S.idleMinRing) p = P + (d > 0.01f ? off / d : transform.forward) * S.idleMinRing;
                if (!Ground(p + Vector3.up * 3f, 8f, out var g, out var n) || Vector3.Angle(n, Vector3.up) > S.maxSlope || Mathf.Abs(g.y - P.y) > 2f) continue;
                if (Overlaps(g + Vector3.up * 0.5f, 0.25f) || LineBlocked(P + Vector3.up, g + Vector3.up * 0.4f)) continue;
                if (Flat(g, from) < 1.2f && k < 8) continue; // a spot worth walking to (no turn-in-place for 30 cm)
                return g;
            }
            return from;
        }

        void BuildPatrol(Vector3 P)
        {
            _patrol.Clear(); float a0 = UnityEngine.Random.value * 360f, span = UnityEngine.Random.Range(90f, 200f);
            int n = Mathf.Max(2, S.patrolPoints);
            for (int i = 0; i < n * 3 && _patrol.Count < n; i++)
            {
                float a = a0 + span * (_patrol.Count / (float)Mathf.Max(1, n - 1)) + UnityEngine.Random.Range(-15f, 15f);
                float r = UnityEngine.Random.Range(Mathf.Max(S.idleMinRing, S.idleRadius * 0.45f), S.idleRadius);
                var p = P + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                if (!Ground(p + Vector3.up * 3f, 8f, out var g, out var nn) || Vector3.Angle(nn, Vector3.up) > S.maxSlope || Mathf.Abs(g.y - P.y) > 2f) continue;
                if (Overlaps(g + Vector3.up * 0.5f, 0.25f) || LineBlocked(P + Vector3.up, g + Vector3.up * 0.4f)) continue;
                if (_patrol.Count > 0 && Flat(g, _patrol[_patrol.Count - 1]) < 1.5f) continue;
                _patrol.Add(g);
            }
            // start at the nearest point
            _patrolI = 0; float best = float.MaxValue;
            for (int i = 0; i < _patrol.Count; i++) { float d = Flat(_patrol[i], transform.position); if (d < best) { best = d; _patrolI = i; } }
            _patrolDir = _patrolI >= _patrol.Count - 1 ? -1 : 1; _patrolWait = 0f;
            if (_patrol.Count < 2) { _act = Act.Sniff; _actSpot = SpotNear(P, transform.position, 2.5f); }
        }

        void PatrolStep(float dt, Vector3 P)
        {
            if (_patrol.Count < 2) { Stop(); return; }
            Activity = "Patrolling around you";
            if (_patrolWait > 0f)
            {
                Stop(); _patrolWait -= dt;
                _pose = _patrolWait > 0.6f && (_patrolI % 2 == 0) ? Pose.Eat : Pose.None; // sniff at every other point
                if (_patrolWait <= 0f)
                {
                    _pose = Pose.None;
                    int nx = _patrolI + _patrolDir; if (nx < 0 || nx >= _patrol.Count) { _patrolDir = -_patrolDir; nx = _patrolI + _patrolDir; }
                    _patrolI = Mathf.Clamp(nx, 0, _patrol.Count - 1);
                }
                return;
            }
            _pose = Pose.None;
            if (MoveTo(_patrol[_patrolI], S.walkSpeed, 0.5f)) _patrolWait = Rand(S.patrolPause);
        }

        void DigDone()
        {
            if (S.digJunk == null || S.digJunk.Count == 0 || Time.time < _nextDigFind) { if (ForceDigFindChance >= 0f) Debug.Log($"[ASH] Dog dig: no find (junk {S.digJunk?.Count ?? 0}, cooldown {Mathf.Max(0f, _nextDigFind - Time.time):F0}s)"); return; }
            float ch = ForceDigFindChance >= 0f ? ForceDigFindChance : (S.digFindChance + S.digFindPerLevel * (Level - 1)) * (Level >= S.keenNoseLevel ? S.digFindKeenMul : 1f);
            if (UnityEngine.Random.value >= ch) return;
            var pool = new List<ASHItemDefinition>(); foreach (var j in S.digJunk) if (j) pool.Add(j); if (pool.Count == 0) return;
            var item = pool[UnityEngine.Random.Range(0, pool.Count)];
            var pos = transform.position + transform.forward * 0.45f + Vector3.up * 0.05f;
            var pk = ASH.Interaction.ASHItemDrop.SpawnAt(item, 1, pos, Quaternion.Euler(0f, UnityEngine.Random.value * 360f, 0f));
            if (!pk) { Debug.LogWarning("[ASH] Dog dig: could not spawn " + item.name); return; }
            _nextDigFind = Time.time + S.digFindCooldown; DigFinds++;
            Play(barkDoubleClip ? barkDoubleClip : barkClip, 0.9f);
            ASHDogMarker.Show(pk.transform, S.lootMarkerColor, item.displayName, S.markerSeconds);
            string id = "dogdig" + pk.GetHashCode();
            ASHCompass.Register(pk.transform.position, ASHCompassMarkerKind.Custom, S.lootMarkerColor, id); _markers.Add((id, Time.time + S.markerSeconds));
            AddXp(S.xpDigFind);
            Toast($"{DogName} dug up something: {item.displayName}!");
            Debug.Log($"[ASH] Dog dug up {item.displayName} at {pos}");
        }

        /// <summary>Turns toward p only past the deadzone, then until nearly facing (hysteresis: no constant re-aiming / spinning).</summary>
        void FaceLoose(Vector3 p, float dt)
        {
            var to = p - transform.position; to.y = 0f; if (to.sqrMagnitude < 0.25f) return;
            float diff = Mathf.Abs(Mathf.DeltaAngle(_yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg));
            if (!_facing && diff > S.faceDeadzone) _facing = true;
            if (_facing) { Face(p, dt); if (diff < 6f) { _facing = false; _yawVel = 0f; } }
        }

        // ================================================================== movement (ground following, no NavMesh)
        bool Ignore(Collider c)
        {
            if (!c || c.isTrigger) return true;
            var t = c.transform; if (t.IsChildOf(transform) || (_player && t.IsChildOf(_player))) return true;
            return c.attachedRigidbody && !c.attachedRigidbody.isKinematic && c.attachedRigidbody.mass < 20f;
        }

        bool Ground(Vector3 from, float dist, out Vector3 point, out Vector3 normal)
        {
            point = from; normal = Vector3.up;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, dist, S.environmentLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool ok = false;
            for (int i = 0; i < n; i++) { var h = Hits[i]; if (Ignore(h.collider) || h.collider.GetComponentInParent<ASHItemPickup>()) continue; if (h.distance < best) { best = h.distance; point = h.point; normal = h.normal; ok = true; } }
            return ok;
        }

        // body capsule (lies along the dog's length), world space
        CapsuleCollider _body;
        void EnsureBody()
        {
            if (_body) return;
            var go = new GameObject("ASH_DogBody"); go.transform.SetParent(transform, false); go.layer = gameObject.layer;
            float ls = Mathf.Max(1e-4f, transform.lossyScale.x); go.transform.localScale = Vector3.one / ls;
            _body = go.AddComponent<CapsuleCollider>(); _body.isTrigger = true; _body.direction = 2;
        }
        void SyncBody() { EnsureBody(); _body.radius = S.bodyRadius; _body.height = S.bodyLength + 2f * S.bodyRadius; _body.center = new Vector3(0f, S.bodyHeight, 0f); }
        void Ends(Vector3 pos, Vector3 fwd, out Vector3 a, out Vector3 b)
        {
            var c = pos + Vector3.up * S.bodyHeight; var h = fwd * (S.bodyLength * 0.5f); a = c + h; b = c - h;
        }

        /// <summary>Nearest non-walkable hit of the body capsule swept along dir (hits it already overlaps are left to Depenetrate).</summary>
        bool Sweep(Vector3 pos, Vector3 fwd, Vector3 dir, float dist, out RaycastHit hit)
        {
            hit = default; Ends(pos, fwd, out var a, out var b);
            int n = Physics.CapsuleCastNonAlloc(a, b, S.bodyRadius * 0.95f, dir, Hits, dist, S.environmentLayers, QueryTriggerInteraction.Ignore);
            float cos = Mathf.Cos(S.maxSlope * Mathf.Deg2Rad), best = float.MaxValue; bool ok = false;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i]; if (h.distance <= 0f || Ignore(h.collider) || h.normal.y >= cos) continue;
                if (h.distance < best) { best = h.distance; hit = h; ok = true; }
            }
            return ok;
        }

        bool Blocked(Vector3 dir, float dist) => Sweep(transform.position, dir, dir, dist, out _);

        /// <summary>Whisker steering: feelers left / right of dir bend the heading away from the nearer obstacle.</summary>
        Vector3 Whiskers(Vector3 dir)
        {
            var o = transform.position + Vector3.up * S.bodyHeight; float len = S.whiskerLength, r = S.bodyRadius * 0.6f;
            float L = Feel(o, Quaternion.Euler(0f, -S.whiskerAngle, 0f) * dir, len, r), R = Feel(o, Quaternion.Euler(0f, S.whiskerAngle, 0f) * dir, len, r);
            if (L >= len && R >= len) return dir;
            float push = (len - Mathf.Min(L, R)) / len; float sign = L < R ? 1f : -1f;
            return Quaternion.Euler(0f, sign * S.whiskerAngle * push, 0f) * dir;
        }

        float Feel(Vector3 o, Vector3 d, float len, float r)
        {
            int n = Physics.SphereCastNonAlloc(o, r, d, Hits, len, S.environmentLayers, QueryTriggerInteraction.Ignore);
            float cos = Mathf.Cos(S.maxSlope * Mathf.Deg2Rad), best = len;
            for (int i = 0; i < n; i++) { var h = Hits[i]; if (h.distance <= 0f || Ignore(h.collider) || h.normal.y >= cos) continue; best = Mathf.Min(best, h.distance); }
            return best;
        }

        void Stop() { _speed = Mathf.MoveTowards(_speed, 0f, S.acceleration * 2f * Time.deltaTime); if (_speed > 0.01f) Step(transform.forward, Time.deltaTime); else { _moving = false; Depenetrate(); } }

        Vector3 _detour; float _detourUntil;

        /// <summary>Moves toward target; true once within arrive.</summary>
        bool MoveTo(Vector3 target, float want, float arrive)
        {
            float dt = Time.deltaTime;
            var to = target - transform.position; to.y = 0f; float d = to.magnitude;
            if (d <= arrive) { _turningInPlace = false; Stop(); return true; }
            var dir = to / d;
            // a target just behind and close: count it as reached instead of spinning round for it (t338)
            if (d < 1.2f && _speed < 0.3f && !_turningInPlace && Mathf.Abs(Mathf.DeltaAngle(_yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg)) > 100f) { Stop(); return true; }
            SetPose(Pose.None);
            if (Time.time < _detourUntil) dir = _detour;
            else if (Blocked(dir, Mathf.Min(d, 0.9f)))
            {
                bool found = false;
                foreach (float a in new[] { 40f, -40f, 75f, -75f, 110f, -110f, 150f, -150f })
                {
                    var alt = Quaternion.Euler(0f, a, 0f) * dir;
                    if (!Blocked(alt, 1.2f)) { dir = alt; _detour = alt; _detourUntil = Time.time + 0.7f; found = true; break; }
                }
                if (!found) { _stuckT += dt; _speed = 0f; _turningInPlace = false; Face(transform.position + dir, dt); Depenetrate(); return false; }
            }
            dir = Whiskers(dir);
            float yawTo = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; float diff = Mathf.DeltaAngle(_yaw, yawTo);
            // sharp angle from (near) standstill: stop and turn in place first (turn clips), otherwise arc through the turn
            if (!_turningInPlace && Mathf.Abs(diff) > S.turnInPlaceAngle && _speed < S.trotSpeed) _turningInPlace = true;
            else if (_turningInPlace && Mathf.Abs(diff) < S.turnInPlaceDone) _turningInPlace = false;
            _yaw = Mathf.SmoothDampAngle(_yaw, yawTo, ref _yawVel, S.turnSmoothTime, S.turnSpeed * (_turningInPlace ? 0.7f : 1f), dt);
            float turnSlow = _turningInPlace ? 0f : Mathf.Lerp(1f, S.arcTurnSlow, Mathf.Clamp01(Mathf.Abs(diff) / 90f));
            want = Mathf.Min(want, d / Mathf.Max(0.2f, dt * 8f) + S.walkSpeed * 0.5f);
            _speed = Mathf.MoveTowards(_speed, want * turnSlow, S.acceleration * dt);
            var before = transform.position;
            Step(Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward, dt);
            float moved = Flat(before, transform.position);
            if (!_turningInPlace && want > 0.5f && moved < want * dt * 0.2f) _stuckT += dt; else _stuckT = Mathf.Max(0f, _stuckT - dt * 2f);
            return false;
        }

        /// <summary>Collide-and-slide with the body capsule, then ground probe (steps up to maxStepUp, never off a drop into nothing), then depenetration.</summary>
        void Step(Vector3 fwd, float dt)
        {
            _moving = _speed > 0.05f;
            var pos = transform.position; var delta = fwd * (_speed * dt); float len = delta.magnitude; const float skin = 0.03f;
            var bodyFwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            if (len > 1e-5f)
            {
                var dir = delta / len;
                if (Sweep(pos, bodyFwd, dir, len + skin, out var hit))
                {
                    float go = Mathf.Max(0f, hit.distance - skin); pos += dir * go;
                    var n = hit.normal; n.y = 0f; n.Normalize();
                    var slide = Vector3.ProjectOnPlane(delta - dir * go, n); slide.y = 0f; float sl = slide.magnitude;
                    if (sl > 1e-5f) { if (Sweep(pos, bodyFwd, slide / sl, sl + skin, out var h2)) sl = Mathf.Max(0f, h2.distance - skin); pos += slide.normalized * sl; }
                    if (go < len * 0.3f) _stuckT += dt * 0.5f;
                }
                else pos += delta;
            }
            float up = S.maxStepUp;
            if (Ground(pos + Vector3.up * (up + 0.05f), up + 1.6f, out var g, out var nrm) && Vector3.Angle(nrm, Vector3.up) <= S.maxSlope + 10f && g.y - transform.position.y <= up)
            {
                pos.y = g.y; transform.position = pos;
                _up = Vector3.Slerp(_up, Vector3.Slerp(Vector3.up, nrm, 0.6f), 1f - Mathf.Exp(-dt * 8f));
            }
            else { _speed = 0f; _stuckT += dt; }
            ApplyRotation();
            Depenetrate();
        }

        static readonly Collider[] Pen = new Collider[32];
        void Depenetrate()
        {
            if (!S.depenetrate) return;
            SyncBody();
            Ends(transform.position, Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward, out var a, out var b);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, S.bodyRadius, Pen, S.environmentLayers, QueryTriggerInteraction.Ignore);
            var push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var c = Pen[i]; if (Ignore(c)) continue;
                if (Physics.ComputePenetration(_body, _body.transform.position, _body.transform.rotation, c, c.transform.position, c.transform.rotation, out var d, out var dist))
                { if (d.y > 0.7f) continue; d.y = 0f; push += d.normalized * dist; }
            }
            if (push.sqrMagnitude > 1e-6f) transform.position += Vector3.ClampMagnitude(push, 0.25f);
        }
        void ApplyRotation()
        {
            var f = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(f, _up).normalized, _up);
        }

        void Face(Vector3 p, float dt)
        {
            var to = p - transform.position; to.y = 0f; if (to.sqrMagnitude < 0.01f) return;
            float yawTo = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _yaw = Mathf.SmoothDampAngle(_yaw, yawTo, ref _yawVel, S.turnSmoothTime, S.turnSpeed * 0.6f, dt); ApplyRotation();
        }

        /// <summary>Puts the dog on safe ground near the player (out of the camera's view when it can).</summary>
        public void TeleportNearPlayer()
        {
            if (!FindPlayer()) return;
            var P = _player.position; var cam = Camera.main; float dist = S.callArriveDistance;
            Vector3 best = Vector3.zero; bool haveVisible = false, have = false;
            foreach (float a in new[] { 180f, 150f, 210f, 120f, 240f, 90f, 270f, 45f, 315f, 0f })
            {
                if (have) break;
                foreach (float k in new[] { 1f, 1.6f, 0.6f })
                {
                    var p = P + Quaternion.Euler(0f, _player.eulerAngles.y + a, 0f) * Vector3.forward * (dist * k);
                    if (!Ground(p + Vector3.up * 3f, 8f, out var g, out var n) || Vector3.Angle(n, Vector3.up) > S.maxSlope || Mathf.Abs(g.y - P.y) > 2f) continue;
                    if (Overlaps(g + Vector3.up * 0.6f, 0.3f)) continue;
                    if (LineBlocked(P + Vector3.up * 1f, g + Vector3.up * 0.5f)) continue;
                    bool hidden = !S.callOutOfView || !cam || Hidden(cam, g + Vector3.up * 0.4f);
                    if (hidden) { best = g; have = true; break; }
                    if (!haveVisible) { haveVisible = true; best = g; }
                }
            }
            if (!have && !haveVisible) { if (!Ground(P - _player.forward * 1.5f + Vector3.up * 2f, 6f, out best, out _)) best = P - _player.forward * 1.5f; }
            transform.position = best; _teleported = true; _lastPos = best; _speed = 0f; _stuckT = 0f; _turningInPlace = false; _yawVel = 0f; _pose = Pose.None; _poseUntil = 0f; _find = null; _pointUntil = -1f;
            var to = P - best; to.y = 0f; if (to.sqrMagnitude > 0.01f) _yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg; _animYaw = _yaw; _up = Vector3.up; ApplyRotation();
            // no Happy one-shot here: it latched and played Idle_2 over the run after a Call; start straight in locomotion
            if (animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController) { animator.ResetTrigger(HHappy); animator.Play("Locomotion", 0, 0f); animator.SetFloat(HSpeed, 0f); animator.SetFloat(HTurn, 0f); }
            Play(barkClip, 0.9f);
        }

        bool Overlaps(Vector3 c, float r)
        {
            int n = Physics.OverlapSphereNonAlloc(c, r, Cols, S.environmentLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!Ignore(Cols[i])) return true;
            return false;
        }

        bool LineBlocked(Vector3 a, Vector3 b)
        {
            var d = b - a; int n = Physics.RaycastNonAlloc(a, d.normalized, Hits, d.magnitude, S.environmentLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!Ignore(Hits[i].collider)) return true;
            return false;
        }

        bool Hidden(Camera cam, Vector3 p)
        {
            var v = cam.WorldToViewportPoint(p);
            if (v.z <= 0f || v.x < -0.05f || v.x > 1.05f || v.y < -0.05f || v.y > 1.05f) return true;
            return LineBlocked(cam.transform.position, p);
        }

        // ================================================================== call
        /// <summary>Whistle the dog over (C, the dog menu, the journal). Far, stuck or out of sight: brought next to you; near: runs to you.</summary>
        public bool Call()
        {
            if (!FindPlayer()) return false;
            if (Dead) { Toast($"{DogName} is gone."); return false; }
            if (Time.time < _nextCall) { Toast($"{DogName} is already coming."); return false; }
            _nextCall = Time.time + S.callCooldown; Debug.Log("[ASH] Dog called");
            if (whistleClip) AudioSource.PlayClipAtPoint(whistleClip, _player.position + Vector3.up * 1.6f, 0.8f);
            if (Downed) { Toast($"{DogName} is down and can't come. Follow its marker on the compass."); return false; }
            Mode = ASHDogMode.Follow; _pose = Pose.None; _find = null; _pointUntil = -1f; _warnUntil = 0f;
            var cam = Camera.main;
            float d = Flat(transform.position, _player.position);
            bool unseen = cam && Hidden(cam, transform.position + Vector3.up * 0.4f) && d > S.followDistance * 2f;
            if (d > S.callTeleportBeyond || _stuckT > 0.5f || unseen) { TeleportNearPlayer(); Toast($"{DogName} comes running."); }
            else { _callRunUntil = Time.time + 8f; Play(barkClip, 0.8f); Toast($"{DogName} comes to you."); }
            Notify(); return true;
        }

        // ================================================================== threats / defend
        bool IsThreat(Collider c, out Transform root)
        {
            root = null; if (!c) return false;
            var t = c.transform; if (t.IsChildOf(transform) || (_player && t.IsChildOf(_player))) return false;
            var hc = c.GetComponentInParent<vIHealthController>(); if (hc != null && hc.isDead) return false;
            var th = c.GetComponentInParent<IASHDogThreat>(); if (th != null) { root = ((Component)th).transform; return th.IsThreat; }
            var dummy = c.GetComponentInParent<ASH.Combat.ASHTrainingDummy>(); if (dummy) { root = dummy.transform; return S.dummyIsThreat; }
            var tags = S.enemyTags; if (tags == null || tags.Count == 0) return false;
            for (var p = t; p; p = p.parent)
            {
                var go = p.gameObject; string tag = go.tag, layer = LayerMask.LayerToName(go.layer);
                for (int i = 0; i < tags.Count; i++) if (!string.IsNullOrEmpty(tags[i]) && (tag == tags[i] || layer == tags[i])) { root = hc != null ? ((Component)hc).transform : p; return true; }
            }
            return false;
        }

        void ScanThreats()
        {
            if (Mode != ASHDogMode.Stay && AssistOk(_assistTarget)) { SetThreat(_assistTarget); return; }
            if (!S.warnDanger && Mode != ASHDogMode.Defend) { SetThreat(null); return; }
            float r = Mathf.Max(S.DangerRadius(Level), Mode == ASHDogMode.Defend ? S.defendRadius : 0f);
            int n = Physics.OverlapSphereNonAlloc(transform.position, r, Cols, ~0, QueryTriggerInteraction.Collide);
            Transform best = null; float bd = float.MaxValue; var refPos = Mode == ASHDogMode.Defend ? _player.position : transform.position;
            for (int i = 0; i < n; i++)
            {
                if (!IsThreat(Cols[i], out var root) || !root) continue;
                float d = (root.position - refPos).sqrMagnitude; if (d < bd) { bd = d; best = root; }
            }
            SetThreat(best);
        }

        void SetThreat(Transform t)
        {
            bool fresh = t && t != _threat;
            _threat = t;
            if (!t) { if (_threatMarker != null) { ASHCompass.Unregister(_threatMarker); _threatMarker = null; } return; }
            if (S.dangerCompassMarkers) { _threatMarker = "dogthreat"; ASHCompass.Register(t, ASHCompassMarkerKind.Enemy, default, _threatMarker); }
            if (S.warnDanger && (fresh || Time.time >= _nextWarn) && Time.time >= _nextWarn)
            {
                _nextWarn = Time.time + S.warnCooldown; _warnUntil = Time.time + S.warnSeconds;
                if (S.growlOnWarn) Growl(); else Play(barkDoubleClip ? barkDoubleClip : barkClip); Toast($"{DogName} growls. Something's out there.");
                AddXp(S.xpWarn);
            }
        }

        bool Engage(Vector3 P)
        {
            var tp = _threat.position;
            if (Flat(tp, P) > S.defendRadius && Flat(tp, transform.position) > S.attackRange * 2f && Time.time - _lastHurt > 3f) return false;
            Activity = "Attacking";
            if (S.growlOnDefendApproach && _growledAt != _threat && Flat(tp, P) <= S.defendRadius) { _growledAt = _threat; _nextGrowl = 0f; Growl(); }
            if (Flat(tp, transform.position) > S.attackRange) { MoveTo(tp, S.runSpeed, S.attackRange * 0.85f); return true; }
            Stop(); Face(tp, Time.deltaTime * 2f);
            if (Time.time >= _nextAttack)
            {
                _nextAttack = Time.time + S.attackCooldown; if (animator) animator.SetTrigger(HAttack);
                _pendingHit = Time.time + S.attackHitDelay; _pendingTarget = _threat;
                if (UnityEngine.Random.value < S.growlBeforeAttackChance) { if (!Growl()) Play(barkClip, 0.6f); } else Play(barkClip, 0.6f);
            }
            return true;
        }

        void ApplyBite()
        {
            var t = _pendingTarget; _pendingTarget = null;
            if (!t || Flat(t.position, transform.position) > S.attackRange + 0.8f) return;
            var recv = t.GetComponentInParent<vIDamageReceiver>(); if (recv == null) recv = t.GetComponentInChildren<vIDamageReceiver>(); if (recv == null) return;
            var hc = recv as vIHealthController; bool wasAlive = hc == null || !hc.isDead;
            var dmg = new vDamage(Mathf.RoundToInt(S.Damage(Level))) { sender = transform, hitPosition = t.position + Vector3.up * 0.7f, damageType = "bite" };
            recv.TakeDamage(dmg);
            AddXp(S.xpBite, false);
            if (hc != null && wasAlive && hc.isDead) { AddXp(S.xpKill); Toast($"{DogName} brought it down."); }
        }

        public void TakeDamage(vDamage damage)
        {
            if (damage == null || Downed || Dead) return;
            _onStart.Invoke(damage);
            float a = damage.damageValue * (Level >= S.toughLevel ? S.toughMul : 1f);
            Health = Mathf.Max(0f, Health - a); _lastHurt = Time.time;
            if (animator) animator.SetTrigger(HHit);
            if (a > MaxHealth * 0.15f) Play(whineClip, 0.8f);
            else if (UnityEngine.Random.value < S.growlOnHurtChance) Growl();
            if (damage.sender && !damage.sender.IsChildOf(_player ? _player : transform)) { var c = damage.sender.GetComponentInChildren<Collider>(); if (c && IsThreat(c, out var r) && r) SetThreat(r); }
            _onRecv.Invoke(damage);
            if (Health <= 0f) GoDown();
            Notify();
        }

        void GoDown()
        {
            Health = 0f; Downed = true; Dead = false; _downT = 0f; _pendingHit = -1f; _speed = 0f; _whimperT = 0f;
            string hint = string.IsNullOrEmpty(S.downedHint) ? "Your dog is hurt" : S.downedHint;
            Toast($"{hint}. Hold E on {DogName} with a bandage, first aid kit or raw meat.");
            Play(whineClip); SetPose(Pose.Down);
            ASHCompass.Register(transform, ASHCompassMarkerKind.Custom, S.dogMarkerColor, "dog"); _compassReg = true;
            if (!_hud) _hud = FindAnyObjectByType<ASHHud>();
            if (_hud) _hud.SetCompanionHint(hint);
            Notify();
        }

        void DownedUpdate(float dt)
        {
            _speed = 0f; SetPose(Pose.Down); Activity = "Down, needs help";
            Dead = false;
            _downT += dt;
            _whimperT -= dt;
            if (_whimperT <= 0f)
            {
                Play(whineClip, 0.85f);
                _whimperT = Mathf.Max(0.8f, S.whimperInterval);
            }
            if (S.selfReviveSeconds > 0f && _downT >= S.selfReviveSeconds) Revive(false);
        }

        public void Revive(bool byPlayer)
        {
            if (!Downed) return;
            Downed = false; Dead = false; Health = MaxHealth * S.reviveHealth; SetPose(Pose.None); _retreat = true; _whimperT = 0f;
            if (byPlayer) AddBond(S.bondPerPet);
            if (!_hud) _hud = FindAnyObjectByType<ASHHud>();
            if (_hud) _hud.SetCompanionHint(null);
            Toast($"{DogName} is back on its feet."); Notify();
        }

        public static bool IsReviveItem(ASHItemDefinition it)
        {
            if (!it) return false;
            if (it.category == ASHItemCategory.Medical) return true;
            string id = (it.itemId ?? "").Replace(" ", "").Replace("_", "").ToLowerInvariant();
            if (id == "rawmeat" || id.Contains("rawmeat")) return true;
            string n = (it.displayName ?? "").ToLowerInvariant();
            return n == "raw meat" || n.Contains("raw meat");
        }

        public static ASHItemDefinition FindReviveItem(ASHInventory inv, out int slot)
        {
            slot = -1;
            if (inv == null) return null;
            var slots = inv.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.IsEmpty || !IsReviveItem(s.item)) continue;
                slot = i; return s.item;
            }
            return null;
        }

        // ================================================================== finds (loot / supply crates)
        void ScanFinds()
        {
            if (Mode != ASHDogMode.Follow || _find || _threat || Time.time < _nextFind || _retreat) return;
            if (!S.findLoot && !(S.findCrates && Level >= S.crateFindLevel)) return; // loot boxes / supply crates (loose pickups only with findLoot)
            var P = _player.position; float r = S.DetectRadius(Level);
            int n = Physics.OverlapSphereNonAlloc(transform.position, r, Cols, ~0, QueryTriggerInteraction.Collide);
            Transform best = null; bool bestCrate = false; string label = null; float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = Cols[i]; if (!c || !c.enabled) continue;
                Transform t = null; bool crate = false; string l = null;
                if (S.findCrates && Level >= S.crateFindLevel) { var cr = c.GetComponentInParent<ASHSupplyCrate>(); if (cr && !cr.Opened) { t = cr.transform; crate = true; l = "Supply crate"; } }
                if (!t) { var fd = c.GetComponentInParent<IASHDogFindable>(); if (fd != null && fd.DogCanFind) { t = ((Component)fd).transform; crate = true; l = string.IsNullOrEmpty(fd.DogFindLabel) ? "Loot box" : fd.DogFindLabel; } }
                if (!t && S.findLoot) { var pk = c.GetComponentInParent<ASHItemPickup>(); if (pk && pk.Item && pk.isActiveAndEnabled) { t = pk.transform; l = pk.Item.displayName; } }
                if (!t) continue;
                int id = t.GetHashCode(); if (_seen.TryGetValue(id, out var until) && Time.time < until) continue;
                float dp = Flat(t.position, P); if (dp < S.ignoreCloserThan || dp > S.leashRadius) continue;
                float d = (t.position - transform.position).sqrMagnitude; if (d < bd) { bd = d; best = t; bestCrate = crate; label = l; }
            }
            if (!best) return;
            _find = best; _findIsCrate = bestCrate; _findLabel = label; _findGiveUp = Time.time + 25f; _pointUntil = -1f;
            _seen[best.GetHashCode()] = Time.time + S.refindCooldown;
        }

        bool FindStep(float dt, Vector3 P)
        {
            if (!_find || Time.time > _findGiveUp || Flat(_find.position, P) > S.leashRadius * 1.25f) { _find = null; _nextFind = Time.time + S.findCooldown * 0.5f; return false; }
            if (_pointUntil < 0f)
            {
                Activity = _findIsCrate ? "Found something" : "Sniffing something out";
                if (!MoveTo(_find.position, S.trotSpeed, 1.4f)) return true;
                _pointUntil = Time.time + S.pointSeconds;
                Play(barkDoubleClip ? barkDoubleClip : barkClip);
                ASHDogMarker.Show(_find, _findIsCrate ? S.crateMarkerColor : S.lootMarkerColor, _findLabel, S.markerSeconds);
                string id = "dogfind" + _find.GetHashCode();
                ASHCompass.Register(_find.position, ASHCompassMarkerKind.Custom, _findIsCrate ? S.crateMarkerColor : S.lootMarkerColor, id); _markers.Add((id, Time.time + S.markerSeconds));
                Toast(_findIsCrate ? $"{DogName} found a supply crate." : $"{DogName} found something: {_findLabel}.");
                AddXp(_findIsCrate ? S.xpFindCrate : S.xpFindLoot);
            }
            Activity = "Pointing"; Stop(); Face(_find.position, dt); SetPose(Pose.Alert);
            if (Time.time >= _pointUntil) { _find = null; _pointUntil = -1f; _nextFind = Time.time + S.findCooldown; }
            return true;
        }

        void Markers()
        {
            for (int i = _markers.Count - 1; i >= 0; i--) if (Time.time >= _markers[i].until) { ASHCompass.Unregister(_markers[i].id); _markers.RemoveAt(i); }
            if (S.dogCompassMarker && !_compassReg && ASHCompass.Instance) { ASHCompass.Register(transform, ASHCompassMarkerKind.Custom, S.dogMarkerColor, "dog"); _compassReg = true; }
            else if (!S.dogCompassMarker && _compassReg && !Downed) { ASHCompass.Unregister("dog"); _compassReg = false; }
        }

        // ================================================================== needs, bond, levels
        void Needs(float dt)
        {
            float gh = dt / Mathf.Max(1f, S.realSecondsPerGameHour);
            bool starving = false;
            if (S.hunger) { Hunger = Mathf.Max(0f, Hunger - S.hungerPerGameHour * gh); starving = Hunger < S.starvingBelow; }
            if (starving && S.bond) Bond = Mathf.Max(0f, Bond - S.bondLossStarving * gh);
            if (!Downed && !Dead && !starving && Time.time - _lastHurt > 5f && Health < MaxHealth) Health = Mathf.Min(MaxHealth, Health + S.healthRegen * dt);
        }

        void AddBond(float a) { if (S.bond) Bond = Mathf.Clamp(Bond + a, 0f, 100f); }

        public void AddXp(float amount, bool notify = true)
        {
            if (amount <= 0f || Level >= S.maxLevel) return;
            Xp += amount * (1f + (S.bond ? S.bondBonus * Bond / 100f : 0f));
            var before = S.Abilities();
            while (Level < S.maxLevel && Xp >= XpNeed)
            {
                Xp -= XpNeed; Level++; Health = Mathf.Min(MaxHealth, Health + S.healthPerLevel);
                var unlocked = new List<string>(); foreach (var a in before) if (a.level == Level) unlocked.Add(a.name);
                Toast($"{DogName} reached level {Level}!" + (unlocked.Count > 0 ? " New: " + string.Join(", ", unlocked) : ""));
                Play(barkDoubleClip);
            }
            if (Level >= S.maxLevel) Xp = 0f;
            EnsurePack();
            if (notify) Notify();
        }

        public void NotifyPlayerCombat(Transform who, bool playerWasHit)
        {
            if (!who || Dead || Downed || Mode == ASHDogMode.Stay) return;
            var animal = who.GetComponentInParent<ASHAnimal>();
            var root = animal ? animal.transform : who;
            if (animal && animal.IsDead) return;
            _assistTarget = root;
            SetThreat(root);
        }

        bool AssistOk(Transform t)
        {
            if (!t) return false;
            var animal = t.GetComponentInParent<ASHAnimal>();
            if (animal && (animal.IsDead || animal.Harvested)) return false;
            var range = ASHWildlifeSettings.I ? ASHWildlifeSettings.I.dogAssistRange : 28f;
            if (_player && Flat(t.position, _player.position) > range) return false;
            return t.gameObject.activeInHierarchy;
        }

        bool AssistStep(Vector3 P)
        {
            if (!AssistOk(_assistTarget)) { _assistTarget = null; return false; }
            SetThreat(_assistTarget);
            var animal = _assistTarget.GetComponentInParent<ASHAnimal>();
            if (animal && animal.DogPolicy == ASHDogWildlifePolicy.WarnDistractRetreat) return Distract(P);
            return Engage(P);
        }

        bool Distract(Vector3 P)
        {
            var tp = _threat ? _threat.position : _assistTarget.position;
            Activity = "Distracting";
            float d = Flat(tp, transform.position);
            float standOff = ASHWildlifeSettings.I ? ASHWildlifeSettings.I.dogDistractDistance : 5f;
            float backFor = ASHWildlifeSettings.I ? ASHWildlifeSettings.I.dogDistractRetreatSeconds : 3.5f;
            if (Time.time < _distractUntil || d < standOff * 0.7f)
            {
                if (d < standOff * 0.7f) _distractUntil = Time.time + backFor;
                MoveTo(P, S.runSpeed, S.defendFollowDistance * 0.8f);
                if (Time.time >= _nextWarn) { _nextWarn = Time.time + 2.5f; Growl(); }
                return true;
            }
            var side = Vector3.Cross(Vector3.up, (tp - P).normalized);
            if (side.sqrMagnitude < 0.01f) side = transform.right;
            var flank = tp + side.normalized * standOff;
            if (Flat(flank, transform.position) > 1.4f) { MoveTo(flank, S.runSpeed, 1.1f); return true; }
            Stop(); Face(tp, Time.deltaTime * 2f); SetPose(Pose.Alert);
            if (Time.time >= _nextWarn) { _nextWarn = Time.time + 2f; Growl(); }
            _distractUntil = Time.time + backFor;
            return true;
        }

        public void SetMode(ASHDogMode m)
        {
            if (Dead) return;
            Debug.Log($"[ASH] Dog mode -> {m}");
            Mode = m; _stayPos = transform.position; _stayT = 0f; _find = null; _pointUntil = -1f; _callRunUntil = 0f;
            if (m == ASHDogMode.Stay) _assistTarget = null;
            Toast(m == ASHDogMode.Stay ? $"{DogName}: stay." : m == ASHDogMode.Follow ? $"{DogName}: follow." : $"{DogName}: defend me.");
            Play(barkClip, 0.5f); Notify();
        }

        public bool Pet()
        {
            if (Dead || Downed) return false;
            if (animator) animator.SetTrigger(HHappy);
            if (Time.time >= _nextPet) { _nextPet = Time.time + S.petCooldown; AddBond(S.bondPerPet); AddXp(S.xpPet, false); Toast($"{DogName} wags its tail."); }
            else Toast($"{DogName} leans into your hand.");
            Notify(); return true;
        }

        public static bool IsDogFood(ASHItemDefinition it) => it && it.IsConsumable && it.category != ASHItemCategory.Medical && it.restoreHunger > 0f;

        /// <summary>Feeds the most filling food in the pack (or the given item).</summary>
        public bool Feed(ASHInventory inv, ASHItemDefinition only = null)
        {
            if (Dead || Downed || !inv) return false;
            ASHItemDefinition best = only;
            if (!best) foreach (var s in inv.Slots) if (s != null && !s.IsEmpty && IsDogFood(s.item) && (!best || s.item.restoreHunger > best.restoreHunger)) best = s.item;
            if (!best || !IsDogFood(best) || inv.Count(best) <= 0) { Toast("No food in your pack."); return false; }
            if (S.hunger && Hunger >= 99f) { Toast($"{DogName} isn't hungry."); return false; }
            inv.Remove(best, 1);
            Hunger = Mathf.Min(100f, Hunger + best.restoreHunger * S.feedFactor);
            AddBond(S.bondPerFeed); AddXp(S.xpFeed, false); SetPose(Pose.Eat, 3f);
            Toast($"{DogName} eats the {best.displayName}."); Notify(); return true;
        }

        // ================================================================== pack
        /// <summary>Moves one inventory slot into the dog's pack. Returns the amount moved.</summary>
        public int GiveFromSlot(ASHInventory inv, int slot)
        {
            if (!inv || slot < 0 || slot >= inv.Slots.Count) return 0; EnsurePack();
            var s = inv.Slots[slot]; if (s == null || s.IsEmpty || ASHInventory.IsKeyItem(s.item)) return 0;
            var item = s.item; int want = s.amount; float cond = s.condition; int limit = Mathf.Max(1, ASHInventory.StackLimit(item)); int moved = 0;
            int pcount = PackSlots;
            for (int i = 0; i < pcount && moved < want; i++) { var p = _pack[i]; if (!p.IsEmpty && p.item == item && p.amount < limit) { int k = Mathf.Min(limit - p.amount, want - moved); p.amount += k; moved += k; } }
            for (int i = 0; i < pcount && moved < want; i++) { var p = _pack[i]; if (p.IsEmpty) { int k = Mathf.Min(limit, want - moved); p.item = item; p.amount = k; p.condition = cond; moved += k; } }
            if (moved > 0) { inv.RemoveFromSlot(slot, moved); Notify(); }
            return moved;
        }

        /// <summary>Moves a dog pack slot into the player's inventory. Returns the amount moved.</summary>
        public int TakeSlot(ASHInventory inv, int i)
        {
            if (!inv || i < 0 || i >= _pack.Count) return 0;
            var p = _pack[i]; if (p.IsEmpty) return 0;
            int added = inv.AddItemWithCondition(p.item, p.amount, p.condition);
            p.amount -= added; if (p.amount <= 0) p.Clear();
            if (added > 0) Notify();
            return added;
        }

        // ================================================================== animation
        void SetPose(Pose p, float hold = 0f)
        {
            if (Time.time < _poseUntil && p != Pose.Down && _pose != Pose.None && p == Pose.None && !_moving) return;
            _pose = p; _poseUntil = hold > 0f ? Time.time + hold : 0f;
        }

        void Animate(float dt)
        {
            if (!animator || !animator.runtimeAnimatorController) return;
            var pose = _moving && _pose != Pose.Down ? Pose.None : _pose;
            if (_poseUntil > 0f && Time.time >= _poseUntil && !_moving) { _poseUntil = 0f; if (_pose == Pose.Alert || _pose == Pose.Eat) _pose = Pose.None; }
            animator.SetInteger(HPose, (int)pose);
            // turn blend from the actual yaw rate (smoothed)
            float rate = Mathf.DeltaAngle(_animYaw, _yaw) / dt; _animYaw = _yaw;
            _turnRate = Mathf.Lerp(_turnRate, rate, 1f - Mathf.Exp(-dt * 10f));
            animator.SetFloat(HTurn, Mathf.Clamp(_turnRate / Mathf.Max(1f, S.turnBlendFullRate), -1f, 1f), 0.12f, dt);
            // speed sync: the blend runs on the measured ground speed (thresholds = the dog's walk / trot / run speeds) and the
            // playback rate = ground speed / the blended clips' own ground speed, so the paws stay planted
            float v = _actualSpeed, play = 1f;
            float gw = Mathf.Max(0.1f, S.walkSpeed), gt = Mathf.Max(gw + 0.01f, S.trotSpeed), gr = Mathf.Max(gt + 0.01f, S.runSpeed);
            float cw = Mathf.Max(0.05f, S.walkClipSpeed), ct = Mathf.Max(0.05f, S.trotClipSpeed), cr = Mathf.Max(0.05f, S.runClipSpeed);
            if (v > 0.05f)
            {
                float nat = v <= gw ? cw : v <= gt ? Mathf.Lerp(cw, ct, (v - gw) / (gt - gw)) : v <= gr ? Mathf.Lerp(ct, cr, (v - gt) / (gr - gt)) : cr;
                float spd = v <= gw ? gw : v; // below walk speed the walk clip fades in from idle at the walk rate
                play = Mathf.Clamp(spd / nat, S.animRateMin, S.animRateMax);
            }
            if (pose != Pose.None) play = 1f;
            _animRate = Mathf.Lerp(_animRate, play, 1f - Mathf.Exp(-dt * 8f));
            animator.speed = _animRate * S.animSpeedScale;
            animator.SetFloat(HSpeed, v, 0.08f, dt);
            if (_moving) { animator.ResetTrigger(HHappy); animator.ResetTrigger(HFidget); }
            PawSteps_(dt);
        }

        // ================================================================== use (E)
        public float GetUsePriority(in ASHUseContext c) => Downed ? 70f : 55f;
        public string GetPrompt(in ASHUseContext c)
        {
            if (Downed)
            {
                var it = FindReviveItem(c.Inventory, out _);
                if (it) return $"[Hold E] Use {it.displayName} to help {DogName}";
                return "Need a bandage, first aid kit or raw meat to help " + DogName;
            }
            return $"{DogName}  (Lv {Level})";
        }
        public float GetHoldDuration(in ASHUseContext c) => Downed && FindReviveItem(c.Inventory, out _) ? S.reviveHoldSeconds : 0f;
        public bool TryUse(in ASHUseContext c)
        {
            if (Downed)
            {
                var it = FindReviveItem(c.Inventory, out int slot);
                if (!it || c.Inventory == null) return false;
                if (!c.Inventory.RemoveFromSlot(slot, 1) && !c.Inventory.Remove(it, 1)) return false;
                Revive(true);
                return true;
            }
            return OpenMenu != null && OpenMenu(this, c.Inventory, c.PlayerTransform);
        }

        // ================================================================== save
        [Serializable] sealed class SlotState { public string id; public int amount; public float condition = 1f; }
        [Serializable] sealed class State
        {
            public string name; public int mode, level = 1; public float xp, health = -1f, hunger = 80f, bond = 20f; public bool downed, dead;
            public bool hasPos, riding; public Vector3 pos, stay; public float yaw; public List<SlotState> pack = new List<SlotState>();
        }
        public string SaveKey => "dogCompanion";
        public int RestoreOrder => 30;
        public string CaptureJson()
        {
            var s = new State { name = _name, mode = (int)Mode, level = Level, xp = Xp, health = Health, hunger = Hunger, bond = Bond, downed = Downed, dead = Dead, hasPos = true, pos = transform.position, stay = _stayPos, yaw = _yaw, riding = Riding };
            foreach (var p in _pack) s.pack.Add(p.IsEmpty ? new SlotState() : new SlotState { id = p.item.itemId, amount = p.amount, condition = p.condition });
            return JsonUtility.ToJson(s);
        }
        public void RestoreJson(string json)
        {
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json);
            if (s == null) return;
            Debug.Log($"[ASH] Dog restored from save (mode {(ASHDogMode)s.mode}, level {s.level})");
            _name = s.name; Mode = (ASHDogMode)Mathf.Clamp(s.mode, 0, 2); Level = Mathf.Clamp(s.level, 1, S.maxLevel); Xp = Mathf.Max(0f, s.xp);
            Health = s.health < 0f ? MaxHealth : Mathf.Min(s.health, MaxHealth); Hunger = s.hunger; Bond = s.bond; Downed = s.downed || s.dead; Dead = false;
            if (Downed) { Health = 0f; SetPose(Pose.Down); if (!_hud) _hud = FindAnyObjectByType<ASHHud>(); if (_hud) _hud.SetCompanionHint(string.IsNullOrEmpty(S.downedHint) ? "Your dog is hurt" : S.downedHint); }
            if (s.hasPos) { transform.position = s.pos; _stayPos = s.stay; _yaw = s.yaw; ApplyRotation(); }
            _pack.Clear();
            if (s.pack != null) foreach (var p in s.pack) { var it = p == null || string.IsNullOrEmpty(p.id) ? null : ASHItemRegistry.I.Find(p.id); _pack.Add(it ? new PackSlot { item = it, amount = p.amount, condition = p.condition } : new PackSlot()); }
            EnsurePack(); _pose = Downed ? Pose.Down : Pose.None; _ride = RidePhase.None; _rideFromSave = s.riding; Notify();
        }
    }
}