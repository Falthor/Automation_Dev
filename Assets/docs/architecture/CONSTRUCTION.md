# Construction

Placing, moving and demolishing: what a click costs, what it opens, and how the material actually gets
there.

Implemented by `ConstructionService` (`Game.Construction`, a plain C# class with no Unity or input
dependency) and `ConstructionSiteSystem` (`Game.Gameplay.Sites`), both owned by `GameRuntime`.

```csharp
public void SelectBuilding(BuildingDefinition definition)
public void Cancel()
public void SetPreviewRotation(Direction rotation)
public bool CanPlace(GridCoord cell)
public PlacementRefusalReason GetPlacementRefusalReason(GridCoord cell)
public bool TryPlace(GridCoord cell, Direction rotation, out ConstructionSiteRuntime site,
    ConstructionSiteRuntime conveyorRunSite = null)
public bool TryCancelPendingAt(GridCoord cell)
public bool TryRedirectExistingConveyor(GridCoord cell, Direction rotation, out ConveyorRuntime redirected)
public bool TryRotateInPlace(GridCoord cell, out BuildingRuntime rotated)
public void BeginRelocation(BuildingRuntime building)
public bool TryRelocate(GridCoord destination, out BuildingRuntime moved)
public bool TryDemolish(GridCoord cell, out BuildingRuntime removed)

public bool CanAfford(BuildingDefinition definition)
public int GetAvailableAmount(string itemId)

public int BuildingCap { get; }
public int OccupiedBuildingSlots { get; }
public void RestoreBuildingCap(int? cap)
```

## 1. Placing opens a chantier, it does not build

`TryPlace` pays for nothing and produces no working building: it opens a **construction site**. The
`BuildingRuntime` is instantiated and occupies its grid cells immediately - so nothing else can be
placed on top of it and a conveyor drag can keep reshaping its anchor - but it carries
`IsUnderConstruction`, is deliberately not registered with the transport system, and has no view. It
neither ticks, transports nor produces until robots have delivered its full cost.

**Owning ground and being operational are two different states**, and the flag is what keeps them
apart everywhere. Not registering a segment stops it ticking, but transport resolves its *neighbours*
through the grid, where a chantier does sit: the transport system therefore reads the grid through one
predicate that returns nothing for an unbuilt segment, so nothing is handed to it, nothing is drained
from it, and a Splitter does not count its cell as a usable exit.

**A segment becomes operational when it has assembled, not when its last item landed.** Delivery fills
the bill; the segment then physically assembles at `SegmentAssembly.RateFor(footprint)`, and only on
reaching 1 does it clear `IsUnderConstruction`, register with transport and raise
`SegmentMaterialized`. That clock lives in Gameplay for exactly this reason. Before the two rules were
separated, an unbuilt powerplant collected coal through its whole construction, then supplied current
for the five further seconds it spent visibly materialising.

Passing `conveyorRunSite` appends the cell to an existing site instead of opening a new one: a whole
conveyor or splitter drag is **one** chantier, not one per segment.

`TryPlace` and `TryDemolish` only mutate grid and runtime state and return the affected
`BuildingRuntime`; they never create or destroy GameObjects. The caller - a Presentation-layer input
adapter - owns the corresponding view, which is what keeps `Game.Construction` free of a dependency on
Presentation.

## 2. The gates, and naming the refusal

`CanPlace` is the non-mutating bool used for ghost tinting. `GetPlacementRefusalReason` is the
explanatory read behind it: the same checks in the same order, returning
`None`/`NotUnlocked`/`OutOfActionRadius`/`CannotAfford`/`BuildingCapReached`/`CellOccupied`. It is
meaningful only while something is selected.

Only the refusals a player cannot see for themselves are announced on screen - the cap and the missing
resources. Out of radius, not unlocked and occupied are already legible from the ghost's tint and from
where the cursor is; a message on each of those would be noise on gestures the player is making
deliberately.

