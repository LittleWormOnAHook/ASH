using System.Collections;
using System.Text;
using ASH.Gameplay;
using ASH.Opening;
using ASH.Player;
using ASH.World;
using ASH.World.Accumulation;
using ASH.World.Weather;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>
    /// Phase 2A smoke test (Play Mode, via the bridge: exec ASH.DebugTools.ASHPhase2ATest.Run). Walks the opening with
    /// shortened timings (runtime only), checks every stage, cycles the six weather states for smoothness, forces a
    /// lightning strike, then suppresses the opening so ASHPlayTest can run from the scene spawn. "[ASH] TEST ..." lines.
    /// </summary>
    public sealed class ASHPhase2ATest : MonoBehaviour
    {
        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] TEST requires Play Mode"); return; }
            new GameObject("ASH_Phase2ATest").AddComponent<ASHPhase2ATest>();
        }

        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            string line = $"TEST {(ok ? "PASS" : "FAIL")} P2A {name}: {detail}";
            _sum.AppendLine(line); Debug.Log("[ASH] " + line);
        }

        IEnumerator Start()
        {
            var seq = FindAnyObjectByType<ASHEruptionSequence>(); var dir = ASHWeatherDirector.Instance; var outp = FindAnyObjectByType<ASHWeatherOutputs>();
            var radio = FindAnyObjectByType<ASHRadioController>(); var quake = FindAnyObjectByType<ASHEarthquakeEffects>(); var objv = FindAnyObjectByType<ASHOpeningObjective>();
            var hud = FindAnyObjectByType<UI.ASHHud>(); var acc = ASHAccumulationController.Instance; var cc = FindAnyObjectByType<vThirdPersonController>(); var input = FindAnyObjectByType<ASHInput>();
            var tracker = FindAnyObjectByType<ASHObjectiveTracker>(); var ash = FindAnyObjectByType<ASHAshFall>();
            Check("components", seq && dir && outp && radio && quake && objv && hud && cc && input && seq.profile,
                $"seq={(bool)seq} director={(bool)dir} outputs={(bool)outp} radio={(bool)radio} quake={(bool)quake} objective={(bool)objv} hud={(bool)hud} acc={(bool)acc} player={(bool)cc} profile={(seq && seq.profile)}");
            if (_fail > 0) { Finish(seq); yield break; }
            var tr = cc.transform;

            // ---- 1 morning
            if (seq.Current == ASHEruptionSequence.Stage.Inactive) seq.Begin();
            yield return new WaitForSeconds(1.5f);
            bool inside = objv.interior && objv.interior.bounds.Contains(tr.position + Vector3.up * 0.5f);
            Check("morning", seq.Current == ASHEruptionSequence.Stage.Morning && inside && radio.Current == ASHRadioController.Mode.Music,
                $"stage={seq.Current} playerInside={inside} pos={tr.position:F1} radio={radio.Current} musicPlaying={radio.Playing} vol={radio.MusicVolume:F2} weather={dir.CurrentKind}");
            Check("indoor detection", outp.Indoor01 > 0.5f, $"indoor={outp.Indoor01:F2}");

            // ---- 2 broadcast (real-time radio timings) + player movement
            seq.SkipTo(ASHEruptionSequence.Stage.Broadcast);
            bool sawAlert = false; float t0 = Time.time; Vector3 p0 = tr.position; float moved = 0f;
            input.SimMove = new Vector2(0f, 0.6f);
            while (Time.time - t0 < seq.profile.musicFadeSeconds + seq.profile.alertSeconds + 3.5f)
            {
                if (radio.Current == ASHRadioController.Mode.Alert && radio.Playing) sawAlert = true;
                if (Time.time - t0 > 1.2f && input.SimMove != Vector2.zero) { moved = Flat(tr.position - p0); input.SimMove = Vector2.zero; }
                yield return null;
            }
            Check("broadcast", sawAlert && radio.Current == ASHRadioController.Mode.Voice && radio.SubtitlesShown >= 2 && !string.IsNullOrEmpty(hud.SubtitleText),
                $"alert={sawAlert} mode={radio.Current} line={radio.LineIndex} subtitles={radio.SubtitlesShown} hud='{Short(hud.SubtitleText)}'");
            Check("control during broadcast", moved > 0.3f, $"moved {moved:F2} m in 1.2 s");

            // ---- 3 quake (shortened)
            quake.duration = 8f; ASHQuakeProp.ResetAll();
            seq.SkipTo(ASHEruptionSequence.Stage.Quake);
            float maxShake = 0f, endI = 1f; int falls = 0; p0 = tr.position; input.SimMove = new Vector2(0f, -0.6f); float movedQ = 0f; t0 = Time.time;
            while (Time.time - t0 < 9.5f)
            {
                maxShake = Mathf.Max(maxShake, ASHCameraKick.ShakeRotation.magnitude);
                if (Time.time - t0 > 3f && input.SimMove != Vector2.zero) { movedQ = Flat(tr.position - p0); input.SimMove = Vector2.zero; }
                yield return null;
            }
            endI = quake.Intensity; foreach (var p in ASHQuakeProp.All) if (p.Fallen) falls++;
            Check("quake curve", quake.Peak > 0.8f && endI < 0.01f && quake.Done, $"peak={quake.Peak:F2} end={endI:F2} done={quake.Done}");
            Check("quake shake+sound+props", maxShake > 0.3f && quake.Clatters > 0 && falls > 0 && ASHCameraKick.ShakeRotation == Vector3.zero, $"maxShake={maxShake:F2}deg clatters={quake.Clatters} fallen={falls}/{ASHQuakeProp.All.Count} shakeAfter={ASHCameraKick.ShakeRotation.magnitude:F2}");
            Check("control during quake", movedQ > 0.3f, $"moved {movedQ:F2} m");
            Check("quiet after quake", seq.Current == ASHEruptionSequence.Stage.Quiet && radio.Current == ASHRadioController.Mode.StaticOnly, $"stage={seq.Current} radio={radio.Current}");

            // ---- 4/5 fast ash ramp -> heavy wave -> easing -> done
            float acc0 = acc ? acc.ashAmount : 0f;
            seq.debugSpeed = 20f; seq.SkipTo(ASHEruptionSequence.Stage.FirstAsh);
            float ashMid = 0f, rateMid = 0f;
            while (seq.Current == ASHEruptionSequence.Stage.FirstAsh && Time.time - t0 < 200f) { ashMid = dir.Current.ashfall; rateMid = ash ? ash.GetComponent<ParticleSystem>().emission.rateOverTime.constant : 0f; yield return null; }
            Check("first ash ramp", ashMid > 0.25f && rateMid > 100f, $"ashfall={ashMid:F2} particles/s={rateMid:F0} weather={dir.Describe()}");
            float minVis = 99999f; bool objShown = false; string objTitle = "";
            while (seq.Current == ASHEruptionSequence.Stage.HeavyWave) { minVis = Mathf.Min(minVis, dir.Current.visibility); if (tracker && tracker.HasPriority) { objShown = true; objTitle = tracker.Title(); } yield return null; }
            Check("heavy wave", minVis < 150f && objShown && dir.AirborneAsh > 0.6f, $"minVis={minVis:F0}m objective='{objTitle}' airborne={dir.AirborneAsh:F2} indoor={outp.Indoor01:F2}");
            float accHeavy = acc ? acc.ashAmount : 0f;
            while (seq.Current == ASHEruptionSequence.Stage.Easing) yield return null;
            yield return null;
            Check("ease + scheduler", seq.Current == ASHEruptionSequence.Stage.Done && dir.schedulerEnabled && !(tracker && tracker.HasPriority), $"stage={seq.Current} scheduler={dir.schedulerEnabled} target={dir.TargetKind}");
            Check("accumulation persists", !acc || (accHeavy > acc0 + 0.005f && acc.ashAmount >= accHeavy - 0.05f), acc ? $"ground ash {acc0:F3} -> {accHeavy:F3} (heavy) -> {acc.ashAmount:F3} (after)" : "no controller");

            // ---- 6 weather states: smooth blends
            dir.debugTransitionSeconds = 2.5f;
            foreach (ASHWeatherKind k in new[] { ASHWeatherKind.CalmHazy, ASHWeatherKind.LightAsh, ASHWeatherKind.HeavyAsh, ASHWeatherKind.RainAsh, ASHWeatherKind.Snowstorm, ASHWeatherKind.Thunderstorm })
            {
                dir.debugState = k; dir.debugOverride = true;
                float last = Mathf.Log(dir.Current.visibility), maxStep = 0f, lastW = dir.Current.atmosphereWeight, maxW = 0f; t0 = Time.time;
                while (Time.time - t0 < 3f)
                {
                    yield return null;
                    float lv = Mathf.Log(Mathf.Max(1f, dir.Current.visibility)); maxStep = Mathf.Max(maxStep, Mathf.Abs(lv - last)); last = lv;
                    maxW = Mathf.Max(maxW, Mathf.Abs(dir.Current.atmosphereWeight - lastW)); lastW = dir.Current.atmosphereWeight;
                }
                var prof = dir.Profile(k);
                bool reached = dir.CurrentKind == k && prof && Mathf.Abs(dir.Current.ashfall - prof.ashfall) < 0.02f && Mathf.Abs(dir.Current.rain - prof.rain) < 0.02f && Mathf.Abs(dir.Current.snow - prof.snow) < 0.02f;
                string extra = "";
                if (k == ASHWeatherKind.RainAsh && outp.rainRig) { var ps = outp.rainRig.GetComponentInChildren<ParticleSystem>(); extra = $" rainRate={(ps ? ps.emission.rateOverTime.constant : 0f):F0}"; reached &= ps && ps.emission.rateOverTime.constant > 0f; }
                if (k == ASHWeatherKind.Snowstorm && outp.snowRig) { var ps = outp.snowRig.GetComponentInChildren<ParticleSystem>(); extra = $" snowRate={(ps ? ps.emission.rateOverTime.constant : 0f):F0}"; reached &= ps && ps.emission.rateOverTime.constant > 0f; }
                Check($"weather {k}", reached && maxStep < 0.25f && maxW < 0.1f, $"{dir.Describe()} maxLogVisStep/frame={maxStep:F3} maxWeightStep={maxW:F3}{extra}");
            }

            // ---- lightning
            int s0 = outp.Strikes; outp.Strike(); float maxFlash = 0f; t0 = Time.time;
            while (Time.time - t0 < 5.5f) { maxFlash = Mathf.Max(maxFlash, outp.Flash); yield return null; }
            Check("lightning + thunder", outp.Strikes > s0 && maxFlash > 0.2f && !string.IsNullOrEmpty(outp.LastThunder), $"strikes {s0}->{outp.Strikes} flash={maxFlash:F2} thunder='{outp.LastThunder}' bolt={(outp.lightningPrefab ? outp.lightningPrefab.name : "none")}");

            // ---- replay restarts cleanly
            seq.debugSpeed = 1f; dir.debugOverride = false; seq.Replay(); yield return new WaitForSeconds(0.6f);
            int fallenAfter = 0; foreach (var p in ASHQuakeProp.All) if (p.Fallen) fallenAfter++;
            Check("replay", seq.Current == ASHEruptionSequence.Stage.Morning && dir.CurrentKind == seq.profile.startState && Mathf.Abs(seq.FogDensity - seq.profile.startFogDensity) < 0.001f && fallenAfter == 0 && !dir.schedulerEnabled, $"stage={seq.Current} weather={dir.CurrentKind} fallen={fallenAfter} scheduler={dir.schedulerEnabled}");
            Finish(seq);
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
        static string Short(string s) { s = s ?? ""; s = System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", ""); return s.Length > 60 ? s.Substring(0, 60) + "..." : s; }

        void Finish(ASHEruptionSequence seq)
        {
            var input = FindAnyObjectByType<ASHInput>(); if (input) input.SimMove = Vector2.zero;
            if (seq) seq.SuppressForTests();
            Debug.Log($"[ASH] TEST SUMMARY P2A pass={_pass} fail={_fail}\n{_sum}");
            Destroy(gameObject);
        }
    }
}
