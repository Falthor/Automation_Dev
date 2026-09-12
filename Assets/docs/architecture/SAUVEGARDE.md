# Save

How a run is written to disk and read back: where the files live, what the file contains, and the
`Capture`/`Restore` shape every system exposes to it.

**What each field means belongs to the system that owns it**, not here. This document holds the
mechanism and the exhaustive key list; why `Discovered` is encoded the way it is belongs to `MAP.md`,
why only decor *removals* are stored belongs to `TERRAIN.md`, and so on. The reason a field has its
shape is a fact about its system and moves when that system moves.

## 1. Where a save lives

Named saves (`Game.Save`, `Assets/Scripts/Save/`): one folder per save name under
`Application.persistentDataPath`, each holding a `save.json` (`SaveService.FolderFor`/`PathFor`).
`Save(data, name)` writes one, `Load(name)` reads one, `List()` enumerates them most-recently-written
first, and `Exists(name)` answers whether a name is taken.

**A save name is the one place in this project where a string a person typed becomes a filesystem
path**, and `SaveService.Sanitise` is what stands between the two. It **drops** rather than escapes:
directory separators, the platform's invalid filename characters and control characters are removed,
so two names cannot collapse onto one folder through an encoding scheme nobody remembers; trailing
dots and spaces go too, because Windows strips them silently and `"Partie."` and `"Partie"` would
otherwise be one folder wearing two names; `.`, `..` and anything left empty fall back to
`SaveService.DefaultName`. A name that survives it addresses exactly one folder inside the save root
and cannot climb out of it.

The single `save.json` that used to sit at the root of `persistentDataPath` predates this. It is
neither read nor deleted - left where it is rather than migrated under a guessed name.

## 2. The version gate

`SaveService.Load` refuses a save whose `Version` does not equal `SaveData.CurrentVersion`, returning
`null` exactly like a read/parse failure - it does not attempt to load an old-format save with
defaults filled in.

**A missing key is not a version change.** A per-building blob missing an individual key falls back
gracefully, and a new top-level field that restores sensibly from absent is **additive**: it gets a
per-field fallback and does *not* bump `CurrentVersion`. Bumping would refuse every existing save in
order to add a field that reads perfectly well as null. `Version` is the coarser, all-or-nothing gate,
for a change too structural for a fallback.

## 3. The file is written by the JSON library alone

`SaveData` and its nested records are deliberately **not** `[Serializable]`: nothing passes them
through Unity serialization, and claiming otherwise only misdescribes the `JObject` fields and the
nullable ints, none of which Unity can serialize. `[NonSerialized]` must never be used to quiet that
either - the JSON library honours it and would silently drop those keys from every save.

The file's shape - its key set, its values, and the fact that a null keeps its key rather than
vanishing - is pinned by `SaveFormatTests`, which enumerates `SaveData` by reflection so a field added
later is covered without anybody remembering to add a line. Its key *order* deliberately is not
pinned: JSON has none.

## 4. The `Capture`/`Restore` contract

`Game.Save` has no dependency on any other `Game.*` assembly (only on the JSON library) - it never
reads private state itself. Every system capable of holding meaningful runtime state exposes a
`Capture`/`Restore` pair as a public member of that system, and only `GameRuntime`
(`Game.Presentation`) calls them, assembling and consuming a `Game.Save.SaveData`:

```csharp
public JObject CaptureState()          // BuildingRuntime and every override
public void RestoreState(JObject state)

public void RestoreContents(IReadOnlyDictionary<string,int> contents)   // PooledItemStock
public void RestoreReserve(float reserve)                               // ComputeSystem
public void RestoreState(ResearchDefinition active, float absorbedCu,
    IEnumerable<ResearchDefinition> queue, IEnumerable<string> unlockedIds) // ResearchSystem
public IEnumerable<string> GetUnlockedIds()                             // ResearchSystem
public void RestoreState(CoreRuntime core, GridCoord coreOrigin,
    IEnumerable<DepositRuntime> deposits)                               // WorldGenerator
public IEnumerable<BuildingRuntime> GetAllBuildings()                   // TransportSystem
public BuildingRuntime CreateForRestore(BuildingDefinition definition,
    GridCoord cell, Direction rotation)                                 // ConstructionService
public void RestoreBuildingCap(int? cap)                                // ConstructionService
public void Restore(float? elapsedSeconds)                              // PlayClock
```

