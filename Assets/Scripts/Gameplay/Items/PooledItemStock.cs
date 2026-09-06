using System.Collections.Generic;

namespace Game.Gameplay.Items
{
    /// <summary>
    /// Pooled per-item stock for production buildings: unlimited distinct item ids, each capped
    /// independently. Distinct from the slot-based Inventory (StorageRuntime's "8 distinct types
    /// x100" model) - CONTRACTS.md §3's Building/Inventory contract has no distinct-type cap, only
    /// a per-item amount cap, which is exactly what production buildings (input and output side
    /// alike) need.
    ///
    /// The cap is asked for per item rather than held as one number, because a production
    /// building's input holds a few crafts' worth of <b>each</b> ingredient: a recipe taking 2 iron
    /// and 4 screws does not want the same ceiling on both. It is also asked live rather than
    /// captured, so changing recipe moves the ceilings with it.
    /// </summary>
    public sealed class PooledItemStock
    {
        readonly Dictionary<string, int> _amounts = new Dictionary<string, int>();
        readonly System.Func<string, int> _capacityFor;

        /// <summary>One ceiling for every item - the Data Center's model, and any stock that does not vary by item.</summary>
        public PooledItemStock(int maxStackPerItem) : this(_ => maxStackPerItem)
        {
        }

        /// <summary>A ceiling that depends on the item, and may change as the building is reconfigured.</summary>
        public PooledItemStock(System.Func<string, int> capacityFor)
        {
            _capacityFor = capacityFor;
        }

        /// <summary>How much of this item may be held at most, right now. 0 means the stock does not take it at all.</summary>
        public int CapacityFor(string itemId) => _capacityFor(itemId);

        /// <summary>Read-only snapshot for the generic transport push step (BuildingRuntime.GetOutputContents()).</summary>
        public IReadOnlyDictionary<string, int> Contents => _amounts;

        public int GetAmount(string itemId) => _amounts.TryGetValue(itemId, out int amount) ? amount : 0;

        public bool CanAccept(string itemId, int amount) => amount > 0 && GetAmount(itemId) + amount <= CapacityFor(itemId);

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
