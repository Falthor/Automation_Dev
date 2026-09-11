using Game.Data;
using Game.Gameplay.Wrecks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Data
{
    /// <summary>
    /// The world-generation settings the game actually ships with, not a fixture.
    ///
    /// Reads the real asset by path, following <see cref="SectorSettingsTests"/> and for the same
    /// reason: a test over a freshly constructed instance asserts the C# field initialisers, and an
    /// unassigned reference in an asset is precisely what a field initialiser cannot tell you about.
    /// </summary>
    public class WorldGenerationSettingsTests
    {
        const string AssetPath = "Assets/Data/World/WorldGenerationSettings.asset";

        static WorldGenerationSettings Load()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldGenerationSettings>(AssetPath);
            Assert.IsNotNull(settings, $"{AssetPath} is missing - GameRuntime generates the world from it.");
            return settings;
        }

        /// <summary>
        /// Every wreck type has a sprite, because a type without one is drawn as nothing at all.
        ///
        /// <b>This shipped.</b> The art sat in Assets/Art/wreck imported with sprite mode None, so it
        /// produced no Sprite sub-asset and this array could not even point at it; the array was
        /// empty, <c>WorldContentSpawner.SpawnWreck</c> returned on a null sprite without a word, and
        /// a wreck a robot had found was marked on the map, counted in the harvest log, and absent
        /// from the ground. Every part of the system worked except the drawing of it.
        ///
        /// Asserted against <see cref="WreckField.TypeCount"/> rather than against 3, so raising the
        /// number of types fails here rather than silently drawing one of them as nothing.
        /// </summary>
        [Test]
        public void EveryWreckTypeHasASprite()
        {
            Sprite[] sprites = Load().WreckSprites;

            Assert.IsNotNull(sprites, "wreckSprites is unassigned: every wreck would be invisible on the ground.");
            Assert.GreaterOrEqual(sprites.Length, WreckField.TypeCount,
                $"WreckField.TypeCount is {WreckField.TypeCount}, and WreckSpriteFor indexes this array by "
                + "TypeIndex, so it needs at least that many.");

            for (int i = 0; i < sprites.Length; i++)
            {
                Assert.IsNotNull(sprites[i], $"wreckSprites[{i}] is empty - that wreck type is drawn as nothing.");
            }
        }

        /// <summary>
        /// And there are rings to put wrecks in. An empty profile is a world with no wrecks at all -
        /// legal, runs, finds nothing, and says nothing about why.
        /// </summary>
        [Test]
        public void TheWreckProfile_HasAtLeastOneRing()
        {
            WreckRingProfile profile = Load().WreckProfile;

            Assert.Greater(profile.RingCount, 0, "No rings means no wrecks anywhere in the world.");
            Assert.Greater(profile.TotalCount, 0, "Rings that hold nothing come to the same thing.");
        }
    }
}
