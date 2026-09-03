---
name: gortex-scripts-buildingruntime
description: "Work in the Scripts · BuildingRuntime area — 40 symbols across 2 files (60% cohesion)"
---

# Scripts · BuildingRuntime

40 symbols | 2 files | 60% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Gameplay\Buildings\BuildingRuntime.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\BuildingRuntimeFlowDefaultsTests.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Gameplay\Buildings\BuildingRuntime.cs` | AddInput, fromDirection, itemId, CanAcceptInput, GetInputAmount, ... |
| `Assets\Scripts\Tests\EditMode\Gameplay\BuildingRuntimeFlowDefaultsTests.cs` | BuildingRuntime_FlowContract_DefaultsToNeutral |

## Entry Points

- `Assets\Scripts\Tests\EditMode\Gameplay\BuildingRuntimeFlowDefaultsTests.cs::BuildingRuntimeFlowDefaultsTests.BuildingRuntime_FlowContract_DefaultsToNeutral`

## How to Explore

```
analyze(operation:"communities", id:"community-23")
explore(operation:"context", task:"understand Scripts · BuildingRuntime", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\Tests\EditMode\Gameplay\BuildingRuntimeFlowDefaultsTests.cs::BuildingRuntimeFlowDefaultsTests.BuildingRuntime_FlowContract_DefaultsToNeutral"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
