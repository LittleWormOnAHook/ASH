using System.Collections.Generic;
using ASH.Combat;
using ASH.Companion;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Movement;
using Invector;
using UnityEngine;

namespace ASH.Wildlife
{
    public enum ASHAnimalMode { Wander, Idle, Graze, Curious, Flee, LookBack, Retreat, Stalk, Chase, Attack, Bluff, Charge, Trapped, Dead, Surprise }

    /// <summary>
    /// Species AI on the dog-style ground locomotor, using each animal's own Malbers clips.
    /// Invector damage in and out, senses (sight cone, hearing, wind scent, gunshots), alert stages,
    /// herd and pack tactics, species-specific combat, knife harvest, traps, size variants.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHAnimal : MonoBehaviour, IASHDogThreat, IASHUsable, vIDamageReceiver
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHAnimal()
        {
            Tokens.Clear();
            Living.Clear();
            _hourCached = 14f;
            _hourCachedAt = -999f;
        }

        public ASHWildlifeKind kind = ASHWildlifeKind.Wolf;
        public ASHWildlifeSize size = ASHWildlifeSize.Adult;
        public int packId;
        public ASHWildlifeSettings settings;

        ASHGroundLocomotion _loco;
        ASHWildlifeAnim _vis;
        Transform _player, _dog, _focus;
        ASHWildlifeSettings.Species Sp => (settings ? settings : ASHWildlifeSettings.I)?.Of(kind);
        ASHWildlifeSettings W => settings ? settings : ASHWildlifeSettings.I;

        float _hp, _hpMax, _nextAttack, _wanderT, _idleUntil, _scareUntil, _stalkUntil, _breakFreeAt, _harvestedAt = -1f;
        float _alert, _senseT, _grazeUntil, _lookUntil, _retreatUntil, _bluffUntil, _chargeUntil, _attackUntil, _attackHitAt, _sprintT, _restT;
        Vector3 _home, _wander, _flee, _lastSense;
        bool _dead, _harvested, _aware, _playerHitMe, _dogHitMe, _attackHit, _hasBluffed, _hitboxes;
        int _packSlot;
        ASHAnimalMode _mode = ASHAnimalMode.Wander;

        [Tooltip("0 = derive from spawn seed + species + placed position (stable across sessions).")]
        public int personalitySeed;

        [Header("Temperament (aggressive species): 0 = Aggressive, 0.5 = Passive-aggressive, 1 = Sneaky")]
        [Tooltip("Tick to use the slider below. Unticked, the slider shows this animal's seeded roll (species range in ASH_WildlifeSettings).")]
        public bool overrideTemperament;
        [Range(0f, 1f)] public float temperament = 0.5f;
        /// <summary>Seed-rolled traits inside the species' ranges (see ASH_WildlifeSettings > species > personality).</summary>
        public struct Personality { public float bold, aggr, speed, flee, wander, graze, cohesion, standOff, surprise, temper; public int seed; public bool circleLeft; }
        Personality _p = new Personality { bold = 0.5f, aggr = 0.5f, speed = 1f, flee = 1f, wander = 1f, graze = 1f, cohesion = 1f, standOff = 1f, surprise = 1f };
        float _pAt = -99f;
        public Personality Traits => _p;

        enum Engage { Approach, Hold, Lunge, BackOff, Circle }
        float _circleR, _circleSign = 1f, _phaseAt;
        int _warns;
        bool _committed;
        float _lastSenseAt = -999f, _corpseBaseY, _corpseLift, _corpseLiftTarget, _corpseAt = -1f, _corpseSampleAt;
        Engage _phase = Engage.Approach;
        float _holdUntil, _phaseUntil, _tokenAt, _tokenBlockedUntil, _lastPelletT = -9f;
        bool _hasToken;
        Transform _tokenTarget;
        Vector3 _backOff;
        static readonly Dictionary<Transform, List<ASHAnimal>> Tokens = new Dictionary<Transform, List<ASHAnimal>>();
        Transform _headBone, _chestBone;
        ASHSnareHook _trap;
        readonly OnReceiveDamage _onStart = new OnReceiveDamage();
        readonly OnReceiveDamage _onRecv = new OnReceiveDamage();

        static readonly List<ASHAnimal> Living = new List<ASHAnimal>();
        static float _hourCached = 14f, _hourCachedAt = -999f;

        public bool IsThreat => !_dead && !_harvested && Sp != null && Sp.temperament != ASHWildlifeTemperament.Passive;
        public bool IsDead => _dead;
        public bool Harvested => _harvested;
        public bool IsTrapped => _mode == ASHAnimalMode.Trapped;
        public bool IsAggroOnPlayerOrDog =>
            !_dead && !_harvested && Sp != null && Sp.temperament != ASHWildlifeTemperament.Passive &&
            (_mode == ASHAnimalMode.Chase || _mode == ASHAnimalMode.Attack || _mode == ASHAnimalMode.Stalk ||
             _mode == ASHAnimalMode.Charge || _mode == ASHAnimalMode.Bluff || _mode == ASHAnimalMode.Retreat ||
             (_mode == ASHAnimalMode.Trapped && IsThreat) ||
             (_mode == ASHAnimalMode.Surprise && Sp.temperament != ASHWildlifeTemperament.Passive && _sKind != ASHSurpriseKind.StandAndHuff));
        public ASHAnimalMode Mode => _mode;
        public ASHDogWildlifePolicy DogPolicy => Sp != null ? Sp.dogPolicy : ASHDogWildlifePolicy.Ignore;
        public Transform UseTransform => transform;
        public OnReceiveDamage onStartReceiveDamage => _onStart;
        public OnReceiveDamage onReceiveDamage => _onRecv;
        public struct ASHSnareHook { public GameObject trap; public ASHTrapSize size; }

        void OnEnable() { if (!Living.Contains(this)) Living.Add(this); }
        void OnDisable() { Living.Remove(this); ReleaseToken(); }
        void OnDestroy() { Living.Remove(this); ReleaseToken(); ASHTorchPresence.Scared -= OnTorchScare; }

        void Awake()
        {
            settings = settings ? settings : ASHWildlifeSettings.I;
            if (Sp != null)
            {
                _hp = _hpMax = W.Health(Sp, size);
                float sc = W.Scale(Sp, size);
                if (Mathf.Abs(transform.localScale.x - sc) > 0.01f) transform.localScale = Vector3.one * sc;
            }
            _home = transform.position;
            _wander = _home;
            FindVitalBones();
            RollPersonality();
            _senseT = (GetHashCode() & 7) * 0.04f;
            var anim = ASHWildlifeAnim.PickAnimator(gameObject); // t542: the Animator that has a controller, not a stray one
            if (anim) anim.applyRootMotion = false;
            _loco = GetComponent<ASHGroundLocomotion>();
            if (!_loco) _loco = gameObject.AddComponent<ASHGroundLocomotion>();
            _loco.Init(transform, anim, W != null ? TuneNow() : new ASHGroundLocomotion.Tune());
            _vis = GetComponent<ASHWildlifeAnim>();
            if (!_vis) _vis = gameObject.AddComponent<ASHWildlifeAnim>();
            _vis.Bind(anim);
            DisableVendorMotors();
            EnsureHitboxes();
            ASHTorchPresence.Scared += OnTorchScare;
        }

        void Start()
        {
            FindPlayer();
            if (_player) ASHPlayerCombatRelay.EnsureOn(_player);
            FindDog();
            _loco.MarkTeleported();
            _packSlot = PackSlotIndex();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _loco.TickMeasure(dt);
            if (_harvested) return;
            if (_dead) { CorpseTick(dt); return; }
            if (Sp == null || W == null) return;
            if (Time.time - _pAt > 1f) RollPersonality();
            _loco.tune = TuneNow();
            _loco.LookAheadScale = (_mode == ASHAnimalMode.Charge || _mode == ASHAnimalMode.Attack) ? W.chargeLookAheadMul : 1f; // t560: avoidance stays on while charging, shorter look-ahead
            if (!_player) FindPlayer();
            FindDog();
            RetargetIfNeeded();

            if (_mode == ASHAnimalMode.Trapped) { TrappedTick(dt); Animate(dt); return; }

            float dPlayer = _player ? Flat(transform.position, _player.position) : 999f;
            float lod = W.aiLodMeters > 10f ? W.aiLodMeters : 75f;
            bool far = dPlayer > lod;
            if (far && (GetHashCode() + Time.frameCount) % 5 != 0)
            {
                if (_mode == ASHAnimalMode.Wander || _mode == ASHAnimalMode.Idle || _mode == ASHAnimalMode.Graze) WanderTick(true);
                Animate(dt);
                return;
            }

            SenseAndDecide(dt, dPlayer);
            TickMode(dt);
            TokenHousekeeping();
            Animate(dt);
        }

        void Animate(float dt)
        {
            _loco.Animate(dt);
            if (_mode == ASHAnimalMode.Graze || _mode == ASHAnimalMode.Curious || _mode == ASHAnimalMode.LookBack || _mode == ASHAnimalMode.Bluff)
                return;
            if (_mode == ASHAnimalMode.Surprise && _sHoldPose) return;
            if (W == null || Sp == null) return;
            _vis.TickLoco(_loco.ActualSpeed, Trot(), Run(), _loco.TurningInPlace, _loco.TurnRate, dt);
        }

        void TickMode(float dt)
        {
            if (_mode != ASHAnimalMode.Surprise && _sArc) ResetArc();
            switch (_mode)
            {
                case ASHAnimalMode.Surprise: SurpriseTick(dt); break;
                case ASHAnimalMode.Trapped: TrappedTick(dt); break;
                case ASHAnimalMode.Graze: GrazeTick(); break;
                case ASHAnimalMode.Curious: CuriousTick(dt); break;
                case ASHAnimalMode.LookBack: LookBackTick(dt); break;
                case ASHAnimalMode.Flee:
                case ASHAnimalMode.Retreat: FleeTick(); break;
                case ASHAnimalMode.Stalk: StalkTick(); break;
                case ASHAnimalMode.Bluff: BluffTick(dt); break;
                case ASHAnimalMode.Charge: ChargeTick(dt); break;
                case ASHAnimalMode.Chase: ChaseTick(); break;
                case ASHAnimalMode.Attack: AttackTick(); break;
                default: WanderTick(false); break;
            }
        }

