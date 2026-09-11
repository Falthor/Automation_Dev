# Map

Authoritative subsystem document for the map as a whole: how it is divided, how it is discovered, how
the undiscovered part is drawn, and how sectors — the internal unit content and identity hang off —
derive theirs.

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
(`Assets/Data/Terrain/DefaultTerrain.asset`, currently **10 000**; the C# default of 60 is never what
runs). The target is 10 000, and the systems below are sized so that reaching it changes settings
rather than code.

Everything region-shaped aligns on **one** division, from `SectorSettings`
(`Assets/Data/World/SectorSettings.asset`):

| | default | what it divides |
|---|---|---|
| chunk | 64 cells | discovery storage, and every future per-region rule (terrain generation, map image, ore-density block) |
| sector | 16 cells | derived content; the unit materialisation writes in — 4×4 tile a chunk exactly |

**Sectors must tile chunks exactly.** Two divisions that disagree about where their boundaries are
would misalign permanently; that is the whole reason for choosing one. `SectorSettings` warns on
import, and `SectorSettingsTests` fails the suite — the warning is a convenience, the test is the
guard, because Unity does not run `OnValidate` while a value is being typed.

**The map size is deliberately not duplicated into `SectorSettings`.** `SectorGrid` is handed the map
size and the sector size separately. Neither `SectorGrid` nor `SectorCatalog` has a default for its
sizes: a default would be a second copy of a setting, so a caller that forgets one fails to compile.

## 2. Discovery

`DiscoveryRuntime` (`Game.Grid`) holds one `DiscoveryState` per cell and is authoritative.

**Three states, and only two of them are stored.**

| | | |
|---|---|---|
| **Unknown** | never seen | total black |
| **Remembered** | seen before, outside every observation radius right now | veiled |
| **Observed** | inside some observer's radius | full, and live |

**Observation is computed, never stored** — `ObservationRuntime` (`Game.Grid`) holds a list of
observer discs rebuilt from scratch every frame and nothing per cell at all. Nothing is written when a
robot advances and nothing erased when it moves away: the cell simply stops being covered by anything
in the list. A stored observation state would be a second source of truth, free to contradict where
the observers actually are — and it would need clearing, which is the bug that shape of code always
produces.

It follows that **nothing of it enters the save**, and there is deliberately no `Capture`/`Restore`
pair to forget to call: the first frame after a load rebuilds the field from the observers the load put
back. `DiscoveryRuntime.GetState` therefore answers with the two stored values only, and
`ObservationRuntime.StateOf` is what answers with all three.

**Discovery gates observation, in that order.** A cell nobody has discovered stays Unknown with an
observer standing on it — which cannot arise in the running game, since everything that observes also
reveals, but it is what makes "never discovered never becomes remembered" true by construction rather
than by everything happening to be called in the right order. The rule is applied twice on purpose:
in `StateOf`, and again when the fog's texels are packed, so the shader is never handed the
contradiction.

**What the third state is for is the static against the living.** Terrain, vegetation and deposits stay
drawn out of observation, because they do not move and showing them is still accurate. A nest or a unit
would be showing information already out of date, so it freezes on its last known state or goes —
nothing of that kind exists yet, so today the distinction is carried entirely by the veil.

**Who observes** is `GameRuntime.RebuildObservers`, and adding a third kind is one line there and
nothing anywhere else: the Core at its live action radius, and every explorer robot that is out at the
radius it uncovers with (§2.1). A robot's observation radius is *derived* from its reveal radius — what
it sees is what it uncovers, and two numbers would drift apart.

**Per cell, not per region.** A revelation of any shape writes the cells it covers. The reverse would
not hold: a per-region state would forbid every free-form revelation.

**The radius writes, it does not define.** The Core's action radius is one writer among others — a
wandering explorer robot is the other (§2.1) — and a discovered cell stays discovered whatever the
radius later does.
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

### 2.1 Free exploration

`ExplorerRobotSystem` (`Game.Gameplay.Exploration`), owned by `GameRuntime` and ticked from its one
central `Update`. **A prototype**, and the values on `ExplorerRobotSettings`
(`Assets/Data/World/ExplorerRobotSettings.asset`) are placed to make it run rather than balanced.

