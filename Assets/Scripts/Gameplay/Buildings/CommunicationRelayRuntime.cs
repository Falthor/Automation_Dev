using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Projects an action radius while powered and fed CU. All-or-nothing like every other power
    /// consumer (ENERGIE.md): no power, or power but not enough CU to sustain the upkeep, and the
    /// radius simply stops existing that tick - <see cref="IsActive"/> is what
    /// ConstructionService.CommunicationRelays reads to decide whether this one currently counts as
    /// covering ground, and what the presentation layer reads to hide its ring.
    /// </summary>
    public sealed class CommunicationRelayRuntime : BuildingRuntime
    {
        readonly CommunicationRelayDefinition _definition;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;

        /// <summary>Whether this relay is currently projecting its radius - powered, and its continuous CU upkeep was actually paid this tick.</summary>
        public bool IsActive { get; private set; }

        /// <summary>How far this relay's own buildable zone reaches, in cells - CONSTRUCTION.md.</summary>
        public int ActionRadiusCells => _definition.ActionRadiusCells;

        public CommunicationRelayRuntime(CommunicationRelayDefinition definition, GridCoord cell, Direction facingRotation,
            ComputeSystem computeSystem, PowerSystem powerSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
        }

        public override void Tick(float deltaTime)
        {
            float performance = ComputeEffectivePerformance(_definition.PowerDemandKw, powerActive: true, _powerSystem);
            if (performance <= 0f)
            {
                IsActive = false;
                return;
            }

            float upkeep = _definition.CuUpkeepPerSecond * deltaTime;
            if (upkeep > 0f && !_computeSystem.CanSpend(upkeep))
            {
                IsActive = false;
                return;
            }

            if (upkeep > 0f) _computeSystem.Spend(upkeep);
            IsActive = true;
        }

        public override JObject CaptureState() => new JObject { ["active"] = IsActive };

        public override void RestoreState(JObject state)
        {
            // Re-evaluated on the very next Tick from real power/CU state regardless - this is only
            // what the radius view and ConstructionService.CommunicationRelays see for the one frame
            // between restore and the first tick, so a reload never shows a relay's ring lit for a
            // frame it should not be.
            IsActive = state.Value<bool?>("active") ?? false;
        }
    }
}