        // ------------------------------------------------------------------ senses / targeting
        void SenseAndDecide(float dt, float dPlayer)
        {
            _senseT -= dt;
            if (_senseT > 0f && _mode != ASHAnimalMode.Attack && _mode != ASHAnimalMode.Charge) return;
            _senseT = 0.18f + (GetHashCode() & 3) * 0.03f;

            bool dogOk = DogUsable();
            float dDog = dogOk ? Flat(transform.position, _dog.position) : 999f;
            float act = Activity01();
            float sight = W.SightRange(Sp) * Mathf.Lerp(0.7f, 1f, act);
            float ang = Sp.sightAngle > 10f ? Sp.sightAngle : 80f;

            bool seeP = InSight(_player, sight, ang);
            bool seeD = dogOk && InSight(_dog, sight * 0.85f, ang);
            bool hearP = Hear(_player);
            bool hearD = dogOk && Hear(_dog);
            bool smellP = Smell(_player, Sp.scentRange * Mathf.Lerp(0.7f, 1.1f, act));
            bool smellD = dogOk && Smell(_dog, Sp.scentRange * 0.8f);
            bool senseP = seeP || hearP || smellP;
            bool senseD = seeD || hearD || smellD;

            if (Time.time - ASHWildlifeStimulus.LastGunshotTime < 0.6f)
            {
                float gDist = Flat(transform.position, ASHWildlifeStimulus.LastGunshotPos);
                float gRange = Sp.hearGunshot > 1f ? Sp.hearGunshot : ASHWildlifeStimulus.LastGunshotRange;
                if (gDist <= gRange)
                {
                    _lastSense = ASHWildlifeStimulus.LastGunshotPos;
                    _alert = 1f;
                    if (Sp.temperament == ASHWildlifeTemperament.Passive)
                    {
                        BeginFlee(ASHWildlifeStimulus.LastGunshotPos, false);
                        AlertHerd(ASHWildlifeStimulus.LastGunshotPos);
                    }
                    else if (!_aware)
                    {
                        _aware = true;
                        _focus = _player ? _player : _focus;
                        _mode = RushMode();
                    }
                }
            }

            if (senseP || senseD) _lastSenseAt = Time.time;
            else if (W != null && Time.time - _lastSenseAt > W.passiveCommitForgetSeconds) { _committed = false; _warns = 0; }
            if (senseP || senseD)
            {
                if (senseP) _lastSense = _player.position;
                else if (senseD) _lastSense = _dog.position;
                _alert = Mathf.Clamp01(_alert + (seeP || seeD ? 0.45f : 0.2f));
            }
            else if (_mode == ASHAnimalMode.Wander || _mode == ASHAnimalMode.Idle || _mode == ASHAnimalMode.Graze || _mode == ASHAnimalMode.Curious)
                _alert = Mathf.Max(0f, _alert - 0.12f);

            if (Sp.scaredByTorch && ASHTorchPresence.IsLitNear(transform.position, Sp.torchScareRadius, out var torchPos))
            {
                if (Sp.temperament == ASHWildlifeTemperament.Passive)
                {
                    BeginFlee(torchPos, true);
                    return;
                }
                // Hostiles (TLD-style): keep a gap from the flame rather than blindly fleeing the map.
                float dTorch = Flat(transform.position, torchPos);
                if (dTorch < Sp.torchScareRadius * 0.55f) { BeginRetreat(torchPos); return; }
            }

            AssignFocus(senseP, senseD, seeP, seeD);
            if (_mode == ASHAnimalMode.Attack || _mode == ASHAnimalMode.Charge || _mode == ASHAnimalMode.Bluff || _mode == ASHAnimalMode.Retreat || _mode == ASHAnimalMode.Flee || _mode == ASHAnimalMode.LookBack || _mode == ASHAnimalMode.Surprise)
                return;

            if (Sp.temperament == ASHWildlifeTemperament.Passive)
            {
                if (_alert >= Mathf.Lerp(0.7f, 0.98f, _p.bold) && (senseP || senseD))
                {
                    var from = senseP && _player ? _player.position : (senseD && _dog ? _dog.position : _lastSense);
                    if ((Sp.surprise == ASHSurpriseKind.FreezeThenBolt || Sp.surprise == ASHSurpriseKind.PlayDead) && TrySurprise())
                    { StartSurprise(Sp.surprise, from); return; }
                    BeginFlee(from, Random.value < Sp.shortFleeChance * Mathf.Lerp(0.6f, 1.5f, _p.bold));
                    AlertHerd(from);
                }
                else if (_alert >= 0.35f && _mode != ASHAnimalMode.Curious)
                {
                    _mode = ASHAnimalMode.Curious;
                    _idleUntil = Time.time + Sp.curiousSeconds;
                    _loco.Stop();
                    if (_lastSense.sqrMagnitude > 0.01f) _loco.FaceLoose(_lastSense, Time.deltaTime);
                    _vis.PlayIdle();
                }
                return;
            }

            // Hostiles / stalkers
            if (PackMateAggro()) { _aware = true; if (!_focus) _focus = _player; _mode = RushMode(); return; }
            if (_alert >= Mathf.Lerp(0.75f, 0.4f, _p.bold) * (1f - TA() * (W != null ? W.aggressiveEagerness : 0f) * 0.6f) && (senseP || senseD || _aware))
            {
                bool engagedBefore = _mode == ASHAnimalMode.Chase || _mode == ASHAnimalMode.Stalk || _mode == ASHAnimalMode.Retreat;
                _aware = true;
                if (!_focus) _focus = senseP ? _player : (senseD ? _dog : _player);
                if (!engagedBefore && Sp.surprise != ASHSurpriseKind.None && Sp.surprise != ASHSurpriseKind.FreezeThenBolt &&
                    Sp.surprise != ASHSurpriseKind.PlayDead && Sp.surprise != ASHSurpriseKind.Pounce && Focus() && TrySurprise())
                { StartSurprise(Sp.surprise, Focus().position); return; }
                // Passive-aggressive: warn / bluff and keep distance unless provoked or crowded
                // ...but once warned enough (or provoked / committed) it follows through instead of looping warnings
                if (PaGate() && !_committed && _warns >= Mathf.Max(1, W.passiveMaxWarnings)) _committed = true;
                if (PaGate() && !_committed && !Provoked(engagedBefore))
                {
                    if (Time.time >= _warnReadyAt) { _warnReadyAt = Time.time + W.passiveWarnCooldown; _warns++; StartBluff(); return; }
                    if (_mode != ASHAnimalMode.Curious)
                    {
                        _mode = ASHAnimalMode.Curious; _idleUntil = Time.time + Sp.curiousSeconds; _loco.Stop();
                        if (_vis.HasCall && Random.value < 0.3f) _vis.PlayCall(); else _vis.PlayIdle();
                    }
                    ReleaseToken();
                    return;
                }
                if (Sp.bluffCharge && !_hasBluffed && kind == ASHWildlifeKind.Bear) { StartBluff(); return; }
                bool stalk = (Sp.temperament == ASHWildlifeTemperament.Stalk || SneakGate()) && !AggGate() && !_playerHitMe && _focus == _player;
                if (stalk)
                {
                    if (_mode != ASHAnimalMode.Stalk) _stalkUntil = Time.time + Random.Range(4f, 10f) * Mathf.Lerp(1.5f, 0.6f, _p.aggr) * (1f + TS());
                    _mode = ASHAnimalMode.Stalk;
                }
                else
                {
                    _committed = true;
                    _mode = RushMode();
                }
            }
            else if (_alert >= 0.25f && !_aware)
            {
                _mode = ASHAnimalMode.Curious;
                _idleUntil = Time.time + Sp.curiousSeconds;
                _loco.Stop();
                if (_vis.HasCall && Random.value < 0.35f) _vis.PlayCall();
                else _vis.PlayIdle();
            }
        }

        void AssignFocus(bool senseP, bool senseD, bool seeP, bool seeD)
        {
            if (_playerHitMe && _player) { _focus = _player; return; }
            if (_dogHitMe && DogUsable()) { _focus = _dog; return; }
            // Prefer the player when both are sensed. Dog only if it was detected first (player not sensed).
            if (senseP) _focus = _player;
            else if (senseD) _focus = _dog;
        }

        void RetargetIfNeeded()
        {
            if (_focus && _focus == _dog && !DogUsable())
            {
                _dogHitMe = false;
                _focus = _player;
                if (_aware && IsThreat && _mode != ASHAnimalMode.Trapped) _mode = RushMode();
            }
        }

        bool DogUsable()
        {
            if (!ASHDogCompanion.IsValidTarget) { _dog = null; return false; }
            _dog = ASHDogCompanion.Active.transform;
            return true;
        }

        bool PackMateAggro()
        {
            if (packId == 0 || Sp == null || Sp.packSize <= 1) return false;
            for (int i = 0; i < Living.Count; i++)
            {
                var a = Living[i];
                if (!a || a == this || a.packId != packId || a._dead) continue;
                if (a.IsAggroOnPlayerOrDog) return true;
            }
            return false;
        }

        int PackSlotIndex()
        {
            if (packId == 0) return 0;
            int n = 0, idx = 0;
            for (int i = 0; i < Living.Count; i++)
            {
                var a = Living[i];
                if (!a || a.packId != packId || a._dead) continue;
                if (a == this) idx = n;
                n++;
            }
            return idx;
        }

        ASHAnimalMode RushMode()
        {
            if (UseStandOff) { if (_mode != ASHAnimalMode.Chase && _mode != ASHAnimalMode.Attack) _phase = Engage.Approach; return ASHAnimalMode.Chase; }
            if (Sp != null && Sp.chargeAndTurn) return ASHAnimalMode.Charge;
            return ASHAnimalMode.Chase;
        }

        bool UseStandOff => W != null && W.standOffEnabled && Sp != null && Sp.standOffMeters > 0.2f && Sp.temperament != ASHWildlifeTemperament.Passive;
        float StandOff => Mathf.Max(Sp.attackRange + 1f, Sp.standOffMeters * _p.standOff * Mathf.Sqrt(W.Scale(Sp, size)) * Mathf.Lerp(1f, W.aggressiveBufferMul, TA()));

        // ------------------------------------------------------------------ temperament blend (0 aggressive .. 0.5 passive-aggressive .. 1 sneaky)
        float _warnReadyAt, _restalkUntil;
        bool _restalk;
        bool TemperOn => W != null && W.temperamentEnabled && Sp != null && Sp.temperament != ASHWildlifeTemperament.Passive;
        /// <summary>Aggressive weight (1 at slider 0).</summary>
        float TA() => TemperOn ? Mathf.Clamp01(1f - 2f * _p.temper) : 0f;
        /// <summary>Passive-aggressive weight (1 at slider 0.5).</summary>
        float TP() => TemperOn ? Mathf.Clamp01(1f - Mathf.Abs(2f * _p.temper - 1f)) : 0f;
        /// <summary>Sneaky weight (1 at slider 1).</summary>
        float TS() => TemperOn ? Mathf.Clamp01(2f * _p.temper - 1f) : 0f;
        bool AggGate() => TA() >= 0.5f;
        bool PaGate() => TP() >= 0.5f;
        bool SneakGate() => TS() >= 0.3f;
        public float TemperamentValue => _p.temper;
        public string TemperamentLabel => !TemperOn ? "n/a" : (_p.temper < 0.25f ? "Aggressive" : _p.temper <= 0.65f ? "Passive-aggressive" : "Sneaky");

