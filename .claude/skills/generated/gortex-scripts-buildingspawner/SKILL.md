---
name: gortex-scripts-buildingspawner
description: "Work in the Scripts · BuildingSpawner area — 45 symbols across 5 files (63% cohesion)"
---

# Scripts · BuildingSpawner

45 symbols | 5 files | 63% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Grid\GridRuntime.cs`
- `Assets\Scripts\Presentation\BuildingGhostView.cs`
- `Assets\Scripts\Presentation\BuildingSpawner.cs`
- `Assets\Scripts\Presentation\ConstructionInputAdapter.cs`
- `Assets\Scripts\Presentation\ProceduralSpriteFactory.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Grid\GridRuntime.cs` | FootprintCenterToWorld, origin, sizeInCells, CellCenterToWorld, cell |
| `Assets\Scripts\Presentation\BuildingGhostView.cs` | Hide |
| `Assets\Scripts\Presentation\BuildingSpawner.cs` | renderer, StandardSortingOrder, color, definition, _straightConveyorArt, ... |
| `Assets\Scripts\Presentation\ConstructionInputAdapter.cs` | UpdateGhost, cell, definition, ResolveGhostSprite |
| `Assets\Scripts\Presentation\ProceduralSpriteFactory.cs` | color, CreateSolidSquareSprite, color, CreateArrowSprite |

## Connected Communities

- **Scripts · Show** (4 cross-edges)
- **Scripts · GameRuntime** (2 cross-edges)
- **Scripts · ConstructionService** (2 cross-edges)
- **Scripts · GridCoord** (2 cross-edges)
- **Scripts · Direction** (2 cross-edges)
- **Scripts/Presentation · ProceduralSpriteFactory** (2 cross-edges)
- **Scripts · BuildingDefinition** (1 cross-edges)
- **Scripts · ConveyorDefinition** (1 cross-edges)
- **Scripts/Presentation · Sync** (1 cross-edges)
- **Scripts · TransportSystem** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-20")
explore(operation:"context", task:"understand Scripts · BuildingSpawner", format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
