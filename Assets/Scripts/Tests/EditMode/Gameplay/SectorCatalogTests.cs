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

        static SectorCatalog NewCatalog(int seed = Seed)
            => new SectorCatalog(new SectorGrid(MapSize, SectorSize), seed, CoreCenter,
                LowRiskWithin, ModerateRiskWithin, HighRiskWithin);

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
            Assert.GreaterOrEqual(SectorCatalog.NameCombinationCount, new SectorGrid(MapSize, SectorSize).Count);
        }

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
            => new SectorCatalog(new SectorGrid(MapSize, sectorSize), Seed, CoreCenter, low, moderate, high);

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
        /// Hard-coded names and risks, captured before the hash moved into Game.Core and asserted
        /// after. Two purposes, both of which need literals rather than a recomputed expectation:
        /// it proves the move renamed nothing, and it catches a runtime whose arithmetic has changed
        /// underneath a world that is derived rather than saved.
        ///
        /// <b>If this fails, do not update the strings.</b> Every existing world has just been
        /// renamed; find out what moved.
        /// </summary>
        [TestCase(0, "Balise du Levant", SectorRisk.Moderate)]
        [TestCase(1, "Brèche de Schiste", SectorRisk.High)]
        [TestCase(7, "Épave de Sel", SectorRisk.Low)]
        [TestCase(100, "Vestiges des Sondes", SectorRisk.High)]
        [TestCase(180, "Relais de Fer", SectorRisk.Low)]
        [TestCase(360, "Relais de l'Orage", SectorRisk.Moderate)]
        public void SectorIdentitiesAreFrozen(int index, string expectedName, SectorRisk expectedRisk)
        {
            SectorCatalog catalog = NewCatalog();

            Assert.AreEqual(expectedName, catalog.NameOf(index));
            Assert.AreEqual(expectedRisk, catalog.RiskOf(index));
        }

    }
}
