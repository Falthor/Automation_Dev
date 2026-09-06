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

        /// <summary>
        /// Runs until the site's first delivery lands, then stops - the deterministic way to catch a
        /// run half built, now that a short chest cannot produce one. Stopping on the state rather
        /// than on a duration is what keeps it from depending on robot travel time.
        /// </summary>
        static void AdvanceToFirstDelivery(Fixture fixture, ConstructionSiteRuntime site)
        {
            for (int i = 0; i < 500 && site.MaterializedCount == 0; i++)
            {
                fixture.Sites.Tick(TickSeconds);
            }

            Assert.Greater(site.MaterializedCount, 0, "Nothing was ever delivered - the run never started.");
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
            Fixture fixture = NewFixture(coreChestContents: 4);
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

        /// <summary>
        /// A building whose materials do not exist is refused at the gesture, not opened as a site
        /// that will wait forever. The failure moved from after the fact to the moment of the click,
        /// which is the whole point of the gate: nothing is queued, nothing occupies the ground, and
        /// the player is told immediately rather than discovering it later in a panel.
        /// </summary>
        [Test]
        public void PlacingWithNoAvailableSource_IsRefused_AndQueuesNothing()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            fixture.Construction.SelectBuilding(costly);
            var cell = new GridCoord(5, 5);

            Assert.AreEqual(PlacementRefusalReason.CannotAfford, fixture.Construction.GetPlacementRefusalReason(cell));
            Assert.IsFalse(fixture.Construction.TryPlace(cell, Direction.North, out ConstructionSiteRuntime site));
            Assert.IsNull(site);

            Assert.AreEqual(0, fixture.Sites.Sites.Count, "No chantier is queued...");
            Assert.IsNull(fixture.Grid.GetOccupant(cell), "...and the ground stays free.");
            Assert.AreEqual(0, fixture.Construction.OccupiedBuildingSlots, "It costs no building slot either.");
        }

        /// <summary>
        /// The gate reads unreserved stock, so sites already placed have taken their material out of
        /// what the next placement can see. Two buildings of four plates need eight between them -
        /// six buys exactly one.
        /// </summary>
        [Test]
        public void PlacingMoreThanTheStockCovers_IsRefusedAtTheFirstOneItCannotCover()
        {
            Fixture fixture = NewFixture(coreChestContents: 6);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            fixture.Construction.SelectBuilding(costly);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5, 5), Direction.North, out _),
                "Six plates cover the first building...");

            Assert.AreEqual(PlacementRefusalReason.CannotAfford,
                fixture.Construction.GetPlacementRefusalReason(new GridCoord(9, 9)),
                "...and the two left over cannot cover a second, because the first one's four are "
                + "reserved and no longer claimable.");

            Assert.IsFalse(fixture.Construction.TryPlace(new GridCoord(9, 9), Direction.North, out _));
            Assert.AreEqual(1, fixture.Sites.Sites.Count);
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
            Fixture fixture = NewFixture(coreChestContents: 8);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            ConstructionSiteRuntime first = PlaceSite(fixture, costly, new GridCoord(5, 5));
            ConstructionSiteRuntime second = PlaceSite(fixture, costly, new GridCoord(9, 9));

            int firstReserved = 0;
            foreach (Reservation reservation in first.Reservations) firstReserved += reservation.Amount;
            int secondReserved = 0;
            foreach (Reservation reservation in second.Reservations) secondReserved += reservation.Amount;

            // Both sites are whole - the gate would have refused the second otherwise - so what this
            // asserts is that their claims are DISJOINT: eight plates covering two bills of four,
            // with nothing left over and no plate counted twice.
            Assert.AreEqual(4, firstReserved, "The older site takes its own four...");
            Assert.AreEqual(4, secondReserved, "...and the younger takes four others, never the same ones.");
            Assert.AreEqual(0, fixture.Construction.GetAvailableAmount(PlateId), "Which is exactly the chest.");
            Assert.AreEqual(8, fixture.CoreChest.GetInputAmount(PlateId), "Still physically there until a robot loads them.");
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
            Fixture fixture = NewFixture(coreChestContents: 4);
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
            // A run is always fully funded now - the gate refuses a segment it cannot cover - so the
            // partial state comes from DELIVERY, not from a short chest. Three segments at one
            // robot-load each, sampled the moment the first load lands: one segment built, two still
            // waiting on trips that have not finished.
            Fixture fixture = NewFixture(coreChestContents: 12);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 4));

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5, 5), Direction.East, out ConstructionSiteRuntime site));
            for (int i = 1; i < 3; i++)
            {
                Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5 + i, 5), Direction.East, out _, site));
            }

            AdvanceToFirstDelivery(fixture, site);

            Assert.AreEqual(1, site.MaterializedCount, "The first load built one segment, not the whole run.");

            Assert.IsFalse(fixture.Sites.TryGetSiteContaining(site.Segments[0], out _),
                "A built segment is a real building and must stay selectable.");
            Assert.IsTrue(fixture.Sites.TryGetSiteContaining(site.Segments[1], out _),
                "A segment still waiting for its delivery must not answer a click.");
            Assert.IsTrue(fixture.Sites.TryGetSiteContaining(site.Segments[2], out _));
        }

        /// <summary>
        /// SegmentProgress is the only form in which per-segment advancement leaves Gameplay: the
        /// rule about which delivery feeds which segment must stay owned here rather than be
        /// re-derived by the view that draws it. Tested at this level for that reason.
        /// </summary>
        [Test]
        public void SegmentProgress_IsOneBehindTheFront_ZeroAhead_AndARatioOnTheSegmentBeingBuilt()
        {
            Fixture fixture = NewFixture(coreChestContents: 12);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 4));

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5, 5), Direction.East, out ConstructionSiteRuntime site));
            for (int i = 1; i < 3; i++)
            {
                Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5 + i, 5), Direction.East, out _, site));
            }

            Assert.AreEqual(0f, site.SegmentProgress(0), 0.0001f, "Reserved is not delivered - nothing has arrived yet.");

            AdvanceToFirstDelivery(fixture, site);

            Assert.AreEqual(1, site.MaterializedCount);
            Assert.AreEqual(1f, site.SegmentProgress(0), 0.0001f, "Built segments read as done.");
            Assert.AreEqual(0f, site.SegmentProgress(1), 0.0001f, "The front consumes deliveries first, so the next one has nothing.");
            Assert.AreEqual(0f, site.SegmentProgress(2), 0.0001f);
            Assert.AreEqual(0f, site.SegmentProgress(7), 0.0001f, "Out of range is 0, not an exception.");
        }

        /// <summary>A single building is the common case: its segment progress is its delivered-over-cost ratio, moving in steps as lots land.</summary>
        [Test]
        public void SegmentProgress_OnASingleBuilding_TracksDeliveredOverCost()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));

            Assert.AreEqual(0f, site.SegmentProgress(0), 0.0001f);

            site.RegisterDelivery(PlateId, 1);
            Assert.AreEqual(0.25f, site.SegmentProgress(0), 0.0001f);

            site.RegisterDelivery(PlateId, 3);
            Assert.AreEqual(1f, site.SegmentProgress(0), 0.0001f);
        }

        static bool IsQueued(Fixture fixture, ConstructionSiteRuntime site)
        {
            foreach (ConstructionSiteRuntime queued in fixture.Sites.Sites)
            {
                if (ReferenceEquals(queued, site)) return true;
            }
            return false;
        }

        static ConstructionSiteRuntime PlaceRun(Fixture fixture, ConveyorDefinition conveyor, GridCoord from, int length)
        {
            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(from, Direction.East, out ConstructionSiteRuntime site));
            for (int i = 1; i < length; i++)
            {
                Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(from.X + i, from.Y), Direction.East, out _, site));
            }
            return site;
        }

        /// <summary>
        /// Chaining two drags - releasing at the end of a run and pressing again on that same last
        /// belt to turn - reuses exactly one cell. Overtaking is a statement about that cell and
        /// nothing else: cancelling the whole chantier there deleted the run the player had just
        /// laid, which is what made a second drag impossible.
        /// </summary>
        [Test]
        public void StartingASecondDragOnTheEndOfTheFirst_TakesOneSegment_NotTheWholeRun()
        {
            Fixture fixture = NewFixture(coreChestContents: 20);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 1));

            ConstructionSiteRuntime first = PlaceRun(fixture, conveyor, new GridCoord(5, 5), 4);
            var reused = new GridCoord(8, 5);

            // A fresh drag anchored on the last belt of the previous one, turning north.
            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(reused, Direction.North, out ConstructionSiteRuntime second));

            Assert.IsTrue(IsQueued(fixture, first), "The first run is still being built.");
            Assert.AreEqual(3, first.Segments.Count, "Only the reused cell left it.");
            Assert.AreEqual(3, first.TotalCost[PlateId], "Its bill shrank by exactly that segment.");
            Assert.AreSame(first.Segments[0], fixture.Grid.GetOccupant(new GridCoord(5, 5)), "Every other segment still owns its ground.");
            Assert.AreSame(second.Segments[0], fixture.Grid.GetOccupant(reused), "And the new drag owns the cell it took.");
        }

        [Test]
        public void OvertakingTheOnlySegmentOfASite_ClosesIt_AndDoesNotClaimItsPlateTwice()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 1));
            var cell = new GridCoord(5, 5);

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(cell, Direction.East, out ConstructionSiteRuntime lone));
            Assert.AreEqual(3, fixture.Construction.GetAvailableAmount(PlateId), "One plate is spoken for.");

            Assert.IsTrue(fixture.Construction.TryPlace(cell, Direction.North, out ConstructionSiteRuntime replacement));

            Assert.IsFalse(IsQueued(fixture, lone), "A site with nothing left to build is over.");
            Assert.IsTrue(IsQueued(fixture, replacement));
            Assert.AreEqual(1, fixture.Sites.Sites.Count, "Exactly one chantier holds that cell.");
            Assert.AreEqual(3, fixture.Construction.GetAvailableAmount(PlateId),
                "The old earmark went back before the new one was taken - one plate is claimed, not two.");
        }

        /// <summary>
        /// Overtaking a belt that is already built demolishes it, and a demolition never destroys
        /// material: the plate comes back the only way anything comes back here, carried by a robot.
        /// It used to be dropped where it stood - view removed, unregistered, cost gone silently.
        /// </summary>
        [Test]
        public void OvertakingABuiltBelt_HaulsItsPlateBack_InsteadOfDestroyingIt()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 4));

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(6, 5), Direction.East, out ConstructionSiteRuntime belt));
            fixture.Simulate(20f);

            Assert.IsTrue(belt.IsComplete, "The belt has to be really built for this to be a demolition at all.");
            Assert.AreEqual(0, fixture.CoreChest.GetInputAmount(PlateId), "Its plates left the chest.");

            // A Splitter's "+" covers (6,5) among its five cells, so the belt loses its ground.
            SplitterDefinition splitter = TestDataFactory.NewSplitter("splitter");
            fixture.Construction.SelectBuilding(splitter);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5, 5), Direction.North, out _));

            fixture.Simulate(30f);

            Assert.AreEqual(4, fixture.CoreChest.GetInputAmount(PlateId),
                "The overtaken belt's cost is repatriated, exactly like any other demolition.");
        }

        /// <summary>
        /// Dragging across a belt that already exists is a change of direction, not a demolition
        /// followed by a new chantier. Rebuilding it charged a second plate, destroyed the first
        /// silently, and dropped a working belt back to a blue silhouette until a robot came round -
        /// for a gesture whose whole intent was "this one goes that way now".
        /// </summary>
        [Test]
        public void DraggingOverABuiltBelt_TurnsItInPlace_WithoutSpendingOrRebuilding()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 4));
            var cell = new GridCoord(6, 5);

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(cell, Direction.East, out ConstructionSiteRuntime belt));
            fixture.Simulate(20f);
            Assert.IsTrue(belt.IsComplete);

            var built = (ConveyorRuntime)fixture.Grid.GetOccupant(cell);
            Assert.IsTrue(fixture.Construction.TryRedirectExistingConveyor(cell, Direction.North, out ConveyorRuntime redirected));

            Assert.AreSame(built, redirected, "The very same belt, re-pointed - never a replacement.");
            Assert.AreSame(built, fixture.Grid.GetOccupant(cell));
            Assert.AreEqual(Direction.North, built.Orientation.Rotation, "It runs north now.");
            Assert.AreEqual(0, fixture.Sites.Sites.Count, "No chantier is opened for a belt that already exists.");
            Assert.AreEqual(0, fixture.Construction.GetAvailableAmount(PlateId), "And the empty chest is no obstacle - nothing is spent.");
        }

        [Test]
        public void APendingBelt_IsNeverRedirected_ItIsStillSomebodysChantier()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 4));
            var cell = new GridCoord(6, 5);

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(cell, Direction.East, out _));

            Assert.IsFalse(fixture.Construction.TryRedirectExistingConveyor(cell, Direction.North, out _),
                "Turning it would leave its chantier building something nobody asked for.");
        }

        /// <summary>
        /// A robot on its way to fetch holds its claim in PendingAmount rather than in the site's
        /// reservations, so a site ending mid-trip has to drop that claim too. Left standing, it
        /// counts against TotalReserved forever: no reservation pass can see past it, so no site
        /// gets served, so no robot is ever reassigned - the stock is simply gone.
        /// </summary>
        [Test]
        public void ASiteEndingWhileARobotFetchesForIt_UnclaimsTheStockItWasSentFor()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(9, 9));

            fixture.Sites.Tick(TickSeconds);

            BuilderRobotRuntime dispatched = null;
            foreach (BuilderRobotRuntime robot in fixture.Sites.Robots)
            {
                if (robot.State == BuilderRobotState.MovingToSource) dispatched = robot;
            }
            Assert.IsNotNull(dispatched, "A robot has to be on its way to the chest for this to mean anything.");
            Assert.Greater(dispatched.PendingAmount, 0);

            Assert.IsTrue(fixture.Sites.CancelSite(site));

            Assert.AreEqual(4, fixture.Construction.GetAvailableAmount(PlateId),
                "A trip called off claims nothing - otherwise that stock stays unreachable for the rest of the game.");
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
