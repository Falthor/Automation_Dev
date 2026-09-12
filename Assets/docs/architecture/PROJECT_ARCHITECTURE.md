# Project Architecture

How the project is put together: its baseline, its principles, its assemblies and their dependency
direction, the one draw-order ladder, and what happens at startup.

**Not what each system does.** Every system has its own document, and this one names none of their
behaviour.

## 1. Purpose

An industrial automation game on an orthogonal grid. The Unity implementation is native to Unity 6.5.
The previous Godot project is a behavioural and design reference where migration explicitly requires
preserving existing behaviour - never an instruction to reproduce its implementation mechanisms.

## 2. Baseline

- Unity 6.5
- Universal Render Pipeline, 2D Renderer
- UI Toolkit as the primary UI technology
- C# for gameplay and runtime code
- MonoBehaviour only where the Unity lifecycle or engine integration is actually required

## 3. Principles

### 3.1 Runtime is the gameplay source of truth

The runtime model owns gameplay state. Unity presentation objects never become the authoritative source
for grid occupancy, footprints, production state, inventory, transport state, power, compute, research
or terrain. A Tilemap, SpriteRenderer, Collider, Animator or prefab may *represent* runtime state but
must not silently replace it.

The runtime grid in particular is authoritative: gameplay must never ask a Tilemap whether a cell is
occupied.

### 3.2 Definition → Runtime → View

```text
BuildingDefinition  →  BuildingRuntime  →  BuildingView  →  GameObject / Prefab
```

- **Definition**: static content and configuration.
- **Runtime**: per-instance mutable state.
- **View**: the Unity representation.

The logical footprint belongs to the content model and is independent of visual bounds and collider
bounds. A building may visually overhang the cells it stands on.

### 3.3 ScriptableObjects are definitions, not state

They hold static content and must not carry mutable state shared between runtime instances. Runtime
inventories, production timers, transport queues and power state belong to runtime objects.

## 4. The assemblies

```text
Game.Core        low-level domain types: coordinates, directions, rotations, small value types.
                 Deliberately small.
Game.Data        static definitions: items, recipes, buildings, research, and visual definition data
                 that is part of static content. Never mutable runtime state.
Game.Grid        the runtime grid model: grid/world conversion, occupancy, footprint validation,
                 terrain data, per-cell discovery and observation, the sector partition, wild decor,
                 the deposit registry.
Game.Gameplay    the simulation: buildings, transport, production, power, compute, research,
                 inventory, selection, construction sites and builder robots, exploration,
                 notifications.
Game.Construction placement, demolition, drag behaviour, unlock checks, preview orchestration.
Game.Save        the save format and the file layer. A leaf.
Game.Presentation runtime state turned into Unity representations: views, animation, effects, camera.
Game.UI          UI Toolkit screens and panels.
Game.Tools       one component, for one reason - see below.
Game.Tests       references whatever the tests need.
```

### Dependency direction

```text
Game.Core
   ↑
Game.Data
Game.Grid
   ↑
Game.Gameplay
   ↑
Game.Construction
   ↑
Game.Presentation
   ↑
Game.UI
```

**A lower-level assembly must never depend on a higher-level presentation or UI assembly.** The exact
graph is a contract and must not be changed casually.

`Game.Save` is a standalone leaf with no dependency on any other `Game.*` assembly. `Game.Presentation`
and `Game.UI` depend on it; nothing lower does, and it depends on nothing higher.

`Game.Tools` exists for one reason: Unity refuses to attach a component that comes from an editor
assembly, so the research tree editor's scene handle has to live in a runtime one. It carries the
`UNITY_EDITOR` constraint, so no build embeds it.

The grid must not depend on concrete production, transport, UI or building subclasses merely to perform
generic grid operations. Gameplay must not depend on Construction merely to run the simulation.
Presentation reads runtime state through public surfaces; runtime never depends on presentation
classes.

**Purely visual configuration is a preset, not a Definition.** A ScriptableObject that configures a
look and has no corresponding Runtime type is not part of the Definition → Runtime → View flow; it
exists so the active look can be swapped by reassigning one asset instead of editing code.

## 5. Draw order

One sorting layer (`Default`); depth is resolved entirely by `sortingOrder`, and every one of them
comes from `SortingBands`. **No view writes a literal.**

**The rule.** An element lower on the grid draws in front of one above it, because its art may overhang
the cell above. Four bands, plus fog over all of them:

