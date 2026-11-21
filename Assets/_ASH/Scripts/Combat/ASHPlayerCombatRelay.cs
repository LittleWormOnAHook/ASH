using ASH.Companion;
using ASH.Wildlife;
using Invector;
using UnityEngine;

namespace ASH.Combat
{
    /// <summary>When the player is hit, tells the dog (Follow/Defend only) who did it. Added to the official player prefab by the wildlife menu.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHPlayerCombatRelay : MonoBehaviour
    {
        vIDamageReceiver _recv;
        bool _hooked;

        void OnEnable() => Hook();
        void Start() => Hook();

        void Hook()
        {
            if (_hooked) return;
            _recv = GetComponentInParent<vIDamageReceiver>() ?? GetComponentInChildren<vIDamageReceiver>();
            if (_recv == null) return;
            _recv.onReceiveDamage.AddListener(OnHurt);
            _hooked = true;
        }

        void OnDisable()
        {
            if (_hooked && _recv != null) _recv.onReceiveDamage.RemoveListener(OnHurt);
            _hooked = false;
        }

        void OnHurt(vDamage damage)
        {
            if (damage == null || !damage.sender) return;
            var dog = ASHDogCompanion.Active;
            if (!dog) return;
            var animal = damage.sender.GetComponentInParent<ASHAnimal>();
            dog.NotifyPlayerCombat(animal ? animal.transform : damage.sender, true);
        }

        /// <summary>Runtime: attach to the active player without requiring a prefab save.</summary>
        public static void EnsureOn(Transform player)
        {
            if (!player) return;
            var root = player;
            var hc = player.GetComponentInParent<vIDamageReceiver>();
            if (hc is Component c && c.transform) root = c.transform;
            if (!root.GetComponent<ASHPlayerCombatRelay>()) root.gameObject.AddComponent<ASHPlayerCombatRelay>();
        }
    }
}
