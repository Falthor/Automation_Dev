Shader "Custom/CloudShadowOverlay"
{
    Properties
    {
        _CloudScale ("Cloud Scale (world units per feature)", Float) = 30
        _CloudSpeed ("Cloud Speed (world units/sec)", Vector) = (1.5, 0.7, 0, 0)
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.45
        _CloudSoftness ("Cloud Edge Softness", Range(0.01, 1)) = 0.25
        _ShadowOpacity ("Shadow Opacity", Range(0, 1)) = 0.35
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0.05, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            float _CloudScale;
            float4 _CloudSpeed;
            float _CloudCoverage;
            float _CloudSoftness;
            float _ShadowOpacity;
            fixed4 _ShadowColor;

            // Hand-rolled value noise: hash + smoothstep interpolation.
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }

            float CloudFBm(float2 p)
            {
                float n = 0.6 * ValueNoise(p) + 0.3 * ValueNoise(p * 2.1) + 0.1 * ValueNoise(p * 4.3);
                return n;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 drift = _CloudSpeed.xy * _Time.y;
                float2 cloudUV = (i.worldPos.xy + drift) / _CloudScale;

                float n = CloudFBm(cloudUV);
                float shadow = smoothstep(_CloudCoverage - _CloudSoftness, _CloudCoverage + _CloudSoftness, n);

                return fixed4(_ShadowColor.rgb, shadow * _ShadowOpacity);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
