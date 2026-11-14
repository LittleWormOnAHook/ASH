using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.FX;
using ASH.Player;
using ASH.PostFX;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ASH.EditorTools.Batch
{
    /// <summary>ASH/Player + ASH/Rendering menus: footstep surface map, puff material, post profiles, SSAO tune, player prefab wiring.</summary>
    public static class ASHFootstepPostTools
    {
        const string PrefabPath = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        const string MapPath = "Assets/_ASH/Settings/ASH_FootstepSurfaceMap.asset";
        const string PuffTex = "Assets/_ASH/Art/FX/ASH_SoftPuff.png", PuffMat = "Assets/_ASH/Art/FX/ASH_FootstepPuff.mat";
        const string PostPath = "Assets/_ASH/Settings/ASH_PostProfile.asset", PostMobilePath = "Assets/_ASH/Settings/ASH_PostProfile_Mobile.asset";
        const string Audio = "Assets/_ASH/Audio/Footsteps", InvAudio = "Assets/Invector-3rdPersonController/Basic Locomotion/Audio/FootSteps";

        static void Folder(string path) { var parts = path.Split('/'); var cur = parts[0]; for (int i = 1; i < parts.Length; i++) { var n = cur + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(n)) AssetDatabase.CreateFolder(cur, parts[i]); cur = n; } }
        static AudioClip[] Clips(string folder) => !AssetDatabase.IsValidFolder(folder) ? new AudioClip[0] :
            AssetDatabase.FindAssets("t:AudioClip", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c).ToArray();
        static ASHFootstepSurfaceMap.Rule R(string s, params string[] k) => new ASHFootstepSurfaceMap.Rule { surface = s, keywords = k };

        [MenuItem("ASH/Player/Setup Footsteps + Puffs + Post Stack", priority = 21)]
        public static void SetupAll() { Map(); Puff(); Post(); Ssao(); Prefab(); AssetDatabase.SaveAssets(); }

        public static ASHFootstepSurfaceMap Map()
        {
            Folder("Assets/_ASH/Settings");
            var map = AssetDatabase.LoadAssetAtPath<ASHFootstepSurfaceMap>(MapPath); bool created = !map;
            if (created) { map = ScriptableObject.CreateInstance<ASHFootstepSurfaceMap>(); AssetDatabase.CreateAsset(map, MapPath); }
            var gravel = Clips(InvAudio + "/Gravel"); var metal = Clips(InvAudio + "/Metal");
            if (created || map.surfaces.Count == 0)
            {
                map.surfaces = new List<ASHFootstepSurfaceMap.Surface>
                {
                    new ASHFootstepSurfaceMap.Surface { id = "Snow", clips = Clips(Audio + "/Snow"), volume = 0.55f, puffs = true, deepVolumeBoost = 0.7f, deepPitchDrop = 0.14f, deepLayerClips = gravel },
                    new ASHFootstepSurfaceMap.Surface { id = "Dirt", clips = Clips(Audio + "/Dirt"), volume = 0.55f, deepVolumeBoost = 0f, deepPitchDrop = 0f },
                    new ASHFootstepSurfaceMap.Surface { id = "Gravel", clips = gravel, volume = 0.5f, deepVolumeBoost = 0f, deepPitchDrop = 0f },
                    new ASHFootstepSurfaceMap.Surface { id = "Stone", clips = Clips(Audio + "/Stone"), volume = 0.6f, deepVolumeBoost = 0f, deepPitchDrop = 0f },
                    new ASHFootstepSurfaceMap.Surface { id = "Wood", clips = Clips(Audio + "/Wood"), volume = 0.65f, deepVolumeBoost = 0f, deepPitchDrop = 0f },
                    new ASHFootstepSurfaceMap.Surface { id = "Metal", clips = metal, volume = 0.55f, deepVolumeBoost = 0f, deepPitchDrop = 0f },
                };
                map.terrainDefault = "Snow"; map.meshDefault = "Stone";
                map.terrainLayerRules = new List<ASHFootstepSurfaceMap.Rule> { R("Gravel", "road", "gravel", "asphalt"), R("Dirt", "dirt", "mud", "soil", "path", "trail"), R("Stone", "rock", "cliff", "stone") };
                map.physicMaterialRules = new List<ASHFootstepSurfaceMap.Rule> { R("Wood", "wood"), R("Stone", "stone", "rock", "concrete"), R("Metal", "metal"), R("Gravel", "gravel"), R("Dirt", "dirt", "mud"), R("Snow", "snow", "ice") };
                map.tagRules = new List<ASHFootstepSurfaceMap.Rule>();
                map.layerRules = new List<ASHFootstepSurfaceMap.LayerRule>();
                map.objectNameRules = new List<ASHFootstepSurfaceMap.Rule>
                {
                    R("Wood", "wood", "plank", "log", "cabin", "deck", "floor", "porch", "barn", "shed", "crate", "pallet", "bridge", "timber"),
                    R("Metal", "metal", "car", "truck", "vehicle", "tank", "pipe", "container"),
                    R("Stone", "rock", "stone", "cliff", "boulder", "concrete", "brick")
                };
                EditorUtility.SetDirty(map);
            }
            var t = Terrain.activeTerrain;
            if (t) foreach (var l in t.terrainData.terrainLayers.Where(x => x)) Debug.Log($"[ASH] BATCH terrain layer '{l.name}' ({(l.diffuseTexture ? l.diffuseTexture.name : "-")}) -> {ASHFootstepSurfaceMap.Match(map.terrainLayerRules, l.name + " " + (l.diffuseTexture ? l.diffuseTexture.name : "")) ?? map.terrainDefault + " (default)"}");
            Debug.Log($"[ASH] BATCH surface map {MapPath}: " + string.Join(", ", map.surfaces.Select(s => $"{s.id} {s.clips.Length} clips")));
            return map;
        }

        public static Material Puff()
        {
            Folder("Assets/_ASH/Art/FX");
            if (!File.Exists(PuffTex))
            {
                var tex = ASHFootstepPuffs.SoftTexture(128); File.WriteAllBytes(PuffTex, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(PuffTex);
                var imp = (TextureImporter)AssetImporter.GetAtPath(PuffTex); imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.sRGBTexture = true; imp.SaveAndReimport();
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(PuffMat);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Simple Lit")); AssetDatabase.CreateAsset(mat, PuffMat); }
            ASHFootstepPuffs.SetupMaterial(mat, AssetDatabase.LoadAssetAtPath<Texture2D>(PuffTex)); EditorUtility.SetDirty(mat);
            return mat;
        }

        static T Get<T>(VolumeProfile p) where T : VolumeComponent { if (!p.TryGet<T>(out var c)) { c = p.Add<T>(true); } c.active = true; return c; }

        /// <summary>URP translation of Genesis 'BOTD Post Processing' (HDRP). See Docs/PostProcessing.md.</summary>
        public static void Post()
        {
            Folder("Assets/_ASH/Settings");
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath); if (!p) { p = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(p, PostPath); }
            var tm = Get<Tonemapping>(p); tm.mode.Override(TonemappingMode.ACES);
            var ca = Get<ColorAdjustments>(p); ca.postExposure.Override(0f); ca.contrast.Override(8f); ca.saturation.Override(-8f); ca.colorFilter.Override(Color.white); ca.hueShift.Override(0f);
            var wb = Get<WhiteBalance>(p); wb.temperature.Override(-15f); wb.tint.Override(0f);
            var bl = Get<Bloom>(p); bl.threshold.Override(1.0f); bl.intensity.Override(0.35f); bl.scatter.Override(0.66f); bl.tint.Override(new Color(0.977f, 0.949f, 0.787f)); bl.highQualityFiltering.Override(true);
            var vg = Get<Vignette>(p); vg.intensity.Override(0.2f); vg.smoothness.Override(0.4f); vg.color.Override(Color.black); vg.rounded.Override(false);
            var fg = Get<FilmGrain>(p); fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(0.15f); fg.response.Override(0.8f);
            var dof = Get<DepthOfField>(p); dof.mode.Override(DepthOfFieldMode.Off); dof.gaussianStart.Override(40f); dof.gaussianEnd.Override(200f); dof.gaussianMaxRadius.Override(1f); dof.highQualitySampling.Override(false);
            var mb = Get<MotionBlur>(p); mb.mode.Override(MotionBlurMode.CameraOnly); mb.quality.Override(MotionBlurQuality.Low); mb.intensity.Override(0.15f); mb.clamp.Override(0.05f);
            var lgg = Get<LiftGammaGain>(p); lgg.lift.Override(new Vector4(0.99f, 1f, 1.02f, 0f)); lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f)); lgg.gain.Override(new Vector4(1f, 1f, 1f, 0f));
            var smh = Get<ShadowsMidtonesHighlights>(p); smh.shadows.Override(new Vector4(0.98f, 1f, 1.03f, 0f)); smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f)); smh.highlights.Override(new Vector4(1.01f, 1f, 0.99f, 0f));
            var chr = Get<ChromaticAberration>(p); chr.intensity.Override(0.03f);
            EditorUtility.SetDirty(p);

            var m = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostMobilePath); if (!m) { m = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(m, PostMobilePath); }
            Get<Tonemapping>(m).mode.Override(TonemappingMode.ACES);
            var mca = Get<ColorAdjustments>(m); mca.contrast.Override(8f); mca.saturation.Override(-8f);
            var mbl = Get<Bloom>(m); mbl.threshold.Override(1.1f); mbl.intensity.Override(0.25f); mbl.highQualityFiltering.Override(false);
            var mvg = Get<Vignette>(m); mvg.intensity.Override(0.18f); mvg.smoothness.Override(0.4f);
            EditorUtility.SetDirty(m);
            Debug.Log($"[ASH] BATCH post profiles: {PostPath} ({p.components.Count} effects), {PostMobilePath} ({m.components.Count})");
        }

        /// <summary>PC renderer SSAO (already present): stronger, so tracks read through AO (the ash surface casts no shadows).</summary>
        public static void Ssao()
        {
            var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/PC_Renderer.asset"); if (!rd) { Debug.LogWarning("[ASH] BATCH no PC_Renderer"); return; }
            var f = rd.rendererFeatures.FirstOrDefault(x => x && x.GetType().Name == "ScreenSpaceAmbientOcclusion");
            if (!f) { Debug.LogWarning("[ASH] BATCH PC_Renderer has no SSAO feature (add it in the renderer: Add Renderer Feature > Screen Space Ambient Occlusion)"); return; }
            var so = new SerializedObject(f); var s = so.FindProperty("m_Settings");
            void Set(string n, float v) { var q = s.FindPropertyRelative(n); if (q != null) { if (q.propertyType == SerializedPropertyType.Float) q.floatValue = v; else if (q.propertyType == SerializedPropertyType.Enum || q.propertyType == SerializedPropertyType.Integer) q.intValue = (int)v; else if (q.propertyType == SerializedPropertyType.Boolean) q.boolValue = v > 0.5f; } }
            Set("Intensity", 0.8f); Set("Radius", 0.3f); Set("DirectLightingStrength", 0.25f); Set("Source", 1); Set("AfterOpaque", 0);
            f.SetActive(true); so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rd);
            Debug.Log($"[ASH] BATCH SSAO on PC_Renderer: intensity {s.FindPropertyRelative("Intensity")?.floatValue}, radius {s.FindPropertyRelative("Radius")?.floatValue}, source {s.FindPropertyRelative("Source")?.intValue} (1 = depth normals)");
        }

        public static void Prefab()
        {
            var map = AssetDatabase.LoadAssetAtPath<ASHFootstepSurfaceMap>(MapPath); var mat = AssetDatabase.LoadAssetAtPath<Material>(PuffMat);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var fs = root.GetComponent<ASHFootsteps>(); if (!fs) fs = root.AddComponent<ASHFootsteps>(); fs.map = map;
                var pf = root.GetComponent<ASHFootstepPuffs>(); if (!pf) pf = root.AddComponent<ASHFootstepPuffs>(); pf.material = mat; fs.puffs = pf;
                var pp = root.GetComponent<ASHPostProcessing>(); if (!pp) pp = root.AddComponent<ASHPostProcessing>();
                pp.profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath); pp.mobileProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostMobilePath);
                var vfs = root.GetComponent<Invector.vFootStep>(); if (vfs) vfs.enabled = false; // unconfigured (no foot triggers); ASHFootsteps replaces it
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[ASH] BATCH prefab {PrefabPath}: ASHFootsteps, ASHFootstepPuffs, ASHPostProcessing; Invector vFootStep {(vfs ? "disabled" : "not present")}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>exec: Play Mode batch check (FPS before/after post, footstep surfaces, puffs).</summary>
        public static void PlayBatchCheck() => ASH.DebugTools.ASHBatchCheck.Run();
    }
}
