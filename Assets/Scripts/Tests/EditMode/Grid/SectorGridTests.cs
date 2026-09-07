using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Grid
{
    /// <summary>
    /// The sector partition, and what a mission actually reveals.
    ///
    /// The disc is the part worth pinning hardest: revealing the square instead would work, look
    /// fine on one sector, and only show itself much later as a visible grid across the map once
    /// several had been opened.
    /// </summary>
    public class SectorGridTests
    {
        const int MapSize = 300;

        static SectorGrid NewGrid(int mapSize = MapSize) => new SectorGrid(mapSize);

        [Test]
        public void TheCurrentMap_IsTwentyFiveSectorsAcross()
        {
            var grid = NewGrid();

            Assert.AreEqual(12, grid.SectorSizeCells);
            Assert.AreEqual(25, grid.Columns);
            Assert.AreEqual(625, grid.Count, "The directive's count for a 300-cell map.");
        }

        [Test]
        public void EveryCellOfTheMap_BelongsToExactlyOneSector()
        {
            var grid = NewGrid(36);
            var seen = new Dictionary<GridCoord, int>();

            for (int index = 0; index < grid.Count; index++)
            {
                foreach (GridCoord cell in grid.CellsOf(index))
                {
                    if (seen.TryGetValue(cell, out int owner)) Assert.Fail($"{cell} claimed by sectors {owner} and {index}.");
                    seen[cell] = index;
                }
            }

            Assert.AreEqual(36 * 36, seen.Count, "Every cell covered, none twice.");
        }

        [Test]
        public void ACellsSector_IsTheOneWhoseCellsContainIt()
        {
            var grid = NewGrid();

            foreach (GridCoord cell in new[] { new GridCoord(0, 0), new GridCoord(11, 11), new GridCoord(12, 11), new GridCoord(150, 150), new GridCoord(299, 299) })
            {
                int index = grid.IndexAt(cell);
                CollectionAssert.Contains(grid.CellsOf(index).ToList(), cell);
            }
        }

        [Test]
        public void ACellOffTheMap_HasNoSector()
        {
            var grid = NewGrid();

            Assert.AreEqual(-1, grid.IndexAt(new GridCoord(-1, 0)));
            Assert.AreEqual(-1, grid.IndexAt(new GridCoord(0, -1)));
            Assert.AreEqual(-1, grid.IndexAt(new GridCoord(MapSize, 0)));
        }

        [Test]
        public void ASectorsCentre_IsTheMiddleOfItsSquare()
        {
            var grid = NewGrid();

            Assert.AreEqual(new Vector2(6f, 6f), grid.CenterCells(0));
            Assert.AreEqual(new Vector2(18f, 6f), grid.CenterCells(1));
            Assert.AreEqual(new Vector2(6f, 18f), grid.CenterCells(grid.Columns));
        }

        // ---- The inscribed disc ----

        [Test]
        public void RevealingASector_LeavesItsFourCornersHidden()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);

            grid.RevealInscribedDisc(0, discovery);

            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(0, 0)), "Bottom-left corner.");
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(11, 0)), "Bottom-right corner.");
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(0, 11)), "Top-left corner.");
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(11, 11)), "Top-right corner.");
        }

        /// <summary>The disc touches the middle of each side - that is what "inscribed" means here, and it is what makes two revealed neighbours leave a fringe rather than meeting.</summary>
        [Test]
        public void RevealingASector_ReachesTheMiddleOfEachSide()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);

            grid.RevealInscribedDisc(0, discovery);

            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(5, 0)), "Bottom edge, middle.");
            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(5, 11)), "Top edge, middle.");
            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(0, 5)), "Left edge, middle.");
            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(11, 5)), "Right edge, middle.");
        }

        [Test]
        public void RevealingASector_TouchesNoCellOutsideIt()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);
            int sector = grid.IndexAt(new GridCoord(150, 150));

            grid.RevealInscribedDisc(sector, discovery);

            var owned = new HashSet<GridCoord>(grid.CellsOf(sector));
            for (int y = 0; y < MapSize; y++)
            {
                for (int x = 0; x < MapSize; x++)
                {
                    var cell = new GridCoord(x, y);
                    if (discovery.IsDiscovered(cell)) Assert.IsTrue(owned.Contains(cell), $"{cell} is outside sector {sector}.");
                }
            }
        }

        [Test]
        public void TwoRevealedNeighbours_LeaveAnUndiscoveredFringeBetweenThem()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);

            grid.RevealInscribedDisc(0, discovery);
            grid.RevealInscribedDisc(1, discovery);

            // The corners they share, on the seam at x = 11/12.
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(11, 0)));
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(12, 0)));
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(11, 11)));
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(12, 11)));
        }

        [Test]
        public void RevealingTheSameSectorTwice_ChangesNothingTheSecondTime()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);

            int first = grid.RevealInscribedDisc(3, discovery);
            int version = discovery.Version;
            int second = grid.RevealInscribedDisc(3, discovery);

            Assert.Greater(first, 0);
            Assert.AreEqual(0, second);
            Assert.AreEqual(version, discovery.Version, "A repeat must not force the fog texture to re-upload.");
        }

        // ---- Derived discovery ----

        [Test]
        public void AnUntouchedSector_IsUnknown()
        {
            var grid = NewGrid();
            Assert.AreEqual(SectorDiscovery.Unknown, grid.DiscoveryOf(0, new DiscoveryRuntime(MapSize)));
        }

        /// <summary>Partial is where a mission-revealed sector stays: the disc can never cover the corners.</summary>
        [Test]
        public void ASectorOpenedByAMission_StaysPartialForever()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);

            grid.RevealInscribedDisc(0, discovery);

            Assert.AreEqual(SectorDiscovery.Partial, grid.DiscoveryOf(0, discovery));
        }

        [Test]
        public void ASectorWhoseEveryCellIsSeen_IsDiscovered()
        {
            var grid = NewGrid();
            var discovery = new DiscoveryRuntime(MapSize);

            discovery.RevealCells(grid.CellsOf(0));

            Assert.AreEqual(SectorDiscovery.Discovered, grid.DiscoveryOf(0, discovery));
        }

        // ---- Edges and bad arguments ----

        [Test]
        public void AMapThatIsNotAWholeNumberOfSectors_KeepsItsEdge()
        {
            var grid = NewGrid(30); // 2.5 sectors across

            Assert.AreEqual(3, grid.Columns, "Rounded up, so the last strip is not lost.");

            int corner = grid.IndexAt(new GridCoord(29, 29));
            Assert.AreEqual(8, corner, "The far corner cell still lands in a sector.");
            Assert.AreEqual(6 * 6, grid.CellsOf(corner).Count(), "The clipped corner sector holds only its real cells.");
        }

        [Test]
        public void ABadIndexOrANullState_IsIgnoredRatherThanThrowing()
        {
            var grid = NewGrid();

            Assert.DoesNotThrow(() => grid.RevealInscribedDisc(-1, new DiscoveryRuntime(MapSize)));
            Assert.DoesNotThrow(() => grid.RevealInscribedDisc(99999, new DiscoveryRuntime(MapSize)));
            Assert.DoesNotThrow(() => grid.RevealInscribedDisc(0, null));
            Assert.AreEqual(SectorDiscovery.Unknown, grid.DiscoveryOf(0, null));
            CollectionAssert.IsEmpty(grid.CellsOf(-1).ToList());
        }
    }
}
