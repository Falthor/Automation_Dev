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
