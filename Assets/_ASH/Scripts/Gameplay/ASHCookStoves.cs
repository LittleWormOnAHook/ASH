using System.Collections.Generic;
using ASH.Survival;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Turns stoves / furnaces in the scene into cook stations (ASHCampfire + stove warmth + a small fire light),
    /// idempotent, no scene changes. Called by ASHPlacedSave.Snapshot (so they are saved like campfires) and after scene load.</summary>
    public static class ASHCookStoves
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void AfterLoad() => EnsureAll();

        public static int EnsureAll()
        {
            var pats = ASHCookingSettings.I.stoveNamePatterns;
            if (pats == null || pats.Length == 0) return 0;
            int n = 0;
            var seen = new HashSet<Transform>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                var t = TopMatch(r.transform, pats);
                if (!t || !seen.Add(t)) continue;
                if (t.GetComponentInParent<ASHCampfire>() || t.GetComponentInChildren<ASHCampfire>(true)) continue;
                if (t.GetComponentInParent<ASHPlacedObject>() || t.name == "ASH_PlacementGhost" || t.gameObject.scene.name == "DontDestroyOnLoad") continue;
                if (Convert(t.gameObject, NiceName(t.name), false, 0f)) n++;
            }
            if (n > 0) ASH.Core.ASHLog.Info($"Cook stoves: {n} stove(s) became cook stations");
            return n;
        }

        static Transform TopMatch(Transform t, string[] pats)
        {
            Transform best = null;
            for (var p = t; p; p = p.parent) if (Matches(p.name, pats)) best = p;
            return best;
        }

        static bool Matches(string name, string[] pats)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            foreach (var p in pats) if (!string.IsNullOrEmpty(p) && n.Contains(p.ToLowerInvariant())) return true;
            return false;
        }

        static string NiceName(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("furnace")) return "Furnace";
            if (n.Contains("fireplace")) return "Fireplace";
            return "Stove";
        }

        /// <summary>Adds the cook station parts to one object (no-op when it already has an ASHCampfire).</summary>
        public static bool Convert(GameObject go, string name, bool startLit, float startFuel)
        {
            if (!go || go.GetComponent<ASHCampfire>()) return false;
            var b = Bounds(go);
            if (!go.GetComponentInChildren<Collider>())
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = go.transform.InverseTransformPoint(b.center);
                var ls = go.transform.lossyScale; bc.size = new Vector3(b.size.x / Mathf.Max(0.001f, Mathf.Abs(ls.x)), b.size.y / Mathf.Max(0.001f, Mathf.Abs(ls.y)), b.size.z / Mathf.Max(0.001f, Mathf.Abs(ls.z)));
            }
            var lgo = new GameObject("ASH_StoveFireLight"); lgo.transform.SetParent(go.transform, false);
            lgo.transform.position = new Vector3(b.center.x, b.min.y + b.size.y * 0.35f, b.center.z);
            var l = lgo.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.22f); l.range = 4f; l.intensity = 1.6f; l.shadows = LightShadows.None; l.enabled = false;
            var fire = go.AddComponent<ASHCampfire>();
            fire.ConfigureStation(name, l, startLit, startFuel);
            if (!go.GetComponent<ASHHeatSource>()) go.AddComponent<ASHHeatSource>().ApplyPreset(ASHHeatSource.Preset.Stove);
            return true;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position + Vector3.up * 0.5f, Vector3.one);
            var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}
