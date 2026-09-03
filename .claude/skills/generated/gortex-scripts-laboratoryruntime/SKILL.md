---
name: gortex-scripts-laboratoryruntime
description: "Work in the Scripts · LaboratoryRuntime area — 64 symbols across 4 files (79% cohesion)"
---

# Scripts · LaboratoryRuntime

64 symbols | 4 files | 79% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Data\LaboratoryDefinition.cs`
- `Assets\Scripts\Gameplay\Buildings\LaboratoryRuntime.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\LaboratoryRuntimeTests.cs`
- `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Data\LaboratoryDefinition.cs` | cardItem, powerDemandKw, PowerDemandKw, CuCostPerCycle, CardItem, ... |
| `Assets\Scripts\Gameplay\Buildings\LaboratoryRuntime.cs` | computeSystem, facingRotation, fromDirection, LaboratoryRuntime, _computeSystem, ... |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\LaboratoryRuntimeTests.cs` | GeneratesRp_IndependentOfWhetherAResearchIsActive, _card, _compute, CardConversion_Frozen_WhileUnpowered, cuCostPerCycle, ... |
| `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs` | maxCardStack, cardConvertIntervalSeconds, rpPerCard, powerDemandKw, cuCostPerCycle, ... |

## Entry Points

- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\LaboratoryRuntimeTests.cs::LaboratoryRuntimeTests.ReportsActiveLab_OnlyWhileResearchIsActive`

## Connected Communities

- **Scripts · ResearchDefinition** (7 cross-edges)
- **Scripts · Recovery_IsImmediate_OnceDemand…** (3 cross-edges)
- **Scripts · ComputeSystem** (2 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-39")
explore(operation:"context", task:"understand Scripts · LaboratoryRuntime", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\Tests\EditMode\Gameplay\Buildings\LaboratoryRuntimeTests.cs::LaboratoryRuntimeTests.ReportsActiveLab_OnlyWhileResearchIsActive"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
