# Workflow

This workflow applies to every non-trivial modification.

## 1. Analyze

Identify:

- system involved
- requested behavior
- behavior to preserve
- likely dependencies
- relevant contracts

Do not modify files during analysis.

## 2. Read documentation

At minimum:

```text
CLAUDE.md
DEVELOPMENT_RULES.md
PROJECT_ARCHITECTURE.md
```

Read `CONTRACTS.md` when a contract or cross-system dependency is involved.

Read subsystem-specific documentation when the task touches that subsystem.

## 3. Define scope

Write down:

- files that may change
- files that must not change
- tests required
- documentation potentially affected

## 4. Check dependencies

Search globally for real usages.

Do not infer dependency structure from the target file alone.

Check:

- call sites
- references
- assembly dependencies
- prefab/scene references when relevant
- tests
- documentation contracts

## 5. Stop on ambiguity

If several architectures are possible, behavior is unclear, or scope spills into another system:

1. present the options;
2. explain consequences;
3. give a recommendation;
4. ask for a decision;
5. do not implement the ambiguous part.

## 6. Modify

Make the smallest coherent change.

Do not combine unrelated cleanup, formatting, refactoring, and functionality.

## 7. Test

Choose tests according to the layer.

```text
Pure runtime/domain logic → EditMode
Unity integration         → PlayMode
```

For procedural behavior, verify determinism where required.

## 8. Regression check

Verify that behavior remains unchanged except for the requested modification.

Pay special attention to:

- grid occupancy
- footprints
- rotations
- transport direction
- production state
- selection
- UI state
- resource accounting

## 9. Cleanup

Delete temporary harnesses, debug assets, and temporary files created solely for the task.

## 10. Documentation

Update permanent documentation when:

- a public contract changes;
- dependency direction changes;
- architecture changes;
- an accepted rule changes.

Do not add historical reports to permanent documentation.

## 11. Final report

Every non-trivial task report should contain:

```text
Files modified:
Files created:
Files deleted:

Behavior implemented:
Behavior preserved:

Tests:
- test
- result

Out of scope:
Questions / decisions:
```

## 12. Git

`main` remains stable.

Non-trivial changes should normally use a dedicated branch.

Commits should represent one coherent change.

Never overwrite another developer's work without explicit authorization.

## 13. Documentation-only tasks

For a purely documentary task:

- perform read-only analysis of the current state;
- do not change code, scenes, prefabs, or assets;
- validate cross-document consistency;
- report inconsistencies rather than silently inventing implementation details.
