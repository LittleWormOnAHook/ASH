using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// Reproducibly (re)builds Assets/_ASH/Scenes/ASH_Prototype.unity: master terrain, sun, sky, fog,
    /// spawn and objective anchors. Gameplay systems are added by later setup steps.
    /// </summary>
    public static class ASHPrototypeSceneBuilder
    {
        public const string ScenePath = "Assets/_ASH/Scenes/ASH_Prototype.unity";
        public const string SkyboxPath = "Assets/_ASH/Materials/ASH_Sky_Procedural.mat";

        // World XZ anchors chosen from heightmap analysis (see Docs/PrototypeStatus.md).
        public static readonly Vector2 SpawnXZ = new Vector2(1905f, 1655f);
        public static readonly Vector2 OverlookXZ = new Vector2(1312f, 1434f);

        [MenuItem("ASH/World/3. Build Prototype Scene")]
        public static void BuildMenu() => Build();

        public static string Build()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(ASHTerrainImporter.DataPath);
            if (data == null) data = ASHTerrainImporter.ImportOrUpdate();
            if (data.terrainLayers == null || data.terrainLayers.Length == 0) ASHTerrainPainter.Paint(data);

            ASHTerrainImporter.EnsureFolder("Assets/_ASH/Scenes");
            ASHTerrainImporter.EnsureFolder("Assets/_ASH/Materials");
            PreserveOpenSceneEdits();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Terrain
            GameObject terrainGo = Terrain.CreateTerrainGameObject(data);
            terrainGo.name = "ASH_MasterTerrain";
            terrainGo.transform.position = new Vector3(0f, ASHTerrainImporter.WorldYOffset, 0f);
            var terrain = terrainGo.GetComponent<Terrain>();
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 1200f;
            terrain.drawInstanced = true;
            terrain.detailObjectDistance = 120f;
            terrain.detailObjectDensity = 1f;
            terrain.treeDistance = 2500f;
            terrain.treeBillboardDistance = 180f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(terrainGo, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);

            // --- Sun
            var sunGo = new GameObject("ASH_Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(28f, 125f, 0f); // morning, low light raking across the prairie

            // --- Sky / ambient / fog (weather-ready: values later driven by the day/night controller)
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, SkyboxPath);
            }
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_AtmosphereThickness", 1.05f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.58f, 0.66f));
            sky.SetColor("_GroundColor", new Color(0.42f, 0.39f, 0.34f));
            sky.SetFloat("_Exposure", 1.15f);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.70f, 0.72f, 0.72f);
            RenderSettings.fogDensity = 0.00045f;

            // --- Anchors
            Vector3 spawn = Ground(terrain, SpawnXZ) + Vector3.up * 0.1f;
            Vector3 overlook = Ground(terrain, OverlookXZ);
            var spawnGo = new GameObject("ASH_PlayerSpawn");
            spawnGo.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(Flat(overlook - spawn)));
            var objectiveGo = new GameObject("ASH_Objective_RidgeOverlook");
            objectiveGo.transform.position = overlook;

            // --- Temporary preview camera at the spawn (replaced by the player rig)
            var camGo = new GameObject("ASH_PreviewCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 6000f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.SetPositionAndRotation(spawn + Vector3.up * 1.7f - spawnGo.transform.forward * 3.5f + Vector3.up * 0.6f,
                Quaternion.LookRotation(Flat(overlook - spawn)) * Quaternion.Euler(4f, 0f, 0f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log($"[ASH] Prototype scene built. Spawn {spawn}, overlook {overlook}, distance {Vector3.Distance(spawn, overlook):F0} m.");
            return ScenePath;
        }

        /// <summary>Never discard someone's unsaved work when the builder replaces the open scene.</summary>
        public static void PreserveOpenSceneEdits()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;
                if (string.IsNullOrEmpty(s.path))
                {
                    ASHTerrainImporter.EnsureFolder("Assets/_ASH/Scenes/Scratch");
                    string p = $"Assets/_ASH/Scenes/Scratch/Unsaved_{System.DateTime.Now:yyyyMMdd_HHmmss}.unity";
                    EditorSceneManager.SaveScene(s, p);
                    Debug.Log("[ASH] Saved untitled open scene to " + p);
                }
                else
                {
                    EditorSceneManager.SaveScene(s);
                    Debug.Log("[ASH] Saved open scene edits before rebuilding: " + s.path);
                }
            }
        }

        public static Vector3 Ground(Terrain t, Vector2 xz)
        {
            var p = new Vector3(xz.x, 0f, xz.y);
            p.y = t.SampleHeight(p + t.transform.position) + t.transform.position.y;
            return p;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized; }

        private static void AddToBuildSettings(string path)
        {
            var list = EditorBuildSettings.scenes.ToList();
            list.RemoveAll(s => s.path == path);
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
