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
- `Assets/docs/directive-grande-carte.md` — the directive this subsystem is being scaled towards. Design intent, not a description of current code.
- `Assets/docs/brouillard-et-zonage.md` — the implementation notebook: decisions taken, deviations and why. Reasoning lives there; current state lives here.

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

- **Names are unique by construction**, through an injective index-to-vocabulary mapping rather than a
  draw that would need a registry to check. A test fails when the vocabulary no longer covers the
  sector count, which is what will happen when the map grows.
- **Risk is measured in cells from the Core**, against thresholds that are exposed balance settings on
  `SectorSettings` — not in sectors crossed, which would tie a property of the world to a division of
  it and move the whole gradient whenever the division changed. The defaults read as the geometry of
  expansion: the starting territory, the mining ring, as far as a secondary Core reaches, beyond.
- **Contents** place the point of interest at the sector's centre, so a mission always shows what it
  found, and scatter deposits anywhere in the square — including the hidden corners, which is what
  makes exploring around a revealed disc worth doing. The scatter respects the sector's real extent:
  edge sectors are clipped where the map does not divide evenly.

**Mission reach** (`SectorMissionRange`) is given the Core's current radius on every call and
remembers none of it, so extending the radius moves the ring with no other number to correct. It
reports whether it had to widen and whether the map is exhausted, so an empty result is never
indistinguishable from a fault.

## 5. What is not built yet

- ~~Lazy terrain generation.~~ **Done, and differently than planned.** Terrain is no longer
  materialised at all: `GetTerrainType` computes its answer from the seed and the coordinate, so
  there is nothing to generate lazily. See `TERRAIN.md` §1.
- **The zoomed-out map**, its hover and its risk display — the interface over §4, which the directive
  places last.
- **Missions themselves**, which everything above is the prerequisite for.
- **Two mission ranges.** `SectorMissionRange` still implements one ring; the directive replaces it
  with an exploration range and a mining range, both derived.
- **Sector content materialisation.** Contents are derived and tested, but nothing turns them into
  real deposits. Doing so needs a decision first: `WorldGenerator` already places clusters around the
  Core, and the sectors near it are discovered on the first frame, so the two would overlap.
