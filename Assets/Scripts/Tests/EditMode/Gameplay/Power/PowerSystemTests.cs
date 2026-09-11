using Game.Gameplay.Power;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Power
{
    /// <summary>
    /// <b>Every test here is two frames long, and that is the contract, not a nuisance.</b> A
    /// building draws during its own tick; <c>Settle</c> turns what was drawn into the next frame's
    /// budgets. So a draw on the first frame always fails - nothing has been allocated yet - and
    /// what a test asserts is the second frame's answer. The old tests could ignore this because
    /// <c>IsPowered</c> compared two totals and 0 &lt;= 0 was true.
    /// </summary>
    public class PowerSystemTests
    {
        /// <summary>One frame of draws and supply reports, settled - so the next frame's draws have a budget to draw on.</summary>
        static void Frame(PowerSystem power, float supplyKw, params (string group, float kw)[] draws)
        {
            power.ReportSupply(supplyKw);
            for (int i = 0; i < draws.Length; i++) power.TryDraw(draws[i].group, draws[i].kw);
            power.Settle();
        }

        [Test]
        public void Settle_MovesPendingReportsIntoSettledTotals()
        {
            var power = new PowerSystem();

            power.TryDraw("foundry", 5f);
            power.ReportSupply(10f);
            Assert.AreEqual(0f, power.SettledDemand);

            power.Settle();

            Assert.AreEqual(5f, power.SettledDemand);
            Assert.AreEqual(10f, power.SettledSupply);
            Assert.AreEqual(5f, power.DemandOf("foundry"), "And it is kept per group, which is what the priority screen reads.");
        }

        [Test]
        public void IsPowered_TrueWhenDemandAtOrBelowSupply()
        {
            var power = new PowerSystem();
            Frame(power, 10f, ("foundry", 10f));

            Assert.IsTrue(power.IsPowered());
        }

        [Test]
        public void IsPowered_FalseWhenDemandExceedsSupply()
        {
            var power = new PowerSystem();
            Frame(power, 10f, ("foundry", 11f));

            Assert.IsFalse(power.IsPowered());
        }

        [Test]
        public void Settle_ClearsPendingAccumulatorsForNextFrame()
        {
            var power = new PowerSystem();
            power.TryDraw("foundry", 5f);
            power.Settle();

            power.Settle(); // no new reports this frame

            Assert.AreEqual(0f, power.SettledDemand);
            Assert.AreEqual(0f, power.DemandOf("foundry"));
        }

        /// <summary>
        /// The whole point of the feature: a group the player put first keeps running through a
        /// shortage, and the shortage lands on whatever is below it.
        /// </summary>
        [Test]
        public void Allocation_ServesThePriorityOrderMostFirst()
        {
            var order = new PowerPriorityOrder();
            order.EnsureKnows(new[] { "datacenter", "extractor", "foundry" });

            var power = new PowerSystem { Priority = order };

            // 6 kW asked for, 4 kW supplied, in three groups of 2 kW.
            Frame(power, 4f, ("foundry", 2f), ("extractor", 2f), ("datacenter", 2f));

            Assert.AreEqual(2f, power.AllocatedTo("datacenter"), "First in the order, served in full.");
            Assert.AreEqual(2f, power.AllocatedTo("extractor"), "Second, and the supply still covers it.");
            Assert.AreEqual(0f, power.AllocatedTo("foundry"), "Third, and there is nothing left - however early it happened to ask.");

            Assert.IsTrue(power.TryDraw("datacenter", 2f));
            Assert.IsTrue(power.TryDraw("extractor", 2f));
            Assert.IsFalse(power.TryDraw("foundry", 2f));
        }

        /// <summary>
        /// The group the running total crosses is served <b>in part</b>: its instances draw until
        /// its share is spent, and the rest of them stop. That is the "1 sur 2 kW" the screen shows,
        /// and the reason the state is not a boolean - a deficit rarely falls exactly between two
        /// groups.
        /// </summary>
        [Test]
        public void Allocation_TheGroupTheShortageFallsOn_RunsSomeOfItsInstances()
        {
            var order = new PowerPriorityOrder();
            order.EnsureKnows(new[] { "datacenter", "extractor" });

            var power = new PowerSystem { Priority = order };

            // Two extractors of 1 kW each behind a datacenter that takes 2 of the 3 kW supplied.
            Frame(power, 3f, ("datacenter", 2f), ("extractor", 1f), ("extractor", 1f));

            Assert.AreEqual(2f, power.DemandOf("extractor"), "Two instances asked for 1 kW each.");
            Assert.AreEqual(1f, power.AllocatedTo("extractor"), "And the group was given 1 of its 2 kW.");

            Assert.IsTrue(power.TryDraw("extractor", 1f), "The first extractor runs...");
            Assert.IsFalse(power.TryDraw("extractor", 1f), "...and the second does not.");
        }

        /// <summary>A type nobody has arbitrated yet is served after everything that has been - never in front of it.</summary>
        [Test]
        public void AGroupTheOrderHasNeverHeardOf_IsServedLast()
        {
            var order = new PowerPriorityOrder();
            order.EnsureKnows(new[] { "foundry" });

            var power = new PowerSystem { Priority = order };

            // The stranger asks first, and still loses.
            Frame(power, 2f, ("newcomer", 2f), ("foundry", 2f));

            Assert.AreEqual(2f, power.AllocatedTo("foundry"));
            Assert.AreEqual(0f, power.AllocatedTo("newcomer"));
        }

        [Test]
        public void Recovery_IsImmediate_OnceSupplyCoversTheDemandAgain()
        {
            var power = new PowerSystem();

            Frame(power, 10f, ("foundry", 20f));
            Assert.IsFalse(power.IsPowered());
            Assert.AreEqual(10f, power.AllocatedTo("foundry"), "Short, but not nothing: it gets what there is.");

            Frame(power, 10f, ("foundry", 5f));

            Assert.IsTrue(power.IsPowered());
            Assert.AreEqual(5f, power.AllocatedTo("foundry"));
        }
    }
}
