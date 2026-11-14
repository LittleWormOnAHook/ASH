using UnityEngine;

namespace ASH.World.Accumulation
{
    /// <summary>
    /// SINGLE SOURCE OF TRUTH for ash/snow surface accumulation (and, from phase 2, deformation).
    /// Layered: snow underneath, ash on top, base surface below. Holds the layer state, simulates it from weather
    /// inputs (SetWeatherInputs / IASHWeatherInputs) and drives the global shader properties read by
    /// ASH/Accumulation Lit and ASH/Terrain Lit Accum. See Docs/AccumulationSystem.md.
    /// </summary>
    [ExecuteAlways, DefaultExecutionOrder(-200), DisallowMultipleComponent]
    public sealed class ASHAccumulationController : MonoBehaviour
    {
        public static ASHAccumulationController Instance { get; private set; }
        public enum Preset { Light, Default, Heavy, Extreme }
        public enum DeformQuality { Off, Low, Medium, High, Ultra, Extreme, Custom }

        [Header("Preset (use the context menu or ASH/World/Accumulation/Preset)")]
        public Preset preset = Preset.Default;

        [Header("Layer state (relative depth 0..1; 1 = full design depth)")]
        [Range(0f, 1f)] public float ashAmount = 0.7f;
        [Range(0f, 1f)] public float snowAmount = 0.6f;
        [Tooltip("Depth in metres of the deepest drifts (knee-deep), drift mask R = 1.")]
        [Range(0.05f, 2f)] public float maxDepthMetres = 0.5f;
        [Tooltip("Typical (ankle) depth in metres where the drift mask is average; drifts rise toward Max Depth (knee).")]
        [Range(0.01f, 1f)] public float ankleDepthMetres = 0.12f;

        [Header("1. Accumulation rate")]
        [Tooltip("Relative depth gained per minute at fall intensity 1.")]
        [Range(0f, 2f)] public float accumulationRate = 0.05f;

        [Header("2. Surface coverage")]
        [Range(0f, 2f)] public float coverage = 1f;
        [Tooltip("Surface normal Y above which material settles (lower = steeper faces collect).")]
        [Range(-1f, 1f)] public float normalThreshold = 0.45f;
        [Range(0.01f, 1f)] public float normalBlend = 0.35f;
        [Range(0.01f, 5f)] public float noiseScale = 0.35f;
        [Range(0f, 1.5f)] public float noiseStrength = 0.35f;

        [Header("3. Deformation (footprints, trails, tracks; ASHDeformer stampers)")]
        [Tooltip("Footprints/trails on (Play Mode). Stamps from ASHDeformer components into ASHDeformationField.")]
        public bool deformation = true;
        [Tooltip("Fraction of the layer a footprint pushes aside (1 = down to the ground).")]
        [Range(0f, 1f)] public float footprintDepth = 0.8f;
        [Tooltip("Trail refill speed as a multiple of the accumulation rate x current fall intensity (new ash/snow fills prints).")]
        [Range(0f, 10f)] public float deformationRefill = 1f;
        [Tooltip("Extra settling refill per minute even without new fall (0 = trails persist until new ash/snow falls).")]
        [Range(0f, 1f)] public float trailFadePerMinute = 0f;
        [Header("Visible depth (player-following displaced surface)")]
        [Tooltip("Real geometric depth near the camera (ASHAccumulationSurface).")]
        public bool visibleDepth = true;
        public Shader surfaceShader, deformShader;

