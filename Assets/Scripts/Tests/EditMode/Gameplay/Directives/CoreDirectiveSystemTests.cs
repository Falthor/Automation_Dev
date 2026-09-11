using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Directives;
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

namespace Game.Tests.EditMode.Gameplay.Directives
{
    /// <summary>
    /// What the Core asks for, and what happens when the player says yes: the material is reserved
    /// out of the same stock a construction site draws on, carried by the same robots, and the
    /// unlock lands only once the last unit has physically arrived.
    /// </summary>
    public class CoreDirectiveSystemTests
    {
        const string WireId = "copper_wire";
        const string PlateId = "iron_plate";
        const float TickSeconds = 0.2f;

        sealed class Fixture
        {
            public GridRuntime Grid;
            public TransportSystem Transport;
            public ConstructionSiteSystem Sites;
            public ResearchSystem Research;
            public CoreDirectiveSystem Directives;

            /// <summary>The hatch under the Core. Its contents fund construction and are <b>invisible to a directive</b> - see <see cref="TheCoreReserve_CannotSatisfyADirective"/>.</summary>
            public StorageRuntime CoreChest;

            /// <summary>An ordinary player-placed box, which is where a directive's material has to come from.</summary>
            public StorageRuntime Chest;

            public BuildingRuntime Core;

            /// <summary>Everything a construction site could claim, hatch included. What a directive may claim is narrower - and is the system's own business now, not something a caller passes in.</summary>
            public IReadOnlyDictionary<string, int> Stock => Sites.GetAvailableAggregate();

            public void Simulate(float seconds)
            {
                for (float elapsed = 0f; elapsed < seconds; elapsed += TickSeconds) Sites.Tick(TickSeconds);
            }
        }

        static CoreDirectiveDefinition NewDirective(ItemDefinition wire, ItemDefinition plate, ItemDefinition reward, ResearchDefinition grants)
        {
            var directive = ScriptableObject.CreateInstance<CoreDirectiveDefinition>();
            var so = new SerializedObject(directive);
            so.FindProperty("id").stringValue = "first_gear";

            SerializedProperty reqs = so.FindProperty("requirements");
            reqs.arraySize = 2;
            reqs.GetArrayElementAtIndex(0).FindPropertyRelative("item").objectReferenceValue = wire;
            reqs.GetArrayElementAtIndex(0).FindPropertyRelative("amount").intValue = 5;
            reqs.GetArrayElementAtIndex(1).FindPropertyRelative("item").objectReferenceValue = plate;
            reqs.GetArrayElementAtIndex(1).FindPropertyRelative("amount").intValue = 5;

            so.FindProperty("rewardItem").objectReferenceValue = reward;
            so.FindProperty("grants").objectReferenceValue = grants;
            so.ApplyModifiedPropertiesWithoutUndo();
            return directive;
        }

