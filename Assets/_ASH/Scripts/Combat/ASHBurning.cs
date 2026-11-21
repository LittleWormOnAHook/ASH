using Invector;
using UnityEngine;

namespace ASH.Combat
{
    /// <summary>Short fire DOT from a lit torch strike. Applies Invector damage so future animal adapters receive it.</summary>
    public sealed class ASHBurning : MonoBehaviour
    {
        float _left, _dps, _tick, _acc;
        Transform _sender;
        vIDamageReceiver _recv;

        public static void Apply(Component target, Transform sender, float seconds, float dps, float tick)
        {
            if (!target || seconds <= 0f || dps <= 0f) return;
            var rec = target.GetComponentInParent<vIDamageReceiver>();
            if (rec == null) rec = target.GetComponentInChildren<vIDamageReceiver>();
            if (rec == null) return;
            var host = rec as Component;
            var go = host ? host.gameObject : target.gameObject;
            var b = go.GetComponent<ASHBurning>();
            if (!b) b = go.AddComponent<ASHBurning>();
            b._recv = rec;
            b._sender = sender;
            b._left = Mathf.Max(b._left, seconds);
            b._dps = dps;
            b._tick = Mathf.Max(0.1f, tick);
            b._acc = 0f;
            b.enabled = true;
        }

        void Update()
        {
            if (_left <= 0f || _recv == null) { enabled = false; return; }
            float dt = Time.deltaTime;
            _left -= dt;
            _acc += dt;
            if (_acc < _tick) return;
            float window = _acc;
            _acc = 0f;
            int amount = Mathf.Max(1, Mathf.RoundToInt(_dps * window));
            var dmg = new vDamage(amount)
            {
                sender = _sender,
                hitPosition = transform.position,
                damageType = "ASH_Burn",
                hitReaction = false
            };
            _recv.TakeDamage(dmg);
            if (_left <= 0f) enabled = false;
        }
    }
}
