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
    /// Aggregates installed CPU/Memory components into Compute supply and Power demand
    /// (TASK_03_DATACENTER.md). Pooled input accepts cpu_mkI/Memory_MK1 via the standard
    /// Building/Inventory contract - see ComponentInstance for the per-slot wear/stability/
    /// replacement rules.
    ///
    /// A freshly placed Data Center primes for 90s (1500 CU consumed, no production, no wear -
    /// GDD §2.3) before any of that applies; priming is a second continuous per-second CU draw
    /// alongside research's own (CONTRACTS.md §10/§13), and pauses at zero CU exactly like
    /// research does. Once primed, its output splits across two axes (research/buildings) via a
    /// concentration-based yield curve (§7) - both currently credit the same single reserve, so
    /// the split only matters for what the UI reports until per-axis reserves exist.
    /// </summary>
    public sealed class DataCenterRuntime : BuildingRuntime
    {
        const int InitialCpuSlots = 2;
        const int InitialMemorySlots = 2;
        const int MaxCpuSlots = 4;
        const int MaxMemorySlots = 4;
        const float StabilityInterval = 5f;
        const float ReplacementDuration = 5f;
        const string DataCenterBay1ResearchId = "datacenter_bay_1";
        const string DataCenterBay2ResearchId = "datacenter_bay_2";
        const string CpuItemId = "cpu_mkI";
        const string MemoryItemId = "Memory_MK1";

        const float PrimingCostCu = 1500f;
        const float PrimingDurationSeconds = 90f;
        const float PrimingAbsorptionRatePerSecond = PrimingCostCu / PrimingDurationSeconds;

        public const float MinReplacementThresholdPercent = 5f;
        public const float MaxReplacementThresholdPercent = 60f;
        public const float DefaultReplacementThresholdPercent = 25f;

        readonly DataCenterDefinition _definition;
        readonly ItemDatabase _itemDatabase;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;
        readonly PooledItemStock _input;
        readonly List<ComponentInstance> _cpuSlots;
        readonly List<ComponentInstance> _memorySlots;
        readonly System.Action<string> _onResearchCompleted;

        /// <summary>Owns every per-component lifetime draw for this Data Center's whole lifetime - one seeded stream, not re-seeded per install, so a fixed seed plus a fixed installation sequence always reproduces the same drawn lifetimes (DEVELOPMENT_RULES.md §7).</summary>
        readonly System.Random _lifetimeRandom;

        float _stabilityTimer;
        float _previousPowerDemand;
        float _primingAbsorbedCu;

        public IReadOnlyList<ComponentInstance> CpuSlots => _cpuSlots;
        public IReadOnlyList<ComponentInstance> MemorySlots => _memorySlots;

        /// <summary>5..60, default 25 - adjustable at any time, for free (TASK_03_DATACENTER.md §5).</summary>
        public float CpuReplacementThresholdPercent { get; private set; } = DefaultReplacementThresholdPercent;

        /// <summary>5..60, default 25 - independent of the CPU setting.</summary>
        public float MemoryReplacementThresholdPercent { get; private set; } = DefaultReplacementThresholdPercent;

        /// <summary>Fraction of installed output aimed at the research axis, in [0,1]; the buildings axis gets the complement. Default 0.5 (50/50). Free and instantaneous to change (§7) - there is no armament axis yet.</summary>
        public float ResearchAxisShare { get; private set; } = 0.5f;

        /// <summary>True from placement until PrimingCostCu has been absorbed - no production, no wear while true (GDD §2.3).</summary>
        public bool IsPriming => _primingAbsorbedCu < PrimingCostCu;

        /// <summary>0..1 fraction of priming absorbed so far.</summary>
        public float PrimingProgress => UnityEngine.Mathf.Clamp01(_primingAbsorbedCu / PrimingCostCu);

        /// <summary>Best-case seconds remaining in priming at its fixed absorption rate - 0 once primed.</summary>
        public float GetPrimingSecondsRemaining() => IsPriming ? (PrimingCostCu - _primingAbsorbedCu) / PrimingAbsorptionRatePerSecond : 0f;

        public DataCenterRuntime(DataCenterDefinition definition, GridCoord cell, Direction facingRotation,
            ItemDatabase itemDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _itemDatabase = itemDatabase;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
            _researchSystem = researchSystem;
            _input = new PooledItemStock(definition.MaxStackPerItem);
            _lifetimeRandom = new System.Random(definition.ComponentLifetimeSeed);

            _cpuSlots = new List<ComponentInstance>(new ComponentInstance[InitialCpuSlots]);
            _memorySlots = new List<ComponentInstance>(new ComponentInstance[InitialMemorySlots]);
            if (researchSystem.IsUnlocked(DataCenterBay1ResearchId)) AddBayPair();
            if (researchSystem.IsUnlocked(DataCenterBay2ResearchId)) AddBayPair();

            _onResearchCompleted = OnResearchCompleted;
            researchSystem.ResearchCompleted += _onResearchCompleted;
        }

        /// <summary>Either extension research appends one CPU bay and one Memory bay, capped at 4+4 - usable by the same install/wear/replacement code, no separate mechanism.</summary>
        void OnResearchCompleted(string researchId)
        {
            if (researchId == DataCenterBay1ResearchId || researchId == DataCenterBay2ResearchId) AddBayPair();
        }

        void AddBayPair()
        {
            if (_cpuSlots.Count < MaxCpuSlots) _cpuSlots.Add(null);
            if (_memorySlots.Count < MaxMemorySlots) _memorySlots.Add(null);
        }

        /// <summary>Unsubscribes from ResearchSystem so a demolished Data Center doesn't keep growing slots forever.</summary>
        public override void OnUnregistered()
        {
            _researchSystem.ResearchCompleted -= _onResearchCompleted;
        }

        public void SetCpuReplacementThreshold(float percent) => CpuReplacementThresholdPercent = UnityEngine.Mathf.Clamp(percent, MinReplacementThresholdPercent, MaxReplacementThresholdPercent);
        public void SetMemoryReplacementThreshold(float percent) => MemoryReplacementThresholdPercent = UnityEngine.Mathf.Clamp(percent, MinReplacementThresholdPercent, MaxReplacementThresholdPercent);
        public void SetResearchAxisShare(float share) => ResearchAxisShare = UnityEngine.Mathf.Clamp01(share);

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (fromDirection == ExitDirection) return false;
            if (System.Array.IndexOf(_definition.AcceptedItemIds, itemId) < 0) return false;
            return _input.CanAccept(itemId, amount);
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection) => _input.Add(itemId, amount);
        public override int GetInputAmount(string itemId) => _input.GetAmount(itemId);

        /// <summary>Raw installed capacity (CU/s) at 100% concentration - not what actually gets credited; see GetResearchAxisProduction/GetBuildingsAxisProduction for that.</summary>
        public float GetTotalComputeOutput() => TotalComputeOutput();
        public float GetTotalPowerDemand() => TotalPowerDemand();

        /// <summary>Σ(share²) of the two axes - TASK_03_DATACENTER.md §7. 1.0 at either extreme (100/0), lowest at an even split.</summary>
        public float GetConcentration()
        {
            float research = ResearchAxisShare;
            float buildings = 1f - research;
            return research * research + buildings * buildings;
        }

        /// <summary>floor + (1-floor) * concentration - the fraction of installed capacity actually produced. floor is DataCenterDefinition.AxisYieldFloor (a parameter, not a buried constant - see there).</summary>
        public float GetYield() => _definition.AxisYieldFloor + (1f - _definition.AxisYieldFloor) * GetConcentration();

        /// <summary>Actual CU/s the research axis currently produces - installed capacity * yield * its own share.</summary>
        public float GetResearchAxisProduction() => TotalComputeOutput() * GetYield() * ResearchAxisShare;

        /// <summary>Actual CU/s the buildings axis currently produces.</summary>
        public float GetBuildingsAxisProduction() => TotalComputeOutput() * GetYield() * (1f - ResearchAxisShare);

        public override void Tick(float deltaTime)
        {
            InstallInto(CpuItemId, _cpuSlots, CpuReplacementThresholdPercent);
            InstallInto(MemoryItemId, _memorySlots, MemoryReplacementThresholdPercent);

            if (IsPriming)
            {
                float wanted = UnityEngine.Mathf.Min(PrimingCostCu - _primingAbsorbedCu, PrimingAbsorptionRatePerSecond * deltaTime);
                _primingAbsorbedCu += _computeSystem.SpendUpTo(wanted);
                _previousPowerDemand = TotalPowerDemand();
                return; // no production, no wear while priming (GDD §2.3)
            }

            // delta already carries the Power gate (0 whenever unpowered, from last frame's
            // reported demand - same one-frame lag as every other Compute/Power report),
            // freezing the wear/stability timers below while unpowered.
            float performance = ComputeEffectivePerformance(_previousPowerDemand, powerActive: true, _powerSystem);
            float effectiveDelta = deltaTime * performance;

            DecayWear(_cpuSlots, effectiveDelta);
            DecayWear(_memorySlots, effectiveDelta);

            ProcessReplacement(effectiveDelta, _cpuSlots, CpuItemId, CpuReplacementThresholdPercent);
            ProcessReplacement(effectiveDelta, _memorySlots, MemoryItemId, MemoryReplacementThresholdPercent);

            _stabilityTimer += effectiveDelta;
            if (_stabilityTimer >= StabilityInterval)
            {
                _stabilityTimer = 0f;
                RecalculateStability(_cpuSlots);
                RecalculateStability(_memorySlots);
            }

            // Compute output is explicitly gated on IsPowered() directly (an instantaneous rate,
            // not something effectiveDelta=0 would zero out on its own) - shutdown must silence
            // it immediately, not just freeze its progression. It is a CU/s rate, so what lands
            // in the reserve is that rate times this tick's own duration. Both axes currently
            // credit the same single reserve (§7) - going through the same public per-axis
            // methods the UI reads keeps this from duplicating the yield calculation.
            if (_powerSystem.IsPowered()) _computeSystem.Grant((GetResearchAxisProduction() + GetBuildingsAxisProduction()) * deltaTime);
            _previousPowerDemand = TotalPowerDemand();
        }

        void InstallInto(string itemId, List<ComponentInstance> slots, float replacementThresholdPercent)
        {
            while (_input.GetAmount(itemId) > 0)
            {
                int slotIndex = slots.FindIndex(s => s == null);
                if (slotIndex == -1) return; // no compatible empty slot - item stays in input (normal jam behavior)

                _input.Take(itemId, 1);
                slots[slotIndex] = new ComponentInstance(itemId, _itemDatabase, _lifetimeRandom, replacementThresholdPercent);
            }
        }

        static void DecayWear(List<ComponentInstance> slots, float deltaTime)
        {
            foreach (ComponentInstance slot in slots) slot?.DecayWear(deltaTime);
        }

        static void RecalculateStability(List<ComponentInstance> slots)
        {
            foreach (ComponentInstance slot in slots) slot?.RecalculatePerformance();
        }

        /// <summary>
        /// Starts/advances/resolves replacement for one slot list. A slot enters replacement the
        /// instant its wear crosses the CURRENT threshold; hard removal at 0% wear takes priority
        /// over completing the timer - a component that decays to 0% before a spare ever arrives
        /// is simply gone.
        /// </summary>
        void ProcessReplacement(float deltaTime, List<ComponentInstance> slots, string itemId, float replacementThresholdPercent)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                ComponentInstance slot = slots[i];
                if (slot == null) continue;

                if (!slot.IsReplacing)
                {
                    if (slot.HasCrossedReplacementThreshold(replacementThresholdPercent))
                    {
                        slot.IsReplacing = true;
                        slot.ReplacementElapsed = 0f;
                    }
                    continue;
                }

                if (slot.Wear <= 0f)
                {
                    slots[i] = null;
                    continue;
                }

                slot.ReplacementElapsed += deltaTime;
                if (slot.ReplacementElapsed < ReplacementDuration) continue;

                if (_input.GetAmount(itemId) > 0)
                {
                    _input.Take(itemId, 1);
                    slots[i] = new ComponentInstance(itemId, _itemDatabase, _lifetimeRandom, replacementThresholdPercent);
                }
                else
                {
                    slots[i] = null; // no spare - slot freed, normal auto-install picks it up later
                }
            }
        }

        float TotalComputeOutput()
        {
            float total = 0f;
            foreach (ComponentInstance slot in _cpuSlots) if (slot != null) total += slot.EffectiveCu();
            foreach (ComponentInstance slot in _memorySlots) if (slot != null) total += slot.EffectiveCu();
            return total;
        }

        float TotalPowerDemand()
        {
            float total = 0f;
            foreach (ComponentInstance slot in _cpuSlots) if (slot != null) total += slot.ActivePowerKw();
            foreach (ComponentInstance slot in _memorySlots) if (slot != null) total += slot.ActivePowerKw();
            return total;
        }

        public override JObject CaptureState()
        {
            return new JObject
            {
                ["stabilityTimer"] = _stabilityTimer,
                ["previousPowerDemand"] = _previousPowerDemand,
                ["primingAbsorbedCu"] = _primingAbsorbedCu,
                ["cpuReplacementThresholdPercent"] = CpuReplacementThresholdPercent,
                ["memoryReplacementThresholdPercent"] = MemoryReplacementThresholdPercent,
                ["researchAxisShare"] = ResearchAxisShare,
                ["input"] = JObject.FromObject(_input.Contents),
                ["cpuSlots"] = CaptureSlots(_cpuSlots),
                ["memorySlots"] = CaptureSlots(_memorySlots)
            };
        }

        static JArray CaptureSlots(List<ComponentInstance> slots)
        {
            var array = new JArray();
            foreach (ComponentInstance slot in slots)
            {
                if (slot == null)
                {
                    array.Add(JValue.CreateNull());
                    continue;
                }

                array.Add(new JObject
                {
                    ["itemId"] = slot.ItemId,
                    ["wear"] = slot.Wear,
                    ["effectivePerformance"] = slot.EffectivePerformance,
                    ["isReplacing"] = slot.IsReplacing,
                    ["replacementElapsed"] = slot.ReplacementElapsed,
                    ["nominalLifetimeSeconds"] = slot.NominalLifetimeSeconds,
                    ["baseLossPerSecond"] = slot.BaseLossPerSecond
                });
            }
            return array;
        }

        /// <summary>
        /// Every key is read with a fallback (CONTRACTS.md §14 / TASK_03_DATACENTER.md §9): a
        /// blob missing a key falls back to a reasonable default instead of throwing, so this
        /// shape can keep growing without breaking an earlier save of the same Version.
        /// </summary>
        public override void RestoreState(JObject state)
        {
            _stabilityTimer = state.Value<float?>("stabilityTimer") ?? 0f;
            _previousPowerDemand = state.Value<float?>("previousPowerDemand") ?? 0f;
            // Absent means this blob predates priming - assume already primed rather than
            // freezing an established playthrough's production on load.
            _primingAbsorbedCu = state.Value<float?>("primingAbsorbedCu") ?? PrimingCostCu;
            CpuReplacementThresholdPercent = state.Value<float?>("cpuReplacementThresholdPercent") ?? DefaultReplacementThresholdPercent;
            MemoryReplacementThresholdPercent = state.Value<float?>("memoryReplacementThresholdPercent") ?? DefaultReplacementThresholdPercent;
            ResearchAxisShare = state.Value<float?>("researchAxisShare") ?? 0.5f;
            _input.RestoreContents(state["input"]?.ToObject<Dictionary<string, int>>());
            RestoreSlots(_cpuSlots, state["cpuSlots"] as JArray, CpuReplacementThresholdPercent);
            RestoreSlots(_memorySlots, state["memorySlots"] as JArray, MemoryReplacementThresholdPercent);
        }

        void RestoreSlots(List<ComponentInstance> slots, JArray saved, float replacementThresholdPercent)
        {
            slots.Clear();
            if (saved == null) return;

            foreach (JToken entry in saved)
            {
                if (entry.Type == JTokenType.Null)
                {
                    slots.Add(null);
                    continue;
                }

                string itemId = entry.Value<string>("itemId");
                float nominalLifetime = entry.Value<float?>("nominalLifetimeSeconds") ?? (_itemDatabase.Get(itemId)?.NominalLifetimeSeconds ?? 120f);
                float baseLoss = entry.Value<float?>("baseLossPerSecond") ?? ComponentInstance.DeriveBaseLossPerSecond(nominalLifetime, replacementThresholdPercent);

                var component = new ComponentInstance(itemId, _itemDatabase, nominalLifetime, baseLoss);
                component.RestoreWearAndPerformance(entry.Value<float?>("wear") ?? 100f, entry.Value<float?>("effectivePerformance") ?? 1f);
                component.IsReplacing = entry.Value<bool?>("isReplacing") ?? false;
                component.ReplacementElapsed = entry.Value<float?>("replacementElapsed") ?? 0f;
                slots.Add(component);
            }
        }
    }
}
