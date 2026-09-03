---
name: gortex-scripts-itemdefinition
description: "Work in the Scripts · ItemDefinition area — 50 symbols across 11 files (75% cohesion)"
---

# Scripts · ItemDefinition

50 symbols | 11 files | 75% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Data\ItemDatabase.cs`
- `Assets\Scripts\Data\ItemDefinition.cs`
- `Assets\Scripts\Data\ItemType.cs`
- `Assets\Scripts\Gameplay\Buildings\ComponentInstance.cs`
- `Assets\Scripts\Tests\EditMode\Data\ItemDatabaseTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FactoryRuntimeTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\LaboratoryRuntimeTests.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\PowerplantGazRuntimeTests.cs`
- `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Data\ItemDatabase.cs` | _byId, BuildLookup, itemId, ItemDatabase, items, ... |
| `Assets\Scripts\Data\ItemDefinition.cs` | PowerKw, cuOutput, DisplayName, CuOutput, displayName, ... |
| `Assets\Scripts\Data\ItemType.cs` | Ingot, ItemType, Component, Ore |
| `Assets\Scripts\Gameplay\Buildings\ComponentInstance.cs` | ComponentInstance.<init>, itemDatabase, itemId |
| `Assets\Scripts\Tests\EditMode\Data\ItemDatabaseTests.cs` | Get_ReturnsRegisteredItem_ById, Get_ReturnsNull_ForUnknownId, ItemDatabaseTests, MinerAiCharbon_IsComponent_NotOre |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs` | SetUp, SetCuPower, pw, item, cu |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FactoryRuntimeTests.cs` | SetUp |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\FoundryRuntimeTests.cs` | SetUp |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\LaboratoryRuntimeTests.cs` | SetUp, SetUp |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\PowerplantGazRuntimeTests.cs` | SetUp |
| `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs` | recipes, NewItemDatabase, id, NewRecipeDatabase, NewItem, ... |

## Connected Communities

- **Scripts · ResearchDefinition** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-49")
explore(operation:"context", task:"understand Scripts · ItemDefinition", format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
