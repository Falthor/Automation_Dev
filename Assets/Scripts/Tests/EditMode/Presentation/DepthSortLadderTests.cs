using System.Collections.Generic;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// The depth window that follows the camera.
    ///
    /// Two properties carry the whole design and both fail silently if broken: what is on screen must
    /// stay correctly ordered however far the camera has travelled, and panning must not re-rank
    /// everything every frame. The old absolute ladder failed the first one at scale - by clamping,
    /// with no error - which is exactly why it had to be replaced.
    /// </summary>
    public class DepthSortLadderTests
    {
        const float MaxViewHalfHeight = 30f;

        static DepthSortLadder NewLadder() => new DepthSortLadder(MaxViewHalfHeight);

        static SpriteRenderer NewRenderer() => new GameObject("depth-sorted").AddComponent<SpriteRenderer>();

        readonly List<GameObject> _spawned = new List<GameObject>();

        SpriteRenderer TrackedRenderer()
        {
            SpriteRenderer renderer = NewRenderer();
            _spawned.Add(renderer.gameObject);
            return renderer;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        // ---- The rule itself ----

        [Test]
        public void ALowerObject_DrawsInFrontOfAHigherOne()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(0f);

            Assert.Greater(ladder.Order(4f, SortingBands.SubSprite), ladder.Order(9f, SortingBands.SubSprite));
        }

        [Test]
        public void ARowAlwaysOutranksASubLayer()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(0f);

            Assert.Greater(ladder.Order(7f, SortingBands.SubSilhouette), ladder.Order(8f, SortingBands.SubOverlay));
        }

        // ---- The property the old ladder lost: correctness far from the origin ----

        /// <summary>
        /// The whole reason for the change. At 9 000 cells the absolute ladder clamped and two
        /// neighbours collapsed onto one order; here the answer is the same as at the origin, because
        /// the window travels with the camera.
        /// </summary>
        [Test]
        public void TwoNeighboursStaySortedAtTheFarEndOfALargeMap()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(9000f);

            int near = ladder.Order(8998f, SortingBands.SubSprite);
            int far = ladder.Order(9002f, SortingBands.SubSprite);

            Assert.Greater(near, far, "The lower of the two must still draw in front.");
            Assert.AreNotEqual(near, far, "Clamped to the same order is exactly the silent failure this replaces.");
        }

        [Test]
        public void EveryRankStaysInsideTheSortedBand_WhereverTheCameraIs()
        {
            var ladder = NewLadder();

            foreach (float cameraY in new[] { 0f, 150f, 5000f, 10000f })
            {
                ladder.FollowCamera(cameraY);

                foreach (float offset in new[] { -200f, -30f, 0f, 30f, 200f })
                {
                    int order = ladder.Order(cameraY + offset, SortingBands.SubSprite);
                    Assert.GreaterOrEqual(order, SortingBands.SortedFirst);
                    Assert.LessOrEqual(order, SortingBands.SortedLast);
                }
            }
        }

        /// <summary>Everything the widest possible view can contain has to be ranked without clamping, or two visible objects tie.</summary>
        [Test]
        public void TheWholeVisibleViewFitsInTheWindow_WithoutClamping()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(500f);

            int top = ladder.Order(500f + MaxViewHalfHeight, SortingBands.SubSprite);
            int bottom = ladder.Order(500f - MaxViewHalfHeight, SortingBands.SubSprite);

            Assert.Greater(top, SortingBands.SortedFirst, "The top of the view is clamped against the window's ceiling.");
            Assert.Less(bottom, SortingBands.SortedLast, "The bottom of the view is clamped against the window's floor.");
        }

        // ---- Re-anchoring is rare ----

        [Test]
        public void PanningWithinTheSlack_DoesNotReAnchor()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(0f);
            int anchors = ladder.AnchorCount;

            for (int i = 0; i < 100; i++) ladder.FollowCamera(i * 0.1f); // 10 world units, well inside the slack

            Assert.AreEqual(anchors, ladder.AnchorCount);
        }

        [Test]
        public void CrossingAWholeMap_ReAnchorsAHandfulOfTimes_NotOncePerFrame()
        {
            var ladder = NewLadder();

            // 10 000 world units in 1-unit steps: 10 000 frames of continuous panning.
            for (int y = 0; y <= 10000; y++) ladder.FollowCamera(y);

            Assert.Less(ladder.AnchorCount, 200, $"Re-anchored {ladder.AnchorCount} times crossing the map.");
            Assert.Greater(ladder.AnchorCount, 1, "It does have to follow, though.");
        }

        // ---- Registration ----

        [Test]
        public void ARegisteredRendererGetsItsRankImmediately_AndAgainOnReAnchoring()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(0f);

            SpriteRenderer renderer = TrackedRenderer();
            ladder.Register(renderer, 10f, SortingBands.SubSprite);
            int atOrigin = renderer.sortingOrder;

            Assert.AreEqual(ladder.Order(10f, SortingBands.SubSprite), atOrigin);

            ladder.FollowCamera(5000f);
            Assert.AreNotEqual(atOrigin, renderer.sortingOrder, "A re-anchoring has to reach renderers already handed out.");
        }

        /// <summary>Relative order is what Unity reads, and it is the thing a re-anchoring must not disturb.</summary>
        [Test]
        public void ReAnchoring_KeepsTheRelativeOrderOfWhatIsStillInTheWindow()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(1000f);

            SpriteRenderer lower = TrackedRenderer();
            SpriteRenderer upper = TrackedRenderer();
            ladder.Register(lower, 995f, SortingBands.SubSprite);
            ladder.Register(upper, 1005f, SortingBands.SubSprite);

            Assert.Greater(lower.sortingOrder, upper.sortingOrder);

            // Far enough to force a re-anchoring, close enough that both are still inside the window.
            ladder.FollowCamera(1100f);

            Assert.Greater(ladder.AnchorCount, 1, "The pan has to have actually moved the window.");
            Assert.Greater(lower.sortingOrder, upper.sortingOrder, "Still true after the window moved.");
        }

        /// <summary>
        /// The trade the whole design rests on, asserted rather than assumed: once the camera has
        /// travelled far enough, objects left behind fall outside the window and collapse onto one
        /// order. That is not a defect - they are nowhere near the screen, and giving up on ordering
        /// them is exactly what buys a rank that fits in a short at any map size.
        /// </summary>
        [Test]
        public void ObjectsFarOutsideTheWindow_AreAllowedToTie()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(1000f);

            SpriteRenderer lower = TrackedRenderer();
            SpriteRenderer upper = TrackedRenderer();
            ladder.Register(lower, 995f, SortingBands.SubSprite);
            ladder.Register(upper, 1005f, SortingBands.SubSprite);

            ladder.FollowCamera(1000f + DepthSortLadder.WindowWorldHeight);

            Assert.AreEqual(lower.sortingOrder, upper.sortingOrder);
        }

        [Test]
        public void UnregisteringStopsRanking_AndDestroyedRenderersAreDroppedOnTheirOwn()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(0f);

            SpriteRenderer kept = TrackedRenderer();
            SpriteRenderer dropped = TrackedRenderer();
            ladder.Register(kept, 1f, SortingBands.SubSprite);
            ladder.Register(dropped, 2f, SortingBands.SubSprite);
            Assert.AreEqual(2, ladder.TrackedCount);

            ladder.Unregister(dropped);
            Assert.AreEqual(1, ladder.TrackedCount);

            Object.DestroyImmediate(kept.gameObject);
            _spawned.Clear();
            ladder.FollowCamera(5000f);

            Assert.AreEqual(0, ladder.TrackedCount, "A destroyed renderer must not be kept forever.");
        }

        // ---- Raised scene decor ----

        /// <summary>
        /// The reason raised decor is in the sorted band at all: a big rock standing lower than a
        /// building has to draw in front of it. This goes through DepthSortedDecor, the path the
        /// wild scatter actually uses, rather than asserting the ladder's arithmetic again.
        /// </summary>
        [Test]
        public void ARaisedRock_DrawsInFrontOfABuildingStandingHigherUp()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(0f);

            SpriteRenderer building = TrackedRenderer();
            ladder.Register(building, 20f, SortingBands.SubSprite);

            DepthSortedDecor rock = NewDecorAt(new Vector3(0f, 14f, 0f));
            rock.RegisterWith(ladder);

            Assert.Greater(rock.GetComponent<SpriteRenderer>().sortingOrder, building.sortingOrder);
        }

        [Test]
        public void ARaisedRock_IsRerankedWhenTheWindowMoves()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(0f);

            DepthSortedDecor rock = NewDecorAt(new Vector3(0f, 10f, 0f));
            rock.RegisterWith(ladder);
            int before = rock.GetComponent<SpriteRenderer>().sortingOrder;

            ladder.FollowCamera(4000f);

            Assert.AreNotEqual(before, rock.GetComponent<SpriteRenderer>().sortingOrder);
        }

        /// <summary>
        /// The wild scatter regenerates on every Play Mode entry, after GameRuntime.Start() has
        /// already swept the scene - so a second sweep is what ranks it, and sweeping twice must not
        /// leave the same sprite on the ladder twice.
        /// </summary>
        [Test]
        public void RegisteringTheSameDecorTwice_DoesNotDuplicateIt()
        {
            var ladder = NewLadder();
            ladder.FollowCamera(0f);

            DepthSortedDecor rock = NewDecorAt(new Vector3(0f, 10f, 0f));
            rock.RegisterWith(ladder);
            rock.RegisterWith(ladder);

            Assert.AreEqual(1, ladder.TrackedCount);
        }

        DepthSortedDecor NewDecorAt(Vector3 position)
        {
            SpriteRenderer renderer = TrackedRenderer();
            renderer.transform.position = position;

            // A real sprite, so bounds.min.y is the bottom of what is actually drawn - which is the
            // key the decor is ranked on, it owning no footprint.
            var texture = new Texture2D(4, 4);
            renderer.sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);

            return renderer.gameObject.AddComponent<DepthSortedDecor>();
        }

        [Test]
        public void ANullRendererIsIgnoredRatherThanThrowing()
        {
            var ladder = NewLadder();

            Assert.DoesNotThrow(() => ladder.Register(null, 0f, SortingBands.SubSprite));
            Assert.DoesNotThrow(() => ladder.Unregister(null));
            Assert.AreEqual(0, ladder.TrackedCount);
        }

        /// <summary>Views spawned in a Start(), before the first FollowCamera, still have to sort against each other.</summary>
        [Test]
        public void AnUnanchoredLadderStillSortsAroundTheOrigin()
        {
            var ladder = NewLadder();

            Assert.Greater(ladder.Order(4f, SortingBands.SubSprite), ladder.Order(9f, SortingBands.SubSprite));
        }
    }
}
