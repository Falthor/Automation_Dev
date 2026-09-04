using System.Collections.Generic;
using Game.Core;
using Game.Gameplay.Buildings;
using Game.Grid;

namespace Game.Gameplay.Transport
{
    /// <summary>
    /// Central tick driving item production and belt movement.
    ///
    /// Conveyors keep their own dedicated pull-from-behind-via-Flow + lane-advance logic (a
    /// conveyor has no pooled input/output, just the items riding it), plus a lower-priority side
    /// merge for a belt arriving perpendicular to it (see TryMergeFromSide). Their two halves -
    /// advance, then hand over - run as two separate passes with every building's own read in
    /// between, so a building takes an item parked at the end of the belt cell its entry arrow
    /// points at before that belt passes it further down the line (see Tick).
    ///
    /// Every other registered building (Storage, production buildings) goes through one generic
    /// push and one generic pull. Push runs at the building's own PushIntervalSeconds: it walks
    /// GetOutputCells() and offers GetOutputContents() to whichever neighbor's
    /// CanAcceptInput/AddInput accepts one unit. Pull runs every tick, before the belts move: it
    /// scans GetInputCells() (the cells the building's entry arrows mark, regardless of that
    /// neighbor's own facing) and takes one unit from the first neighbor whose
    /// PeekPullableItem()/CanAcceptInput lines up. This replaces the previous Storage-specific
    /// pull loop with the same shared code path - Storage no longer requires the neighbor's own
    /// output to be aimed at it (CONTRACTS.md §13: this is a deliberate, documented behavior
    /// change, matching the source project's building.gd exactly, not a Storage-specific redesign).
    /// </summary>
    public sealed class TransportSystem
    {
        static readonly Direction[] AllDirections = { Direction.North, Direction.East, Direction.South, Direction.West };

        const float ConveyorSpeedCellsPerSecond = 1.5f;

        readonly GridRuntime _grid;
        readonly List<ConveyorRuntime> _conveyors = new List<ConveyorRuntime>();
        readonly List<SplitterRuntime> _splitters = new List<SplitterRuntime>();
        readonly List<CrossroadRuntime> _crossroads = new List<CrossroadRuntime>();
        readonly List<StorageRuntime> _storages = new List<StorageRuntime>();

        /// <summary>
        /// Every registered building except conveyors (which have their own dedicated
        /// lane-advance loop below, needing direct GridRuntime access no other building needs).
        /// Ticked uniformly via the generic BuildingRuntime.Tick() virtual, then run through the
        /// generic push/pull step - both are safe no-ops for a building that doesn't override
        /// them (e.g. an Extractor's push/pull is a no-op since it has no pooled input/output).
        /// </summary>
        readonly List<BuildingRuntime> _allOthers = new List<BuildingRuntime>();
        readonly Dictionary<BuildingRuntime, float> _pushPullTimers = new Dictionary<BuildingRuntime, float>();

        /// <summary>Last consumer served by a given pull source, keyed by the source building itself - lets two consumers sharing one input point (e.g. two Factories both facing the same conveyor cell) alternate instead of the earlier-registered one always winning (see RunGenericPulls).</summary>
        readonly Dictionary<BuildingRuntime, BuildingRuntime> _lastPullServedBy = new Dictionary<BuildingRuntime, BuildingRuntime>();

        /// <summary>Every registered Storage in the world, for UI that needs to aggregate across all of them (e.g. the global Storage panel).</summary>
        public IReadOnlyList<StorageRuntime> Storages => _storages;

        /// <summary>Every registered building across every internal list (CONTRACTS.md §14) - used only by the save/load system to enumerate every placed building at once; no other consumer should need this.</summary>
        public IEnumerable<BuildingRuntime> GetAllBuildings()
        {
            foreach (BuildingRuntime building in _conveyors) yield return building;
            foreach (BuildingRuntime building in _splitters) yield return building;
            foreach (BuildingRuntime building in _crossroads) yield return building;
            foreach (BuildingRuntime building in _allOthers) yield return building;
        }