        [Header("Deformation quality (graphics setting; runtime-safe via SetDeformationQuality)")]
        [Tooltip("Off = shader-only coverage (no displaced surface, no trail field). Custom = keep the values below.")]
        public DeformQuality deformationQuality = DeformQuality.Medium;
        [Tooltip("Deformation render texture texels per side.")] [Range(256, 4096)] public int deformResolution = 1024;
        [Tooltip("Radius of the area that records footprints/trails around the player, metres (window = 2x).")] [Range(8f, 256f)] public float deformAreaRadius = 64f;
        [Tooltip("Radius of the displaced (real-depth) mesh around the camera, metres.")] [Range(4f, 128f)] public float displacedRadius = 24f;
        [Tooltip("Mesh grid spacing, metres (smaller = denser mesh, sharper prints).")] [Range(0.05f, 1f)] public float meshDensity = 0.2f;
        [Tooltip("Stamp/scroll updates per second (0 = every frame).")] [Range(0f, 120f)] public float stampUpdateRate = 30f;
        [Tooltip("Refill updates per second while idle.")] [Range(0.5f, 60f)] public float refillUpdateRate = 4f;
        [Tooltip("Displacement fades to shader-only coverage over this distance at the mesh edge, metres.")] [Range(0.5f, 40f)] public float displacementFade = 8f;
        [Tooltip("0 Low (coarse vertex normals), 1 Medium (vertex normals), 2 High (+ per-pixel footprint normals).")] [Range(0, 2)] public int normalQuality = 1;
        [Tooltip("CPU readback of the trail field for gameplay depth queries: radius around the follow target, metres.")] [Range(4f, 64f)] public float readbackRadius = 16f;

        [Header("4. Wind redistribution")]
        [Tooltip("How strongly wind strips exposed surfaces and piles drifts in lee/hollows (drift mask).")]
        [Range(0f, 1f)] public float windRedistribution = 0.7f;
        [Tooltip("Prevailing wind (blows TOWARDS, world XZ). Used to bake the drift mask.")]
        public Vector2 prevailingWind = new Vector2(1f, 0f);

        [Header("5. Rain and melting")]
        [Range(0f, 1f)] public float wetness = 0f;
        [Tooltip("Snow lost per minute per degree C above 0.")]
        [Range(0f, 1f)] public float meltPerDegree = 0.01f;
        [Range(0f, 10f)] public float rainMeltBoost = 2f;
        [Tooltip("Wetness gain per minute at rain 1.")] [Range(0f, 5f)] public float wetRate = 0.6f;
        [Tooltip("Wetness loss per minute when dry.")] [Range(0f, 5f)] public float dryRate = 0.05f;
        [Tooltip("Extra visual melt of the snow layer (manual).")] [Range(0f, 1f)] public float extraMelt = 0f;

        [Header("6. Persistence (0 = permanent)")]
        [Range(0f, 1f)] public float ashDecayPerMinute = 0f;
        [Range(0f, 1f)] public float snowDecayPerMinute = 0f;

        [Header("Look")]
        public Color ashColor = new Color(0.44f, 0.44f, 0.46f);
        [Range(0.1f, 1f)] public float wetAshDarkness = 0.5f;
        public Color snowColor = new Color(0.93f, 0.94f, 0.96f);
        public Texture2D ashTexture, snowTexture;
        [Range(0.2f, 20f)] public float ashTiling = 3f, snowTiling = 4f;
        [Tooltip("Mean linear luminance of the ash texture; it is used as detail around Ash Color (0.05 for ASH_TL_Ash_D).")]
        [Range(0.005f, 1f)] public float ashTextureMean = 0.05f;

        [Header("Terrain")]
        public Terrain terrain;
        [Tooltip("RGBA: R depth01 (0.35 ankle .. 1 knee drift), G wind exposure, B slope keep. Readable.")]
        public Texture2D driftMask;

        [Header("Main paths (ASH_RegionLayout roads/trails; drift mask A channel)")]
        [Tooltip("Depth removed on the path itself (0 = none, 1 = bare). Runtime; also lowers GetDepthMetres for speed/deformation.")]
        [Range(0f, 1f)] public float pathDepthReduction = 0.6f;
        [Tooltip("Visual coverage removed on paths (0 = none, 1 = fully clear).")]
        [Range(0f, 1f)] public float pathCoverageReduction = 0.6f;
        [Tooltip("Falloff beyond the painted path edge, metres (bake: re-run Generate Drift Mask).")]
        [Range(0f, 40f)] public float pathFalloffMetres = 5f;
        [Tooltip("Extra corridor added to the painted half-width, metres (bake).")]
        [Range(0f, 10f)] public float pathCorridorExtra = 0.5f;
        public Material terrainMaterial;
        [HideInInspector] public Material originalTerrainMaterial;
        public bool applyTerrainMaterial = true;

