using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Smelts ore into ingots via the shared ProductionBuildingRuntime state machine. One fixed
    /// output side (never accepts input); an intake cooldown between accepted deliveries on the
    /// other sides. Mirrors the source project's foundry.gd exactly.
    /// </summary>
    public sealed class FoundryRuntime : ProductionBuildingRuntime
    {
        readonly FoundryDefinition _definition;
        readonly ItemDatabase _itemDatabase;

        float _intakeCooldown;

        public FoundryRuntime(FoundryDefinition definition, GridCoord cell, Direction facingRotation,
            RecipeDatabase recipeDatabase, ItemDatabase itemDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation, recipeDatabase, computeSystem, powerSystem, researchSystem,
                definition.MaxStackPerItem, definition.PowerDemandKw)
        {
            _definition = definition;
            _itemDatabase = itemDatabase;
        }

        protected override string[] GetRecipeIdWhitelist() => _definition.RecipeIds;

        protected override bool AcceptsItemType(string itemId) => _itemDatabase.Get(itemId)?.Type == ItemType.Ore;

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (_intakeCooldown > 0f) return false;
            return base.CanAcceptInput(itemId, amount, fromDirection);
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection)
        {
            _intakeCooldown = _definition.IntakeIntervalSeconds;
            base.AddInput(itemId, amount, fromDirection);
        }

        protected override void OnBeforeProductionTick(float effectiveDeltaTime)
        {
            if (_intakeCooldown > 0f) _intakeCooldown -= effectiveDeltaTime;
        }

        public override JObject CaptureState()
        {
            JObject state = base.CaptureState();
            state["intakeCooldown"] = _intakeCooldown;
            return state;
        }

        public override void RestoreState(JObject state)
        {
            base.RestoreState(state);
            _intakeCooldown = state.Value<float?>("intakeCooldown") ?? 0f;
        }
    }
}
