using Game.Core;
using Game.Tests.EditMode.TestSupport;
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

        /// <summary>
        /// A rectangular building feeds every cell of its output edge, not only the representative
        /// middle one - that whole edge is what transport pushes across, so a belt laid on either
        /// cell of a 2-wide edge is fed by it.
        /// </summary>
        [Test]
        public void ARectangularBuilding_FeedsEveryCellOfItsOutputEdge()
        {
            Game.Data.BuildingDefinition definition = NewDefinition(new Vector2Int(2, 2));
            var runtime = new Game.Gameplay.Buildings.BuildingRuntime(definition, Origin, Direction.North);

            foreach (GridCoord cell in runtime.GetOutputCells()) Assert.IsTrue(runtime.FeedsCell(cell));
            Assert.IsFalse(runtime.FeedsCell(new GridCoord(0, -1)), "The south side takes deliveries, it does not make them.");
        }

        /// <summary>
        /// A Crossroad's two lanes both leave it, and neither of them is the side ExitDirection
        /// names: that falls back to FacingRotation, which on a Crossroad is lane B's <b>entry</b>.
        ///
        /// This is what made a belt started at a Crossroad's exit behave as if it stood on open
        /// ground - nothing was found feeding it, so a drag that turned straight away re-pointed the
        /// anchor instead of cornering it.
        /// </summary>
        [TestCase(Direction.North)]
        [TestCase(Direction.East)]
        [TestCase(Direction.South)]
        [TestCase(Direction.West)]
        public void ACrossroad_FeedsBothOfItsExits_AndNeitherOfItsEntries(Direction rotation)
        {
            var crossroad = new Game.Gameplay.Buildings.CrossroadRuntime(TestDataFactory.NewCrossroad(), Origin, rotation);

            Assert.IsTrue(crossroad.FeedsCell(crossroad.NeighborCell(crossroad.ExitA)), "Lane A leaves here.");
            Assert.IsTrue(crossroad.FeedsCell(crossroad.NeighborCell(crossroad.ExitB)), "Lane B leaves here.");
            Assert.IsFalse(crossroad.FeedsCell(crossroad.NeighborCell(crossroad.EntryA)));
            Assert.IsFalse(crossroad.FeedsCell(crossroad.NeighborCell(crossroad.EntryB)));
        }

        /// <summary>The exit ExitDirection would have named is an entry - stated once so the regression cannot come back through the base implementation.</summary>
        [Test]
        public void ACrossroadsRectangularOutputCell_IsNotOneOfItsExits()
        {
            var crossroad = new Game.Gameplay.Buildings.CrossroadRuntime(TestDataFactory.NewCrossroad(), Origin, Direction.North);

            Assert.AreEqual(crossroad.NeighborCell(crossroad.EntryB), crossroad.GetOutputCell(),
                "Precondition: the generic output cell lands on an entry side.");
            Assert.IsFalse(crossroad.FeedsCell(crossroad.GetOutputCell()));
        }

        /// <summary>A Splitter sends items down every side but its entry, so a belt on any of the three is fed by it.</summary>
        [TestCase(Direction.North)]
        [TestCase(Direction.East)]
        [TestCase(Direction.South)]
        [TestCase(Direction.West)]
        public void ASplitter_FeedsEverySideButItsEntry(Direction rotation)
        {
            var splitter = new Game.Gameplay.Buildings.SplitterRuntime(TestDataFactory.NewSplitter(), Origin, rotation);

            foreach (Direction side in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
            {
                bool isEntry = side == splitter.EntrySide;
                Assert.AreEqual(!isEntry, splitter.FeedsCell(splitter.NeighborCell(side)), $"side {side}");
            }
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
