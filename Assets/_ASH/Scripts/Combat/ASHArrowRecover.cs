using System.Collections.Generic;
using ASH.Interaction;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Combat
{
    /// <summary>On the visual of ASH_ArrowProjectile. When the arrow hits something, ASHCombatBridge parents this visual to the hit
    /// collider's transform (it moves with it) and calls <see cref="Stick"/>: unless it breaks (ASHCombatTuning.arrowBreakChance,
    /// default 0) it becomes a 1x arrow pickup (interact). Oldest stuck arrows are removed past maxStuckArrows; each one is
    /// cleaned up after stuckArrowSeconds. Fallback: an arrow moved under an Invector "ArrowParent" sticks the same way.</summary>
    public sealed class ASHArrowRecover : MonoBehaviour
    {
        public ASHItemDefinition arrowItem;
        bool _stuck; float _stuckAt;
        static readonly Queue<GameObject> _stuckList = new Queue<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _stuckList.Clear();

        public bool Stuck => _stuck;

        public void Stick(ASHItemDefinition item)
        {
            if (_stuck) return;
            _stuck = true; _stuckAt = Time.time; if (item) arrowItem = item;
            var t = ASHCombatTuning.Live;
            _stuckList.Enqueue(gameObject);
            while (_stuckList.Count > Mathf.Max(1, t.maxStuckArrows)) { var old = _stuckList.Dequeue(); if (old && old != gameObject) Destroy(old); }
            foreach (var c in GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in GetComponentsInChildren<Rigidbody>(true)) Destroy(r);
            if (!arrowItem || (t.arrowBreakChance > 0f && Random.value < t.arrowBreakChance)) return;
            // trigger box around the arrow in its own space (a little fat so it is easy to look at)
            var box = gameObject.AddComponent<BoxCollider>(); box.isTrigger = true;
            bool has = false; var b = new Bounds();
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!has) { b = new Bounds(p, Vector3.zero); has = true; } else b.Encapsulate(p);
                }
            }
            if (!has) b = new Bounds(new Vector3(0f, 0f, -0.35f), new Vector3(0.05f, 0.05f, 0.7f));
            var s = transform.lossyScale; float sx = Mathf.Max(1e-4f, Mathf.Abs(s.x)), sy = Mathf.Max(1e-4f, Mathf.Abs(s.y)), sz = Mathf.Max(1e-4f, Mathf.Abs(s.z));
            box.center = b.center;
            box.size = new Vector3(Mathf.Max(b.size.x, 0.25f / sx), Mathf.Max(b.size.y, 0.25f / sy), Mathf.Max(b.size.z, 0.3f / sz));
            var pk = gameObject.AddComponent<ASHItemPickup>(); pk.Configure(arrowItem, 1);
        }

        void Update()
        {
            if (_stuck) { if (Time.time - _stuckAt > ASHCombatTuning.Live.stuckArrowSeconds) Destroy(gameObject); return; }
            var p = transform.parent; if (p && p.name == "ArrowParent") Stick(arrowItem);
        }
    }
}
