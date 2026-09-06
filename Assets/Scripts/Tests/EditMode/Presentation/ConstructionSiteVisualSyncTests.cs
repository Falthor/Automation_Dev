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

        /// <summary>
        /// Runs until the site's first delivery lands, then stops - the deterministic way to catch a
        /// site half supplied, now that a short chest cannot produce one (an underfunded placement is
        /// refused outright). Stopping on the state rather than on a duration keeps it independent of
        /// robot travel time.
        /// </summary>
        static void AdvanceToFirstDelivery(Fixture fixture, ConstructionSiteRuntime site)
        {
            for (int i = 0; i < 500 && site.SegmentProgress(site.MaterializedCount) <= 0f; i++)
            {
                fixture.Simulate(0.2f);
            }

            Assert.Greater(site.SegmentProgress(site.MaterializedCount), 0f,
                "Nothing was ever delivered - the site never started.");
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
            Fixture fixture = NewFixture(coreChestContents: 4);
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
            // A site is always fully funded now - the gate refuses one it cannot cover - so the
            // part-delivered state comes from the robots' round trips. A bill of twelve is more than
            // the eight two robots carry in one wave, so the first delivery necessarily leaves it
            // short.
            Fixture fixture = NewFixture(coreChestContents: 12);
            StorageDefinition costly = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 12));
            ConstructionSiteRuntime site = PlaceSite(fixture, costly, new GridCoord(5, 5));
            BuildingRuntime segment = site.Segments[0];

            fixture.Views.Tick();
            AdvanceToFirstDelivery(fixture, site);
            fixture.Views.Tick();

            Assert.Greater(site.SegmentProgress(0), 0f, "One wave has landed, so the segment is part-delivered...");
            Assert.IsFalse(site.IsComplete, "...and still short of its twelve.");

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

        /// <summary>
        /// A Splitter/Crossroad's "+" occupies five cells of a 3x3 box and deliberately leaves the
        /// four corners free - its placement origin among them. A detached segment's liveness was
        /// read off that origin cell, so the grid answered "nothing there" for a building that was
        /// perfectly alive: every splitter was discarded the frame it materialised, its dissolve cut
        /// on the spot and no real view ever spawned behind it. It simply vanished once built.
        /// </summary>
        [Test]
        public void AMaterializedSplitter_IsNotDiscarded_ThoughItsOriginCellIsFreeByDesign()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            SplitterDefinition splitter = TestDataFactory.NewSplitter("splitter", (fixture.Plate, 4));
            ConstructionSiteRuntime site = PlaceSite(fixture, splitter, new GridCoord(5, 5));
            BuildingRuntime segment = site.Segments[0];

            Assert.IsNull(fixture.Grid.GetOccupant(segment.Cell), "The premise: a '+' does not stand on its own origin.");

            fixture.Views.Tick();
            fixture.Simulate(12f);

            Assert.IsTrue(site.IsComplete);

            fixture.Views.Tick();
            Assert.IsTrue(fixture.Views.Draws(segment), "It keeps assembling like any other segment.");

            fixture.Assemble(segment, 1f);
            fixture.Views.Tick();

            Assert.AreEqual(1, fixture.SpawnedRealViews.Count, "And hands over to a real view instead of disappearing.");
            Assert.AreSame(segment, fixture.SpawnedRealViews[0]);
        }

        /// <summary>
        /// A transport piece lies flat on the ground it was laid on. It pours no concrete once
        /// built, so it must show none while converting either - the pad would appear for the
        /// length of the build and vanish at the handover. The exclusion used to name
        /// ConveyorRuntime alone, which is narrower than the family it meant.
        /// </summary>
        [Test]
        public void ASplitterSite_ShowsNoConcretePad_TheFinishedOneKeepsNone()
        {
            Fixture fixture = NewFixture(coreChestContents: 8);
            var slabbed = new List<GridCoord>();
            fixture.Views.SetGroundSlabSpawner((cell, footprint) => { slabbed.Add(cell); return null; });

            SplitterDefinition splitter = TestDataFactory.NewSplitter("splitter", (fixture.Plate, 4));
            PlaceSite(fixture, splitter, new GridCoord(5, 5));
            fixture.Views.Tick();

            Assert.IsEmpty(slabbed, "A '+' keeps no pad, so its site shows none.");

            // The control: a building that does keep one still gets it while converting.
            StorageDefinition storage = TestDataFactory.NewStorage("target", cost: (fixture.Plate, 4));
            PlaceSite(fixture, storage, new GridCoord(12, 12));
            fixture.Views.Tick();

            Assert.AreEqual(new[] { new GridCoord(12, 12) }, slabbed);
        }

        /// <summary>
        /// The silhouette, the assembling sprite and the real view must all be the size the
        /// building is actually drawn at - BuildingSpawner.ArtWorldSize, RenderOverscan included.
        /// Overscan used to be applied only inside BuildingSpawner, so everything previewing a
        /// building came out that much smaller than what got built: 9% on the Foundry, visible to
        /// the naked eye against a finished neighbour.
        /// </summary>
        [Test]
        public void SilhouetteAndAssembly_AreSizedToTheArtTheRealViewWillUse_OverscanIncluded()
        {
            Fixture fixture = NewFixture(coreChestContents: 4);
            FoundryDefinition foundry = TestDataFactory.NewFoundry(10, 0f, 0f);
            Assert.AreNotEqual(1f, foundry.RenderOverscan, "Precondition: the Foundry is the overscanned case this guards.");

            // A cost, or a zero-cost site materialises on the spot and never shows a silhouette.
            SetCost(foundry, fixture.Plate, 4);

            ConstructionSiteRuntime site = PlaceSite(fixture, foundry, new GridCoord(5, 5));
            BuildingRuntime segment = site.Segments[0];

            fixture.Views.Tick();

            SpriteRenderer silhouette = fixture.Views.SilhouetteOf(segment);
            Assert.IsNotNull(silhouette, "The segment must still be pending for this to mean anything.");

            Vector2 expected = BuildingSpawner.ArtWorldSize(foundry, fixture.Grid.CellSize);
            AssertDrawnWorldSize(silhouette, expected, "silhouette");
            AssertDrawnWorldSize(fixture.Views.DissolveOf(segment).GetComponent<SpriteRenderer>(), expected, "assembling sprite");
        }

        /// <summary>TestDataFactory's typed builders take a cost only where a test already needed one; the Foundry's is written here the same way.</summary>
        static void SetCost(BuildingDefinition definition, ItemDefinition item, int amount)
        {
            var so = new SerializedObject(definition);
            SerializedProperty array = so.FindProperty("cost");
            array.arraySize = 1;
            SerializedProperty element = array.GetArrayElementAtIndex(0);
            element.FindPropertyRelative("item").objectReferenceValue = item;
            element.FindPropertyRelative("amount").intValue = amount;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AssertDrawnWorldSize(SpriteRenderer renderer, Vector2 expected, string what)
        {
            Vector3 scale = renderer.transform.localScale;
            Vector3 native = renderer.sprite.bounds.size;
            Assert.AreEqual(expected.x, native.x * scale.x, 0.0001f, what + " width");
            Assert.AreEqual(expected.y, native.y * scale.y, 0.0001f, what + " height");
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
            Fixture fixture = NewFixture(coreChestContents: 4);
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
            // Belts costing twelve each: one wave of two robots carries eight, so the front belt is
            // part-delivered and none of the three is built yet.
            Fixture fixture = NewFixture(coreChestContents: 36);
            ConveyorDefinition conveyor = TestDataFactory.NewConveyor("conveyor", (fixture.Plate, 12));

            fixture.Construction.SelectBuilding(conveyor);
            Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5, 5), Direction.East, out ConstructionSiteRuntime site));
            for (int i = 1; i < 3; i++)
            {
                Assert.IsTrue(fixture.Construction.TryPlace(new GridCoord(5 + i, 5), Direction.East, out _, site));
            }

            fixture.Views.Tick();
            AdvanceToFirstDelivery(fixture, site);
            fixture.Views.Tick();

            Assert.AreEqual(3, site.Segments.Count);
            Assert.AreEqual(0, site.MaterializedCount, "Eight of the twelve the first belt costs.");

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
