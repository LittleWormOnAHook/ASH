using System.Linq;
using ASH.Rendering;
using ASH.World;
using ASH.World.Weather;
using INab.BetterFog.URP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ASH.EditorTools
{
    /// <summary>ASH > Systems > Weather > Ensure INab Fog: idempotent. (1) adds INab's BetterFog renderer feature (Before Rendering
    /// Post Processing) to every URP renderer used by the URP assets in Assets/Settings; (2) creates Resources/ASH_BetterFogSettings
    /// and Assets/_ASH/Settings/ASH_BetterFogProfile (Better Fog override with snowy-valley starting values) if missing; (3) creates
    /// or updates ONE scene-root object "ASH_Fog" with a global Volume + ASHBetterFogDriver. Marks the scene dirty, never saves it.</summary>
    public static class ASHBetterFogSetup
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_BetterFogSettings.asset";
        const string ProfilePath = "Assets/_ASH/Settings/ASH_BetterFogProfile.asset";

        [MenuItem("ASH/Systems/Weather/Save Fog Profile from Play")]
        public static void SaveFromPlay()
        {
            if (!Application.isPlaying) { Debug.Log("[ASH] FOG Save Fog Profile from Play: only in Play mode (in Edit mode just edit ASH_BetterFogProfile, it saves normally)."); return; }
            var d = Object.FindAnyObjectByType<ASHBetterFogDriver>();
            if (!d) { Debug.LogWarning("[ASH] FOG no ASHBetterFogDriver in the scene"); return; }
            d.SaveProfileToAsset();
        }

        [MenuItem("ASH/Systems/Weather/Ensure INab Fog")]
        public static void Ensure()
        {
            int feats = EnsureFeatures();
            var settings = AssetDatabase.LoadAssetAtPath<ASHBetterFogSettings>(SettingsPath);
            if (!settings)
            {
                settings = ScriptableObject.CreateInstance<ASHBetterFogSettings>();
                System.IO.Directory.CreateDirectory("Assets/_ASH/Resources");
                AssetDatabase.CreateAsset(settings, SettingsPath);
                Debug.Log("[ASH] FOG settings created " + SettingsPath);
            }
            var prof = EnsureProfile();

            var go = GameObject.Find("ASH_Fog");
            bool created = false;
            if (!go) { go = new GameObject("ASH_Fog"); Undo.RegisterCreatedObjectUndo(go, "Create ASH_Fog"); created = true; }
            var vol = go.GetComponent<Volume>(); if (!vol) vol = Undo.AddComponent<Volume>(go);
            vol.isGlobal = true; vol.priority = 60f; vol.weight = 1f; vol.sharedProfile = prof;
            var drv = go.GetComponent<ASHBetterFogDriver>(); if (!drv) drv = Undo.AddComponent<ASHBetterFogDriver>(go);
            drv.volume = vol; drv.settings = settings;
            if (!drv.clock) drv.clock = Object.FindAnyObjectByType<ASHDayNightClock>();
            EditorUtility.SetDirty(vol); EditorUtility.SetDirty(drv);
            EditorSceneManager.MarkSceneDirty(go.scene);

            var cam = Camera.main; var cd = cam ? cam.GetUniversalAdditionalCameraData() : null;
            if (cd && !cd.renderPostProcessing) Debug.LogWarning("[ASH] FOG Main Camera has Post Processing off - Better Fog only renders on cameras with Post Processing on.");
            Debug.Log($"[ASH] FOG ensure done: ASH_Fog {(created ? "created" : "updated")}, features+{feats}, profile={ProfilePath}, sceneFog={(RenderSettings.fog ? RenderSettings.fogMode.ToString() : "off")} (turned off at runtime by the driver), weatherDirector={(Object.FindAnyObjectByType<ASHWeatherDirector>() ? "found" : "MISSING")}, clock={(drv.clock ? "found" : "MISSING")}, cameraPost={(cd ? cd.renderPostProcessing.ToString() : "no main camera")}");
        }

        static int EnsureFeatures()
        {
            int added = 0;
            var datas = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" })
                .Select(g => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a).SelectMany(a => { var so = new SerializedObject(a); var l = so.FindProperty("m_RendererDataList");
                    return Enumerable.Range(0, l.arraySize).Select(i => l.GetArrayElementAtIndex(i).objectReferenceValue as UniversalRendererData); })
                .Where(r => r).Distinct();
            foreach (var data in datas)
            {
                var existing = data.rendererFeatures.OfType<BetterFog>().FirstOrDefault();
                if (existing) continue;
                var f = ScriptableObject.CreateInstance<BetterFog>(); f.name = "BetterFog";
                f.m_Settings.Event = RenderPassEvent.BeforeRenderingPostProcessing;
                AssetDatabase.AddObjectToAsset(f, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long id);
                var so = new SerializedObject(data); var list = so.FindProperty("m_RendererFeatures"); var map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = f;
                map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                so.ApplyModifiedProperties(); data.SetDirty(); EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
                added++; Debug.Log($"[ASH] FOG BetterFog feature added to {AssetDatabase.GetAssetPath(data)}");
            }
            return added;
        }

        static VolumeProfile EnsureProfile()
        {
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (!prof) { prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, ProfilePath); }
            if (!prof.TryGet(out BetterFogVolumeComponent f))
            {
                f = prof.Add<BetterFogVolumeComponent>(false); f.name = "BetterFogVolumeComponent";
                f.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(f, prof);
                // snowy, ash-hazed Colorado valley starting look (the driver takes over in Play mode)
                f._FogIntensity.Override(1f);
                f._FogColor.Override(new Color(0.70f, 0.70f, 0.70f));
                f._UseDistanceFog.Override(true); f._FogType.Override(FogMode.ExponentialSquared);
                f._FogDensity.Override(0.0025f); f._DistanceFogOffset.Override(0f);
                f._UseHeightFog.Override(true); f._HeightDensity.Override(0.12f);
                var t = Terrain.activeTerrain; var p = GameObject.FindWithTag("Player");
                var at = p ? p.transform.position : (Camera.main ? Camera.main.transform.position : Vector3.zero);
                f._Height.Override((t ? t.SampleHeight(at) + t.GetPosition().y : at.y) + 8f);
                f._UseSkyboxHeightFog.Override(true); f._SkyboxFill.Override(0f);
                f._UseSunLight.Override(true); f._SunIntensity.Override(0.35f); f._SunPower.Override(4f);
                f._UseNoise.Override(true); f._NoiseIntensity.Override(0.15f); f._Scale1.Override(45f); f._NoiseDistanceEnd.Override(120f);
                EditorUtility.SetDirty(f); EditorUtility.SetDirty(prof); AssetDatabase.SaveAssetIfDirty(prof);
                Debug.Log("[ASH] FOG profile override created " + ProfilePath);
            }
            return prof;
        }
    }
}