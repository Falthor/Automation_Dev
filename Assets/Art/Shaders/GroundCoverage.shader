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

        // How far the boundary is sampled, in cells. Widens the lit band; it does not move it.
        _RimWidth ("Rim band width, in cells", Range(0.1, 3)) = 1
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
            float4 _CoverageTex_TexelSize;
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

                // How much the field changes over one cell around here. Inside a site's footprint
                // the field is a plateau, so this is ~0; it only rises in the bilinear falloff at
                // the edge, which is where the lit boundary belongs.
                //
                // Keying the rim on the coverage VALUE instead does not work, and looked like a
                // solid slab: while a site is only part built its whole footprint sits at one low
                // value, so every pixel of it reads as "near the threshold" at once. The boundary
                // is a spatial property, so it has to be measured spatially.
                //
                // It also scales itself: a barely started site has a small step at its edge and so
                // a faint rim, which is the behaviour wanted anyway.
                float2 step = _CoverageTex_TexelSize.xy * max(_RimWidth, 0.001);
                float left = tex2D(_CoverageTex, uv - float2(step.x, 0)).r;
                float right = tex2D(_CoverageTex, uv + float2(step.x, 0)).r;
                float down = tex2D(_CoverageTex, uv - float2(0, step.y)).r;
                float up = tex2D(_CoverageTex, uv + float2(0, step.y)).r;

                float rim = max(max(abs(coverage - left), abs(coverage - right)),
                                max(abs(coverage - down), abs(coverage - up)));

                fixed3 rgb = lerp(_Tint.rgb, _RimColor.rgb, saturate(rim));

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