        [Header("Weather inputs")]
        [Tooltip("Optional component implementing IASHWeatherInputs (e.g. ASHWeatherDirector). Polled every frame.")]
        public MonoBehaviour weatherSource;
        [Tooltip("Used while nothing calls SetWeatherInputs and no weatherSource is set.")]
        public ASHWeatherInputs ambientWeather = ASHWeatherInputs.Calm;
        public bool simulateInPlayMode = true;

        ASHWeatherInputs _inputs; bool _hasExternal; float _lastExternal = -999f;
        public ASHWeatherInputs CurrentInputs => _inputs;

        // ------------------------------------------------------------- public API
        /// <summary>Push weather (call every frame or whenever it changes). Overrides ambientWeather for 5 s.</summary>
        public void SetWeatherInputs(in ASHWeatherInputs w) { _inputs = w; _hasExternal = true; _lastExternal = Time.time; }
        public void SetWeatherInputs(float ashfall, float snowfall, Vector2 windDir, float windStrength, float temperatureC, float rain)
            => SetWeatherInputs(new ASHWeatherInputs { ashfallIntensity = ashfall, snowfallIntensity = snowfall, windDirection = windDir, windStrength = windStrength, temperatureC = temperatureC, rainIntensity = rain });

        /// <summary>Material depth in metres at a world position (for speed/footstep systems).</summary>
        public float GetDepthMetres(Vector3 worldPos)
        {
            return GetBaseDepthMetres(worldPos) * (1f - GetDeformation(worldPos));
        }

        /// <summary>Depth before any deformation (the undisturbed layer).</summary>
        public float GetBaseDepthMetres(Vector3 worldPos)
        {
            var dm = SampleDrift(worldPos);
            float r = Mathf.Lerp(0.35f, dm.r, Mathf.Clamp01(EffectiveWind() * 2f)); // same curve as ASH_DepthMetres (shader)
            float d = r <= 0.35f ? ankleDepthMetres * r / 0.35f : Mathf.Lerp(ankleDepthMetres, maxDepthMetres, Mathf.SmoothStep(0f, 1f, (r - 0.35f) / 0.65f));
            return Mathf.Clamp01(ashAmount + snowAmount * (1f - extraMelt)) * (1f - pathDepthReduction * dm.a) * coverage * d;
        }

        /// <summary>Relative ash / snow layer thickness at a position (0..1 each) before deformation.</summary>
        public void GetLayers(Vector3 worldPos, out float ash, out float snow)
        {
            var dm = SampleDrift(worldPos);
            float mask = Mathf.Lerp(0.5f, dm.r, EffectiveWind()) * (1f - pathDepthReduction * dm.a) * coverage;
            ash = ashAmount * mask; snow = snowAmount * (1f - extraMelt) * mask;
        }

