using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;

namespace Game.Gameplay.Buildings
{
    /// <summary>Crafts tier-1/tier-2 intermediate components via the shared production contract.</summary>
    public sealed class FactoryRuntime : ProductionBuildingRuntime
    {
        readonly FactoryDefinition _definition;

        public FactoryRuntime(FactoryDefinition definition, GridCoord cell, Direction facingRotation,
            RecipeDatabase recipeDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation, recipeDatabase, computeSystem, powerSystem, researchSystem,
                definition.MaxStackPerItem, definition.PowerDemandKw, definition.AcceptedItemIds)
        {
            _definition = definition;
        }

        protected override string[] GetRecipeIdWhitelist() => _definition.RecipeIds;
    }
}
