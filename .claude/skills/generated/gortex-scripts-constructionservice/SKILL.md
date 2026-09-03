---
name: gortex-scripts-constructionservice
description: "Work in the Scripts · ConstructionService area — 107 symbols across 8 files (74% cohesion)"
---

# Scripts · ConstructionService

107 symbols | 8 files | 74% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Construction\ConstructionService.cs`
- `Assets\Scripts\Data\StorageDefinition.cs`
- `Assets\Scripts\Gameplay\Buildings\ConveyorRuntime.cs`
- `Assets\Scripts\Gameplay\Transport\TransportSystem.cs`
- `Assets\Scripts\Grid\GridRuntime.cs`
- `Assets\Scripts\Presentation\ConstructionInputAdapter.cs`
- `Assets\Scripts\Tests\EditMode\Construction\ConstructionServiceTests.cs`
- `Assets\Scripts\Tests\EditMode\Grid\GridRuntimeTests.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Construction\ConstructionService.cs` | cell, SetPreviewRotation, GetAvailableAmount, PayCost, _transport, ... |
| `Assets\Scripts\Data\StorageDefinition.cs` | StorageDefinition |
| `Assets\Scripts\Gameplay\Buildings\ConveyorRuntime.cs` | ConfigureAsCornerShape |
| `Assets\Scripts\Gameplay\Transport\TransportSystem.cs` | cell, IsBeltOrStorage |
| `Assets\Scripts\Grid\GridRuntime.cs` | sizeInCells, cellSize, origin, ClearOccupant, GridRuntime, ... |
| `Assets\Scripts\Presentation\ConstructionInputAdapter.cs` | axis, PlaceStraightSegment, cell |
| `Assets\Scripts\Tests\EditMode\Construction\ConstructionServiceTests.cs` | SelectBuilding_Cancel_SetPreviewRotation_UpdateState, CanPlace_True_AfterUnlockResearchIsUnlocked, NewGatedStorageDefinition, NewService, MakeResearch, ... |
| `Assets\Scripts\Tests\EditMode\Grid\GridRuntimeTests.cs` | Occupancy_TracksSetClearOverwrite |

## Entry Points

- `Assets\Scripts\Tests\EditMode\Grid\GridRuntimeTests.cs::GridRuntimeTests.Occupancy_TracksSetClearOverwrite`
- `Assets\Scripts\Tests\EditMode\Construction\ConstructionServiceTests.cs::ConstructionServiceTests.CanPlace_True_AfterUnlockResearchIsUnlocked`
- `Assets\Scripts\Tests\EditMode\Construction\ConstructionServiceTests.cs::ConstructionServiceTests.TryPlace_OntoExistingConveyor_OvertakesAndReplaces`
- `Assets\Scripts\Tests\EditMode\Construction\ConstructionServiceTests.cs::ConstructionServiceTests.CanPlace_MatchesTryPlaceOutcome_WithoutMutating`
- `Assets\Scripts\Tests\EditMode\Construction\ConstructionServiceTests.cs::ConstructionServiceTests.SelectBuilding_Cancel_SetPreviewRotation_UpdateState`

## Connected Communities

- **Scripts · ResearchDefinition** (6 cross-edges)
- **Scripts · ConveyorRuntime** (2 cross-edges)
- **Scripts/Presentation · Unregister** (2 cross-edges)
- **Scripts · GridCoord** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-34")
explore(operation:"context", task:"understand Scripts · ConstructionService", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\Tests\EditMode\Grid\GridRuntimeTests.cs::GridRuntimeTests.Occupancy_TracksSetClearOverwrite"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
