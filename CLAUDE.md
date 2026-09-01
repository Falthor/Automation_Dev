# CLAUDE.md

Entry point for the Unity project's development documentation.

## Before any modification

1. Read `docs/architecture/DEVELOPMENT_RULES.md`.
2. Read `docs/architecture/PROJECT_ARCHITECTURE.md` for the affected system.
3. Read `docs/architecture/CONTRACTS.md` when multiple systems or a public contract are involved.
4. Follow `docs/architecture/WORKFLOW.md`.
5. Read a more specific subsystem document when the task concerns a documented subsystem.
6. For Global UI / HUD work (Top Bar, Bottom Nav, panel routing, Selection), also read `docs/architecture/GLOBAL_UI.md` — an imported Godot reference spec, not a Unity implementation to port mechanically. Its own header states exactly which sections are already implemented in Unity versus future work; treat sections marked "not implemented yet" as intent/rationale only, never as a description of current Unity code.

## Mandatory principles

- The repository documentation describes the current Unity project state and its accepted architecture.
- Do not reintroduce historical explanations, rejected alternatives, or obsolete Godot implementation details into permanent Unity documentation.
- The Godot project is the behavioral reference for migration where explicitly stated, not an instruction to reproduce Godot implementation mechanisms.
- Do not introduce abstractions without a concrete need justified by the rules.
- If an architecturally significant choice is ambiguous, stop and ask for a decision before implementation.
- Do not access another system's internal state when a public contract exists.
- After every modification, verify consistency with architecture, contracts, and rules.
- Keep generated code comments concise.
- Audit tasks are read-only unless modification is explicitly requested.

## Unity baseline

- Unity 6.5
- URP
- 2D Renderer
- Runtime custom grid as gameplay source of truth
- Terrain gameplay data isolated from presentation
- ScriptableObjects are definitions, not shared runtime state
- UI Toolkit is the primary UI technology

## Source-of-truth order

`DEVELOPMENT_RULES.md` → `PROJECT_ARCHITECTURE.md` → `CONTRACTS.md` → `WORKFLOW.md`

Subsystem-specific documents may provide the detailed behavior for their own domain (e.g. `GLOBAL_UI.md` for the Global UI/HUD). Where a subsystem document and this order disagree, the four documents above still win.
