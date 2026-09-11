# Contracts

Public contracts between Unity runtime systems.

A consumer must use the public contract rather than depend on another system's internal fields or concrete implementation details.

## 1. General contract rule

```text
Consumer
   ↓
Public contract
   ↓
Provider implementation
```

Never:

```text
Consumer
   ↓
Provider private/internal state
```

Concrete-type checks may be used for behavior dispatch when the contract genuinely requires different behavior, but the check must not become an excuse to access internal fields.

## 2. Building / Flow

Declared on `BuildingRuntime` (`Game.Gameplay.Buildings`), with neutral defaults; only flow-participating buildings override them.

```csharp
public virtual object PeekPullableItem()
public virtual void ConsumePulledItem(object item)
public virtual bool IsFlowReceiver()
```

### `PeekPullableItem()`

Returns the item currently available for transfer, or `null` when none is available. Return type is `object` until a dedicated transport item type exists; this is a documented placeholder, not the final contract.

### `ConsumePulledItem(item)`

Consumes the item previously exposed by `PeekPullableItem()`. No-op by default.

### `IsFlowReceiver()`

Indicates whether the building participates in directional flow. `false` by default; `ConveyorRuntime` overrides it to `true`.

A caller querying a neighboring building uses these methods instead of depending on `ConveyorRuntime`, a future `SplitterRuntime`, or another concrete class.

## 3. Building / Inventory

For non-belt buildings. `itemId` is a `string` - the fixed key an `ItemDefinition` (`Game.Data`) is registered under in an `ItemDatabase`; there is no per-building duplicate of item identity or metadata.

### `CanAcceptInput(itemId, amount, fromDirection)`

Checks whether input can be received.

### `AddInput(itemId, amount, fromDirection)`

Adds accepted input.

### `TakeInput(itemId, amount)`

Consumes input.

### `AddOutput(itemId, amount)`

Adds produced output.

### `TakeOutput(itemId, amount)`

Removes output.

### `GetInputAmount(itemId)`

Reads current input quantity.

### `GetOutputContents()`

Returns a read-only `IReadOnlyDictionary<string,int>` snapshot of everything currently held in output. Empty by default; only a building with a real pooled output (`ProductionBuildingRuntime`) overrides it. Exists so a generic caller (the transport push step) can enumerate what a building's output holds without knowing its concrete type - `AddOutput`/`TakeOutput` alone are write/consume operations, not an enumeration.

### `ProductionBuildingRuntime.GetInputContents()`

Not part of the base `BuildingRuntime` contract (only `ProductionBuildingRuntime` and its subclasses have a pooled input to enumerate). Returns a read-only `IReadOnlyDictionary<string,int>` snapshot of everything currently held in input - mirrors `GetOutputContents()` for the other side of the same building. Read by the building's own inspector panel (`ProductionPanelController`) to show what it is holding. It is deliberately **not** part of GlobalStock's aggregate nor of a construction site's collection order (§15): only a production building's *output* is claimable by a builder robot.

