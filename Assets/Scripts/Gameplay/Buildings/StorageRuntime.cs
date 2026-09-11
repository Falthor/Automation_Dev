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

        /// <summary>Which slot the next hand-out starts looking from - see <see cref="PeekPullableItem"/>. Not saved: where a chest was in its rotation is not worth a save-format change, and a restored chest simply starts again from its first slot.</summary>
        int _handOutCursor;

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

        /// <summary>
        /// What a belt leading away from this chest may take: one unit, from the slot the rotation
        /// is currently on.
        ///
        /// <b>A chest empties, and this is the whole of how.</b> It used to expose nothing at all,
        /// so a belt pointing away from a box was never fed and a full box could only be emptied by
        /// hand, slot by slot.
        ///
        /// <b>The cursor moves on after every unit taken</b>, so a chest holding plates, gears and
        /// wire hands out one of each in turn rather than emptying its first slot and only then
        /// starting on the second. The same rotation is what shares one chest between several
        /// belts - the belts take turns in TransportSystem, and the items take turns here.
        ///
        /// <b>The Core chest hands out nothing</b>, for the same reason it accepts nothing from a
        /// belt (<c>StorageDefinition.RejectsConveyorInput</c>): it is the construction reserve, and
        /// a conveyor may not connect to it in either direction. A builder robot still takes from it
        /// through <c>TakeInput</c>, which is a different path entirely.
        /// </summary>
        public override object PeekPullableItem()
        {
            if (_definition.RejectsConveyorInput) return null;

            System.Collections.Generic.IReadOnlyList<InventorySlot> slots = _inventory.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlot slot = slots[(_handOutCursor + i) % slots.Count];
                if (!slot.IsEmpty) return slot.ItemId;
            }

            return null;
        }

        public override void ConsumePulledItem(object item)
        {
            if (!(item is string itemId)) return;

            _inventory.Take(itemId, 1);
            if (_inventory.SlotCount > 0) _handOutCursor = (_handOutCursor + 1) % _inventory.SlotCount;
        }

        /// <summary>
        /// A chest hands out on <b>every</b> side it is touched on, because it has no output side to
        /// declare - the same reason <c>HandsOutTo</c> already answered true for all four, and the
        /// same rule the entry side has always followed on the way in.
        ///
        /// The base answer is one edge, derived from <c>ExitDirection</c>, which on a chest is
        /// nothing but the rotation it happened to be placed with: a belt leaving its north side was
        /// fed or not depending on which way a box the player never rotates was facing.
        ///
        /// Computed arithmetically rather than by walking <c>GetEdgeCells()</c>: this is asked of
        /// every chest by every belt touching it on every tick, and that helper builds an array each
        /// time it is called.
        /// </summary>
        public override bool FeedsCell(GridCoord cell)
        {
            UnityEngine.Vector2Int size = Definition.FootprintSize;
            bool alignedX = cell.X >= Cell.X && cell.X < Cell.X + size.x;
            bool alignedY = cell.Y >= Cell.Y && cell.Y < Cell.Y + size.y;

            if (alignedX && (cell.Y == Cell.Y - 1 || cell.Y == Cell.Y + size.y)) return true;
            return alignedY && (cell.X == Cell.X - 1 || cell.X == Cell.X + size.x);
        }

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
