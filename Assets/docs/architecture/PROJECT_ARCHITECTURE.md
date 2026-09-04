# Project Architecture

## 1. Purpose

This document defines the accepted target architecture for the Unity project.

The project is an industrial automation game on an orthogonal grid. The Unity implementation is native to Unity 6.5. The previous Godot project is used as a behavioral and design reference where migration work explicitly requires preservation of existing behavior.

This document describes the Unity architecture, not the historical Godot implementation.

## 2. Unity baseline

- Unity 6.5
- Universal Render Pipeline (URP)
- 2D Renderer
- UI Toolkit as the primary UI technology
- C# for gameplay/runtime code
- MonoBehaviour only where Unity lifecycle or engine integration is actually required

## 3. Architectural principles

### 3.1 Runtime is the gameplay source of truth

The runtime model owns gameplay state.

Unity presentation objects do not become the authoritative source for:

- grid occupancy
- building footprint
- production state
- inventory
- transport state
- power
- compute
- research
- terrain gameplay state

A Tilemap, SpriteRenderer, Collider, Animator, or prefab may represent runtime state but must not silently replace it.

### 3.2 Definition → Runtime → View

The standard building flow is:

```text
BuildingDefinition
       ↓
BuildingRuntime
       ↓
BuildingView
       ↓
Unity GameObject / Prefab
```

- Definition: static content and configuration.
- Runtime: per-instance mutable state.
- View: Unity representation.

### 3.3 ScriptableObjects

ScriptableObjects are used for static definitions.

They must not contain mutable state shared between multiple runtime instances.

Examples:

```text
ItemDefinition
RecipeDefinition
BuildingDefinition
ResearchDefinition
```

Runtime inventories, production timers, transport queues, power state, and similar mutable state belong to runtime objects.

## 4. Assembly architecture

The project uses the following assemblies:

```text
Game.Core
Game.Data
Game.Grid
Game.Gameplay
Game.Construction
Game.Save
Game.Presentation
Game.UI
Game.Tests
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

`Game.Save` (§21) is a standalone leaf assembly with no dependency on any other `Game.*` assembly - only on the save format's own serialization needs. `Game.Presentation` and `Game.UI` depend on it; nothing lower depends on it, and it depends on nothing higher.

`Game.Tests` references the assemblies required by the tests.

A lower-level assembly must not depend on a higher-level presentation or UI assembly.

The exact dependency graph is a contract and must not be changed casually.

## 5. Core

`Game.Core` contains low-level domain types that have no dependency on Unity presentation or higher-level gameplay.

Examples:

- grid coordinates
- directions
- rotations
- identifiers
- footprints
- small value types
- neutral results/errors where justified

Core must remain deliberately small.

## 6. Data

`Game.Data` contains static definitions.

Examples:

- items
- recipes
- buildings
- research
- visual definition data where it is part of static content

Data may depend on Core.

Data must not own mutable runtime state.

## 7. Grid

`Game.Grid` owns the runtime grid model.

Responsibilities include:

- grid/world conversion
- cell occupancy
- footprint validation
- rotation handling
- terrain gameplay data
- ore/deposit registry
- grid coordinates and cell queries

The grid must not depend on concrete production, transport, UI, or building subclasses merely to perform generic grid operations.

### Grid versus Tilemap

The runtime grid is authoritative.

Tilemap is a presentation mechanism when used.

Gameplay must not ask a Tilemap whether a cell is occupied as its source of truth.

## 8. Gameplay

`Game.Gameplay` contains the simulation and runtime gameplay systems.

Functional areas include:

```text
Buildings
Transport
Production
Power
Compute
Research
Inventory
Selection
```

These are functional responsibilities inside the gameplay assembly unless a future dependency boundary justifies another assembly.

The project must not create one assembly per subsystem merely for organizational appearance.

## 9. Construction

`Game.Construction` owns:

- construction tool state
- placement validation orchestration
- placement
- demolition
- conveyor drag/replacement behavior
- construction costs, drawn from the player's global stock first, then the Core, then every placed Storage, and refunded in full to the global stock on demolition
- building unlock checks
- construction preview orchestration

Construction may depend on Core, Data, Grid, and Gameplay.

Gameplay must not depend on Construction merely to execute normal simulation.

## 10. Presentation

`Game.Presentation` translates runtime state into Unity representations.

Examples:

- BuildingView
- TerrainView
- ConveyorView
- animation presentation
- visual effects
- camera integration where appropriate

Presentation may read runtime state through approved contracts.

Purely visual configuration (e.g. TerrainView's ground texture set and tiling) is exposed through a dedicated ScriptableObject preset (`GroundTextureProfile`) rather than inline fields, so the active look can be swapped by reassigning one asset instead of editing code. This is a presentation-only preset, not a Definition in the Definition → Runtime → View sense (§3.2): it has no corresponding Runtime type. See [`TERRAIN.md`](TERRAIN.md) for the full ground-rendering system (biome noise blend, relief lighting) and its constraints.

Runtime must not depend on presentation classes.

## 11. UI

`Game.UI` uses UI Toolkit.

Structure:

```text
UXML = structure
USS  = styling
C#   = UI behavior
```

UI reads public runtime contracts.

UI must not access internal fields of gameplay systems.

### Selection

Selection is runtime state.

The intended flow is:

```text
Input
  ↓
