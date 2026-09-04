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
            _research = new ResearchSystem(_compute);
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
            DataCenterDefinition definition = TestDataFactory.NewDataCenter(maxStackPerItem, new[] { "cpu_mkI", "Memory_MK1" }, null);
            return new DataCenterRuntime(definition, new GridCoord(0, 0), Direction.North, _itemDatabase, _compute, _power, _research);
        }

        /// <summary>Finishes priming in one oversized tick (the internal Mathf.Min caps absorption at exactly what's left, so any big-enough deltaTime works regardless of the exact 1500/90 rounding) - a fresh Data Center never produces anything until this is done.</summary>
        static void FinishPriming(DataCenterRuntime dataCenter) => dataCenter.Tick(200f);

        /// <summary>
        /// TASK_03_DATACENTER.md §1 - debt check, written before any other modification. Task 02
        /// renamed the extra_cpu_slot research asset to storage_box via git mv (GUID preserved,
        /// id changed); DataCenterRuntime recognizes its bay-granting research by a literal
        /// string ("extra_cpu_slot"), not by asset reference, so the renamed asset's new id
        /// should already be a dead match - completing storage_box must add no bay of any kind.
        /// If it did, a stray asset-reference lookup would be hiding somewhere.
        /// </summary>
        [Test]
        public void CompletingStorageBox_AddsNoDataCenterBay()
        {
            DataCenterRuntime withoutStorageBox = NewDataCenter();

            ResearchDefinition storageBox = TestDataFactory.NewResearch("storage_box", 10f);
            _research.Enqueue(storageBox);
            _research.Tick(60f);
            Assert.IsTrue(_research.IsUnlocked("storage_box"));

            DataCenterRuntime withStorageBoxAlreadyUnlocked = NewDataCenter();

            Assert.AreEqual(withoutStorageBox.CpuSlots.Count, withStorageBoxAlreadyUnlocked.CpuSlots.Count);
            Assert.AreEqual(withoutStorageBox.MemorySlots.Count, withStorageBoxAlreadyUnlocked.MemorySlots.Count);
        }

        [Test]
        public void StartsWithTwoCpuAndTwoMemorySlots()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.AreEqual(2, dataCenter.CpuSlots.Count);
            Assert.AreEqual(2, dataCenter.MemorySlots.Count);
        }

        [Test]
        public void DatacenterBay1_AddsOneCpuBayAndOneMemoryBay()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            ResearchDefinition bay1 = TestDataFactory.NewResearch("datacenter_bay_1", 10f);

            _research.Enqueue(bay1);
            _research.Tick(60f);

            Assert.AreEqual(3, dataCenter.CpuSlots.Count);
            Assert.AreEqual(3, dataCenter.MemorySlots.Count);
        }

        [Test]
        public void DatacenterBay2_AfterBay1_BringsItToFourAndFour_TheStatedCeiling()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            ResearchDefinition bay1 = TestDataFactory.NewResearch("datacenter_bay_1", 10f);
            ResearchDefinition bay2 = TestDataFactory.NewResearch("datacenter_bay_2", 10f, prerequisites: new[] { bay1 });

            _research.Enqueue(bay1);
            _research.Tick(60f);
            _research.Enqueue(bay2);
            _research.Tick(60f);

            Assert.AreEqual(4, dataCenter.CpuSlots.Count);
            Assert.AreEqual(4, dataCenter.MemorySlots.Count);
        }

        [Test]
        public void NewDataCenter_StartsAtFourAndFour_IfBothBaysAlreadyUnlockedAtConstruction()
        {
            ResearchDefinition bay1 = TestDataFactory.NewResearch("datacenter_bay_1", 10f);
            ResearchDefinition bay2 = TestDataFactory.NewResearch("datacenter_bay_2", 10f, prerequisites: new[] { bay1 });
            _research.Enqueue(bay1);
            _research.Tick(60f);
            _research.Enqueue(bay2);
            _research.Tick(60f);
            Assert.IsTrue(_research.IsUnlocked("datacenter_bay_1"));
            Assert.IsTrue(_research.IsUnlocked("datacenter_bay_2"));

            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.AreEqual(4, dataCenter.CpuSlots.Count);
            Assert.AreEqual(4, dataCenter.MemorySlots.Count);
        }

        [Test]
        public void OnUnregistered_StopsReactingToFutureBayResearchCompletions()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.OnUnregistered();

            ResearchDefinition bay1 = TestDataFactory.NewResearch("datacenter_bay_1", 10f);
            _research.Enqueue(bay1);
            _research.Tick(60f);

            Assert.AreEqual(2, dataCenter.CpuSlots.Count);
            Assert.AreEqual(2, dataCenter.MemorySlots.Count);
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
            dataCenter.AddInput("cpu_mkI", 5, Direction.South); // only 2 initial CPU slots

            dataCenter.Tick(0f);

            Assert.AreEqual(3, dataCenter.GetInputAmount("cpu_mkI"));
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
            _power.ReportSupply(9999f);
            _power.Settle();

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

            _power.ReportDemand(9999f); // nothing supplies -> unpowered once settled
            _power.Settle();
            _compute.Spend(5000f); // make room under the cap so a grant would be visible

            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            float before = _compute.Reserve;
            dataCenter.Tick(1f);

            Assert.AreEqual(before, _compute.Reserve);
        }

        [Test]
        public void ComputeGrant_CreditsInstalledComponentsOutputForTheTicksDuration_WhenPoweredAndPrimed()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            FinishPriming(dataCenter);

            _power.ReportSupply(9999f);
            _power.Settle();
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
            _power.ReportSupply(9999f);
            _power.Settle();
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
            _power.ReportSupply(9999f);
            _power.Settle();
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            dataCenter.Tick(0f);

            // ResearchAxisShare already defaults to 0.5.
            float installedTotal = dataCenter.GetTotalComputeOutput();
            Assert.AreEqual(installedTotal * 0.30f, dataCenter.GetResearchAxisProduction(), 0.01f);
            Assert.AreEqual(installedTotal * 0.30f, dataCenter.GetBuildingsAxisProduction(), 0.01f);
        }

        [Test]
        public void CaptureAndRestore_RoundTripsFullState()
        {
            _power.ReportSupply(9999f);
            _power.Settle();

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
