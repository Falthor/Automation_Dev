Shader "Custom/FogOfWar"
{
    Properties
    {
        // What the player has discovered, one texel per cell (or per sub-cell - see
        // FogOfWarView.texelsPerCell), 1 = discovered. Written by FogOfWarView from
        // Game.Grid.DiscoveryRuntime, bilinear, and re-uploaded only when that state changes.
        //
        // Bilinear is not a detail: the field is binary per cell, and the interpolation between
        // texels is the only thing that turns it into a boundary a threshold can cut anywhere
        // other than exactly on a cell edge. Point filtering here gives a staircase, and no
        // amount of noise below hides it.
        _FogTex ("Discovery (R8, 1 = discovered)", 2D) = "black" {}

        // World-space rectangle the texture covers: (minX, minY, sizeX, sizeY). The quad is drawn
        // larger than this so panning off the map stays fogged; the mapping is stated here rather
        // than implied by the quad's own transform, exactly as Custom/GroundCoverage does with
        // _ZoneBounds.
        _MapBounds ("Map bounds (minX, minY, sizeX, sizeY)", Vector) = (0, 0, 1, 1)

        _FogColor ("Fog colour", Color) = (0.02, 0.03, 0.05, 0.96)

        // Where in the interpolated ramp the fog's edge falls. 0.5 puts it halfway between a
        // discovered cell and its unknown neighbour.
        _Threshold ("Threshold", Range(0, 1)) = 0.5

        // How far the fog fades out over its own edge, in threshold units - not world units. It is
        // clamped to at least one pixel below, so the boundary never aliases however hard it is
        // tightened.
        _EdgeSoftness ("Edge softness, in threshold units", Range(0, 1)) = 0.114

        // The grain of the border, in periods per world unit. This was first guessed at 0.4, on the
        // theory that a fog boundary has to be readable across several cells where the
        // materialisation makes sub-cell teeth at ~12. Tuning it on screen said otherwise: it landed
        // at 3, so the border wants detail at roughly a third of a cell after all. What a coarse
        // setting actually produces is a smooth, slow bulge - which reads as a deformed circle, and
        // still reads as a circle. Breaking that up takes detail smaller than the eye can follow
        // around the perimeter.
        //
        // The defaults here are only a fallback: FogOfWarView pushes the serialized values over
        // them at Initialize, and again on any Inspector edit.
        _NoiseScale ("Noise scale (periods per world unit)", Float) = 3
        _NoiseWeight ("Noise weight, in threshold units", Range(0, 1)) = 0.396
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
            #pragma target 3.0     // fwidth, for the one-pixel floor on the edge
            #include "UnityCG.cginc"

            // Only the randomness is shared. The materialisation's own composition lives in
            // NanoNoise.hlsl and is deliberately NOT included here: this border has nothing to do
            // with that effect, and borrowing its octaves would mean tuning the dissolve moves the
            // fog's edge. What must never be reimplemented is the hash - a fourth hand-rolled one is
            // exactly what produced the banding ValueNoise.hlsl exists to avoid.
            #include "ValueNoise.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            sampler2D _FogTex;
            float4 _MapBounds;
            fixed4 _FogColor;
            float _Threshold;
            float _EdgeSoftness;
            float _NoiseScale;
            float _NoiseWeight;

            // The border's own grain. Three octaves because one gives a single scale of detail, and
            // against a smooth threshold that reads as a slow undulation - a rounder circle, not a
            // broken one. The stretch that follows is not cosmetic: a weighted sum of three 0-1
            // noises concentrates around 0.5 with a usable span of about 0.25 to 0.75, so without it
            // a weight delivers roughly half the irregularity it claims. The constants follow from
            // the weights right above them and have to be revisited together.
            //
            // These numbers currently equal the materialisation's. That is a starting point, not a
            // constraint - they are the fog's to change, and changing them moves nothing else.
            float FogBorderFbm(float2 p)
            {
                float fbm = 0.62 * ValueNoise2D(p)
                          + 0.27 * ValueNoise2D(p * 2.2)
                          + 0.11 * ValueNoise2D(p * 4.5);

                return saturate((fbm - 0.25) / 0.5);
            }

            // Centred on zero, so _NoiseWeight widens the wobble symmetrically instead of also
            // pushing the whole border outwards. Subtracted from the signed distance below, which is
            // the same thing as adding it to the threshold.
            float FogBorderJitter(float2 worldPos, float noiseScale, float noiseWeight)
            {
                return (FogBorderFbm(worldPos * noiseScale) - 0.5) * noiseWeight;
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
                float2 mapSize = max(_MapBounds.zw, float2(0.0001, 0.0001));
                float2 uv = (i.worldPos.xy - _MapBounds.xy) / mapSize;

                // Outside the map the sampler clamps to the border texel, which is unknown - so the
                // fog simply continues past the edge of the world, and panning off the map does not
                // reveal an unfogged void.
                float discovered = tex2D(_FogTex, uv).r;

                // Signed distance to the fog's edge: positive where the map is still hidden.
                float hidden = _Threshold - discovered;
                hidden -= FogBorderJitter(i.worldPos.xy, _NoiseScale, _NoiseWeight);

                // Discovered ground costs nothing: no blending and no overdraw over the part of the
                // map the player has actually seen, which is the part they are looking at.
                clip(hidden);

                // Never thinner than a pixel, whatever the zoom, so tightening the softness to zero
                // gives a crisp edge rather than a stair-stepped one.
                float softness = max(_EdgeSoftness, fwidth(hidden));

                return fixed4(_FogColor.rgb, _FogColor.a * saturate(hidden / softness));
            }
            ENDCG
        }
    }

    Fallback Off
}
