using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Items;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// The unique, world-generated Core: a permanent Power source (unconditional per-tick report)
    /// and the game's CU source (a fixed grant into the global reserve at a fixed interval), no
    /// cable/network needed for either. Never placed by the player (see WorldGenerator).
    ///
    /// The Core no longer accepts any item deliveries at all (design decision: a conveyor or any
    /// other building must never be able to hand it resources) - CanAcceptInput always refuses,
    /// which is the single chokepoint every transport path (generic pull/push, splitter/crossroad
    /// delivery) already checks before calling AddInput. The starting resources that used to live
    /// in a global, building-less pool now live in a real, dedicated Storage Box world-generated
    /// just south of the Core instead (WorldGenerator.CoreStorage) - see
    /// ConstructionService.GetAvailableAmount/PayCost, which still list the Core as a cost source
    /// for backward compatibility with an older save that had items in it, but it can never gain
    /// new ones from here on.
    ///
    /// Also the sole owner of the action radius as runtime state (TASK_04_PLAFOND_RAYON.md §4):
    /// CoreDefinition.ActionRadiusCells is only the starting value. ActionRadiusCells here is what
    /// every placement check must read - extended_bandwidth grows it in place, live, no reload
    /// needed. Persisted directly in CaptureState/RestoreState rather than re-derived from
    /// ResearchSystem.IsUnlocked at construction, matching that task's save decision.
    /// </summary>
    public sealed class CoreRuntime : BuildingRuntime
    {
        public const string ExtendedBandwidthResearchId = "extended_bandwidth";

        /// <summary>
        /// 32, not the ticket's originally stated 30: the invitation ore clusters WorldGenerator
        /// actually places (InvitationMinDistanceCells/MaxDistanceCells = 28-34) sit farther out
        /// than the ticket assumed (25-28), so 30 would only have made each cluster partially
        /// reachable. Measured in-world with the real fixed ResourceSeed: the farthest invitation
        /// deposit is ~31.9 cells out (coal); 32 clears all three clusters in full.
        /// </summary>
        public const int ExtendedActionRadiusCells = 32;

        readonly CoreDefinition _definition;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;
        readonly PooledItemStock _inventory = new PooledItemStock(int.MaxValue);
        readonly System.Action<string> _onResearchCompleted;

        float _cuTimer;

        /// <summary>Current action radius in cells - starts at CoreDefinition.ActionRadiusCells, grows via extended_bandwidth.</summary>
        public int ActionRadiusCells { get; private set; }

        public CoreRuntime(CoreDefinition definition, GridCoord cell, Direction facingRotation,
            ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
            _researchSystem = researchSystem;
            ActionRadiusCells = definition.ActionRadiusCells;

            _onResearchCompleted = OnResearchCompleted;
            researchSystem.ResearchCompleted += _onResearchCompleted;
        }

        void OnResearchCompleted(string researchId)
        {
            if (researchId == ExtendedBandwidthResearchId) ActionRadiusCells = ExtendedActionRadiusCells;
        }

        /// <summary>Unsubscribes from ResearchSystem - the Core is never demolished in practice, but this keeps the same hygiene as every other research-reactive building (DataCenterRuntime).</summary>
        public override void OnUnregistered()
        {
            _researchSystem.ResearchCompleted -= _onResearchCompleted;
        }

        /// <summary>Read-only snapshot for the Core inspector panel (CONTRACTS.md §12) - always empty from here on, kept for an older save that had items in it.</summary>
        public IReadOnlyDictionary<string, int> GetContents() => _inventory.Contents;

        /// <summary>Always refuses: the Core no longer receives anything, by design - see the class doc comment.</summary>
        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection) => false;
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

        public override JObject CaptureState()
        {
            return new JObject
            {
                ["cuTimer"] = _cuTimer,
                ["actionRadiusCells"] = ActionRadiusCells,
                ["contents"] = JObject.FromObject(_inventory.Contents)
            };
        }

        /// <summary>
        /// actionRadiusCells falls back to the definition's starting value when absent (a save
        /// from before TASK_04_PLAFOND_RAYON.md) - never to 0, which would make every placement
        /// check reject everything (TASK_03_DATACENTER.md's restore-tolerance precedent).
        /// </summary>
        public override void RestoreState(JObject state)
        {
            _cuTimer = state.Value<float?>("cuTimer") ?? 0f;
            ActionRadiusCells = state.Value<int?>("actionRadiusCells") ?? _definition.ActionRadiusCells;
            _inventory.RestoreContents(state["contents"]?.ToObject<Dictionary<string, int>>());
        }
    }
}