**The only thing in the game that goes anywhere.** It opens the ground, it is what turns derived
deposits into real ones, and the map screen is about it. Exploration is an action the player starts and
interrupts rather than a trip that is aimed and resolves — there is no launch, no duration and no
report — and a robot is **visible the whole time it is working**.

Three states, and no more: `Idle` at the base, `Exploring`, `Returning`. Clicking a robot in the world
opens its panel — with the same halo a selected building gets, asked for by centre rather than by cell
because a robot stands between cells and keeps moving. The one button there sends an idle one out and
turns a wandering one round.

**The fleet arrives when the CU reserve has fallen to `appearAtReserveCu`** (25 000), which is also what
opens the map screen. A fall rather than a rise: the introduction drains CU, and the thing that pays
turning up as the player runs dry is what makes it a way out rather than a reward.
`GameRuntime.startWithEverythingUnlocked` bypasses it for development.

**There is no destination, and that is the design rather than a gap.** A destination plus straight-line
travel uncovers a radius: three sorties would draw three spokes out of the Core and the map would fill
in as a star. So a robot carries a *heading* that changes continuously, and three things bend it, in
this order:

| | |
|---|---|
| a **drift** | a value noise sampled over a phase that advances with time, never a fresh draw per frame — independent draws average to nothing over a second and leave the robot shivering along a straight line. This is what makes the trace serpentine |
| a **pull towards the unknown** | two probes off the current heading (±45°, 30 cells out), each reading a robot-sized patch; the robot leans towards whichever side has less behind it, scaled by the difference rather than its sign. Enough to follow the edge of what it has opened instead of crossing back over it, with nothing that resembles an objective |
| a **recall** | past `maxRadiusCells` (**330**, the same figure the expedition zones use for their outer edge) the heading bends inwards, ramped over the next 40 cells. Not a wall and not a stop — it turns |

**A turn rate is a curve radius, read against the speed**: at `v` cells per second and `w` degrees per
second the robot turns on a circle of radius `v / (w · π/180)`. At the shipped 2 and 6 that is a
19-cell arc, which reads as a wide meander; at 30°/s it would be 3.8 cells, which reads as a robot
spinning on the spot. That is why the drift is small, and it is the first thing to know before moving
any of the three.

- **The pull reads off-map as discovered.** There is nothing out there to find, so the world's edge
  repels exactly like ground already walked. Read as unknown it would draw every robot at the border.
- **Departure bearings step by the golden ratio**, not by a draw: consecutive sorties leave about 137.5°
  apart, so two of them never uncover the same ground. A fair draw is perfectly free to put two five
  degrees apart, which is the one thing this prototype is watched for.
- **It uncovers on both legs.** A disc of `revealRadiusCells` every cell of travel — overlapping
  heavily at a radius of 6, so the trail is a band rather than a row of beads.

  The return used to write nothing, on the reasoning that it crosses ground already walked. That was
  wrong, and visibly so: **the outward leg meanders while the return is a straight line**, so the
  return cuts across the gaps between the meanders and the robot was seen travelling through pure
  black. Ground under a robot is ground it can see, and both legs now go through one call site.
- **It also *observes* as it goes** (§2), at the same radius, so a moving robot drags a disc of live
  ground behind the veil and leaves it veiled again as it passes. An idle robot at the base is skipped,
  its disc being inside the Core's anyway.
- **It is what makes derived deposits real.** A sector's contents only become deposits when something
  reports on it, and the robots are the only caller: when the robot crosses into a new sector it
  materialises the 3×3 block around it — the block, because a 12-cell reveal disc straddles up to four
  16-cell sectors and materialising only the one underneath would leave ore missing from ground the
  robot plainly uncovered. See `MATERIALISATION.md` and §4.

  **A materialised deposit goes in through `WorldGenerator.AddDeposit`, never straight into the
  grid.** That call is what puts it in `WorldGenerator.OreDeposits` and raises `DepositAppeared` —
  and those two are what the view and the save read. A deposit written only into `Game.Grid` is real
  to everything that asks the grid (the hover glow lights up, an Extractor can be placed on it) and
  invisible to everything else: undrawn, and gone on the next load. `GridRuntime.PlaceDeposit`
  returns the runtime it creates for exactly this reason, and dropping that return value is the whole
  defect.

