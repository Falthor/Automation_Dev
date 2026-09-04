using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Transport;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Transport
{
    public class TransportSystemTests
    {
        [Test]
        public void Tick_TwoConsumersSharingOneInputCell_AlternateWhichOneIsFed()
        {
            // Reproduces the reported bug: two Factories both facing the same conveyor cell -
            // one to its north (entry south), one to its west (entry east) - used to always feed
            // the same, earlier-registered Factory every tick. The recipe's ingredient amount is
            // set absurdly high so neither Factory ever actually starts crafting (which would
            // consume the delivered item and confuse the win-count below) - this test only cares
            // about which Factory's input pool grows on a given tick, not production itself.
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            var ironOre = TestDataFactory.NewItem("iron_ore", ItemType.Ore);
            var recipe = TestDataFactory.NewRecipe("iron_ore_sink", 100f, 0f, 1, (ironOre, 1000));
            var recipeDatabase = TestDataFactory.NewRecipeDatabase(recipe);

            var definitionA = TestDataFactory.NewFactory(50, 0f, new[] { "iron_ore_sink" }, System.Array.Empty<string>());
            var definitionB = TestDataFactory.NewFactory(50, 0f, new[] { "iron_ore_sink" }, System.Array.Empty<string>());

            var conveyorCell = new GridCoord(5, 5);
            // North of the conveyor, entry pointing South at it.
            var factoryA = new FactoryRuntime(definitionA, new GridCoord(5, 6), Direction.North, recipeDatabase, new ComputeSystem(), new PowerSystem(), new ResearchSystem());
            // West of the conveyor, entry pointing East at it.
            var factoryB = new FactoryRuntime(definitionB, new GridCoord(4, 5), Direction.West, recipeDatabase, new ComputeSystem(), new PowerSystem(), new ResearchSystem());
            factoryA.SetSelectedRecipe("iron_ore_sink");
            factoryB.SetSelectedRecipe("iron_ore_sink");
            transport.Register(factoryA);
            transport.Register(factoryB);

            var conveyorDefinition = ScriptableObject.CreateInstance<ConveyorDefinition>();
            var conveyor = new ConveyorRuntime(conveyorDefinition, conveyorCell, Direction.North);
            grid.SetOccupant(conveyorCell, conveyor);

            int aWins = 0;
            int bWins = 0;
            for (int i = 0; i < 6; i++)
            {
                conveyor.ReceiveItem("iron_ore");
                conveyor.AdvanceItem(10f, 100f); // force the item's progress to 1 (pullable) this tick

                int beforeA = factoryA.GetInputAmount("iron_ore");
                int beforeB = factoryB.GetInputAmount("iron_ore");

                transport.Tick(0.016f);

                if (factoryA.GetInputAmount("iron_ore") > beforeA) aWins++;
                else if (factoryB.GetInputAmount("iron_ore") > beforeB) bWins++;
            }

            Assert.AreEqual(3, aWins, "Factory A should win exactly half of the contested pulls.");
            Assert.AreEqual(3, bWins, "Factory B should win exactly half of the contested pulls.");
        }
    }
}
