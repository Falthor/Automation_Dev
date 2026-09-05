using System.Collections.Generic;
using Game.Data;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Generates placeholder conveyor sprites in code (no art assets). Art is drawn in the
    /// canonical "Rotation = North" reference frame: ConveyorView rotates the transform to
    /// match the runtime orientation, and mirrors localScale.x for corner chirality.
    /// Straight: entry south (bottom) -> exit north (top).
    /// Corner (unmirrored): entry south (bottom) -> exit east (right).
    /// </summary>
    public sealed class ProceduralSpriteFactory
    {
        const int TextureSize = 32;
        const float PixelsPerUnit = 32f;

        const int GlowTextureSize = 64;

        readonly Dictionary<(ConveyorShapeKind, Color32), Sprite> _cache = new Dictionary<(ConveyorShapeKind, Color32), Sprite>();
        readonly Dictionary<Color32, Sprite> _solidSquareCache = new Dictionary<Color32, Sprite>();
        readonly Dictionary<Color32, Sprite> _radialGlowCache = new Dictionary<Color32, Sprite>();
        readonly Dictionary<(Texture2D, Texture2D), Material> _groundSlabMaterialCache = new Dictionary<(Texture2D, Texture2D), Material>();
        Sprite _groundSlabUnitSprite;

        /// <summary>Soft radial falloff (opaque at center, fully transparent at the edge) - used for the hover halo around world content like ore deposits.</summary>
        public Sprite CreateRadialGlowSprite(Color color)
        {
            Color32 key = color;
            if (_radialGlowCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var texture = new Texture2D(GlowTextureSize, GlowTextureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[GlowTextureSize * GlowTextureSize];
            float center = (GlowTextureSize - 1) * 0.5f;
            for (int y = 0; y < GlowTextureSize; y++)
            {
                for (int x = 0; x < GlowTextureSize; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    falloff *= falloff;
                    pixels[y * GlowTextureSize + x] = new Color(color.r, color.g, color.b, color.a * falloff);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, GlowTextureSize, GlowTextureSize),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);

            _radialGlowCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Shared material for the tiled concrete slab shown under a building/the Core, keyed by
        /// (diffuse, normal) so every caller reusing the same YFCM texture pair reuses the same
        /// Material instance (no persisted .mat asset needed). The shader itself comes from
        /// settings.SlabShader - this class is constructed in a dozen places and has no inspector,
        /// so its one shader dependency travels with the settings it is already handed. The shader
        /// tiles/lights/edge-fades entirely
        /// from world position and per-renderer MaterialPropertyBlock values (_UVOffset,
        /// _FootprintWorldSize) set by the caller - this material itself carries no per-instance
        /// state.
        /// </summary>
        public Material GetGroundSlabMaterial(GroundSlabSettings settings)
        {
            var key = (settings.SlabDiffuse, settings.SlabNormal);
            if (!_groundSlabMaterialCache.TryGetValue(key, out var material))
            {
                material = new Material(settings.SlabShader) { name = "BuildingGroundSlab (Instance)" };
                material.SetTexture("_SlabTex", settings.SlabDiffuse);
                material.SetTexture("_SlabNormal", settings.SlabNormal);
                _groundSlabMaterialCache[key] = material;
            }

            // Applied every call (even on a cache hit) rather than only at creation, so changing
            // GameRuntime's ground-slab options and rebuilding views (next Play session) actually
            // takes effect on the shared material instead of freezing at whatever value was first
            // seen.
            material.SetFloat("_SlabBrightness", settings.SlabDarken);
            material.SetFloat("_SandBandWidth", settings.SandBandWidth);
            material.SetFloat("_EdgeSoftness", settings.EdgeSoftness);
            material.SetFloat("_SandNoiseScale", settings.SandNoiseScale);
            material.SetFloat("_SandNoiseAmplitude", settings.SandNoiseAmplitude);

            for (int slot = 0; slot < settings.BiomeTextures.Length && slot < 3; slot++)
            {
                Texture tex = settings.BiomeTextures[slot];
                material.SetTexture($"_BiomeTex{slot}", tex != null ? tex : Texture2D.whiteTexture);
                float weight = slot < settings.BiomeWeights.Length ? settings.BiomeWeights[slot] : 1f;
                material.SetFloat($"_BiomeWeight{slot}", weight);
            }
            material.SetFloat("_BiomeTexCount", settings.BiomeTexCount);
            material.SetFloat("_BiomeCellSize", settings.BiomeCellSize);
            material.SetFloat("_BiomeEdgeSoftness", settings.BiomeEdgeSoftness);
            material.SetFloat("_BiomeSeed", settings.BiomeSeed);
            material.SetVector("_VariationOrigin", settings.VariationOrigin);
            material.SetVector("_TextureWorldSize", new Vector4(settings.TextureWorldSize.x, settings.TextureWorldSize.y, 0f, 0f));

            return material;
        }

        /// <summary>
        /// Plain flat unit sprite for the ground slab - unlike every other sprite in this class,
        /// its own pixels are never shown; GetGroundSlabMaterial's shader supplies all visible
        /// color from its own textures, so this only needs to give the SpriteRenderer a quad mesh
        /// and a 0..1 UV0 for the shader's edge-fade math.
        /// </summary>
        public Sprite GetGroundSlabUnitSprite()
        {
            if (_groundSlabUnitSprite != null) return _groundSlabUnitSprite;

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, false);

            _groundSlabUnitSprite = Sprite.Create(
                texture,
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f),
                1f);

            return _groundSlabUnitSprite;
        }

        /// <summary>Flat colored square placeholder, used for content with no art asset yet (e.g. ore deposits).</summary>
        public Sprite CreateSolidSquareSprite(Color color)
        {
            Color32 key = color;
            if (_solidSquareCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var texture = NewTexture();
            var pixels = new Color[TextureSize * TextureSize];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);

            _solidSquareCache[key] = sprite;
            return sprite;
        }

        Sprite _arrowSprite;

        /// <summary>Small triangle pointing North in the canonical frame (rotate the transform for other directions).</summary>
        public Sprite CreateArrowSprite(Color color)
        {
            if (_arrowSprite != null) return _arrowSprite;

            var texture = NewTexture();
            var pixels = new Color[TextureSize * TextureSize];
            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            for (int y = 0; y < TextureSize; y++)
            {
                float halfWidthAtY = (TextureSize - y) * 0.5f;
                float center = TextureSize * 0.5f;
                for (int x = 0; x < TextureSize; x++)
                {
                    if (Mathf.Abs(x - center) <= halfWidthAtY * 0.5f)
                    {
                        pixels[y * TextureSize + x] = color;
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            _arrowSprite = Sprite.Create(
                texture,
                new Rect(0, 0, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);

            return _arrowSprite;
        }

        public Sprite CreateShapeSprite(ConveyorShapeKind shape, Color color)
        {
            var key = (shape, (Color32)color);
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            Texture2D texture = shape switch
            {
                ConveyorShapeKind.Straight => DrawStraight(color),
                ConveyorShapeKind.Corner => DrawCorner(color),
                _ => DrawStraight(color)
            };

            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);

            _cache[key] = sprite;
            return sprite;
        }

        static Texture2D DrawCorner(Color color)
        {
            var tex = NewTexture();
            Color background = color * 0.5f;
            Color band = color;

            int bandStart = TextureSize / 2 - TextureSize / 8;
            int bandEnd = TextureSize / 2 + TextureSize / 8;

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    bool inVerticalBand = x >= bandStart && x < bandEnd && y <= bandEnd;
                    bool inHorizontalBand = y >= bandStart && y < bandEnd && x >= bandStart;
                    tex.SetPixel(x, y, inVerticalBand || inHorizontalBand ? band : background);
                }
            }

            tex.Apply(false, false);
            return tex;
        }

        static Texture2D DrawStraight(Color color)
        {
            var tex = NewTexture();
            Color background = color * 0.5f;
            Color band = color;

            int bandStart = TextureSize / 2 - TextureSize / 8;
            int bandEnd = TextureSize / 2 + TextureSize / 8;

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    bool inVerticalBand = x >= bandStart && x < bandEnd;
                    tex.SetPixel(x, y, inVerticalBand ? band : background);
                }
            }

            tex.Apply(false, false);
            return tex;
        }

        static Texture2D NewTexture()
        {
            return new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }
    }
}