**Persistence:** `SaveData.ExplorerRobots` — position, heading, state, plus where the drift had got to
and how many sorties have been made, so a reloaded robot carries on the bend it was in the middle of
rather than snapping onto a fresh one. No destination, because there is none to have.

Measured on the shipped values: over a 240 s sortie the path strays well off the line between its own
two ends (so it is not a ruler), and over 120 s it ends more than half its path length from the base
(so it is not circling). `ExplorerRobotSystemTests` holds both, and prints the figures.

## 3. Drawing the fog

`FogOfWarView` + `Custom/FogOfWar` draw one quad over a **window that follows the camera**, sampling a
one-texel-per-cell **RG16** texture in `FilterMode.Bilinear`: **R is what has ever been discovered, G
is what is observed right now**.

**Two channels of one texture rather than two textures.** The pair costs exactly the bytes two R8
textures would and buys three things they would not: one upload instead of two, one sample instead of
two, and a window the two fields cannot disagree about — they are read against the same origin at the
same instant because they are the same fetch.

**They change on different clocks, and the upload follows each.** Discovery moves rarely; observation
moves whenever an observer does, so on every frame a robot is walking. A discovery change repacks both
channels, an observation change repacks only G — which keeps the per-frame cost to a handful of squared
distances per texel instead of 65 000 chunk lookups. A still frame with a still fleet costs two integer
comparisons.

**The veil's strength is the whole visible difference between the second and third state**
(`rememberedVeil`, shipped at **0.55** of full fog). At 1 remembered ground reads as unknown and the map
has two visible states instead of three; at 0 there is no veil and it has two the other way round. It
is a screen judgement, not a derived figure.

The shader carries two signed distances — one per channel, cut by the same world-space grain so the two
boundaries share one irregularity — and takes the **stronger** of the two alphas rather than their sum:
the unknown is already fully opaque, and adding a veil to it would only flatten the very difference the
third state exists to draw. `clip` now needs both terms spent, so observed ground still costs nothing
while remembered ground pays a blend.

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

A sector is the internal unit a derived set of contents hangs off, and the unit
`SectorMaterialisation` writes in. **Nothing is aimed at one, no screen names one, and the player
never points at one.** Called a *sector*, never a *zone*: "zone" is already the Core's and the AI
agents' signal zones, which are a different thing.

**A regular tiling, computed and never stored.** `SectorGrid` turns a coordinate into an index,
origin, centre and cells by arithmetic — nothing is walked, there is no list of sectors anywhere. The
same choice terrain makes: derive rather than materialise, so that nothing has to be generated and no
order can matter.

**Contents are derived, never materialised.** `SectorCatalog` computes what a sector holds as a
pure function of the world seed and the sector index, when asked. The seed is the terrain's
(`TerrainRuntime.Seed`) — the only one a save restores — so the same sector answers the same thing in
a loaded game, and nothing about a sector enters the save.

The mixing goes through `Game.Core.DeterministicHash`, shared with the terrain and explicitly written
out: `System.Random` and `string.GetHashCode` are barred from anything derived, because neither is
guaranteed stable across runtime versions and a change would move every unmaterialised deposit in
every existing world. Frozen by tests with hard-coded features, centres and deposit cells.

**One sector in `SectorSettings.oreClusterOneSectorIn` (12) holds an ore cluster. The rest hold
nothing.** Measured on the shipped seed: 27 of 361 sectors on a 300-cell map, one in 13.4.

**A cluster is one contiguous patch, not a handful of scattered cells.** It is *grown* rather than
stamped — a cell already in the patch is picked, a direction is drawn, and the neighbour joins if it
is free and still inside the sector — so no two look alike and every cell touches another. The seed
cell can be anywhere in the square, corners included, which is what makes coming back over the same
neighbourhood at a different angle worth something. The growth respects the sector's real extent, so
edge sectors are clipped where the map does not divide evenly.

