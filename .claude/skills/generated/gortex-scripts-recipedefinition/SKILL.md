---
name: gortex-scripts-recipedefinition
description: "Work in the Scripts · RecipeDefinition area — 37 symbols across 3 files (74% cohesion)"
---

# Scripts · RecipeDefinition

37 symbols | 3 files | 74% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Data\RecipeDatabase.cs`
- `Assets\Scripts\Data\RecipeDefinition.cs`
- `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Data\RecipeDatabase.cs` | RecipeDatabase, recipeId, _byId, recipes, BuildLookup, ... |
| `Assets\Scripts\Data\RecipeDefinition.cs` | unlockResearch, RecipeIngredient, ComputeCost, TimeSeconds, amount, ... |
| `Assets\Scripts\Tests\EditMode\TestSupport\TestDataFactory.cs` | computeCost, unlockResearch, timeSeconds, id, timeSeconds, ... |

## How to Explore

```
analyze(operation:"communities", id:"community-50")
explore(operation:"context", task:"understand Scripts · RecipeDefinition", format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
