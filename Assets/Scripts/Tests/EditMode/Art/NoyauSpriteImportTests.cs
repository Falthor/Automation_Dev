using Game.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Art
{
    /// <summary>
    /// Pins the import settings of the Core's 1024x1024 art. The settings live in the asset's
    /// .meta (this project has no editor tooling at all, so there is no AssetPostprocessor to hang
    /// them on) and a .meta is exactly the kind of file a careless re-import silently rewrites -
    /// hence a test rather than a comment.
    ///
    /// The one that actually matters is the size: 1024 px at 256 pixels-per-unit is 4 world units,
    /// which is exactly the Core's 4x4 footprint at this project's cellSize of 1 - so
    /// WorldContentSpawner fits it at scale 1 instead of rescaling it.
    /// </summary>
    public sealed class NoyauSpriteImportTests
    {
        const string AssetPath = "Assets/Art/Buildings/noyau_1024.png";
        const string CoreDefinitionPath = "Assets/Data/World/CoreDefinition.asset";

        /// <summary>Kept in step with CoreDefinition.footprintSize; the sprite is authored to fill it exactly.</summary>
        const float ExpectedWorldSize = 4f;

        TextureImporter Importer()
        {
            var importer = AssetImporter.GetAtPath(AssetPath) as TextureImporter;
            Assert.IsNotNull(importer, $"{AssetPath} is not imported as a texture.");
            return importer;
        }

        /// <summary>Mesh type and pivot alignment live on TextureImporterSettings, not on the importer itself.</summary>
        TextureImporterSettings ImporterSettings()
        {
            var settings = new TextureImporterSettings();
            Importer().ReadTextureSettings(settings);
            return settings;
        }

        [Test]
        public void Sprite_MatchesTheCoresFootprintInWorldUnits()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetPath);
            Assert.IsNotNull(sprite, $"{AssetPath} did not import as a Sprite.");

            Assert.AreEqual(ExpectedWorldSize, sprite.bounds.size.x, 0.0001f);
            Assert.AreEqual(ExpectedWorldSize, sprite.bounds.size.y, 0.0001f);
        }

        [Test]
        public void Texture_KeepsItsFullResolution()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            Assert.IsNotNull(texture, $"{AssetPath} did not import as a Texture2D.");

            Assert.AreEqual(1024, texture.width);
            Assert.AreEqual(1024, texture.height);
        }

        [Test]
        public void Importer_UsesTheRequestedSpriteSettings()
        {
            TextureImporter importer = Importer();
            TextureImporterSettings settings = ImporterSettings();

            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode);
            Assert.AreEqual(256f, importer.spritePixelsPerUnit);
            Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType);
            Assert.AreEqual(FilterMode.Bilinear, importer.filterMode);
            Assert.IsTrue(importer.alphaIsTransparency);
        }

        [Test]
        public void Importer_PivotsOnTheCenter()
        {
            TextureImporterSettings settings = ImporterSettings();

            Assert.AreEqual((int)SpriteAlignment.Center, settings.spriteAlignment);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), settings.spritePivot);
        }

        /// <summary>
        /// The art is only reachable through CoreDefinition - nothing else in the project points at
        /// it - so the wiring belongs with the import settings rather than in its own file.
        /// </summary>
        [Test]
        public void CoreDefinition_ShowsThisSpriteAtItsOwnFootprint()
        {
            var core = AssetDatabase.LoadAssetAtPath<CoreDefinition>(CoreDefinitionPath);
            Assert.IsNotNull(core, $"{CoreDefinitionPath} is missing.");

            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Sprite>(AssetPath), core.Sprite);
            Assert.AreEqual(new Vector2Int(4, 4), core.FootprintSize,
                "The sprite is authored to fill a 4x4 footprint exactly; a different footprint means the import PPU is now wrong.");
        }

        /// <summary>WorldContentSpawner hands the frame list to SpriteFlipbook, which then owns the renderer's sprite - two or more frames would simply hide the still image assigned above.</summary>
        [Test]
        public void CoreDefinition_HasNoLeftoverFlipbookHidingTheStill()
        {
            var core = AssetDatabase.LoadAssetAtPath<CoreDefinition>(CoreDefinitionPath);

            Assert.Less(core.AnimationFrames.Length, 2);
        }

        [Test]
        public void Importer_StaysLossless()
        {
            TextureImporterPlatformSettings platform = Importer().GetDefaultPlatformTextureSettings();

            Assert.AreEqual(TextureImporterCompression.Uncompressed, platform.textureCompression);
            Assert.GreaterOrEqual(platform.maxTextureSize, 1024);
        }
    }
}
