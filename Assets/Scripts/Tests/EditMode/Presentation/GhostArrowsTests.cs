using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Presentation;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// What the placement ghost marks, and what the ghost view then draws from it.
    ///
    /// <b>Two halves of one seam, and the reason both are here.</b> CrossPieceConnectionsTests
    /// proves a Splitter's sides are described correctly; nothing proved anything ever asked for
    /// them. DEVELOPMENT_RULES §7 - both halves of a seam can be right, tested and documented while
    /// nothing joins them. These start from the composition the adapter calls and from the view it
    /// hands the result to.
    ///
    /// The one link left unpinned is the adapter's own call, which needs a camera and a mouse and
    /// therefore a PlayMode test.
    /// </summary>
    public class GhostArrowsTests
    {
        readonly List<Object> _assets = new List<Object>();
        readonly List<GameObject> _objects = new List<GameObject>();

        readonly List<(Direction side, bool inward)> _scratch = new List<(Direction, bool)>();
        readonly List<(GridCoord cell, Direction side, bool inward)> _arrows = new List<(GridCoord, Direction, bool)>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _objects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _objects.Clear();

            // The arrows the view spawns are loose roots, not children of anything tracked above.
            foreach (Transform arrow in FindLooseArrows()) Object.DestroyImmediate(arrow.gameObject);

            foreach (Object asset in _assets)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }
            _assets.Clear();
        }

        T Track<T>(T asset) where T : Object
        {
            _assets.Add(asset);
            return asset;
        }

        static readonly GridCoord Origin = new GridCoord(6, 6);

        // ---- What the ghost marks ----

        /// <summary>
        /// A Splitter previews one entry and three exits, on the four neighbouring cells. This is
        /// the case the two definition flags cannot express at all, and the one that was drawn by
        /// nothing before.
        /// </summary>
        [Test]
        public void ASplitter_MarksOneEntryAndThreeExits_OnItsFourNeighbours()
        {
            SplitterDefinition splitter = Track(TestDataFactory.NewSplitter());

            GhostArrows.For(splitter, Origin, Direction.East, Direction.North, _scratch, _arrows);

            Assert.AreEqual(4, _arrows.Count, "one entry, three exits");

            var inward = new List<Direction>();
            var outward = new List<Direction>();
            foreach ((GridCoord cell, Direction side, bool isInward) in _arrows)
            {
                Assert.AreEqual(Origin + side.ToOffset(), cell,
                    "an arrow marks the neighbouring cell on its own side, never the piece's own");
                (isInward ? inward : outward).Add(side);
            }

            CollectionAssert.AreEqual(new[] { Direction.East }, inward, "the entry is the facing side");
            Assert.AreEqual(3, outward.Count);
            CollectionAssert.DoesNotContain(outward, Direction.East, "the entry side is never also an exit");
        }

        /// <summary>A Crossroad previews two of each - the shape neither flag can name.</summary>
        [Test]
        public void ACrossroad_MarksTwoEntriesAndTwoExits()
        {
            CrossroadDefinition crossroad = Track(TestDataFactory.NewCrossroad());

            GhostArrows.For(crossroad, Origin, Direction.North, Direction.North, _scratch, _arrows);

            var inward = 0;
            var outward = 0;
            var sides = new List<Direction>();
            foreach ((GridCoord cell, Direction side, bool isInward) in _arrows)
            {
                Assert.AreEqual(Origin + side.ToOffset(), cell);
                sides.Add(side);
                if (isInward) inward++; else outward++;
            }

            Assert.AreEqual(2, inward, "two entries");
            Assert.AreEqual(2, outward, "two exits");
            CollectionAssert.AllItemsAreUnique(sides, "each side is marked once");
        }

        /// <summary>
        /// The ordinary path still answers: a single-input building marks exactly one entry, on the
        /// side chosen with T, and its output. Here because extracting this rule must not have
        /// quietly dropped the family it was already serving.
        /// </summary>
        [Test]
        public void ASingleInputBuilding_MarksItsOutputAndTheOneSideItTakesFrom()
        {
            FoundryDefinition foundry = Track(TestDataFactory.NewFoundry(0f, 0f, "ingot"));

            GhostArrows.For(foundry, Origin, Direction.North, Direction.South, _scratch, _arrows);

            var inward = new List<Direction>();
            var outward = new List<Direction>();
            foreach ((GridCoord _, Direction side, bool isInward) in _arrows) (isInward ? inward : outward).Add(side);

            Assert.AreEqual(1, outward.Count, "one output arrow");
            Assert.AreEqual(Direction.North, outward[0], "on the facing side");
            Assert.AreEqual(1, inward.Count, "and exactly one entry, not three");
            Assert.AreEqual(Direction.South, inward[0], "on the side T landed on");
        }

        /// <summary>A building declaring neither marks nothing at all - a Storage takes from every side and says so by drawing no arrow.</summary>
        [Test]
        public void ABuildingDeclaringNeither_MarksNothing()
        {
            StorageDefinition storage = Track(TestDataFactory.NewStorage("box"));

            GhostArrows.For(storage, Origin, Direction.North, Direction.North, _scratch, _arrows);

            Assert.IsEmpty(_arrows);
        }

        // ---- What the view draws from it ----

        BuildingGhostView NewGhostView()
        {
            var go = new GameObject("ghost", typeof(SpriteRenderer), typeof(BuildingGhostView));
            _objects.Add(go);
            return go.GetComponent<BuildingGhostView>();
        }

        /// <summary>
        /// The other half: the view turns one entry into one visible arrow, pointing the way the
        /// built view would point it. A description nothing draws is the defect this pins.
        /// </summary>
        [Test]
        public void TheGhostView_DrawsOneArrowPerEntry_PointingTheWayTheBuiltViewWould()
        {
            var factory = new ProceduralSpriteFactory();
            Sprite output = factory.CreateArrowSprite(BuildingSpawner.OutputArrowColor);
            Sprite input = factory.CreateArrowSprite(BuildingSpawner.InputArrowColor);

            BuildingGhostView ghost = NewGhostView();

            var arrows = new List<(Vector3 position, Direction side, bool inward)>
            {
                (Vector3.zero, Direction.East, false),
                (Vector3.one, Direction.West, true)
            };

            ghost.Show(factory.CreateSolidSquareSprite(Color.white), Vector2.one, Vector3.zero, Direction.North, true,
                output, input, arrows, 0.25f);

            // The view parents its arrows to its own parent, and this ghost sits at the scene root,
            // so they arrive as root objects rather than as children of it.
            var drawn = new List<Transform>();
            foreach (Transform arrow in FindLooseArrows())
            {
                if (arrow.gameObject.activeSelf) drawn.Add(arrow);
            }

            Assert.AreEqual(2, drawn.Count, "one arrow per entry, and no stale extras left active");

            foreach (Transform arrow in drawn)
            {
                float z = arrow.rotation.eulerAngles.z;
                bool isOutput = Mathf.Approximately(Mathf.DeltaAngle(z, -BuildingSpawner.ArrowPointing(Direction.East, false).ToRotationDegrees()), 0f);
                bool isInput = Mathf.Approximately(Mathf.DeltaAngle(z, -BuildingSpawner.ArrowPointing(Direction.West, true).ToRotationDegrees()), 0f);
                Assert.IsTrue(isOutput || isInput, $"an arrow points somewhere neither entry asked for: {z} degrees");
            }
        }

        /// <summary>The ghost parents its arrows to its own parent, so a ghost sitting at the scene root leaves them as loose roots.</summary>
        static List<Transform> FindLooseArrows()
        {
            var found = new List<Transform>();
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "GhostArrow") found.Add(root.transform);
            }
            return found;
        }
    }
}
