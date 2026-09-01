Shader "Custom/TilemapMaskTransition"
{
    Properties
    {
        _GroundTex ("Main Texture", 2D) = "white" {}
        _MaskTex ("Transition Mask", 2D) = "white" {}
        _MaskOrigin ("Mask Origin (world)", Vector) = (0, 0, 0, 0)
        _MaskWorldSize ("Mask World Size", Vector) = (32, 32, 0, 0)
        _TextureWorldSize ("Texture World Size", Vector) = (4, 4, 0, 0)
        _CellSize ("Cell Size", Float) = 1
        _Threshold ("Threshold", Range(0, 1)) = 0.3
        _NoiseAmount ("Noise Amount", Float) = 0.8
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
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float3 worldPos : TEXCOORD1;
            };

            sampler2D _GroundTex;
            sampler2D _MaskTex;
            float4 _MaskOrigin;
            float4 _MaskWorldSize;
            float4 _TextureWorldSize;
            float _CellSize;
            float _Threshold;
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
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 local = i.worldPos.xy - _MaskOrigin.xy;

                float m = tex2D(_MaskTex, local / _MaskWorldSize.xy).r;
                float band = 4.0 * m * (1.0 - m);

                float n = ValueNoise(local / _CellSize * _NoiseScale) * 2.0 - 1.0;

                clip(m + n * _NoiseAmount * band - _Threshold);

                float2 texUV = i.worldPos.xy / _TextureWorldSize.xy;
                return tex2D(_GroundTex, texUV) * i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