        static CoreDirectiveDatabase NewDatabase(params CoreDirectiveDefinition[] directives)
        {
            var database = ScriptableObject.CreateInstance<CoreDirectiveDatabase>();
            var so = new SerializedObject(database);
            SerializedProperty list = so.FindProperty("directives");
            list.arraySize = directives.Length;
            for (int i = 0; i < directives.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = directives[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return database;
        }

        /// <summary>
        /// <paramref name="wire"/> and <paramref name="plate"/> go into an <b>ordinary</b> box, because
        /// that is where a directive's material has to come from; <paramref name="reserveWire"/> and
        /// <paramref name="reservePlate"/> go into the Core's hatch, which a directive may not touch.
        ///
        /// The two used to be one: everything was seeded into the hatch and the tests handed
        /// <c>GetAvailableAggregate()</c> to Validate, so they accepted on stock the haul was then
        /// forbidden to claim. Seven tests went red the day that rule was posed and stayed red because
        /// nothing here distinguished the two containers.
        /// </summary>
        static Fixture NewFixture(int wire, int plate, out ResearchDefinition gate,
            int reserveWire = 0, int reservePlate = 0)
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);
            var sites = new ConstructionSiteSystem(transport, grid, new NotificationSystem(), Vector2.zero);
            var research = new ResearchSystem(new ComputeSystem());

            StorageDefinition coreChestDefinition = TestDataFactory.NewStorage(ConstructionSiteSystem.CoreStorageDefinitionId, 6, 200, rejectsConveyorInput: true);
            var coreChest = new StorageRuntime(coreChestDefinition, new GridCoord(0, 0), Direction.North);
            grid.SetOccupantFootprint(coreChest.Cell, coreChestDefinition.FootprintSize, coreChest);
            transport.Register(coreChest);
            if (reserveWire > 0) coreChest.SeedInitialContents(WireId, reserveWire);
            if (reservePlate > 0) coreChest.SeedInitialContents(PlateId, reservePlate);

            StorageDefinition chestDefinition = TestDataFactory.NewStorage("storage_box", 6, 200);
            var chest = new StorageRuntime(chestDefinition, new GridCoord(4, 0), Direction.North);
            grid.SetOccupantFootprint(chest.Cell, chestDefinition.FootprintSize, chest);
            transport.Register(chest);
            if (wire > 0) chest.SeedInitialContents(WireId, wire);
            if (plate > 0) chest.SeedInitialContents(PlateId, plate);

            // Any building will do as the destination - what matters is that the robots have
            // somewhere to carry to. A Storage stands in for the Core here.
            StorageDefinition coreDefinition = TestDataFactory.NewStorage("core", 6, 200);
            var core = new StorageRuntime(coreDefinition, new GridCoord(9, 9), Direction.North);
            grid.SetOccupantFootprint(core.Cell, coreDefinition.FootprintSize, core);

            gate = TestDataFactory.NewResearch("core_gear_unlock", 0f);
            CoreDirectiveDefinition directive = NewDirective(
                TestDataFactory.NewItem(WireId), TestDataFactory.NewItem(PlateId), TestDataFactory.NewItem("Gear"), gate);

            return new Fixture
            {
                Grid = grid,
                Transport = transport,
                Sites = sites,
                Research = research,
                Core = core,
                CoreChest = coreChest,
                Chest = chest,
                Directives = new CoreDirectiveSystem(NewDatabase(directive), sites, research)
            };
        }

        [Test]
        public void ValidateIsRefused_UntilEveryRequirementIsInStock()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 4, out _);

            Assert.IsFalse(fixture.Directives.CanValidate(), "Four plates of the five asked for: the button stays grey.");
            Assert.IsFalse(fixture.Directives.Validate(fixture.Core),
                "And pressing it anyway does nothing - enabled and accepted are the same condition.");

            fixture.Chest.SeedInitialContents(PlateId, 1);

