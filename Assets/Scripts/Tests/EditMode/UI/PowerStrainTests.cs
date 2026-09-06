using Game.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode.UI
{
    /// <summary>
    /// The Top Bar's Power card turns amber above 85% of production drawn - the warning that comes
    /// before a deficit, not a milder version of one.
    /// </summary>
    public class PowerStrainTests
    {
        [TestCase(0f, 100f, false, TestName = "Nothing drawn")]
        [TestCase(50f, 100f, false, TestName = "Half")]
        [TestCase(84f, 100f, false, TestName = "Just under")]
        [TestCase(85f, 100f, false, TestName = "Exactly at the threshold - above 85%, not from it")]
        [TestCase(86f, 100f, true, TestName = "Just over")]
        [TestCase(100f, 100f, true, TestName = "Everything produced is drawn - saturated, but not short")]
        public void TheAmberBand_IsAbove85PercentOfProduction(float demand, float supply, bool expected)
        {
            Assert.AreEqual(expected, TopBarController.IsPowerStrained(demand, supply));
        }

        /// <summary>
        /// Past 100% it is a deficit, which the card already says in red. Amber there would replace a
        /// "you are short" with a "you are nearly short", which is the wrong way round.
        /// </summary>
        [Test]
        public void ADeficit_IsNotAmber()
        {
            Assert.IsFalse(TopBarController.IsPowerStrained(101f, 100f));
            Assert.IsFalse(TopBarController.IsPowerStrained(500f, 100f));
        }

        /// <summary>No production at all: a demand against nothing is a deficit, and zero against zero is not a ratio.</summary>
        [Test]
        public void NoProduction_IsNeverAmber()
        {
            Assert.IsFalse(TopBarController.IsPowerStrained(0f, 0f));
            Assert.IsFalse(TopBarController.IsPowerStrained(10f, 0f));
        }
    }
}
