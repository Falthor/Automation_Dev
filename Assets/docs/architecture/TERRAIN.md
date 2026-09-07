# Terrain

Authoritative subsystem document for terrain: the gameplay-side terrain data owned by `Game.Grid`, the presentation-side ground rendering owned by `Game.Presentation` (`TerrainView`, `GroundTextureProfile`, `ShadedGroundTiled.shader`, `CloudShadowOverlay.shader`), and the wild decor scattered on top of it.

It does **not** cover how the map is divided, discovered or hidden — chunks, sectors, discovery state and fog of war are [`MAP.md`](MAP.md).

## Related documents

- [`MAP.md`](MAP.md) — the map's division, discovery state, fog of war and sectors. `TerrainRuntime.Seed` is what its sector identities derive from.
- [`DEVELOPMENT_RULES.md`](DEVELOPMENT_RULES.md) — determinism rule (§ "Deterministic generators must produce identical results for identical seed and parameters when determinism is part of the contract").
- [`PROJECT_ARCHITECTURE.md`](PROJECT_ARCHITECTURE.md) — §7 Grid (terrain gameplay data ownership), §10 Presentation (the `GroundTextureProfile` preset pattern). This document expands both with implementation detail; where the two disagree, `PROJECT_ARCHITECTURE.md` wins per the source-of-truth order in `CLAUDE.md`.

---

## 1. Gameplay-authoritative terrain (`Game.Grid`)

`TerrainRuntime` (`Assets/Scripts/Grid/TerrainRuntime.cs`) is the sole source of truth for per-cell terrain type. It is a **pure function of the seed and the coordinate**, computed on demand from `TerrainGenerationSettings` (`Game.Data`: `size`, `seed`, `terrainScale`, `proportion`):

- A 3-octave Perlin fBm (weights 0.6/0.3/0.1 at frequencies ×1/×2.1/×4.3, via `SampleContinuous`) is sampled per cell; a cell is `TerrainType.Top` if the value is below `proportion`, otherwise `TerrainType.Base`. Out-of-bounds cells read as `Base`.
- **Nothing is stored.** There is no per-cell array: `GetTerrainType` computes its answer each time. A world of any size therefore costs nothing to hold or to construct, which is what lets the map grow. The cost moved from memory to three Perlin samples per query; nothing queries it per frame today, and a cache added later should be filled *from* this function so purity survives the optimisation.
- Because nothing is stored, the order cells are asked about cannot matter — the determinism the save relies on is structural rather than a discipline to keep. `TerrainRuntimeTests` pins it, including across chunk boundaries, so that a future cache cannot introduce a seam unnoticed.
- `SampleContinuous` is exposed publicly so Presentation could rebuild a higher-resolution mask derived from the exact same function, if a gameplay-driven visual ever needs one — it is not currently consumed by any renderer.
- `GetTerrainType` currently has no gameplay consumers (only its own EditMode tests) and does not influence rendering. Its existence is reserved for later gameplay rules (e.g. terrain-dependent placement or movement).

**The seed offsets come from `Game.Core.DeterministicHash`, never from `System.Random`.** `System.Random` has no guarantee of stability across runtime versions, and terrain is re-derived at every load while the buildings standing on it are saved: a Unity upgrade changing its sequence would recompose every existing world underneath bases the player had built. The rule generalises — **nothing derived may use `System.Random` or `string.GetHashCode`**; both are fine only for what is thrown away or saved. `DeterministicHashTests` and `TerrainRuntimeTests` pin the arithmetic with hard-coded expected values, which is the only kind of test that can catch a runtime changing its mind.

**Terrain does not enter the save.** It is re-derived at load from the four numbers the save carries (`TerrainSeed`, `TerrainSize`, `TerrainScale`, `TerrainProportion`), which is why those must be captured from the **running world** rather than from the settings asset — editing the asset between two sessions would otherwise regenerate a different world underneath buildings already placed.

