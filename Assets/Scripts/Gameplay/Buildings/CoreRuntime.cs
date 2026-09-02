using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Items;
using Game.Gameplay.Power;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// The unique, world-generated Core: a permanent Power/Compute source (unconditional per-tick
    /// report, no cable/network needed) plus a pooled, uncapped inventory seeded with a starting
    /// stock - the first currency ConstructionService draws construction costs from (alongside
    /// every placed Storage). Never placed by the player (see WorldGenerator); accepts input from
    /// every side, like Storage, since it has no output direction of its own.
    /// </summary>
    public sealed class CoreRuntime : BuildingRuntime
    {
        readonly CoreDefinition _definition;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly PooledItemStock _inventory = new PooledItemStock(int.MaxValue);

        public CoreRuntime(CoreDefinition definition, GridCoord cell, Direction facingRotation,
            ComputeSystem computeSystem, PowerSystem powerSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;

            foreach (RecipeIngredient entry in definition.StartingStock)
            {
                if (entry.Item != null) _inventory.Add(entry.Item.Id, entry.Amount);
            }
        }

        /// <summary>Read-only snapshot for the Core inspector panel (CONTRACTS.md §12).</summary>
        public IReadOnlyDictionary<string, int> GetContents() => _inventory.Contents;

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection) => _inventory.CanAccept(itemId, amount);
        public override void AddInput(string itemId, int amount, Direction fromDirection) => _inventory.Add(itemId, amount);
        public override int TakeInput(string itemId, int amount) => _inventory.Take(itemId, amount);
        public override int GetInputAmount(string itemId) => _inventory.GetAmount(itemId);

        public override void Tick(float deltaTime)
        {
            _computeSystem.ReportSupply(_definition.CuOutput);
            _powerSystem.ReportSupply(_definition.PowerOutputKw);
        }
    }
}
