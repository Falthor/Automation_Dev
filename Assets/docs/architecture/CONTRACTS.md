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

`TryPlace` deducts the definition's cost (player's global stock first, then Core, then every Storage); `TryDemolish` refunds that same cost in full into the global stock, so placing and removing a building is cost-neutral.

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
public void Tick(float deltaTime)
```

CU is a **currency, not a flow**. There is one mechanism: a pooled reserve (`Reserve`, capped at `ReserveCap` = 60000, starting full) credited by `Grant` and spent in one-shot chunks via `CanSpend`/`Spend`. Nothing draws CU per second, nothing is throttled by a CU ratio: a building either affords the cycle it is about to start, or waits at 0 progress until the reserve can pay for it.

Two kinds of spender, both charged in full at the instant a cycle starts:

- a recipe-based production building pays its **recipe's** `ComputeCost` (§6);
- Extractor, Laboratory and Gas Powerplant pay their own **`BuildingDefinition.CuCostPerCycle`** - per extraction, per card converted into RP, and per unit of fuel burned respectively. A powerplant that cannot pay does not light its fuel, and therefore supplies no Power that tick.

Two sources credit the reserve:

- the **Core**, whose `CuOutput` is 0 in the current data - it produces no CU (the `CuOutputIntervalSeconds` mechanism remains in code but has no effect at this value);
- a **Data Center**, which credits its installed components' output for the duration of each tick, and only while powered.

`Tick(deltaTime)` (called once per `GameRuntime.Update()`) only advances the window `IncomePerSecond` is averaged over - the credited-CU-per-second figure the UI shows. Anything granted above the cap is discarded, and `IncomePerSecond` counts only what was really credited.

The UI reads `Reserve`/`IncomePerSecond` through this contract; it must not show a continuous consumption figure, because there is none.

## 11. Research

Implemented by `ResearchSystem` (`Game.Gameplay.Research`), owned by `GameRuntime`:

```csharp
public void AddRp(float amount)
public bool HasActiveResearch()
public ResearchDefinition GetActiveResearch()
public float GetProgress()
public void ReportActiveLab()
public int GetActiveLabCount()
public bool IsUnlocked(string researchId)
public bool ArePrerequisitesMet(ResearchDefinition research)
public bool Start(ResearchDefinition research)
public void Tick(float deltaTime)
public event Action<string> ResearchCompleted
```

One RP pool, one active research slot at a time. Laboratories call `ReportActiveLab()` every tick while a research is active; `Tick()` (report-then-settle, same one-frame lag as Power/Compute) advances progress at `activeLabCount / 60` per second, so completion takes 60s/30s/20s/15s for 1/2/3/4 simultaneously active Laboratories. `Start` deducts the cost immediately and rejects if something is already active, already unlocked, RP is insufficient, or its prerequisite is not completed yet.

A research may require one other research to be completed first (`ResearchDefinition.RequiresResearch`, a direct asset reference - the tree is a chain today, so it is one reference and not a list). `ArePrerequisitesMet` is the read-only form the UI uses to show *why* a row is unavailable instead of only greying it out.

Unlike Power/Compute, there is no separate id-keyed registry: `ResearchDefinition` (`Game.Data`, id/displayName/cost) is referenced directly wherever a gate applies - `BuildingDefinition.UnlockResearch` (checked by `ConstructionService.IsPlaceable`, not by any runtime) and `RecipeDefinition.UnlockResearch` (checked by `ProductionBuildingRuntime.GetRecipeIds()`). Building placement and recipe availability both query unlock state through `IsUnlocked(id)` rather than reading internal research collections.

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
public void RestoreState(float rp, ResearchDefinition active,
    float progress, IEnumerable<string> unlockedIds)                    // ResearchSystem
public IEnumerable<string> GetUnlockedIds()                             // ResearchSystem
public void RestoreState(CoreRuntime core, GridCoord coreOrigin,
    int actionRadiusCells, IEnumerable<DepositRuntime> deposits)        // WorldGenerator
public void RestoreState(int remainingQuantity)                         // DepositRuntime
public IEnumerable<BuildingRuntime> GetAllBuildings()                   // TransportSystem
public BuildingRuntime CreateForRestore(BuildingDefinition definition,
    GridCoord cell, Direction rotation)                                 // ConstructionService
```

`BuildingRuntime.CaptureState()`/`RestoreState(JObject)` are virtual, empty by default; each subclass with real mutable state overrides both (`ProductionBuildingRuntime` and its subclasses, `StorageRuntime`, `ConveyorRuntime`, `CoreRuntime`, `ExtractorRuntime`, `LaboratoryRuntime`, `PowerplantGazRuntime`, `DataCenterRuntime`, `SplitterRuntime`, `CrossroadRuntime`). A building's envelope (`Definition.Id`, `Cell`, `FacingRotation`) is captured generically by `GameRuntime`, not by the building itself - only its type-specific payload goes through `CaptureState()`.

`ConstructionService.CreateForRestore` is the one other caller of the definition→runtime factory besides `TryPlace`: same instantiation switch, but with no cost deduction and no placement validity check (both already happened once, at the original construction the save captured). It relies on the caller having already placed any deposit a restored Extractor sits on, exactly like `TryPlace` relies on `IsPlaceable` having checked that beforehand.

`GameRuntime` resolves a saved `BuildingDefinition`/`ResearchDefinition` id back to its asset via two small serialized catalogs (`buildingCatalog`, `researchCatalog`) populated in the Inspector, following the same "id → asset" registry pattern as `ItemDatabase`/`RecipeDatabase` - there is no separate `BuildingDatabase` elsewhere in the project.

Trigger points (both in `GameRuntime`, `Game.Presentation`):

- **New Game** (`MainMenu.unity` → `Bootstrap.unity` with `PendingGameStart.LoadedSave == null`): generates the world exactly as before, then writes the save immediately - "New Game generates a save" per the menu's own contract.
- **Quit** (`OnApplicationQuit`): recaptures every system's current state and overwrites the save - this is what makes a later Load reflect real progress, not just the New Game snapshot.
- **Load** (`MainMenu.unity` → `Bootstrap.unity` with `PendingGameStart.LoadedSave != null`): `GameRuntime.Awake()` restores every system from the save instead of generating a new world, in dependency order (Core → deposits → every other building, since an Extractor resolves its deposit from whatever already occupies its cell).

`Game.Save.PendingGameStart` carries the player's New Game/Load choice across the `MainMenu.unity → Bootstrap.unity` scene load. It is the one deliberately mutable static field the save system introduces (DEVELOPMENT_RULES.md §5): a single field, consumed and cleared at the very start of `GameRuntime.Awake()`, never read anywhere else.
