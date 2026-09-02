using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Tests.EditMode.TestSupport;
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
            SetCuPower(cpu, 1000f, 2f);
            ItemDefinition memory = TestDataFactory.NewItem("Memory_MK1", ItemType.Component);
            SetCuPower(memory, 500f, 1f);
            _itemDatabase = TestDataFactory.NewItemDatabase(cpu, memory);

            _compute = new ComputeSystem();
            _power = new PowerSystem();
            _research = new ResearchSystem();
        }

        static void SetCuPower(ItemDefinition item, float cu, float pw)
        {
            var so = new SerializedObject(item);
            so.FindProperty("cuOutput").floatValue = cu;
            so.FindProperty("powerKw").floatValue = pw;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        DataCenterRuntime NewDataCenter(int maxStackPerItem = 10)
        {
            DataCenterDefinition definition = TestDataFactory.NewDataCenter(maxStackPerItem, new[] { "cpu_mkI", "Memory_MK1" }, null);
            return new DataCenterRuntime(definition, new GridCoord(0, 0), Direction.North, _itemDatabase, _compute, _power, _research);
        }

        [Test]
        public void StartsWithFourCpuAndFourMemorySlots()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.AreEqual(4, dataCenter.CpuSlots.Count);
            Assert.AreEqual(4, dataCenter.MemorySlots.Count);
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
            dataCenter.AddInput("cpu_mkI", 5, Direction.South); // only 4 initial CPU slots

            dataCenter.Tick(0f);

            Assert.AreEqual(1, dataCenter.GetInputAmount("cpu_mkI"));
        }

        [Test]
        public void ComputeGrant_ZeroWhileUnpowered_EvenTheTickAComponentIsInstalled()
        {
            _power.ReportDemand(9999f); // nothing supplies -> unpowered once settled
            _power.Settle();
            _compute.Spend(5000f); // make room under the cap so a grant would be visible

            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            float before = _compute.Reserve;
            dataCenter.Tick(1f);

            Assert.AreEqual(before, _compute.Reserve);
        }

        [Test]
        public void ComputeGrant_CreditsInstalledComponentsOutputForTheTicksDuration_WhenPowered()
        {
            _power.ReportSupply(9999f);
            _power.Settle();
            _compute.Spend(5000f);

            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.AddInput("cpu_mkI", 1, Direction.South);
            float before = _compute.Reserve;
            dataCenter.Tick(1f);

            Assert.AreEqual(before + 1000f, _compute.Reserve); // 1000 CU/s for 1 second
        }

        [Test]
        public void ResearchCompleted_ExtraCpuSlot_AppendsOneCpuSlot_NotMemory()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            ResearchDefinition extraSlot = TestDataFactory.NewResearch("extra_cpu_slot", 20f);
            _research.AddRp(20f);
            _research.Start(extraSlot);
            _research.ReportActiveLab();

            _research.Tick(60f);

            Assert.AreEqual(5, dataCenter.CpuSlots.Count);
            Assert.AreEqual(4, dataCenter.MemorySlots.Count);
        }

        [Test]
        public void NewDataCenter_StartsWithExtraSlot_IfAlreadyUnlockedAtConstruction()
        {
            ResearchDefinition extraSlot = TestDataFactory.NewResearch("extra_cpu_slot", 20f);
            _research.AddRp(20f);
            _research.Start(extraSlot);
            _research.ReportActiveLab();
            _research.Tick(60f);
            Assert.IsTrue(_research.IsUnlocked("extra_cpu_slot"));

            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.AreEqual(5, dataCenter.CpuSlots.Count);
        }

        [Test]
        public void OnUnregistered_StopsReactingToFutureResearchCompletions()
        {
            DataCenterRuntime dataCenter = NewDataCenter();
            dataCenter.OnUnregistered();

            ResearchDefinition extraSlot = TestDataFactory.NewResearch("extra_cpu_slot", 20f);
            _research.AddRp(20f);
            _research.Start(extraSlot);
            _research.ReportActiveLab();
            _research.Tick(60f);

            Assert.AreEqual(4, dataCenter.CpuSlots.Count);
        }

        [Test]
        public void CanAcceptInput_RejectsItemNotInAcceptedList()
        {
            DataCenterRuntime dataCenter = NewDataCenter();

            Assert.IsFalse(dataCenter.CanAcceptInput("Iron_Ingot", 1, Direction.South));
        }
    }
}
