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
    /// The simulation does not depend on where the camera is looking, on what has been discovered, or
    /// on how far from the origin a building stands.
    ///
    /// The large-map directive §4.5 asks for this to be <b>verified rather than built</b>: it is
    /// almost certainly already true, since nothing in the simulation consults the camera or any
    /// view. A test is what keeps it true - the day someone reaches for a renderer or a discovery
    /// state from inside a Tick, a base at the far end of a 10 000-cell map would quietly stop
    /// producing while the one on screen kept working, which is close to undebuggable from a report.
    /// </summary>
    public class DistantBuildingProductionTests
    {
        /// <summary>Far corner of the map the project is heading towards, not of the current one.</summary>
        static readonly GridCoord FarCorner = new GridCoord(9998, 9998);

        ItemDefinition _iron;
        ItemDefinition _ironIngot;
        ItemDatabase _itemDatabase;
        RecipeDatabase _recipeDatabase;
        ComputeSystem _compute;
        PowerSystem _power;
        ResearchSystem _research;

        [SetUp]
        public void SetUp()
        {
            _iron = TestDataFactory.NewItem("minerai_fer", ItemType.Ore);
            _ironIngot = TestDataFactory.NewItem("Iron_Ingot", ItemType.Ingot);
            _itemDatabase = TestDataFactory.NewItemDatabase(_iron, _ironIngot);

            RecipeDefinition recipe = TestDataFactory.NewRecipe("Iron_Ingot", timeSeconds: 1f, computeCost: 0f, outputAmount: 1, (_iron, 1));
            _recipeDatabase = TestDataFactory.NewRecipeDatabase(recipe);

            _compute = new ComputeSystem();
            _power = new PowerSystem();
            _research = new ResearchSystem(_compute);
        }

        FoundryRuntime NewFoundryAt(GridCoord cell)
        {
            FoundryDefinition definition = TestDataFactory.NewFoundry(powerDemandKw: 0f, intakeIntervalSeconds: 0f, "Iron_Ingot");
            var foundry = new FoundryRuntime(definition, cell, Direction.North, _recipeDatabase, _itemDatabase, _compute, _power, _research);
            foundry.SetSelectedRecipe("Iron_Ingot");
            return foundry;
        }

        static void Feed(FoundryRuntime foundry, int amount)
        {
            for (int i = 0; i < amount; i++) foundry.AddInput("minerai_fer", 1, Direction.South);
        }

        /// <summary>Finished ingots waiting to be pushed out. Zero while none has been produced - the output dictionary simply has no entry then.</summary>
        static int Produced(FoundryRuntime foundry)
            => foundry.GetOutputContents().TryGetValue("Iron_Ingot", out int amount) ? amount : 0;

        static void Run(FoundryRuntime foundry, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.1f) foundry.Tick(0.1f);
        }

        [Test]
        public void ABuildingAtTheFarCornerOfTheMap_ProducesLikeOneAtTheOrigin()
        {
            FoundryRuntime nearby = NewFoundryAt(new GridCoord(0, 0));
            FoundryRuntime distant = NewFoundryAt(FarCorner);

            Feed(nearby, 5);
            Feed(distant, 5);

            Run(nearby, 5f);
            Run(distant, 5f);

            Assert.AreEqual(Produced(nearby), Produced(distant),
                "A building's output must not depend on how far from the origin it stands.");
            Assert.Greater(Produced(distant), 0, "and it has to actually have produced something");
        }

        /// <summary>
        /// Nothing about the simulation reads discovery, so a building on ground the player has never
        /// revealed keeps working. That is deliberate: a secondary base at the other end of the map
        /// runs whether or not anyone is looking at it.
        /// </summary>
        [Test]
        public void ABuildingOnUndiscoveredGround_KeepsProducing()
        {
            FoundryRuntime distant = NewFoundryAt(FarCorner);

            // No DiscoveryRuntime is involved at all - which is the point. Nothing here has one to
            // consult, so a Tick cannot come to depend on it by accident.
            Feed(distant, 3);
            Run(distant, 4f);

            Assert.Greater(Produced(distant), 0);
        }

        [Test]
        public void ProductionCostsTheSameWhereverItHappens()
        {
            FoundryRuntime nearby = NewFoundryAt(new GridCoord(0, 0));
            FoundryRuntime distant = NewFoundryAt(FarCorner);

            Feed(nearby, 1);
            Feed(distant, 1);

            Run(nearby, 2f);
            Run(distant, 2f);

            Assert.AreEqual(nearby.GetState(), distant.GetState(),
                "Two identical buildings fed identically must reach the same state, wherever they are.");
        }
    }
}
