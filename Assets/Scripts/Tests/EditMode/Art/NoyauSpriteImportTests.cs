using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Art
{
    /// <summary>
    /// Pins the import settings of the still Core art. The settings live in the asset's .meta
    /// (this project has no editor tooling at all, so there is no AssetPostprocessor to hang them
    /// on) and a .meta is exactly the kind of file a careless re-import silently rewrites - hence
    /// a test rather than a comment.
    ///
    /// The one that actually matters is the size: 1024 px at 256 pixels-per-unit is 4 world units,
    /// which is exactly the Core's 4x4 footprint at this project's cellSize of 1 - so a spawner
    /// fits it at scale 1 instead of rescaling it.
    ///
    /// CoreDefinition currently shows the 12-frame animated sheet instead, so nothing asserts
    /// which of the two is wired in: that is an art choice, free to change either way, and a test
    /// pinning it would only have to be rewritten each time it does.
    /// </summary>
    public sealed class NoyauSpriteImportTests
    {
        const string AssetPath = "Assets/Art/Buildings/noyau_1024.png";

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

        [Test]
        public void Importer_StaysLossless()
        {
            TextureImporterPlatformSettings platform = Importer().GetDefaultPlatformTextureSettings();

            Assert.AreEqual(TextureImporterCompression.Uncompressed, platform.textureCompression);
            Assert.GreaterOrEqual(platform.maxTextureSize, 1024);
        }
    }
}
