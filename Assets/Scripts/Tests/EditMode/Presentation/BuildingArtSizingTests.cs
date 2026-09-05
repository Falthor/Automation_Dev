using System.IO;
using Game.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// RenderOverscan is not a free number: for a building whose art is meant to fill its own
    /// footprint, it compensates for the transparent margin around that art, and is therefore
    /// 1 / (opaque width as a fraction of the frame). That makes it a property of the art file, and
    /// it goes stale the moment the file is replaced.
    ///
    /// Which has now happened twice, both times noticed on screen rather than here. The Foundry's
    /// 1.09 was measured against a sheet with 21 px margins; the replacement has 4 px margins, and
    /// the stale value drew the building 7% wider than the cells it occupies.
    /// </summary>
    public class BuildingArtSizingTests
    {
        /// <summary>
        /// Deliberately only the Foundry. Conveyor, Splitter and Crossroad also override
        /// RenderOverscan, but for the opposite reason: theirs pushes their arms INTO the
        /// neighbouring cell to close a seam. Holding them to this rule would be asserting the very
        /// thing they are built to break.
        /// </summary>
        [Test]
        public void TheFoundrysRenderOverscan_MatchesTheMarginOfTheArtItIsSetAgainst()
        {
            var definition = AssetDatabase.LoadAssetAtPath<FoundryDefinition>("Assets/Data/Buildings/FoundryDefinition.asset");
            Assert.IsNotNull(definition, "The Foundry definition asset.");
            Assert.IsNotNull(definition.Sprite, "The Foundry has art assigned.");

            float opaqueFraction = OpaqueWidthFractionOf(definition.Sprite);
            float expected = 1f / opaqueFraction;

            Assert.AreEqual(expected, definition.RenderOverscan, 0.01f,
                $"The art's opaque box fills {opaqueFraction:P1} of its frame, so RenderOverscan should be "
                + $"{expected:0.000}, not {definition.RenderOverscan:0.000}. Re-measure it against the current art "
                + "rather than adjusting the drawn size somewhere else.");
        }

        /// <summary>
        /// Measured from the PNG on disk rather than the imported texture, which is not readable -
        /// and reading the source is the more honest measurement anyway, since it is what the artist
        /// produced. Falls back to the imported size if the importer rescaled the sheet.
        /// </summary>
        static float OpaqueWidthFractionOf(Sprite sprite)
        {
            string path = AssetDatabase.GetAssetPath(sprite.texture);
            Assert.IsNotEmpty(path, "The sprite's texture has an asset path.");

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(decoded.LoadImage(File.ReadAllBytes(path)), "The source PNG decodes.");

                // sprite.rect is in imported-texture pixels; the source may differ if the importer
                // rescaled it, so everything is measured in source pixels.
                float scale = (float)decoded.width / sprite.texture.width;
                var frame = new RectInt(
                    Mathf.RoundToInt(sprite.rect.x * scale),
                    Mathf.RoundToInt(sprite.rect.y * scale),
                    Mathf.RoundToInt(sprite.rect.width * scale),
                    Mathf.RoundToInt(sprite.rect.height * scale));

                Color32[] pixels = decoded.GetPixels32();

                int minX = int.MaxValue;
                int maxX = int.MinValue;
                const byte AlphaThreshold = 8;

                for (int y = 0; y < frame.height; y++)
                {
                    int row = (frame.y + y) * decoded.width;
                    for (int x = 0; x < frame.width; x++)
                    {
                        if (pixels[row + frame.x + x].a < AlphaThreshold) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                    }
                }

                Assert.Less(minX, int.MaxValue, "The frame has opaque pixels at all.");
                return (maxX - minX + 1) / (float)frame.width;
            }
            finally
            {
                Object.DestroyImmediate(decoded);
            }
        }
    }
}
