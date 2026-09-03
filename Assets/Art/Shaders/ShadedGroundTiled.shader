Shader "Custom/ShadedGroundTiled"
{
    Properties
    {
        _BiomeTex0 ("Base Texture 0", 2D) = "white" {}
        _BiomeTex1 ("Base Texture 1", 2D) = "white" {}
        _BiomeTex2 ("Base Texture 2", 2D) = "white" {}
        _BiomeTexCount ("Base Texture Count", Float) = 1
        _BiomeWeight0 ("Base Weight 0", Float) = 1
        _BiomeWeight1 ("Base Weight 1", Float) = 1
        _BiomeWeight2 ("Base Weight 2", Float) = 1

        _AccentTex0 ("Accent Texture 0", 2D) = "white" {}
        _AccentTex1 ("Accent Texture 1", 2D) = "white" {}
        _AccentTex2 ("Accent Texture 2", 2D) = "white" {}
        _AccentTexCount ("Accent Texture Count", Float) = 0
        _AccentWeight0 ("Accent Weight 0", Float) = 1
        _AccentWeight1 ("Accent Weight 1", Float) = 1
        _AccentWeight2 ("Accent Weight 2", Float) = 1
        _AccentShare ("Accent Total Area Share", Range(0, 1)) = 0.2

        _BiomeNormal0 ("Base Normal 0", 2D) = "bump" {}
        _BiomeNormal1 ("Base Normal 1", 2D) = "bump" {}
        _BiomeNormal2 ("Base Normal 2", 2D) = "bump" {}
        _AccentNormal0 ("Accent Normal 0", 2D) = "bump" {}
        _AccentNormal1 ("Accent Normal 1", 2D) = "bump" {}
        _AccentNormal2 ("Accent Normal 2", 2D) = "bump" {}

        _ReliefLightDir ("Relief Light Direction (xyz)", Vector) = (0.5, 0.5, 0.7, 0)
        _ReliefLightIntensity ("Relief Light Intensity", Range(0, 2)) = 1
        _ReliefAmbient ("Relief Ambient (shadow floor)", Range(0, 1)) = 0.55

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
            float _BiomeTexCount;
            float _BiomeWeight0;
            float _BiomeWeight1;
            float _BiomeWeight2;

            sampler2D _AccentTex0;
            sampler2D _AccentTex1;
            sampler2D _AccentTex2;
            float _AccentTexCount;
            float _AccentWeight0;
            float _AccentWeight1;
            float _AccentWeight2;
            float _AccentShare;

            sampler2D _BiomeNormal0;
            sampler2D _BiomeNormal1;
            sampler2D _BiomeNormal2;
            sampler2D _AccentNormal0;
            sampler2D _AccentNormal1;
            sampler2D _AccentNormal2;

            float4 _ReliefLightDir;
            float _ReliefLightIntensity;
            float _ReliefAmbient;

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
                return tex2D(_BiomeTex2, uv);
            }

            fixed4 SampleAccent(int idx, float2 uv)
            {
                if (idx <= 0) return tex2D(_AccentTex0, uv);
                if (idx == 1) return tex2D(_AccentTex1, uv);
                return tex2D(_AccentTex2, uv);
            }

            fixed4 SampleBaseNormal(int idx, float2 uv)
            {
                if (idx <= 0) return tex2D(_BiomeNormal0, uv);
                if (idx == 1) return tex2D(_BiomeNormal1, uv);
                return tex2D(_BiomeNormal2, uv);
            }

            fixed4 SampleAccentNormal(int idx, float2 uv)
            {
                if (idx <= 0) return tex2D(_AccentNormal0, uv);
                if (idx == 1) return tex2D(_AccentNormal1, uv);
                return tex2D(_AccentNormal2, uv);
            }

            int PickWeighted(float h, int count, float w0, float w1, float w2)
            {
                float weights[3];
                weights[0] = w0; weights[1] = w1; weights[2] = w2;

                float total = 0.0;
                for (int k = 0; k < count; k++) total += max(weights[k], 0.0001);

                float target = h * total;
                float cumulative = 0.0;
                for (int j = 0; j < count; j++)
                {
                    cumulative += max(weights[j], 0.0001);
                    if (target <= cumulative) return j;
                }
                return count - 1;
            }

            // Splits [0,1) into weighted bands (one per texture) and reports which band a field
            // value landed in, plus the neighboring band (otherIdx) and how far the value is from
            // that shared boundary (edgeDist, in field-value units) - used to drive the speckle
            // transition at that boundary. edgeDist is left huge when there is no neighboring band
            // on that side (single texture, or sitting at the very end of the range).
            int PickBand(float fieldValue, int count, float w0, float w1, float w2, out int otherIdx, out float edgeDist)
            {
                float weights[3];
                weights[0] = w0; weights[1] = w1; weights[2] = w2;

                float total = 0.0;
                for (int k = 0; k < count; k++) total += max(weights[k], 0.0001);

                float cum = 0.0;
                int idx = count - 1;
                float lower = 0.0;
                float upper = 1.0;
                for (int j = 0; j < count; j++)
                {
                    float next = cum + max(weights[j], 0.0001) / total;
                    if (fieldValue < next || j == count - 1)
                    {
                        idx = j; lower = cum; upper = next;
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
                int baseNear = PickBand(baseField, baseCount, _BiomeWeight0, _BiomeWeight1, _BiomeWeight2, baseOther, baseEdgeDist);

                float baseT = 0.5 + 0.5 * smoothstep(0.0, max(_BiomeEdgeSoftness, 0.0001), baseEdgeDist);
                fixed4 groundColor = lerp(SampleBase(baseOther, texUV), SampleBase(baseNear, texUV), baseT);

                // Relief: blend the two candidate normal maps the same way as the diffuse colors
                // (unpacked and renormalized, not raw packed bytes, so the blend stays a valid
                // unit vector), then light it with a fixed direction - a cheap stand-in for a real
                // Light2D, giving the flat ground a sense of bump/relief without needing one.
                float3 normal = normalize(lerp(
                    UnpackNormal(SampleBaseNormal(baseOther, texUV)),
                    UnpackNormal(SampleBaseNormal(baseNear, texUV)),
                    baseT));

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
                        accentIdx = PickWeighted(pickHash, accentCount, _AccentWeight0, _AccentWeight1, _AccentWeight2);
                    }
                    fixed4 accentColor = SampleAccent(accentIdx, texUV);

                    fixed4 colorNear = nearIsAccent ? accentColor : groundColor;
                    fixed4 colorFar = nearIsAccent ? groundColor : accentColor;
                    groundColor = lerp(colorFar, colorNear, accentT);

                    float3 accentNormalSample = UnpackNormal(SampleAccentNormal(accentIdx, texUV));
                    float3 normalNear = nearIsAccent ? accentNormalSample : normal;
                    float3 normalFar = nearIsAccent ? normal : accentNormalSample;
                    normal = normalize(lerp(normalFar, normalNear, accentT));
                }

                float3 lightDir = normalize(_ReliefLightDir.xyz);
                float ndotl = saturate(dot(normal, lightDir));
                float lightTerm = _ReliefAmbient + (1.0 - _ReliefAmbient) * ndotl * _ReliefLightIntensity;
                groundColor.rgb *= lightTerm;

                return groundColor * i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
