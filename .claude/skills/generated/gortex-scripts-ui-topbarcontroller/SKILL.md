---
name: gortex-scripts-ui-topbarcontroller
description: "Work in the Scripts/UI · TopBarController area — 51 symbols across 1 files (97% cohesion)"
---

# Scripts/UI · TopBarController

51 symbols | 1 files | 97% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\UI\TopBarController.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\UI\TopBarController.cs` | FormatThousands, _computeCard, seconds, Update, powerIcon, ... |

## Entry Points

- `Assets\Scripts\UI\TopBarController.cs::TopBarController.Start`

## How to Explore

```
analyze(operation:"communities", id:"community-67")
explore(operation:"context", task:"understand Scripts/UI · TopBarController", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\UI\TopBarController.cs::TopBarController.Start"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
