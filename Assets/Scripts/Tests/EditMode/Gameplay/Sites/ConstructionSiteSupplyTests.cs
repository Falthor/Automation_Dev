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

using SupplyLine = Game.Gameplay.Sites.ConstructionSiteRuntime.SupplyLine;

namespace Game.Tests.EditMode.Gameplay.Sites
{
    /// <summary>
    /// The reservation counter: a site's bill of materials in the three states a player asks about -
    /// arrived, reserved, missing.
    ///
    /// What is really being tested here is that <b>reserved</b> is a state of its own. Delivered and
    /// cost alone can only say "10 of 15", which reads the same for a site whose material is secured
    /// and for one forgotten because nothing produces what it needs. Those two situations are what
    /// the player is trying to tell apart, and only the reservation total separates them.
    ///
    /// Reserved says nothing about movement, deliberately: it is a claim on <b>stock</b>, and it
    /// holds whether or not a robot has been dispatched. Which site a robot is actually walking
    /// toward is a separate question, answered by the panel's service line and tested next to it.
    /// </summary>
    public class ConstructionSiteSupplyTests
    {
        const string PlateId = "iron_plate";
        const string GearId = "gear";
        const float TickSeconds = 0.2f;

        sealed class Fixture
        {
            public GridRuntime Grid;
            public TransportSystem Transport;
            public ConstructionSiteSystem Sites;
            public ConstructionService Construction;
            public ItemDefinition Plate;
            public ItemDefinition Gear;
            public StorageRuntime CoreChest;

            public void Simulate(float seconds)
            {
                for (float elapsed = 0f; elapsed < seconds; elapsed += TickSeconds)
                {
                    Sites.Tick(TickSeconds);
                }
            }
        }

        static Fixture NewFixture(int plates = 0, int gears = 0)
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
                Sites = sites,
                Construction = construction,
                Plate = TestDataFactory.NewItem(PlateId),
                Gear = TestDataFactory.NewItem(GearId)
            };

            StorageDefinition chestDefinition = TestDataFactory.NewStorage(
                ConstructionSiteSystem.CoreStorageDefinitionId, 6, 200, rejectsConveyorInput: true);
            fixture.CoreChest = new StorageRuntime(chestDefinition, new GridCoord(0, 0), Direction.North);
            grid.SetOccupantFootprint(fixture.CoreChest.Cell, chestDefinition.FootprintSize, fixture.CoreChest);
            transport.Register(fixture.CoreChest);

            if (plates > 0) fixture.CoreChest.SeedInitialContents(PlateId, plates);
            if (gears > 0) fixture.CoreChest.SeedInitialContents(GearId, gears);

