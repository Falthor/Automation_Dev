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
            public StorageRuntime CoreChest;
            public BuildingRuntime Core;

            public IReadOnlyDictionary<string, int> Stock => Sites.GetAvailableAggregate();

            public void Simulate(float seconds)
            {
                for (float elapsed = 0f; elapsed < seconds; elapsed += TickSeconds) Sites.Tick(TickSeconds);
            }
        }

        static CoreDirectiveDefinition NewDirective(ItemDefinition wire, ItemDefinition plate, ItemDefinition reward, ResearchDefinition grants, bool unlocksResearchMenu = false)
        {
            var directive = ScriptableObject.CreateInstance<CoreDirectiveDefinition>();
            var so = new SerializedObject(directive);
            so.FindProperty("id").stringValue = "first_gear";
            so.FindProperty("unlocksResearchMenu").boolValue = unlocksResearchMenu;

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

        static Fixture NewFixture(int wire, int plate, out ResearchDefinition gate)
            => NewFixture(wire, plate, out gate, unlocksResearchMenu: false);

        static Fixture NewFixture(int wire, int plate, out ResearchDefinition gate, bool unlocksResearchMenu)
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);
            var sites = new ConstructionSiteSystem(transport, grid, new NotificationSystem(), Vector2.zero);
            var research = new ResearchSystem(new ComputeSystem());

            StorageDefinition chestDefinition = TestDataFactory.NewStorage(ConstructionSiteSystem.CoreStorageDefinitionId, 6, 200, rejectsConveyorInput: true);
            var chest = new StorageRuntime(chestDefinition, new GridCoord(0, 0), Direction.North);
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
                TestDataFactory.NewItem(WireId), TestDataFactory.NewItem(PlateId), TestDataFactory.NewItem("Gear"), gate, unlocksResearchMenu);

            return new Fixture
            {
                Grid = grid,
                Transport = transport,
                Sites = sites,
                Research = research,
                Core = core,
                CoreChest = chest,
                Directives = new CoreDirectiveSystem(NewDatabase(directive), sites, research)
            };
        }

        [Test]
        public void ValidateIsRefused_UntilEveryRequirementIsInStock()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 4, out _);

            Assert.IsFalse(fixture.Directives.CanValidate(fixture.Stock), "Four plates of the five asked for: the button stays grey.");
            Assert.IsFalse(fixture.Directives.Validate(fixture.Stock, fixture.Core),
                "And pressing it anyway does nothing - enabled and accepted are the same condition.");

            fixture.CoreChest.SeedInitialContents(PlateId, 1);

            Assert.IsTrue(fixture.Directives.CanValidate(fixture.Stock));
        }

        [Test]
        public void Validating_SendsTheRobots_AndGrantsTheUnlockOnlyOnceEverythingHasLanded()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out ResearchDefinition gate);

            Assert.IsTrue(fixture.Directives.Validate(fixture.Stock, fixture.Core));
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

            Assert.IsTrue(fixture.Directives.Validate(fixture.Stock, fixture.Core));
            fixture.Simulate(30f);

            Assert.AreEqual(3, fixture.CoreChest.GetInputAmount(WireId), "Eight held, five asked for.");
            Assert.AreEqual(3, fixture.CoreChest.GetInputAmount(PlateId));
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

            Assert.IsTrue(fixture.Directives.Validate(fixture.Stock, fixture.Core));

            Assert.IsFalse(fixture.Stock.ContainsKey(PlateId),
                "Every plate is spoken for by the directive, so nothing else may count on one.");
        }

        [Test]
        public void ASecondValidation_IsRefusedWhileOneIsInFlight()
        {
            Fixture fixture = NewFixture(wire: 20, plate: 20, out _);

            Assert.IsTrue(fixture.Directives.Validate(fixture.Stock, fixture.Core));
            Assert.IsFalse(fixture.Directives.CanValidate(fixture.Stock), "The Core asks for one thing at a time.");
            Assert.IsFalse(fixture.Directives.Validate(fixture.Stock, fixture.Core));
        }

        [Test]
        public void CaptureRestore_KeepsWhichDirectiveIsCurrent()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out _);
            Assert.IsNotNull(fixture.Directives.Current);

            fixture.Directives.Validate(fixture.Stock, fixture.Core);
            fixture.Simulate(30f);
            Assert.IsNull(fixture.Directives.Current, "Precondition: it is done.");

            Newtonsoft.Json.Linq.JObject captured = fixture.Directives.CaptureState();

            Fixture reloaded = NewFixture(wire: 5, plate: 5, out _);
            reloaded.Directives.RestoreState(captured);

            Assert.IsNull(reloaded.Directives.Current, "A finished directive stays finished across a save.");
        }

        /// <summary>
        /// Research is not something the run starts with: it is the first directive's own reward,
        /// and until that directive is done the Top Bar card and the Bottom Nav icon have nothing to
        /// show. Validating is not enough - the menu arrives with the last crate, like every other
        /// thing a directive grants.
        /// </summary>
        [Test]
        public void TheResearchMenu_ArrivesWithTheDirectiveThatGrantsIt_NotBefore()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out _, unlocksResearchMenu: true);

            Assert.IsFalse(fixture.Directives.IsResearchMenuUnlocked, "A new run has no Research menu.");

            Assert.IsTrue(fixture.Directives.Validate(fixture.Stock, fixture.Core));
            Assert.IsFalse(fixture.Directives.IsResearchMenuUnlocked, "Nor while the robots are still carrying it.");

            fixture.Simulate(30f);

            Assert.IsTrue(fixture.Directives.IsResearchMenuUnlocked);
        }

        /// <summary>
        /// The development bypass opens the menu and touches nothing else. What is being pinned is the
        /// "nothing else": a shortcut that nudged the directive index would silently hand the player a
        /// directive's reward and skip its ask, and the run would no longer be the run being debugged.
        /// </summary>
        [Test]
        public void ForcingTheResearchMenu_OpensIt_WithoutCompletingTheDirectiveThatGrantsIt()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out _, unlocksResearchMenu: true);
            CoreDirectiveDefinition asked = fixture.Directives.Current;

            fixture.Directives.ResearchMenuForcedOpen = true;

            Assert.IsTrue(fixture.Directives.IsResearchMenuUnlocked);
            Assert.AreSame(asked, fixture.Directives.Current, "The Core still asks for the same directive.");
            Assert.AreEqual(1, fixture.Directives.CurrentNumber, "and it is still the first one.");
        }

        /// <summary>A directive that grants no menu never opens one, however many of them complete.</summary>
        [Test]
        public void ADirectiveThatDoesNotGrantTheMenu_LeavesItClosed()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out _, unlocksResearchMenu: false);

            fixture.Directives.Validate(fixture.Stock, fixture.Core);
            fixture.Simulate(30f);

            Assert.IsNull(fixture.Directives.Current, "Precondition: it completed.");
            Assert.IsFalse(fixture.Directives.IsResearchMenuUnlocked);
        }

        /// <summary>
        /// The menu is derived from which directives are done, so the index the save already carries
        /// restores it - there is nothing else to persist, and nothing that could disagree with it.
        /// </summary>
        [Test]
        public void TheResearchMenu_SurvivesASave_ThroughTheDirectiveIndexAlone()
        {
            Fixture fixture = NewFixture(wire: 5, plate: 5, out _, unlocksResearchMenu: true);
            fixture.Directives.Validate(fixture.Stock, fixture.Core);
            fixture.Simulate(30f);
            Assert.IsTrue(fixture.Directives.IsResearchMenuUnlocked, "Precondition.");

            Newtonsoft.Json.Linq.JObject captured = fixture.Directives.CaptureState();

            Fixture reloaded = NewFixture(wire: 0, plate: 0, out _, unlocksResearchMenu: true);
            Assert.IsFalse(reloaded.Directives.IsResearchMenuUnlocked, "Precondition: a fresh system starts closed.");

            reloaded.Directives.RestoreState(captured);

            Assert.IsTrue(reloaded.Directives.IsResearchMenuUnlocked);
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