**Its size grows with distance from the Core** (`OreClusterProfile`): six to ten tiles just outside
the Core's furthest reach, ten to fifteen at the limit a robot wanders to (330), interpolated
between and clamped at both ends. Distance is the only thing exploring costs, so it has to be the
thing that pays — a flat size makes the far half of the map the near half with a longer walk.
Measured on the shipped map: 8.0 tiles on average inside 100 cells, 12.3 past 260.

**Nothing derived lands inside the Core's furthest reach** (`GameRuntime.FurthestActionRadiusCells`,
80 today) - the highest radius any research grants, derived from the research effects rather than
written down. The ore in it is placed by hand, at chosen distances,
because the introduction depends on it; growing the radius gives the player room, not new ore. A sector is skipped when *any
part of it* falls inside the radius rather than having its cells clipped — a clipped cluster would be
two tiles against a wall, which is worse than none.

`SectorMaterialisation` (§2.1) is what reads all of it.

**A sector's only other property is its geometry**, and that is arithmetic: `SectorGrid` answers
which square a cell falls in, where that square starts, where its middle is, and which cells it
holds. It knows nothing about discovery — a robot reveals a disc wherever it happens to be, and the
partition has no part in it.

**How far the world extends is one figure**, and it belongs to the robots:
`ExplorerRobotSettings.maxRadiusCells` (**330**), which is where a wandering robot is turned back
(§2.1) and what the map draws as its outer ring (§5).

## 4a. Wrecks

**Eight places in the disc around the Core, derived and never stored.** `WreckField`
(`Game.Gameplay.Wrecks`) computes them once at construction from `TerrainRuntime.Seed` — the seed a
save restores — so a loaded world finds them where it left them. Each is three cells across and
draws one of three sprites, freely: the same wreck may appear more than once, which is what keeps
eight of them from reading as a catalogue.

**Rings, because a density cannot answer both questions.** Uniform over 330 cells, the figure that
puts a wreck in the first few minutes puts a hundred on the map, and the figure that makes eight rare
puts the first one three quarters of an hour in. The rings decouple the two:

| Ring | Wrecks | One per |
|---|---|---|
| 40 → 75 | 2 | ~6 300 cells |
| 75 → 160 | 2 | ~31 000 cells |
| 160 → 330 | 4 | ~65 000 cells |

The innermost is deliberately tight: a robot always starts there, so it crosses one almost at once.
Ring bounds and counts are settings (`WorldGenerationSettings.wreckRings`).

**The structure separates them, so nothing checks.** A wreck's angle is its rank's share of the
circle plus a jitter bounded to `WreckRingProfile.AngularJitterFraction` (a third) of that share; its
radius is drawn between its ring's bounds. Rings separate radially, the bound separates angularly.
**The minimum angular separation is derived, not a setting** — the share less twice the jitter, so 60°
for a ring of two and 30° for a ring of four — because exposing it as well would allow three numbers
that contradict each other. There is no proximity test, no register of what is already placed and no
rejection loop: a loop would do the same job worse and would make the result depend on the order
things were drawn in.

**Found by revealing the ground it stands on**, on the same beat and against the same radius as the
reveal (`ExplorerRobotSystem.RevealAround`). One rule rather than a separate proximity range, which
could otherwise let a robot walk over an undiscovered wreck or spot one through the fog. Eight
distance checks, no allocation, and nothing at all once they are all found.

**A found wreck stays drawn outside observation**, like a deposit and for the same reason: it is its
own object rather than a cell, and it does not change. It also appears on the zoomed-out map (§5), as
a fixed-size square — never a disc, which is a robot.

**Only the discovered set is stored** (`SaveData.WrecksDiscovered`, comma-separated indices).
Position and type are pure functions of the seed. Restore is tolerant: an absent value is a world
nobody has found anything in, and an index the current rings no longer produce is ignored rather than
throwing.

## 5. The zoomed-out map's terrain

`SectorMapImage` (`Game.Presentation`) draws the revealed ground the map screen is built on: **one tile
per discovered chunk, one texel per cell, and nothing anywhere else.**

**The chunk is the unit because it is the unit everywhere else.** Discovery storage already creates a
chunk on first write and reads an absent one as unknown (§2); this does the same with pixels. A tile is
64×64 cells — 16 KB — so the introduction's four chunks around the Core cost 64 KB, and fifty chunks of
a well-explored run cost 800 KB.

