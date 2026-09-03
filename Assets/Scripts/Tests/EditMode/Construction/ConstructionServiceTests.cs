using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
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

        // Item/Recipe databases are only needed to place a Foundry - none of these tests do, so
        // null is fine here (would throw only if a test actually selected a FoundryDefinition).
        static ConstructionService NewService(GridRuntime grid)
        {
            return new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), new ResearchSystem());
        }

        [Test]
        public void TryPlace_OnEmptyCell_Succeeds()
        {
            var service = NewService(new GridRuntime(1f));
            service.SelectBuilding(NewConveyorDefinition());

            bool result = service.TryPlace(new GridCoord(0, 0), Direction.North, out BuildingRuntime placed);

            Assert.IsTrue(result);
            Assert.IsNotNull(placed);
            Assert.IsInstanceOf<ConveyorRuntime>(placed);
        }

        [Test]
        public void TryPlace_WithoutSelection_Fails()
        {
            var service = NewService(new GridRuntime(1f));

            bool result = service.TryPlace(new GridCoord(0, 0), Direction.North, out BuildingRuntime placed);

            Assert.IsFalse(result);
            Assert.IsNull(placed);
        }

        [Test]
        public void TryPlace_OntoExistingConveyor_OvertakesAndReplaces()
        {
            var grid = new GridRuntime(1f);
            var service = NewService(grid);
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
            var service = NewService(grid);
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
            var service = NewService(grid);
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
            var service = NewService(new GridRuntime(1f));

            bool result = service.TryDemolish(new GridCoord(5, 5), out BuildingRuntime removed);

            Assert.IsFalse(result);
            Assert.IsNull(removed);
        }

        static StorageDefinition NewGatedStorageDefinition(ResearchDefinition unlockResearch)
        {
            var definition = ScriptableObject.CreateInstance<StorageDefinition>();
            var so = new UnityEditor.SerializedObject(definition);
            so.FindProperty("unlockResearch").objectReferenceValue = unlockResearch;
            so.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        [Test]
        public void CanPlace_False_WhenUnlockResearchNotUnlocked()
        {
            var grid = new GridRuntime(1f);
            var research = new ResearchSystem();
            var service = new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), research);
            var definition = NewGatedStorageDefinition(MakeResearch("test_gate", 10f));
            service.SelectBuilding(definition);

            Assert.IsFalse(service.CanPlace(new GridCoord(0, 0)));
        }

        [Test]
        public void CanPlace_True_AfterUnlockResearchIsUnlocked()
        {
            var grid = new GridRuntime(1f);
            var research = new ResearchSystem();
            var service = new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), research);
            var unlockResearch = MakeResearch("test_gate", 10f);
            var definition = NewGatedStorageDefinition(unlockResearch);
            service.SelectBuilding(definition);
            Assert.IsFalse(service.CanPlace(new GridCoord(0, 0)));

            research.AddRp(10f);
            research.Start(unlockResearch);
            research.ReportActiveLab();
            research.Tick(60f);
            Assert.IsTrue(research.IsUnlocked("test_gate"));

            Assert.IsTrue(service.CanPlace(new GridCoord(0, 0)));
        }

        static ResearchDefinition MakeResearch(string id, float cost)
        {
            var research = ScriptableObject.CreateInstance<ResearchDefinition>();
            var so = new UnityEditor.SerializedObject(research);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("cost").floatValue = cost;
            so.ApplyModifiedPropertiesWithoutUndo();
            return research;
        }

        [Test]
        public void SelectBuilding_Cancel_SetPreviewRotation_UpdateState()
        {
            var service = NewService(new GridRuntime(1f));
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