`BuildingRuntime.CaptureState()`/`RestoreState(JObject)` are virtual and empty by default; each
subclass with real mutable state overrides both. A building's envelope (`Definition.Id`, `Cell`,
`FacingRotation`, `InputSide`) is captured generically by `GameRuntime`, never by the building itself -
only its type-specific payload goes through `CaptureState()`.

`ConstructionService.CreateForRestore` is the one other caller of the definition-to-runtime factory
besides `TryPlace`: the same instantiation switch, with no cost and no placement check, both having
happened once already at the construction the save captured. `GameRuntime` resolves a saved
`BuildingDefinition` id back to its asset through a serialized catalogue populated in the Inspector,
and a saved `ResearchDefinition` id through `ResearchDatabase.Get(id)`.

**An absent `Capture`/`Restore` pair can itself be the contract.** `ObservationRuntime` has none, and
that absence is deliberate - see `MAP.md`.

## 5. The keys

The exhaustive list, which is what `SaveFormatTests` holds:

| Key | Owner of its meaning |
|---|---|
| `Version`, `SavedAtUtc` | this document |
| `TerrainSeed`, `TerrainSize`, `TerrainScale`, `TerrainProportion` | `TERRAIN.md` |
| `Discovered` | `MAP.md` |
| `DecorRemoved` | `TERRAIN.md` |
| `WrecksDiscovered` | `MAP.md` |
| `ExplorerRobots` | `MAP.md` |
| `ComputeReserve` | `CALCUL.md` |
| `ResearchActiveId`, `ResearchProgress`, `ResearchQueue`, `ResearchUnlocked` | `RECHERCHE.md` |
| `ConstructionSites` | `CONSTRUCTION.md` |
| `CoreDirectives` | `RECHERCHE.md` |
| `CoreDefinitionId`, `CoreCellX`, `CoreCellY`, `CoreState` | `CONSTRUCTION.md` |
| `BuildingCap` | `CONSTRUCTION.md` |
| `PlayTimeSeconds` | this document |
| `PowerPriority` | `ENERGIE.md` |
| `Deposits` | `MAP.md` |
| `Buildings` | `CONSTRUCTION.md` |

`PlayTimeSeconds` (nullable, `Game.Gameplay.Session.PlayClock`) is how long the run has been played in
simulated seconds; absent restores as a run starting its count, never as one that lasted zero seconds.

**Nullable is how "absent" is told from "zero".** `BuildingCap` and `PlayTimeSeconds` are nullable
precisely so that a save predating them is distinguishable from one that set them explicitly. If the
writer ever started omitting nulls, the two would become the same thing and every per-field fallback
would silently become a fallback to nothing - which is why `SaveFormatTests` asserts that a null keeps
its key.

## 6. When it is written

Both trigger points are in `GameRuntime`:

- **New Game** (`MainMenu.unity` → `Bootstrap.unity` with `PendingGameStart.LoadedSave == null`):
  generates the world, then writes the save immediately - "New Game generates a save", per the menu's
  own contract.
- **Save** (the in-game menu, `GameRuntime.SaveAs`): recaptures every system's current state and
  writes it under the chosen name. It is the only thing that brings a save up to date.
- **Load** (`PendingGameStart.LoadedSave != null`): `GameRuntime.Awake()` restores every system
  instead of generating a world, in dependency order - Core, then deposits, then every other
  building, since an Extractor resolves its deposit from whatever already occupies its cell.
  Construction sites restore last, their reservations being re-resolved by container cell.

**Nothing is written on quit**, whether through the menu's Quit or by closing the window.

`Game.Save.PendingGameStart` carries the player's New Game/Load choice across the scene load. It is
the one deliberately mutable static field the save system introduces: a single field, consumed and
cleared at the very start of `GameRuntime.Awake()`, never read anywhere else. Because it is cleared
there, anything later in the frame that must tell a fresh run from a restored one has to be told by
`GameRuntime` or not at all - `GameRuntime.StartedFromNewGame` is that answer.
