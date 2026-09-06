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
    /// <summary>
    /// Switching a factory off from its panel: it stops producing and, above all, stops drawing -
    /// the point of the button is to take a machine off a strained network, so "consumes nothing"
    /// has to be literally true rather than "consumes nothing while it happens to be idle".
    /// </summary>
    public class ProductionPauseTests
    {
        const string WireId = "copper_wire";
        const string IngotId = "copper_Ingot";
        const float PowerDemandKw = 3f;

        static FactoryRuntime NewRunningFactory(out PowerSystem power)
        {
            var compute = new ComputeSystem();
            power = new PowerSystem();

            RecipeDatabase recipes = TestDataFactory.NewRecipeDatabase(
                TestDataFactory.NewRecipe(WireId, 3f, 0f, 2, (TestDataFactory.NewItem(IngotId), 1)));

            var factory = new FactoryRuntime(
                TestDataFactory.NewFactory(PowerDemandKw, new[] { WireId }, new[] { IngotId }),
                new GridCoord(0, 0), Direction.East, recipes, compute, power, new ResearchSystem(compute));

            factory.SetSelectedRecipe(WireId);
            factory.AddInput(IngotId, 3, Direction.North);
            return factory;
        }

        /// <summary>One tick of the pair, with enough supply that nothing is throttled - the demand reported is then purely the building's own.</summary>
        static void Step(FactoryRuntime factory, PowerSystem power)
        {
            power.ReportSupply(1000f);
            factory.Tick(0.5f);
            power.Settle();
        }

        [Test]
        public void APausedBuilding_DrawsNoPowerAtAll()
        {
            FactoryRuntime factory = NewRunningFactory(out PowerSystem power);
            Step(factory, power);
            Step(factory, power);
            Assert.AreEqual(PowerDemandKw, power.SettledDemand, 0.001f, "Precondition: it was drawing.");

            factory.SetPaused(true);
            Step(factory, power);
            Step(factory, power);

            Assert.AreEqual(0f, power.SettledDemand, 0.001f);
            Assert.AreEqual(ProductionState.Paused, factory.GetState());
            Assert.AreEqual("EN PAUSE", factory.GetStateLabel());
        }

        [Test]
        public void Unpausing_PutsItBackToWork()
        {
            FactoryRuntime factory = NewRunningFactory(out PowerSystem power);
            factory.SetPaused(true);
            Step(factory, power);

            factory.SetPaused(false);
            Step(factory, power);
            Step(factory, power);

            Assert.AreEqual(ProductionState.Producing, factory.GetState());
            Assert.AreEqual(PowerDemandKw, power.SettledDemand, 0.001f);
        }

        /// <summary>
        /// A cycle in progress freezes; it is not abandoned. Its ingredients and its CU are already
        /// spent and are not handed back - the same bargain an unpowered building already makes.
        /// Pausing is a way to stop drawing, not a way to take a cycle back.
        /// </summary>
        [Test]
        public void PausingMidCycle_FreezesItRatherThanLosingIt()
        {
            FactoryRuntime factory = NewRunningFactory(out PowerSystem power);
            Step(factory, power);
            Step(factory, power);
            float progress = factory.GetProductionProgress();
            Assert.Greater(progress, 0f, "Precondition: a cycle is under way.");

            factory.SetPaused(true);
            for (int i = 0; i < 20; i++) Step(factory, power);

            Assert.AreEqual(progress, factory.GetProductionProgress(), 0.001f, "The timer stood still.");
        }

        /// <summary>A switched-off building comes back switched off: reloading is not a reason to put it back on a network the player took it off.</summary>
        [Test]
        public void PausedSurvivesASaveRoundTrip()
        {
            FactoryRuntime factory = NewRunningFactory(out PowerSystem power);
            factory.SetPaused(true);

            Newtonsoft.Json.Linq.JObject captured = factory.CaptureState();

            FactoryRuntime reloaded = NewRunningFactory(out _);
            Assert.IsFalse(reloaded.IsPaused, "Precondition: a fresh one runs.");

            reloaded.RestoreState(captured);

            Assert.IsTrue(reloaded.IsPaused);
        }

        [Test]
        public void Restore_OnASaveWithoutTheKey_ComesBackRunning()
        {
            FactoryRuntime factory = NewRunningFactory(out _);

            Assert.DoesNotThrow(() => factory.RestoreState(new Newtonsoft.Json.Linq.JObject()));
            Assert.IsFalse(factory.IsPaused);
        }
    }
}
