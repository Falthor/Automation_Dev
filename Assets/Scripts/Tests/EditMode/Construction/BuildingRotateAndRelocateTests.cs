using System.Collections.Generic;
using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Notifications;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Sites;
using Game.Gameplay.Transport;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Construction
{
    /// <summary>
    /// Turning and moving a building the player already owns.
    ///
    /// Both used to mean demolish-and-rebuild, which charged the bill a second time, dropped a
    /// working building back to a silhouette until a robot came round, and - for a chest - lost
    /// everything inside it. The two properties pinned hardest below are the ones that would rot
    /// silently: a moved chest is the <b>same object</b> at a new address rather than a copy, and a
    /// building is never an obstacle to itself.
    /// </summary>
    public class BuildingRotateAndRelocateTests
    {
        GridRuntime _grid;
        ConstructionService _construction;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridRuntime(1f);

            var transport = new TransportSystem(_grid);
            var sites = new ConstructionSiteSystem(transport, _grid, new NotificationSystem(), Vector2.zero);
            _construction = new ConstructionService(_grid, null, null, new ComputeSystem(), new PowerSystem(),
                new ResearchSystem(new ComputeSystem()), transport, null, sites);
        }

        // ---- Rotation ----

        [Test]
        public void RotatingABuilding_TurnsItAQuarterTurnClockwise()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);

            Assert.IsTrue(_construction.TryRotateInPlace(box.Cell, out BuildingRuntime rotated));
            Assert.AreSame(box, rotated);
            Assert.AreEqual(Direction.East, box.FacingRotation);
        }

        [Test]
        public void RotatingFourTimes_ComesBackToWhereItStarted()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);

            for (int i = 0; i < 4; i++) _construction.TryRotateInPlace(box.Cell, out _);

            Assert.AreEqual(Direction.North, box.FacingRotation);
        }

        /// <summary>Turning a building has to move where it hands out, or it is only a sprite change.</summary>
        [Test]
        public void RotatingABuilding_MovesTheCellItHandsOutTo()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);
            Vector2Int size = box.Definition.FootprintSize;

            GridCoord before = BuildingRuntime.ComputeOutputCell(box.Cell, size, box.FacingRotation);
            _construction.TryRotateInPlace(box.Cell, out _);
            GridCoord after = BuildingRuntime.ComputeOutputCell(box.Cell, size, box.FacingRotation);

            Assert.AreNotEqual(before, after);
        }

        [Test]
        public void RotatingABuilding_OccupiesExactlyTheSameCells()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);
            List<GridCoord> before = OccupiedCells(box);

            _construction.TryRotateInPlace(box.Cell, out _);

            CollectionAssert.AreEquivalent(before, OccupiedCells(box), "Every footprint is square, so nothing may move.");
        }

        /// <summary>The guard that makes "every footprint is square" safe to rely on, rather than an assumption nobody restated.</summary>
        [Test]
        public void ANonSquareFootprint_RefusesToRotate()
        {
            StorageDefinition definition = NewStorageWithFootprint(new Vector2Int(2, 3));
            var building = new StorageRuntime(definition, new GridCoord(5, 5), Direction.North);
            _grid.SetOccupantFootprint(building.Cell, definition.FootprintCells, building);

            Assert.IsFalse(building.CanRotateInPlace);
            Assert.IsFalse(_construction.TryRotateInPlace(building.Cell, out _));
            Assert.AreEqual(Direction.North, building.FacingRotation);
        }

        [Test]
        public void AnEmptyCell_CannotBeRotated()
        {
            Assert.IsFalse(_construction.TryRotateInPlace(new GridCoord(3, 3), out _));
        }

        [Test]
        public void RotatingCostsNothing_AndOpensNoSite()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);
            int slotsBefore = _construction.OccupiedBuildingSlots;

            _construction.TryRotateInPlace(box.Cell, out _);

            Assert.AreEqual(slotsBefore, _construction.OccupiedBuildingSlots);
            Assert.IsNull(_construction.Selected, "Rotating is not a placement gesture.");
        }

        // ---- Relocation ----

        [Test]
        public void MovingAChest_TakesItsContentsWithIt()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);
            box.SeedInitialContents("iron_ingot", 17);

            _construction.BeginRelocation(box);
            Assert.IsTrue(_construction.TryRelocate(new GridCoord(20, 20), out BuildingRuntime moved));

            Assert.AreSame(box, moved, "The same object moves - that is what makes the contents follow.");
            Assert.AreEqual(17, box.GetInputAmount("iron_ingot"));
        }

        [Test]
        public void MovingABuilding_FreesTheGroundItLeaves()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);

            _construction.BeginRelocation(box);
            _construction.TryRelocate(new GridCoord(20, 20), out _);

            Assert.IsNull(_grid.GetOccupant(new GridCoord(10, 10)), "The old cell must be free.");
            Assert.AreSame(box, _grid.GetOccupant(new GridCoord(20, 20)));
            Assert.AreEqual(new GridCoord(20, 20), box.Cell);
        }

        /// <summary>Without this, "one cell over" is refused because the box counts itself as an obstacle.</summary>
        [Test]
        public void ABuildingIsNotAnObstacleToItself()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);

            _construction.BeginRelocation(box);

            Assert.IsTrue(_construction.CanPlace(new GridCoord(10, 10)));
            Assert.IsTrue(_construction.TryRelocate(new GridCoord(10, 10), out _));
        }

        [Test]
        public void MovingOntoAnotherBuilding_IsRefused()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);
            PlaceBox(new GridCoord(20, 20), Direction.North);

            _construction.BeginRelocation(box);

            Assert.IsFalse(_construction.TryRelocate(new GridCoord(20, 20), out _));
            Assert.AreEqual(new GridCoord(10, 10), box.Cell, "A refused move must leave it where it was.");
        }

        [Test]
        public void ARefusedMove_KeepsTheGestureAlive()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);
            PlaceBox(new GridCoord(20, 20), Direction.North);

            _construction.BeginRelocation(box);
            _construction.TryRelocate(new GridCoord(20, 20), out _);

            Assert.AreSame(box, _construction.RelocationTarget, "A bad click must not cancel the move.");
        }

        [Test]
        public void AFinishedMove_EndsTheGesture()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);

            _construction.BeginRelocation(box);
            _construction.TryRelocate(new GridCoord(20, 20), out _);

            Assert.IsNull(_construction.RelocationTarget);
            Assert.IsNull(_construction.Selected, "And the ghost stops following the mouse.");
        }

        [Test]
        public void SelectingABuildingToPlace_CancelsAMoveInProgress()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);

            _construction.BeginRelocation(box);
            _construction.SelectBuilding(box.Definition);

            Assert.IsNull(_construction.RelocationTarget);
        }

        [Test]
        public void AMoveStartsFromTheBuildingsCurrentRotation()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.East);

            _construction.BeginRelocation(box);

            Assert.AreEqual(Direction.East, _construction.PreviewRotation, "The ghost must not snap back to North.");
            Assert.AreSame(box.Definition, _construction.Selected);
        }

        [Test]
        public void AMoveTakesTheRotationBeingPreviewed()
        {
            StorageRuntime box = PlaceBox(new GridCoord(10, 10), Direction.North);

            _construction.BeginRelocation(box);
            _construction.SetPreviewRotation(Direction.South);
            _construction.TryRelocate(new GridCoord(20, 20), out _);

            Assert.AreEqual(Direction.South, box.FacingRotation);
        }

        [Test]
        public void MovingWithNothingSelected_DoesNothing()
        {
            Assert.IsFalse(_construction.TryRelocate(new GridCoord(5, 5), out BuildingRuntime moved));
            Assert.IsNull(moved);
        }

        // ---- Helpers ----

        StorageRuntime PlaceBox(GridCoord cell, Direction rotation)
        {
            var box = new StorageRuntime(TestDataFactory.NewStorage(), cell, rotation);
            _grid.SetOccupantFootprint(cell, box.Definition.FootprintCells, box);
            return box;
        }

        static StorageDefinition NewStorageWithFootprint(Vector2Int footprint)
        {
            StorageDefinition definition = TestDataFactory.NewStorage();
            var so = new SerializedObject(definition);
            so.FindProperty("footprintSize").vector2IntValue = footprint;
            so.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        List<GridCoord> OccupiedCells(BuildingRuntime building)
        {
            var cells = new List<GridCoord>();
            for (int x = 0; x < 32; x++)
            {
                for (int y = 0; y < 32; y++)
                {
                    var cell = new GridCoord(x, y);
                    if (ReferenceEquals(_grid.GetOccupant(cell), building)) cells.Add(cell);
                }
            }
            return cells;
        }
    }
}
