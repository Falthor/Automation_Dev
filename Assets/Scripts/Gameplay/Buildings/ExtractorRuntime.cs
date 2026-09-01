using Game.Core;
using Game.Data;
using Game.Grid;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Automatic producer sitting on a DepositRuntime. Exposes its output through the existing
    /// Flow contract (PeekPullableItem/ConsumePulledItem) - the same mechanism a conveyor uses
    /// to hand items to its downstream neighbor - so a conveyor placed at FacingRotation can
    /// pull from it with no extractor-specific transport code. Production accumulates into a
    /// bounded internal buffer (InternalStorageCapacity) rather than a single pending item, so a
    /// temporarily disconnected output doesn't stall production immediately - only once the
    /// buffer is actually full.
    /// </summary>
    public sealed class ExtractorRuntime : BuildingRuntime
    {
        public const int InternalStorageCapacity = 20;

        readonly ExtractorDefinition _definition;
        readonly DepositRuntime _deposit;

        float _productionTimer;
        int _bufferedAmount;

        public DepositRuntime Deposit => _deposit;
        public OreType OreType => _deposit.OreType;
        public int BufferedAmount => _bufferedAmount;

        /// <summary>Progress toward the next extraction cycle, in [0,1]. Frozen (does not advance) while the internal buffer is full.</summary>
        public float ProductionProgress
        {
            get
            {
                float interval = _definition.ExtractionIntervalSeconds;
                if (interval <= 0f) return 1f;
                float t = _productionTimer / interval;
                if (t < 0f) return 0f;
                return t > 1f ? 1f : t;
            }
        }

        public ExtractorRuntime(ExtractorDefinition definition, GridCoord cell, Direction facingRotation, DepositRuntime deposit)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _deposit = deposit;
        }

        /// <summary>Advances the production timer; call once per simulation tick.</summary>
        public void Tick(float deltaTime)
        {
            if (_bufferedAmount >= InternalStorageCapacity) return; // full: output blocked (e.g. no conveyor attached) - stop producing entirely.

            _productionTimer += deltaTime;
            if (_productionTimer < _definition.ExtractionIntervalSeconds) return;

            _productionTimer = 0f;

            int room = InternalStorageCapacity - _bufferedAmount;
            int toExtract = System.Math.Min(_definition.ItemsPerCycle, room);
            if (_deposit.TryExtract(toExtract, out int extracted) && extracted > 0)
            {
                _bufferedAmount += extracted;
            }
        }

        public override object PeekPullableItem() => _bufferedAmount > 0 ? (object)_deposit.OreType : null;

        public override void ConsumePulledItem(object item)
        {
            if (_bufferedAmount > 0 && Equals(_deposit.OreType, item))
            {
                _bufferedAmount -= 1;
            }
        }
    }
}
