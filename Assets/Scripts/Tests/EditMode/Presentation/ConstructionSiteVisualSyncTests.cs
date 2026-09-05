using System.Collections.Generic;
using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Notifications;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Sites;
using Game.Gameplay.Transport;
using Game.Grid;
using Game.Presentation;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// directive-materialisation-nano.md §2 and §3: the three visual states of a construction site
    /// segment, and above all the handover to the real building view.
    ///
    /// The state machine is what is under test, never the rendering. ConstructionSiteVisualSync.Tick
    /// and BuildDissolveView.Tick are both frame-free for exactly this reason, so a test can step the
    /// simulation, step the views, and assert in between.
    /// </summary>
    public class ConstructionSiteVisualSyncTests
    {
        const string PlateId = "iron_plate";
        const float TickSeconds = 0.2f;

        /// <summary>Footprint cells per second, set high enough that a single Tick finishes any assembly - the pacing itself is BuildDissolveViewTests' subject, not this one's.</summary>
        const float InstantAssemblyRate = 100f;

        readonly List<Object> _spawned = new List<Object>();

        sealed class Fixture
        {
            public GridRuntime Grid;
            public TransportSystem Transport;
            public ConstructionSiteSystem Sites;
            public ConstructionService Construction;
            public ItemDefinition Plate;
            public ConstructionSiteVisualSync Views;
            public List<BuildingRuntime> SpawnedRealViews;

            public void Simulate(float seconds)
            {
                for (float elapsed = 0f; elapsed < seconds; elapsed += TickSeconds)
                {
                    Sites.Tick(TickSeconds);
                }
            }

            /// <summary>Advances a segment's dissolve, mimicking the LateUpdate BuildDissolveView runs for itself in play mode.</summary>
            public void Assemble(BuildingRuntime segment, float seconds)
            {
                BuildDissolveView dissolve = Views.DissolveOf(segment);
                if (dissolve != null) dissolve.Tick(seconds);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object spawned in _spawned)
            {
                if (spawned != null) Object.DestroyImmediate(spawned);
            }
            _spawned.Clear();
        }

        Fixture NewFixture(int coreChestContents)
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);
            var sites = new ConstructionSiteSystem(transport, grid, new NotificationSystem(), Vector2.zero);
            var construction = new ConstructionService(grid, null, null, new ComputeSystem(), new PowerSystem(),
                new ResearchSystem(new ComputeSystem()), transport, null, sites);

            StorageDefinition coreChestDefinition = TestDataFactory.NewStorage(ConstructionSiteSystem.CoreStorageDefinitionId, 6, 200, rejectsConveyorInput: true);
            var coreChest = new StorageRuntime(coreChestDefinition, new GridCoord(0, 0), Direction.North);
            grid.SetOccupantFootprint(coreChest.Cell, coreChestDefinition.FootprintSize, coreChest);
            transport.Register(coreChest);
            if (coreChestContents > 0) coreChest.SeedInitialContents(PlateId, coreChestContents);

            var fixture = new Fixture
            {
                Grid = grid,
                Transport = transport,
                Sites = sites,
                Construction = construction,
                Plate = TestDataFactory.NewItem(PlateId),
                SpawnedRealViews = new List<BuildingRuntime>()
            };

            var host = new GameObject("ConstructionSiteVisuals");
            _spawned.Add(host);
            fixture.Views = host.AddComponent<ConstructionSiteVisualSync>();
            BindSettings(fixture.Views, NewSettings());
            fixture.Views.Initialize(sites, grid, fixture.SpawnedRealViews.Add);

            return fixture;
        }

        /// <summary>Same approach as BuildDissolveViewTests: the asset is a definition with no production setters, so a test writes it through SerializedObject.</summary>
        NanoConstructionSettings NewSettings()
        {
            var settings = ScriptableObject.CreateInstance<NanoConstructionSettings>();
            _spawned.Add(settings);

            var so = new SerializedObject(settings);
            so.FindProperty("assemblyRate").floatValue = InstantAssemblyRate;

            // Without lowering the floor, the derived rate would be capped at 1/0.25 = 4 per second
            // and "instant" would stop being instant.
            so.FindProperty("minAssemblyDuration").floatValue = 0.01f;
            so.FindProperty("sitePlaceholderAlpha").floatValue = 0.35f;
            so.FindProperty("siteSilhouetteSortingOrder").intValue = 7;

            // Any shader will do - nothing here asserts on pixels; what matters is that the view
            // considers itself able to assemble, which is what gates the whole handover path.
            so.FindProperty("dissolveShader").objectReferenceValue = Shader.Find("Sprites/Default");
            so.ApplyModifiedPropertiesWithoutUndo();

            return settings;
        }

        static void BindSettings(ConstructionSiteVisualSync views, NanoConstructionSettings settings)
        {
            var so = new SerializedObject(views);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static ConstructionSiteRuntime PlaceSite(Fixture fixture, BuildingDefinition definition, GridCoord cell)
        {
            fixture.Construction.SelectBuilding(definition);
            Assert.IsTrue(fixture.Construction.TryPlace(cell, Direction.North, out ConstructionSiteRuntime site));
            return site;
        }

        // --- The three states ---

        [Test]
        public void APendingSegment_ShowsAFullSilhouette_AndNothingAssembled()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));

            fixture.Views.Tick();

            SpriteRenderer silhouette = fixture.Views.SilhouetteOf(site.Segments[0]);
            Assert.IsNotNull(silhouette, "A placed site is visible immediately, before any material arrives.");
            Assert.AreEqual(0.6f, silhouette.color.a, 0.0001f, "Nothing delivered: the silhouette is at its own full tint, not the faded one.");
            Assert.AreEqual(7, silhouette.sortingOrder, "The silhouette sits under the drop shadow and the sprite.");
            Assert.AreEqual(0f, fixture.Views.DissolveOf(site.Segments[0]).DisplayedProgress, 0.0001f, "The sprite is entirely clipped away.");
            Assert.AreEqual(0, fixture.Views.AssemblingCount);
        }

        [Test]
        public void AsMaterialArrives_TheSilhouetteFadesToThePlaceholderAlpha()
        {
            Fixture fixture = NewFixture(coreChestContents: 2);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));
            BuildingRuntime segment = site.Segments[0];

            fixture.Views.Tick();
            fixture.Simulate(12f);
            fixture.Views.Tick();

            Assert.Greater(site.SegmentProgress(0), 0f, "The chest only holds half the cost, so the segment is part-delivered and still pending.");
            Assert.IsFalse(site.IsComplete);

            fixture.Assemble(segment, 0.05f);
            fixture.Views.Tick();

            Assert.AreEqual(0.35f, fixture.Views.SilhouetteOf(segment).color.a, 0.0001f,
                "Once the sprite starts forming over it, the silhouette drops to sitePlaceholderAlpha.");
            Assert.AreEqual(1, fixture.Views.AssemblingCount);
        }

        /// <summary>
        /// The reason the assembling set has to outlive the site. A segment materialises the instant
        /// its last item lands and leaves ConstructionSiteSystem's pending range on that very tick,
        /// but on screen it is only as far along as its dissolve - so the view must survive, keep
        /// assembling, and only then let the real view take over.
        /// </summary>
        [Test]
        public void AMaterializedSegment_KeepsAssembling_AndTheRealViewAppearsOnlyWhenItCompletes()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));
            BuildingRuntime segment = site.Segments[0];

            fixture.Views.Tick();
            fixture.Simulate(12f);

            Assert.IsTrue(site.IsComplete, "The material is all delivered: the segment is a real building now.");
            Assert.AreEqual(0, fixture.Sites.Sites.Count, "And its site is gone.");

            fixture.Views.Tick();

            Assert.IsTrue(fixture.Views.Draws(segment), "The view outlives the site it came from.");
            Assert.IsEmpty(fixture.SpawnedRealViews, "Nothing real is spawned while the sprite is still assembling.");
            Assert.AreEqual(1, fixture.Views.AssemblingCount);

            fixture.Assemble(segment, 1f);
            fixture.Views.Tick();

            Assert.AreEqual(1, fixture.SpawnedRealViews.Count, "The real view is spawned when the dissolve reaches 1.");
            Assert.AreSame(segment, fixture.SpawnedRealViews[0]);
            Assert.IsFalse(fixture.Views.Draws(segment), "And the assembling objects go away in the same call, so no frame shows both.");

            fixture.Views.Tick();
            Assert.AreEqual(1, fixture.SpawnedRealViews.Count, "Never spawned twice.");
        }

        // --- Edge cases ---

        [Test]
        public void DemolishingASegmentWhileItAssembles_DropsItsViewWithoutEverSpawningTheRealOne()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            var cell = new GridCoord(5, 5);
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, cell);
            BuildingRuntime segment = site.Segments[0];

            fixture.Views.Tick();
            fixture.Simulate(12f);
            fixture.Views.Tick();
            Assert.IsTrue(fixture.Views.Draws(segment), "Precondition: materialized, still assembling.");

            Assert.IsTrue(fixture.Construction.TryDemolish(cell, out _));
            fixture.Views.Tick();

            Assert.IsFalse(fixture.Views.Draws(segment), "A demolished segment takes its half-assembled sprite with it.");
            Assert.IsEmpty(fixture.SpawnedRealViews, "It must never hand over to a real view - the building no longer exists.");
        }

        [Test]
        public void CancellingAPendingSite_DropsItsSilhouettes()
        {
            Fixture fixture = NewFixture(coreChestContents: 0);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            var cell = new GridCoord(5, 5);
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, cell);
            BuildingRuntime segment = site.Segments[0];

            fixture.Views.Tick();
            Assert.IsTrue(fixture.Views.Draws(segment));

            Assert.IsTrue(fixture.Construction.TryCancelSiteAt(cell));
            fixture.Views.Tick();

            Assert.IsFalse(fixture.Views.Draws(segment));
            Assert.IsEmpty(fixture.SpawnedRealViews);
        }

        /// <summary>
        /// A conveyor drag is one site of many segments, built strictly in placement order. Driving
        /// the dissolve from ConstructionSiteRuntime.SegmentProgress rather than from the site's
        /// aggregate is what makes a long belt assemble piece by piece instead of dissolving as one
        /// block - see the notebook's entry on that accessor.
        /// </summary>
        [Test]
        public void OnAConveyorRun_OnlyTheSegmentBeingBuiltDissolves()
        {
            Fixture fixture = NewFixture(coreChestContents: 1);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 2));

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5, 5), Direction.East, out ConstructionSiteRuntime site));
            for (int i = 1; i < 3; i++)
            {
                Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5 + i, 5), Direction.East, out _, site));
            }

            fixture.Views.Tick();
            fixture.Simulate(12f);
            fixture.Views.Tick();

            Assert.AreEqual(3, site.Segments.Count);
            Assert.AreEqual(0, site.MaterializedCount, "One plate out of the two the first belt costs.");

            Assert.Greater(fixture.Views.DissolveOf(site.Segments[0]).TargetProgress, 0f, "The front segment is the one taking material.");
            Assert.AreEqual(0f, fixture.Views.DissolveOf(site.Segments[1]).TargetProgress, 0.0001f, "The ones behind it have nothing yet.");
            Assert.AreEqual(0f, fixture.Views.DissolveOf(site.Segments[2]).TargetProgress, 0.0001f);

            for (int i = 0; i < 3; i++)
            {
                fixture.Assemble(site.Segments[i], 0.05f);
            }
            fixture.Views.Tick();

            Assert.AreEqual(1, fixture.Views.AssemblingCount, "Exactly one belt is materialising; the other two are still bare silhouettes.");
            Assert.AreEqual(0.6f, fixture.Views.SilhouetteOf(site.Segments[2]).color.a, 0.0001f);
        }
    }
}