| Band | Ordering | Contents |
|---|---|---|
| Ground | fixed | terrain, nano coverage, flat decor and vegetation, concrete, deposits, grid, action radius, belts, the items riding them, Splitter and Crossroad |
| Sorted | by depth | every building, the Core, construction silhouettes, their shadows and arrows, raised rocks, the builder robots |
| Flying | fixed | empty - the robots walk, so they are in the sorted band. Kept for drones, projectiles, aerial effects |
| Information | fixed | placement previews, their arrows, the hover outline |

**Every order is derived from the one below it**, with no literal but the first and no gap between the
bands: no rank is stored anywhere - not in a scene, not in a save - so renumbering is free and
inserting a layer is one line.

**The sort key is the bottom edge, never the centre of the art** (`DepthSortLadder.Order(worldBottomY,
subLayer)`): the footprint's bottom row for a building, the sprite's bottom for free-standing decor.
Stated as a world coordinate, so grid-aligned buildings and scattered decor go through one function.
Within a row, four sub-layers - silhouette, shadow, sprite, overlay - and a row's difference always
outweighs a sub-layer's.

**The sorted band is measured against the camera, not against the world.** `sortingOrder` is a `short`,
and ranking off absolute world Y needs four values per cell per sub-layer - which fits a small map and
silently stops working on a large one, because the rank clamps rather than failing. `DepthSortLadder`
ranks against a window that follows the view and re-anchors when the camera approaches its edge, so the
band's size follows the zoom-out cap instead of the map: a 300-cell world and a 10 000-cell one cost
the same 4 096 orders.

Two consequences. Only what is on screen at the same time is ordered - objects far outside the window
collapse onto one rank, which is what the scheme trades for its bounded size. And **a sorted-band rank
can never be stored**: it is true only for the window it was measured in. Panning re-ranks nothing,
since every rank shifts by the same amount and only their comparison is read.

**Why not Unity's Transparency Sort Mode in Custom Axis.** It sorts on each transform's own position,
and every building root stands at its footprint's *centre* - precisely the key the rule forbids, so
every asset pivot and every spawn would have to be re-anchored first. It would also replace the belts'
cell-parity tie-break at a seam with Y, and a horizontal run shares one Y - back to an undefined
winner. And it cannot be asserted outside a running camera, where a computed order is a pure function
with tests.

**What is in which band is a judgement about the art, not about the type.** Vegetation splits: flowers,
bushes, dead wood and pebbles are drawn top-down with no rising silhouette and stay on the ground;
large rocks are drawn at an angle with a mass well above their base, and are sorted. Ore deposits are a
scatter of small chunks lying flat, so they are ground - which is what keeps an Extractor's
construction silhouette from being hidden behind the ore it stands on, with no exception to write down.

**Permanent marks stay with the thing they mark.** A placed building's own input and output arrows are
world decoration and belong to the sorted band, ranked by the cell each arrow sits on. The information
band is for what answers a gesture in progress - a preview hidden behind a building would be a preview
that failed at its job.

Scene decor that rises above its base carries a `DepthSortedDecor` marker instead of a baked rank, and
`GameRuntime` puts it on the ladder at startup - a number frozen in a scene would be true only for
wherever the camera stood when it was written. A test scans every scene and fails if any renderer
carries a sorted-band order at all.

## 6. Bootstrap and lifecycle

`MainMenu.unity` is scene index 0 and loads first; it presents New Game and Load, and hands over to
`Bootstrap.unity`. Bootstrap is an orchestration boundary, not a God Manager.

```text
Unity → MainMenu → Bootstrap → GameRuntime
          ↓
    definitions → runtime systems → grid → terrain → gameplay → presentation → UI → ready
```

**Systems must not depend on incidental `Awake()`/`Start()` ordering.** Cross-object wiring belongs in
`Start`, which runs after every object's `Awake`; anything needing a value another component builds
partway through its own `Start` takes it on its first `Update` instead.

Simulation systems are plain C# objects unless Unity lifecycle integration is genuinely required.
**A central simulation tick is preferred over unrelated `Update()` loops**: nothing that belongs to the
simulation drives itself from its own `Update`. Exact tick frequency remains deliberately open.

## 7. High-risk shared areas

Modify with care, because most of the project passes through them:

- the grid;
- the data and content registries;
- the building runtime and the surfaces it exposes;
- construction;
- selection;
- transport.

The list may evolve with the codebase.
