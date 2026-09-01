Shader "Custom/GridLinesOverlay"
{
    Properties
    {
        _CellSize ("Cell Size", Float) = 1
        _LineThickness ("Line Thickness (world units)", Float) = 0.03
        _LineColor ("Line Color", Color) = (0, 0, 0, 0.35)
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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 cellLocal = frac(i.worldPos.xy / _CellSize);
                float2 distToEdge = min(cellLocal, 1.0 - cellLocal) * _CellSize;
                float minDist = min(distToEdge.x, distToEdge.y);

                float alpha = 1.0 - smoothstep(0.0, _LineThickness, minDist);
                return fixed4(_LineColor.rgb, _LineColor.a * alpha);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