`CannotAfford` reads the aggregate **minus what other sites have already reserved**, so placing four
buildings with stock for three refuses the fourth rather than letting four sites fight over one stock
afterwards. Placing still does not pay - it opens a site that reserves the whole bill - but the bill
must be coverable at that instant, which is what makes a placed site's missing count zero in ordinary
play.

`CanAfford` is read twice over: by the Building menu for its affordable/unaffordable styling, and by
the placement gate itself, so the greyed-out card and the refused click can never disagree.

Drag-gesture decoding - turning a mouse drag into a sequence of single-cell calls, and detecting when
to reshape an anchor into a corner - is input interpretation and lives in the Presentation-layer
adapter. The service stays single-cell and input-agnostic.

The Network Cable drags the same way but simpler: it is a plain `BuildingRuntime` with no shape to
reconfigure, so a turn - the same Ctrl "drop axis" gesture, or a fresh click onto an existing cable's
own endpoint - does not reshape anything in place. It removes the plain cable segment at the pivot cell
(`ConstructionInputAdapter.DemolishAt`, which already handles both a still-pending segment and a
materialised one) and places the omnidirectional Network Junction there instead. Whether a movement
counts as a turn is read off the existing segment's own `FacingRotation` rather than a flow-based entry
lookup, since an undirected cable has no "feeds/fed by" to ask.

## 3. Overtaking

The occupancy check lets a Conveyor, Splitter or Crossroad be placed onto belts already laid instead of
forcing a demolition first. `TryPlace` settles what each overtaken cell owes **after** the placement is
known valid, never before, so a refused placement costs the player nothing:

- a belt **still pending** was paid for by nobody, so it simply leaves its chantier - *only it*, its
  cost off the bill and the earmarks it alone justified released. The rest of a dragged run keeps its
  own ground; cancelling the whole chantier there deleted a run the player was still laying. A site
  left with no segment at all is closed exactly like a cancelled one.
- a belt **already built** is being demolished, so its cost becomes a repatriation job like any other
  demolition. Dropping it where it stood destroyed the material silently, which is the one thing
  demolition is careful never to do.

## 4. What the player already owns is re-aimed, never re-bought

`TryRedirectExistingConveyor` is what the input layer reaches for first, and it is not a placement at
all: dragging a belt across a belt already built re-points that belt in place. Nothing is spent, no
site is opened, nothing is demolished, and the items riding it keep riding it. A pending belt is never
redirected: it is somebody's chantier, and turning it would build something nobody asked for.

`TryRotateInPlace` is the same reasoning one step more general - a placed building turns a quarter
turn, keeping its recipe, its progress and its contents. Refused for the Core and its chest, for
anything still under construction, and for a non-square footprint, which would land on different cells.

`BeginRelocation`/`TryRelocate` move a building the player already owns: the same ghost, the same
gates, the same rotation preview, and the next click puts it down. **The same instance moves** -
nothing is copied, so a chest's contents were never anywhere but inside the object that changed
address. It costs nothing and does not pass through the building cap: moving one adds none.

**Every gate applies to all three except affordability**, which they have no business reading: they
spend nothing, so an empty chest is no reason to refuse turning or moving something already paid for.

## 5. Demolition, cancellation, and what may never be removed

`TryDemolish` removes the building immediately - the player wants the space back - but refunds nothing
anywhere: the cost becomes a repatriation job a robot must physically haul back.

`TryCancelPendingAt` is the counterpart for something still pending, and is what the demolition input
routes to when the clicked cell belongs to a chantier rather than a finished building. It is scoped to
the **one segment** under the cursor: that segment's cost comes off the bill, the earmarks it alone
justified go back, and its ground is freed, while every sibling of a dragged run keeps its own cell and
its own share.

