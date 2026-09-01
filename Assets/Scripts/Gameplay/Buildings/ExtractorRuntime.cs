using Game.Core;
using Game.Data;
using Game.Grid;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Automatic producer sitting on a DepositRuntime. Exposes its output through the existing
    /// Flow contract (PeekPullableItem/ConsumePulledItem) - the same mechanism a conveyor uses
    /// to hand items to its downstream neighbor - so a conveyor placed at FacingRotation can
    /// pull from it with no extractor-specific transport code. Production pauses while the
    /// produced item has not yet been pulled (naturally throttled by downstream capacity).
    /// </summary>
    public sealed class ExtractorRuntime : BuildingRuntime
    {
        readonly ExtractorDefinition _definition;
        readonly DepositRuntime _deposit;

        float _productionTimer;
        object _pendingItem;

        public DepositRuntime Deposit => _deposit;

        public ExtractorRuntime(ExtractorDefinition definition, GridCoord cell, Direction facingRotation, DepositRuntime deposit)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _deposit = deposit;
        }

        /// <summary>Advances the production timer; call once per simulation tick.</summary>
        public void Tick(float deltaTime)
        {
            if (_pendingItem != null) return; // output blocked until pulled - see class remarks.

            _productionTimer += deltaTime;
            if (_productionTimer < _definition.ExtractionIntervalSeconds) return;

            _productionTimer = 0f;

            if (_deposit.TryExtract(_definition.ItemsPerCycle, out int extracted) && extracted > 0)
            {
                _pendingItem = _deposit.OreType;
            }
        }

        public override object PeekPullableItem() => _pendingItem;

        public override void ConsumePulledItem(object item)
        {
            if (Equals(_pendingItem, item))
            {
                _pendingItem = null;
            }
        }
    }
}
