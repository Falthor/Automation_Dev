---
name: gortex-scripts-buildingdefinition
description: "Work in the Scripts · BuildingDefinition area — 40 symbols across 6 files (65% cohesion)"
---

# Scripts · BuildingDefinition

40 symbols | 6 files | 65% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Data\BuildingDefinition.cs`
- `Assets\Scripts\Data\CrossroadDefinition.cs`
- `Assets\Scripts\Data\SplitterDefinition.cs`
- `Assets\Scripts\Presentation\ConstructionInputAdapter.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\BuildingRuntimeFlowDefaultsTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\BuildingRuntimeEdgeGeometryTests.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Data\BuildingDefinition.cs` | BuildingDefinition, UnlockResearch, unlockResearch, CuCostPerCycle, FootprintSize, ... |
| `Assets\Scripts\Data\CrossroadDefinition.cs` | FootprintCells, RenderOverscan, CrossroadDefinition |
| `Assets\Scripts\Data\SplitterDefinition.cs` | artNativeEntrySide, SplitterDefinition, RenderOverscan, FootprintCells, ArtNativeEntrySide |
| `Assets\Scripts\Presentation\ConstructionInputAdapter.cs` | ResolveGhostRotation, definition |
| `Assets\Scripts\Tests\EditMode\Gameplay\BuildingRuntimeFlowDefaultsTests.cs` | DummyDefinition |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\BuildingRuntimeEdgeGeometryTests.cs` | DummySquareDefinition |

## How to Explore

```
analyze(operation:"communities", id:"community-53")
explore(operation:"context", task:"understand Scripts · BuildingDefinition", format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
