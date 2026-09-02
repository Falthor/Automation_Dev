using Game.Gameplay.Compute;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Compute
{
    public class ComputeSystemTests
    {
        [Test]
        public void Reserve_StartsAtCap()
        {
            var compute = new ComputeSystem();

            Assert.AreEqual(ComputeSystem.ReserveCap, compute.Reserve);
        }

        [Test]
        public void GetPerformanceRatio_CappedAtOne_WhenSupplyExceedsDemand()
        {
            var compute = new ComputeSystem();
            compute.ReportDemand(10f);
            compute.ReportSupply(100f);
            compute.Settle();

            Assert.AreEqual(1f, compute.GetPerformanceRatio());
        }

        [Test]
        public void GetPerformanceRatio_ThrottlesProportionally_WhenDemandExceedsSupply()
        {
            var compute = new ComputeSystem();
            compute.ReportDemand(100f);
            compute.ReportSupply(50f);
            compute.Settle();

            Assert.AreEqual(0.5f, compute.GetPerformanceRatio());
        }

        [Test]
        public void GetPerformanceRatio_IsOne_WhenNoDemand()
        {
            var compute = new ComputeSystem();
            compute.Settle();

            Assert.AreEqual(1f, compute.GetPerformanceRatio());
        }

        [Test]
        public void CanSpend_And_Spend_DeductFromReserve_NeverThrottled()
        {
            var compute = new ComputeSystem();

            Assert.IsTrue(compute.CanSpend(100f));
            compute.Spend(100f);

            Assert.AreEqual(ComputeSystem.ReserveCap - 100f, compute.Reserve);
        }

        [Test]
        public void CanSpend_False_WhenReserveInsufficient()
        {
            var compute = new ComputeSystem();
            compute.Spend(compute.Reserve);

            Assert.IsFalse(compute.CanSpend(1f));
        }

        [Test]
        public void GrowReserve_AddsSettledSupplyTimesDelta_CappedAtReserveCap()
        {
            var compute = new ComputeSystem();
            compute.Spend(1000f);
            compute.ReportSupply(100f);
            compute.Settle();

            compute.GrowReserve(1f);

            Assert.AreEqual(ComputeSystem.ReserveCap - 1000f + 100f, compute.Reserve);
        }

        [Test]
        public void GrowReserve_NeverExceedsCap()
        {
            var compute = new ComputeSystem();
            compute.ReportSupply(1000000f);
            compute.Settle();

            compute.GrowReserve(1f);

            Assert.AreEqual(ComputeSystem.ReserveCap, compute.Reserve);
        }
    }
}
