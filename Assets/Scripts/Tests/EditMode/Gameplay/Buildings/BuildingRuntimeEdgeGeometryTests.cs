using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class BuildingRuntimeEdgeGeometryTests
    {
        static readonly GridCoord Origin = new GridCoord(0, 0);
        static readonly Vector2Int Footprint3x2 = new Vector2Int(3, 2); // width=3 (x), height=2 (y)

        [Test]
        public void ComputeOutputCells_North_ReturnsOneCellPerColumn()
        {
            GridCoord[] cells = Game.Gameplay.Buildings.BuildingRuntime.ComputeOutputCells(Origin, Footprint3x2, Direction.North);

            CollectionAssert.AreEquivalent(
                new[] { new GridCoord(0, 2), new GridCoord(1, 2), new GridCoord(2, 2) },
                cells);
        }

        [Test]
        public void ComputeOutputCells_East_ReturnsOneCellPerRow()
        {
            GridCoord[] cells = Game.Gameplay.Buildings.BuildingRuntime.ComputeOutputCells(Origin, Footprint3x2, Direction.East);

            CollectionAssert.AreEquivalent(
                new[] { new GridCoord(3, 0), new GridCoord(3, 1) },
                cells);
        }

        [Test]
        public void ComputeEdgeCells_CoversAllFourSides_WithCorrectCountPerSide()
        {
            var edges = Game.Gameplay.Buildings.BuildingRuntime.ComputeEdgeCells(Origin, Footprint3x2);

            int northCount = 0, southCount = 0, eastCount = 0, westCount = 0;
            foreach (var (_, side) in edges)
            {
                switch (side)
                {
                    case Direction.North: northCount++; break;
                    case Direction.South: southCount++; break;
                    case Direction.East: eastCount++; break;
                    case Direction.West: westCount++; break;
                }
            }

            Assert.AreEqual(3, northCount); // width-wide sides
            Assert.AreEqual(3, southCount);
            Assert.AreEqual(2, eastCount); // height-wide sides
            Assert.AreEqual(2, westCount);
        }

        [Test]
        public void GetOutputCell_SingleCell_MatchesFirstOfGetOutputCells()
        {
            var runtime = new Game.Gameplay.Buildings.BuildingRuntime(
                ScriptableObject.CreateInstance<DummySquareDefinition>(), Origin, Direction.North);

            Assert.AreEqual(runtime.GetOutputCells()[0], runtime.GetOutputCell());
        }

        class DummySquareDefinition : Game.Data.BuildingDefinition
        {
        }
    }
}
