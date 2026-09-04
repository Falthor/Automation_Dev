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

            _memoireResearch = TestDataFactory.NewResearch("memoire", 100f);
            _ironPlateRecipe = TestDataFactory.NewRecipe("Iron_Plate", 3f, 300f, 2, (_ironIngot, 2));
            _memoryRecipe = TestDataFactory.NewRecipe("Memory_MK1", 3f, 1500f, 1, _memoireResearch, (_ironPlate, 3));
            _recipeDatabase = TestDataFactory.NewRecipeDatabase(_ironPlateRecipe, _memoryRecipe);

            _compute = new ComputeSystem();
            _power = new PowerSystem();
            _research = new ResearchSystem(_compute);
        }

        FactoryRuntime NewFactory()
        {
            FactoryDefinition definition = TestDataFactory.NewFactory(
                100, 3f,
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
            Assert.IsTrue(_research.IsUnlocked("memoire"));

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
    }
}
