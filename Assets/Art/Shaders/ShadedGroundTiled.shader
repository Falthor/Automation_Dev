Shader "Custom/ShadedGroundTiled"
{
    Properties
    {
        _GroundTex ("Ground Texture", 2D) = "white" {}
        _GroundTex2 ("Ground Texture 2 (optional variation)", 2D) = "white" {}
        _UseGroundTex2 ("Use Second Texture", Float) = 0
        _VariationScale ("Variation Noise Scale", Float) = 0.15
        _VariationSoftness ("Variation Blend Softness", Range(0.01, 1)) = 0.35
        _MaskTex ("Shading Mask", 2D) = "white" {}
        _MaskOrigin ("Mask Origin (world)", Vector) = (0, 0, 0, 0)
        _MaskWorldSize ("Mask World Size", Vector) = (32, 32, 0, 0)
        _TextureWorldSize ("Texture World Size", Vector) = (4, 4, 0, 0)
        _CellSize ("Cell Size", Float) = 1
        _ShadingIntensity ("Shading Intensity", Range(0, 1)) = 0.25
        _NoiseAmount ("Noise Amount", Float) = 0.2
        _NoiseScale ("Noise Scale", Float) = 2.75
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
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 worldPos : TEXCOORD0;
            };

            sampler2D _GroundTex;
            sampler2D _GroundTex2;
            float _UseGroundTex2;
            float _VariationScale;
            float _VariationSoftness;
            sampler2D _MaskTex;
            float4 _MaskOrigin;
            float4 _MaskWorldSize;
            float4 _TextureWorldSize;
            float _CellSize;
            float _ShadingIntensity;
            float _NoiseAmount;
            float _NoiseScale;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 local = i.worldPos.xy - _MaskOrigin.xy;

                // m in [0,1]: smooth, organically-bordered patch mask (same generation/
                // supersampling as the two-texture transition, just used to modulate
                // brightness instead of cross-fading between two ground textures).
                float m = tex2D(_MaskTex, local / _MaskWorldSize.xy).r;

                float n = ValueNoise(local / _CellSize * _NoiseScale) * 2.0 - 1.0;
                float shade = 1.0 + (m * 2.0 - 1.0) * _ShadingIntensity + n * _NoiseAmount * _ShadingIntensity;

                float2 texUV = i.worldPos.xy / _TextureWorldSize.xy;
                fixed4 groundColor = tex2D(_GroundTex, texUV);

                if (_UseGroundTex2 > 0.5)
                {
                    // Large, organic low-frequency patches (independent from the per-cell
                    // shading noise above) blending in a second ground texture - breaks up
                    // repetition when mixing two variants of the same terrain type.
                    float variation = ValueNoise(local * _VariationScale);
                    float blend = smoothstep(0.5 - _VariationSoftness, 0.5 + _VariationSoftness, variation);
                    fixed4 groundColor2 = tex2D(_GroundTex2, texUV);
                    groundColor = lerp(groundColor, groundColor2, blend);
                }

                groundColor.rgb *= shade;

                return groundColor * i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
