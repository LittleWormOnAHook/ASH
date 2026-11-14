// ASH deformation field update (ping-pong blit): scroll with the follow window, refill, then stamp deformer segments.
// R = fraction of the ash/snow layer pushed aside (0 untouched .. 1 down to the ground). Driven by ASHDeformationField.
Shader "Hidden/ASH/DeformUpdate"
{
    Properties { _MainTex ("Prev", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _ASH_DF_Rect;     // x minX, y minZ, z size (m)
            float4 _ASH_DF_Offset;   // xy uv offset of the previous window, z refill this frame, w clear (1 = reset)
            float4 _ASH_DF_Seg[32];  // ax, az, bx, bz
            float4 _ASH_DF_SegP[32]; // radius, strength
            float  _ASH_DF_Count;

            float frag(v2f_img i) : SV_Target
            {
                float2 puv = i.uv + _ASH_DF_Offset.xy;
                float prev = (any(puv < 0) || any(puv > 1) || _ASH_DF_Offset.w > 0.5) ? 0 : tex2D(_MainTex, puv).r;
                prev = max(0, prev - _ASH_DF_Offset.z);
                float2 w = _ASH_DF_Rect.xy + i.uv * _ASH_DF_Rect.z;
                int n = (int)_ASH_DF_Count;
                for (int k = 0; k < 32; k++)
                {
                    if (k >= n) break;
                    float2 a = _ASH_DF_Seg[k].xy, b = _ASH_DF_Seg[k].zw, ab = b - a;
                    float t = saturate(dot(w - a, ab) / max(dot(ab, ab), 1e-5));
                    float d = length(w - (a + ab * t));
                    float r = _ASH_DF_SegP[k].x;
                    float s = _ASH_DF_SegP[k].y * (1 - smoothstep(r * 0.55, r, d));
                    prev = max(prev, s);
                }
                return prev;
            }
            ENDCG
        }
    }
}
