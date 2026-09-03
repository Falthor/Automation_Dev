# Terrain

Authoritative subsystem document for terrain: the gameplay-side terrain data owned by `Game.Grid`, and the presentation-side ground rendering owned by `Game.Presentation` (`TerrainView`, `GroundTextureProfile`, `ShadedGroundTiled.shader`, `CloudShadowOverlay.shader`).

## Related documents

- [`DEVELOPMENT_RULES.md`](DEVELOPMENT_RULES.md) — determinism rule (§ "Deterministic generators must produce identical results for identical seed and parameters when determinism is part of the contract").
- [`PROJECT_ARCHITECTURE.md`](PROJECT_ARCHITECTURE.md) — §7 Grid (terrain gameplay data ownership), §10 Presentation (the `GroundTextureProfile` preset pattern). This document expands both with implementation detail; where the two disagree, `PROJECT_ARCHITECTURE.md` wins per the source-of-truth order in `CLAUDE.md`.

---

## 1. Gameplay-authoritative terrain (`Game.Grid`)

`TerrainRuntime` (`Assets/Scripts/Grid/TerrainRuntime.cs`) is the sole source of truth for per-cell terrain type. It is generated once, deterministically, from `TerrainGenerationSettings` (`Game.Data`: `size`, `seed`, `terrainScale`, `proportion`):

- A 3-octave Perlin fBm (weights 0.6/0.3/0.1 at frequencies ×1/×2.1/×4.3, via `SampleContinuous`) is sampled per cell; a cell is `TerrainType.Top` if the value is below `proportion`, otherwise `TerrainType.Base`. Out-of-bounds cells read as `Base`.
- `SampleContinuous` is exposed publicly so Presentation could rebuild a higher-resolution mask derived from the exact same function, if a gameplay-driven visual ever needs one — it is not currently consumed by any renderer.
- `GetTerrainType` currently has no gameplay consumers (only its own EditMode tests) and does not influence rendering. Its existence is reserved for later gameplay rules (e.g. terrain-dependent placement or movement).

`Game.Grid` owns this data (`PROJECT_ARCHITECTURE.md` §7); do not access `TerrainRuntime` internals from outside approved contracts, and do not let a Tilemap or any visual stand in as the source of truth for terrain type.

## 2. Ground rendering (`Game.Presentation`)

`TerrainView` (`Assets/Scripts/Presentation/TerrainView.cs`) renders the map as flat sprite layers driven entirely by shader parameters — there is no per-cell mesh or tile grid on the presentation side. `Initialize(TerrainRuntime, GridRuntime)` creates:

- **Ground** (sorting order 0): a single sprite scaled to the full map, material `Custom/ShadedGroundTiled`. All texture/biome/relief parameters below are pushed to this material once, at initialization.
- **Clouds** (sorting order 1, optional): an animated shadow overlay, material `Custom/CloudShadowOverlay`. Unrelated to the biome system; purely a moving tint on top.

Ground rendering reads no gameplay state beyond `TerrainRuntime.Size` and `GridRuntime.CellSize/CellToWorld` (for scale/origin) — **it does not read `TerrainType` at all**. There is no per-cell brightness/type modulation; all visual variety described below is independent, presentation-only noise.

### 2.1 `GroundTextureProfile`

A ScriptableObject preset (`Terrain/Ground Texture Profile` asset menu) holding every tunable for the look below, so the active look can be swapped by reassigning one asset (`TerrainView.textureProfile`) instead of editing code. It is presentation-only: no corresponding Runtime type, per `PROJECT_ARCHITECTURE.md` §10.

The live asset in `Bootstrap.unity` is `Assets/Data/Terrain/GroundProfile_Yughues.asset`.

### 2.2 Biome blend: two independent noise layers

The ground texture comes from two independent single-octave `ValueNoise` fields (hand-rolled in-shader, not `Mathf.PerlinNoise`), each split into textures by a threshold/weight rule, blended with a plain `smoothstep` + `lerp` — no dithering, no Voronoi/cellular diagram, no multi-octave fBm stacking for the blend itself:

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

- **Hash function**: `Hash21` is Dave Hoskins' "hash without sine," not a hand-rolled one. A cheaper hand-rolled hash tried during development showed clear periodic banding artifacts (a regular ladder/grid pattern) at some frequency/position combinations instead of true randomness — prefer a well-tested hash over inventing a new one.
- **Seed magnitude**: `TerrainView` deliberately draws a *small* random seed (`Random.Range(0, 10000)`), not a full `int` range. The shader's hash multiplies the seed by ~100–450 inside a `frac()`, and float32 only has ~7 significant digits; a huge seed swamps the noise field's position-dependent bits entirely, collapsing it to a near-constant value (visually: the whole map renders as one texture, no variation).
- **Sampler budget**: every texture slot (`_BiomeTex`, `_AccentTex`, `_BiomeNormal`, `_AccentNormal`, each ×`MaxBiomeTextures`) is a separate `sampler2D`, declared statically regardless of how many are actually assigned. The common `ps_4_0` shader profile caps combined texture samplers at 16; exceeding it does **not** reliably surface as an Editor compile error — it can compile fine in the Editor preview and then render solid magenta at Play Mode runtime with no console error. `#pragma target 5.0` was tried as a fix for this project's pipeline and did not resolve it. The actual fix is keeping `4 × GroundTextureProfile.MaxBiomeTextures ≤ 16`, i.e. `MaxBiomeTextures ≤ 4` (currently 3, leaving small headroom). Raise `MaxBiomeTextures` only after budgeting total samplers against this limit, and verify in Play Mode (not just Editor compile) afterward.

### 2.5 Extending the palette

To add another base or accent texture: drop the `Texture2D` (and optional normal map) into the matching array on the `GroundTextureProfile` asset and add a weight entry — no code or shader change needed, up to `MaxBiomeTextures` per layer. Both feature sizes and the noise system itself are expressed in world units / independent of map size, so growing the map (`TerrainGenerationSettings.size`) does not require recalibrating any biome parameter — the same feature sizes simply tile across more area.

## 3. Cloud shadow overlay

`Custom/CloudShadowOverlay` (pre-existing, unrelated to the biome work above): an animated moving-noise shadow tint, configured via `TerrainView`'s `showCloudShadows`/`cloudScale`/`cloudSpeed`/`cloudCoverage`/`cloudSoftness`/`cloudShadowOpacity`/`cloudShadowColor`. Purely decorative, no gameplay coupling.
