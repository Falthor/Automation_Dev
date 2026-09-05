using Game.UI;
using NUnit.Framework;

using SupplyLine = Game.Gameplay.Sites.ConstructionSiteRuntime.SupplyLine;

namespace Game.Tests.EditMode.UI
{
    /// <summary>
    /// The construction site panel's two reading rules, held apart from the panel itself so they can
    /// be checked without a UIDocument: what the count line says, and which ingredient is coloured
    /// as a problem.
    /// </summary>
    public class ConstructionSitePanelFormatTests
    {
        static SupplyLine Line(int delivered, int enRoute, int total)
            => new SupplyLine("iron_plate", total, delivered, enRoute, total - delivered - enRoute);

        /// <summary>
        /// The rule the whole panel is built to make readable. The useful split is not between
        /// arrived and en route - it is between "the system is handling this" and "I have to produce
        /// it" - so a bar filled end to end means do nothing, whether it is solid or hatched.
        ///
        /// The trap it exists to avoid is flagging on Delivered &lt; Total, which would paint an
        /// ingredient red for the whole time the robots are busy fetching exactly what it needs.
        /// </summary>
        [Test]
        public void TheDangerColour_KeysOnMissingMaterial_NotOnAnUnfinishedDelivery()
        {
            Assert.IsFalse(ConstructionSitePanelController.IsAlarming(Line(delivered: 0, enRoute: 40, total: 40)),
                "Nothing has arrived, but all of it is reserved and coming: the player has nothing to do, "
                + "so this must not be flagged. Delivered < Total would have flagged it.");

            Assert.IsFalse(ConstructionSitePanelController.IsAlarming(Line(delivered: 15, enRoute: 25, total: 40)),
                "Part arrived, the rest on its way - still fully handled.");

            Assert.IsFalse(ConstructionSitePanelController.IsAlarming(Line(delivered: 40, enRoute: 0, total: 40)),
                "And a finished ingredient is obviously not a problem.");

            Assert.IsTrue(ConstructionSitePanelController.IsAlarming(Line(delivered: 10, enRoute: 0, total: 40)),
                "Thirty missing and nothing coming: this one is the player's to produce.");

            Assert.IsTrue(ConstructionSitePanelController.IsAlarming(Line(delivered: 10, enRoute: 20, total: 40)),
                "Partly covered is not covered - ten units still have no source anywhere, so it is "
                + "flagged even though something is in flight.");
        }

        [Test]
        public void TheCount_ShowsTheAddition_OnlyWhileSomethingIsInFlight()
        {
            Assert.AreEqual("10 + 20 / 40", ConstructionSitePanelController.CountText(Line(delivered: 10, enRoute: 20, total: 40)),
                "I have ten, twenty are coming, I need forty.");

            Assert.AreEqual("0 / 30", ConstructionSitePanelController.CountText(Line(delivered: 0, enRoute: 0, total: 30)),
                "With nothing in flight the addition is noise and disappears.");

            Assert.AreEqual("30 / 30", ConstructionSitePanelController.CountText(Line(delivered: 30, enRoute: 0, total: 30)));
        }

        /// <summary>The two filled segments are laid out left to right in one track, so their shares have to be shares of the same whole and never overrun it.</summary>
        [Test]
        public void TheBarSegments_ShareOneTrack_AndNeverOverrunIt()
        {
            SupplyLine half = Line(delivered: 10, enRoute: 10, total: 40);
            Assert.AreEqual(25f, ConstructionSitePanelController.FillPercent(half.Delivered, half.Total), 0.001f);
            Assert.AreEqual(25f, ConstructionSitePanelController.FillPercent(half.EnRoute, half.Total), 0.001f);

            SupplyLine full = Line(delivered: 12, enRoute: 28, total: 40);
            float filled = ConstructionSitePanelController.FillPercent(full.Delivered, full.Total)
                         + ConstructionSitePanelController.FillPercent(full.EnRoute, full.Total);
            Assert.AreEqual(100f, filled, 0.001f, "Delivered plus en route with nothing missing fills the bar exactly.");
        }

        /// <summary>A building that costs nothing has a bill of zero, and a bar cannot be a share of nothing.</summary>
        [Test]
        public void AnIngredientWithNoCost_FillsNothing_RatherThanDividingByZero()
        {
            Assert.AreEqual(0f, ConstructionSitePanelController.FillPercent(0, 0), 0.001f);
            Assert.AreEqual(0f, ConstructionSitePanelController.FillPercent(5, 0), 0.001f);
        }
    }
}