        public TransportSystem(GridRuntime grid)
        {
            _grid = grid;
        }

        public void Register(BuildingRuntime building)
        {
            if (building is ConveyorRuntime conveyor)
            {
                _conveyors.Add(conveyor);
                return;
            }

            if (building is SplitterRuntime splitter)
            {
                _splitters.Add(splitter);
                return;
            }

            if (building is CrossroadRuntime crossroad)
            {
                _crossroads.Add(crossroad);
                return;
            }

            _allOthers.Add(building);
            if (building is StorageRuntime storage) _storages.Add(storage);
        }

        public void Unregister(BuildingRuntime building)
        {
            building.OnUnregistered();
            _lastPullServedBy.Remove(building);

            if (building is ConveyorRuntime conveyor)
            {
                _conveyors.Remove(conveyor);
                return;
            }

            if (building is SplitterRuntime splitter)
            {
                _splitters.Remove(splitter);
                return;
            }

            if (building is CrossroadRuntime crossroad)
            {
                _crossroads.Remove(crossroad);
                return;
            }

            _allOthers.Remove(building);
            _pushPullTimers.Remove(building);
            if (building is StorageRuntime storage) _storages.Remove(storage);
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _allOthers.Count; i++)
            {
                _allOthers[i].Tick(deltaTime);
            }

            // The belt phase is deliberately split in two, with the buildings' own read wedged
            // between the halves. Advancing and handing over in one pass per belt made the whole
            // line's behavior depend on the order belts happen to sit in this list, and left no
            // moment where an item is observably parked at the end of a cell: a belt advanced its
            // item to the end and the next belt - visited later in that same pass - immediately
            // took it. A building alongside a *running* line therefore never saw anything to pick
            // up and only got fed once the line downstream jammed. Advancing everything first,
            // then letting buildings read, then letting belts hand over, makes the order
            // irrelevant and gives a building priority over the belt continuing past it.
            for (int i = 0; i < _conveyors.Count; i++)
            {
                _conveyors[i].AdvanceItem(deltaTime, ConveyorSpeedCellsPerSecond);
            }

            // Buildings read their input cells every tick (not at PushIntervalSeconds like the
            // push side): how fast a building may absorb what it reads is its own business - e.g.
            // FoundryRuntime's intake cooldown - not a side effect of how often transport looks.
            RunGenericPulls();

            for (int i = 0; i < _conveyors.Count; i++)
            {
                ConveyorRuntime conveyor = _conveyors[i];
                if (!conveyor.HasRoomForNewItem) continue;

                GridCoord behind = conveyor.Cell + conveyor.Orientation.Rotation.Opposite();
                if (TryPullFromNeighbor(behind, conveyor.Cell, out object item, out BuildingRuntime source))
                {
                    conveyor.ReceiveItem(item);
                    source.ConsumePulledItem(item);
                }
                else
                {
                    TryMergeFromSide(conveyor);
                }
            }

            TickSplitters();
            TickCrossroads(deltaTime);

            for (int i = 0; i < _allOthers.Count; i++)
            {
                BuildingRuntime building = _allOthers[i];
                float timer = _pushPullTimers.TryGetValue(building, out float existing) ? existing : 0f;
                timer += deltaTime;
                if (timer < building.PushIntervalSeconds)
                {
                    _pushPullTimers[building] = timer;
                    continue;
                }

                _pushPullTimers[building] = 0f;
                TryGenericPush(building);
            }
        }

