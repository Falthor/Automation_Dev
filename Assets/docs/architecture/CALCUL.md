# Compute

Two typed CU reserves - Building Compute and Research Compute - what credits each, what spends from
each, and why either is a currency rather than a flow.

Implemented by `ComputeSystem` (`Game.Gameplay.Compute`), one plain class instantiated twice.
`GameRuntime` owns both instances (`BuildingCompute`, `ResearchCompute`), plus a third,
`ArmamentCompute`, which has no producer or spender yet and is not shown anywhere - reserved for
when that content exists:

```csharp
public void Grant(float amount)
public bool CanSpend(float cost)
public void Spend(float cost)
public float SpendUpTo(float maxAmount)
public void Tick(float deltaTime)
```

## 1. A currency, not a flow

Each reserve is a pooled amount (`Reserve`, capped at `ReserveCap`, starting full) credited by
`Grant`. Every spender pays a **single one-shot chunk the instant a cycle starts**, through
`CanSpend`/`Spend`: a recipe-based production building pays its recipe's compute cost, an Extractor
and a Gas Powerplant their own `CuCostPerCycle`, a Data Center's priming its fixed 1500 CU. There is
no throttling ratio - a cycle either affords itself in full or waits at zero progress. A powerplant
that cannot pay does not light its fuel, and supplies no power that tick.

**Every one-shot spender pays from Building Compute.** Production, extraction, priming - none of it
is a research, so none of it draws from the other reserve.

**Power plants ask first.** `TransportSystem.Tick` runs their state machines in a pass of their own
before every other building's, so a base that has spent itself down to nothing relights its plants
rather than handing the first CU that comes back to whichever factory happened to be registered first.
A plant produces the current the rest of the base runs on; it is the one consumer that must not queue
behind its own dependants.

## 2. Research picks its own reserve

**Research** and **Data Center priming** are the only per-second draws, both through
`SpendUpTo(maxAmount)`: it withdraws up to `maxAmount`, less if the reserve holds less, and returns how
much was actually taken - it never goes negative and never throws. Priming always draws from Building
Compute (§1); which reserve a research's own absorption draws from is that research's own choice.

**`ResearchDefinition.ComputeSource`** names it: `Research` (the default) or `Building`, for a
research authored as an engineering effort rather than a research one. `ResearchSystem` holds both
reserves and reads the active research's `ComputeSource` on every `Tick` - never a fixed reserve
chosen once at construction, so a research started before `ComputeSource` existed on its asset and
one added since behave identically.

This is a deliberate exception to §1's one-shot rule for both callers. A freshly placed Data Center
spends ninety seconds absorbing its priming cost at a fixed rate before producing anything, and
pauses at zero CU exactly like research does: progress is a running total that is never rolled back.
`SpendUpTo` has exactly these two callers; every other spender uses `CanSpend`/`Spend`.

## 3. What credits each reserve

- The **Core**, whose `CuOutput` is 0 in the current data - it produces no CU. The
  `CuOutputIntervalSeconds` mechanism remains in code but has no effect at that value. What it does
  grant lands in **Building Compute**.
- A **Data Center**, which credits its installed components' output for the duration of each tick, only
  while powered and only once primed, split across its research and buildings axes by a
  concentration-based yield curve. `GetResearchAxisProduction()` credits **Research Compute**;
  `GetBuildingsAxisProduction()` credits **Building Compute** - two separate `Grant` calls, never
  summed into one.

Anything granted above a reserve's cap is discarded.

`Tick(deltaTime)`, called once per `GameRuntime.Update()` for every reserve, only advances the window
`IncomePerSecond` is averaged over - the credited-CU-per-second figure the UI shows. It counts only
what was really credited.

## 4. What the UI may read, and what it must not

The Top Bar shows Building Compute and Research Compute as two separate elements, each reading its
own `Reserve` and `IncomePerSecond` through this contract. Neither **must show a continuous
consumption figure**, because outside research and priming there is none. The two legitimate
continuous-rate figures are research's own absorption ceiling with its estimated time remaining, and
the Data Center's priming progress; both are read from the systems that own them, never recomputed
here.

Armament Compute is not read anywhere yet.

## 5. Thresholds read against the cap

A threshold expressed against a reserve lives **here, beside the cap**, as a fraction of it - never as
an absolute elsewhere. `ExplorerFleetArrivalFraction` is the one such threshold today, read against
**Building Compute** - the reserve that inherited the single undifferentiated reserve's role when it
split in two.

An absolute is left behind whenever the cap moves, which turns an emergency trigger into an
introduction trigger with nothing to signal it; a fraction has no second number to forget. And the
property belongs to the reserve it is read against rather than to whatever reads it - carried on the
consumer's own settings, it drifts away with them.
