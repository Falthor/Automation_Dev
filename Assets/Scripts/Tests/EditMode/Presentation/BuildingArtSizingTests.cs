using System.IO;
using Game.Data;
using Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// How big a building is drawn is derived, never entered: as wide as its footprint, as tall as
    /// that width times its frame's own proportion (BuildingSpawner.ArtWorldSize).
    ///
    /// <b>This replaced two numbers that went stale on every re-export</b> - an art box per
    /// definition and a margin constant measured against one particular sheet - and both were
    /// noticed on screen rather than here: a stale 1.09 drew the Foundry 7% wider than the cells it
    /// stands on. Nothing here is measured against a file any more; what is asserted is the rule,
    /// against the real definitions, so any sheet that comes back is treated the same way.
    /// </summary>
    public class BuildingArtSizingTests
    {
        const float CellSize = 1f;

        /// <summary>Every building drawn from a sheet the artist re-exports, so a new cut of any of them is held to this.</summary>
        static readonly string[] DefinitionPaths =
        {
            "Assets/Data/World/CoreDefinition.asset",
            "Assets/Data/Buildings/ConstructorDefinition.asset",
            "Assets/Data/Buildings/FactoryDefinition.asset",
            "Assets/Data/Buildings/FoundryDefinition.asset",
            "Assets/Data/Buildings/PowerplantGazDefinition.asset",
            "Assets/Data/Buildings/ExtractorDefinition.asset",
            "Assets/Data/Buildings/DataCenterDefinition.asset",
        };

        /// <summary>The sheets exported for the derived rule - 512x640 frames drawn to their own edges.</summary>
        static readonly string[] ReExportedSheetPaths =
        {
            "Assets/Data/World/CoreDefinition.asset",
            "Assets/Data/Buildings/ConstructorDefinition.asset",
            "Assets/Data/Buildings/FactoryDefinition.asset",
            "Assets/Data/Buildings/FoundryDefinition.asset",
            "Assets/Data/Buildings/PowerplantGazDefinition.asset",
            "Assets/Data/Buildings/DataCenterDefinition.asset",
        };

        [Test]
        public void NoBuildingIsEverDrawnWiderThanTheCellsItStandsOn()
        {
            foreach (string path in DefinitionPaths)
            {
                var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
                Assert.IsNotNull(definition, path);
                Assert.IsNotNull(definition.Sprite, $"{definition.Id} has art assigned");

                Vector2 drawn = BuildingSpawner.ArtWorldSize(definition, CellSize, definition.Sprite);

                Assert.AreEqual(definition.FootprintSize.x * CellSize, drawn.x, 0.0001f,
                    $"{definition.Id} is drawn {drawn.x} wide on {definition.FootprintSize.x} cells");
            }
        }

        /// <summary>
        /// The height follows the art, not a figure someone typed: a 512x640 frame over a 3x3
        /// footprint is 3 wide and 3.75 tall. A frame no taller than it is wide keeps the footprint's
        /// own height, which is what stops a belt's 256x222 frame from leaving a seam in its cell.
        /// </summary>
        [Test]
        public void TheHeightFollowsTheFramesProportion_AndNeverFallsBelowTheFootprint()
        {
            foreach (string path in DefinitionPaths)
            {
                var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
                Vector2 frame = definition.Sprite.rect.size;
                Vector2 drawn = BuildingSpawner.ArtWorldSize(definition, CellSize, definition.Sprite);

                float expected = Mathf.Max(definition.FootprintSize.y * CellSize, drawn.x * (frame.y / frame.x));
                Assert.AreEqual(expected, drawn.y, 0.0001f, $"{definition.Id} from a {frame.x}x{frame.y} frame");
                Assert.GreaterOrEqual(drawn.y, definition.FootprintSize.y * CellSize - 0.0001f, definition.Id);
            }
        }

        /// <summary>
        /// And the excess height goes upward: the lift is exactly what puts the art's bottom edge on
        /// the footprint's bottom edge, so a building stands on its ground rather than straddling it.
        /// The bug this pins was visible - the Core hung half a cell below its own footprint.
        /// </summary>
        [Test]
        public void TheLiftPutsTheArtsBaseOnTheFootprintsBottomEdge()
        {
            foreach (string path in DefinitionPaths)
            {
                var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
                Vector2 drawn = BuildingSpawner.ArtWorldSize(definition, CellSize, definition.Sprite);
                float lift = BuildingSpawner.ArtLift(definition, CellSize, definition.Sprite);

                // Measured from the footprint's centre, where every view puts the art's own centre.
                float artBottom = lift - drawn.y * 0.5f;
                float footprintBottom = -definition.FootprintSize.y * CellSize * 0.5f;

                Assert.AreEqual(footprintBottom, artBottom, 0.0001f, $"{definition.Id}'s base");
                Assert.GreaterOrEqual(lift, -0.0001f, $"{definition.Id} is never pushed down");
            }
        }

        /// <summary>
        /// The art of the current sheets reaches its own frame edges, which is what makes deriving
        /// the size from the frame the same thing as deriving it from the building. Measured on the
        /// source PNG, and loose (94%) on purpose: this guards against a sheet exported with wide
        /// empty margins - which would draw the building visibly smaller than its ground - not
        /// against a pixel.
        ///
        /// Held against the re-exported sheets only. The older art (the Extractor's fills 91% of its
        /// frame) predates the rule and is drawn exactly as it always was, since a square frame takes
        /// the footprint's own box; asking it to pass would be asking for a re-export, not a fix.
        /// </summary>
        [Test]
        public void TheArtFillsItsFrame_SoTheFrameIsAFairMeasureOfTheBuilding()
        {
            foreach (string path in ReExportedSheetPaths)
            {
                var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
                float opaqueFraction = OpaqueWidthFractionOf(definition.Sprite);

                Assert.GreaterOrEqual(opaqueFraction, 0.94f,
                    $"{definition.Id}'s art fills only {opaqueFraction:P1} of its frame's width, so it would be drawn "
                    + "that much narrower than its footprint. Re-export the sheet to its edges rather than "
                    + "compensating for the margin somewhere in code.");
            }
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
