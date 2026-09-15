# Transport

How items move between buildings: the surfaces a building exposes, the geometry of where it takes
from and hands out to, the generic push and pull, the belt network and its entry rate, and the
configuration of belts, Splitters and Crossroads.

Implemented by `TransportSystem` (`Game.Gameplay.Transport`) over contracts declared on
`BuildingRuntime` (`Game.Gameplay.Buildings`).

## 1. The surfaces a building exposes

Declared on `BuildingRuntime` with neutral defaults; only flow-participating buildings override them.

```csharp
public virtual object PeekPullableItem()
public virtual void ConsumePulledItem(object item)
```

`PeekPullableItem()` returns the item currently available for transfer, or `null`. The return type is
`object` until a dedicated transport item type exists - a documented placeholder, not the final
shape. `ConsumePulledItem(item)` consumes what the peek exposed.

**Whether a building is part of the belt network is asked of its definition**, never of the runtime's
type: `TransportSystem.IsBeltGated` reads `BuildingDefinition.IsTransportPiece`, which the ground slab
and the map read too. A runtime-side answer to the same question existed, was overridden and tested,
and was never once consulted.

A caller querying a neighbour uses these rather than depending on a concrete class.

For non-belt buildings there is also a pooled-inventory surface. `itemId` is a `string` - the fixed
key an `ItemDefinition` is registered under in an `ItemDatabase`; there is no per-building duplicate
of item identity.

```csharp
CanAcceptInput(itemId, amount, fromDirection)   AddInput(itemId, amount, fromDirection)
TakeInput(itemId, amount)                       GetInputAmount(itemId)
AddOutput(itemId, amount)                       TakeOutput(itemId, amount)
GetOutputContents()
```

`GetOutputContents()` returns a read-only snapshot of everything held in output. Empty by default;
only a building with a real pooled output overrides it. It exists so the generic push can enumerate
what a building holds without knowing its type - `AddOutput`/`TakeOutput` alone are write and consume
operations, not an enumeration.