`TryDemolish` refuses for the Core, for the Core chest fixture - matched by definition id, since a
restored instance is just an ordinary entry in the save's building list - and for any building that is
still a pending site's unbuilt segment. The first two are world-generated fixtures the player never
placed; the chest especially, since its contents would otherwise be lost for good. Only the
construction *cost* is ever repatriated, never a building's own held contents.

## 6. The building cap

`BuildingCap` is runtime state owned by this service, not by any definition: it starts at
`DefaultBuildingCap` and is raised by research effects - the highest target completed wins, so one
landing late never lowers it - and is restored directly from a save rather than re-derived.

`OccupiedBuildingSlots` counts every registered building except the Core, the Core chest fixture and
the transport family, **plus** every pending construction site of a slot-consuming type. A site takes
its slot from the moment it is placed, otherwise the cap could be walked straight past by queueing
sites faster than robots can serve them. It is computed live from the registered buildings, never a
separately tracked counter, so placing and demolishing can never drift out of sync with it.

## 7. The Core's reach

`CoreDefinition.ActionRadiusCells` is only the starting value. The live radius is runtime state on
`CoreRuntime.ActionRadiusCells`, which the placement gate reads and which the Core extends itself on
research completion - the highest target completed wins, exactly like the cap.

The highest target any research carries is the Core's furthest reach,
`GameRuntime.FurthestActionRadiusCells`, derived from the research effects and never written down a
second time.

## 8. Reaching beyond the Core: Communication Relays

**"In radius" composes.** Once `ResearchEffectKind.UnlockOutOfRadiusConstruction` is completed
(`ConstructionService.HasUnlockedOutOfRadiusConstruction`, a flag that never goes back like
`HasDataCenter`), a cell counts as in range if it is within the Core's radius **or** within any
currently-active `CommunicationRelayRuntime`'s own radius - each checked whole-footprint-at-once
against its own single circle, never a per-cell mix of two sources. Outside every one of them, only
what a relay needs to reach and be reached may still be placed: a straight/corner conveyor, a pole, the
network cable and its junction, and the Communication Relay itself (`ConstructionService.
IsEligibleOutsideRadius`) - and only on ground that is **already discovered**. Discovery is otherwise
never consulted by placement anywhere else in the game; this is the one exception, and it exists only
out here.

**A Communication Relay projects its own radius while active, and only then.** "Active"
(`CommunicationRelayRuntime.IsActive`) is all-or-nothing like every other power consumer
(ÉNERGIE.md): unpowered, or powered but starved of its continuous CU upkeep, and the radius simply
stops existing that tick - `ConstructionService.CommunicationRelays` still lists the relay (demolition
is the only thing that removes an entry), but the placement gate skips it until it is active again. This
is the one building in the game whose CU is spent **continuously** rather than in one shot per cycle -
see CALCUL.md's own exception for why.

A conveyor run outside every radius has no length limit of its own: what pulls a base outward and
forces the player to reach for a relay is ore placement itself, not a limit on the belt carrying it
there - MAP.md.

**A relay reveals its own disc the moment it comes into being, whatever its power state.** Discovery
is a one-way fact about ground the player just committed to, not a thing that comes and goes with
`IsActive` the way the buildable zone does - `ConstructionService.CreateOccupant` calls
`DiscoveryRuntime.RevealDisc` right where the `CommunicationRelayRuntime` is created, the same single
factory a live placement and a restored save both go through, so a reload never has to redo it
(`Reveal` is a no-op past the first call, and `SaveData.Discovered` already carries it). The same
mechanism the Core's own radius uses to write into discovery (`GameRuntime.RevealDiscoveredByCore`),
just triggered once at creation instead of re-checked every tick, since a relay's own radius never
grows the way the Core's does with research.

