---
name: gortex-scripts-ui-buildingmenucontroller
description: "Work in the Scripts/UI · BuildingMenuController area — 62 symbols across 2 files (92% cohesion)"
---

# Scripts/UI · BuildingMenuController

62 symbols | 2 files | 92% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\UI\BuildingCategory.cs`
- `Assets\Scripts\UI\BuildingMenuController.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\UI\BuildingCategory.cs` | Production, Power, Organisation, BuildingCategory, Logistic |
| `Assets\Scripts\UI\BuildingMenuController.cs` | ResolveIcon, category, ingredient, computeIcon, definition, ... |

## Entry Points

- `Assets\Scripts\UI\BuildingMenuController.cs::BuildingMenuController.Start`

## Connected Communities

- **Scripts/Presentation · ProceduralSpriteFactory** (1 cross-edges)
- **Scripts · BuildingSpawner** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-54")
explore(operation:"context", task:"understand Scripts/UI · BuildingMenuController", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\UI\BuildingMenuController.cs::BuildingMenuController.Start"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
