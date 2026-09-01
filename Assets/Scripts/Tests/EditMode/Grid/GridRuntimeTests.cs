using Game.Core;
using Game.Grid;
using NUnit.Framework;

namespace Game.Tests.EditMode.Grid
{
    public class GridRuntimeTests
    {
        [TestCase(1f)]
        [TestCase(2.5f)]
        public void WorldToCell_CellToWorld_RoundTrip(float cellSize)
        {
            var grid = new GridRuntime(cellSize);
            var cell = new GridCoord(3, -2);

            var world = grid.CellToWorld(cell);
            var roundTripped = grid.WorldToCell(world);

            Assert.AreEqual(cell, roundTripped);
        }

        [Test]
        public void Occupancy_TracksSetClearOverwrite()
        {
            var grid = new GridRuntime(1f);
            var cell = new GridCoord(0, 0);
            var occupantA = new object();
            var occupantB = new object();

            Assert.IsFalse(grid.IsOccupied(cell));
            Assert.IsNull(grid.GetOccupant(cell));

            grid.SetOccupant(cell, occupantA);
            Assert.IsTrue(grid.IsOccupied(cell));
            Assert.AreSame(occupantA, grid.GetOccupant(cell));

            grid.SetOccupant(cell, occupantB);
            Assert.AreSame(occupantB, grid.GetOccupant(cell));

            grid.ClearOccupant(cell);
            Assert.IsFalse(grid.IsOccupied(cell));
        }
    }
}
