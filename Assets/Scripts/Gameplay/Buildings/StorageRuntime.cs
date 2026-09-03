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
        readonly Inventory _inventory;

        /// <summary>Public read contract for UI (e.g. the Storage panel) to enumerate contents by slot.</summary>
        public System.Collections.Generic.IReadOnlyList<InventorySlot> Slots => _inventory.Slots;

        public StorageRuntime(StorageDefinition definition, GridCoord cell, Direction facingRotation)
            : base(definition, cell, facingRotation)
        {
            _inventory = new Inventory();
        }

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            return _inventory.CanAccept(itemId, amount);
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection)
        {
            _inventory.Add(itemId, amount);
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
            return new JObject { ["slots"] = slots };
        }

        public override void RestoreState(JObject state)
        {
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
