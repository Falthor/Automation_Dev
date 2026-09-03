using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class FoundryRuntimeTests
    {
        ItemDefinition _iron;
        ItemDefinition _ironIngot;
        ItemDefinition _charbon;
        ItemDatabase _itemDatabase;
        RecipeDefinition _ironIngotRecipe;
        RecipeDatabase _recipeDatabase;
        ComputeSystem _compute;
        PowerSystem _power;
        ResearchSystem _research;

        [SetUp]
        public void SetUp()
        {
            _iron = TestDataFactory.NewItem("minerai_fer", ItemType.Ore);
            _ironIngot = TestDataFactory.NewItem("Iron_Ingot", ItemType.Ingot);
            _charbon = TestDataFactory.NewItem("minerai_charbon", ItemType.Component);
            _itemDatabase = TestDataFactory.NewItemDatabase(_iron, _ironIngot, _charbon);

            _ironIngotRecipe = TestDataFactory.NewRecipe("Iron_Ingot", timeSeconds: 1f, computeCost: 100f, outputAmount: 1, (_iron, 1));
            _recipeDatabase = TestDataFactory.NewRecipeDatabase(_ironIngotRecipe);

            _compute = new ComputeSystem();
            _power = new PowerSystem();
            _research = new ResearchSystem();
        }

        FoundryRuntime NewFoundry(int maxStackPerItem = 20, float powerDemandKw = 2f, float intakeIntervalSeconds = 2f)
        {
            FoundryDefinition definition = TestDataFactory.NewFoundry(maxStackPerItem, powerDemandKw, intakeIntervalSeconds, "Iron_Ingot", "lingot_cuivre");
            return new FoundryRuntime(definition, new GridCoord(0, 0), Direction.North, _recipeDatabase, _itemDatabase, _compute, _power, _research);
        }

        [Test]
        public void NoRecipeSelected_StateIsIdle()
        {
            FoundryRuntime foundry = NewFoundry();

            foundry.Tick(1f);

            Assert.AreEqual(ProductionState.Idle, foundry.GetState());
        }

        [Test]
        public void RecipeSelected_MissingIngredients_StateIsWaitingResources()
        {
            FoundryRuntime foundry = NewFoundry();
            foundry.SetSelectedRecipe("Iron_Ingot");

            foundry.Tick(1f);

            Assert.AreEqual(ProductionState.WaitingResources, foundry.GetState());
        }

        [Test]
        public void CanAcceptInput_AcceptsRecipeIngredient_FromNonOutputSide()
        {
            FoundryRuntime foundry = NewFoundry();
            foundry.SetSelectedRecipe("Iron_Ingot");

            Assert.IsTrue(foundry.CanAcceptInput("minerai_fer", 1, Direction.South));
        }

        [Test]
        public void CanAcceptInput_RejectsFromOutputSide()
        {
            FoundryRuntime foundry = NewFoundry();
            foundry.SetSelectedRecipe("Iron_Ingot");

            // Facing North (default rotation) -> output side is North.
            Assert.IsFalse(foundry.CanAcceptInput("minerai_fer", 1, Direction.North));
        }

        [Test]
        public void CanAcceptInput_RejectsNonOreItem()
        {
            FoundryRuntime foundry = NewFoundry();
            foundry.SetSelectedRecipe("Iron_Ingot");

            Assert.IsFalse(foundry.CanAcceptInput("minerai_charbon", 1, Direction.South));
        }

        [Test]
        public void CanAcceptInput_RejectsItemNotInSelectedRecipe()
        {
            FoundryRuntime foundry = NewFoundry();
            foundry.SetSelectedRecipe("Iron_Ingot");

            Assert.IsFalse(foundry.CanAcceptInput("Iron_Ingot", 1, Direction.South));
        }

        [Test]
        public void AddInput_StartsIntakeCooldown_BlockingFurtherDeliveries()
        {
            FoundryRuntime foundry = NewFoundry(intakeIntervalSeconds: 2f);
            foundry.SetSelectedRecipe("Iron_Ingot");

            foundry.AddInput("minerai_fer", 1, Direction.South);

            Assert.IsFalse(foundry.CanAcceptInput("minerai_fer", 1, Direction.South));

            foundry.Tick(2.1f);

            Assert.IsTrue(foundry.CanAcceptInput("minerai_fer", 1, Direction.South));
        }

        [Test]
        public void FullCycle_ProducesOutput_AfterProductionTimeElapses()
        {
            FoundryRuntime foundry = NewFoundry();
            foundry.SetSelectedRecipe("Iron_Ingot");
            foundry.AddInput("minerai_fer", 1, Direction.South);

            foundry.Tick(0.5f);
            Assert.AreEqual(ProductionState.Producing, foundry.GetState());
            Assert.AreEqual(0, foundry.GetOutputContents().Count);

            foundry.Tick(0.6f); // crosses the 1s recipe time

            Assert.AreEqual(1, foundry.GetOutputContents()["Iron_Ingot"]);
        }

        [Test]
        public void CycleStart_DeductsComputeCostOnce_NotPerTick()
        {
            FoundryRuntime foundry = NewFoundry();
            foundry.SetSelectedRecipe("Iron_Ingot");
            foundry.AddInput("minerai_fer", 1, Direction.South);

            foundry.Tick(0.3f);
            float afterFirstTick = _compute.Reserve;
            foundry.Tick(0.3f);

            Assert.AreEqual(ComputeSystem.ReserveCap - 100f, afterFirstTick);
            Assert.AreEqual(afterFirstTick, _compute.Reserve);
        }

        [Test]
        public void InsufficientCompute_StateIsWaitingCompute_NothingConsumed()
        {
            _compute.Spend(_compute.Reserve); // drain to 0
            FoundryRuntime foundry = NewFoundry();
            foundry.SetSelectedRecipe("Iron_Ingot");
            foundry.AddInput("minerai_fer", 1, Direction.South);

            foundry.Tick(0.1f);

            Assert.AreEqual(ProductionState.WaitingCompute, foundry.GetState());
            Assert.AreEqual(1, foundry.GetInputAmount("minerai_fer")); // not consumed
        }

        [Test]
        public void OutputFull_StateIsOutputBlocked_NothingConsumed()
        {
            FoundryRuntime foundry = NewFoundry(maxStackPerItem: 1);
            foundry.SetSelectedRecipe("Iron_Ingot");
            foundry.AddOutput("Iron_Ingot", 1); // fill output to its cap
            foundry.AddInput("minerai_fer", 1, Direction.South);

            foundry.Tick(0.1f);

            Assert.AreEqual(ProductionState.OutputBlocked, foundry.GetState());
            Assert.AreEqual(1, foundry.GetInputAmount("minerai_fer")); // not consumed
        }

        [Test]
        public void SwitchingRecipeMidCycle_AbandonsCycle_DoesNotRefundConsumedIngredients()
        {
            FoundryRuntime foundry = NewFoundry();
            foundry.SetSelectedRecipe("Iron_Ingot");
            foundry.AddInput("minerai_fer", 1, Direction.South);
            foundry.Tick(0.3f); // starts the cycle: ingredient + compute already taken

            Assert.AreEqual(0, foundry.GetInputAmount("minerai_fer"));

            foundry.SetSelectedRecipe(""); // deselect - always allowed regardless of whitelist

            Assert.AreEqual(0f, foundry.GetProgress());
            Assert.AreEqual(0, foundry.GetInputAmount("minerai_fer")); // still not refunded
        }

        [Test]
        public void PowerDemand_OnlyReported_WhileProducing()
        {
            FoundryRuntime foundry = NewFoundry(powerDemandKw: 2f);
            foundry.SetSelectedRecipe("Iron_Ingot");

            foundry.Tick(0.1f); // WaitingResources - not producing yet, reports nothing
            _power.Settle();
            Assert.AreEqual(0f, _power.SettledDemand);

            foundry.AddInput("minerai_fer", 1, Direction.South);
            foundry.Tick(0.1f); // state becomes Producing at the end of this tick
            _power.Settle();
            Assert.AreEqual(0f, _power.SettledDemand); // one-frame lag: demand for this tick was reported based on the previous (non-Producing) state

            foundry.Tick(0.1f); // now demand is reported, since the PREVIOUS tick ended Producing
            _power.Settle();

            Assert.AreEqual(2f, _power.SettledDemand);
        }

        [Test]
        public void Unpowered_FreezesProductionProgress_WithoutLosingConsumedIngredients()
        {
            FoundryRuntime foundry = NewFoundry(powerDemandKw: 2f);
            foundry.SetSelectedRecipe("Iron_Ingot");
            foundry.AddInput("minerai_fer", 1, Direction.South);

            foundry.Tick(0.5f); // cycle starts, ingredient+compute taken; state becomes Producing at the end of this tick
            Assert.AreEqual(0, foundry.GetInputAmount("minerai_fer"));

            // One-frame lag: the tick above reported no demand yet (it checks the PREVIOUS
            // state, which was Idle). One more tick (0 delta, so it doesn't itself advance
            // progress) is needed before Settle() reflects this building's real demand.
            foundry.Tick(0f);
            _power.Settle();

            // Nothing in the world reports Power supply (no Core/Powerplant this phase) - demand
            // now exceeds settled supply (0), so the next tick must be unpowered and NOT advance.
            Assert.IsFalse(_power.IsPowered());

            float progressBeforeUnpoweredTick = foundry.GetProgress();
            foundry.Tick(1f); // would normally finish a 1s recipe - must be frozen instead
            _power.Settle();

            Assert.AreEqual(progressBeforeUnpoweredTick, foundry.GetProgress());
            Assert.AreEqual(ProductionState.Producing, foundry.GetState());
            Assert.AreEqual(0, foundry.GetOutputContents().Count); // cycle never completed
        }
    }
}
