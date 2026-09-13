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

**`BuildingRuntime.IsUnderpowered`** is set there too - true exactly when the last draw was refused,
false otherwise. `PowerShortageBadgeView` reads it every frame the same way `PausedBadgeView` reads
`IsPaused`, and wears a red Energy icon on any consumer (`Definition.DrawsPower`) currently stalled on
it.

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

## 4. The pole network

A power-consuming building draws only while `BuildingRuntime.PoleNetwork` (null means unrestricted -
the same convention `IsWithinCoreRadius` uses for a missing Core) says it stands within reach of a
pole whose network currently reaches a source. Implemented by `PoleNetworkSystem`
(`Game.Gameplay.Power`), owned by `GameRuntime.PoleNetwork`, ticked once per frame before
`Transport.Tick()` so every consumer's draw that frame reads this frame's fed state, not last frame's.

```csharp
public void RegisterPole(PoleRuntime pole)
public void UnregisterPole(PoleRuntime pole)
public List<PoleRuntime> FindConnectionCandidates(GridCoord cell, PoleRuntime exclude = null)
public void Tick(float deltaTime)
public bool IsNetworkFed(int networkId)
public bool IsCovered(GridCoord origin, Vector2Int[] footprintCells)
public IReadOnlyList<PoleRuntime> Poles { get; }
public IReadOnlyList<(PoleRuntime A, PoleRuntime B)> Edges { get; }
```

**The connection rule, and it is the whole rule.** A pole placed within `ConnectionRangeCells` of one
or more existing poles connects to the *nearest pole of each distinct network* in range - never just
the single nearest neighbour, which would leave a pole at the edge of one group unconnected to a
third one also in range. One cable per distinct network found; zero cables (a fresh, isolated
network of one) when none are. Two poles of a network already connected another way never connect a
second time - only the nearest representative of each network is ever a candidate.

**Two ranges, not one.** `PowerRangeCells` (2, a 5x5 square - Chebyshev distance, not a circle) is how
far a pole reaches a building to power it and how far it reaches a Core/Powerplant to be fed by it -
the same "who is next to me" test either way. `ConnectionRangeCells` (8) is how far apart two poles
may stand and still connect - deliberately larger, since a pole's wire reaches further than its own
power field. Both live on `PoleNetworkSettings`, one asset like the rest of this project's tunables.

**A network is fed when any one of its poles is within `PowerRangeCells` of a source.** A source is
`BuildingDefinition.SuppliesPower` - true for the Core and the Gas Powerplant today, the only two
types that ever call `PowerSystem.ReportSupply` - read purely by position, never by whether the
source is actually producing right now (a Powerplant out of fuel still counts as "a source nearby"
for wiring purposes). Being fed is a property of the whole network, not of the one pole next to the
source: `Tick()` computes it once per network and every pole in it shares the answer.

**A pole still under construction connects nothing and conducts nothing.** It occupies its cell
immediately like any chantier, but every fed/coverage query skips it until it is actually built - a
half-built pole has no wire in it yet.

**Removing a pole splits its network exactly when the graph says it should, and nothing decides that
by looking at the removed pole's position.** Every cable touching the removed pole is dropped, then
every remaining pole's network is recomputed fresh by walking what is left of the graph - the one
real computation this system does, and simple rather than clever because the pole counts this game
ever reaches make a full recompute just as cheap as detecting a split specially. The graph is a tree
by construction (one cable per network found, never a second between two poles already in one), so
removing a pole from its middle genuinely splits it.

**Nothing about the graph is saved.** A pole is an ordinary building - its position is captured and
restored exactly like any other (SAUVEGARDE.md) - but which pole is in which network and which
cables exist are never written to the save at all. `PoleRuntime.NetworkId` is entirely a function of
every pole's position and the order they were placed in; restoring calls `RegisterPole` for each
restored pole in save order (`ConstructionService.CreateAndRegisterOccupant`, the same chokepoint
placement and restore both already go through), which deterministically reconstructs the exact
groups the session had. The same "what comes from the player is saved, what is derived is recomputed"
rule `WreckField`'s own seed-derived layout already follows.