SelectionRuntime
  ↓
SelectionChanged
  ↓
UI / contextual inspector
```

A contextual inspector does not search the world independently to determine what is selected.

### Global UI

The global UI includes:

- Top Status Bar
- game menu
- pause
- Bottom Navigation
- Construction Toolbar

Detailed behavior may be documented in a dedicated UI document when implemented.

## 12. Buildings

A building consists conceptually of:

```text
BuildingDefinition
BuildingRuntime
BuildingView
Prefab
```

The logical footprint is defined by the gameplay/content model and is independent from visual bounds.

```text
Logical Footprint
      ≠
Visual Bounds
      ≠
Collider Bounds
```

A building may visually overhang its logical footprint.

### Building categories

The functional categories from the source project are retained:

```text
Core
Animated / production-oriented buildings
Belt / transport buildings
```

Concrete types include, where migrated:

- Core
- Extractor
- Laboratory
- PowerplantGaz
- DataCenter
- StorageBox
- Factory / ProductionBuilding
- Foundry
- AdvancedFoundry
- Assembler
- Conveyor
- Splitter

The exact inheritance hierarchy is an implementation choice; functional contracts are not.

### Core

Core is a special world entity and is unique.

Its gameplay behavior must remain consistent with the source project's accepted behavior when migrated, including its role as the starting power/compute source where those systems are implemented.

The game's starting items are not part of it: they belong to the player, seeded once at game start into a global stock (`GameRuntime.GlobalStock`, from `WorldGenerationSettings.StartingStock`) that no building owns. The Core keeps its own pooled inventory for what transport actually delivers to it, and starts empty.

### Ore deposits

Ore deposits are world entities, not buildings.

## 13. Transport

Transport uses a lane/item runtime model.

A belt-like building may contain lanes, and each lane may contain ordered items in transit.

The runtime model owns:

- entry
- exit
- item type
- progress
- lane ordering
- transport capacity/spacing rules

Presentation draws the transport state; it does not own the authoritative queue.

The detailed transport behavior remains a subsystem-level specification and should be migrated from `docs/TRANSPORT.md` when that subsystem is implemented.

## 14. Production

Production buildings use explicit runtime production state.

The player-facing production contract exposes:

- available recipes
- selected recipe
- production time
- required ingredients
- progress
- resource availability
- production state

The UI does not access timers or inventories directly.

Automatic producers such as Extractor and Foundry remain behaviorally distinct from player-selected recipe production.

## 15. Power and Compute

Power and Compute are global simulation domains.

They expose aggregate values through public runtime contracts.

The simulation owns:

- supply
- demand
- active/inactive state
- performance ratios where applicable
- reserves where applicable

The UI reads the exposed values and does not recompute the economy independently.

## 16. Research

Research owns:

- research pool
- active research
- contribution
- progression
- unlock state

Building construction may query research unlock state through a public contract.

## 17. Bootstrap and lifecycle

The entry scene is:

```text
Bootstrap.unity
```

Bootstrap is an orchestration boundary, not a God Manager.

Conceptual startup:

```text
Unity
 ↓
