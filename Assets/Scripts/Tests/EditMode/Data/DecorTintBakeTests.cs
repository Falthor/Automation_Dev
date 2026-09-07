using Game.Data;
using Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Data
{
    /// <summary>
    /// The decor sprite tints are baked into `DecorSettings` rather than measured at every launch,
    /// because a sprite's average colour never changes. What *can* change is the ground they are muted
    /// towards — and when it does, nothing in the running game notices: the rocks simply stop matching
    /// their terrain, which reads as art rather than as a defect.
    ///
    /// So this recomputes what the baker would write today and compares it to what the asset holds.
    /// The same hazard, and the same answer, as the sorting ranks baked into a scene: a value frozen
    /// in an asset needs a test that recomputes it, or it stays green while it stops being true.
    ///
    /// It reads the **shipped** assets — the settings the game loads and the ground profile the
    /// shipped scene references — never a fixture copy. That rule was learned the hard way from a
    /// sector-name test that passed against its own constant while the real map had outgrown it.
    /// </summary>
    public class DecorTintBakeTests
    {
        const string DecorSettingsPath = "Assets/Data/World/DecorSettings.asset";
        const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        /// <summary>Colours survive a bake through a serialized float, so an exact comparison would fail on the last bit.</summary>
        const float Tolerance = 0.002f;

        static DecorSettings LoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DecorSettings>(DecorSettingsPath);
            Assert.IsNotNull(settings, $"{DecorSettingsPath} is missing - the shipped decor settings are what this test exists to check.");
            return settings;
        }

        static GroundTextureProfile LoadGroundProfile()
        {
            foreach (string path in AssetDatabase.GetDependencies(BootstrapScenePath, true))
            {
                var profile = AssetDatabase.LoadAssetAtPath<GroundTextureProfile>(path);
                if (profile != null) return profile;
            }

            Assert.Fail($"No GroundTextureProfile among the dependencies of {BootstrapScenePath}.");
            return null;
        }

        static Color GroundTone(GroundTextureProfile profile)
        {
            Texture2D first = profile.baseTextures.Length > 0 ? profile.baseTextures[0] : null;
            Texture2D second = profile.baseTextures.Length > 1 ? profile.baseTextures[1] : first;
            return SpriteTintBaking.GroundToneOf(first, second);
        }

        [Test]
        public void EverySpriteHasABakedTint()
        {
            DecorSettings settings = LoadSettings();
            Assert.Greater(settings.Kinds.Length, 0);

            foreach (DecorSettings.Kind kind in settings.Kinds)
            {
                Assert.AreEqual(kind.Sprites.Length, kind.SpriteTints.Length,
                    $"kind '{kind.Id}' has {kind.Sprites.Length} sprites but {kind.SpriteTints.Length} tints - "
                    + "a sprite was added or removed without re-running Tools/Decor/Bake Sprite Tints, so some sprites draw untinted.");
            }
        }

        /// <summary>
        /// The one that catches a ground change. If the profile's base textures move, every tint the
        /// asset holds was computed against a tone that no longer exists.
        /// </summary>
        [Test]
        public void TheBakedTints_StillMatchWhatTheArtWouldGiveToday()
        {
            DecorSettings settings = LoadSettings();
            Color ground = GroundTone(LoadGroundProfile());

            foreach (DecorSettings.Kind kind in settings.Kinds)
            {
                for (int s = 0; s < kind.Sprites.Length && s < kind.SpriteTints.Length; s++)
                {
                    Color raw = SpriteTintBaking.AverageColorOf(kind.Sprites[s]);
                    Color expected = SpriteTintBaking.MuteTowards(raw, ground);
                    Color actual = kind.SpriteTints[s];

                    string where = $"kind '{kind.Id}', sprite {s} ({(kind.Sprites[s] != null ? kind.Sprites[s].name : "null")})";
                    string how = " - re-run Tools/Decor/Bake Sprite Tints. The art or the ground profile moved since the tints were baked.";

                    Assert.AreEqual(expected.r, actual.r, Tolerance, where + " red" + how);
                    Assert.AreEqual(expected.g, actual.g, Tolerance, where + " green" + how);
                    Assert.AreEqual(expected.b, actual.b, Tolerance, where + " blue" + how);
                }
            }
        }

        /// <summary>
        /// A SpriteRenderer colour can only darken. An uncapped multiplier reached above 4 on a rock
        /// whose raw blue was very low and bleached it to white instead of muting it - so the cap is
        /// what the formula is *for*, and a baked value above 1 would mean it had been bypassed.
        /// </summary>
        [Test]
        public void NoBakedTintBrightens()
        {
            foreach (DecorSettings.Kind kind in LoadSettings().Kinds)
            {
                foreach (Color tint in kind.SpriteTints)
                {
                    Assert.LessOrEqual(tint.r, 1f, $"kind '{kind.Id}' holds a tint above 1 - it would clip to white, not brighten.");
                    Assert.LessOrEqual(tint.g, 1f, $"kind '{kind.Id}' holds a tint above 1 - it would clip to white, not brighten.");
                    Assert.LessOrEqual(tint.b, 1f, $"kind '{kind.Id}' holds a tint above 1 - it would clip to white, not brighten.");
                }
            }
        }
    }
}
