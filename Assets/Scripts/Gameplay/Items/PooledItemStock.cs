using System.Collections.Generic;

namespace Game.Gameplay.Items
{
    /// <summary>
    /// Pooled per-item stock for production buildings: unlimited distinct item ids, each capped
    /// independently at MaxStackPerItem. Distinct from the slot-based Inventory (StorageRuntime's
    /// "8 distinct types x100" model) - CONTRACTS.md §3's Building/Inventory contract has no
    /// distinct-type cap, only a per-item amount cap, which is exactly what production buildings
    /// (input and output side alike) need.
    /// </summary>
    public sealed class PooledItemStock
    {
        readonly Dictionary<string, int> _amounts = new Dictionary<string, int>();

        public int MaxStackPerItem { get; }

        public PooledItemStock(int maxStackPerItem)
        {
            MaxStackPerItem = maxStackPerItem;
        }

        /// <summary>Read-only snapshot for the generic transport push step (BuildingRuntime.GetOutputContents()).</summary>
        public IReadOnlyDictionary<string, int> Contents => _amounts;

        public int GetAmount(string itemId) => _amounts.TryGetValue(itemId, out int amount) ? amount : 0;

        public bool CanAccept(string itemId, int amount) => amount > 0 && GetAmount(itemId) + amount <= MaxStackPerItem;

        public void Add(string itemId, int amount)
        {
            _amounts[itemId] = GetAmount(itemId) + amount;
        }

        /// <summary>Removes up to amount, returning what was actually taken (may be less than requested).</summary>
        public int Take(string itemId, int amount)
        {
            int current = GetAmount(itemId);
            int taken = System.Math.Min(current, amount);
            int remaining = current - taken;
            if (remaining <= 0) _amounts.Remove(itemId);
            else _amounts[itemId] = remaining;
            return taken;
        }

        /// <summary>Replaces all contents wholesale. Used only by the save/load system (CONTRACTS.md §14) to restore a previously-captured snapshot - never by gameplay code.</summary>
        public void RestoreContents(IReadOnlyDictionary<string, int> contents)
        {
            _amounts.Clear();
            if (contents == null) return;
            foreach (var kvp in contents)
            {
                if (kvp.Value > 0) _amounts[kvp.Key] = kvp.Value;
            }
        }
    }
}
