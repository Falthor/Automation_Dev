using System.Collections.Generic;

namespace Game.Gameplay.Items
{
    /// <summary>One inventory slot: empty, or up to the owning Inventory's CapacityPerSlot of a single item id.</summary>
    public struct InventorySlot
    {
        public string ItemId;
        public int Amount;
        public bool IsEmpty => Amount <= 0;
    }

    /// <summary>
    /// Slot-based storage: a fixed number of slots, each holding up to CapacityPerSlot of a
    /// single item type - so at most SlotCount distinct item types can be held at once. Runtime
    /// mutable state - owned by a building runtime (e.g. StorageRuntime), never by a
    /// ScriptableObject. The public surface (GetAmount/CanAccept/Add/Take) still matches
    /// CONTRACTS.md §3's pooled-inventory shape; slots are an internal storage strategy, not a
    /// new contract.
    ///
    /// SlotCount/CapacityPerSlot are per-instance (constructor parameters), not fixed constants:
    /// the standard Storage Box uses DefaultSlotCount/DefaultCapacityPerSlot, but a special
    /// fixture (e.g. the Core's starting-resources box) may need a different shape - see
    /// StorageDefinition's slotCountOverride/capacityPerSlotOverride.
    /// </summary>
    public sealed class Inventory
    {
        public const int DefaultSlotCount = 2;
        public const int DefaultCapacityPerSlot = 100;

        public int SlotCount { get; }
        public int CapacityPerSlot { get; }

        readonly InventorySlot[] _slots;

        public Inventory(int slotCount = DefaultSlotCount, int capacityPerSlot = DefaultCapacityPerSlot)
        {
            SlotCount = slotCount;
            CapacityPerSlot = capacityPerSlot;
            _slots = new InventorySlot[slotCount];
        }

        /// <summary>Read-only view of the slots for UI (CONTRACTS.md §12: UI reads the public contract, never a private field).</summary>
        public IReadOnlyList<InventorySlot> Slots => _slots;

        public int GetAmount(string itemId)
        {
            int total = 0;
            foreach (var slot in _slots)
            {
                if (!slot.IsEmpty && slot.ItemId == itemId) total += slot.Amount;
            }
            return total;
        }

        public bool CanAccept(string itemId, int amount)
        {
            return amount > 0 && RemainingCapacityFor(itemId) >= amount;
        }

        public void Add(string itemId, int amount)
        {
            int remaining = amount;

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i].IsEmpty || _slots[i].ItemId != itemId) continue;

                int room = CapacityPerSlot - _slots[i].Amount;
                int add = System.Math.Min(room, remaining);
                _slots[i].Amount += add;
                remaining -= add;
            }

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty) continue;

                int add = System.Math.Min(CapacityPerSlot, remaining);
                _slots[i].ItemId = itemId;
                _slots[i].Amount = add;
                remaining -= add;
            }
        }

        public int Take(string itemId, int amount)
        {
            int taken = 0;

            for (int i = 0; i < _slots.Length && taken < amount; i++)
            {
                if (_slots[i].IsEmpty || _slots[i].ItemId != itemId) continue;

                int take = System.Math.Min(_slots[i].Amount, amount - taken);
                _slots[i].Amount -= take;
                taken += take;
            }

            return taken;
        }

        /// <summary>
        /// Empties one slot outright, destroying what was in it. Answers what it discarded.
        ///
        /// By index, not by item id: this serves the player pointing at one square of a box, and two
        /// squares can hold the same item. Take() would have emptied whichever it found first.
        ///
        /// The only path in the project that makes items cease to exist on purpose - everything else
        /// moves them. It exists because a box holding something no longer wanted is otherwise stuck
        /// with it forever: nothing consumes a stack the player has no use for.
        /// </summary>
        public int ClearSlot(int index)
        {
            if (index < 0 || index >= _slots.Length) return 0;

            int discarded = _slots[index].Amount;
            _slots[index].ItemId = null;
            _slots[index].Amount = 0;
            return discarded;
        }

        int RemainingCapacityFor(string itemId)
        {
            int capacity = 0;
            foreach (var slot in _slots)
            {
                if (slot.IsEmpty) capacity += CapacityPerSlot;
                else if (slot.ItemId == itemId) capacity += CapacityPerSlot - slot.Amount;
            }
            return capacity;
        }
    }
}
