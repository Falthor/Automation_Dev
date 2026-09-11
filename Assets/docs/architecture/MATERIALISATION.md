# Materialisation

Authoritative subsystem document for the nano assembly effect: a placed building assembles on screen
at the pace of its deliveries, and the ground under it converts at the same pace.

It does **not** cover what a construction site is, who delivers to it, or when a building starts
working — that is `CONTRACTS.md` §15 and `PROJECT_ARCHITECTURE.md` §17. This document covers what is
drawn.

## Related documents

- [`PROJECT_ARCHITECTURE.md`](PROJECT_ARCHITECTURE.md) — §10 Presentation, §10.1 draw order (the
  bands this subsystem's layers sit in), §17 (the central tick that drives sites).
- [`CONTRACTS.md`](CONTRACTS.md) — §15, including `ConstructionSiteRuntime.SegmentProgress`, the one
  read-only accessor Presentation was granted.
- [`TERRAIN.md`](TERRAIN.md) — the ground this effect converts, and the shared noise primitive.
- [`../carnets/materialisation-nano.md`](../carnets/materialisation-nano.md) — the false trails, the
  measurements that overturned an intuition, and the traps. Not needed to use the system.

---

## 1. Where it lives

| File | Role |
|---|---|
| `Art/Shaders/BuildDissolve.shader` | Cuts the sprite under a noised reveal front, with a lit rim. |
| `Scripts/Presentation/BuildDissolveView.cs` | Drives the effect: progress smoothing, delivery flash, `MaterialPropertyBlock`, removal at completion. |
| `Scripts/Presentation/ConstructionSiteVisualSync.cs` | A segment's three states, and the hand-over to the real view. |
| `Art/Shaders/GroundCoverage.shader` | Tints converted ground and lights its rim, from the distance-to-front stored in the zone's texture. |
| `Scripts/Presentation/GroundCoverageRenderer.cs` | The conversion field: one texture, quad and material **per zone**, re-uploaded only when the field changed. |
| `Scripts/Presentation/NanoConstructionSettings.cs` | The settings class. |
| `Data/Presentation/NanoConstructionSettings.asset` | The single instance. **The only file to open to change the look.** |
| `Scenes/DissolveTest.unity` | Eye-validation scene: a camera, a Foundry at game size, the component wired. Outside Build Settings, no dependency on the rest of the game. |

## 2. The canonical pair: `ArtWorldSize` / `FootprintSize`

**`ArtWorldSize`** for everything that must **coincide with the drawing** — placement preview, site
silhouette, dissolving sprite, real view. **`FootprintSize`** for everything that **marks occupied
cells** — the concrete slab, and the ground coverage, which expresses converted cells rather than the
extent of the drawing.

The two coincide on a well-framed sprite with no overscan, which is what makes getting it wrong
invisible on simple cases and glaring on the Foundry. `BuildingSpawner.ArtWorldSize` is the single
place that answers the size question; four paths draw a building and all four go through
`BuildingSpawner.FitSpriteUniform`.

**`RenderOverscan` is a measurement of the art, not a free number.** For a building whose art is
meant to fill its footprint, it compensates the transparent margin: `1 / (opaque width ÷ frame
width)`. It is therefore a property **of the file** and expires the moment the file is replaced. A
test reads the source PNG and measures the opaque box. The Conveyor, Splitter and Crossroad have an
overscan of the opposite sense — theirs deliberately pushes their arms into the neighbouring cell to
close a seam — and the test excludes them explicitly.

**Height overhang is carried by the sprite's pivot**, never by a transform offset: the pivot sits at
the centre of the *footprint* inside a taller frame. Any path that positions the art at the centre of
the footprint then puts the base on the right cells knowing nothing about the overhang.

## 3. The two layers share one frontier

**The field stores the signed distance to the front**, not a quantity of coverage: 128 is the front,
so the shader `clip()`s without any notion of threshold and its rim is written exactly like the
building's. That is the only storage form that lets both layers share one lit frontier.

**The ground is revealed by the building's front, on the building's rectangle**, reading the same
`revealMode` — so the two cannot be tuned against each other. It is that identity, and nothing else,
that makes the two fronts one wave rather than two animations that merely start together.

**The grain is computed per fragment, never baked into the texture.** The texture carries only the
smooth threshold. See the carnet for the sampling limit that forced this.

**The concrete slab is revealed by the same field.** Coverage and slab say the same thing about the
same cells — this ground is acquired — so the site slab samples `_CoverageTex` with its zone's
`_ZoneBounds`, exactly as `Custom/GroundCoverage` does. The **final** slab must not read the field:
the field recedes for four seconds after completion, and the concrete would recede with it.

**Two textures, one encoding, one frontier.** Recomputing the threshold in the slab's shader would
put the same rule in two languages, and it would diverge at the first tuning.

## 4. Settings

All of it in `Data/Presentation/NanoConstructionSettings.asset`. Nothing is per-building, and that is
deliberate. Changes show immediately in Play: the component re-reads the asset every tick.

| Field | Effect |
|---|---|
| `noiseScale` | Grain size. Higher = finer, more numerous patches. |
| `noiseWeight` | At 0 the front is a clean sweep; higher, it breaks up. |
| `rimWidth`, `rimColor` | The lit band following the front. |
| `revealMode` | 0 = the building rises from the ground, 1 = it forms from its centre. |
| `deliveryFlashDuration`, `deliveryFlashIntensity` | The flash when matter arrives. |
| `groundIntensity` | Strength of the converted ground's tint. Deliberately discreet. |
| `groundRimColor` | Colour of the converted ground **and** its rim. |
| `groundRimIntensity` | Decoupled from `groundIntensity`: tuning one must not extinguish the other. |
| `groundLeadShare` | At what fraction of the building's progress the ground has finished converting. **Below 1 the ground runs ahead**, which is the only way to see it — the sprite covers its own footprint. |
| `groundOverflowCells` | How far conversion overflows the footprint, measured from its **corner**. **This is what stops the final patch being a square**; at 0 the frontier is the rectangle again. |
| `groundTexelsPerCell` | Field resolution. Carries only the smooth threshold, so it no longer limits grain — only the fidelity of the sweep and of the overflow frontier. |
| `groundRimWidth` | In threshold units, same unit and sense as `rimWidth`. |
| `coverageFadeSeconds` | How long the front takes to recede once the site is done. |
| `groundCoverageSortingOrder` | **3**, between terrain (0 and 1) and the concrete slab (5). |
| `sitePlaceholderAlpha`, `siteSilhouetteSortingOrder` | The blue silhouette during assembly. |

`dissolveShader` points at `Custom/BuildDissolve`, **as an asset reference, never `Shader.Find`** —
see [`../BUILD.md`](../BUILD.md) §5.

**The assembly rate is deliberately not here.** It lives in `Game.Gameplay.Sites.SegmentAssembly`,
because it decides **when a building starts working**: a segment is not registered, powered or active
until its assembly reaches 1. While it was a rendering setting, the gas plant fed the network and
burned coal during the five seconds it was still visibly materialising. `SegmentAssembly.CellsPerSecond`
= 1.8 cells per second, so a 9-cell plant takes at least 5 seconds. **The tuning reference is the gas
plant**, and any new value has to be judged on it.

**The assembly does not wait for the material.** It runs from the moment the site is placed and
reaches 1 at its own pace, whatever has been delivered - it was clamped to the delivered fraction,
which drew two facts as one and left a site waiting on its last plate sitting visibly half-built. It
is an animation: it says something is being assembled here, not how much material arrived. How much
arrived is on the site's own panel, in numbers.

Being drawn as finished is therefore not being built. `CanMaterializeNextSegment` still requires the
segment's whole cost **and** an assembly of 1, so a fully-drawn site with a plate outstanding keeps
its panel, keeps its robots coming, and goes on producing, transporting and accepting nothing.

## 5. The view pipeline

Three states, one owner. `ConstructionSiteVisualSync` draws the dissolving sprite itself and hands
over to `BuildingSpawner.SpawnView` when assembly finishes. `BuildingSpawner` gained no mode.

| State | What is seen |
|---|---|
| Waiting | Blue silhouette at full `siteTint` alpha, sprite entirely cut away |
| Assembling | Silhouette at `sitePlaceholderAlpha`, sprite materialising over it |
| Done | The real view |

**The assembling set outlives the site.** A segment leaves `ConstructionSiteSystem`'s pending range
the moment it materialises, which is one frame before the view has been told to hand over. Those views are therefore *detached*: they stay in the component, driven to target 1, and
are released at `DisplayedProgress == 1`. Their liveness criterion becomes the grid, not the site.

The hand-over **spawns the real view and destroys the assembly objects in the same call**, so no frame
shows both or neither. `ConstructionInputAdapter` lends its `BuildingSpawner` rather than making a
second one: a second spawner would have its own per-cell view dictionary, and demolition would no
longer find views created by the other.

**Loading a save** goes through `GameRuntime.Start()` on a fresh scene, so the assembling set is
empty; restored buildings get their real view directly.

## 6. Known limits

- **The patch overflows the footprint, and that is the mechanism.** A 3×3 shows on a little over 4×4,
  with irregular contours. `groundOverflowCells` at 0 is the one value not to choose.
- **Only one zone exists today**, the Core's. Zones must not overlap; two overlapping quads would add
  their coverages visually.
- **The silhouette sits at order 7, which is wrong for the Extractor.** An Extractor is placed on a
  deposit, which is drawn higher, so its waiting silhouette passes *behind* the deposit it is
  reserving — the single most useful placement cue, on the most-placed building in the game. Fixing
  it needs a full renumbering of the ladder; deliberately out of scope.
- **`GameRuntime` builds a second `BuildingSpawner`** for the restore path, with its own per-cell view
  dictionary — exactly the trap `SetViewSpawner` avoids for sites. Pre-existing.
- **Progress weights each item by its unit count.** A screw is worth a circuit board. Real, known, and
  untriaged; the fallback, when it bites, is the mean of per-ingredient rates.
- The bar counts only **delivered** material, not what is in flight.
- `revealMode = 1` (radial) is implemented, ground included, but has never been looked at.
- **Ore deposits still escape `ArtWorldSize`**: `WorldContentSpawner` keeps its own per-axis fit, and
  three deposit arts are not square, so they are stretched vertically today.
- Nothing is tested on the rendering itself, by instruction.
