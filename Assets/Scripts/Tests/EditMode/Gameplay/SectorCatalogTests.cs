using System.Collections.Generic;
using Game.Core;
using Game.Gameplay.Sectors;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay
{
    /// <summary>
    /// A sector's identity is a pure function of the world seed and its index. The directive asks
    /// for exactly one guarantee here and it is the one that would break silently: same seed, same
    /// sector, same name and same risk - whatever the order of discovery, and across a save.
    /// </summary>
    public class SectorCatalogTests
    {
        const int MapSize = 300;
        const int Seed = 20260907;

        static readonly Vector2 CoreCenter = new Vector2(150f, 150f);

        static SectorCatalog NewCatalog(int seed = Seed)
            => new SectorCatalog(new SectorGrid(MapSize), seed, CoreCenter);

        // ---- Determinism ----

        [Test]
        public void TheSameSeedAndSector_GiveTheSameNameAndRisk()
        {
            var first = NewCatalog();
            var second = NewCatalog();

            for (int index = 0; index < 625; index += 37)
            {
                Assert.AreEqual(first.NameOf(index), second.NameOf(index), $"name of sector {index}");
                Assert.AreEqual(first.RiskOf(index), second.RiskOf(index), $"risk of sector {index}");
            }
        }

        /// <summary>Asking out of order, or asking twice, must not move anything - there is no state to advance.</summary>
        [Test]
        public void TheOrderSectorsAreAskedAbout_ChangesNothing()
        {
            var forward = NewCatalog();
            var backward = NewCatalog();

            var names = new Dictionary<int, string>();
            for (int index = 0; index < 100; index++) names[index] = forward.NameOf(index);

            for (int index = 99; index >= 0; index--)
            {
                Assert.AreEqual(names[index], backward.NameOf(index));
                Assert.AreEqual(names[index], backward.NameOf(index), "and asking a second time is still the same");
            }
        }

        [Test]
        public void TheSameSectorContents_ComeBackIdentical()
        {
            var first = NewCatalog();
            var second = NewCatalog();

            for (int index = 0; index < 625; index += 53)
            {
                SectorContents a = first.ContentsOf(index);
                SectorContents b = second.ContentsOf(index);

                Assert.AreEqual(a.Feature, b.Feature, $"feature of sector {index}");
                Assert.AreEqual(a.FeatureCell, b.FeatureCell, $"feature cell of sector {index}");
                CollectionAssert.AreEqual(a.DepositCells, b.DepositCells, $"deposits of sector {index}");
            }
        }

        [Test]
        public void ADifferentSeed_GivesADifferentMap()
        {
            var a = NewCatalog(Seed);
            var b = NewCatalog(Seed + 1);

            int differingNames = 0;
            for (int index = 0; index < 200; index++)
            {
                if (a.NameOf(index) != b.NameOf(index)) differingNames++;
            }

            Assert.Greater(differingNames, 150, "A new world should not read like the previous one.");
        }

        // ---- Names ----

        [Test]
        public void NoTwoSectorsShareAName()
        {
            var catalog = NewCatalog();
            var seen = new Dictionary<string, int>();

            for (int index = 0; index < catalog.Grid.Count; index++)
            {
                string name = catalog.NameOf(index);
                if (seen.TryGetValue(name, out int owner)) Assert.Fail($"\"{name}\" used by sectors {owner} and {index}.");
                seen[name] = index;
            }
        }

        /// <summary>
        /// Uniqueness above holds only while there are at least as many name combinations as
        /// sectors. This is the test that fails the day the map grows, instead of the map quietly
        /// growing duplicate names.
        /// </summary>
        [Test]
        public void ThereAreEnoughNamesForEverySectorOfTheCurrentMap()
        {
            Assert.GreaterOrEqual(SectorCatalog.NameCombinationCount, new SectorGrid(MapSize).Count);
        }

        [Test]
        public void ANameIsNeverEmptyOrABareNumber()
        {
            var catalog = NewCatalog();

            for (int index = 0; index < 625; index += 11)
            {
                string name = catalog.NameOf(index);
                Assert.IsNotEmpty(name);
                StringAssert.Contains(" ", name, "A name is a place, not a label.");
                Assert.IsFalse(int.TryParse(name, out _));
            }
        }

        [Test]
        public void AnIndexOffTheMap_HasNoName()
        {
            var catalog = NewCatalog();

            Assert.IsEmpty(catalog.NameOf(-1));
            Assert.IsEmpty(catalog.NameOf(99999));
        }

        // ---- Risk ----

        [Test]
        public void RiskGrowsWithDistanceFromTheCore()
        {
            var catalog = NewCatalog();
            var grid = catalog.Grid;
            int coreSector = grid.IndexAt(new GridCoord(150, 150));

            // Averaged over a row, so the per-sector jitter cannot decide the outcome.
            float near = AverageRisk(catalog, grid, coreSector, 1);
            float far = AverageRisk(catalog, grid, coreSector, 8);

            Assert.Less(near, far, "A far sector should read as a bigger bet than a near one.");
        }

        static float AverageRisk(SectorCatalog catalog, SectorGrid grid, int coreSector, int ring)
        {
            int coreColumn = grid.ColumnOf(coreSector);
            int coreRow = grid.RowOf(coreSector);

            int total = 0;
            int count = 0;

            for (int column = coreColumn - ring; column <= coreColumn + ring; column++)
            {
                int index = grid.IndexAt(column, coreRow + ring);
                if (index < 0) continue;

                total += (int)catalog.RiskOf(index);
                count++;
            }

            return count == 0 ? 0f : total / (float)count;
        }

        // ---- Contents ----

        [Test]
        public void TheFeatureSitsAtTheCentre_SoAMissionAlwaysShowsIt()
        {
            var catalog = NewCatalog();

            for (int index = 0; index < 625; index += 29)
            {
                SectorContents contents = catalog.ContentsOf(index);
                if (contents.Feature == SectorFeature.None) continue;

                var fresh = new DiscoveryRuntime(MapSize);
                catalog.Grid.RevealInscribedDisc(index, fresh);

                Assert.IsTrue(fresh.IsDiscovered(contents.FeatureCell), $"sector {index}'s feature fell outside its own revealed disc");
            }
        }

        [Test]
        public void DepositsAreScatteredAcrossTheWholeSquare_NotOnlyInsideTheDisc()
        {
            var catalog = NewCatalog();
            int outsideTheDisc = 0;

            for (int index = 0; index < catalog.Grid.Count; index++)
            {
                var disc = new DiscoveryRuntime(MapSize);
                catalog.Grid.RevealInscribedDisc(index, disc);

                foreach (GridCoord deposit in catalog.ContentsOf(index).DepositCells)
                {
                    if (!disc.IsDiscovered(deposit)) outsideTheDisc++;
                }
            }

            Assert.Greater(outsideTheDisc, 0, "If every deposit landed in the disc, nothing would be left in the corners to find.");
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
    }
}
