# Data Center

The hardware model: three quantities, the wear curve they run on, when a part is replaced, and what
multiplies the result.

**None of this is shown to the player.** The panel gives a figure, a band and a bar; its explanation
window states no formula. This document is for whoever changes the balance.

Source of truth is `ComponentInstance.cs` and `DataCenterRuntime.cs`. If a figure here disagrees with
the code, the code is right and this document is wrong.

## 1. Three quantities, routinely confused

| | What it is | Formula |
|---|---|---|
| **Wear** | the only one that moves on its own, 100 (new) → 0 (dead) | decays, accelerating - below |
| **Stability** | a **probability**, never a multiplier | `95% − 65%·(1 − wear/100)` |
| **A part's yield** | the multiplier actually applied to its base CU | drawn every `StabilityInterval` |

The draw, every `StabilityInterval`: **Stability %** chance of the **performance ceiling**, otherwise a
uniform draw between the **fluctuation floor** and that same ceiling. The floor is
`0.70 − 0.40·(1 − wear/100)`. The ceiling is **1.00 down to 60 % wear**, then declines:
`1.00 − 0.20·((60 − wear)/55)` below that - a worn part can no longer reach what a fresh one does, even
on a good roll. Below 60 % wear both branches of the draw are therefore capped under 1.00.

| Wear | Stability | Floor | Ceiling | Mean yield |
|---|---|---|---|---|
| 100 (new) | 95 % | 0.70 | 1.00 | **0.993** |
| 25 (default threshold) | 46 % | 0.40 | 0.873 | **0.746** |
| 5 (calibration floor) | 33 % | 0.32 | 0.80 | **0.640** |

It is the **width of that range** the panel draws, not the stability as a number: a worn part visibly
jumps from one draw to the next, which reads before any percentage does. The interval came down from
five seconds to two so the panel's tick moves often enough to read as instability rather than as a
freeze - **the draw itself is unchanged**, the mean does not move, only the variance over a minute
tightens. Not to be confused with `ReplacementDuration`, which is five seconds.

## 2. The wear curve

```text
dWear/dt = −baseLoss · (1 + 2·(1 − wear/100))
```

The multiplier is **1** at install, **2.5** at 25 % wear, **2.9** at 5 %. `baseLoss` is solved once at
construction so that wear runs from 100 to `LifetimeFloorPercent` (5) in exactly the lifetime drawn for
that part:

```text
baseLoss = 100 · ln(2.9) / (2·L)        →  0.444 pt/s for L = 120 s
t(wear w) = L · ln(3 − 2w) / ln(2.9)
```

**The calibration floor is a constant, not the replacement threshold**, and this is the easiest thing to
break here. Calibrating the curve on the threshold would make lifetime *independent* of the threshold:
moving the slider would change nothing, the curve straightening to arrive at the threshold at the same
instant. The threshold only decides where on a fixed curve one stops.

Nominal lifetime is **dispersed ±25 %** through a seeded generator the Data Center owns, so two parts
installed together do not die together.

Time to replacement, for L = 120 s:

| Threshold | Time | As a fraction of L |
|---|---|---|
| 60 % | 66 s | 0.552 |
| 25 % (default) | 103 s | 0.861 |
| 5 % | 120 s | 1.000 |

## 3. Replacement and reconfiguration

The slider runs **5 to 60 %**, default **25 %**, and is re-read continuously: moving it acts
immediately, the value is not frozen at install. On crossing, the part goes into replacement, its CU
falls to **0 for `ReplacementDuration`** (5 s), then the bay takes a spare from the input if there is
one - otherwise it empties and automatic installation picks it up later. **Wear is frozen for as long
as a part is replacing or reconfiguring** - it has already stopped producing, so it stops ageing too;
a component can no longer reach 0 % wear mid-window regardless of how low the threshold is set.

Reconfiguring a bay to the other type reuses this exact mechanism (`DataCenterBay.ReconfigureTarget`
set, the installed `ComponentInstance.IsReplacing` driving the same timer) rather than a parallel one -
see §6. Retargeting a bay that is already replacing or reconfiguring does not restart the timer;
retargeting back to what it already is cancels for free unless the part had already crossed its own
threshold, in which case the ordinary wear-triggered replacement it was already due for continues.

## 4. What multiplies everything else

**Axis arbitration.** `concentration = r² + (1−r)²`, and
`baseYield = floor + (1 − floor)·concentration` with `AxisYieldFloor` = 0.2 (`GetYield()`). Split
50/50 gives **0.6**; everything on one axis gives **1.0**. The same parts therefore produce 40 % less
at equal shares, before Memory's recovery below.

