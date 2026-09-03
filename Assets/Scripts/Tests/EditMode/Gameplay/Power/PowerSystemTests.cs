using Game.Gameplay.Power;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Power
{
    public class PowerSystemTests
    {
        [Test]
        public void Settle_MovesPendingReportsIntoSettledTotals()
        {
            var power = new PowerSystem();

            power.ReportDemand(5f);
            power.ReportSupply(10f);
            Assert.AreEqual(0f, power.SettledDemand);

            power.Settle();

            Assert.AreEqual(5f, power.SettledDemand);
            Assert.AreEqual(10f, power.SettledSupply);
        }

        [Test]
        public void IsPowered_TrueWhenDemandAtOrBelowSupply()
        {
            var power = new PowerSystem();
            power.ReportDemand(10f);
            power.ReportSupply(10f);
            power.Settle();

            Assert.IsTrue(power.IsPowered());
        }

        [Test]
        public void IsPowered_FalseWhenDemandExceedsSupply()
        {
            var power = new PowerSystem();
            power.ReportDemand(11f);
            power.ReportSupply(10f);
            power.Settle();

            Assert.IsFalse(power.IsPowered());
        }

        [Test]
        public void Settle_ClearsPendingAccumulatorsForNextFrame()
        {
            var power = new PowerSystem();
            power.ReportDemand(5f);
            power.Settle();

            power.Settle(); // no new reports this frame

            Assert.AreEqual(0f, power.SettledDemand);
        }

        [Test]
        public void Recovery_IsImmediate_OnceDemandDropsBackAtOrBelowSupply()
        {
            var power = new PowerSystem();
            power.ReportDemand(20f);
            power.ReportSupply(10f);
            power.Settle();
            Assert.IsFalse(power.IsPowered());

            power.ReportDemand(5f);
            power.ReportSupply(10f);
            power.Settle();

            Assert.IsTrue(power.IsPowered());
        }
    }
}
