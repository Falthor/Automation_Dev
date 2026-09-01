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

For non-belt buildings:

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

The pooled inventory model must not be mixed with the belt lane model.

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
```

### `GetStateLabel()`

Returns the presentation label for the current state.

Foundry and Extractor do not need to implement this player-selected-recipe contract when their production remains automatic.

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

Power consumers and sources expose their contribution through a public contract.

The UI reads aggregate supply/demand and powered state through that contract.

The UI must not inspect individual building private power fields.

## 10. Compute

Compute exposes:

- supply
- demand
- performance ratio
- pooled reserve where applicable

The UI reads these values.

Continuous demand and one-time cycle costs remain distinct concepts.

## 11. Research

Research exposes:

- current research points
- active research
- progression
- unlock state

Building placement queries unlock state through Research's public contract rather than reading internal research collections.

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