        /// <summary>Hit by the player or dog, or the target came inside the provoke distance (wider once already engaged).</summary>
        bool Provoked(bool engaged)
        {
            if (_playerHitMe || _dogHitMe) return true;
            var t = Focus();
            if (!t || W == null) return false;
            return Flat(transform.position, t.position) <= W.passiveProvokeMeters * (engaged ? 1.4f : 1f);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // preview the seeded temperament on placed instances (edit mode only; nothing else is touched)
            if (Application.isPlaying || overrideTemperament) return;
            try
            {
                if (!settings && !ASHWildlifeSettings.I) return;
                var keep = _home;
                _home = transform.position;
                RollPersonality();
                _home = keep;
            }
            catch { /* settings not loaded yet */ }
        }
#endif

        // ------------------------------------------------------------------ speeds / personality
        float Walk() => W.Walk(Sp, size) * _p.speed;
        float Trot() => W.Trot(Sp, size) * _p.speed;
        float Run() => W.Run(Sp, size) * _p.speed;

        ASHGroundLocomotion.Tune TuneNow()
        {
            var t = W.Loco(Sp, size);
            float m = _p.speed > 0.05f ? _p.speed : 1f;
            t.walkSpeed *= m; t.trotSpeed *= m; t.runSpeed *= m;
            t.speedParamScale /= m;
            return t;
        }

        void RollPersonality()
        {
            _pAt = Time.time;
            var neutral = new Personality { bold = 0.5f, aggr = 0.5f, speed = 1f, flee = 1f, wander = 1f, graze = 1f, cohesion = 1f, standOff = 1f, surprise = 1f };
            if (W == null || Sp == null || !W.personalityEnabled)
            {
                _p = neutral;
                _p.temper = overrideTemperament ? Mathf.Clamp01(temperament) : (Sp != null ? (Sp.temperamentMin + Sp.temperamentMax) * 0.5f : 0.5f);
                if (!overrideTemperament) temperament = _p.temper;
                return;
            }
            int seed = personalitySeed;
            if (seed == 0)
            {
                // stable across sessions: global spawn seed + species + where it was placed (decimetre grid)
                unchecked
                {
                    seed = W.spawnSeed * 73856093;
                    seed ^= ((int)kind + 1) * 19349663;
                    seed ^= Mathf.RoundToInt(_home.x * 10f) * 83492791;
                    seed ^= Mathf.RoundToInt(_home.z * 10f) * 2971215;
                    seed ^= Mathf.RoundToInt(_home.y * 10f) * 433494437;
                    if (seed == 0) seed = 1;
                }
            }
            var r = W.Ranges(Sp);
            uint st = (uint)seed;
            float N() { st ^= st << 13; st ^= st >> 17; st ^= st << 5; return (st & 0xFFFFFF) / 16777215f; }
            for (int i = 0; i < 3; i++) N();
            float R(float a, float b) => Mathf.Lerp(Mathf.Min(a, b), Mathf.Max(a, b), N());
            _p = new Personality
            {
                seed = seed,
                bold = R(r.boldnessMin, r.boldnessMax),
                aggr = R(r.aggressionMin, r.aggressionMax),
                speed = R(r.speedMin, r.speedMax),
                flee = R(r.fleeMin, r.fleeMax),
                wander = R(r.wanderMin, r.wanderMax),
                graze = R(r.grazeMin, r.grazeMax),
                cohesion = R(r.cohesionMin, r.cohesionMax),
                standOff = R(r.standOffMin, r.standOffMax),
                circleLeft = N() < 0.5f,
            };
            // rolled last so the earlier traits stay identical to before surprises existed
            _p.surprise = R(W.surpriseTendencyMin, W.surpriseTendencyMax);
            float rolledTemper = R(Sp.temperamentMin, Sp.temperamentMax);
            _p.temper = overrideTemperament ? Mathf.Clamp01(temperament) : rolledTemper;
            if (!overrideTemperament) temperament = _p.temper;
        }

        // ------------------------------------------------------------------ attack turns (one or few attackers per target)
        bool TryToken(Transform t)
        {
            if (!t || W == null) return true;
            if (_hasToken && _tokenTarget == t) return true;
            if (Time.time < _tokenBlockedUntil) return false;
            ReleaseToken();
            if (!Tokens.TryGetValue(t, out var list)) { list = new List<ASHAnimal>(); Tokens[t] = list; }
            list.RemoveAll(a => !a || a._dead || !a._hasToken || a._tokenTarget != t);
            if (list.Count >= Mathf.Max(1, W.maxAttackersPerTarget)) return false;
            list.Add(this);
            _hasToken = true; _tokenTarget = t; _tokenAt = Time.time;
            return true;
        }

        void ReleaseToken()
        {
            if (!_hasToken) return;
            if (_tokenTarget && Tokens.TryGetValue(_tokenTarget, out var list)) list.Remove(this);
            _hasToken = false; _tokenTarget = null;
            if (W != null) _tokenBlockedUntil = Time.time + W.attackTurnGap;
        }

        void TokenHousekeeping()
        {
            if (!_hasToken) return;
            bool engaged = _mode == ASHAnimalMode.Attack || _mode == ASHAnimalMode.Charge || (_mode == ASHAnimalMode.Chase && _phase == Engage.Lunge);
            if (!engaged || _dead || (W != null && Time.time - _tokenAt > W.attackTurnTimeout)) ReleaseToken();
        }

        Vector3 Ring(Transform t, float radius)
        {
            var from = transform.position - t.position; from.y = 0f;
            if (from.sqrMagnitude < 0.04f) from = -t.forward;
            return t.position + from.normalized * radius;
        }

        Transform Focus()
        {
            if (_focus == _dog && !DogUsable()) _focus = _player;
            if (_focus) return _focus;
            if (_player) return _player;
            return DogUsable() ? _dog : null;
        }

        bool InSight(Transform t, float range, float angle)
        {
            if (!t || Flat(transform.position, t.position) > range) return false;
            var to = t.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return true;
            if (Vector3.Angle(transform.forward, to) > angle * 0.5f) return false;
            return HasLos(t);
        }

        bool Hear(Transform t)
        {
            if (!t) return false;
            float range = Sp.hearWalk;
            var b = ASHCombatBridge.Player;
            if (t == _player && b)
            {
                if (b.Sprinting) range = Sp.hearSprint;
                else if (b.Crouching) range = b.InputMag > 0.08f ? Sp.hearCrouch : Sp.hearCrouch * 0.4f;
                else if (b.InputMag < 0.08f) range = Sp.hearCrouch * 0.45f;
            }
            range *= Activity01();
            return Flat(transform.position, t.position) <= range;
        }

        bool Smell(Transform t, float scentRange)
        {
            if (!t || scentRange <= 0.2f) return false;
            float d = Flat(transform.position, t.position);
            if (!W.windScent) return d <= scentRange * 0.35f;
            var wind = ASHWildlifeStimulus.WindDir;
            var toAnimal = transform.position - t.position; toAnimal.y = 0f;
            if (toAnimal.sqrMagnitude < 0.01f) return true;
            float downwind = Vector3.Dot(wind.normalized, toAnimal.normalized); // 1 = animal is downwind of the target
            float range = scentRange * Mathf.Lerp(0.18f, 1.2f, (downwind + 1f) * 0.5f);
            return d <= range;
        }

        float Activity01()
        {
            float h = GameHour();
            bool day = h >= 6f && h < 20f;
            float a = day ? Sp.dayActivity : Sp.nightActivity;
            if (h >= 5f && h < 8f || h >= 17f && h < 20f) a = Mathf.Max(a, (Sp.dayActivity + Sp.nightActivity) * 0.6f);
            return Mathf.Clamp(a, 0.15f, 2f);
        }

        static float GameHour()
        {
            if (Time.unscaledTime < _hourCachedAt) return _hourCached;
            _hourCachedAt = Time.unscaledTime + 2f;
            _hourCached = 14f;
            try
            {
                var ty = System.Type.GetType("Enviro.EnviroManager, Assembly-CSharp");
                if (ty == null) return _hourCached;
                var inst = ty.GetProperty("instance")?.GetValue(null) ?? ty.GetField("instance")?.GetValue(null);
                if (inst == null) return _hourCached;
                var time = inst.GetType().GetProperty("Time")?.GetValue(inst);
                if (time == null) return _hourCached;
                foreach (var n in new[] { "GetTimeOfDay", "hours", "Hours", "hour", "Hour" })
                {
                    var m = time.GetType().GetMethod(n, System.Type.EmptyTypes);
                    if (m != null && (m.ReturnType == typeof(float) || m.ReturnType == typeof(double)))
                    { _hourCached = System.Convert.ToSingle(m.Invoke(time, null)); break; }
                    var p = time.GetType().GetProperty(n);
                    if (p != null && (p.PropertyType == typeof(float) || p.PropertyType == typeof(double)))
                    { _hourCached = System.Convert.ToSingle(p.GetValue(time)); break; }
                }
            }
            catch { /* keep fallback hour */ }
            return _hourCached;
        }

        // ------------------------------------------------------------------ movement states
        void WanderTick(bool cheap)
        {
            if (Time.time < _idleUntil)
            {
                _loco.Stop();
                return;
            }
            if (!cheap && _vis.HasEat && _mode != ASHAnimalMode.Graze && Random.value < Sp.grazeChance * _p.graze * 0.02f * Activity01())
            {
                _mode = ASHAnimalMode.Graze;
                _grazeUntil = Time.time + Random.Range(Sp.grazeMin, Sp.grazeMax);
                _loco.Stop();
                _vis.PlayEat();
                return;
            }
            if (!cheap && !_aware && Sp.surprise == ASHSurpriseKind.Pounce && Random.value < 0.01f && TrySurprise())
            {
                var fwd = transform.forward; fwd.y = 0f;
                StartSurprise(ASHSurpriseKind.Pounce, transform.position + fwd.normalized * Mathf.Max(1f, Sp.surpriseMeters));
                return;
            }
            if (!cheap && _vis.HasSleep && Activity01() < 0.4f && Random.value < 0.01f)
            {
                _idleUntil = Time.time + Random.Range(4f, 10f);
                _loco.Stop();
                _vis.PlaySleep();
                return;
            }
            if (Time.time >= _wanderT || _loco.StuckTime > 1.2f)
            {
                _wanderT = Time.time + Random.Range(3f, 8f);
                var o = Random.insideUnitSphere; o.y = 0f;
                float r = Mathf.Max(3f, Sp.wanderRadius * _p.wander);
                _wander = _home + o.normalized * Random.Range(r * 0.25f, r);
                if (Random.value < 0.22f) { _idleUntil = Time.time + Random.Range(1.5f, 4f); _mode = ASHAnimalMode.Idle; _vis.PlayIdle(); return; }
            }
            _mode = ASHAnimalMode.Wander;
            _loco.MoveTo(_wander, Fatigued(Walk()), 1.1f);
        }

