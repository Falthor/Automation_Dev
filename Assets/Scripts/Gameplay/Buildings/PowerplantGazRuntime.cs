using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Electricity source. Immediately operational once built (no fuel required) but only
    /// actually supplies Power while it holds Coal_ore, consumed 1 unit every
    /// FuelCycleTimeSeconds. No fuel -> 0 Power supplied and the burn timer holds at 0, but the
    /// building stays built/selectable. Its own CU/self-power draw is unconditional regardless
    /// of fuel state - matching the source project's powerplant_gaz.gd exactly.
    /// </summary>
    public sealed class PowerplantGazRuntime : BuildingRuntime
    {
        readonly PowerplantGazDefinition _definition;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;

        int _fuelAmount;
        float _fuelTimer;

        public int FuelAmount => _fuelAmount;
        public float FuelTimer => _fuelTimer;
        public bool HasFuel => _fuelAmount > 0;

        public PowerplantGazRuntime(PowerplantGazDefinition definition, GridCoord cell, Direction facingRotation,
            ComputeSystem computeSystem, PowerSystem powerSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
        }

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (fromDirection == ExitDirection) return false;
            if (itemId != _definition.FuelItem.Id) return false;
            return _fuelAmount + amount <= _definition.MaxFuelStack;
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection) => _fuelAmount += amount;
        public override int GetInputAmount(string itemId) => itemId == _definition.FuelItem.Id ? _fuelAmount : 0;

        public override void Tick(float deltaTime)
        {
            // Self-consumption and CU draw are unconditional - unrelated to whether it currently has fuel.
            _computeSystem.ReportDemand(_definition.CuDemand);
            _powerSystem.ReportDemand(_definition.SelfPowerDemandKw);

            if (!HasFuel)
            {
                _fuelTimer = 0f;
                return;
            }

            _powerSystem.ReportSupply(_definition.PowerOutputKw);
            _fuelTimer += deltaTime;
            if (_fuelTimer < _definition.FuelCycleTimeSeconds) return;

            _fuelAmount -= 1;
            _fuelTimer = !HasFuel ? 0f : _fuelTimer - _definition.FuelCycleTimeSeconds;
        }
    }
}
