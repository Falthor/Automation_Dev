Shader "Custom/WorldSpaceTiledUnlit"
{
    Properties
    {
        _GroundTex ("Main Texture", 2D) = "white" {}
        _TextureWorldSize ("Texture World Size", Vector) = (4, 4, 0, 0)
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
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 worldPos : TEXCOORD0;
            };

            sampler2D _GroundTex;
            float4 _TextureWorldSize;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.worldPos.xy / _TextureWorldSize.xy;
                return tex2D(_GroundTex, uv) * i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
