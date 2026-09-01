# Development Rules

Rules applicable to every modification of the Unity project, by human developers or Claude Code.

## 1. Architecture

- Development is not permanently assigned by functional area.
- Respect the assembly dependency graph.
- A system must not access another system's internal state when a public contract exists.
- Do not use concrete-type knowledge as a shortcut around a contract.
- Do not introduce circular dependencies.
- Do not introduce a Manager, Service, Controller, Interface, Strategy, EventBus, Factory, or similar abstraction without a concrete problem and sufficient real consumers.
- ScriptableObjects represent static definitions, not shared mutable runtime state.
- Runtime gameplay state is authoritative; presentation is not.
- Grid runtime is authoritative; Tilemap is not.
- Logical footprint is independent from visual bounds.
- MonoBehaviour is not the default location for simulation logic.

## 2. Migration behavior

- When porting an existing Godot behavior, preserve observable behavior unless a behavior change is explicitly requested.
- Do not translate Godot implementation mechanisms literally when Unity has a different native mechanism.
- Autoloads are not automatically converted into Unity Singletons.
- Scenes are not automatically converted into Godot-style global nodes.
- A Godot Resource is not automatically mapped to a ScriptableObject if it represents runtime state.
- Preserve contracts and semantics, not implementation accidents.

## 3. Scope

Before modifying anything:

- identify the system;
- identify expected behavior;
- identify behavior that must remain unchanged;
- identify files to modify;
- identify files that must not be touched;
- search real usages globally;
- inspect relevant contracts.

Modify only what is necessary.

Improvements discovered outside the requested scope must be reported without silently applying them.

## 4. Files and abstractions

- Do not create a file solely because another file is long.
- Extract a file only when responsibility is clearly distinct, coupling remains low, and the resulting contract is small and justified.
- Do not reformat a file in the same change as a functional modification.
- Do not mix refactoring and new functionality unless the task explicitly requires both.
- Prefer the smallest coherent change.

## 5. Unity-specific rules

- Do not use `Update()` in many independent gameplay objects when the behavior belongs to the central simulation tick.
- Do not rely on incidental `Awake()`/`Start()` ordering for system initialization.
- Do not use `Resources` as a global service locator.
- Do not make every runtime system a Singleton.
- Do not use a prefab as the authoritative gameplay database.
- Do not derive gameplay occupancy from renderer/collider dimensions.
- Keep editor-only concerns separate from runtime code when practical.

## 6. UI rules

- UI Toolkit is the primary UI technology.
- UXML defines structure.
- USS defines style.
- C# defines behavior.
- UI reads public contracts.
- UI must not access internal runtime fields.
- UI must not duplicate simulation calculations.
- Selection is owned by runtime selection state.
- Contextual panels react to selection/state rather than independently searching the world.

## 7. Tests

- Prefer EditMode tests for pure C# runtime logic.
- Use PlayMode tests for Unity integration.
- Add regression tests for important bugs where practical.
- Deterministic generators must produce identical results for identical seed and parameters when determinism is part of the contract.
- Do not pursue arbitrary coverage percentages at the expense of useful tests.

## 8. Documentation

Permanent documentation describes the current accepted state.

Do not put:

- historical reasoning;
- rejected alternatives;
- temporary work reports;
- obsolete architecture

into permanent architecture documents.

Architecturally significant decisions should have an ADR when the decision is important enough to constrain future implementation.

## 9. Claude Code

Before implementation:

1. read `CLAUDE.md`;
2. read the applicable architecture/rules documents;
3. identify scope;
4. inspect dependencies;
5. stop on ambiguity.

After implementation:

1. inspect the actual diff;
2. run appropriate tests;
3. check for regressions;
4. update permanent documentation if the architecture/contracts changed;
5. report modified/created/deleted files;
6. report tests and results;
7. report out-of-scope observations.

## 10. Git

- `main` must remain stable and working.
- Non-trivial work should use a dedicated branch.
- One coherent change per commit is preferred.
- Do not modify, merge, reset, rebase, or delete another developer's work without explicit authorization.

Suggested commit prefixes:

```text
feat:
fix:
refactor:
test:
docs:
chore:
```
