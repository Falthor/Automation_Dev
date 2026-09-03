---
name: gortex-scripts-powerplantgazruntime
description: "Work in the Scripts · PowerplantGazRuntime area — 64 symbols across 4 files (84% cohesion)"
---

# Scripts · PowerplantGazRuntime

64 symbols | 4 files | 84% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Data\PowerplantGazDefinition.cs`
- `Assets\Scripts\Gameplay\Buildings\PowerplantGazRuntime.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\PowerplantGazRuntimeTests.cs`
- `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Data\PowerplantGazDefinition.cs` | FuelCycleTimeSeconds, selfPowerDemandKw, PowerDemandKw, FuelItem, PowerplantGazDefinition, ... |
| `Assets\Scripts\Gameplay\Buildings\PowerplantGazRuntime.cs` | CanAcceptInput, definition, amount, _fuelAmount, itemId, ... |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\PowerplantGazRuntimeTests.cs` | cuCostPerCycle, _compute, PowerplantGazRuntimeTests, fuelCycleTimeSeconds, selfPowerDemandKw, ... |
| `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs` | powerOutputKw, fuelItem, maxFuelStack, NewPowerplantGaz, fuelCycleTimeSeconds, ... |

## Entry Points

- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\PowerplantGazRuntimeTests.cs::PowerplantGazRuntimeTests.FuelTimer_Freezes_WhenFuelRunsOut`

## Connected Communities

- **Scripts · Recovery_IsImmediate_OnceDemand…** (4 cross-edges)
- **Scripts · ComputeSystem** (2 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-40")
explore(operation:"context", task:"understand Scripts · PowerplantGazRuntime", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\Tests\EditMode\Gameplay\Buildings\PowerplantGazRuntimeTests.cs::PowerplantGazRuntimeTests.FuelTimer_Freezes_WhenFuelRunsOut"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
