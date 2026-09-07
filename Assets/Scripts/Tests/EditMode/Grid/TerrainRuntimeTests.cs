using System.Collections.Generic;
using System.Diagnostics;
using Game.Core;
using Game.Grid;
using NUnit.Framework;

namespace Game.Tests.EditMode.Grid
{
    /// <summary>
    /// Terrain is a pure function of the seed and the coordinate, and nothing is stored.
    ///
    /// The order tests below are the ones the large-map directive §4.3 asks for. They pass trivially
    /// now, and that is the point: with no storage there is no order to vary, so the property cannot
    /// be broken rather than merely being respected. They stay as the guard for the day someone adds
    /// a per-chunk cache - which is exactly when order would start to matter again.
    /// </summary>
    public class TerrainRuntimeTests
    {
        /// <summary>The shipped chunk size. Nothing in terrain is chunked today; the boundary cases below exist so that a cache added later cannot introduce a seam unnoticed.</summary>
        const int ChunkSize = 64;

        static TerrainRuntime NewTerrain(int size = 200, int seed = 42)
            => new TerrainRuntime(size, seed, terrainScale: 10f, proportion: 0.3f);

        [Test]
        public void SameSeedAndParameters_ProduceIdenticalTerrain()
        {
            var a = new TerrainRuntime(size: 20, seed: 42, terrainScale: 10f, proportion: 0.3f);
            var b = new TerrainRuntime(size: 20, seed: 42, terrainScale: 10f, proportion: 0.3f);

            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    var cell = new GridCoord(x, y);
                    Assert.AreEqual(a.GetTerrainType(cell), b.GetTerrainType(cell));
                }
            }
        }

        [Test]
        public void DifferentSeed_CanProduceDifferentTerrain()
        {
            var a = new TerrainRuntime(size: 20, seed: 1, terrainScale: 10f, proportion: 0.3f);
            var b = new TerrainRuntime(size: 20, seed: 2, terrainScale: 10f, proportion: 0.3f);

            bool anyDifference = false;
            for (int y = 0; y < 20 && !anyDifference; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    var cell = new GridCoord(x, y);
                    if (a.GetTerrainType(cell) != b.GetTerrainType(cell))
                    {
                        anyDifference = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(anyDifference);
        }

        [Test]
        public void OutOfBoundsCell_ReturnsBase()
        {
            var terrain = new TerrainRuntime(size: 10, seed: 0, terrainScale: 10f, proportion: 0.3f);

            Assert.AreEqual(TerrainType.Base, terrain.GetTerrainType(new GridCoord(-1, 0)));
            Assert.AreEqual(TerrainType.Base, terrain.GetTerrainType(new GridCoord(10, 10)));
        }

        [Test]
        public void SampleContinuous_IsDeterministicPerCoordinate()
        {
            var terrain = new TerrainRuntime(size: 10, seed: 7, terrainScale: 10f, proportion: 0.3f);

            float a = terrain.SampleContinuous(3.5f, 2.25f);
            float b = terrain.SampleContinuous(3.5f, 2.25f);

            Assert.AreEqual(a, b);
        }

        // ---- Order independence (directive §4.3) ----

        /// <summary>
        /// The same region asked about forwards and backwards. Worth stating plainly that this can no
        /// longer fail: there is no state for an order to disturb. It starts being able to fail the
        /// day one is introduced, which is why it is kept.
        /// </summary>
        [Test]
        public void TheOrderCellsAreAskedAbout_ChangesNothing()
        {
            TerrainRuntime forward = NewTerrain();
            TerrainRuntime backward = NewTerrain();

            var seen = new Dictionary<GridCoord, TerrainType>();

            for (int y = 100; y < 140; y++)
            {
                for (int x = 100; x < 140; x++)
                {
                    var cell = new GridCoord(x, y);
                    seen[cell] = forward.GetTerrainType(cell);
                }
            }

            for (int y = 139; y >= 100; y--)
            {
                for (int x = 139; x >= 100; x--)
                {
                    var cell = new GridCoord(x, y);
                    Assert.AreEqual(seen[cell], backward.GetTerrainType(cell), cell.ToString());
                }
            }
        }

        /// <summary>
        /// A region straddling chunk boundaries rather than sitting inside one. A region wholly inside
        /// a chunk would pass even if a chunked generator had no margin at all, so it would prove
        /// nothing about the seam - which is the defect this shape exists to catch.
        /// </summary>
        [Test]
        public void ARegionStraddlingChunkBoundaries_IsIdenticalWhicheverSideIsAskedFirst()
        {
            TerrainRuntime nearSideFirst = NewTerrain();
            TerrainRuntime farSideFirst = NewTerrain();

            int lo = ChunkSize - 3;
            int hi = ChunkSize + 3;

            // One instance walks the seam left-to-right, the other right-to-left, so each cell is
            // reached with a different history behind it.
            var walkedForward = new Dictionary<GridCoord, TerrainType>();
            for (int y = lo; y < hi; y++)
            {
                for (int x = lo; x < hi; x++)
                {
                    var cell = new GridCoord(x, y);
                    walkedForward[cell] = nearSideFirst.GetTerrainType(cell);
                }
            }

            for (int y = hi - 1; y >= lo; y--)
            {
                for (int x = hi - 1; x >= lo; x--)
                {
                    var cell = new GridCoord(x, y);
                    Assert.AreEqual(walkedForward[cell], farSideFirst.GetTerrainType(cell),
                        "cell " + cell + " on the chunk seam differs with the order it was reached");
                }
            }
        }

        [Test]
        public void ACellIsTheSameWhetherAskedAloneOrAfterItsWholeNeighbourhood()
        {
            var probe = new GridCoord(ChunkSize, ChunkSize);

            TerrainRuntime alone = NewTerrain();
            TerrainType askedAlone = alone.GetTerrainType(probe);

            TerrainRuntime warmed = NewTerrain();
            for (int y = probe.Y - 2; y <= probe.Y + 2; y++)
            {
                for (int x = probe.X - 2; x <= probe.X + 2; x++) warmed.GetTerrainType(new GridCoord(x, y));
            }

            Assert.AreEqual(askedAlone, warmed.GetTerrainType(probe));
        }

        // ---- Nothing is materialised ----

        /// <summary>
        /// A 10 000-cell world used to allocate and fill 100 million entries at construction.
        /// Constructing one has to be free now - if this ever starts taking time, storage has come
        /// back.
        /// </summary>
        [Test]
        public void ConstructingAHugeWorld_CostsNothing()
        {
            var watch = Stopwatch.StartNew();
            var terrain = new TerrainRuntime(size: 10000, seed: 3, terrainScale: 10f, proportion: 0.3f);
            watch.Stop();

            Assert.Less(watch.ElapsedMilliseconds, 100,
                "constructing a 10 000-cell terrain took " + watch.ElapsedMilliseconds + " ms - something is being materialised.");

            Assert.AreEqual(TerrainType.Base, terrain.GetTerrainType(new GridCoord(-1, -1)));

            TerrainType far = terrain.GetTerrainType(new GridCoord(9999, 9999));
            Assert.IsTrue(far == TerrainType.Base || far == TerrainType.Top, "and it still answers at the far corner");
        }

        // ---- What a reloaded save relies on ----

        /// <summary>
        /// Terrain is not saved, it is re-derived, so the four numbers the save carries have to be
        /// enough to rebuild exactly the same world. This property is the whole of why that is safe.
        /// </summary>
        [Test]
        public void TheFourSavedNumbers_RebuildTheSameWorld()
        {
            var original = new TerrainRuntime(size: 120, seed: 20260907, terrainScale: 14f, proportion: 0.42f);
            var reloaded = new TerrainRuntime(original.Size, original.Seed, original.TerrainScale, original.Proportion);

            for (int y = 0; y < original.Size; y += 3)
            {
                for (int x = 0; x < original.Size; x += 3)
                {
                    var cell = new GridCoord(x, y);
                    Assert.AreEqual(original.GetTerrainType(cell), reloaded.GetTerrainType(cell), cell.ToString());
                }
            }
        }

        /// <summary>
        /// A different seed is a different world - which is precisely why the save has to carry the
        /// running world's numbers and not the settings asset's. Editing the asset between two
        /// sessions would otherwise regenerate this much terrain underneath buildings already placed.
        /// </summary>
        [Test]
        public void ADifferentSeedIsADifferentWorld()
        {
            var fromSave = new TerrainRuntime(size: 120, seed: 1, terrainScale: 14f, proportion: 0.42f);
            var fromEditedAsset = new TerrainRuntime(size: 120, seed: 2, terrainScale: 14f, proportion: 0.42f);

            int differences = 0;
            for (int y = 0; y < 120; y += 3)
            {
                for (int x = 0; x < 120; x += 3)
                {
                    var cell = new GridCoord(x, y);
                    if (fromSave.GetTerrainType(cell) != fromEditedAsset.GetTerrainType(cell)) differences++;
                }
            }

            Assert.Greater(differences, 0, "If these matched, the seed would not be doing anything.");
        }

        // ---- Frozen against the runtime ----

        /// <summary>
        /// Hard-coded terrain, for the one reason that justifies literals: this world is
        /// <b>re-derived at every load rather than saved</b>, while the buildings standing on it are
        /// saved. If the arithmetic underneath ever answers differently - a Unity upgrade, a
        /// well-meaning refactor of the hash - the ground recomposes under a base the player built,
        /// and they find it straddling terrain they do not recognise.
        ///
        /// A test that recomputed its expectation would move with the change and see nothing. Four
        /// Top and four Base, deliberately: an implementation that returned one constant would pass
        /// half a lopsided set.
        ///
        /// <b>If this fails, do not update the values.</b> Every existing world has just changed
        /// shape. Find out what moved.
        /// </summary>
        [TestCase(287, 0, TerrainType.Top)]
        [TestCase(164, 37, TerrainType.Top)]
        [TestCase(246, 74, TerrainType.Top)]
        [TestCase(205, 148, TerrainType.Top)]
        [TestCase(0, 0, TerrainType.Base)]
        [TestCase(41, 0, TerrainType.Base)]
        [TestCase(82, 0, TerrainType.Base)]
        [TestCase(123, 0, TerrainType.Base)]
        public void TerrainIsFrozenAgainstTheRuntime(int x, int y, TerrainType expected)
        {
            var terrain = new TerrainRuntime(size: 300, seed: 20260907, terrainScale: 45f, proportion: 0.3f);

            Assert.AreEqual(expected, terrain.GetTerrainType(new GridCoord(x, y)));
        }

        /// <summary>
        /// The offsets come from an explicit hash, never from System.Random - which has no guarantee
        /// of stability across runtime versions and would therefore reshape every derived world on a
        /// Unity upgrade. Asserted rather than trusted: a seed of zero has to produce a real offset,
        /// which is exactly what a mixer fed no salt would fail to do.
        /// </summary>
        [Test]
        public void AZeroSeedStillProducesARealWorld()
        {
            var zero = new TerrainRuntime(size: 60, seed: 0, terrainScale: 45f, proportion: 0.3f);
            var one = new TerrainRuntime(size: 60, seed: 1, terrainScale: 45f, proportion: 0.3f);

            int differences = 0;
            int tops = 0;

            for (int y = 0; y < 60; y++)
            {
                for (int x = 0; x < 60; x++)
                {
                    var cell = new GridCoord(x, y);
                    if (zero.GetTerrainType(cell) == TerrainType.Top) tops++;
                    if (zero.GetTerrainType(cell) != one.GetTerrainType(cell)) differences++;
                }
            }

            Assert.Greater(tops, 0, "a zero seed produced a uniform world - its offsets did not mix");
            Assert.Greater(differences, 0, "seed 0 and seed 1 produced the same world");
        }

    }
}
