using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Construction
{
    public class ConstructionServiceTests
    {
        static ConveyorDefinition NewConveyorDefinition()
        {
            return ScriptableObject.CreateInstance<ConveyorDefinition>();
        }

        [Test]
        public void TryPlace_OnEmptyCell_Succeeds()
        {
            var service = new ConstructionService(new GridRuntime(1f));
            service.SelectBuilding(NewConveyorDefinition());

            bool result = service.TryPlace(new GridCoord(0, 0), Direction.North, out BuildingRuntime placed);

            Assert.IsTrue(result);
            Assert.IsNotNull(placed);
            Assert.IsInstanceOf<ConveyorRuntime>(placed);
        }

        [Test]
        public void TryPlace_WithoutSelection_Fails()
        {
            var service = new ConstructionService(new GridRuntime(1f));

            bool result = service.TryPlace(new GridCoord(0, 0), Direction.North, out BuildingRuntime placed);

            Assert.IsFalse(result);
            Assert.IsNull(placed);
        }

        [Test]
        public void TryPlace_OntoExistingConveyor_OvertakesAndReplaces()
        {
            var grid = new GridRuntime(1f);
            var service = new ConstructionService(grid);
            var cell = new GridCoord(2, 2);

            service.SelectBuilding(NewConveyorDefinition());
            service.TryPlace(cell, Direction.North, out BuildingRuntime first);

            service.SelectBuilding(NewConveyorDefinition());
            bool result = service.TryPlace(cell, Direction.East, out BuildingRuntime second);

            Assert.IsTrue(result);
            Assert.AreNotSame(first, second);
            Assert.AreSame(second, grid.GetOccupant(cell));
        }

        [Test]
        public void CanPlace_MatchesTryPlaceOutcome_WithoutMutating()
        {
            var grid = new GridRuntime(1f);
            var service = new ConstructionService(grid);
            var cell = new GridCoord(1, 1);
            service.SelectBuilding(NewConveyorDefinition());

            bool canPlaceBefore = service.CanPlace(cell);
            Assert.IsTrue(canPlaceBefore);
            Assert.IsFalse(grid.IsOccupied(cell));

            service.TryPlace(cell, Direction.North, out _);

            // A second conveyor can still overtake the first.
            Assert.IsTrue(service.CanPlace(cell));
        }

        [Test]
        public void TryDemolish_OnOccupiedCell_Succeeds()
        {
            var grid = new GridRuntime(1f);
            var service = new ConstructionService(grid);
            var cell = new GridCoord(0, 0);
            service.SelectBuilding(NewConveyorDefinition());
            service.TryPlace(cell, Direction.North, out _);

            bool result = service.TryDemolish(cell, out BuildingRuntime removed);

            Assert.IsTrue(result);
            Assert.IsNotNull(removed);
            Assert.IsFalse(grid.IsOccupied(cell));
        }

        [Test]
        public void TryDemolish_OnEmptyCell_Fails()
        {
            var service = new ConstructionService(new GridRuntime(1f));

            bool result = service.TryDemolish(new GridCoord(5, 5), out BuildingRuntime removed);

            Assert.IsFalse(result);
            Assert.IsNull(removed);
        }

        [Test]
        public void SelectBuilding_Cancel_SetPreviewRotation_UpdateState()
        {
            var service = new ConstructionService(new GridRuntime(1f));
            var definition = NewConveyorDefinition();

            service.SelectBuilding(definition);
            Assert.AreSame(definition, service.Selected);
            Assert.AreEqual(Direction.North, service.PreviewRotation);

            service.SetPreviewRotation(Direction.East);
            Assert.AreEqual(Direction.East, service.PreviewRotation);

            service.Cancel();
            Assert.IsNull(service.Selected);
        }
    }
}
