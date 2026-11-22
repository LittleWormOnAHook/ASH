using System.Collections.Generic;
using UnityEngine;

namespace ASH.Inventory
{
    /// <summary>Every item definition by itemId, for save files. Resources/ASH_ItemRegistry, refreshed by ASH/Systems/Install Data
    /// (re-run it after adding items). Falls back to items reachable from the player's pack and recipes.</summary>
    [CreateAssetMenu(menuName = "ASH/Inventory/Item Registry", fileName = "ASH_ItemRegistry")]
    public sealed class ASHItemRegistry : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHItemRegistry()
        {
            _i = default;
        }

        public List<ASHItemDefinition> items = new List<ASHItemDefinition>();
        Dictionary<string, ASHItemDefinition> _map;

        static ASHItemRegistry _i;
        public static ASHItemRegistry I { get { if (!_i) { _i = Resources.Load<ASHItemRegistry>("ASH_ItemRegistry"); if (!_i) _i = CreateInstance<ASHItemRegistry>(); } return _i; } }

        public ASHItemDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_map == null) { _map = new Dictionary<string, ASHItemDefinition>(); foreach (var it in items) if (it && !_map.ContainsKey(it.itemId)) _map[it.itemId] = it; }
            if (_map.TryGetValue(id, out var r) && r) return r;
            // fallback: anything the scene references
            var craft = FindAnyObjectByType<ASHCrafting>();
            if (craft) foreach (var rc in craft.Recipes) { if (!rc) continue; if (rc.outputItem && rc.outputItem.itemId == id) return Cache(rc.outputItem); foreach (var g in rc.ingredients) if (g.item && g.item.itemId == id) return Cache(g.item); }
            foreach (var res in Resources.FindObjectsOfTypeAll<ASHItemDefinition>()) if (res.itemId == id) return Cache(res);
            return null;
        }
        ASHItemDefinition Cache(ASHItemDefinition it) { _map[it.itemId] = it; return it; }
    }
}
