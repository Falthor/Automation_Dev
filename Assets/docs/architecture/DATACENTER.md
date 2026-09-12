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

The draw, every `StabilityInterval`: **Stability %** chance of exactly `1.00`, otherwise a uniform draw
between the **fluctuation floor** and `1.00`, that floor being `0.70 − 0.40·(1 − wear/100)`.

| Wear | Stability | Floor | Mean yield |
|---|---|---|---|
| 100 (new) | 95 % | 0.70 | **0.993** |
| 25 (default threshold) | 46 % | 0.40 | **0.839** |
| 5 (calibration floor) | 33 % | 0.32 | **0.773** |

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

## 3. Replacement

The slider runs **5 to 60 %**, default **25 %**, and is re-read continuously: moving it acts
immediately, the value is not frozen at install. On crossing, the part goes into replacement, its CU
falls to **0 for `ReplacementDuration`**, then the slot takes a spare from the input if there is one -
otherwise it empties and automatic installation picks it up later. Wear keeps running during those
seconds, and if it reaches 0 the slot empties at once.

## 4. What multiplies everything else

**Axis arbitration.** `concentration = r² + (1−r)²`, and
`yield = floor + (1 − floor)·concentration` with `AxisYieldFloor` = 0.2. Split 50/50 gives **0.6**;
everything on one axis gives **1.0**. The same parts therefore produce 40 % less at equal shares.

**Power is all or nothing.** `ComputeEffectivePerformance` returns 1 or 0 - there is no gradual
degradation. Unpowered: no production **and no wear**. The same during priming: neither production nor
wear.

## 5. The shipped values

| | CU/s | kW | Nominal life |
|---|---|---|---|
| CPU MkI | 40 | 5.3 | 120 s |
| Memory MK1 | 25 | 2.5 | 120 s |

Consumption followed production in the same proportion when production rose from 15 to 40 and from 10
to 25 CU/s: **7.5 CU per kW for the CPU, 10 for the memory**. A full Data Center (4 + 4) therefore draws
31.2 kW - more than the Core alone supplies, so it needs a plant.

CPU and memory have **no mechanical difference**: same formulas, same thresholds, same curve. Only the
numbers and the bay differ. Slots start at 1 + 1 and gain a pair per bay research, capped at 4 + 4.

## 6. "Yield" means two things

Worth watching while reading the code. `DataCenterRuntime.GetYield()` is the **axis concentration**
factor and has nothing to do with wear. The factor the panel shows under the production figure is
`real / nominal`, which folds in wear, the stability draw, a replacement in progress **and** the
concentration.

Both are legitimate; they simply answer different questions - and it is the second the player watches,
because it is the only one that moves when the bays tire.
