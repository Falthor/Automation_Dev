Shader "Custom/GridLinesOverlay"
{
    Properties
    {
        _CellSize ("Cell Size", Float) = 1
        _LineThickness ("Line Thickness (world units)", Float) = 0.03
        _LineColor ("Line Color", Color) = (0, 0, 0, 0.35)

        _ChunkSize ("Chunk Size (world units)", Float) = 64
        _ChunkLineThickness ("Chunk Line Thickness (world units)", Float) = 0.12
        _ChunkLineColor ("Chunk Line Color", Color) = (0, 0, 0, 0.55)

        /// Scales the chunk trame away without touching its colour or its spacing, so the same
        /// material can draw the cell grid alone while a building is being placed.
        _ChunkOpacity ("Chunk Lines Opacity", Range(0, 1)) = 1
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
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            float _CellSize;
            float _LineThickness;
            fixed4 _LineColor;

            float _ChunkSize;
            float _ChunkLineThickness;
            fixed4 _ChunkLineColor;
            float _ChunkOpacity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // How strongly a world position sits on a line of a trame of the given spacing.
            float TrameAlpha(float2 worldXY, float spacing, float thickness)
            {
                float2 local = frac(worldXY / spacing);
                float2 distToEdge = min(local, 1.0 - local) * spacing;
                float minDist = min(distToEdge.x, distToEdge.y);

                return 1.0 - smoothstep(0.0, thickness, minDist);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float cellAlpha = TrameAlpha(i.worldPos.xy, _CellSize, _LineThickness) * _LineColor.a;
                float chunkAlpha = TrameAlpha(i.worldPos.xy, _ChunkSize, _ChunkLineThickness)
                                 * _ChunkLineColor.a * _ChunkOpacity;

                // Chunk lines over cell lines, composited rather than added: a chunk boundary falls on
                // a cell boundary by construction (a chunk is a whole number of cells), so adding the
                // two would darken exactly the lines meant to stand out and clip them flat.
                float outAlpha = chunkAlpha + cellAlpha * (1.0 - chunkAlpha);
                float3 outColor = outAlpha > 0.0001
                    ? (_ChunkLineColor.rgb * chunkAlpha + _LineColor.rgb * cellAlpha * (1.0 - chunkAlpha)) / outAlpha
                    : _LineColor.rgb;

                return fixed4(outColor, outAlpha);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
