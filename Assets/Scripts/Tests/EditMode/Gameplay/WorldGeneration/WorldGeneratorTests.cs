using System.Linq;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
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

        static WorldGenerationSettings NewSettings(int actionRadiusCells)
        {
            var ironItem = TestDataFactory.NewItem("iron_ore", ItemType.Ore);
            var copperItem = TestDataFactory.NewItem("copper_ore", ItemType.Ore);
            var coalItem = TestDataFactory.NewItem("Coal_ore", ItemType.Component);

            var core = TestDataFactory.NewCore(actionRadiusCells, new Vector2Int(4, 4));
            var iron = TestDataFactory.NewOreDeposit(ironItem, new Vector2Int(2, 2));
            var copper = TestDataFactory.NewOreDeposit(copperItem, new Vector2Int(2, 2));
            var coal = TestDataFactory.NewOreDeposit(coalItem, new Vector2Int(2, 2));

            return TestDataFactory.NewWorldGenerationSettings(core, iron, copper, coal, ResourceSeed);
        }

        [Test]
        public void Generate_WithRoomyRadius_PlacesOneGuaranteedClusterAndOneInvitationClusterPerResource()
        {
            var settings = NewSettings(actionRadiusCells: 22);
            var grid = new GridRuntime(1f);
            var generator = new Game.Gameplay.WorldGeneration.WorldGenerator();

            generator.Generate(grid, MapSizeCells, settings, new ComputeSystem(), new PowerSystem());

            // One in-radius cluster + one invitation cluster per resource - 4 deposits each.
            Assert.AreEqual(8, generator.OreDeposits.Count(d => d.ItemId == "iron_ore"));
            Assert.AreEqual(8, generator.OreDeposits.Count(d => d.ItemId == "copper_ore"));
            Assert.AreEqual(8, generator.OreDeposits.Count(d => d.ItemId == "Coal_ore"));

            Vector2 coreCenter = new Vector2(generator.CoreOrigin.X + 2f, generator.CoreOrigin.Y + 2f);
            bool InRadius(DepositRuntime deposit)
            {
                float dx = deposit.Origin.X - coreCenter.x;
                float dy = deposit.Origin.Y - coreCenter.y;
                return Mathf.Sqrt(dx * dx + dy * dy) <= generator.ActionRadiusCells;
            }

            foreach (string itemId in new[] { "iron_ore", "copper_ore", "Coal_ore" })
            {
                var deposits = generator.OreDeposits.Where(d => d.ItemId == itemId).ToList();
                Assert.AreEqual(4, deposits.Count(InRadius), $"{itemId}: one in-radius cluster x 4 deposits");
                Assert.AreEqual(4, deposits.Count(d => !InRadius(d)), $"{itemId}: one invitation cluster x 4 deposits, outside the radius");
            }
        }

        [Test]
        public void Generate_SameSeed_ProducesIdenticalDepositOrigins()
        {
            var settingsA = NewSettings(actionRadiusCells: 22);
            var generatorA = new Game.Gameplay.WorldGeneration.WorldGenerator();
            generatorA.Generate(new GridRuntime(1f), MapSizeCells, settingsA, new ComputeSystem(), new PowerSystem());

            var settingsB = NewSettings(actionRadiusCells: 22);
            var generatorB = new Game.Gameplay.WorldGeneration.WorldGenerator();
            generatorB.Generate(new GridRuntime(1f), MapSizeCells, settingsB, new ComputeSystem(), new PowerSystem());

            CollectionAssert.AreEqual(
                generatorA.OreDeposits.Select(d => d.Origin).ToList(),
                generatorB.OreDeposits.Select(d => d.Origin).ToList());
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
                generator.Generate(grid, MapSizeCells, settings, new ComputeSystem(), new PowerSystem()));
        }
    }
}
