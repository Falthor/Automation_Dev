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

using DrawnSegment = Game.Presentation.ConstructionSiteVisualSync.DrawnSegment;
using ZoneDescriptor = Game.Presentation.GroundCoverageRenderer.ZoneDescriptor;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// directive-materialisation-nano.md §7: the per-zone ground coverage field. Nothing here
    /// asserts on rendering - GroundCoverageRenderer.Tick is frame-free precisely so the field, the
    /// decay and the upload gating can be driven step by step.
    /// </summary>
    public class GroundCoverageRendererTests
    {
        const float FadeSeconds = 4f;

        readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object spawned in _spawned)
            {
                if (spawned != null) Object.DestroyImmediate(spawned);
            }
            _spawned.Clear();
        }

        NanoConstructionSettings NewSettings()
        {
            var settings = ScriptableObject.CreateInstance<NanoConstructionSettings>();
            _spawned.Add(settings);

            var so = new SerializedObject(settings);
            so.FindProperty("coverageFadeSeconds").floatValue = FadeSeconds;
            so.FindProperty("coverageShader").objectReferenceValue = Shader.Find("Sprites/Default");
            so.ApplyModifiedPropertiesWithoutUndo();

            return settings;
        }

        GroundCoverageRenderer NewRenderer(out GridRuntime grid)
        {
            grid = new GridRuntime(1f);

            var go = new GameObject("GroundCoverage");
            _spawned.Add(go);

            var renderer = go.AddComponent<GroundCoverageRenderer>();
            renderer.Initialize(grid, NewSettings());
            return renderer;
        }

        /// <summary>One zone centred on the origin, big enough for every cell these tests touch.</summary>
        static List<ZoneDescriptor> OneZone(int id = 1, int radius = 16)
            => new List<ZoneDescriptor> { new ZoneDescriptor(id, new GridCoord(0, 0), radius) };

        static List<DrawnSegment> NoSegments() => new List<DrawnSegment>();

        static BuildingRuntime NewSegment(StorageDefinition definition, GridCoord cell)
            => new StorageRuntime(definition, cell, Direction.North);

        /// <summary>
        /// A multi-cell building without dragging in a real production runtime: the concrete types
        /// with big footprints (Foundry and friends) need a recipe database, compute, power and
        /// research to construct, none of which this layer touches. Only the definition's footprint
        /// matters here.
        /// </summary>
        static StorageDefinition NewFootprint(int width, int height)
        {
            StorageDefinition definition = TestDataFactory.NewStorage("wide");
            var so = new SerializedObject(definition);
            so.FindProperty("footprintSize").vector2IntValue = new Vector2Int(width, height);
            so.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        // --- The field ---

        [Test]
        public void Coverage_IsWrittenOnTheFootprintCells_AndOnThoseOnly()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            BuildingRuntime segment = NewSegment(definition, new GridCoord(3, 4));

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.5f) });

            Assert.AreEqual(0.5f, renderer.CoverageAt(new GridCoord(3, 4)), 0.0001f, "The cell the site occupies.");
            Assert.AreEqual(0f, renderer.CoverageAt(new GridCoord(2, 4)), 0.0001f, "West neighbour is untouched.");
            Assert.AreEqual(0f, renderer.CoverageAt(new GridCoord(4, 4)), 0.0001f, "East neighbour is untouched.");
            Assert.AreEqual(0f, renderer.CoverageAt(new GridCoord(3, 5)), 0.0001f, "North neighbour is untouched.");
            Assert.AreEqual(0f, renderer.CoverageAt(new GridCoord(3, 3)), 0.0001f, "South neighbour is untouched.");
        }

        /// <summary>
        /// The value written is the smoothed one the dissolve is showing, never the site's raw
        /// advancement - otherwise the ground steps at each delivery while the building glides.
        /// </summary>
        [Test]
        public void Coverage_TakesTheDisplayedProgress_NotAnythingElse()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            BuildingRuntime segment = NewSegment(definition, new GridCoord(1, 1));

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.25f) });
            Assert.AreEqual(0.25f, renderer.CoverageAt(new GridCoord(1, 1)), 0.0001f);

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.8f) });
            Assert.AreEqual(0.8f, renderer.CoverageAt(new GridCoord(1, 1)), 0.0001f);
        }

        [Test]
        public void AMultiCellFootprint_ConvertsEveryOneOfItsCells()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition wide = NewFootprint(3, 3);
            var origin = new GridCoord(5, 5);
            BuildingRuntime segment = NewSegment(wide, origin);

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });

            Assert.AreEqual(9, wide.FootprintCells.Length, "Precondition: a 3x3 footprint.");
            foreach (Vector2Int offset in wide.FootprintCells)
            {
                Assert.AreEqual(1f, renderer.CoverageAt(new GridCoord(origin.X + offset.x, origin.Y + offset.y)), 0.0001f,
                    "footprint cell " + offset);
            }

            Assert.AreEqual(0f, renderer.CoverageAt(new GridCoord(origin.X - 1, origin.Y)), 0.0001f,
                "One cell west of the footprint is outside it.");
            Assert.AreEqual(0f, renderer.CoverageAt(new GridCoord(origin.X + wide.FootprintSize.x, origin.Y)), 0.0001f,
                "And one cell past its east edge.");
        }

        // --- Decay ---

        [Test]
        public void ACellWithNoSite_FadesToZeroOverCoverageFadeSeconds()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            BuildingRuntime segment = NewSegment(definition, new GridCoord(2, 2));
            var cell = new GridCoord(2, 2);

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });
            Assert.AreEqual(1f, renderer.CoverageAt(cell), 0.0001f);

            // Half the fade with the site gone.
            renderer.Tick(FadeSeconds * 0.5f, OneZone(), NoSegments());
            Assert.AreEqual(0.5f, renderer.CoverageAt(cell), 0.0001f, "Half of coverageFadeSeconds removes half the coverage.");

            renderer.Tick(FadeSeconds * 0.5f, OneZone(), NoSegments());
            Assert.AreEqual(0f, renderer.CoverageAt(cell), 0.0001f, "The full duration takes it to exactly zero.");

            renderer.Tick(FadeSeconds, OneZone(), NoSegments());
            Assert.AreEqual(0f, renderer.CoverageAt(cell), 0.0001f, "And it never goes negative.");
        }

        [Test]
        public void AStillActiveSite_HoldsItsCellAgainstTheDecay()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            BuildingRuntime segment = NewSegment(definition, new GridCoord(2, 2));
            var live = new List<DrawnSegment> { new DrawnSegment(segment, 1f) };

            renderer.Tick(0f, OneZone(), live);
            renderer.Tick(FadeSeconds, OneZone(), live);

            Assert.AreEqual(1f, renderer.CoverageAt(new GridCoord(2, 2)), 0.0001f,
                "The site rewrites its cell after the decay pass, so a live chantier never fades.");
        }

        // --- Upload gating ---

        [Test]
        public void TheTexture_IsOnlyUploadedWhenTheFieldChanged()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            BuildingRuntime segment = NewSegment(definition, new GridCoord(0, 0));

            renderer.Tick(0f, OneZone(), NoSegments());
            int afterCreation = renderer.UploadCount;
            Assert.AreEqual(1, afterCreation, "A new zone uploads once, to start from a known field.");

            renderer.Tick(0.1f, OneZone(), NoSegments());
            renderer.Tick(0.1f, OneZone(), NoSegments());
            Assert.AreEqual(afterCreation, renderer.UploadCount, "An empty field that cannot change must not be re-uploaded.");

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.4f) });
            Assert.AreEqual(afterCreation + 1, renderer.UploadCount, "Writing coverage uploads once.");

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.4f) });
            Assert.AreEqual(afterCreation + 1, renderer.UploadCount, "Re-writing the same value changes nothing, so nothing is uploaded.");

            renderer.Tick(0.1f, OneZone(), NoSegments());
            Assert.AreEqual(afterCreation + 2, renderer.UploadCount, "Fading is a change, so it does upload.");
        }

        // --- Zone lifecycle ---

        [Test]
        public void AZoneThatCloses_ReleasesItsTexture()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);

            renderer.Tick(0f, OneZone(id: 7), NoSegments());
            Assert.AreEqual(1, renderer.ZoneCount);

            renderer.Tick(0f, new List<ZoneDescriptor>(), NoSegments());

            Assert.AreEqual(0, renderer.ZoneCount, "A zone that has fallen keeps nothing allocated.");
            Assert.AreEqual(0f, renderer.CoverageAt(new GridCoord(0, 0)), 0.0001f);
        }

        [Test]
        public void TwoZones_EachHoldTheirOwnField()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");

            var zones = new List<ZoneDescriptor>
            {
                new ZoneDescriptor(1, new GridCoord(0, 0), 4),
                new ZoneDescriptor(2, new GridCoord(100, 0), 4)
            };

            BuildingRuntime near = NewSegment(definition, new GridCoord(1, 0));
            BuildingRuntime far = NewSegment(definition, new GridCoord(101, 0));

            renderer.Tick(0f, zones, new List<DrawnSegment>
            {
                new DrawnSegment(near, 0.3f),
                new DrawnSegment(far, 0.9f)
            });

            Assert.AreEqual(2, renderer.ZoneCount);
            Assert.AreEqual(0.3f, renderer.CoverageAt(new GridCoord(1, 0)), 0.0001f);
            Assert.AreEqual(0.9f, renderer.CoverageAt(new GridCoord(101, 0)), 0.0001f);
        }

        [Test]
        public void ASiteOutsideEveryZone_IsSimplyNotDrawn()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            BuildingRuntime segment = NewSegment(definition, new GridCoord(500, 500));

            Assert.DoesNotThrow(() => renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) }));
            Assert.AreEqual(0f, renderer.CoverageAt(new GridCoord(500, 500)), 0.0001f);
        }
    }
}
