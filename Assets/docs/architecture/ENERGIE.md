# Power

Global electrical supply and demand, and how the current is shared out when there is not enough of it.

Implemented by `PowerSystem` (`Game.Gameplay.Power`), owned by `GameRuntime`:

```csharp
public bool TryDraw(string groupId, float kilowatts)
public void ReportSupply(float kilowatts)
public void Settle()
public bool IsPowered()
public float DemandOf(string groupId)
public float AllocatedTo(string groupId)
public int AskingInstancesOf(string groupId)
public int ServedInstancesOf(string groupId)
public PowerPriorityOrder Priority { get; set; }
```

## 1. Report, then settle

Consumers draw and sources report during their own tick; `Settle()`, called once per
`GameRuntime.Update()` before that tick, turns the previous frame's reports into `SettledDemand` and
`SettledSupply` **and into this frame's per-group budgets**. One frame of intentional lag. Recovery is
automatic the instant demand drops back at or under supply, with no cooldown.

`TryDraw` both reports and answers, deliberately: a building that asks counts towards the next frame's
demand whether or not it runs this one, and splitting the two invites a caller to do one without the
other. `BuildingRuntime.ComputeEffectivePerformance` is the single caller for every building whose
progress freezes while unpowered, and it draws against `Definition.Id`.

## 2. The current is allocated by type, in the order the player arranges

`Settle` walks the priority order and gives each group all of its demand or whatever is left of supply,
whichever is smaller - so **the shortage falls on exactly one group**, the one the running total
crosses, and everything below it gets nothing.

Stopping every powered building at once would be a dead end rather than a setback: the Data Center
stops, CU production with it, and without CU nothing can be built or burned to get out of it.

**Partial service is per instance, not per building's speed.** A group allocated 1 kW of the 2 kW it
asked for runs one of its two extractors rather than both at half speed: within a group, instances draw
until its share is spent, in the order they tick. A machine is either running or it is not, which is
the only thing the rest of the simulation knows how to represent. `ServedInstancesOf` and
`AskingInstancesOf` are counted at the draw, never divided out of the kilowatts - a group's instances
need not draw the same amount (the Data Center's demand is its installed components') and an instance
takes its whole demand or nothing.

**Nothing is served off the top.** A plant draws nothing from the network it feeds, so every kilowatt
belongs to a group the player can order; there is no ungated load for this system to be unable to
arbitrate.

The UI reads the settled totals and the per-group figures through this contract and must not inspect
individual buildings' private power fields.

## 3. The priority order

`PowerPriorityOrder`, owned by `GameRuntime.PowerPriority` and read by `PowerSystem` at every settle.
**An order of type identifiers, never of positions** - that is what makes the list extensible without a
migration.

Seeded from `GameRuntime`'s building catalogue, never from a list written by hand, so a type added to
the game appears in it without any screen or contract being edited. It holds **every** known type,
including ones never built: a screen may filter what it draws, the order underneath does not, which is
what stops the list reshuffling itself the day a new type is first built. A type the order has never
heard of ranks after everything in it.

Three restore rules, and they are the whole claim:

- a saved identifier the game still has keeps its place;
- a type the game has and the save does not - added since - goes to the bottom;
- a saved identifier the game no longer has is dropped silently, because a removed building type is
  not an error in somebody's save.

It travels as `SaveData.PowerPriority`, a list of ids; absent restores as the catalogue's own order,
which is the default arbitration anyway.
