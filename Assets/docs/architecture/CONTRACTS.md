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

`GetOutputCells()` returns every cell along the building's output edge (more than one for a footprint wider/taller than 1 cell in the relevant dimension). `GetEdgeCells()` returns every cell touching any of the 4 sides, paired with which side (from this building's own perspective) it touches - used as the `fromDirection` argument to `CanAcceptInput`/`AddInput`.

`TransportSystem` (`Game.Gameplay.Transport`) runs one generic push/pull step, at each building's own `PushIntervalSeconds` (`BuildingRuntime`, default 1s), for every registered building that is not itself belt-driven (Storage, and every `ProductionBuildingRuntime`):

- **Push**: walks `GetOutputCells()`; for the first neighbor whose `CanAcceptInput` accepts one unit of something in `GetOutputContents()`, transfers it via `TakeOutput`/`AddInput`.
- **Pull**: walks `GetEdgeCells()` (all 4 sides, regardless of that neighbor's own facing); for the first neighbor exposing a `PeekPullableItem()` this building's own `CanAcceptInput` accepts, transfers it via `ConsumePulledItem`/`AddInput`.

Conveyors keep their own dedicated pull-from-behind-via-Flow + lane-advance logic (a conveyor has no pooled input/output, just a single carried item) and are not part of this generic step.

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

A cycle takes ALL its ingredients and its recipe's one-shot Compute cost (§10) at once, the instant it starts (the transition into `PRODUCING`) - not progressively as it advances. Power demand (§9) is reported only while `PRODUCING`, based on whether the building was `PRODUCING` at the end of the *previous* tick (the same report-then-settle one-frame lag Power/Compute already have) - if unpowered, the effective delta passed to the state machine that tick is scaled to 0, freezing an in-progress cycle's timer without losing already-consumed ingredients/Compute. Continuous Compute demand and this one-shot cycle cost remain distinct (§10); a `ProductionBuildingRuntime` never reports continuous Compute demand.

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
public void ReportDemand(float cuPerSecond)
public void ReportSupply(float cuPerSecond)
public float GetPerformanceRatio()
public bool CanSpend(float cost)
public void Spend(float cost)
public void GrowReserve(float deltaTime)
public void Settle()
```

Two independent mechanisms share the same settled supply number:

1. **Continuous flow**: same report-then-settle pattern as Power; `GetPerformanceRatio()` (`SettledSupply / SettledDemand`, capped at 1) is the throttle a continuous consumer applies to itself.
2. **Pooled reserve** (`Reserve`, capped at `ReserveCap` = 25000): grows every frame by `SettledSupply * deltaTime` (`GrowReserve`, called once per `GameRuntime.Update()`), spent in one-shot chunks via `CanSpend`/`Spend` (a recipe's `ComputeCost` at cycle start, §6). Never throttled by `GetPerformanceRatio()` - it is a spent balance, not a continuous draw.

The UI reads these values through this contract. Continuous demand and one-time cycle costs remain distinct concepts.

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
public bool Start(ResearchDefinition research)
public void Tick(float deltaTime)
public event Action<string> ResearchCompleted
```

One RP pool, one active research slot at a time. Laboratories call `ReportActiveLab()` every tick while a research is active; `Tick()` (report-then-settle, same one-frame lag as Power/Compute) advances progress at `activeLabCount / 60` per second, so completion takes 60s/30s/20s/15s for 1/2/3/4 simultaneously active Laboratories. `Start` deducts the cost immediately and rejects if something is already active, already unlocked, or RP is insufficient.

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
