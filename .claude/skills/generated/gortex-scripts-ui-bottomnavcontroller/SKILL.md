---
name: gortex-scripts-ui-bottomnavcontroller
description: "Work in the Scripts/UI · BottomNavController area — 38 symbols across 2 files (91% cohesion)"
---

# Scripts/UI · BottomNavController

38 symbols | 2 files | 91% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\UI\BottomNavController.cs`
- `Assets\Scripts\UI\BuildingMenuController.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\UI\BottomNavController.cs` | RefreshToolbar, RefreshCategoryHighlight, _slotIcons, keyboard, visualTree, ... |
| `Assets\Scripts\UI\BuildingMenuController.cs` | slotIndex, definition, AssignToSlot |

## Entry Points

- `Assets\Scripts\UI\BottomNavController.cs::BottomNavController.Start`

## Connected Communities

- **Scripts/UI · BuildingMenuController** (1 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-52")
explore(operation:"context", task:"understand Scripts/UI · BottomNavController", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\UI\BottomNavController.cs::BottomNavController.Start"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