**Placing a pole previews exactly the cable(s) it would actually create.** While a Pole is the armed
construction tool, `ConstructionInputAdapter.UpdateGhost` asks `PoleNetworkSystem.FindConnectionCandidates`
for the candidate cell every frame - the same nearest-per-network lookup `RegisterPole` itself commits
with, never a separate approximation - and `PoleNetworkVisualSync.ShowPreview` draws one translucent
cable to each pole found. No cable at all is therefore the honest signal that the candidate is outside
every network's `ConnectionRangeCells`, without a second "am I in range" indicator to keep in sync with
the real rule. `PoleRangeView` (below) follows the same ghost for the power field, so placing a pole
shows both reaches - who it would connect to, and who it would power - before it costs anything.

**Dragging a Pole lays a chain spaced exactly `ConnectionRangeCells` apart, in whatever direction the
cursor takes.** `ConstructionInputAdapter.AdvancePoleDrag` is deliberately not the conveyor/cable's
axis-locked drag: a pole has no facing to preview differently on the diagonal, so the next one is
placed the moment the cursor's straight-line (Chebyshev) distance from the last one reaches the
network's own connection range, at the point exactly that far along the line toward the cursor - free
to run in any of the eight directions or anywhere between, not snapped to one. A drag that covers
several range-lengths in one frame steps through all of them, each new pole becoming the anchor the
next step measures from. Releasing always drops one final pole at the cursor's cell if it has not
already landed exactly on one, closing the chain even short of a full range rather than leaving a gap
the player has to click separately. Each pole placed this way is still its own chantier, never merged
into one the way a dragged conveyor run is (`CONSTRUCTION.md` §9) - `IsDraggableRun` deliberately
excludes `PoleDefinition`, since a pole is a discrete piece placed repeatedly by the gesture, not a
line of segments belonging to each other.

**Rendering is a pure function of the network, rebuilt only on a topology change.**
`PoleNetworkVisualSync` (`Game.Presentation`) draws each cable as a handful of short rotated sprite
segments sampling a quadratic Bezier between the two poles' attachment points
(`PoleNetworkSettings.CableAttachmentHeightCells` above the footprint, near the top of the pole's
art rather than the ground it stands on), its control point offset downward by
`SagPerCellDistance` times the span in cells - a short cable is nearly straight, a long one visibly
sags. Segment positions are only recomputed when a cable is added or removed; a fed/unfed colour
change is a plain tint applied every frame, never a rebuild - no per-frame allocation either way. A
small indicator dot at each pole's own attachment point carries the same colour, so an isolated pole
with no cable at all still reads as fed or not. Drawn in `SortingBands.PoleCable`, the Information
band - above every building regardless of depth, the same "overlay, not a thing standing in the
world" reasoning a placement preview already gets.

**`PoleRangeView` (`Game.Presentation`) is the square power field itself, `PowerRangeCells` wide -
a Chebyshev square, not a circle like the Core's own ring (`ActionRadiusView`).** It shows two ways:
clicking a built pole (`BuildingSelectionInput`, a second click on the same pole dismisses it) and
following the ghost every frame while a Pole is the armed tool (`ConstructionInputAdapter`) - never
both at once, since an armed tool already routes every click away from building inspection. A Pole
has no info panel of its own, so this deliberately does not go through `SelectionRuntime.SelectedBuilding`
(reserved for a building with a real panel to clear it) - the view owns its own show/hide state and
never blocks other world input the way an open panel does. Every building the field touches gets its
own four-bar halo (the same frame `BuildingHoverHighlightView` draws, pooled here since several can
be touched at once) - one per distinct `BuildingRuntime`, however many of the field's cells it
occupies.
