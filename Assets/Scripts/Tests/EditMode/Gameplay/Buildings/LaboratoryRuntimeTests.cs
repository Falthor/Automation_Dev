using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class LaboratoryRuntimeTests
    {
        ItemDefinition _card;
        ComputeSystem _compute;
        PowerSystem _power;
        ResearchSystem _research;

        [SetUp]
        public void SetUp()
        {
            _card = TestDataFactory.NewItem("Data_Card", ItemType.Component);
            _compute = new ComputeSystem();
            _power = new PowerSystem();
            _research = new ResearchSystem();
        }

        LaboratoryRuntime NewLab(float cardConvertIntervalSeconds = 2f, float rpPerCard = 2f, float cuCostPerCycle = 250f, float powerDemandKw = 3f)
        {
            LaboratoryDefinition definition = TestDataFactory.NewLaboratory(_card, 100, powerDemandKw, cuCostPerCycle, cardConvertIntervalSeconds, rpPerCard);
            return new LaboratoryRuntime(definition, new GridCoord(0, 0), Direction.North, _compute, _power, _research);
        }

        [Test]
        public void ConvertsCard_ToRp_AfterInterval()
        {
            LaboratoryRuntime lab = NewLab(cardConvertIntervalSeconds: 2f, rpPerCard: 2f);
            lab.AddInput("Data_Card", 1, Direction.South);

            lab.Tick(1.9f);
            Assert.AreEqual(0f, _research.Rp);

            lab.Tick(0.2f);
            Assert.AreEqual(2f, _research.Rp);
        }

        [Test]
        public void GeneratesRp_IndependentOfWhetherAResearchIsActive()
        {
            LaboratoryRuntime lab = NewLab();
            lab.AddInput("Data_Card", 1, Direction.South);

            lab.Tick(2.1f); // no active research at all

            Assert.AreEqual(2f, _research.Rp);
        }

        [Test]
        public void ReportsActiveLab_OnlyWhileResearchIsActive()
        {
            LaboratoryRuntime lab = NewLab();

            lab.Tick(0.1f); // no active research
            _research.Tick(0f);
            Assert.AreEqual(0, _research.GetActiveLabCount());

            _research.AddRp(1000f);
            _research.Start(TestDataFactory.NewResearch("test", 10f));
            lab.Tick(0.1f);
            _research.Tick(0f);
            Assert.AreEqual(1, _research.GetActiveLabCount());
        }

        [Test]
        public void CardConversion_Frozen_WhileUnpowered()
        {
            LaboratoryRuntime lab = NewLab();
            lab.AddInput("Data_Card", 1, Direction.South);
            _power.ReportDemand(9999f); // nothing supplies -> unpowered
            _power.Settle();

            lab.Tick(10f);

            Assert.AreEqual(0f, _research.Rp);
        }

        [Test]
        public void CanAcceptInput_RejectsNonCardItem()
        {
            LaboratoryRuntime lab = NewLab();

            Assert.IsFalse(lab.CanAcceptInput("Iron_Ingot", 1, Direction.South));
        }
    }
}