        /// <summary>
        /// Side merge: a neighbor whose own output points into this conveyor, but across one of
        /// its two side edges rather than its back edge, hands over one item. That neighbor is
        /// any building, not just another belt - it is equally how a Foundry/Factory standing
        /// alongside a belt drops its output onto it, instead of only being able to feed a belt
        /// aimed back at its output edge. Deliberately the lower priority of the two intakes: it
        /// is only attempted when the straight-through pull above found nothing, and only while
        /// this belt still has a free slot, so the belt being merged into always keeps the right
        /// of way and a merging item simply waits its turn.
        ///
        /// The exit edge is excluded along with the entry edge: a neighbor sitting there and
        /// pointing back at us is two belts facing each other head-on, which would otherwise pass
        /// the same item back and forth forever.
        /// </summary>
        void TryMergeFromSide(ConveyorRuntime conveyor)
        {
            Direction entry = conveyor.Orientation.Rotation.Opposite();
            Direction exit = conveyor.ExitDirection;

            foreach (Direction side in AllDirections)
            {
                if (side == entry || side == exit) continue;
                if (!(_grid.GetOccupant(conveyor.Cell + side) is BuildingRuntime neighbor)) continue;
                if (!OutputsTo(neighbor, conveyor.Cell)) continue;

                object item = neighbor.PeekPullableItem();
                if (item == null) continue;

                conveyor.ReceiveItem(item);
                neighbor.ConsumePulledItem(item);
                return;
            }
        }

        /// <summary>
        /// Every tick: pull one item off the fixed entry side if empty, then attempt delivery
        /// (assigned exit first, falling back to the other connected exits) if holding one -
        /// mirroring the conveyor loop above but using the splitter's own arm-tip cells instead
        /// of a single "behind" offset, since its footprint isn't 1x1.
        /// </summary>
        void TickSplitters()
        {
            for (int i = 0; i < _splitters.Count; i++)
            {
                SplitterRuntime splitter = _splitters[i];

                if (!splitter.HasItem)
                {
                    GridCoord armCell = splitter.ArmCell(splitter.EntrySide);
                    GridCoord neighborCell = splitter.NeighborCell(splitter.EntrySide);
                    if (TryPullFromNeighbor(neighborCell, armCell, out object item, out BuildingRuntime source) && item is string itemId)
                    {
                        splitter.AddInput(itemId, 1, splitter.EntrySide);
                        source.ConsumePulledItem(item);
                    }
                }

                if (splitter.HasItem)
                {
                    TryDeliverFromSplitter(splitter);
                }
            }
        }

        /// <summary>
        /// Tries the currently assigned exit first, then cycles through every other connected
        /// exit before giving up for this tick - a jammed destination never blocks the others.
        /// Connected candidates and the assignment itself are recomputed every call (not
        /// cached), so a neighbor placed/removed after intake is picked up immediately.
        /// </summary>
        void TryDeliverFromSplitter(SplitterRuntime splitter)
        {
            var connected = new List<Direction>(3);
            foreach (Direction direction in SplitterRuntime.CandidateExits(splitter.EntrySide))
            {
                if (IsBeltOrStorage(splitter.NeighborCell(direction))) connected.Add(direction);
            }
            if (connected.Count == 0) return;

            if (splitter.AssignedExit == null || !connected.Contains(splitter.AssignedExit.Value))
            {
                splitter.AssignExit(splitter.NextCursorExit(connected));
            }

            Direction assigned = splitter.AssignedExit.Value;
            if (TryDeliverSplitterItem(splitter, assigned)) return;

            foreach (Direction direction in connected)
            {
                if (direction == assigned) continue;
                if (TryDeliverSplitterItem(splitter, direction)) return;
            }
        }

        bool IsBeltOrStorage(GridCoord cell)
        {
            object occupant = _grid.GetOccupant(cell);
            return occupant is ConveyorRuntime || occupant is SplitterRuntime || occupant is CrossroadRuntime || occupant is StorageRuntime;
        }

        /// <summary>
        /// Hands the splitter's held item to whatever sits at the given exit's neighbor cell. A
        /// conveyor target is fed directly via ReceiveItem (conveyors never accept the generic
        /// CanAcceptInput/AddInput push contract - see the conveyor loop above); anything else
        /// goes through the normal Building/Inventory contract.
        /// </summary>
        bool TryDeliverSplitterItem(SplitterRuntime splitter, Direction direction)
        {
            if (!TryDeliverItem(splitter.NeighborCell(direction), direction, splitter.HeldItemId)) return false;
            splitter.ClearHeldItem();
            return true;
        }

