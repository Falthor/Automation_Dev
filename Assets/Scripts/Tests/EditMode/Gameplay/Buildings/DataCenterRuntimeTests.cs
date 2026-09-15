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

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class DataCenterRuntimeTests
    {
        ItemDatabase _itemDatabase;
        ComputeSystem _compute;
        PowerSystem _power;
        ResearchSystem _research;

        // Test researches, named for what they do - never after a shipped research.
        ResearchDefinition _bays1;
        ResearchDefinition _bays2;
        ResearchDefinition _assist1;
        ResearchDefinition _assist2;
        ResearchDefinition _unrelated;
        ResearchDefinition _researchCore;
        ResearchDefinition _buildingsCore;
        ResearchDefinition _darkCore;
        ResearchDefinition _memoryUnlock;

        [SetUp]
        public void SetUp()
        {
            ItemDefinition cpu = TestDataFactory.NewItem("cpu_mkI", ItemType.Component);
            SetCuPowerLifetime(cpu, 1000f, 2f, 120f);
            ItemDefinition memory = TestDataFactory.NewItem("Memory_MK1", ItemType.Component);
            SetCuPowerLifetime(memory, 500f, 1f, 120f);
            _itemDatabase = TestDataFactory.NewItemDatabase(cpu, memory);

            _compute = new ComputeSystem();
            _power = new PowerSystem();
            _bays1 = TestDataFactory.WithEffects(TestDataFactory.NewResearch("bays_a", 10f), new ResearchEffect(ResearchEffectKind.DataCenterBayPairs, value: 1));
            _bays2 = TestDataFactory.WithEffects(TestDataFactory.NewResearch("bays_b", 10f, prerequisites: new[] { _bays1 }), new ResearchEffect(ResearchEffectKind.DataCenterBayPairs, value: 1));
            _assist1 = TestDataFactory.WithEffects(TestDataFactory.NewResearch("assist_a", 10f), new ResearchEffect(ResearchEffectKind.MemoryAssistCapacityTenths, value: 15));
            _assist2 = TestDataFactory.WithEffects(TestDataFactory.NewResearch("assist_b", 10f, prerequisites: new[] { _assist1 }), new ResearchEffect(ResearchEffectKind.MemoryAssistCapacityTenths, value: 20));
            _unrelated = TestDataFactory.NewResearch("unrelated", 10f);
            _researchCore = TestDataFactory.NewResearch("core_a", 0f);
            _buildingsCore = TestDataFactory.NewResearch("core_b", 0f);
            _darkCore = TestDataFactory.NewResearch("core_c", 0f);
            // Granted immediately below: this file is about bay/component mechanics, not about the
            // Memory gate itself - HasUnlockedMemoryTests owns a fixture that deliberately withholds
            // this, to test the gate on its own.
            _memoryUnlock = TestDataFactory.WithEffects(TestDataFactory.NewResearch("memory_unlock_a", 10f), new ResearchEffect(ResearchEffectKind.UnlockDataCenterMemory));
            _research = new ResearchSystem(_compute, _compute, new ResearchCatalog(new[] { _bays1, _bays2, _assist1, _assist2, _unrelated, _researchCore, _buildingsCore, _darkCore, _memoryUnlock }));
            _research.Grant(_memoryUnlock.Id);
        }

        static void SetCuPowerLifetime(ItemDefinition item, float cu, float pw, float lifetimeSeconds)
        {
            var so = new SerializedObject(item);
            so.FindProperty("cuOutput").floatValue = cu;
            so.FindProperty("powerKw").floatValue = pw;
            so.FindProperty("nominalLifetimeSeconds").floatValue = lifetimeSeconds;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        DataCenterRuntime NewDataCenter(int maxStackPerItem = 10)
        {
            DataCenterDefinition definition = TestDataFactory.NewDataCenter(maxStackPerItem, new[] { "cpu_mkI", "Memory_MK1" }, new[] { _researchCore, _buildingsCore });
            return new DataCenterRuntime(definition, new GridCoord(0, 0), Direction.North, _itemDatabase, _compute, _compute, _power, _research);
        }

        /// <summary>Finishes priming in one oversized tick (the internal Mathf.Min caps absorption at exactly what's left, so any big-enough deltaTime works regardless of the exact 1500/90 rounding) - a fresh Data Center never produces anything until this is done.</summary>
        static void FinishPriming(DataCenterRuntime dataCenter) => dataCenter.Tick(200f);

        /// <summary>
        /// Leaves the network able to serve the datacenter group from here on.
        ///
        /// <b>Both halves matter.</b> Supply alone is not enough: power is allocated per building
        /// type, and a group is allocated against what it asked for on the <i>previous</i> frame -
        /// so the demand has to be heard once before a budget can exist for it. The fixture used to
        /// report supply and settle before anything had asked for anything, and got away with it
        /// because the old contract was a comparison of two totals where 0 &lt;= 0 read as powered.
        ///
        /// 9999 on both sides: this is "the network is not the thing under test", and a budget that
        /// large lets every instance in every test here draw as often as it likes.
        /// </summary>
        void PowerTheDataCenter()
        {
            _power.ReportSupply(9999f);
            _power.TryDraw("datacenter", 9999f);
            _power.Settle();
        }

        /// <summary>Configures a bay and lets one zero-length tick install whatever spare sits in the input - the pattern every test that needs an active component starts from.</summary>
        static void ConfigureAndInstall(DataCenterRuntime dataCenter, int bayIndex, DataCenterBayType type, string itemId, int amount = 1)
        {
            dataCenter.SetBayAssignment(bayIndex, type);
            dataCenter.AddInput(itemId, amount, Direction.South);
            dataCenter.Tick(0f);
        }

        [Test]
        public void StartsWithTwoUnassignedEmptyBays()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.AreEqual(2, dataCenter.Bays.Count);
            foreach (DataCenterBay bay in dataCenter.Bays)
            {
                Assert.AreEqual(DataCenterBayType.Unassigned, bay.Assignment);
                Assert.IsNull(bay.Component);
            }
        }

        [Test]
        public void AResearchWithNoBayEffect_AddsNoBay()
        {
            DataCenterRuntime builtBefore = NewDataCenter();

            _research.Enqueue(_unrelated);
            _research.Tick(60f);

            DataCenterRuntime builtAfter = NewDataCenter();

            Assert.AreEqual(2, builtBefore.Bays.Count);
            Assert.AreEqual(2, builtAfter.Bays.Count);
        }

        /// <summary>
        /// The research menu opens when the Datacenter finishes priming (GDD §5.4), because that is
        /// when it powers the cores its definition names. Not at placement, not partway through - and
        /// never a core it does not name, which waits for something else.
        /// </summary>
        [Test]
        public void FinishingPriming_PowersTheCoresItsDefinitionNames_AndNotBefore()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            dataCenter.Tick(1f);
            Assert.IsTrue(dataCenter.IsPriming, "Precondition: one second into ninety.");
            Assert.IsFalse(_research.IsUnlocked(_researchCore.Id), "Nothing is powered while priming.");

            FinishPriming(dataCenter);
            dataCenter.Tick(0.1f);

            Assert.IsTrue(_research.IsUnlocked(_researchCore.Id));
            Assert.IsTrue(_research.IsUnlocked(_buildingsCore.Id));
            Assert.IsFalse(_research.IsUnlocked(_darkCore.Id));
        }

        [Test]
        public void ABayPairEffect_AddsTwoUnassignedBays()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            _research.Enqueue(_bays1);
            _research.Tick(60f);

            Assert.AreEqual(4, dataCenter.Bays.Count);
            Assert.AreEqual(DataCenterBayType.Unassigned, dataCenter.Bays[2].Assignment);
            Assert.AreEqual(DataCenterBayType.Unassigned, dataCenter.Bays[3].Assignment);
        }

        [Test]
        public void TwoBayPairEffects_BringItToSix()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            _research.Enqueue(_bays1);
            _research.Tick(60f);
            _research.Enqueue(_bays2);
            _research.Tick(60f);

            Assert.AreEqual(6, dataCenter.Bays.Count);
        }

        /// <summary>The bays of researches completed before this Datacenter was built are there from the start - it is not only the completion event that grants them.</summary>
        [Test]
        public void NewDataCenter_StartsWithTheBaysOfResearchesAlreadyCompleted()
        {
            _research.Enqueue(_bays1);
            _research.Tick(60f);
            _research.Enqueue(_bays2);
            _research.Tick(60f);

            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.AreEqual(6, dataCenter.Bays.Count);
        }

        [Test]
        public void OnUnregistered_StopsReactingToFutureBayResearchCompletions()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.OnUnregistered();

            _research.Enqueue(_bays1);
            _research.Tick(60f);

            Assert.AreEqual(2, dataCenter.Bays.Count);
        }

        [Test]
        public void UnassignedBay_InstallsNothing_EvenWithSpareInInput()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);

            dataCenter.Tick(0f);

            Assert.IsNull(dataCenter.Bays[0].Component);
            Assert.AreEqual(1, dataCenter.GetInputAmount("cpu_mkI"));
        }

        [Test]
        public void CpuBay_InstallsCpu_ButNeverMemory()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.SetBayAssignment(0, DataCenterBayType.Cpu);
            dataCenter.AddInput("Memory_MK1", 1, Direction.South);

            dataCenter.Tick(0f);

            Assert.IsNull(dataCenter.Bays[0].Component, "A CPU bay must never accept a Memory component.");
            Assert.AreEqual(1, dataCenter.GetInputAmount("Memory_MK1"));

            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            dataCenter.Tick(0f);

            Assert.IsNotNull(dataCenter.Bays[0].Component);
            Assert.AreEqual("cpu_mkI", dataCenter.Bays[0].Component.ItemId);
        }

        [Test]
        public void MemoryBay_InstallsMemory_ButNeverCpu()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.SetBayAssignment(0, DataCenterBayType.Memory);
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);

            dataCenter.Tick(0f);

            Assert.IsNull(dataCenter.Bays[0].Component, "A Memory bay must never accept a CPU component.");

            dataCenter.AddInput("Memory_MK1", 1, Direction.South);
            dataCenter.Tick(0f);

            Assert.IsNotNull(dataCenter.Bays[0].Component);
            Assert.AreEqual("Memory_MK1", dataCenter.Bays[0].Component.ItemId);
        }

        [Test]
        public void SetBayAssignment_OnAnEmptyBay_IsInstantaneous_WhicheverItsCurrentType()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            dataCenter.SetBayAssignment(0, DataCenterBayType.Cpu);
            Assert.AreEqual(DataCenterBayType.Cpu, dataCenter.Bays[0].Assignment);

            dataCenter.SetBayAssignment(0, DataCenterBayType.Memory);
            Assert.AreEqual(DataCenterBayType.Memory, dataCenter.Bays[0].Assignment, "Reassigning an empty bay must be instant, no reconfiguration delay.");
            Assert.IsNull(dataCenter.Bays[0].ReconfigureTarget);
        }

        [Test]
        public void SetBayAssignment_OnAnOccupiedBay_StartsAFiveSecondReconfiguration_ProducingNothingMeanwhile()
        {
            PowerTheDataCenter();
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            dataCenter.SetBayAssignment(0, DataCenterBayType.Memory);

            DataCenterBay bay = dataCenter.Bays[0];
            Assert.AreEqual(DataCenterBayType.Memory, bay.ReconfigureTarget);
            Assert.AreEqual(DataCenterBayType.Cpu, bay.Assignment, "Assignment only flips once the delay completes.");
            Assert.AreEqual(0f, bay.Component.EffectiveCu(), "Must produce nothing while reconfiguring.");

            dataCenter.Tick(4.9f);
            Assert.AreEqual(DataCenterBayType.Cpu, dataCenter.Bays[0].Assignment, "Not yet - under 5 seconds.");

            dataCenter.Tick(0.2f);
            Assert.AreEqual(DataCenterBayType.Memory, dataCenter.Bays[0].Assignment);
            Assert.IsNull(dataCenter.Bays[0].Component, "The old component is discarded, not returned - a fresh Memory must be delivered.");
            Assert.IsNull(dataCenter.Bays[0].ReconfigureTarget);
        }

        [Test]
        public void ReconfiguringBay_LooksForTheNewTypeOnly_OnceComplete()
        {
            PowerTheDataCenter();
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            dataCenter.SetBayAssignment(0, DataCenterBayType.Memory);
            dataCenter.AddInput("Memory_MK1", 1, Direction.South);
            dataCenter.Tick(5.1f); // completes the reconfiguration - the bay is empty by the end of this tick
            dataCenter.Tick(0f);   // InstallInto runs at the START of a tick, so it needs one more to see the now-empty bay

            Assert.IsNotNull(dataCenter.Bays[0].Component);
            Assert.AreEqual("Memory_MK1", dataCenter.Bays[0].Component.ItemId);
        }

        [Test]
        public void Wear_DoesNotDecay_WhileReconfiguring()
        {
            PowerTheDataCenter();
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            dataCenter.SetBayAssignment(0, DataCenterBayType.Memory);
            float wearAtStart = dataCenter.Bays[0].Component.Wear;

            dataCenter.Tick(3f);

            Assert.AreEqual(wearAtStart, dataCenter.Bays[0].Component.Wear, "Wear must freeze once a bay stops producing for reconfiguration.");
        }

        [Test]
        public void AlreadyReplacingForWear_ChangingTarget_DoesNotRestartTheTimer()
        {
            PowerTheDataCenter();
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.SetCpuReplacementThreshold(DataCenterRuntime.MaxReplacementThresholdPercent); // 60%
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            const float step = 0.5f;
            float elapsed = 0f;
            while (!dataCenter.Bays[0].Component.IsReplacing && elapsed < 200f)
            {
                dataCenter.Tick(step);
                elapsed += step;
            }
            Assert.IsTrue(dataCenter.Bays[0].Component.IsReplacing, "Precondition: already replacing for wear.");
            float elapsedBeforeRetarget = dataCenter.Bays[0].Component.ReplacementElapsed;

            dataCenter.SetBayAssignment(0, DataCenterBayType.Memory); // player retargets mid-flight

            Assert.AreEqual(elapsedBeforeRetarget, dataCenter.Bays[0].Component.ReplacementElapsed, "Retargeting an in-flight replacement must not reset its timer.");
            Assert.AreEqual(DataCenterBayType.Memory, dataCenter.Bays[0].ReconfigureTarget);
        }

        [Test]
        public void CancellingAReconfiguration_BeforeItsComponentWasDue_RestoresProductionForFree()
        {
            PowerTheDataCenter();
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            dataCenter.SetBayAssignment(0, DataCenterBayType.Memory);
            dataCenter.Tick(1f);
            dataCenter.SetBayAssignment(0, DataCenterBayType.Cpu); // back to what it already was

            DataCenterBay bay = dataCenter.Bays[0];
            Assert.IsNull(bay.ReconfigureTarget);
            Assert.IsFalse(bay.Component.IsReplacing);
            Assert.AreEqual(DataCenterBayType.Cpu, bay.Assignment);
            Assert.Greater(bay.Component.EffectiveCu(), 0f, "Cancelled before its own threshold - must resume producing.");
        }

        [Test]
        public void CanAcceptInput_RejectsItemNotInAcceptedList()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.IsFalse(dataCenter.CanAcceptInput("Iron_Ingot", 1, Direction.South));
        }

        /// <summary>
        /// Regression: the Data Center has no physical output, so unlike a Foundry or a Powerplant
        /// it must accept from every side including the one FacingRotation happens to name. A belt
        /// feeding from North - the default placement rotation, so also the most common case - was
        /// silently refused before this was fixed.
        /// </summary>
        [Test]
        public void CanAcceptInput_AcceptsFromTheFacingSide()
        {
            DataCenterRuntime dataCenter = NewDataCenter(); // built facing North

            Assert.IsTrue(dataCenter.CanAcceptInput("cpu_mkI", 1, Direction.North));
        }

        [Test]
        public void Tick_WhilePriming_ConsumesCuButProducesNothing_EvenPoweredWithAComponentInstalled()
        {
            PowerTheDataCenter();

            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.SetBayAssignment(0, DataCenterBayType.Cpu);
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            float reserveBefore = _compute.Reserve;

            dataCenter.Tick(1f);

            Assert.IsTrue(dataCenter.IsPriming);
            Assert.IsNotNull(dataCenter.Bays[0].Component, "Pre-stocking a bay during priming must still work.");
            Assert.AreEqual(reserveBefore - 1500f / 90f, _compute.Reserve, 0.01f, "Priming draws its own fixed rate, not the installed component's output.");
        }

        [Test]
        public void Tick_PrimingConsumesExactly1500Cu_OverExactly90Seconds()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            float reserveBefore = _compute.Reserve;

            float elapsed = 0f;
            while (dataCenter.IsPriming && elapsed < 200f)
            {
                dataCenter.Tick(1f);
                elapsed += 1f;
            }

            Assert.IsFalse(dataCenter.IsPriming);
            Assert.AreEqual(90f, elapsed, 1.5f);
            Assert.AreEqual(reserveBefore - 1500f, _compute.Reserve, 0.5f);
        }

        [Test]
        public void Tick_PrimingPausesAtZeroCu_AndPreservesProgressExactly()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.Tick(1f);
            float progressBeforeStarvation = dataCenter.PrimingProgress;
            Assert.Greater(progressBeforeStarvation, 0f);

            _compute.Spend(_compute.Reserve); // drain to exactly 0
            dataCenter.Tick(20f); // would absorb ~333 more CU if the reserve had it

            Assert.AreEqual(progressBeforeStarvation, dataCenter.PrimingProgress, 0.00001f, "Progress must not move while the reserve is at zero.");
            Assert.IsTrue(dataCenter.IsPriming);
        }

        [Test]
        public void Tick_PrimingResumesExactlyWhereItLeftOff_OnceCuIsAvailableAgain()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.Tick(1f);
            float progressBeforeStarvation = dataCenter.PrimingProgress;

            _compute.Spend(_compute.Reserve);
            dataCenter.Tick(20f); // starved

            _compute.Grant(1000f);
            dataCenter.Tick(1f); // resumes: one more second's worth absorbed

            float expectedProgress = progressBeforeStarvation + (1500f / 90f) / 1500f;
            Assert.AreEqual(expectedProgress, dataCenter.PrimingProgress, 0.0001f);
        }

        [Test]
        public void ComputeGrant_ZeroWhileUnpowered_EvenWithAComponentInstalled_OncePrimed()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);

            _power.TryDraw("datacenter", 9999f);
            _power.Settle();
            _compute.Spend(5000f);

            dataCenter.SetBayAssignment(0, DataCenterBayType.Cpu);
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            dataCenter.Tick(0f);

            float before = _compute.Reserve;
            dataCenter.Tick(1f);

            Assert.AreEqual(before, _compute.Reserve);
        }

        [Test]
        public void ComputeGrant_CreditsInstalledComponentsOutputForTheTicksDuration_WhenPoweredAndPrimed()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);

            PowerTheDataCenter();
            _compute.Spend(5000f);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            float before = _compute.Reserve;
            dataCenter.Tick(1f);

            // Only bay 0 (CPU) is active; no Memory means no coverage, so FinalYield == GetYield().
            // Default 50/50 axis split, default 0.20 yield floor -> yield = 0.20 + 0.80*0.5 = 0.60.
            Assert.AreEqual(before + 1000f * 0.60f, _compute.Reserve, 0.01f);
        }

        [Test]
        public void AxisProduction_Is100PercentResearch_ZeroBuildings_AtFullConcentration()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            PowerTheDataCenter();
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            dataCenter.SetResearchAxisShare(1f); // 100/0

            float installedTotal = dataCenter.GetTotalComputeOutput();
            Assert.AreEqual(installedTotal, dataCenter.GetResearchAxisProduction(), 0.01f);
            Assert.AreEqual(0f, dataCenter.GetBuildingsAxisProduction(), 0.01f);
        }

        [Test]
        public void AxisProduction_Is30PercentEach_At5050Split_WithDefaultFloor()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            PowerTheDataCenter();
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            // ResearchAxisShare already defaults to 0.5. No Memory installed -> FinalYield == GetYield() == 0.60.
            float installedTotal = dataCenter.GetTotalComputeOutput();
            Assert.AreEqual(installedTotal * 0.30f, dataCenter.GetResearchAxisProduction(), 0.01f);
            Assert.AreEqual(installedTotal * 0.30f, dataCenter.GetBuildingsAxisProduction(), 0.01f);
        }

        /// <summary>
        /// Regression: both axes used to credit whichever single ComputeSystem this class was
        /// handed, which every other test here cannot catch since NewDataCenter() passes the same
        /// _compute instance for both reserves. Two distinct instances are the only way to prove
        /// the split is real rather than cosmetic.
        /// </summary>
        [Test]
        public void AxisProduction_CreditsEachIntoItsOwnReserve_NeverTheOther()
        {
            var buildingCompute = new ComputeSystem();
            var researchCompute = new ComputeSystem();
            DataCenterDefinition definition = TestDataFactory.NewDataCenter(10, new[] { "cpu_mkI", "Memory_MK1" }, new[] { _researchCore, _buildingsCore });
            var dataCenter = new DataCenterRuntime(definition, new GridCoord(0, 0), Direction.North, _itemDatabase, buildingCompute, researchCompute, _power, _research);

            FinishPriming(dataCenter);
            PowerTheDataCenter();
            buildingCompute.Spend(5000f);
            researchCompute.Spend(5000f);
            dataCenter.SetBayAssignment(0, DataCenterBayType.Cpu);
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            dataCenter.Tick(0f); // installs

            float buildingBefore = buildingCompute.Reserve;
            float researchBefore = researchCompute.Reserve;
            dataCenter.Tick(1f);

            // Default 50/50 split, default 0.20 yield floor -> 0.30 of the 1000 installed each side.
            Assert.AreEqual(buildingBefore + 1000f * 0.30f, buildingCompute.Reserve, 0.01f);
            Assert.AreEqual(researchBefore + 1000f * 0.30f, researchCompute.Reserve, 0.01f);
        }

        /// <summary>Priming is an installation cost, not a research one - Research Compute must stay untouched by it.</summary>
        [Test]
        public void Priming_SpendsFromBuildingCompute_NeverFromResearchCompute()
        {
            var buildingCompute = new ComputeSystem();
            var researchCompute = new ComputeSystem();
            DataCenterDefinition definition = TestDataFactory.NewDataCenter(10, new[] { "cpu_mkI", "Memory_MK1" }, new[] { _researchCore, _buildingsCore });
            var dataCenter = new DataCenterRuntime(definition, new GridCoord(0, 0), Direction.North, _itemDatabase, buildingCompute, researchCompute, _power, _research);

            FinishPriming(dataCenter);

            Assert.Less(buildingCompute.Reserve, ComputeSystem.ReserveCap, "Priming's 1500 CU came from Building Compute.");
            Assert.AreEqual(ComputeSystem.ReserveCap, researchCompute.Reserve, "Research Compute is untouched by priming.");
        }

        [Test]
        public void HigherReplacementThreshold_ConsumesMoreSpareComponents_OverTheSameDuration()
        {
            PowerTheDataCenter();

            DataCenterRuntime lowThreshold = NewDataCenter(maxStackPerItem: 1000);
            lowThreshold.SetCpuReplacementThreshold(DataCenterRuntime.MinReplacementThresholdPercent);
            lowThreshold.SetBayAssignment(0, DataCenterBayType.Cpu);
            FinishPriming(lowThreshold);
            lowThreshold.AddInput("cpu_mkI", 500, Direction.South);

            DataCenterRuntime highThreshold = NewDataCenter(maxStackPerItem: 1000);
            highThreshold.SetCpuReplacementThreshold(DataCenterRuntime.MaxReplacementThresholdPercent);
            highThreshold.SetBayAssignment(0, DataCenterBayType.Cpu);
            FinishPriming(highThreshold);
            highThreshold.AddInput("cpu_mkI", 500, Direction.South);

            const float duration = 400f;
            const float step = 0.1f;
            for (float elapsed = 0f; elapsed < duration; elapsed += step)
            {
                lowThreshold.Tick(step);
                highThreshold.Tick(step);
            }

            int lowConsumed = 500 - lowThreshold.GetInputAmount("cpu_mkI");
            int highConsumed = 500 - highThreshold.GetInputAmount("cpu_mkI");

            Assert.Greater(highConsumed, lowConsumed, "A higher replacement threshold must burn through more spare CPUs over the same wall-clock duration - it stops each component earlier in its life.");
        }

        [Test]
        public void ComponentEntersReplacement_AtWearMatchingTheConfiguredThreshold()
        {
            PowerTheDataCenter();

            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.SetCpuReplacementThreshold(40f);
            dataCenter.SetBayAssignment(0, DataCenterBayType.Cpu);
            FinishPriming(dataCenter);
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            dataCenter.Tick(0f); // installs

            const float step = 0.05f;
            float elapsed = 0f;
            while (dataCenter.Bays[0].Component != null && !dataCenter.Bays[0].Component.IsReplacing && elapsed < 500f)
            {
                dataCenter.Tick(step);
                elapsed += step;
            }

            Assert.IsNotNull(dataCenter.Bays[0].Component, "Should have entered replacement well before being hard-removed at 0% wear.");
            Assert.IsTrue(dataCenter.Bays[0].Component.IsReplacing);
            Assert.AreEqual(40f, dataCenter.Bays[0].Component.Wear, 1f, "Wear at the moment replacement starts must match the configured threshold.");
        }

        // --- Memory coverage / final yield (DATACENTER.md) ---

        // Fixture CU values (SetUp: cpu_mkI=1000, Memory_MK1=500) stand in for the shipped 40/25 -
        // what these three test is the summation shape (2xCPU, 1 each, 2 each), not the real balance.

        [Test]
        public void RawCompute_TwoCpu_SumsBothCpuBays()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");
            ConfigureAndInstall(dataCenter, 1, DataCenterBayType.Cpu, "cpu_mkI");

            Assert.AreEqual(2000f, dataCenter.GetTotalComputeOutput(), 0.01f);
        }

        [Test]
        public void RawCompute_OneCpuOneMemory_SumsBoth()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");
            ConfigureAndInstall(dataCenter, 1, DataCenterBayType.Memory, "Memory_MK1");

            Assert.AreEqual(1500f, dataCenter.GetTotalComputeOutput(), 0.01f);
        }

        [Test]
        public void RawCompute_TwoCpuTwoMemory_SumsAllFour()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            _research.Enqueue(_bays1);
            _research.Tick(60f);
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");
            ConfigureAndInstall(dataCenter, 1, DataCenterBayType.Cpu, "cpu_mkI");
            ConfigureAndInstall(dataCenter, 2, DataCenterBayType.Memory, "Memory_MK1");
            ConfigureAndInstall(dataCenter, 3, DataCenterBayType.Memory, "Memory_MK1");

            Assert.AreEqual(3000f, dataCenter.GetTotalComputeOutput(), 0.01f);
        }

        [TestCase(1, 1, 1.0f, 1.0f)]
        [TestCase(2, 1, 1.0f, 0.5f)]
        [TestCase(3, 1, 1.0f, 1f / 3f)]
        [TestCase(2, 2, 1.0f, 1.0f)]
        [TestCase(3, 1, 1.5f, 0.5f)]
        [TestCase(4, 2, 2.0f, 1.0f)]
        public void MemoryCoverage_MatchesTheSpecifiedRatios(int cpuBays, int memoryBays, float assistCapacityFromResearch, float expectedCoverage)
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            // Enough bay pairs to fit the requested composition.
            while (dataCenter.Bays.Count < cpuBays + memoryBays)
            {
                _research.Enqueue(dataCenter.Bays.Count < 4 ? _bays1 : _bays2);
                _research.Tick(60f);
            }

            if (assistCapacityFromResearch >= 2.0f)
            {
                _research.Enqueue(_assist1);
                _research.Tick(60f);
                _research.Enqueue(_assist2);
                _research.Tick(60f);
            }
            else if (assistCapacityFromResearch > DataCenterRuntime.DefaultMemoryAssistCapacity)
            {
                _research.Enqueue(_assist1);
                _research.Tick(60f);
            }

            FinishPriming(dataCenter);
            for (int i = 0; i < cpuBays; i++) ConfigureAndInstall(dataCenter, i, DataCenterBayType.Cpu, "cpu_mkI");
            for (int i = 0; i < memoryBays; i++) ConfigureAndInstall(dataCenter, cpuBays + i, DataCenterBayType.Memory, "Memory_MK1");

            Assert.AreEqual(expectedCoverage, dataCenter.GetMemoryCoverage(), 0.001f);
        }

        [Test]
        public void FinalYield_At5050Split_NoMemory_EqualsBaseYield()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            Assert.AreEqual(0.60f, dataCenter.GetYield(), 0.001f);
            Assert.AreEqual(dataCenter.GetYield(), dataCenter.GetFinalYield(), 0.001f, "No active Memory -> zero coverage -> FinalYield must equal the base concentration yield.");
        }

        [Test]
        public void FinalYield_At5050Split_FullCoverage_Is090()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");
            ConfigureAndInstall(dataCenter, 1, DataCenterBayType.Memory, "Memory_MK1");

            Assert.AreEqual(1f, dataCenter.GetMemoryCoverage(), 0.001f, "Precondition: 1 CPU + 1 Memory at default capacity 1.0 is full coverage.");
            Assert.AreEqual(0.90f, dataCenter.GetFinalYield(), 0.001f);
        }

        [Test]
        public void NoActiveCpu_ProducesNothing_EvenWithMemoryInstalled()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            PowerTheDataCenter();
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Memory, "Memory_MK1");

            Assert.Greater(dataCenter.GetTotalComputeOutput(), 0f, "Precondition: Memory alone still contributes to RawCompute.");
            Assert.AreEqual(0, dataCenter.ActiveCpuCount);
            Assert.AreEqual(0f, dataCenter.GetMemoryCoverage(), "No division by zero, and no coverage without a CPU to cover.");
            Assert.AreEqual(0f, dataCenter.GetResearchAxisProduction());
            Assert.AreEqual(0f, dataCenter.GetBuildingsAxisProduction());
        }

        [Test]
        public void NoActiveMemory_CoverageIsZero_FinalYieldEqualsBaseYield()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            ConfigureAndInstall(dataCenter, 0, DataCenterBayType.Cpu, "cpu_mkI");

            Assert.AreEqual(0, dataCenter.ActiveMemoryCount);
            Assert.AreEqual(0f, dataCenter.GetMemoryCoverage());
            Assert.AreEqual(dataCenter.GetYield(), dataCenter.GetFinalYield(), 0.0001f);
        }

        [Test]
        public void CaptureAndRestore_RoundTripsFullState()
        {
            PowerTheDataCenter();

            DataCenterRuntime original = NewDataCenter();
            FinishPriming(original);

            ConfigureAndInstall(original, 0, DataCenterBayType.Cpu, "cpu_mkI");
            original.SetCpuReplacementThreshold(40f);
            original.SetMemoryReplacementThreshold(10f);
            original.SetResearchAxisShare(0.75f);
            original.Tick(5f); // let some wear accumulate
            float wearBeforeCapture = original.Bays[0].Component.Wear;
            float lifetimeBeforeCapture = original.Bays[0].Component.NominalLifetimeSeconds;

            JObject state = original.CaptureState();

            DataCenterRuntime restored = NewDataCenter();
            restored.RestoreState(state);

            Assert.AreEqual(DataCenterBayType.Cpu, restored.Bays[0].Assignment);
            Assert.AreEqual(wearBeforeCapture, restored.Bays[0].Component.Wear);
            Assert.AreEqual(lifetimeBeforeCapture, restored.Bays[0].Component.NominalLifetimeSeconds);
            Assert.AreEqual(40f, restored.CpuReplacementThresholdPercent);
            Assert.AreEqual(10f, restored.MemoryReplacementThresholdPercent);
            Assert.AreEqual(0.75f, restored.ResearchAxisShare);
            Assert.IsFalse(restored.IsPriming);
        }

        [Test]
        public void CaptureAndRestore_RoundTripsAReconfigurationInProgress()
        {
            PowerTheDataCenter();
            DataCenterRuntime original = NewDataCenter();
            FinishPriming(original);
            ConfigureAndInstall(original, 0, DataCenterBayType.Cpu, "cpu_mkI");
            original.SetBayAssignment(0, DataCenterBayType.Memory);
            original.Tick(2f);

            JObject state = original.CaptureState();
            DataCenterRuntime restored = NewDataCenter();
            restored.RestoreState(state);

            Assert.AreEqual(DataCenterBayType.Memory, restored.Bays[0].ReconfigureTarget);
            Assert.AreEqual(DataCenterBayType.Cpu, restored.Bays[0].Assignment);
            Assert.IsTrue(restored.Bays[0].Component.IsReplacing);
            Assert.AreEqual(original.Bays[0].Component.ReplacementElapsed, restored.Bays[0].Component.ReplacementElapsed, 0.0001f);
        }

        [Test]
        public void RestoreState_MigratesALegacyCpuAndMemorySlotBlob_OneForOneIntoTypedBays()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            var state = new JObject
            {
                ["cpuSlots"] = new JArray
                {
                    new JObject { ["itemId"] = "cpu_mkI", ["wear"] = 60f, ["effectivePerformance"] = 0.9f, ["isReplacing"] = false, ["replacementElapsed"] = 0f },
                    JValue.CreateNull()
                },
                ["memorySlots"] = new JArray
                {
                    new JObject { ["itemId"] = "Memory_MK1", ["wear"] = 30f, ["effectivePerformance"] = 0.7f, ["isReplacing"] = true, ["replacementElapsed"] = 2f }
                }
            };

            dataCenter.RestoreState(state);

            Assert.AreEqual(3, dataCenter.Bays.Count);
            Assert.AreEqual(DataCenterBayType.Cpu, dataCenter.Bays[0].Assignment);
            Assert.AreEqual(60f, dataCenter.Bays[0].Component.Wear);
            Assert.AreEqual(DataCenterBayType.Cpu, dataCenter.Bays[1].Assignment);
            Assert.IsNull(dataCenter.Bays[1].Component, "A null legacy slot restores as an empty typed bay.");
            Assert.AreEqual(DataCenterBayType.Memory, dataCenter.Bays[2].Assignment);
            Assert.AreEqual(30f, dataCenter.Bays[2].Component.Wear);
            Assert.IsTrue(dataCenter.Bays[2].Component.IsReplacing);
            Assert.AreEqual(2f, dataCenter.Bays[2].Component.ReplacementElapsed);
        }

        [Test]
        public void RestoreState_ToleratesACompletelyEmptyBlob_FallsBackToDefaults()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.DoesNotThrow(() => dataCenter.RestoreState(new JObject()));
            Assert.AreEqual(DataCenterRuntime.DefaultReplacementThresholdPercent, dataCenter.CpuReplacementThresholdPercent);
            Assert.AreEqual(DataCenterRuntime.DefaultReplacementThresholdPercent, dataCenter.MemoryReplacementThresholdPercent);
            Assert.AreEqual(0.5f, dataCenter.ResearchAxisShare);
            Assert.IsFalse(dataCenter.IsPriming, "Absent primingAbsorbedCu must default to 'already primed', not re-freeze an established playthrough.");
            Assert.AreEqual(0, dataCenter.Bays.Count, "An absent bays array (and no legacy keys either) clears to no bays.");
        }

        [Test]
        public void RestoreState_ToleratesABayBlobMissingTheNewLifetimeFields()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            var state = new JObject
            {
                ["bays"] = new JArray
                {
                    new JObject
                    {
                        ["assignment"] = (int)DataCenterBayType.Cpu,
                        ["component"] = new JObject { ["itemId"] = "cpu_mkI", ["wear"] = 60f, ["effectivePerformance"] = 0.9f, ["isReplacing"] = false, ["replacementElapsed"] = 0f }
                    }
                }
            };

            Assert.DoesNotThrow(() => dataCenter.RestoreState(state));
            Assert.AreEqual(60f, dataCenter.Bays[0].Component.Wear);
            Assert.Greater(dataCenter.Bays[0].Component.NominalLifetimeSeconds, 0f);
            Assert.Greater(dataCenter.Bays[0].Component.BaseLossPerSecond, 0f);
        }
    }
}
