---
name: gortex-scripts-productionpanelcontroller
description: "Work in the Scripts · ProductionPanelController area — 60 symbols across 3 files (85% cohesion)"
---

# Scripts · ProductionPanelController

60 symbols | 3 files | 85% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Gameplay\Buildings\ProductionBuildingRuntime.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FactoryRuntimeTests.cs`
- `Assets\Scripts\UI\ProductionPanelController.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Gameplay\Buildings\ProductionBuildingRuntime.cs` | GetRecipeIdWhitelist, HasResourcesFor, GetPowerDemandKw, recipeId, GetSelectedRecipe, ... |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FactoryRuntimeTests.cs` | GetRecipeIds_ExcludesResearchGatedRecipe_UntilUnlocked |
| `Assets\Scripts\UI\ProductionPanelController.cs` | powerIcon, _productionTab, ResolveItemName, _timeLabel, RefreshActionButton, ... |

## Entry Points

- `Assets\Scripts\UI\ProductionPanelController.cs::ProductionPanelController.Start`

## Connected Communities

- **Scripts · NewFoundry** (2 cross-edges)
- **Gameplay/Buildings · ProductionBuildingRuntime** (2 cross-edges)
- **Scripts · ProductionState** (2 cross-edges)
- **Scripts · ResearchDefinition** (1 cross-edges)
- **Scripts · FactoryRuntimeTests** (1 cross-edges)
- **Scripts · BuildingSpawner** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-64")
explore(operation:"context", task:"understand Scripts · ProductionPanelController", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\UI\ProductionPanelController.cs::ProductionPanelController.Start"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
