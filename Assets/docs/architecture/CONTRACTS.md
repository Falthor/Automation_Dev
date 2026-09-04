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

Not part of the base `BuildingRuntime` contract (only `ProductionBuildingRuntime` and its subclasses have a pooled input to enumerate). Returns a read-only `IReadOnlyDictionary<string,int>` snapshot of everything currently held in input - mirrors `GetOutputContents()` for the other side of the same building. Used by the aggregate Storage panel (`Game.UI`) and by `ConstructionService` (§8) to include every production building's own internal stock (input+output) alongside the player's global stock and every placed Storage box - the same pool in both places, so what the Storage panel shows is exactly what a construction cost can draw from.

The pooled inventory model must not be mixed with the belt lane model. Two pooled-inventory shapes coexist under this same contract: `Inventory` (`Game.Gameplay.Items`, used by `StorageRuntime`) is slot-based - a fixed `SlotCount` of distinct item ids, each slot capped at `CapacityPerSlot`. `PooledItemStock` (`Game.Gameplay.Items`, used by `ProductionBuildingRuntime`'s input and output) has unlimited distinct item ids, each capped independently at a `MaxStackPerItem`. Which one a building uses is an internal representation choice; both satisfy the same public methods above.

## 3a. Generic transport push/pull

`BuildingRuntime` exposes footprint- and rotation-aware geometry so a generic transport step never needs to know a building's concrete type:

```csharp
public GridCoord[] GetOutputCells()
public (GridCoord cell, Direction fromMySide)[] GetEdgeCells()
```

```csharp
public (GridCoord cell, Direction fromMySide)[] GetInputCells()
```

`GetOutputCells()` returns every cell along the building's output edge (more than one for a footprint wider/taller than 1 cell in the relevant dimension). `GetEdgeCells()` returns every cell touching any of the 4 sides, paired with which side (from this building's own perspective) it touches - used as the `fromDirection` argument to `CanAcceptInput`/`AddInput`. `GetInputCells()` returns the subset items are actually taken from: for a building declaring directional input (`BuildingDefinition.HasInputArrows`) that is one cell per side other than its output side - exactly the cells its entry arrows are drawn on, so an arrow always marks a real intake point and there are no invisible ones; for a building declaring none (Storage, Core) it is every edge cell, matching the "input from any side" behavior those are defined with.

`TransportSystem` (`Game.Gameplay.Transport`) runs one generic push and one generic pull for every registered building that is not itself belt-driven (Storage, and every `ProductionBuildingRuntime`):

