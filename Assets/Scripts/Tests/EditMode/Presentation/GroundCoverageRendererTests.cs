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
using UnityEngine.Experimental.Rendering;

using DrawnSegment = Game.Presentation.ConstructionSiteVisualSync.DrawnSegment;
using ZoneDescriptor = Game.Presentation.GroundCoverageRenderer.ZoneDescriptor;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// directive-materialisation-nano.md §7: the per-zone ground coverage field. Nothing here
    /// asserts on rendering - GroundCoverageRenderer.Tick is frame-free precisely so the field, the
    /// conversion front, its retreat and the upload gating can be driven step by step.
    /// </summary>
    public class GroundCoverageRendererTests
    {
        const float FadeSeconds = 4f;
        const float OverflowCells = 0.45f;

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

        /// <summary>
        /// The noise is off by default so a cell's threshold is exactly its rounded-box distance and
        /// the expected values below can be written out; the two tests that are about the noise turn
        /// it back on.
        /// </summary>
        NanoConstructionSettings NewSettings(float noiseWeight = 0f, int texelsPerCell = 4, float leadShare = 1f)
        {
            var settings = ScriptableObject.CreateInstance<NanoConstructionSettings>();
            _spawned.Add(settings);

            var so = new SerializedObject(settings);
            so.FindProperty("coverageFadeSeconds").floatValue = FadeSeconds;
            so.FindProperty("groundOverflowCells").floatValue = OverflowCells;
            so.FindProperty("groundNoiseWeight").floatValue = noiseWeight;
            so.FindProperty("groundNoiseScale").floatValue = 1.2f;
            so.FindProperty("groundTexelsPerCell").intValue = texelsPerCell;

            // The lead is neutral by default, so every other test can speak in the ground's own
            // progress instead of restating the remap in each expectation. One test owns it.
            so.FindProperty("groundLeadShare").floatValue = leadShare;

            so.FindProperty("coverageShader").objectReferenceValue = Shader.Find("Sprites/Default");
            so.ApplyModifiedPropertiesWithoutUndo();

            return settings;
        }

        GroundCoverageRenderer NewRenderer(out GridRuntime grid, float noiseWeight = 0f, int texelsPerCell = 4, float leadShare = 1f)
        {
            grid = new GridRuntime(1f);

            var go = new GameObject("GroundCoverage");
            _spawned.Add(go);

            var renderer = go.AddComponent<GroundCoverageRenderer>();
            renderer.Initialize(grid, NewSettings(noiseWeight, texelsPerCell, leadShare));
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
        /// research to construct, none of which this layer touches. Only the footprint matters here.
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
        public void AtFullProgress_TheWholeFootprintIsConverted()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition wide = NewFootprint(3, 3);
            var origin = new GridCoord(5, 5);
            BuildingRuntime segment = NewSegment(wide, origin);

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });

            Assert.AreEqual(9, wide.FootprintCells.Length, "Precondition: a 3x3 footprint.");
            foreach (Vector2Int offset in wide.FootprintCells)
            {
                Assert.IsTrue(renderer.IsConvertedAt(new GridCoord(origin.X + offset.x, origin.Y + offset.y)),
                    "footprint cell " + offset);
            }
        }

        /// <summary>
        /// The reason the field is not bounded by the footprint. A threshold that stops at the
        /// outline makes the rectangle itself the final boundary, so the finished patch is a square
        /// however the front travels inside it. The threshold keeps rising through the ring instead,
        /// which both rounds the corners off and lets the conversion spill onto the neighbours.
        /// </summary>
        [Test]
        public void AtFullProgress_TheConversionSpillsOntoTheSides_ButNotTheDiagonals()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            var cell = new GridCoord(3, 4);
            BuildingRuntime segment = NewSegment(definition, cell);

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });

            Assert.IsTrue(renderer.IsConvertedAt(cell), "The cell the site occupies.");
            Assert.IsTrue(renderer.IsConvertedAt(new GridCoord(cell.X + 1, cell.Y)), "The conversion reaches past the footprint's edge.");
            Assert.IsTrue(renderer.IsConvertedAt(new GridCoord(cell.X - 1, cell.Y)), "On every side.");
            Assert.IsTrue(renderer.IsConvertedAt(new GridCoord(cell.X, cell.Y + 1)));
            Assert.IsTrue(renderer.IsConvertedAt(new GridCoord(cell.X, cell.Y - 1)));

            Assert.Greater(renderer.FrontDistanceAt(new GridCoord(cell.X + 1, cell.Y)),
                renderer.FrontDistanceAt(new GridCoord(cell.X + 1, cell.Y + 1)),
                "A diagonal is further from the footprint than a side, so it trails it - the boundary is round, not a bigger square.");

            Assert.IsFalse(renderer.IsConvertedAt(new GridCoord(cell.X + 1, cell.Y + 1)), "And has not been reached.");

            Assert.IsFalse(renderer.IsConvertedAt(new GridCoord(cell.X + 2, cell.Y)),
                "The spill is bounded by groundOverflowCells, not open-ended.");
        }

        /// <summary>
        /// The ground runs ahead of the building and is finished long before it. It has to: the
        /// sprite covers its own footprint, so a ground on the same clock is hidden for the whole
        /// build and only ever shows as a thin halo in the last instants.
        /// </summary>
        [Test]
        public void TheGround_FinishesAtItsLeadShareOfTheBuild()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _, leadShare: 0.5f);
            StorageDefinition wide = NewFootprint(3, 3);
            var origin = new GridCoord(5, 5);
            BuildingRuntime segment = NewSegment(wide, origin);

            var footprint = new Vector2Int(3, 3);

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.5f) });
            float atLead = renderer.MinFrontDistanceOver(origin, footprint);
            Assert.Greater(atLead, 0f, "At half the build the ground is already converted everywhere.");

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });
            Assert.AreEqual(atLead, renderer.MinFrontDistanceOver(origin, footprint), 0.0001f,
                "And it stays exactly there for the rest of the build rather than carrying on.");

            GroundCoverageRenderer level = NewRenderer(out _);
            level.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.5f) });
            Assert.Less(level.MinFrontDistanceOver(origin, footprint), 0f,
                "Without a lead the same instant still leaves the corners unconverted.");
        }

        /// <summary>
        /// The guarantee the ground's own phase owes: when it ends, the field is past the front over
        /// every texel of the footprint - not merely at its cell centres, which are the most
        /// converted points of all. It has to hold by construction and not by luck at the current
        /// settings, so this pushes the noise to its ceiling and takes the largest footprint in play.
        /// </summary>
        [Test]
        public void AtTheEndOfItsPhase_TheFieldIsPastTheFrontEverywhereOnTheFootprint()
        {
            foreach (Vector2Int size in new[] { new Vector2Int(1, 1), new Vector2Int(3, 3), new Vector2Int(4, 4), new Vector2Int(1, 5), new Vector2Int(9, 9) })
            {
                GroundCoverageRenderer renderer = NewRenderer(out _, noiseWeight: 1f);
                StorageDefinition definition = NewFootprint(size.x, size.y);
                var origin = new GridCoord(-4, -4);
                BuildingRuntime segment = NewSegment(definition, origin);

                renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });

                Assert.Greater(renderer.MinFrontDistanceOver(origin, size), 0f,
                    "A " + size.x + "x" + size.y + " footprint still has an unconverted spot at the end of the ground's phase.");
            }
        }

        /// <summary>
        /// Writing the site's progress straight onto every cell gave the whole footprint one value,
        /// so the square lit and faded as a single block with no front travelling across it. Each
        /// point now carries its own static threshold, and the conversion leaves the centre and
        /// reaches the corners last.
        /// </summary>
        [Test]
        public void TheConversion_StartsAtTheCentreAndReachesTheCornersLast()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition wide = NewFootprint(3, 3);
            var origin = new GridCoord(5, 5);
            BuildingRuntime segment = NewSegment(wide, origin);

            var centre = new GridCoord(origin.X + 1, origin.Y + 1);
            var edge = new GridCoord(origin.X + 1, origin.Y);
            var corner = new GridCoord(origin.X, origin.Y);

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.2f) });

            Assert.IsTrue(renderer.IsConvertedAt(centre), "The centre converts first.");
            Assert.IsFalse(renderer.IsConvertedAt(edge), "An edge cell is further out and has not started.");
            Assert.IsFalse(renderer.IsConvertedAt(corner), "A corner is the furthest of all.");

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.7f) });

            Assert.IsTrue(renderer.IsConvertedAt(edge), "By 0.7 the front has passed the edge cells.");
            Assert.Greater(renderer.FrontDistanceAt(edge), renderer.FrontDistanceAt(corner),
                "And the corner still trails the edge.");

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });

            Assert.IsTrue(renderer.IsConvertedAt(corner), "Reaching 1 must convert even the corners.");
        }

        [Test]
        public void AtZeroProgress_NothingIsConverted()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition wide = NewFootprint(3, 3);
            var origin = new GridCoord(5, 5);
            BuildingRuntime segment = NewSegment(wide, origin);

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0f) });

            foreach (Vector2Int offset in wide.FootprintCells)
            {
                Assert.IsFalse(renderer.IsConvertedAt(new GridCoord(origin.X + offset.x, origin.Y + offset.y)),
                    "cell " + offset);
            }
        }

        /// <summary>The threshold depends on the point alone, never on time, so a footprint always converts in the same order.</summary>
        [Test]
        public void TheThresholds_AreStatic()
        {
            GroundCoverageRenderer first = NewRenderer(out _, noiseWeight: 0.25f);
            GroundCoverageRenderer second = NewRenderer(out _, noiseWeight: 0.25f);
            StorageDefinition wide = NewFootprint(3, 3);
            var origin = new GridCoord(5, 5);
            BuildingRuntime segment = NewSegment(wide, origin);

            first.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.5f) });
            second.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 0.5f) });

            foreach (Vector2Int offset in wide.FootprintCells)
            {
                var cell = new GridCoord(origin.X + offset.x, origin.Y + offset.y);
                Assert.AreEqual(first.FrontDistanceAt(cell), second.FrontDistanceAt(cell), 0.0001f, "cell " + offset);
            }
        }

        // --- The noise, and the resolution it needs ---

        /// <summary>
        /// Without noise the field is symmetric about the footprint, so four cells at the same
        /// distance carry the same value and the patch is a perfectly regular rounded rectangle. The
        /// noise is what makes the outer boundary irregular - the whole point of pushing the field
        /// past the footprint in the first place.
        /// </summary>
        [Test]
        public void TheNoise_BreaksTheSymmetryOfTheBoundary()
        {
            StorageDefinition wide = NewFootprint(3, 3);
            var origin = new GridCoord(5, 5);
            BuildingRuntime segment = NewSegment(wide, origin);
            var live = new List<DrawnSegment> { new DrawnSegment(segment, 1f) };

            var ring = new[]
            {
                new GridCoord(origin.X + 1, origin.Y - 1),
                new GridCoord(origin.X + 1, origin.Y + 3),
                new GridCoord(origin.X - 1, origin.Y + 1),
                new GridCoord(origin.X + 3, origin.Y + 1)
            };

            GroundCoverageRenderer clean = NewRenderer(out _);
            clean.Tick(0f, OneZone(), live);

            foreach (GridCoord cell in ring)
            {
                Assert.AreEqual(clean.FrontDistanceAt(ring[0]), clean.FrontDistanceAt(cell), 0.005f,
                    "Without noise the four sides are interchangeable: " + cell.X + "," + cell.Y);
            }

            GroundCoverageRenderer noisy = NewRenderer(out _, noiseWeight: 0.25f);
            noisy.Tick(0f, OneZone(), live);

            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (GridCoord cell in ring)
            {
                float distance = noisy.FrontDistanceAt(cell);
                min = Mathf.Min(min, distance);
                max = Mathf.Max(max, distance);
            }

            Assert.Greater(max - min, 0.02f, "With noise they no longer are, so the outline stops being a shape.");
        }

        /// <summary>
        /// The noise can only break the outline up at the resolution the field is stored at. At one
        /// texel per cell every point inside a cell reads the same value, so the boundary can only
        /// ever follow the grid.
        /// </summary>
        [Test]
        public void TheField_IsFinerThanOneValuePerCell()
        {
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            var cell = new GridCoord(2, 2);
            BuildingRuntime segment = NewSegment(definition, cell);
            var live = new List<DrawnSegment> { new DrawnSegment(segment, 0.5f) };

            GroundCoverageRenderer coarse = NewRenderer(out GridRuntime coarseGrid, texelsPerCell: 1);
            coarse.Tick(0f, OneZone(), live);

            GroundCoverageRenderer fine = NewRenderer(out _, texelsPerCell: 4);
            fine.Tick(0f, OneZone(), live);

            Vector2 centre = coarseGrid.CellCenterToWorld(cell);
            Vector2 offCentre = centre + new Vector2(0.35f, 0.35f);

            Assert.AreEqual(coarse.FrontDistanceAtWorld(centre), coarse.FrontDistanceAtWorld(offCentre), 0.0001f,
                "One texel per cell cannot tell two points of the same cell apart.");

            Assert.AreNotEqual(fine.FrontDistanceAtWorld(centre), fine.FrontDistanceAtWorld(offCentre),
                "Four texels per cell can.");

            Assert.AreEqual(33, coarse.TexelSideOf(1), "A 33-cell zone at one texel per cell.");
            Assert.AreEqual(33 * 4, fine.TexelSideOf(1), "And the texture really is allocated at the finer resolution.");
        }

        /// <summary>
        /// The field is data, not colour. The project renders in Linear colour space, so a texture
        /// carrying the default sRGB flag is gamma-decoded by the GPU on every sample: the byte
        /// meaning "exactly on the front" would arrive as 0.216 instead of 0.502, and everything
        /// within reach of the front would be clipped away. Nothing in C# can observe that - the
        /// decode happens in the sampler - so the format itself is what gets asserted.
        /// </summary>
        [Test]
        public void TheFieldTexture_IsLinear_NotSrgb()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            renderer.Tick(0f, OneZone(), NoSegments());

            Texture2D texture = renderer.TextureOf(1);

            Assert.IsNotNull(texture);
            Assert.AreEqual(GraphicsFormat.R8_UNorm, texture.graphicsFormat,
                "R8_SRGB would have the sampler gamma-decode a signed distance field.");
        }

        // --- The retreat ---

        /// <summary>
        /// The front retreats the way it came instead of the field dimming in place. Subtracting
        /// from the stored values would take the whole converted plateau down through the rim band
        /// at once and flash the entire patch on its way out.
        /// </summary>
        [Test]
        public void ASiteThatStopsBeingDrawn_WalksItsFrontBackToNothing()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition wide = NewFootprint(3, 3);
            var origin = new GridCoord(2, 2);
            BuildingRuntime segment = NewSegment(wide, origin);

            var centre = new GridCoord(origin.X + 1, origin.Y + 1);
            var corner = new GridCoord(origin.X, origin.Y);

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });
            Assert.IsTrue(renderer.IsConvertedAt(corner));

            renderer.Tick(FadeSeconds * 0.7f, OneZone(), NoSegments());

            Assert.IsTrue(renderer.IsConvertedAt(centre), "Well into the fade the centre is still converted...");
            Assert.IsFalse(renderer.IsConvertedAt(corner), "...and the front has already left the corners.");

            renderer.Tick(FadeSeconds * 0.3f, OneZone(), NoSegments());
            Assert.IsFalse(renderer.IsConvertedAt(centre), "The full duration takes it back to nothing.");

            renderer.Tick(FadeSeconds, OneZone(), NoSegments());
            Assert.IsFalse(renderer.IsConvertedAt(centre), "And it stays there.");
        }

        [Test]
        public void AStillActiveSite_HoldsItsGroundAgainstTheRetreat()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            BuildingRuntime segment = NewSegment(definition, new GridCoord(2, 2));
            var live = new List<DrawnSegment> { new DrawnSegment(segment, 1f) };

            renderer.Tick(0f, OneZone(), live);
            renderer.Tick(FadeSeconds, OneZone(), live);

            Assert.IsTrue(renderer.IsConvertedAt(new GridCoord(2, 2)),
                "A site that is still drawn keeps its own progress, so its ground never retreats.");
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

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });
            Assert.AreEqual(afterCreation + 1, renderer.UploadCount, "Writing a front uploads once.");

            renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) });
            Assert.AreEqual(afterCreation + 1, renderer.UploadCount, "Re-writing the same progress changes nothing, so nothing is uploaded.");

            renderer.Tick(0.1f, OneZone(), NoSegments());
            Assert.AreEqual(afterCreation + 2, renderer.UploadCount, "A retreating front is a change, so it does upload.");
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
            Assert.IsFalse(renderer.IsConvertedAt(new GridCoord(0, 0)));
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
                new DrawnSegment(near, 1f),
                new DrawnSegment(far, 1f)
            });

            Assert.AreEqual(2, renderer.ZoneCount);
            Assert.IsTrue(renderer.IsConvertedAt(new GridCoord(1, 0)), "Each zone holds its own site.");
            Assert.IsTrue(renderer.IsConvertedAt(new GridCoord(101, 0)));
            Assert.IsFalse(renderer.IsConvertedAt(new GridCoord(50, 0)), "And nothing lies between them.");
        }

        [Test]
        public void ASiteOutsideEveryZone_IsSimplyNotDrawn()
        {
            GroundCoverageRenderer renderer = NewRenderer(out _);
            StorageDefinition definition = TestDataFactory.NewStorage("target");
            BuildingRuntime segment = NewSegment(definition, new GridCoord(500, 500));

            Assert.DoesNotThrow(() => renderer.Tick(0f, OneZone(), new List<DrawnSegment> { new DrawnSegment(segment, 1f) }));
            Assert.IsFalse(renderer.IsConvertedAt(new GridCoord(500, 500)));
        }
    }
}