        void GrazeTick()
        {
            _loco.Stop();
            if (Time.time >= _grazeUntil) { _mode = ASHAnimalMode.Wander; return; }
            if (!_vis.InState(_vis.eatState)) _vis.PlayEat();
        }

        void CuriousTick(float dt)
        {
            _loco.Stop();
            if (_lastSense.sqrMagnitude > 0.01f) _loco.FaceLoose(_lastSense, dt);
            if (Time.time >= _idleUntil)
            {
                if (_alert >= 0.85f && Sp.temperament == ASHWildlifeTemperament.Passive) BeginFlee(_lastSense, true);
                else if (_alert >= 0.55f && Sp.temperament != ASHWildlifeTemperament.Passive && !(PaGate() && !Provoked(false))) { _aware = true; _mode = RushMode(); }
                else { _mode = ASHAnimalMode.Wander; _alert *= 0.5f; }
            }
        }

        void LookBackTick(float dt)
        {
            _loco.Stop();
            if (_lastSense.sqrMagnitude > 0.01f) _loco.Face(_lastSense, dt);
            _vis.PlayIdle();
            if (Time.time >= _lookUntil) _mode = ASHAnimalMode.Wander;
        }

        void BeginFlee(Vector3 from, bool shortFlee)
        {
            InterruptIdle();
            float dist = (shortFlee ? Sp.shortFleeMeters : Sp.longFleeMeters) * _p.flee;
            _flee = FleePoint(from, dist);
            _scareUntil = Time.time + (shortFlee ? Random.Range(1.2f, 2.6f) : Random.Range(3f, Mathf.Max(4f, Sp.torchScareSeconds)));
            _mode = ASHAnimalMode.Flee;
            _lastSense = from;
        }

        void BeginRetreat(Vector3 from)
        {
            InterruptIdle();
            _flee = FleePoint(from, Sp.shortFleeMeters * 1.4f * _p.flee);
            _retreatUntil = Time.time + Sp.retreatSeconds;
            _scareUntil = _retreatUntil;
            _mode = ASHAnimalMode.Retreat;
        }

        void AlertHerd(Vector3 from)
        {
            if (Sp == null || !Sp.herdFlee || W == null) return;
            float r = W.herdRadius * _p.cohesion;
            for (int i = 0; i < Living.Count; i++)
            {
                var a = Living[i];
                if (!a || a == this || a._dead || a.Sp == null || !a.Sp.herdFlee) continue;
                bool kin = a.kind == kind || (kind == ASHWildlifeKind.Deer && a.kind == ASHWildlifeKind.Fawn) || (kind == ASHWildlifeKind.Fawn && a.kind == ASHWildlifeKind.Deer);
                if (!kin) continue;
                if (Flat(transform.position, a.transform.position) > r) continue;
                if (a._mode == ASHAnimalMode.Flee || a._mode == ASHAnimalMode.LookBack || a._mode == ASHAnimalMode.Trapped) continue;
                a.BeginFlee(from, Random.value < a.Sp.shortFleeChance);
            }
        }

        Vector3 FleePoint(Vector3 from, float dist)
        {
            var away = transform.position - from; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            away = (away.normalized + transform.right * Random.Range(-0.35f, 0.35f)).normalized;
            return transform.position + away * dist;
        }

        void FleeTick()
        {
            bool done = _loco.MoveTo(_flee, Fatigued(Run()) * Burst(), 1.4f) || Time.time >= _scareUntil;
            if (!done) return;
            _scareUntil = 0f;
            if (_mode == ASHAnimalMode.Retreat && Time.time >= _retreatUntil && IsThreat)
            {
                _mode = RushMode();
                return;
            }
            if (Sp.temperament == ASHWildlifeTemperament.Passive && Random.value < Sp.lookBackChance)
            {
                _mode = ASHAnimalMode.LookBack;
                _lookUntil = Time.time + Sp.lookBackSeconds;
                _loco.Stop();
                _vis.PlayIdle();
                return;
            }
            _mode = ASHAnimalMode.Idle;
            _idleUntil = Time.time + Random.Range(0.8f, 2.2f);
        }

        void StalkTick()
        {
            var t = Focus() ?? _player;
            if (!t) { WanderTick(false); return; }
            if (_playerHitMe && Time.time >= _restalkUntil) { _mode = RushMode(); ChaseTick(); return; }
            float d = Flat(transform.position, t.position);
            if (d <= Sp.attackRange * 1.1f)
            {
                if (UseStandOff) { _phase = Engage.Hold; _holdUntil = 0f; _mode = ASHAnimalMode.Chase; return; }
                _mode = ASHAnimalMode.Attack; AttackTick(); return;
            }
            // Cougar: stay behind the target, close when line of sight breaks (ambush).
            Vector3 dest;
            if (Sp.ambush || SneakGate())
            {
                var behind = t.position - t.forward * Mathf.Clamp(d * 0.35f, 4f, 12f);
                dest = behind;
                bool los = HasLos(t);
                float spd = los ? Walk() : Run();
                if (los && d < Sp.detectRange * 0.7f && Vector3.Dot(t.forward, (transform.position - t.position).normalized) > 0.35f)
                    dest = t.position - t.forward * 10f; // they looked this way, slide back around
                _loco.MoveTo(dest, Fatigued(spd), 1.6f);
                if (d < Sp.detectRange * 0.45f && !los) { _mode = ASHAnimalMode.Chase; }
            }
            else
            {
                float hold = Mathf.Clamp(Sp.detectRange * 0.8f, 8f, Sp.stalkRange * 0.7f);
                var to = transform.position - t.position; to.y = 0f;
                dest = t.position + (to.sqrMagnitude > 0.2f ? to.normalized : t.forward) * hold;
                _loco.MoveTo(dest, Walk(), 2f);
                if (Time.time >= _stalkUntil && _stalkUntil > 0f) { _aware = true; _mode = ASHAnimalMode.Chase; }
            }
        }

        void StartBluff()
        {
            _hasBluffed = true;
            _mode = ASHAnimalMode.Bluff;
            _bluffUntil = Time.time + Random.Range(1.6f, 2.6f);
            var t = Focus();
            if (t) _flee = Vector3.Lerp(transform.position, t.position, 0.7f);
            _vis.PlayCall();
        }

        void BluffTick(float dt)
        {
            var t = Focus();
            if (t) _loco.Face(t.position, dt);
            if (t && Time.time < _bluffUntil - 0.6f)
                _loco.MoveTo(_flee, Run(), 2.2f);
            else
                _loco.Stop();
            if (Time.time < _bluffUntil) return;
            // Territorial warning, then either a real rush or they break off if the player backs away.
            bool rush = false;
            if (t)
            {
                if (PaGate()) rush = Provoked(false) || _warns >= Mathf.Max(1, W.passiveMaxWarnings); // passive-aggressive: follows through when provoked or warned enough
                else rush = Flat(transform.position, t.position) < Sp.detectRange * 0.9f && Random.value < Mathf.Lerp(0.4f, 0.92f, _p.aggr) * (1f - 0.7f * TP());
            }
            if (rush)
            { _committed = true; _mode = RushMode(); }
            else { _aware = false; _alert = 0.3f; _mode = ASHAnimalMode.Wander; }
        }

        void ChargeTick(float dt)
        {
            var t = Focus();
            if (!t) { _mode = ASHAnimalMode.Wander; return; }
            _loco.Face(t.position, dt);
            float d = Flat(transform.position, t.position);
            if (d <= Sp.attackRange * 1.05f) { _mode = ASHAnimalMode.Attack; AttackTick(); return; }
            var to = t.position - transform.position; to.y = 0f;
            bool passed = to.sqrMagnitude > 0.2f && Vector3.Dot(transform.forward, to.normalized) < -0.15f && d < 4f;
            if (passed || _loco.StuckTime > 0.8f)
            {
                // Missed or struck a tree: turn and charge again.
                _chargeUntil = Time.time + 0.6f;
                _loco.Face(t.position, dt * 3f);
                if (Time.time >= _chargeUntil - 0.01f) { /* keep facing */ }
                return;
            }
            _loco.MoveTo(t.position, Fatigued(Run() * W.chargeSpeedMul), Sp.attackRange * 0.6f);
        }

        void ChaseTick()
        {
            var t = Focus();
            if (!t) { WanderTick(false); return; }
            if (UseStandOff) { StandOffTick(t); return; }
            float d = Flat(transform.position, t.position);
            if (d <= Sp.attackRange) { _mode = ASHAnimalMode.Attack; AttackTick(); return; }
            Vector3 dest = t.position;
            if (Sp.packFlank && packId != 0)
            {
                int slot = _packSlot;
                float ang = slot * 70f - 35f;
                float rad = (W.packFlankRadius > 1f ? W.packFlankRadius : 7f) * _p.cohesion;
                dest = t.position + Quaternion.Euler(0f, ang, 0f) * (t.forward * -1f) * rad;
                // Lowest slot tests a bite when close; others keep circling.
                if (slot == 0 && d < Sp.attackRange + 2.5f) dest = t.position;
            }
            _loco.MoveTo(dest, Fatigued(Run()), Sp.attackRange * 0.7f);
        }

