using System.Collections.Generic;
using ASH.Interaction;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Combat
{
    /// <summary>On the visual of ASH_ArrowProjectile (Invector vArrow detach object). Once the arrow sticks (vArrow moves it under
    /// an "ArrowParent"), it may become a 1x Wooden Arrow pickup (ASHCombatTuning.arrowRecoverChance). Oldest stuck arrows are
    /// removed past the cap, and each one expires after stuckArrowLifetime.</summary>
    public sealed class ASHArrowRecover : MonoBehaviour
    {
        public ASHItemDefinition arrowItem;
        bool _stuck; float _stuckAt;
        static readonly Queue<GameObject> _stuckList = new Queue<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _stuckList.Clear();

        void Update()
        {
            if (_stuck) { if (Time.time - _stuckAt > ASHCombatTuning.Live.stuckArrowLifetime) Destroy(gameObject); return; }
            var p = transform.parent; if (!p || p.name != "ArrowParent") return;
            _stuck = true; _stuckAt = Time.time;
            var t = ASHCombatTuning.Live;
            _stuckList.Enqueue(gameObject);
            while (_stuckList.Count > Mathf.Max(1, t.maxStuckArrows)) { var old = _stuckList.Dequeue(); if (old && old != gameObject) Destroy(old); }
            if (!arrowItem || Random.value > t.arrowRecoverChance) return;
            var box = gameObject.AddComponent<BoxCollider>(); box.isTrigger = true;
            var rs = GetComponentsInChildren<Renderer>(); if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                box.center = transform.InverseTransformPoint(b.center);
                var s = transform.lossyScale; box.size = new Vector3(Mathf.Max(0.12f, b.size.x / Mathf.Abs(s.x)), Mathf.Max(0.12f, b.size.y / Mathf.Abs(s.y)), Mathf.Max(0.12f, b.size.z / Mathf.Abs(s.z)));
            }
            var pk = gameObject.AddComponent<ASHItemPickup>(); pk.Configure(arrowItem, 1);
        }
    }
}
