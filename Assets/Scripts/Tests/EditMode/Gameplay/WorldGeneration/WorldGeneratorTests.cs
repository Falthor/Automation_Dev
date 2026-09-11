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
        const int MapSizeCells = 200;
        const int ResourceSeed = 12345;

        /// <summary>
        /// Between the two rings of invitation clusters, measured on a deposit's origin: the first
        /// ring's centres stop at 29 cells and its far deposits at about 32.5, the second's start at
        /// 40 and its nearest deposits at about 34.8 - rounding of the centre included.
        /// </summary>
        const float RingsSplitCells = 33.5f;

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
            generator.Generate(new GridRuntime(1f), MapSizeCells, settings, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            return generator;
        }

        [Test]
        public void Generate_WithRoomyRadius_PlacesEveryClusterOfEveryRing()
        {
            var settings = NewSettings(actionRadiusCells: 22);
            var grid = new GridRuntime(1f);
            var generator = new Game.Gameplay.WorldGeneration.WorldGenerator();

            generator.Generate(grid, MapSizeCells, settings, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));

            // In the radius 4 of each; first ring 4 of each; second ring 8 iron, 8 copper, 4 coal.
            Assert.AreEqual(16, generator.OreDeposits.Count(d => d.ItemId == "iron_ore"));
            Assert.AreEqual(16, generator.OreDeposits.Count(d => d.ItemId == "copper_ore"));
            Assert.AreEqual(12, generator.OreDeposits.Count(d => d.ItemId == "Coal_ore"));

            Vector2 coreCenter = new Vector2(generator.CoreOrigin.X + 2f, generator.CoreOrigin.Y + 2f);
            float Distance(DepositRuntime deposit)
            {
                float dx = deposit.Origin.X - coreCenter.x;
                float dy = deposit.Origin.Y - coreCenter.y;
                return Mathf.Sqrt(dx * dx + dy * dy);
            }

            foreach (var (itemId, secondRing) in new[] { ("iron_ore", 8), ("copper_ore", 8), ("Coal_ore", 4) })
            {
                var deposits = generator.OreDeposits.Where(d => d.ItemId == itemId).ToList();
                Assert.AreEqual(4, deposits.Count(d => Distance(d) <= generator.ActionRadiusCells), $"{itemId}: one in-radius cluster x 4 deposits");
                Assert.AreEqual(4, deposits.Count(d => Distance(d) > generator.ActionRadiusCells && Distance(d) < RingsSplitCells), $"{itemId}: one first-ring cluster x 4 deposits");
                Assert.AreEqual(secondRing, deposits.Count(d => Distance(d) > RingsSplitCells), $"{itemId}: one second-ring cluster");
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
        /// A random seed changes where the ore lands, never whether the world is playable. The
        /// guarantee is the same one Generate() throws to protect, checked over many draws rather
        /// than over the one seed that happened to be stored in the asset.
        /// </summary>
        [Test]
        public void Generate_WithARandomSeed_StillPlacesEveryGuaranteedCluster()
        {
            for (int i = 0; i < 25; i++)
            {
                var generator = Generate(NewSettings(actionRadiusCells: 22, randomizeResourceSeed: true));

                Assert.AreEqual(16, generator.OreDeposits.Count(d => d.ItemId == "iron_ore"), $"draw {i}, seed {generator.ResourceSeed}");
                Assert.AreEqual(16, generator.OreDeposits.Count(d => d.ItemId == "copper_ore"), $"draw {i}, seed {generator.ResourceSeed}");
                Assert.AreEqual(12, generator.OreDeposits.Count(d => d.ItemId == "Coal_ore"), $"draw {i}, seed {generator.ResourceSeed}");
            }
        }

        /// <summary>
        /// TASK_04_PLAFOND_RAYON.md's follow-up correction: the invitation band
        /// (InvitationMinDistanceCells/MaxDistanceCells) must sit entirely beyond the starting
        /// 22-cell radius, entirely within the fog's starting reveal (22 + fogRadiusMarginCells,
        /// mirrored here from GameRuntime), and entirely within the first extension (42, the first
        /// radius research) - on every seed, not just the one that happened to pass before.
        /// A cluster drawn past the extended radius would be permanently unreachable regardless of
        /// how the player plays; a cluster under the starting radius would be constructible before
        /// the research exists to explain why it wasn't.
        /// </summary>
        /// <summary>
        /// The smallest radius any shipped research grants - the first extension a player can reach.
        /// Read from the research assets rather than restated: the invitation clusters promise to be
        /// reachable after that research, whatever figure it carries.
        /// </summary>
        static int FirstResearchedRadius()
        {
            var database = UnityEditor.AssetDatabase.LoadAssetAtPath<ResearchDatabase>("Assets/Data/Research/ResearchDatabase.asset");
            Assert.IsNotNull(database, "the shipped research database");

            int lowest = int.MaxValue;
            foreach (ResearchDefinition research in database.GetAll())
            {
                foreach (ResearchEffect effect in research.Effects)
                {
                    if (effect.Kind == ResearchEffectKind.ActionRadius) lowest = Mathf.Min(lowest, effect.Value);
                }
            }

            Assert.AreNotEqual(int.MaxValue, lowest, "no shipped research extends the radius");
            return lowest;
        }

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
        public void InvitationClusters_AreFullyUnderStartingFog_OutOfStartingRadius_AndFullyConstructibleAfterExtension(int seed)
        {
            const int startingRadius = 22;
            const int fogRadiusMarginCells = 10; // mirrors GameRuntime.fogRadiusMarginCells
            int extendedRadius = FirstResearchedRadius();

            var settings = NewSettings(startingRadius, seed);
            var grid = new GridRuntime(1f);
            var generator = new Game.Gameplay.WorldGeneration.WorldGenerator();
            generator.Generate(grid, MapSizeCells, settings, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));

            Vector2 coreCenter = new Vector2(generator.CoreOrigin.X + 2f, generator.CoreOrigin.Y + 2f);
            float Distance(DepositRuntime d)
            {
                float dx = d.Origin.X - coreCenter.x;
                float dy = d.Origin.Y - coreCenter.y;
                return Mathf.Sqrt(dx * dx + dy * dy);
            }

            foreach (string itemId in new[] { "iron_ore", "copper_ore", "Coal_ore" })
            {
                var invitationDeposits = generator.OreDeposits.Where(d => d.ItemId == itemId && Distance(d) > startingRadius && Distance(d) < RingsSplitCells).ToList();
                Assert.AreEqual(4, invitationDeposits.Count, $"seed {seed}, {itemId}: expected exactly one first-ring invitation cluster (4 deposits) beyond the starting radius.");

                foreach (DepositRuntime deposit in invitationDeposits)
                {
                    float distance = Distance(deposit);
                    Assert.Greater(distance, startingRadius, $"seed {seed}, {itemId}: an invitation deposit must not be constructible before the first radius research.");
                    Assert.LessOrEqual(distance, startingRadius + fogRadiusMarginCells, $"seed {seed}, {itemId}: an invitation deposit must be visible under the starting fog.");
                    Assert.LessOrEqual(distance, extendedRadius, $"seed {seed}, {itemId}: an invitation deposit must be constructible after the first radius research.");
                }
            }
        }

        /// <summary>
        /// The second ring, on every seed: one cluster per resource - 8 iron, 8 copper, 4 coal - whose
        /// centre lies 40 to 60 cells from the Core. Only the centre is held to the band; the
        /// deposits may overhang it by half the cluster's diagonal.
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
        public void SecondRing_HoldsEightIronEightCopperFourCoal_CentredBetween40And60(int seed)
        {
            Game.Gameplay.WorldGeneration.WorldGenerator generator = Generate(NewSettings(22, seed));
            Vector2 coreCenter = new Vector2(generator.CoreOrigin.X + 2f, generator.CoreOrigin.Y + 2f);

            foreach (var (itemId, expected) in new[] { ("iron_ore", 8), ("copper_ore", 8), ("Coal_ore", 4) })
            {
                var ring = generator.OreDeposits
                    .Where(d => d.ItemId == itemId && Vector2.Distance(new Vector2(d.Origin.X, d.Origin.Y), coreCenter) > RingsSplitCells)
                    .ToList();
                Assert.AreEqual(expected, ring.Count, $"seed {seed}, {itemId}: deposits in the second ring");

                // The cluster's centre is the middle of its deposits' own centres (2x2 each here).
                var centre = new Vector2(ring.Average(d => d.Origin.X + 1f), ring.Average(d => d.Origin.Y + 1f));
                float distance = Vector2.Distance(centre, coreCenter);
                Assert.GreaterOrEqual(distance, 40f - 1f, $"seed {seed}, {itemId}: centre {distance:F1} cells out");
                Assert.LessOrEqual(distance, 60f + 1f, $"seed {seed}, {itemId}: centre {distance:F1} cells out");
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
                generator.Generate(grid, MapSizeCells, settings, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem())));
        }
    }
}