`Game.Grid` owns this data (`PROJECT_ARCHITECTURE.md` §7); do not access `TerrainRuntime` internals from outside approved contracts, and do not let a Tilemap or any visual stand in as the source of truth for terrain type.

## 2. Ground rendering (`Game.Presentation`)

`TerrainView` (`Assets/Scripts/Presentation/TerrainView.cs`) renders the map as flat sprite layers driven entirely by shader parameters — there is no per-cell mesh or tile grid on the presentation side. `Initialize(TerrainRuntime, GridRuntime)` creates:

- **Ground** (`SortingBands.TerrainBase`): a single sprite scaled to the full map, material `Custom/ShadedGroundTiled`. All texture/biome/relief parameters below are pushed to this material once, at initialization.
- **Clouds** (`SortingBands.TerrainTop`, optional): an animated shadow overlay, material `Custom/CloudShadowOverlay`. Unrelated to the biome system; purely a moving tint on top.

Ground rendering reads no gameplay state beyond `TerrainRuntime.Size` and `GridRuntime.CellSize/CellToWorld` (for scale/origin) — **it does not read `TerrainType` at all**. There is no per-cell brightness/type modulation; all visual variety described below is independent, presentation-only noise.

### 2.1 `GroundTextureProfile`

A ScriptableObject preset (`Terrain/Ground Texture Profile` asset menu) holding every tunable for the look below, so the active look can be swapped by reassigning one asset (`TerrainView.textureProfile`) instead of editing code. It is presentation-only: no corresponding Runtime type, per `PROJECT_ARCHITECTURE.md` §10.

The live asset in `Bootstrap.unity` is `Assets/Data/Terrain/GroundProfile_Yughues.asset`.

### 2.2 Biome blend: two independent noise layers

The ground texture comes from two independent single-octave `ValueNoise2D` fields (computed in-shader, not `Mathf.PerlinNoise`), each split into textures by a threshold/weight rule, blended with a plain `smoothstep` + `lerp` — no dithering, no Voronoi/cellular diagram, no multi-octave fBm stacking for the blend itself:

- **Base layer** — `baseTextures[]` (dominant, tile the whole map) + `baseWeights[]` (relative pick weight; missing/zero falls back to equal split). One noise field (`biomeCellSize` = feature size in world units, small — a handful of units so several alternations are visible in one normal camera view) is split into weighted bands via `PickBand`; the two nearest bands blend smoothly across `biomeEdgeSoftness` (width in field-value units, not world units).
- **Accent layer** — `accentTextures[]` (sparse, optional) + `accentWeights[]`, overlaid on top of the base result wherever an independent second noise field (`accentCellSize`, normally smaller than `biomeCellSize` so accents stay small and scattered) crosses a threshold. The accent layer's total map-area share is **randomized per seed** within `[accentShareMin, accentShareMax]` — the base layer fills the remaining share. A separate seed offset keeps the accent field's shape from correlating with the base field's boundaries.
- **Seed** — `seed` (int) and `randomizeSeedEachRun` (bool). When enabled (default), `TerrainView.Initialize` draws a fresh small-magnitude random seed every game start (see §2.4 for why "small"); disable and set a fixed `seed` to reproduce one exact layout, e.g. for testing. The seed drives both noise fields and the accent-share randomization together, so a fixed seed reproduces the entire layout exactly.
- **Texture count** — up to `GroundTextureProfile.MaxBiomeTextures` (currently **3**) per layer; see §2.5 for why this cap exists and how to raise it safely.

**Design constraint (read before changing scale/blend parameters):** feature sizes must stay small relative to the camera's normal view (roughly 10–40 world units for this game's zoom) and blending must stay a plain smooth lerp, not dithering. Both a cellular/Voronoi-style diagram and a wide/large-scale blend read as geometric or as an artificial "halo," respectively, at this game's top-down zoom — a small-scale single-octave field with a smooth lerp reads as natural soft grain instead. If patches ever look too large/blocky or transitions too abrupt, adjust `biomeCellSize`/`accentCellSize` (scale) and `biomeEdgeSoftness`/`accentEdgeSoftness` (blend width) before considering a different blend mechanism.

