using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using Game.Presentation;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// DropShadow's own behaviour is covered in PlayMode; what is tested here is the <b>wiring</b> -
    /// that every path BuildingSpawner uses to draw a building actually attaches one.
    ///
    /// That is the part that has gone wrong before on this component. RenderOverscan was applied by
    /// one art path out of four and missed by the other three, and the shadow arrived the same way:
    /// the system existed, wired to the Core alone. A spawn path added later without a shadow reads
    /// as a lighting bug rather than a missing line, so each path is asserted separately.
    /// </summary>
    public class BuildingSpawnerShadowTests
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

        BuildingShadowSettings NewShadowSettings()
        {
            var settings = ScriptableObject.CreateInstance<BuildingShadowSettings>();
            _assets.Add(settings);
            return settings;
        }

        SplitterDefinition NewSplitter()
        {
            var definition = ScriptableObject.CreateInstance<SplitterDefinition>();
            _assets.Add(definition);

            var so = new SerializedObject(definition);
            so.FindProperty("id").stringValue = "splitter";
            so.FindProperty("displayName").stringValue = "Splitter";
            so.FindProperty("footprintSize").vector2IntValue = new Vector2Int(3, 3);
            so.ApplyModifiedPropertiesWithoutUndo();

            return definition;
        }

        static BuildingSpawner NewSpawner(BuildingShadowSettings shadowSettings)
            => new BuildingSpawner(new GridRuntime(1f), new ProceduralSpriteFactory(), shadowSettings: shadowSettings);

        /// <summary>Each test spawns exactly one building into the scene, so a single search is unambiguous.</summary>
        static DropShadow SpawnedShadow(BuildingSpawner spawner, BuildingRuntime runtime)
        {
            spawner.SpawnView(runtime);

            DropShadow[] shadows = Object.FindObjectsByType<DropShadow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.LessOrEqual(shadows.Length, 1, "Precondition: one building per test.");

            return shadows.Length == 1 ? shadows[0] : null;
        }

        [Test]
        public void AStandardBuilding_CastsAShadow()
        {
            BuildingShadowSettings settings = NewShadowSettings();

            StorageDefinition definition = TestDataFactory.NewStorage("shadowed");
            var runtime = new StorageRuntime(definition, new GridCoord(0, 0), Direction.North);

            DropShadow shadow = SpawnedShadow(NewSpawner(settings), runtime);

            Assert.IsNotNull(shadow, "A building drawn by SpawnStandardView must cast a shadow.");
            Assert.AreSame(settings, shadow.Settings, "And read the one shared settings asset, never a copy.");
        }

        /// <summary>
        /// The Splitter and Crossroad go through their own spawn path, which also rotates the view.
        /// Asserted separately because "wired on one path only" is exactly the failure mode here.
        /// </summary>
        [Test]
        public void ARotatingCrossBuilding_CastsAShadowToo()
        {
            BuildingShadowSettings settings = NewShadowSettings();

            SplitterDefinition definition = NewSplitter();
            var runtime = new SplitterRuntime(definition, new GridCoord(0, 0), Direction.East);

            DropShadow shadow = SpawnedShadow(NewSpawner(settings), runtime);

            Assert.IsNotNull(shadow, "A Splitter is drawn by SpawnRotatingCrossView, which needs the shadow just as much.");
            Assert.AreSame(settings, shadow.Settings);
        }

        /// <summary>A belt lies flat on the ground: it has nothing to cast, and there are hundreds of them.</summary>
        [Test]
        public void AConveyor_CastsNone()
        {
            ConveyorDefinition definition = TestDataFactory.NewConveyor("belt");
            var runtime = new ConveyorRuntime(definition, new GridCoord(0, 0), Direction.North);

            Assert.IsNull(SpawnedShadow(NewSpawner(NewShadowSettings()), runtime),
                "Conveyors are deliberately excluded from the shadow pass.");
        }

        /// <summary>
        /// Null settings disable the shadow entirely rather than falling back to a hardcoded look -
        /// the same all-or-nothing convention as the concrete slab's missing textures.
        /// </summary>
        [Test]
        public void WithoutSettings_NothingCastsAShadow()
        {
            StorageDefinition definition = TestDataFactory.NewStorage("unshadowed");
            var runtime = new StorageRuntime(definition, new GridCoord(0, 0), Direction.North);

            Assert.IsNull(SpawnedShadow(NewSpawner(null), runtime), "No settings asset means no shadow at all.");
        }
    }
}
