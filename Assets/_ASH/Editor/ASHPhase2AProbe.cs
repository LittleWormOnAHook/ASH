using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.EditorTools
{
    /// <summary>Read-only audit of the open scene for Phase 2A (cabin, spawn, props, lights, volumes, weather systems). Changes nothing.</summary>
    public static class ASHPhase2AProbe
    {
        public static void Run()
        {
            var sb = new StringBuilder("[ASH] P2A PROBE\n");
            var spawn = GameObject.Find("ASH_PlayerSpawn");
            sb.Append($"spawn={(spawn ? spawn.transform.position.ToString("F2") + " rotY " + spawn.transform.eulerAngles.y.ToString("F0") : "none")}\n");
            var player = Object.FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>();
            sb.Append($"player={(player ? player.name + " " + player.transform.position.ToString("F2") : "none")}\n");
            Vector3 c = spawn ? spawn.transform.position : player ? player.transform.position : Vector3.zero;
            foreach (var n in new[] { "Grandpa", "Cabin", "cabin", "Radio", "radio" })
                foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t => t.name.Contains(n) && t.parent == null || (t.name.Contains(n) && t.name.Length < 40)).Take(12))
                    sb.Append($"  name~{n}: {Path(go)} pos {go.position:F1}\n");
            sb.Append("-- renderers within 9 m of spawn (top-level groups):\n");
            var near = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude).Where(r => r.bounds.SqrDistance(c) < 81f && !(r is ParticleSystemRenderer) && !r.transform.IsChildOf(player ? player.transform : null)).ToList();
            foreach (var r in near.Take(160))
            {
                var rb = r.GetComponentInParent<Rigidbody>(); var col = r.GetComponentInParent<Collider>();
                sb.Append($"  {Path(r.transform)} size {r.bounds.size:F2} at {r.bounds.center:F2} static={r.gameObject.isStatic} rb={(rb ? (rb.isKinematic ? "kin" : "dyn") : "-")} col={(col ? col.GetType().Name : "-")}\n");
            }
            sb.Append($"   ({near.Count} renderers total)\n");
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include)) sb.Append($"light {Path(l.transform)} {l.type} int {l.intensity:F2} on={l.isActiveAndEnabled} dist {Vector3.Distance(l.transform.position, c):F0}\n");
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include)) sb.Append($"volume {Path(v.transform)} global={v.isGlobal} prio {v.priority} w {v.weight} profile {(v.sharedProfile ? AssetDatabase.GetAssetPath(v.sharedProfile) + " [" + string.Join(",", v.sharedProfile.components.Select(x => x.GetType().Name + (x.active ? "" : "(off)"))) + "]" : "none")}\n");
            sb.Append($"RenderSettings fog={RenderSettings.fog} mode={RenderSettings.fogMode} dens={RenderSettings.fogDensity} start={RenderSettings.fogStartDistance} end={RenderSettings.fogEndDistance} sky={(RenderSettings.skybox ? RenderSettings.skybox.name + " / " + RenderSettings.skybox.shader.name : "none")} sun={(RenderSettings.sun ? RenderSettings.sun.name : "none")}\n");
            foreach (var t in new[] { "ASH.World.ASHDayNightClock", "ASH.World.ASHAshFall", "ASH.World.Accumulation.ASHAccumulationController", "ASH.Gameplay.ASHObjectiveTracker", "ASH.UI.ASHHud", "ASH.Interaction.ASHInteractor", "ASH.PostFX.ASHPostProcessing", "ASH.Map.ASHJournalLog", "ASH.Rendering.ASHFogArea", "ASH.Interaction.ASHWaterSource", "ASH.Interaction.ASHItemPickup" })
            {
                var ty = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(t)).FirstOrDefault(x => x != null);
                if (ty == null) { sb.Append($"type {t} missing\n"); continue; }
                var all = Object.FindObjectsByType(ty, FindObjectsInactive.Include);
                sb.Append($"{ty.Name} x{all.Length}: {string.Join("; ", all.Take(6).Select(o => Path(((Component)o).transform) + " d=" + Vector3.Distance(((Component)o).transform.position, c).ToString("F0")))}\n");
            }
            var cam = Camera.main; sb.Append($"Camera.main={(cam ? Path(cam.transform) : "none")} comps [{(cam ? string.Join(",", cam.GetComponents<Component>().Select(x => x.GetType().Name)) : "")}]\n");
            var clock = Object.FindAnyObjectByType<ASH.World.ASHDayNightClock>(); if (clock) sb.Append($"clock time {clock.timeOfDay} mpd {clock.minutesPerDay} haze {clock.ashHaze} driveFog {clock.driveFog}\n");
            var ash = Object.FindAnyObjectByType<ASH.World.ASHAshFall>(); if (ash) sb.Append($"ashfall rate {ash.particlesPerSecond} max {ash.maxParticles} active {ash.isActiveAndEnabled}\n");
            var acc = ASH.World.Accumulation.ASHAccumulationController.Instance ?? Object.FindAnyObjectByType<ASH.World.Accumulation.ASHAccumulationController>(); if (acc) sb.Append($"accum ash {acc.ashAmount} snow {acc.snowAmount} rate {acc.accumulationRate} src {(acc.weatherSource ? acc.weatherSource.name : "-")}\n");
            Debug.Log(sb.ToString());
            System.IO.File.WriteAllText("AshBridge/p2a_probe.txt", sb.ToString());
        }
        static string Path(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}
