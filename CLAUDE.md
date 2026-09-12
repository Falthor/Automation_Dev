# CLAUDE.md

Entry point for the Unity project's development documentation.

## Before any modification

1. Read `Assets/docs/architecture/DEVELOPMENT_RULES.md`. It governs every modification and outranks
   everything else.
2. Read `Assets/docs/README.md` and open the document that owns the subject you are about to touch.

**One subject, one document.** A subject is described in exactly one place and absent from every
other, so the document that owns yours is the only one you need - and the only one to update when the
behaviour changes.

## Mandatory principles

- The repository documentation describes the current Unity project state and its accepted
  architecture.
- Do not reintroduce historical explanations, rejected alternatives or obsolete Godot implementation
  details into permanent documentation.
- The Godot project is the behavioural reference for migration where explicitly stated, never an
  instruction to reproduce Godot implementation mechanisms.
- Do not introduce abstractions without a concrete need justified by the rules.
- If an architecturally significant choice is ambiguous, stop and ask for a decision before
  implementing.
- Do not access another system's internal state when a public surface exists.
- After every modification, verify consistency with the documents that own what you touched.
- Keep generated code comments concise.
- Audit tasks are read-only unless modification is explicitly requested.

## Unity baseline

- Unity 6.5, URP, 2D Renderer
- Runtime custom grid as the gameplay source of truth
- ScriptableObjects are definitions, not shared runtime state
- UI Toolkit is the primary UI technology
