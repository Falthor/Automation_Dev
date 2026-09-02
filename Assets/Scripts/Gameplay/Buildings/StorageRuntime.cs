using Game.Core;
using Game.Data;
using Game.Gameplay.Items;

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
    }
}
