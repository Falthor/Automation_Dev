using System;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Research;
using Game.Tests.EditMode.TestSupport;
using Game.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.UI
{
    /// <summary>
    /// What the two menus list, held apart from the panels themselves so it can be checked without a
    /// UIDocument - same approach as ConstructionSitePanelFormatTests.
    ///
    /// Both rules answer the same question - "does this exist yet, as far as the player is
    /// concerned" - and both are about the menu offering nothing it cannot deliver.
    /// </summary>
    public class MenuVisibilityTests
    {
        /// <summary>A ResearchSystem with exactly these researches already completed.</summary>
        static ResearchSystem Unlocked(params ResearchDefinition[] unlocked)
        {
            var research = new ResearchSystem(new ComputeSystem());
            var unlockedIds = new string[unlocked.Length];
            for (int i = 0; i < unlocked.Length; i++) unlockedIds[i] = unlocked[i].Id;
            research.RestoreState(null, 0f, Array.Empty<ResearchDefinition>(), unlockedIds);
            return research;
        }

        // --- Research list ---

        /// <summary>
        /// The introduction's menu must not advertise what comes after it. Everything behind the
        /// Datacenter - the bays, the advanced foundry, memory allocation, extended bandwidth -
        /// stays out of the list until that milestone is done.
        /// </summary>
        [Test]
        public void AResearchBehindAMilestone_IsNotListedUntilThatMilestoneIsDone()
        {
            ResearchDefinition datacenter = TestDataFactory.NewResearch("datacenter", 1000f);
            ResearchDefinition bays = TestDataFactory.SetRevealedBy(
                TestDataFactory.NewResearch("datacenter_bay_1", 2000f, 1_000_000f, datacenter), datacenter);

            Assert.IsFalse(ResearchPanelController.IsRevealed(bays, Unlocked()),
                "Before the Datacenter, the menu must not mention what follows it.");
            Assert.IsTrue(ResearchPanelController.IsRevealed(bays, Unlocked(datacenter)),
                "Once it is done, they appear.");
        }

        /// <summary>
        /// The distinction the field exists for: a locked research is normally shown anyway. The
        /// visible chain up to the Datacenter is what tells the player where the introduction is
        /// going, so "cannot start it yet" must never be read as "hide it".
        /// </summary>
        [Test]
        public void AResearchWithUnmetPrerequisites_IsStillListed_WhenItDeclaresNoMilestone()
        {
            ResearchDefinition circuitBoard = TestDataFactory.NewResearch("circuit_board", 500f);
            ResearchDefinition assembler = TestDataFactory.NewResearch("assembler", 800f, 1_000_000f, circuitBoard);

            ResearchSystem research = Unlocked();

            Assert.IsFalse(research.ArePrerequisitesMet(assembler), "Precondition: it cannot be started.");
            Assert.IsTrue(ResearchPanelController.IsRevealed(assembler, research), "And it is listed all the same, locked.");
        }

        // --- Building menu category rail ---

        static BuildingMenuEntry Entry(BuildingDefinition definition, BuildingCategory category)
            => new BuildingMenuEntry { definition = definition, category = category };

        static BuildingDefinition NewBuilding(ResearchDefinition unlockResearch)
        {
            var definition = ScriptableObject.CreateInstance<StorageDefinition>();
            var so = new UnityEditor.SerializedObject(definition);
            so.FindProperty("id").stringValue = "storage";
            so.FindProperty("unlockResearch").objectReferenceValue = unlockResearch;
            so.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        /// <summary>
        /// A category tab is exactly as available as its contents. Organisation holds the Storage Box
        /// alone, so before that research it was a tab onto an empty grid - the menu announcing a
        /// section of the game the player cannot reach.
        /// </summary>
        [Test]
        public void ACategoryTab_AppearsOnlyOnceSomethingInItIsUnlocked()
        {
            ResearchDefinition storageBox = TestDataFactory.NewResearch("storage_box", 400f);
            BuildingMenuEntry[] entries = { Entry(NewBuilding(storageBox), BuildingCategory.Organisation) };

            Assert.IsFalse(BuildingMenuController.HasVisibleBuilding(entries, BuildingCategory.Organisation, Unlocked()));
            Assert.IsTrue(BuildingMenuController.HasVisibleBuilding(entries, BuildingCategory.Organisation, Unlocked(storageBox)));
        }

        /// <summary>A category holding something ungated is always there - the rail must not vanish for a player who has researched nothing.</summary>
        [Test]
        public void ACategoryHoldingAnUngatedBuilding_IsAlwaysThere()
        {
            BuildingMenuEntry[] entries = { Entry(NewBuilding(null), BuildingCategory.Production) };

            Assert.IsTrue(BuildingMenuController.HasVisibleBuilding(entries, BuildingCategory.Production, Unlocked()));
        }

        /// <summary>An empty category has no tab either - the rail is built from what is in it, not from the enum.</summary>
        [Test]
        public void ACategoryWithNoEntryAtAll_HasNoTab()
        {
            BuildingMenuEntry[] entries = { Entry(NewBuilding(null), BuildingCategory.Production) };

            Assert.IsFalse(BuildingMenuController.HasVisibleBuilding(entries, BuildingCategory.Power, Unlocked()));
        }
    }
}
