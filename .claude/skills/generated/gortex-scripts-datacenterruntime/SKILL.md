---
name: gortex-scripts-datacenterruntime
description: "Work in the Scripts · DataCenterRuntime area — 48 symbols across 2 files (69% cohesion)"
---

# Scripts · DataCenterRuntime

48 symbols | 2 files | 69% cohesion

## When to Use

Use this skill when working on files in:
- `Assets\Scripts\Gameplay\Buildings\DataCenterRuntime.cs`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs`

## Key Files

| File | Symbols |
|------|---------|
| `Assets\Scripts\Gameplay\Buildings\DataCenterRuntime.cs` | itemId, _previousPowerDemand, DataCenterRuntime, MemorySlots, GetInputAmount, ... |
| `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs` | _research, _itemDatabase, DataCenterRuntimeTests, Tick_ExcessDelivered_StaysInInput_WhenNoEmptySlotLeft, StartsWithFourCpuAndFourMemorySlots, ... |

## Entry Points

- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs::DataCenterRuntimeTests.ComputeGrant_CreditsInstalledComponentsOutputForTheTicksDuration_WhenPowered`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs::DataCenterRuntimeTests.ComputeGrant_ZeroWhileUnpowered_EvenTheTickAComponentIsInstalled`
- `Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs::DataCenterRuntimeTests.Tick_InstallsDeliveredComponent_IntoFirstEmptySlot`

## Connected Communities

- **Scripts · Recovery_IsImmediate_OnceDemand…** (4 cross-edges)
- **Gameplay/Buildings · ComponentInstance** (4 cross-edges)
- **Scripts · ComputeSystem** (2 cross-edges)

## How to Explore

```
analyze(operation:"communities", id:"community-36")
explore(operation:"context", task:"understand Scripts · DataCenterRuntime", format:"gcx")
relations(operation:"usages", target:{symbol:"Assets\Scripts\Tests\EditMode\Gameplay\Buildings\DataCenterRuntimeTests.cs::DataCenterRuntimeTests.ComputeGrant_CreditsInstalledComponentsOutputForTheTicksDuration_WhenPowered"}, format:"gcx")
```

_`format: "gcx"` returns the [GCX1 compact wire format](../../docs/wire-format.md) — round-trippable, ~27% fewer tokens than JSON. Drop it for JSON output; agents using `@gortex/wire` or the Go `github.com/gortexhq/gcx-go` package decode either._
