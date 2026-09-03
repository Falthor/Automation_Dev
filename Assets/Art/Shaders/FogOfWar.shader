Shader "Custom/FogOfWar"
{
    Properties
    {
        _Center ("Center (world)", Vector) = (0, 0, 0, 0)
        _Radius ("Radius (world units)", Float) = 10
        _EdgeSoftness ("Edge Softness (world units)", Float) = 2
        _FogColor ("Fog Color", Color) = (0.02, 0.03, 0.05, 0.96)
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
            float _EdgeSoftness;
            fixed4 _FogColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // Fully clear inside (radius - softness), fully opaque fog beyond
            // (radius + softness), smoothly blended in between - a single static
            // vision source (the Core), not a multi-source/explored-memory system.
            fixed4 frag(v2f i) : SV_Target
            {
                float dist = distance(i.worldPos.xy, _Center.xy);
                float alpha = smoothstep(_Radius - _EdgeSoftness, _Radius + _EdgeSoftness, dist);
                return fixed4(_FogColor.rgb, _FogColor.a * alpha);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