The pooled inventory model must not be mixed with the belt lane model. Two pooled-inventory shapes coexist under this same contract: `Inventory` (`Game.Gameplay.Items`, used by `StorageRuntime`) is slot-based - a fixed `SlotCount` of distinct item ids, each slot capped at `CapacityPerSlot`. `PooledItemStock` (`Game.Gameplay.Items`, used by `ProductionBuildingRuntime`'s input and output) has unlimited distinct item ids, each capped independently by `CapacityFor(itemId)` - a question rather than a stored number, because the two sides answer it differently. Which one a building uses is an internal representation choice; both satisfy the same public methods above.

A production building's **input** holds `ProductionBuildingRuntime.InputCraftsHeld` (3) crafts' worth of each ingredient: the selected recipe's per-craft amount times three, so a recipe taking 2 iron and 4 screws buffers 6 and 12. Read live, so changing recipe moves the ceilings with it, and anything the current recipe does not consume has a ceiling of zero. Its **output** is a flat `OutputStackCapacity` (10) whatever the building or recipe - that buffer exists against a stalled belt, and its right size has nothing to do with the recipe. Neither is configurable per definition: a building's intake is sized by what it actually consumes.

## 3a. Generic transport push/pull

`BuildingRuntime` exposes footprint- and rotation-aware geometry so a generic transport step never needs to know a building's concrete type:

```csharp
public GridCoord[] GetOutputCells()
public (GridCoord cell, Direction fromMySide)[] GetEdgeCells()
```

```csharp
public (GridCoord cell, Direction fromMySide)[] GetInputCells()
public virtual bool FeedsCell(GridCoord cell)
```

`GetOutputCells()` returns the cells items actually leave through. For a building declaring an output arrow (`BuildingDefinition.HasOutputArrow`) that is the single cell the arrow is drawn on — the mirror of the entry-arrow rule below, and for the same reason: what is drawn is what happens, and there are no invisible outlets. For one declaring none (Storage, Core, a belt) it is the whole output edge. `GetEdgeCells()` returns every cell touching any of the 4 sides, paired with which side (from this building's own perspective) it touches - used as the `fromDirection` argument to `CanAcceptInput`/`AddInput`. `GetInputCells()` returns the subset items are actually taken from, and it now has **three** shapes, each a different promise:

| Declaration | Takes from | Who |
|---|---|---|
| `HasSingleInputArrow` | **one** cell, on `BuildingRuntime.InputSide`, and nowhere else | Foundry, Constructor |
| `HasInputArrows` | one cell per side other than its output side | Factory, Assembler, Advanced Foundry |
| neither | every edge cell — "input from any side" | Storage, Core |

In all three, an arrow marks a real intake point and there are no invisible ones. The single-input case makes that promise much stronger: a belt touching any other face is **refused**, however full it is. That refusal is enforced twice on purpose - by this list, and again in `ProductionBuildingRuntime.CanAcceptInput` - because the generic push and the belt hand-over both ask the target directly rather than consulting the list.

**`InputSide` is per-building state, chosen at placement.** It defaults to the opposite of the output (what a belt running straight through wants) and is moved with `T` while the placement ghost is up; `SetInputSide` refuses the output side, since a side that both took and gave would feed the building its own production. `SetFacingRotation` puts it back to the default if a rotation leaves it sitting on the new output side - the alternative is a building that silently takes nothing. It travels in the save as `BuildingSaveData.InputSide` (nullable: absent restores to the default, not to North).

`FeedsCell(cell)` answers whether items leaving this building land in `cell` - the question "is this cell fed by that neighbour", which conveyor placement asks to inherit a belt's direction from whatever already flows into it. The default is the whole output edge. **Splitter and Crossroad override it**, and must: neither has a single output side, and both inherit `ExitDirection` from `FacingRotation`, which on those two types names an *entry* (`SplitterRuntime.EntrySide`, `CrossroadRuntime.EntryB`). Any code asking "where does this building output" through `GetOutputCell()` gets a wrong answer for them; ask `FeedsCell` instead.

`TransportSystem` (`Game.Gameplay.Transport`) runs one generic push and one generic pull for every registered building that is not itself belt-driven (Storage, and every `ProductionBuildingRuntime`):

- **Push**, at the building's own `PushIntervalSeconds` (`BuildingRuntime`, default 1s): walks `GetOutputCells()`; for the first neighbor whose `CanAcceptInput` accepts one unit of something in `GetOutputContents()`, transfers it via `TakeOutput`/`AddInput`.
- **Pull**, every tick: walks `GetInputCells()`; for the first neighbor that `HandsOutTo` this building's touching cell and exposes a `PeekPullableItem()` this building's own `CanAcceptInput` accepts, transfers it via `ConsumePulledItem`/`AddInput`. How fast a building may actually absorb what it reads is otherwise its own concern (e.g. `FoundryRuntime`'s or `StorageRuntime`'s own intake cooldown), not a side effect of transport's polling rate - **except** for one structural rule transport itself enforces on top of that, described next.

  **`TransportSystem.RawOutputPullIntervalSeconds`** (1s, matching the fastest conveyor today - 60 items/min): a pull *source* that is not itself belt-gated (not a `ConveyorRuntime`/`SplitterRuntime`/`CrossroadRuntime`) may hand off at most one item per interval via this generic pull path, no matter which or how many consumers want it. A `ProductionBuildingRuntime`'s raw pooled output has no throughput cap of its own - only a conveyor's progress meter naturally throttles delivery - so without this, any consumer sitting flush against one (no conveyor in between) could drain its entire backlog in a single tick. Living here rather than as a per-building intake cooldown makes it immutable: no current or future building type can bypass it by simply omitting one. It does not slow down a conveyor pulling from the same kind of source - `ConveyorRuntime.HasRoomForNewItem` already caps that at the belt's own rated throughput, via the separate belt-specific pull loop below, not this one.

  Three separate things, and conflating them once made a belt line carry four times its rating. **The entry rate** (`RawOutputPullIntervalSeconds`, 1s = 60 items/min = `TransportSystem.ConveyorItemsPerMinute`, the figure the Building menu quotes) is how often something that is *not already a belt* may put an item onto the belt network - through the generic pull, through a belt's own straight-through pull, through a side merge, or through a Splitter's or Crossroad's entry arm, all four checked by `MayEnterBeltNetwork`. The arms were the one way in that did not check it, and drained a non-belt source one item per *tick*; a splitter against a chest emptied it at the frame rate. **`ConveyorSpeedCellsPerSecond`** (1.5) is how fast what is on a belt travels. **`ConveyorRuntime.MaxItemsPerCell`** (3, at `MinItemSpacing` apart) is how tightly items pack once the line ahead is blocked: a jam buffer, never a rate.

  **The rate is set once, at the entry, and never at a seam inside the network.** Past that gate items simply travel, spaced 1.5 cells apart by the rate they entered at, and a belt hands over the instant the next one physically has room. Metering each belt at the line's own rate instead was tried and reverted: it produces the same items-per-minute and makes every item stop dead at every cell boundary, because an item reaches the seam two thirds of a second after entering and a one-second gate makes it wait out the remaining third. What a jam holds is the counterpart: items pack to 4.5 times their free-flow density, and a cleared blockage drains at the belt's own speed rather than trickling back out at the entry rate.

  When two different buildings both include the exact same physical source cell among their own `GetInputCells()` - e.g. two Factories placed on adjacent sides of one conveyor cell, both facing it as their entry - only one can actually take that source's single pullable item on a given tick. Rather than always favoring whichever consumer happens to be registered first (the earlier bug), the pull step resolves this per source with round-robin: it remembers the last consumer served by each source and, when more than one of today's contenders wants that same source, gives it to whichever contender was not served last time. This is unrelated to the different, still-valid upstream/downstream priority described below for two machines reading two different points along one belt line.

One tick therefore runs: building state machines → **every belt advances** → **every building reads its input cells** → **every belt hands over** (back-edge pull, then side merge) → splitters/crossroads → building pushes. The belt phase is split around the building read on purpose. Doing a belt's advance and hand-over together, belt by belt, made the whole line's behavior depend on the order belts sit in the internal list and left no moment where an item is observably parked at the end of a cell - the next belt, visited later in that same pass, took it immediately. A building alongside a *running* line then never saw anything to pick up and was only fed once the line downstream jammed. With the split, order stops mattering and a building takes an item parked at the cell its entry arrow points at before the belt carries it further. Consequence to keep in mind when designing a line: of two machines reading the same belt, the upstream one is served first and the downstream one gets what is left, exactly like the belt's own downstream continuation.

Conveyors keep their own dedicated pull-from-behind-via-Flow + lane-advance logic (a conveyor has no pooled input/output, just the items riding it) and are not part of this generic step.

A conveyor also accepts a **side merge**: when its pull from the back edge finds nothing and it still has a free slot, it takes one item from any building whose own output points into it across one of its two side edges - another belt merging in, or a production building standing alongside dropping its output onto the belt it runs past. The exit edge is excluded, so two belts facing each other head-on never trade the same item back and forth. The side is always the lower priority of the two intakes - the in-line belt is served first every tick, and a merging item only enters when the receiving belt has room (`HasRoomForNewItem`); otherwise it waits where it is.

Both intakes match against the source's own hand-out cells (`FeedsCell`), which for an arrow-declaring building is the one cell its arrow marks.

**Where a source hands out is the source's rule, on every path.** The generic push walks `GetOutputCells()`; the generic pull asks `HandsOutTo` before reaching into a neighbour; the belt intakes ask `FeedsCell`. A building that shows an output arrow therefore hands out there and nowhere else - a box parked against its back, or beside its arrow on a wide edge, is not served.

A building declaring no output side (Storage, the Core, **a belt**) may still be taken from wherever it is touched, which is what those are for: the pull is otherwise the generic one, any of the 4 sides, matching the source project's `Building._try_pull()` (§13). For a belt that is a deliberate tap, not an oversight - a machine may read a line running past its entry arrow, which is the counterpart of the side merge it uses to drop its output onto one, and the layout the round-robin above exists to share fairly.

**What may feed a Storage is a rule about the pair** (`TransportSystem.MayFeedStorage`), and it is the one place a direction is asked of the source rather than left to `HandsOutTo`: a chest takes from **whatever points at it** - a belt of either shape, a Splitter, a Crossroad, or a machine's own output face - and from nothing that merely touches it. The direction belongs here because a belt's open flank is right for a machine and wrong for a chest, which would otherwise drain every line it happened to sit beside.

It said more than that twice, and both were wrong in the same way: straight conveyors only, then the belt network only. Each was an attempt to say "aimed at it" by naming types, and each refused a layout nobody could see a reason for - the first corners, the second a Constructor's output face against a box.

**A chest hands out to a belt leading away from it, and to nothing else** (`MayTakeFromStorage`): the taker must be part of the belt network - a belt, a Splitter or a Crossroad - and it must be taking through its own **back edge**, which is the straight-through pull. A machine standing against a chest may not take from one, and neither may a line merely running past it: the **side merge refuses a Storage source outright**. A chest has no output side to declare, so it answers `FeedsCell` for all four of them, and without that refusal every buffer in a base bled into whatever line passed it. It exposes one unit at a time (`StorageRuntime.PeekPullableItem`) from a cursor that moves on after every unit, so a chest holding three item types hands out one of each in turn instead of emptying its first slot first. The **Core chest** is the one container a conveyor may not connect to in either direction: `RejectsConveyorInput` refuses a belt's delivery and suppresses the hand-out too, because a belt draining it would empty the run's construction reserve onto a line. A builder robot (`TakeInput`) and the player emptying a slot by hand are separate paths and are not affected.

**Several belts leaving one chest share it in turn.** One item per `RawOutputPullIntervalSeconds` leaves a non-belt source, so the belt loop runs in three passes: one offering each source to a belt it did not serve last (`_lastPullServedBy`, shared with the generic pull), one picking up what that deferred - also the only pass a lone belt is served on - and one for the side merges, which stay the lower-priority intake. Before that, the first belt in the registration order took every item and the others never moved.

**All three intake paths ask it**: the generic pull a chest runs for itself, the push a Splitter or Crossroad makes, and the generic push a production building makes across its output cells. The third was missing for a while, which is the one a player meets first - a Constructor parked against a box filled it with no belt anywhere in sight. The Core chest is stricter still and refuses every conveyor (`StorageDefinition.RejectsConveyorInput`), and a builder robot bypasses both through `AddFromRobot`.

## 4. Conveyor configuration

The caller expresses intent; `ConveyorRuntime` (`Game.Gameplay.Buildings`) owns its internal representation (`ConveyorOrientation`: shape, rotation, mirrored). `Mirrored` is never set directly by a caller.

```csharp
public void ConfigureAsStraight(Direction exitDirection)
public void ConfigureAsCorner(Direction entryDirection, Direction exitDirection)
public void ConfigureAsCornerShape()
public void ConfigureAsCrossroadShape()
public void SetRotation(Direction rotation)
```

`Direction` (`Game.Core`) is the cardinal enum (`North`/`East`/`South`/`West`) used throughout the grid/building contracts.

### `ConfigureAsStraight(exitDirection)`

Configures a straight conveyor toward the requested exit.

### `ConfigureAsCorner(entryDirection, exitDirection)`

Configures a corner between the requested entry and exit. `entryDirection` and `exitDirection` must be perpendicular; rotation and chirality (`Mirrored`) are derived internally from a single canonical reference orientation. Throws `ArgumentException` for equal or opposite direction pairs (use `ConfigureAsStraight` for those).

### `ConfigureAsCornerShape()` / `ConfigureAsCrossroadShape()`

Set the shape without implying a direction. Rotation is applied separately via `SetRotation(Direction)`.

### `SetRotation(Direction rotation)`

Applies a rotation to the current shape without changing it. Used both by the two methods above and by construction's rotate-preview flow.

A caller must not depend on an internal conveyor enum/type or directly manipulate internal representation merely to obtain a desired visual/orientation result.

## 5. Splitter configuration

### `ConfigureAsReplacementOf(conveyor)`

Configures a splitter to replace a conveyor while preserving the intended receiving side.

The splitter owns the translation from conveyor orientation semantics to splitter orientation semantics.

### Candidate exit connectivity

`TransportSystem.TryDeliverFromSplitter` only considers a non-entry side a candidate exit when some `BuildingRuntime` occupies its neighbor cell - any concrete type, not a fixed whitelist. A splitter wired directly into a production building (Factory, Foundry, ...) with no conveyor in between delivers to it exactly like it would to a Storage box or another belt piece; `TryDeliverItem`'s own `CanAcceptInput` check is what actually gates whether delivery succeeds.

## 6. ProductionBuilding

Implemented by `ProductionBuildingRuntime` (`Game.Gameplay.Buildings`), extended by `FoundryRuntime` (and, in later phases, Factory/AdvancedFoundry/Assembler). Backed by a `RecipeDatabase` (`Game.Data`) lookup and two `PooledItemStock` instances (input/output) - see §3.

### `GetRecipeIds()`

Returns recipes offered by the production building - leaving out any recipe a research unlocks until one of those researches is completed (`ResearchSystem.IsRecipeUnlocked`, §11).

### `GetSelectedRecipe()`

Returns the active recipe.

### `SetSelectedRecipe(recipeId)`

The sole public entry point for starting or changing the selected recipe.

Changing the recipe while a cycle is running follows the accepted source behavior: the active cycle is abandoned, already-consumed ingredients are not refunded, and the timer resets.

### `GetProductionTime()`

Returns duration for the active recipe.

### `GetRequiredIngredients()`

Returns requirements for the active recipe.

### `GetProgress()`

Returns progress from 0.0 to 1.0.

### `HasRequiredResources()`

Checks requirements for the active recipe.

### `HasResourcesFor(recipeId)`

Checks requirements for another recipe without changing the active recipe.

### `GetState()`

Returns one of the accepted production states:

```text
IDLE
PRODUCING
WAITING_RESOURCES
OUTPUT_BLOCKED
WAITING_COMPUTE
```

### `GetStateLabel()`

Returns the presentation label for the current state.

A cycle takes ALL its ingredients and its recipe's one-shot Compute cost (§10) at once, the instant it starts (the transition into `PRODUCING`) - not progressively as it advances. Power demand (§9) is reported only while `PRODUCING`, based on whether the building was `PRODUCING` at the end of the *previous* tick (the same report-then-settle one-frame lag Power has) - if unpowered, the effective delta passed to the state machine that tick is scaled to 0, freezing an in-progress cycle's timer without losing already-consumed ingredients/Compute. Only Power gates a building's speed; CU is never a continuous draw (§10).

Extractor does not need to implement this player-selected-recipe contract - its production remains fully automatic.

## 7. Selection

Selection owns what is currently inspected and the global UI-panel selection state. Four slots - an inspected **building**, an inspected **construction site**, an inspected **explorer robot**, and a named **global panel** - of which at most one is ever set: opening any of them closes the others.

The public API must support the equivalent behavior of:

```text
Select(building)
SelectSite(site)
SelectExplorerRobot(robot)
Clear()
GetSelectedBuilding()
OpenGlobalPanel(name) / CloseGlobalPanel()
```

and one observable changed-notification per inspection slot.

`Clear()` empties whichever inspection slot was set and notifies **only** that one: a slot that was already empty raises nothing, so a panel never re-runs its close path for a selection it did not hold.

A construction site gets a slot of its own rather than being carried in the building slot. Its segments *are* `BuildingRuntime`s, and are what the grid returns for those cells, so routing one through `Select` would open the panel of the building it is going to become - a production panel over a machine that does not exist yet. What a site is waiting for and what a building is doing are different questions about the same cell.

An explorer robot gets one for the same reason read the other way round: it is **not** a `BuildingRuntime` at all, and every per-building panel keys off the building slot's notification with an `as` cast - so riding that slot would have needed each of them to learn to ignore it. It is also not a grid occupant, so unlike the other two slots there is no cell for the world to mark: a robot is found by distance from the click, not by lookup (`MAP.md` §2.1).

`GameRuntime.IsUIBlockingInput` is true while any of the four slots is set.

**Routing a world click to a panel** is one map, owned by whichever component resolves clicks, and reused rather than copied - notably by the construction site panel's handover (§15). Only building types that actually have a panel may become the selection: selecting one that has none would block world input with nothing able to clear it.

## 8. Construction

Construction exposes intent-level operations rather than requiring callers to manipulate its internal preview state. Implemented as `ConstructionService` (`Game.Construction`), a plain C# class (no Unity/input dependency):

```csharp
public void SelectBuilding(BuildingDefinition definition)
public void Cancel()
public void SetPreviewRotation(Direction rotation)
public bool CanPlace(GridCoord cell)
public PlacementRefusalReason GetPlacementRefusalReason(GridCoord cell)
public bool TryPlace(GridCoord cell, Direction rotation, out ConstructionSiteRuntime site,
    ConstructionSiteRuntime conveyorRunSite = null)
public bool TryCancelPendingAt(GridCoord cell)
public bool TryRedirectExistingConveyor(GridCoord cell, Direction rotation, out ConveyorRuntime redirected)
public bool TryDemolish(GridCoord cell, out BuildingRuntime removed)

public bool CanAfford(BuildingDefinition definition)   // the placement gate, and the menu's styling
public int GetAvailableAmount(string itemId)           // reads GlobalStock's aggregate (§15)

public int BuildingCap { get; }              // 36 by default, raised by BuildingCap research effects (§11)
public int OccupiedBuildingSlots { get; }    // live count against BuildingCap
public void RestoreBuildingCap(int? cap)
```

`TryPlace` no longer pays for anything and no longer produces a working building: it opens a **construction site** (§15). The `BuildingRuntime` is instantiated and occupies its grid cells immediately - so nothing else can be placed on top of it and a conveyor drag can keep reshaping its anchor - but it carries `IsUnderConstruction` and is deliberately not registered with `TransportSystem`, and has no view, so it neither ticks, transports nor produces until robots have delivered its full cost.

Owning ground and being operational are two different states, and the flag is what keeps them apart everywhere. Not registering a segment stops it ticking, but transport resolves its **neighbours** through `Game.Grid`, where a chantier does sit: `TransportSystem` therefore reads the grid through one predicate (`ActiveBuildingAt`) that returns nothing for an unbuilt segment, so nothing is handed to it, nothing is drained from it, and a Splitter does not count its cell as a usable exit.

**A segment becomes operational when it has assembled, not when its last item landed.** Delivery fills the bill; the segment then physically assembles at `SegmentAssembly.RateFor(footprint)` (`Game.Gameplay.Sites`), and only on reaching 1 does it clear `IsUnderConstruction`, register with `TransportSystem` and raise `SegmentMaterialized`. That clock lives in Gameplay for exactly this reason, and `ConstructionSiteVisualSync` reads it rather than running one of its own - `NanoConstructionSettings` keeps only the look of the effect. The two rules together are what a player means by "in construction": before this, an unbuilt powerplant collected coal through its whole construction, then supplied current for the five further seconds it spent visibly materialising. Passing `conveyorRunSite` appends the cell to that existing site instead of opening a new one: a whole conveyor/splitter drag is **one** chantier, not one per segment.

`TryDemolish` removes the building immediately (the player wants the space back) but refunds nothing anywhere: the cost becomes a repatriation job a robot must physically haul back (§15). `TryCancelPendingAt` is the counterpart for something still pending, and is what the demolition input routes to when the clicked cell belongs to a chantier rather than a finished building. It is scoped to the **one segment** under the cursor: that segment's cost comes off the bill, the earmarks it alone justified go back, and its ground is freed, while every sibling of a dragged run keeps its own cell and its own share. A single building is one segment, so for it this cancels the chantier outright; a run left with no segment at all is closed the same way.

**Overtaking.** The occupancy check lets a Conveyor/Splitter/Crossroad be placed onto belts already laid instead of forcing a demolition first, and `TryPlace` settles what each overtaken cell owes - after the placement is known valid, never before, so a refused placement costs the player nothing:

- a belt **still pending** was paid for by nobody, so it simply leaves its chantier - *only it*, its cost off the bill and the earmarks it alone justified released. The rest of a dragged run keeps its own ground; cancelling the whole chantier there deleted a run the player was still laying, which is what a second conveyor drag started on the end of the first used to do. A site left with no segment at all is closed exactly like a cancelled one.
- a belt **already built** is being demolished, so its cost becomes a repatriation job like any other demolition. Dropping it where it stood destroyed the material silently, which is the one thing demolition is careful never to do.

`TryRedirectExistingConveyor` is what the input layer reaches for first, and it is not a placement at all: dragging a belt across a belt already built re-points that belt in place. Nothing is spent, no site is opened, nothing is demolished, and the items riding it keep riding it. Every gate still applies except affordability - a redirect spends nothing, so an empty chest is no reason to refuse turning a belt the player already owns. A pending belt is never redirected: it is somebody's chantier, and turning it would build something nobody asked for.

`CanAfford`/`GetAvailableAmount` both read the §15 aggregate, unreserved stock only. `CanAfford` is read twice over: by the Building menu for its "you can/cannot pay for this yet" styling, and by the placement gate itself (`PlacementRefusalReason.CannotAfford`), so the greyed-out card and the refused click can never disagree about what is affordable.

`TryDemolish` refuses (returns `false`, `removed` stays `null`) for the Core, for the Core chest fixture (matched by definition id `core_storage`, since a restored instance is just an ordinary entry in the save's building list, not a tracked reference), and for any building that is still a pending site's unbuilt segment. The first two are world-generated fixtures the player never placed and must never be able to remove, the chest especially since its contents would otherwise be lost for good (only the construction *cost* is ever repatriated, never a building's own held contents).

`OccupiedBuildingSlots` counts every registered building except the Core, the Core chest fixture (a world fixture, not a player decision) and Conveyor/Splitter/Crossroad, **plus** every pending construction site of a slot-consuming type - a site takes its slot from the moment it is placed, otherwise the cap could be walked straight past by queueing sites faster than robots can serve them.

`TryPlace`/`TryDemolish` only mutate `Game.Grid`/runtime state and return the affected `BuildingRuntime` via `out`; they never create or destroy GameObjects. The caller (a Presentation-layer input adapter) is responsible for the corresponding view, which is what keeps `Game.Construction` free of a dependency on `Game.Presentation`. `CanPlace` is a non-mutating query used for ghost-preview valid/invalid tinting.

`GetPlacementRefusalReason` (TASK_04_PLAFOND_RAYON.md §3.2) is the explanatory counterpart to `CanPlace`: same checks, same order, but returns a `PlacementRefusalReason` (`None`/`NotUnlocked`/`OutOfActionRadius`/`CannotAfford`/`BuildingCapReached`/`CellOccupied`) instead of a bare bool, for player-facing messaging - meaningful only while `Selected != null`. `CannotAfford` reads the aggregate **minus what other sites have already reserved**, so placing four buildings with stock for three refuses the fourth rather than letting four sites fight over one stock afterwards. Placing still does not pay - it opens a site that reserves the whole bill and waits for robots to carry it - but the bill must be coverable at that instant, which is what makes a placed site's `missing` count zero in ordinary play.

`BuildingCap` (TASK_04_PLAFOND_RAYON.md §3) is runtime state owned by `ConstructionService`, not any definition: starts at 36 and is raised by `BuildingCap` research effects (§11) - the highest target completed wins, so one landing late never lowers it - (via `ResearchSystem.ResearchCompleted`, same pattern as `DataCenterRuntime`'s bay/threshold subscriptions), and restored directly from a save (`RestoreBuildingCap`) rather than re-derived from `ResearchSystem.IsUnlocked`. `OccupiedBuildingSlots` counts every building currently registered with the constructor-injected `TransportSystem` except the Core and every `ConveyorRuntime`/`SplitterRuntime`/`CrossroadRuntime` - computed live from `TransportSystem.GetAllBuildings()`, never a separately tracked counter, so placing and demolishing can never drift out of sync with it. `IsPlaceable`'s cap check applies to every other building type.

The Core's action radius (`CoreDefinition.ActionRadiusCells` is only the starting value) is runtime state on `CoreRuntime.ActionRadiusCells` instead - `IsWithinActionRadius` reads that, never the definition. `CoreRuntime` owns and extends it through `ActionRadius` research effects (§11) - the highest target completed wins - (`ResearchSystem.ResearchCompleted`), exactly like `BuildingCap` above. The highest target any research carries is the Core's furthest reach, `GameRuntime.FurthestActionRadiusCells` - derived by `ResearchCatalog.HighestActionRadius` and never written down a second time - which world generation reads as the edge of the starting territory (`MAP.md`) and the ground coverage sizes its texture on; the invitation ore clusters must lie within the lowest one; `WorldGenerator.ActionRadiusCells` is a plain pass-through of it.

Drag-gesture decoding (turning a mouse drag into a sequence of single-cell `TryPlace` calls, and detecting when to reshape the drag anchor into a corner) is input-interpretation and lives in the Presentation-layer input adapter, not in `ConstructionService` itself, which stays single-cell and Unity-input-agnostic.

The construction system owns preview/ghost state and placement orchestration.

## 9. Power

Implemented by `PowerSystem` (`Game.Gameplay.Power`), owned by `GameRuntime`:

```csharp
public bool TryDraw(string groupId, float kilowatts)
public void ReportSupply(float kilowatts)
public void Settle()
public bool IsPowered()
public float DemandOf(string groupId)
public float AllocatedTo(string groupId)
public int AskingInstancesOf(string groupId)
public int ServedInstancesOf(string groupId)
public PowerPriorityOrder Priority { get; set; }
```

Report-then-settle: consumers draw and sources report during their own tick; `Settle()` (called once per `GameRuntime.Update()`, before that tick) turns the previous frame's reports into `SettledDemand`/`SettledSupply` **and into this frame's per-group budgets** - one frame of intentional lag. Recovery is automatic the instant demand drops back at/under supply, with no cooldown.

**Power is allocated by building type, in the order the player arranges.** It used to be one boolean for the whole base (`SettledDemand <= SettledSupply`), so a shortage stopped every powered building at once - which is a dead end rather than a setback: with the Data Center among them, CU production stopped too, and without CU nothing can be built or burned to get out of it. `Settle` walks `Priority` (§9a) and gives each group all of its demand or whatever is left of supply, whichever is smaller, so **the shortage falls on exactly one group** - the one the running total crosses - and everything below it gets nothing.

`TryDraw` both reports and answers, deliberately: a building that asks counts towards the next frame's demand whether or not it runs this one, and splitting the two invites a caller to do one without the other. `BuildingRuntime.ComputeEffectivePerformance` is the single caller for every building whose progress freezes while unpowered, and it draws against `Definition.Id`.

**Partial service is per instance, not per building's speed.** A group allocated 1 kW of the 2 kW it asked for runs one of its two extractors rather than both at half speed: within a group, instances draw until its share is spent, in the order they tick. A machine is either running or it is not, which is the only thing the rest of the simulation knows how to represent. `ServedInstancesOf`/`AskingInstancesOf` are counted at the draw, not divided out of the kilowatts - a group's instances need not draw the same amount (the Data Center's demand is its installed components') and an instance takes its whole demand or nothing.

**Nothing is served off the top.** The Gas Powerplant used to declare 2 kW of self-consumption, reported unconditionally and never gated - a load nobody could switch off and that this system had no way to arbitrate. It was removed rather than modelled as overhead: a plant draws nothing from the network it feeds.

The UI reads the settled totals and the per-group figures through this contract and must not inspect individual building private power fields.

### 9a. Power priority order

`PowerPriorityOrder` (`Game.Gameplay.Power`), owned by `GameRuntime.PowerPriority` and read by `PowerSystem` at every settle. **An order of type identifiers, never of positions** - that is what makes the list extensible without a migration.

Seeded from `GameRuntime`'s building catalogue, never from a list written by hand, so a type added to the game appears in it without any screen or contract being edited. It holds **every** known type, including ones never built: a screen may filter what it draws, the order underneath does not, which is what stops the list reshuffling itself the day a new type is built. A type the order has never heard of ranks after everything in it.

Three restore rules, and they are the whole claim (`PowerPriorityOrderTests`):

- a saved identifier the game still has keeps its place;
- a type the game has and the save does not - added since - goes to the bottom;
- a saved identifier the game no longer has is dropped silently, because a removed building type is not an error in somebody's save.

It travels as `SaveData.PowerPriority` (a list of ids; absent restores as the catalogue's own order, so no version bump - the same per-field tolerance `BuildingCap` uses).

## 10. Compute

Implemented by `ComputeSystem` (`Game.Gameplay.Compute`), owned by `GameRuntime`:

```csharp
public void Grant(float amount)
public bool CanSpend(float cost)
public void Spend(float cost)
public float SpendUpTo(float maxAmount)
public void Tick(float deltaTime)
```

CU is a pooled reserve (`Reserve`, capped at `ReserveCap` = 70000, starting full) credited by `Grant`. It is a **currency, not a flow** for every spender but two:

- Every production cycle - a recipe-based production building (its recipe's `ComputeCost`, §6), Extractor, and Gas Powerplant (their own `BuildingDefinition.CuCostPerCycle`, per extraction/per unit of fuel burned respectively) - still pays in a **single one-shot chunk the instant the cycle starts**, via `CanSpend`/`Spend`. There is no throttling ratio for these: a cycle either affords itself in full or waits at 0 progress. A powerplant that cannot pay does not light its fuel, and therefore supplies no Power that tick.

  **Power plants ask first.** `TransportSystem.Tick` runs their state machines in a pass of their own before every other building's, so a base that has spent itself down to nothing relights its plants rather than handing the first CU that comes back to whichever factory happened to be registered first. A plant produces the current the rest of the base runs on; it is the one consumer that must not queue behind its own dependants.
- **Research** (`Game.Gameplay.Research.ResearchSystem`, §11) and **Data Center priming** (`Game.Gameplay.Buildings.DataCenterRuntime`, TASK_03_DATACENTER.md) are the two continuous per-second draws, both via `SpendUpTo(maxAmount)`: it withdraws up to `maxAmount`, less if the reserve holds less, and returns how much was actually taken - it never goes negative and never throws. This is a deliberate, documented exception to "CU is a currency, not a flow" (CONTRACTS.md §13 contract evolution, first for research in TASK_02_REFONTE_RECHERCHE.md, extended to priming in TASK_03_DATACENTER.md): a freshly placed Data Center spends 90s absorbing 1500 CU at a fixed rate before producing anything, and pauses at zero CU exactly like research - progress is a running total that is never rolled back. `SpendUpTo` has exactly these two callers; every other spender keeps using `CanSpend`/`Spend`.

Two sources credit the reserve:

- the **Core**, whose `CuOutput` is 0 in the current data - it produces no CU (the `CuOutputIntervalSeconds` mechanism remains in code but has no effect at this value);
- a **Data Center**, which credits its installed components' output for the duration of each tick, only while powered, and only once primed (`DataCenterRuntime.IsPriming`) - split across its research/buildings axes by a concentration-based yield curve (see the Data Center's own subsystem behavior for the exact formula), though both axes currently credit this same single reserve.

`Tick(deltaTime)` (called once per `GameRuntime.Update()`) only advances the window `IncomePerSecond` is averaged over - the credited-CU-per-second figure the UI shows. Anything granted above the cap is discarded, and `IncomePerSecond` counts only what was really credited.

The UI reads `Reserve`/`IncomePerSecond` through this contract for the general CU display; it must not show a continuous consumption figure there, because outside of research and priming there is none. The two legitimate continuous-rate figures are research's own absorption ceiling/estimated time remaining (§11) and the Data Center's priming progress, which the UI reads from `ResearchSystem`/`DataCenterRuntime` respectively, not from `ComputeSystem`.

## 11. Research

Implemented by `ResearchSystem` (`Game.Gameplay.Research`), owned by `GameRuntime`. CU/absorption model (TASK_02_REFONTE_RECHERCHE.md), not RP/laboratories:

```csharp
public bool HasActiveResearch()
public ResearchDefinition GetActiveResearch()
public float AbsorbedCu
public float GetProgress()
public float GetEstimatedSecondsRemaining()
public IReadOnlyList<ResearchDefinition> GetQueue()
public ResearchSystem(ComputeSystem computeSystem, ResearchCatalog catalog = null)
public bool IsUnlocked(string researchId)
public bool IsBuildingUnlocked(BuildingDefinition building)
public bool IsRecipeUnlocked(RecipeDefinition recipe)
public ResearchDefinition Definition(string researchId)
public IEnumerable<string> GetUnlockedIds()
public void Grant(string researchId)
public bool ArePrerequisitesMet(ResearchDefinition research)
public bool CanQueue(ResearchDefinition research)
public bool Enqueue(ResearchDefinition research)
public bool Dequeue(ResearchDefinition research)
public bool ReorderQueue(int fromIndex, int toIndex)
public void CancelActive()
public void Tick(float deltaTime)
public event Action<string> ResearchCompleted
```

A research never defines a duration, only a total cost (`ResearchDefinition.CuCost`) and an absorption ceiling (`AbsorptionRatePerSecond`). Duration is the consequence: `cost / min(absorptionRatePerSecond, what the reserve can currently give)`. `ResearchSystem` holds a `ComputeSystem` reference (constructor-injected) and draws from it every `Tick` via `ComputeSystem.SpendUpTo` (§10) - never more than the research's own ceiling, never more than the reserve currently holds. Progress (`AbsorbedCu`) is a running total that is never rolled back: at zero reserve the draw for that tick is simply zero, which is what makes "pause without loss" a consequence of the model rather than a special case to implement. This is the one documented exception to CU being a pure one-shot currency (§10).

One active research at a time (`ActiveResearch`); everything else waits in a reorderable queue (`GetQueue`/`ReorderQueue`/`Dequeue`). `Enqueue` starts a research immediately if nothing is active, otherwise appends it to the queue; either way it first checks `CanQueue` (not unlocked, not already active/queued, every prerequisite met - **not** CU availability, which is never a precondition to queueing, only to progressing). When the active research completes, `Tick` pulls the next one off the head of the queue on the following tick (same one-frame-lag convention as Power/Compute's report-then-settle). `CancelActive` abandons the active research, discarding its absorbed CU - the same "switching abandons the cycle without refunding" precedent `ProductionBuildingRuntime.SetSelectedRecipe` already establishes.

A research may require any number of other researches to be completed first (`ResearchDefinition.Prerequisites`, a list - not the single-reference chain of the old RP model). `ArePrerequisitesMet` is the read-only form the UI uses to show *why* a row is unavailable instead of only greying it out, and to highlight specifically which prerequisites are missing when a locked node is clicked.

`ResearchDatabase.GetCores()` lists the roots of the research network (GDD §5.4) - Research, Buildings, Armament. A core is never bought: it is an ordinary unlock id granted through `Grant` by whatever powers it, and a research belongs to a branch by naming its core as a prerequisite. `DataCenterRuntime` grants the cores its definition lists (`DataCenterDefinition.PoweredCores` - Research and Buildings) once its priming is done; nothing grants the armament core yet. `GetAll()` excludes the cores, so nothing lists or counts them as research; `Get(id)` still finds them. **The research menu exists only once a core is powered** (`ResearchPanelController.IsAvailable`) - the Top Bar card and the Bottom Nav entry alike; before that the introduction runs on the Core directives alone. A research left out of the database is on no branch and cannot be reached. **Where a node sits on the network is data, not layout**: `Tier` (its ring) and `Angle` on the research's own asset, placed by hand. `ResearchNetworkPlacement` (`Game.Data`) is the one conversion between them and a position - a ring every step from the centre, the angle counter-clockwise from the right - so what draws the network and what places it read it the same way. Nothing computes, spreads or corrects a position.

`ResearchDatabase` (`Game.Data`) is the id-keyed registry, on the same model as `ItemDatabase`/`RecipeDatabase` - one asset assigned on `GameRuntime`, `Get(id)` for a single lookup, `GetAll()` for the UI to enumerate the whole tree.

**A research carries its effects** (`ResearchDefinition.Effects`, a list of `ResearchEffect`), and nothing else carries them: a building or a recipe does not name the research that opens it, the research names it. The list is closed - `UnlockBuilding` (a `BuildingDefinition`), `UnlockRecipe` (a `RecipeDefinition`), `ActionRadius` and `BuildingCap` (target values: the highest completed wins, so completion order never matters) and `DataCenterBayPairs` (a number of pairs, summed). `ResearchCatalog` (`Game.Data`) indexes every research the game knows - the tree, its cores, and the unlocks the Core directives grant, which are researches too - by id and by what it unlocks; `GameRuntime` builds it once and hands it to `ResearchSystem`.

Gates ask `IsBuildingUnlocked` (`ConstructionService.GetPlacementRefusalReason`, the building menu) and `IsRecipeUnlocked` (`ProductionBuildingRuntime.GetRecipeIds`): a type no research names is not gated, one that is named opens when any research naming it is completed. The figures are applied by their owners on `ResearchCompleted`, reading the completed research's effects through `Definition(id)`: `CoreRuntime` the radius, `ConstructionService` the cap, `DataCenterRuntime` the bays - including, for a Datacenter built later, those of every research already completed (`GetUnlockedIds`). `ResearchCatalog.HighestActionRadius(startingRadius)` is the highest radius target any known research carries - the Core's furthest reach (§8). **No system compares a research id.** An id is how a save names an unlock and how `ResearchCompleted` reports one, never what an effect is keyed on: renaming a research cannot silently detach its effect.

`ResearchTreeValidation` (`Game.Data`, pure) holds the two checks a tree cannot recover from. `FindCycles(researches)` returns every research on a prerequisite cycle, one requiring itself included. `FindUnreachable(cores, researches)` returns every research that can never be unlocked from the cores - read strictly: every non-null prerequisite must be a core or reachable itself, and a research with no prerequisite is not a root, only the cores are. The shipped tree is held to both by a test that reads the assets, and in the editor and development builds `GameRuntime` runs both on the database at startup and logs an error naming any research they find.

## 12. UI contract

UI is a consumer of runtime contracts.

UI must not:

- read private inventories
- read private timers
- read internal production flags
- mutate Grid directly
- determine occupancy from Tilemap
- infer gameplay state from SpriteRenderer state

UI may:

- request a public state
- issue an allowed command through a public runtime API
- display static definition data
- react to selection/state notifications

### 12a. Escape

**`GameRuntime.Escape` is the only reader of the Escape key, and the only place the priority between the things Escape can close is written down.** A consumer asks `IsClaimedBy(EscapeClaimant)` with its own tier and acts only if it gets `true`; it must not read the key itself, and it must not infer its turn from its own state alone.

The stack is **armed construction tool, then contextual panel, then global panel**. `EscapeArbiter.Claimant` derives it from `ConstructionService.Selected` and `SelectionRuntime`'s slots, so exactly one tier is ever the claimant - there is no consumed flag, and no dependence on which component's `Update` runs first. `EscapeClaimant.None` is a description of the state and never something a consumer can claim.

Two consequences a new consumer has to know. A contextual and a global panel cannot both be open (§7's mutual exclusion), so their relative order never decides anything today - it is stated so that it stays decided here if that changes. And an armed tool **can** coexist with an open panel, since nothing disarms a tool when a panel opens: a consumer serving the `ArmedTool` tier must therefore not sit behind a gate on `IsUIBlockingInput`, or the key is awarded to a reader that never runs.

### 12b. Input bindings

**Every keyboard shortcut is an action in one table, and nothing reads a key directly.** The table is `InputSystem_Actions.inputactions`, the Input System's **project-wide actions asset** (named from `ProjectSettings/ProjectSettings.asset` and `EditorBuildSettings.asset` - two references that do not look like code). `InputBindings` is the only way in: `Find(name)` resolves an action, `IsPressed`/`WasPressedThisFrame` read it null-tolerantly. A consumer resolves once in `Start` and holds the reference; `FindAction` walks the maps and has no business running per frame.

`InputActionCatalogue` names every reassignable action once, with its French label and its section, in display order. **The names appear both there and in the asset, and that is the one duplication here that could not be designed away** - the asset format has nowhere to put a label. `InputBindingTableTests` asserts the two sets are exactly equal in both directions, so an action added to one and forgotten in the other fails the suite instead of reaching play as a blank row or a dead shortcut.

**The table is keyboard-only, and a test enforces it.** The mouse buttons and the wheel are read straight from the device and are deliberately not reassignable: the click/drag arbitration is a contract between `CameraPanController`, `BuildingSelectionInput` and `ConstructionInputAdapter` sharing one slop threshold, and a reassignable button could produce a configuration in which clicking selects nothing. The map's pointer drag could not be in the table even if it were reassignable - it is a UI Toolkit `PointerDownEvent`, not an Input System read - and it is pinned to the left button by a named constant. The intro and Genesis screens answer to any key at all, which is not a binding.

**The camera and the map share four pan actions rather than owning four each.** They always read the same physical keys, and used to hold two literal copies of them; reassigning "vers le nord" now moves both, which is what reassigning it means.

`Game.Save.PreferencesService` owns `preferences.json`, **beside the saves and never inside one**: a keyboard layout belongs to the person playing, not to the run, so it has to survive starting a new game and must not travel with a save file. One JSON object, one key per concern (`inputBindings` holds the Input System's own override blob, opaque here). An absent or unreadable file means "no preferences", which is the truthful default; writing an empty override set **erases** the key rather than keeping the last non-default value.

**Reassigning goes through `ShortcutsPanel`** (`Game.UI`), and two of its pieces are public surface. `ActionSharingTheKeyWith(actionName)` names the other catalogue action currently on that key, or null - it compares **effective** paths, so two actions agreeing only on their asset defaults are a clash while one whose override moved it away is not, and several unbound actions do not clash with each other. `InputBindings.Suspend`/`Resume` turn the whole table off **for as long as the shortcuts screen is open**, not only during a capture: `PerformInteractiveRebinding` refuses to run on an enabled action, the key being assigned must not also do its old job, and in game the screen sits over a running world where a live shortcut would act behind it. `Show` suspends, `Hide` resumes, and that is the only path that restores them - so **Escape does not close the screen**, being an action itself. A capture still cancels on Escape, which the rebinding operation handles below the action layer. An **empty override path** is how an action is left with no key, which is what overwriting a shortcut does to the row that held it.

**`InputBindings.ApplyStoredOverrides` clears every override before applying.** Domain Reload is disabled (`DEVELOPMENT_RULES.md` §5), so the actions asset instance survives Play sessions - applying on top of what was left would let a session's unsaved reassignment leak into the next one. Idempotence is the requirement, not a nicety.

## 13. Contract evolution

Changing a public contract is an architectural change.

When a contract changes:

1. identify all consumers globally;
2. update the contract documentation;
3. update affected tests;
4. verify dependency direction;
5. report any behavior change explicitly.

## 14. Save / Restore

Named saves (`Game.Save`, `Assets/Scripts/Save/`): one folder per save name under `Application.persistentDataPath`, each holding a `save.json` (`SaveService.FolderFor`/`PathFor`). `Save(data, name)` writes one, `Load(name)` reads one, `List()` enumerates them most-recently-written first, and `Exists(name)` answers whether a name is taken.

**A save name is the one place in this project where a string a person typed becomes a filesystem path**, and `SaveService.Sanitise` is what stands between the two. It **drops** rather than escapes: directory separators, the platform's invalid filename characters and control characters are removed, so two names cannot collapse onto one folder through an encoding scheme nobody remembers; trailing dots and spaces go too, because Windows strips them silently and `"Partie."` and `"Partie"` would otherwise be one folder wearing two names; `.`, `..` and anything left empty fall back to `SaveService.DefaultName`. A name that survives it addresses exactly one folder inside the save root and cannot climb out of it.

The single `save.json` that used to sit at the root of `persistentDataPath` predates this. It is neither read nor deleted - left where it is rather than migrated under a guessed name.

`SaveService.Load` refuses a save whose `Version` does not equal `SaveData.CurrentVersion`, returning `null` exactly like a read/parse failure (TASK_03_DATACENTER.md's decision) - it does not attempt to load an old-format save with defaults filled in. A per-building blob missing an individual key still falls back gracefully (see below); `Version` is the coarser, all-or-nothing gate for a change too structural for that.

The save is written and read by the JSON library alone. `SaveData` and its nested records are therefore deliberately **not** `[Serializable]`: nothing passes them through Unity serialization, and claiming otherwise only misdescribes the three `JObject` fields and the nullable `int`, none of which Unity can serialize. `[NonSerialized]` must never be used to quiet that either - the JSON library honours it and would silently drop those keys from every save. The file's shape (its key set, its values, and the fact that a null keeps its key rather than vanishing) is pinned by `SaveFormatTests`; its key *order* deliberately is not, JSON having none.

`Game.Save` has no dependency on any other `Game.*` assembly (only on the JSON library) - it never reads private state itself. Every system capable of holding meaningful runtime state exposes a `Capture`/`Restore` pair as a public member of that system, and only `GameRuntime` (`Game.Presentation`) calls them, assembling/consuming a `Game.Save.SaveData`:

```csharp
public JObject CaptureState()          // BuildingRuntime and every override
public void RestoreState(JObject state)

public void RestoreContents(IReadOnlyDictionary<string,int> contents)   // PooledItemStock
public void RestoreReserve(float reserve)                               // ComputeSystem
public void RestoreState(ResearchDefinition active, float absorbedCu,
    IEnumerable<ResearchDefinition> queue, IEnumerable<string> unlockedIds) // ResearchSystem
public IEnumerable<string> GetUnlockedIds()                             // ResearchSystem
public void RestoreState(CoreRuntime core, GridCoord coreOrigin,
    IEnumerable<DepositRuntime> deposits)                               // WorldGenerator
public IEnumerable<BuildingRuntime> GetAllBuildings()                   // TransportSystem
public BuildingRuntime CreateForRestore(BuildingDefinition definition,
    GridCoord cell, Direction rotation)                                 // ConstructionService
public void RestoreBuildingCap(int? cap)                                // ConstructionService
public void Restore(float? elapsedSeconds)                              // PlayClock
```

**`ObservationRuntime` has no `Capture`/`Restore` pair, and that absence is the contract** (`MAP.md` §2). Which cells are being observed is recomputed every frame from where the observers are, so there is nothing to round-trip: a loaded game rebuilds the whole field on its first frame from the Core and the robots the load put back. Adding a save field for it would create a second source of truth able to contradict the observers' real positions - and it would need clearing, which is the defect that shape of code always produces. `ObservationRuntimeTests` pins the half that could break silently: what discovery captures is byte-identical whether anything is watching or not.

`SaveData.ExplorerRobots` is a `JObject` blob of the same kind, owned by `Game.Gameplay.Exploration.ExplorerRobotSystem` (`MAP.md` §2.1): per robot its position, heading, state, drift phase and sortie count. No `Version` bump - additive with a per-field fallback, and an absent key restores as a fleet standing at the base, which is the truthful default rather than a convenient one: a robot nobody has sent anywhere is at home. A blob listing fewer robots than the configured fleet restores the rest at home too.

`CoreRuntime`'s own `CaptureState`/`RestoreState` (TASK_04_PLAFOND_RAYON.md §6) now also round-trips `actionRadiusCells` alongside `cuTimer`/`contents` - absent falls back to `CoreDefinition.ActionRadiusCells`, never to 0. `SaveData.BuildingCap` (nullable) is the matching top-level field for `ConstructionService.BuildingCap`, restored via `RestoreBuildingCap`; absent falls back to `ConstructionService.DefaultBuildingCap` (40). Neither addition bumped `SaveData.Version` - both are simple additive fields with a per-field fallback, not the kind of structural reshaping the Version gate exists for. `SaveData.PlayTimeSeconds` (nullable, `Game.Gameplay.Session.PlayClock`) is a third of the same kind: how long the run has been played, in simulated seconds; absent restores as a run starting its count, never as one that lasted zero seconds. `DepositSaveData` lost its `RemainingQuantity` for the opposite reason: a deposit never runs out (ALIGNEMENT_PROJET.md §8), so it holds no mutable state and there is nothing to round-trip - only where it is and what it is. No `Version` bump either: an older save's key is simply ignored, which is exactly right now that the answer is "infinite" whatever number it carried.

`BuildingRuntime.CaptureState()`/`RestoreState(JObject)` are virtual, empty by default; each subclass with real mutable state overrides both (`ProductionBuildingRuntime` and its subclasses, `StorageRuntime`, `ConveyorRuntime`, `CoreRuntime`, `ExtractorRuntime`, `PowerplantGazRuntime`, `DataCenterRuntime`, `SplitterRuntime`, `CrossroadRuntime`). A building's envelope (`Definition.Id`, `Cell`, `FacingRotation`) is captured generically by `GameRuntime`, not by the building itself - only its type-specific payload goes through `CaptureState()`.

`ConstructionService.CreateForRestore` is the one other caller of the definition→runtime factory besides `TryPlace`: same instantiation switch, but with no cost deduction and no placement validity check (both already happened once, at the original construction the save captured). It relies on the caller having already placed any deposit a restored Extractor sits on, exactly like `TryPlace` relies on `IsPlaceable` having checked that beforehand.

`GameRuntime` resolves a saved `BuildingDefinition` id back to its asset via a small serialized catalog (`buildingCatalog`) populated in the Inspector, following the same "id → asset" pattern as `ItemDatabase`/`RecipeDatabase` - there is no separate `BuildingDatabase` elsewhere in the project. A saved `ResearchDefinition` id is resolved through `ResearchDatabase.Get(id)` instead (§11) - one real database, not a second ad hoc catalog.

Trigger points (both in `GameRuntime`, `Game.Presentation`):

- **New Game** (`MainMenu.unity` → `Bootstrap.unity` with `PendingGameStart.LoadedSave == null`): generates the world exactly as before, then writes the save immediately - "New Game generates a save" per the menu's own contract.
- **Save** (the in-game menu, `GameRuntime.SaveAs`): recaptures every system's current state and writes it under the chosen name. It is the only thing that brings a save up to date - **nothing is written on quit**, whether through the menu's Quit or by closing the window.
- **Load** (`MainMenu.unity` → `Bootstrap.unity` with `PendingGameStart.LoadedSave != null`): `GameRuntime.Awake()` restores every system from the save instead of generating a new world, in dependency order (Core → deposits → every other building, since an Extractor resolves its deposit from whatever already occupies its cell).

`SaveData.Discovered` round-trips the discovery state (`DiscoveryRuntime.CaptureState()`/`RestoreState(string)`). It is a **string**, not a `JObject`, for two reasons: `Game.Grid` references only `Game.Core` and `Game.Data`, and giving it a JSON type would add a dependency it does not otherwise need - the same reasoning as the plain-value round-trips above. And the file is written `Formatting.Indented`, so one entry per cell would run to megabytes on a large map. The encoding is run-length (`state:length` pairs, comma-separated, row-major), which costs a few hundred characters for a map that is mostly unknown.

Restore is tolerant like every other: a null, an empty string or a malformed run leaves the rest of the map unknown rather than throwing, and a run past the end of the map is ignored. A save predating the field therefore loads as an undiscovered map and the Core's radius writes its own disc back on the first tick. No `Version` bump - an additive field with a per-field fallback.

`SaveData.DecorRemoved` round-trips what the player has cleared of the wild decor (`DecorRuntime.CaptureState()`/`RestoreState(string)`), as a comma-separated list of cell indices. A string, for the same `Game.Grid` dependency reason as `Discovered`. **Only the removals are stored**: what grows is a pure function of the seed and re-derives itself at load, so storing it would be storing what the seed already says — but the seed cannot say that a rock was cleared to make room for a building, and without the delta set that rock returns the moment the camera's decor window leaves and comes back. Nothing is recorded for ground that grew nothing, and nothing is recorded for a cell an ore deposit covers (that is filtered live, so the ground comes back when the deposit is mined out). Restore is tolerant; a save predating the field loads as a world nobody has cleared anything in, which is exactly what it recorded. No `Version` bump — an additive field with a per-field fallback. See [`TERRAIN.md`](TERRAIN.md) §4.

`SaveData.WrecksDiscovered` round-trips which wrecks the player has found, as comma-separated indices (`WreckField.CaptureState()`/`RestoreState(string)`). **Only the discovered set**: where a wreck is and which of the three sprites it draws are pure functions of `SaveData.TerrainSeed`, re-derived at load, so storing them would be storing what the seed already says. Restore is tolerant - an absent value is a world nobody has found anything in, and an index the current ring settings no longer produce is ignored rather than throwing, so changing a ring's count costs the wrecks that no longer exist and not the save. No `Version` bump: an additive field with its own fallback. See [`MAP.md`](MAP.md) §4a.

`SaveData`'s four terrain fields (`TerrainSeed`, `TerrainSize`, `TerrainScale`, `TerrainProportion`) are captured from the **running world** (`GameRuntime.Terrain`), never from the settings asset. The two agree on a fresh game and diverge on a loaded one, which runs on the values its save carried. Terrain is not stored anywhere - it is re-derived from exactly these four numbers - so writing the asset's values back would re-stamp a save with whatever the asset happens to say today, and regenerate a different world underneath buildings already placed.

**`WorldGenerator` owns every deposit, and `AddDeposit` is the only way to add one after generation.** It places into `Game.Grid` and registers in `OreDeposits` in one call, then raises `DepositAppeared` so presentation can spawn the view. A caller must not reach for `GridRuntime.PlaceDeposit` itself: it returns the runtime it created, and dropping that return value leaves a deposit that the grid knows about while the view and the save do not - drawn nowhere and lost on the next load, with nothing failing anywhere. `SectorMaterialisation` with no world writes nothing at all rather than writing where nothing owns it.

The sectors add nothing to the save. What a sector holds is a pure function of the world seed and the sector index, so it is re-derived at load rather than stored; `SaveData.TerrainSeed` is what actually has to survive for it to come back identical. What a robot has already materialised is a different matter and travels with the deposits themselves.

`Game.Save.PendingGameStart` carries the player's New Game/Load choice across the `MainMenu.unity → Bootstrap.unity` scene load. It is the one deliberately mutable static field the save system introduces (DEVELOPMENT_RULES.md §5): a single field, consumed and cleared at the very start of `GameRuntime.Awake()`, never read anywhere else.

Because it is cleared there, anything later in the frame that must tell a fresh run from a restored one has to be told by `GameRuntime` or not at all: `GameRuntime.StartedFromNewGame` is that answer, set from `loadedSave == null` in `Awake()` and read-only afterwards. Its one consumer is the awakening message (GLOBAL_UI.md §4a), which belongs to the birth of the Core rather than to the launch of the game.

`SaveData.ConstructionSites` (a `JObject`) round-trips every construction site (segments, delivered totals, reservations) and both builder robots (position, state, cargo) plus any repatriation still in flight, via `ConstructionSiteSystem.CaptureState()`/`RestoreState(...)` (§15). It restores last, after every real building is back in `Game.Grid` and registered, because a site's segments are rebuilt with the same `CreateForRestore` factory and its reservations are re-resolved by container cell. An absent key restores as two idle robots with no site, without throwing. `SaveData.GlobalStock` no longer exists: the aggregate holds nothing, so there is nothing to serialize - it is recomputed from the real containers at load. Both changes bumped `SaveData.CurrentVersion` to 3.

## 15. Construction sites, builder robots, and GlobalStock as a view

Implemented by `ConstructionSiteSystem` (`Game.Gameplay.Sites`), owned and ticked by `GameRuntime` alongside Power/Compute/Transport/Research (TASK_05_ROBOT_CONSTRUCTEUR.md).

**GlobalStock's contract is inverted, and the name deliberately stays the same.** It used to be a container: it held the starting stock and every demolition refund, was the first pool construction costs drew from, and was serialized. It now holds **nothing at all**. It is a read-only aggregated view, recomputed on every read, over exactly three sources in this fixed order:

```text
Core chest (the core_storage fixture)
   → every placed Storage
      → every production building's OUTPUT
```

minus everything already reserved by a construction site. Its invariant: **what GlobalStock reports is exactly what a builder robot could still be sent to fetch.** Items riding a conveyor or already in a robot's cargo are therefore never counted - they are no longer claimable. A production building's *input* is no longer part of it either (only its output is), a deliberate narrowing of the previous behavior: the aggregate and the robots' collection order must be the same list, and a robot does not raid work-in-progress ingredients out of a machine.

`GameRuntime.GlobalStock` is that aggregate (`IReadOnlyDictionary<string,int>`), and the Storage panel's aggregate view is a straight read of it rather than a second summation.

**Reservation is localized.** A reservation is a `(container, itemId, amount)` triple held by one site, never a bare total - two sites can otherwise both promise themselves the same physical stack and the second one blocks with nothing to explain it. Every tick, every open site, **oldest first**, tries to reserve what it still needs from the collection order above; an older site always wins a newly produced unit over a younger one. Reserved items stay physically in their container (still visible, still counted in that container's own contents) and only leave it when a robot actually loads them.

**Sites.** Placing opens a `ConstructionSiteRuntime` holding one segment (a normal building) or several in placement order (a whole conveyor/splitter drag). Segments materialize strictly in order, each the moment its own cost has been delivered - a dragged belt line grows from its anchor as the robots supply it. Materializing means: register with `TransportSystem` and raise `SegmentMaterialized`, which the Presentation layer turns into a spawned view. Until then a segment is inert.

### `ConstructionSiteRuntime.SegmentProgress(index)`

How far along one segment is, `0` to `1`. Read-only, and the **only** form in which per-segment advancement leaves `Game.Gameplay`: the rule deciding which delivery feeds which segment stays here rather than being re-derived by whoever draws it. Since segments materialize strictly in order and consume the delivered pile in that same order, it answers `1` before the front, `0` after it, and a real ratio only for the segment currently being built. Within a segment, items are weighed by unit count, the same way the whole-site ratio is.

A view that needs a whole site's advancement still computes it from `TotalCost` / `Delivered`; `SegmentProgress` exists for drawing each segment materializing on its own rather than a whole conveyor drag dissolving in one block.

### `ConstructionSiteRuntime.GetSupply(list)`

The bill of materials in the three states a player asks about, one `SupplyLine` per ingredient: **delivered**, **reserved**, **missing**. Read-only, filled into a caller-owned list, and in an order fixed by the bill rather than by a dictionary's enumeration, so a panel rebuilt every frame cannot reshuffle its rows.

`Reserved` covers both halves of a promise - earmarked in a container and not yet collected, and already riding in a robot's cargo. Both are the same statement about **stock**: this material is spoken for and nothing else may take it. It is the only thing that separates a site whose material is secured from one forgotten because nothing produces what it needs; on a delivered count alone the two read identically until one of them silently never finishes. The three states always account for the whole of that ingredient's cost, mid-flight included.

**A site is never opened short.** Placement is gated on `CanAfford`, read against unreserved stock, so opening a site reserves its whole bill on the spot and `Missing` is zero on every queued chantier. A shortage is therefore a refused placement (`PlacementRefusalReason.CannotAfford`), never a stranded site - and the refusal must name its cause, since nothing on screen distinguishes a click that did nothing from one that was refused. `GetStillNeeded` and `IsStalled` stay as defensive reads for a source destroyed while holding reserved material, not as states ordinary play produces.

**Reserved says nothing about movement**, and must not be presented as if it did. A reservation holds whether or not a robot has been dispatched, so several sites placed at once are all fully reserved while only one is being served. Which site a robot is actually walking toward is a separate question with a separate answer - the robots whose `TargetSite` is that site and whose state is `MovingToSite` - and it belongs beside the bill, never inside it.

Assembled here rather than left to the reader for the same reason as `SegmentProgress`: the rule maintaining those numbers stays with the object that maintains them.

**Inspecting a site.** Clicking a not-yet-materialized segment opens the site's supply panel through `Selection.SelectSite` (§7), never the panel of the building it will become. When the site finishes, that panel hands over to the finished building's own panel instead of closing - completion and cancellation are the same event only from the code's side, and `IsComplete` tells them apart (cancelling frees the segments that were never built, so a cancelled site is by construction one whose segments did not all materialize).

**Robots.** Two `BuilderRobotRuntime` (4.4 cells/s, free diagonal movement, no pathfinding), driven only by this system's tick - never by their own `Update()`; the view reads `Position` and converts it to world space, nothing more. They always serve the **oldest site that currently has something reserved and not yet delivered**: a site blocked on a material nobody has is skipped rather than blocking the queue, and reclaims the robots as soon as it can be served again. "One chantier at a time" is about simultaneous execution (both robots serve the same one), not about strict queue order. Each robot claims its share of a site's reservations before leaving, so two robots never fetch the same promised piece twice.

**Cargo is uncapped for construction and capped at `BuilderRobotRuntime.DirectiveCargoCapacity` (10) for a Core directive.** The two are different kinds of job: a building waiting on its materials should not take five waves to receive a bill one robot could carry, while a directive is a hand-over the player chose to take on and how many waves it asks for is part of what it asks. Repatriation follows construction, being the same bill read backwards.

**One source container per round trip, and everything it holds for that job.** A trip is multi-item now, not one ingredient: with the materials in one chest - which is where the player's are, in the Core's own reserve - a whole building's bill arrives in one trip. Spread across two chests it is two trips. There are still no multi-stop tours, which is the decision that rule comes from. A consequence worth knowing: a long conveyor drag is one site, so a funded run now completes from a single delivery rather than a wave per five units.

A dispatched robot's claim moves out of the site's reservations and into its own `PendingAmount`, which counts against a container's reserved total for the whole outbound trip - otherwise the units sit claimed by nobody between dispatch and pickup, and one stack gets promised twice. The counterpart is that **a site leaving the queue must release the robots working for it**: cargo already picked up is dropped off like a repatriation, and a robot merely on its way to fetch drops its `PendingAmount` claim. A claim left standing is unreachable stock for the rest of the game - no reservation pass can see past it, so no site is served, so no robot is ever reassigned to clear it.

**Demolition and its overflow.** The building disappears immediately; its construction cost becomes a repatriation job a robot carries back - Core chest first, then any Storage with room for the whole cargo. If no container anywhere can take it, the robot keeps the cargo, a notification names the cause and the parade, and after 20 seconds **the cargo is destroyed**. This loss is a deliberate, documented simplification, punitive and silent by design: it is the anti-deadlock that keeps a permanently loaded robot from making construction impossible (and the reason there are two robots - one can still build a Storage while the other is stuck). It is a decision, not an oversight: the day it should change, the alternatives are dropping the cargo on the ground or refusing the demolition outright.

**Notifications.** `NotificationSystem` (`Game.Gameplay.Notifications`) is a generic queue - severity, message, display duration, optional countdown - read by a left-edge banner. A blocked robot and a chantier missing materials are its first two callers, not its purpose; it never blocks interaction and no gameplay decision ever reads it.
