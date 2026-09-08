# Map

Authoritative subsystem document for the map as a whole: how it is divided, how it is discovered, how
the undiscovered part is drawn, and how sectors — the unit a mission is aimed at — derive their
identity.

It does **not** cover what a cell's terrain *is* or how the ground *looks*: that is
[`TERRAIN.md`](TERRAIN.md). The boundary is simple — `TERRAIN.md` owns `TerrainRuntime` and the ground
shader, this document owns everything that divides, reveals or hides the map.

## Related documents

- [`PROJECT_ARCHITECTURE.md`](PROJECT_ARCHITECTURE.md) — §7 Grid (who owns per-cell world state), §10.1 Draw order (the fog's band, and the camera-relative depth ladder that solves the same scaling problem for a different system). Where the two disagree, `PROJECT_ARCHITECTURE.md` wins per the source-of-truth order in `CLAUDE.md`.
- [`CONTRACTS.md`](CONTRACTS.md) — §14 Save/Restore (`SaveData.Discovered`, and why sectors are not saved).
- [`TERRAIN.md`](TERRAIN.md) — terrain type and ground rendering.
- [`../design/expansion-territoriale.md`](../design/expansion-territoriale.md) — secondary Cores, mining zones, deposit density and the generation parameters. **Design intent, none of it implemented**; this document describes what is.
- [`../carnets/brouillard-et-zonage.md`](../carnets/brouillard-et-zonage.md) — the implementation notebook: decisions taken, deviations and why. Reasoning lives there; current state lives here.

---

## 1. Size and division

The map is square and its size lives on `TerrainGenerationSettings.size`
(`Assets/Data/Terrain/DefaultTerrain.asset`, currently **300**; the C# default of 60 is never what
runs). The target is 10 000, and the systems below are sized so that reaching it changes settings
rather than code.

Everything region-shaped aligns on **one** division, from `SectorSettings`
(`Assets/Data/World/SectorSettings.asset`):

| | default | what it divides |
|---|---|---|
| chunk | 64 cells | discovery storage, and every future per-region rule (terrain generation, map image, ore-density block) |
| sector | 16 cells | the unit a mission is aimed at — 4×4 tile a chunk exactly |

**Sectors must tile chunks exactly.** Two divisions that disagree about where their boundaries are
would misalign permanently; that is the whole reason for choosing one. `SectorSettings` warns on
import, and `SectorSettingsTests` fails the suite — the warning is a convenience, the test is the
guard, because Unity does not run `OnValidate` while a value is being typed.

**The map size is deliberately not duplicated into `SectorSettings`.** `SectorGrid` is handed the map
size and the sector size separately. Neither `SectorGrid` nor `SectorCatalog` has a default for its
sizes: a default would be a second copy of a setting, so a caller that forgets one fails to compile.

## 2. Discovery

`DiscoveryRuntime` (`Game.Grid`) holds one `DiscoveryState` per cell and is authoritative.

**Per cell, not per region.** A revelation of any shape writes the cells it covers. The reverse would
not hold: a per-region state would forbid every free-form revelation.

**The radius writes, it does not define.** The Core's action radius is one writer among others — a
mission is another — and a discovered cell stays discovered whatever the radius later does.
`DiscoveryRuntime` knows nothing about the Core: it takes a centre and a radius, not a building, so no
read path can recompute a distance and collapse the fog back into a disc. `GameRuntime` is the writer,
after `Research.Tick`, so a widened radius is written the frame it is granted.

**Stored per chunk, created on first write.** A chunk nobody has revealed a cell in does not exist,
and an absent chunk reads as **unknown** — never as discovered, which would reveal the map wholesale.
The cost follows what the player has explored rather than the size of the world: the same exploration
costs the same on a 300-cell map and on a 10 000-cell one. This is storage only — no caller can tell,
and the captured save string is unchanged.

**`Version` advances only when a call actually changed something.** The fog renderer compares it
against what it last uploaded, which is the whole of "re-upload only when the state changed".

**Persistence:** `SaveData.Discovered`, run-length encoded — see `CONTRACTS.md` §14.

## 3. Drawing the fog

`FogOfWarView` + `Custom/FogOfWar` draw one quad over a **window that follows the camera**, sampling a
one-texel-per-cell R8 texture in `FilterMode.Bilinear`.

**The window's size has nothing to do with the map's.** One texel per cell over a whole world would be
16 MB at 4 000 cells and past most GPUs' limit beyond 8 192. The zoom-out cap bounds how much world
can be on screen at once, so the texture covers a window a little larger than the widest possible view
— 256 cells by default, 64 KB, whatever the map's size. It re-anchors only when the camera nears its
edge, so panning costs one comparison per frame rather than an upload.

**Outside the window reads as undiscovered.** Clamping to the border texel was right while the texture
covered the whole map — its edge was always unknown — but a moving window has discovered texels on its
border, and clamping would smear them outwards. The shader forces `discovered = 0` outside the UV
range instead.

**The fog is fully opaque, and that is a functional constraint rather than a taste.** Below alpha 1,
the camera's own background shows through wherever no terrain is drawn — measurably so past the edge
of the world, where a blue wash appeared against the brown of undiscovered ground and drew the map's
border for the player. The fog hides absence, not scenery, so its opacity is not an aesthetic dial.
The reason is repeated on the `fogColor` field itself, so that anyone softening the fog meets it
before changing the value.

Bilinear filtering is not a detail: the field is binary per cell, and the interpolation between texels
is the only thing that turns it into a boundary a threshold can cut anywhere. The shader's noise then
breaks that boundary up so it does not read as a circle. The texture is `linear: true` — an R8 read
through a gamma curve arrives at the shader as a different number than it was written, and this one is
compared against a threshold.

The fog sits above every band in the draw-order ladder (`PROJECT_ARCHITECTURE.md` §10.1).

## 4. Sectors

A sector is the unit a mission is aimed at. Called a *sector*, never a *zone*: "zone" is already the
Core's and the AI agents' signal zones, which are a different thing.

**A regular tiling, computed and never stored.** `SectorGrid` turns a coordinate into an index,
origin, centre and cells by arithmetic — nothing is walked, there is no list of sectors anywhere. The
same choice terrain makes: derive rather than materialise, so that nothing has to be generated and no
order can matter.

**A mission reveals the disc inscribed in a sector, not the sector.** The four corners stay hidden, so
two revealed neighbours leave an undiscovered fringe and the tiling never shows on screen — which is
what allows the partition to be a plain grid. A sector opened by a mission therefore rests at
`SectorDiscovery.Partial` forever; that state means "there is something here and you have not seen all
of it".

**Identity is derived, never materialised.** `SectorCatalog` computes a sector's name, risk and
contents as pure functions of the world seed and the sector index, when asked. The seed is the
terrain's (`TerrainRuntime.Seed`) — the only one a save restores — so the same sector answers the same
thing in a loaded game.

The mixing goes through `Game.Core.DeterministicHash`, shared with the terrain and explicitly written
out: `System.Random` and `string.GetHashCode` are barred from anything derived, because neither is
guaranteed stable across runtime versions and a change would rename every sector in every existing
world. Frozen by tests with hard-coded names.

- **A name is a region plus a position in it** — "Cratère de Suie H12". Sectors are not named one by
  one: 390 625 of them against 768 vocabulary combinations is 80 % collisions, and neighbours sharing
  a name makes designating a mission destination impossible. Widening the vocabulary is not an
  answer — 390 625 distinct generated names would all read alike anyway — so uniqueness moved off the
  name and onto the pair. It also reads better: a player learns one region instead of fifty unrelated
  nouns.

  **Collision is now structurally impossible rather than tested for.** The region count per axis is
  derived and capped at `SectorCatalog.MaxRegionsPerAxis` (27, since 27² = 729 fits in 768 and 28²
  does not), so no map size can produce more regions than there are names; the region name comes from
  the same injective index-to-vocabulary mapping the sectors used to use, and the suffix is the
  sector's own position inside its region. `SectorSettings.preferredRegionSizeCells` says how much
  ground should carry one name — an intent, not the answer: a map large enough to need more regions
  than the cap allows gets wider ones instead. At the shipped 10 000 that is 27 regions of 371 cells,
  24 sectors across.
- **Risk is measured in cells from the Core**, against thresholds that are exposed balance settings on
  `SectorSettings` — not in sectors crossed, which would tie a property of the world to a division of
  it and move the whole gradient whenever the division changed. The defaults read as the geometry of
  expansion: the starting territory, the mining ring, as far as a secondary Core reaches, beyond.
- **Contents** place the point of interest at the sector's centre, so a mission always shows what it
  found, and scatter deposits anywhere in the square — including the hidden corners, which is what
  makes exploring around a revealed disc worth doing. The scatter respects the sector's real extent:
  edge sectors are clipped where the map does not divide evenly.

**Mission reach** (`SectorMissionRange`) is two bands, because there are two kinds of mission and they
want opposite things.

| Mission | Band | What it is looking for |
|---|---|---|
| Mining | current Core radius → exploration threshold | new deposits |
| Exploration | exploration threshold → no outer edge | secondary Core sites, nests, points of interest |

They **partition** everything past the Core's reach: a sector belongs to exactly one, and a test walks
outward across the seam to hold that. The mining band closes from the inside as the radius grows —
ground the Core already covers needs no mission to reach — while the exploration band does not move,
since the current radius has no part in where it starts.

**The threshold is derived and never entered**: two maximum Core radii back to back, plus
`SectorSettings.territorySpacingCells`, the empty ground wanted between two territories. That is what
keeps a secondary Core's radius from ever touching the main one's. The gap is the only figure of it
that is a choice; writing the resulting distance down as a setting would make a second copy that stops
agreeing the day a Core's maximum radius moves. At the shipped ceiling of 32 cells
(`CoreRuntime.ExtendedActionRadiusCells`) and a gap of 90, the threshold is **154 cells** — the
directive's 250 is the same formula at a maximum radius of 80, which no Core reaches yet.

The Core's radius is passed in on every call and nothing here remembers it. A result reports the band
it looked in and whether that band is exhausted, so an empty answer is never indistinguishable from a
fault. Enumeration is bounded by a limit: the exploration band holds most of the map, and a caller
wanting somewhere to go wants a few destinations, not four hundred thousand — the predicate
`EligibilityOf` is the primary operation, since a mission is launched by designating one sector.

## 5. Expedition zones

An **expedition zone** is the wedge of ground a run chooses and maps. `ExpeditionZoneSystem`
(`Game.Gameplay.Expeditions`), owned by `GameRuntime`, built before `MissionSystem` because the launch
path is gated on it.

**Three divisions cohabit and the vocabulary keeps them apart.** A *sector* (§4) is the square a
mission is aimed at. A *signal zone* is the territory a Core or an AI agent holds. An *expedition
zone* is this. The bare word "zone" is still reserved for the second; the qualified name is used
everywhere in code, and there is no type, field or local called `Zone`.

**Six angular slices, and the count is the only figure entered.** `ExpeditionZoneSettings`
(`Assets/Data/World/ExpeditionZoneSettings.asset`) holds the count, the jitters (±20 cells, ±10 % of a
slice) and every site count. The slice angle is `360 / count` and is a property, never a field: an
angle entered beside a count is a second figure that stops agreeing, and six 90° slices do not cover a
circle.

**Radial bounds.**

| | |
|---|---|
| inner | the Core's action radius **at the moment the zones were laid out** |
| outer | the exploration threshold plus one maximum Core radius — **186 cells** at the shipped 154 and 32 |

The outer edge is what makes a zone finite, and therefore mappable to the end at all: an unbounded 60°
wedge runs to the corner of the world and never finishes. It is derived, read off
`SectorMissionRange.ExplorationMinimumCells` rather than recomputed — two copies of
"2 × max radius + territory gap" are two things that stop agreeing the day a Core reaches further.

**The inner edge is frozen at layout and travels in the save**, which is a deliberate reading of the
design's "current radius". Live, it would shrink the zone under the player as research widens the Core:
sites near the inner edge would fall out of their own zone, and — measurably — cartography would go
*backwards*, because dropping ground that is fully discovered from both halves of a ratio lowers it.
See [`../carnets/expeditions.md`](../carnets/expeditions.md).

**Membership is one division, not one test per zone.** `ZoneAt` folds a bearing into `[0, 360)` and
divides by the slice, so the circle is partitioned by construction — no seam can be claimed twice or by
nobody, whatever the count. `ZoneOfSector` measures on the sector's **centre**, the same rule the
mission bands use.

**The first launch is the choice, and it shuts the rest until this one is mapped.** All six are legal
targets until one is chosen; sending the first robot into one picks that direction, and afterwards only
that zone accepts a mission. `MissionSystem.CanLaunch` applies it ahead of every kind's own rules, so a
recovery is as refused outside the zone as a reconnaissance — `LaunchRefusal.OutsideChosenZone`, which
is the only refusal the rule needs. See `CONTRACTS.md` §16.

**The lock is a wait, not a forfeit.** The five reopen at `ExpeditionZoneSettings.ZoneReleaseRatio` of
the chosen zone's cartography — shipped at 1, which the introduction is not meant to reach. So the five
stay shut for its whole length while the rule stays true, rather than the screen promising a return the
code refuses.

**Choosing a direction is not going there.** A zone's content is derived the moment it is picked, but
none of it is shown: `IsSurveyed` is false until a mission *reports* from the zone, and the map draws no
site before that. That is what the first mission is for — it comes back with the whole list, not with
the corner it stood in. Per zone and saved.

**That first mission is a kind of its own: `MissionKind.Decouverte`, one per zone.** Not a prospection
under another name — it answers a different question, it is the only thing a zone offers before it has
been visited, and it is refused (`LaunchRefusal.ZoneAlreadySurveyed`) once its zone has reported.

| | |
|---|---|
| band | none. A prospection is a choice among several; this is the only one on offer |
| duration | `MissionSettings.DiscoverySeconds`, **flat** — two minutes in all six directions |
| reward | the introduction's reconnaissance budget, like every reconnaissance |
| target | the zone's entry sector, which is what the choice screen aims it at |
| what it opens | the ground around **each site it reports**, one disc per site at `SectorGrid.InscribedRadiusCells` |

**A discovery opens ground, and that was a defect before it did.** It came back with a list of sites and
dropped their marks into the dark: the zone still read as untouched, and the one mission whose purpose is
to show what is out there showed nothing. Each site is now reported with the ground it stands on —
patches, not the wedge. Opening the whole zone here would finish its cartography on the first mission,
and with it lift the lock on the other five.

**The flat duration is the point, not a shortcut.** A discovery only ever goes to an entry sector, and
the six are one wedge turned six times — so the travel term every other kind carries would add nothing
but the few seconds' spread that rounding a cell into a sector produces, and two minutes would stop
being two minutes for five of the six directions.

**A first mission is aimed at the zone's entry sector** (`EntrySectorOf`): on the zone's own bearing,
one sector past the inner edge. The six are one wedge turned six times, so all six entry sectors sit at
the same distance and a first mission costs the same whichever direction is picked — which is what lets
the choice screen say that what it shows is true and identical everywhere.

**Content is derived and deterministic**, like a sector's: pure functions of the world seed and the
zone index through `Game.Core.DeterministicHash`, never `System.Random`. Three prospections, two far
reconnaissances, 2–3 recoveries, 1–2 civilisation studies, and a finite hidden stock of 3–4. A site's
identity is derived; only what the player did to it (revealed, consumed) enters the save.

**The first zone chosen carries a composed content instead** — 1 prospection, 2 far reconnaissances,
3 recoveries, 2 civilisation studies and 4 hidden, all from `ExpeditionZoneSettings`. The other five
keep the derivation, and nothing decides yet whether they will ever be composed.

- **A rule about the data, never about the geometry.** There is no zone 0 and no "is this the starting
  slice" test: the six are equivalent while they are on offer, which the design requires, and it is the
  *choice* that composes whichever one it lands on. Same form as the placed-sector rule in §6, and
  durable for the same reason — no starting perimeter to maintain.
- **The composition is the count, not the coordinates.** A position cannot be authored for a slice
  nobody has picked yet, so placement, findings and the hidden stock's kinds stay derived from the seed
  and the zone index. Two runs choosing the same direction get the same layout.
- **Choosing drops what that zone had already derived**, which is what makes the order the two happen
  in stop mattering. A screen offering the six would otherwise have cached the derived content and the
  composition would arrive too late to be seen — the ordering hazard §6 names for placed sector content,
  in its other form. Nothing is lost: no mission can launch before a zone is chosen, so no site can
  carry state yet, and a test states that rather than leaving it assumed.
- **Nothing new enters the save.** The composition is a function of the chosen zone, which already
  travels, and of a settings asset — so a reload re-derives it. What is pinned instead is that the
  re-derivation lands on exactly the same sites, positions included.

- **A site is placed where its own kind of mission may go.** Far reconnaissances sit past the
  exploration threshold, which is the only band a far reconnaissance may be sent into; everything else
  sits short of it, where a prospection may be sent. A site its own mission cannot reach would be a
  quest nobody can accept.
- **The jitter can never throw a site out of its zone or its band**: the even ladder is laid across the
  span *minus* the jitter on both sides, so what is drawn lands back inside. Structural rather than
  clamped — a clamp would pile sites on a boundary and hide the settings having outgrown the geometry.
- **Exactly one of the two far reconnaissances carries the secondary Core site**, drawn between them, so
  some runs find it on the first and some on the second. The other carries the trace that puts the
  zone's civilisation study on the map: it must not be empty, or half the players discover that one of
  their two quests held nothing.
- **The hidden stock is finite by construction** — a fixed-length part of a derived list, not a draw
  repeated on demand. `RevealNextHiddenSite` hands over one and returns null once it is spent, after
  which a field study finds only ground.

**The field study** (`MissionKind.EtudeDeTerrain`, named for the design's *étude de terrain*) is
an ordinary site in the near stretch, aimed at exactly like a prospection and sharing its band — no
free targeting, no designation mode of its own. What separates it is what it can find: it carries a
one-in-three chance of turning up a hidden site, and it is the stock's only consumer.

**That draw is made at launch and carried by the mission** (`MissionRuntime.RevealsHiddenSite`), like
the outcome and the reward and for the same reason — a mission saved in flight has to land identically.
Whether there is still anything to find is asked at the landing, since another study may have emptied
the stock in the meantime. It pays a flat figure and spends neither of the introduction's two budgets;
what bounds it is the number of field-study sites a zone holds.

**Cartography is measured in surface, never in sites.** A field study *adds* sites, so a bar over a
site count would go backwards the moment the player found something. Over a fixed set of cells with a
discovery that only ever adds, monotonic is a property of the construction rather than one to police.
`CartographyOf` walks the bounding box of the outer disc once for all zones — so the six can never
disagree about the partition — and caches against `DiscoveryRuntime.Version`, which already exists to
answer "has anything actually changed". It allocates nothing.

## 6. The zoomed-out map's terrain

`SectorMapImage` (`Game.Presentation`) draws the revealed ground the map screen is built on: **one tile
per discovered chunk, one texel per cell, and nothing anywhere else.**

**The chunk is the unit because it is the unit everywhere else.** Discovery storage already creates a
chunk on first write and reads an absent one as unknown (§2); this does the same with pixels. A tile is
64×64 cells — 16 KB — so the introduction's four chunks around the Core cost 64 KB, and fifty chunks of
a well-explored run cost 800 KB.

**It was one texel per sector, and that was the right answer to the wrong question.** One texture for
the whole world is 400 MB per cell against 1.5 MB per sector, so the sector won on arithmetic. But a
sector is 16 cells and a mission reveals a disc of radius 8: the revelation was smaller than the texel
it was painted into, `DiscoveryOf` answered `Partial`, and the whole 16-cell square took one flat
colour. The map read as a grid of blocks. Tiling by chunk keeps the memory bounded *and* gives the
revelation an edge.

**Unknown is not a colour.** A cell nobody has seen is transparent and nothing is drawn there, so the
black is the absence of a map rather than a shape painted on one — which is what gives the revealed
patches their weight. An earlier version tinted it so a sector could be aimed at; a mission is aimed at
a site now, and sites are drawn on top. See [`../carnets/expeditions.md`](../carnets/expeditions.md).

**No sector grid is drawn over it.** Nobody counts squares on a strategic map, and the blocks the lines
used to explain are gone with the per-sector image.

**Trip trails are discovery, not a drawing.** A docking mission opens a band along the path it took, so
the terrain above draws it with everything else — there is no trail layer, nothing to store, and nothing
extra to save. `MissionSystem.RevealTrail` asks `DiscoveryRuntime.RevealDisc` in steps along the curve
rather than writing a band shape: the disc already exists, and a mission asks for a shape rather than
reimplementing one.

- **Every trail starts at the Core**, which is where a robot leaves from and returns to, and why they
  converge into a star rather than scattering.
- **The band is a third of the arrival disc's width**, derived from it. Two independent numbers would
  stop agreeing.
- **The path comes from the world seed and the target sector, and from nothing else.** Two missions to
  the same place therefore follow one road, so going back reveals almost nothing and a fresh direction
  is worth more. Its curve bows by `MissionSettings.TrailBendFractionOfDistance` — a taste value, unlike
  the width.
- **It lands at the docking, with everything else.** Nothing reaches the Core while a robot is out.

**A consequence to know: a trail spends sectors.** A reconnaissance may only be aimed at a wholly
unknown sector, and a trail crosses ground on its way. Measured on the shipped settings, one mission to
a target 144 cells out opened 909 cells and took **eight** sectors out of the 288 reachable ones. That
is the design's "returning reveals almost nothing" seen from the other side, and it is stated here
rather than discovered later.

Rebuilding is guarded on `DiscoveryRuntime.Version` and then per chunk on its own stamp, so a still
frame costs one integer comparison and a revelation repaints the one chunk it landed in. The element
holds one child per tile, reused across pans and zooms, so moving the view allocates nothing.

**The base is drawn on it, cell by cell** — blue for what transforms or holds, white for what carries
(`ConveyorRuntime`, `SplitterRuntime`, `CrossroadRuntime`). Two colours and no legend: at this distance
what a base looks like from above is its shape and its transport network, not its building types.

- **Per cell, from `BuildingDefinition.FootprintCells`**, the same list the grid, demolition and the
  action-radius check go through. One rectangle per building would claim ground the player does not
  hold — a splitter's footprint is a cross.
- **Only once a cell is worth a pixel.** At the whole-world scale a belt is a fraction of one, so
  hundreds of sub-pixel quads would cost a frame to draw a smudge over the Core's own mark, which
  already says "you are here". The base appears as the player zooms towards it.
- **Rebuilt only when the building count moves.** Expanding footprints allocates an array per building,
  and nothing can be built or demolished while the map covers the screen.

**Zone separators and the outer ring are drawn at every scale.** They were the whole-world view's alone,
which took a zone's boundary away exactly when the player zoomed in to work inside it — and everything
outside that boundary is refused. Zoomed in they simply leave the screen, which is how a boundary that
is far away should behave.

**The game puts one site forward, once.** After the first report the chosen zone's first standing
recovery is drawn as `MapSiteState.Highlighted` — a wider ring and the one label shown without hovering.
It goes out **at the launch that follows it**, whatever that launch was aimed at, and never lights again
in any zone: kept until the site is exploited it would be a rail, offered again in the next zone it would
be a tutorial that never ends. Derived from the zone's own list; only "already spent" is state, and it
travels in the save.

**The pointer finds sites, and the panel talks about nothing else.** A sector is never named, hovered or
described — not when it is unknown, and not when it is known either: naming a square the player never
pointed at and cannot act on ("Sillon de basalte J10") describes the division rather than the place. A
selected site shows its own kind, its own state and **its own mission**; the pane used to list every
mission the sector under the mark admitted, so clicking a recovery offered a prospection.

**Hovering reads a direction, clicking fixes it.** Before a zone is chosen the pane follows the pointer
across the six, which is how they are compared; a click stops that, and from then on only another click
moves it.

**Nothing on this map is square.** The sector division is how a mission is aimed, never something the
player points at: a hover, a selection and a mission's target are rings around a mark, and what the
pointer finds is a site rather than the square under it. A click aims at **the site's** sector, not at
the one under the cursor — a mark is picked from up to 16 px away, which at the zone scale is a good
fraction of a sector.

## 7. What is not built yet

- ~~Lazy terrain generation.~~ **Done, and differently than planned.** Terrain is no longer
  materialised at all: `GetTerrainType` computes its answer from the seed and the coordinate, so
  there is nothing to generate lazily. See `TERRAIN.md` §1.
- ~~The map screen itself.~~ **Built** — `SectorMapElement` and `SectorMapPanelController` (`Game.UI`):
  the three scales and their breadcrumb, the zone separators and outer ring, the sites in their four
  states, the base (§6), and a side pane that changes with the scale. Missions launch from it.

  **Before a zone is chosen the pane is one card, not a list**: the direction's picture, its
  cartography, its status, what the launch costs in the other five, and a single action — the
  discovery (§5). Listing the kinds a sector happens to admit would describe ground nobody has walked. The card stays through the flight, with the mission's own clock as its bar, and
  gives way to the sections when the report lands and the zone's sites appear (§5).

  What is still missing on it: a launch panel proper, the site hover line (name, kind, estimated risk),
  the highlighted site's behaviour (`MapSiteState.Highlighted` draws but nothing produces it), and the
  count of sites left beside the cartography figure.
- ~~Mission trip traces.~~ **Done, and there was never a layer to draw.** See §6.
- ~~The field study.~~ **Done** — `MissionKind.EtudeDeTerrain`, three sites per zone, in the mining
  band like a prospection. See §5.
- ~~A third explorer robot.~~ **Not missing — gone.** It came from the abnormal signal, which has been
  removed from the design; the dormant nest now waits on the Datacenter priming alone. Two robots is
  the intended fleet, not a shortfall.
- ~~A starting zone distinct from the other five.~~ **Done** — see §5. The five unchosen ones are not
  composed, and whether they ever should be is not decided.
- **Missions themselves** — the process exists (`MissionSystem`, `Game.Gameplay.Missions`): probes,
  the state machine, the two reconnaissances, launch-time draw surviving a save, and the introduction's
  finite reward budget. What is missing is everything with a screen — the launch panel, the mission
  counter on the top bar, the reports — plus units and the four late missions. See
  [`../carnets/expeditions.md`](../carnets/expeditions.md).
- ~~Two mission ranges.~~ **Done** — see §4. The single ring is gone.
- ~~Sector content materialisation.~~ **Done** — `SectorMaterialisation` writes a reported sector's
  derived deposits into the grid, called from `MissionSystem` immediately after the revelation, from
  the same place and in the same order.

  **A sector already carrying placed content keeps it; the derivation only fills sectors that have
  none.** A rule about the data rather than about geometry, which is what makes it durable — there is
  no starting perimeter to maintain, and it covers in advance anything else placed by hand, a scripted
  wreck or a particular nest. The **whole sector** is skipped, not just its occupied cells, so derived
  ore never grows in the gaps between hand-placed clusters. The starting area stays hand-composed
  because the introduction depends on the right resources at the right distance, which a derivation
  does not guarantee.

  **The ordering constraint holds by construction, and is asserted from both directions.**
  `WorldGenerator` places its clusters during `Awake`; nothing materialises until a mission lands. A
  test places first and derives second, then does the reverse and shows the sector no longer reads as
  empty — so the risk is named rather than assumed away.

  **A sector holds one ore, never a mixture** (`SectorContents.ResourceIndex`), which is what gives a
  destination an identity: choosing where to go becomes a decision rather than a draw.

  Materialisation is **idempotent with no bookkeeping**: an occupied cell is skipped, and deposits are
  saved, so a reloaded world finds its own deposits standing and writes nothing. A set of materialised
  sectors would be a second source of truth able to disagree with the grid.
