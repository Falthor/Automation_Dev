using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Converts delivered Cartes de donnee into RP continuously (independent of whether a
    /// research is selected), and contributes to whichever research is currently active. Draws
    /// CU only while a research is active; Power is always drawn. Matches laboratory.gd exactly.
    /// </summary>
    public sealed class LaboratoryRuntime : BuildingRuntime
    {
        readonly LaboratoryDefinition _definition;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;

        int _cardAmount;
        float _cardTimer;

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

            float performance = ComputeEffectivePerformance(
                cuDemand: _definition.CuDemand, computeActive: researchActive,
                powerDemand: _definition.PowerDemandKw, powerActive: true,
                _computeSystem, _powerSystem);

            if (researchActive) _researchSystem.ReportActiveLab();

            if (_cardAmount <= 0)
            {
                _cardTimer = 0f;
                return;
            }

            _cardTimer += deltaTime * performance;
            if (_cardTimer < _definition.CardConvertIntervalSeconds) return;

            _cardAmount -= 1;
            _researchSystem.AddRp(_definition.RpPerCard);
            _cardTimer = _cardAmount <= 0 ? 0f : _cardTimer - _definition.CardConvertIntervalSeconds;
        }
    }
}
