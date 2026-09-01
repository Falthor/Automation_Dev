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

        public override bool CanAcceptInput(OreType itemType, int amount, Direction fromDirection)
        {
            return _inventory.CanAccept(itemType, amount);
        }

        public override void AddInput(OreType itemType, int amount, Direction fromDirection)
        {
            _inventory.Add(itemType, amount);
        }

        public override int TakeInput(OreType itemType, int amount)
        {
            return _inventory.Take(itemType, amount);
        }

        public override int GetInputAmount(OreType itemType)
        {
            return _inventory.GetAmount(itemType);
        }
    }
}