### 2.3 Relief lighting

Each texture in either layer may carry an optional normal map (`baseNormals[]` / `accentNormals[]`, same length/order as the matching texture array; a missing entry falls back to a flat unbumped normal — never a plain white texture, which is not a valid encoded normal). The two candidate normal samples are unpacked, blended the same way as the diffuse colors (unpacked-and-renormalized, not raw packed bytes), and lit by a **single fixed light direction** — not a real `Light2D`, no dynamic lighting or shadow casting:

- `TerrainView.reliefLightDirection` (2D direction) + `reliefLightHeight` (the light's implied Z component — lower is more grazing/dramatic, higher is flatter/softer).
- `reliefLightIntensity` (0–2) and `reliefAmbient` (0–1, the shadow floor — brightness where `N·L` is zero) control contrast.

This is purely cosmetic bump/relief; it has no gameplay meaning and does not react to time of day, weather, or any runtime event.

### 2.4 Shader implementation notes

`Assets/Art/Shaders/ShadedGroundTiled.shader` (CGPROGRAM, `Fallback "Sprites/Default"`):

- **Hash and value noise**: `Hash21` and `ValueNoise2D` live in `Assets/Art/Shaders/ValueNoise.hlsl`, included here — they are shared with the materialisation and fog-of-war shaders, which had each grown a verbatim copy. `Hash21` is Dave Hoskins' "hash without sine," not a hand-rolled one. A cheaper hand-rolled hash tried during development showed clear periodic banding artifacts (a regular ladder/grid pattern) at some frequency/position combinations instead of true randomness — prefer a well-tested hash over inventing a new one. Only the primitive is shared: each caller composes its own octaves, so tuning one effect's grain never moves another's.
- **Seed magnitude**: `TerrainView` deliberately draws a *small* random seed (`Random.Range(0, 10000)`), not a full `int` range. The shader's hash multiplies the seed by ~100–450 inside a `frac()`, and float32 only has ~7 significant digits; a huge seed swamps the noise field's position-dependent bits entirely, collapsing it to a near-constant value (visually: the whole map renders as one texture, no variation).
- **Sampler budget**: every texture slot (`_BiomeTex`, `_AccentTex`, `_BiomeNormal`, `_AccentNormal`, each ×`MaxBiomeTextures`) is a separate `sampler2D`, declared statically regardless of how many are actually assigned. The common `ps_4_0` shader profile caps combined texture samplers at 16; exceeding it does **not** reliably surface as an Editor compile error — it can compile fine in the Editor preview and then render solid magenta at Play Mode runtime with no console error. `#pragma target 5.0` was tried as a fix for this project's pipeline and did not resolve it. The actual fix is keeping `4 × GroundTextureProfile.MaxBiomeTextures ≤ 16`, i.e. `MaxBiomeTextures ≤ 4` (currently 3, leaving small headroom). Raise `MaxBiomeTextures` only after budgeting total samplers against this limit, and verify in Play Mode (not just Editor compile) afterward.

### 2.5 Extending the palette

To add another base or accent texture: drop the `Texture2D` (and optional normal map) into the matching array on the `GroundTextureProfile` asset and add a weight entry — no code or shader change needed, up to `MaxBiomeTextures` per layer. Both feature sizes and the noise system itself are expressed in world units / independent of map size, so growing the map (`TerrainGenerationSettings.size`) does not require recalibrating any biome parameter — the same feature sizes simply tile across more area.

## 3. Cloud shadow overlay

`Custom/CloudShadowOverlay` (pre-existing, unrelated to the biome work above): an animated moving-noise shadow tint, configured via `TerrainView`'s `showCloudShadows`/`cloudScale`/`cloudSpeed`/`cloudCoverage`/`cloudSoftness`/`cloudShadowOpacity`/`cloudShadowColor`. Purely decorative, no gameplay coupling.

## 4. Wild decor

Rocks, bushes, flowers and dead wood scattered on the ground. No gameplay effect: decor blocks nothing, is never an occupant, and a building placed over it simply clears it.

**Derived per chunk, like the terrain and for the same reason.** `DecorRuntime` (`Assets/Scripts/Grid/DecorRuntime.cs`) answers what one chunk holds as a pure function of the world seed and the chunk's coordinates, via `DeterministicHash` — never `System.Random`. Nothing is placed step by step and there is no sequential state to advance, so a chunk answers the same thing whether it is asked first, last, or twice. Density is expressed **per chunk** (`DecorSettings.spotsPerChunk`), never as a total for the map: a total cannot follow a change of map size.

**Decor grows in clumps, from derived anchors.** A spot is one item for a solitary kind (trees, large rocks) and a whole clump for a clumping one (thickets, flower beds, rock outcrops) — `DecorClustering` carries the chance, the size range and the radius per kind. Items placed one per draw give an *even* scatter, and an even scatter reads as regularity exactly as a grid does; clumping is what breaks it. The anchor is itself hashed from the chunk and the spot index, so a clump is a pure function like everything else — there is no sequential "pick an anchor, walk outward" pass, which a chunked derivation could not host.

Deriving a chunk therefore also derives the anchors of its **eight neighbours**, keeping only the members that land inside. This is not the forbidden kind of neighbour access: what is forbidden is consulting a neighbour's *state* — an answer that depends on whether it has been asked yet, or on what it decided to keep. Here the neighbour's anchors are re-derived from the same pure function, so the answer still depends on nothing but the seed and the coordinates. Without it, every clump straddling a boundary would be cut along the chunk line, drawing the chunk grid on the ground in vegetation; `DecorRuntimeTests.ClumpsAreNotCutAlongChunkLines` is what holds that. One ring suffices because a clump radius is clamped to the chunk size, and an anchor that cannot reach the chunk being derived is rejected before its biome sample — which is what keeps the extra ring from costing nine times the work.

**A window, not a world.** `DecorVisualSync` (`Game.Presentation`) instantiates only the chunks covering the widest possible view plus `windowMarginCells`, and pools the objects it takes away. The chunk grid is the hysteresis — panning inside one chunk costs a comparison. Raised kinds register with `DepthSortLadder` on entering the window and unregister on leaving; flat kinds take `SortingBands.FlatVegetation` and never touch the ladder.

**Two filters, and the difference between them is the whole design:**

- **What the player cleared** is stored, in `SaveData.DecorRemoved` (`CONTRACTS.md` §14). The derivation knows nothing of what happened on the ground, so without this a rock cleared to make room for a building grows back the moment the camera leaves and returns. `ConstructionService.CreateAndRegister` is the single chokepoint that records it — every building passes through it, whether placed, restored from a save, or materialised by a robot, and it clears **before** taking the cell.
- **What is taken by something the seed already knows** — today an ore deposit — is filtered live through `DecorRuntime.GroundIsTaken` and never stored. Recording a deposit's footprint would write hundreds of cells into every save to say something the seed can answer, and would leave the ground bare once the deposit was mined out.

Nothing is recorded where nothing grows: a footprint is cells, decor is roughly one item per hundred cells, so an unfiltered sweep would put about a hundred useless entries in the save for every rock actually cleared. `DecorRuntime.GrowsAt` memoises the chunk derivations it needs for this — measured, a 200-building base sweeping its own footprint at load costs 3 ms rather than 231.

The CPU classification of which ground band a spot sits on is `BiomeField` — a float32 port of the ground shader's own base-layer maths, deliberately matching the shader's precision rather than exceeding it (see §2 and the class summary). `DecorSettings.bandEdgeExclusion` grows nothing within a rounding of a band boundary, which is where the port and the GPU can disagree.