**The chunk is the unit rather than the sector, and the arithmetic is not why.** One texture for the
whole world would be 400 MB per cell against 1.5 MB per sector, so the sector wins on memory — and
loses on everything else: a sector is 16 cells and a revelation is a disc of radius 6, so the
revelation is smaller than the texel it would be painted into and a whole 16-cell square takes one flat
colour. The map read as a grid of blocks. Tiling by chunk keeps the memory bounded *and* gives the
revelation an edge.

**Unknown is not a colour.** A cell nobody has seen is transparent and nothing is drawn there, so the
black is the absence of a map rather than a shape painted on one — which is what gives the revealed
patches their weight. See [`../carnets/expeditions.md`](../carnets/expeditions.md).

**No sector grid is drawn over it.** Nobody counts squares on a strategic map, and the blocks the lines
used to explain are gone with the per-sector image.

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

**The ore is drawn per discovered cell, and discovery is the question - not materialisation.** A
deposit exists from the moment its sector materialises, and §2.1 materialises the whole 3x3 block of
sectors around a robot on purpose: a 12-cell reveal disc straddles four 16-cell sectors, and
materialising only the one underneath would leave ore missing from ground the robot plainly uncovered.
So a materialised deposit is routinely 30 cells from anything anybody has seen. `RenderDeposits`
therefore asks `DiscoveryRuntime.IsDiscovered` of every deposit cell, and a cluster half opened reads
as half a patch, which is the truth about it. Without that gate the map showed the ore of the whole
materialised world - deposits sitting in black, outside the explored corridor and beyond the robots'
own range - and it was read, reasonably, as the robots finding ore they could not reach.

The marks are rebuilt when either fact moves: a new deposit, or new ground (`DiscoveryRuntime.Version`).
Guarding on the deposit count alone made the first build of the map the last one, because the count is
exactly what does not change as a robot walks.

**The robots are drawn on it, and they are the reason to open it.** One mark each — amber when out,
muted at the base, ringed when it is the one whose panel is open, which is the map's half of the halo
the world draws on the selected robot. Fixed in pixels rather than in cells: a robot is a thing to find
on this map, not a thing with a size on the ground, and one that shrank with the zoom would vanish at
exactly the scale where the player is looking for it. A parked robot is muted rather than hidden — "the
fleet is home" is an answer too.

**The outer ring is drawn at every scale**, dim, at `ExplorerRobotSettings.maxRadiusCells`. It states
where the reachable ground ends rather than offering anything, and zoomed in it simply leaves the
screen, which is how a boundary that is far away should behave.

**Nothing here is pointed at.** No hover, no selection, no target — dragging pans and the wheel zooms,
and that is the whole of the pointer's job. There is no click behaviour left to tell a drag apart from,
so there is no slop and no click/drag arbitration either. The side pane and the breadcrumb are gone with
the designation they served; one line under the map says how many robots are out and what they carry,
because that is what decides whether to go and find one.

## 6. What is not built yet

- ~~Lazy terrain generation.~~ **Done, and differently than planned.** Terrain is no longer
  materialised at all: `GetTerrainType` computes its answer from the seed and the coordinate, so there
  is nothing to generate lazily. See `TERRAIN.md` §1.
- ~~The map screen.~~ **Built, then cut back to what it is for** — `SectorMapElement` and
  `SectorMapPanelController` (`Game.UI`): revealed terrain, the base, the Core and its radius, the outer
  ring and the robots. It designates nothing (§5).
- ~~Sector content materialisation.~~ **Done**, and driven by the robots (§2.1). See
  `MATERIALISATION.md`.
- **Nests and units.** Nothing exists yet, which is why the third discovery state (§2) is carried
  entirely by the veil today: terrain, vegetation and deposits are the only things drawn out of
  observation, and all three are static.
- **A secondary Core.** [`../design/expansion-territoriale.md`](../design/expansion-territoriale.md)
  holds the design; none of it is implemented, and no figure in the project reserves room for it — the
  only reach the game measures is the robots' own 330 cells.
- **What the datacard prototype still owes**: the threshold is a guess, and
  `ExplorerHarvestLog` exists to measure it. See the notebook.
