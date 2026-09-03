Shader "Custom/ShadedGroundTiled"
{
    Properties
    {
        _BiomeTex0 ("Base Texture 0", 2D) = "white" {}
        _BiomeTex1 ("Base Texture 1", 2D) = "white" {}
        _BiomeTex2 ("Base Texture 2", 2D) = "white" {}
        _BiomeTex3 ("Base Texture 3", 2D) = "white" {}
        _BiomeTex4 ("Base Texture 4", 2D) = "white" {}
        _BiomeTex5 ("Base Texture 5", 2D) = "white" {}
        _BiomeTexCount ("Base Texture Count", Float) = 1
        _BiomeWeight0 ("Base Weight 0", Float) = 1
        _BiomeWeight1 ("Base Weight 1", Float) = 1
        _BiomeWeight2 ("Base Weight 2", Float) = 1
        _BiomeWeight3 ("Base Weight 3", Float) = 1
        _BiomeWeight4 ("Base Weight 4", Float) = 1
        _BiomeWeight5 ("Base Weight 5", Float) = 1

        _AccentTex0 ("Accent Texture 0", 2D) = "white" {}
        _AccentTex1 ("Accent Texture 1", 2D) = "white" {}
        _AccentTex2 ("Accent Texture 2", 2D) = "white" {}
        _AccentTex3 ("Accent Texture 3", 2D) = "white" {}
        _AccentTex4 ("Accent Texture 4", 2D) = "white" {}
        _AccentTex5 ("Accent Texture 5", 2D) = "white" {}
        _AccentTexCount ("Accent Texture Count", Float) = 0
        _AccentWeight0 ("Accent Weight 0", Float) = 1
        _AccentWeight1 ("Accent Weight 1", Float) = 1
        _AccentWeight2 ("Accent Weight 2", Float) = 1
        _AccentWeight3 ("Accent Weight 3", Float) = 1
        _AccentWeight4 ("Accent Weight 4", Float) = 1
        _AccentWeight5 ("Accent Weight 5", Float) = 1
        _AccentShare ("Accent Total Area Share", Range(0, 1)) = 0.2

        _VariationOrigin ("Variation Origin (world)", Vector) = (0, 0, 0, 0)
        _TextureWorldSize ("Texture World Size", Vector) = (4, 4, 0, 0)

        _BiomeCellSize ("Base Feature Size (world units)", Float) = 12
        _BiomeEdgeSoftness ("Base Edge Softness", Range(0.01, 0.4)) = 0.1

        _AccentCellSize ("Accent Feature Size (world units)", Float) = 7
        _AccentEdgeSoftness ("Accent Edge Softness", Range(0.01, 0.4)) = 0.1

        _BiomeSeed ("Biome Seed", Float) = 0
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

            sampler2D _BiomeTex0;
            sampler2D _BiomeTex1;
            sampler2D _BiomeTex2;
            sampler2D _BiomeTex3;
            sampler2D _BiomeTex4;
            sampler2D _BiomeTex5;
            float _BiomeTexCount;
            float _BiomeWeight0;
            float _BiomeWeight1;
            float _BiomeWeight2;
            float _BiomeWeight3;
            float _BiomeWeight4;
            float _BiomeWeight5;

            sampler2D _AccentTex0;
            sampler2D _AccentTex1;
            sampler2D _AccentTex2;
            sampler2D _AccentTex3;
            sampler2D _AccentTex4;
            sampler2D _AccentTex5;
            float _AccentTexCount;
            float _AccentWeight0;
            float _AccentWeight1;
            float _AccentWeight2;
            float _AccentWeight3;
            float _AccentWeight4;
            float _AccentWeight5;
            float _AccentShare;

            float4 _VariationOrigin;
            float4 _TextureWorldSize;

            float _BiomeCellSize;
            float _BiomeEdgeSoftness;

            float _AccentCellSize;
            float _AccentEdgeSoftness;

            float _BiomeSeed;

            // Value noise: hash + smoothstep interpolation. Hash21 is Dave Hoskins' well-tested
            // "hash without sine" (not a hand-rolled one) - a cheaper hand-rolled version tried
            // earlier showed clear periodic banding artifacts (looked like a regular ladder/grid
            // pattern) at some frequency/position combinations instead of true randomness.
            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }

            fixed4 SampleBase(int idx, float2 uv)
            {
                if (idx <= 0) return tex2D(_BiomeTex0, uv);
                if (idx == 1) return tex2D(_BiomeTex1, uv);
                if (idx == 2) return tex2D(_BiomeTex2, uv);
                if (idx == 3) return tex2D(_BiomeTex3, uv);
                if (idx == 4) return tex2D(_BiomeTex4, uv);
                return tex2D(_BiomeTex5, uv);
            }

            fixed4 SampleAccent(int idx, float2 uv)
            {
                if (idx <= 0) return tex2D(_AccentTex0, uv);
                if (idx == 1) return tex2D(_AccentTex1, uv);
                if (idx == 2) return tex2D(_AccentTex2, uv);
                if (idx == 3) return tex2D(_AccentTex3, uv);
                if (idx == 4) return tex2D(_AccentTex4, uv);
                return tex2D(_AccentTex5, uv);
            }

            int PickWeighted(float h, int count, float w0, float w1, float w2, float w3, float w4, float w5)
            {
                float weights[6];
                weights[0] = w0; weights[1] = w1; weights[2] = w2;
                weights[3] = w3; weights[4] = w4; weights[5] = w5;

                float total = 0.0;
                for (int k = 0; k < count; k++) total += max(weights[k], 0.0001);

                float target = h * total;
                float cumulative = 0.0;
                for (int k = 0; k < count; k++)
                {
                    cumulative += max(weights[k], 0.0001);
                    if (target <= cumulative) return k;
                }
                return count - 1;
            }

            // Splits [0,1) into weighted bands (one per texture) and reports which band a field
            // value landed in, plus the neighboring band (otherIdx) and how far the value is from
            // that shared boundary (edgeDist, in field-value units) - used to drive the speckle
            // transition at that boundary. edgeDist is left huge when there is no neighboring band
            // on that side (single texture, or sitting at the very end of the range).
            int PickBand(float fieldValue, int count, float w0, float w1, float w2, float w3, float w4, float w5, out int otherIdx, out float edgeDist)
            {
                float weights[6];
                weights[0] = w0; weights[1] = w1; weights[2] = w2;
                weights[3] = w3; weights[4] = w4; weights[5] = w5;

                float total = 0.0;
                for (int k = 0; k < count; k++) total += max(weights[k], 0.0001);

                float cum = 0.0;
                int idx = count - 1;
                float lower = 0.0;
                float upper = 1.0;
                for (int k = 0; k < count; k++)
                {
                    float next = cum + max(weights[k], 0.0001) / total;
                    if (fieldValue < next || k == count - 1)
                    {
                        idx = k; lower = cum; upper = next;
                        break;
                    }
                    cum = next;
                }

                float distLower = fieldValue - lower;
                float distUpper = upper - fieldValue;

                if (count <= 1) { otherIdx = idx; edgeDist = 1e9; }
                else if (idx == 0) { otherIdx = 1; edgeDist = distUpper; }
                else if (idx == count - 1) { otherIdx = idx - 1; edgeDist = distLower; }
                else if (distLower < distUpper) { otherIdx = idx - 1; edgeDist = distLower; }
                else { otherIdx = idx + 1; edgeDist = distUpper; }

                return idx;
            }

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
                float2 local = i.worldPos.xy - _VariationOrigin.xy;
                float2 texUV = i.worldPos.xy / _TextureWorldSize.xy;

                // ---- Base layer: a single noise field split into weighted bands (one per dominant
                // texture) - the same technique validated earlier this session for a single
                // gradient, now used at a small scale so several alternations are visible within a
                // normal camera view, rather than a couple of huge regions. A plain smooth blend
                // (no dithering) at this small scale reads as soft, continuous grain - dithering
                // only looked bad because it was tried at a much bigger scale where the transition
                // band itself became a visible shape.
                float2 baseSeedOffset = float2(_BiomeSeed, -_BiomeSeed * 1.37);
                float baseField = ValueNoise(local / max(_BiomeCellSize, 0.0001) + baseSeedOffset);

                int baseCount = max((int)_BiomeTexCount, 1);
                int baseOther;
                float baseEdgeDist;
                int baseNear = PickBand(baseField, baseCount, _BiomeWeight0, _BiomeWeight1, _BiomeWeight2, _BiomeWeight3, _BiomeWeight4, _BiomeWeight5, baseOther, baseEdgeDist);

                float baseT = 0.5 + 0.5 * smoothstep(0.0, max(_BiomeEdgeSoftness, 0.0001), baseEdgeDist);
                fixed4 groundColor = lerp(SampleBase(baseOther, texUV), SampleBase(baseNear, texUV), baseT);

                // ---- Accent layer: sparse textures from an independent, smaller-scale noise
                // field, overlaid wherever that field exceeds a threshold set so roughly
                // _AccentShare of the map qualifies. A separate seed offset keeps its shape from
                // correlating with the base layer, so accents read as scattered, not nested in the
                // base pattern. Which accent texture appears (only relevant with more than one) is
                // chosen per broad area, not per pixel, so a single patch stays one texture.
                int accentCount = (int)_AccentTexCount;
                if (accentCount > 0)
                {
                    float2 accentSeedOffset = float2(_BiomeSeed * 1.91 + 500.0, -_BiomeSeed * 0.63 - 500.0);
                    float accentField = ValueNoise(local / max(_AccentCellSize, 0.0001) + accentSeedOffset);

                    float threshold = 1.0 - _AccentShare;
                    float edgeDist = accentField - threshold;
                    bool nearIsAccent = edgeDist > 0.0;
                    float accentT = 0.5 + 0.5 * smoothstep(0.0, max(_AccentEdgeSoftness, 0.0001), abs(edgeDist));

                    int accentIdx = 0;
                    if (accentCount > 1)
                    {
                        float2 chunk = floor(local / max(_AccentCellSize * 3.0, 0.0001));
                        float pickHash = Hash21(chunk + accentSeedOffset + float2(55.0, 77.0));
                        accentIdx = PickWeighted(pickHash, accentCount, _AccentWeight0, _AccentWeight1, _AccentWeight2, _AccentWeight3, _AccentWeight4, _AccentWeight5);
                    }
                    fixed4 accentColor = SampleAccent(accentIdx, texUV);

                    fixed4 colorNear = nearIsAccent ? accentColor : groundColor;
                    fixed4 colorFar = nearIsAccent ? groundColor : accentColor;
                    groundColor = lerp(colorFar, colorNear, accentT);
                }

                return groundColor * i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
