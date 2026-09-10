using Game.Data;
using NUnit.Framework;

namespace Game.Tests.EditMode.Data
{
    /// <summary>
    /// The ramp that makes a cluster grow with distance.
    ///
    /// <b>The two ends are literals, and they are the decision.</b> Six to ten tiles just outside the
    /// Core's reach, ten to fifteen at the limit a robot wanders to. Everything between is
    /// interpolation, and everything about the shape of the map that a player will notice is in those
    /// four numbers - so they are pinned here once, and the catalog's own tests assert against the
    /// profile rather than repeating them.
    /// </summary>
    public class OreClusterProfileTests
    {
        /// <summary>CoreRuntime.ExtendedActionRadiusCells and ExplorerRobotSettings.MaxRadiusCells.</summary>
        const float NearRadius = 32f;
        const float FarRadius = 330f;

        static OreClusterProfile Shipped => new OreClusterProfile(12, 6, 10, 10, 15, NearRadius, FarRadius);

        [Test]
        public void AtTheCoresEdge_ItIsSixToTen()
        {
            OreClusterProfile profile = Shipped;

            Assert.AreEqual(6, profile.MinTilesAt(NearRadius));
            Assert.AreEqual(10, profile.MaxTilesAt(NearRadius));
        }

        [Test]
        public void AtTheWanderLimit_ItIsTenToFifteen()
        {
            OreClusterProfile profile = Shipped;

            Assert.AreEqual(10, profile.MinTilesAt(FarRadius));
            Assert.AreEqual(15, profile.MaxTilesAt(FarRadius));
        }

        /// <summary>
        /// Clamped at both ends rather than extrapolated. Inside the near radius nothing is derived at
        /// all, so the value is only ever asked for by a caller that has not checked; past the far
        /// radius a robot cannot reach, and a cluster that kept growing would be a promise the game
        /// never keeps.
        /// </summary>
        [Test]
        public void OutsideBothEnds_ItStopsRatherThanRunningOn()
        {
            OreClusterProfile profile = Shipped;

            Assert.AreEqual(6, profile.MinTilesAt(0f), "inside the Core's reach");
            Assert.AreEqual(10, profile.MaxTilesAt(0f));

            Assert.AreEqual(10, profile.MinTilesAt(FarRadius * 3f), "far past anything reachable");
            Assert.AreEqual(15, profile.MaxTilesAt(FarRadius * 3f));
        }

        /// <summary>
        /// Never smaller further out. Obvious from a lerp between rising values and worth pinning
        /// anyway: the day the two bands are tuned into crossing each other, this is what says so
        /// rather than a map that quietly rewards staying home.
        /// </summary>
        [Test]
        public void ItNeverShrinksWithDistance()
        {
            OreClusterProfile profile = Shipped;

            int previousFloor = 0;
            int previousCeiling = 0;

            for (float distance = 0f; distance <= FarRadius + 50f; distance += 5f)
            {
                int floor = profile.MinTilesAt(distance);
                int ceiling = profile.MaxTilesAt(distance);

                Assert.GreaterOrEqual(floor, previousFloor, $"the floor fell at {distance} cells");
                Assert.GreaterOrEqual(ceiling, previousCeiling, $"the ceiling fell at {distance} cells");
                Assert.GreaterOrEqual(ceiling, floor, $"the band inverted at {distance} cells");

                previousFloor = floor;
                previousCeiling = ceiling;
            }
        }

        /// <summary>
        /// The ramp actually ramps in between, rather than jumping at one end. Halfway out should be
        /// halfway up, which is what a player reads as ore getting better the further they go.
        /// </summary>
        [Test]
        public void HalfwayOut_ItIsHalfwayUp()
        {
            OreClusterProfile profile = Shipped;
            float halfway = (NearRadius + FarRadius) * 0.5f;

            Assert.AreEqual(0.5f, profile.RampAt(halfway), 0.001f);
            Assert.AreEqual(8, profile.MinTilesAt(halfway));
            // 12, not 13: the middle of 10 to 15 is 12.5, and Mathf.RoundToInt rounds a half to the
            // even number. Worth the literal rather than a formula, because that is the kind of
            // detail a recomputed expectation would agree with while both were wrong.
            Assert.AreEqual(12, profile.MaxTilesAt(halfway));
        }

        /// <summary>
        /// A profile handed values that contradict each other has to answer something usable rather
        /// than propagate them: a ceiling under its floor, or a far radius inside the near one, are
        /// typos in an asset and should cost a shrug, not an exception in world generation.
        /// </summary>
        [Test]
        public void ContradictoryFigures_AreStraightenedOut()
        {
            var backwards = new OreClusterProfile(0, 10, 4, 20, 2, 500f, 100f);

            Assert.AreEqual(1, backwards.OneSectorIn, "a rate of zero would divide by it");
            Assert.GreaterOrEqual(backwards.NearMaxTiles, backwards.NearMinTiles);
            Assert.GreaterOrEqual(backwards.FarMaxTiles, backwards.FarMinTiles);
            Assert.Greater(backwards.FarRadiusCells, backwards.NearRadiusCells);

            Assert.DoesNotThrow(() => backwards.MaxTilesAt(250f));
        }
    }
}
