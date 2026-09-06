using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Sites;
using Game.Tests.EditMode.TestSupport;
using Game.UI;
using NUnit.Framework;

using SupplyLine = Game.Gameplay.Sites.ConstructionSiteRuntime.SupplyLine;

namespace Game.Tests.EditMode.UI
{
    /// <summary>
    /// The construction site panel's reading rules, held apart from the panel itself so they can be
    /// checked without a UIDocument.
    ///
    /// The panel answers two different questions in two different places, and these tests keep them
    /// apart: the bars describe the <b>stock</b> committed to a site ("must I produce?"), the service
    /// line describes the <b>robots</b> ("is this moving?"). Conflating them is what made four sites
    /// placed at once all report delivery under way when only one was being served.
    /// </summary>
    public class ConstructionSitePanelFormatTests
    {
        static SupplyLine Line(int delivered, int reserved, int total)
            => new SupplyLine("iron_plate", total, delivered, reserved, total - delivered - reserved);

        // --- The bars: stock ---

        /// <summary>
        /// The rule the whole panel is built to make readable. The useful split is not between
        /// arrived and reserved - it is between "the system has this covered" and "I have to produce
        /// it" - so a bar filled end to end means do nothing, whether it is solid or hatched.
        ///
        /// The trap it exists to avoid is flagging on Delivered &lt; Total, which would paint an
        /// ingredient red for the whole time its material sits reserved with this site's name on it.
        /// </summary>
        [Test]
        public void TheDangerColour_KeysOnMissingMaterial_NotOnAnUndeliveredOne()
        {
            Assert.IsFalse(ConstructionSitePanelController.IsAlarming(Line(delivered: 0, reserved: 40, total: 40)),
                "Nothing has arrived, but all of it is reserved: the player has nothing to do, so this "
                + "must not be flagged. Delivered < Total would have flagged it.");

            Assert.IsFalse(ConstructionSitePanelController.IsAlarming(Line(delivered: 15, reserved: 25, total: 40)),
                "Part arrived, the rest reserved - still fully covered.");

            Assert.IsFalse(ConstructionSitePanelController.IsAlarming(Line(delivered: 40, reserved: 0, total: 40)),
                "And a finished ingredient is obviously not a problem.");

            Assert.IsTrue(ConstructionSitePanelController.IsAlarming(Line(delivered: 10, reserved: 0, total: 40)),
                "Thirty missing and nothing reserved: this one is the player's to produce.");

            Assert.IsTrue(ConstructionSitePanelController.IsAlarming(Line(delivered: 10, reserved: 20, total: 40)),
                "Partly covered is not covered - ten units have no source anywhere, so it is flagged "
                + "even though something is reserved.");
        }

        /// <summary>
        /// The count is what the bar shows as filled, so the two can never contradict each other.
        /// A split numerator - "2 + 3 / 5" - would put the same breakdown in two places, and "3 / 8"
        /// under a bar filled end to end would leave the reader deciding which one is lying.
        /// </summary>
        [Test]
        public void TheCount_MatchesTheFilledPartOfTheBar()
        {
            Assert.AreEqual("5 / 5", ConstructionSitePanelController.CountText(Line(delivered: 2, reserved: 3, total: 5)),
                "Everything secured, whatever the split between arrived and reserved.");

            Assert.AreEqual("5 / 5", ConstructionSitePanelController.CountText(Line(delivered: 5, reserved: 0, total: 5)),
                "And the same count when it is all arrived - a full bar always reads as a full count.");

            Assert.AreEqual("3 / 8", ConstructionSitePanelController.CountText(Line(delivered: 1, reserved: 2, total: 8)));
            Assert.AreEqual("0 / 30", ConstructionSitePanelController.CountText(Line(delivered: 0, reserved: 0, total: 30)));
        }

        /// <summary>The case the reservation counter exists for, and the one a naive bar gets wrong: nothing delivered, everything secured.</summary>
        [Test]
        public void AllReservedAndNothingArrived_FillsTheBarEntirely_WithNoSolidPart()
        {
            SupplyLine line = Line(delivered: 0, reserved: 40, total: 40);

            Assert.AreEqual(0f, ConstructionSitePanelController.FillPercent(line.Delivered, line.Total), 0.001f,
                "No solid segment at all...");
            Assert.AreEqual(100f, ConstructionSitePanelController.FillPercent(line.Reserved, line.Total), 0.001f,
                "...and the hatched one covers the whole track.");
            Assert.AreEqual(0, line.Missing, "Nothing neutral shows through, so the bar reads 'do nothing'.");
        }

