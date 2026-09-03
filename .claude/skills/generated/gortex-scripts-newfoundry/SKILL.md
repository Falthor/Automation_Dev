---
name: gortex-scripts-newfoundry
description: "Work in the Scripts · NewFoundry area — 53 symbols across 4 files (70% cohesion)"
---

# Scripts · NewFoundry

53 symbols | 4 files | 70% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Gameplay\Buildings\FoundryRuntime.cs`
- `Assets\Scripts\Gameplay\Buildings\ProductionBuildingRuntime.cs`
- `Assets\Scripts\Tests\EditMode\Core\GridCoordTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Gameplay\Buildings\FoundryRuntime.cs` | _intakeCooldown, FoundryRuntime, itemId, _itemDatabase, fromDirection, ... |
| `Assets\Scripts\Gameplay\Buildings\ProductionBuildingRuntime.cs` | SetSelectedRecipe, AddInput, fromDirection, itemId, GetState, ... |
| `Assets\Scripts\Tests\EditMode\Core\GridCoordTests.cs` | Test |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs` | CanAcceptInput_AcceptsRecipeIngredient_FromNonOutputSide, _ironIngotRecipe, InsufficientCompute_StateIsWaitingCompute_NothingConsumed, PowerDemand_OnlyReported_WhileProducing, maxStackPerItem, ... |

## Entry Points

- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs::FoundryRuntimeTests.Unpowered_FreezesProductionProgress_WithoutLosingConsumedIngredients`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs::FoundryRuntimeTests.PowerDemand_OnlyReported_WhileProducing`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs::FoundryRuntimeTests.FullCycle_ProducesOutput_AfterProductionTimeElapses`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs::FoundryRuntimeTests.SwitchingRecipeMidCycle_AbandonsCycle_DoesNotRefundConsumedIngredients`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs::FoundryRuntimeTests.InsufficientCompute_StateIsWaitingCompute_NothingConsumed`

## Connected Communities

- **Scripts · Recovery_IsImmediate_OnceDemand…** (6 cross-edges)
- **Scripts · ProductionPanelController** (2 cross-edges)
- **Gameplay/Buildings · ProductionBuildingRuntime** (1 cross-edges)
- **Scripts · ComputeSystem** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-38")
explore(operation:"context", task:"understand Scripts · NewFoundry", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs::FoundryRuntimeTests.Unpowered_FreezesProductionProgress_WithoutLosingConsumedIngredients"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