        /// <summary>
        /// Every tick: pull one item per lane off its fixed entry side if that lane is empty,
        /// advance both lanes, then deliver whichever lane(s) have reached their exit. Mirrors
        /// the conveyor loop but for two independent lanes sharing one "+" footprint.
        /// </summary>
        void TickCrossroads(float deltaTime)
        {
            for (int i = 0; i < _crossroads.Count; i++)
            {
                CrossroadRuntime crossroad = _crossroads[i];

                TryPullIntoCrossroadLane(crossroad, crossroad.EntryA, isLaneA: true);
                TryPullIntoCrossroadLane(crossroad, crossroad.EntryB, isLaneA: false);

                crossroad.AdvanceItems(deltaTime, ConveyorSpeedCellsPerSecond);

                if (crossroad.HasItemA && crossroad.ProgressA >= 1f && TryDeliverItem(crossroad.NeighborCell(crossroad.ExitA), crossroad.ExitA, crossroad.ItemA))
                {
                    crossroad.ClearA();
                }

                if (crossroad.HasItemB && crossroad.ProgressB >= 1f && TryDeliverItem(crossroad.NeighborCell(crossroad.ExitB), crossroad.ExitB, crossroad.ItemB))
                {
                    crossroad.ClearB();
                }
            }
        }

        void TryPullIntoCrossroadLane(CrossroadRuntime crossroad, Direction entry, bool isLaneA)
        {
            if (isLaneA ? crossroad.HasItemA : crossroad.HasItemB) return;

            GridCoord armCell = crossroad.ArmCell(entry);
            GridCoord neighborCell = crossroad.NeighborCell(entry);
            if (!TryPullFromNeighbor(neighborCell, armCell, out object item, out BuildingRuntime source)) return;

            if (isLaneA) crossroad.ReceiveA(item);
            else crossroad.ReceiveB(item);
            source.ConsumePulledItem(item);
        }

