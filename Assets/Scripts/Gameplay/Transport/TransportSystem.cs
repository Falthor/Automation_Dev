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

        public const float ConveyorSpeedCellsPerSecond = 1.5f;

        /// <summary>
        /// What a belt line carries, in items per minute - the figure the Building menu quotes on
        /// hover.
        ///
        /// <b>The rate is set upstream, not on the belt.</b> A line's cadence is whatever is feeding
        /// it, and every source that is not itself a belt hands over at RawOutputPullIntervalSeconds
        /// - so that is where this comes from, rather than being restated. At 1.5 cells/s the items
        /// then sit 1.5 cells apart and simply travel; nothing along the way has to meter anything.
        ///
        /// Metering each belt to the same rate was tried and reverted: it gives the same figure on a
        /// throughput graph and makes every item stop at every cell boundary, because an item
        /// reaches the seam two thirds of a second after entering and a one-second gate makes it
        /// wait out the rest. See ConveyorRuntime.HasRoomForNewItem.
        ///
        /// Not the belt's <b>capacity</b>, which is speed times MaxItemsPerCell - what it can hold
        /// when the line ahead is blocked and items pack up. That is a buffer, and quoting it told
        /// the player a belt did four times what a line does.
        ///
        /// The same for a corner as for a straight: one cell at one speed either way, so turning a
        /// line costs nothing.
        /// </summary>
        public const float ConveyorItemsPerMinute = 60f / RawOutputPullIntervalSeconds;

        /// <summary>
        /// Immutable rule, enforced here rather than by each building type: a source that isn't
        /// itself belt-gated (a Conveyor's progress meter, a Splitter/Crossroad's own one-item-at-
        /// a-time transit) can hand off at most one item per RawOutputPullIntervalSeconds via the
        /// generic pull path (RunGenericPulls) - regardless of which building pulls it. Without
        /// this, any consumer sitting flush against a production building's raw pooled output (no
        /// conveyor in between) could drain its entire backlog in a single tick, since that raw
        /// output has no throughput cap of its own. Living at the transport layer means no current
        /// or future building type can bypass it by simply omitting its own intake cooldown - it
        /// is not an opt-in a building author could forget. 1 second matches our fastest conveyor
        /// today (60 items/min); retune when a faster conveyor tier ships. It applies to a conveyor
        /// pulling from such a source too, through the belt-specific loop's own check
        /// (MayEnterBeltNetwork): a belt taking straight from a production output is an entry into
        /// the belt network, and rating entries is what gives a line its throughput. It was once
        /// believed HasRoomForNewItem covered that case - it does not, it is a spacing rule, and a
        /// line fed by a Foundry measured 257/min against a documented 60.
        /// </summary>
        public const float RawOutputPullIntervalSeconds = 1f;

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

        /// <summary>Seconds remaining before a given non-belt-gated source may hand off another item via the generic pull path - see RawOutputPullIntervalSeconds.</summary>
        readonly Dictionary<BuildingRuntime, float> _rawOutputPullCooldown = new Dictionary<BuildingRuntime, float>();

        /// <summary>Every registered Storage in the world, for UI that needs to aggregate across all of them (e.g. the global Storage panel).</summary>
        public IReadOnlyList<StorageRuntime> Storages => _storages;

        /// <summary>Every registered building across every internal list (CONTRACTS.md §14). Three consumers, all of them needing the whole set at once: the save, the building cap (CONTRACTS.md §8) and the map's drawing of the base (MAP.md §6). Not a general-purpose accessor - anything that wants one building has a narrower way to it.</summary>
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

        /// <summary>
        /// The building transport is allowed to deal with at a cell: null for empty ground, for a
        /// deposit, and for a construction site's unbuilt segment.
        ///
        /// The single door through which this system reads Game.Grid, which is the point of it. Not
        /// ticking a pending segment was already covered by never registering it - but every
        /// hand-off resolves its neighbour through the grid, where a segment does sit, and each of
        /// those six lookups asked only "is a BuildingRuntime there?". So a belt fed coal to an
        /// unbuilt powerplant, a splitter counted one as a valid exit, and a factory could be
        /// drained through an unbuilt neighbour. Written once here rather than as six repeated
        /// conditions, because the seventh lookup is the one that would forget.
        /// </summary>
        BuildingRuntime ActiveBuildingAt(GridCoord cell)
        {
            if (!(_grid.GetOccupant(cell) is BuildingRuntime building)) return null;
            return building.IsUnderConstruction ? null : building;
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
            _rawOutputPullCooldown.Remove(building);

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
            // RunGenericPulls itself still enforces RawOutputPullIntervalSeconds against a
            // non-belt-gated source, independently of any per-building cooldown.
            TickRawOutputPullCooldowns(deltaTime);
            RunGenericPulls();

            for (int i = 0; i < _conveyors.Count; i++)
            {
                ConveyorRuntime conveyor = _conveyors[i];
                if (!conveyor.HasRoomForNewItem) continue;

                GridCoord behind = conveyor.Cell + conveyor.Orientation.Rotation.Opposite();
                if (TryPullFromNeighbor(behind, conveyor.Cell, out object item, out BuildingRuntime source) && MayEnterBeltNetwork(source))
                {
                    conveyor.ReceiveItem(item);
                    source.ConsumePulledItem(item);
                    NoteEnteredBeltNetwork(source);
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
                BuildingRuntime neighbor = ActiveBuildingAt(conveyor.Cell + side);
                if (neighbor == null) continue;
                if (!OutputsTo(neighbor, conveyor.Cell)) continue;

                object item = neighbor.PeekPullableItem();
                if (item == null) continue;
                if (!MayEnterBeltNetwork(neighbor)) continue;

                conveyor.ReceiveItem(item);
                neighbor.ConsumePulledItem(item);
                NoteEnteredBeltNetwork(neighbor);
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
                if (HasBuildingNeighbor(splitter.NeighborCell(direction))) connected.Add(direction);
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

        /// <summary>
        /// Whether some building at all sits at that cell - a candidate exit for the splitter to
        /// consider, regardless of concrete type. Actual delivery is still gated by
        /// TryDeliverItem's own CanAcceptInput check; this only decides which directions are
        /// worth trying instead of being skipped outright. Previously restricted to
        /// Conveyor/Splitter/Crossroad/Storage, which silently excluded every production building
        /// (Factory, Foundry, AdvancedFoundry, DataCenter) - a splitter wired directly
        /// into one, with no belt in between, never delivered.
        /// </summary>
        bool HasBuildingNeighbor(GridCoord cell) => ActiveBuildingAt(cell) != null;

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
            BuildingRuntime occupant = ActiveBuildingAt(neighborCell);

            if (occupant is ConveyorRuntime targetConveyor)
            {
                if (!targetConveyor.HasRoomForNewItem) return false;
                targetConveyor.ReceiveItem(item);
                return true;
            }

            if (occupant != null && item is string itemId)
            {
                Direction fromDirection = exitDirection.Opposite();
                if (!occupant.CanAcceptInput(itemId, 1, fromDirection)) return false;
                occupant.AddInput(itemId, 1, fromDirection);
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
                BuildingRuntime target = ActiveBuildingAt(cell);
                if (target == null || ReferenceEquals(target, building)) continue;

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
                    BuildingRuntime occupant = ActiveBuildingAt(cell);
                    if (occupant == null) continue;

                    // The consumer's own cell touching that neighbour - one step back inside itself
                    // from the edge cell it just scanned.
                    if (!occupant.HandsOutTo(cell + fromMySide.Opposite())) continue;

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

                // RawOutputPullIntervalSeconds: a source whose own pullable item isn't already
                // belt-gated (Conveyor/Splitter/Crossroad) can hand off at most one item per
                // interval here, no matter which/how many consumers want it.
                bool sourceIsBeltGated = IsBeltGated(source);
                if (!sourceIsBeltGated && _rawOutputPullCooldown.ContainsKey(source)) continue;

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
                if (!sourceIsBeltGated) _rawOutputPullCooldown[source] = RawOutputPullIntervalSeconds;
            }
        }

        static bool IsBeltGated(BuildingRuntime building) =>
            building is ConveyorRuntime || building is SplitterRuntime || building is CrossroadRuntime;

        /// <summary>
        /// Whether this source may put another item onto the belt network right now.
        ///
        /// A belt, Splitter or Crossroad always may: passing an item along <b>inside</b> the network
        /// costs nothing and must not be metered, or every item stops at every seam waiting for a
        /// gate - which is precisely what a per-belt intake interval did.
        ///
        /// Anything else is an <b>entry point</b>, and is rated at RawOutputPullIntervalSeconds. One
        /// gate, where the items come from, is what gives a whole line its throughput: past it the
        /// items simply travel, spaced by the rate they entered at.
        /// </summary>
        bool MayEnterBeltNetwork(BuildingRuntime source) =>
            IsBeltGated(source) || !_rawOutputPullCooldown.ContainsKey(source);

        void NoteEnteredBeltNetwork(BuildingRuntime source)
        {
            if (!IsBeltGated(source)) _rawOutputPullCooldown[source] = RawOutputPullIntervalSeconds;
        }

        /// <summary>Decrements every source's RawOutputPullIntervalSeconds cooldown, dropping it once it reaches zero (see _rawOutputPullCooldown).</summary>
        void TickRawOutputPullCooldowns(float deltaTime)
        {
            if (_rawOutputPullCooldown.Count == 0) return;

            var expired = new List<BuildingRuntime>();
            var keys = new List<BuildingRuntime>(_rawOutputPullCooldown.Keys);
            foreach (BuildingRuntime source in keys)
            {
                float remaining = _rawOutputPullCooldown[source] - deltaTime;
                if (remaining <= 0f) expired.Add(source);
                else _rawOutputPullCooldown[source] = remaining;
            }

            foreach (BuildingRuntime source in expired)
            {
                _rawOutputPullCooldown.Remove(source);
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

            BuildingRuntime candidate = ActiveBuildingAt(neighborCell);
            if (candidate == null) return false;
            if (!OutputsTo(candidate, destinationCell)) return false;

            object pulled = candidate.PeekPullableItem();
            if (pulled == null) return false;

            item = pulled;
            source = candidate;
            return true;
        }

        /// <summary>
        /// Whether items leaving that building land in that cell - asked of the building itself,
        /// which is the only thing that knows: a Splitter or a Crossroad has several exits and no
        /// single output edge to read.
        /// </summary>
        static bool OutputsTo(BuildingRuntime building, GridCoord cell) => building.FeedsCell(cell);
    }
}
