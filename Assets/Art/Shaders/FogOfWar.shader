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
        _EdgeSoftness ("Edge softness, in threshold units", Range(0, 1)) = 0.18

        // The grain of the border. Much coarser than the materialisation's (which runs at ~12
        // periods per world unit to make sub-cell teeth): here the wobble has to be readable at a
        // scale of a few cells, or a fog boundary reads as the circle it must not be.
        _NoiseScale ("Noise scale (periods per world unit)", Float) = 0.4
        _NoiseWeight ("Noise weight, in threshold units", Range(0, 1)) = 0.35
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
            #include "NanoNoise.hlsl"

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

                // Signed distance to the fog's edge: positive where the map is still hidden. The
                // jitter is SUBTRACTED, following NanoFrontJitter's own convention - the same thing
                // as adding it to the threshold.
                float hidden = _Threshold - discovered;
                hidden -= NanoFrontJitter(i.worldPos.xy, _NoiseScale, _NoiseWeight);

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
