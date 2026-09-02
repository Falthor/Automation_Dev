using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class PowerplantGazRuntimeTests
    {
        ItemDefinition _fuel;
        ComputeSystem _compute;
        PowerSystem _power;

        [SetUp]
        public void SetUp()
        {
            _fuel = TestDataFactory.NewItem("Coal_ore", ItemType.Component);
            _compute = new ComputeSystem();
            _power = new PowerSystem();
        }

        PowerplantGazRuntime NewPlant(float powerOutputKw = 10f, float selfPowerDemandKw = 2f, float cuCostPerCycle = 150f, float fuelCycleTimeSeconds = 10f)
        {
            PowerplantGazDefinition definition = TestDataFactory.NewPowerplantGaz(_fuel, 20, powerOutputKw, selfPowerDemandKw, cuCostPerCycle, fuelCycleTimeSeconds);
            return new PowerplantGazRuntime(definition, new GridCoord(0, 0), Direction.North, _compute, _power);
        }

        [Test]
        public void NoFuel_SuppliesNoPower_ButStillReportsSelfDemand()
        {
            PowerplantGazRuntime plant = NewPlant();

            plant.Tick(1f);
            _power.Settle();

            Assert.AreEqual(0f, _power.SettledSupply);
            Assert.AreEqual(2f, _power.SettledDemand); // self-demand always reported
        }

        [Test]
        public void WithFuel_SuppliesPower_Unconditionally()
        {
            PowerplantGazRuntime plant = NewPlant(powerOutputKw: 10f);
            plant.AddInput("Coal_ore", 1, Direction.South);

            plant.Tick(1f);
            _power.Settle();

            Assert.AreEqual(10f, _power.SettledSupply);
        }

        [Test]
        public void ConsumesOneFuelUnit_PerCycle()
        {
            PowerplantGazRuntime plant = NewPlant(fuelCycleTimeSeconds: 10f);
            plant.AddInput("Coal_ore", 1, Direction.South);

            plant.Tick(9.9f);
            Assert.AreEqual(1, plant.FuelAmount);

            plant.Tick(0.2f);
            Assert.AreEqual(0, plant.FuelAmount);
        }

        [Test]
        public void FuelTimer_Freezes_WhenFuelRunsOut()
        {
            PowerplantGazRuntime plant = NewPlant(fuelCycleTimeSeconds: 10f);
            plant.AddInput("Coal_ore", 1, Direction.South);
            plant.Tick(10.1f); // burns the only unit

            Assert.AreEqual(0, plant.FuelAmount);
            Assert.AreEqual(0f, plant.FuelTimer);

            plant.Tick(5f); // no fuel - timer must not advance
            Assert.AreEqual(0f, plant.FuelTimer);
        }

        [Test]
        public void CanAcceptInput_RejectsNonFuelItem()
        {
            PowerplantGazRuntime plant = NewPlant();

            Assert.IsFalse(plant.CanAcceptInput("Iron_Ingot", 1, Direction.South));
        }

        [Test]
        public void CanAcceptInput_RejectsFromOutputSide()
        {
            PowerplantGazRuntime plant = NewPlant();

            Assert.IsFalse(plant.CanAcceptInput("Coal_ore", 1, Direction.North)); // facing North -> output side is North
        }
    }
}
