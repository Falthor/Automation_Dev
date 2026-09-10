using Game.Core;
using Game.Gameplay.Sectors;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay
{
    /// <summary>
    /// A sector's contents are a pure function of the world seed and its index. One guarantee
    /// matters here and it is the one that would break silently: same seed, same sector, same
    /// contents - whatever the order of discovery, and across a save.
    /// </summary>
    public class SectorCatalogTests
    {
        const int MapSize = 300;
        const int Seed = 20260907;

        const int SectorSize = 16;
        const int ChunkSize = 64;

        static SectorCatalog NewCatalog(int seed = Seed)
            => new SectorCatalog(new SectorGrid(MapSize, SectorSize), seed);

        /// <summary>
        /// Reveals the disc inscribed in a sector. A fixture convenience for asking "would a robot
        /// passing through the middle of this square have seen that cell?" - not something the game
        /// does: a robot reveals discs wherever it happens to be, and never by sector.
        /// </summary>
        static void RevealSector(SectorGrid grid, int index, DiscoveryRuntime discovery)
            => discovery.RevealDisc(grid.CenterCells(index), grid.InscribedRadiusCells);

        // ---- Determinism ----

        [Test]
        public void TheSameSectorContents_ComeBackIdentical()
        {
            var first = NewCatalog();
            var second = NewCatalog();

            for (int index = 0; index < 361; index += 53)
            {
                SectorContents a = first.ContentsOf(index);
                SectorContents b = second.ContentsOf(index);

                Assert.AreEqual(a.Feature, b.Feature, $"feature of sector {index}");
                Assert.AreEqual(a.FeatureCell, b.FeatureCell, $"feature cell of sector {index}");
                Assert.AreEqual(a.ResourceIndex, b.ResourceIndex, $"ore of sector {index}");
                CollectionAssert.AreEqual(a.DepositCells, b.DepositCells, $"deposits of sector {index}");
            }
        }

        /// <summary>
        /// Asking about sectors in a different order must change nothing. Trivially true of a pure
        /// function and worth pinning anyway: the day a cache is added here to save a hash, this is
        /// the test that notices the cache was keyed wrong.
        /// </summary>
        [Test]
        public void TheOrderSectorsAreAskedAbout_ChangesNothing()
        {
            var forward = NewCatalog();
            var backward = NewCatalog();

            var features = new SectorFeature[100];
            var ores = new int[100];
            for (int index = 0; index < 100; index++)
            {
                SectorContents contents = forward.ContentsOf(index);
                features[index] = contents.Feature;
                ores[index] = contents.ResourceIndex;
            }

            for (int index = 99; index >= 0; index--)
            {
                Assert.AreEqual(features[index], backward.ContentsOf(index).Feature, $"feature of sector {index}");
                Assert.AreEqual(ores[index], backward.ContentsOf(index).ResourceIndex, $"ore of sector {index}");
            }
        }

        [Test]
        public void ADifferentSeed_GivesADifferentMap()
        {
            var a = NewCatalog(Seed);
            var b = NewCatalog(Seed + 1);

            int differing = 0;
            for (int index = 0; index < 200; index++)
            {
                SectorContents left = a.ContentsOf(index);
                SectorContents right = b.ContentsOf(index);

                if (left.Feature != right.Feature
                    || left.ResourceIndex != right.ResourceIndex
                    || left.DepositCells.Length != right.DepositCells.Length) differing++;
            }

            Assert.Greater(differing, 120, "A new world should not read like the previous one.");
        }

        // ---- Contents ----

        /// <summary>
        /// The point of interest sits at the sector's centre, so a robot crossing the middle of the
        /// square cannot miss it. The deposits are the opposite case, below.
        /// </summary>
        [Test]
        public void TheFeatureSitsAtTheCentre_SoCrossingTheSquareShowsIt()
        {
            var catalog = NewCatalog();

            for (int index = 0; index < 361; index += 29)
            {
                SectorContents contents = catalog.ContentsOf(index);
                if (contents.Feature == SectorFeature.None) continue;

                var fresh = new DiscoveryRuntime(MapSize, ChunkSize);
                RevealSector(catalog.Grid, index, fresh);

                Assert.IsTrue(fresh.IsDiscovered(contents.FeatureCell), $"sector {index}'s feature fell outside its own inscribed disc");
            }
        }

        [Test]
        public void DepositsAreScatteredAcrossTheWholeSquare_NotOnlyInsideTheDisc()
        {
            var catalog = NewCatalog();
            int outsideTheDisc = 0;

            for (int index = 0; index < catalog.Grid.Count; index++)
            {
                var disc = new DiscoveryRuntime(MapSize, ChunkSize);
                RevealSector(catalog.Grid, index, disc);

                foreach (GridCoord deposit in catalog.ContentsOf(index).DepositCells)
                {
                    if (!disc.IsDiscovered(deposit)) outsideTheDisc++;
                }
            }

            Assert.Greater(outsideTheDisc, 0,
                "If every deposit sat inside the disc, wandering back over the same ground at another angle would find nothing.");
        }

        [Test]
        public void EveryDepositLandsInsideItsOwnSector()
        {
            var catalog = NewCatalog();

            for (int index = 0; index < catalog.Grid.Count; index += 7)
            {
                foreach (GridCoord deposit in catalog.ContentsOf(index).DepositCells)
                {
                    Assert.AreEqual(index, catalog.Grid.IndexAt(deposit), $"a deposit of sector {index} fell in another one");
                }
            }
        }

        [Test]
        public void ABadIndex_ReturnsEmptyContentsRatherThanThrowing()
        {
            var catalog = NewCatalog();

            Assert.DoesNotThrow(() => catalog.ContentsOf(-1));
            Assert.AreEqual(SectorFeature.None, catalog.ContentsOf(-1).Feature);
            Assert.IsNotNull(catalog.ContentsOf(-1).DepositCells);
        }

        // ---- Frozen contents ----

        /// <summary>
        /// Hard-coded features, centres, deposit counts and ores. Two purposes, both of which need
        /// literals rather than a recomputed expectation: they prove a refactor moved nothing, and
        /// they catch a runtime whose arithmetic has changed underneath a world that is derived
        /// rather than saved.
        ///
        /// <b>If this fails, do not update the numbers.</b> Every existing world has just had its
        /// unmaterialised deposits moved; find out what changed.
        ///
        /// Sector 360 is the clipped corner - 300 is not a whole number of 16s - so its centre is the
        /// middle of a 12x12 square, not of a 16x16 one. That is the case a nominal-square scatter
        /// would have got wrong.
        /// </summary>
        [TestCase(0, SectorFeature.Nest, 8, 8, 4, 2)]
        [TestCase(1, SectorFeature.OreCluster, 24, 8, 2, 1)]
        [TestCase(7, SectorFeature.Nest, 120, 8, 2, 0)]
        [TestCase(100, SectorFeature.Wreck, 88, 88, 5, 2)]
        [TestCase(180, SectorFeature.Nest, 152, 152, 2, 1)]
        [TestCase(360, SectorFeature.OreCluster, 294, 294, 4, 1)]
        public void SectorContentsAreFrozen(int index, SectorFeature expectedFeature,
            int expectedCentreX, int expectedCentreY, int expectedDeposits, int expectedResource)
        {
            SectorContents contents = NewCatalog().ContentsOf(index);

            Assert.AreEqual(expectedFeature, contents.Feature, $"feature of sector {index}");
            Assert.AreEqual(new GridCoord(expectedCentreX, expectedCentreY), contents.FeatureCell, $"centre of sector {index}");
            Assert.AreEqual(expectedDeposits, contents.DepositCells.Length, $"deposit count of sector {index}");
            Assert.AreEqual(expectedResource, contents.ResourceIndex, $"ore of sector {index}");
        }

        /// <summary>
        /// One sector's deposits, cell by cell. The counts above would survive a change to the
        /// scatter that moved every deposit; this would not.
        /// </summary>
        [Test]
        public void OneSectorsDepositCells_AreFrozenExactly()
        {
            GridCoord[] deposits = NewCatalog().ContentsOf(100).DepositCells;

            CollectionAssert.AreEqual(
                new[]
                {
                    new GridCoord(82, 85),
                    new GridCoord(81, 95),
                    new GridCoord(85, 88),
                    new GridCoord(93, 88),
                    new GridCoord(80, 81)
                },
                deposits);
        }

        /// <summary>
        /// The mixer moved to <see cref="DeterministicHash"/> when the terrain came to need the same
        /// guarantee. Restated here because the sectors are the other caller: a hash that drifts
        /// recomposes a derived world under buildings that were saved, and the two callers must be
        /// able to fail independently.
        /// </summary>
        [Test]
        public void TheSharedMixerStillAnswersTheSame()
        {
            Assert.AreEqual(3553548991u, DeterministicHash.Mix(20260907, 0, 0xC2B2AE35));
        }
    }
}
