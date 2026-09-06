Shader "Custom/BuildDissolve"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Progress ("Progress", Range(0, 1)) = 0
        _NoiseScale ("Noise Scale (periods per world unit)", Float) = 6.3
        _NoiseWeight ("Noise Weight", Range(0, 1)) = 0.3
        _RimWidth ("Rim Width", Range(0, 1)) = 0.09
        _RimColor ("Rim Color", Color) = (0.2353, 0.7255, 0.9216, 1)
        _RimBoost ("Rim Boost (delivery flash)", Range(0, 1)) = 0
        _RevealMode ("Reveal Mode (0 = bottom up, 1 = radial)", Float) = 0

        // World-space AABB of the caster: (minX, minY, sizeX, sizeY). Written per instance by
        // BuildDissolveView. The reveal gradient has to be normalized over the building's own
        // extent, and neither sprite UVs (an atlas frame occupies an arbitrary sub-rect, so uv.y
        // is not 0-1 and shifts from frame to frame) nor object space (pivot and extents unknown
        // to the shader) provide that. World space does, and keeps the whole shader independent
        // of pivot, sprite size and sheet layout - the same reason the noise is world-space.
        _BuildBounds ("Build Bounds (minX, minY, sizeX, sizeY)", Vector) = (0, 0, 1, 1)
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

            // The grain is shared with the ground layers rather than owned here - see NanoNoise.hlsl.
            // Three copies of it would be three things that can be tuned apart, and the moment two
            // differ the effect stops reading as one process.
            #include "NanoNoise.hlsl"

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
                float3 worldPos : TEXCOORD1;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Progress;
            float _NoiseScale;
            float _NoiseWeight;
            float _RimWidth;
            fixed4 _RimColor;
            float _RimBoost;
            float _RevealMode;
            float4 _BuildBounds;


            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv) * i.color;

                float2 boundsSize = max(_BuildBounds.zw, float2(0.0001, 0.0001));
                float2 normalized = saturate((i.worldPos.xy - _BuildBounds.xy) / boundsSize);

                float base = _RevealMode < 0.5
                    ? normalized.y
                    : saturate(length(normalized - 0.5) * 2.0);

                // World coordinates, never sprite UVs: the pattern stays pinned to the terrain, so
                // it survives a sheet frame change and two neighbouring sites share one continuous
                // field instead of two juxtaposed patterns.
                float noise = NanoFbm(i.worldPos.xy * _NoiseScale);

                // The one and only place _NoiseWeight is applied. Both terms span 0-1, so field
                // does too, and _Progress can be compared to it directly - no epsilon nudge.
                float field = base * (1.0 - _NoiseWeight) + noise * _NoiseWeight;

                float distanceToFront = _Progress - field;

                clip(distanceToFront);

                if (distanceToFront < _RimWidth && _RimWidth > 0.0)
                {
                    float rim = (1.0 - distanceToFront / _RimWidth) * (1.0 + _RimBoost);
                    // Scaled by the sprite's own alpha so the rim cannot glow on fully
                    // transparent pixels around the silhouette.
                    tex.rgb += _RimColor.rgb * _RimColor.a * rim * tex.a;
                }

                return tex;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
