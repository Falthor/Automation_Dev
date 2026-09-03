using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;

namespace Game.Gameplay.Buildings
{
    /// <summary>Smelts ore into Acier via one fixed recipe (no player choice).</summary>
    public sealed class AdvancedFoundryRuntime : ProductionBuildingRuntime
    {
        readonly AdvancedFoundryDefinition _definition;

        public AdvancedFoundryRuntime(AdvancedFoundryDefinition definition, GridCoord cell, Direction facingRotation,
            RecipeDatabase recipeDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation, recipeDatabase, computeSystem, powerSystem, researchSystem,
                definition.MaxStackPerItem, definition.PowerDemandKw, definition.AcceptedItemIds)
        {
            _definition = definition;
        }

        protected override string[] GetRecipeIdWhitelist() => _definition.RecipeIds;
    }
}
