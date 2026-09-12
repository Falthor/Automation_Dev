# Production

Recipe-based buildings: what they offer, how a cycle runs, what they buffer, and what stops them.

Implemented by `ProductionBuildingRuntime` (`Game.Gameplay.Buildings`), extended by `FoundryRuntime`,
`FactoryRuntime`, `AdvancedFoundryRuntime` and `ConstructorRuntime`. Backed by a `RecipeDatabase`
lookup and two `PooledItemStock` instances, one for input and one for output.

## 1. The contract

```csharp
public IReadOnlyList<string> GetRecipeIds()
public string GetSelectedRecipe()
public void SetSelectedRecipe(string recipeId)
public float GetProductionTime()
public IReadOnlyDictionary<string,int> GetRequiredIngredients()
public float GetProgress()
public bool HasRequiredResources()
public bool HasResourcesFor(string recipeId)
public ProductionState GetState()
public IReadOnlyDictionary<string,int> GetInputContents()
public bool IsPaused { get; }
public void SetPaused(bool paused)
```

`GetRecipeIds()` leaves out any recipe a research gates until one of those researches is completed.
`SetSelectedRecipe` is the sole public entry point for starting or changing the active recipe.
`GetSelectedRecipe()` returns the recipe's id, not the definition - the UI resolves it against
`RecipeDatabase` itself. There is no `GetStateLabel()`: the words shown under the progress bar
(`ProductionPanelController.StateCaption`) are the UI's own, kept out of Game.Gameplay.

`IsPaused`/`SetPaused` switch a building off and on at the player's request - distinct from `Idle`
(nothing to do): a paused building draws no power and holds its cycle exactly where it stood. It
persists across save/load. Paused also refuses input on both sides, so a belt feeding it backs up
rather than piling material into a stopped machine.

`GetInputContents()` is **not** part of the base building contract - only a building with a pooled input
has one to enumerate. It mirrors `GetOutputContents()` for the other side of the same building, and is
read by the building's own inspector panel. It is deliberately not part of the construction aggregate
nor of a builder robot's collection order: only a production building's *output* is claimable.

The UI does not access timers or inventories directly.

## 2. One cycle

```text
IDLE  PRODUCING  WAITING_RESOURCES  OUTPUT_BLOCKED  WAITING_COMPUTE  PAUSED
```

A cycle takes **all** its ingredients and its recipe's one-shot compute cost at once, the instant it
starts - the transition into `PRODUCING` - never progressively as it advances.

**Changing recipe abandons the running cycle**: already-consumed ingredients are not refunded and the
timer resets. That is the accepted behaviour, and it is the precedent research follows when a player
cancels an active one.

Power demand is reported only while `PRODUCING`, and based on whether the building was `PRODUCING` at
the end of the *previous* tick - the same report-then-settle lag power uses throughout. If unpowered,
the effective delta passed to the state machine that tick is scaled to zero, freezing an in-progress
cycle's timer without losing the ingredients or compute already consumed.

**Only power gates a building's speed.** CU is never a continuous draw here: a cycle either affords its
one-shot cost at the start or waits at zero progress.

## 3. What it buffers, and why neither figure is configurable

A production building's **input** holds `InputCraftsHeld` (3) crafts' worth of each ingredient: the
selected recipe's per-craft amount times three, so a recipe taking 2 iron and 4 screws buffers 6 and 12.
Read live, so changing recipe moves the ceilings with it, and anything the current recipe does not
consume has a ceiling of zero.

Its **output** is a flat `OutputStackCapacity` (10) whatever the building or recipe: that buffer exists
against a stalled belt, and its right size has nothing to do with the recipe.

Neither is per-definition. **A building's intake is sized by what it actually consumes**, not by a
number somebody chose for it.

## 4. What is not this

The Extractor does not implement the player-selected-recipe contract: its production is fully
automatic. It still participates in the same item surfaces and the same power and compute rules.

Recipe rates shown on screen - crafts per minute, output per minute, ingredient demand per minute - are
derived from the recipe's own duration and yield rather than authored beside them, so retuning either
moves every figure with it. A maintained-by-hand rate is a number that eventually lies.
