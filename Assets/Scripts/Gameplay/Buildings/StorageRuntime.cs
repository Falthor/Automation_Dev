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

        /// <summary>Seeds contents directly - for world-generation fixtures (the Core's reserve) initialized once before the game actually starts.</summary>
        public void SeedInitialContents(string itemId, int amount) => _inventory.Add(itemId, amount);

        /// <summary>
        /// Throws away one slot's contents at the player's request, and answers how much was
        /// destroyed. Nothing else in the project destroys items - see <see cref="Inventory.ClearSlot"/>
        /// for why a box needs to be able to.
        /// </summary>
        public int DiscardSlot(int index) => _inventory.ClearSlot(index);

        /// <summary>
        /// Takes whatever arrives, the instant it arrives. A Storage has no absorption rate, unlike
        /// a production building: it is a container, not a machine with a cycle, and the only thing
        /// that can stop it accepting is being full.
        ///
        /// It used to carry the same intake cooldown a Foundry has, to cap how fast a box parked
        /// against an output could drain it. That made a box slower than the belt feeding it, so
        /// items backed up in front of a container that was visibly empty.
        /// </summary>
        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (_definition.RejectsConveyorInput) return false;
            return _inventory.CanAccept(itemId, amount);
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection)
        {
            _inventory.Add(itemId, amount);
        }

        /// <summary>
        /// A builder robot's delivery/repatriation (Game.Gameplay.Sites.ConstructionSiteSystem) is
        /// a distinct path from the transport contract above: it bypasses both the intake cooldown
        /// (a conveyor-throughput throttle that has no bearing on a robot's much slower delivery
        /// cadence) and RejectsConveyorInput (which exists specifically to keep a conveyor out, not
        /// a robot). Only real slot capacity gates a robot's delivery.
        /// </summary>
        public bool CanAcceptFromRobot(string itemId, int amount) => _inventory.CanAccept(itemId, amount);

        public void AddFromRobot(string itemId, int amount) => _inventory.Add(itemId, amount);

        /// <summary>Nothing to advance: a container has no cycle. Kept because the base declares it.</summary>
        public override void Tick(float deltaTime)
        {
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
            // No intake cooldown any more, so none is written. An older save carrying one is simply
            // ignored on the way back in - per-field tolerance, CONTRACTS.md §14, which is why the
            // format version does not move for this.
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