**Discovered is not the same as watched, and a relay needs both to show no fog at all.** Discovery
(above) is permanent; `GameRuntime.Observation` is live, rebuilt every frame from scratch
(`RebuildObservers`), and is what `FogOfWarView` actually reads to tell "remembered" ground (veiled)
from "currently observed" ground (clear) - MAP.md's own two-channel texture. Every currently-active
relay is one of `RebuildObservers`' observer sources, at its own action radius, exactly like the Core;
skipped the moment `IsActive` goes false, so an unpowered relay's ground stays discovered (it can
never be forgotten) but drops back to the veil rather than reading as watched. A relay that has never
been active yet still shows the veil inside its own radius, not the full fog of the unknown - the
one-time `RevealDisc` at creation already lit it as discovered.

## 9. Chantiers: reservation, segments, order

Placing opens a `ConstructionSiteRuntime` holding one segment - a normal building - or several in
placement order, a whole conveyor or splitter drag. Segments materialize strictly in order, each the
moment its own cost has been delivered, so a dragged belt line grows from its anchor as the robots
supply it. Until then a segment is inert.

**Reservation is localized.** A reservation is a `(container, itemId, amount)` triple held by one site,
never a bare total - two sites can otherwise both promise themselves the same physical stack, and the
second one blocks with nothing to explain it. Every tick, every open site, **oldest first**, tries to
reserve what it still needs; an older site always wins a newly produced unit over a younger one.
Reserved items stay physically in their container - still visible, still counted in that container's
own contents - and only leave when a robot actually loads them.

`SegmentProgress(index)` is how far along one segment is, `0` to `1`, and the **only** form in which
per-segment advancement leaves Gameplay: the rule deciding which delivery feeds which segment stays
here rather than being re-derived by whoever draws it. Since segments materialize strictly in order
and consume the delivered pile in that same order, it answers `1` before the front, `0` after it, and a
real ratio only for the segment currently being built.

`GetSupply(list)` gives the bill in the three states a player asks about - **delivered**, **reserved**,
**missing** - one line per ingredient, in an order fixed by the bill rather than by a dictionary, so a
panel rebuilt every frame cannot reshuffle its rows.

`Reserved` covers both halves of a promise: earmarked in a container and not yet collected, and already
riding in a robot's cargo. Both are the same statement about **stock** - this material is spoken for.
It is the only thing that separates a site whose material is secured from one forgotten because nothing
produces what it needs; on a delivered count alone the two read identically until one silently never
finishes.

**Reserved says nothing about movement**, and must not be presented as if it did. A reservation holds
whether or not a robot has been dispatched, so several sites placed at once are all fully reserved
while only one is being served. Which site a robot is walking toward is a separate question with a
separate answer, and it belongs beside the bill, never inside it.

**A site is never opened short.** Placement is gated on affordability read against unreserved stock, so
opening a site reserves its whole bill on the spot and `Missing` is zero on every queued chantier. A
shortage is therefore a refused placement, never a stranded site. `GetStillNeeded` and `IsStalled` stay
as defensive reads for a source destroyed while holding reserved material, not as states ordinary play
produces.

## 10. GlobalStock is a view, not a container

**The name deliberately stays the same and the contract is inverted.** It used to hold the starting
stock and every demolition refund. It now holds **nothing at all**: it is a read-only aggregate,
recomputed on every read, over exactly three sources in this fixed order:

```text
Core chest (the core_storage fixture)
   → every placed Storage
      → every production building's OUTPUT
```

minus everything already reserved - by a construction site, or by an in-flight Core delivery (§11). Its
invariant: **what GlobalStock reports is exactly what a builder robot could still be sent to fetch.** Items riding a conveyor or already in a
robot's cargo are never counted - they are no longer claimable. A production building's *input* is not
part of it either, a deliberate narrowing: the aggregate and the robots' collection order must be the
same list, and a robot does not raid work-in-progress ingredients out of a machine.

`GameRuntime.GlobalStock` is that aggregate, and the Storage panel's aggregate view is a straight read
of it rather than a second summation.

## 11. The robots

Two `BuilderRobotRuntime` (`SpeedCellsPerSecond`, free diagonal movement, no pathfinding), driven only
by this system's tick - never by their own `Update()`. The view reads `Position` and converts it to
world space, nothing more.