        /// <summary>Approach to a buffer ring, hold/circle there, lunge in when it's this animal's turn, attack, back off, repeat.</summary>
        void StandOffTick(Transform t)
        {
            float d = Flat(transform.position, t.position);
            float ring = StandOff;
            float dt = Time.deltaTime;
            switch (_phase)
            {
                case Engage.Approach:
                {
                    Vector3 dest = Ring(t, ring);
                    if (Sp.packFlank && packId != 0)
                    {
                        float ang = _packSlot * 70f - 35f;
                        dest = t.position + Quaternion.Euler(0f, ang, 0f) * (t.forward * -1f) * ring;
                    }
                    if (d <= ring + 0.8f) { EnterHold(); return; }
                    _loco.MoveTo(dest, Fatigued(d > ring + 6f ? Run() : Trot()), 0.6f);
                    return;
                }
                case Engage.Hold:
                {
                    // target walked into us: take the turn if free, otherwise give ground
                    if (d <= Sp.attackRange * 1.05f)
                    {
                        if (TryToken(t)) { _mode = ASHAnimalMode.Attack; AttackTick(); return; }
                        StartCircle(t); return;
                    }
                    if (d > ring + 3.5f) { _phase = Engage.Approach; return; }
                    var from = transform.position - t.position; from.y = 0f;
                    if (from.sqrMagnitude < 0.04f) from = -t.forward;
                    // deadband: only re-space when clearly off the ring, otherwise just drift around it (no forward/back jitter)
                    float rr = Mathf.Clamp(d, ring - W.holdDeadband, ring + W.holdDeadband);
                    var dest = t.position + Quaternion.Euler(0f, (_p.circleLeft ? -1f : 1f) * Mathf.Max(8f, W.circleDegPerSecond * 0.6f), 0f) * from.normalized * rr;
                    if (W.circleDegPerSecond > 0.5f && Flat(transform.position, dest) > 0.5f) _loco.MoveTo(dest, Walk(), 0.35f);
                    else { _loco.Stop(); _loco.Face(t.position, dt); }
                    if (Time.time >= _holdUntil && TryToken(t)) { _phase = Engage.Lunge; _phaseUntil = Time.time + 4f + Mathf.Max(0f, d - Sp.lungeDistance) / Mathf.Max(0.5f, Walk()); if (Sp.chargeAndTurn || Random.value < 0.35f) _vis.PlayCall(); }
                    return;
                }
                case Engage.Lunge:
                {
                    if (!_hasToken && !TryToken(t)) { EnterHold(); return; }
                    if (d <= Sp.attackRange) { _mode = ASHAnimalMode.Attack; AttackTick(); return; }
                    if (Time.time > _phaseUntil || _loco.StuckTime > 1f) { ReleaseToken(); StartCircle(t); return; }
                    // walk in to lunge distance, then a short lunge at the species' lunge speed
                    float lungeFrom = Mathf.Max(Sp.attackRange + 0.5f, Sp.lungeDistance);
                    float spd = d > lungeFrom ? Walk() * 1.1f : Run() * Sp.lungeSpeed * (Sp.chargeAndTurn ? W.chargeSpeedMul : W.lungeSpeedMul);
                    _loco.MoveTo(t.position, d > lungeFrom ? spd : Fatigued(spd), Sp.attackRange * 0.7f);
                    return;
                }
                case Engage.Circle:
                {
                    // strafe arc around the target at the buffer, then hold briefly and lunge again
                    if (d <= Sp.attackRange * 1.05f && Time.time - _phaseAt > 0.8f && TryToken(t)) { _mode = ASHAnimalMode.Attack; AttackTick(); return; }
                    if (d > _circleR + 6f) { _phase = Engage.Approach; return; }
                    var from = transform.position - t.position; from.y = 0f;
                    if (from.sqrMagnitude < 0.04f) from = -t.forward;
                    float lead = Mathf.Lerp(80f, 35f, Mathf.Clamp01(d / Mathf.Max(0.5f, _circleR)));
                    var dest = t.position + Quaternion.Euler(0f, _circleSign * lead, 0f) * from.normalized * _circleR;
                    _loco.MoveTo(dest, Fatigued(Trot() * Sp.circleSpeedMul), 0.3f);
                    if (_loco.StuckTime > 0.9f) { _circleSign = -_circleSign; _loco.MarkTeleported(); }
                    if (Time.time > _phaseUntil) EnterHold();
                    return;
                }
                default: // BackOff
                {
                    bool arrived = _loco.MoveTo(_backOff, Trot(), 0.8f);
                    if (arrived || Time.time > _phaseUntil || _loco.StuckTime > 0.8f)
                    {
                        // Sneaky: melt away and stalk in again instead of circling in the open
                        if (_restalk) { _restalk = false; ReleaseToken(); _restalkUntil = Time.time + 8f; _stalkUntil = Time.time + Random.Range(3f, 7f); _mode = ASHAnimalMode.Stalk; return; }
                        EnterHold();
                    }
                    return;
                }
            }
        }

        void EnterHold()
        {
            _phase = Engage.Hold;
            float a = Mathf.Lerp(1.5f, 0.6f, _p.aggr);
            if (W != null) a *= Mathf.Lerp(1f, W.aggressiveHoldMul, TA()) * Mathf.Lerp(1f, W.passiveHoldMul, TP());
            _holdUntil = Time.time + Random.Range(Sp.standOffHoldMin, Mathf.Max(Sp.standOffHoldMin, Sp.standOffHoldMax)) * a;
        }

        void StartBackOff(Transform t, bool afterStrike = false)
        {
            _phase = Engage.BackOff;
            _mode = ASHAnimalMode.Chase;
            float ring = StandOff * (afterStrike && W != null ? Mathf.Lerp(1f, W.sneakyWithdrawMul, TS()) : 1f);
            _restalk = afterStrike && SneakGate() && W != null && Random.value < W.sneakyRestalkChance * TS() * 2f;
            _backOff = Ring(t, ring);
            _phaseUntil = Time.time + (W != null ? W.backOffMaxSeconds : 2.4f);
            if (!_restalk && Sp != null && Sp.circleAfterAttack) StartCircle(t, ring);
        }

        /// <summary>After a strike (or crowded without the turn): circle the target in a random direction instead of snapping back.</summary>
        void StartCircle(Transform t, float ring = -1f)
        {
            if (Sp == null || !Sp.circleAfterAttack) { if (_phase != Engage.BackOff) StartBackOff(t); return; }
            _mode = ASHAnimalMode.Chase;
            _phase = Engage.Circle;
            _phaseAt = Time.time;
            _circleSign = Random.value < 0.5f ? -1f : 1f;
            _circleR = (ring > 0f ? ring : StandOff) * Sp.circleRadiusMul;
            _phaseUntil = Time.time + Random.Range(Sp.circleSecondsMin, Mathf.Max(Sp.circleSecondsMin, Sp.circleSecondsMax));
        }

        void AttackTick()
        {
            var t = Focus();
            if (!t) { _mode = ASHAnimalMode.Wander; return; }
            _loco.Face(t.position, Time.deltaTime);
            float d = Flat(transform.position, t.position);
            if (_attackUntil > Time.time)
            {
                _loco.Stop();
                if (!_attackHit && (Time.time >= _attackHitAt || _vis.NormalizedTime >= Sp.attackHitNormalized))
                {
                    _attackHit = true;
                    TryDealAttack(t);
                }
                return;
            }
            if (UseStandOff)
            {
                // the strike has finished: give up the turn and retreat to the buffer
                if (_attackUntil > 0f && Time.time >= _attackUntil) { _attackUntil = 0f; ReleaseToken(); StartBackOff(t, true); return; }
                if (!TryToken(t)) { StartCircle(t); return; }
            }
            if (d > Sp.attackRange * 1.3f) { _mode = RushMode(); return; }
            _loco.Stop();
            if (Time.time < _nextAttack) return;
            _nextAttack = Time.time + Sp.attackCooldown * Mathf.Lerp(1.35f, 0.75f, _p.aggr) * (W != null ? Mathf.Lerp(1f, W.aggressiveCooldownMul, TA()) : 1f);
            _vis.PlayAttack();
            _attackHit = false;
            float wind = Mathf.Clamp(Sp.attackHitNormalized, 0.18f, 0.8f);
            _attackHitAt = Time.time + wind * 0.9f;
            _attackUntil = Time.time + 1.15f;
        }

