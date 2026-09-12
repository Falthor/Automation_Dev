using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using Game.Presentation;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// F2 puts the world's input/output arrows away (InputActionCatalogue.ShowConnections). The key
    /// is pinned by InputBindingTableTests; what is pinned here is what pressing it does.
    ///
    /// The half worth guarding is the second one: a building placed while the arrows are hidden must
    /// be born hidden. A toggle that only walks what already exists looks correct until something is
    /// built, and then leaks one arrow at a time with nothing to see but arrows coming back.
    /// </summary>
    public class ConnectionArrowVisibilityTests
    {
        readonly List<GameObject> _preexistingRoots = new List<GameObject>();
        readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _preexistingRoots.Clear();
            _preexistingRoots.AddRange(SceneManager.GetActiveScene().GetRootGameObjects());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!_preexistingRoots.Contains(root)) Object.DestroyImmediate(root);
            }

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

        static BuildingSpawner NewSpawner() => new BuildingSpawner(new GridRuntime(1f), new ProceduralSpriteFactory());

        /// <summary>
        /// A Splitter: it draws four arrows through the cross-piece path, takes nothing but its own
        /// definition to construct, and is the family whose arrows were added last - so it is the
        /// one a toggle is most likely to have been written without.
        /// </summary>
        BuildingRuntime NewBuildingWithArrows(GridCoord cell)
        {
            SplitterDefinition definition = Track(TestDataFactory.NewSplitter());
            return new SplitterRuntime(definition, cell, Direction.North);
        }

        /// <summary>Every arrow currently in the scene, whatever it is parented to.</summary>
        static List<SpriteRenderer> ArrowsInScene()
        {
            var found = new List<SpriteRenderer>();
            foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include))
            {
                if (renderer.gameObject.name == "InputArrow" || renderer.gameObject.name == "OutputArrow") found.Add(renderer);
            }
            return found;
        }

        [Test]
        public void ArrowsAreDrawnByDefault()
        {
            BuildingSpawner spawner = NewSpawner();
            spawner.SpawnView(NewBuildingWithArrows(new GridCoord(0, 0)));

            List<SpriteRenderer> arrows = ArrowsInScene();
            Assert.Greater(arrows.Count, 0, "a Splitter draws one entry arrow and three exit arrows");
            foreach (SpriteRenderer arrow in arrows) Assert.IsTrue(arrow.enabled, "and they are on until the player puts them away");
        }

        [Test]
        public void HidingThem_TurnsOffEveryArrowAlreadyDrawn()
        {
            BuildingSpawner spawner = NewSpawner();
            spawner.SpawnView(NewBuildingWithArrows(new GridCoord(0, 0)));

            spawner.SetConnectionArrowsVisible(false);

            foreach (SpriteRenderer arrow in ArrowsInScene()) Assert.IsFalse(arrow.enabled);

            spawner.SetConnectionArrowsVisible(true);

            foreach (SpriteRenderer arrow in ArrowsInScene()) Assert.IsTrue(arrow.enabled, "and back on again");
        }

        /// <summary>
        /// The half a walk-what-exists toggle gets wrong: what is built afterwards.
        /// </summary>
        [Test]
        public void ABuildingPlacedWhileTheyAreHidden_IsBornHidden()
        {
            BuildingSpawner spawner = NewSpawner();
            spawner.SetConnectionArrowsVisible(false);

            spawner.SpawnView(NewBuildingWithArrows(new GridCoord(4, 4)));

            List<SpriteRenderer> arrows = ArrowsInScene();
            Assert.Greater(arrows.Count, 0, "precondition: the building did draw its arrows");
            foreach (SpriteRenderer arrow in arrows)
            {
                Assert.IsFalse(arrow.enabled, "an arrow created while they are away must not bring itself back");
            }
        }

        /// <summary>
        /// A demolished building takes its arrows with it while the spawner still holds their
        /// renderers: the next toggle has to drop the dead ones rather than throw on them, which is
        /// what keeps the list from being one that only grows.
        ///
        /// The view is destroyed directly rather than through <c>RemoveView</c>, which calls
        /// <c>Object.Destroy</c> - illegal in edit mode, and the error Unity logs for it fails the
        /// test for a reason that has nothing to do with what is being checked. What reaches the
        /// spawner is the same either way: renderers that are gone.
        /// </summary>
        [Test]
        public void TogglingAfterTheViewIsGone_DropsTheDeadArrowsRatherThanThrowing()
        {
            BuildingSpawner spawner = NewSpawner();
            spawner.SpawnView(NewBuildingWithArrows(new GridCoord(0, 0)));
            Assert.Greater(ArrowsInScene().Count, 0, "precondition: there were arrows to lose");

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!_preexistingRoots.Contains(root)) Object.DestroyImmediate(root);
            }

            Assert.DoesNotThrow(() => spawner.SetConnectionArrowsVisible(false));
            Assert.DoesNotThrow(() => spawner.SetConnectionArrowsVisible(true));
            Assert.IsEmpty(ArrowsInScene(), "and nothing came back from the dead");
        }
    }
}
