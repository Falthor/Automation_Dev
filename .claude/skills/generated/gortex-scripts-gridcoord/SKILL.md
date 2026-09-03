---
name: gortex-scripts-gridcoord
description: "Work in the Scripts · GridCoord area — 99 symbols across 13 files (65% cohesion)"
---

# Scripts · GridCoord

99 symbols | 13 files | 65% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Construction\ConstructionService.cs`
- `Assets\Scripts\Core\Direction.cs`
- `Assets\Scripts\Core\GridCoord.cs`
- `Assets\Scripts\Gameplay\Buildings\BuildingRuntime.cs`
- `Assets\Scripts\Gameplay\Buildings\CrossFootprint.cs`
- `Assets\Scripts\Gameplay\Buildings\CrossroadRuntime.cs`
- `Assets\Scripts\Gameplay\Buildings\SplitterRuntime.cs`
- `Assets\Scripts\Gameplay\Transport\TransportSystem.cs`
- `Assets\Scripts\Grid\GridRuntime.cs`
- `Assets\Scripts\Presentation\ConstructionInputAdapter.cs`
- `Assets\Scripts\Tests\EditMode\Core\DirectionTests.cs`
- `Assets\Scripts\Tests\EditMode\Core\GridCoordTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\BuildingRuntimeEdgeGeometryTests.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Construction\ConstructionService.cs` | Cancel |
| `Assets\Scripts\Core\Direction.cs` | direction, ToOffset |
| `Assets\Scripts\Core\GridCoord.cs` | x, ToVector2Int, FromVector2Int, GetHashCode, other, ... |
| `Assets\Scripts\Gameplay\Buildings\BuildingRuntime.cs` | footprintSize, cell, GetOutputCells, exitDirection, GetOutputCell, ... |
| `Assets\Scripts\Gameplay\Buildings\CrossFootprint.cs` | ArmCell, direction, NeighborCell, CrossFootprint, direction, ... |
| `Assets\Scripts\Gameplay\Buildings\CrossroadRuntime.cs` | ArmCell, direction |
| `Assets\Scripts\Gameplay\Buildings\SplitterRuntime.cs` | direction, direction, ArmCell, NeighborCell |
| `Assets\Scripts\Gameplay\Transport\TransportSystem.cs` | TickSplitters, OutputsTo, cell, TryPullFromNeighbor, destinationCell, ... |
| `Assets\Scripts\Grid\GridRuntime.cs` | world, WorldToCell |
| `Assets\Scripts\Presentation\ConstructionInputAdapter.cs` | HandleRotateAndCancel, Update, cell, AllDirections, _lastDemolishedCell, ... |
| `Assets\Scripts\Tests\EditMode\Core\DirectionTests.cs` | ToOffset_MatchesCardinalConvention |
| `Assets\Scripts\Tests\EditMode\Core\GridCoordTests.cs` | Addition_WithDirection_OffsetsByOneCell, Equality_ComparesByValue, GridCoordTests |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\BuildingRuntimeEdgeGeometryTests.cs` | Footprint3x2, ComputeOutputCells_North_ReturnsOneCellPerColumn, GetOutputCell_SingleCell_MatchesFirstOfGetOutputCells, Origin, BuildingRuntimeEdgeGeometryTests, ... |

## Connected Communities

- **Scripts · Direction** (5 cross-edges)
- **Scripts · ConstructionService** (5 cross-edges)
- **Scripts · TransportSystem** (3 cross-edges)
- **Scripts/Presentation · Unregister** (3 cross-edges)
- **Scripts · BuildingSpawner** (2 cross-edges)
- **Scripts · ConveyorRuntime** (1 cross-edges)
- **Scripts/Presentation · PlaceBar** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-4")
explore(operation:"context", task:"understand Scripts · GridCoord", format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
