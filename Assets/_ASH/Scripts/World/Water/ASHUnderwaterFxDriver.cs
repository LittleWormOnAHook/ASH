using System;
using System.Reflection;
using UnityEngine;

namespace ASH.World.Water
{
    /// <summary>Applies ASHWaterFxSettings.underwaterEffects to Gaia's GaiaUnderwaterEffects at runtime (reflection, so Gaia stays optional).
    /// Nothing is deleted and the scene is not changed: turn underwaterEffects back on (even in Play) to restore it.</summary>
    public class ASHUnderwaterFxDriver : MonoBehaviour
    {
        static Type _gt;
        float _next; bool? _applied; Behaviour _comp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<ASHUnderwaterFxDriver>()) return;
            var go = new GameObject("ASH_UnderwaterFxDriver") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go); go.AddComponent<ASHUnderwaterFxDriver>();
        }

        static Type GaiaType()
        {
            if (_gt != null) return _gt;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType("Gaia.GaiaUnderwaterEffects"); if (t != null) return _gt = t; }
            return null;
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            var s = ASHWaterFxSettings.I; _next = Time.unscaledTime + s.recheckInterval;
            var t = GaiaType(); if (t == null) return;
            if (!_comp) { _comp = FindAnyObjectByType(t, FindObjectsInactive.Include) as Behaviour; _applied = null; }
            if (!_comp) return;
            bool want = s.underwaterEffects;
            if (_applied == want && (want || _comp.enabled == !s.disableComponentToo)) return;
            if (want) _comp.enabled = true;
            try { t.GetProperty("EnableUnderwaterEffects", BindingFlags.Public | BindingFlags.Instance)?.SetValue(_comp, want); } catch { }
            if (!want && s.disableComponentToo) _comp.enabled = false;
            _applied = want;
        }
    }
}
