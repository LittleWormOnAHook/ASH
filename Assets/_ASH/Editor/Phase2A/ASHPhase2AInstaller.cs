using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Opening;
using ASH.World;
using ASH.World.Weather;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// ASH > Phase 2A > Install Opening Sequence. Idempotent and additive: creates the weather/opening/audio assets when
    /// missing, then adds ONE new root "ASH_Phase2A" to the open scene (weather director + outputs, opening sequence,
    /// radio, quake, cabin interior trigger, opening spawn, placeholder cabin dressing, rain/snow rigs). Never moves or
    /// edits existing scene objects, never saves the scene. Running it again only fills missing references.
    /// </summary>
    public static class ASHPhase2AInstaller
    {
        const string DataDir = "Assets/_ASH/Data/Weather", OpenDir = "Assets/_ASH/Data/Opening", PrefabDir = "Assets/_ASH/Prefabs/Weather", AudioDir = "Assets/_ASH/Audio/Phase2A";
        const string Enviro = "Assets/Enviro 3 - Sky and Weather";
        const string RootName = "ASH_Phase2A";
        static StringBuilder _log;

        // ================================================================== menus
        [MenuItem("ASH/Phase 2A/Install Opening Sequence", false, 0)]
        public static void Install()
        {
            if (Application.isPlaying) { Debug.LogWarning("[ASH] P2A install: exit Play Mode first"); return; }
            _log = new StringBuilder("[ASH] P2A INSTALL\n");
            var profiles = EnsureWeatherProfiles();
            var audio = EnsureAudioBank();
            var opening = EnsureOpeningProfile();
            var rain = EnsurePrefabCopy($"{Enviro}/Prefabs/Particle Systems/Rain.prefab", $"{PrefabDir}/ASH_RainRig.prefab");
            var snow = EnsurePrefabCopy($"{Enviro}/Prefabs/Particle Systems/Snow.prefab", $"{PrefabDir}/ASH_SnowRig.prefab");
            var bolt = EnsurePrefabCopy($"{Enviro}/Prefabs/Lightning/LightningStrike.prefab", $"{PrefabDir}/ASH_LightningStrike.prefab");
            AssetDatabase.SaveAssets();
            InstallScene(profiles, audio, opening, rain, snow, bolt);
            Debug.Log(_log.ToString());
            System.IO.Directory.CreateDirectory("AshBridge"); System.IO.File.WriteAllText("AshBridge/p2a_install.txt", _log.ToString());
        }

        [MenuItem("ASH/Phase 2A/Rebuild Placeholders (delete ASH_Phase2A and reinstall)", false, 1)]
        public static void Reinstall()
        {
            if (Application.isPlaying) return;
            var root = GameObject.Find(RootName); if (root) Undo.DestroyObjectImmediate(root);
            Install();
        }

        [MenuItem("ASH/Phase 2A/Toggle Opening On Play", false, 2)]
        public static void ToggleOpening()
        {
            var s = Object.FindAnyObjectByType<ASHEruptionSequence>(); if (!s) { Debug.LogWarning("[ASH] No opening sequence in the scene"); return; }
            Undo.RecordObject(s, "Toggle opening"); s.playOnStart = !s.playOnStart; EditorUtility.SetDirty(s);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
            Debug.Log($"[ASH] Opening on Play = {s.playOnStart}");
        }

        [MenuItem("ASH/Phase 2A/Debug/Replay Opening", false, 20)] static void MReplay() { var s = Seq(); if (s) s.Replay(); }
        [MenuItem("ASH/Phase 2A/Debug/Skip To/Broadcast", false, 21)] static void S1() => Skip(ASHEruptionSequence.Stage.Broadcast);
        [MenuItem("ASH/Phase 2A/Debug/Skip To/Earthquake", false, 22)] static void S2() => Skip(ASHEruptionSequence.Stage.Quake);
        [MenuItem("ASH/Phase 2A/Debug/Skip To/Quiet", false, 23)] static void S3() => Skip(ASHEruptionSequence.Stage.Quiet);
        [MenuItem("ASH/Phase 2A/Debug/Skip To/First Ash", false, 24)] static void S4() => Skip(ASHEruptionSequence.Stage.FirstAsh);
        [MenuItem("ASH/Phase 2A/Debug/Skip To/Heavy Wave", false, 25)] static void S5() => Skip(ASHEruptionSequence.Stage.HeavyWave);
        [MenuItem("ASH/Phase 2A/Debug/Skip To/Easing", false, 26)] static void S6() => Skip(ASHEruptionSequence.Stage.Easing);
        [MenuItem("ASH/Phase 2A/Debug/Skip To/Done (scheduler)", false, 27)] static void S7() => Skip(ASHEruptionSequence.Stage.Done);
        [MenuItem("ASH/Phase 2A/Debug/Weather/Partly Cloudy, Light Snow", false, 39)] static void W7() => Wx(ASHWeatherKind.LightSnowMorning);
        [MenuItem("ASH/Phase 2A/Debug/Weather/Calm-Hazy", false, 40)] static void W1() => Wx(ASHWeatherKind.CalmHazy);
        [MenuItem("ASH/Phase 2A/Debug/Weather/Light Ash", false, 41)] static void W2() => Wx(ASHWeatherKind.LightAsh);
        [MenuItem("ASH/Phase 2A/Debug/Weather/Heavy Ash", false, 42)] static void W3() => Wx(ASHWeatherKind.HeavyAsh);
        [MenuItem("ASH/Phase 2A/Debug/Weather/Rain + Ash", false, 43)] static void W4() => Wx(ASHWeatherKind.RainAsh);
        [MenuItem("ASH/Phase 2A/Debug/Weather/Snowstorm", false, 44)] static void W5() => Wx(ASHWeatherKind.Snowstorm);
        [MenuItem("ASH/Phase 2A/Debug/Weather/Thunderstorm", false, 45)] static void W6() => Wx(ASHWeatherKind.Thunderstorm);
        [MenuItem("ASH/Phase 2A/Debug/Weather/Clear (scene as tuned)", false, 46)] static void W0() => Wx(ASHWeatherKind.Clear);
        [MenuItem("ASH/Phase 2A/Debug/Weather/Auto (scheduler)", false, 47)]
        static void WAuto() { var d = ASHWeatherDirector.Instance; if (!d) return; d.debugOverride = false; d.SetScheduler(true); Debug.Log("[ASH] Weather -> scheduler"); }
        [MenuItem("ASH/Phase 2A/Debug/Weather/Strike Lightning", false, 48)]
        static void WStrike() { var o = Object.FindAnyObjectByType<ASHWeatherOutputs>(); if (o && Application.isPlaying) o.Strike(); }

        public static void LogSun()
        {
            var clock = Object.FindAnyObjectByType<ASHDayNightClock>();
            var so = clock ? new SerializedObject(clock).FindProperty("sun") : null; var l = so != null ? so.objectReferenceValue as Light : null;
            var rs = RenderSettings.sun;
            Debug.Log($"[ASH] SUN clock.sun={(l ? l.name : "none")} RenderSettings.sun={(rs ? rs.name + (rs.isActiveAndEnabled ? "" : " (inactive)") : "none")} match={(l && rs == l)} sunFwd={(l ? l.transform.forward.ToString("F3") : "-")}");
        }

        static ASHEruptionSequence Seq() { if (!Application.isPlaying) { Debug.LogWarning("[ASH] Play Mode only"); return null; } return Object.FindAnyObjectByType<ASHEruptionSequence>(); }
        static void Skip(ASHEruptionSequence.Stage s) { var q = Seq(); if (q) q.SkipTo(s); }
        static void Wx(ASHWeatherKind k)
        {
            var d = ASHWeatherDirector.Instance; if (!d || !Application.isPlaying) { Debug.LogWarning("[ASH] Play Mode only"); return; }
            d.debugState = k; d.debugOverride = true; Debug.Log($"[ASH] Weather override -> {k}");
        }

        // ================================================================== assets
        static void Folder(string path)
        {
            var parts = path.Split('/'); string cur = parts[0];
            for (int i = 1; i < parts.Length; i++) { var next = cur + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]); cur = next; }
        }

        static T Ensure<T>(string path, System.Action<T> init, out bool created) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path); created = false;
            if (a) return a;
            a = ScriptableObject.CreateInstance<T>(); init(a); AssetDatabase.CreateAsset(a, path); created = true;
            return a;
        }

        // ================================================================== doors
        const string DoorAudio = "Assets/Audio/Doors", DeviceAudio = "Assets/Audio/Devices";

        static void FillDoorAudio(ASHDoor d, ASHPhase2AAudio bank)
        {
            if (d.openClips == null || d.openClips.Length == 0) d.openClips = Many(Clip($"{DoorAudio}/Wood Open Door.wav"), Clip($"{DoorAudio}/openDoor.wav"));
            if (d.closeClips == null || d.closeClips.Length == 0) d.closeClips = Many(Clip($"{DoorAudio}/Wood Close Door.wav"), Clip($"{DoorAudio}/closeDoor.wav"));
            if (d.lockedClips == null || d.lockedClips.Length == 0) { var l = new List<AudioClip> { Clip($"{DeviceAudio}/locked.wav") }; if (bank && bank.rattles != null) l.AddRange(bank.rattles); d.lockedClips = Many(l.ToArray()); }
            if (d.unlockClips == null || d.unlockClips.Length == 0) d.unlockClips = Many(Clip($"{DoorAudio}/Unlock Door.mp3"), Clip($"{DeviceAudio}/unlocked.wav"));
        }

        /// <summary>Interactive door for a scene door leaf: own object (scene leaf is never moved or re-parented in the editor).</summary>
        static ASHDoor SetupDoor(Transform root, GameObject leaf, ASHPhase2AAudio bank)
        {
            if (!leaf) { _log.Append("WARN cabin door: no 'WoodenDoor-Cabin' in the scene - doorway relies on the exit blockers only\n"); return null; }
            var t = root.Find("ASH_CabinDoor"); if (!t) { t = new GameObject("ASH_CabinDoor").transform; t.SetParent(root, false); }
            var d = t.GetComponent<ASHDoor>(); if (!d) d = t.gameObject.AddComponent<ASHDoor>();
            if (!d.leaf) d.leaf = leaf.transform;
            t.SetPositionAndRotation(leaf.transform.position, leaf.transform.rotation); // our helper object only
            t.gameObject.layer = 0;
            FillDoorAudio(d, bank);
            if (leaf.isStatic) _log.Append($"WARN door leaf '{leaf.name}' is Static - untick Static so it can swing\n");
            _log.Append($"cabin door: {leaf.name} at {leaf.transform.position:F2} (hinge auto from mesh bounds, opens {d.openAngle:F0} deg away from the player)\n");
            return d;
        }

        [MenuItem("ASH/World/Make Selected Door Interactive (E)", false, 60)]
        static void MakeSelectedDoor()
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
            {
                if (go.GetComponent<ASHDoor>() || !go.GetComponent<MeshFilter>()) continue;
                var holder = new GameObject("ASH_Door_" + go.name); Undo.RegisterCreatedObjectUndo(holder, "Make Door");
                holder.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
                var d = holder.AddComponent<ASHDoor>(); d.leaf = go.transform; FillDoorAudio(d, null); n++;
            }
            Debug.Log($"[ASH] Made {n} interactive door(s) (each on its own ASH_Door_* object; the door meshes were not moved). Save the scene to keep them.");
        }

        /// <summary>Finds openings out of the cabin (rays from inside that escape) other than the door, and covers each with an
        /// invisible collider (layer Ignore Raycast, so interaction and the camera ignore it). Rebuilt on every install.</summary>
        static GameObject BuildExitBlockers(Transform root, GameObject cabin, BoxCollider interior, GameObject doorLeaf)
        {
            var old = root.Find("ASH_OpeningExitBlockers"); if (old) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("ASH_OpeningExitBlockers"); go.transform.SetParent(root, false); go.layer = 2;
            if (!interior) return go;
            Physics.SyncTransforms();
            var b = interior.bounds; float floor = b.min.y;
            Bounds doorB = default; bool hasDoor = false;
            if (doorLeaf && doorLeaf.GetComponent<Renderer>()) { doorB = doorLeaf.GetComponent<Renderer>().bounds; doorB.Expand(new Vector3(0.6f, 0.4f, 0.6f)); hasDoor = true; }
            var pts = new List<(Vector3 p, Vector3 n)>();
            var cabT = cabin ? cabin.transform : null;
            bool WallHit(Vector3 o, Vector3 d, float dist, out RaycastHit best)
            {
                best = default; float bd = float.MaxValue; bool any = false;
                foreach (var h in Physics.RaycastAll(o, d, dist, ~(1 << 2), QueryTriggerInteraction.Ignore))
                    if (h.distance < bd && (!cabT || h.collider.transform.IsChildOf(cabT))) { bd = h.distance; best = h; any = true; }
                return any;
            }
            var inner = b; inner.Expand(new Vector3(-0.8f, 10f, -0.8f));
            Bounds cabinB = default; var cabCol = cabin ? cabin.GetComponentInChildren<Collider>() : null; if (cabCol) { cabinB = cabCol.bounds; cabinB.Expand(0.6f); }
            var origins = new List<Vector3>();
            for (int gx = -1; gx <= 1; gx++) for (int gz = -1; gz <= 1; gz++) origins.Add(new Vector3(b.center.x + gx * b.extents.x * 0.6f, 0f, b.center.z + gz * b.extents.z * 0.6f));
            float maxR = Mathf.Max(b.extents.x, b.extents.z) + 3.5f;
            foreach (var o0 in origins)
                for (float h = 0.25f; h <= 2.8f; h += 0.15f)
                    for (int i = 0; i < 120; i++)
                    {
                        float a = i * 3f * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        var o = new Vector3(o0.x, floor + h, o0.z);
                        if (Physics.CheckSphere(o, 0.05f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (Physics.Raycast(o, dir, out var hit, 25f, ~(1 << 2), QueryTriggerInteraction.Ignore) && hit.distance < maxR) continue;
                        // escaped: the wall is where a slightly lower/higher ray in the same direction stops
                        // wall distance: neighbouring rays (angle, then height) that do stop at the wall around the opening
                        float wall = -1f; Vector3 n = -dir;
                        foreach (float da in new[] { 4f, -4f, 8f, -8f, 12f, -12f, 18f, -18f })
                        {
                            var d2 = Quaternion.AngleAxis(da, Vector3.up) * dir;
                            if (WallHit(o, d2, maxR, out var wh)) { wall = wh.distance * Mathf.Max(0.2f, Vector3.Dot(d2, -wh.normal)) / Mathf.Max(0.2f, Vector3.Dot(dir, -wh.normal)); n = wh.normal; break; }
                        }
                        if (wall < 0f) foreach (float dh in new[] { -0.3f, -0.6f, 0.3f, 0.6f, -1f })
                            if (WallHit(o + Vector3.up * dh, dir, maxR, out var wh)) { wall = wh.distance; n = wh.normal; break; }
                        if (wall < 0f || wall > maxR) continue; // no wall found nearby: not a cabin opening
                        var p = o + dir * (wall + 0.05f);
                        if (cabinB.size != Vector3.zero && !cabinB.Contains(p)) continue;
                        if (inner.Contains(new Vector3(p.x, inner.center.y, p.z))) continue; // never inside the room
                        if (hasDoor && doorB.Contains(p)) continue;
                        n.y = 0f; if (n.sqrMagnitude < 1e-4f) n = -dir; n.Normalize();
                        pts.Add((p, n));
                    }
            // cluster points per wall direction and proximity, one box per cluster
            var used = new bool[pts.Count]; int boxes = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                if (used[i]) continue;
                var cl = new List<int> { i }; used[i] = true;
                for (int k = 0; k < cl.Count; k++)
                    for (int j = 0; j < pts.Count; j++)
                        if (!used[j] && Vector3.Dot(pts[j].n, pts[cl[k]].n) > 0.8f && (pts[j].p - pts[cl[k]].p).sqrMagnitude < 0.8f * 0.8f) { used[j] = true; cl.Add(j); }
                var nrm = pts[i].n; var rot = Quaternion.LookRotation(-nrm, Vector3.up);
                var inv = Quaternion.Inverse(rot); Vector3 mn = Vector3.one * 1e9f, mx = -mn;
                foreach (var j in cl) { var l = inv * pts[j].p; mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l); }
                var pad = new Vector3(0.35f, 0.35f, 0f); mn -= pad; mx += pad;
                var c = rot * ((mn + mx) * 0.5f);
                var bt = new GameObject($"Blocker_{boxes}").transform; bt.SetParent(go.transform, false); bt.gameObject.layer = 2;
                bt.SetPositionAndRotation(c, rot);
                var bc = bt.gameObject.AddComponent<BoxCollider>(); bc.size = new Vector3(Mathf.Max(0.3f, mx.x - mn.x), Mathf.Max(0.3f, mx.y - mn.y), 0.5f);
                boxes++;
                _log.Append($"  blocker {boxes - 1}: c={c:F2} size={bc.size:F2} facing {(-nrm):F2} ({cl.Count} samples)\n");
            }
            _log.Append($"exit blockers: {boxes} invisible blocker(s) over {pts.Count} escaping ray samples (door area excluded); active until the doors unlock\n");
            return go;
        }

        static ASHWeatherProfile[] EnsureWeatherProfiles()
        {
            Folder(DataDir);
            var list = new List<ASHWeatherProfile>();
            void P(ASHWeatherKind k, string name, System.Action<ASHWeatherProfile> f)
            {
                var p = Ensure<ASHWeatherProfile>($"{DataDir}/ASH_Weather_{k}.asset", x => { x.kind = k; x.displayName = name; x.ApplyKindDefaults(); f(x); }, out bool c);
                if (c) _log.Append($"created ASH_Weather_{k}\n");
                else { if (p.dataVersion < 1) p.ApplyKindDefaults(); EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p); } // persist the v1 migration
                list.Add(p);
            }
            P(ASHWeatherKind.Clear, "Clear (scene as tuned)", x => { x.wind = 0.15f; x.windSpeed = 6f; x.gustiness = 0.25f; x.visibilityMetres = 8000f; x.atmosphereWeight = 0f; x.vignette = 0.15f; x.windVolume = 0.12f; x.ambienceVolume = 0.55f; x.temperatureC = 8f; x.probability = 0f; });
            P(ASHWeatherKind.LightSnowMorning, "Partly Cloudy, Light Snow (opening)", x => { x.snow = 0.15f; x.ashFlakeSize = 1f; x.wind = 0.25f; x.windSpeed = 4f; x.gustiness = 0.45f; x.cloudDarkness = 0.22f; x.cloudCoverage = 0.45f; x.spindrift = 0.6f; x.ambientScale = 0.95f; x.skyExposureScale = 0.9f; x.skyAshTint = 0f; x.haze = 0.1f; x.visibilityMetres = 3000f; x.volFogDensity = 1f; x.volFogAlbedo = new Color(0.86f, 0.88f, 0.92f); x.atmosphereWeight = 0.25f; x.saturation = -5f; x.postExposure = 0f; x.colorFilter = new Color(0.96f, 0.98f, 1f); x.vignette = 0.15f; x.windVolume = 0.22f; x.ambienceVolume = 0.5f; x.temperatureC = -2f; x.probability = 0f; x.majorEvent = false; });
            P(ASHWeatherKind.CalmHazy, "Calm / Hazy", x => { x.ashfall = 0.04f; x.wind = 0.2f; x.cloudDarkness = 0.15f; x.ambientScale = 0.9f; x.skyExposureScale = 0.85f; x.skyAshTint = 0.25f; x.haze = 0.35f; x.visibilityMetres = 2500f; x.volFogDensity = 1.4f; x.volFogAlbedo = new Color(0.75f, 0.7f, 0.64f); x.atmosphereWeight = 0.45f; x.saturation = -12f; x.postExposure = -0.1f; x.colorFilter = new Color(1f, 0.95f, 0.88f); x.vignette = 0.2f; x.windVolume = 0.15f; x.ambienceVolume = 0.35f; x.temperatureC = 7f; x.probability = 1f; x.majorEvent = false; x.durationMinutes = new Vector2(4f, 9f); });
            P(ASHWeatherKind.LightAsh, "Light Ash", x => { x.ashfall = 0.35f; x.ashFlakeSize = 0.9f; x.wind = 0.3f; x.windSpeed = 7f; x.gustiness = 0.35f; x.cloudDarkness = 0.4f; x.ambientScale = 0.7f; x.skyExposureScale = 0.55f; x.skyAshTint = 0.55f; x.haze = 0.6f; x.visibilityMetres = 700f; x.volFogDensity = 2.5f; x.volFogAlbedo = new Color(0.66f, 0.62f, 0.58f); x.volFogNoise = 0.6f; x.atmosphereWeight = 0.75f; x.saturation = -28f; x.postExposure = -0.3f; x.colorFilter = new Color(0.95f, 0.9f, 0.82f); x.vignette = 0.25f; x.windVolume = 0.3f; x.ambienceVolume = 0.12f; x.temperatureC = 5f; x.probability = 1.2f; x.majorEvent = true; x.durationMinutes = new Vector2(6f, 12f); });
            P(ASHWeatherKind.HeavyAsh, "Heavy Ash", x => { x.ashfall = 1f; x.ashFlakeSize = 1.25f; x.wind = 0.7f; x.windSpeed = 12f; x.gustiness = 0.6f; x.cloudDarkness = 0.78f; x.ambientScale = 0.4f; x.skyExposureScale = 0.25f; x.skyAshTint = 0.85f; x.haze = 0.9f; x.visibilityMetres = 90f; x.volFogDensity = 6f; x.volFogAlbedo = new Color(0.55f, 0.52f, 0.5f); x.volFogNoise = 0.8f; x.atmosphereWeight = 1f; x.saturation = -45f; x.postExposure = -0.7f; x.colorFilter = new Color(0.9f, 0.84f, 0.76f); x.vignette = 0.35f; x.windVolume = 0.75f; x.ambienceVolume = 0f; x.temperatureC = 3f; x.probability = 1f; x.majorEvent = true; x.durationMinutes = new Vector2(5f, 10f); });
            P(ASHWeatherKind.RainAsh, "Rain + Ash", x => { x.ashfall = 0.3f; x.rain = 0.7f; x.wind = 0.45f; x.windSpeed = 9f; x.gustiness = 0.45f; x.cloudDarkness = 0.6f; x.ambientScale = 0.55f; x.skyExposureScale = 0.4f; x.skyAshTint = 0.6f; x.haze = 0.6f; x.visibilityMetres = 350f; x.volFogDensity = 3f; x.volFogAlbedo = new Color(0.5f, 0.5f, 0.5f); x.atmosphereWeight = 0.85f; x.saturation = -32f; x.postExposure = -0.45f; x.colorFilter = new Color(0.88f, 0.88f, 0.86f); x.vignette = 0.3f; x.windVolume = 0.4f; x.rainVolume = 0.75f; x.ambienceVolume = 0f; x.thunderPerMinute = 0.3f; x.temperatureC = 6f; x.probability = 0.8f; x.majorEvent = true; x.durationMinutes = new Vector2(5f, 10f); });
            P(ASHWeatherKind.Snowstorm, "Snowstorm", x => { x.ashfall = 0.1f; x.snow = 0.85f; x.wind = 0.75f; x.windSpeed = 13f; x.gustiness = 0.55f; x.cloudDarkness = 0.55f; x.ambientScale = 0.75f; x.skyExposureScale = 0.6f; x.skyAshTint = 0.2f; x.haze = 0.5f; x.visibilityMetres = 120f; x.volFogDensity = 4.5f; x.volFogAlbedo = new Color(0.9f, 0.92f, 0.95f); x.atmosphereWeight = 0.9f; x.saturation = -25f; x.postExposure = -0.1f; x.colorFilter = new Color(0.9f, 0.95f, 1f); x.vignette = 0.3f; x.windVolume = 0.8f; x.ambienceVolume = 0f; x.temperatureC = -8f; x.probability = 0.6f; x.majorEvent = true; x.durationMinutes = new Vector2(5f, 12f); });
            P(ASHWeatherKind.Thunderstorm, "Thunderstorm", x => { x.ashfall = 0.15f; x.rain = 0.9f; x.wind = 0.6f; x.windSpeed = 11f; x.gustiness = 0.7f; x.cloudDarkness = 0.85f; x.ambientScale = 0.4f; x.skyExposureScale = 0.25f; x.skyAshTint = 0.5f; x.haze = 0.55f; x.visibilityMetres = 300f; x.volFogDensity = 2.8f; x.volFogAlbedo = new Color(0.45f, 0.46f, 0.5f); x.atmosphereWeight = 0.95f; x.saturation = -30f; x.postExposure = -0.6f; x.colorFilter = new Color(0.82f, 0.85f, 0.92f); x.vignette = 0.35f; x.windVolume = 0.5f; x.rainVolume = 1f; x.ambienceVolume = 0f; x.thunderPerMinute = 4f; x.temperatureC = 10f; x.probability = 0.5f; x.majorEvent = true; x.durationMinutes = new Vector2(3f, 7f); });
            return list.ToArray();
        }

        static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        static AudioClip Named(string name, string folder = null)
        {
            var guids = folder != null && AssetDatabase.IsValidFolder(folder) ? AssetDatabase.FindAssets(name + " t:AudioClip", new[] { folder }) : AssetDatabase.FindAssets(name + " t:AudioClip");
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath).Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith(name)).OrderBy(p => p.Length).ToList();
            return paths.Count > 0 ? Clip(paths[0]) : null;
        }
        static AudioClip[] Many(params AudioClip[] c) => c.Where(x => x).ToArray();
        static AudioClip[] Series(string prefix, int from, int to) { var l = new List<AudioClip>(); for (int i = from; i <= to; i++) { var c = Clip($"{AudioDir}/{prefix}{i:00}.ogg"); if (c) l.Add(c); } return l.ToArray(); }

        static ASHPhase2AAudio EnsureAudioBank()
        {
            Folder(OpenDir);
            var a = Ensure<ASHPhase2AAudio>($"{OpenDir}/ASH_Phase2A_Audio.asset", _ => { }, out bool created);
            // fill empty slots only (hand-assigned clips are kept)
            var so = new SerializedObject(a);
            void One(string field, AudioClip c) { var p = so.FindProperty(field); if (p != null && p.objectReferenceValue == null && c) p.objectReferenceValue = c; }
            void Arr(string field, AudioClip[] c) { var p = so.FindProperty(field); if (p == null || p.arraySize > 0 || c.Length == 0) return; p.arraySize = c.Length; for (int i = 0; i < c.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = c[i]; }
            Arr("radioMusic", Many(Named("Lone Harvest")));
            One("staticLoop", Clip($"{AudioDir}/ASH_RadioStatic_Loop.ogg"));
            Arr("staticBursts", Series("ASH_RadioStatic_Burst_", 1, 3));
            One("rumbleLoop", Clip($"{AudioDir}/ASH_Quake_RumbleLoop.ogg"));
            Arr("rumbleSwell", Series("ASH_Quake_Swell_", 1, 2));
            Arr("creaks", Series("ASH_Quake_Creak_", 1, 5));
            Arr("rattles", Series("ASH_Quake_Rattle_", 1, 5));
            Arr("falls", Series("ASH_Quake_Fall_", 1, 5));
            One("windSoft", Named("QP01 0001")); One("windStrong", Named("QP01 0004")); One("windHowl", Named("QP01 0005"));
            One("rainSoft", Named("QP01 0011") ?? Named("enviro_rain_1", Enviro)); One("rainStrong", Named("QP01 0013") ?? Named("enviro_rain_2", Enviro));
            One("morningAmbience", Named("QP01 0047") ?? Named("enviro_ambient_day", Enviro));
            var thunder = new List<AudioClip>(); for (int i = 1; i <= 8; i++) { var c = Named("enviro_thunder_" + i, Enviro); if (c) thunder.Add(c); }
            thunder.AddRange(Series("ASH_Thunder_", 1, 2));
            Arr("thunder", thunder.ToArray());
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(a);
            _log.Append($"audio bank {(created ? "created" : "updated")}: music {a.radioMusic?.Length ?? 0}, static {(a.staticLoop ? a.staticLoop.name : "generated")}, bursts {a.staticBursts?.Length ?? 0}, rumble {(a.rumbleLoop ? "yes" : "no")}, creaks {a.creaks?.Length ?? 0}, rattles {a.rattles?.Length ?? 0}, falls {a.falls?.Length ?? 0}, wind {(a.windSoft ? 1 : 0) + (a.windStrong ? 1 : 0) + (a.windHowl ? 1 : 0)}/3, rain {(a.rainSoft ? 1 : 0) + (a.rainStrong ? 1 : 0)}/2, amb {(a.morningAmbience ? a.morningAmbience.name : "-")}, thunder {a.thunder?.Length ?? 0}, alert {(a.alertTone ? a.alertTone.name : "generated")}, voice {a.broadcastVoice?.Length ?? 0} (subtitles only)\n");
            return a;
        }

        static ASHOpeningProfile EnsureOpeningProfile()
        {
            Folder(OpenDir);
            var p = Ensure<ASHOpeningProfile>($"{OpenDir}/ASH_OpeningProfile.asset", _ => { }, out bool c);
            if (c) _log.Append("created ASH_OpeningProfile\n");
            return p;
        }

        static GameObject EnsurePrefabCopy(string src, string dst)
        {
            Folder(PrefabDir);
            var have = AssetDatabase.LoadAssetAtPath<GameObject>(dst); if (have) return have;
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(src)) { _log.Append($"WARN missing {src} (Enviro 3 not imported?)\n"); return null; }
            if (!AssetDatabase.CopyAsset(src, dst)) { _log.Append($"WARN copy failed {src}\n"); return null; }
            _log.Append($"copied {System.IO.Path.GetFileName(src)} -> {dst}\n");
            return AssetDatabase.LoadAssetAtPath<GameObject>(dst);
        }

        // ================================================================== scene
        static Transform _playerRoot;

        static bool Cast(Vector3 o, Vector3 dir, float dist, out RaycastHit hit)
        {
            var hits = Physics.RaycastAll(o, dir, dist, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (_playerRoot && h.transform.IsChildOf(_playerRoot)) continue;
                if (h.transform.root.name == RootName) continue;
                hit = h; return true;
            }
            hit = default; return false;
        }

        sealed class Interior
        {
            public bool ok; public Bounds box; public float floorY, ceilY; public Vector3 spawn; public float step;
            public List<Vector3> cells = new List<Vector3>(); public List<(Vector3 floor, Vector3 wallDir)> edges = new List<(Vector3, Vector3)>();
            public Dictionary<Vector2Int, (float floor, float ceil)> grid = new Dictionary<Vector2Int, (float, float)>();
        }

        static Interior AnalyzeCabin(GameObject cabin, Vector3 door)
        {
            var r = new Interior { step = 0.4f };
            var rends = cabin.GetComponentsInChildren<Renderer>(); if (rends.Length == 0) return r;
            var b = rends[0].bounds; foreach (var x in rends) b.Encapsulate(x.bounds);
            Physics.SyncTransforms();
            var cols = cabin.GetComponentsInChildren<Collider>();
            _log.Append($"cabin bounds c={b.center:F2} s={b.size:F2} colliders={cols.Length} ({string.Join(",", cols.Select(c => c.GetType().Name + (c is MeshCollider m && m.convex ? "(convex!)" : "")).Distinct())})\n");
            float st = r.step; int nx = Mathf.CeilToInt(b.size.x / st), nz = Mathf.CeilToInt(b.size.z / st);
            var pass = new Dictionary<Vector2Int, (float floor, float ceil)>();
            Vector3[] dirs = new Vector3[8]; for (int i = 0; i < 8; i++) { float a = i * 45f * Mathf.Deg2Rad; dirs[i] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }
            for (int ix = 1; ix < nx; ix++)
                for (int iz = 1; iz < nz; iz++)
                {
                    var o = new Vector3(b.min.x + ix * st, b.min.y + 2.4f, b.min.z + iz * st);
                    if (Physics.CheckSphere(o, 0.12f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!Cast(o, Vector3.down, 3.6f, out var fh) || fh.normal.y < 0.7f) continue;
                    var f = fh.point;
                    if (!Cast(f + Vector3.up * 0.3f, Vector3.up, 8f, out var ch)) continue;
                    if (ch.point.y - f.y < 1.9f) continue;
                    bool enclosed = true; var mid = f + Vector3.up * 1.3f;
                    foreach (var d in dirs) if (!Cast(mid, d, 15f, out _)) { enclosed = false; break; }
                    if (!enclosed) continue;
                    if (Physics.CheckCapsule(f + Vector3.up * 0.45f, f + Vector3.up * 1.6f, 0.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    pass[new Vector2Int(ix, iz)] = (f.y, ch.point.y);
                }
            if (pass.Count == 0) { _log.Append("WARN interior analysis found no enclosed floor\n"); return r; }
            // largest 4-connected component
            var seen = new HashSet<Vector2Int>(); List<Vector2Int> best = null;
            Vector2Int[] n4 = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            foreach (var k in pass.Keys)
            {
                if (seen.Contains(k)) continue;
                var comp = new List<Vector2Int>(); var q = new Queue<Vector2Int>(); q.Enqueue(k); seen.Add(k);
                while (q.Count > 0) { var c = q.Dequeue(); comp.Add(c); foreach (var d in n4) { var nb = c + d; if (pass.ContainsKey(nb) && seen.Add(nb)) q.Enqueue(nb); } }
                if (best == null || comp.Count > best.Count) best = comp;
            }
            // other rooms: components on the same floor level (doorways between rooms split the grid)
            var mainFloor = best.Average(c => pass[c].floor); var rooms = new List<Vector2Int>(best); int roomCount = 1;
            var seen2 = new HashSet<Vector2Int>(best);
            foreach (var k in pass.Keys)
            {
                if (seen2.Contains(k)) continue;
                var comp = new List<Vector2Int>(); var q2 = new Queue<Vector2Int>(); q2.Enqueue(k); seen2.Add(k);
                while (q2.Count > 0) { var c = q2.Dequeue(); comp.Add(c); foreach (var d in n4) { var nb = c + d; if (pass.ContainsKey(nb) && seen2.Add(nb)) q2.Enqueue(nb); } }
                if (comp.Count >= 6 && Mathf.Abs(comp.Average(c => pass[c].floor) - mainFloor) < 0.3f) { rooms.AddRange(comp); roomCount++; }
            }
            var set = new HashSet<Vector2Int>(best);
            // clearance (BFS from the component's border)
            var dist = new Dictionary<Vector2Int, int>(); var bq = new Queue<Vector2Int>();
            foreach (var c in best) if (n4.Any(d => !set.Contains(c + d))) { dist[c] = 0; bq.Enqueue(c); }
            while (bq.Count > 0) { var c = bq.Dequeue(); foreach (var d in n4) { var nb = c + d; if (set.Contains(nb) && !dist.ContainsKey(nb)) { dist[nb] = dist[c] + 1; bq.Enqueue(nb); } } }
            Vector3 W(Vector2Int c) => new Vector3(b.min.x + c.x * st, pass[c].floor, b.min.z + c.y * st);
            Vector3 centroid = Vector3.zero; foreach (var c in best) centroid += W(c); centroid /= best.Count;
            float minF = rooms.Min(c => pass[c].floor), minC = rooms.Min(c => pass[c].ceil);
            var mn = W(best[0]); var mx = mn; foreach (var c in rooms) { var w = W(c); mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w); }
            mn -= new Vector3(st * 0.5f, 0, st * 0.5f); mx += new Vector3(st * 0.5f, 0, st * 0.5f);
            r.floorY = minF; r.ceilY = Mathf.Min(minC, minF + 3.5f);
            r.box = new Bounds(new Vector3((mn.x + mx.x) * 0.5f, (r.floorY + r.ceilY) * 0.5f, (mn.z + mx.z) * 0.5f), new Vector3(mx.x - mn.x, r.ceilY - r.floorY + 0.4f, mx.z - mn.z));
            int maxD = best.Max(c => dist[c]);
            var sp = best.Where(c => dist[c] >= Mathf.Max(1, maxD - 1)).OrderBy(c => (W(c) - centroid).sqrMagnitude).First();
            r.spawn = W(sp);
            foreach (var c in best) { r.cells.Add(W(c)); r.grid[c] = pass[c]; }
            foreach (var c in best)
                foreach (var d in n4)
                    if (!set.Contains(c + d) && dist[c] == 0)
                    {
                        var w = W(c); if (door != Vector3.zero && Vector3.Distance(new Vector3(w.x, 0, w.z), new Vector3(door.x, 0, door.z)) < 2.2f) continue;
                        r.edges.Add((w, new Vector3(d.x, 0, d.y)));
                    }
            r.ok = true;
            _log.Append($"interior: {roomCount} room(s), {rooms.Count} cells, main {best.Count} ({pass.Count} enclosed), floor {r.floorY:F2}, ceiling {r.ceilY:F2}, box c={r.box.center:F2} s={r.box.size:F2}, spawn {r.spawn:F2} (clearance {dist[sp] * st:F1} m), wall cells {r.edges.Count}\n");
            return r;
        }

        static void InstallScene(ASHWeatherProfile[] profiles, ASHPhase2AAudio audio, ASHOpeningProfile opening, GameObject rainPrefab, GameObject snowPrefab, GameObject boltPrefab)
        {
            var cc = Object.FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>(); _playerRoot = cc ? cc.transform.root : null;
            var root = GameObject.Find(RootName); bool fresh = !root;
            if (fresh) { root = new GameObject(RootName); Undo.RegisterCreatedObjectUndo(root, "Install Phase 2A"); }
            T Child<T>(string name, out bool made) where T : Component
            {
                var t = root.transform.Find(name); made = false;
                if (!t) { t = new GameObject(name).transform; t.SetParent(root.transform, false); made = true; }
                var c = t.GetComponent<T>(); if (!c) c = t.gameObject.AddComponent<T>(); return c;
            }

            var cabin = GameObject.Find("Grandpa Cabin");
            var doorGo = GameObject.Find("WoodenDoor-Cabin") ?? GameObject.Find("WoodenDoor"); var door = doorGo ? doorGo.transform.position : Vector3.zero;
            var interiorCol = Child<BoxCollider>("ASH_CabinInterior", out bool madeInterior);
            Interior inside = null;
            if (madeInterior || !root.transform.Find("ASH_OpeningSpawn"))
            {
                inside = cabin ? AnalyzeCabin(cabin, door) : null;
                if (!cabin) _log.Append("WARN no 'Grandpa Cabin' in scene\n");
            }
            if (madeInterior)
            {
                interiorCol.isTrigger = true; interiorCol.gameObject.layer = 2; // Ignore Raycast: interactor / camera never see it
                if (inside != null && inside.ok) { interiorCol.transform.position = inside.box.center; interiorCol.size = inside.box.size + new Vector3(0.3f, 0f, 0.3f); }
                else if (cabin) { var rb = cabin.GetComponentsInChildren<Renderer>().Select(x => x.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; }); interiorCol.transform.position = rb.center; interiorCol.size = rb.size * 0.8f; _log.Append("WARN interior trigger from 80% of cabin bounds\n"); }
            }
            var spawnT = root.transform.Find("ASH_OpeningSpawn");
            if (!spawnT)
            {
                spawnT = new GameObject("ASH_OpeningSpawn").transform; spawnT.SetParent(root.transform, false);
                spawnT.position = inside != null && inside.ok ? inside.spawn : interiorCol.bounds.center - Vector3.up * (interiorCol.size.y * 0.5f - 0.1f);
            }

            // ---- dressing (placeholders), only on a fresh install
            ASHRadioController radio = root.GetComponentInChildren<ASHRadioController>();
            if (fresh && inside != null && inside.ok) Dress(root.transform, inside, door, spawnT, out radio);
            if (!radio)
            {
                var rt = BuildRadio(root.transform); rt.position = spawnT.position + spawnT.forward * 1.2f + Vector3.up * 0.9f; radio = rt.GetComponent<ASHRadioController>();
                _log.Append("radio placed without a table (no interior analysis)\n");
            }
            radio.audioBank = audio; radio.profile = opening;

            // ---- weather
            var director = Child<ASHWeatherDirector>("ASH_Weather", out bool madeW);
            var outputs = director.GetComponent<ASHWeatherOutputs>(); if (!outputs) outputs = director.gameObject.AddComponent<ASHWeatherOutputs>();
            if (madeW) { director.profiles = profiles; director.initialState = ASHWeatherKind.Clear; director.accumulationRate = -1f; director.schedulerEnabled = false; }
            if (director.profiles == null || director.profiles.Length == 0) director.profiles = profiles;
            else { var l = director.profiles.Where(x => x).ToList(); int n0 = l.Count; foreach (var p in profiles) if (!l.Any(x => x.kind == p.kind)) l.Add(p); if (l.Count != n0) { director.profiles = l.ToArray(); _log.Append($"added {l.Count - n0} weather profile(s) to the director\n"); } }
            if (!outputs.clock) outputs.clock = Object.FindAnyObjectByType<ASHDayNightClock>();
            if (!outputs.ashFall) outputs.ashFall = Object.FindAnyObjectByType<ASHAshFall>();
            if (!outputs.interior) outputs.interior = interiorCol;
            if (!outputs.audioBank) outputs.audioBank = audio;
            if (!outputs.lightningPrefab) outputs.lightningPrefab = boltPrefab;
            if (!outputs.cloudShader) outputs.cloudShader = Shader.Find("ASH/CloudLayer");
            var clouds = director.GetComponent<ASHCloudLayer>(); if (!clouds) clouds = director.gameObject.AddComponent<ASHCloudLayer>(); if (!clouds.shader) clouds.shader = outputs.cloudShader;
            if (!director.GetComponent<ASHSpindrift>()) director.gameObject.AddComponent<ASHSpindrift>();
            var rigs = root.transform.Find("ASH_WeatherRigs"); if (!rigs) { rigs = new GameObject("ASH_WeatherRigs").transform; rigs.SetParent(root.transform, false); }
            if (!outputs.rainRig && rainPrefab) { var g = (GameObject)PrefabUtility.InstantiatePrefab(rainPrefab, rigs); g.name = "ASH_RainRig"; outputs.rainRig = g; }
            if (!outputs.snowRig && snowPrefab) { var g = (GameObject)PrefabUtility.InstantiatePrefab(snowPrefab, rigs); g.name = "ASH_SnowRig"; outputs.snowRig = g; }
            foreach (var rig in new[] { outputs.rainRig, outputs.snowRig }) if (rig) foreach (var ps in rig.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.playOnAwake = true; }

            // ---- opening
            var quake = Child<ASHEarthquakeEffects>("ASH_Earthquake", out _);
            if (!quake.audioBank) quake.audioBank = audio; if (!quake.soundArea) quake.soundArea = interiorCol; if (!quake.radio) quake.radio = radio;
            var obj = Child<ASHOpeningObjective>("ASH_OpeningObjective", out _); if (!obj.interior) obj.interior = interiorCol;
            var seq = Child<ASHEruptionSequence>("ASH_OpeningSequence", out _);
            if (!seq.profile) seq.profile = opening; if (!seq.openingSpawn) seq.openingSpawn = spawnT; if (!seq.director) seq.director = director;
            // ---- cabin door (E open/close, locked until the quake) + invisible blockers over other exits
            var cabinDoor = SetupDoor(root.transform, doorGo, audio);
            if (cabinDoor && (seq.cabinDoors == null || seq.cabinDoors.Length == 0 || !seq.cabinDoors[0])) seq.cabinDoors = new[] { cabinDoor };
            var blockers = BuildExitBlockers(root.transform, cabin, interiorCol, doorGo);
            seq.exitBlockers = blockers;
            if (!seq.radio) seq.radio = radio; if (!seq.quake) seq.quake = quake; if (!seq.objective) seq.objective = obj; if (!seq.clock) seq.clock = Object.FindAnyObjectByType<ASHDayNightClock>();

            foreach (var c in root.GetComponentsInChildren<Component>(true)) if (c) EditorUtility.SetDirty(c);
            EditorSceneManager.MarkSceneDirty(root.scene);
            _log.Append($"{(fresh ? "installed" : "updated")} {RootName}: spawn {spawnT.position:F2}, interior {interiorCol.transform.position:F2} size {interiorCol.size:F2}, radio {radio.transform.position:F2}, rain {(outputs.rainRig ? "Enviro copy" : "none")}, snow {(outputs.snowRig ? "Enviro copy" : "none")}, lightning {(outputs.lightningPrefab ? "Enviro copy" : "flash only")}, clock {(outputs.clock ? "ok" : "MISSING")}, ashfall {(outputs.ashFall ? "ok" : "MISSING")}. Scene NOT saved.\n");
        }

        // ---------------------------------------------------------------- placeholder dressing
        static GameObject Prefab(string name)
        {
            var p = AssetDatabase.FindAssets(name + " t:Prefab").Select(AssetDatabase.GUIDToAssetPath).Where(x => System.IO.Path.GetFileNameWithoutExtension(x) == name).OrderBy(x => x.Length).FirstOrDefault();
            return p != null ? AssetDatabase.LoadAssetAtPath<GameObject>(p) : null;
        }

        static GameObject Place(string prefabName, Transform parent, string fallbackName, Vector3 fallbackSize, Color c)
        {
            var pf = Prefab(prefabName); GameObject g;
            if (pf) { g = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent); }
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.transform.SetParent(parent, false); g.transform.localScale = fallbackSize;
                Tint(g, c); _log.Append($"  {prefabName} prefab not found -> cube placeholder\n");
            }
            g.name = fallbackName; return g;
        }

        static void Tint(GameObject g, Color c)
        {
            var r = g.GetComponent<Renderer>(); if (!r) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit"); if (!sh) return;
            var m = new Material(sh) { color = c }; m.SetFloat("_Smoothness", 0.25f); r.sharedMaterial = m;
        }

        static Bounds RB(GameObject g) { var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.one * 0.2f); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
        static void SitOn(GameObject g, Vector3 at, float surfaceY) { g.transform.position = at; var b = RB(g); g.transform.position += new Vector3(at.x - b.center.x, surfaceY - b.min.y + 0.005f, at.z - b.center.z); }
        static void AgainstWall(GameObject g, Vector3 floor, Vector3 wallDir, float y)
        {
            g.transform.rotation = Quaternion.LookRotation(-wallDir, Vector3.up);
            if (Cast(new Vector3(floor.x, y, floor.z), wallDir, 3f, out var h)) { var b = RB(g); float half = Vector3.Scale(b.extents, new Vector3(Mathf.Abs(wallDir.x), 0, Mathf.Abs(wallDir.z))).magnitude; SitOn(g, h.point - wallDir * (half + 0.04f), floor.y); }
            else SitOn(g, floor, floor.y);
        }

        static Transform BuildRadio(Transform parent)
        {
            var root = new GameObject("ASH_Radio (placeholder)"); root.transform.SetParent(parent, false);
            var wood = new Color(0.33f, 0.2f, 0.12f); var dark = new Color(0.08f, 0.08f, 0.08f); var brass = new Color(0.72f, 0.6f, 0.34f);
            void Part(PrimitiveType t, string n, Vector3 pos, Vector3 scale, Color c, Vector3 rot = default)
            {
                var g = GameObject.CreatePrimitive(t); g.name = n; Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(root.transform, false); g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localEulerAngles = rot; Tint(g, c);
            }
            Part(PrimitiveType.Cube, "Body", new Vector3(0, 0.12f, 0), new Vector3(0.38f, 0.24f, 0.17f), wood);
            Part(PrimitiveType.Cylinder, "Speaker", new Vector3(-0.07f, 0.125f, 0.086f), new Vector3(0.15f, 0.004f, 0.15f), dark, new Vector3(90, 0, 0));
            Part(PrimitiveType.Cube, "Dial", new Vector3(0.1f, 0.15f, 0.086f), new Vector3(0.11f, 0.05f, 0.004f), brass);
            Part(PrimitiveType.Cylinder, "Knob", new Vector3(0.1f, 0.07f, 0.09f), new Vector3(0.035f, 0.01f, 0.035f), dark, new Vector3(90, 0, 0));
            Part(PrimitiveType.Cylinder, "Antenna", new Vector3(0.15f, 0.38f, -0.04f), new Vector3(0.006f, 0.16f, 0.006f), new Color(0.6f, 0.6f, 0.62f), new Vector3(0, 0, -18));
            var box = root.AddComponent<BoxCollider>(); box.center = new Vector3(0, 0.12f, 0); box.size = new Vector3(0.38f, 0.24f, 0.17f);
            root.AddComponent<ASHRadioController>();
            return root.transform;
        }

        static void Dress(Transform root, Interior inn, Vector3 door, Transform spawn, out ASHRadioController radio)
        {
            var dress = new GameObject("ASH_CabinDressing (placeholders)").transform; dress.SetParent(root, false);
            var used = new List<Vector3>();
            (Vector3 floor, Vector3 wallDir)? Edge(float minFromSpawn, float minFromUsed, Vector3? avoidDir = null)
            {
                foreach (var e in inn.edges.OrderBy(e => -Vector3.Distance(e.floor, door == Vector3.zero ? inn.box.center : door)))
                {
                    if (Vector3.Distance(e.floor, spawn.position) < minFromSpawn) continue;
                    if (used.Any(u => Vector3.Distance(u, e.floor) < minFromUsed)) continue;
                    if (avoidDir.HasValue && Vector3.Dot(avoidDir.Value, e.wallDir) > 0.5f) continue;
                    used.Add(e.floor); return e;
                }
                return null;
            }
            // table + radio against the wall farthest from the door
            radio = null;
            var te = Edge(1.6f, 0f);
            if (te.HasValue)
            {
                var table = Place("WoodenTable", dress, "ASH_Table", new Vector3(1.1f, 0.78f, 0.65f), new Color(0.36f, 0.24f, 0.15f));
                AgainstWall(table, te.Value.floor, te.Value.wallDir, te.Value.floor.y + 0.5f);
                var tb = RB(table); float top = tb.max.y;
                var rt = BuildRadio(dress); rt.rotation = Quaternion.LookRotation(-te.Value.wallDir); SitOn(rt.gameObject, tb.center - te.Value.wallDir * 0f + Vector3.Cross(Vector3.up, te.Value.wallDir) * tb.extents.magnitude * 0.18f, top); radio = rt.GetComponent<ASHRadioController>();
                spawn.rotation = Quaternion.LookRotation(Flat(rt.position - spawn.position), Vector3.up);
                // bottle that falls off the table
                var bottle = Place("LeatherBottle", dress, "ASH_Bottle (falls)", new Vector3(0.08f, 0.22f, 0.08f), new Color(0.3f, 0.22f, 0.14f));
                SitOn(bottle, tb.center - Vector3.Cross(Vector3.up, te.Value.wallDir) * tb.extents.magnitude * 0.25f - te.Value.wallDir * 0.1f, top);
                MakeFall(bottle);
                // supplies for the objective
                Pickup(true, dress, tb.center - Vector3.Cross(Vector3.up, te.Value.wallDir) * tb.extents.magnitude * 0.05f - te.Value.wallDir * 0.12f, top);
            }
            var ce = Edge(1.2f, 1.4f);
            if (ce.HasValue)
            {
                var crate = Place("Crate", dress, "ASH_Crate", new Vector3(0.6f, 0.45f, 0.45f), new Color(0.42f, 0.3f, 0.18f));
                AgainstWall(crate, ce.Value.floor, ce.Value.wallDir, ce.Value.floor.y + 0.3f);
                Wobble(crate, 1.2f, 0.004f);
                var cb = RB(crate); Pickup(false, dress, cb.center, cb.max.y);
            }
            var be = Edge(1.2f, 1.2f);
            if (be.HasValue)
            {
                var bucket = Place("WaterBucket", dress, "ASH_Bucket", new Vector3(0.32f, 0.34f, 0.32f), new Color(0.4f, 0.4f, 0.42f));
                AgainstWall(bucket, be.Value.floor, be.Value.wallDir, be.Value.floor.y + 0.2f); Wobble(bucket, 3f, 0.008f);
            }
            // picture on a nail (swings)
            var pe = Edge(0.8f, 1.0f);
            if (pe.HasValue && Cast(new Vector3(pe.Value.floor.x, pe.Value.floor.y + 1.6f, pe.Value.floor.z), pe.Value.wallDir, 3f, out var wh))
            {
                var pivot = new GameObject("ASH_Picture (swings)").transform; pivot.SetParent(dress, false);
                pivot.position = wh.point + wh.normal * 0.025f + Vector3.up * 0.2f; pivot.rotation = Quaternion.LookRotation(-wh.normal);
                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube); frame.name = "Frame"; Object.DestroyImmediate(frame.GetComponent<Collider>()); frame.transform.SetParent(pivot, false);
                frame.transform.localPosition = new Vector3(0, -0.2f, 0); frame.transform.localScale = new Vector3(0.42f, 0.34f, 0.025f); Tint(frame, new Color(0.25f, 0.17f, 0.1f));
                var photo = GameObject.CreatePrimitive(PrimitiveType.Quad); photo.name = "Photo"; Object.DestroyImmediate(photo.GetComponent<Collider>()); photo.transform.SetParent(pivot, false);
                photo.transform.localPosition = new Vector3(0, -0.2f, -0.014f); photo.transform.localScale = new Vector3(0.34f, 0.26f, 1f); photo.transform.localEulerAngles = new Vector3(0, 180, 0); Tint(photo, new Color(0.62f, 0.55f, 0.42f));
                var qp = pivot.gameObject.AddComponent<ASHQuakeProp>(); qp.kind = ASHQuakeProp.Kind.Swing; qp.maxAngle = 4f;
            }
            // hanging lantern (swings) near the middle of the room
            var lc = inn.cells.OrderBy(c => Mathf.Abs(Vector3.Distance(c, spawn.position) - 1.3f)).First();
            if (Cast(lc + Vector3.up * 0.3f, Vector3.up, 6f, out var roof))
            {
                var pivot = new GameObject("ASH_HangingLantern (swings)").transform; pivot.SetParent(dress, false); pivot.position = roof.point - Vector3.up * 0.03f;
                float drop = Mathf.Clamp(roof.point.y - lc.y - 2.1f, 0.25f, 1.2f);
                var rope = GameObject.CreatePrimitive(PrimitiveType.Cylinder); rope.name = "Cord"; Object.DestroyImmediate(rope.GetComponent<Collider>()); rope.transform.SetParent(pivot, false);
                rope.transform.localPosition = new Vector3(0, -drop * 0.5f, 0); rope.transform.localScale = new Vector3(0.012f, drop * 0.5f, 0.012f); Tint(rope, new Color(0.2f, 0.18f, 0.15f));
                var lantern = Place("Lantern_ON", pivot, "Lantern", new Vector3(0.16f, 0.26f, 0.16f), new Color(0.9f, 0.75f, 0.4f));
                foreach (var col in lantern.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                foreach (var rb in lantern.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(rb);
                var lb = RB(lantern); lantern.transform.position += new Vector3(pivot.position.x - lb.center.x, pivot.position.y - drop - lb.max.y, pivot.position.z - lb.center.z);
                var qp = pivot.gameObject.AddComponent<ASHQuakeProp>(); qp.kind = ASHQuakeProp.Kind.Swing; qp.maxAngle = 7f;
            }
            _log.Append($"dressing: {dress.childCount} placeholder objects in the cabin\n");
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized; }
        static void Wobble(GameObject g, float angle, float offset) { var q = g.AddComponent<ASHQuakeProp>(); q.kind = ASHQuakeProp.Kind.Wobble; q.maxAngle = angle; q.maxOffset = offset; }
        static void MakeFall(GameObject g)
        {
            if (!g.GetComponentInChildren<Collider>()) { var b = RB(g); var bc = g.AddComponent<BoxCollider>(); bc.center = g.transform.InverseTransformPoint(b.center); bc.size = b.size; }
            var rb = g.GetComponent<Rigidbody>(); if (!rb) rb = g.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.mass = 0.6f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            var q = g.AddComponent<ASHQuakeProp>(); q.kind = ASHQuakeProp.Kind.Fall; q.fallThreshold = 0.55f; q.fallPush = 0.5f; q.maxAngle = 6f;
        }

        static void Pickup(bool water, Transform parent, Vector3 at, float surfaceY)
        {
            var defs = AssetDatabase.FindAssets("t:ASHItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d && d.worldPickupPrefab && (water ? d.restoreThirst > 0f : d.restoreHunger > 0f)).ToList();
            var key = water ? new[] { "water", "bottle" } : new[] { "bean", "can", "food" };
            var def = defs.OrderBy(d => key.Select((k, i) => (d.itemId + d.displayName).ToLowerInvariant().Contains(k) ? i : 99).Min()).FirstOrDefault();
            if (!def) { _log.Append($"  no {(water ? "water" : "food")} item with a pickup prefab\n"); return; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(def.worldPickupPrefab, parent); g.name = $"ASH_Pickup_{def.itemId}";
            var pk = g.GetComponent<ASHItemPickup>(); if (!pk) { pk = g.AddComponent<ASHItemPickup>(); pk.Configure(def, 1); }
            foreach (var rb in g.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            SitOn(g, at, surfaceY);
            _log.Append($"  pickup {def.displayName} at {g.transform.position:F2}\n");
        }
    }
}
