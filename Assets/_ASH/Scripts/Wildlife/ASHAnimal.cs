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
    public enum ASHAnimalMode { Wander, Idle, Flee, Stalk, Chase, Attack, Trapped, Dead }

    /// <summary>
    /// Species AI on top of the dog-style ground locomotor and each animal's own Malbers clips.
    /// Knife harvest, Invector damage, dog threat, traps, packs, size variants.
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

        float _hp, _nextAttack, _wanderT, _idleUntil, _scareUntil, _stalkUntil, _breakFreeAt, _harvestedAt = -1f;
        Vector3 _home, _wander, _flee;
        bool _dead, _harvested, _aware;
        ASHAnimalMode _mode = ASHAnimalMode.Wander;
        ASHSnareHook _trap;
        readonly OnReceiveDamage _onStart = new OnReceiveDamage();
        readonly OnReceiveDamage _onRecv = new OnReceiveDamage();

        public bool IsThreat => !_dead && !_harvested && Sp != null && Sp.temperament != ASHWildlifeTemperament.Passive;
        public bool IsDead => _dead;
        public bool Harvested => _harvested;
        public bool IsTrapped => _mode == ASHAnimalMode.Trapped;
        /// <summary>True when this hostile is aggro'd on the player or the pet dog (chase, attack, or a live trapped wolf).</summary>
        public bool IsAggroOnPlayerOrDog =>
            !_dead && !_harvested && Sp != null && Sp.temperament != ASHWildlifeTemperament.Passive &&
            (_mode == ASHAnimalMode.Chase || _mode == ASHAnimalMode.Attack || (_mode == ASHAnimalMode.Trapped && IsThreat));
        public ASHAnimalMode Mode => _mode;
        public ASHDogWildlifePolicy DogPolicy => Sp != null ? Sp.dogPolicy : ASHDogWildlifePolicy.Ignore;
        public Transform UseTransform => transform;
        public OnReceiveDamage onStartReceiveDamage => _onStart;
        public OnReceiveDamage onReceiveDamage => _onRecv;

        public struct ASHSnareHook { public GameObject trap; public ASHTrapSize size; }

        void Awake()
        {
            settings = settings ? settings : ASHWildlifeSettings.I;
            if (Sp != null)
            {
                _hp = W.Health(Sp, size);
                float sc = W.Scale(Sp, size);
                if (Mathf.Abs(transform.localScale.x - sc) > 0.01f) transform.localScale = Vector3.one * sc;
            }
            _home = transform.position;
            _wander = _home;
            var anim = GetComponentInChildren<Animator>();
            if (anim) anim.applyRootMotion = false;
            _loco = GetComponent<ASHGroundLocomotion>() ?? gameObject.AddComponent<ASHGroundLocomotion>();
            _loco.Init(transform, anim, W != null ? W.Loco(Sp, size) : new ASHGroundLocomotion.Tune());
            _vis = GetComponent<ASHWildlifeAnim>() ?? gameObject.AddComponent<ASHWildlifeAnim>();
            _vis.Bind(anim);
            DisableVendorMotors();
            ASHTorchPresence.Scared += OnTorchScare;
        }

        void OnDestroy() => ASHTorchPresence.Scared -= OnTorchScare;

        void Start()
        {
            FindPlayer();
            if (_player) ASHPlayerCombatRelay.EnsureOn(_player);
            _loco.MarkTeleported();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _loco.TickMeasure(dt);
            if (_harvested) return;
            if (_dead)
            {
                _loco.Stop();
                _loco.Animate(dt);
                return;
            }
            if (Sp == null || W == null) return;
            _loco.tune = W.Loco(Sp, size);
            if (!_player) FindPlayer();
            if (!_dog) FindDog();

            if (_mode == ASHAnimalMode.Trapped) { TrappedTick(dt); _loco.Animate(dt); _vis.TickLoco(0f, W.Trot(Sp, size), W.Run(Sp, size), false, 0f, dt); return; }

            if (Time.time < _scareUntil) { FleeTick(); Animate(dt); return; }

            if (Sp.scaredByTorch && ASHTorchPresence.IsLitNear(transform.position, Sp.torchScareRadius, out var torchPos))
            {
                BeginFlee(torchPos, true);
                FleeTick();
                Animate(dt);
                return;
            }

            float dPlayer = _player ? Flat(transform.position, _player.position) : 999f;
            float dDog = _dog ? Flat(transform.position, _dog.position) : 999f;
            float range = Mathf.Max(Sp.detectRange, Sp.stalkRange);
            bool seePlayer = _player && dPlayer <= range && HasLos(_player);
            bool seeDog = _dog && dDog <= range && HasLos(_dog);

            if (Sp.temperament == ASHWildlifeTemperament.Passive)
            {
                if (seePlayer && dPlayer <= Sp.detectRange) BeginFlee(_player.position, Random.value < Sp.shortFleeChance);
                else if (seeDog && dDog <= Sp.detectRange) BeginFlee(_dog.position, Random.value < Sp.shortFleeChance);
                if (_mode == ASHAnimalMode.Flee) FleeTick();
                else WanderTick();
            }
            else
            {
                if (PackAlert(dPlayer, seePlayer || seeDog)) { _aware = true; _mode = ASHAnimalMode.Chase; if (!_focus) _focus = NearerThreat(dPlayer, dDog, seePlayer, seeDog); }
                if (Sp.temperament == ASHWildlifeTemperament.Stalk && seePlayer && !_aware && dPlayer > Sp.detectRange * 0.55f && dPlayer <= Sp.stalkRange)
                {
                    _mode = ASHAnimalMode.Stalk;
                    _stalkUntil = Time.time + Random.Range(4f, 10f);
                    _focus = _player;
                }
                var threat = NearerThreat(dPlayer, dDog, seePlayer && dPlayer <= Sp.detectRange, seeDog && dDog <= Sp.detectRange);
                if (threat)
                {
                    _focus = threat;
                    _aware = true;
                    float d = threat == _dog ? dDog : dPlayer;
                    _mode = d <= Sp.attackRange ? ASHAnimalMode.Attack : ASHAnimalMode.Chase;
                }
                else if (_aware)
                {
                    float dFocus = _focus ? Flat(transform.position, _focus.position) : Mathf.Min(dPlayer, dDog);
                    if (dFocus > Mathf.Max(Sp.detectRange, Sp.stalkRange) * 1.7f) { _aware = false; _focus = null; _mode = ASHAnimalMode.Wander; }
                }

                if (_mode == ASHAnimalMode.Attack) AttackTick();
                else if (_mode == ASHAnimalMode.Chase) ChaseTick();
                else if (_mode == ASHAnimalMode.Stalk) StalkTick(dPlayer);
                else if (_mode == ASHAnimalMode.Flee) FleeTick();
                else WanderTick();
            }
            Animate(dt);
        }

        void Animate(float dt)
        {
            _loco.Animate(dt);
            _vis.TickLoco(_loco.ActualSpeed, W.Trot(Sp, size), W.Run(Sp, size), _loco.TurningInPlace, _loco.TurnRate, dt);
        }

        void WanderTick()
        {
            if (Time.time < _idleUntil) { _loco.Stop(); if (_player) _loco.FaceLoose(_player.position, Time.deltaTime); return; }
            if (Time.time >= _wanderT || _loco.StuckTime > 1.2f)
            {
                _wanderT = Time.time + Random.Range(3f, 8f);
                var o = Random.insideUnitSphere; o.y = 0f;
                float r = Mathf.Max(3f, Sp.wanderRadius);
                _wander = _home + o.normalized * Random.Range(r * 0.25f, r);
                if (Random.value < 0.22f) { _idleUntil = Time.time + Random.Range(1.5f, 4f); _vis.PlayIdle(); return; }
            }
            _mode = ASHAnimalMode.Wander;
            _loco.MoveTo(_wander, W.Walk(Sp, size), 1.1f);
        }

        void BeginFlee(Vector3 from, bool shortFlee)
        {
            float dist = shortFlee ? Sp.shortFleeMeters : Sp.longFleeMeters;
            var away = transform.position - from; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            _flee = transform.position + away.normalized * dist;
            _scareUntil = Time.time + (shortFlee ? Random.Range(1.2f, 2.4f) : Random.Range(3f, Sp.torchScareSeconds));
            _mode = ASHAnimalMode.Flee;
        }

        void FleeTick()
        {
            if (_loco.MoveTo(_flee, W.Run(Sp, size), 1.4f) || Time.time >= _scareUntil)
            {
                _mode = ASHAnimalMode.Idle;
                _idleUntil = Time.time + Random.Range(0.8f, 2.2f);
                _scareUntil = 0f;
                if (_player) _loco.Face(_player.position, Time.deltaTime);
            }
        }

        void StalkTick(float dPlayer)
        {
            if (!_player) { WanderTick(); return; }
            if (Time.time >= _stalkUntil) { _aware = true; _focus = _player; _mode = ASHAnimalMode.Chase; ChaseTick(); return; }
            float hold = Mathf.Clamp(Sp.detectRange * 0.8f, 8f, Sp.stalkRange * 0.7f);
            var to = transform.position - _player.position; to.y = 0f;
            var dest = _player.position + (to.sqrMagnitude > 0.2f ? to.normalized : _player.forward) * hold;
            _loco.MoveTo(dest, W.Walk(Sp, size), 2f);
        }

        void ChaseTick()
        {
            var t = Focus();
            if (!t) { WanderTick(); return; }
            float d = Flat(transform.position, t.position);
            if (d <= Sp.attackRange) { _mode = ASHAnimalMode.Attack; AttackTick(); return; }
            _loco.MoveTo(t.position, W.Run(Sp, size), Sp.attackRange * 0.7f);
        }

        void AttackTick()
        {
            var t = Focus();
            if (!t) { _mode = ASHAnimalMode.Wander; return; }
            _loco.Face(t.position, Time.deltaTime);
            float d = Flat(transform.position, t.position);
            if (d > Sp.attackRange * 1.25f) { _mode = ASHAnimalMode.Chase; ChaseTick(); return; }
            _loco.Stop();
            if (Time.time < _nextAttack) return;
            _nextAttack = Time.time + Sp.attackCooldown;
            _vis.PlayAttack();
            float dmg = W.Damage(Sp, size);
            if (dmg <= 0f) return;
            var hc = t.GetComponentInParent<vIHealthController>();
            if (hc != null) hc.TakeDamage(new vDamage { damageValue = Mathf.RoundToInt(dmg), sender = transform, hitPosition = t.position });
        }

        void TrappedTick(float dt)
        {
            if (_trap.trap == null) { _mode = ASHAnimalMode.Wander; return; }
            _loco.Stop();
            if (kind == ASHWildlifeKind.Wolf && Time.time >= _breakFreeAt && _breakFreeAt > 0f)
            {
                ExitTrap();
                if (_player) { _aware = true; _mode = ASHAnimalMode.Chase; }
                return;
            }
            if (Sp.temperament == ASHWildlifeTemperament.Passive) return;
            if (_player)
            {
                _loco.Face(_player.position, dt);
                float d = Flat(transform.position, _player.position);
                if (d <= Sp.attackRange && Time.time >= _nextAttack)
                {
                    _nextAttack = Time.time + Sp.attackCooldown;
                    _vis.PlayAttack();
                    float dmg = W.Damage(Sp, size);
                    var hc = _player.GetComponentInParent<vIHealthController>();
                    if (hc != null && dmg > 0f) hc.TakeDamage(new vDamage { damageValue = Mathf.RoundToInt(dmg), sender = transform, hitPosition = _player.position });
                }
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
            else
            {
                // Prey caught in a trap dies in it and waits for a knife harvest.
                Die();
            }
        }

        public void ExitTrap()
        {
            _trap = default;
            _breakFreeAt = 0f;
            if (!_dead) _mode = ASHAnimalMode.Wander;
        }

        bool PackAlert(float dPlayer, bool see)
        {
            if (packId == 0 || Sp == null || Sp.packSize <= 1) return see && dPlayer <= Sp.detectRange;
            var all = FindObjectsByType<ASHAnimal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var a = all[i];
                if (!a || a == this || a.packId != packId || a._dead) continue;
                if (a._aware || a._mode == ASHAnimalMode.Chase || a._mode == ASHAnimalMode.Attack) return true;
            }
            return false;
        }

        Transform NearerThreat(float dPlayer, float dDog, bool seePlayer, bool seeDog)
        {
            if (seePlayer && seeDog) return dDog < dPlayer && _dog ? _dog : _player;
            if (seeDog) return _dog;
            if (seePlayer) return _player;
            return null;
        }

        Transform Focus()
        {
            if (_focus) return _focus;
            if (_player) return _player;
            return _dog;
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
                return false;
            }
            return true;
        }

        void OnTorchScare(Vector3 pos, float radius, float duration)
        {
            if (_dead || Sp == null || !Sp.scaredByTorch) return;
            if (Flat(transform.position, pos) <= radius)
            {
                BeginFlee(pos, false);
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
            var d = ASHDogCompanion.Active;
            _dog = d ? d.transform : null;
        }

        public void TakeDamage(vDamage damage)
        {
            if (_dead || damage == null) return;
            _onStart.Invoke(damage);
            float amt = damage.damageValue;
            if (amt <= 0f) return;
            _hp -= amt;
            _onRecv.Invoke(damage);
            _vis.PlayHit();
            _aware = Sp != null && Sp.temperament != ASHWildlifeTemperament.Passive;
            if (_aware && damage.sender)
            {
                if (_player && (damage.sender == _player || damage.sender.IsChildOf(_player))) _focus = _player;
                else if (_dog && (damage.sender == _dog || damage.sender.IsChildOf(_dog))) _focus = _dog;
                else _focus = damage.sender;
            }
            if (_aware && _mode != ASHAnimalMode.Trapped) _mode = ASHAnimalMode.Chase;
            if (Sp != null && Sp.temperament == ASHWildlifeTemperament.Passive && _mode != ASHAnimalMode.Trapped && damage.sender)
                BeginFlee(damage.sender.position, false);
            if (_hp <= 0f) Die();
        }

        public void ApplyDamage(float amount, Transform sender)
        {
            TakeDamage(new vDamage { damageValue = Mathf.RoundToInt(amount), sender = sender, hitPosition = transform.position });
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
