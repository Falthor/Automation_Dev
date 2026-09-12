# Compute

The CU reserve: what credits it, what spends it, and why it is a currency rather than a flow.

Implemented by `ComputeSystem` (`Game.Gameplay.Compute`), owned by `GameRuntime`:

```csharp
public void Grant(float amount)
public bool CanSpend(float cost)
public void Spend(float cost)
public float SpendUpTo(float maxAmount)
public void Tick(float deltaTime)
```

## 1. A currency, not a flow

CU is a pooled reserve (`Reserve`, capped at `ReserveCap`, starting full) credited by `Grant`. Every
spender pays a **single one-shot chunk the instant a cycle starts**, through `CanSpend`/`Spend`: a
recipe-based production building pays its recipe's compute cost, an Extractor and a Gas Powerplant
their own `CuCostPerCycle`. There is no throttling ratio - a cycle either affords itself in full or
waits at zero progress. A powerplant that cannot pay does not light its fuel, and supplies no power
that tick.

**Power plants ask first.** `TransportSystem.Tick` runs their state machines in a pass of their own
before every other building's, so a base that has spent itself down to nothing relights its plants
rather than handing the first CU that comes back to whichever factory happened to be registered first.
A plant produces the current the rest of the base runs on; it is the one consumer that must not queue
behind its own dependants.

## 2. The two continuous draws, and why they are the exception

**Research** and **Data Center priming** are the only per-second draws, both through
`SpendUpTo(maxAmount)`: it withdraws up to `maxAmount`, less if the reserve holds less, and returns how
much was actually taken - it never goes negative and never throws.

This is a deliberate exception to the rule above. A freshly placed Data Center spends ninety seconds
absorbing its priming cost at a fixed rate before producing anything, and pauses at zero CU exactly
like research does: progress is a running total that is never rolled back. `SpendUpTo` has exactly
these two callers; every other spender uses `CanSpend`/`Spend`.

## 3. What credits the reserve

- The **Core**, whose `CuOutput` is 0 in the current data - it produces no CU. The
  `CuOutputIntervalSeconds` mechanism remains in code but has no effect at that value.
- A **Data Center**, which credits its installed components' output for the duration of each tick, only
  while powered and only once primed, split across its research and buildings axes by a
  concentration-based yield curve. Both axes currently credit this same single reserve.

Anything granted above the cap is discarded.

`Tick(deltaTime)`, called once per `GameRuntime.Update()`, only advances the window `IncomePerSecond`
is averaged over - the credited-CU-per-second figure the UI shows. It counts only what was really
credited.

## 4. What the UI may read, and what it must not

The general CU display reads `Reserve` and `IncomePerSecond` through this contract. It **must not show
a continuous consumption figure there**, because outside research and priming there is none. The two
legitimate continuous-rate figures are research's own absorption ceiling with its estimated time
remaining, and the Data Center's priming progress; both are read from the systems that own them, never
recomputed here.

## 5. Thresholds read against the cap

A threshold expressed against the reserve lives **here, beside the cap**, as a fraction of it - never as
an absolute elsewhere. `ExplorerFleetArrivalFraction` is the one such threshold today.

An absolute is left behind whenever the cap moves, which turns an emergency trigger into an
introduction trigger with nothing to signal it; a fraction has no second number to forget. And the
property belongs to the reserve it is read against rather than to whatever reads it - carried on the
consumer's own settings, it drifts away with them.