        /// <summary>One-shot disturbance (explosion, dropped object, tire): pushes material aside around a point.</summary>
        public void Deform(Vector3 worldPos, float radius, float strength) { if (Field) Field.AddStamp(worldPos, worldPos, radius, strength); }
        /// <summary>Segment disturbance (drag, tire track) from a to b.</summary>
        public void DeformSegment(Vector3 a, Vector3 b, float radius, float strength) { if (Field) Field.AddStamp(a, b, radius, strength); }
        /// <summary>0 = untouched .. 1 = pushed to the ground (CPU readback of the deformation field, ~0.15 s latency).</summary>
        public float GetDeformation(Vector3 worldPos) => Field ? Field.Sample(worldPos) : 0f;
        /// <summary>Refill of trails per second (fraction of layer), from new fall and settling.</summary>
        public float DeformRefillPerSecond() => (deformationRefill * accumulationRate * (_inputs.ashfallIntensity + _inputs.snowfallIntensity) + trailFadePerMinute) / 60f;
        /// <summary>Graphics-menu entry point: applies a deformation quality preset at runtime (rebuilds RT/mesh cleanly).</summary>
        public void SetDeformationQuality(DeformQuality q) { ApplyDeformationQualityPreset(q); deformationQuality = q; ApplyQualityToComponents(); }
        /// <summary>Custom values at runtime (graphics menu sliders). Any argument &lt; 0 keeps the current value.</summary>
        public void SetDeformationQualityCustom(int resolution = -1, float areaRadius = -1, float meshRadius = -1, float gridStep = -1, float stampHz = -1, float refillHz = -1, float fade = -1, int normals = -1)
        {
            deformationQuality = DeformQuality.Custom;
            if (resolution > 0) deformResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 256, 4096);
            if (areaRadius > 0) deformAreaRadius = Mathf.Clamp(areaRadius, 8f, 256f);
            if (meshRadius > 0) displacedRadius = Mathf.Clamp(meshRadius, 4f, 128f);
            if (gridStep > 0) meshDensity = Mathf.Clamp(gridStep, 0.05f, 1f);
            if (stampHz >= 0) stampUpdateRate = Mathf.Clamp(stampHz, 0f, 120f);
            if (refillHz > 0) refillUpdateRate = Mathf.Clamp(refillHz, 0.5f, 60f);
            if (fade > 0) displacementFade = Mathf.Clamp(fade, 0.5f, 40f);
            if (normals >= 0) normalQuality = Mathf.Clamp(normals, 0, 2);
            ApplyQualityToComponents();
        }
        public bool DeformationQualityOn => deformationQuality != DeformQuality.Off;

        public void ApplyDeformationQualityPreset(DeformQuality q)
        {
            //                    res   area  mesh  grid    stampHz refillHz fade normals
            switch (q)
            {
                case DeformQuality.Low:     SetQ(512,  32f,  16f, 0.33f,  20f,  2f,  5f, 0); break;
                case DeformQuality.Medium:  SetQ(1024, 64f,  24f, 0.2f,   30f,  4f,  8f, 1); break;
                case DeformQuality.High:    SetQ(2048, 64f,  36f, 0.15f,  60f, 10f, 12f, 2); break;
                case DeformQuality.Ultra:   SetQ(2048, 96f,  48f, 0.1f,    0f, 20f, 16f, 2); break;
                case DeformQuality.Extreme: SetQ(4096, 128f, 64f, 0.075f,  0f, 30f, 24f, 2); break;
            }
        }
        void SetQ(int res, float area, float mesh, float grid, float stamp, float refill, float fade, int normals)
        { deformResolution = res; deformAreaRadius = area; displacedRadius = mesh; meshDensity = grid; stampUpdateRate = stamp; refillUpdateRate = refill; displacementFade = fade; normalQuality = normals; }

        /// <summary>Single place the quality values reach the field/surface (they rebuild themselves when sizes change).</summary>
        public void ApplyQualityToComponents()
        {
            if (Field)
            {
                Field.resolution = deformResolution; Field.sizeMetres = deformAreaRadius * 2f;
                Field.stampRate = stampUpdateRate; Field.refillRate = refillUpdateRate; Field.readbackRadius = readbackRadius;
            }
            if (Surface) { Surface.size = displacedRadius * 2f; Surface.gridStep = meshDensity; Surface.edgeFade = displacementFade; Surface.normalQuality = normalQuality; }
        }
        DeformQuality _appliedQ = (DeformQuality)(-1);

        public ASHDeformationField Field { get; private set; }
        public ASHAccumulationSurface Surface { get; private set; }

