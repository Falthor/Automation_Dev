Shader "Custom/TvTuneIn"
{
    // Full-screen "TV catching a signal" transition, driven by a single _Progress (0..1) set from
    // TvTuneInEffect.cs. Noise/roll distortion intensity ramps up over the first 70% of progress,
    // stays at peak until 85%, then the last 15% flashes the whole frame to white before the quad
    // is deactivated and Bootstrap loads.
    Properties
    {
        _NoiseColor ("Noise Color", Color) = (0.85, 0.85, 0.85, 1)
        _BackgroundColor ("Background Color", Color) = (0, 0, 0, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _RollSpeed ("Roll Speed", Float) = 1.5
        _ScanlineDensity ("Scanline Density", Float) = 300
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        ZWrite Off
        ZTest Always
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
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _NoiseColor;
            fixed4 _BackgroundColor;
            float _Progress;
            float _RollSpeed;
            float _ScanlineDensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Cheap deterministic pseudo-random hash, no texture sampling needed.
            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float intensity = saturate(_Progress / 0.7);
                float flash = saturate((_Progress - 0.85) / 0.15);

                // Rolling horizontal bands: a slow vertical roll of the sampling origin, plus a
                // per-band horizontal jitter that reads as the picture "tearing" while it tunes in.
                float roll = frac(i.uv.y + _Time.y * _RollSpeed * 0.05);
                float band = floor(roll * 40);
                float2 uv = i.uv;
                uv.x += (hash(float2(band, floor(_Time.y * 20))) - 0.5) * 0.06 * intensity;

                float n = hash(uv * float2(900, 900) + _Time.y * 60);
                float scan = sin(i.uv.y * _ScanlineDensity) * 0.5 + 0.5;

                fixed3 col = lerp(_BackgroundColor.rgb, _NoiseColor.rgb * n, intensity);
                col *= lerp(1.0, scan, intensity * 0.3);
                col = lerp(col, fixed3(1, 1, 1), flash);

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
