using Game.Data;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests.EditMode.Data
{
    /// <summary>
    /// The settings the game actually ships with, not a fixture.
    ///
    /// <b>Why a test and not just OnValidate.</b> The asset's OnValidate does warn, but only when
    /// Unity imports or reloads it - not while a value is being typed into the Inspector. So an
    /// inconsistent setting can sit in the project for a whole session, and be committed, without
    /// anyone seeing anything. This runs on every suite, whatever put the value there.
    ///
    /// Deliberately reads the real asset by path. A test over a freshly constructed instance would
    /// only assert the C# field initialisers, which is not where a bad value comes from.
    /// </summary>
    public class SectorSettingsTests
    {
        const string AssetPath = "Assets/Data/World/SectorSettings.asset";

        static SectorSettings Load()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SectorSettings>(AssetPath);
            Assert.IsNotNull(settings, $"{AssetPath} is missing - GameRuntime reads its sector sizes and risk thresholds from it.");
            return settings;
        }

        /// <summary>
        /// The one structural rule: sectors have to tile chunks exactly. Otherwise every per-chunk
        /// rule and every per-sector rule disagree about where their boundaries are, which is the
        /// thing choosing a single division was meant to prevent.
        /// </summary>
        [Test]
        public void SectorsTileChunksExactly()
        {
            SectorSettings settings = Load();

            Assert.IsTrue(settings.SectorsTileChunksExactly,
                $"a sector of {settings.SectorSizeCells} does not divide a chunk of {settings.ChunkSizeCells}.");
            Assert.AreEqual(settings.ChunkSizeCells / settings.SectorSizeCells, settings.SectorsPerChunkAxis);
        }

        [Test]
        public void TheShippedDivisionIsSixteenInSixtyFour()
        {
            SectorSettings settings = Load();

            Assert.AreEqual(64, settings.ChunkSizeCells);
            Assert.AreEqual(16, settings.SectorSizeCells);
            Assert.AreEqual(4, settings.SectorsPerChunkAxis, "4x4 sectors per chunk.");
        }

        [Test]
        public void TheRiskThresholdsGrowOutward()
        {
            SectorSettings settings = Load();

            Assert.Less(settings.LowRiskWithinCells, settings.ModerateRiskWithinCells);
            Assert.Less(settings.ModerateRiskWithinCells, settings.HighRiskWithinCells);
            Assert.Greater(settings.LowRiskWithinCells, 0f, "A zero first threshold would make nowhere safe, including home.");
        }

        /// <summary>
        /// The player has to start somewhere that reads as their own ground. The Core's initial radius
        /// is 40, so a first threshold below that would put the starting base in a risk band it has no
        /// business being in - which is exactly what the previous ring-counted thresholds did.
        /// </summary>
        [Test]
        public void TheStartingAreaIsInTheLowestRiskBand()
        {
            SectorSettings settings = Load();

            Assert.GreaterOrEqual(settings.LowRiskWithinCells, 40f,
                "The Core starts with a radius of 40; everything inside it should read as safe.");
        }
    }
}