        public void ApplyPreset(Preset p)
        {
            preset = p;
            switch (p)
            {
                case Preset.Light: ashAmount = 0.35f; snowAmount = 0.3f; coverage = 0.8f; accumulationRate = 0.02f; windRedistribution = 0.6f; noiseStrength = 0.45f; normalThreshold = 0.5f; break;
                case Preset.Default: ashAmount = 0.7f; snowAmount = 0.6f; coverage = 1f; accumulationRate = 0.05f; windRedistribution = 0.7f; noiseStrength = 0.35f; normalThreshold = 0.45f; break;
                case Preset.Heavy: ashAmount = 0.95f; snowAmount = 0.85f; coverage = 1.3f; accumulationRate = 0.1f; windRedistribution = 0.8f; noiseStrength = 0.25f; normalThreshold = 0.35f; break;
                case Preset.Extreme: ashAmount = 1f; snowAmount = 1f; coverage = 2f; accumulationRate = 0.5f; windRedistribution = 1f; noiseStrength = 0.15f; normalThreshold = 0.15f; break;
            }
            PushGlobals();
        }
        [ContextMenu("Preset/Light")] void PL() => ApplyPreset(Preset.Light);
        [ContextMenu("Preset/Default")] void PD() => ApplyPreset(Preset.Default);
        [ContextMenu("Preset/Heavy")] void PH() => ApplyPreset(Preset.Heavy);
        [ContextMenu("Preset/Extreme")] void PE() => ApplyPreset(Preset.Extreme);

        // ------------------------------------------------------------- lifecycle
        void OnEnable()
        {
            Instance = this; _inputs = ambientWeather;
            if (!terrain) terrain = Terrain.activeTerrain;
            if (applyTerrainMaterial && terrain && terrainMaterial && terrain.materialTemplate != terrainMaterial)
            {
                if (!originalTerrainMaterial) originalTerrainMaterial = terrain.materialTemplate;
                terrain.materialTemplate = terrainMaterial;
            }
            PushGlobals();
            if (Application.isPlaying)
            {
                Field = GetComponent<ASHDeformationField>(); if (!Field) Field = gameObject.AddComponent<ASHDeformationField>();
                if (!Field.updateShader) Field.updateShader = deformShader ? deformShader : Shader.Find("Hidden/ASH/DeformUpdate");
                Surface = GetComponent<ASHAccumulationSurface>(); if (!Surface) Surface = gameObject.AddComponent<ASHAccumulationSurface>();
                if (!Surface.shader) Surface.shader = surfaceShader ? surfaceShader : Shader.Find("Hidden/ASH/Accumulation Surface");
            }
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalVector(ID_Layers, Vector4.zero); // shaders go inert without a controller
        }

        void OnValidate() { if (isActiveAndEnabled) PushGlobals(); }

        void Update()
        {
            if (deformationQuality != _appliedQ) { if (deformationQuality != DeformQuality.Custom) ApplyDeformationQualityPreset(deformationQuality); _appliedQ = deformationQuality; }
            if (Application.isPlaying) ApplyQualityToComponents();
            if (Application.isPlaying && simulateInPlayMode) Simulate(Time.deltaTime);
            PushGlobals();
        }

        void Simulate(float dt)
        {
            if (weatherSource is IASHWeatherInputs src) _inputs = src.GetWeatherInputs();
            else if (!_hasExternal || Time.time - _lastExternal > 5f) { _inputs = ambientWeather; _hasExternal = false; }
            float m = dt / 60f; var w = _inputs;
            ashAmount = Mathf.Clamp01(ashAmount + accumulationRate * w.ashfallIntensity * m);
            snowAmount = Mathf.Clamp01(snowAmount + accumulationRate * w.snowfallIntensity * m);
            if (w.temperatureC > 0f) snowAmount = Mathf.Clamp01(snowAmount - meltPerDegree * w.temperatureC * (1f + rainMeltBoost * w.rainIntensity) * m);
            float wetTarget = Mathf.Max(w.rainIntensity, w.temperatureC > 0f && snowAmount > 0.01f ? 0.35f : 0f);
            wetness = wetTarget > wetness ? Mathf.MoveTowards(wetness, wetTarget, wetRate * m) : Mathf.MoveTowards(wetness, wetTarget, dryRate * m);
            ashAmount = Mathf.Clamp01(ashAmount - ashDecayPerMinute * m);
            snowAmount = Mathf.Clamp01(snowAmount - snowDecayPerMinute * m);
            // Deformation refill: ASHDeformationField applies DeformRefillPerSecond() each frame.
        }

        float EffectiveWind() => Mathf.Clamp01(windRedistribution * (0.5f + (Application.isPlaying ? _inputs.windStrength : ambientWeather.windStrength)));

