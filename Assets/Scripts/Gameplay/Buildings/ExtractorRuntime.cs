using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
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
    ///
    /// Its rate (ItemsPerMinute) grows with each ExtractorItemsPerMinute research effect completed
    /// (RECHERCHE.md), the same target-not-a-step shape as CoreRuntime.ActionRadiusCells.
    /// </summary>
    public sealed class ExtractorRuntime : BuildingRuntime
    {
        public const int InternalStorageCapacity = 20;

        readonly ExtractorDefinition _definition;
        readonly DepositRuntime _deposit;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;
        readonly System.Action<string> _onResearchCompleted;

        float _productionTimer;
        int _bufferedAmount;

        /// <summary>Whether the extraction currently in progress has already paid its CU. Reset once it completes, so each extraction is charged exactly once.</summary>
        bool _cycleCharged;

        /// <summary>
        /// Current rate in items/min - starts at ExtractorDefinition.ItemsPerMinute, grows with each
        /// ExtractorItemsPerMinute effect completed (RECHERCHE.md). A target, not a step, matching
        /// CoreRuntime.ActionRadiusCells: the highest completed wins, so completion order and a
        /// reload never shrink it.
        /// </summary>
        public float ItemsPerMinute { get; private set; }

        public DepositRuntime Deposit => _deposit;
        public string ItemId => _deposit.ItemId;
        public int BufferedAmount => _bufferedAmount;

        /// <summary>
        /// Duration of one extraction cycle at the current researched rate - ItemsPerCycle fixed,
        /// the interval itself shrinking as ItemsPerMinute grows. So a panel showing time remaining
        /// reflects the same speed the extractor is actually running at.
        /// </summary>
        public float ExtractionIntervalSeconds => ItemsPerMinute > 0f ? _definition.ItemsPerCycle / ItemsPerMinute * 60f : _definition.ExtractionIntervalSeconds;

        /// <summary>Progress toward the next extraction cycle, in [0,1]. Frozen (does not advance) while the internal buffer is full.</summary>
        public float ProductionProgress
        {
            get
            {
                float interval = ExtractionIntervalSeconds;
                if (interval <= 0f) return 1f;
                float t = _productionTimer / interval;
                if (t < 0f) return 0f;
                return t > 1f ? 1f : t;
            }
        }

        public ExtractorRuntime(ExtractorDefinition definition, GridCoord cell, Direction facingRotation, DepositRuntime deposit,
            ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _deposit = deposit;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
            _researchSystem = researchSystem;
            // Not just definition.ItemsPerMinute: an Extractor built after extractor_power already
            // completed (the ordinary case - it is rarely the very first thing researched) must
            // start at the researched rate, not fall back to it only on the next completion.
            ItemsPerMinute = UnlockedItemsPerMinute(definition, researchSystem);

            _onResearchCompleted = OnResearchCompleted;
            researchSystem.ResearchCompleted += _onResearchCompleted;
        }

        void OnResearchCompleted(string researchId)
        {
            ItemsPerMinute = System.Math.Max(ItemsPerMinute, RateOf(_researchSystem.Definition(researchId)));
        }

        /// <summary>The highest rate every research already completed grants - what an Extractor built after them starts with, same reasoning as DataCenterRuntime.UnlockedBayPairs.</summary>
        static float UnlockedItemsPerMinute(ExtractorDefinition definition, ResearchSystem research)
        {
            float rate = definition.ItemsPerMinute;
            foreach (string id in research.GetUnlockedIds()) rate = System.Math.Max(rate, RateOf(research.Definition(id)));
            return rate;
        }

        static float RateOf(ResearchDefinition research)
        {
            if (research == null) return 0f;

            float rate = 0f;
            IReadOnlyList<ResearchEffect> effects = research.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Kind == ResearchEffectKind.ExtractorItemsPerMinute) rate = System.Math.Max(rate, effects[i].Value);
            }
            return rate;
        }

        /// <summary>Unsubscribes from ResearchSystem so a demolished Extractor doesn't keep growing its rate forever.</summary>
        public override void OnUnregistered()
        {
            _researchSystem.ResearchCompleted -= _onResearchCompleted;
        }

        /// <summary>Advances the production timer; call once per simulation tick.</summary>
        public override void Tick(float deltaTime)
        {
            bool bufferFull = _bufferedAmount >= InternalStorageCapacity;

            // Power is drawn only while it can actually output - a full buffer (e.g. no conveyor
            // attached) stops drawing power for work it isn't doing.
            float performance = ComputeEffectivePerformance(_definition.PowerDemandKw, powerActive: !bufferFull, _powerSystem);

            if (bufferFull) return; // full: output blocked (e.g. no conveyor attached) - stop producing entirely.

            // One extraction costs CuCostPerCycle, taken in full when it starts (CALCUL.md) - the
            // same rule a production building's recipe cost follows. Too little in the reserve and
            // the extraction simply does not start: the timer holds at 0 rather than running for free.
            if (!_cycleCharged)
            {
                if (!_computeSystem.CanSpend(_definition.CuCostPerCycle)) return;
                _computeSystem.Spend(_definition.CuCostPerCycle);
                _cycleCharged = true;
            }

            _productionTimer += deltaTime * performance;
            if (_productionTimer < ExtractionIntervalSeconds) return;

            _productionTimer = 0f;
            _cycleCharged = false;

            // The deposit is inexhaustible, so the only thing that can
            // limit a cycle's yield is this extractor's own buffer.
            int room = InternalStorageCapacity - _bufferedAmount;
            _bufferedAmount += System.Math.Min(_definition.ItemsPerCycle, room);
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
                ["cycleCharged"] = _cycleCharged,
                ["itemsPerMinute"] = ItemsPerMinute
            };
        }

        public override void RestoreState(JObject state)
        {
            _productionTimer = state.Value<float?>("productionTimer") ?? 0f;
            _bufferedAmount = state.Value<int?>("bufferedAmount") ?? 0;
            _cycleCharged = state.Value<bool?>("cycleCharged") ?? false;
            // Persisted directly rather than re-derived from ResearchSystem.IsUnlocked at
            // construction, matching CoreRuntime.ActionRadiusCells - an absent key is an older
            // save or a fresh extractor, both of which are exactly the definition's own rate.
            ItemsPerMinute = state.Value<float?>("itemsPerMinute") ?? _definition.ItemsPerMinute;
        }
    }
}
