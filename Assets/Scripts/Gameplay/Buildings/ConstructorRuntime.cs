using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Crafts intermediate components through the shared production contract, taking deliveries on
    /// one chosen side only (ConstructorDefinition.HasSingleInputArrow).
    ///
    /// Nothing here implements that restriction: it lives in ProductionBuildingRuntime.CanAcceptInput
    /// and BuildingRuntime.GetInputCells, off the definition's own declaration, so this type is the
    /// Factory's shape with a different recipe list - which is all it is meant to be.
    /// </summary>
    public sealed class ConstructorRuntime : ProductionBuildingRuntime
    {
        readonly ConstructorDefinition _definition;

        public ConstructorRuntime(ConstructorDefinition definition, GridCoord cell, Direction facingRotation,
            RecipeDatabase recipeDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation, recipeDatabase, computeSystem, powerSystem, researchSystem,
                definition.PowerDemandKw, definition.AcceptedItemIds)
        {
            _definition = definition;
        }

        protected override string[] GetRecipeIdWhitelist() => _definition.RecipeIds;
    }
}
