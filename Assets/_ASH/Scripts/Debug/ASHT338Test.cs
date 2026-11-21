using System.Collections;
using System.Text;
using ASH.Companion;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>t338/t341 dog test (exec ASH.DebugTools.ASHT338Test.Run): player idle; dog idle brain for 45 s
    /// (turn-in-place fraction, activity changes, shortest activity), growl clips, forced dig with a find and a playful growl. Logs "[ASH] DOGI ...".</summary>
    public sealed class ASHT338Test : MonoBehaviour
    {
        public static float Seconds = 45f;
        static bool _peeOnly; public static void RunPeeOnly() { _peeOnly = true; Run(); }
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_T338Test").AddComponent<ASHT338Test>(); }
        readonly StringBuilder _sum = new StringBuilder();
        void Log(string l) { _sum.AppendLine(l); Debug.Log("[ASH] DOGI " + l); }

        IEnumerator Start()
        {
            yield return null;
            var dog = FindAnyObjectByType<ASHDogCompanion>();
            if (!dog) { Log("no dog"); Finish(); yield break; }
            dog.SetMode(ASHDogMode.Follow);
            var S = ASHCompanionSettings.I;
            if (_peeOnly) { _peeOnly = false; yield return new WaitForSeconds(4f); yield return Pee(dog); Finish(); yield break; }
            Log($"growl clips: warning {S.growlClips?.Length ?? 0}, playful {S.playGrowlClips?.Length ?? 0}; footstep volume {S.pawStepVolume}");
            int g0 = dog.GrowlCount; bool w = dog.TestGrowl(false); yield return new WaitForSeconds(3.5f); bool p = dog.TestGrowl(true);
            Log($"growl warning played {w}, playful played {p}, count +{dog.GrowlCount - g0}");
            yield return new WaitForSeconds(2f);

            // idle brain
            float t = 0f, turn = 0f, minAct = 999f, actStart = Time.time; int changes = 0; string last = dog.IdleActivity; var seen = new StringBuilder(last);
            while (t < Seconds)
            {
                yield return null; t += Time.deltaTime;
                if (dog.TurningInPlace) turn += Time.deltaTime;
                var a = dog.IdleActivity;
                if (a != last) { float d = Time.time - actStart; if (last != "Settle" && last != "None") minAct = Mathf.Min(minAct, d); changes++; seen.Append(" > ").Append(a); last = a; actStart = Time.time; }
            }
            Log($"idle {Seconds:F0}s: turning in place {turn / Seconds * 100f:F1}% of the time, {changes} activity changes, shortest activity {(minAct > 900f ? -1f : minAct):F1}s");
            Log("sequence: " + seen);

            // forced dig with a find + playful growl
            ASHDogCompanion.ForceDigFindChance = 1f; ASHDogCompanion.ForceDigGrowlChance = 1f; ASHDogCompanion.ForceNextAct = "Dig";
            int f0 = dog.DigFinds, gg = dog.GrowlCount; float until = Time.time + 40f; bool dug = false;
            while (Time.time < until && dog.DigFinds == f0) { if (dog.IdleActivity == "Dig") dug = true; if (!dug && dog.IdleActivity != "Dig" && dog.IdleActivity != "Settle") ASHDogCompanion.ForceNextAct = "Dig"; yield return null; }
            Log($"forced dig: dug {dug}, finds +{dog.DigFinds - f0}, growls during dig +{dog.GrowlCount - gg}");
            ASHDogCompanion.ForceDigFindChance = -1f; ASHDogCompanion.ForceDigGrowlChance = -1f;

            yield return Pee(dog);
            Finish();
        }

        IEnumerator Pee(ASHDogCompanion dog)
        {
            int p0 = dog.Pees; float until = Time.time + 45f; bool peeing = false; string acts = "", lastA = "";
            ASHDogCompanion.ForceNextAct = "Pee";
            while (Time.time < until && dog.Pees == p0)
            {
                var a = dog.IdleActivity; if (a == "Pee") peeing = true;
                if (!peeing && a != "Pee" && a != "Settle") ASHDogCompanion.ForceNextAct = "Pee";
                if (dog.Activity != lastA)
                {
                    lastA = dog.Activity;
                    var pl = GameObject.FindWithTag("Player"); float dP = pl ? Vector3.Distance(pl.transform.position, dog.transform.position) : -1f;
                    acts += $"{dog.Activity}(dP {dP:F1} idle {dog.PlayerIdleSeconds:F1} tgt {(pl ? Vector3.Distance(pl.transform.position, dog.PeeTargetPos) : -1f):F1} spot {Vector3.Distance(dog.transform.position, dog.ActSpot):F1})|";
                }
                yield return null;
            }
            Log($"forced pee: started {peeing}, done +{dog.Pees - p0}, target '{dog.LastPeeTarget}', lifted leg right {ASHDogCompanion.LearnedLegRight}; activities {acts}");
            float u2 = Time.time + 3f; while (Time.time < u2) yield return null;
            int spots = 0; foreach (var g in FindObjectsByType<Transform>()) if (g.name == "ASH_DogPeeSpot") spots++;
            Log($"pee spots on the ground: {spots}");
        }

        void Finish() { ASHDogCompanion.ForceDigFindChance = -1f; ASHDogCompanion.ForceDigGrowlChance = -1f; ASHDogCompanion.ForceNextAct = null; Debug.Log("[ASH] DOGI SUMMARY\n" + _sum); Destroy(gameObject); }
    }
}