        static readonly int ID_Layers = Shader.PropertyToID("_ASH_Layers"), ID_Surface = Shader.PropertyToID("_ASH_Surface"), ID_Weather = Shader.PropertyToID("_ASH_Weather"),
            ID_Noise = Shader.PropertyToID("_ASH_Noise"), ID_AshCol = Shader.PropertyToID("_ASH_AccumColor"), ID_SnowCol = Shader.PropertyToID("_ASH_SnowColor"), ID_TexNorm = Shader.PropertyToID("_ASH_TexNorm"),
            ID_Rect = Shader.PropertyToID("_ASH_TerrainRect"), ID_DRect = Shader.PropertyToID("_ASH_DeformRect"),
            ID_AshTex = Shader.PropertyToID("_ASH_AshTex"), ID_SnowTex = Shader.PropertyToID("_ASH_SnowTex"), ID_Drift = Shader.PropertyToID("_ASH_DriftMask"), ID_Deform = Shader.PropertyToID("_ASH_DeformTex"), ID_Depth = Shader.PropertyToID("_ASH_Depth");

        public void PushGlobals()
        {
            var wd = (Application.isPlaying ? _inputs.windDirection : prevailingWind); if (wd.sqrMagnitude < 1e-4f) wd = prevailingWind; wd.Normalize();
            Shader.SetGlobalVector(ID_Layers, new Vector4(ashAmount, snowAmount, Mathf.Clamp01(ashAmount + snowAmount), enabled ? 1f : 0f));
            Shader.SetGlobalVector(ID_Surface, new Vector4(coverage, normalThreshold, normalBlend, EffectiveWind()));
            Shader.SetGlobalVector(ID_Weather, new Vector4(wetness, extraMelt, wd.x, wd.y));
            Shader.SetGlobalVector(ID_Noise, new Vector4(noiseScale, noiseStrength, ashTiling, snowTiling));
            Shader.SetGlobalColor(ID_AshCol, new Color(ashColor.r, ashColor.g, ashColor.b, wetAshDarkness));
            Shader.SetGlobalColor(ID_SnowCol, snowColor);
            Shader.SetGlobalVector(ID_TexNorm, new Vector4(1f / Mathf.Max(ashTextureMean, 0.005f), driftMask ? pathDepthReduction : 0f, driftMask ? pathCoverageReduction : 0f, 0f));
            Shader.SetGlobalTexture(ID_AshTex, ashTexture ? ashTexture : Texture2D.grayTexture);
            Shader.SetGlobalTexture(ID_SnowTex, snowTexture ? snowTexture : Texture2D.whiteTexture);
            Shader.SetGlobalTexture(ID_Drift, driftMask ? driftMask : Texture2D.grayTexture);
            if (!Field || !Field.isActiveAndEnabled || !Field.Active) { Shader.SetGlobalTexture(ID_Deform, Texture2D.blackTexture); Shader.SetGlobalVector(ID_DRect, Vector4.zero); }
            Shader.SetGlobalVector(ID_Depth, new Vector4(maxDepthMetres, visibleDepth && DeformationQualityOn && Application.isPlaying ? 1f : 0f, ankleDepthMetres, 0f));
            if (terrain)
            {
                var p = terrain.GetPosition(); var s = terrain.terrainData.size;
                Shader.SetGlobalVector(ID_Rect, new Vector4(p.x, p.z, 1f / s.x, 1f / s.z));
            }
        }

        Color SampleDrift(Vector3 worldPos)
        {
            if (!driftMask || !driftMask.isReadable || !terrain) return new Color(0.35f, 0.5f, 1f, 0f);
            var p = terrain.GetPosition(); var s = terrain.terrainData.size;
            float u = (worldPos.x - p.x) / s.x, v = (worldPos.z - p.z) / s.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return new Color(0.35f, 0.5f, 1f, 0f);
            return driftMask.GetPixelBilinear(u, v);
        }

        [ContextMenu("Restore Original Terrain Material")]
        public void RestoreTerrainMaterial() { if (terrain) terrain.materialTemplate = originalTerrainMaterial; applyTerrainMaterial = false; }
    }
}
