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
        // R = what has ever been discovered. G = what is observed right now. One texel per cell
        // (or per sub-cell - see FogOfWarView.texelsPerCell), 1 = yes in both. Written by
        // FogOfWarView from Game.Grid.DiscoveryRuntime and ObservationRuntime.
        //
        // Two channels of one texture rather than two textures: the pair costs the same bytes and
        // buys one upload, one sample, and a window the two fields cannot disagree about. The
        // packing guarantees G implies R, so this shader is never handed "observed but never
        // discovered".
        //
        // R changes rarely (a revelation); G changes whenever an observer moves, so on most frames
        // a robot is walking. Both arrive here the same way.
        _FogTex ("Discovery in R, observation in G (RG16)", 2D) = "black" {}

        // World-space rectangle the texture covers: (minX, minY, sizeX, sizeY). This is a WINDOW
        // that follows the camera, not the map - its size is bounded by the zoom-out cap and has
        // nothing to do with how big the world is. Written by FogOfWarView on every re-anchoring, and
        // stated here rather than implied by the quad's own transform, exactly as
        // Custom/GroundCoverage does with _ZoneBounds.
        _WindowBounds ("Window bounds (minX, minY, sizeX, sizeY)", Vector) = (0, 0, 1, 1)

        // Opaque. Anything less lets the camera's own background through wherever no terrain is
        // drawn - past the edge of the world today, and anywhere ungenerated once terrain is lazy.
        _FogColor ("Fog colour", Color) = (0.02, 0.03, 0.05, 1)

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

        // How heavily discovered-but-unobserved ground is veiled, against the full opacity of the
        // unknown. This is the whole visible difference between the second and third states: at 1
        // remembered ground reads as unknown and the map has two states instead of three, at 0 there
        // is no veil and it has two the other way round. Judged on screen; FogOfWarView pushes the
        // serialized value over this default.
        _RememberedVeil ("Remembered veil, share of full fog", Range(0, 1)) = 0.55
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
            float4 _WindowBounds;
            fixed4 _FogColor;
            float _Threshold;
            float _EdgeSoftness;
            float _NoiseScale;
            float _NoiseWeight;
            float _RememberedVeil;

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
                float2 windowSize = max(_WindowBounds.zw, float2(0.0001, 0.0001));
                float2 uv = (i.worldPos.xy - _WindowBounds.xy) / windowSize;

                float2 field = tex2D(_FogTex, uv).rg;

                // Outside the window, forced to undiscovered AND unobserved. Clamping to the border
                // texel was right while the texture covered the whole map - its edge was always
                // unknown - but a window that follows the camera has discovered texels on its border,
                // and clamping would smear them outwards into a wedge of cleared fog. Anything with
                // no state is unknown, and outside the window there is no state at all. The window
                // always contains the view, so this only ever affects what is off screen.
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                field *= inside;

                // One grain for both edges, so the two boundaries are cut by the same irregularity
                // rather than by two independent wobbles - and it is world-space, so the observation
                // edge keeps the same shape as it travels with a robot instead of crawling.
                float jitter = FogBorderJitter(i.worldPos.xy, _NoiseScale, _NoiseWeight);

                // Two signed distances. `hidden` is positive where the map has never been seen;
                // `veiled` is positive wherever nothing is watching, which includes the unknown.
                float hidden = _Threshold - field.r - jitter;
                float veiled = _Threshold - field.g - jitter;

                // Observed ground costs nothing: no blending and no overdraw over the part of the map
                // something is actually looking at. Both terms have to be spent before a fragment can
                // be thrown away - discovered but unobserved still owes a veil.
                clip(max(hidden, veiled));

                // Never thinner than a pixel, whatever the zoom, so tightening the softness to zero
                // gives a crisp edge rather than a stair-stepped one. Measured per boundary: the two
                // fields have different gradients, so one shared width would alias whichever edge is
                // the steeper of the pair.
                float unknownAlpha = saturate(hidden / max(_EdgeSoftness, fwidth(hidden)));
                float veilAlpha = saturate(veiled / max(_EdgeSoftness, fwidth(veiled))) * _RememberedVeil;

                // The stronger of the two, never their sum: the unknown is already fully opaque, and
                // adding a veil to it would only push past 1 and flatten the very difference the
                // third state exists to draw.
                return fixed4(_FogColor.rgb, _FogColor.a * max(unknownAlpha, veilAlpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
