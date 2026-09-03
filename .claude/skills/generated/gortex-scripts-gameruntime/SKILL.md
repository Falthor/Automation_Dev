---
name: gortex-scripts-gameruntime
description: "Work in the Scripts · GameRuntime area — 49 symbols across 4 files (71% cohesion)"
---

# Scripts · GameRuntime

49 symbols | 4 files | 71% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Data\TerrainGenerationSettings.cs`
- `Assets\Scripts\Presentation\ActionRadiusView.cs`
- `Assets\Scripts\Presentation\GameRuntime.cs`
- `Assets\Scripts\Presentation\ItemVisualSync.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Data\TerrainGenerationSettings.cs` | Size, size, Proportion, Seed, seed, ... |
| `Assets\Scripts\Presentation\ActionRadiusView.cs` | SortingOrder, _material, lineColor, Initialize, centerWorld, ... |
| `Assets\Scripts\Presentation\GameRuntime.cs` | cellSize, Research, GameRuntime, IsUIBlockingInput, Grid, ... |
| `Assets\Scripts\Presentation\ItemVisualSync.cs` | Initialize, grid, spriteFactory, itemDatabase |

## Entry Points

- `Assets\Scripts\Presentation\GameRuntime.cs::GameRuntime.Start`

## Connected Communities

- **Scripts/Presentation · WorldContentSpawner** (2 cross-edges)
- **Scripts · BuildingSpawner** (1 cross-edges)
- **Scripts/Presentation · Unregister** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-27")
explore(operation:"context", task:"understand Scripts · GameRuntime", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\Presentation\GameRuntime.cs::GameRuntime.Start"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
