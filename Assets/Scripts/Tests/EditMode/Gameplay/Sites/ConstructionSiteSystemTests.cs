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
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Sites
{
    /// <summary>
    /// TASK_05_ROBOT_CONSTRUCTEUR.md §9's test list: construction sites, localized reservation,
    /// the two builder robots, demolition/repatriation and the save round-trip.
    /// </summary>
    public class ConstructionSiteSystemTests
    {
        const string PlateId = "iron_plate";
        const float TickSeconds = 0.2f;

        sealed class Fixture
        {
            public GridRuntime Grid;
            public TransportSystem Transport;
            public NotificationSystem Notifications;
            public ConstructionSiteSystem Sites;
            public ConstructionService Construction;
            public ItemDefinition Plate;
            public StorageRuntime CoreChest;

            /// <summary>Runs the central tick often enough for a robot to cross `cells` at its own speed, plus margin.</summary>
            public void Simulate(float seconds)
            {
                for (float elapsed = 0f; elapsed < seconds; elapsed += TickSeconds)
                {
                    Sites.Tick(TickSeconds);
                }
            }
        }

        static Fixture NewFixture(int coreChestContents = 0, bool withCoreChest = true)
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);
            var notifications = new NotificationSystem();
            var sites = new ConstructionSiteSystem(transport, grid, notifications, Vector2.zero);
            var construction = new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(),
                new ResearchSystem(new ComputeSystem()), transport, null, sites);

            var fixture = new Fixture
            {
                Grid = grid,
                Transport = transport,
                Notifications = notifications,
                Sites = sites,
                Construction = construction,
                Plate = TestDataFactory.NewItem(PlateId)
            };

            if (withCoreChest)
            {
                StorageDefinition coreChestDefinition = TestDataFactory.NewStorage(ConstructionSiteSystem.CoreStorageDefinitionId, 6, 200, rejectsConveyorInput: true);
                fixture.CoreChest = new StorageRuntime(coreChestDefinition, new GridCoord(0, 0), Direction.North);
                grid.SetOccupantFootprint(fixture.CoreChest.Cell, coreChestDefinition.FootprintSize, fixture.CoreChest);
                transport.Register(fixture.CoreChest);
                if (coreChestContents > 0) fixture.CoreChest.SeedInitialContents(PlateId, coreChestContents);
            }

            return fixture;
        }

        static StorageRuntime AddStorage(Fixture fixture, GridCoord cell, int contents = 0, int slotCount = 0, int capacityPerSlot = 0)
        {
            StorageDefinition definition = TestDataFactory.NewStorage("storage", slotCount, capacityPerSlot);
            var storage = new StorageRuntime(definition, cell, Direction.North);
            fixture.Grid.SetOccupantFootprint(cell, definition.FootprintSize, storage);
            fixture.Transport.Register(storage);
            if (contents > 0) storage.SeedInitialContents(PlateId, contents);
            return storage;
        }

        static ConstructionSiteRuntime PlaceSite(Fixture fixture, BuildingDefinition definition, GridCoord cell)
        {
            fixture.Construction.SelectBuilding(definition);
            Assert.IsTrue(fixture.Construction.TryPlace(cell, Direction.North, out ConstructionSiteRuntime site));
            return site;
        }

        static bool IsRegistered(TransportSystem transport, BuildingRuntime building)
        {
            foreach (BuildingRuntime registered in transport.GetAllBuildings())
            {
                if (ReferenceEquals(registered, building)) return true;
            }
            return false;
        }

        // --- Sites ---

        [Test]
        public void APlacedSite_IsNeitherRegisteredNorFunctional_UntilItsMaterialsArrive()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));

            Assert.IsFalse(site.IsComplete);
            Assert.IsFalse(IsRegistered(fixture.Transport, site.Segments[0]), "A chantier must not tick, transport or produce anything.");
            Assert.AreSame(site.Segments[0], fixture.Grid.GetOccupant(new GridCoord(5, 5)), "It still owns its ground, so nothing else can be placed there.");
        }

        [Test]
        public void ASite_BecomesABuilding_ExactlyWhenTheLastPieceIsDelivered()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));

            fixture.Simulate(0.4f);
            Assert.IsFalse(site.IsComplete, "Nothing is delivered before a robot has actually travelled there.");

            fixture.Simulate(12f);

            Assert.IsTrue(site.IsComplete);
            Assert.IsTrue(IsRegistered(fixture.Transport, site.Segments[0]), "Once complete the building is registered and functional.");
        }

        [Test]
        public void TwoPendingSites_AreServedOneAtATime_OldestFirst()
        {
            Fixture fixture = NewFixture(coreChestContents: 8);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            ConstructionSiteRuntime first = PlaceSite(fixture, costly, new GridCoord(5, 5));
            ConstructionSiteRuntime second = PlaceSite(fixture, costly, new GridCoord(9, 9));

            fixture.Simulate(4f);

            Assert.IsTrue(first.IsComplete, "The oldest site is served first, by both robots at once.");
            Assert.IsFalse(second.IsComplete, "Only one chantier is served at a time - the second one waits its turn.");

            fixture.Simulate(20f);
            Assert.IsTrue(second.IsComplete, "The next site is then served in turn.");
        }

        [Test]
        public void TwoRobotsServingOneSite_NeverDeliverMoreThanItsCost()
        {
            Fixture fixture = NewFixture(coreChestContents: 40);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 8));

            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(6, 6));
            fixture.Simulate(30f);

            Assert.IsTrue(site.IsComplete);
            site.Delivered.TryGetValue(PlateId, out int delivered);
            Assert.AreEqual(8, delivered, "Each robot reserves its own share before leaving - the same piece is never fetched twice.");
            Assert.AreEqual(32, fixture.CoreChest.GetInputAmount(PlateId), "Exactly the cost left the chest, no more.");
        }

        [Test]
        public void ASiteWithNoAvailableSource_NamesWhatIsMissing()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));
            fixture.Simulate(1f);

            IReadOnlyDictionary<string, int> missing = site.GetStillNeeded();
            Assert.IsTrue(missing.ContainsKey(PlateId));
            Assert.AreEqual(4, missing[PlateId]);
            Assert.Greater(fixture.Notifications.Active.Count, 0, "A chantier without materials must say so - never a silent wait.");
        }

        // --- Reservation ---

        [Test]
        public void Reservation_RemovesPromisedItemsFromWhatIsAvailable()
        {
            Fixture fixture = NewFixture(coreChestContents: 10);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            Assert.AreEqual(10, fixture.Construction.GetAvailableAmount(PlateId));

            PlaceSite(fixture, costly, new GridCoord(5, 5));

            Assert.AreEqual(6, fixture.Construction.GetAvailableAmount(PlateId), "Promised pieces stay in the chest but are no longer available to anything else.");
            Assert.AreEqual(10, fixture.CoreChest.GetInputAmount(PlateId), "They are only taken when a robot actually loads them.");
        }

        [Test]
        public void TwoSites_NeverReserveTheSamePiecesInTheSameContainer()
        {
            Fixture fixture = NewFixture(coreChestContents: 6);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            ConstructionSiteRuntime first = PlaceSite(fixture, costly, new GridCoord(5, 5));
            ConstructionSiteRuntime second = PlaceSite(fixture, costly, new GridCoord(9, 9));

            int firstReserved = 0;
            foreach (Reservation reservation in first.Reservations) firstReserved += reservation.Amount;
            int secondReserved = 0;
            foreach (Reservation reservation in second.Reservations) secondReserved += reservation.Amount;

            Assert.AreEqual(4, firstReserved, "The older site takes what it needs first.");
            Assert.AreEqual(2, secondReserved, "The younger one only gets what is left.");
            Assert.AreEqual(0, fixture.Construction.GetAvailableAmount(PlateId));
        }

        [Test]
        public void GlobalStockAggregate_IsExactlyCoreChestPlusStoragesPlusProductionOutput()
        {
            Fixture fixture = NewFixture(coreChestContents: 5);
            AddStorage(fixture, new GridCoord(20, 20), contents: 7);

            var recipeDatabase = TestDataFactory.NewRecipeDatabase();
            FactoryDefinition factoryDefinition = TestDataFactory.NewFactory(50, 0f, System.Array.Empty<string>(), System.Array.Empty<string>());
            var factory = new FactoryRuntime(factoryDefinition, new GridCoord(30, 30), Direction.North, recipeDatabase,
                new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            factory.AddOutput(PlateId, 3);
            factory.AddInput(PlateId, 100, Direction.North); // input is deliberately NOT part of the aggregate
            fixture.Transport.Register(factory);

            IReadOnlyDictionary<string, int> aggregate = fixture.Sites.GetAvailableAggregate();

            Assert.AreEqual(15, aggregate[PlateId], "Core chest (5) + Storage (7) + production output (3) - and nothing else, notably not the production input.");
        }

        // --- Core chest ---

        [Test]
        public void NoConveyorCanConnectToTheCoreChest()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);

            Assert.IsFalse(fixture.CoreChest.CanAcceptInput(PlateId, 1, Direction.North));
            Assert.IsTrue(fixture.CoreChest.CanAcceptFromRobot(PlateId, 1), "A robot's delivery is a different path entirely.");
        }

        [Test]
        public void TheCoreChest_DoesNotCountAgainstTheBuildingCap()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);

            Assert.AreEqual(0, fixture.Construction.OccupiedBuildingSlots, "The Core chest is a world fixture, not a player decision.");

            AddStorage(fixture, new GridCoord(20, 20));
            Assert.AreEqual(1, fixture.Construction.OccupiedBuildingSlots, "A player-built Storage Box still counts.");
        }

        [Test]
        public void APendingSite_CountsAgainstTheBuildingCapImmediately()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            PlaceSite(fixture, costly, new GridCoord(5, 5));

            Assert.AreEqual(1, fixture.Construction.OccupiedBuildingSlots);
        }

        // --- Demolition / repatriation ---

        [Test]
        public void Demolition_RemovesTheBuildingImmediately_AndBringsItsMaterialsBackByRobot()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            // Placed and completed the cheap way: seed the chest, let robots build it, then empty it again.
            fixture.CoreChest.SeedInitialContents(PlateId, 4);
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));
            fixture.Simulate(20f);
            Assert.IsTrue(site.IsComplete);
            Assert.AreEqual(0, fixture.CoreChest.GetInputAmount(PlateId));

            Assert.IsTrue(fixture.Construction.TryDemolish(new GridCoord(5, 5), out BuildingRuntime removed));
            fixture.Transport.Unregister(removed);

            Assert.IsFalse(fixture.Grid.IsOccupied(new GridCoord(5, 5)), "The space is freed immediately - that is usually the point of demolishing.");
            Assert.AreEqual(0, fixture.CoreChest.GetInputAmount(PlateId), "Nothing reappears instantly: a robot has to carry it back.");

            fixture.Simulate(20f);

            Assert.AreEqual(4, fixture.CoreChest.GetInputAmount(PlateId), "The materials come back physically, into the Core chest first.");
        }

        [Test]
        public void Repatriation_FallsBackToAStorage_WhenTheCoreChestIsFull()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);
            // A one-slot, one-unit chest: it can hold the seeded plate and nothing more.
            StorageRuntime storage = AddStorage(fixture, new GridCoord(20, 20));

            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 2));
            fixture.CoreChest.SeedInitialContents(PlateId, 2);
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));
            fixture.Simulate(20f);
            Assert.IsTrue(site.IsComplete);

            // Fill the Core chest to the brim (6 slots x 200) so it can take nothing more.
            for (int slot = 0; slot < 6; slot++)
            {
                fixture.CoreChest.SeedInitialContents($"filler_{slot}", 200);
            }

            Assert.IsTrue(fixture.Construction.TryDemolish(new GridCoord(5, 5), out BuildingRuntime removed));
            fixture.Transport.Unregister(removed);
            fixture.Simulate(30f);

            Assert.AreEqual(2, storage.GetInputAmount(PlateId), "Core chest first, then any Storage with room.");
        }

        [Test]
        public void ARobotThatCannotUnload_DestroysItsCargoAfter20Seconds_AndBecomesAvailableAgain()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 2));
            fixture.CoreChest.SeedInitialContents(PlateId, 2);
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));
            fixture.Simulate(20f);
            Assert.IsTrue(site.IsComplete);

            for (int slot = 0; slot < 6; slot++)
            {
                fixture.CoreChest.SeedInitialContents($"filler_{slot}", 200);
            }

            Assert.IsTrue(fixture.Construction.TryDemolish(new GridCoord(5, 5), out BuildingRuntime removed));
            fixture.Transport.Unregister(removed);

            fixture.Simulate(1f);
            BuilderRobotRuntime blocked = null;
            foreach (BuilderRobotRuntime robot in fixture.Sites.Robots)
            {
                if (robot.State == BuilderRobotState.Blocked) blocked = robot;
            }
            Assert.IsNotNull(blocked, "With nowhere at all to unload, the robot blocks and says so.");
            Assert.Greater(fixture.Notifications.Active.Count, 0);

            fixture.Simulate(BuilderRobotRuntime.BlockedDestructionSeconds + 2f);

            Assert.AreEqual(BuilderRobotState.Idle, blocked.State, "The anti-deadlock destroys the cargo and frees the robot.");
            Assert.AreEqual(0, blocked.CargoTotal);
        }

        [Test]
        public void GlobalStockAggregate_IsNeverCreditedByADemolition()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));
            fixture.Simulate(20f);
            Assert.IsTrue(site.IsComplete);

            int beforeDemolition = fixture.Construction.GetAvailableAmount(PlateId);
            Assert.IsTrue(fixture.Construction.TryDemolish(new GridCoord(5, 5), out BuildingRuntime removed));
            fixture.Transport.Unregister(removed);

            Assert.AreEqual(beforeDemolition, fixture.Construction.GetAvailableAmount(PlateId),
                "Demolition credits nothing anywhere - the aggregate only moves once a robot has physically delivered.");
        }

        // --- Conveyor drag ---

        [Test]
        public void AConveyorDrag_CreatesASingleSite_ThatMaterializesSegmentBySegment()
        {
            Fixture fixture = NewFixture(coreChestContents: 3);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 1));

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5, 5), Direction.East, out ConstructionSiteRuntime site));
            for (int i = 1; i < 3; i++)
            {
                Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5 + i, 5), Direction.East, out ConstructionSiteRuntime sameSite, site));
                Assert.AreSame(site, sameSite, "A whole drag is one chantier, not one per segment.");
            }

            Assert.AreEqual(3, site.Segments.Count);
            Assert.AreEqual(1, fixture.Sites.Sites.Count);

            fixture.Simulate(20f);

            Assert.IsTrue(site.IsComplete);
            Assert.AreEqual(3, site.MaterializedCount);
        }

        /// <summary>
        /// A pending segment occupies the grid from the moment it is placed, so anything resolving
        /// a click through Grid.GetOccupant would happily open the panel of a building that has
        /// received nothing (BuildingSelectionInput guards on exactly this predicate). What matters
        /// is that the guard keys on the segment being materialized and not on the site still being
        /// open: in a half-built run, the finished segment is a real, working building and must
        /// stay selectable while its siblings do not.
        /// </summary>
        [Test]
        public void APartlyBuiltRun_ReportsOnlyItsUnmaterializedSegmentsAsPending()
        {
            // One plate in the chest, one plate per segment: segment 0 can be built, 1 and 2 cannot.
            Fixture fixture = NewFixture(coreChestContents: 1);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 1));

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5, 5), Direction.East, out ConstructionSiteRuntime site));
            for (int i = 1; i < 3; i++)
            {
                Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5 + i, 5), Direction.East, out _, site));
            }

            fixture.Simulate(20f);

            Assert.AreEqual(1, site.MaterializedCount, "Only the first segment had material.");

            Assert.IsFalse(fixture.Sites.TryGetSiteContaining(site.Segments[0], out _),
                "The built segment is a real building and must stay selectable.");
            Assert.IsTrue(fixture.Sites.TryGetSiteContaining(site.Segments[1], out _),
                "A segment still waiting for material must not answer a click.");
            Assert.IsTrue(fixture.Sites.TryGetSiteContaining(site.Segments[2], out _));
        }

        // --- Save / restore ---

        [Test]
        public void CaptureRestore_RoundTripsAPartialSiteAndALoadedRobot()
        {
            Fixture fixture = NewFixture(coreChestContents: 10);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 8));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));

            // 1s in: both robots have loaded at the chest and are still travelling to the site -
            // exactly the "chantier partiel + robot chargé" case the ticket asks to round-trip.
            fixture.Simulate(1f);
            Assert.IsFalse(site.IsComplete);
            int cargoInFlight = 0;
            foreach (BuilderRobotRuntime robot in fixture.Sites.Robots) cargoInFlight += robot.CargoTotal;
            Assert.Greater(cargoInFlight, 0, "A robot must actually be carrying something for this test to mean anything.");

            Newtonsoft.Json.Linq.JObject captured = fixture.Sites.CaptureState();

            // A fresh world with the same containers, restoring that capture.
            Fixture restored = NewFixture(coreChestContents: 10);
            restored.Sites.RestoreState(captured,
                (definition, cell, rotation) => restored.Construction.CreateForRestore(definition, cell, rotation),
                id => id == "target" ? costly : null);

            Assert.AreEqual(1, restored.Sites.Sites.Count);
            ConstructionSiteRuntime restoredSite = restored.Sites.Sites[0];
            Assert.AreEqual(site.Id, restoredSite.Id);
            Assert.AreEqual(site.Segments.Count, restoredSite.Segments.Count);
            Assert.AreEqual(site.MaterializedCount, restoredSite.MaterializedCount);
            Assert.AreEqual(site.RemainingNeeded(PlateId), restoredSite.RemainingNeeded(PlateId));

            restored.Simulate(30f);
            Assert.IsTrue(restoredSite.IsComplete, "A restored chantier keeps building where it left off.");
        }

        [Test]
        public void Restore_OnABlobMissingTheseKeys_DoesNotThrow()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);

            Assert.DoesNotThrow(() => fixture.Sites.RestoreState(new Newtonsoft.Json.Linq.JObject(),
                (definition, cell, rotation) => fixture.Construction.CreateForRestore(definition, cell, rotation),
                id => null));
            Assert.DoesNotThrow(() => fixture.Sites.RestoreState(null,
                (definition, cell, rotation) => fixture.Construction.CreateForRestore(definition, cell, rotation),
                id => null));

            Assert.AreEqual(0, fixture.Sites.Sites.Count);
            Assert.AreEqual(2, fixture.Sites.Robots.Count, "Falls back to two idle robots with no site.");
        }
    }
}