Bootstrap
 ↓
GameRuntime
 ↓
Initialize definitions
 ↓
Initialize runtime systems
 ↓
Initialize Grid
 ↓
Generate/load terrain
 ↓
Initialize gameplay
 ↓
Initialize presentation
 ↓
Initialize UI
 ↓
Game Ready
```

Systems must not depend on incidental `Awake()`/`Start()` ordering.

Simulation systems should be C# objects unless Unity lifecycle integration is genuinely required.

A central simulation tick is preferred over unrelated gameplay `Update()` loops. Exact tick frequency remains intentionally open until simulation requirements justify a decision.

## 18. Tests

Two categories are used:

```text
EditMode
PlayMode
```

EditMode is preferred for pure domain/runtime logic.

PlayMode is used for:

- Unity integration
- scenes
- prefabs
- colliders
- rendering integration
- Animator behavior
- UI Toolkit integration
- input integration

Procedural systems must be deterministic where the design requires reproducibility:

```text
same seed + same parameters = same result
```

No arbitrary global coverage target is required.

## 19. High-risk shared areas

The Unity equivalents of the most shared systems must be modified carefully:

- Grid
- Data/content registries
- Building runtime/contracts
- Construction
- selection
- transport contracts

The exact list may evolve with the codebase.

## 20. Source-project behavioral constraints

Where migration explicitly targets the existing Godot behavior, preserve observable behavior unless a change is requested.

Important source behaviors include:

- orthogonal grid logic
- footprint/rotation rules
- generic building flow contracts
- separate pooled inventory versus belt lanes
- production-cycle semantics
- conveyor configuration intent
- splitter replacement intent
- selection behavior
- deterministic terrain behavior where applicable

These behaviors belong in `CONTRACTS.md` or subsystem-specific documents once implemented in Unity.

## 21. Save system

A mono-save system exists (`Game.Save`, detailed in `CONTRACTS.md` §14): a single fixed save file on disk, always overwritten in place, no save slots.

The entry point is no longer `Bootstrap.unity` directly - `MainMenu.unity` is scene index 0 in Build Settings and loads first, with `Bootstrap.unity` at index 1. `MainMenu.unity` presents New Game / Load; both write `Game.Save.PendingGameStart.LoadedSave` and load `Bootstrap.unity`, which `GameRuntime.Awake()` reads to decide between generating a new world and restoring one from the save file. New Game writes the save immediately (its initial state); the save is rewritten with current progress on `OnApplicationQuit`.

Every runtime system capable of holding meaningful state (`GridRuntime` via the buildings placed on it, `ComputeSystem`, `ResearchSystem`, `TransportSystem`'s registered buildings, `WorldGenerator`, and every `BuildingRuntime`) exposes a `Capture`/`Restore` pair used only by the save layer - this is a public contract addition (CONTRACTS.md §14), not a private-field bypass (§1/§12 still hold).


## 22. Accepted architectural decisions

The following decisions are part of the accepted baseline and do not require separate ADR files.

### Unity version
Unity 6.5 is the project baseline.

### Rendering
URP with the 2D Renderer is the project baseline.

### Runtime grid
A custom runtime grid is authoritative for gameplay. Tilemap is presentation only when used.

### Definitions
ScriptableObjects represent static definitions. Mutable per-instance state belongs to runtime objects.

### Bootstrap
A dedicated Bootstrap scene and explicit initialization are used. Godot Autoloads are not reproduced as a collection of Unity Singletons.

### Buildings
Buildings follow the conceptual flow:

```text
Definition → Runtime → View → Prefab
```

### UI
UI Toolkit is the primary UI technology. UXML defines structure, USS defines styling, and C# defines behavior.

### Assemblies
The baseline assembly structure is:

```text
Game.Core
Game.Data
Game.Grid
Game.Gameplay
Game.Construction
Game.Save
Game.Presentation
Game.UI
Game.Tests
```

### Testing
EditMode is preferred for pure runtime/domain logic. PlayMode is used for Unity integration.

### Git and Claude Code
The repository documentation is part of the development workflow. `CLAUDE.md` is the entry point, and architecture, contracts, rules, and workflow are versioned with the project.