            Assert.IsTrue(fixture.Directives.CanValidate());
        }

        /// <summary>
        /// <b>The rule the seven red tests were hiding.</b> Material already sitting in the hatch under
        /// the Core has not been brought anywhere - it started there - so it cannot satisfy a request to
        /// bring the Core something. Without this the opening directive is answered by the starting
        /// stock alone, with no machine built.
        ///
        /// This had no green coverage at all: the suite validated against the wide aggregate, which
        /// includes the hatch, so it was asserting the opposite of the rule and failing further down for
        /// a reason nobody read.
        /// </summary>
        [Test]
        public void TheCoreReserve_CannotSatisfyADirective()
        {
            Fixture fixture = NewFixture(wire: 0, plate: 0, out _,
                reserveWire: 50, reservePlate: 50);

            Assert.AreEqual(50, fixture.Stock[WireId], "Precondition: a construction site could still spend it.");

            Assert.IsFalse(fixture.Directives.CanValidate(),
                "Fifty of each in the hatch, and the Core still asks for five: the reserve is not an answer.");
            Assert.IsFalse(fixture.Directives.Validate(fixture.Core));

            fixture.Chest.SeedInitialContents(WireId, 5);
            fixture.Chest.SeedInitialContents(PlateId, 5);

            Assert.IsTrue(fixture.Directives.CanValidate(), "Brought into an ordinary box, the same material counts.");
        }

        /// <summary>
        /// And the haul respects the same boundary once it is running: the ordinary box is drained, the
        /// hatch is not touched. Checked separately from the decision above, because deciding and
        /// reserving are two passes and it is their disagreement that broke the suite.
        /// </summary>
        [Test]
        public void TheHaul_DrainsTheBox_AndLeavesTheCoreReserveAlone()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out _,
                reserveWire: 40, reservePlate: 40);

            Assert.IsTrue(fixture.Directives.Validate(fixture.Core));
            fixture.Simulate(30f);

            Assert.AreEqual(0, fixture.Chest.GetInputAmount(WireId), "Five held, five asked for.");
            Assert.AreEqual(0, fixture.Chest.GetInputAmount(PlateId));
            Assert.AreEqual(40, fixture.CoreChest.GetInputAmount(WireId), "The reserve is untouched.");
            Assert.AreEqual(40, fixture.CoreChest.GetInputAmount(PlateId));
        }

        [Test]
        public void Validating_SendsTheRobots_AndGrantsTheUnlockOnlyOnceEverythingHasLanded()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out ResearchDefinition gate);

            Assert.IsTrue(fixture.Directives.Validate(fixture.Core));
            Assert.IsTrue(fixture.Directives.IsDelivering, "The haul is in flight.");
            Assert.IsFalse(fixture.Research.IsUnlocked(gate.Id), "Nothing is granted at the moment of validating.");

            fixture.Simulate(0.4f);
            Assert.IsFalse(fixture.Research.IsUnlocked(gate.Id), "Nor while the robots are still travelling.");

            fixture.Simulate(30f);

            Assert.IsTrue(fixture.Research.IsUnlocked(gate.Id), "It lands with the last unit.");
            Assert.IsNull(fixture.Directives.Current, "The only directive is done, so the Core has nothing left to show.");
            Assert.IsFalse(fixture.Directives.IsDelivering);
        }

        /// <summary>
        /// The material really leaves the chests: a directive is a hand-over, not a display. Ten
        /// units were asked for and ten are gone.
        /// </summary>
        [Test]
        public void TheMaterialIsActuallyTakenFromTheContainers()
        {
            Fixture fixture = NewFixture(wire: 8, plate: 8, out _);

            Assert.IsTrue(fixture.Directives.Validate(fixture.Core));
            fixture.Simulate(30f);

            Assert.AreEqual(3, fixture.Chest.GetInputAmount(WireId), "Eight held, five asked for.");
            Assert.AreEqual(3, fixture.Chest.GetInputAmount(PlateId));
        }

        /// <summary>
        /// A validated directive claims stock on the same footing as a construction site. Without
        /// that, the same five plates would be promised to both and one of them would be waiting on
        /// material that no longer exists anywhere.
        /// </summary>
        [Test]
        public void AValidatedDirective_TakesItsMaterialOutOfWhatIsAvailable()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out _);

            Assert.AreEqual(5, fixture.Stock[PlateId]);

            Assert.IsTrue(fixture.Directives.Validate(fixture.Core));

            Assert.IsFalse(fixture.Stock.ContainsKey(PlateId),
                "Every plate is spoken for by the directive, so nothing else may count on one.");
        }

        [Test]
        public void ASecondValidation_IsRefusedWhileOneIsInFlight()
        {
            Fixture fixture = NewFixture(wire: 20, plate: 20, out _);

            Assert.IsTrue(fixture.Directives.Validate(fixture.Core));
            Assert.IsFalse(fixture.Directives.CanValidate(), "The Core asks for one thing at a time.");
            Assert.IsFalse(fixture.Directives.Validate(fixture.Core));
        }

        [Test]
        public void CaptureRestore_KeepsWhichDirectiveIsCurrent()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out _);
            Assert.IsNotNull(fixture.Directives.Current);

            fixture.Directives.Validate(fixture.Core);
            fixture.Simulate(30f);
            Assert.IsNull(fixture.Directives.Current, "Precondition: it is done.");

            Newtonsoft.Json.Linq.JObject captured = fixture.Directives.CaptureState();

            Fixture reloaded = NewFixture(wire: 5, plate: 5, out _);
            reloaded.Directives.RestoreState(captured);

            Assert.IsNull(reloaded.Directives.Current, "A finished directive stays finished across a save.");
        }

        [Test]
        public void Restore_OnAnOlderSaveWithNoDirectiveKey_StartsAtTheFirstOne()
        {
            Fixture fixture = NewFixture(wire: 0, plate: 0, out _);

            Assert.DoesNotThrow(() => fixture.Directives.RestoreState(null));
            Assert.IsNotNull(fixture.Directives.Current, "Absent means 'the first one', which is where a new game starts.");
        }
    }
}
