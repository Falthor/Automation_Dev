Shader "Custom/GroundCoverage"
{
    Properties
    {
        // Not a coverage amount: the signed distance to the conversion front, packed into [0, 1]
        // with 0.5 as the front itself. Written by GroundCoverageRenderer at
        // groundTexelsPerCell texels per cell, bilinear. The interpolation between texels is what
        // turns a sampled field into a smooth boundary - there is no smoothing anywhere in C#.
        _CoverageTex ("Front distance (R8, 0.5 = the front)", 2D) = "black" {}

        _Tint ("Converted ground tint", Color) = (0.1176, 0.5490, 0.7255, 1)
        _Intensity ("Tint strength", Range(0, 1)) = 0.15

        _RimColor ("Rim colour", Color) = (0.1176, 0.5490, 0.7255, 1)
        _RimIntensity ("Rim strength", Range(0, 1)) = 0.6

        // Width of the lit band behind the front, in threshold units - the same unit and the same
        // meaning as Custom/BuildDissolve's own _RimWidth, so the two layers light their boundary
        // the same way and read as one effect.
        _RimWidth ("Rim width, in threshold units", Range(0, 1)) = 0.08
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
            #pragma target 3.0     // fwidth, for the one-pixel edge
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

                // Exactly BuildDissolve's distanceToFront, only precomputed on the CPU because the
                // threshold depends on which site owns the cell, which a fragment cannot know.
                float distanceToFront = tex2D(_CoverageTex, uv).r * 2.0 - 1.0;

                // Untouched terrain costs nothing: no blending, no overdraw, over the whole zone
                // rectangle that is almost always empty.
                clip(distanceToFront);

                // The rim is the distance to the threshold, the same way the building's is. Keying
                // it on the coverage amount instead does not work and looked like a solid slab:
                // inside a footprint that amount is a plateau, so a barely started site read as
                // "near the threshold" over its whole surface at once. Distance to the front is a
                // property of the point, not of how far along the site is.
                float rim = _RimWidth > 0.0 ? saturate(1.0 - distanceToFront / _RimWidth) : 0.0;

                // Screen-space antialiasing of the outer edge, one pixel wide whatever the zoom.
                // Deliberately not a tunable softness: a soft edge would fade out the rim exactly
                // where it is brightest, and a crisp lit boundary is the whole point.
                float edge = smoothstep(0.0, max(fwidth(distanceToFront), 1e-5), distanceToFront);

                fixed3 rgb = lerp(_Tint.rgb, _RimColor.rgb, rim);

                // Rim strength is its own parameter, never multiplied by _Intensity: the converted
                // ground stays deliberately discreet while its boundary stays readable. Coupling
                // them would make tuning one switch the other off.
                float alpha = _Intensity + rim * _RimIntensity * (1.0 + _RimBoost);

                return fixed4(rgb, saturate(alpha) * edge);
            }
            ENDCG
        }
    }

    Fallback Off
}
