using System.Collections.Generic;
using Game.Data;

namespace Game.Gameplay.Items
{
    /// <summary>
    /// Pooled per-item-type storage (totals only, no slots/stacking). Runtime mutable state -
    /// owned by a building runtime (e.g. StorageRuntime), never by a ScriptableObject.
    /// </summary>
    public sealed class Inventory
    {
        readonly Dictionary<OreType, int> _amounts = new Dictionary<OreType, int>();

        public int CapacityPerItem { get; }

        public IReadOnlyDictionary<OreType, int> Amounts => _amounts;

        public Inventory(int capacityPerItem)
        {
            CapacityPerItem = capacityPerItem;
        }

        public int GetAmount(OreType itemType)
        {
            return _amounts.TryGetValue(itemType, out int amount) ? amount : 0;
        }

        public bool CanAccept(OreType itemType, int amount)
        {
            return amount > 0 && GetAmount(itemType) + amount <= CapacityPerItem;
        }

        public void Add(OreType itemType, int amount)
        {
            _amounts[itemType] = GetAmount(itemType) + amount;
        }

        public int Take(OreType itemType, int amount)
        {
            int available = GetAmount(itemType);
            int taken = System.Math.Min(available, amount);
            if (taken > 0)
            {
                _amounts[itemType] = available - taken;
            }

            return taken;
        }
    }
}
