using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Tests.EditMode.TestSupport;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Research
{
    /// <summary>
    /// The post-Datacenter first act rework: Directive 4 grants CPU only, Metallurgie avancee gates
    /// Steel and the new Memoire branch, Architecture memoire gates both the Memory Mk.I recipe and
    /// DataCenterRuntime's Memory bay assignment, Ordonnancement I and Hors de portee move behind
    /// their new prerequisites. Reads the shipped assets by path wherever the assertion is about
    /// what ships - a fixture copy would stop tracking the real asset the moment it changed.
    /// </summary>
    public class PostDatacenterFirstActTests
    {
        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"{path} is missing.");
            return asset;
        }

        static bool HasEffect(ResearchDefinition research, ResearchEffectKind kind)
        {
            foreach (ResearchEffect effect in research.Effects) if (effect.Kind == kind) return true;
            return false;
        }

        static bool HasPrerequisite(ResearchDefinition research, ResearchDefinition candidate)
        {
            foreach (ResearchDefinition prerequisite in research.Prerequisites) if (prerequisite == candidate) return true;
            return false;
        }

        // --- Directive 4 ---

        [Test]
        public void CoreDirective4_GrantsCpuAndDatacenter_NoLongerMemory()
        {
            var directive4 = Load<ResearchDefinition>("Assets/Data/Research/core_directive_4.asset");
            var datacenter = Load<DataCenterDefinition>("Assets/Data/Buildings/DataCenterDefinition.asset");
            var cpuRecipe = Load<RecipeDefinition>("Assets/Data/Recipes/cpu_mkI_Recipe.asset");
            var memoryRecipe = Load<RecipeDefinition>("Assets/Data/Recipes/Memory_MK1_Recipe.asset");

            bool unlocksDatacenter = false, unlocksCpu = false, unlocksMemory = false;
            foreach (ResearchEffect effect in directive4.Effects)
            {
                if (effect.Kind == ResearchEffectKind.UnlockBuilding && effect.Building == datacenter) unlocksDatacenter = true;
                if (effect.Kind == ResearchEffectKind.UnlockRecipe && effect.Recipe == cpuRecipe) unlocksCpu = true;
                if (effect.Kind == ResearchEffectKind.UnlockRecipe && effect.Recipe == memoryRecipe) unlocksMemory = true;
            }

            Assert.IsTrue(unlocksDatacenter, "Directive 4 must still unlock the Datacenter.");
            Assert.IsTrue(unlocksCpu, "Directive 4 must still unlock CPU Mk.I.");
            Assert.IsFalse(unlocksMemory, "Directive 4 must no longer unlock Memory Mk.I directly.");
        }

        // --- Metallurgie avancee ---

        [Test]
        public void AdvancedFoundry_RequiresCapaciteIAndExtensionI()
        {
            var advancedFoundry = Load<ResearchDefinition>("Assets/Data/Research/advanced_foundry.asset");
            var capaciteI = Load<ResearchDefinition>("Assets/Data/Research/memory_allocation.asset");
            var extensionI = Load<ResearchDefinition>("Assets/Data/Research/datacenter_bay_1.asset");

            // Also requires cortex_buildings directly (kept alongside the two below) - both
            // cortices feed this research, not just the Datacenter's own branch.
            Assert.IsTrue(HasPrerequisite(advancedFoundry, capaciteI), "Must require Capacite de gestion I.");
            Assert.IsTrue(HasPrerequisite(advancedFoundry, extensionI), "Must require Extension de baies I.");
        }

        [Test]
        public void AdvancedFoundry_UnlocksTheBuildingAndSteel()
        {
            var advancedFoundry = Load<ResearchDefinition>("Assets/Data/Research/advanced_foundry.asset");
            var building = Load<BuildingDefinition>("Assets/Data/Buildings/AdvancedFoundryDefinition.asset");
            var steel = Load<RecipeDefinition>("Assets/Data/Recipes/Steel_Recipe.asset");

            bool unlocksBuilding = false, unlocksSteel = false;
            foreach (ResearchEffect effect in advancedFoundry.Effects)
            {
                if (effect.Kind == ResearchEffectKind.UnlockBuilding && effect.Building == building) unlocksBuilding = true;
                if (effect.Kind == ResearchEffectKind.UnlockRecipe && effect.Recipe == steel) unlocksSteel = true;
            }

            Assert.IsTrue(unlocksBuilding);
            Assert.IsTrue(unlocksSteel);
        }

        [Test]
        public void AdvancedFoundryBuilding_OffersOnlySteel_AndAcceptsItsIngredients()
        {
            var building = Load<AdvancedFoundryDefinition>("Assets/Data/Buildings/AdvancedFoundryDefinition.asset");
            var steel = Load<RecipeDefinition>("Assets/Data/Recipes/Steel_Recipe.asset");

            CollectionAssert.AreEquivalent(new[] { "Steel" }, building.RecipeIds);
            foreach (RecipeIngredient ingredient in steel.Ingredients)
            {
                CollectionAssert.Contains(building.AcceptedItemIds, ingredient.Item.Id);
            }
        }

        // --- Architecture memoire ---

        [Test]
        public void MemoryArchitecture_RequiresScrews_AndUnlocksMemoryRecipeAndTheGate()
        {
            var memoryArchitecture = Load<ResearchDefinition>("Assets/Data/Research/memory_architecture.asset");
            var screws = Load<ResearchDefinition>("Assets/Data/Research/screws.asset");
            var memoryRecipe = Load<RecipeDefinition>("Assets/Data/Recipes/Memory_MK1_Recipe.asset");

            Assert.AreEqual(1, memoryArchitecture.Prerequisites.Count);
            Assert.IsTrue(HasPrerequisite(memoryArchitecture, screws));

            bool unlocksMemory = false;
            foreach (ResearchEffect effect in memoryArchitecture.Effects)
            {
                if (effect.Kind == ResearchEffectKind.UnlockRecipe && effect.Recipe == memoryRecipe) unlocksMemory = true;
            }
            Assert.IsTrue(unlocksMemory);
            Assert.IsTrue(HasEffect(memoryArchitecture, ResearchEffectKind.UnlockDataCenterMemory));
        }

        [Test]
        public void MemoryMk1Recipe_IsLockedUntilArchitectureMemoireIsGranted()
        {
            var memoryArchitecture = Load<ResearchDefinition>("Assets/Data/Research/memory_architecture.asset");
            var memoryRecipe = Load<RecipeDefinition>("Assets/Data/Recipes/Memory_MK1_Recipe.asset");
            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(), new ResearchCatalog(new[] { memoryArchitecture }));

            Assert.IsFalse(research.IsRecipeUnlocked(memoryRecipe), "Locked before Architecture memoire.");

            research.Grant(memoryArchitecture.Id);

            Assert.IsTrue(research.IsRecipeUnlocked(memoryRecipe), "Unlocked once Architecture memoire completes.");
        }

        // --- Datacenter Memory bay gate ---

        [Test]
        public void SetBayAssignment_RefusesMemory_UntilTheRealArchitectureMemoireIsGranted()
        {
            var (dataCenter, research) = NewGateFixture();

            Assert.IsFalse(dataCenter.HasUnlockedMemory);
            Assert.IsFalse(dataCenter.SetBayAssignment(0, DataCenterBayType.Memory), "Refused before the research completes.");
            Assert.AreEqual(DataCenterBayType.Unassigned, dataCenter.Bays[0].Assignment);
            Assert.IsTrue(dataCenter.SetBayAssignment(0, DataCenterBayType.Cpu), "CPU stays available throughout.");

            research.Grant("memory_architecture");

            Assert.IsTrue(dataCenter.HasUnlockedMemory);
            Assert.IsTrue(dataCenter.SetBayAssignment(1, DataCenterBayType.Memory), "Allowed once granted.");
        }

        static (DataCenterRuntime dataCenter, ResearchSystem research) NewGateFixture()
        {
            var memoryArchitecture = Load<ResearchDefinition>("Assets/Data/Research/memory_architecture.asset");
            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(), new ResearchCatalog(new[] { memoryArchitecture }));
            ItemDefinition cpu = TestDataFactory.NewItem("cpu_mkI", ItemType.Component);
            ItemDefinition memory = TestDataFactory.NewItem("Memory_MK1", ItemType.Component);
            ItemDatabase itemDatabase = TestDataFactory.NewItemDatabase(cpu, memory);
            DataCenterDefinition definition = TestDataFactory.NewDataCenter(10, new[] { "cpu_mkI", "Memory_MK1" });
            var dataCenter = new DataCenterRuntime(definition, new GridCoord(0, 0), Direction.North, itemDatabase, new ComputeSystem(), new ComputeSystem(), new PowerSystem(), research);
            return (dataCenter, research);
        }

        // --- Ordonnancement I ---

        [Test]
        public void OrdonnancementI_RequiresMemoryArchitecture()
        {
            var ordonnancement1 = Load<ResearchDefinition>("Assets/Data/Research/ordonnancement_1.asset");
            var memoryArchitecture = Load<ResearchDefinition>("Assets/Data/Research/memory_architecture.asset");

            Assert.AreEqual(1, ordonnancement1.Prerequisites.Count);
            Assert.IsTrue(HasPrerequisite(ordonnancement1, memoryArchitecture));
        }

        [Test]
        public void OrdonnancementI_IsUnreachable_UntilMemoryArchitectureCompletes()
        {
            var advancedFoundry = Load<ResearchDefinition>("Assets/Data/Research/advanced_foundry.asset");
            var memoryArchitecture = Load<ResearchDefinition>("Assets/Data/Research/memory_architecture.asset");
            var ordonnancement1 = Load<ResearchDefinition>("Assets/Data/Research/ordonnancement_1.asset");
            var capaciteI = Load<ResearchDefinition>("Assets/Data/Research/memory_allocation.asset");
            var extensionI = Load<ResearchDefinition>("Assets/Data/Research/datacenter_bay_1.asset");
            var portee1 = Load<ResearchDefinition>("Assets/Data/Research/extended_bandwidth.asset");

            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(),
                new ResearchCatalog(new[] { advancedFoundry, memoryArchitecture, ordonnancement1, capaciteI, extensionI, portee1 }));

            Assert.IsFalse(research.ArePrerequisitesMet(ordonnancement1));

            research.Grant(portee1.Id);
            research.Grant(capaciteI.Id);
            research.Grant(extensionI.Id);
            research.Grant(advancedFoundry.Id);
            Assert.IsFalse(research.ArePrerequisitesMet(ordonnancement1), "Metallurgie avancee alone is still not Architecture memoire.");

            research.Grant(memoryArchitecture.Id);
            Assert.IsTrue(research.ArePrerequisitesMet(ordonnancement1));
        }

        // --- Hors de portee ---

        [Test]
        public void OutOfLimit_RequiresPorteeIII_NotCortexBuildingsDirectly()
        {
            var outOfLimit = Load<ResearchDefinition>("Assets/Data/Research/OutOfLimit.asset");
            var porteeIII = Load<ResearchDefinition>("Assets/Data/Research/extended_bandwidth_3.asset");
            var cortexBuildings = Load<ResearchDefinition>("Assets/Data/Research/cortex_buildings.asset");

            Assert.AreEqual(1, outOfLimit.Prerequisites.Count);
            Assert.IsTrue(HasPrerequisite(outOfLimit, porteeIII));
            Assert.IsFalse(HasPrerequisite(outOfLimit, cortexBuildings), "No longer directly under the Buildings core.");
        }

        // --- Migration ---

        [Test]
        public void Migration_FreshDatacenter_NoProof_MemoryStaysLocked()
        {
            var (dataCenter, research) = NewMigrationFixture();
            dataCenter.RestoreState(MinimalSaveBlob());

            Assert.IsFalse(research.IsUnlocked("memory_architecture"));
            Assert.IsFalse(dataCenter.HasUnlockedMemory);
        }

        [Test]
        public void Migration_OldSaveWithOrdonnancementUnlocked_GrantsMemoryArchitecture()
        {
            var (dataCenter, research) = NewMigrationFixture();
            research.Grant("ordonnancement_1");

            dataCenter.RestoreState(MinimalSaveBlob());

            Assert.IsTrue(research.IsUnlocked("memory_architecture"),
                "Ordonnancement I is unreachable under the new tree without it - proof of an older save.");
            Assert.IsTrue(dataCenter.HasUnlockedMemory);
        }

        [Test]
        public void Migration_OldSaveWithAnActiveMemoryBay_GrantsMemoryArchitecture_AndKeepsTheBay()
        {
            var (dataCenter, research) = NewMigrationFixture();

            JObject blob = MinimalSaveBlob();
            var bays = (JArray)blob["bays"];
            bays[0]["assignment"] = (int)DataCenterBayType.Memory;
            bays[0]["component"] = new JObject
            {
                ["itemId"] = "Memory_MK1",
                ["wear"] = 80f,
                ["effectivePerformance"] = 1f,
                ["isReplacing"] = false,
                ["replacementElapsed"] = 0f,
                ["nominalLifetimeSeconds"] = 120f,
                ["baseLossPerSecond"] = 0.5f
            };

            dataCenter.RestoreState(blob);

            Assert.IsTrue(research.IsUnlocked("memory_architecture"));
            Assert.IsTrue(dataCenter.HasUnlockedMemory);
            Assert.AreEqual(DataCenterBayType.Memory, dataCenter.Bays[0].Assignment, "The bay itself is untouched - never stripped.");
            Assert.AreEqual("Memory_MK1", dataCenter.Bays[0].Component.ItemId);
        }

        static (DataCenterRuntime dataCenter, ResearchSystem research) NewMigrationFixture()
        {
            var memoryArchitecture = Load<ResearchDefinition>("Assets/Data/Research/memory_architecture.asset");
            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(), new ResearchCatalog(new[] { memoryArchitecture }));
            ItemDefinition cpu = TestDataFactory.NewItem("cpu_mkI", ItemType.Component);
            ItemDefinition memory = TestDataFactory.NewItem("Memory_MK1", ItemType.Component);
            ItemDatabase itemDatabase = TestDataFactory.NewItemDatabase(cpu, memory);
            DataCenterDefinition definition = TestDataFactory.NewDataCenter(10, new[] { "cpu_mkI", "Memory_MK1" });
            var dataCenter = new DataCenterRuntime(definition, new GridCoord(0, 0), Direction.North, itemDatabase, new ComputeSystem(), new ComputeSystem(), new PowerSystem(), research);
            return (dataCenter, research);
        }

        static JObject MinimalSaveBlob()
        {
            var bays = new JArray();
            for (int i = 0; i < 2; i++) bays.Add(new JObject { ["assignment"] = 0 });
            return new JObject
            {
                ["stabilityTimer"] = 0f,
                ["previousPowerDemand"] = 0f,
                ["primingAbsorbedCu"] = 1500f,
                ["cpuReplacementThresholdPercent"] = 25f,
                ["memoryReplacementThresholdPercent"] = 25f,
                ["researchAxisShare"] = 0.5f,
                ["input"] = new JObject(),
                ["bays"] = bays
            };
        }
    }
}