**Memory coverage recovers part of that loss.** `coverage = min(1, activeMemory·MemoryAssistCapacity /
activeCpu)` (0 with no active CPU - never a division by zero), and
`finalYield = baseYield + (1 − baseYield)·coverage·MemoryPenaltyRecovery` with
`MemoryPenaltyRecovery` = 0.75 (`GetFinalYield()`, DataCenterRuntime). Full coverage at a 50/50 split
recovers 0.75 of the 0.4 lost, landing at **0.9**, never 1.0 - Memory makes an even split cheap, not
free. `MemoryAssistCapacity` starts at 1.0 CPU per active Memory bay and is a research target (highest
completed wins, RECHERCHE.md): 1.5 after Ordonnancement parallèle I, 2.0 after II.

**No active CPU, no output**, however much Memory is installed: `GetResearchAxisProduction()`/
`GetBuildingsAxisProduction()` are gated on `ActiveCpuCount > 0`. Memory's own `EffectiveCu()` still
counts toward `GetTotalComputeOutput()` (the diagnostic "raw" figure) - the gate is on what is
actually credited, not on what a bay nominally produces.

**Power is all or nothing.** `ComputeEffectivePerformance` returns 1 or 0 - there is no gradual
degradation. Unpowered: no production **and no wear**. The same during priming: neither production nor
wear.

## 5. The shipped values

| | CU/s | kW | Nominal life |
|---|---|---|---|
| CPU MkI | 40 | 5.3 | 120 s |
| Memory MK1 | 25 | 2.5 | 120 s |

Consumption followed production in the same proportion when production rose from 15 to 40 and from 10
to 25 CU/s: **7.5 CU per kW for the CPU, 10 for the memory**.

CPU and memory have **no mechanical difference** at the component level: same formulas, same
thresholds, same curve. Only the numbers, and which item a bay is currently configured to take,
differ.

## 6. Universal bays

A bay (`DataCenterBay`) is physically interchangeable; what it is comes from
`DataCenterBayType Assignment` (`Unassigned`/`Cpu`/`Memory`), chosen by the player through
`DataCenterRuntime.SetBayAssignment` - the one entry point for both a first assignment and a later
reconfiguration. A bay starts `Unassigned` and empty and installs nothing until assigned; assigning an
empty bay (whichever type it already is) is instant. Assigning an occupied bay to a different type
starts the reconfiguration described in §3.

Every Data Center starts with **2** universal bays. `DataCenterBayPairs` research effects (unchanged
by name - `datacenter_bay_1`/`_2`/`_3`) each add **2** fresh `Unassigned` bays rather than one
pre-typed CPU and one Memory, up to `MaxBaySlots` = 8. The totals at each tier are unchanged from
before this bay type existed (2 → 4 → 6), since the old model always granted its pairs symmetrically;
only `datacenter_bay_3` (6 → 8) is new.

**Assigning `Memory` is gated; `Cpu` never is.** `SetBayAssignment` returns `bool` and refuses a
`Memory` target outright until `DataCenterRuntime.HasUnlockedMemory` is true - a research target
(RECHERCHE.md's `UnlockDataCenterMemory`, `memory_architecture`), not a step, re-derived the same
way as `MemoryAssistCapacity` (scanned on construction and on every `ResearchCompleted`, never goes
back to false). The panel reflects this rather than deciding it: the Memory button stays visible but
disabled, with a tooltip naming the research, until the runtime says otherwise. Reconfiguring an
already-Memory bay away from it, or re-confirming what it already is, is never blocked - the gate is
on becoming Memory, not on having been Memory.

`RestoreState` grants `memory_architecture` on the spot to a save that already proves Memory was
usable before this research existed - an active or targeted Memory bay right there, or
`ordonnancement_1`/`_2` already unlocked (unreachable under the current tree without it). A save
with only `core_directive_4` and no such proof gets nothing extra: the gate holding for a save that
never actually used Memory is the point of it. A bay a save restores as Memory is never stripped
either way - only future assignments are gated, never what a save already has.

## 7. "Yield" means several things

Worth watching while reading the code. `GetYield()` is the axis concentration factor alone
(`baseYield`, §4); `GetFinalYield()` adds Memory's coverage recovery on top. The factor the panel
shows under the production figure is `real / nominal`, which folds in wear, the stability draw, a
replacement/reconfiguration in progress, the axis split **and** Memory coverage.

All three are legitimate; they simply answer different questions - and it is `real / nominal` the
player watches, because it is the only one that moves when the bays tire.
