Shader "Custom/ActionRadiusOverlay"
{
    Properties
    {
        _Center ("Center (world)", Vector) = (0, 0, 0, 0)
        _Radius ("Radius (world units)", Float) = 10
        _LineThickness ("Line Thickness (world units)", Float) = 0.2
        _LineColor ("Line Color", Color) = (0.2, 0.85, 1, 0.6)
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

            float4 _Center;
            float _Radius;
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
                float dist = distance(i.worldPos.xy, _Center.xy);
                float distToRing = abs(dist - _Radius);
                float alpha = 1.0 - smoothstep(0.0, _LineThickness, distToRing);
                return fixed4(_LineColor.rgb, _LineColor.a * alpha);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
