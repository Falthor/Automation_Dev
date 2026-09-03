---
name: gortex-scripts-researchdefinition
description: "Work in the Scripts · ResearchDefinition area — 56 symbols across 8 files (80% cohesion)"
---

# Scripts · ResearchDefinition

56 symbols | 8 files | 80% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Data\ResearchDefinition.cs`
- `Assets\Scripts\Gameplay\Buildings\DataCenterRuntime.cs`
- `Assets\Scripts\Gameplay\Research\ResearchSystem.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FactoryRuntimeTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Research\ResearchSystemTests.cs`
- `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs`
- `Assets\Scripts\UI\ResearchPanelController.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Data\ResearchDefinition.cs` | DisplayName, cost, RequiresResearch, requiresResearch, displayName, ... |
| `Assets\Scripts\Gameplay\Buildings\DataCenterRuntime.cs` | OnUnregistered |
| `Assets\Scripts\Gameplay\Research\ResearchSystem.cs` | ArePrerequisitesMet, GetActiveLabCount, research, ReportActiveLab, IsUnlocked, ... |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs` | NewDataCenter_StartsWithExtraSlot_IfAlreadyUnlockedAtConstruction, ResearchCompleted_ExtraCpuSlot_AppendsOneCpuSlot_NotMemory, OnUnregistered_StopsReactingToFutureResearchCompletions |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FactoryRuntimeTests.cs` | GetRecipeIds_IncludesGatedRecipe_OnceUnlocked |
| `Assets\Scripts\Tests\EditMode\Gameplay\Research\ResearchSystemTests.cs` | Start_Succeeds_DeductsCost, ResearchCompleted_EventFires_WithCompletedId, Start_Fails_WhenAlreadyActive, ReportActiveLab_IsOneFrameLagged_LikePowerAndCompute, Tick_OneActiveLab_CompletesAfter60Seconds, ... |
| `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs` | NewResearch, cost, id |
| `Assets\Scripts\UI\ResearchPanelController.cs` | StatusText, Refresh, completed, BuildRows, research, ... |

## Entry Points

- `Assets\Scripts\Tests\EditMode\Gameplay\Research\ResearchSystemTests.cs::ResearchSystemTests.Start_Fails_WhenAlreadyUnlocked`
- `Assets\Scripts\Tests\EditMode\Gameplay\Research\ResearchSystemTests.cs::ResearchSystemTests.Tick_OneActiveLab_CompletesAfter60Seconds`
- `Assets\Scripts\Tests\EditMode\Gameplay\Research\ResearchSystemTests.cs::ResearchSystemTests.Tick_TwoActiveLabs_CompletesTwiceAsFast`
- `Assets\Scripts\Tests\EditMode\Gameplay\Research\ResearchSystemTests.cs::ResearchSystemTests.Start_Succeeds_DeductsCost`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs::DataCenterRuntimeTests.NewDataCenter_StartsWithExtraSlot_IfAlreadyUnlockedAtConstruction`

## Connected Communities

- **Scripts · DataCenterRuntime** (3 cross-edges)
- **Scripts · ProductionPanelController** (1 cross-edges)
- **Scripts · FactoryRuntimeTests** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-45")
explore(operation:"context", task:"understand Scripts · ResearchDefinition", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\Tests\EditMode\Gameplay\Research\ResearchSystemTests.cs::ResearchSystemTests.Start_Fails_WhenAlreadyUnlocked"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
