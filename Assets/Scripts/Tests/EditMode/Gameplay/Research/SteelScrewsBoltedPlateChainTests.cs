using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Research
{
    /// <summary>
    /// The mechanical chain gating Memory Mk.I: Metallurgie avancee (Fonderie avancee, Plaque
    /// d'acier) now leads to Vis (recette Vis, recette Plaque d'acier boulonnee) before Architecture
    /// memoire is reachable. Reads the shipped assets by path - a fixture copy would stop tracking
    /// the real asset the moment it changed.
    /// </summary>
    public class SteelScrewsBoltedPlateChainTests
    {
        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"{path} is missing.");
            return asset;
        }

        static bool HasEffect(ResearchDefinition research, ResearchEffectKind kind, RecipeDefinition recipe)
        {
            foreach (ResearchEffect effect in research.Effects)
                if (effect.Kind == kind && effect.Recipe == recipe) return true;
            return false;
        }

        static bool HasPrerequisite(ResearchDefinition research, ResearchDefinition candidate)
        {
            foreach (ResearchDefinition prerequisite in research.Prerequisites) if (prerequisite == candidate) return true;
            return false;
        }

        // --- Vis ---

        [Test]
        public void Screws_CostsFourThousandFiveHundred_AndRequiresAdvancedFoundry()
        {
            var screws = Load<ResearchDefinition>("Assets/Data/Research/screws.asset");
            var advancedFoundry = Load<ResearchDefinition>("Assets/Data/Research/advanced_foundry.asset");

            Assert.AreEqual(4500, screws.CuCost);
            Assert.AreEqual(1, screws.Prerequisites.Count);
            Assert.IsTrue(HasPrerequisite(screws, advancedFoundry));
        }

        [Test]
        public void Screws_UnlocksScrewsRecipeAndBoltedSteelPlateRecipe()
        {
            var screws = Load<ResearchDefinition>("Assets/Data/Research/screws.asset");
            var screwsRecipe = Load<RecipeDefinition>("Assets/Data/Recipes/Screws_Recipe.asset");
            var boltedPlateRecipe = Load<RecipeDefinition>("Assets/Data/Recipes/Bolted_Steel_Plate_Recipe.asset");

            Assert.IsTrue(HasEffect(screws, ResearchEffectKind.UnlockRecipe, screwsRecipe));
            Assert.IsTrue(HasEffect(screws, ResearchEffectKind.UnlockRecipe, boltedPlateRecipe));
        }

        [Test]
        public void ScrewsRecipe_IsOneIronPlateToFourScrews_EightSeconds_TenCu()
        {
            var recipe = Load<RecipeDefinition>("Assets/Data/Recipes/Screws_Recipe.asset");
            var ironPlate = Load<ItemDefinition>("Assets/Data/Items/Iron_Plate.asset");

            Assert.AreEqual(1, recipe.Ingredients.Length);
            Assert.AreEqual(ironPlate, recipe.Ingredients[0].Item);
            Assert.AreEqual(1, recipe.Ingredients[0].Amount);
            Assert.AreEqual(4, recipe.OutputAmount);
            Assert.AreEqual(8f, recipe.TimeSeconds);
            Assert.AreEqual(10f, recipe.ComputeCost);
        }

        [Test]
        public void BoltedSteelPlateRecipe_IsThreeSteelTwelveScrewsToOnePlate_TwelveSeconds_ThirtyFiveCu()
        {
            var recipe = Load<RecipeDefinition>("Assets/Data/Recipes/Bolted_Steel_Plate_Recipe.asset");
            var steel = Load<ItemDefinition>("Assets/Data/Items/Steel.asset");
            var screwsItem = Load<ItemDefinition>("Assets/Data/Items/Screws.asset");

            Assert.AreEqual(2, recipe.Ingredients.Length);
            Assert.AreEqual(steel, recipe.Ingredients[0].Item);
            Assert.AreEqual(3, recipe.Ingredients[0].Amount);
            Assert.AreEqual(screwsItem, recipe.Ingredients[1].Item);
            Assert.AreEqual(12, recipe.Ingredients[1].Amount);
            Assert.AreEqual(1, recipe.OutputAmount);
            Assert.AreEqual(12f, recipe.TimeSeconds);
            Assert.AreEqual(35f, recipe.ComputeCost);
        }

        [Test]
        public void Constructor_OffersScrewsAndBoltedSteelPlate_AndAcceptsTheirIngredients()
        {
            var constructor = Load<ConstructorDefinition>("Assets/Data/Buildings/ConstructorDefinition.asset");

            CollectionAssert.Contains(constructor.RecipeIds, "Screws");
            CollectionAssert.Contains(constructor.RecipeIds, "Bolted_Steel_Plate");
            CollectionAssert.Contains(constructor.AcceptedItemIds, "Iron_Plate");
            CollectionAssert.Contains(constructor.AcceptedItemIds, "Steel");
            CollectionAssert.Contains(constructor.AcceptedItemIds, "Screws");
        }

        [Test]
        public void ScrewsAndBoltedSteelPlateRecipes_AreLockedUntilScrewsCompletes()
        {
            var screws = Load<ResearchDefinition>("Assets/Data/Research/screws.asset");
            var screwsRecipe = Load<RecipeDefinition>("Assets/Data/Recipes/Screws_Recipe.asset");
            var boltedPlateRecipe = Load<RecipeDefinition>("Assets/Data/Recipes/Bolted_Steel_Plate_Recipe.asset");
            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(), new ResearchCatalog(new[] { screws }));

            Assert.IsFalse(research.IsRecipeUnlocked(screwsRecipe));
            Assert.IsFalse(research.IsRecipeUnlocked(boltedPlateRecipe));

            research.Grant(screws.Id);

            Assert.IsTrue(research.IsRecipeUnlocked(screwsRecipe));
            Assert.IsTrue(research.IsRecipeUnlocked(boltedPlateRecipe));
        }

        // --- Architecture memoire, one step further out ---

        [Test]
        public void MemoryArchitecture_IsUnreachable_UntilScrewsCompletes()
        {
            var advancedFoundry = Load<ResearchDefinition>("Assets/Data/Research/advanced_foundry.asset");
            var screws = Load<ResearchDefinition>("Assets/Data/Research/screws.asset");
            var memoryArchitecture = Load<ResearchDefinition>("Assets/Data/Research/memory_architecture.asset");
            var capaciteI = Load<ResearchDefinition>("Assets/Data/Research/memory_allocation.asset");
            var extensionI = Load<ResearchDefinition>("Assets/Data/Research/datacenter_bay_1.asset");

            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(),
                new ResearchCatalog(new[] { advancedFoundry, screws, memoryArchitecture, capaciteI, extensionI }));

            research.Grant(capaciteI.Id);
            research.Grant(extensionI.Id);
            research.Grant(advancedFoundry.Id);
            Assert.IsFalse(research.ArePrerequisitesMet(memoryArchitecture),
                "Metallurgie avancee alone must not open Architecture memoire - Vis is required first.");

            research.Grant(screws.Id);
            Assert.IsTrue(research.ArePrerequisitesMet(memoryArchitecture));
        }

        // --- Memory Mk.I recipe ---

        [Test]
        public void MemoryMk1Recipe_IsOneControlUnitTwoBoltedPlatesEightCopperWire_FortyEightSeconds_TwoHundredCu()
        {
            var recipe = Load<RecipeDefinition>("Assets/Data/Recipes/Memory_MK1_Recipe.asset");
            var controlUnit = Load<ItemDefinition>("Assets/Data/Items/control_unit.asset");
            var boltedPlate = Load<ItemDefinition>("Assets/Data/Items/Bolted_Steel_Plate.asset");
            var copperWire = Load<ItemDefinition>("Assets/Data/Items/copper_wire.asset");

            Assert.AreEqual(3, recipe.Ingredients.Length);
            Assert.AreEqual(controlUnit, recipe.Ingredients[0].Item);
            Assert.AreEqual(1, recipe.Ingredients[0].Amount);
            Assert.AreEqual(boltedPlate, recipe.Ingredients[1].Item);
            Assert.AreEqual(2, recipe.Ingredients[1].Amount);
            Assert.AreEqual(copperWire, recipe.Ingredients[2].Item);
            Assert.AreEqual(8, recipe.Ingredients[2].Amount);
            Assert.AreEqual(1, recipe.OutputAmount);
            Assert.AreEqual(48f, recipe.TimeSeconds);
            Assert.AreEqual(200f, recipe.ComputeCost);
        }

        [Test]
        public void AdvancedFoundryRuntime_ActuallyOffersSteel_OnceAdvancedFoundryIsGranted()
        {
            // Regresses a real bug found while building this chain: Steel_Recipe.asset existed and
            // was correctly named by AdvancedFoundryDefinition.RecipeIds and by advanced_foundry's own
            // UnlockRecipe effect, but was never added to RecipeDatabase.asset - so
            // ProductionBuildingRuntime.GetRecipeIds() silently dropped it (recipe == null) and Steel
            // could never actually be crafted. Exercised at the real entry point, not the database list.
            var recipeDatabase = Load<RecipeDatabase>("Assets/Data/RecipeDatabase.asset");
            var advancedFoundryDefinition = Load<AdvancedFoundryDefinition>("Assets/Data/Buildings/AdvancedFoundryDefinition.asset");
            var advancedFoundry = Load<ResearchDefinition>("Assets/Data/Research/advanced_foundry.asset");
            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(), new ResearchCatalog(new[] { advancedFoundry }));
            research.Grant(advancedFoundry.Id);

            var runtime = new AdvancedFoundryRuntime(advancedFoundryDefinition, new GridCoord(0, 0), Direction.North,
                recipeDatabase, new ComputeSystem(), new PowerSystem(), research);

            CollectionAssert.Contains(runtime.GetRecipeIds(), "Steel");
        }

        // --- Databases ---

        [Test]
        public void NewItemsAndRecipes_AreRegisteredInTheirDatabases()
        {
            var itemDatabase = Load<ItemDatabase>("Assets/Data/ItemDatabase.asset");
            var recipeDatabase = Load<RecipeDatabase>("Assets/Data/RecipeDatabase.asset");
            var researchDatabase = Load<ResearchDatabase>("Assets/Data/Research/ResearchDatabase.asset");

            Assert.IsNotNull(itemDatabase.Get("Screws"));
            Assert.IsNotNull(itemDatabase.Get("Bolted_Steel_Plate"));
            Assert.IsNotNull(recipeDatabase.Get("Screws"));
            Assert.IsNotNull(recipeDatabase.Get("Bolted_Steel_Plate"));
            Assert.IsNotNull(recipeDatabase.Get("Steel"), "Steel_Recipe was created by an earlier chantier but never registered - fixed here.");
            Assert.IsNotNull(researchDatabase.Get("screws"));
        }

        // --- Art ---

        [Test]
        public void NewItems_HaveTheirRealIcon()
        {
            // Final art shipped after this chain was first built (screw.png / bolted_steel_plate.png) -
            // supersedes the FallbackColor-only placeholder these two items launched with.
            var screwsItem = Load<ItemDefinition>("Assets/Data/Items/Screws.asset");
            var boltedPlateItem = Load<ItemDefinition>("Assets/Data/Items/Bolted_Steel_Plate.asset");

            Assert.IsNotNull(screwsItem.Icon);
            Assert.IsNotNull(boltedPlateItem.Icon);
        }
    }
}
