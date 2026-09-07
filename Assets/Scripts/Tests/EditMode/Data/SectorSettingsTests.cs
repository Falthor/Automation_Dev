using Game.Data;
using Game.Gameplay.Sectors;
using Game.Grid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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

        /// <summary>
        /// Enough region names for the map the game actually ships with.
        ///
        /// <b>This used to be a tripwire and is now a statement.</b> It once asserted that the pool
        /// held one name per sector; that duly went off when the map reached 10 000 and 390 625
        /// sectors wanted 768 names. Names stopped being one per sector, and the region count became
        /// derived and capped - so the property now holds by construction and this checks that the
        /// construction is still what it claims, rather than waiting to catch it failing.
        ///
        /// It reads both assets: the map's size from TerrainGenerationSettings, the sector's from
        /// SectorSettings. A test about the shipped game has to read the shipped game - the lesson of
        /// the version of it that measured its own fixture constant and stayed green for months.
        /// </summary>
        [Test]
        public void EveryRegionOfTheShippedMapHasAnUnsharedName()
        {
            var terrain = AssetDatabase.LoadAssetAtPath<TerrainGenerationSettings>("Assets/Data/Terrain/DefaultTerrain.asset");
            Assert.IsNotNull(terrain, "the terrain settings asset is missing");

            SectorSettings settings = Load();
            var grid = new SectorGrid(terrain.Size, settings.SectorSizeCells);
            var catalog = new SectorCatalog(grid, 1, Vector2.zero, 40f, 250f, 330f, settings.PreferredRegionSizeCells);

            int regions = catalog.RegionsPerAxis * catalog.RegionsPerAxis;

            Assert.LessOrEqual(regions, SectorCatalog.NameCombinationCount,
                $"a {terrain.Size}-cell map is cut into {regions} regions and the vocabulary offers "
                + $"{SectorCatalog.NameCombinationCount} names. The region count is supposed to be capped so this "
                + "cannot happen - the cap has been broken, not the vocabulary outgrown.");
        }

        /// <summary>
        /// No map size, however large, can produce more regions than there are names. The cap is what
        /// turned this from something to test into something to state, so what is worth testing is
        /// the cap itself - across sizes far past anything planned.
        /// </summary>
        [Test]
        public void NoMapSizeCanOutgrowTheNamePool()
        {
            SectorSettings settings = Load();

            foreach (int mapSize in new[] { 64, 300, 1000, 10000, 40000, 250000 })
            {
                var grid = new SectorGrid(mapSize, settings.SectorSizeCells);
                var catalog = new SectorCatalog(grid, 1, Vector2.zero, 40f, 250f, 330f, settings.PreferredRegionSizeCells);

                int regions = catalog.RegionsPerAxis * catalog.RegionsPerAxis;
                Assert.LessOrEqual(regions, SectorCatalog.NameCombinationCount,
                    $"a map of {mapSize} produced {regions} regions");
                Assert.GreaterOrEqual(catalog.RegionSizeCells, 1);
            }
        }
    }
}
