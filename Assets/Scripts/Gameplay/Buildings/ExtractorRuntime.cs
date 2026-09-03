using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Grid;
using Newtonsoft.Json.Linq;

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
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;

        float _productionTimer;
        int _bufferedAmount;

        /// <summary>Whether the extraction currently in progress has already paid its CU. Reset once it completes, so each extraction is charged exactly once.</summary>
        bool _cycleCharged;

        public DepositRuntime Deposit => _deposit;
        public string ItemId => _deposit.ItemId;
        public int BufferedAmount => _bufferedAmount;

        /// <summary>Duration of one extraction cycle, so a panel can turn ProductionProgress into a remaining time without reading the definition itself.</summary>
        public float ExtractionIntervalSeconds => _definition.ExtractionIntervalSeconds;

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

        public ExtractorRuntime(ExtractorDefinition definition, GridCoord cell, Direction facingRotation, DepositRuntime deposit,
            ComputeSystem computeSystem, PowerSystem powerSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _deposit = deposit;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
        }

        /// <summary>Advances the production timer; call once per simulation tick.</summary>
        public override void Tick(float deltaTime)
        {
            bool bufferFull = _bufferedAmount >= InternalStorageCapacity;

            // Power is drawn only while it can actually output - a full buffer (e.g. no conveyor
            // attached) stops drawing power for work it isn't doing, matching the source
            // project's extractor.gd exactly.
            float performance = ComputeEffectivePerformance(_definition.PowerDemandKw, powerActive: !bufferFull, _powerSystem);

            if (bufferFull) return; // full: output blocked (e.g. no conveyor attached) - stop producing entirely.

            // One extraction costs CuCostPerCycle, taken in full when it starts (§10) - the same
            // rule a production building's recipe cost follows. Too little in the reserve and the
            // extraction simply does not start: the timer holds at 0 rather than running for free.
            if (!_cycleCharged)
            {
                if (!_computeSystem.CanSpend(_definition.CuCostPerCycle)) return;
                _computeSystem.Spend(_definition.CuCostPerCycle);
                _cycleCharged = true;
            }

            _productionTimer += deltaTime * performance;
            if (_productionTimer < _definition.ExtractionIntervalSeconds) return;

            _productionTimer = 0f;
            _cycleCharged = false;

            int room = InternalStorageCapacity - _bufferedAmount;
            int toExtract = System.Math.Min(_definition.ItemsPerCycle, room);
            if (_deposit.TryExtract(toExtract, out int extracted) && extracted > 0)
            {
                _bufferedAmount += extracted;
            }
        }

        public override object PeekPullableItem() => _bufferedAmount > 0 ? (object)_deposit.ItemId : null;

        public override void ConsumePulledItem(object item)
        {
            if (_bufferedAmount > 0 && Equals(_deposit.ItemId, item))
            {
                _bufferedAmount -= 1;
            }
        }

        public override JObject CaptureState()
        {
            return new JObject
            {
                ["productionTimer"] = _productionTimer,
                ["bufferedAmount"] = _bufferedAmount,
                ["cycleCharged"] = _cycleCharged
            };
        }

        public override void RestoreState(JObject state)
        {
            _productionTimer = state.Value<float?>("productionTimer") ?? 0f;
            _bufferedAmount = state.Value<int?>("bufferedAmount") ?? 0;
            _cycleCharged = state.Value<bool?>("cycleCharged") ?? false;
        }
    }
}
