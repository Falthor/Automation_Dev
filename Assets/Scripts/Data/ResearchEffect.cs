using System;
using UnityEngine;

namespace Game.Data
{
    /// <summary>What completing a research does. Closed on purpose: each kind is read by exactly one system, named on the value.</summary>
    public enum ResearchEffectKind
    {
        /// <summary>A building type may be placed - ConstructionService and the building menu, through ResearchSystem.IsBuildingUnlocked.</summary>
        UnlockBuilding = 0,

        /// <summary>A recipe is offered by the machines that list it - ProductionBuildingRuntime, through ResearchSystem.IsRecipeUnlocked.</summary>
        UnlockRecipe = 1,

        /// <summary>The Core's action radius becomes Value cells - CoreRuntime. A target, not a step.</summary>
        ActionRadius = 2,

        /// <summary>The building cap becomes Value slots - ConstructionService. A target, not a step.</summary>
        BuildingCap = 3,

        /// <summary>A Datacenter gains Value pairs of bays, one CPU and one memory each - DataCenterRuntime. Summed across researches.</summary>
        DataCenterBayPairs = 4
    }

    /// <summary>
    /// One effect of a research (CONTRACTS.md §11), declared on the research and nowhere else.
    ///
    /// <b>A tagged record rather than a class hierarchy.</b> The list is short and closed, and Unity
    /// serialises a plain struct without SerializeReference. Only the field its kind names is read:
    /// Building for UnlockBuilding, Recipe for UnlockRecipe, Value for the three figures - and the
    /// inspector (ResearchEffectDrawer) shows only that one.
    ///
    /// ActionRadius and BuildingCap carry a target, not an increment: the highest target among the
    /// completed researches wins, so the order they complete in never matters.
    /// </summary>
    [Serializable]
    public struct ResearchEffect
    {
        [SerializeField] ResearchEffectKind kind;
        [SerializeField] BuildingDefinition building;
        [SerializeField] RecipeDefinition recipe;
        [SerializeField] int value;

        public ResearchEffect(ResearchEffectKind kind, BuildingDefinition building = null, RecipeDefinition recipe = null, int value = 0)
        {
            this.kind = kind;
            this.building = building;
            this.recipe = recipe;
            this.value = value;
        }

        public ResearchEffectKind Kind => kind;
        public BuildingDefinition Building => building;
        public RecipeDefinition Recipe => recipe;
        public int Value => value;

        public static ResearchEffect UnlockBuilding(BuildingDefinition building) => new ResearchEffect(ResearchEffectKind.UnlockBuilding, building: building);
        public static ResearchEffect UnlockRecipe(RecipeDefinition recipe) => new ResearchEffect(ResearchEffectKind.UnlockRecipe, recipe: recipe);
    }
}
