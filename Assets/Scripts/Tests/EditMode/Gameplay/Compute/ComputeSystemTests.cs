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

        /// <summary>Pins the actual target value (TASK_01_REBALANCE_DATA.md), not just internal self-consistency with Reserve_StartsAtCap above.</summary>
        [Test]
        public void ReserveCap_Is60000()
        {
            Assert.AreEqual(60000f, ComputeSystem.ReserveCap);
        }

        [Test]
        public void CanSpend_And_Spend_DeductFromReserve()
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
        public void Grant_CreditsTheReserve()
        {
            var compute = new ComputeSystem();
            compute.Spend(3000f);

            compute.Grant(3000f);

            Assert.AreEqual(ComputeSystem.ReserveCap, compute.Reserve);
        }

        [Test]
        public void Grant_NeverExceedsCap()
        {
            var compute = new ComputeSystem();

            compute.Grant(1000000f);

            Assert.AreEqual(ComputeSystem.ReserveCap, compute.Reserve);
        }

        [Test]
        public void Grant_IgnoresNonPositiveAmounts()
        {
            var compute = new ComputeSystem();
            compute.Spend(1000f);

            compute.Grant(0f);
            compute.Grant(-500f);

            Assert.AreEqual(ComputeSystem.ReserveCap - 1000f, compute.Reserve);
        }

        [Test]
        public void IncomePerSecond_AveragesWhatWasActuallyCredited_OverTheWindow()
        {
            var compute = new ComputeSystem();
            compute.Spend(10000f);

            // One 3000 CU grant per 5s window reads as 600 CU/s.
            compute.Grant(3000f);
            compute.Tick(5f);

            Assert.AreEqual(600f, compute.IncomePerSecond, 0.01f);
        }

        [Test]
        public void IncomePerSecond_ExcludesWhatTheCapDiscarded()
        {
            var compute = new ComputeSystem();
            compute.Spend(1000f);

            // Only 1000 of the 5000 actually fits back under the cap.
            compute.Grant(5000f);
            compute.Tick(5f);

            Assert.AreEqual(200f, compute.IncomePerSecond, 0.01f);
        }

        [Test]
        public void IncomePerSecond_StaysUnchanged_BeforeTheWindowElapses()
        {
            var compute = new ComputeSystem();
            compute.Spend(10000f);

            compute.Grant(3000f);
            compute.Tick(1f);

            Assert.AreEqual(0f, compute.IncomePerSecond);
        }
    }
}
