// ASH Volumetric Fog (URP 17, RenderGraph). Driven by ASHVolumetricFogFeature + the "ASH Volumetric Fog" volume override.
// Pass 0 raymarches depth-aware height fog (main-light in-scattering with Henyey-Greenstein phase, optional main-light
// shadows for light shafts, drifting ash-haze noise) into an RGBA16F target: rgb in-scattered light, a transmittance.
// Pass 1 composites it over the camera colour, with a depth-aware (bilateral) upsample when the march ran at half res.
Shader "Hidden/ASH/Volumetric Fog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        float4 _ASH_VF_RayBL, _ASH_VF_RayRight, _ASH_VF_RayUp; // world rays (forward component 1) at the bottom-left corner + spans
        float4 _ASH_VF_Cam;        // xyz camera pos, w time
        float4 _ASH_VF_Density;    // x density (1/m), y base height, z height falloff, w max distance
        float4 _ASH_VF_Albedo;     // rgb scattering albedo
        float4 _ASH_VF_Ambient;    // rgb ambient light (tint * intensity * scene ambient)
        float4 _ASH_VF_Light;      // x intensity, y anisotropy g, z steps, w frame index (0 = no temporal jitter)
        float4 _ASH_VF_Noise;      // x scale (1/m), y strength, z evolve speed, w unused
        float4 _ASH_VF_Wind;       // xy wind dir * speed (m/s), zw unused
        float4 _ASH_VF_LowRes;     // xy low-res size, zw 1/size (composite)
        TEXTURE2D(_ASH_FogTex);

        float ASH_IGN(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }
        float ASH_Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float ASH_Noise(float3 x)
        {
            float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(ASH_Hash(i), ASH_Hash(i + float3(1, 0, 0)), f.x), lerp(ASH_Hash(i + float3(0, 1, 0)), ASH_Hash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(ASH_Hash(i + float3(0, 0, 1)), ASH_Hash(i + float3(1, 0, 1)), f.x), lerp(ASH_Hash(i + float3(0, 1, 1)), ASH_Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }
        float ASH_HG(float c, float g) { float g2 = g * g; return (1.0 - g2) / pow(max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5); } // x4pi: 1 = isotropic
        ENDHLSL

        Pass
        {
            Name "March"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMarch
            #pragma multi_compile_local _ _ASH_VF_SHADOWS
            #pragma multi_compile_local _ _ASH_VF_NOISE
            #if defined(_ASH_VF_SHADOWS)
                #define _MAIN_LIGHT_SHADOWS_CASCADE 1
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float ASH_FogDensity(float3 p)
            {
                float d = _ASH_VF_Density.x * exp(-_ASH_VF_Density.z * max(p.y - _ASH_VF_Density.y, 0.0));
                #if defined(_ASH_VF_NOISE)
                    float t = _ASH_VF_Cam.w;
                    float3 q = (p - float3(_ASH_VF_Wind.x, 0, _ASH_VF_Wind.y) * t) * _ASH_VF_Noise.x + float3(0, t * _ASH_VF_Noise.z, 0);
                    float n = ASH_Noise(q) * 0.65 + ASH_Noise(q * 2.03 + 17.1) * 0.35;
                    d *= max(0.0, 1.0 + _ASH_VF_Noise.y * (n * 2.0 - 1.0));
                #endif
                return d;
            }

            float4 FragMarch(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float raw = SampleSceneDepth(uv);
                float3 ray = _ASH_VF_RayBL.xyz + uv.x * _ASH_VF_RayRight.xyz + uv.y * _ASH_VF_RayUp.xyz;
                float rayLen = length(ray); float3 dir = ray / rayLen;
                #if UNITY_REVERSED_Z
                    bool sky = raw <= 1e-6;
                #else
                    bool sky = raw >= 1.0 - 1e-6;
                #endif
                float dist = sky ? _ASH_VF_Density.w : LinearEyeDepth(raw, _ZBufferParams) * rayLen;
                float tMax = min(dist, _ASH_VF_Density.w);
                int steps = (int)_ASH_VF_Light.z; float stepLen = tMax / steps;
                float jitter = ASH_IGN(i.positionCS.xy + _ASH_VF_Light.w * float2(5.588238, 3.1416));

                Light ml = GetMainLight();
                float phase = ASH_HG(dot(dir, ml.direction), _ASH_VF_Light.y);
                float3 sunL = ml.color * (_ASH_VF_Light.x * phase);
                float3 ambL = _ASH_VF_Ambient.rgb;
                float3 cam = _ASH_VF_Cam.xyz;
                float T = 1.0; float3 L = 0;
                [loop] for (int s = 0; s < steps; s++)
                {
                    float3 p = cam + dir * ((s + jitter) * stepLen);
                    float sigma = ASH_FogDensity(p);
                    if (sigma < 1e-7) continue;
                    float shadow = 1.0;
                    #if defined(_ASH_VF_SHADOWS)
                        shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                    #endif
                    float3 S = (sunL * shadow + ambL) * _ASH_VF_Albedo.rgb;   // scattering = sigma * albedo
                    float tr = exp(-sigma * stepLen);
                    L += T * S * (1.0 - tr);                                   // energy-conserving step integral
                    T *= tr;
                    if (T < 0.003) break;
                }
                return float4(L, T);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            #pragma multi_compile_local _ _ASH_VF_HALFRES

            float4 FragComposite(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                #if defined(_ASH_VF_HALFRES)
                    // depth-aware upsample: weight the 4 nearest low-res texels by bilinear weight and depth similarity
                    float zc = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    float2 lp = uv * _ASH_VF_LowRes.xy - 0.5; float2 b = floor(lp); float2 f = lp - b;
                    float4 acc = 0; float wsum = 0;
                    [unroll] for (int k = 0; k < 4; k++)
                    {
                        float2 o = float2(k & 1, k >> 1);
                        float2 tuv = (b + o + 0.5) * _ASH_VF_LowRes.zw;
                        float zl = LinearEyeDepth(SampleSceneDepth(tuv), _ZBufferParams);
                        float wb = (o.x > 0 ? f.x : 1 - f.x) * (o.y > 0 ? f.y : 1 - f.y);
                        float w = wb / (1e-3 + abs(zl - zc) / max(zc, 1e-3)) + 1e-5;
                        acc += SAMPLE_TEXTURE2D_LOD(_ASH_FogTex, sampler_PointClamp, tuv, 0) * w; wsum += w;
                    }
                    float4 fog = acc / wsum;
                #else
                    float4 fog = SAMPLE_TEXTURE2D_LOD(_ASH_FogTex, sampler_PointClamp, uv, 0);
                #endif
                return float4(col.rgb * fog.a + fog.rgb, col.a);
            }
            ENDHLSL
        }
    }
}
