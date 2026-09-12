using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Notifications;
using Game.Gameplay.Research;
using Game.Gameplay.Sites;
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
        // Since TASK_05_ROBOT_CONSTRUCTEUR.md, placing opens a construction site rather than
        // building instantly, so a ConstructionSiteSystem is now part of the minimal setup.
        static ConstructionService NewService(GridRuntime grid) => NewService(grid, out _);

        static ConstructionService NewService(GridRuntime grid, out ConstructionSiteSystem sites)
        {
            var transport = new TransportSystem(grid);
            sites = new ConstructionSiteSystem(transport, grid, new NotificationSystem(), Vector2.zero);
            return new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()), transport, null, sites);
        }

        /// <summary>
        /// Runs the queue until everything placed has actually been built.
        ///
        /// Costing nothing is not the same as being built: a free segment owes no delivery but still
        /// has to assemble, and until it has, it is a pending segment its own chantier still holds -
        /// which TryDemolish refuses to touch.
        /// </summary>
        static void BuildEverything(ConstructionSiteSystem sites)
        {
            for (int i = 0; i < 200; i++) sites.Tick(0.2f);
        }

        /// <summary>The building a site was opened for. Cost-free definitions (used throughout these tests) materialize on the spot, so this is also the finished building.</summary>
        static BuildingRuntime FirstSegment(ConstructionSiteRuntime site) => site?.Segments[0];

        [Test]
        public void TryPlace_OnEmptyCell_OpensASiteForThatBuilding()
        {
            var service = NewService(new GridRuntime(1f));
            service.SelectBuilding(NewConveyorDefinition());

            bool result = service.TryPlace(new GridCoord(0, 0), Direction.North, out ConstructionSiteRuntime site);

            Assert.IsTrue(result);
            Assert.IsNotNull(site);
            Assert.IsInstanceOf<ConveyorRuntime>(FirstSegment(site));
        }

        [Test]
        public void TryPlace_WithoutSelection_Fails()
        {
            var service = NewService(new GridRuntime(1f));

            bool result = service.TryPlace(new GridCoord(0, 0), Direction.North, out ConstructionSiteRuntime site);

            Assert.IsFalse(result);
            Assert.IsNull(site);
        }

        [Test]
        public void TryPlace_OntoExistingConveyor_OvertakesAndReplaces()
        {
            var grid = new GridRuntime(1f);
            var service = NewService(grid);
            var cell = new GridCoord(2, 2);

            service.SelectBuilding(NewConveyorDefinition());
            service.TryPlace(cell, Direction.North, out ConstructionSiteRuntime firstSite);
            // Read now: overtaking removes the pending segment from the site that held it, so by the
            // time the second placement returns, the first site has none left to ask for.
            BuildingRuntime first = FirstSegment(firstSite);

            service.SelectBuilding(NewConveyorDefinition());
            bool result = service.TryPlace(cell, Direction.East, out ConstructionSiteRuntime secondSite);

            Assert.IsTrue(result);
            Assert.AreNotSame(first, FirstSegment(secondSite));
            Assert.AreSame(FirstSegment(secondSite), grid.GetOccupant(cell));
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
            var service = NewService(grid, out ConstructionSiteSystem sites);
            var cell = new GridCoord(0, 0);
            service.SelectBuilding(NewConveyorDefinition());
            service.TryPlace(cell, Direction.North, out _);
            BuildEverything(sites);

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

        /// <summary>A Storage the given research unlocks. The research names the building - never the reverse.</summary>
        static StorageDefinition NewGatedStorageDefinition(ResearchDefinition unlockedBy)
        {
            var definition = ScriptableObject.CreateInstance<StorageDefinition>();
            TestDataFactory.WithEffects(unlockedBy, ResearchEffect.UnlockBuilding(definition));
            return definition;
        }

        [Test]
        public void CanPlace_False_WhenUnlockResearchNotUnlocked()
        {
            var grid = new GridRuntime(1f);
            ResearchDefinition gate = TestDataFactory.NewResearch("test_gate", 10f);
            var definition = NewGatedStorageDefinition(gate);
            var service = NewServiceWithResearch(grid, new ResearchSystem(new ComputeSystem(), new ResearchCatalog(new[] { gate })));
            service.SelectBuilding(definition);

            Assert.IsFalse(service.CanPlace(new GridCoord(0, 0)));
            Assert.AreEqual(PlacementRefusalReason.NotUnlocked, service.GetPlacementRefusalReason(new GridCoord(0, 0)));
        }

        static ConstructionService NewServiceWithResearch(GridRuntime grid, ResearchSystem research)
        {
            var transport = new TransportSystem(grid);
            var sites = new ConstructionSiteSystem(transport, grid, new NotificationSystem(), Vector2.zero);
            return new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), research, transport, null, sites);
        }

        [Test]
        public void CanPlace_True_AfterUnlockResearchIsUnlocked()
        {
            var grid = new GridRuntime(1f);
            ResearchDefinition gate = TestDataFactory.NewResearch("test_gate", 10f);
            var definition = NewGatedStorageDefinition(gate);
            var research = new ResearchSystem(new ComputeSystem(), new ResearchCatalog(new[] { gate }));
            var service = NewServiceWithResearch(grid, research);
            service.SelectBuilding(definition);
            Assert.IsFalse(service.CanPlace(new GridCoord(0, 0)));

            research.Enqueue(gate);
            research.Tick(60f);

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
        public void TryPlace_ReservesFromProductionOutput_WithoutTakingAnythingYet()
        {
            // Since TASK_05_ROBOT_CONSTRUCTEUR.md, placing never deducts: it reserves. The items
            // stay physically in the production building's output until a robot picks them up, but
            // stop counting as available (GlobalStock's invariant: what it shows is exactly what a
            // robot could still be sent to fetch).
            var grid = new GridRuntime(1f);
            var ironPlate = TestDataFactory.NewItem("iron_plate", ItemType.Component);
            var recipeDatabase = TestDataFactory.NewRecipeDatabase();
            var transport = new TransportSystem(grid);
            var sites = new ConstructionSiteSystem(transport, grid, new NotificationSystem(), Vector2.zero);
            var service = new ConstructionService(grid, null, recipeDatabase, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()), transport, null, sites);

            var factoryDefinition = TestDataFactory.NewFactory(0f, System.Array.Empty<string>(), System.Array.Empty<string>());
            var factory = new FactoryRuntime(factoryDefinition, new GridCoord(5, 5), Direction.North, recipeDatabase, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            factory.AddOutput("iron_plate", 10);
            transport.Register(factory);

            var definition = NewStorageDefinitionWithCost((ironPlate, 10));
            service.SelectBuilding(definition);

            Assert.AreEqual(10, service.GetAvailableAmount("iron_plate"));
            Assert.IsTrue(service.CanAfford(definition));

            bool placed = service.TryPlace(new GridCoord(0, 0), Direction.North, out ConstructionSiteRuntime site);

            Assert.IsTrue(placed);
            Assert.IsNotNull(site);
            factory.GetOutputContents().TryGetValue("iron_plate", out int stillInOutput);
            Assert.AreEqual(10, stillInOutput, "Reserved items stay physically where they are until a robot loads them.");
            Assert.AreEqual(0, service.GetAvailableAmount("iron_plate"), "Reserved items are no longer available to anything else.");
        }

        /// <summary>
        /// A building whose materials do not exist cannot be placed at all. Placing still does not
        /// PAY - it opens a site that reserves the whole bill and waits for robots to carry it - but
        /// the bill has to be coverable by unreserved stock at that instant, so a site is never
        /// opened against material nobody has.
        /// </summary>
        [Test]
        public void TryPlace_WithNothingAvailable_IsRefused()
        {
            var grid = new GridRuntime(1f);
            var ironPlate = TestDataFactory.NewItem("iron_plate", ItemType.Component);
            var service = NewService(grid);
            var definition = NewStorageDefinitionWithCost((ironPlate, 10));
            service.SelectBuilding(definition);

            Assert.IsFalse(service.CanAfford(definition));
            Assert.AreEqual(PlacementRefusalReason.CannotAfford, service.GetPlacementRefusalReason(new GridCoord(0, 0)),
                "And the refusal names its cause: nothing on screen distinguishes a click that did "
                + "nothing from one that was refused.");
            Assert.IsFalse(service.CanPlace(new GridCoord(0, 0)), "So the ghost reads as invalid too.");
            Assert.IsFalse(service.TryPlace(new GridCoord(0, 0), Direction.North, out ConstructionSiteRuntime site));
            Assert.IsNull(site);
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

        static (ConstructionService service, TransportSystem transport, ResearchSystem research, CoreRuntime core) NewServiceWithCore(int actionRadiusCells, params ResearchDefinition[] known)
            => NewServiceWithCore(actionRadiusCells, out _, known);

        static (ConstructionService service, TransportSystem transport, ResearchSystem research, CoreRuntime core) NewServiceWithCore(int actionRadiusCells, out ConstructionSiteSystem siteSystem, params ResearchDefinition[] known)
        {
            var grid = new GridRuntime(1f);
            var research = new ResearchSystem(new ComputeSystem(), new ResearchCatalog(known));
            var coreDefinition = TestDataFactory.NewCore(actionRadiusCells, new Vector2Int(4, 4));
            var core = new CoreRuntime(coreDefinition, new GridCoord(0, 0), Direction.North, new ComputeSystem(), new PowerSystem(), research);
            grid.SetOccupantFootprint(core.Cell, coreDefinition.FootprintSize, core);
            var transport = new TransportSystem(grid);
            transport.Register(core);
            var sites = new ConstructionSiteSystem(transport, grid, new NotificationSystem(), Vector2.zero);
            siteSystem = sites;
            var service = new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(), research, transport, core, sites);
            return (service, transport, research, core);
        }

        /// <summary>Places a cost-free building: its site materializes immediately (nothing owed), and ConstructionSiteSystem registers it with Transport itself - no manual Register here any more.</summary>
        static BuildingRuntime PlaceAndRegister(ConstructionService service, TransportSystem transport, BuildingDefinition definition, GridCoord cell)
        {
            service.SelectBuilding(definition);
            Assert.IsTrue(service.TryPlace(cell, Direction.North, out ConstructionSiteRuntime site), $"Expected placement to succeed at {cell}");
            return FirstSegment(site);
        }

        static StorageDefinition NewFreeStorageDefinition() => ScriptableObject.CreateInstance<StorageDefinition>();

        /// <summary>A costless 1x1 building that does take a cap slot - what the cap tests fill the world with, now that Storage is exempt like the transport pieces.</summary>
        static FactoryDefinition NewFreeCountingDefinition() => ScriptableObject.CreateInstance<FactoryDefinition>();

        /// <summary>
        /// A box is somewhere to put things, not a machine - it takes no slot, and a world full of
        /// them still has room for a factory.
        /// </summary>
        [Test]
        public void StorageBoxes_DoNotCountAgainstTheBuildingCap()
        {
            var (service, transport, _, _) = NewServiceWithCore(1000);

            for (int i = 0; i < ConstructionService.DefaultBuildingCap + 5; i++)
            {
                PlaceAndRegister(service, transport, NewFreeStorageDefinition(), new GridCoord(10 + i * 2, 10));
            }

            Assert.AreEqual(0, service.OccupiedBuildingSlots, "Boxes take no slots at all.");

            service.SelectBuilding(NewFreeCountingDefinition());
            Assert.AreEqual(PlacementRefusalReason.None, service.GetPlacementRefusalReason(new GridCoord(-20, -20)),
                "And a machine can still be built past a wall of them.");
        }

        [Test]
        public void TryPlace_AtBuildingCap_RefusesWithBuildingCapReached()
        {
            var (service, transport, _, _) = NewServiceWithCore(1000);
            for (int i = 0; i < ConstructionService.DefaultBuildingCap; i++)
            {
                PlaceAndRegister(service, transport, NewFreeCountingDefinition(), new GridCoord(10 + i * 2, 10));
            }
            Assert.AreEqual(ConstructionService.DefaultBuildingCap, service.OccupiedBuildingSlots);

            var cell = new GridCoord(10 + ConstructionService.DefaultBuildingCap * 2, 10);
            service.SelectBuilding(NewFreeCountingDefinition());

            Assert.AreEqual(PlacementRefusalReason.BuildingCapReached, service.GetPlacementRefusalReason(cell));
            Assert.IsFalse(service.TryPlace(cell, Direction.North, out ConstructionSiteRuntime site));
            Assert.IsNull(site);
        }

        [Test]
        public void TryPlace_AtBuildingCap_ConveyorSplitterAndCrossroad_RemainPlaceable()
        {
            var (service, transport, _, _) = NewServiceWithCore(1000);
            for (int i = 0; i < ConstructionService.DefaultBuildingCap; i++)
            {
                PlaceAndRegister(service, transport, NewFreeCountingDefinition(), new GridCoord(10 + i * 2, 10));
            }

            service.SelectBuilding(NewConveyorDefinition());
            var conveyorCell = new GridCoord(-10, -10);
            Assert.AreEqual(PlacementRefusalReason.None, service.GetPlacementRefusalReason(conveyorCell));
            Assert.IsTrue(service.TryPlace(conveyorCell, Direction.North, out _));

            // Splitter/Crossroad are single cells now, but keep them apart anyway - the point of the
            // test is the cap, not adjacency.
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
            var (service, transport, _, _) = NewServiceWithCore(1000, out ConstructionSiteSystem sites);
            var cell = new GridCoord(5, 5);
            PlaceAndRegister(service, transport, NewFreeCountingDefinition(), cell);
            Assert.AreEqual(1, service.OccupiedBuildingSlots);

            // Costing nothing is not being built: until it has assembled it is a pending segment its
            // chantier still holds, and TryDemolish refuses those.
            BuildEverything(sites);

            Assert.IsTrue(service.TryDemolish(cell, out BuildingRuntime removed));
            transport.Unregister(removed); // mirrors ConstructionInputAdapter's real TryDemolish + Unregister pairing

            Assert.AreEqual(0, service.OccupiedBuildingSlots);
        }

        /// <summary>A test research carrying one figure. Named for what it does, never after a shipped research: the id is not what the effect hangs on.</summary>
        static ResearchDefinition Raising(ResearchEffectKind kind, int value)
            => TestDataFactory.WithEffects(TestDataFactory.NewResearch(kind + "_" + value, 10f), new ResearchEffect(kind, value: value));

        [Test]
        public void ABuildingCapEffect_RaisesTheCapToItsTarget()
        {
            ResearchDefinition cap75 = Raising(ResearchEffectKind.BuildingCap, 75);
            var (service, _, research, _) = NewServiceWithCore(1000, cap75);
            Assert.AreEqual(36, service.BuildingCap, "A run starts at 36 slots.");

            research.Enqueue(cap75);
            research.Tick(60f);

            Assert.AreEqual(75, service.BuildingCap);
        }

        /// <summary>Each research sets its own target and the highest completed wins: a lower one landing after a higher one never takes slots away.</summary>
        [Test]
        public void BuildingCapTargets_TheHighestReachedWins_WhateverTheOrder()
        {
            ResearchDefinition cap75 = Raising(ResearchEffectKind.BuildingCap, 75);
            ResearchDefinition cap100 = Raising(ResearchEffectKind.BuildingCap, 100);
            ResearchDefinition cap200 = Raising(ResearchEffectKind.BuildingCap, 200);
            var (service, _, research, _) = NewServiceWithCore(1000, cap75, cap100, cap200);

            research.Grant(cap100.Id);
            Assert.AreEqual(100, service.BuildingCap);

            research.Grant(cap200.Id);
            Assert.AreEqual(200, service.BuildingCap);

            research.Grant(cap75.Id);
            Assert.AreEqual(200, service.BuildingCap, "A lower target after a higher one changes nothing.");
        }

        [Test]
        public void ARadiusEffect_MakesACellAt27CellsFromCore_PlaceableWhereItWasRefusedBefore()
        {
            ResearchDefinition radius42 = Raising(ResearchEffectKind.ActionRadius, 42);
            var (service, _, research, _) = NewServiceWithCore(22, radius42);
            var farCell = new GridCoord(27, 0); // beyond the starting 22-cell radius, within 42

            service.SelectBuilding(NewConveyorDefinition());
            Assert.AreEqual(PlacementRefusalReason.OutOfActionRadius, service.GetPlacementRefusalReason(farCell));
            Assert.IsFalse(service.CanPlace(farCell));

            research.Enqueue(radius42);
            research.Tick(60f);

            Assert.IsTrue(service.CanPlace(farCell), "A cell at 27 cells from the Core must become placeable once the radius extends to 42 - this is the exact promise the invitation ore clusters make.");
            Assert.IsTrue(service.TryPlace(farCell, Direction.North, out ConstructionSiteRuntime site));
            Assert.IsNotNull(FirstSegment(site));
        }

        /// <summary>
        /// <b>The buildable disc is the circle the player is looking at.</b> ActionRadiusView draws a
        /// ring centred on the Core's footprint centre; this gate used to measure from the Core's
        /// lowest-left cell, two cells away on a 4x4 Core. The disc was therefore offset from the
        /// ring: it reached two cells past it on one side and stopped two cells short on the other,
        /// and the ghost stayed green over ground outside the ring.
        ///
        /// Asserted from both sides of the Core, because an offset disc is right in the middle and
        /// wrong at both edges - a single-sided test would have passed against the old arithmetic.
        ///
        /// The Core here is 4x4 at origin (0,0), so its centre is (2,2) and its ring runs from
        /// x = -20 to x = 24 with a 22-cell radius.
        /// </summary>
        [Test]
        public void TheBuildableDisc_IsCentredWhereTheRingIsDrawn()
        {
            var (service, _, _, core) = NewServiceWithCore(22);
            service.SelectBuilding(NewConveyorDefinition());

            Assert.AreEqual(new Vector2Int(4, 4), core.Definition.FootprintSize);
            Assert.AreEqual(new GridCoord(0, 0), core.Cell);

            // Past the ring on the low side: the ring stops at x = -20, and measuring from the
            // origin cell instead let this through.
            Assert.IsFalse(service.CanPlace(new GridCoord(-22, 0)),
                "a cell outside the drawn ring must be refused, however it measures from the origin cell");

            // Short of the ring on the high side: the ring reaches x = 24, and measuring from the
            // origin cell refused this.
            Assert.IsTrue(service.CanPlace(new GridCoord(23, 0)),
                "a cell inside the drawn ring must be allowed");

            // And the middle is unaffected either way.
            Assert.IsTrue(service.CanPlace(new GridCoord(5, 5)));
        }

        /// <summary>
        /// A cell is in or out by its body, not by its corner. Half a cell on top of the two the
        /// centre was out by, and the same class of error: a coordinate is not a place.
        /// </summary>
        [Test]
        public void ACellCountsByItsCentre_NotItsCoordinate()
        {
            var (service, _, _, _) = NewServiceWithCore(10);
            service.SelectBuilding(NewConveyorDefinition());

            // Core centre (2,2), radius 10. Cell (12,2) has its centre at (12.5,2.5): 10.51 out,
            // so refused. Its coordinate alone is 10.0 out, which the old check called inside.
            Assert.IsFalse(service.CanPlace(new GridCoord(12, 2)));
            Assert.IsTrue(service.CanPlace(new GridCoord(11, 2)), "one cell in, at 9.51, is inside");
        }

        [Test]
        public void IsWithinActionRadius_ReadsCoreRuntimeValue_NotTheFrozenDefinitionValue()
        {
            ResearchDefinition radius42 = Raising(ResearchEffectKind.ActionRadius, 42);
            var (service, _, research, core) = NewServiceWithCore(22, radius42);
            research.Enqueue(radius42);
            research.Tick(60f);

            var coreDefinition = (CoreDefinition)core.Definition;
            Assert.AreEqual(22, coreDefinition.ActionRadiusCells, "The definition asset itself never changes - only the runtime value grows.");
            Assert.AreEqual(42, core.ActionRadiusCells);

            service.SelectBuilding(NewConveyorDefinition());
            Assert.IsTrue(service.CanPlace(new GridCoord(27, 0)), "Placement must follow core.ActionRadiusCells (42), not CoreDefinition.ActionRadiusCells (still 22).");
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
