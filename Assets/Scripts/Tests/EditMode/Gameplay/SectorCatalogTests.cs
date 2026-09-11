using System.Collections.Generic;
using Game.Core;
using Game.Data;
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

        // The shipped cluster figures, restated rather than read from the asset: a test that followed
        // the setting could not fail when the setting is wrong.
        const int OneSectorIn = 12;
        const int NearMinTiles = 6;
        const int NearMaxTiles = 10;
        const int FarMinTiles = 10;
        const int FarMaxTiles = 15;

        /// <summary>The two ends of the ramp, fixed here so the pinned cases below stay pinned. The shipped ones are GameRuntime.FurthestActionRadiusCells and ExplorerRobotSettings.MaxRadiusCells.</summary>
        const float NearRadius = 32f;
        const float FarRadius = 330f;

        /// <summary>The middle of the fixture map, where a generated world puts its Core.</summary>
        static readonly Vector2 CoreCentre = new Vector2(MapSize / 2f, MapSize / 2f);

        static OreClusterProfile Profile => new OreClusterProfile(
            OneSectorIn, NearMinTiles, NearMaxTiles, FarMinTiles, FarMaxTiles, NearRadius, FarRadius);

        static SectorCatalog NewCatalog(int seed = Seed)
            => new SectorCatalog(new SectorGrid(MapSize, SectorSize), seed, CoreCentre, Profile);

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

            for (int index = 0; index < 361; index += 7)
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

            // Which sectors carry ore at all, rather than a cell-by-cell diff: at one sector in
            // twelve most of them are bare in both worlds, so comparing every sector would report
            // agreement about emptiness and call that a similar map.
            int onlyInA = 0;
            int onlyInB = 0;

            for (int index = 0; index < a.Grid.Count; index++)
            {
                bool inA = a.ContentsOf(index).Feature == SectorFeature.OreCluster;
                bool inB = b.ContentsOf(index).Feature == SectorFeature.OreCluster;

                if (inA && !inB) onlyInA++;
                else if (inB && !inA) onlyInB++;
            }

            Assert.Greater(onlyInA, 10, "A new world should put its ore somewhere else.");
            Assert.Greater(onlyInB, 10);
        }

        // ---- The clusters ----

        /// <summary>
        /// <b>Ore is rare, and this is the number that says how rare.</b> It used to be four sectors
        /// in eight, with a wreck and a nest yielding ore as well, so seven in eight held some - and a
        /// robot materialises the 3x3 block around every sector it crosses. One sortie turned up
        /// dozens of tiles, which is not a find.
        ///
        /// Measured over the whole fixture map rather than asserted at a rate, and printed, because
        /// the figure is the thing being judged.
        /// </summary>
        [Test]
        public void OneSectorInTwelve_CarriesOre()
        {
            var catalog = NewCatalog();

            int carrying = 0;
            for (int index = 0; index < catalog.Grid.Count; index++)
            {
                if (catalog.ContentsOf(index).Feature == SectorFeature.OreCluster) carrying++;
            }

            float oneIn = catalog.Grid.Count / (float)carrying;
            TestContext.Out.WriteLine($"{carrying} of {catalog.Grid.Count} sectors carry ore: one in {oneIn:F1}");

            // A wide band: the assertion is that the rate is roughly what the setting says, not that
            // this particular seed lands on it.
            Assert.Greater(oneIn, OneSectorIn * 0.6f);
            Assert.Less(oneIn, OneSectorIn * 1.7f);
        }

        /// <summary>
        /// <b>A cluster is one patch of touching cells, not a handful scattered over 256.</b> That is
        /// the difference between something to aim an Extractor at and litter, and it was the other
        /// half of what the old derivation got wrong.
        /// </summary>
        [Test]
        public void EveryClusterIsOnePatchOfTouchingCells()
        {
            var catalog = NewCatalog();
            int checkedClusters = 0;

            for (int index = 0; index < catalog.Grid.Count; index++)
            {
                GridCoord[] cells = catalog.ContentsOf(index).DepositCells;
                if (cells.Length == 0) continue;

                checkedClusters++;
                var patch = new HashSet<GridCoord>(cells);

                foreach (GridCoord cell in cells)
                {
                    bool touches =
                        patch.Contains(new GridCoord(cell.X + 1, cell.Y)) ||
                        patch.Contains(new GridCoord(cell.X - 1, cell.Y)) ||
                        patch.Contains(new GridCoord(cell.X, cell.Y + 1)) ||
                        patch.Contains(new GridCoord(cell.X, cell.Y - 1));

                    Assert.IsTrue(touches, $"sector {index} has a deposit at {cell} with no neighbour - that is litter, not a cluster");
                }
            }

            Assert.Greater(checkedClusters, 10, "the fixture map has to hold enough clusters to be worth checking");
        }

        /// <summary>
        /// Every cluster sits inside the band its own distance allows - six to ten just outside the
        /// Core's reach, ten to fifteen at the limit, interpolated between (OreClusterProfile).
        ///
        /// Asserted against the profile rather than against literals, because the profile is where
        /// the decision lives; the literals that matter are pinned in OreClusterProfileTests, once.
        /// </summary>
        [Test]
        public void EveryClusterFitsTheBandItsDistanceAllows()
        {
            var catalog = NewCatalog();

            int smallest = int.MaxValue;
            int largest = 0;
            int total = 0;
            int clusters = 0;

            for (int index = 0; index < catalog.Grid.Count; index++)
            {
                int size = catalog.ContentsOf(index).DepositCells.Length;
                if (size == 0) continue;

                float distance = Vector2.Distance(catalog.Grid.CenterCells(index), CoreCentre);
                int floor = Profile.MinTilesAt(distance);
                int ceiling = Profile.MaxTilesAt(distance);

                Assert.GreaterOrEqual(size, floor, $"sector {index} is {distance:F0} cells out and holds {size} tiles, under its floor of {floor}");
                Assert.LessOrEqual(size, ceiling, $"sector {index} is {distance:F0} cells out and holds {size} tiles, over its ceiling of {ceiling}");

                clusters++;
                total += size;
                smallest = Mathf.Min(smallest, size);
                largest = Mathf.Max(largest, size);
            }

            TestContext.Out.WriteLine($"{clusters} clusters: {smallest} to {largest} tiles, {total / (float)clusters:F1} on average");
            Assert.Greater(clusters, 10);
        }

        /// <summary>
        /// <b>Walking further has to pay, and this is the measurement that says it does.</b> Read on a
        /// map the size the game ships with, because the fixture's 300 cells barely reach a third of
        /// the way up the ramp - a test on it would assert the formula rather than its effect.
        ///
        /// Printed, because the figures are the thing being judged.
        /// </summary>
        [Test]
        public void FarClustersAreBiggerThanNearOnes()
        {
            const int ShippedMapSize = 10000;

            var grid = new SectorGrid(ShippedMapSize, SectorSize);
            var centre = new Vector2(ShippedMapSize / 2f, ShippedMapSize / 2f);
            var catalog = new SectorCatalog(grid, Seed, centre, Profile);

            int nearCount = 0, nearTiles = 0;
            int farCount = 0, farTiles = 0;

            for (int index = 0; index < grid.Count; index++)
            {
                int size = catalog.ContentsOf(index).DepositCells.Length;
                if (size == 0) continue;

                float distance = Vector2.Distance(grid.CenterCells(index), centre);
                if (distance < NearRadius || distance > FarRadius) continue;

                if (distance < 100f) { nearCount++; nearTiles += size; }
                else if (distance > 260f) { farCount++; farTiles += size; }
            }

            Assert.Greater(nearCount, 3, "too few near clusters to average");
            Assert.Greater(farCount, 3, "too few far clusters to average");

            float near = nearTiles / (float)nearCount;
            float far = farTiles / (float)farCount;
            TestContext.Out.WriteLine($"inside 100 cells: {nearCount} clusters at {near:F1} tiles; past 260: {farCount} at {far:F1}");

            Assert.Greater(far, near + 1.5f, "the far half of the reachable map has to be worth the walk");
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

            for (int index = 0; index < 361; index += 3)
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
        /// <b>They were rewritten twice, deliberately</b>: once when the derivation stopped
        /// scattering two to five cells over every sector and started growing one rare patch, and
        /// again when a patch's size came to depend on how far out it is. Both were changes of scheme,
        /// decided and recorded - the only kind of reason that justifies touching these.
        ///
        /// Two bare sectors are in the list on purpose: emptiness is most of the map now, and a rate
        /// that drifted would show up here first. Sectors 75 and 360 are clipped by the map's edge -
        /// 300 is not a whole number of 16s - so their centres are the middles of 12-wide squares,
        /// which is the case a nominal-square derivation gets wrong.
        /// </summary>
        [TestCase(6, SectorFeature.OreCluster, 104, 8, 9, 0)]
        [TestCase(58, SectorFeature.OreCluster, 24, 56, 9, 1)]
        [TestCase(71, SectorFeature.OreCluster, 232, 56, 10, 2)]
        [TestCase(75, SectorFeature.OreCluster, 294, 56, 8, 1)]
        [TestCase(0, SectorFeature.None, 8, 8, 0, 2)]
        [TestCase(360, SectorFeature.None, 294, 294, 0, 1)]
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
            GridCoord[] deposits = NewCatalog().ContentsOf(6).DepositCells;

            // The growth order, not a sorted set: the sequence is the arithmetic, and sorting the
            // expectation would hide a change to the walk that happened to keep the same footprint.
            CollectionAssert.AreEqual(
                new[]
                {
                    new GridCoord(101, 4),
                    new GridCoord(101, 5),
                    new GridCoord(102, 5),
                    new GridCoord(100, 4),
                    new GridCoord(101, 6),
                    new GridCoord(102, 4),
                    new GridCoord(100, 5),
                    new GridCoord(99, 4),
                    new GridCoord(102, 3)
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
