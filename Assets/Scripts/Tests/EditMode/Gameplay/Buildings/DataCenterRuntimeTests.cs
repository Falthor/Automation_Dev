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
        ResearchDefinition _unrelated;
        ResearchDefinition _researchCore;
        ResearchDefinition _buildingsCore;
        ResearchDefinition _darkCore;

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
            _unrelated = TestDataFactory.NewResearch("unrelated", 10f);
            _researchCore = TestDataFactory.NewResearch("core_a", 0f);
            _buildingsCore = TestDataFactory.NewResearch("core_b", 0f);
            _darkCore = TestDataFactory.NewResearch("core_c", 0f);
            _research = new ResearchSystem(_compute, new ResearchCatalog(new[] { _bays1, _bays2, _unrelated, _researchCore, _buildingsCore, _darkCore }));
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
            return new DataCenterRuntime(definition, new GridCoord(0, 0), Direction.North, _itemDatabase, _compute, _power, _research);
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

        /// <summary>A research with no bay effect adds no bay, whether it completes before the Datacenter exists or after.</summary>
        [Test]
        public void AResearchWithNoBayEffect_AddsNoBay()
        {
            DataCenterRuntime builtBefore = NewDataCenter();

            _research.Enqueue(_unrelated);
            _research.Tick(60f);

            DataCenterRuntime builtAfter = NewDataCenter();

            Assert.AreEqual(1, builtBefore.CpuSlots.Count);
            Assert.AreEqual(1, builtBefore.MemorySlots.Count);
            Assert.AreEqual(1, builtAfter.CpuSlots.Count);
            Assert.AreEqual(1, builtAfter.MemorySlots.Count);
        }

        [Test]
        public void StartsWithOneCpuAndOneMemorySlot()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.AreEqual(1, dataCenter.CpuSlots.Count);
            Assert.AreEqual(1, dataCenter.MemorySlots.Count);
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
        public void ABayPairEffect_AddsOneCpuBayAndOneMemoryBay()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            _research.Enqueue(_bays1);
            _research.Tick(60f);

            Assert.AreEqual(2, dataCenter.CpuSlots.Count);
            Assert.AreEqual(2, dataCenter.MemorySlots.Count);
        }

        [Test]
        public void TwoBayPairEffects_BringItToThreeAndThree()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            _research.Enqueue(_bays1);
            _research.Tick(60f);
            _research.Enqueue(_bays2);
            _research.Tick(60f);

            Assert.AreEqual(3, dataCenter.CpuSlots.Count);
            Assert.AreEqual(3, dataCenter.MemorySlots.Count);
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

            Assert.AreEqual(3, dataCenter.CpuSlots.Count);
            Assert.AreEqual(3, dataCenter.MemorySlots.Count);
        }

        [Test]
        public void OnUnregistered_StopsReactingToFutureBayResearchCompletions()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.OnUnregistered();

            _research.Enqueue(_bays1);
            _research.Tick(60f);

            Assert.AreEqual(1, dataCenter.CpuSlots.Count);
            Assert.AreEqual(1, dataCenter.MemorySlots.Count);
        }

        [Test]
        public void Tick_InstallsDeliveredComponent_IntoFirstEmptySlot()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);

            dataCenter.Tick(0f);

            Assert.IsNotNull(dataCenter.CpuSlots[0]);
            Assert.AreEqual("cpu_mkI", dataCenter.CpuSlots[0].ItemId);
            Assert.AreEqual(0, dataCenter.GetInputAmount("cpu_mkI"));
        }

        [Test]
        public void Tick_ExcessDelivered_StaysInInput_WhenNoEmptySlotLeft()
        {
            DataCenterRuntime dataCenter = NewDataCenter(maxStackPerItem: 10);
            dataCenter.AddInput("cpu_mkI", 5, Direction.South); // only 1 initial CPU slot

            dataCenter.Tick(0f);

            Assert.AreEqual(4, dataCenter.GetInputAmount("cpu_mkI"));
        }

        [Test]
        public void CanAcceptInput_RejectsItemNotInAcceptedList()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.IsFalse(dataCenter.CanAcceptInput("Iron_Ingot", 1, Direction.South));
        }

        [Test]
        public void Tick_WhilePriming_ConsumesCuButProducesNothing_EvenPoweredWithAComponentInstalled()
        {
            PowerTheDataCenter();

            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            float reserveBefore = _compute.Reserve;

            dataCenter.Tick(1f);

            Assert.IsTrue(dataCenter.IsPriming);
            Assert.IsNotNull(dataCenter.CpuSlots[0], "Pre-stocking a bay during priming must still work.");
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
            // Stepping in whole 1s ticks can overshoot by exactly one iteration when the
            // cumulative sum lands a hair under 1500 due to float rounding (1500/90 doesn't
            // divide evenly) - tolerate that one extra step rather than the underlying absorption.
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

            // Nothing supplies, so the datacenter group is allocated nothing once settled - power
            // is drawn per building type now, and the datacenter's own group is what gates its CU.
            _power.TryDraw("datacenter", 9999f);
            _power.Settle();
            _compute.Spend(5000f); // make room under the cap so a grant would be visible

            dataCenter.AddInput("cpu_mkI", 1, Direction.South);

            // Installed on its own tick first, like the test above: a building reports the demand it
            // had at the end of the previous tick, so one that has just been given its first
            // component is still asking for nothing - and a group asking for nothing is not a group
            // the network can refuse. The one-frame lag is the power contract's, not this test's.
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

            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            dataCenter.Tick(0f); // installs
            float before = _compute.Reserve;
            dataCenter.Tick(1f);

            // Default 50/50 axis split, default 0.20 yield floor -> yield = 0.20 + 0.80*0.5 = 0.60.
            Assert.AreEqual(before + 1000f * 0.60f, _compute.Reserve, 0.01f);
        }

        [Test]
        public void AxisProduction_Is100PercentResearch_ZeroBuildings_AtFullConcentration()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);
            PowerTheDataCenter();
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            dataCenter.Tick(0f);

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
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            dataCenter.Tick(0f);

            // ResearchAxisShare already defaults to 0.5.
            float installedTotal = dataCenter.GetTotalComputeOutput();
            Assert.AreEqual(installedTotal * 0.30f, dataCenter.GetResearchAxisProduction(), 0.01f);
            Assert.AreEqual(installedTotal * 0.30f, dataCenter.GetBuildingsAxisProduction(), 0.01f);
        }

        [Test]
        public void HigherReplacementThreshold_ConsumesMoreSpareComponents_OverTheSameDuration()
        {
            PowerTheDataCenter();

            DataCenterRuntime lowThreshold = NewDataCenter(maxStackPerItem: 1000);
            lowThreshold.SetCpuReplacementThreshold(DataCenterRuntime.MinReplacementThresholdPercent);
            FinishPriming(lowThreshold);
            lowThreshold.AddInput("cpu_mkI", 500, Direction.South);

            DataCenterRuntime highThreshold = NewDataCenter(maxStackPerItem: 1000);
            highThreshold.SetCpuReplacementThreshold(DataCenterRuntime.MaxReplacementThresholdPercent);
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
            FinishPriming(dataCenter);
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            dataCenter.Tick(0f); // installs

            const float step = 0.05f;
            float elapsed = 0f;
            while (dataCenter.CpuSlots[0] != null && !dataCenter.CpuSlots[0].IsReplacing && elapsed < 500f)
            {
                dataCenter.Tick(step);
                elapsed += step;
            }

            Assert.IsNotNull(dataCenter.CpuSlots[0], "Should have entered replacement well before being hard-removed at 0% wear.");
            Assert.IsTrue(dataCenter.CpuSlots[0].IsReplacing);
            Assert.AreEqual(40f, dataCenter.CpuSlots[0].Wear, 1f, "Wear at the moment replacement starts must match the configured threshold.");
        }

        [Test]
        public void CaptureAndRestore_RoundTripsFullState()
        {
            PowerTheDataCenter();

            DataCenterRuntime original = NewDataCenter();
            FinishPriming(original);

            original.AddInput("cpu_mkI", 1, Direction.South);
            original.Tick(0f); // installs
            original.SetCpuReplacementThreshold(40f);
            original.SetMemoryReplacementThreshold(10f);
            original.SetResearchAxisShare(0.75f);
            original.Tick(5f); // let some wear accumulate
            float wearBeforeCapture = original.CpuSlots[0].Wear;
            float lifetimeBeforeCapture = original.CpuSlots[0].NominalLifetimeSeconds;

            JObject state = original.CaptureState();

            DataCenterRuntime restored = NewDataCenter();
            restored.RestoreState(state);

            Assert.AreEqual(wearBeforeCapture, restored.CpuSlots[0].Wear);
            Assert.AreEqual(lifetimeBeforeCapture, restored.CpuSlots[0].NominalLifetimeSeconds);
            Assert.AreEqual(40f, restored.CpuReplacementThresholdPercent);
            Assert.AreEqual(10f, restored.MemoryReplacementThresholdPercent);
            Assert.AreEqual(0.75f, restored.ResearchAxisShare);
            Assert.IsFalse(restored.IsPriming);
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
            Assert.AreEqual(0, dataCenter.CpuSlots.Count, "An absent cpuSlots array clears to no slots, per RestoreSlots' own null-check - matches the pre-existing convention for every other restorable list.");
        }

        [Test]
        public void RestoreState_ToleratesASlotBlobMissingTheNewLifetimeFields()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            var state = new JObject
            {
                ["cpuSlots"] = new JArray
                {
                    new JObject { ["itemId"] = "cpu_mkI", ["wear"] = 60f, ["effectivePerformance"] = 0.9f, ["isReplacing"] = false, ["replacementElapsed"] = 0f }
                },
                ["memorySlots"] = new JArray()
            };

            Assert.DoesNotThrow(() => dataCenter.RestoreState(state));
            Assert.AreEqual(60f, dataCenter.CpuSlots[0].Wear);
            Assert.Greater(dataCenter.CpuSlots[0].NominalLifetimeSeconds, 0f);
            Assert.Greater(dataCenter.CpuSlots[0].BaseLossPerSecond, 0f);
        }
    }
}
