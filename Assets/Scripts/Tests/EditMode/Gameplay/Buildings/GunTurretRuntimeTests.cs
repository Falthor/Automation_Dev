using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    /// <summary>
    /// The Armament branch's newly-wired content: Tourelle, Usine d'armement (now a real
    /// FactoryDefinition instead of a ShowcaseDefinition), Munition Mk.I - unlocked directly by
    /// arms_factory itself, the standalone "ammo" research having been removed as redundant. Reads
    /// the shipped assets by path - a fixture copy would stop tracking them the moment they changed.
    /// </summary>
    public class GunTurretRuntimeTests
    {
        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"{path} is missing.");
            return asset;
        }

        [Test]
        public void GunTurretResearch_UnlocksTheRealTurretBuilding()
        {
            var research = Load<ResearchDefinition>("Assets/Data/Research/gun_turret.asset");
            var building = Load<BuildingDefinition>("Assets/Data/Buildings/GunTurretDefinition.asset");

            bool unlocksBuilding = false;
            foreach (ResearchEffect effect in research.Effects)
            {
                if (effect.Kind == ResearchEffectKind.UnlockBuilding && effect.Building == building) unlocksBuilding = true;
            }
            Assert.IsTrue(unlocksBuilding);
        }

        [Test]
        public void ArmsFactoryResearch_UnlocksTheBuildingAndTheAmmoRecipe()
        {
            var research = Load<ResearchDefinition>("Assets/Data/Research/arms_factory.asset");
            var building = Load<BuildingDefinition>("Assets/Data/Buildings/ArmsFactoryDefinition.asset");
            var recipe = Load<RecipeDefinition>("Assets/Data/Recipes/Ammo_MK1_Recipe.asset");

            bool unlocksBuilding = false, unlocksRecipe = false;
            foreach (ResearchEffect effect in research.Effects)
            {
                if (effect.Kind == ResearchEffectKind.UnlockBuilding && effect.Building == building) unlocksBuilding = true;
                if (effect.Kind == ResearchEffectKind.UnlockRecipe && effect.Recipe == recipe) unlocksRecipe = true;
            }
            Assert.IsTrue(unlocksBuilding);
            Assert.IsTrue(unlocksRecipe, "Munitions no longer exists as its own research - arms_factory unlocks the recipe directly.");
        }

        [Test]
        public void AmmoResearch_NoLongerExists()
        {
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<ResearchDefinition>("Assets/Data/Research/ammo.asset"),
                "Removed - redundant with arms_factory's own UnlockRecipe effect.");
        }

        [Test]
        public void ArmsFactoryBuilding_OffersOnlyAmmo_AndAcceptsSteel()
        {
            var building = Load<FactoryDefinition>("Assets/Data/Buildings/ArmsFactoryDefinition.asset");
            CollectionAssert.AreEquivalent(new[] { "Ammo_MK1" }, building.RecipeIds);
            CollectionAssert.Contains(building.AcceptedItemIds, "Steel");
        }

        [Test]
        public void AmmoRecipe_IsThreeSteelToOneAmmo_FiveSeconds()
        {
            var recipe = Load<RecipeDefinition>("Assets/Data/Recipes/Ammo_MK1_Recipe.asset");
            var steel = Load<ItemDefinition>("Assets/Data/Items/Steel.asset");

            Assert.AreEqual(1, recipe.Ingredients.Length);
            Assert.AreEqual(steel, recipe.Ingredients[0].Item);
            Assert.AreEqual(3, recipe.Ingredients[0].Amount);
            Assert.AreEqual(1, recipe.OutputAmount);
            Assert.AreEqual(5f, recipe.TimeSeconds);
        }

        [Test]
        public void GunTurretDefinition_Is3x3_AndCostsMotorsAndAControlUnit()
        {
            var turret = Load<GunTurretDefinition>("Assets/Data/Buildings/GunTurretDefinition.asset");
            var motor = Load<ItemDefinition>("Assets/Data/Items/electric_motor.asset");
            var controlUnit = Load<ItemDefinition>("Assets/Data/Items/control_unit.asset");

            Assert.AreEqual(new Vector2Int(3, 3), turret.FootprintSize);

            var costById = new Dictionary<string, int>();
            foreach (RecipeIngredient ingredient in turret.Cost) costById[ingredient.Item.Id] = ingredient.Amount;
            Assert.AreEqual(2, costById.Count);
            Assert.AreEqual(3, costById[motor.Id]);
            Assert.AreEqual(1, costById[controlUnit.Id]);

            Assert.IsNotNull(turret.AmmoItem);
            Assert.AreEqual("Ammo_MK1", turret.AmmoItem.Id);
        }

        [Test]
        public void GunTurretRuntime_AcceptsOnlyItsAmmo_UpToMaxStack()
        {
            var turretDefinition = Load<GunTurretDefinition>("Assets/Data/Buildings/GunTurretDefinition.asset");
            var turret = new GunTurretRuntime(turretDefinition, new GridCoord(0, 0), Direction.North);

            Assert.IsFalse(turret.CanAcceptInput("Steel", 1, Direction.South), "Nothing but its own ammo.");
            Assert.IsTrue(turret.CanAcceptInput("Ammo_MK1", 1, Direction.South));

            turret.AddInput("Ammo_MK1", turretDefinition.MaxAmmoStack, Direction.South);
            Assert.AreEqual(turretDefinition.MaxAmmoStack, turret.GetInputAmount("Ammo_MK1"));
            Assert.IsFalse(turret.CanAcceptInput("Ammo_MK1", 1, Direction.South), "Full.");
        }

        [Test]
        public void GunTurretRuntime_CaptureAndRestore_RoundTripsAmmo()
        {
            var turretDefinition = Load<GunTurretDefinition>("Assets/Data/Buildings/GunTurretDefinition.asset");
            var turret = new GunTurretRuntime(turretDefinition, new GridCoord(0, 0), Direction.North);
            turret.AddInput("Ammo_MK1", 7, Direction.South);

            var restored = new GunTurretRuntime(turretDefinition, new GridCoord(0, 0), Direction.North);
            restored.RestoreState(turret.CaptureState());

            Assert.AreEqual(7, restored.GetInputAmount("Ammo_MK1"));
        }
    }
}
