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
        float4 _ASH_VF_Height2;    // x below-base density factor, y ceiling Y, z ceiling fade, w density multiplier
        float4 _ASH_VF_Albedo;     // rgb scattering albedo
        float4 _ASH_VF_Ambient;    // rgb ambient light (tint * intensity * scene ambient)
        float4 _ASH_VF_Light;      // x intensity, y anisotropy g, z steps, w frame index (0 = no temporal jitter)
        float4 _ASH_VF_SunTint;    // rgb main light tint, w shadow strength
        float4 _ASH_VF_Noise;      // x scale (1/m), y strength, z evolve speed, w octaves
        float4 _ASH_VF_Wind;       // xy wind dir * speed (m/s), z additional-light anisotropy, w additional-light intensity
        float4 _ASH_VF_Dist;       // x start distance, y start fade, z sky density mul (0 = no sky fog), w sky distance factor
        float4 _ASH_VF_Misc;       // x jitter strength, y early-out opacity, z brightness, w debug view
        float4 _ASH_VF_LowRes;     // xy low-res size, z 1/width, w upsample depth sensitivity
        float  _ASH_VF_AreaCount;
        float4x4 _ASH_VF_AreaW2L[8];
        float4 _ASH_VF_AreaHalf[8];   // xyz half extents (sphere: x radius), w 1 = sphere
        float4 _ASH_VF_AreaParams[8]; // x density, y edge falloff m, z noise amount, w height falloff
        float4 _ASH_VF_AreaColor[8];
        float  _ASH_VF_LightCount;
        float4 _ASH_VF_LightPos[8];   // xyz pos, w range
        float4 _ASH_VF_LightColor[8]; // rgb colour*intensity, w cos inner (spot) / -1 point
        float4 _ASH_VF_LightSpot[8];  // xyz direction, w cos outer (spot) / -2 point
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

            float ASH_HazeNoise(float3 p)
            {
                float t = _ASH_VF_Cam.w;
                float3 q = (p - float3(_ASH_VF_Wind.x, 0, _ASH_VF_Wind.y) * t) * _ASH_VF_Noise.x + float3(0, t * _ASH_VF_Noise.z, 0);
                float n = ASH_Noise(q), w = 1.0, a = 0.5, sum = 1.0;
                [unroll] for (int o = 1; o < 3; o++) { if (o >= (int)_ASH_VF_Noise.w) break; q = q * 2.03 + 17.1; n += ASH_Noise(q) * a; sum += a; a *= 0.5; }
                return n / sum; // 0..1
            }

            // extinction (global height fog + local areas) and the scattering-weighted albedo at p
            float ASH_FogDensity(float3 p, float skyMul, out float3 albedo)
            {
                float h = p.y - _ASH_VF_Density.y;
                float d = _ASH_VF_Density.x * (h > 0.0 ? exp(-_ASH_VF_Density.z * h) : _ASH_VF_Height2.x);
                d *= saturate((_ASH_VF_Height2.y - p.y) / _ASH_VF_Height2.z) * skyMul;
                float n = 0.5;
                #if defined(_ASH_VF_NOISE)
                    n = ASH_HazeNoise(p);
                    d *= max(0.0, 1.0 + _ASH_VF_Noise.y * (n * 2.0 - 1.0));
                #endif
                float3 acc = d * _ASH_VF_Albedo.rgb; float total = d;
                int na = (int)_ASH_VF_AreaCount;
                [loop] for (int i = 0; i < 8; i++)
                {
                    if (i >= na) break;
                    float3 q = mul(_ASH_VF_AreaW2L[i], float4(p, 1.0)).xyz; float4 hh = _ASH_VF_AreaHalf[i]; float4 ap = _ASH_VF_AreaParams[i];
                    float sd;
                    if (hh.w > 0.5) sd = length(q) - hh.x;
                    else { float3 e = abs(q) - hh.xyz; sd = length(max(e, 0.0)) + min(max(e.x, max(e.y, e.z)), 0.0); }
                    if (sd >= 0.0) continue;
                    float w = saturate(-sd / ap.y); w = w * w * (3.0 - 2.0 * w);
                    float ad = ap.x * w * exp(-ap.w * max(q.y + hh.y, 0.0)) * max(0.0, 1.0 + ap.z * (n * 2.0 - 1.0));
                    acc += ad * _ASH_VF_AreaColor[i].rgb; total += ad;
                }
                albedo = total > 1e-8 ? acc / total : _ASH_VF_Albedo.rgb;
                return total;
            }

            float3 ASH_AddLights(float3 p, float3 dir)
            {
                float3 L = 0; int nl = (int)_ASH_VF_LightCount;
                [loop] for (int i = 0; i < 8; i++)
                {
                    if (i >= nl) break;
                    float4 lp = _ASH_VF_LightPos[i]; float3 v = lp.xyz - p; float d2 = max(dot(v, v), 1e-4); float r2 = lp.w * lp.w;
                    if (d2 > r2) continue;
                    float3 ld = v * rsqrt(d2);
                    float f = saturate(1.0 - (d2 / r2) * (d2 / r2)); float att = f * f / (1.0 + d2);
                    float4 sp = _ASH_VF_LightSpot[i]; float4 lc = _ASH_VF_LightColor[i];
                    if (sp.w > -1.5) att *= smoothstep(sp.w, max(lc.w, sp.w + 1e-3), dot(-ld, sp.xyz));
                    L += lc.rgb * att * ASH_HG(dot(dir, ld), _ASH_VF_Wind.z);
                }
                return L * _ASH_VF_Wind.w;
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
                float skyMul = sky ? _ASH_VF_Dist.z : 1.0;
                if (sky && skyMul <= 0.0) return float4(0, 0, 0, 1);
                float dist = sky ? _ASH_VF_Density.w * _ASH_VF_Dist.w : LinearEyeDepth(raw, _ZBufferParams) * rayLen;
                float tMin = min(_ASH_VF_Dist.x, dist), tMax = min(dist, sky ? _ASH_VF_Density.w * _ASH_VF_Dist.w : _ASH_VF_Density.w);
                if (tMax <= tMin) return float4(0, 0, 0, 1);
                int steps = (int)_ASH_VF_Light.z; float stepLen = (tMax - tMin) / steps;
                float jitter = lerp(0.5, ASH_IGN(i.positionCS.xy + _ASH_VF_Light.w * float2(5.588238, 3.1416)), _ASH_VF_Misc.x);

                Light ml = GetMainLight();
                float phase = ASH_HG(dot(dir, ml.direction), _ASH_VF_Light.y);
                float3 sunL = ml.color * _ASH_VF_SunTint.rgb * (_ASH_VF_Light.x * phase);
                float3 ambL = _ASH_VF_Ambient.rgb;
                float3 cam = _ASH_VF_Cam.xyz;
                float T = 1.0; float3 L = 0; float minT = 1.0 - _ASH_VF_Misc.y;
                [loop] for (int s = 0; s < steps; s++)
                {
                    float t = tMin + (s + jitter) * stepLen;
                    float3 p = cam + dir * t;
                    float3 alb;
                    float sigma = ASH_FogDensity(p, skyMul, alb) * saturate((t - _ASH_VF_Dist.x) / _ASH_VF_Dist.y);
                    if (sigma < 1e-7) continue;
                    float shadow = 1.0;
                    #if defined(_ASH_VF_SHADOWS)
                        shadow = lerp(1.0, MainLightRealtimeShadow(TransformWorldToShadowCoord(p)), _ASH_VF_SunTint.w);
                    #endif
                    float3 S = (sunL * shadow + ambL + ASH_AddLights(p, dir)) * alb;   // scattering = sigma * albedo
                    float tr = exp(-sigma * stepLen);
                    L += T * S * (1.0 - tr);                                           // energy-conserving step integral
                    T *= tr;
                    if (T < minT) break;
                }
                return float4(L * _ASH_VF_Misc.z, T);
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
                    float2 lp = uv * _ASH_VF_LowRes.xy - 0.5; float2 b = floor(lp); float2 f = lp - b; float2 texel = 1.0 / _ASH_VF_LowRes.xy;
                    float4 acc = 0; float wsum = 0;
                    [unroll] for (int k = 0; k < 4; k++)
                    {
                        float2 o = float2(k & 1, k >> 1);
                        float2 tuv = (b + o + 0.5) * texel;
                        float zl = LinearEyeDepth(SampleSceneDepth(tuv), _ZBufferParams);
                        float wb = (o.x > 0 ? f.x : 1 - f.x) * (o.y > 0 ? f.y : 1 - f.y);
                        float w = wb / (1e-3 + _ASH_VF_LowRes.w * abs(zl - zc) / max(zc, 1e-3)) + 1e-5;
                        acc += SAMPLE_TEXTURE2D_LOD(_ASH_FogTex, sampler_PointClamp, tuv, 0) * w; wsum += w;
                    }
                    float4 fog = acc / wsum;
                #else
                    float4 fog = SAMPLE_TEXTURE2D_LOD(_ASH_FogTex, sampler_PointClamp, uv, 0);
                #endif
                int dbg = (int)_ASH_VF_Misc.w;
                if (dbg == 1) return float4(fog.rgb + fog.aaa * 0.0, col.a);           // fog light only
                if (dbg == 2) return float4(fog.aaa, col.a);                         // transmittance
                if (dbg == 3) return float4(fog.rgb / max(1.0 - fog.a, 1e-3), col.a); // normalised fog colour
                return float4(col.rgb * fog.a + fog.rgb, col.a);
            }
            ENDHLSL
        }
    }
}
