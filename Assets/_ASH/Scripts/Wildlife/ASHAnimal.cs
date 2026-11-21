using System;
using System.Collections.Generic;
using System.Reflection;
using ASH.Combat;
using ASH.Companion;
using ASH.Interaction;
using ASH.Inventory;
using Invector;
using UnityEngine;
using UnityEngine.Events;

namespace ASH.Wildlife
{
    /// <summary>
    /// Wolf / bear adapter over a Malbers animal when present, or a simple motor + CrossFade on model-only prefabs (bears).
    /// Implements dog threat, Invector damage, torch scare (wolves), and knife harvest of the carcass.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHAnimal : MonoBehaviour, IASHDogThreat, IASHUsable, vIDamageReceiver
    {
        public ASHWildlifeKind kind = ASHWildlifeKind.Wolf;
        public ASHWildlifeSettings settings;

        CharacterController _cc;
        Animator _anim;
        Transform _player;
        ASHWildlifeSettings.Species Sp => (settings ? settings : ASHWildlifeSettings.I)?.Of(kind);
        float _hp, _nextAttack, _wanderT, _scareUntil, _hitFlash;
        Vector3 _home, _wander, _flee;
        bool _dead, _harvested, _chasing;
        string _animState = "";
        readonly OnReceiveDamage _onStart = new OnReceiveDamage();
        readonly OnReceiveDamage _onRecv = new OnReceiveDamage();
        object _ai, _dmg;
        MethodInfo _setTarget, _clearTarget, _setDest, _receiveDmg;

        public bool IsThreat => !_dead && !_harvested;
        public bool IsDead => _dead;
        public bool Harvested => _harvested;
        public ASHDogWildlifePolicy DogPolicy => Sp != null ? Sp.dogPolicy : ASHDogWildlifePolicy.Engage;
        public Transform UseTransform => transform;
        public OnReceiveDamage onStartReceiveDamage => _onStart;
        public OnReceiveDamage onReceiveDamage => _onRecv;

        void Awake()
        {
            settings = settings ? settings : ASHWildlifeSettings.I;
            _cc = GetComponent<CharacterController>();
            _anim = GetComponentInChildren<Animator>();
            if (Sp != null) _hp = Sp.maxHealth;
            _home = transform.position;
            _wander = _home;
            BindMalbers();
            ASHTorchPresence.Scared += OnTorchScare;
        }

        void OnDestroy() => ASHTorchPresence.Scared -= OnTorchScare;

        void Start()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) _player = p.transform;
            if (!_player && ASHCombatBridge.Player) _player = ASHCombatBridge.Player.transform;
            if (_player) ASHPlayerCombatRelay.EnsureOn(_player);
        }

        void Update()
        {
            if (_dead || Sp == null) return;
            if (!_player)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p) _player = p.transform;
            }
            if (Time.time < _scareUntil) { FleeTick(); return; }

            if (kind == ASHWildlifeKind.Wolf && Sp.scaredByTorch &&
                ASHTorchPresence.IsLitNear(transform.position, Sp.torchScareRadius, out var torchPos))
            {
                _flee = transform.position + (transform.position - torchPos).normalized * 12f;
                _scareUntil = Time.time + 1.2f;
                FleeTick();
                return;
            }

            float dPlayer = _player ? Flat(transform.position, _player.position) : 999f;
            bool see = _player && dPlayer <= Sp.detectRange && HasLos();
            if (see) _chasing = true;
            else if (dPlayer > Sp.detectRange * 1.6f) _chasing = false;

            if (_chasing && _player) ChaseTick(dPlayer);
            else WanderTick();
        }

        bool HasLos()
        {
            if (!_player) return false;
            var origin = transform.position + Vector3.up * 1.1f;
            var dest = _player.position + Vector3.up * 1.1f;
            if (Physics.Linecast(origin, dest, out var hit, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == _player || hit.transform.IsChildOf(_player)) return true;
                if (hit.transform.IsChildOf(transform)) return true;
                return false;
            }
            return true;
        }

        void ChaseTick(float dPlayer)
        {
            Face(_player.position);
            if (dPlayer > Sp.attackRange)
            {
                MoveTowards(_player.position, Sp.runSpeed);
                AnimMove(Sp.runSpeed);
                MalbersTarget(_player);
                return;
            }
            StopMove();
            AnimMove(0f);
            if (Time.time >= _nextAttack) DoAttack();
        }

        void WanderTick()
        {
            MalbersTarget(null);
            if ((_wanderT -= Time.deltaTime) <= 0f)
            {
                _wanderT = UnityEngine.Random.Range(3f, 8f);
                var r = UnityEngine.Random.insideUnitSphere * Sp.wanderRadius; r.y = 0f;
                _wander = _home + r;
                SnapGround(ref _wander);
            }
            if (Flat(transform.position, _wander) > 1.2f)
            {
                Face(_wander);
                MoveTowards(_wander, Sp.walkSpeed);
                AnimMove(Sp.walkSpeed);
            }
            else { StopMove(); AnimMove(0f); }
        }

        void FleeTick()
        {
            MalbersTarget(null);
            Face(_flee);
            MoveTowards(_flee, Sp.runSpeed);
            AnimMove(Sp.runSpeed);
            MalbersDest(_flee);
        }

        void DoAttack()
        {
            _nextAttack = Time.time + Sp.attackCooldown;
            CrossFade(kind == ASHWildlifeKind.Bear ? "Be_Attack Bite Right" : null, "Attack");
            if (!_player) return;
            var recv = _player.GetComponentInParent<vIDamageReceiver>();
            if (recv == null) recv = _player.GetComponentInChildren<vIDamageReceiver>();
            if (recv == null) return;
            var dmg = new vDamage(Mathf.RoundToInt(Sp.damage))
            {
                sender = transform,
                hitPosition = _player.position + Vector3.up * 1f,
                damageType = kind == ASHWildlifeKind.Bear ? "bear" : "wolf"
            };
            recv.TakeDamage(dmg);
            var dog = ASHDogCompanion.Active;
            if (dog) dog.NotifyPlayerCombat(transform, true);
        }

        public void TakeDamage(vDamage damage)
        {
            if (damage == null || _dead) return;
            _onStart.Invoke(damage);
            ApplyHit(damage.damageValue, damage.sender);
            _onRecv.Invoke(damage);
        }

        public void NotifyDamagedByPlayer(Transform attacker, float amount)
        {
            if (_dead) return;
            ApplyHit(amount, attacker);
        }

        void ApplyHit(float amount, Transform sender)
        {
            if (_dead) return;
            _hp = Mathf.Max(0f, _hp - Mathf.Max(0f, amount));
            _hitFlash = Time.time + 0.4f;
            CrossFade(kind == ASHWildlifeKind.Bear ? "Be_Damaged Front Left" : null, "Hit");
            MalbersDamage(amount, sender);
            var dog = ASHDogCompanion.Active;
            if (dog && sender && _player && (sender == _player || sender.IsChildOf(_player)))
                dog.NotifyPlayerCombat(transform, false);
            if (_hp <= 0f) Die();
        }

        void Die()
        {
            if (_dead) return;
            _dead = true;
            _chasing = false;
            StopMove();
            MalbersTarget(null);
            if (_cc) _cc.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>())
                if (c && c is CharacterController == false) { /* leave a trigger for use */ }
            CrossFade(kind == ASHWildlifeKind.Bear ? "Be_Sleep Idle" : null, "Death");
            if (_anim) _anim.speed = 1f;
        }

        public float GetUsePriority(in ASHUseContext c) => _dead && !_harvested ? 52f : -1f;

        public float GetHoldDuration(in ASHUseContext c) => Sp != null ? Sp.harvestHold : 4f;

        public string GetPrompt(in ASHUseContext c)
        {
            if (!_dead || _harvested || Sp == null) return null;
            if (c.Inventory == null || !c.Inventory.HasTool(ASHToolType.Knife))
                return "Needs a knife to harvest the " + Sp.displayName.ToLowerInvariant();
            return "Harvest " + Sp.displayName;
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (!_dead || _harvested || Sp == null || c.Inventory == null) return false;
            if (!c.Inventory.HasTool(ASHToolType.Knife)) return false;
            int given = 0;
            if (Sp.drops != null)
            {
                for (int i = 0; i < Sp.drops.Count; i++)
                {
                    var d = Sp.drops[i];
                    if (!d.item || d.chance < 1f && UnityEngine.Random.value > d.chance) continue;
                    int n = UnityEngine.Random.Range(d.min, Mathf.Max(d.min, d.max) + 1);
                    if (n <= 0) continue;
                    given += c.Inventory.AddItem(d.item, n);
                }
            }
            _harvested = true;
            gameObject.SetActive(false);
            return given > 0 || true;
        }

        void OnTorchScare(Vector3 pos, float radius, float seconds)
        {
            if (_dead || Sp == null || !Sp.scaredByTorch) return;
            if ((transform.position - pos).sqrMagnitude > radius * radius) return;
            _flee = transform.position + (transform.position - pos).normalized * Mathf.Max(8f, radius);
            _scareUntil = Time.time + Mathf.Max(1f, seconds > 0f ? seconds : Sp.torchScareSeconds);
            _chasing = false;
        }

        // ------------------------------------------------------------------ movement / anim
        void MoveTowards(Vector3 dest, float speed)
        {
            var flat = dest - transform.position; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            var dir = flat.normalized;
            var delta = dir * speed * Time.deltaTime;
            if (_cc && _cc.enabled)
            {
                delta.y = -2.5f * Time.deltaTime;
                _cc.Move(delta);
            }
            else
            {
                var p = transform.position + dir * speed * Time.deltaTime;
                SnapGround(ref p);
                transform.position = p;
            }
            MalbersDest(dest);
        }

        void StopMove()
        {
            if (_cc && _cc.enabled) _cc.Move(Vector3.zero);
        }

        void Face(Vector3 dest)
        {
            var flat = dest - transform.position; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), Time.deltaTime * 8f);
        }

        void AnimMove(float speed)
        {
            if (!_anim) return;
            if (kind == ASHWildlifeKind.Bear)
            {
                string st = speed < 0.2f ? "Be_Idle 01" : speed < 3.5f ? "Be_Walk" : speed < 5.5f ? "Be_Trot" : "Be_Run";
                CrossFade(st, null);
            }
            else if (_anim.parameters != null)
            {
                for (int i = 0; i < _anim.parameterCount; i++)
                {
                    var p = _anim.parameters[i];
                    if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) _anim.SetFloat(p.nameHash, speed);
                    if (p.name == "Vertical" && p.type == AnimatorControllerParameterType.Float) _anim.SetFloat(p.nameHash, speed);
                }
            }
        }

        void CrossFade(string state, string trigger)
        {
            if (!_anim) return;
            if (!string.IsNullOrEmpty(trigger))
            {
                try { _anim.SetTrigger(trigger); } catch { /* parameter may not exist */ }
            }
            if (string.IsNullOrEmpty(state) || state == _animState) return;
            if (_anim.HasState(0, Animator.StringToHash(state)))
            {
                _anim.CrossFadeInFixedTime(state, 0.2f);
                _animState = state;
            }
        }

        static void SnapGround(ref Vector3 p)
        {
            var origin = p + Vector3.up * 4f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore))
                p = hit.point;
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        // ------------------------------------------------------------------ Malbers (optional, reflection so a clone without the vendor still compiles)
        void BindMalbers()
        {
            var aiType = FindType("MalbersAnimations.Controller.MAnimalAIControl", "MalbersAnimations");
            var dmgType = FindType("MalbersAnimations.MDamageable", "MalbersAnimations");
            if (aiType != null) _ai = GetComponentInChildren(aiType);
            if (dmgType != null) _dmg = GetComponentInChildren(dmgType);
            if (_ai != null)
            {
                _setTarget = _ai.GetType().GetMethod("SetTarget", new[] { typeof(Transform), typeof(bool) })
                             ?? _ai.GetType().GetMethod("SetTarget", new[] { typeof(Transform) });
                _clearTarget = _ai.GetType().GetMethod("ClearTarget", Type.EmptyTypes);
                _setDest = _ai.GetType().GetMethod("SetDestination", new[] { typeof(Vector3), typeof(bool) })
                           ?? _ai.GetType().GetMethod("SetDestination", new[] { typeof(Vector3) });
            }
            if (_dmg != null)
            {
                foreach (var m in _dmg.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    var ps = m.GetParameters();
                    if (m.Name == "ReceiveDamage" && ps.Length == 2 && ps[1].ParameterType == typeof(float))
                    { _receiveDmg = m; break; }
                }
            }
            HookHealthEmpty();
        }

        void HookHealthEmpty()
        {
            var statsType = FindType("MalbersAnimations.Stats", "MalbersAnimations");
            if (statsType == null) return;
            var stats = GetComponentInChildren(statsType) as Component;
            if (!stats) return;
            var list = statsType.GetField("stats")?.GetValue(stats) as System.Collections.IEnumerable
                       ?? statsType.GetProperty("stats")?.GetValue(stats) as System.Collections.IEnumerable;
            if (list == null) return;
            foreach (var st in list)
            {
                if (st == null) continue;
                var id = st.GetType().GetField("ID")?.GetValue(st);
                string name = null;
                if (id != null) name = id.GetType().GetProperty("name")?.GetValue(id) as string ?? id.ToString();
                if (string.IsNullOrEmpty(name) || name.IndexOf("health", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (st.GetType().GetField("OnStatEmpty")?.GetValue(st) is UnityEvent ev)
                    ev.AddListener(Die);
                break;
            }
        }

        void MalbersTarget(Transform t)
        {
            if (_ai == null) return;
            try
            {
                if (!t) { _clearTarget?.Invoke(_ai, null); return; }
                if (_setTarget == null) return;
                var ps = _setTarget.GetParameters();
                if (ps.Length == 2) _setTarget.Invoke(_ai, new object[] { t, true });
                else _setTarget.Invoke(_ai, new object[] { t });
            }
            catch { /* vendor API shape can vary */ }
        }

        void MalbersDest(Vector3 p)
        {
            if (_ai == null || _setDest == null) return;
            try
            {
                var ps = _setDest.GetParameters();
                if (ps.Length == 2) _setDest.Invoke(_ai, new object[] { p, true });
                else _setDest.Invoke(_ai, new object[] { p });
            }
            catch { }
        }

        void MalbersDamage(float amount, Transform sender)
        {
            if (_receiveDmg == null || _dmg == null) return;
            try { _receiveDmg.Invoke(_dmg, new object[] { null, amount }); } catch { }
        }

        static Type FindType(string full, string asm)
        {
            var t = Type.GetType(full + ", " + asm, false);
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != asm) continue;
                t = a.GetType(full, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
