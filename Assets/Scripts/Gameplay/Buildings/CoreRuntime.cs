using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Items;
using Game.Gameplay.Power;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// The unique, world-generated Core: a permanent Power source (unconditional per-tick report)
    /// and the game's CU source (a fixed grant into the global reserve at a fixed interval), no
    /// cable/network needed for either, plus a pooled, uncapped inventory - one of the sources
    /// ConstructionService draws construction costs from (alongside the player's global starting
    /// stock and every placed Storage). It starts empty: the game's starting resources belong to
    /// the player, not to this building (WorldGenerationSettings.StartingStock). Never placed by
    /// the player (see WorldGenerator); accepts input from every side, like Storage, since it has
    /// no output direction of its own.
    /// </summary>
    public sealed class CoreRuntime : BuildingRuntime
    {
        readonly CoreDefinition _definition;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly PooledItemStock _inventory = new PooledItemStock(int.MaxValue);

        float _cuTimer;

        public CoreRuntime(CoreDefinition definition, GridCoord cell, Direction facingRotation,
            ComputeSystem computeSystem, PowerSystem powerSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
        }

        /// <summary>Read-only snapshot for the Core inspector panel (CONTRACTS.md §12).</summary>
        public IReadOnlyDictionary<string, int> GetContents() => _inventory.Contents;

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection) => _inventory.CanAccept(itemId, amount);
        public override void AddInput(string itemId, int amount, Direction fromDirection) => _inventory.Add(itemId, amount);
        public override int TakeInput(string itemId, int amount) => _inventory.Take(itemId, amount);
        public override int GetInputAmount(string itemId) => _inventory.GetAmount(itemId);

        public override void Tick(float deltaTime)
        {
            // CU arrives as a periodic grant, not a per-second flow: the reserve jumps by
            // CuOutput once every CuOutputIntervalSeconds. Power stays a continuous supply.
            _cuTimer += deltaTime;
            if (_cuTimer >= _definition.CuOutputIntervalSeconds)
            {
                _cuTimer -= _definition.CuOutputIntervalSeconds;
                _computeSystem.Grant(_definition.CuOutput);
            }

            _powerSystem.ReportSupply(_definition.PowerOutputKw);
        }
    }
}
