using ASH.Inventory;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>Drops items into the world as pickups: the item's worldPickupPrefab when set, otherwise a simple pickup built from
    /// the held visual (or a small tinted box). Placed on the ground in front of the dropper.</summary>
    public static class ASHItemDrop
    {
        public const float DropDistance = 1.1f;

        /// <summary>Spawns a pickup holding amount of item on the ground in front of who. Returns the pickup (null when nothing to drop).</summary>
        public static ASHItemPickup Spawn(ASHItemDefinition item, int amount, Transform who)
        {
            if (item == null || amount <= 0 || who == null) return null;
            var fwd = Vector3.ProjectOnPlane(who.forward, Vector3.up); if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            var pos = GroundPoint(who.position + fwd * DropDistance, who);
            var rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f);
            GameObject go;
            if (item.worldPickupPrefab) go = Object.Instantiate(item.worldPickupPrefab, pos, rot);
            else go = BuildFallback(item, pos, rot);
            go.name = (item.worldPickupPrefab ? item.worldPickupPrefab.name : "ASH_Pickup_" + item.itemId) + "_Dropped";
            var pk = go.GetComponent<ASHItemPickup>(); if (!pk) pk = go.AddComponent<ASHItemPickup>();
            pk.Configure(item, amount);
            return pk;
        }

        /// <summary>First solid (non-trigger) surface under p that is not part of who, else p at who's height.</summary>
        public static Vector3 GroundPoint(Vector3 p, Transform who)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 1.6f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Vector3 at = new Vector3(p.x, who ? who.position.y : p.y, p.z);
            foreach (var h in hits)
            {
                if (who && h.collider.transform.IsChildOf(who.root)) continue;
                if (h.distance < best) { best = h.distance; at = h.point; }
            }
            return at;
        }

        static GameObject BuildFallback(ASHItemDefinition item, Vector3 pos, Quaternion rot)
        {
            var root = new GameObject("ASH_Pickup_" + item.itemId);
            root.transform.SetPositionAndRotation(pos, rot);
            GameObject vis = null;
            if (item.heldPrefab)
            {
                vis = Object.Instantiate(item.heldPrefab, root.transform, false);
                foreach (var l in vis.GetComponentsInChildren<Light>(true)) l.enabled = false;
                foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
                foreach (var mb in vis.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
                vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * vis.transform.localRotation;   // lie it down
            }
            else
            {
                vis = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(vis.GetComponent<Collider>());
                vis.transform.SetParent(root.transform, false); vis.transform.localScale = new Vector3(0.22f, 0.12f, 0.22f);
                var r = vis.GetComponent<Renderer>(); if (r) r.material.color = item.uiTint;
            }
            vis.name = "Visual";
            // sit the visual on the ground and fit a trigger around it
            var rs = vis.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                vis.transform.position += Vector3.up * (pos.y - b.min.y + 0.01f);
                b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                var col = root.AddComponent<BoxCollider>(); col.isTrigger = true;
                var c = root.transform.InverseTransformPoint(b.center); var s = root.transform.InverseTransformVector(b.size);
                col.center = c; col.size = new Vector3(Mathf.Max(Mathf.Abs(s.x), 0.25f) + 0.12f, Mathf.Max(Mathf.Abs(s.y), 0.25f), Mathf.Max(Mathf.Abs(s.z), 0.25f) + 0.12f);
            }
            else { var col = root.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = Vector3.one * 0.35f; col.center = Vector3.up * 0.17f; }
            int layer = LayerMask.NameToLayer("Resource");
            if (layer >= 0) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            return root;
        }
    }
}