        [Test]
        public void TheBarSegments_ShareOneTrack_AndNeverOverrunIt()
        {
            SupplyLine half = Line(delivered: 10, reserved: 10, total: 40);
            Assert.AreEqual(25f, ConstructionSitePanelController.FillPercent(half.Delivered, half.Total), 0.001f);
            Assert.AreEqual(25f, ConstructionSitePanelController.FillPercent(half.Reserved, half.Total), 0.001f);

            SupplyLine full = Line(delivered: 12, reserved: 28, total: 40);
            float filled = ConstructionSitePanelController.FillPercent(full.Delivered, full.Total)
                         + ConstructionSitePanelController.FillPercent(full.Reserved, full.Total);
            Assert.AreEqual(100f, filled, 0.001f, "Delivered plus reserved with nothing missing fills the bar exactly.");
        }

        /// <summary>A building that costs nothing has a bill of zero, and a bar cannot be a share of nothing.</summary>
        [Test]
        public void AnIngredientWithNoCost_FillsNothing_RatherThanDividingByZero()
        {
            Assert.AreEqual(0f, ConstructionSitePanelController.FillPercent(0, 0), 0.001f);
            Assert.AreEqual(0f, ConstructionSitePanelController.FillPercent(5, 0), 0.001f);
        }

        // --- The service line: robots ---

        static ConstructionSiteRuntime NewSite(int id = 1)
        {
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            var segment = new StorageRuntime(definition, new GridCoord(id * 4, 4), Direction.North);
            return new ConstructionSiteRuntime(id, segment);
        }

        static BuilderRobotRuntime NewRobot(ConstructionSiteRuntime target, BuilderRobotState state)
            => new BuilderRobotRuntime(0, UnityEngine.Vector2.zero) { TargetSite = target, State = state };

        /// <summary>
        /// Only a robot actually walking toward this site counts. One assigned to it but heading for
        /// a chest is moving the other way, and a reservation is not a robot at all - which is the
        /// whole reason this line exists beside the bars rather than inside them.
        /// </summary>
        [Test]
        public void OnlyRobotsCarryingTowardThisSite_CountAsApproaching()
        {
            ConstructionSiteRuntime site = NewSite(1);
            ConstructionSiteRuntime other = NewSite(2);

            var robots = new List<BuilderRobotRuntime>
            {
                NewRobot(site, BuilderRobotState.MovingToSource),
                NewRobot(site, BuilderRobotState.Loading),
                NewRobot(other, BuilderRobotState.MovingToSite),
                NewRobot(null, BuilderRobotState.Idle)
            };

            Assert.AreEqual(0, ConstructionSitePanelController.RobotsApproaching(robots, site),
                "Fetching for this site is not approaching it, and another site's robot is not ours.");

            robots.Add(NewRobot(site, BuilderRobotState.MovingToSite));

            Assert.AreEqual(1, ConstructionSitePanelController.RobotsApproaching(robots, site));
        }

        [Test]
        public void TheServiceLine_SwitchesBetweenApproachingAndWaiting()
        {
            Assert.AreEqual("en attente d'un robot", ConstructionSitePanelController.ServiceText(0),
                "Material may be entirely reserved and still have nothing moving toward it - that is "
                + "precisely the state three of four simultaneous sites are in.");

            Assert.AreEqual("1 robot en approche", ConstructionSitePanelController.ServiceText(1));
            Assert.AreEqual("2 robots en approche", ConstructionSitePanelController.ServiceText(2));
        }

        /// <summary>
        /// The scenario that prompted the split: several sites placed at once, all of them fully
        /// reserved, and only one of them being served. The bars are identical; the service line is
        /// the only thing that separates them.
        /// </summary>
        [Test]
        public void SeveralFullyReservedSites_ReadIdenticallyOnTheBars_AndDifferOnlyOnService()
        {
            ConstructionSiteRuntime served = NewSite(1);
            ConstructionSiteRuntime waiting = NewSite(2);

            var robots = new List<BuilderRobotRuntime> { NewRobot(served, BuilderRobotState.MovingToSite) };

            SupplyLine sameBill = Line(delivered: 0, reserved: 5, total: 5);
            Assert.AreEqual("5 / 5", ConstructionSitePanelController.CountText(sameBill));
            Assert.IsFalse(ConstructionSitePanelController.IsAlarming(sameBill),
                "Neither site is the player's problem: both have their material secured.");

            Assert.AreEqual("1 robot en approche",
                ConstructionSitePanelController.ServiceText(ConstructionSitePanelController.RobotsApproaching(robots, served)));

            Assert.AreEqual("en attente d'un robot",
                ConstructionSitePanelController.ServiceText(ConstructionSitePanelController.RobotsApproaching(robots, waiting)));
        }
    }
}