**Two pooled shapes coexist under one contract.** `Inventory` (used by `StorageRuntime`) is
slot-based: a fixed `SlotCount` of distinct item ids, each capped at `CapacityPerSlot`.
`PooledItemStock` (used for a production building's input and output) has unlimited distinct ids,
each capped independently by `CapacityFor(itemId)` - a question rather than a stored number, because
the two sides answer it differently. Which one a building uses is an internal choice.

The pooled model must not be mixed with the belt lane model.

## 2. Where a building takes from and hands out to

`BuildingRuntime` exposes footprint- and rotation-aware geometry, so a generic step never needs a
concrete type:

```csharp
public GridCoord[] GetOutputCells()
public (GridCoord cell, Direction fromMySide)[] GetEdgeCells()
public (GridCoord cell, Direction fromMySide)[] GetInputCells()
public virtual bool FeedsCell(GridCoord cell)
```

`GetOutputCells()` returns the cells items actually leave through. For a building declaring an output
arrow that is the single cell the arrow is drawn on; for one declaring none (Storage, the Core, a
belt) it is the whole output edge. `GetEdgeCells()` returns every cell touching any of the four
sides, paired with which side it touches - the `fromDirection` argument to `CanAcceptInput`/`AddInput`.

`GetInputCells()` returns the subset items are actually taken from, and it has **three** shapes, each
a different promise:

| Declaration | Takes from | Who |
|---|---|---|
| `HasSingleInputArrow` | **one** cell, on `BuildingRuntime.InputSide`, and nowhere else | Foundry, Constructor |
| `HasInputArrows` | one cell per side other than its output side | Factory, Advanced Foundry |
| neither | every edge cell — "input from any side" | Storage, Core |

In all three, **an arrow marks a real intake point and there are no invisible ones**. The
single-input case makes that promise much stronger: a belt touching any other face is refused,
however full it is. That refusal is enforced twice on purpose - by this list, and again in
`ProductionBuildingRuntime.CanAcceptInput` - because the generic push and the belt hand-over both ask
the target directly rather than consulting the list.

**`InputSide` is per-building state, chosen at placement.** It defaults to the opposite of the output
(what a belt running straight through wants) and is moved with `T` while the placement ghost is up;
`SetInputSide` refuses the output side, since a side that both took and gave would feed the building
its own production. `SetFacingRotation` puts it back to the default if a rotation leaves it sitting on
the new output side - the alternative is a building that silently takes nothing.

`FeedsCell(cell)` answers whether items leaving this building land in `cell` - the question "is this
cell fed by that neighbour", which conveyor placement asks in order to inherit a belt's direction from
whatever already flows into it. The default is the whole output edge. **Splitter and Crossroad
override it, and must**: neither has a single output side, and both inherit `ExitDirection` from
`FacingRotation`, which on those two types names an *entry*. Any code asking "where does this building
output" through `GetOutputCell()` gets a wrong answer for them; ask `FeedsCell` instead.

**Where a source hands out is the source's rule, on every path.** The generic push walks
`GetOutputCells()`; the generic pull asks `HandsOutTo` before reaching into a neighbour; the belt
intakes ask `FeedsCell`. A building that shows an output arrow hands out there and nowhere else - a
box parked against its back, or beside its arrow on a wide edge, is not served.

## 3. The generic push and pull

`TransportSystem` runs one generic push and one generic pull for every registered building that is not
itself belt-driven (Storage, and every `ProductionBuildingRuntime`):

- **Push**, at the building's own `PushIntervalSeconds` (default 1s): walks `GetOutputCells()`; for
  the first neighbour whose `CanAcceptInput` accepts one unit of something in `GetOutputContents()`,
  transfers it via `TakeOutput`/`AddInput`.
- **Pull**, every tick: walks `GetInputCells()`; for the first neighbour that `HandsOutTo` this
  building's touching cell and exposes a `PeekPullableItem()` this building's own `CanAcceptInput`
  accepts, transfers it via `ConsumePulledItem`/`AddInput`.

How fast a building may actually absorb what it reads is otherwise its own concern - the Foundry's
intake cooldown is the only one, and a chest has none at all - not a side effect of the polling rate,
except for the structural rule below.

**When two buildings share one physical source cell** - two Factories on adjacent sides of one
conveyor cell, both facing it - only one can take that source's single pullable item on a tick. Rather
than always favouring whichever is registered first, the pull resolves it per source with round-robin:
it remembers the last consumer served by each source and gives it to whichever contender was not served
last time.

## 4. The entry rate, and the three things it is not

**`TransportSystem.RawOutputPullIntervalSeconds`** (1s, matching the fastest conveyor - 60 items/min):
a pull source that is not itself belt-gated may hand off at most one item per interval, no matter how
many consumers want it. A production building's raw pooled output has no throughput cap of its own, so
without this any consumer sitting flush against one could drain its entire backlog in a single tick.
Living here rather than as a per-building cooldown makes it immutable: no building type can bypass it
by omitting one.

Three separate things, and conflating them once made a belt line carry four times its rating:

- **The entry rate** (`RawOutputPullIntervalSeconds`, which `ConveyorItemsPerMinute` — the figure the
  Building menu quotes — is derived from) is how often something *not already a belt* may put an item
  onto the network -
  through the generic pull, a belt's straight-through pull, a side merge, or a Splitter's or
  Crossroad's entry arm, all four checked by `MayEnterBeltNetwork`. The arms were the one way in that
  did not check it, and drained a non-belt source one item per *tick*.
- **`ConveyorSpeedCellsPerSecond`** is how fast what is on a belt travels.
- **`ConveyorRuntime.MaxItemsPerCell`** (at `MinItemSpacing` apart) is how tightly items pack once the
  line ahead is blocked: a jam buffer, never a rate.

**The rate is set once, at the entry, and never at a seam inside the network.** Past that gate items
simply travel, spaced by the rate they entered at, and a belt hands over the instant the next one
physically has room. Metering each belt at the line's own rate instead was tried and reverted: it
produces the same items per minute and makes every item stop dead at every cell boundary, because an
item reaches the seam two thirds of a second after entering and a one-second gate makes it wait out
the remaining third. What a jam holds is the counterpart: items pack to 4.5 times their free-flow
density, and a cleared blockage drains at the belt's own speed rather than trickling back out at the
entry rate.

## 5. One tick

```text
building state machines
  → every belt advances
    → every building reads its input cells
      → every belt hands over (back-edge pull, then side merge)
        → splitters and crossroads
          → building pushes
```

**The belt phase is split around the building read on purpose.** Doing a belt's advance and hand-over
together, belt by belt, made the whole line's behaviour depend on the order belts sit in the internal
list, and left no moment where an item is observably parked at the end of a cell - the next belt,
visited later in the same pass, took it immediately. A building alongside a *running* line then never
saw anything to pick up and was only fed once the line downstream jammed. With the split, order stops
mattering and a building takes an item parked at the cell its entry arrow points at before the belt
carries it further.

Consequence worth keeping in mind when designing a line: of two machines reading the same belt, the
upstream one is served first and the downstream one gets what is left, exactly like the belt's own
downstream continuation.

## 6. The belts' own intakes

Conveyors keep their own pull-from-behind plus lane-advance logic - a conveyor has no pooled input or
output, just the items riding it - and are not part of the generic step.

A conveyor also accepts a **side merge**: when its pull from the back edge finds nothing and it still
has a free slot, it takes one item from any building whose own output points into it across one of its
two side edges - another belt merging in, or a production building dropping its output onto the belt it
runs past. The exit edge is excluded, so two belts facing each other head-on never trade the same item
back and forth. The side is always the lower priority of the two intakes: the in-line belt is served
first every tick, and a merging item only enters when the receiving belt has room.

**Several belts leaving one chest share it in turn.** One item per interval leaves a non-belt source,
so the belt loop runs in three passes: one offering each source to a belt it did not serve last, one
picking up what that deferred - also the only pass a lone belt is served on - and one for the side
merges. Before that, the first belt in registration order took every item and the others never moved.

## 7. What a chest may trade with

**What may feed a Storage is a rule about the pair** (`MayFeedStorage`), and it is the one place a
direction is asked of the source rather than left to `HandsOutTo`: a chest takes from **whatever points
at it** - a belt of either shape, a Splitter, a Crossroad, or a machine's own output face - and from
nothing that merely touches it. The direction belongs here because a belt's open flank is right for a
machine and wrong for a chest, which would otherwise drain every line it happened to sit beside.

It said more than that twice, and both were wrong in the same way: straight conveyors only, then the
belt network only. Each was an attempt to say "aimed at it" by naming types, and each refused a layout
nobody could see a reason for.

**A chest hands out to whatever its generic pull offers it to** - a belt taking through its own back
edge, or a machine standing against it reaching straight in, exactly as if a belt sat between them.
`MayTakeFromStorage` used to refuse the machine's case outright, on the reasoning that allowing it
would turn every chest into an unread feeder; in play a chest sitting between a belt and the machine it
was meant to supply just dead-ended there instead - the chest could take the delivery (nothing else
contended for it) but could never pass it on (a chest cannot hand to another chest, and a machine could
not reach in), so it read as the chest stealing the delivery and never emptying. Removed for that
reason - a machine adjacent to a chest is now an ordinary consumer of it.

**The side merge still refuses a Storage source outright**, unaffected by the above: a line merely
running past a chest may not drain it that way, only the two forms above (a belt's own back edge, or a
machine's straight pull) may. A chest has no output side to declare, so it answers `FeedsCell` for all
four of them - without the side-merge refusal, every buffer in a base would bleed into whatever line
happened to run past it.

It exposes one unit at a time from a cursor that moves on after every unit, so a chest holding three
item types hands out one of each in turn instead of emptying its first slot first.

**The Core chest is the one container a conveyor may not connect to in either direction**:
`StorageDefinition.RejectsConveyorInput` refuses a belt's delivery and suppresses the hand-out too,
because a belt draining it would empty the run's construction reserve onto a line. A builder robot and
the player emptying a slot by hand are separate paths and are not affected.

**All three intake paths ask it**: the generic pull a chest runs for itself, the push a Splitter or
Crossroad makes, and the generic push a production building makes across its output cells. The third
was missing for a while, and it is the one a player meets first - a Constructor parked against a box
filled it with no belt anywhere in sight.

## 8. Configuring a conveyor

The caller expresses intent; `ConveyorRuntime` owns its internal representation (`ConveyorOrientation`:
shape, rotation, mirrored). `Mirrored` is never set directly by a caller.

```csharp
public void ConfigureAsStraight(Direction exitDirection)
public void ConfigureAsCorner(Direction entryDirection, Direction exitDirection)
public void ConfigureAsCornerShape()
public void SetRotation(Direction rotation)
```

`ConfigureAsCorner` requires perpendicular directions; rotation and chirality are derived internally
from a single canonical reference orientation, and it throws for equal or opposite pairs - use
`ConfigureAsStraight` for those. `ConfigureAsCornerShape` sets the shape without implying a direction,
rotation being applied separately. There are two shapes and only two (`ConveyorShapeKind`: Straight,
Corner) - a crossroad is its own building type, not a conveyor shape.

A caller must not depend on the internal enum or manipulate the representation directly to obtain a
desired visual result.

## 9. Splitter and Crossroad

A Splitter routes one conveyor-fed item across up to three outputs - every cardinal side except its one
fixed entry side - round-robin per item, holding at most one item at a time. A Crossroad carries two
independent single-item lanes crossing in one cell. Both are driven by their own dedicated steps rather
than the generic push and pull, because their input and output cells are read from their own sides
rather than from a rectangle's edges.

**There is no dedicated conversion entry point** for replacing a belt with one. A Splitter or a
Crossroad is placed straight onto belts already laid rather than requiring a demolition first - see
`CONSTRUCTION.md` for the occupancy exception and what each overtaken cell owes. The piece is placed
like any other, and its entry side is its `FacingRotation`, taken from the rotation the ghost was
previewing at the click.

**Candidate exit connectivity.** `TryDeliverFromSplitter` only considers a non-entry side a candidate
exit when some `BuildingRuntime` occupies its neighbour cell - any concrete type, not a fixed
whitelist. A splitter wired directly into a production building with no conveyor in between delivers to
it exactly like it would to a Storage box or another belt piece; `TryDeliverItem`'s own
`CanAcceptInput` check is what actually gates whether delivery succeeds.
