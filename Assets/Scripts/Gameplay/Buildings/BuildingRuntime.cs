using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Base runtime state for a placed building. Implements the Building/Flow and
    /// Building/Inventory contracts (CONTRACTS.md §2/§3) with neutral defaults; only
    /// participating buildings override them.
    /// </summary>
    public class BuildingRuntime
    {
        static readonly IReadOnlyDictionary<string, int> EmptyContents = new Dictionary<string, int>();
        static readonly Direction[] AllDirections = { Direction.North, Direction.East, Direction.South, Direction.West };

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
        /// Interval (seconds) between this building's own generic push/pull attempts
        /// (TransportSystem's generic step, distinct from Extractor/Conveyor's own bespoke
        /// ticking). 1 second by default, matching the source project's push_interval.
        /// </summary>
        public virtual float PushIntervalSeconds => 1f;

        /// <summary>
        /// The single cell this building hands its output to for a 1-wide output edge. Equal to
        /// GetOutputCells()[0]; kept as a convenience for callers (ghost preview, transport pull
        /// alignment) that only ever deal with a 1-cell-wide edge.
        /// </summary>
        public GridCoord GetOutputCell() => GetOutputCells()[0];

        /// <summary>
        /// Every cell immediately outside this building along its output edge - width/height
        /// aware, so a multi-cell-wide edge (e.g. a 3-wide footprint facing North) returns every
        /// cell across that edge, not just one.
        /// </summary>
        public GridCoord[] GetOutputCells() => ComputeOutputCells(Cell, Definition.FootprintSize, ExitDirection);

        /// <summary>
        /// Every cell touching this building's footprint on any of its 4 sides, paired with the
        /// direction (from this building's own perspective) that side is on. Used by the generic
        /// transport pull step to check every adjacent neighbor, not just the output edge.
        /// </summary>
        public (GridCoord cell, Direction fromMySide)[] GetEdgeCells() => ComputeEdgeCells(Cell, Definition.FootprintSize);

        /// <summary>
        /// The cells this building actually takes input from - exactly the cells its input arrows
        /// are drawn on, so what the player sees is what transport does. A building declaring
        /// directional input (HasInputArrows) accepts on one cell per side other than its output
        /// side, and nowhere else; one declaring none (Storage, Core) keeps accepting on every
        /// edge cell, matching the "input from any side" behavior it is defined with.
        /// </summary>
        public (GridCoord cell, Direction fromMySide)[] GetInputCells()
        {
            return Definition.HasInputArrows
                ? ComputeInputCells(Cell, Definition.FootprintSize, ExitDirection)
                : GetEdgeCells();
        }

        /// <summary>
        /// Footprint-aware output cells for a given cell/footprint/exit direction, with no
        /// BuildingRuntime instance required - lets the construction ghost preview show the same
        /// output arrow(s) a building will have once actually placed, before it exists.
        /// </summary>
        public static GridCoord[] ComputeOutputCells(GridCoord cell, UnityEngine.Vector2Int footprintSize, Direction exitDirection)
        {
            switch (exitDirection)
            {
                case Direction.North:
                {
                    var cells = new GridCoord[footprintSize.x];
                    for (int x = 0; x < footprintSize.x; x++) cells[x] = new GridCoord(cell.X + x, cell.Y + footprintSize.y);
                    return cells;
                }
                case Direction.South:
                {
                    var cells = new GridCoord[footprintSize.x];
                    for (int x = 0; x < footprintSize.x; x++) cells[x] = new GridCoord(cell.X + x, cell.Y - 1);
                    return cells;
                }
                case Direction.East:
                {
                    var cells = new GridCoord[footprintSize.y];
                    for (int y = 0; y < footprintSize.y; y++) cells[y] = new GridCoord(cell.X + footprintSize.x, cell.Y + y);
                    return cells;
                }
                default: // West
                {
                    var cells = new GridCoord[footprintSize.y];
                    for (int y = 0; y < footprintSize.y; y++) cells[y] = new GridCoord(cell.X - 1, cell.Y + y);
                    return cells;
                }
            }
        }

        /// <summary>
        /// The one input cell per side (other than the output side) a directional-input building
        /// accepts on, without a BuildingRuntime instance - so the construction ghost draws its
        /// entry arrows on exactly the cells the placed building will read from.
        /// </summary>
        public static (GridCoord cell, Direction fromMySide)[] ComputeInputCells(GridCoord cell, UnityEngine.Vector2Int footprintSize, Direction exitDirection)
        {
            var result = new List<(GridCoord, Direction)>(3);
            var sides = new HashSet<Direction>();
            foreach ((GridCoord edgeCell, Direction fromMySide) in ComputeEdgeCells(cell, footprintSize))
            {
                if (fromMySide == exitDirection) continue;
                if (!sides.Add(fromMySide)) continue;
                result.Add((edgeCell, fromMySide));
            }
            return result.ToArray();
        }

        /// <summary>Footprint-aware version of GetEdgeCells(), usable without a BuildingRuntime instance (e.g. ghost preview).</summary>
        public static (GridCoord cell, Direction fromMySide)[] ComputeEdgeCells(GridCoord cell, UnityEngine.Vector2Int footprintSize)
        {
            var result = new List<(GridCoord, Direction)>(2 * (footprintSize.x + footprintSize.y));
            for (int x = 0; x < footprintSize.x; x++)
            {
                result.Add((new GridCoord(cell.X + x, cell.Y + footprintSize.y), Direction.North));
                result.Add((new GridCoord(cell.X + x, cell.Y - 1), Direction.South));
            }
            for (int y = 0; y < footprintSize.y; y++)
            {
                result.Add((new GridCoord(cell.X + footprintSize.x, cell.Y + y), Direction.East));
                result.Add((new GridCoord(cell.X - 1, cell.Y + y), Direction.West));
            }
            return result.ToArray();
        }

        // Building/Inventory contract (CONTRACTS.md §3), for non-belt buildings. Neutral
        // defaults here; only pooled-inventory buildings (e.g. StorageRuntime,
        // ProductionBuildingRuntime) override them. Must not be mixed with the belt lane model
        // (the Flow contract above). itemId is the fixed string key from Game.Data.ItemDatabase.

        /// <summary>Checks whether input can be received.</summary>
        public virtual bool CanAcceptInput(string itemId, int amount, Direction fromDirection) => false;

        /// <summary>Adds accepted input.</summary>
        public virtual void AddInput(string itemId, int amount, Direction fromDirection)
        {
        }

        /// <summary>Consumes input. Returns the amount actually taken.</summary>
        public virtual int TakeInput(string itemId, int amount) => 0;

        /// <summary>Adds produced output.</summary>
        public virtual void AddOutput(string itemId, int amount)
        {
        }

        /// <summary>Removes output. Returns the amount actually taken.</summary>
        public virtual int TakeOutput(string itemId, int amount) => 0;

        /// <summary>Reads current input quantity.</summary>
        public virtual int GetInputAmount(string itemId) => 0;

        /// <summary>
        /// Read-only snapshot of everything currently held in output, for the generic transport
        /// push step to enumerate without knowing the concrete building type. Empty by default -
        /// only a building with a real pooled output (ProductionBuildingRuntime) overrides this.
        /// </summary>
        public virtual IReadOnlyDictionary<string, int> GetOutputContents() => EmptyContents;

        /// <summary>
        /// Per-instance simulation tick, driven by TransportSystem once per frame for every
        /// registered building except conveyors (which have their own dedicated lane-advance
        /// loop needing direct GridRuntime access). No-op by default - only a building with its
        /// own timers/state machine (Extractor, ProductionBuildingRuntime, PowerplantGaz,
        /// Laboratory, DataCenter) overrides this.
        /// </summary>
        public virtual void Tick(float deltaTime)
        {
        }

        /// <summary>
        /// Called once when this building is removed from the world (TransportSystem.Unregister).
        /// No-op by default - only a building holding an external subscription/resource that
        /// would otherwise outlive it (DataCenter's ResearchSystem.ResearchCompleted subscription)
        /// needs to override this.
        /// </summary>
        public virtual void OnUnregistered()
        {
        }

        /// <summary>
        /// Shared Power gating pipeline (CONTRACTS.md §9), used by every building whose own tick
        /// progress must freeze while unpowered: reports demand only while "active", then returns
        /// 0 if the network cannot cover it. The caller multiplies its own deltaTime by the
        /// returned value before advancing any timer. Compute plays no part here - CU is a
        /// reserve spent in one shot when a cycle starts (§10), never a continuous draw that
        /// throttles a building's speed.
        /// </summary>
        protected static float ComputeEffectivePerformance(float powerDemand, bool powerActive, PowerSystem power)
        {
            if (powerDemand > 0f && powerActive)
            {
                power.ReportDemand(powerDemand);
                if (!power.IsPowered()) return 0f;
            }

            return 1f;
        }
    }
}
