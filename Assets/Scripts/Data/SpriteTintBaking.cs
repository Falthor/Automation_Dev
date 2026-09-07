using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// The two computations behind a baked decor tint: what colour a sprite averages, and what
    /// multiplier mutes it towards the ground.
    ///
    /// <b>Here rather than in the editor tool</b> so that the tool which bakes the values and the test
    /// which checks they are still right cannot drift apart. A baked value whose formula lives in one
    /// place and whose guard re-implements it from memory is the same hazard as a rank copied into a
    /// scene: it stays green while it stops being true.
    ///
    /// Nothing calls this at runtime. The results are authored into <see cref="DecorSettings"/> and
    /// read from there, so no texture is ever sampled while the game is running.
    /// </summary>
    public static class SpriteTintBaking
    {
        /// <summary>Below this a raw channel is treated as this, so dividing by it cannot explode.</summary>
        const float MinimumChannel = 0.02f;

        /// <summary>How many pixels across the longest edge to sample at most. The average of a rock does not need every texel.</summary>
        const int SampleResolution = 48;

        /// <summary>
        /// A sprite's own average colour over its opaque pixels - a constant of the art, which is why
        /// it can be baked at all.
        ///
        /// Sprite textures are non-readable as imported, so GetPixel would throw; the texture is
        /// blitted through a RenderTexture first, which works whatever the import settings say and
        /// leaves them alone.
        /// </summary>
        public static Color AverageColorOf(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return Color.white;

            Texture2D source = sprite.texture;
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height);
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Rect region = sprite.textureRect;
            int x0 = Mathf.FloorToInt(region.x);
            int y0 = Mathf.FloorToInt(region.y);
            int width = Mathf.FloorToInt(region.width);
            int height = Mathf.FloorToInt(region.height);
            int step = Mathf.Max(1, Mathf.Min(width, height) / SampleResolution);

            float r = 0f, g = 0f, b = 0f;
            int counted = 0;

            for (int y = y0; y < y0 + height; y += step)
            {
                for (int x = x0; x < x0 + width; x += step)
                {
                    Color pixel = readable.GetPixel(x, y);
                    if (pixel.a < 0.5f) continue;   // transparent margin says nothing about the art

                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    counted++;
                }
            }

            Object.DestroyImmediate(readable);
            return counted > 0 ? new Color(r / counted, g / counted, b / counted, 1f) : Color.white;
        }

        /// <summary>
        /// The multiplier that pulls a sprite averaging <paramref name="raw"/> towards
        /// <paramref name="target"/>.
        ///
        /// <b>Capped at 1 per channel, and that cap is not a detail.</b> A SpriteRenderer colour can
        /// only darken: a multiplier above 1 does not brighten the art, it clips the channel to white.
        /// An uncapped version of this reached above 4 on a rock whose raw blue was very low and made
        /// it look bleached rather than muted. Channels already at or below the target are left alone;
        /// only those above it are pulled down.
        /// </summary>
        public static Color MuteTowards(Color raw, Color target)
        {
            return new Color(
                Mathf.Min(1f, target.r / Mathf.Max(raw.r, MinimumChannel)),
                Mathf.Min(1f, target.g / Mathf.Max(raw.g, MinimumChannel)),
                Mathf.Min(1f, target.b / Mathf.Max(raw.b, MinimumChannel)),
                1f);
        }

        /// <summary>
        /// The ground tone decor is muted towards: the darker of the profile's first two base
        /// textures. Darker rather than an average of both, because a rock reading slightly too dark
        /// for the ground it sits on looks like shadow, and one reading too light looks like litter.
        /// </summary>
        public static Color GroundToneOf(Texture2D first, Texture2D second)
        {
            Color a = AverageColorOfTexture(first);
            Color b = AverageColorOfTexture(second);
            return a.r + a.g + a.b <= b.r + b.g + b.b ? a : b;
        }

        /// <summary>A whole texture's average, sampled small. Same blit for the same non-readable reason.</summary>
        public static Color AverageColorOfTexture(Texture2D source)
        {
            if (source == null) return Color.white;

            RenderTexture rt = RenderTexture.GetTemporary(32, 32);
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
            readable.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Color[] pixels = readable.GetPixels();
            float r = 0f, g = 0f, b = 0f;
            foreach (Color pixel in pixels)
            {
                r += pixel.r;
                g += pixel.g;
                b += pixel.b;
            }

            Object.DestroyImmediate(readable);
            return new Color(r / pixels.Length, g / pixels.Length, b / pixels.Length, 1f);
        }
    }
}
