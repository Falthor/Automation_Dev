using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Transport;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
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
            return new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
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
            var research = new ResearchSystem(new ComputeSystem());
            var service = new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), research);
            var definition = NewGatedStorageDefinition(TestDataFactory.NewResearch("test_gate", 10f));
            service.SelectBuilding(definition);

            Assert.IsFalse(service.CanPlace(new GridCoord(0, 0)));
        }

        [Test]
        public void CanPlace_True_AfterUnlockResearchIsUnlocked()
        {
            var grid = new GridRuntime(1f);
            var research = new ResearchSystem(new ComputeSystem());
            var service = new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), research);
            var unlockResearch = TestDataFactory.NewResearch("test_gate", 10f);
            var definition = NewGatedStorageDefinition(unlockResearch);
            service.SelectBuilding(definition);
            Assert.IsFalse(service.CanPlace(new GridCoord(0, 0)));

            research.Enqueue(unlockResearch);
            research.Tick(60f);
            Assert.IsTrue(research.IsUnlocked("test_gate"));

            Assert.IsTrue(service.CanPlace(new GridCoord(0, 0)));
        }

        static StorageDefinition NewStorageDefinitionWithCost(params (ItemDefinition item, int amount)[] cost)
        {
            var definition = ScriptableObject.CreateInstance<StorageDefinition>();
            var so = new UnityEditor.SerializedObject(definition);
            UnityEditor.SerializedProperty array = so.FindProperty("cost");
            array.arraySize = cost.Length;
            for (int i = 0; i < cost.Length; i++)
            {
                UnityEditor.SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("item").objectReferenceValue = cost[i].item;
                element.FindPropertyRelative("amount").intValue = cost[i].amount;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        [Test]
        public void CanAfford_And_TryPlace_DrawFromProductionBuildingInternalStock()
        {
            // Reproduces the reported bug: the Storage panel counts a production building's own
            // input+output stock as spendable, so ConstructionService must draw from the exact
            // same pool - otherwise the Building menu shows an item as unaffordable (red) even
            // though the Storage panel reports enough of it.
            var grid = new GridRuntime(1f);
            var ironPlate = TestDataFactory.NewItem("iron_plate", ItemType.Component);
            var recipeDatabase = TestDataFactory.NewRecipeDatabase();
            var transport = new TransportSystem(grid);
            var service = new ConstructionService(grid, null, recipeDatabase, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()), transport);

            var factoryDefinition = TestDataFactory.NewFactory(50, 0f, System.Array.Empty<string>(), System.Array.Empty<string>());
            var factory = new FactoryRuntime(factoryDefinition, new GridCoord(5, 5), Direction.North, recipeDatabase, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            factory.AddOutput("iron_plate", 10);
            transport.Register(factory);

            var definition = NewStorageDefinitionWithCost((ironPlate, 10));
            service.SelectBuilding(definition);

            Assert.AreEqual(10, service.GetAvailableAmount("iron_plate"));
            Assert.IsTrue(service.CanAfford(definition));

            bool placed = service.TryPlace(new GridCoord(0, 0), Direction.North, out BuildingRuntime result);

            Assert.IsTrue(placed);
            Assert.IsNotNull(result);
            factory.GetOutputContents().TryGetValue("iron_plate", out int remaining);
            Assert.AreEqual(0, remaining);
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

        // --- TASK_04_PLAFOND_RAYON.md: building cap + action radius as runtime state ---

        static (ConstructionService service, TransportSystem transport, ResearchSystem research, CoreRuntime core) NewServiceWithCore(int actionRadiusCells)
        {
            var grid = new GridRuntime(1f);
            var research = new ResearchSystem(new ComputeSystem());
            var coreDefinition = TestDataFactory.NewCore(actionRadiusCells, new Vector2Int(4, 4));
            var core = new CoreRuntime(coreDefinition, new GridCoord(0, 0), Direction.North, new ComputeSystem(), new PowerSystem(), research);
            grid.SetOccupantFootprint(core.Cell, coreDefinition.FootprintSize, core);
            var transport = new TransportSystem(grid);
            transport.Register(core);
            var service = new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), research, transport, core);
            return (service, transport, research, core);
        }

        static BuildingRuntime PlaceAndRegister(ConstructionService service, TransportSystem transport, BuildingDefinition definition, GridCoord cell)
        {
            service.SelectBuilding(definition);
            Assert.IsTrue(service.TryPlace(cell, Direction.North, out BuildingRuntime placed), $"Expected placement to succeed at {cell}");
            transport.Register(placed);
            return placed;
        }

        static StorageDefinition NewFreeStorageDefinition() => ScriptableObject.CreateInstance<StorageDefinition>();

        [Test]
        public void TryPlace_AtBuildingCap_RefusesWithBuildingCapReached()
        {
            var (service, transport, _, _) = NewServiceWithCore(1000);
            for (int i = 0; i < ConstructionService.DefaultBuildingCap; i++)
            {
                PlaceAndRegister(service, transport, NewFreeStorageDefinition(), new GridCoord(10 + i * 2, 10));
            }
            Assert.AreEqual(ConstructionService.DefaultBuildingCap, service.OccupiedBuildingSlots);

            var cell = new GridCoord(10 + ConstructionService.DefaultBuildingCap * 2, 10);
            service.SelectBuilding(NewFreeStorageDefinition());

            Assert.AreEqual(PlacementRefusalReason.BuildingCapReached, service.GetPlacementRefusalReason(cell));
            Assert.IsFalse(service.TryPlace(cell, Direction.North, out BuildingRuntime placed));
            Assert.IsNull(placed);
        }

        [Test]
        public void TryPlace_AtBuildingCap_ConveyorSplitterAndCrossroad_RemainPlaceable()
        {
            var (service, transport, _, _) = NewServiceWithCore(1000);
            for (int i = 0; i < ConstructionService.DefaultBuildingCap; i++)
            {
                PlaceAndRegister(service, transport, NewFreeStorageDefinition(), new GridCoord(10 + i * 2, 10));
            }

            service.SelectBuilding(NewConveyorDefinition());
            var conveyorCell = new GridCoord(-10, -10);
            Assert.AreEqual(PlacementRefusalReason.None, service.GetPlacementRefusalReason(conveyorCell));
            Assert.IsTrue(service.TryPlace(conveyorCell, Direction.North, out _));

            // Splitter/Crossroad are 3x3 "+" footprints - keep them far apart so they don't overlap each other.
            service.SelectBuilding(ScriptableObject.CreateInstance<SplitterDefinition>());
            Assert.AreEqual(PlacementRefusalReason.None, service.GetPlacementRefusalReason(new GridCoord(-20, -20)));

            service.SelectBuilding(ScriptableObject.CreateInstance<CrossroadDefinition>());
            Assert.AreEqual(PlacementRefusalReason.None, service.GetPlacementRefusalReason(new GridCoord(-30, -30)));
        }

        [Test]
        public void OccupiedBuildingSlots_ExcludesTheCore()
        {
            var (service, _, _, _) = NewServiceWithCore(1000);

            Assert.AreEqual(0, service.OccupiedBuildingSlots, "The Core is placed by world generation, not a player decision, and must not count against the cap.");
        }

        [Test]
        public void TryDemolish_FreesABuildingSlotImmediately()
        {
            var (service, transport, _, _) = NewServiceWithCore(1000);
            var cell = new GridCoord(5, 5);
            PlaceAndRegister(service, transport, NewFreeStorageDefinition(), cell);
            Assert.AreEqual(1, service.OccupiedBuildingSlots);

            Assert.IsTrue(service.TryDemolish(cell, out BuildingRuntime removed));
            transport.Unregister(removed); // mirrors ConstructionInputAdapter's real TryDemolish + Unregister pairing

            Assert.AreEqual(0, service.OccupiedBuildingSlots);
        }

        [Test]
        public void MemoryAllocation_Completed_RaisesBuildingCapTo52()
        {
            var (service, _, research, _) = NewServiceWithCore(1000);
            Assert.AreEqual(40, service.BuildingCap);

            ResearchDefinition memoryAllocation = TestDataFactory.NewResearch("memory_allocation", 10f);
            research.Enqueue(memoryAllocation);
            research.Tick(60f);

            Assert.IsTrue(research.IsUnlocked("memory_allocation"));
            Assert.AreEqual(52, service.BuildingCap);
        }

        [Test]
        public void ExtendedBandwidth_MakesACellAt27CellsFromCore_PlaceableWhereItWasRefusedBefore()
        {
            var (service, _, research, _) = NewServiceWithCore(22);
            var farCell = new GridCoord(27, 0); // beyond the starting 22-cell radius, within the extended 32

            service.SelectBuilding(NewConveyorDefinition());
            Assert.AreEqual(PlacementRefusalReason.OutOfActionRadius, service.GetPlacementRefusalReason(farCell));
            Assert.IsFalse(service.CanPlace(farCell));

            ResearchDefinition extendedBandwidth = TestDataFactory.NewResearch("extended_bandwidth", 10f);
            research.Enqueue(extendedBandwidth);
            research.Tick(60f);

            Assert.IsTrue(service.CanPlace(farCell), "A cell at 27 cells from the Core must become placeable once the radius extends to 32 - this is the exact promise the invitation ore clusters make.");
            Assert.IsTrue(service.TryPlace(farCell, Direction.North, out BuildingRuntime placed));
            Assert.IsNotNull(placed);
        }

        [Test]
        public void IsWithinActionRadius_ReadsCoreRuntimeValue_NotTheFrozenDefinitionValue()
        {
            var (service, _, research, core) = NewServiceWithCore(22);
            ResearchDefinition extendedBandwidth = TestDataFactory.NewResearch("extended_bandwidth", 10f);
            research.Enqueue(extendedBandwidth);
            research.Tick(60f);

            var coreDefinition = (CoreDefinition)core.Definition;
            Assert.AreEqual(22, coreDefinition.ActionRadiusCells, "The definition asset itself never changes - only the runtime value grows.");
            Assert.AreEqual(32, core.ActionRadiusCells);

            service.SelectBuilding(NewConveyorDefinition());
            Assert.IsTrue(service.CanPlace(new GridCoord(27, 0)), "Placement must follow core.ActionRadiusCells (32), not CoreDefinition.ActionRadiusCells (still 22).");
        }

        [Test]
        public void RestoreBuildingCap_SetsThePersistedValue()
        {
            var (service, _, _, _) = NewServiceWithCore(1000);

            service.RestoreBuildingCap(52);

            Assert.AreEqual(52, service.BuildingCap);
        }

        [Test]
        public void RestoreBuildingCap_ToleratesNull_FallsBackToDefault()
        {
            var (service, _, _, _) = NewServiceWithCore(1000);

            service.RestoreBuildingCap(null);

            Assert.AreEqual(ConstructionService.DefaultBuildingCap, service.BuildingCap);
        }
    }
}
