using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ASH.Rendering
{
    /// <summary>URP renderer feature for the "ASH Volumetric Fog" volume override (RenderGraph). Add to the URP renderer
    /// (PC_Renderer etc.; ASH > Rendering > Install Volumetric Fog does it). Runs before transparents: march pass (half or full
    /// res) then a depth-aware composite into a new camera colour texture.</summary>
    public sealed class ASHVolumetricFogFeature : ScriptableRendererFeature
    {
        public Shader shader;
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;
        [Tooltip("Also render in Scene view cameras.")] public bool sceneView = true;
        /// <summary>Debug/test switch (not serialized).</summary>
        public static bool DebugDisable;

        Material _mat; FogPass _pass;

        public override void Create()
        {
            _pass = new FogPass { renderPassEvent = passEvent };
        }

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

        sealed class FogPass : ScriptableRenderPass
        {
            static readonly int ID_BL = Shader.PropertyToID("_ASH_VF_RayBL"), ID_R = Shader.PropertyToID("_ASH_VF_RayRight"), ID_U = Shader.PropertyToID("_ASH_VF_RayUp"),
                ID_Cam = Shader.PropertyToID("_ASH_VF_Cam"), ID_Den = Shader.PropertyToID("_ASH_VF_Density"), ID_Alb = Shader.PropertyToID("_ASH_VF_Albedo"),
                ID_Amb = Shader.PropertyToID("_ASH_VF_Ambient"), ID_Li = Shader.PropertyToID("_ASH_VF_Light"), ID_No = Shader.PropertyToID("_ASH_VF_Noise"),
                ID_Wi = Shader.PropertyToID("_ASH_VF_Wind"), ID_LR = Shader.PropertyToID("_ASH_VF_LowRes"), ID_Fog = Shader.PropertyToID("_ASH_FogTex");
            readonly ProfilingSampler _march = new ProfilingSampler("ASH VolFog March"), _comp = new ProfilingSampler("ASH VolFog Composite");
            readonly Vector3[] _corners = new Vector3[4];
            Material _mat; ASHVolumetricFog _v; int _frame;

            class PassData { public Material mat; public TextureHandle src, fog; }

            public void Setup(Material m, ASHVolumetricFog v) { _mat = m; _v = v; }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>(); var camData = frameData.Get<UniversalCameraData>();
                if (res.isActiveTargetBackBuffer) return;
                var cam = camData.camera; var v = _v;
                v.Resolve(out int steps, out bool half);

                // per-camera constants
                cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1), 1f, Camera.MonoOrStereoscopicEye.Mono, _corners); // BL, TL, TR, BR (view space, z = 1)
                var t = cam.transform; Vector3 bl = t.TransformVector(_corners[0]), tl = t.TransformVector(_corners[1]), br = t.TransformVector(_corners[3]);
                _mat.SetVector(ID_BL, bl); _mat.SetVector(ID_R, br - bl); _mat.SetVector(ID_U, tl - bl);
                _mat.SetVector(ID_Cam, new Vector4(t.position.x, t.position.y, t.position.z, Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 10000.0)));
                _mat.SetVector(ID_Den, new Vector4(v.density.value, v.baseHeight.value, v.heightFalloff.value, v.maxDistance.value));
                Color alb = v.albedo.value;
                if (v.useDayNightColor.value) { var fc = RenderSettings.fogColor; float mx = Mathf.Max(fc.maxColorComponent, 1e-3f); alb *= new Color(fc.r / mx, fc.g / mx, fc.b / mx, 1f); }
                _mat.SetVector(ID_Alb, (Vector4)alb.linear);
                Color amb = v.ambientTint.value.linear * RenderSettings.ambientLight.linear * v.ambientIntensity.value;
                _mat.SetVector(ID_Amb, (Vector4)amb);
                _frame = (_frame + 1) % 64;
                _mat.SetVector(ID_Li, new Vector4(v.lightIntensity.value, v.anisotropy.value, steps, v.temporalJitter.value ? _frame + 1 : 0));
                _mat.SetVector(ID_No, new Vector4(v.noiseScale.value, v.noiseStrength.value, v.noiseSpeed.value, 0));
                float a = v.windDirection.value * Mathf.Deg2Rad; _mat.SetVector(ID_Wi, new Vector4(Mathf.Sin(a) * v.windSpeed.value, Mathf.Cos(a) * v.windSpeed.value, 0, 0));
                CoreUtils.SetKeyword(_mat, "_ASH_VF_SHADOWS", v.shadows.value);
                CoreUtils.SetKeyword(_mat, "_ASH_VF_NOISE", v.noiseStrength.value > 0f);
                CoreUtils.SetKeyword(_mat, "_ASH_VF_HALFRES", half);

                var desc = camData.cameraTargetDescriptor; desc.depthBufferBits = 0; desc.msaaSamples = 1;
                var fogDesc = desc; fogDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                if (half) { fogDesc.width = Mathf.Max(1, desc.width / 2); fogDesc.height = Mathf.Max(1, desc.height / 2); }
                _mat.SetVector(ID_LR, new Vector4(fogDesc.width, fogDesc.height, 1f / fogDesc.width, 1f / fogDesc.height));
                TextureHandle src = res.activeColorTexture;
                TextureHandle fog = UniversalRenderer.CreateRenderGraphTexture(renderGraph, fogDesc, "ASH_VolFog", false, FilterMode.Point, TextureWrapMode.Clamp);
                TextureHandle dst = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "ASH_VolFogColor", false, FilterMode.Bilinear, TextureWrapMode.Clamp);

                using (var b = renderGraph.AddRasterRenderPass<PassData>("ASH VolFog March", out var d, _march))
                {
                    d.mat = _mat; d.src = src;
                    b.UseTexture(src); b.UseAllGlobalTextures(true);
                    b.SetRenderAttachment(fog, 0, AccessFlags.Write);
                    b.SetRenderFunc((PassData p, RasterGraphContext ctx) => Blitter.BlitTexture(ctx.cmd, p.src, new Vector4(1, 1, 0, 0), p.mat, 0));
                }
                using (var b = renderGraph.AddRasterRenderPass<PassData>("ASH VolFog Composite", out var d, _comp))
                {
                    d.mat = _mat; d.src = src; d.fog = fog;
                    b.UseTexture(src); b.UseTexture(fog); b.UseAllGlobalTextures(true);
                    b.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    b.SetRenderFunc((PassData p, RasterGraphContext ctx) => { p.mat.SetTexture(ID_Fog, (RTHandle)p.fog); Blitter.BlitTexture(ctx.cmd, p.src, new Vector4(1, 1, 0, 0), p.mat, 1); });
                }
                res.cameraColor = dst;
            }
        }
    }
}
