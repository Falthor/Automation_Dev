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

        /// <summary>
        /// The placement ghost draws its output arrow from the static form; the building that grows
        /// there draws it from the instance form. They have to name the same cell, or the preview is
        /// describing a different building from the one about to be placed.
        ///
        /// A 2x2 output edge is where it broke: the ghost took cells[0] and the building takes the
        /// middle one, which agree on every odd width and disagree on every even one. Checked on all
        /// four rotations, since the edge changes axis with them.
        /// </summary>
        [TestCase(Direction.North)]
        [TestCase(Direction.East)]
        [TestCase(Direction.South)]
        [TestCase(Direction.West)]
        public void TheGhostsOutputCell_IsTheOneTheBuiltBuildingUses_OnAnEvenWidthEdge(Direction rotation)
        {
            Game.Data.BuildingDefinition definition = NewDefinition(new Vector2Int(2, 2));
            var runtime = new Game.Gameplay.Buildings.BuildingRuntime(definition, Origin, rotation);

            Assert.AreEqual(
                runtime.GetOutputCell(),
                Game.Gameplay.Buildings.BuildingRuntime.ComputeOutputCell(Origin, definition.FootprintSize, rotation));
        }

        /// <summary>The representative cell is the middle of the edge, not its first - the rule the two forms share.</summary>
        [Test]
        public void TheOutputCell_IsTheMiddleOfTheEdge_NotItsFirst()
        {
            Vector2Int footprint = new Vector2Int(2, 2);

            GridCoord[] edge = Game.Gameplay.Buildings.BuildingRuntime.ComputeOutputCells(Origin, footprint, Direction.North);
            GridCoord chosen = Game.Gameplay.Buildings.BuildingRuntime.ComputeOutputCell(Origin, footprint, Direction.North);

            Assert.AreEqual(2, edge.Length, "Precondition: a two-cell edge, where first and middle differ.");
            Assert.AreEqual(edge[1], chosen);
            Assert.AreNotEqual(edge[0], chosen, "cells[0] is what the ghost used to take.");
        }

        static DummySquareDefinition NewDefinition(Vector2Int footprintSize)
        {
            var definition = ScriptableObject.CreateInstance<DummySquareDefinition>();
            var so = new UnityEditor.SerializedObject(definition);
            so.FindProperty("footprintSize").vector2IntValue = footprintSize;
            so.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        class DummySquareDefinition : Game.Data.BuildingDefinition
        {
        }
    }
}
