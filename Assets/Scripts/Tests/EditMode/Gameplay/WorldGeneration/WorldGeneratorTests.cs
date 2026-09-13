using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.WorldGeneration;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.WorldGeneration
{
    public class WorldGeneratorTests
    {
        const int MapSizeCells = 400;
        const int ResourceSeed = 12345;

        static WorldGenerationSettings NewSettings(int actionRadiusCells, int resourceSeed = ResourceSeed, bool randomizeResourceSeed = false)
        {
            var ironItem = TestDataFactory.NewItem("iron_ore", ItemType.Ore);
            var copperItem = TestDataFactory.NewItem("copper_ore", ItemType.Ore);
            var coalItem = TestDataFactory.NewItem("Coal_ore", ItemType.Component);

            var core = TestDataFactory.NewCore(actionRadiusCells, new Vector2Int(4, 4));
            var iron = TestDataFactory.NewOreDeposit(ironItem, new Vector2Int(2, 2));
            var copper = TestDataFactory.NewOreDeposit(copperItem, new Vector2Int(2, 2));
            var coal = TestDataFactory.NewOreDeposit(coalItem, new Vector2Int(2, 2));

            return TestDataFactory.NewWorldGenerationSettings(core, iron, copper, coal, resourceSeed, randomizeResourceSeed);
        }

        static Game.Gameplay.WorldGeneration.WorldGenerator Generate(WorldGenerationSettings settings)
        {
            var generator = new Game.Gameplay.WorldGeneration.WorldGenerator();
            generator.Generate(new GridRuntime(1f), MapSizeCells, settings, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem(), new ComputeSystem()));
            return generator;
        }

        /// <summary>The three resources and the band's own configured min/max for each - read from the settings rather than restated, so retuning OreBand's shipped defaults never desyncs this file from what it actually asserts.</summary>
        static IEnumerable<(string ItemId, int Min, int Max)> BandRanges(OreBand band)
        {
            yield return ("iron_ore", band.IronDepositsMin, band.IronDepositsMax);
            yield return ("copper_ore", band.CopperDepositsMin, band.CopperDepositsMax);
            yield return ("Coal_ore", band.CoalDepositsMin, band.CoalDepositsMax);
        }

        [Test]
        public void Generate_WithRoomyRadius_PlacesTheStartingClusterAndTheGuaranteedBand()
        {
            var settings = NewSettings(actionRadiusCells: 22);
            var grid = new GridRuntime(1f);
            var generator = new Game.Gameplay.WorldGeneration.WorldGenerator();

            generator.Generate(grid, MapSizeCells, settings, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem(), new ComputeSystem()));

            Vector2 coreCenter = new Vector2(generator.CoreOrigin.X + 2f, generator.CoreOrigin.Y + 2f);
            float Distance(DepositRuntime deposit)
            {
                float dx = deposit.Origin.X - coreCenter.x;
                float dy = deposit.Origin.Y - coreCenter.y;
                return Mathf.Sqrt(dx * dx + dy * dy);
            }

            OreBand band = settings.OreBands[0];
            foreach (var (itemId, bandMin, bandMax) in BandRanges(band))
            {
                var deposits = generator.OreDeposits.Where(d => d.ItemId == itemId).ToList();
                Assert.AreEqual(4, deposits.Count(d => Distance(d) <= generator.ActionRadiusCells), $"{itemId}: starting cluster x 4 deposits, within the starting radius");

                int bandCount = deposits.Count(d => Distance(d) > generator.ActionRadiusCells);
                Assert.GreaterOrEqual(bandCount, bandMin, $"{itemId}: band deposit count below its minimum");
                Assert.LessOrEqual(bandCount, bandMax, $"{itemId}: band deposit count above its maximum");
            }
        }

        [Test]
        public void Generate_WithAPinnedSeed_ProducesIdenticalDepositOrigins()
        {
            var generatorA = Generate(NewSettings(actionRadiusCells: 22));
            var generatorB = Generate(NewSettings(actionRadiusCells: 22));

            CollectionAssert.AreEqual(
                generatorA.OreDeposits.Select(d => d.Origin).ToList(),
                generatorB.OreDeposits.Select(d => d.Origin).ToList());
            Assert.AreEqual(ResourceSeed, generatorA.ResourceSeed, "A pinned world reports the seed it was pinned to.");
        }

        /// <summary>
        /// The shipped behaviour, and the thing that was missing: placement was random in shape and
        /// fixed in fact - one seed in the settings asset - so every new game rebuilt the same world
        /// and the ore looked hand-placed.
        ///
        /// Asserted over several worlds rather than two, because two random draws can legitimately
        /// land a cluster in the same cell; five all matching cannot happen by chance. What it pins
        /// is that the seed is drawn per generation, not that any particular pair differs.
        /// </summary>
        [Test]
        public void Generate_WithARandomSeed_PutsTheOreSomewhereElseEachNewGame()
        {
            var origins = new List<List<GridCoord>>();
            var seeds = new List<int>();

            for (int i = 0; i < 5; i++)
            {
                var generator = Generate(NewSettings(actionRadiusCells: 22, randomizeResourceSeed: true));
                origins.Add(generator.OreDeposits.Select(d => d.Origin).ToList());
                seeds.Add(generator.ResourceSeed);
            }

            Assert.Greater(seeds.Distinct().Count(), 1, "Every new game drew the same seed - nothing was randomized.");
            Assert.Greater(origins.Distinct(new OriginListComparer()).Count(), 1,
                "Five worlds, one layout: the ore lands in the same place every game.");
        }

        sealed class OriginListComparer : IEqualityComparer<List<GridCoord>>
        {
            public bool Equals(List<GridCoord> a, List<GridCoord> b) => a.SequenceEqual(b);
            public int GetHashCode(List<GridCoord> value) => value.Count;
        }

        /// <summary>
        /// A random seed changes where the ore lands, and how many deposits its band draws within its
        /// own min-max, never whether the world is playable. The guarantee is the same one Generate()
        /// throws to protect, checked over many draws rather than over the one seed that happened to
        /// be stored in the asset.
        /// </summary>
        [Test]
        public void Generate_WithARandomSeed_StillPlacesEveryGuaranteedCluster()
        {
            for (int i = 0; i < 25; i++)
            {
                var settings = NewSettings(actionRadiusCells: 22, randomizeResourceSeed: true);
                var generator = Generate(settings);
                OreBand band = settings.OreBands[0];

                foreach (var (itemId, bandMin, bandMax) in BandRanges(band))
                {
                    int count = generator.OreDeposits.Count(d => d.ItemId == itemId);
                    Assert.GreaterOrEqual(count, 4 + bandMin, $"draw {i}, seed {generator.ResourceSeed}, {itemId}");
                    Assert.LessOrEqual(count, 4 + bandMax, $"draw {i}, seed {generator.ResourceSeed}, {itemId}");
                }
            }
        }

        /// <summary>
        /// The guaranteed band, on every seed: each resource's deposit count falls within the band's
        /// own configured min-max, and the cluster's centre falls within the band's own distance
        /// range. Only the centre is held to the band; the deposits may overhang it by half the
        /// cluster's diagonal.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        public void GuaranteedBand_HoldsItsConfiguredCount_CentredWithinItsDistanceRange(int seed)
        {
            var settings = NewSettings(22, seed);
            Game.Gameplay.WorldGeneration.WorldGenerator generator = Generate(settings);
            Vector2 coreCenter = new Vector2(generator.CoreOrigin.X + 2f, generator.CoreOrigin.Y + 2f);
            OreBand band = settings.OreBands[0];

            foreach (var (itemId, bandMin, bandMax) in BandRanges(band))
            {
                var beyondStart = generator.OreDeposits
                    .Where(d => d.ItemId == itemId && Vector2.Distance(new Vector2(d.Origin.X, d.Origin.Y), coreCenter) > 22f)
                    .ToList();

                Assert.GreaterOrEqual(beyondStart.Count, bandMin, $"seed {seed}, {itemId}: band deposit count");
                Assert.LessOrEqual(beyondStart.Count, bandMax, $"seed {seed}, {itemId}: band deposit count");

                // The cluster's centre is the middle of its deposits' own centres (2x2 each here).
                var centre = new Vector2(beyondStart.Average(d => d.Origin.X + 1f), beyondStart.Average(d => d.Origin.Y + 1f));
                float distance = Vector2.Distance(centre, coreCenter);
                Assert.GreaterOrEqual(distance, band.MinDistanceCells - 1f, $"seed {seed}, {itemId}: centre {distance:F1} cells out");
                Assert.LessOrEqual(distance, band.MaxDistanceCells + 1f, $"seed {seed}, {itemId}: centre {distance:F1} cells out");
            }
        }

        [Test]
        public void Generate_ActionRadiusTooSmallForAGuaranteedCluster_Throws()
        {
            // maxDistance (radius - clusterFootprint) must exceed InRadiusMinDistanceCells (10)
            // for any in-radius cluster to have a chance; radius=12 gives maxDistance=8 < 10, so
            // the placement loop never runs a single attempt and the guaranteed cluster fails.
            var settings = NewSettings(actionRadiusCells: 12);
            var grid = new GridRuntime(1f);
            var generator = new Game.Gameplay.WorldGeneration.WorldGenerator();

            Assert.Throws<System.InvalidOperationException>(() =>
                generator.Generate(grid, MapSizeCells, settings, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem(), new ComputeSystem())));
        }
    }
}
