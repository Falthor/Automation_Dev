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
    public class FactoryRuntimeTests
    {
        ItemDefinition _ironIngot;
        ItemDefinition _ironPlate;
        ItemDatabase _itemDatabase;
        RecipeDefinition _ironPlateRecipe;
        RecipeDefinition _memoryRecipe;
        RecipeDatabase _recipeDatabase;
        ResearchDefinition _memoireResearch;
        ComputeSystem _compute;
        PowerSystem _power;
        ResearchSystem _research;

        [SetUp]
        public void SetUp()
        {
            _ironIngot = TestDataFactory.NewItem("Iron_Ingot", ItemType.Ingot);
            _ironPlate = TestDataFactory.NewItem("Iron_Plate", ItemType.Component);
            _itemDatabase = TestDataFactory.NewItemDatabase(_ironIngot, _ironPlate);

            _memoireResearch = TestDataFactory.NewResearch("memory_gate", 100f);
            _ironPlateRecipe = TestDataFactory.NewRecipe("Iron_Plate", 3f, 300f, 2, (_ironIngot, 2));
            _memoryRecipe = TestDataFactory.NewRecipe("Memory_MK1", 3f, 1500f, 1, (_ironPlate, 3));
            TestDataFactory.WithEffects(_memoireResearch, ResearchEffect.UnlockRecipe(_memoryRecipe));
            _recipeDatabase = TestDataFactory.NewRecipeDatabase(_ironPlateRecipe, _memoryRecipe);

            _compute = new ComputeSystem();
            _power = new PowerSystem();
            _research = new ResearchSystem(_compute, new ResearchCatalog(new[] { _memoireResearch }));
        }

        FactoryRuntime NewFactory()
        {
            FactoryDefinition definition = TestDataFactory.NewFactory(
                3f,
                new[] { "Iron_Plate", "Memory_MK1" },
                new[] { "Iron_Ingot", "Iron_Plate" });
            return new FactoryRuntime(definition, new GridCoord(0, 0), Direction.North, _recipeDatabase, _compute, _power, _research);
        }

        [Test]
        public void GetRecipeIds_ExcludesResearchGatedRecipe_UntilUnlocked()
        {
            FactoryRuntime factory = NewFactory();

            CollectionAssert.Contains(factory.GetRecipeIds(), "Iron_Plate");
            CollectionAssert.DoesNotContain(factory.GetRecipeIds(), "Memory_MK1");
        }

        [Test]
        public void GetRecipeIds_IncludesGatedRecipe_OnceUnlocked()
        {
            FactoryRuntime factory = NewFactory();
            _research.Enqueue(_memoireResearch);
            _research.Tick(60f);
            Assert.IsTrue(_research.IsUnlocked(_memoireResearch.Id));

            CollectionAssert.Contains(factory.GetRecipeIds(), "Memory_MK1");
        }

        [Test]
        public void SetSelectedRecipe_RejectsGatedRecipe_BeforeUnlock()
        {
            FactoryRuntime factory = NewFactory();

            factory.SetSelectedRecipe("Memory_MK1");

            Assert.AreEqual(string.Empty, factory.GetSelectedRecipe());
        }

        [Test]
        public void CanAcceptInput_RejectsItemNotInAcceptedList()
        {
            FactoryRuntime factory = NewFactory();
            factory.SetSelectedRecipe("Iron_Plate");

            // Iron_Plate is a valid ingredient for a different Factory recipe, but not one this
            // building's accepted list would ever reject on its own - use an item outside both
            // the accepted list and the current recipe to prove the accepted-list filter fires.
            Assert.IsFalse(factory.CanAcceptInput("Memory_MK1", 1, Direction.South));
        }

        /// <summary>
        /// Two recipes consuming different amounts give the same building different ceilings: 2 iron
        /// per plate buffers 6, 3 plates per memory module buffers 9. A single number could not have
        /// said both, which is why the cap is asked for per item rather than held as one.
        /// </summary>
        [Test]
        public void InputCapacity_DiffersPerIngredientAndPerRecipe()
        {
            FactoryRuntime factory = NewFactory();

            factory.SetSelectedRecipe("Iron_Plate");
            Assert.IsTrue(factory.CanAcceptInput("Iron_Ingot", 6, Direction.South), "2 per craft x 3 crafts.");
            Assert.IsFalse(factory.CanAcceptInput("Iron_Ingot", 7, Direction.South));

            _research.Enqueue(_memoireResearch);
            _research.Tick(60f);
            factory.SetSelectedRecipe("Memory_MK1");

            Assert.IsTrue(factory.CanAcceptInput("Iron_Plate", 9, Direction.South), "3 per craft x 3 crafts.");
            Assert.IsFalse(factory.CanAcceptInput("Iron_Plate", 10, Direction.South));
            Assert.IsFalse(factory.CanAcceptInput("Iron_Ingot", 1, Direction.South),
                "The previous recipe's ingredient is not consumed any more, so none of it is held.");
        }

        /// <summary>The output buffer is flat: ten of whatever is produced, whatever the recipe or the building.</summary>
        [Test]
        public void OutputCapacity_IsTenRegardlessOfTheRecipe()
        {
            FactoryRuntime factory = NewFactory();
            factory.SetSelectedRecipe("Iron_Plate");

            Assert.AreEqual(10, ProductionBuildingRuntime.OutputStackCapacity);

            factory.AddInput("Iron_Ingot", 6, Direction.South);
            factory.AddOutput("Iron_Plate", ProductionBuildingRuntime.OutputStackCapacity);
            factory.Tick(0.1f);

            Assert.AreEqual(ProductionState.OutputBlocked, factory.GetState());
            Assert.AreEqual(6, factory.GetInputAmount("Iron_Ingot"), "A blocked output consumes nothing.");
        }
    }
}