- **Push**, at the building's own `PushIntervalSeconds` (`BuildingRuntime`, default 1s): walks `GetOutputCells()`; for the first neighbor whose `CanAcceptInput` accepts one unit of something in `GetOutputContents()`, transfers it via `TakeOutput`/`AddInput`.
- **Pull**, every tick: walks `GetInputCells()` (regardless of that neighbor's own facing); for the first neighbor exposing a `PeekPullableItem()` this building's own `CanAcceptInput` accepts, transfers it via `ConsumePulledItem`/`AddInput`. How fast a building may actually absorb what it reads stays its own concern (e.g. `FoundryRuntime`'s intake cooldown), not a side effect of transport's polling rate.

  When two different buildings both include the exact same physical source cell among their own `GetInputCells()` - e.g. two Factories placed on adjacent sides of one conveyor cell, both facing it as their entry - only one can actually take that source's single pullable item on a given tick. Rather than always favoring whichever consumer happens to be registered first (the earlier bug), the pull step resolves this per source with round-robin: it remembers the last consumer served by each source and, when more than one of today's contenders wants that same source, gives it to whichever contender was not served last time. This is unrelated to the different, still-valid upstream/downstream priority described below for two machines reading two different points along one belt line.

One tick therefore runs: building state machines → **every belt advances** → **every building reads its input cells** → **every belt hands over** (back-edge pull, then side merge) → splitters/crossroads → building pushes. The belt phase is split around the building read on purpose. Doing a belt's advance and hand-over together, belt by belt, made the whole line's behavior depend on the order belts sit in the internal list and left no moment where an item is observably parked at the end of a cell - the next belt, visited later in that same pass, took it immediately. A building alongside a *running* line then never saw anything to pick up and was only fed once the line downstream jammed. With the split, order stops mattering and a building takes an item parked at the cell its entry arrow points at before the belt carries it further. Consequence to keep in mind when designing a line: of two machines reading the same belt, the upstream one is served first and the downstream one gets what is left, exactly like the belt's own downstream continuation.

Conveyors keep their own dedicated pull-from-behind-via-Flow + lane-advance logic (a conveyor has no pooled input/output, just the items riding it) and are not part of this generic step.

A conveyor also accepts a **side merge**: when its pull from the back edge finds nothing and it still has a free slot, it takes one item from any building whose own output points into it across one of its two side edges - another belt merging in, or a production building standing alongside dropping its output onto the belt it runs past. The exit edge is excluded, so two belts facing each other head-on never trade the same item back and forth. The side is always the lower priority of the two intakes - the in-line belt is served first every tick, and a merging item only enters when the receiving belt has room (`HasRoomForNewItem`); otherwise it waits where it is.

Both intakes match against the source's **whole output edge** (`GetOutputCells()`), not just its first cell, so a building whose footprint is wider than one cell hands its output to every belt cell it faces rather than to one arbitrary cell of that edge.

**Behavior change**: Storage's pull previously required the neighbor's own configured output to be aimed at Storage (`GetOutputCell() == destination`). It now uses the same generic pull as every other non-belt building - any of the 4 sides, no alignment check - matching the source project's `Building._try_pull()` exactly. This is a deliberate, documented change (§13), not a Storage-specific redesign.

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

## 6. ProductionBuilding

Implemented by `ProductionBuildingRuntime` (`Game.Gameplay.Buildings`), extended by `FoundryRuntime` (and, in later phases, Factory/AdvancedFoundry/Assembler). Backed by a `RecipeDatabase` (`Game.Data`) lookup and two `PooledItemStock` instances (input/output) - see §3.

### `GetRecipeIds()`

Returns recipes offered by the production building.

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

Selection owns the currently inspected building and the global UI-panel selection state.

The public API must support the equivalent behavior of:

```text
Select(building)
Clear()
GetSelectedBuilding()
```

and an observable selection-changed notification.

The global panel state and building selection remain mutually exclusive when that behavior is retained from the source project.

## 8. Construction

Construction exposes intent-level operations rather than requiring callers to manipulate its internal preview state. Implemented as `ConstructionService` (`Game.Construction`), a plain C# class (no Unity/input dependency):

```csharp
public void SelectBuilding(BuildingDefinition definition)
public void Cancel()
public void SetPreviewRotation(Direction rotation)
public bool CanPlace(GridCoord cell)
public bool TryPlace(GridCoord cell, Direction rotation, out BuildingRuntime placed)
public bool TryDemolish(GridCoord cell, out BuildingRuntime removed)
```

`TryPlace` deducts the definition's cost (player's global stock first, then Core, then every Storage, then every production building's own internal stock - input before output); `TryDemolish` refunds that same cost in full into the global stock, so placing and removing a building is cost-neutral.

`TryPlace`/`TryDemolish` only mutate `Game.Grid`/runtime state and return the affected `BuildingRuntime` via `out`; they never create or destroy GameObjects. The caller (a Presentation-layer input adapter) is responsible for the corresponding view, which is what keeps `Game.Construction` free of a dependency on `Game.Presentation`. `CanPlace` is a non-mutating query used for ghost-preview valid/invalid tinting.

Drag-gesture decoding (turning a mouse drag into a sequence of single-cell `TryPlace` calls, and detecting when to reshape the drag anchor into a corner) is input-interpretation and lives in the Presentation-layer input adapter, not in `ConstructionService` itself, which stays single-cell and Unity-input-agnostic.

The construction system owns preview/ghost state and placement orchestration.

## 9. Power

Implemented by `PowerSystem` (`Game.Gameplay.Power`), owned by `GameRuntime`:

```csharp
public void ReportDemand(float kilowatts)
public void ReportSupply(float kilowatts)
public bool IsPowered()
public void Settle()
```

Report-then-settle: consumers/sources call `ReportDemand`/`ReportSupply` during their own tick; `Settle()` (called once per `GameRuntime.Update()`, before that tick) moves the previous frame's reports into `SettledDemand`/`SettledSupply` and clears the accumulators - one frame of intentional lag. `IsPowered()` is binary (`SettledDemand <= SettledSupply`), no partial degradation, and recovers automatically the instant reported demand drops back at/under supply (no cooldown).

The UI reads `SettledDemand`/`SettledSupply`/`IsPowered()` through this contract and must not inspect individual building private power fields.

## 10. Compute

Implemented by `ComputeSystem` (`Game.Gameplay.Compute`), owned by `GameRuntime`:

```csharp
public void Grant(float amount)
public bool CanSpend(float cost)
public void Spend(float cost)
public float SpendUpTo(float maxAmount)
public void Tick(float deltaTime)
```

CU is a pooled reserve (`Reserve`, capped at `ReserveCap` = 60000, starting full) credited by `Grant`. It is a **currency, not a flow** for every spender but one:

- Every production cycle - a recipe-based production building (its recipe's `ComputeCost`, §6), Extractor, and Gas Powerplant (their own `BuildingDefinition.CuCostPerCycle`, per extraction/per unit of fuel burned respectively) - still pays in a **single one-shot chunk the instant the cycle starts**, via `CanSpend`/`Spend`. There is no throttling ratio for these: a cycle either affords itself in full or waits at 0 progress. A powerplant that cannot pay does not light its fuel, and therefore supplies no Power that tick.
- **Research** (`Game.Gameplay.Research.ResearchSystem`, §11) is the one continuous per-second draw, via `SpendUpTo(maxAmount)`: it withdraws up to `maxAmount`, less if the reserve holds less, and returns how much was actually taken - it never goes negative and never throws. This is a deliberate, documented exception to "CU is a currency, not a flow" (CONTRACTS.md §13 contract evolution, TASK_02_REFONTE_RECHERCHE.md): the research absorption model needs a real per-second rate, capped by both the research's own `AbsorptionRatePerSecond` and whatever the reserve can currently give. `SpendUpTo` has exactly one caller; every other spender keeps using `CanSpend`/`Spend`.

Two sources credit the reserve:

- the **Core**, whose `CuOutput` is 0 in the current data - it produces no CU (the `CuOutputIntervalSeconds` mechanism remains in code but has no effect at this value);
- a **Data Center**, which credits its installed components' output for the duration of each tick, and only while powered.

`Tick(deltaTime)` (called once per `GameRuntime.Update()`) only advances the window `IncomePerSecond` is averaged over - the credited-CU-per-second figure the UI shows. Anything granted above the cap is discarded, and `IncomePerSecond` counts only what was really credited.

The UI reads `Reserve`/`IncomePerSecond` through this contract for the general CU display; it must not show a continuous consumption figure there, because outside of research there is none. The one legitimate continuous-rate figure is research's own absorption ceiling and estimated time remaining (§11), which the UI reads from `ResearchSystem`, not from `ComputeSystem`.

## 11. Research

Implemented by `ResearchSystem` (`Game.Gameplay.Research`), owned by `GameRuntime`. CU/absorption model (TASK_02_REFONTE_RECHERCHE.md), not RP/laboratories:

```csharp
public bool HasActiveResearch()
public ResearchDefinition GetActiveResearch()
public float AbsorbedCu
public float GetProgress()
public float GetEstimatedSecondsRemaining()
public IReadOnlyList<ResearchDefinition> GetQueue()
public bool IsUnlocked(string researchId)
public IEnumerable<string> GetUnlockedIds()
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

`ResearchDatabase` (`Game.Data`) is the id-keyed registry, on the same model as `ItemDatabase`/`RecipeDatabase` - one asset assigned on `GameRuntime`, `Get(id)` for a single lookup, `GetAll()` for the UI to enumerate the whole tree. A gate itself still references a `ResearchDefinition` directly, exactly as before: `BuildingDefinition.UnlockResearch` (checked by `ConstructionService.IsPlaceable`, not by any runtime) and `RecipeDefinition.UnlockResearch` (checked by `ProductionBuildingRuntime.GetRecipeIds()`). Building placement and recipe availability both query unlock state through `IsUnlocked(id)` rather than reading internal research collections.

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

## 13. Contract evolution

Changing a public contract is an architectural change.

When a contract changes:

1. identify all consumers globally;
2. update the contract documentation;
3. update affected tests;
4. verify dependency direction;
5. report any behavior change explicitly.

## 14. Save / Restore

Mono-save (`Game.Save`, `Assets/Scripts/Save/`): one fixed file (`SaveService.SavePath`, `Application.persistentDataPath/save.json`), always overwritten in place - no save slots, no multi-file history.

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
    int actionRadiusCells, IEnumerable<DepositRuntime> deposits)        // WorldGenerator
public void RestoreState(int remainingQuantity)                         // DepositRuntime
public IEnumerable<BuildingRuntime> GetAllBuildings()                   // TransportSystem
public BuildingRuntime CreateForRestore(BuildingDefinition definition,
    GridCoord cell, Direction rotation)                                 // ConstructionService
```

`BuildingRuntime.CaptureState()`/`RestoreState(JObject)` are virtual, empty by default; each subclass with real mutable state overrides both (`ProductionBuildingRuntime` and its subclasses, `StorageRuntime`, `ConveyorRuntime`, `CoreRuntime`, `ExtractorRuntime`, `PowerplantGazRuntime`, `DataCenterRuntime`, `SplitterRuntime`, `CrossroadRuntime`). A building's envelope (`Definition.Id`, `Cell`, `FacingRotation`) is captured generically by `GameRuntime`, not by the building itself - only its type-specific payload goes through `CaptureState()`.

`ConstructionService.CreateForRestore` is the one other caller of the definition→runtime factory besides `TryPlace`: same instantiation switch, but with no cost deduction and no placement validity check (both already happened once, at the original construction the save captured). It relies on the caller having already placed any deposit a restored Extractor sits on, exactly like `TryPlace` relies on `IsPlaceable` having checked that beforehand.

`GameRuntime` resolves a saved `BuildingDefinition` id back to its asset via a small serialized catalog (`buildingCatalog`) populated in the Inspector, following the same "id → asset" pattern as `ItemDatabase`/`RecipeDatabase` - there is no separate `BuildingDatabase` elsewhere in the project. A saved `ResearchDefinition` id is resolved through `ResearchDatabase.Get(id)` instead (§11) - one real database, not a second ad hoc catalog.

Trigger points (both in `GameRuntime`, `Game.Presentation`):

- **New Game** (`MainMenu.unity` → `Bootstrap.unity` with `PendingGameStart.LoadedSave == null`): generates the world exactly as before, then writes the save immediately - "New Game generates a save" per the menu's own contract.
- **Quit** (`OnApplicationQuit`): recaptures every system's current state and overwrites the save - this is what makes a later Load reflect real progress, not just the New Game snapshot.
- **Load** (`MainMenu.unity` → `Bootstrap.unity` with `PendingGameStart.LoadedSave != null`): `GameRuntime.Awake()` restores every system from the save instead of generating a new world, in dependency order (Core → deposits → every other building, since an Extractor resolves its deposit from whatever already occupies its cell).

`Game.Save.PendingGameStart` carries the player's New Game/Load choice across the `MainMenu.unity → Bootstrap.unity` scene load. It is the one deliberately mutable static field the save system introduces (DEVELOPMENT_RULES.md §5): a single field, consumed and cleared at the very start of `GameRuntime.Awake()`, never read anywhere else.
