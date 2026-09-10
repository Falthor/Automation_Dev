using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Wrecks;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Wrecks
{
    /// <summary>
    /// Where the wrecks are, and what survives a save.
    ///
    /// <b>The structure is what is being tested, not a loop.</b> Nothing checks proximity when the
    /// field is derived: rings separate radially and a jitter bounded to a third of the angular share
    /// separates angularly, by construction. So these tests assert that the construction actually
    /// holds - eight of them, each inside its own ring, no two of a ring closer than the bound
    /// allows - because if it does not, there is no rejection loop underneath to save it.
    /// </summary>
    public class WreckFieldTests
    {
        const int Seed = 20260907;

        /// <summary>The middle of the shipped map, where a generated world puts its Core.</summary>
        static readonly Vector2 CoreCentre = new Vector2(5000f, 5000f);

        /// <summary>ExplorerRobotSettings.MaxRadiusCells - how far a robot wanders, and the outer edge of the field.</summary>
        const float WanderLimit = 330f;

        /// <summary>The shipped rings, restated rather than read from the asset: a test that followed the setting could not fail when the setting is wrong.</summary>
        static WreckRingProfile Shipped => new WreckRingProfile(new[]
        {
            new WreckRing(40f, 75f, 2),
            new WreckRing(75f, 160f, 2),
            new WreckRing(160f, 330f, 4)
        });

        static WreckField NewField(int seed = Seed) => new WreckField(seed, CoreCentre, Shipped);

        // ---- How many, and where ----

        [Test]
        public void ThereAreExactlyEight_AllInsideTheDisc()
        {
            WreckField field = NewField();

            Assert.AreEqual(8, field.Sites.Count);
            Assert.AreEqual(8, Shipped.TotalCount, "the profile and the field have to agree about the count");

            foreach (WreckSite site in field.Sites)
            {
                float distance = Vector2.Distance(site.CentreCells, CoreCentre);
                Assert.AreEqual(distance, site.DistanceFromCoreCells, 0.001f, "the site's own distance has to be its actual distance");
                Assert.LessOrEqual(distance, WanderLimit, $"wreck {site.Index} sits {distance:F0} cells out, past anything a robot reaches");
            }
        }

        /// <summary>
        /// <b>No wreck outside its own ring.</b> The rings are what separates them radially and what
        /// makes the first one findable while the last is a long prospect - a wreck in the wrong band
        /// breaks both at once, and nothing else would notice.
        /// </summary>
        [Test]
        public void EveryWreckIsInsideItsOwnRing()
        {
            WreckField field = NewField();

            foreach (WreckSite site in field.Sites)
            {
                WreckRing ring = Shipped.Ring(site.Ring);
                float distance = site.DistanceFromCoreCells;

                Assert.GreaterOrEqual(distance, ring.InnerRadiusCells - 0.001f,
                    $"wreck {site.Index} is {distance:F1} cells out, inside ring {site.Ring}'s floor of {ring.InnerRadiusCells}");
                Assert.LessOrEqual(distance, ring.OuterRadiusCells + 0.001f,
                    $"wreck {site.Index} is {distance:F1} cells out, past ring {site.Ring}'s ceiling of {ring.OuterRadiusCells}");
            }
        }

        [Test]
        public void EachRingHoldsTheNumberItAsksFor()
        {
            WreckField field = NewField();
            var perRing = new int[Shipped.RingCount];

            foreach (WreckSite site in field.Sites) perRing[site.Ring]++;

            for (int ring = 0; ring < Shipped.RingCount; ring++)
            {
                Assert.AreEqual(Shipped.Ring(ring).Count, perRing[ring], $"ring {ring}");
            }
        }

        // ---- The separation the structure guarantees ----

        /// <summary>
        /// <b>Two wrecks of one ring are never closer than the jitter allows.</b> This is the property
        /// the whole design rests on: the bound on the jitter is what replaces a proximity check, so
        /// if it does not hold there is nothing behind it.
        ///
        /// Asserted against the derived minimum rather than a literal, and the literals are pinned
        /// once in the profile's own reasoning: two per ring gives 180° apart with 60° of play, so
        /// never closer than 60°; four gives 90° with 30° of play, so never closer than 30°.
        /// </summary>
        [Test]
        public void TwoWrecksOfARing_KeepTheirAngularDistance()
        {
            WreckField field = NewField();
            int compared = 0;

            for (int i = 0; i < field.Sites.Count; i++)
            {
                for (int j = i + 1; j < field.Sites.Count; j++)
                {
                    WreckSite a = field.Sites[i];
                    WreckSite b = field.Sites[j];
                    if (a.Ring != b.Ring) continue;

                    float separation = Mathf.Abs(Mathf.DeltaAngle(AngleOf(a), AngleOf(b)));
                    float minimum = Shipped.MinimumSeparationDegrees(a.Ring);

                    Assert.GreaterOrEqual(separation, minimum - 0.01f,
                        $"wrecks {a.Index} and {b.Index} of ring {a.Ring} are {separation:F1}° apart, under the {minimum:F1}° the jitter bound guarantees");

                    compared++;
                }
            }

            Assert.Greater(compared, 0, "the shipped rings have to hold at least one pair to compare");
        }

        /// <summary>
        /// The jitter has to actually move things, or the eight would sit on exact multiples of the
        /// share and read as a pattern - which is the one thing eight points on a big disc must not
        /// do. Measured, because "it is random" is not an assertion.
        /// </summary>
        [Test]
        public void TheJitterActuallyBreaksTheRegularity()
        {
            WreckField field = NewField();
            float worst = 0f;

            foreach (WreckSite site in field.Sites)
            {
                float share = Shipped.SpacingDegrees(site.Ring);
                float offset = Mathf.Abs(Mathf.DeltaAngle(AngleOf(site), site.RankInRing * share));

                worst = Mathf.Max(worst, offset);
                Assert.LessOrEqual(offset, share * WreckRingProfile.AngularJitterFraction + 0.01f,
                    $"wreck {site.Index} strayed {offset:F1}° from its share, past the bound");
            }

            TestContext.Out.WriteLine($"largest stray from an exact share: {worst:F1}°");
            Assert.Greater(worst, 5f, "if nothing strays, eight wrecks sit on exact multiples and read as a pattern");
        }

        static float AngleOf(WreckSite site)
        {
            Vector2 fromCore = site.CentreCells - CoreCentre;
            return Mathf.Atan2(fromCore.y, fromCore.x) * Mathf.Rad2Deg;
        }

        // ---- Derived, so the same seed is the same map ----

        [Test]
        public void TheSameSeed_GivesTheSameMap()
        {
            WreckField first = NewField();
            WreckField second = NewField();

            for (int i = 0; i < first.Sites.Count; i++)
            {
                Assert.AreEqual(first.Sites[i].CentreCells, second.Sites[i].CentreCells, $"wreck {i}");
                Assert.AreEqual(first.Sites[i].TypeIndex, second.Sites[i].TypeIndex, $"wreck {i}'s type");
                Assert.AreEqual(first.Sites[i].Origin, second.Sites[i].Origin, $"wreck {i}'s footprint");
            }
        }

        [Test]
        public void ADifferentSeed_MovesThem()
        {
            WreckField a = NewField(Seed);
            WreckField b = NewField(Seed + 1);

            int moved = 0;
            for (int i = 0; i < a.Sites.Count; i++)
            {
                if (a.Sites[i].CentreCells != b.Sites[i].CentreCells) moved++;
            }

            Assert.AreEqual(a.Sites.Count, moved, "a new world should not put its wrecks in the same places");
        }

        [Test]
        public void EveryTypeIsOneOfTheThree_AndRepeatsAreAllowed()
        {
            WreckField field = NewField();
            var used = new HashSet<int>();

            foreach (WreckSite site in field.Sites)
            {
                Assert.GreaterOrEqual(site.TypeIndex, 0);
                Assert.Less(site.TypeIndex, WreckField.TypeCount);
                used.Add(site.TypeIndex);
            }

            // Not an assertion that they repeat - eight draws from three could in principle be all
            // distinct - but a statement that nothing forbids it. Printed so the spread is visible.
            TestContext.Out.WriteLine($"{used.Count} of {WreckField.TypeCount} wreck types used across {field.Sites.Count} sites");
        }

        // ---- Discovery ----

        [Test]
        public void ARobotPassingOverOne_FindsIt()
        {
            WreckField field = NewField();
            WreckSite target = field.Sites[0];

            Assert.AreEqual(0, field.DiscoveredCount);

            // Six cells is ExplorerRobotSettings.RevealRadiusCells - a wreck is found when the ground
            // it stands on is uncovered, and nothing else.
            Assert.AreEqual(0, field.DiscoverWithin(target.CentreCells + new Vector2(50f, 0f), 6f),
                "fifty cells away is not passing over it");

            Assert.AreEqual(1, field.DiscoverWithin(target.CentreCells, 6f));
            Assert.IsTrue(target.Discovered);
            Assert.AreEqual(1, field.DiscoveredCount);

            Assert.AreEqual(0, field.DiscoverWithin(target.CentreCells, 6f), "finding it twice is not finding two");
        }

        [Test]
        public void FindingOne_AnnouncesIt()
        {
            WreckField field = NewField();
            var announced = new List<WreckSite>();
            field.Discovered += announced.Add;

            field.DiscoverWithin(field.Sites[3].CentreCells, 6f);

            Assert.AreEqual(1, announced.Count, "without the event nothing draws it");
            Assert.AreSame(field.Sites[3], announced[0]);
        }

        // ---- What is stored, and only that ----

        /// <summary>
        /// <b>A wreck found stays found across a save.</b> Only the discovered set travels: the field
        /// is rebuilt from the seed on the other side, and the indices are matched against it.
        /// </summary>
        [Test]
        public void ADiscoveredWreck_SurvivesARoundTrip()
        {
            WreckField before = NewField();
            before.DiscoverWithin(before.Sites[1].CentreCells, 6f);
            before.DiscoverWithin(before.Sites[6].CentreCells, 6f);
            Assert.AreEqual(2, before.DiscoveredCount);

            string state = before.CaptureState();

            WreckField after = NewField();
            after.RestoreState(state);

            Assert.AreEqual(2, after.DiscoveredCount);
            for (int i = 0; i < after.Sites.Count; i++)
            {
                Assert.AreEqual(before.Sites[i].Discovered, after.Sites[i].Discovered, $"wreck {i}");
            }
        }

        [Test]
        public void RestoringNothing_IsAWorldNobodyHasFoundAnythingIn()
        {
            WreckField field = NewField();
            field.DiscoverEverything();
            Assert.AreEqual(8, field.DiscoveredCount);

            field.RestoreState(null);
            Assert.AreEqual(0, field.DiscoveredCount);

            field.DiscoverEverything();
            field.RestoreState(string.Empty);
            Assert.AreEqual(0, field.DiscoveredCount);
        }

        /// <summary>
        /// A save written when the rings held more wrecks names indices this field does not have.
        /// That has to cost those wrecks, not the save - the same tolerance every other restore in
        /// this project has.
        /// </summary>
        [Test]
        public void RestoringNonsense_CostsTheWrecksAndNotTheSave()
        {
            WreckField field = NewField();

            Assert.DoesNotThrow(() => field.RestoreState("0,99,-4,abc,,2,2"));

            Assert.IsTrue(field.Sites[0].Discovered);
            Assert.IsTrue(field.Sites[2].Discovered);
            Assert.AreEqual(2, field.DiscoveredCount, "a repeated index is one wreck, not two");
        }

        // ---- The footprint ----

        [Test]
        public void TheFootprintIsThreeCellsCentredOnTheSite()
        {
            WreckField field = NewField();

            foreach (WreckSite site in field.Sites)
            {
                Assert.AreEqual(3, WreckSite.FootprintCells);
                Assert.AreEqual(Mathf.FloorToInt(site.CentreCells.x) - 1, site.Origin.X, $"wreck {site.Index}");
                Assert.AreEqual(Mathf.FloorToInt(site.CentreCells.y) - 1, site.Origin.Y, $"wreck {site.Index}");
            }
        }
    }
}
