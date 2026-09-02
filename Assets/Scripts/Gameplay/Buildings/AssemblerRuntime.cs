using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Assembles cpu_mkI and mechanical_component. Placement itself is gated by
    /// BuildingDefinition.UnlockResearch (checked by ConstructionService), so no recipe-level
    /// gate is needed here.
    /// </summary>
    public sealed class AssemblerRuntime : ProductionBuildingRuntime
    {
        readonly AssemblerDefinition _definition;

        public AssemblerRuntime(AssemblerDefinition definition, GridCoord cell, Direction facingRotation,
            RecipeDatabase recipeDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation, recipeDatabase, computeSystem, powerSystem, researchSystem,
                definition.MaxStackPerItem, definition.PowerDemandKw, definition.AcceptedItemIds)
        {
            _definition = definition;
        }

        protected override string[] GetRecipeIdWhitelist() => _definition.RecipeIds;
    }
}
