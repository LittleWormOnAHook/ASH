using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ASH.Rendering
{
    /// <summary>URP renderer feature for the "ASH Volumetric Fog" volume override (RenderGraph). Add to the URP renderer
    /// (PC_Renderer etc.; ASH > Rendering > Install Volumetric Fog does it). Runs before transparents: march pass (half or full
    /// res) then a depth-aware composite into a new camera colour texture. Reads ASHFogArea components (local density areas
    /// and global-blend areas) and up to 8 visible point/spot lights.</summary>
    public sealed class ASHVolumetricFogFeature : ScriptableRendererFeature
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHVolumetricFogFeature()
        {
            DebugDisable = default;
        }

        public Shader shader;
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;
        [Tooltip("Also render in Scene view cameras.")] public bool sceneView = true;
        /// <summary>Debug/test switch (not serialized).</summary>
        public static bool DebugDisable;
        public const int MaxAreas = 8, MaxLights = 8;

        Material _mat; FogPass _pass;

        public override void Create() { _pass = new FogPass { renderPassEvent = passEvent }; }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (DebugDisable) return;
            var ct = renderingData.cameraData.cameraType;
            if (ct != CameraType.Game && !(sceneView && ct == CameraType.SceneView)) return;
            var v = VolumeManager.instance.stack?.GetComponent<ASHVolumetricFog>();
            if (v == null || !v.IsActive()) return;
            if (!shader) shader = Shader.Find("Hidden/ASH/Volumetric Fog");
            if (!shader) return;
            if (!_mat) _mat = CoreUtils.CreateEngineMaterial(shader);
            _pass.renderPassEvent = passEvent;
            _pass.Setup(_mat, v);
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) { CoreUtils.Destroy(_mat); _mat = null; }

        /// <summary>Global settings after Blend Global fog areas.</summary>
        public struct Blended { public float density, falloff, anisotropy, light, ambient, noise, maxDistance; public Color albedo; }

        public static Blended Blend(ASHVolumetricFog v, List<ASHFogArea> sorted)
        {
            var b = new Blended { density = v.density.value, falloff = v.heightFalloff.value, anisotropy = v.anisotropy.value, light = v.lightIntensity.value,
                ambient = v.ambientIntensity.value, noise = v.noiseStrength.value, maxDistance = v.maxDistance.value, albedo = v.albedo.value };
            foreach (var a in sorted)
            {
                if (a.mode != ASHFogArea.AreaMode.BlendGlobal || a.Weight <= 0f) continue; float w = a.Weight;
                b.density = Mathf.Lerp(b.density, a.density, w); b.falloff = Mathf.Lerp(b.falloff, a.heightFalloffGlobal, w);
                b.anisotropy = Mathf.Lerp(b.anisotropy, a.anisotropy, w); b.light = Mathf.Lerp(b.light, a.lightIntensity, w);
                b.ambient = Mathf.Lerp(b.ambient, a.ambientIntensity, w); b.noise = Mathf.Lerp(b.noise, a.noiseStrength, w);
                b.maxDistance = Mathf.Lerp(b.maxDistance, a.maxDistance, w); b.albedo = Color.Lerp(b.albedo, a.albedo, w);
            }
            return b;
        }

        sealed class FogPass : ScriptableRenderPass
        {
            static readonly int ID_BL = Shader.PropertyToID("_ASH_VF_RayBL"), ID_R = Shader.PropertyToID("_ASH_VF_RayRight"), ID_U = Shader.PropertyToID("_ASH_VF_RayUp"),
                ID_Cam = Shader.PropertyToID("_ASH_VF_Cam"), ID_Den = Shader.PropertyToID("_ASH_VF_Density"), ID_H2 = Shader.PropertyToID("_ASH_VF_Height2"),
                ID_Alb = Shader.PropertyToID("_ASH_VF_Albedo"), ID_Amb = Shader.PropertyToID("_ASH_VF_Ambient"), ID_Li = Shader.PropertyToID("_ASH_VF_Light"),
                ID_Sun = Shader.PropertyToID("_ASH_VF_SunTint"), ID_No = Shader.PropertyToID("_ASH_VF_Noise"), ID_Wi = Shader.PropertyToID("_ASH_VF_Wind"),
                ID_Dist = Shader.PropertyToID("_ASH_VF_Dist"), ID_Misc = Shader.PropertyToID("_ASH_VF_Misc"), ID_LR = Shader.PropertyToID("_ASH_VF_LowRes"),
                ID_Fog = Shader.PropertyToID("_ASH_FogTex"), ID_AC = Shader.PropertyToID("_ASH_VF_AreaCount"), ID_AM = Shader.PropertyToID("_ASH_VF_AreaW2L"),
                ID_AH = Shader.PropertyToID("_ASH_VF_AreaHalf"), ID_AP = Shader.PropertyToID("_ASH_VF_AreaParams"), ID_ACol = Shader.PropertyToID("_ASH_VF_AreaColor"),
                ID_LC = Shader.PropertyToID("_ASH_VF_LightCount"), ID_LP = Shader.PropertyToID("_ASH_VF_LightPos"), ID_LCol = Shader.PropertyToID("_ASH_VF_LightColor"),
                ID_LD = Shader.PropertyToID("_ASH_VF_LightSpot"), ID_Sh = Shader.PropertyToID("_ASH_VF_Shadow"), ID_BD = Shader.PropertyToID("_ASH_VF_BlurDir");
            readonly ProfilingSampler _march = new ProfilingSampler("ASH VolFog March"), _comp = new ProfilingSampler("ASH VolFog Composite"), _blurH = new ProfilingSampler("ASH VolFog Denoise H"), _blurV = new ProfilingSampler("ASH VolFog Denoise V");
            readonly Vector3[] _corners = new Vector3[4];
            readonly Matrix4x4[] _aM = new Matrix4x4[MaxAreas]; readonly Vector4[] _aH = new Vector4[MaxAreas], _aP = new Vector4[MaxAreas], _aC = new Vector4[MaxAreas];
            readonly Vector4[] _lP = new Vector4[MaxLights], _lC = new Vector4[MaxLights], _lD = new Vector4[MaxLights];
            readonly List<ASHFogArea> _sorted = new List<ASHFogArea>(), _local = new List<ASHFogArea>();
            readonly List<(float d, int i)> _lights = new List<(float, int)>();
            Material _mat; ASHVolumetricFog _v; int _frame;

            class PassData { public Material mat; public TextureHandle src, fog; public Vector4 dir; }

            public void Setup(Material m, ASHVolumetricFog v) { _mat = m; _v = v; }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>(); var camData = frameData.Get<UniversalCameraData>(); var lightData = frameData.Get<UniversalLightData>();
                if (res.isActiveTargetBackBuffer) return;
                var cam = camData.camera; var v = _v; var t = cam.transform; Vector3 cp = t.position;
                v.Resolve(out int steps, out bool half);

                _sorted.Clear(); foreach (var a in ASHFogArea.Active) if (a && a.isActiveAndEnabled) _sorted.Add(a);
                _sorted.Sort((x, y) => x.priority.CompareTo(y.priority));
                var b = Blend(v, _sorted);
                float dm = v.densityMultiplier.value;

                cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1), 1f, Camera.MonoOrStereoscopicEye.Mono, _corners); // BL, TL, TR, BR (view space, z = 1)
                Vector3 bl = t.TransformVector(_corners[0]), tl = t.TransformVector(_corners[1]), br = t.TransformVector(_corners[3]);
                _mat.SetVector(ID_BL, bl); _mat.SetVector(ID_R, br - bl); _mat.SetVector(ID_U, tl - bl);
                _mat.SetVector(ID_Cam, new Vector4(cp.x, cp.y, cp.z, Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 10000.0)));
                float baseH = v.heightMode.value == ASHFogHeightMode.RelativeToCamera ? cp.y + v.baseHeight.value : v.baseHeight.value;
                _mat.SetVector(ID_Den, new Vector4(b.density * dm, baseH, b.falloff, b.maxDistance));
                _mat.SetVector(ID_H2, new Vector4(v.belowBaseDensity.value, v.ceilingHeight.value, Mathf.Max(0.1f, v.ceilingFade.value), dm));
                Color alb = b.albedo;
                if (v.useDayNightColor.value)
                {
                    var fc = RenderSettings.fogColor; float mx = Mathf.Max(fc.maxColorComponent, 1e-3f);
                    alb *= Color.Lerp(Color.white, new Color(fc.r / mx, fc.g / mx, fc.b / mx, 1f), v.dayNightColorBlend.value);
                }
                _mat.SetVector(ID_Alb, (Vector4)alb.linear);
                _mat.SetVector(ID_Amb, (Vector4)(v.ambientTint.value.linear * RenderSettings.ambientLight.linear * b.ambient));
                _frame = (_frame + 1) % 64;
                _mat.SetVector(ID_Li, new Vector4(b.light, b.anisotropy, steps, v.temporalJitter.value ? _frame + 1 : 0));
                _mat.SetVector(ID_Sun, new Vector4(v.mainLightTint.value.linear.r, v.mainLightTint.value.linear.g, v.mainLightTint.value.linear.b, v.shadowStrength.value));
                _mat.SetVector(ID_No, new Vector4(v.noiseScale.value, b.noise, v.noiseSpeed.value, v.noiseOctaves.value));
                float ang = v.windDirection.value * Mathf.Deg2Rad;
                _mat.SetVector(ID_Wi, new Vector4(Mathf.Sin(ang) * v.windSpeed.value, Mathf.Cos(ang) * v.windSpeed.value, v.additionalAnisotropy.value, v.additionalLightIntensity.value));
                _mat.SetVector(ID_Dist, new Vector4(v.startDistance.value, Mathf.Max(0.001f, v.startFade.value), v.fogSky.value ? v.skyDensity.value : 0f, v.skyDistance.value));
                _mat.SetVector(ID_Misc, new Vector4(v.jitterStrength.value, v.earlyOutOpacity.value, v.brightness.value, (int)v.debugView.value));
                _mat.SetVector(ID_Sh, new Vector4(v.shadowMaxDistance.value, Mathf.Max(0.1f, v.shadowFade.value), v.softShadows.value ? 1f : 0f, 0f));

                // local density areas: nearest 8 to the camera (priority first)
                _local.Clear(); foreach (var a in _sorted) if (a.mode == ASHFogArea.AreaMode.LocalDensity && a.localDensity > 0f) _local.Add(a);
                _local.Sort((x, y) => x.priority != y.priority ? y.priority.CompareTo(x.priority) : x.Distance(cp).CompareTo(y.Distance(cp)));
                int na = 0;
                foreach (var a in _local)
                {
                    if (na >= MaxAreas) break;
                    if (!a.GetShape(out var m, out var h, out bool sph)) continue;
                    if (a.Distance(cp) > b.maxDistance) continue;
                    _aM[na] = m; _aH[na] = new Vector4(h.x, h.y, h.z, sph ? 1f : 0f);
                    _aP[na] = new Vector4(a.localDensity * dm, Mathf.Max(0.01f, a.edgeFalloff), a.noiseAmount, a.heightFalloff);
                    var lc = a.color.linear; _aC[na] = new Vector4(lc.r, lc.g, lc.b, 1f); na++;
                }
                _mat.SetFloat(ID_AC, na);
                if (na > 0) { _mat.SetMatrixArray(ID_AM, _aM); _mat.SetVectorArray(ID_AH, _aH); _mat.SetVectorArray(ID_AP, _aP); _mat.SetVectorArray(ID_ACol, _aC); }

                // additional point/spot lights (nearest first)
                int nl = 0;
                if (v.additionalLights.value && v.maxAdditionalLights.value > 0)
                {
                    _lights.Clear(); var vis = lightData.visibleLights;
                    for (int i = 0; i < vis.Length; i++)
                    {
                        if (i == lightData.mainLightIndex) continue; var vl = vis[i];
                        if (vl.lightType != LightType.Point && vl.lightType != LightType.Spot) continue;
                        Vector3 lp = vl.localToWorldMatrix.GetColumn(3); float d = Vector3.Distance(lp, cp) - vl.range;
                        if (d > b.maxDistance) continue; _lights.Add((d, i));
                    }
                    _lights.Sort((x, y) => x.d.CompareTo(y.d));
                    int cap = Mathf.Min(v.maxAdditionalLights.value, MaxLights);
                    foreach (var (d, i) in _lights)
                    {
                        if (nl >= cap) break; var vl = vis[i];
                        Vector3 lp = vl.localToWorldMatrix.GetColumn(3); Vector3 fwd = vl.localToWorldMatrix.GetColumn(2);
                        _lP[nl] = new Vector4(lp.x, lp.y, lp.z, Mathf.Max(0.01f, vl.range));
                        var col = vl.finalColor; _lC[nl] = new Vector4(col.r, col.g, col.b, vl.lightType == LightType.Spot ? 1f : 0f);
                        if (vl.lightType == LightType.Spot)
                        {
                            float outer = Mathf.Cos(vl.spotAngle * 0.5f * Mathf.Deg2Rad);
                            float innerDeg = vl.light ? vl.light.innerSpotAngle : vl.spotAngle * 0.7f; float inner = Mathf.Cos(innerDeg * 0.5f * Mathf.Deg2Rad);
                            _lD[nl] = new Vector4(fwd.x, fwd.y, fwd.z, 0f); _lC[nl].w = 1f; _lP[nl].w = Mathf.Max(0.01f, vl.range);
                            _lD[nl].w = outer; _lCone[nl] = inner;
                        }
                        else { _lD[nl] = new Vector4(0, 0, 1, -2f); _lCone[nl] = -1f; }
                        nl++;
                    }
                    for (int k = 0; k < nl; k++) _lC[k].w = _lCone[k]; // w: cos inner (spot) or -1 (point)
                }
                _mat.SetFloat(ID_LC, nl);
                if (nl > 0) { _mat.SetVectorArray(ID_LP, _lP); _mat.SetVectorArray(ID_LCol, _lC); _mat.SetVectorArray(ID_LD, _lD); }

                CoreUtils.SetKeyword(_mat, "_ASH_VF_SHADOWS", v.shadows.value && v.shadowStrength.value > 0f);
                CoreUtils.SetKeyword(_mat, "_ASH_VF_NOISE", b.noise > 0f || na > 0);
                CoreUtils.SetKeyword(_mat, "_ASH_VF_HALFRES", half);

                var desc = camData.cameraTargetDescriptor; desc.depthBufferBits = 0; desc.msaaSamples = 1;
                var fogDesc = desc; fogDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                if (half) { fogDesc.width = Mathf.Max(1, desc.width / 2); fogDesc.height = Mathf.Max(1, desc.height / 2); }
                _mat.SetVector(ID_LR, new Vector4(fogDesc.width, fogDesc.height, 1f / fogDesc.width, v.upsampleDepthSensitivity.value));
                TextureHandle src = res.activeColorTexture;
                TextureHandle fog = UniversalRenderer.CreateRenderGraphTexture(renderGraph, fogDesc, "ASH_VolFog", false, FilterMode.Point, TextureWrapMode.Clamp);
                TextureHandle dst = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "ASH_VolFogColor", false, FilterMode.Bilinear, TextureWrapMode.Clamp);

                using (var bld = renderGraph.AddRasterRenderPass<PassData>("ASH VolFog March", out var d, _march))
                {
                    d.mat = _mat; d.src = src;
                    bld.UseTexture(src); bld.UseAllGlobalTextures(true);
                    bld.SetRenderAttachment(fog, 0, AccessFlags.Write);
                    bld.SetRenderFunc((PassData p, RasterGraphContext ctx) => Blitter.BlitTexture(ctx.cmd, p.src, new Vector4(1, 1, 0, 0), p.mat, 0));
                }
                if (v.denoise.value && v.denoiseStrength.value > 0f)
                {
                    TextureHandle tmp = UniversalRenderer.CreateRenderGraphTexture(renderGraph, fogDesc, "ASH_VolFogDenoise", false, FilterMode.Point, TextureWrapMode.Clamp);
                    float r = v.denoiseRadius.value, ds = v.denoiseDepthSensitivity.value, st = v.denoiseStrength.value;
                    Blur(renderGraph, fog, tmp, new Vector4(r / fogDesc.width, 0f, ds, st), _blurH);
                    Blur(renderGraph, tmp, fog, new Vector4(0f, r / fogDesc.height, ds, st), _blurV);
                }
                using (var bld = renderGraph.AddRasterRenderPass<PassData>("ASH VolFog Composite", out var d, _comp))
                {
                    d.mat = _mat; d.src = src; d.fog = fog;
                    bld.UseTexture(src); bld.UseTexture(fog); bld.UseAllGlobalTextures(true);
                    bld.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    bld.SetRenderFunc((PassData p, RasterGraphContext ctx) => { p.mat.SetTexture(ID_Fog, (RTHandle)p.fog); Blitter.BlitTexture(ctx.cmd, p.src, new Vector4(1, 1, 0, 0), p.mat, 2); }); // pass 2 = Composite
                }
                res.cameraColor = dst;
            }
            void Blur(RenderGraph rg, TextureHandle from, TextureHandle to, Vector4 dir, ProfilingSampler ps)
            {
                using (var bld = rg.AddRasterRenderPass<PassData>(ps.name, out var d, ps))
                {
                    d.mat = _mat; d.src = from; d.dir = dir;
                    bld.UseTexture(from); bld.UseAllGlobalTextures(true); bld.AllowGlobalStateModification(true);
                    bld.SetRenderAttachment(to, 0, AccessFlags.Write);
                    // global (recorded in order) - a material value would be read at execution and both passes would get the last one
                    bld.SetRenderFunc((PassData p, RasterGraphContext ctx) => { ctx.cmd.SetGlobalVector(ID_BD, p.dir); Blitter.BlitTexture(ctx.cmd, p.src, new Vector4(1, 1, 0, 0), p.mat, 1); }); // pass 1 = Blur
                }
            }
            readonly float[] _lCone = new float[MaxLights];
        }
    }
}
