using System.Collections.Generic;
using Game.Gameplay.Sectors;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay
{
    /// <summary>
    /// The two mission bands.
    ///
    /// A mining mission looks for deposits between the Core's current reach and the exploration
    /// threshold; an exploration mission looks for somewhere a secondary Core could stand, at the
    /// threshold and beyond. They were a single ring of "current radius + 30" until now, which
    /// matched neither: it sat wholly inside what is now the mining band and could never reach an
    /// exploration target at all.
    ///
    /// <b>The threshold is derived and that is what these tests are really about.</b> It is two
    /// maximum Core radii back to back plus the gap wanted between two territories, so the day a Core
    /// reaches further the threshold follows on its own. A test that asserted 250 as a literal would
    /// pass while the derivation was replaced by a constant, which is the failure mode worth guarding.
    ///
    /// Run on a map large enough for the threshold to be inside it - the property could not even be
    /// stated on the 300-cell map these tests used to use.
    /// </summary>
    public class SectorMissionRangeTests
    {
        const int MapSize = 10000;
        const int SectorSize = 16;
        const int ChunkSize = 64;

        /// <summary>The shipped ceiling, CoreRuntime.ExtendedActionRadiusCells. Restated so this fails when that moves rather than following it silently.</summary>
        const float MaxCoreRadius = 32f;

        /// <summary>SectorSettings' own default.</summary>
        const float TerritorySpacing = 90f;

        static readonly Vector2 CoreCenter = new Vector2(5000f, 5000f);

        static SectorGrid NewGrid(int mapSize = MapSize) => new SectorGrid(mapSize, SectorSize);

        static DiscoveryRuntime NewDiscovery(int mapSize = MapSize) => new DiscoveryRuntime(mapSize, ChunkSize);

        static SectorMissionRange NewRange() => new SectorMissionRange(MaxCoreRadius, TerritorySpacing);

        /// <summary>The nearest sector to a given distance from the Core, along the x axis - what a test uses to sit just inside or just outside a band.</summary>
        static int SectorAtDistance(SectorGrid grid, float distanceCells)
        {
            int column = Mathf.RoundToInt((CoreCenter.x + distanceCells) / SectorSize);
            int row = Mathf.RoundToInt(CoreCenter.y / SectorSize);
            return grid.IndexAt(column, row);
        }

        // ---- The threshold is derived, not written down ----

        [Test]
        public void TheThresholdIsTwoMaximumRadiiPlusTheGap()
        {
            Assert.AreEqual(2f * MaxCoreRadius + TerritorySpacing, NewRange().ExplorationMinimumCells, 0.001f);
        }

        /// <summary>
        /// The directive's own worked example: at a maximum radius of 80 and a gap of 90, the
        /// threshold is 250. That is where the number in the document comes from, and reproducing it
        /// from the two inputs is what proves the code derives rather than remembers.
        ///
        /// The shipped maximum radius is 32, not 80, so the shipped threshold is 154. The document's
        /// 250 describes a Core that reaches further than this one yet does.
        /// </summary>
        [Test]
        public void TheDirectivesTwoHundredAndFiftyFallsOutOfTheSameFormula()
        {
            Assert.AreEqual(250f, new SectorMissionRange(80f, 90f).ExplorationMinimumCells, 0.001f);
            Assert.AreEqual(290f, new SectorMissionRange(100f, 90f).ExplorationMinimumCells, 0.001f,
                "the directive's own follow-up: a maximum radius of 100 must give 290 with nothing else to correct");
        }

        // ---- The two bands meet, and do not overlap ----

        /// <summary>
        /// The property that makes the pair a partition rather than two independent settings: every
        /// sector past the Core's reach belongs to exactly one of the two.
        /// </summary>
        [Test]
        public void EverySectorBeyondTheRadiusBelongsToExactlyOneBand()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            SectorMissionRange range = NewRange();

            const float Radius = 22f;

            for (float distance = 30f; distance < 600f; distance += 7f)
            {
                int index = SectorAtDistance(grid, distance);

                SectorEligibility mining = range.EligibilityOf(SectorMissionKind.Mining, grid, discovery, CoreCenter, Radius, index);
                SectorEligibility exploration = range.EligibilityOf(SectorMissionKind.Exploration, grid, discovery, CoreCenter, Radius, index);

                bool inMining = mining == SectorEligibility.Eligible;
                bool inExploration = exploration == SectorEligibility.Eligible;

                Assert.IsTrue(inMining ^ inExploration,
                    $"a sector {distance} cells out is {(inMining && inExploration ? "in both bands" : "in neither band")}");
            }
        }

        [Test]
        public void JustInsideTheThresholdIsMining_JustOutsideIsExploration()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            SectorMissionRange range = NewRange();

            float threshold = range.ExplorationMinimumCells;
            int inside = SectorAtDistance(grid, threshold - 24f);
            int outside = SectorAtDistance(grid, threshold + 24f);

            Assert.AreEqual(SectorEligibility.Eligible,
                range.EligibilityOf(SectorMissionKind.Mining, grid, discovery, CoreCenter, 22f, inside));
            Assert.AreEqual(SectorEligibility.TooClose,
                range.EligibilityOf(SectorMissionKind.Exploration, grid, discovery, CoreCenter, 22f, inside));

            Assert.AreEqual(SectorEligibility.TooFar,
                range.EligibilityOf(SectorMissionKind.Mining, grid, discovery, CoreCenter, 22f, outside));
            Assert.AreEqual(SectorEligibility.Eligible,
                range.EligibilityOf(SectorMissionKind.Exploration, grid, discovery, CoreCenter, 22f, outside));
        }

        // ---- Mining follows the radius; exploration does not ----

        /// <summary>
        /// The directive's named test, in the shape the two bands give it. A mining band computed once
        /// from the starting radius keeps offering ground the Core has since swallowed - so extending
        /// the radius has to close the band from the inside.
        /// </summary>
        [Test]
        public void ExtendingTheRadius_ClosesTheMiningBandFromTheInside()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            SectorMissionRange range = NewRange();

            int near = SectorAtDistance(grid, 40f);

            Assert.AreEqual(SectorEligibility.Eligible,
                range.EligibilityOf(SectorMissionKind.Mining, grid, discovery, CoreCenter, 22f, near),
                "40 cells out is beyond a radius of 22");

            Assert.AreEqual(SectorEligibility.TooClose,
                range.EligibilityOf(SectorMissionKind.Mining, grid, discovery, CoreCenter, 60f, near),
                "and inside a radius of 60, so it is no longer somewhere a mission needs to go");
        }

        [Test]
        public void ExtendingTheRadius_DoesNotMoveTheExplorationBand()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            SectorMissionRange range = NewRange();

            int far = SectorAtDistance(grid, 400f);

            Assert.AreEqual(SectorEligibility.Eligible,
                range.EligibilityOf(SectorMissionKind.Exploration, grid, discovery, CoreCenter, 22f, far));
            Assert.AreEqual(SectorEligibility.Eligible,
                range.EligibilityOf(SectorMissionKind.Exploration, grid, discovery, CoreCenter, MaxCoreRadius, far),
                "exploration is measured from the threshold, which the current radius has no part in");
        }

        [Test]
        public void TheMiningBandRunsFromTheRadiusToTheThreshold()
        {
            NewRange().BandFor(SectorMissionKind.Mining, 22f, out float inner, out float outer);

            Assert.AreEqual(22f, inner, 0.001f);
            Assert.AreEqual(NewRange().ExplorationMinimumCells, outer, 0.001f);
        }

        [Test]
        public void TheExplorationBandHasNoOuterEdge()
        {
            NewRange().BandFor(SectorMissionKind.Exploration, 22f, out float inner, out float outer);

            Assert.AreEqual(NewRange().ExplorationMinimumCells, inner, 0.001f);
            Assert.IsTrue(float.IsPositiveInfinity(outer),
                "bounding exploration by the map would make the band change shape with the map size");
        }

        // ---- Discovery ----

        [Test]
        public void AnAlreadyDiscoveredSector_IsNotADestination()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            SectorMissionRange range = NewRange();

            int index = SectorAtDistance(grid, 100f);
            Assert.AreEqual(SectorEligibility.Eligible,
                range.EligibilityOf(SectorMissionKind.Mining, grid, discovery, CoreCenter, 22f, index));

            grid.RevealInscribedDisc(index, discovery);

            Assert.AreEqual(SectorEligibility.AlreadyKnown,
                range.EligibilityOf(SectorMissionKind.Mining, grid, discovery, CoreCenter, 22f, index),
                "a mission there would reveal a disc that is already revealed");
        }

        [Test]
        public void AnIndexOffTheMap_IsNotASector()
        {
            SectorGrid grid = NewGrid();

            Assert.AreEqual(SectorEligibility.NotASector,
                NewRange().EligibilityOf(SectorMissionKind.Mining, grid, NewDiscovery(), CoreCenter, 22f, -1));
            Assert.AreEqual(SectorEligibility.NotASector,
                NewRange().EligibilityOf(SectorMissionKind.Mining, grid, NewDiscovery(), CoreCenter, 22f, 99999999));
        }

        // ---- Enumeration ----

        [Test]
        public void DestinationsAreAllEligible()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            SectorMissionRange range = NewRange();

            var into = new List<int>();
            range.Destinations(SectorMissionKind.Mining, grid, discovery, CoreCenter, 22f, into);

            Assert.Greater(into.Count, 0);
            foreach (int index in into)
            {
                Assert.AreEqual(SectorEligibility.Eligible,
                    range.EligibilityOf(SectorMissionKind.Mining, grid, discovery, CoreCenter, 22f, index));
            }
        }

        /// <summary>
        /// The exploration band holds most of a 390 625-sector map. Listing it whole would be both a
        /// large allocation and useless to a caller that wants somewhere to go, so the walk stops at
        /// the limit rather than finishing the map.
        /// </summary>
        [Test]
        public void EnumerationStopsAtTheLimit()
        {
            SectorGrid grid = NewGrid();
            var into = new List<int>();

            SectorRangeResult result = NewRange().Destinations(
                SectorMissionKind.Exploration, grid, NewDiscovery(), CoreCenter, 22f, into, limit: 12);

            Assert.AreEqual(12, into.Count);
            Assert.AreEqual(12, result.Count);
            Assert.IsFalse(result.Exhausted);
        }

        /// <summary>
        /// The one state a mission system must never reach silently: nowhere left to go. Set up on a
        /// band narrow enough to empty by hand - a radius just short of the threshold.
        /// </summary>
        [Test]
        public void AnEmptyBandReportsItselfExhausted()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            SectorMissionRange range = NewRange();

            float radius = range.ExplorationMinimumCells - 12f;

            var into = new List<int>();
            range.Destinations(SectorMissionKind.Mining, grid, discovery, CoreCenter, radius, into, limit: 4096);
            Assert.Greater(into.Count, 0, "the fixture needs a band with something in it to start with");

            foreach (int index in into) grid.RevealInscribedDisc(index, discovery);

            SectorRangeResult result = range.Destinations(
                SectorMissionKind.Mining, grid, discovery, CoreCenter, radius, into, limit: 4096);

            Assert.AreEqual(0, result.Count);
            Assert.IsTrue(result.Exhausted);
        }

        [Test]
        public void AnEmptyGridIsExhausted()
        {
            SectorRangeResult result = NewRange().Destinations(
                SectorMissionKind.Mining, null, NewDiscovery(), CoreCenter, 22f, new List<int>());

            Assert.AreEqual(0, result.Count);
            Assert.IsTrue(result.Exhausted);
        }

        [Test]
        public void TheResultReportsTheBandItLookedIn()
        {
            SectorRangeResult result = NewRange().Destinations(
                SectorMissionKind.Mining, NewGrid(), NewDiscovery(), CoreCenter, 22f, new List<int>());

            Assert.AreEqual(22f, result.InnerRadiusCells, 0.001f);
            Assert.AreEqual(NewRange().ExplorationMinimumCells, result.OuterRadiusCells, 0.001f);
        }
    }
}
