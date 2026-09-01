using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Renders the authoritative TerrainRuntime data as a single shaded ground layer plus an
    /// animated cloud-shadow overlay, reusing the technique validated in the TerrainTransition
    /// prototype's single-texture shading mode (ShadedGroundTiled / CloudShadowOverlay shaders).
    /// The rendered mask is derived from the same deterministic TerrainRuntime.SampleContinuous
    /// function that produced the authoritative per-cell TerrainType, so the brightness
    /// variation can never meaningfully disagree with gameplay data - it only adds cosmetic
    /// sub-cell shading exactly where neighboring cells actually differ in type.
    /// </summary>
    public sealed class TerrainView : MonoBehaviour
    {
        const string GroundShaderName = "Custom/ShadedGroundTiled";
        const string CloudShaderName = "Custom/CloudShadowOverlay";

        [Header("Texture")]
        [SerializeField] GroundTextureProfile textureProfile;
        [SerializeField] Color tint = Color.white;

        [Header("Rendering")]
        [SerializeField, Range(1, 8)] int maskSupersample = 4;
        [SerializeField, Range(0f, 1f)] float shadingIntensity = 0.25f;
        [SerializeField] float shadingNoiseAmount = 0.20f;
        [SerializeField] float noiseScale = 2.75f;

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
        Texture2D _maskTexture;

        public void Initialize(TerrainRuntime terrain, GridRuntime grid)
        {
            (_groundRenderer, _groundMaterial) = CreateLayer("Ground", 0, GroundShaderName);

            float worldSize = terrain.Size * grid.CellSize;
            Vector3 origin = grid.CellToWorld(new Game.Core.GridCoord(0, 0));
            var worldSizeVec = new Vector4(worldSize, worldSize, 0f, 0f);

            _groundRenderer.transform.localScale = new Vector3(worldSize, worldSize, 1f);
            _maskTexture = BuildMask(terrain);

            Texture2D groundTexture = textureProfile != null ? textureProfile.groundTexture : null;
            Texture2D groundTexture2 = textureProfile != null ? textureProfile.groundTexture2 : null;
            float textureWorldSize = textureProfile != null ? textureProfile.textureWorldSize : 22f;

            _groundMaterial.SetTexture("_GroundTex", groundTexture != null ? groundTexture : Texture2D.whiteTexture);
            _groundMaterial.SetTexture("_GroundTex2", groundTexture2 != null ? groundTexture2 : Texture2D.whiteTexture);
            _groundMaterial.SetFloat("_UseGroundTex2", groundTexture2 != null ? 1f : 0f);
            _groundMaterial.SetFloat("_VariationScale", textureProfile != null ? textureProfile.variationScale : 0.15f);
            _groundMaterial.SetFloat("_VariationSoftness", textureProfile != null ? textureProfile.variationSoftness : 0.35f);
            _groundMaterial.SetTexture("_MaskTex", _maskTexture);
            _groundMaterial.SetVector("_MaskOrigin", origin);
            _groundMaterial.SetVector("_MaskWorldSize", worldSizeVec);
            _groundMaterial.SetVector("_TextureWorldSize", new Vector4(textureWorldSize, textureWorldSize, 0f, 0f));
            _groundMaterial.SetFloat("_CellSize", grid.CellSize);
            _groundMaterial.SetFloat("_ShadingIntensity", shadingIntensity);
            _groundMaterial.SetFloat("_NoiseAmount", shadingNoiseAmount);
            _groundMaterial.SetFloat("_NoiseScale", noiseScale);
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

        Texture2D BuildMask(TerrainRuntime terrain)
        {
            int resolution = terrain.Size * maskSupersample;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "TerrainMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float cellX = (float)x / maskSupersample;
                    float cellY = (float)y / maskSupersample;
                    float value = terrain.SampleContinuous(cellX, cellY);
                    float m = value < terrain.Proportion ? 1f : 0f;
                    pixels[y * resolution + x] = new Color(m, m, m, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
