---
name: gortex-scripts-direction
description: "Work in the Scripts · Direction area — 82 symbols across 10 files (69% cohesion)"
---

# Scripts · Direction

82 symbols | 10 files | 69% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Core\Direction.cs`
- `Assets\Scripts\Gameplay\Buildings\BuildingRuntime.cs`
- `Assets\Scripts\Gameplay\Buildings\ConveyorRuntime.cs`
- `Assets\Scripts\Gameplay\Buildings\SplitterRuntime.cs`
- `Assets\Scripts\Gameplay\Transport\TransportSystem.cs`
- `Assets\Scripts\Presentation\ConstructionInputAdapter.cs`
- `Assets\Scripts\Tests\EditMode\Core\DirectionTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\BuildingRuntimeEdgeGeometryTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\ConveyorOrientationTests.cs`
- `Assets\Scripts\Tests\EditMode\Grid\GridRuntimeTests.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Core\Direction.cs` | Direction, RotateCW, FromRotationDegrees, South, West, ... |
| `Assets\Scripts\Gameplay\Buildings\BuildingRuntime.cs` | ComputeEdgeCells, exitDirection, GetEdgeCells, footprintSize, cell, ... |
| `Assets\Scripts\Gameplay\Buildings\ConveyorRuntime.cs` | entryDirection, ConfigureAsCorner, exitDirection |
| `Assets\Scripts\Gameplay\Buildings\SplitterRuntime.cs` | fromDirection, EntrySide, definition, itemId, AddInput, ... |
| `Assets\Scripts\Gameplay\Transport\TransportSystem.cs` | TryDeliverSplitterItem, TryDeliverFromSplitter, direction, splitter, splitter |
| `Assets\Scripts\Presentation\ConstructionInputAdapter.cs` | DominantDirection, delta |
| `Assets\Scripts\Tests\EditMode\Core\DirectionTests.cs` | degrees, direction, expected, direction, direction, ... |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\BuildingRuntimeEdgeGeometryTests.cs` | ComputeEdgeCells_CoversAllFourSides_WithCorrectCountPerSide |
| `Assets\Scripts\Tests\EditMode\Gameplay\ConveyorOrientationTests.cs` | NewConveyor, exit, ConfigureAsCorner_IsDeterministic, ConfigureAsCorner_EqualOrOppositePairs_Throws, ConfigureAsStraight_SetsRotationToExitDirection, ... |
| `Assets\Scripts\Tests\EditMode\Grid\GridRuntimeTests.cs` | TestCase |

## Connected Communities

- **Scripts · TransportSystem** (3 cross-edges)
- **Scripts · ConstructionService** (1 cross-edges)
- **Scripts · Show** (1 cross-edges)
- **Scripts · ConveyorRuntime** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-42")
explore(operation:"context", task:"understand Scripts · Direction", format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
