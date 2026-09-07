using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Gameplay.Sectors;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay
{
    /// <summary>
    /// The mission ring.
    ///
    /// The directive names one test explicitly and it is the first below: extending the Core's
    /// radius must move the ring. That is the guarantee that would rot silently - a ring computed
    /// once from the starting radius works perfectly until the first radius research, and then
    /// quietly keeps offering the same destinations.
    /// </summary>
    public class SectorMissionRangeTests
    {
        const int MapSize = 300;

        static readonly Vector2 CoreCenter = new Vector2(150f, 150f);

        static SectorGrid NewGrid() => new SectorGrid(MapSize);

        static List<int> Destinations(SectorMissionRange range, SectorGrid grid, DiscoveryRuntime discovery, float radius)
        {
            var into = new List<int>();
            range.Destinations(grid, discovery, CoreCenter, radius, into);
            return into;
        }

        // ---- The ring follows the radius ----

        [Test]
        public void ExtendingTheRadius_MovesTheRingOutward()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);
            var range = new SectorMissionRange();

            List<int> near = Destinations(range, grid, discovery, 22f);
            List<int> far = Destinations(range, grid, discovery, 60f);

            CollectionAssert.IsNotEmpty(near);
            CollectionAssert.IsNotEmpty(far);

            float nearest = far.Min(index => Vector2.Distance(grid.CenterCells(index), CoreCenter));
            Assert.Greater(nearest, 22f, "Nothing inside the old radius may remain a destination.");

            CollectionAssert.AreNotEquivalent(near, far, "The ring has to be somewhere else, not merely larger.");
        }

        [Test]
        public void NothingInsideTheRadius_IsADestination()
        {
            var grid = NewGrid();
            var range = new SectorMissionRange();

            const float radius = 40f;
            foreach (int index in Destinations(range, grid, new DiscoveryRuntime(MapSize), radius))
            {
                Assert.Greater(Vector2.Distance(grid.CenterCells(index), CoreCenter), radius, $"sector {index} is inside the radius");
            }
        }

        [Test]
        public void NothingBeyondTheRange_IsADestination()
        {
            var grid = NewGrid();
            var range = new SectorMissionRange();

            const float radius = 22f;
            foreach (int index in Destinations(range, grid, new DiscoveryRuntime(MapSize), radius))
            {
                Assert.LessOrEqual(
                    Vector2.Distance(grid.CenterCells(index), CoreCenter),
                    radius + SectorMissionRange.DefaultRangeBeyondRadiusCells + 0.001f,
                    $"sector {index} is past the ring");
            }
        }

        /// <summary>The 30 cells are a setting, not a constant compiled into a comparison.</summary>
        [Test]
        public void TheRangeIsASetting_AndWideningItAddsDestinations()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);

            var tight = new SectorMissionRange { RangeBeyondRadiusCells = 15f };
            var loose = new SectorMissionRange { RangeBeyondRadiusCells = 45f };

            Assert.Less(Destinations(tight, grid, discovery, 22f).Count, Destinations(loose, grid, discovery, 22f).Count);
        }

        /// <summary>Membership is decided on the centre, so a sector straddling the boundary is in or out by one clear rule.</summary>
        [Test]
        public void MembershipIsDecidedOnTheCentre_NotOnTheCells()
        {
            var grid = NewGrid();
            var range = new SectorMissionRange { RangeBeyondRadiusCells = 30f };
            const float radius = 22f;

            List<int> destinations = Destinations(range, grid, new DiscoveryRuntime(MapSize), radius);

            foreach (int index in destinations)
            {
                float distance = Vector2.Distance(grid.CenterCells(index), CoreCenter);
                Assert.IsTrue(distance > radius && distance <= radius + 30f);
            }

            // A sector whose cells straddle the inner boundary but whose centre is inside must be out.
            int straddling = grid.IndexAt(new GridCoord(150, 168));
            float centreDistance = Vector2.Distance(grid.CenterCells(straddling), CoreCenter);
            if (centreDistance <= radius) CollectionAssert.DoesNotContain(destinations, straddling);
        }

        // ---- Discovered sectors drop out, and the ring can empty ----

        [Test]
        public void ASectorAlreadyDiscovered_IsNoLongerADestination()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);
            var range = new SectorMissionRange();

            List<int> before = Destinations(range, grid, discovery, 22f);
            int target = before[0];

            grid.RevealInscribedDisc(target, discovery);

            CollectionAssert.DoesNotContain(Destinations(range, grid, discovery, 22f), target);
        }

        [Test]
        public void WhenTheRingEmpties_ItWidensRatherThanGoingSilent()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);
            var range = new SectorMissionRange();

            // Empty the ring the player can currently reach.
            foreach (int index in Destinations(range, grid, discovery, 22f)) grid.RevealInscribedDisc(index, discovery);

            var into = new List<int>();
            SectorRangeResult result = range.Destinations(grid, discovery, CoreCenter, 22f, into);

            Assert.IsTrue(result.Widened, "The ring had to grow.");
            Assert.IsFalse(result.Exhausted);
            Assert.GreaterOrEqual(result.Count, range.MinimumDestinations, "There must still be somewhere to go.");
            Assert.Greater(result.OuterRadiusCells, 22f + SectorMissionRange.DefaultRangeBeyondRadiusCells);
            CollectionAssert.IsNotEmpty(into);
        }

        [Test]
        public void AnUntouchedRing_IsNotReportedAsWidened()
        {
            var grid = NewGrid();
            var into = new List<int>();

            SectorRangeResult result = new SectorMissionRange().Destinations(grid, new DiscoveryRuntime(MapSize), CoreCenter, 22f, into);

            Assert.IsFalse(result.Widened);
            Assert.IsFalse(result.Exhausted);
            Assert.AreEqual(into.Count, result.Count);
        }

        /// <summary>A fully explored map has to say so, rather than returning an empty list that reads the same as a bug.</summary>
        [Test]
        public void AFullyDiscoveredMap_ReportsItselfExhausted()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);
            for (int index = 0; index < grid.Count; index++) discovery.RevealCells(grid.CellsOf(index));

            SectorRangeResult result = new SectorMissionRange().Destinations(grid, discovery, CoreCenter, 22f, new List<int>());

            Assert.AreEqual(0, result.Count);
            Assert.IsTrue(result.Exhausted);
        }

        [Test]
        public void ANullGridOrList_IsIgnoredRatherThanThrowing()
        {
            var range = new SectorMissionRange();

            Assert.DoesNotThrow(() => range.Destinations(null, null, CoreCenter, 22f, null));
            Assert.DoesNotThrow(() => range.Destinations(NewGrid(), null, CoreCenter, 22f, null));
            Assert.IsTrue(range.Destinations(null, null, CoreCenter, 22f, null).Exhausted);
        }
    }
}
