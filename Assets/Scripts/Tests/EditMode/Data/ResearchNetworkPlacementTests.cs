using Game.Data;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Data
{
    /// <summary>
    /// The convention the research panel and the research tree editor share (ResearchNetworkPlacement).
    /// Not ergonomics: if the two read it differently, every node the editor places is drawn somewhere
    /// else in the game, mirrored or turned.
    /// </summary>
    public class ResearchNetworkPlacementTests
    {
        const float Step = 10f;
        const float Tolerance = 1e-3f;

        [Test]
        public void AnAngleOfNinety_IsStraightUp_AtTheTierTimesTheStep()
        {
            Vector2 offset = ResearchNetworkPlacement.Offset(2f, 90f, Step);

            Assert.AreEqual(0f, offset.x, Tolerance);
            Assert.AreEqual(2f * Step, offset.y, Tolerance);
        }

        [Test]
        public void APosition_ComesBackAsTheTierAndAngleItWasMadeFrom_OnARingOrBetweenTwo()
        {
            foreach (float tier in new[] { 1f, 2f, 2.5f, 5.3f })
            {
                foreach (float angle in new[] { 0f, 37.5f, 150f, 270f, 359f })
                {
                    ResearchNetworkPlacement.FromOffset(ResearchNetworkPlacement.Offset(tier, angle, Step), Step, out float backTier, out float backAngle);

                    Assert.AreEqual(tier, backTier, Tolerance, $"tier {tier} at {angle}");
                    Assert.AreEqual(angle, backAngle, Tolerance, $"tier {tier} at {angle}");
                }
            }
        }

        [Test]
        public void AnOffsetBetweenRings_StaysBetweenThem_AndNeverInsideTheFirst()
        {
            ResearchNetworkPlacement.FromOffset(new Vector2(24f, 0f), Step, out float tier, out _);
            Assert.AreEqual(2.4f, tier, Tolerance, "The convention does not round - settling a dropped node is the editor's choice.");

            ResearchNetworkPlacement.FromOffset(new Vector2(1f, 0f), Step, out tier, out _);
            Assert.AreEqual(1f, tier, Tolerance, "The centre is the Datacenter's.");

            ResearchNetworkPlacement.FromOffset(new Vector2(0f, -5f), Step, out _, out float angle);
            Assert.AreEqual(270f, angle, Tolerance, "Angles are given in [0, 360).");
        }
    }
}
