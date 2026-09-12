using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Presentation;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// What the placement ghost promises about a Splitter or a Crossroad, checked against what the
    /// built piece actually does.
    ///
    /// <b>The check is deliberately not against the derivation.</b> Asking whether the ghost's sides
    /// match <c>EntryA</c>/<c>EntrySide</c> would only confirm that two callers of one function agree,
    /// which the compiler already guarantees. So each previewed arrow is put to the piece itself
    /// through the two methods transport routes by: an entry must be a side the piece
    /// <see cref="BuildingRuntime.CanAcceptInput"/> from, an exit a cell it
    /// <see cref="BuildingRuntime.FeedsCell"/> - and neither may be the other.
    ///
    /// A preview that lies is not a cosmetic defect: it is the only thing on screen saying which way
    /// a piece will route before it exists.
    /// </summary>
    public class CrossPieceConnectionsTests
    {
        static readonly Direction[] AllFacings = { Direction.North, Direction.East, Direction.South, Direction.West };

        static readonly GridCoord SomeCell = new GridCoord(4, 4);

        [Test]
        public void ASplitterIsPreviewedWithTheSidesItWillBeBuiltWith()
        {
            SplitterDefinition definition = TestDataFactory.NewSplitter();
            var described = new List<(Direction side, bool inward)>();

            foreach (Direction facing in AllFacings)
            {
                Assert.IsTrue(CrossPieceConnections.Describe(definition, facing, described), "a Splitter is a cross piece");
                Assert.AreEqual(4, described.Count, $"one entry and three exits, facing {facing}");

                AssertEachArrowMatchesThePiece(new SplitterRuntime(definition, SomeCell, facing), described, facing);
            }
        }

        [Test]
        public void ACrossroadIsPreviewedWithTheSidesItWillBeBuiltWith()
        {
            CrossroadDefinition definition = TestDataFactory.NewCrossroad();
            var described = new List<(Direction side, bool inward)>();

            foreach (Direction facing in AllFacings)
            {
                Assert.IsTrue(CrossPieceConnections.Describe(definition, facing, described), "a Crossroad is a cross piece");
                Assert.AreEqual(4, described.Count, $"two entries and two exits, facing {facing}");

                AssertEachArrowMatchesThePiece(new CrossroadRuntime(definition, SomeCell, facing), described, facing);
            }
        }

        /// <summary>
        /// Every side is drawn once and only once. A piece drawing two arrows on one side would be
        /// drawing them on top of each other, and the one underneath would never be seen.
        /// </summary>
        [Test]
        public void NoSideIsPreviewedTwice()
        {
            var described = new List<(Direction side, bool inward)>();

            foreach (BuildingDefinition definition in new BuildingDefinition[] { TestDataFactory.NewSplitter(), TestDataFactory.NewCrossroad() })
            {
                foreach (Direction facing in AllFacings)
                {
                    CrossPieceConnections.Describe(definition, facing, described);

                    var sides = new List<Direction>();
                    foreach ((Direction side, bool _) in described) sides.Add(side);

                    CollectionAssert.AllItemsAreUnique(sides, $"{definition.GetType().Name} facing {facing}");
                }
            }
        }

        /// <summary>
        /// Anything else is not a cross piece and must say so rather than returning an empty
        /// description - the caller tells the two families apart by the answer, and an empty list
        /// would read as "a cross piece with no connections at all".
        /// </summary>
        [Test]
        public void AnOrdinaryBuildingIsNotDescribedAtAll()
        {
            var described = new List<(Direction side, bool inward)>();

            Assert.IsFalse(CrossPieceConnections.Describe(TestDataFactory.NewStorage(), Direction.North, described));
            Assert.IsEmpty(described);
        }

        static void AssertEachArrowMatchesThePiece(BuildingRuntime built, List<(Direction side, bool inward)> described, Direction facing)
        {
            foreach ((Direction side, bool inward) in described)
            {
                GridCoord neighbour = SomeCell + side.ToOffset();

                if (inward)
                {
                    Assert.IsTrue(built.CanAcceptInput("iron", 1, side),
                        $"the ghost draws an entry on {side} at facing {facing}, and the built piece refuses deliveries from there");
                    Assert.IsFalse(built.FeedsCell(neighbour),
                        $"the ghost draws an entry on {side} at facing {facing}, and the built piece pushes items out of it");
                }
                else
                {
                    Assert.IsTrue(built.FeedsCell(neighbour),
                        $"the ghost draws an exit on {side} at facing {facing}, and the built piece never sends anything there");
                    Assert.IsFalse(built.CanAcceptInput("iron", 1, side),
                        $"the ghost draws an exit on {side} at facing {facing}, and the built piece also takes deliveries from it");
                }
            }
        }
    }
}
