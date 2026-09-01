using Game.Core;
using Game.Data;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Base runtime state for a placed building. Implements the Building/Flow contract
    /// (CONTRACTS.md) with neutral defaults; only flow-participating buildings override them.
    /// </summary>
    public class BuildingRuntime
    {
        public BuildingDefinition Definition { get; }
        public GridCoord Cell { get; internal set; }
        public Direction FacingRotation { get; protected set; }

        public BuildingRuntime(BuildingDefinition definition, GridCoord cell, Direction facingRotation)
        {
            Definition = definition;
            Cell = cell;
            FacingRotation = facingRotation;
        }

        /// <summary>Returns the item currently available for transfer, or null when none is available.</summary>
        public virtual object PeekPullableItem() => null;

        /// <summary>Consumes the item previously exposed by PeekPullableItem().</summary>
        public virtual void ConsumePulledItem(object item)
        {
        }

        /// <summary>Whether this building participates in directional flow. False by default.</summary>
        public virtual bool IsFlowReceiver() => false;

        /// <summary>
        /// The true direction items leave from. Equal to FacingRotation for every building
        /// except a conveyor corner, where the visual rotation and the geometric exit are not
        /// the same value (see ConveyorRuntime.ExitDirection) - override there, not here.
        /// </summary>
        public virtual Direction ExitDirection => FacingRotation;

        /// <summary>
        /// The single cell this building hands its output to, accounting for footprint size
        /// (e.g. a 2x2 building facing North outputs one row past its top edge, aligned to its
        /// origin column). For a 1x1 footprint this is exactly Cell + ExitDirection.
        /// </summary>
        public GridCoord GetOutputCell()
        {
            UnityEngine.Vector2Int size = Definition.FootprintSize;
            switch (ExitDirection)
            {
                case Direction.North: return new GridCoord(Cell.X, Cell.Y + size.y);
                case Direction.East: return new GridCoord(Cell.X + size.x, Cell.Y);
                case Direction.South: return new GridCoord(Cell.X, Cell.Y - 1);
                default: return new GridCoord(Cell.X - 1, Cell.Y); // West
            }
        }

        // Building/Inventory contract (CONTRACTS.md §3), for non-belt buildings. Neutral
        // defaults here; only pooled-inventory buildings (e.g. StorageRuntime) override them.
        // Must not be mixed with the belt lane model (the Flow contract above).

        /// <summary>Checks whether input can be received.</summary>
        public virtual bool CanAcceptInput(OreType itemType, int amount, Direction fromDirection) => false;

        /// <summary>Adds accepted input.</summary>
        public virtual void AddInput(OreType itemType, int amount, Direction fromDirection)
        {
        }

        /// <summary>Consumes input. Returns the amount actually taken.</summary>
        public virtual int TakeInput(OreType itemType, int amount) => 0;

        /// <summary>Adds produced output.</summary>
        public virtual void AddOutput(OreType itemType, int amount)
        {
        }

        /// <summary>Removes output. Returns the amount actually taken.</summary>
        public virtual int TakeOutput(OreType itemType, int amount) => 0;

        /// <summary>Reads current input quantity.</summary>
        public virtual int GetInputAmount(OreType itemType) => 0;
    }
}
