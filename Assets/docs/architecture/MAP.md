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

**The choice locks the rest.** All six are available until one is chosen; afterwards only that one is.
`MissionSystem.CanLaunch` applies it, ahead of every kind's own rules, so a recovery is as refused
outside the zone as a reconnaissance — `LaunchRefusal.NoZoneChosen` and `OutsideChosenZone`. **Nothing
launches before a zone is chosen.**

**Content is derived and deterministic**, like a sector's: pure functions of the world seed and the
zone index through `Game.Core.DeterministicHash`, never `System.Random`. Three prospections, two far
reconnaissances, 2–3 recoveries, 1–2 civilisation studies, and a finite hidden stock of 3–4. A site's
identity is derived; only what the player did to it (revealed, consumed) enters the save.

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
  repeated on demand. `RevealNextHiddenSite` hands over one and returns null once it is spent.
  **Nothing calls it yet**: the field study is the mission kind meant to, and it does not exist — see
  §6.

**Cartography is measured in surface, never in sites.** A field study will *add* sites, so a bar over
a site count would go backwards the moment the player found something. Over a fixed set of cells with
a discovery that only ever adds, monotonic is a property of the construction rather than one to police.
`CartographyOf` walks the bounding box of the outer disc once for all zones — so the six can never
disagree about the partition — and caches against `DiscoveryRuntime.Version`, which already exists to
answer "has anything actually changed". It allocates nothing.

## 6. What is not built yet

- ~~Lazy terrain generation.~~ **Done, and differently than planned.** Terrain is no longer
  materialised at all: `GetTerrainType` computes its answer from the seed and the coordinate, so
  there is nothing to generate lazily. See `TERRAIN.md` §1.
- **The zoomed-out map**, its hover and its risk display — the interface over §4, which the directive
  places last. **The zone-choice screen comes before it**: until one exists, nothing launches at all
  except through `ExpeditionZoneSystem.Choose` from a script (§5).
- **The field study** — the fourth mission kind, and the only one with no predefined site.
  `MissionKind` has three members; this is not one of them. Three things wait on it: the hidden stock
  has no consumer, so `RevealNextHiddenSite` is never called in play; 100 % cartography is unreachable,
  since every other mission aims at a finite site; and the starting zone holds six or seven launchable
  quests instead of the nine the design's own arithmetic is built on. **Any measurement of the
  introduction has to know this before it starts**, or it will read a short run as a balance problem.
- **A third explorer robot.** `MissionSettings.explorerRobotCount` is 2 and nothing ever raises it, so
  the dormant nest's first condition — the third robot acquired — cannot be met.
- **A starting zone distinct from the other five.** Every zone derives the same content (§5). The
  design wants the first one composed differently; whether it should be, and whether "first" means a
  particular slice or whichever the player picks, is still open — see
  [`../design/directive-zones-et-missions-intro.md`](../design/directive-zones-et-missions-intro.md) §9.
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