        void TryDealAttack(Transform t)
        {
            if (!t) return;
            if (Flat(transform.position, t.position) > Sp.attackRange * 1.45f) return;
            var to = t.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.04f && Vector3.Angle(transform.forward, to) > 75f) return;
            float dmg = W.Damage(Sp, size);
            if (dmg <= 0f) return;
            HurtTarget(t, dmg);
            // Wolf testing bite: after a snap, step back to the flank.
            if (Sp.packFlank && packId != 0 && _packSlot == 0)
                _mode = ASHAnimalMode.Chase;
        }

        void HurtTarget(Transform t, float dmg)
        {
            var dog = t.GetComponentInParent<ASHDogCompanion>();
            if (dog)
            {
                dog.TakeDamage(new vDamage(Mathf.RoundToInt(dmg)) { sender = transform, hitPosition = t.position + Vector3.up * 0.4f, damageType = "claw" });
                return;
            }
            var bridge = ASHCombatBridge.Player;
            if (bridge && _player && (t == _player || t.IsChildOf(_player) || _player.IsChildOf(t) || t == bridge.transform || t.IsChildOf(bridge.transform)))
            {
                bridge.HurtFromWorld(dmg, transform, t.position + Vector3.up * 1f);
                return;
            }
            var hc = t.GetComponentInParent<vIHealthController>();
            if (hc != null) hc.TakeDamage(new vDamage(Mathf.RoundToInt(dmg)) { sender = transform, hitPosition = t.position, damageType = "claw" });
        }

        float Fatigued(float speed)
        {
            bool running = speed >= Trot() + 0.2f;
            if (running) { _sprintT += Time.deltaTime; _restT = 0f; }
            else { _restT += Time.deltaTime; if (_restT > 2f) _sprintT = Mathf.Max(0f, _sprintT - Time.deltaTime * 1.4f); }
            if (_sprintT > Sp.fatigueRunSeconds) return Mathf.Min(speed, Trot());
            return speed;
        }

        void InterruptIdle()
        {
            _idleUntil = 0f;
            _grazeUntil = 0f;
            if (_mode == ASHAnimalMode.Graze || _mode == ASHAnimalMode.Idle || _mode == ASHAnimalMode.Curious) { /* overwritten by caller */ }
        }

        void TrappedTick(float dt)
        {
            if (_trap.trap == null) { _mode = ASHAnimalMode.Wander; return; }
            _loco.Stop();
            if (kind == ASHWildlifeKind.Wolf && Time.time >= _breakFreeAt && _breakFreeAt > 0f)
            {
                ExitTrap();
                if (_player) { _aware = true; _focus = _player; _mode = ASHAnimalMode.Chase; }
                return;
            }
            if (Sp.temperament == ASHWildlifeTemperament.Passive) return;
            var t = _player;
            if (!t) return;
            _loco.Face(t.position, dt);
            float d = Flat(transform.position, t.position);
            if (d <= Sp.attackRange && Time.time >= _nextAttack)
            {
                _nextAttack = Time.time + Sp.attackCooldown;
                _vis.PlayAttack();
                _attackHit = false;
                _attackHitAt = Time.time + Mathf.Clamp(Sp.attackHitNormalized, 0.18f, 0.8f) * 0.9f;
                _attackUntil = Time.time + 1.15f;
            }
            if (_attackUntil > Time.time && !_attackHit && Time.time >= _attackHitAt)
            {
                _attackHit = true;
                TryDealAttack(t);
            }
        }

        public bool CanEnterTrap(ASHTrapSize sizeWanted) =>
            !_dead && !_harvested && _mode != ASHAnimalMode.Trapped && Sp != null && Sp.trapSize == sizeWanted && sizeWanted != ASHTrapSize.None;

        public void EnterTrap(GameObject trap, ASHTrapSize trapSize)
        {
            if (!CanEnterTrap(trapSize)) return;
            _trap = new ASHSnareHook { trap = trap, size = trapSize };
            _mode = ASHAnimalMode.Trapped;
            _loco.Stop();
            if (kind == ASHWildlifeKind.Wolf) _breakFreeAt = Time.time + Mathf.Max(5f, W.wolfBreakFreeSeconds);
            else Die();
        }

        public void ExitTrap()
        {
            _trap = default;
            _breakFreeAt = 0f;
            if (!_dead) _mode = ASHAnimalMode.Wander;
        }

        bool HasLos(Transform t)
        {
            if (!t) return false;
            var origin = transform.position + Vector3.up * 1.0f;
            var dest = t.position + Vector3.up * 1.1f;
            var dir = dest - origin;
            float dist = dir.magnitude;
            if (dist < 0.2f) return true;
            if (Physics.Raycast(origin, dir / dist, out var hit, dist, W.environmentLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(t) || hit.transform == t) return true;
                if (hit.transform.IsChildOf(transform)) return true;
                return false;
            }
            return true;
        }

        void OnTorchScare(Vector3 pos, float radius, float duration)
        {
            if (_dead || Sp == null || !Sp.scaredByTorch) return;
            if (Flat(transform.position, pos) <= radius)
            {
                if (Sp.temperament == ASHWildlifeTemperament.Passive) BeginFlee(pos, false);
                else BeginRetreat(pos);
                if (duration > 0f) _scareUntil = Time.time + duration;
            }
        }

        void FindPlayer()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) _player = p.transform;
            if (!_player && ASHCombatBridge.Player) _player = ASHCombatBridge.Player.transform;
        }

        void FindDog()
        {
            if (ASHDogCompanion.IsValidTarget) _dog = ASHDogCompanion.Active.transform;
            else _dog = null;
        }

        // ------------------------------------------------------------------ surprise behaviours (seeded chance + cooldown per animal)
        ASHSurpriseKind _sKind;
        int _sStep;
        float _sUntil, _sStart, _surpriseReadyAt, _boostUntil, _boostMul = 1f;
        Vector3 _sPoint, _sFrom;
        bool _sHoldPose, _sArc;
        Transform _sArcT;
        Vector3 _sArcBase;

        float Burst() => Time.time < _boostUntil ? _boostMul : 1f;

        bool TrySurprise()
        {
            if (W == null || Sp == null || !W.surprisesEnabled || Sp.surprise == ASHSurpriseKind.None) return false;
            if (Time.time < _surpriseReadyAt) return false;
            float chance = Sp.surpriseChance * W.surpriseChanceMul * (W.personalityEnabled ? _p.surprise : 1f);
            if (TemperOn)
            {
                float bias = W.temperamentSurpriseBias;
                if (Sp.surprise == ASHSurpriseKind.StalkFromCover || Sp.surprise == ASHSurpriseKind.CircleBehind) chance *= Mathf.Max(0f, 1f + bias * (TS() - TA() * 0.5f));
                else if (Sp.surprise == ASHSurpriseKind.FalseCharge || Sp.surprise == ASHSurpriseKind.StandAndHuff) chance *= Mathf.Max(0f, 1f + bias * (TP() - TS() * 0.5f));
            }
            if (Random.value >= Mathf.Clamp01(chance))
            {
                _surpriseReadyAt = Time.time + 4f; // don't re-roll every sense tick
                return false;
            }
            _surpriseReadyAt = Time.time + Random.Range(Mathf.Min(Sp.surpriseCooldownMin, Sp.surpriseCooldownMax), Mathf.Max(Sp.surpriseCooldownMin, Sp.surpriseCooldownMax));
            return true;
        }

        void StartSurprise(ASHSurpriseKind k, Vector3 point)
        {
            InterruptIdle();
            ReleaseToken();
            _sKind = k;
            _sStep = 0;
            _sStart = Time.time;
            _sPoint = point;
            _sFrom = point;
            _sHoldPose = false;
            _mode = ASHAnimalMode.Surprise;
            float secs = Sp.surpriseSeconds;
            switch (k)
            {
                case ASHSurpriseKind.FreezeThenBolt:
                    _sHoldPose = true; _loco.Stop(); _vis.PlayIdle();
                    _sUntil = Time.time + secs * Mathf.Lerp(1.3f, 0.6f, _p.bold) * Random.Range(0.8f, 1.2f);
                    break;
                case ASHSurpriseKind.PlayDead:
                    _sHoldPose = true; _loco.Stop();
                    if (!PlayState(Sp.surpriseState)) _vis.PlayFakeDeath();
                    _sUntil = Time.time + secs * Random.Range(0.8f, 1.3f);
                    break;
                case ASHSurpriseKind.StandAndHuff:
                    _sHoldPose = true; _loco.Stop();
                    if (!PlayState(Sp.surpriseState)) { if (_vis.HasCall) _vis.PlayCall(); else _vis.PlayIdle(); }
                    _sUntil = Time.time + secs * Random.Range(0.85f, 1.25f);
                    break;
                case ASHSurpriseKind.Pounce:
                    _sHoldPose = true; _loco.Stop(); _vis.PlayIdle();
                    _sUntil = Time.time + Mathf.Max(0.3f, secs);
                    break;
                case ASHSurpriseKind.FalseCharge:
                    if (_vis.HasCall) _vis.PlayCall();
                    _sUntil = Time.time + 6f;
                    break;
                case ASHSurpriseKind.CircleBehind:
                    _sUntil = Time.time + 9f;
                    break;
                case ASHSurpriseKind.StalkFromCover:
                    if (!FindCover(point, out _sPoint)) { _mode = Sp.temperament == ASHWildlifeTemperament.Stalk ? ASHAnimalMode.Stalk : RushMode(); return; }
                    _sUntil = Time.time + 14f;
                    break;
            }
        }

        void SurpriseTick(float dt)
        {
            var t = Focus();
            float secs = Sp.surpriseSeconds;
            switch (_sKind)
            {
                case ASHSurpriseKind.FreezeThenBolt:
                {
                    _loco.Stop();
                    if (_player) _loco.FaceLoose(_player.position, dt);
                    if (Time.time < _sUntil) return;
                    EndSurprise();
                    _boostMul = Sp.surpriseBurstMul; _boostUntil = Time.time + 2.5f;
                    BeginFlee(_player ? _player.position : _sFrom, false);
                    AlertHerd(_player ? _player.position : _sFrom);
                    return;
                }
                case ASHSurpriseKind.PlayDead:
                {
                    _loco.Stop();
                    // keep still while the threat is close (up to 3x the time), then get up and run
                    bool close = _player && Flat(transform.position, _player.position) < Sp.surpriseMeters;
                    if (Time.time < _sUntil || (close && Time.time - _sStart < secs * 3f)) return;
                    EndSurprise();
                    _vis.PlayIdle();
                    _boostMul = Sp.surpriseBurstMul; _boostUntil = Time.time + 3f;
                    BeginFlee(_player ? _player.position : _sFrom, false);
                    return;
                }
                case ASHSurpriseKind.StandAndHuff:
                {
                    _loco.Stop();
                    if (t) _loco.Face(t.position, dt);
                    if (Time.time < _sUntil) return;
                    EndSurprise();
                    _vis.PlayIdle();
                    // huffed a warning: rush if they stayed close, otherwise lose interest
                    if (t && Flat(transform.position, t.position) < Sp.detectRange * 0.6f && Random.value < Mathf.Lerp(0.35f, 0.9f, _p.aggr))
                    { _boostMul = Sp.surpriseBurstMul; _boostUntil = Time.time + 2f; _mode = RushMode(); }
                    else { _aware = false; _alert = 0.3f; _mode = ASHAnimalMode.Wander; _wanderT = 0f; }
                    return;
                }
                case ASHSurpriseKind.Pounce:
                {
                    if (_sStep == 0)
                    {
                        _loco.Stop();
                        _loco.Face(_sPoint, dt);
                        if (Time.time < _sUntil) return;
                        _sStep = 1; _sHoldPose = false; _sUntil = Time.time + 0.55f; _sFrom = transform.position;
                        BeginArc();
                        return;
                    }
                    if (_sStep == 1)
                    {
                        float k = Mathf.Clamp01(1f - (_sUntil - Time.time) / 0.55f);
                        SetArc(Mathf.Sin(k * Mathf.PI) * 0.45f * Mathf.Sqrt(Mathf.Max(0.3f, transform.lossyScale.y)));
                        _loco.MoveTo(_sPoint, Run() * Sp.surpriseBurstMul, 0.3f);
                        if (Time.time < _sUntil) return;
                        ResetArc();
                        _sStep = 2; _sHoldPose = true; _loco.Stop(); _sUntil = Time.time + Random.Range(1.2f, 2.4f);
                        if (_vis.HasEat) _vis.PlayEat(); else _vis.PlayIdle();
                        return;
                    }
                    _loco.Stop();
                    if (Time.time >= _sUntil) { EndSurprise(); _mode = ASHAnimalMode.Wander; }
                    return;
                }
                case ASHSurpriseKind.FalseCharge:
                {
                    if (!t) { EndSurprise(); _mode = ASHAnimalMode.Wander; return; }
                    float d = Flat(transform.position, t.position);
                    if (_sStep == 0)
                    {
                        _loco.Face(t.position, dt);
                        if (d > Sp.surpriseMeters && Time.time < _sUntil && _loco.StuckTime < 0.8f)
                        { _loco.MoveTo(t.position, Run() * Sp.surpriseBurstMul, Sp.surpriseMeters * 0.9f); return; }
                        // skid to a stop short of the target and huff
                        _sStep = 1; _sHoldPose = true; _loco.Stop(); _sUntil = Time.time + secs;
                        if (_vis.HasCall) _vis.PlayCall(); else _vis.PlayIdle();
                        return;
                    }
                    if (_sStep == 1)
                    {
                        _loco.Stop(); _loco.Face(t.position, dt);
                        if (Time.time < _sUntil) return;
                        _sStep = 2; _sHoldPose = false; _sPoint = Ring(t, Mathf.Max(StandOffOrRange() + 2f, Sp.surpriseMeters * 1.8f)); _sUntil = Time.time + 2.5f;
                        return;
                    }
                    bool back = _loco.MoveTo(_sPoint, Trot(), 0.8f);
                    if (back || Time.time >= _sUntil || _loco.StuckTime > 0.8f) { EndSurprise(); _mode = RushMode(); if (UseStandOff) EnterHold(); }
                    return;
                }
                case ASHSurpriseKind.CircleBehind:
                {
                    if (!t) { EndSurprise(); _mode = ASHAnimalMode.Wander; return; }
                    float ring = Mathf.Max(Sp.surpriseMeters, StandOffOrRange() + 1.5f);
                    var behind = t.position - t.forward * ring;
                    var from = transform.position - t.position; from.y = 0f;
                    var back = -t.forward; back.y = 0f;
                    float ang = Vector3.SignedAngle(from.sqrMagnitude > 0.01f ? from : back, back, Vector3.up);
                    // swing around on the ring toward the target's back in steps, so it reads as circling
                    var step = Quaternion.Euler(0f, Mathf.Clamp(ang, -50f, 50f), 0f) * (from.sqrMagnitude > 0.01f ? from.normalized : back.normalized);
                    var dest = Mathf.Abs(ang) < 25f ? behind : t.position + step * ring;
                    _loco.MoveTo(dest, Fatigued(Trot() * Sp.surpriseBurstMul), 0.6f);
                    bool there = Mathf.Abs(ang) < 25f && Flat(transform.position, behind) < 2f;
                    if (there || Time.time >= _sUntil || _loco.StuckTime > 1.2f)
                    {
                        EndSurprise();
                        _boostMul = Sp.surpriseBurstMul; _boostUntil = Time.time + 2f;
                        _mode = ASHAnimalMode.Chase;
                        if (UseStandOff && TryToken(t)) { _phase = Engage.Lunge; _phaseUntil = Time.time + 4f; }
                        else if (UseStandOff) EnterHold();
                    }
                    return;
                }
                case ASHSurpriseKind.StalkFromCover:
                {
                    if (!t) { EndSurprise(); _mode = ASHAnimalMode.Wander; return; }
                    if (_playerHitMe) { EndSurprise(); _mode = RushMode(); return; }
                    if (_sStep == 0)
                    {
                        bool arrived = _loco.MoveTo(_sPoint, Walk() * 0.85f, 0.7f);
                        if (arrived || _loco.StuckTime > 1.2f) { _sStep = 1; _sHoldPose = true; _loco.Stop(); _vis.PlayIdle(); _sUntil = Time.time + secs * Random.Range(0.8f, 1.4f); }
                        else if (Time.time >= _sUntil) { EndSurprise(); _mode = ASHAnimalMode.Stalk; }
                        return;
                    }
                    _loco.Stop(); _loco.FaceLoose(t.position, dt);
                    float d = Flat(transform.position, t.position);
                    // burst out early if the target wanders close, or once the wait is over
                    if (d > Sp.surpriseMeters * 2.5f && Time.time >= _sUntil) { EndSurprise(); _mode = ASHAnimalMode.Stalk; return; }
                    if (d < Sp.surpriseMeters * 0.6f || Time.time >= _sUntil)
                    {
                        EndSurprise();
                        _boostMul = Sp.surpriseBurstMul; _boostUntil = Time.time + 3f;
                        _aware = true; _mode = ASHAnimalMode.Chase;
                        if (UseStandOff && TryToken(t)) { _phase = Engage.Lunge; _phaseUntil = Time.time + 5f; }
                    }
                    return;
                }
                default: EndSurprise(); _mode = ASHAnimalMode.Wander; return;
            }
        }

        float StandOffOrRange() => UseStandOff ? StandOff : Sp.attackRange + 1f;

        void EndSurprise() { EndSurpriseVisual(); _sKind = ASHSurpriseKind.None; }

        void EndSurpriseVisual() { _sHoldPose = false; ResetArc(); }

        bool PlayState(string state)
        {
            if (string.IsNullOrEmpty(state) || !_vis.Has(state)) return false;
            var a = GetComponentInChildren<Animator>();
            if (!a) return false;
            a.CrossFadeInFixedTime(state, 0.2f);
            return true;
        }

        /// <summary>Hide spot for a stalking cat: a tree / rock between it and the target, on the far side.</summary>
        bool FindCover(Vector3 target, out Vector3 spot)
        {
            spot = transform.position;
            float r = Mathf.Max(4f, Sp.surpriseMeters);
            var mid = Vector3.Lerp(transform.position, target, 0.45f);
            var hits = Physics.OverlapSphere(mid, r, W.environmentLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool found = false;
            foreach (var c in hits)
            {
                if (!c || c is TerrainCollider || c.transform.IsChildOf(transform)) continue;
                if (_player && c.transform.IsChildOf(_player)) continue;
                if (c.GetComponentInParent<ASHAnimal>()) continue;
                var b = c.bounds;
                float w = Mathf.Max(b.size.x, b.size.z);
                if (w < 0.25f || w > 10f || b.size.y < 0.6f) continue;
                var away = b.center - target; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) continue;
                var p = b.center + away.normalized * (w * 0.5f + 0.9f);
                p.y = transform.position.y;
                float dTarget = Flat(p, target);
                if (dTarget < 5f || dTarget > r * 2.5f) continue;
                float score = Flat(p, transform.position) + Mathf.Abs(dTarget - r) * 0.5f;
                if (score < best) { best = score; spot = p; found = true; }
            }
            return found;
        }

        void BeginArc()
        {
            var a = GetComponentInChildren<Animator>();
            _sArcT = a && a.transform != transform ? a.transform : null;
            if (!_sArcT) return;
            _sArcBase = _sArcT.localPosition;
            _sArc = true;
        }

        void SetArc(float up)
        {
            if (!_sArc || !_sArcT) return;
            float sc = Mathf.Max(0.05f, transform.lossyScale.y);
            _sArcT.localPosition = _sArcBase + Vector3.up * (up / sc);
        }

        void ResetArc()
        {
            if (_sArc && _sArcT) _sArcT.localPosition = _sArcBase;
            _sArc = false;
        }

        // ------------------------------------------------------------------ damage in
        public void TakeDamage(vDamage damage)
        {
            if (_dead || damage == null) return;
            _onStart.Invoke(damage);
            float raw = damage.damageValue;
            if (raw <= 0f) return;
            string wk = WeaponKind(damage, out string gun);
            bool vital = (damage.damageType ?? "").IndexOf("vital", System.StringComparison.OrdinalIgnoreCase) >= 0;
            float amt;
            if (W != null && W.shotCountLethality && wk != "melee" && Sp != null)
            {
                if (gun == "shotgun")
                {
                    // one blast = many pellets: only the first pellet in the window counts
                    if (Time.time - _lastPelletT < W.shotgunPelletWindow) return;
                    _lastPelletT = Time.time;
                }
                bool zone = vital || VitalZone(damage.hitPosition);
                var src = damage.sender ? damage.sender.position : (_player ? _player.position : transform.position);
                float dist = Flat(src, transform.position);
                int shots = W.ShotsToKill(Sp, size);
                if (gun == "pistol") shots = Mathf.Max(1, Mathf.CeilToInt(shots * W.pistolShotsMul));
                bool oneShot = (zone && ((gun == "bow" && W.bowVitalOneShot) || (gun == "rifle" && W.rifleVitalOneShot)))
                               || (gun == "shotgun" && dist <= W.shotgunOneShotMeters);
                float per = _hpMax / shots + 0.01f;
                amt = oneShot ? _hp + 1f : (gun == "pistol" && zone ? per * 2f : per);
            }
            else amt = W != null ? W.Incoming(Sp, size, raw, wk, vital) : raw;
            _hp -= amt;
            _onRecv.Invoke(damage);
            if (_mode == ASHAnimalMode.Surprise) EndSurpriseVisual();
            _vis.PlayHit();
            InterruptIdle();

            Transform sender = damage.sender;
            bool fromPlayer = sender && _player && (sender == _player || sender.IsChildOf(_player));
            bool fromDog = sender && _dog && (sender == _dog || sender.IsChildOf(_dog));
            if (!fromDog && sender)
            {
                var dog = sender.GetComponentInParent<ASHDogCompanion>();
                if (dog && dog == ASHDogCompanion.Active) fromDog = true;
            }

            if (fromPlayer) { _playerHitMe = true; _dogHitMe = false; _focus = _player; }
            else if (fromDog && DogUsable()) { _dogHitMe = true; _playerHitMe = false; _focus = _dog; }
            else if (sender) _focus = sender;

            _lastSense = sender ? sender.position : transform.position;
            _alert = 1f;

            if (_mode != ASHAnimalMode.Trapped)
            {
                if (Sp != null && Sp.temperament == ASHWildlifeTemperament.Passive)
                {
                    bool shortFlee = Random.value < Sp.shortFleeChance || kind == ASHWildlifeKind.Deer || kind == ASHWildlifeKind.Fawn;
                    BeginFlee(_lastSense, shortFlee);
                    AlertHerd(_lastSense);
                }
                else
                {
                    _aware = true;
                    if (_hpMax > 0f && _hp / _hpMax <= Sp.retreatHealth && Random.value < Sp.retreatChance * Mathf.Lerp(1.4f, 0.6f, _p.bold) * (W != null ? Mathf.Lerp(1f, W.passiveRetreatMul, TP()) : 1f))
                        BeginRetreat(_lastSense);
                    else
                        _mode = RushMode();
                }
            }
            if (_hp <= 0f) Die();
        }

        /// <summary>"arrow" / "bullet" / "melee"; gun = "bow", "rifle", "pistol", "shotgun" or "" (melee / unknown).</summary>
        string WeaponKind(vDamage damage, out string gun)
        {
            gun = "";
            string t0 = (damage.damageType ?? "").ToLowerInvariant();
            if (t0.Contains("melee") || t0.Contains("claw") || t0.Contains("animal")) return "melee";
            var b = ASHCombatBridge.Player;
            bool fromPlayer = damage.sender && b && (damage.sender == b.transform || damage.sender.IsChildOf(b.transform) || (_player && (damage.sender == _player || damage.sender.IsChildOf(_player))));
            if (fromPlayer && b.CurrentData && b.CurrentData.kind != ASHWeaponKind.Melee)
            {
                if (b.CurrentData.kind == ASHWeaponKind.Bow) { gun = "bow"; return "arrow"; }
                var it = b.CurrentItem;
                string id = it ? ((it.itemId ?? "") + " " + (it.displayName ?? "")).ToLowerInvariant() : "";
                if (id.Contains("shotgun")) gun = "shotgun";
                else if (id.Contains("pistol") || id.Contains("revolver") || id.Contains("sidearm") || (it && it.gripStyle == ASHGripStyle.PistolGrip)) gun = "pistol";
                else gun = "rifle";
                return "bullet";
            }
            string k = WeaponKindRaw(damage);
            if (k == "arrow") gun = "bow";
            else if (k == "bullet") { string n = (t0 + " " + (damage.sender ? damage.sender.name.ToLowerInvariant() : "")); gun = n.Contains("shotgun") ? "shotgun" : n.Contains("pistol") ? "pistol" : "rifle"; }
            return k;
        }

        static string WeaponKindRaw(vDamage damage)
        {
            string t = (damage.damageType ?? "").ToLowerInvariant();
            if (t.Contains("arrow") || t.Contains("bow")) return "arrow";
            if (t.Contains("bullet") || t.Contains("gun") || t.Contains("pistol") || t.Contains("rifle")) return "bullet";
            if (damage.sender)
            {
                string n = damage.sender.name.ToLowerInvariant();
                if (n.Contains("arrow") || n.Contains("bow") || n.Contains("bolt")) return "arrow";
                if (n.Contains("bullet") || n.Contains("pistol") || n.Contains("rifle") || n.Contains("shotgun") || n.Contains("gun")) return "bullet";
                if (damage.sender.GetComponentInParent<Invector.vShooter.vShooterWeapon>())
                {
                    var w = damage.sender.GetComponentInParent<Invector.vShooter.vShooterWeapon>();
                    return w && w.chargeWeapon ? "arrow" : "bullet";
                }
            }
            return "melee";
        }

        /// <summary>Dead: let the motor coast to a stop, then keep the body on top of the visible ash/snow layer
        /// (terrain height + accumulation depth from ASHAccumulationController) instead of under it.</summary>
        void CorpseTick(float dt)
        {
            var w = W;
            if (_loco.Speed > 0.01f || _corpseAt < 0f || Time.time - _corpseAt < 0.25f)
            {
                _loco.Stop(); _loco.Animate(dt);
                _corpseBaseY = transform.position.y - _corpseLift;
                return;
            }
            if (w == null || !w.corpseOnSurface) { if (_corpseLift != 0f) { SetCorpseLift(0f); } return; }
            if (Time.time >= _corpseSampleAt)
            {
                _corpseSampleAt = Time.time + w.corpseRefreshSeconds;
                _corpseLiftTarget = Mathf.Max(0f, LayerDepthUnderBody() * w.corpseDepthFactor + w.corpseLiftOffset);
            }
            float settle = Mathf.Max(0.01f, w.corpseSettleSeconds);
            SetCorpseLift(Mathf.MoveTowards(_corpseLift, _corpseLiftTarget, dt * Mathf.Max(0.05f, Mathf.Abs(_corpseLiftTarget - _corpseLift)) / settle + dt * 0.02f));
        }

        void SetCorpseLift(float lift)
        {
            var p = transform.position;
            p.y = _corpseBaseY + lift;
            transform.position = p;
            _corpseLift = lift;
        }

        float LayerDepthUnderBody()
        {
            var acc = ASH.World.Accumulation.ASHAccumulationController.Instance;
            if (!acc || !acc.isActiveAndEnabled) return 0f;
            float len = 0.5f;
            if (W != null && Sp != null) { var b = W.Body(Sp); len = Mathf.Max(0.15f, b.y) * W.Scale(Sp, size); }
            var c = transform.position; var f = transform.forward * len; var r = transform.right * len * 0.4f;
            float d = acc.GetDepthMetres(c);
            d = Mathf.Max(d, acc.GetDepthMetres(c + f));
            d = Mathf.Max(d, acc.GetDepthMetres(c - f));
            d = Mathf.Max(d, acc.GetDepthMetres(c + r));
            d = Mathf.Max(d, acc.GetDepthMetres(c - r));
            return d;
        }

        public void ApplyDamage(float amount, Transform sender)
        {
            TakeDamage(new vDamage(Mathf.RoundToInt(amount)) { sender = sender, hitPosition = transform.position });
        }

        public void Die()
        {
            if (_dead) return;
            _dead = true;
            ReleaseToken();
            _mode = ASHAnimalMode.Dead;
            _loco.Stop();
            _vis.PlayDeath();
            _corpseAt = Time.time; _corpseLift = 0f; _corpseLiftTarget = 0f; _corpseSampleAt = 0f; _corpseBaseY = transform.position.y;
            foreach (var c in GetComponentsInChildren<Collider>())
                if (c && c is CharacterController == false && !c.isTrigger) c.enabled = true;
        }

        public float GetUsePriority(in ASHUseContext c)
        {
            if (!_dead || _harvested) return -1f;
            if (c.Inventory == null || !c.Inventory.HasTool(ASHToolType.Knife)) return 8f;
            return 40f;
        }

        public float GetHoldDuration(in ASHUseContext c) => !_dead || _harvested ? 0f : (Sp != null ? Sp.harvestHold : 3f);

        public string GetPrompt(in ASHUseContext c)
        {
            if (!_dead || _harvested) return null;
            string name = Sp != null ? Sp.displayName : "Animal";
            if (c.Inventory == null || !c.Inventory.HasTool(ASHToolType.Knife)) return "Need a knife to harvest the " + name.ToLower();
            return "[Hold E] Harvest " + name;
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (!_dead || _harvested || c.Inventory == null || !c.Inventory.HasTool(ASHToolType.Knife)) return false;
            HarvestInto(c.Inventory);
            return true;
        }

        public void HarvestInto(ASHInventory inv)
        {
            if (_harvested || inv == null || Sp == null) return;
            _harvested = true;
            _harvestedAt = Time.time;
            if (Sp.drops != null)
            {
                foreach (var d in Sp.drops)
                {
                    if (d == null || !d.item || Random.value > d.chance) continue;
                    int n = Random.Range(d.min, d.max + 1);
                    if (n > 0) inv.AddItem(d.item, n);
                }
            }
            foreach (var col in GetComponentsInChildren<Collider>()) if (col) col.enabled = false;
            gameObject.SetActive(false);
        }

        void FindVitalBones()
        {
            foreach (var tr in GetComponentsInChildren<Transform>(true))
            {
                string n = tr.name.ToLowerInvariant();
                if (n.Contains("ash_") || n.Contains("collider") || n.Contains("hitbox")) continue;
                if (!_headBone && (n.EndsWith("head") || n.Contains(" head") || n.Contains("_head") || n == "head")) _headBone = tr;
                if (!_chestBone && (n.Contains("chest") || n.Contains("ribcage") || n.Contains("spine2") || n.Contains("spine 2") || n.Contains("spine_02") || n.Contains("spine02"))) _chestBone = tr;
            }
        }

        /// <summary>Head or heart hit, judged from the hit point against the head / chest bones (or a body-shape estimate).</summary>
        bool VitalZone(Vector3 hit)
        {
            if (hit.sqrMagnitude < 0.0001f || W == null || Sp == null) return false;
            var body = W.Body(Sp);
            float sc = Mathf.Max(0.1f, transform.lossyScale.x);
            float r = Mathf.Max(0.08f, body.x) * sc;
            Vector3 head = _headBone ? _headBone.position : transform.TransformPoint(new Vector3(0f, (body.z * 1.25f), body.y * 0.5f + body.x * 1.1f));
            Vector3 heart = _chestBone ? _chestBone.position - transform.up * r * 0.35f : transform.TransformPoint(new Vector3(0f, body.z * 0.85f, body.y * 0.25f));
            if ((hit - head).magnitude <= r * W.headZoneScale) return true;
            if ((hit - heart).magnitude <= r * W.heartZoneScale) return true;
            return false;
        }

        void EnsureHitboxes()
        {
            if (_hitboxes) return;
            _hitboxes = true;
            try { if (gameObject.tag == "Untagged" || gameObject.tag == "Animal") gameObject.tag = "Enemy"; }
            catch { /* tag missing in a stripped project */ }
            int layer = LayerMask.NameToLayer("Enemy");
            if (layer >= 0 && gameObject.layer != layer) gameObject.layer = layer;
            bool solid = false;
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (!c || c is CharacterController) continue;
                if (!c.isTrigger) solid = true;
                if (layer >= 0) c.gameObject.layer = layer;
                try { if (c.gameObject.tag == "Untagged" || c.gameObject.tag == "Animal") c.gameObject.tag = "Enemy"; } catch { }
                var hb = c.GetComponent<ASHWildlifeHitbox>();
                if (!hb) hb = c.gameObject.AddComponent<ASHWildlifeHitbox>();
                hb.Bind(this);
            }
            if (!solid)
            {
                Transform ht = transform.Find("ASH_Hitbox");
                var hg = ht ? ht.gameObject : new GameObject("ASH_Hitbox");
                if (!ht) hg.transform.SetParent(transform, false);
                if (layer >= 0) hg.layer = layer;
                try { hg.tag = "Enemy"; } catch { }
                var cap = hg.GetComponent<CapsuleCollider>() ?? hg.AddComponent<CapsuleCollider>();
                cap.isTrigger = false;
                var body = W != null ? W.Body(Sp) : new Vector3(0.3f, 0.7f, 0.5f);
                cap.height = Mathf.Max(0.4f, body.y + body.z);
                cap.radius = Mathf.Max(0.12f, body.x);
                cap.center = new Vector3(0f, Mathf.Max(0.2f, body.z), 0f);
                cap.direction = 1;
                var hb = hg.GetComponent<ASHWildlifeHitbox>() ?? hg.AddComponent<ASHWildlifeHitbox>();
                hb.Bind(this);
            }
        }

        void DisableVendorMotors()
        {
            var cc = GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!mb) continue;
                var n = mb.GetType().FullName ?? "";
                if (n.Contains("MAnimal") || n.Contains("AnimalAI") || n.Contains("AIControl") || n.Contains("MalbersAnimal"))
                    mb.enabled = false;
            }
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    }
}
