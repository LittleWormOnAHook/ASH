using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Combat
{
    /// <summary>Lit-torch positions and scare pulses. Wildlife (wolves) subscribe; nothing else has to know about Malbers.</summary>
    public static class ASHTorchPresence
    {
        public static event Action<Vector3, float, float> Scared; // world position, radius, duration seconds

        static readonly List<Transform> _lit = new List<Transform>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _lit.Clear(); Scared = null; }

        public static void SetLit(Transform torch, bool lit)
        {
            if (!torch) return;
            if (lit) { if (!_lit.Contains(torch)) _lit.Add(torch); }
            else _lit.Remove(torch);
        }

        public static bool AnyLit => _lit.Count > 0;

        public static bool IsLitNear(Vector3 pos, float radius, out Vector3 torchPos)
        {
            float r2 = radius * radius;
            torchPos = pos;
            bool found = false;
            float best = float.PositiveInfinity;
            for (int i = _lit.Count - 1; i >= 0; i--)
            {
                var t = _lit[i];
                if (!t) { _lit.RemoveAt(i); continue; }
                float d = (t.position - pos).sqrMagnitude;
                if (d <= r2 && d < best) { best = d; torchPos = t.position; found = true; }
            }
            return found;
        }

        public static void Scare(Vector3 pos, float radius, float seconds)
        {
            Scared?.Invoke(pos, radius, seconds);
        }
    }
}