        /// <summary>
        /// Hands an item to whatever occupies neighborCell, as seen from a "+"-shaped building's
        /// exit side - a conveyor target is fed directly via ReceiveItem (conveyors never accept
        /// the generic CanAcceptInput/AddInput push contract), anything else goes through the
        /// normal Building/Inventory contract. Shared by Splitter and Crossroad delivery.
        /// </summary>
        bool TryDeliverItem(GridCoord neighborCell, Direction exitDirection, object item)
        {
            object occupant = _grid.GetOccupant(neighborCell);

            if (occupant is ConveyorRuntime targetConveyor)
            {
                if (!targetConveyor.HasRoomForNewItem) return false;
                targetConveyor.ReceiveItem(item);
                return true;
            }

            if (occupant is BuildingRuntime targetBuilding && item is string itemId)
            {
                Direction fromDirection = exitDirection.Opposite();
                if (!targetBuilding.CanAcceptInput(itemId, 1, fromDirection)) return false;
                targetBuilding.AddInput(itemId, 1, fromDirection);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Offers one unit of whatever the building's output currently holds to the first
        /// accepting neighbor across its output edge (may be several cells wide).
        /// </summary>
        void TryGenericPush(BuildingRuntime building)
        {
            IReadOnlyDictionary<string, int> contents = building.GetOutputContents();
            if (contents.Count == 0) return;

            foreach (GridCoord cell in building.GetOutputCells())
            {
                if (!(_grid.GetOccupant(cell) is BuildingRuntime target) || ReferenceEquals(target, building)) continue;

                foreach (var kvp in contents)
                {
                    if (kvp.Value <= 0) continue;
                    if (!target.CanAcceptInput(kvp.Key, 1, building.ExitDirection.Opposite())) continue;

                    building.TakeOutput(kvp.Key, 1);
                    target.AddInput(kvp.Key, 1, building.ExitDirection.Opposite());
                    return;
                }
            }
        }

        /// <summary>
        /// Actively grabs one item off a neighbor exposing a pullable item (Flow contract),
        /// regardless of that neighbor's own facing, but only across a building's own input cells
        /// - the cells its entry arrows are drawn on (GetInputCells). Straight-on, directly
        /// touching cells only (no reaching around corners or through other buildings).
        ///
        /// Two-phase to stay fair when two consumers share one input point (e.g. two Factories
        /// both facing the same conveyor cell): phase 1 lets every building pick its own single
        /// best candidate without consuming anything yet, phase 2 resolves any source that more
        /// than one consumer wants by round-robin (see _lastPullServedBy) instead of always
        /// letting whichever consumer happens to be registered first win every tick.
        /// </summary>
        void RunGenericPulls()
        {
            var intents = new List<(BuildingRuntime consumer, BuildingRuntime source, Direction fromSide, string itemId)>();

            for (int i = 0; i < _allOthers.Count; i++)
            {
                BuildingRuntime building = _allOthers[i];
                foreach (var (cell, fromMySide) in building.GetInputCells())
                {
                    if (!(_grid.GetOccupant(cell) is BuildingRuntime occupant)) continue;

                    object item = occupant.PeekPullableItem();
                    if (item == null || !(item is string itemId)) continue;
                    if (!building.CanAcceptInput(itemId, 1, fromMySide)) continue;

                    intents.Add((building, occupant, fromMySide, itemId));
                    break;
                }
            }

            var contendersBySource = new Dictionary<BuildingRuntime, List<(BuildingRuntime consumer, Direction fromSide, string itemId)>>();
            foreach (var intent in intents)
            {
                if (!contendersBySource.TryGetValue(intent.source, out var list))
                {
                    list = new List<(BuildingRuntime, Direction, string)>();
                    contendersBySource[intent.source] = list;
                }
                list.Add((intent.consumer, intent.fromSide, intent.itemId));
            }

            foreach (var kvp in contendersBySource)
            {
                BuildingRuntime source = kvp.Key;
                var contenders = kvp.Value;

                // A source exposes only one pullable item at a time, so only one contender can
                // actually take it this tick - pick round-robin among today's contenders rather
                // than always the first one found, so two consumers sharing one source alternate.
                int startIndex = 0;
                if (_lastPullServedBy.TryGetValue(source, out BuildingRuntime lastWinner))
                {
                    int lastIndex = contenders.FindIndex(c => ReferenceEquals(c.consumer, lastWinner));
                    if (lastIndex >= 0) startIndex = (lastIndex + 1) % contenders.Count;
                }

                var chosen = contenders[startIndex];
                object item = source.PeekPullableItem();
                if (!(item is string itemId) || !chosen.consumer.CanAcceptInput(itemId, 1, chosen.fromSide)) continue;

                source.ConsumePulledItem(item);
                chosen.consumer.AddInput(itemId, 1, chosen.fromSide);
                _lastPullServedBy[source] = chosen.consumer;
            }
        }

        /// <summary>
        /// A candidate at <paramref name="neighborCell"/> may only be pulled from if its own
        /// configured output actually targets <paramref name="destinationCell"/> - otherwise a
        /// conveyor/extractor pointed elsewhere would be incorrectly drained by an unrelated
        /// neighbor. Used only by the conveyor lane pull above (a conveyor has exactly one back
        /// edge, unlike the generic multi-side pull).
        /// </summary>
        bool TryPullFromNeighbor(GridCoord neighborCell, GridCoord destinationCell, out object item, out BuildingRuntime source)
        {
            item = null;
            source = null;

            if (!(_grid.GetOccupant(neighborCell) is BuildingRuntime candidate)) return false;
            if (!OutputsTo(candidate, destinationCell)) return false;

            object pulled = candidate.PeekPullableItem();
            if (pulled == null) return false;

            item = pulled;
            source = candidate;
            return true;
        }

        /// <summary>
        /// Whether that cell is anywhere along the building's output edge. The whole edge counts,
        /// not just its first cell (GetOutputCell()): a footprint wider than one cell hands its
        /// output to every cell it faces, so a belt may take from any of them.
        /// </summary>
        static bool OutputsTo(BuildingRuntime building, GridCoord cell)
        {
            foreach (GridCoord outputCell in building.GetOutputCells())
            {
                if (outputCell == cell) return true;
            }
            return false;
        }
    }
}
