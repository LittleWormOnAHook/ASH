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
    public enum ASHAnimalMode { Wander, Idle, Graze, Curious, Flee, LookBack, Retreat, Stalk, Chase, Attack, Bluff, Charge, Trapped, Dead }

    /// <summary>
    /// Species AI on the dog-style ground locomotor, using each animal's own Malbers clips.
    /// Invector damage in and out, senses (sight cone, hearing, wind scent, gunshots), alert stages,
    /// herd and pack tactics, species-specific combat, knife harvest, traps, size variants.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHAnimal : MonoBehaviour, IASHDogThreat, IASHUsable, vIDamageReceiver
    {
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
             (_mode == ASHAnimalMode.Trapped && IsThreat));
        public ASHAnimalMode Mode => _mode;
        public ASHDogWildlifePolicy DogPolicy => Sp != null ? Sp.dogPolicy : ASHDogWildlifePolicy.Ignore;
        public Transform UseTransform => transform;
        public OnReceiveDamage onStartReceiveDamage => _onStart;
        public OnReceiveDamage onReceiveDamage => _onRecv;
        public struct ASHSnareHook { public GameObject trap; public ASHTrapSize size; }

        void OnEnable() { if (!Living.Contains(this)) Living.Add(this); }
        void OnDisable() { Living.Remove(this); }
        void OnDestroy() { Living.Remove(this); ASHTorchPresence.Scared -= OnTorchScare; }

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
            _senseT = (GetHashCode() & 7) * 0.04f;
            var anim = GetComponentInChildren<Animator>();
            if (anim) anim.applyRootMotion = false;
            _loco = GetComponent<ASHGroundLocomotion>() ?? gameObject.AddComponent<ASHGroundLocomotion>();
            _loco.Init(transform, anim, W != null ? W.Loco(Sp, size) : new ASHGroundLocomotion.Tune());
            _vis = GetComponent<ASHWildlifeAnim>() ?? gameObject.AddComponent<ASHWildlifeAnim>();
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
            if (_dead) { _loco.Stop(); _loco.Animate(dt); return; }
            if (Sp == null || W == null) return;
            _loco.tune = W.Loco(Sp, size);
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
            Animate(dt);
        }

        void Animate(float dt)
        {
            _loco.Animate(dt);
            if (_mode == ASHAnimalMode.Graze || _mode == ASHAnimalMode.Curious || _mode == ASHAnimalMode.LookBack || _mode == ASHAnimalMode.Bluff)
                return;
            if (W == null || Sp == null) return;
            _vis.TickLoco(_loco.ActualSpeed, W.Trot(Sp, size), W.Run(Sp, size), _loco.TurningInPlace, _loco.TurnRate, dt);
        }

        void TickMode(float dt)
        {
            switch (_mode)
            {
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
            if (_mode == ASHAnimalMode.Attack || _mode == ASHAnimalMode.Charge || _mode == ASHAnimalMode.Bluff || _mode == ASHAnimalMode.Retreat || _mode == ASHAnimalMode.Flee || _mode == ASHAnimalMode.LookBack)
                return;

            if (Sp.temperament == ASHWildlifeTemperament.Passive)
            {
                if (_alert >= 0.85f && (senseP || senseD))
                {
                    var from = senseP && _player ? _player.position : (senseD && _dog ? _dog.position : _lastSense);
                    BeginFlee(from, Random.value < Sp.shortFleeChance);
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
            if (_alert >= 0.55f && (senseP || senseD || _aware))
            {
                _aware = true;
                if (!_focus) _focus = senseP ? _player : (senseD ? _dog : _player);
                if (Sp.bluffCharge && !_hasBluffed && kind == ASHWildlifeKind.Bear) { StartBluff(); return; }
                if (Sp.temperament == ASHWildlifeTemperament.Stalk && !_playerHitMe && _focus == _player)
                    _mode = ASHAnimalMode.Stalk;
                else
                    _mode = RushMode();
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
            if (Sp != null && Sp.chargeAndTurn) return ASHAnimalMode.Charge;
            return ASHAnimalMode.Chase;
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
            if (!cheap && _vis.HasEat && _mode != ASHAnimalMode.Graze && Random.value < Sp.grazeChance * 0.02f * Activity01())
            {
                _mode = ASHAnimalMode.Graze;
                _grazeUntil = Time.time + Random.Range(Sp.grazeMin, Sp.grazeMax);
                _loco.Stop();
                _vis.PlayEat();
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
                float r = Mathf.Max(3f, Sp.wanderRadius);
                _wander = _home + o.normalized * Random.Range(r * 0.25f, r);
                if (Random.value < 0.22f) { _idleUntil = Time.time + Random.Range(1.5f, 4f); _mode = ASHAnimalMode.Idle; _vis.PlayIdle(); return; }
            }
            _mode = ASHAnimalMode.Wander;
            _loco.MoveTo(_wander, Fatigued(W.Walk(Sp, size)), 1.1f);
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
                else if (_alert >= 0.55f && Sp.temperament != ASHWildlifeTemperament.Passive) { _aware = true; _mode = RushMode(); }
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
            float dist = shortFlee ? Sp.shortFleeMeters : Sp.longFleeMeters;
            _flee = FleePoint(from, dist);
            _scareUntil = Time.time + (shortFlee ? Random.Range(1.2f, 2.6f) : Random.Range(3f, Mathf.Max(4f, Sp.torchScareSeconds)));
            _mode = ASHAnimalMode.Flee;
            _lastSense = from;
        }

        void BeginRetreat(Vector3 from)
        {
            InterruptIdle();
            _flee = FleePoint(from, Sp.shortFleeMeters * 1.4f);
            _retreatUntil = Time.time + Sp.retreatSeconds;
            _scareUntil = _retreatUntil;
            _mode = ASHAnimalMode.Retreat;
        }

        void AlertHerd(Vector3 from)
        {
            if (Sp == null || !Sp.herdFlee || W == null) return;
            float r = W.herdRadius;
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
            bool done = _loco.MoveTo(_flee, Fatigued(W.Run(Sp, size)), 1.4f) || Time.time >= _scareUntil;
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
            if (_playerHitMe) { _mode = RushMode(); ChaseTick(); return; }
            float d = Flat(transform.position, t.position);
            if (d <= Sp.attackRange * 1.1f) { _mode = ASHAnimalMode.Attack; AttackTick(); return; }
            // Cougar: stay behind the target, close when line of sight breaks (ambush).
            Vector3 dest;
            if (Sp.ambush)
            {
                var behind = t.position - t.forward * Mathf.Clamp(d * 0.35f, 4f, 12f);
                dest = behind;
                bool los = HasLos(t);
                float spd = los ? W.Walk(Sp, size) : W.Run(Sp, size);
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
                _loco.MoveTo(dest, W.Walk(Sp, size), 2f);
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
                _loco.MoveTo(_flee, W.Run(Sp, size), 2.2f);
            else
                _loco.Stop();
            if (Time.time < _bluffUntil) return;
            // Territorial warning, then either a real rush or they break off if the player backs away.
            if (t && Flat(transform.position, t.position) < Sp.detectRange * 0.9f && Random.value < 0.65f)
                _mode = RushMode();
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
            _loco.MoveTo(t.position, Fatigued(W.Run(Sp, size) * 1.15f), Sp.attackRange * 0.6f);
        }

        void ChaseTick()
        {
            var t = Focus();
            if (!t) { WanderTick(false); return; }
            float d = Flat(transform.position, t.position);
            if (d <= Sp.attackRange) { _mode = ASHAnimalMode.Attack; AttackTick(); return; }
            Vector3 dest = t.position;
            if (Sp.packFlank && packId != 0)
            {
                int slot = _packSlot;
                float ang = slot * 70f - 35f;
                float rad = W.packFlankRadius > 1f ? W.packFlankRadius : 7f;
                dest = t.position + Quaternion.Euler(0f, ang, 0f) * (t.forward * -1f) * rad;
                // Lowest slot tests a bite when close; others keep circling.
                if (slot == 0 && d < Sp.attackRange + 2.5f) dest = t.position;
            }
            _loco.MoveTo(dest, Fatigued(W.Run(Sp, size)), Sp.attackRange * 0.7f);
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
            if (d > Sp.attackRange * 1.3f) { _mode = RushMode(); return; }
            _loco.Stop();
            if (Time.time < _nextAttack) return;
            _nextAttack = Time.time + Sp.attackCooldown;
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
            bool running = speed >= W.Trot(Sp, size) + 0.2f;
            if (running) { _sprintT += Time.deltaTime; _restT = 0f; }
            else { _restT += Time.deltaTime; if (_restT > 2f) _sprintT = Mathf.Max(0f, _sprintT - Time.deltaTime * 1.4f); }
            if (_sprintT > Sp.fatigueRunSeconds) return Mathf.Min(speed, W.Trot(Sp, size));
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

        // ------------------------------------------------------------------ damage in
        public void TakeDamage(vDamage damage)
        {
            if (_dead || damage == null) return;
            _onStart.Invoke(damage);
            float raw = damage.damageValue;
            if (raw <= 0f) return;
            string wk = WeaponKind(damage);
            bool vital = (damage.damageType ?? "").IndexOf("vital", System.StringComparison.OrdinalIgnoreCase) >= 0;
            float amt = W != null ? W.Incoming(Sp, size, raw, wk, vital) : raw;
            _hp -= amt;
            _onRecv.Invoke(damage);
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
                    if (_hpMax > 0f && _hp / _hpMax <= Sp.retreatHealth && Random.value < Sp.retreatChance)
                        BeginRetreat(_lastSense);
                    else
                        _mode = RushMode();
                }
            }
            if (_hp <= 0f) Die();
        }

        static string WeaponKind(vDamage damage)
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

        public void ApplyDamage(float amount, Transform sender)
        {
            TakeDamage(new vDamage(Mathf.RoundToInt(amount)) { sender = sender, hitPosition = transform.position });
        }

        public void Die()
        {
            if (_dead) return;
            _dead = true;
            _mode = ASHAnimalMode.Dead;
            _loco.Stop();
            _vis.PlayDeath();
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
