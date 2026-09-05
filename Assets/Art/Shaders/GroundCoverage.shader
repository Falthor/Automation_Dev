Shader "Custom/GroundCoverage"
{
    Properties
    {
        // One texel per grid cell, R8, bilinear. The interpolation between texels is what turns a
        // per-cell field into a continuous boundary - there is no smoothing anywhere in C#.
        _CoverageTex ("Coverage (R8, one texel per cell)", 2D) = "black" {}

        _Tint ("Converted ground tint", Color) = (0.1176, 0.5490, 0.7255, 1)
        _Intensity ("Tint strength", Range(0, 1)) = 0.15

        _RimColor ("Rim colour", Color) = (0.1176, 0.5490, 0.7255, 1)
        _RimIntensity ("Rim strength", Range(0, 1)) = 0.6
        _RimWidth ("Rim band width, in coverage units", Range(0.001, 1)) = 0.35
        _RimBoost ("Rim boost (delivery flash)", Range(0, 1)) = 0

        // World-space rectangle this zone's texture covers: (minX, minY, sizeX, sizeY). Written per
        // zone by GroundCoverageRenderer. The quad carries its own zone's texture, so the shader has
        // no zone to resolve and no indirection to do - it just converts world position to UV over
        // this rectangle, the same way BuildDissolve normalises over _BuildBounds.
        _ZoneBounds ("Zone bounds (minX, minY, sizeX, sizeY)", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

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
                float3 worldPos : TEXCOORD0;
            };

            sampler2D _CoverageTex;
            fixed4 _Tint;
            float _Intensity;
            fixed4 _RimColor;
            float _RimIntensity;
            float _RimWidth;
            float _RimBoost;
            float4 _ZoneBounds;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 zoneSize = max(_ZoneBounds.zw, float2(0.0001, 0.0001));
                float2 uv = (i.worldPos.xy - _ZoneBounds.xy) / zoneSize;

                float coverage = tex2D(_CoverageTex, uv).r;

                // Untouched terrain costs nothing: no blending, no overdraw, over the whole zone
                // rectangle that is almost always empty.
                clip(coverage - 0.002);

                // The lit boundary sits where coverage falls off, which with a bilinear field is
                // exactly the fringe around the converted cells.
                float rim = 1.0 - smoothstep(0.0, max(_RimWidth, 0.0001), coverage);

                fixed3 rgb = lerp(_Tint.rgb, _RimColor.rgb, rim);

                // Rim strength is its own parameter, never multiplied by _Intensity: the converted
                // ground stays deliberately discreet while its boundary stays readable. Coupling
                // them would make tuning one switch the other off.
                float alpha = coverage * _Intensity + rim * _RimIntensity * (1.0 + _RimBoost);

                return fixed4(rgb, saturate(alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
