using Game.Core;
using Game.Data;
using Game.Gameplay.Items;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Pooled-inventory building (CONTRACTS.md §3 Building/Inventory contract). Accepts input
    /// from any adjacent direction - it has no belt orientation of its own, unlike a conveyor.
    /// </summary>
    public sealed class StorageRuntime : BuildingRuntime
    {
        readonly StorageDefinition _definition;
        readonly Inventory _inventory;

        /// <summary>
        /// Time remaining before another delivery can be accepted (StorageDefinition.
        /// IntakeIntervalSeconds) - caps absorption at the fastest conveyor's throughput so a
        /// Storage placed straight against a production building's output can't drain it faster
        /// than a belt ever could.
        /// </summary>
        float _intakeCooldown;

        /// <summary>Public read contract for UI (e.g. the Storage panel) to enumerate contents by slot.</summary>
        public System.Collections.Generic.IReadOnlyList<InventorySlot> Slots => _inventory.Slots;

        public StorageRuntime(StorageDefinition definition, GridCoord cell, Direction facingRotation)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            int slotCount = definition.SlotCountOverride > 0 ? definition.SlotCountOverride : Inventory.DefaultSlotCount;
            int capacityPerSlot = definition.CapacityPerSlotOverride > 0 ? definition.CapacityPerSlotOverride : Inventory.DefaultCapacityPerSlot;
            _inventory = new Inventory(slotCount, capacityPerSlot);
        }

        /// <summary>
        /// Seeds contents directly, bypassing the intake cooldown - for world-generation fixtures
        /// (the Core's starting-resources box) initialized once before the game actually starts,
        /// not a real delivery that should count against the absorption rate.
        /// </summary>
        public void SeedInitialContents(string itemId, int amount) => _inventory.Add(itemId, amount);

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (_intakeCooldown > 0f) return false;
            return _inventory.CanAccept(itemId, amount);
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection)
        {
            _intakeCooldown = _definition.IntakeIntervalSeconds;
            _inventory.Add(itemId, amount);
        }

        public override void Tick(float deltaTime)
        {
            if (_intakeCooldown > 0f) _intakeCooldown -= deltaTime;
        }

        public override int TakeInput(string itemId, int amount)
        {
            return _inventory.Take(itemId, amount);
        }

        public override int GetInputAmount(string itemId)
        {
            return _inventory.GetAmount(itemId);
        }

        public override JObject CaptureState()
        {
            var slots = new JArray();
            foreach (InventorySlot slot in _inventory.Slots)
            {
                if (slot.IsEmpty) continue;
                slots.Add(new JObject { ["itemId"] = slot.ItemId, ["amount"] = slot.Amount });
            }
            return new JObject { ["slots"] = slots, ["intakeCooldown"] = _intakeCooldown };
        }

        public override void RestoreState(JObject state)
        {
            _intakeCooldown = state.Value<float?>("intakeCooldown") ?? 0f;

            if (!(state["slots"] is JArray slots)) return;
            foreach (JToken entry in slots)
            {
                string itemId = entry.Value<string>("itemId");
                int amount = entry.Value<int?>("amount") ?? 0;
                if (!string.IsNullOrEmpty(itemId) && amount > 0) _inventory.Add(itemId, amount);
            }
        }
    }
}
