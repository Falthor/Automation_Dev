using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Renders the authoritative TerrainRuntime data as a single tiled ground layer plus an
    /// animated cloud-shadow overlay, reusing the technique validated in the TerrainTransition
    /// prototype (ShadedGroundTiled / CloudShadowOverlay shaders). The ground layer's texture
    /// comes from two independent randomized single-octave noise fields (GroundTextureProfile) -
    /// a base layer (dominant textures, splitting the whole map by weighted bands of one field at
    /// biomeCellSize) and a sparser accent layer overlaid on top wherever a second,
    /// independently-seeded field crosses its own threshold (accentCellSize), so accents read as
    /// scattered small patches rather than nesting inside the base shapes. Both feature sizes are
    /// kept small (a handful of world units) so several alternations are visible within a normal
    /// camera view. Both layers use a plain smooth blend (smoothstep + lerp, no dithering) at that
    /// small scale, which reads as soft continuous grain rather than a visible edge - dithering
    /// and large blob-sized halos were both tried and rejected earlier for looking artificial. The
    /// accent layer's total area share is itself randomized each run within
    /// [accentShareMin, accentShareMax] (GroundTextureProfile.seed drives all of it, or a fresh
    /// random seed each run if randomizeSeedEachRun is set). There is no per-cell brightness
    /// modulation from TerrainType, only this large-scale texture variety. Each texture's optional
    /// normal map (GroundTextureProfile.baseNormals/accentNormals) is lit by a single fixed
    /// direction (reliefLightDirection/Height) - a cheap stand-in for a real Light2D, purely for a
    /// bump/relief look; there is no dynamic lighting or shadow casting involved.
    /// </summary>
    public sealed class TerrainView : MonoBehaviour
    {
        const string GroundShaderName = "Custom/ShadedGroundTiled";
        const string CloudShaderName = "Custom/CloudShadowOverlay";

        [Header("Texture")]
        [SerializeField] GroundTextureProfile textureProfile;
        [SerializeField] Color tint = Color.white;

        [Header("Relief lighting (normal-mapped ground, fixed direction - no real Light2D)")]
        [SerializeField] Vector2 reliefLightDirection = new Vector2(0.5f, 0.5f);
        [SerializeField, Min(0.01f)] float reliefLightHeight = 0.7f;
        [SerializeField, Range(0f, 2f)] float reliefLightIntensity = 1f;
        [SerializeField, Range(0f, 1f)] float reliefAmbient = 0.55f;

        [Header("Cloud shadows (animated)")]
        [SerializeField] bool showCloudShadows = true;
        [SerializeField, Min(0.01f)] float cloudScale = 30f;
        [SerializeField] Vector2 cloudSpeed = new Vector2(1.5f, 0.7f);
        [SerializeField, Range(0f, 1f)] float cloudCoverage = 0.45f;
        [SerializeField, Range(0.01f, 1f)] float cloudSoftness = 0.25f;
        [SerializeField, Range(0f, 1f)] float cloudShadowOpacity = 0.35f;
        [SerializeField] Color cloudShadowColor = new Color(0f, 0f, 0.05f, 1f);

        SpriteRenderer _groundRenderer;
        SpriteRenderer _cloudRenderer;
        Material _groundMaterial;
        Material _cloudMaterial;

        public void Initialize(TerrainRuntime terrain, GridRuntime grid)
        {
            (_groundRenderer, _groundMaterial) = CreateLayer("Ground", 0, GroundShaderName);

            float worldSize = terrain.Size * grid.CellSize;
            Vector3 origin = grid.CellToWorld(new Game.Core.GridCoord(0, 0));

            _groundRenderer.transform.localScale = new Vector3(worldSize, worldSize, 1f);

            float textureWorldSize = textureProfile != null ? textureProfile.textureWorldSize : 22f;
            _groundMaterial.SetVector("_VariationOrigin", origin);
            _groundMaterial.SetVector("_TextureWorldSize", new Vector4(textureWorldSize, textureWorldSize, 0f, 0f));

            int baseCount = SetTexturePalette(_groundMaterial, "_BiomeTex", "_BiomeWeight",
                textureProfile != null ? textureProfile.baseTextures : null,
                textureProfile != null ? textureProfile.baseWeights : null);
            _groundMaterial.SetFloat("_BiomeTexCount", baseCount);
            SetNormalPalette(_groundMaterial, "_BiomeNormal", textureProfile != null ? textureProfile.baseNormals : null);

            _groundMaterial.SetFloat("_BiomeCellSize", textureProfile != null ? textureProfile.biomeCellSize : 12f);
            _groundMaterial.SetFloat("_BiomeEdgeSoftness", textureProfile != null ? textureProfile.biomeEdgeSoftness : 0.1f);

            int accentCount = SetTexturePalette(_groundMaterial, "_AccentTex", "_AccentWeight",
                textureProfile != null ? textureProfile.accentTextures : null,
                textureProfile != null ? textureProfile.accentWeights : null);
            bool hasAccents = textureProfile != null && textureProfile.accentTextures != null && textureProfile.accentTextures.Length > 0;
            _groundMaterial.SetFloat("_AccentTexCount", hasAccents ? accentCount : 0);
            SetNormalPalette(_groundMaterial, "_AccentNormal", textureProfile != null ? textureProfile.accentNormals : null);

            _groundMaterial.SetFloat("_AccentCellSize", textureProfile != null ? textureProfile.accentCellSize : 7f);
            _groundMaterial.SetFloat("_AccentEdgeSoftness", textureProfile != null ? textureProfile.accentEdgeSoftness : 0.1f);

            Vector2 lightDir2D = reliefLightDirection.sqrMagnitude > 0.0001f ? reliefLightDirection.normalized : Vector2.right;
            _groundMaterial.SetVector("_ReliefLightDir", new Vector4(lightDir2D.x, lightDir2D.y, reliefLightHeight, 0f));
            _groundMaterial.SetFloat("_ReliefLightIntensity", reliefLightIntensity);
            _groundMaterial.SetFloat("_ReliefAmbient", reliefAmbient);

            // Kept small (not a full int range): the shader's hash multiplies this by ~100-450
            // inside a frac(), and float32 only has ~7 significant digits - a huge seed would
            // swamp the noise field's position-dependent bits entirely, collapsing it to a
            // constant.
            bool randomizeSeed = textureProfile == null || textureProfile.randomizeSeedEachRun;
            int seed = randomizeSeed ? UnityEngine.Random.Range(0, 10000) : textureProfile.seed;
            _groundMaterial.SetFloat("_BiomeSeed", seed);

            // The accent layer's total area share is itself randomized within the profile's
            // configured range (rather than fixed), derived from the same seed so a fixed/testing
            // seed still reproduces the exact same result.
            var seededRng = new System.Random(seed);
            float shareMin = textureProfile != null ? textureProfile.accentShareMin : 0.15f;
            float shareMax = textureProfile != null ? textureProfile.accentShareMax : 0.25f;
            float accentShare = shareMin + (float)seededRng.NextDouble() * Mathf.Max(shareMax - shareMin, 0f);
            _groundMaterial.SetFloat("_AccentShare", hasAccents ? accentShare : 0f);

            _groundRenderer.color = tint;

            if (showCloudShadows)
            {
                (_cloudRenderer, _cloudMaterial) = CreateLayer("Clouds", 1, CloudShaderName);
                _cloudRenderer.transform.localScale = new Vector3(worldSize, worldSize, 1f);

                _cloudMaterial.SetFloat("_CloudScale", cloudScale);
                _cloudMaterial.SetVector("_CloudSpeed", new Vector4(cloudSpeed.x, cloudSpeed.y, 0f, 0f));
                _cloudMaterial.SetFloat("_CloudCoverage", cloudCoverage);
                _cloudMaterial.SetFloat("_CloudSoftness", cloudSoftness);
                _cloudMaterial.SetFloat("_ShadowOpacity", cloudShadowOpacity);
                _cloudMaterial.SetColor("_ShadowColor", cloudShadowColor);
            }
        }

        /// <summary>Fills a shader's fixed 0..MaxBiomeTextures-1 texture/weight slots from a profile array and returns how many entries were actually used (clamped to at least 1, so the shader always has a valid texture even with an empty palette).</summary>
        static int SetTexturePalette(Material material, string texturePrefix, string weightPrefix, Texture2D[] textures, float[] weights)
        {
            int count = textures != null ? textures.Length : 0;
            int usedCount = Mathf.Clamp(count, 1, GroundTextureProfile.MaxBiomeTextures);

            for (int slot = 0; slot < GroundTextureProfile.MaxBiomeTextures; slot++)
            {
                Texture2D tex = slot < count ? textures[slot] : null;
                material.SetTexture($"{texturePrefix}{slot}", tex != null ? tex : Texture2D.whiteTexture);

                float weight = (weights != null && slot < weights.Length && weights[slot] > 0f) ? weights[slot] : 1f;
                material.SetFloat($"{weightPrefix}{slot}", weight);
            }

            return usedCount;
        }

        /// <summary>Assigns a normal map per slot where the profile provides one, leaving the shader's own flat "bump" default for any slot without one (never Texture2D.whiteTexture - that is not a valid encoded normal).</summary>
        static void SetNormalPalette(Material material, string prefix, Texture2D[] normals)
        {
            int count = normals != null ? normals.Length : 0;
            for (int slot = 0; slot < GroundTextureProfile.MaxBiomeTextures; slot++)
            {
                Texture2D tex = slot < count ? normals[slot] : null;
                if (tex != null) material.SetTexture($"{prefix}{slot}", tex);
            }
        }

        (SpriteRenderer, Material) CreateLayer(string name, int sortingOrder, string shaderName)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateUnitSprite();
            renderer.sortingOrder = sortingOrder;

            var material = new Material(Shader.Find(shaderName)) { name = $"Terrain{name} (Instance)" };
            renderer.sharedMaterial = material;

            return (renderer, material);
        }

        static Sprite CreateUnitSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0f, 0f), 1f);
        }
    }
}
