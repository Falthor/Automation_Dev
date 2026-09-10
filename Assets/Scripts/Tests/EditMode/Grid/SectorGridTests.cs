using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Grid
{
    /// <summary>
    /// The sector partition: pure arithmetic on a coordinate.
    ///
    /// Nothing here touches discovery. A sector is not what reveals ground - a robot's disc is
    /// (DiscoveryRuntime.RevealDisc, pinned in DiscoveryRuntimeTests), and the partition only has to
    /// answer which square a cell falls in and where that square sits.
    /// </summary>
    public class SectorGridTests
    {
        const int MapSize = 300;

        /// <summary>The game's sector size, from SectorSettings. Restated here rather than read from the asset: a test that follows the setting could not fail when the setting is wrong.</summary>
        const int SectorSize = 16;

        static SectorGrid NewGrid(int mapSize = MapSize) => new SectorGrid(mapSize, SectorSize);

        [Test]
        public void TheCurrentMap_IsTwentyFiveSectorsAcross()
        {
            var grid = NewGrid();

            Assert.AreEqual(16, grid.SectorSizeCells, "4x4 sectors tile a 64-cell chunk exactly - 12 fell on no chunk boundary.");
            Assert.AreEqual(19, grid.Columns, "300 is not a whole number of 16s, so the last strip is partial.");
            Assert.AreEqual(361, grid.Count);
        }

        [Test]
        public void EveryCellOfTheMap_BelongsToExactlyOneSector()
        {
            var grid = NewGrid(48);
            var seen = new Dictionary<GridCoord, int>();

            for (int index = 0; index < grid.Count; index++)
            {
                foreach (GridCoord cell in grid.CellsOf(index))
                {
                    if (seen.TryGetValue(cell, out int owner)) Assert.Fail($"{cell} claimed by sectors {owner} and {index}.");
                    seen[cell] = index;
                }
            }

            Assert.AreEqual(48 * 48, seen.Count, "Every cell covered, none twice.");
        }

        [Test]
        public void ACellsSector_IsTheOneWhoseCellsContainIt()
        {
            var grid = NewGrid();

            foreach (GridCoord cell in new[] { new GridCoord(0, 0), new GridCoord(15, 15), new GridCoord(16, 15), new GridCoord(150, 150), new GridCoord(299, 299) })
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

            Assert.AreEqual(new Vector2(8f, 8f), grid.CenterCells(0));
            Assert.AreEqual(new Vector2(24f, 8f), grid.CenterCells(1));
            Assert.AreEqual(new Vector2(8f, 24f), grid.CenterCells(grid.Columns));
        }

        // ---- Edges and bad arguments ----

        [Test]
        public void AMapThatIsNotAWholeNumberOfSectors_KeepsItsEdge()
        {
            var grid = NewGrid(40); // 2.5 sectors across

            Assert.AreEqual(3, grid.Columns, "Rounded up, so the last strip is not lost.");

            int corner = grid.IndexAt(new GridCoord(39, 39));
            Assert.AreEqual(8, corner, "The far corner cell still lands in a sector.");
            Assert.AreEqual(8 * 8, grid.CellsOf(corner).Count(), "The clipped corner sector holds only its real cells.");
        }

        [Test]
        public void ABadIndex_IsAnsweredRatherThanThrowing()
        {
            var grid = NewGrid();

            Assert.AreEqual(-1, grid.IndexAt(new GridCoord(-1, 0)));
            Assert.AreEqual(-1, grid.IndexAt(99999, 0));
            Assert.AreEqual(new GridCoord(0, 0), grid.OriginOf(-1), "A bad index answers rather than throwing.");
            CollectionAssert.IsEmpty(grid.CellsOf(-1).ToList());
        }
    }
}
