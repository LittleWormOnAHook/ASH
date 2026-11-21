using Invector;
using UnityEngine;

namespace ASH.Wildlife
{
    /// <summary>
    /// Forwards Invector melee / projectile / torch hits from a child collider to the parent ASHAnimal.
    /// Invector only looks for vIDamageReceiver on the collider's own GameObject, not parents.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHWildlifeHitbox : MonoBehaviour, vIDamageReceiver
    {
        [SerializeField] ASHAnimal animal;
        [SerializeField] bool vital;
        readonly OnReceiveDamage _onStart = new OnReceiveDamage();
        readonly OnReceiveDamage _onRecv = new OnReceiveDamage();
        public OnReceiveDamage onStartReceiveDamage => _onStart;
        public OnReceiveDamage onReceiveDamage => _onRecv;

        public void Bind(ASHAnimal a)
        {
            animal = a;
            if (!vital)
            {
                string n = (name + " " + (transform.parent ? transform.parent.name : "")).ToLowerInvariant();
                vital = n.Contains("head") || n.Contains("neck") || n.Contains("skull") || n.Contains("jaw");
            }
        }

        void Awake()
        {
            if (!animal) animal = GetComponentInParent<ASHAnimal>();
            Bind(animal);
        }

        public void TakeDamage(vDamage damage)
        {
            if (animal == null) animal = GetComponentInParent<ASHAnimal>();
            if (animal == null || damage == null) return;
            _onStart.Invoke(damage);
            if (vital)
            {
                var d = new vDamage(damage);
                d.damageType = string.IsNullOrEmpty(d.damageType) ? "vital" : d.damageType + "|vital";
                animal.TakeDamage(d);
                _onRecv.Invoke(d);
            }
            else
            {
                animal.TakeDamage(damage);
                _onRecv.Invoke(damage);
            }
        }
    }
}