They always serve the **oldest site that currently has something reserved and not yet delivered**: a
site blocked on a material nobody has is skipped rather than blocking the queue, and reclaims the
robots as soon as it can be served again. "One chantier at a time" is about simultaneous execution -
both robots serve the same one - not about strict queue order. Each robot claims its share of a site's
reservations before leaving, so two robots never fetch the same promised piece twice.

**A free robot's task order is fixed: a construction site's earmark first, an in-flight Core delivery
second, a repatriation job last.** A directive never takes a robot from a building already waiting on
its material, and a demolished building's cargo only claims whichever robot the first two have nothing
left to hand.

**Cargo is uncapped for construction and capped at `DirectiveCargoCapacity` for a Core directive.** The
two are different kinds of job: a building waiting on its materials should not take five waves to
receive a bill one robot could carry, while a directive is a hand-over the player chose to take on and
how many waves it asks for is part of what it asks. Repatriation follows construction, being the same
bill read backwards.

**One source container per round trip, and everything it holds for that job.** A trip is multi-item: with
the materials in one chest - which is where the player's are - a whole building's bill arrives in one
trip. Spread across two chests it is two trips. There are no multi-stop tours, which is the decision
that rule comes from; a consequence worth knowing is that a long conveyor drag is one site, so a funded
run completes from a single delivery rather than a wave per five units.

A dispatched robot's claim moves out of the site's reservations and into its own `PendingAmount`, which
counts against a container's reserved total for the whole outbound trip - otherwise the units sit
claimed by nobody between dispatch and pickup, and one stack gets promised twice. The counterpart is
that **a site leaving the queue must release the robots working for it**: cargo already picked up is
dropped off like a repatriation, and a robot merely on its way to fetch drops its claim. A claim left
standing is unreachable stock for the rest of the game - no reservation pass can see past it, so no
site is served, so no robot is ever reassigned to clear it.

## 12. Demolition's overflow, and the loss it accepts

The building disappears immediately; its construction cost becomes a repatriation job a robot carries
back - Core chest first, then any Storage with room for the whole cargo. If no container anywhere can
take it, the robot keeps the cargo, a notification names the cause and the countdown, and after
`BlockedDestructionSeconds` **the cargo is destroyed**.

This loss is a deliberate, documented simplification, punitive and silent by design: it is the
anti-deadlock that keeps a permanently loaded robot from making construction impossible, and the reason
there are two robots - one can still build a Storage while the other is stuck. It is a decision, not an
oversight: the day it should change, the alternatives are dropping the cargo on the ground or refusing
the demolition outright.

**Notifications.** `NotificationSystem` (`Game.Gameplay.Notifications`) is a generic queue - severity,
message, display duration, optional countdown - read by a left-edge banner. A blocked robot and a
chantier missing materials are its first two callers, not its purpose; it never blocks interaction and
no gameplay decision ever reads it.

## 13. What travels in the save

- `Buildings` - every placed building's envelope (definition id, cell, rotation, input side) plus its
  own type-specific blob.
- `CoreDefinitionId`, `CoreCellX`, `CoreCellY`, `CoreState` - the Core, whose blob round-trips its
  current action radius alongside its timer and contents; an absent radius falls back to the
  definition's starting value, never to 0.
- `BuildingCap` - nullable, so an absent value is distinguishable from an explicit one and falls back
  to `DefaultBuildingCap`.
- `ConstructionSites` - every site (segments, delivered totals, reservations) and both robots
  (position, state, cargo) plus any repatriation in flight. It restores **last**, after every real
  building is back in the grid and registered, because a site's segments are rebuilt with the same
  restore factory and its reservations are re-resolved by container cell. An absent key restores as two
  idle robots with no site.

There is no `GlobalStock` field: the aggregate holds nothing, so there is nothing to serialize - it is
recomputed from the real containers at load.
