using System;
using System.Reflection;
using UnityEngine;

namespace ASH.Core
{
    /// <summary>
    /// Domain reload is off on Enter Play Mode, so vendor statics survive between Play sessions. Most vendor registries clean
    /// themselves up in OnDisable; these two don't, so they're cleared here by reflection (no vendor edits, compiles without the packs):
    /// Malbers MAnimal.Animals (added in OnEnable, never removed) and Invector vMessageReceiver.onReceiveGlobalMessage (subscribed in Start).
    /// </summary>
    static class ASHVendorStaticReset
    {
        static readonly (string type, string field)[] Targets =
        {
            ("MalbersAnimations.Controller.MAnimal", "Animals"),
            ("Invector.vMessageReceiver", "onReceiveGlobalMessage"),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetVendorStatics()
        {
            foreach (var (typeName, fieldName) in Targets)
            {
                try
                {
                    var t = Find(typeName);
                    var f = t?.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null && !f.IsInitOnly && !f.FieldType.IsValueType) f.SetValue(null, null);
                }
                catch (Exception e) { Debug.LogWarning($"[ASH] Vendor static reset {typeName}.{fieldName}: {e.Message}"); }
            }
        }

        static Type Find(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