            return fixture;
        }

        static ConstructionSiteRuntime PlaceSite(Fixture fixture, BuildingDefinition definition, GridCoord cell)
        {
            fixture.Construction.SelectBuilding(definition);
            Assert.IsTrue(fixture.Construction.TryPlace(cell, Direction.North, out ConstructionSiteRuntime site));
            return site;
        }

        static readonly List<SupplyLine> Lines = new List<SupplyLine>();

        static SupplyLine LineFor(ConstructionSiteRuntime site, string itemId)
        {
            site.GetSupply(Lines);
            foreach (SupplyLine line in Lines)
            {
                if (line.ItemId == itemId) return line;
            }

            Assert.Fail("No supply line for " + itemId);
            return default;
        }

        /// <summary>
        /// The distinction the whole counter exists for. Materials earmarked in a container but not
        /// yet collected have not arrived - counting them as delivered would claim the site is fed
        /// when nothing has moved - but they are not missing either, because they are spoken for and
        /// no other site can take them.
        /// </summary>
        [Test]
        public void MaterialsReservedButNotYetCollected_ReadAsReserved_NeitherArrivedNorMissing()
        {
            Fixture fixture = NewFixture(plates: 4);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));

            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));

            SupplyLine line = LineFor(site, PlateId);

            Assert.AreEqual(4, line.Total);
            Assert.AreEqual(0, line.Delivered, "Placing a site moves nothing physically.");
            Assert.AreEqual(4, line.Reserved, "But it does claim what it needs, and that claim is what 'reserved' reports.");
            Assert.AreEqual(0, line.Missing, "Nothing is missing: the plates exist and are spoken for.");
            Assert.IsFalse(line.IsStalled);
        }

        /// <summary>
        /// One ingredient short is one ingredient too many: the gate is all-or-nothing per building,
        /// so a Foundry whose plates exist but whose gears do not cannot be placed at all. It does
        /// not open as a half-supplied site.
        ///
        /// This is what makes a placed site's missing count zero in ordinary play - the missing state
        /// is now a property of a refused gesture rather than of a queued chantier.
        /// </summary>
        [Test]
        public void OneMissingIngredient_RefusesTheWholePlacement_RatherThanOpeningAHalfSuppliedSite()
        {
            Fixture fixture = NewFixture(plates: 4, gears: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", 0, 0, false, 0f, (fixture.Plate, 4), (fixture.Gear, 3));

            fixture.Construction.SelectBuilding(costly);
            var cell = new GridCoord(5, 5);

            Assert.IsFalse(fixture.Construction.CanAfford(costly), "The gears do not exist anywhere.");
            Assert.IsFalse(fixture.Construction.TryPlace(cell, Direction.North, out ConstructionSiteRuntime site));
            Assert.IsNull(site);
            Assert.AreEqual(0, fixture.Sites.Sites.Count);

            // And the plates it would have taken are still free for something else.
            Assert.AreEqual(4, fixture.Sites.GetAvailableAggregate()[PlateId],
                "A refused placement reserves nothing, so it cannot strand material behind it.");
        }

        /// <summary>
        /// The state the counter was built to show, now reached the only way it still can: a site is
        /// placed with its whole bill reserved, so nothing is ever missing on a queued chantier.
        /// </summary>
        [Test]
        public void APlacedSite_HasItsWholeBillReserved_AndNothingMissing()
        {
            Fixture fixture = NewFixture(plates: 4, gears: 3);
            StorageDefinition costly = TestDataFactory.NewStorage("target", 0, 0, false, 0f, (fixture.Plate, 4), (fixture.Gear, 3));

            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));

            SupplyLine plates = LineFor(site, PlateId);
            SupplyLine gears = LineFor(site, GearId);

            Assert.AreEqual(4, plates.Reserved);
            Assert.AreEqual(3, gears.Reserved);
            Assert.AreEqual(0, plates.Missing);
            Assert.AreEqual(0, gears.Missing);
            Assert.IsFalse(plates.IsStalled);
            Assert.IsFalse(gears.IsStalled);

            Assert.IsTrue(site.IsFullySupplied, "Which is now true of every site that exists at all.");
        }

        /// <summary>A promise becomes an arrival: the same units cross from one column to the other, never appearing in both or neither.</summary>
        [Test]
        public void OnDelivery_UnitsCrossFromReservedIntoArrived()
        {
            Fixture fixture = NewFixture(plates: 4);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));

            Assert.AreEqual(0, LineFor(site, PlateId).Delivered);
            Assert.AreEqual(4, LineFor(site, PlateId).Reserved);

            fixture.Simulate(12f);

            SupplyLine line = LineFor(site, PlateId);
            Assert.AreEqual(4, line.Delivered, "Everything promised has now physically landed.");
            Assert.AreEqual(0, line.Reserved, "And nothing is still on its way.");
            Assert.AreEqual(0, line.Missing);
            Assert.IsTrue(site.IsComplete);
        }

        /// <summary>
        /// The three states are one statement about one ingredient, so they must account for the
        /// whole bill at every instant - including mid-flight, where a robot is carrying part of it
        /// and the rest is still sitting in the chest.
        /// </summary>
        [Test]
        public void TheThreeStates_AlwaysAccountForTheWholeBill()
        {
            Fixture fixture = NewFixture(plates: 8, gears: 6);
            StorageDefinition costly = TestDataFactory.NewStorage("target", 0, 0, false, 0f, (fixture.Plate, 8), (fixture.Gear, 6));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(6, 6));

            for (int step = 0; step < 40; step++)
            {
                site.GetSupply(Lines);
                foreach (SupplyLine line in Lines)
                {
                    Assert.AreEqual(line.Total, line.Delivered + line.Reserved + line.Missing,
                        $"step {step}, {line.ItemId}: {line.Delivered} + {line.Reserved} + {line.Missing} != {line.Total}");
                    Assert.GreaterOrEqual(line.Delivered, 0);
                    Assert.GreaterOrEqual(line.Reserved, 0);
                    Assert.GreaterOrEqual(line.Missing, 0);
                }

                fixture.Simulate(0.6f);
            }

            Assert.IsTrue(site.IsComplete, "Precondition: the run actually got somewhere.");
        }

        /// <summary>
        /// Production catching up turns a refused placement into an allowed one. This is the
        /// replacement for what used to be a stalled site recovering: the waiting now happens before
        /// the placement rather than after it, so the same player experience - "I could not build
        /// this, now I can" - is a property of the gate instead of a property of a queued chantier.
        /// </summary>
        [Test]
        public void WhenProductionCatchesUp_ThePlacementBecomesAllowed()
        {
            Fixture fixture = NewFixture(plates: 4, gears: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", 0, 0, false, 0f, (fixture.Plate, 4), (fixture.Gear, 3));

            fixture.Construction.SelectBuilding(costly);
            var cell = new GridCoord(5, 5);
            Assert.IsFalse(fixture.Construction.TryPlace(cell, Direction.North, out _), "No gears anywhere yet.");

            fixture.CoreChest.SeedInitialContents(GearId, 3);

            Assert.IsTrue(fixture.Construction.TryPlace(cell, Direction.North, out ConstructionSiteRuntime site),
                "The gears exist now, so the same gesture is allowed.");
            Assert.AreEqual(3, LineFor(site, GearId).Reserved);
            Assert.AreEqual(0, LineFor(site, GearId).Missing);
        }

        /// <summary>
        /// The rows come back in the order they were costed, every time. A panel refreshed every
        /// frame off a Dictionary's enumeration order could reshuffle its rows under the cursor, and
        /// nothing in the Dictionary contract forbids it.
        /// </summary>
        [Test]
        public void TheRowOrder_FollowsTheBill_AndDoesNotMoveBetweenReads()
        {
            Fixture fixture = NewFixture(plates: 4, gears: 3);
            StorageDefinition costly = TestDataFactory.NewStorage("target", 0, 0, false, 0f, (fixture.Plate, 4), (fixture.Gear, 3));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));

            site.GetSupply(Lines);
            Assert.AreEqual(2, Lines.Count);
            Assert.AreEqual(PlateId, Lines[0].ItemId);
            Assert.AreEqual(GearId, Lines[1].ItemId);

            var firstRead = new List<SupplyLine>(Lines);

            for (int step = 0; step < 15; step++)
            {
                fixture.Simulate(0.6f);

                site.GetSupply(Lines);
                Assert.AreEqual(firstRead.Count, Lines.Count);
                for (int i = 0; i < Lines.Count; i++)
                {
                    Assert.AreEqual(firstRead[i].ItemId, Lines[i].ItemId, "row " + i + " moved at step " + step);
                }
            }
        }

        /// <summary>
        /// Both a finished site and a cancelled one leave the queue, and the panel has to tell them
        /// apart: cancelled, there is nothing left and it closes; finished, it hands over to the
        /// panel of the building that has just appeared under the player's cursor. IsComplete is the
        /// discriminator it keys off, so this pins it.
        /// </summary>
        [Test]
        public void ACancelledSite_IsNotComplete_UnlikeAFinishedOne()
        {
            Fixture finished = NewFixture(plates: 4);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (finished.Plate, 4));
            ConstructionSiteRuntime completedSite = PlaceSite(finished, costly, new GridCoord(5, 5));

            finished.Simulate(12f);

            Assert.IsTrue(completedSite.IsComplete);
            Assert.AreEqual(0, finished.Sites.Sites.Count, "A finished site leaves the queue...");

            Fixture abandoned = NewFixture(plates: 4);
            StorageDefinition sameCost = TestDataFactory.NewStorage("target", cost: (abandoned.Plate, 4));
            ConstructionSiteRuntime cancelledSite = PlaceSite(abandoned, sameCost, new GridCoord(5, 5));

            Assert.IsTrue(abandoned.Sites.CancelPendingSegment(cancelledSite.Segments[0]));

            Assert.AreEqual(0, abandoned.Sites.Sites.Count, "...and so does a cancelled one.");
            Assert.IsFalse(cancelledSite.IsComplete,
                "But a cancelled site built none of the segments it was given, and a site with no "
                + "segment left has not 'completed' them all - it has nothing. That arithmetic trap "
                + "is what IsComplete guards against, and it is what separates 'hand over to the new "
                + "building' from 'there is nothing there any more'.");
        }

        /// <summary>
        /// Everything one container has promised: what sites still hold as reservations, plus what
        /// robots have been dispatched to fetch from it and not yet picked up. The two together are
        /// what may never exceed the container's contents.
        /// </summary>
        static int ClaimedIn(Fixture fixture, StorageRuntime container, string itemId)
        {
            int total = 0;

            foreach (ConstructionSiteRuntime site in fixture.Sites.Sites)
            {
                total += site.ReservedIn(container, itemId);
            }

            foreach (BuilderRobotRuntime robot in fixture.Sites.Robots)
            {
                if (robot.PendingAmount <= 0) continue;
                if (!ReferenceEquals(robot.SourceContainer, container) || robot.PendingItemId != itemId) continue;
                total += robot.PendingAmount;
            }

            return total;
        }

        /// <summary>
        /// Four sites placed at once with only two robots all report material on its way, and that is
        /// correct. A reservation is a claim on <b>stock</b>, not a robot assignment: the plates are
        /// earmarked in the chest, no other site can take them, and the two robots will work through
        /// the queue oldest-first. Tying reservations to robot cargo instead would leave the third
        /// and fourth sites reading "missing" while the material for them sits in the chest with
        /// their name on it - which is exactly the "delivered 10 of 15" ambiguity the counter exists
        /// to remove.
        /// </summary>
        [Test]
        public void MoreSitesThanRobots_AllShowMaterialComing_WhenTheStockCoversThemAll()
        {
            Fixture fixture = NewFixture(plates: 20);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 5));

            var sites = new List<ConstructionSiteRuntime>();
            for (int i = 0; i < 4; i++) sites.Add(PlaceSite(fixture, costly, new GridCoord(5 + 3 * i, 5)));

            Assert.AreEqual(2, fixture.Sites.Robots.Count, "Precondition: fewer robots than sites.");

            foreach (ConstructionSiteRuntime site in sites)
            {
                SupplyLine line = LineFor(site, PlateId);
                Assert.AreEqual(5, line.Reserved, "Every site's bill is claimed, robots or not.");
                Assert.AreEqual(0, line.Missing);
            }

            Assert.AreEqual(20, ClaimedIn(fixture, fixture.CoreChest, PlateId),
                "And the four claims add up to exactly what the chest holds - no unit promised twice.");
        }

        /// <summary>
        /// The invariant that actually protects the counter: no container ever promises more than it
        /// holds, at any instant of a full run, however many sites are queued against it.
        ///
        /// The window this was written for: a robot releases its site's reservation when it is
        /// dispatched but only takes the items on arrival, so mid-trip the units sit in the chest
        /// claimed by no reservation at all. Counting reservations alone offered them to the next
        /// site, and one stack got promised to two chantiers - the first robot emptied it and the
        /// second site went on showing material coming that existed nowhere.
        /// </summary>
        [Test]
        public void NoContainer_EverPromisesMoreThanItHolds_AtAnyPointOfARun()
        {
            Fixture fixture = NewFixture(plates: 14);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 5));

            // As many as the stock covers - the gate refuses the rest, which is itself the first
            // half of the invariant. Two here, and the fourteenth plate stays unclaimed.
            fixture.Construction.SelectBuilding(costly);
            int placed = 0;
            for (int i = 0; i < 5; i++)
            {
                if (fixture.Construction.TryPlace(new GridCoord(5 + 3 * i, 5), Direction.North, out _)) placed++;
            }

            Assert.AreEqual(2, placed, "Fourteen plates buy two sites of five, never three.");

            for (int step = 0; step < 120; step++)
            {
                int held = fixture.CoreChest.GetInputAmount(PlateId);
                int claimed = ClaimedIn(fixture, fixture.CoreChest, PlateId);

                Assert.LessOrEqual(claimed, held,
                    $"step {step}: the chest holds {held} plates but has promised {claimed}. "
                    + "Two sites are counting on the same physical stack.");

                fixture.Simulate(0.2f);
            }
        }

        /// <summary>
        /// The same shortage, stated the way the gate now expresses it: what used to become a queued
        /// site reporting missing material is refused up front instead. Every site that exists has
        /// its whole bill, so a missing count on a queued chantier is not a state the player can
        /// reach by placing things.
        /// </summary>
        [Test]
        public void WhenTheStockRunsShort_TheExtraPlacementsAreRefused_AndEverySiteThatExistsIsWhole()
        {
            Fixture fixture = NewFixture(plates: 14);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 5));

            fixture.Construction.SelectBuilding(costly);
            var sites = new List<ConstructionSiteRuntime>();
            for (int i = 0; i < 5; i++)
            {
                if (fixture.Construction.TryPlace(new GridCoord(5 + 3 * i, 5), Direction.North, out ConstructionSiteRuntime site))
                {
                    sites.Add(site);
                }
            }

            Assert.AreEqual(2, sites.Count, "Fourteen plates buy two sites of five; the other three are refused.");

            foreach (ConstructionSiteRuntime site in sites)
            {
                Assert.AreEqual(0, LineFor(site, PlateId).Missing,
                    "No site is ever opened short, so none of them reports missing material.");
            }

            Assert.AreEqual(4, fixture.Sites.GetAvailableAggregate()[PlateId],
                "And the four plates that could not buy a third site stay claimable for something else.");
        }

        /// <summary>
        /// A dragged conveyor run is one site with many segments, so its bill is the sum of theirs -
        /// the panel speaks about the whole run, which is what the player placed.
        /// </summary>
        [Test]
        public void ADraggedRun_ReportsOneBillForTheWholeSite()
        {
            Fixture fixture = NewFixture(plates: 12);
            ConveyorDefinition belt = TestDataFactory.NewConveyor("belt", (fixture.Plate, 1));

            fixture.Construction.SelectBuilding(belt);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(4, 4), Direction.East, out ConstructionSiteRuntime site));
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5, 4), Direction.East, out ConstructionSiteRuntime same, site));
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(6, 4), Direction.East, out _, site));
            Assert.AreSame(site, same, "A drag extends one site rather than starting a new one per cell.");

            SupplyLine line = LineFor(site, PlateId);
            Assert.AreEqual(3, line.Total, "Three segments at one plate each is one bill of three.");
            Assert.AreEqual(3, site.Segments.Count);
        }
    }
}
