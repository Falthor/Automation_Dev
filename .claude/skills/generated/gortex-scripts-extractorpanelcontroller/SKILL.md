---
name: gortex-scripts-extractorpanelcontroller
description: "Work in the Scripts · ExtractorPanelController area — 63 symbols across 4 files (83% cohesion)"
---

# Scripts · ExtractorPanelController

63 symbols | 4 files | 83% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Data\ExtractorDefinition.cs`
- `Assets\Scripts\Gameplay\Buildings\ExtractorRuntime.cs`
- `Assets\Scripts\Grid\DepositRuntime.cs`
- `Assets\Scripts\UI\ExtractorPanelController.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Data\ExtractorDefinition.cs` | cuCostPerCycle, CuCostPerCycle, ExtractionIntervalSeconds, ItemsPerCycle, ExtractorDefinition, ... |
| `Assets\Scripts\Gameplay\Buildings\ExtractorRuntime.cs` | cell, _computeSystem, ConsumePulledItem, ExtractorRuntime.<init>, _cycleCharged, ... |
| `Assets\Scripts\Grid\DepositRuntime.cs` | origin, DepositRuntime, RemainingQuantity, DepositRuntime.<init>, ItemId, ... |
| `Assets\Scripts\UI\ExtractorPanelController.cs` | ExtractorPanelController, gameRuntime, OnSelectionChanged, building, Render, ... |

## Entry Points

- `Assets\Scripts\UI\ExtractorPanelController.cs::ExtractorPanelController.Start`

## Connected Communities

- **Scripts · BuildingSpawner** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-59")
explore(operation:"context", task:"understand Scripts · ExtractorPanelController", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\UI\ExtractorPanelController.cs::ExtractorPanelController.Start"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
