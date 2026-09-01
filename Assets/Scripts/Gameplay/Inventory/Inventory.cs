using System.Collections.Generic;
using Game.Data;

namespace Game.Gameplay.Items
{
    /// <summary>One inventory slot: empty, or up to CapacityPerSlot of a single item type.</summary>
    public struct InventorySlot
    {
        public OreType ItemType;
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
    /// </summary>
    public sealed class Inventory
    {
        public const int SlotCount = 8;
        public const int CapacityPerSlot = 100;

        readonly InventorySlot[] _slots = new InventorySlot[SlotCount];

        /// <summary>Read-only view of the slots for UI (CONTRACTS.md §12: UI reads the public contract, never a private field).</summary>
        public IReadOnlyList<InventorySlot> Slots => _slots;

        public int GetAmount(OreType itemType)
        {
            int total = 0;
            foreach (var slot in _slots)
            {
                if (!slot.IsEmpty && slot.ItemType == itemType) total += slot.Amount;
            }
            return total;
        }

        public bool CanAccept(OreType itemType, int amount)
        {
            return amount > 0 && RemainingCapacityFor(itemType) >= amount;
        }

        public void Add(OreType itemType, int amount)
        {
            int remaining = amount;

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i].IsEmpty || _slots[i].ItemType != itemType) continue;

                int room = CapacityPerSlot - _slots[i].Amount;
                int add = System.Math.Min(room, remaining);
                _slots[i].Amount += add;
                remaining -= add;
            }

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty) continue;

                int add = System.Math.Min(CapacityPerSlot, remaining);
                _slots[i].ItemType = itemType;
                _slots[i].Amount = add;
                remaining -= add;
            }
        }

        public int Take(OreType itemType, int amount)
        {
            int taken = 0;

            for (int i = 0; i < _slots.Length && taken < amount; i++)
            {
                if (_slots[i].IsEmpty || _slots[i].ItemType != itemType) continue;

                int take = System.Math.Min(_slots[i].Amount, amount - taken);
                _slots[i].Amount -= take;
                taken += take;
            }

            return taken;
        }

        int RemainingCapacityFor(OreType itemType)
        {
            int capacity = 0;
            foreach (var slot in _slots)
            {
                if (slot.IsEmpty) capacity += CapacityPerSlot;
                else if (slot.ItemType == itemType) capacity += CapacityPerSlot - slot.Amount;
            }
            return capacity;
        }
    }
}
