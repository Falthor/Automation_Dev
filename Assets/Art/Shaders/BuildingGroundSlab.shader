Shader "Custom/BuildingGroundSlab"
{
    Properties
    {
        _SlabTex ("Slab Diffuse", 2D) = "white" {}
        _SlabNormal ("Slab Normal", 2D) = "bump" {}
        _SlabBrightness ("Slab Brightness", Range(0, 1)) = 1

        _TileWorldSize ("Tile World Size", Float) = 1.5
        _UVOffset ("UV Offset", Vector) = (0, 0, 0, 0)
        _FootprintWorldSize ("Footprint World Size", Vector) = (1, 1, 0, 0)
        _EdgeSoftness ("Edge Softness (world units)", Float) = 0.6
        _EdgeMask ("Edge Mask (west,east,south,north; 1 = touching another slab)", Vector) = (0, 0, 0, 0)

        _SandBandWidth ("Sand Band Width (world units)", Float) = 1.0
        _SandNoiseScale ("Sand Noise Scale (world units)", Float) = 1.2
        _SandNoiseAmplitude ("Sand Noise Amplitude (world units)", Float) = 0.5

        // Base-layer ground inputs, mirrored from Custom/ShadedGroundTiled so this shader can
        // recompute the exact same base-layer color (Mars/Gravel04) at a given world position -
        // see the frag() comment on why this must match formula-for-formula.
        _BiomeTex0 ("Ground Base Texture 0", 2D) = "white" {}
        _BiomeTex1 ("Ground Base Texture 1", 2D) = "white" {}
        _BiomeTex2 ("Ground Base Texture 2", 2D) = "white" {}
        _BiomeTexCount ("Ground Base Texture Count", Float) = 1
        _BiomeWeight0 ("Ground Base Weight 0", Float) = 1
        _BiomeWeight1 ("Ground Base Weight 1", Float) = 1
        _BiomeWeight2 ("Ground Base Weight 2", Float) = 1
        _BiomeCellSize ("Ground Base Feature Size (world units)", Float) = 12
        _BiomeEdgeSoftness ("Ground Base Edge Softness", Float) = 0.1
        _BiomeSeed ("Ground Biome Seed", Float) = 0
        _VariationOrigin ("Ground Variation Origin (world)", Vector) = (0, 0, 0, 0)
        _TextureWorldSize ("Ground Texture World Size", Vector) = (4, 4, 0, 0)

        _ReliefLightDir ("Relief Light Direction (xyz)", Vector) = (0.5, 0.5, 0.7, 0)
        _ReliefLightIntensity ("Relief Light Intensity", Range(0, 2)) = 1.4
        _ReliefAmbient ("Relief Ambient (shadow floor)", Range(0, 1)) = 0.35
        _ReliefBumpScale ("Relief Bump Scale", Range(0, 4)) = 2.5
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

            // Not named _MainTex/_BumpMap: SpriteRenderer auto-injects its own sprite texture
            // into the per-renderer property block under those names, which would silently
            // override an explicitly-assigned diffuse/normal - same reason ShadedGroundTiled.shader
            // names its textures _BiomeTex0 etc. instead.
            sampler2D _SlabTex;
            sampler2D _SlabNormal;
            float _SlabBrightness;

            float _TileWorldSize;
            float4 _UVOffset;
            float4 _FootprintWorldSize;
            float _EdgeSoftness;
            float4 _EdgeMask;

            float _SandBandWidth;
            float _SandNoiseScale;
            float _SandNoiseAmplitude;

            sampler2D _BiomeTex0;
            sampler2D _BiomeTex1;
            sampler2D _BiomeTex2;
            float _BiomeTexCount;
            float _BiomeWeight0;
            float _BiomeWeight1;
            float _BiomeWeight2;
            float _BiomeCellSize;
            float _BiomeEdgeSoftness;
            float _BiomeSeed;
            float4 _VariationOrigin;
            float4 _TextureWorldSize;

            float4 _ReliefLightDir;
            float _ReliefLightIntensity;
            float _ReliefAmbient;
            float _ReliefBumpScale;

            // Same validated "hash without sine" + smoothstep value noise as ShadedGroundTiled.shader
            // (see that file's comment on why a well-tested hash beats a hand-rolled one) - reused
            // here rather than reinvented, for the sand-encroachment patchiness below.
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

            // Verbatim copy of ShadedGroundTiled.shader's PickBand - splits [0,1) into weighted
            // bands and reports which one fieldValue landed in, plus the neighboring band and the
            // field-value distance to their shared boundary. Must stay identical to that shader's
            // copy so the two produce the same band for the same input.
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
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // World-position-based tiling (not the sprite's own UV) keeps the tile scale
                // constant in world units regardless of footprint size - the same trick
                // ShadedGroundTiled.shader uses for its biome textures.
                float2 texUV = i.worldPos.xy / max(_TileWorldSize, 0.0001) + _UVOffset.xy;

                fixed4 diffuse = tex2D(_SlabTex, texUV);
                diffuse.rgb *= _SlabBrightness;

                // Distance (world units) from the nearest edge of the footprint quad: i.uv spans
                // 0..1 across it, so UV distance to a given edge times the quad's own world size
                // converts to a world-unit distance, used below both for the sand encroachment
                // and the final alpha fade. Computed per side (not just the isotropic min) so
                // _EdgeMask (set by GroundSlabNeighborLinker when another slab is flush against
                // that side) can push a masked side's distance to effectively infinite - meaning
                // that side never fades/reveals sand, it just stays opaque concrete all the way
                // to the neighboring slab.
                const float kMaskedDist = 1e9;
                float distWest  = lerp(i.uv.x * _FootprintWorldSize.x, kMaskedDist, step(0.5, _EdgeMask.x));
                float distEast  = lerp((1.0 - i.uv.x) * _FootprintWorldSize.x, kMaskedDist, step(0.5, _EdgeMask.y));
                float distSouth = lerp(i.uv.y * _FootprintWorldSize.y, kMaskedDist, step(0.5, _EdgeMask.z));
                float distNorth = lerp((1.0 - i.uv.y) * _FootprintWorldSize.y, kMaskedDist, step(0.5, _EdgeMask.w));
                float edgeDist = min(min(distWest, distEast), min(distSouth, distNorth));

                // Sand encroachment: rather than only fading the slab away to reveal the ground
                // sprite underneath, blend the REAL ground base-layer color (Mars/Gravel04) on top
                // of the still-mostly-opaque slab as it approaches the edge, so it reads as sand
                // drifting over the concrete instead of the concrete simply vanishing or fading to
                // a flat tint. The noise perturbs the effective distance before thresholding, so
                // the encroachment line is a jagged, patchy boundary (matching TERRAIN.md's
                // small-scale-noise-plus-smoothstep look) instead of a perfect rounded-rectangle
                // ring. _UVOffset (already randomized per building for the diffuse tiling phase)
                // doubles as this noise field's per-instance offset, so different buildings don't
                // show identical sand patterns.
                float2 noiseCoord = i.worldPos.xy / max(_SandNoiseScale, 0.0001) + _UVOffset.xy * 3.7;
                float sandNoise = ValueNoise(noiseCoord);
                float perturbedEdgeDist = edgeDist + (sandNoise - 0.5) * _SandNoiseAmplitude;
                float sandMask = 1.0 - smoothstep(0.0, max(_SandBandWidth, 0.0001), perturbedEdgeDist);

                // Ground base-layer recompute - formula-for-formula identical to
                // ShadedGroundTiled.shader's frag() base layer, fed the same live _Biome*/
                // _VariationOrigin/_TextureWorldSize values, so it reproduces the exact texture
                // the real Ground sprite shows at this world position (no seam at the boundary).
                float2 groundLocal = i.worldPos.xy - _VariationOrigin.xy;
                float2 groundTexUV = i.worldPos.xy / max(_TextureWorldSize.xy, 0.0001);
                float2 baseSeedOffset = float2(_BiomeSeed, -_BiomeSeed * 1.37);
                float baseField = ValueNoise(groundLocal / max(_BiomeCellSize, 0.0001) + baseSeedOffset);

                int baseCount = max((int)_BiomeTexCount, 1);
                int baseOther;
                float baseEdgeDist;
                int baseNear = PickBand(baseField, baseCount, _BiomeWeight0, _BiomeWeight1, _BiomeWeight2, baseOther, baseEdgeDist);
                float baseT = 0.5 + 0.5 * smoothstep(0.0, max(_BiomeEdgeSoftness, 0.0001), baseEdgeDist);
                fixed4 groundColor = lerp(SampleBase(baseOther, groundTexUV), SampleBase(baseNear, groundTexUV), baseT);

                diffuse.rgb = lerp(diffuse.rgb, groundColor.rgb, sandMask);

                float3 normalTS = UnpackNormal(tex2D(_SlabNormal, texUV));
                normalTS.xy *= _ReliefBumpScale;
                float3 normal = normalize(normalTS);

                float3 lightDir = normalize(_ReliefLightDir.xyz);
                float ndotl = saturate(dot(normal, lightDir));
                float lightTerm = _ReliefAmbient + (1.0 - _ReliefAmbient) * ndotl * _ReliefLightIntensity;
                diffuse.rgb *= lightTerm;

                // Beyond the sand band, the slab still fades out via alpha (same as before) to
                // avoid a hard geometric cutoff at the far edge of the overscanned quad.
                diffuse.a *= smoothstep(0.0, max(_EdgeSoftness, 0.0001), edgeDist);

                return diffuse * i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
