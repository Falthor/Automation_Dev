using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Electricity source. Immediately operational once built (no fuel required) but only
    /// actually supplies Power while it holds Coal_ore, consumed 1 unit every
    /// FuelCycleTimeSeconds. No fuel -> 0 Power supplied and the burn timer holds at 0, but the
    /// building stays built/selectable. Its own self-power draw is unconditional regardless of
    /// fuel state - matching the source project's powerplant_gaz.gd exactly.
    /// </summary>
    public sealed class PowerplantGazRuntime : BuildingRuntime
    {
        readonly PowerplantGazDefinition _definition;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;

        int _fuelAmount;
        float _fuelTimer;

        /// <summary>Whether the fuel unit currently burning has already paid its CU, so each consumed unit is charged exactly once.</summary>
        bool _burnCharged;

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
            // Self-consumption is unconditional - unrelated to whether it currently has fuel.
            _powerSystem.ReportDemand(_definition.SelfPowerDemandKw);

            if (!HasFuel)
            {
                _fuelTimer = 0f;
                _burnCharged = false;
                return;
            }

            // Burning one unit of fuel costs CuCostPerCycle, taken in full when that unit starts
            // burning (§10). Without the CU the unit never lights, so no Power is supplied either.
            if (!_burnCharged)
            {
                if (!_computeSystem.CanSpend(_definition.CuCostPerCycle)) return;
                _computeSystem.Spend(_definition.CuCostPerCycle);
                _burnCharged = true;
            }

            _powerSystem.ReportSupply(_definition.PowerOutputKw);
            _fuelTimer += deltaTime;
            if (_fuelTimer < _definition.FuelCycleTimeSeconds) return;

            _fuelAmount -= 1;
            _burnCharged = false;
            _fuelTimer = !HasFuel ? 0f : _fuelTimer - _definition.FuelCycleTimeSeconds;
        }

        public override JObject CaptureState()
        {
            return new JObject
            {
                ["fuelAmount"] = _fuelAmount,
                ["fuelTimer"] = _fuelTimer,
                ["burnCharged"] = _burnCharged
            };
        }

        public override void RestoreState(JObject state)
        {
            _fuelAmount = state.Value<int?>("fuelAmount") ?? 0;
            _fuelTimer = state.Value<float?>("fuelTimer") ?? 0f;
            _burnCharged = state.Value<bool?>("burnCharged") ?? false;
        }
    }
}
