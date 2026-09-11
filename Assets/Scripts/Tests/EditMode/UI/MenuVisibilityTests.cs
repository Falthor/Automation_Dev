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
    /// Whether the research menu exists yet, and what the building menu lists - held apart from the
    /// panels themselves so it can be checked without a UIDocument, the same approach as
    /// ConstructionSitePanelFormatTests.
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

        // --- Research menu ---

        static ResearchDatabase NewDatabase(ResearchDefinition[] cores, params ResearchDefinition[] researches)
        {
            var database = ScriptableObject.CreateInstance<ResearchDatabase>();
            var so = new UnityEditor.SerializedObject(database);
            SetArray(so.FindProperty("cores"), cores);
            SetArray(so.FindProperty("researches"), researches);
            so.ApplyModifiedPropertiesWithoutUndo();
            return database;
        }

        static void SetArray(UnityEditor.SerializedProperty array, ResearchDefinition[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>
        /// There is no research menu during the introduction: it runs on the Core's directives. The
        /// menu - Top Bar card and Bottom Nav icon alike - exists from the moment a core is powered,
        /// which the Datacenter does when its priming is done.
        /// </summary>
        [Test]
        public void TheResearchMenu_ExistsOnlyOnceACoreIsPowered()
        {
            ResearchDefinition researchCore = TestDataFactory.NewResearch("cortex_research", 0f);
            ResearchDefinition armament = TestDataFactory.NewResearch("cortex_armament", 0f);
            ResearchDefinition bays = TestDataFactory.NewResearch("datacenter_bay_1", 2000f, 1_000_000f, researchCore);
            ResearchDatabase database = NewDatabase(new[] { researchCore, armament }, bays);

            Assert.IsFalse(ResearchPanelController.IsAvailable(database, Unlocked()), "A new run has no research menu.");
            Assert.IsTrue(ResearchPanelController.IsAvailable(database, Unlocked(researchCore)));
        }

        /// <summary>A core is a root of the network, not a research: nothing lists or counts it as one - but a save naming it still resolves.</summary>
        [Test]
        public void TheCores_AreNotInTheResearchList_ButAreStillFoundById()
        {
            ResearchDefinition researchCore = TestDataFactory.NewResearch("cortex_research", 0f);
            ResearchDefinition bays = TestDataFactory.NewResearch("datacenter_bay_1", 2000f, 1_000_000f, researchCore);
            ResearchDatabase database = NewDatabase(new[] { researchCore }, bays);

            Assert.AreEqual(1, database.GetAll().Count);
            Assert.AreSame(bays, database.GetAll()[0]);
            Assert.AreSame(researchCore, database.Get("cortex_research"));
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
