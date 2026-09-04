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
    /// Aggregates installed CPU/Memory components into Compute supply and Power demand. Pooled
    /// input accepts cpu_mkI/Memory_MK1 via the standard Building/Inventory contract. Direct
    /// translation of the source project's data_center.gd - see ComponentInstance for the
    /// per-slot wear/stability/replacement rules.
    /// </summary>
    public sealed class DataCenterRuntime : BuildingRuntime
    {
        const int InitialCpuSlots = 4;
        const int InitialMemorySlots = 4;
        const float StabilityInterval = 5f;
        const float WearInterval = 30f;
        const float ReplacementDuration = 5f;
        const string ExtraCpuSlotResearchId = "extra_cpu_slot";
        const string CpuItemId = "cpu_mkI";
        const string MemoryItemId = "Memory_MK1";

        readonly DataCenterDefinition _definition;
        readonly ItemDatabase _itemDatabase;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;
        readonly PooledItemStock _input;
        readonly List<ComponentInstance> _cpuSlots;
        readonly List<ComponentInstance> _memorySlots;
        readonly System.Action<string> _onResearchCompleted;

        float _stabilityTimer;
        float _wearTimer;
        float _previousPowerDemand;

        public IReadOnlyList<ComponentInstance> CpuSlots => _cpuSlots;
        public IReadOnlyList<ComponentInstance> MemorySlots => _memorySlots;

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

            _cpuSlots = new List<ComponentInstance>(new ComponentInstance[InitialCpuSlots]);
            _memorySlots = new List<ComponentInstance>(new ComponentInstance[InitialMemorySlots]);
            if (researchSystem.IsUnlocked(ExtraCpuSlotResearchId)) _cpuSlots.Add(null);

            _onResearchCompleted = OnResearchCompleted;
            researchSystem.ResearchCompleted += _onResearchCompleted;
        }

        /// <summary>A completed "extra_cpu_slot" research appends one more empty CPU slot, usable by the same install/wear/replacement code - no separate mechanism. Memory capacity is never touched.</summary>
        void OnResearchCompleted(string researchId)
        {
            if (researchId == ExtraCpuSlotResearchId) _cpuSlots.Add(null);
        }

        /// <summary>Unsubscribes from ResearchSystem so a demolished Data Center doesn't keep growing slots forever.</summary>
        public override void OnUnregistered()
        {
            _researchSystem.ResearchCompleted -= _onResearchCompleted;
        }

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (fromDirection == ExitDirection) return false;
            if (System.Array.IndexOf(_definition.AcceptedItemIds, itemId) < 0) return false;
            return _input.CanAccept(itemId, amount);
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection) => _input.Add(itemId, amount);
        public override int GetInputAmount(string itemId) => _input.GetAmount(itemId);

        public float GetTotalComputeOutput() => TotalComputeOutput();
        public float GetTotalPowerDemand() => TotalPowerDemand();

        public override void Tick(float deltaTime)
        {
            InstallInto(CpuItemId, _cpuSlots);
            InstallInto(MemoryItemId, _memorySlots);

            // delta already carries the Power gate (0 whenever unpowered, from last frame's
            // reported demand - same one-frame lag as every other Compute/Power report),
            // freezing the wear/stability timers below while unpowered.
            float performance = ComputeEffectivePerformance(_previousPowerDemand, powerActive: true, _powerSystem);
            float effectiveDelta = deltaTime * performance;

            _wearTimer += effectiveDelta;
            if (_wearTimer >= WearInterval)
            {
                _wearTimer = 0f;
                DecayWear(_cpuSlots);
                DecayWear(_memorySlots);
            }

            ProcessReplacement(effectiveDelta, _cpuSlots, CpuItemId);
            ProcessReplacement(effectiveDelta, _memorySlots, MemoryItemId);

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
            // in the reserve is that rate times this tick's own duration.
            if (_powerSystem.IsPowered()) _computeSystem.Grant(TotalComputeOutput() * deltaTime);
            _previousPowerDemand = TotalPowerDemand();
        }

        void InstallInto(string itemId, List<ComponentInstance> slots)
        {
            while (_input.GetAmount(itemId) > 0)
            {
                int slotIndex = slots.FindIndex(s => s == null);
                if (slotIndex == -1) return; // no compatible empty slot - item stays in input (normal jam behavior)

                _input.Take(itemId, 1);
                slots[slotIndex] = new ComponentInstance(itemId, _itemDatabase);
            }
        }

        static void DecayWear(List<ComponentInstance> slots)
        {
            foreach (ComponentInstance slot in slots) slot?.DecayWear();
        }

        static void RecalculateStability(List<ComponentInstance> slots)
        {
            foreach (ComponentInstance slot in slots) slot?.RecalculatePerformance();
        }

        /// <summary>
        /// Starts/advances/resolves replacement for one slot list. A slot enters replacement the
        /// instant its wear crosses the threshold; hard removal at 0% wear takes priority over
        /// completing the timer - a component that decays to 0% before a spare ever arrives is
        /// simply gone.
        /// </summary>
        void ProcessReplacement(float deltaTime, List<ComponentInstance> slots, string itemId)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                ComponentInstance slot = slots[i];
                if (slot == null) continue;

                if (!slot.IsReplacing)
                {
                    if (slot.HasCrossedReplacementThreshold)
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
                    slots[i] = new ComponentInstance(itemId, _itemDatabase);
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
                ["wearTimer"] = _wearTimer,
                ["previousPowerDemand"] = _previousPowerDemand,
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
                    ["replacementElapsed"] = slot.ReplacementElapsed
                });
            }
            return array;
        }

        public override void RestoreState(JObject state)
        {
            _stabilityTimer = state.Value<float?>("stabilityTimer") ?? 0f;
            _wearTimer = state.Value<float?>("wearTimer") ?? 0f;
            _previousPowerDemand = state.Value<float?>("previousPowerDemand") ?? 0f;
            _input.RestoreContents(state["input"]?.ToObject<Dictionary<string, int>>());
            RestoreSlots(_cpuSlots, state["cpuSlots"] as JArray);
            RestoreSlots(_memorySlots, state["memorySlots"] as JArray);
        }

        void RestoreSlots(List<ComponentInstance> slots, JArray saved)
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
                var component = new ComponentInstance(itemId, _itemDatabase);
                component.RestoreWearAndPerformance(entry.Value<float?>("wear") ?? 100f, entry.Value<float?>("effectivePerformance") ?? 1f);
                component.IsReplacing = entry.Value<bool?>("isReplacing") ?? false;
                component.ReplacementElapsed = entry.Value<float?>("replacementElapsed") ?? 0f;
                slots.Add(component);
            }
        }
    }
}
