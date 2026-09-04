using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Converts delivered Cartes de donnee into RP continuously (independent of whether a
    /// research is selected), and contributes to whichever research is currently active. Power is
    /// always drawn and freezes the conversion when the network cannot cover it.
    /// </summary>
    public sealed class LaboratoryRuntime : BuildingRuntime
    {
        readonly LaboratoryDefinition _definition;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;

        int _cardAmount;
        float _cardTimer;

        /// <summary>Whether the conversion currently in progress has already paid its CU, so each converted card is charged exactly once.</summary>
        bool _conversionCharged;

        public float CardTimer => _cardTimer;

        public LaboratoryRuntime(LaboratoryDefinition definition, GridCoord cell, Direction facingRotation,
            ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
            _researchSystem = researchSystem;
        }

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (fromDirection == ExitDirection) return false;
            if (itemId != _definition.CardItem.Id) return false;
            return _cardAmount + amount <= _definition.MaxCardStack;
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection) => _cardAmount += amount;
        public override int GetInputAmount(string itemId) => itemId == _definition.CardItem.Id ? _cardAmount : 0;

        public override void Tick(float deltaTime)
        {
            bool researchActive = _researchSystem.HasActiveResearch();

            float performance = ComputeEffectivePerformance(_definition.PowerDemandKw, powerActive: true, _powerSystem);

            if (researchActive) _researchSystem.ReportActiveLab();

            if (_cardAmount <= 0)
            {
                _cardTimer = 0f;
                _conversionCharged = false;
                return;
            }

            // Turning one card into RP costs CuCostPerCycle, taken in full when that conversion
            // starts (§10). Too little in the reserve and it does not start at all.
            if (!_conversionCharged)
            {
                if (!_computeSystem.CanSpend(_definition.CuCostPerCycle)) return;
                _computeSystem.Spend(_definition.CuCostPerCycle);
                _conversionCharged = true;
            }

            _cardTimer += deltaTime * performance;
            if (_cardTimer < _definition.CardConvertIntervalSeconds) return;

            _cardAmount -= 1;
            _researchSystem.AddRp(_definition.RpPerCard);
            _conversionCharged = false;
            _cardTimer = _cardAmount <= 0 ? 0f : _cardTimer - _definition.CardConvertIntervalSeconds;
        }

        public override JObject CaptureState()
        {
            return new JObject
            {
                ["cardAmount"] = _cardAmount,
                ["cardTimer"] = _cardTimer,
                ["conversionCharged"] = _conversionCharged
            };
        }

        public override void RestoreState(JObject state)
        {
            _cardAmount = state.Value<int?>("cardAmount") ?? 0;
            _cardTimer = state.Value<float?>("cardTimer") ?? 0f;
            _conversionCharged = state.Value<bool?>("conversionCharged") ?? false;
        }
    }
}
