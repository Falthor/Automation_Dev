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

        const int SectorSize = 16;
        const int ChunkSize = 64;

        // The shipped thresholds, in cells - SectorSettings' own defaults, restated so a test can
        // fail when the asset is wrong rather than following it.
        const float LowRiskWithin = 40f;
        const float ModerateRiskWithin = 250f;
        const float HighRiskWithin = 330f;

        /// <summary>Wider than the fixture map, so the whole of it falls in one region - which is what a 300-cell map gets under the shipped settings too.</summary>
        const int PreferredRegionSize = 384;

        static SectorCatalog NewCatalog(int seed = Seed)
            => new SectorCatalog(new SectorGrid(MapSize, SectorSize), seed, CoreCenter,
                LowRiskWithin, ModerateRiskWithin, HighRiskWithin, PreferredRegionSize);

        // ---- Determinism ----

        [Test]
        public void TheSameSeedAndSector_GiveTheSameNameAndRisk()
        {
            var first = NewCatalog();
            var second = NewCatalog();

            for (int index = 0; index < 361; index += 37)
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

            for (int index = 0; index < 361; index += 53)
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

        /// <summary>
        /// Over a fixture grid, not the shipped map: this asserts that the index-to-name mapping is
        /// injective, which is a property of the arithmetic. Whether the vocabulary is large enough
        /// for the map the game ships with is a different question, and it is asked in
        /// SectorSettingsTests against the real assets.
        /// </summary>
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

        // The guard for "are there enough names for the map the game actually ships" used to live
        // here, and it read this file's own MapSize constant. It therefore measured a fixture and
        // stayed green while the shipped map grew past the vocabulary. It has moved to
        // SectorSettingsTests, which reads the real assets - see DEVELOPMENT_RULES.md §7.

        [Test]
        public void ANameIsNeverEmptyOrABareNumber()
        {
            var catalog = NewCatalog();

            for (int index = 0; index < 361; index += 11)
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

        /// <summary>
        /// On the current 300-cell map the farthest a sector centre can be from the middle is about
        /// 212 cells, so the High and Critical bands are simply out of reach - the shipped thresholds
        /// are calibrated for the 10 000-cell map this is heading towards. Low against Moderate is
        /// what can be asserted here, and it is enough to pin the gradient's direction.
        /// </summary>
        [Test]
        public void RiskGrowsWithDistanceFromTheCore()
        {
            var catalog = NewCatalog();

            float near = AverageRiskAtAbout(catalog, 20f);
            float far = AverageRiskAtAbout(catalog, 200f);

            Assert.Less(near, far, "A far sector should read as a bigger bet than a near one.");
        }

        /// <summary>
        /// The property the ring-counting version could not have, and the regression that would have
        /// gone unnoticed: thresholds are in cells, so cutting the map differently does not move the
        /// danger. Counting rings of sectors stretched the whole gradient by a third when the sector
        /// size went from 12 to 16, silently and with nothing able to see it.
        ///
        /// Averaged, because the per-sector jitter is keyed on the index and the two grids number
        /// their sectors differently - the gradient has to match, not each individual draw.
        /// </summary>
        [Test]
        public void TheSameDistanceGivesTheSameRisk_WhateverTheSectorSize()
        {
            var coarse = NewCatalogWith(SectorSize, LowRiskWithin, ModerateRiskWithin, HighRiskWithin);
            var fine = NewCatalogWith(8, LowRiskWithin, ModerateRiskWithin, HighRiskWithin);

            foreach (float distance in new[] { 20f, 100f, 200f })
            {
                Assert.AreEqual(AverageRiskAtAbout(coarse, distance), AverageRiskAtAbout(fine, distance), 0.2f,
                    $"at {distance} cells");
            }
        }

        /// <summary>They are balance settings, not values derived from something else - so moving them has to move the risk, and nothing else has to be corrected alongside.</summary>
        [Test]
        public void TheThresholdsAreSettings_AndMovingThemMovesTheRisk()
        {
            var tight = NewCatalogWith(SectorSize, 10f, 20f, 30f);
            var generous = NewCatalogWith(SectorSize, 200f, 250f, 300f);

            Assert.Greater(AverageRiskAtAbout(tight, 100f), AverageRiskAtAbout(generous, 100f) + 1f,
                "At the same distance, much tighter thresholds have to read as clearly more dangerous.");
        }

        static SectorCatalog NewCatalogWith(int sectorSize, float low, float moderate, float high)
            => new SectorCatalog(new SectorGrid(MapSize, sectorSize), Seed, CoreCenter, low, moderate, high, PreferredRegionSize);

        /// <summary>
        /// The real risk, averaged over every sector roughly that far from the Core. Averaged on
        /// purpose: RiskOf nudges individual sectors off the gradient by one band, so a single sector
        /// says nothing about where the band boundaries are.
        /// </summary>
        static float AverageRiskAtAbout(SectorCatalog catalog, float distanceCells)
        {
            SectorGrid grid = catalog.Grid;
            int total = 0;
            int count = 0;

            for (int index = 0; index < grid.Count; index++)
            {
                float d = Vector2.Distance(grid.CenterCells(index), CoreCenter);
                if (Mathf.Abs(d - distanceCells) > 20f) continue;

                total += (int)catalog.RiskOf(index);
                count++;
            }

            // A handful of sectors is not an average, it is noise: the jitter moves a quarter of them
            // by a whole band, so three samples can read 0.67 where the true value is 0.25.
            Assert.GreaterOrEqual(count, 10,
                $"only {count} sectors sit about {distanceCells} cells from the Core - too few to average.");

            return total / (float)count;
        }

        // ---- Contents ----

        [Test]
        public void TheFeatureSitsAtTheCentre_SoAMissionAlwaysShowsIt()
        {
            var catalog = NewCatalog();

            for (int index = 0; index < 361; index += 29)
            {
                SectorContents contents = catalog.ContentsOf(index);
                if (contents.Feature == SectorFeature.None) continue;

                var fresh = new DiscoveryRuntime(MapSize, ChunkSize);
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
                var disc = new DiscoveryRuntime(MapSize, ChunkSize);
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

        // ---- Frozen identities ----

        /// <summary>
        /// Hard-coded names and risks. Two purposes, both of which need literals rather than a
        /// recomputed expectation: they prove a refactor renamed nothing, and they catch a runtime
        /// whose arithmetic has changed underneath a world that is derived rather than saved.
        ///
        /// <b>If this fails, do not update the strings.</b> Every existing world has just been
        /// renamed; find out what moved.
        ///
        /// <b>The names were rewritten once, deliberately</b>, when one name per sector was abandoned:
        /// 390 625 sectors could not have 768 distinct names, so a sector is now a region name plus
        /// its position in that region. That was a change of scheme, decided and recorded, not a
        /// drifting hash - which is the only kind of reason that justifies touching these literals.
        /// The <b>risks</b> were not touched: that arithmetic did not move, and it did not have to be
        /// taken on trust, because these same cases still assert the values they always did.
        /// </summary>
        [TestCase(0, "Balise du Levant A1", SectorRisk.Moderate)]
        [TestCase(1, "Balise du Levant B1", SectorRisk.High)]
        [TestCase(7, "Balise du Levant H1", SectorRisk.Low)]
        [TestCase(100, "Balise du Levant F6", SectorRisk.High)]
        [TestCase(180, "Balise du Levant J10", SectorRisk.Low)]
        [TestCase(360, "Balise du Levant S19", SectorRisk.Moderate)]
        public void SectorIdentitiesAreFrozen(int index, string expectedName, SectorRisk expectedRisk)
        {
            SectorCatalog catalog = NewCatalog();

            Assert.AreEqual(expectedName, catalog.NameOf(index));
            Assert.AreEqual(expectedRisk, catalog.RiskOf(index));
        }

        /// <summary>
        /// The point of the whole rework, stated as the thing that was broken: designating a mission
        /// destination is impossible when neighbours share a name. On the shipped map, where the old
        /// scheme collided on four sectors in five.
        /// </summary>
        [Test]
        public void OnTheShippedMap_NeighbouringSectorsHaveDifferentNames()
        {
            var grid = new SectorGrid(10000, SectorSize);
            var catalog = new SectorCatalog(grid, Seed, CoreCenter,
                LowRiskWithin, ModerateRiskWithin, HighRiskWithin, PreferredRegionSize);

            // The first sector column of the second region - rounded up, not down: a region of 371
            // cells ends inside sector column 23, whose origin at 368 is still region 0, so the
            // second region starts at column 24. Taking the floor would have put this whole block
            // safely inside one region while claiming to straddle a seam.
            int seam = Mathf.CeilToInt(catalog.RegionSizeCells / (float)grid.SectorSizeCells);

            Assert.AreNotEqual(
                catalog.NameOf(grid.IndexAt(seam - 1, seam)).Split(' ')[0],
                catalog.NameOf(grid.IndexAt(seam, seam)).Split(' ')[0],
                "the fixture is supposed to straddle a region boundary, and these two columns are in the same region");

            var seen = new Dictionary<string, int>();

            for (int row = seam - 4; row <= seam + 4; row++)
            {
                for (int column = seam - 4; column <= seam + 4; column++)
                {
                    int index = grid.IndexAt(column, row);
                    Assert.GreaterOrEqual(index, 0);

                    string name = catalog.NameOf(index);
                    if (seen.TryGetValue(name, out int owner))
                        Assert.Fail($"\"{name}\" is worn by both sector {owner} and sector {index}, which are neighbours.");

                    seen[name] = index;
                }
            }
        }

        /// <summary>Neighbours share their region name - that is the point of a region, and it is what makes the map readable rather than fifty unrelated nouns.</summary>
        [Test]
        public void NeighboursShareTheirRegionName()
        {
            var grid = new SectorGrid(10000, SectorSize);
            var catalog = new SectorCatalog(grid, Seed, CoreCenter,
                LowRiskWithin, ModerateRiskWithin, HighRiskWithin, PreferredRegionSize);

            int centre = grid.IndexAt(grid.Columns / 2, grid.Columns / 2);

            string here = catalog.NameOf(centre);
            string next = catalog.NameOf(centre + 1);

            string regionHere = here.Substring(0, here.LastIndexOf(' '));
            string regionNext = next.Substring(0, next.LastIndexOf(' '));

            Assert.AreEqual(regionHere, regionNext, "two adjacent sectors in mid-region should be in the same region");
            Assert.AreNotEqual(here, next, "and still be told apart by their suffix");
        }

        [Test]
        public void ColumnLettersRunPastZ()
        {
            Assert.AreEqual("A", SectorCatalog.ColumnLetters(0));
            Assert.AreEqual("Z", SectorCatalog.ColumnLetters(25));
            Assert.AreEqual("AA", SectorCatalog.ColumnLetters(26));
            Assert.AreEqual("AB", SectorCatalog.ColumnLetters(27));
            Assert.AreEqual("BA", SectorCatalog.ColumnLetters(52));
        }

    }
}
