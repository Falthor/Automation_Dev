# Development Rules

Rules applicable to every modification of the Unity project, by human developers or Claude Code.

## 1. Architecture

- Development is not permanently assigned by functional area.
- Respect the assembly dependency graph.
- A system must not access another system's internal state when a public contract exists.
- Do not use concrete-type knowledge as a shortcut around a contract. A concrete-type check may be used for behaviour dispatch when the contract genuinely requires different behaviour, but it must not become an excuse to reach into internal fields.
- **Changing a public contract is an architectural change.** Identify every consumer globally, update the owning document, update the affected tests, verify the dependency direction still holds, and report any behaviour change explicitly.
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
- This project runs with Domain Reload disabled (`ProjectSettings/EditorSettings.asset`), so a mutable static field's value survives across Play sessions instead of resetting automatically. Before introducing any mutable static field (a `static` field that is not `readonly`, or a `static readonly` collection/reference whose contents are mutated after initialization), stop and explicitly warn the user of this risk instead of adding it silently. This is a strict rule.

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
- A test asserting a property of the **shipped game** must read the shipped artefact — the asset, the scene, the file — never a value recopied into its own fixture. A copied constant stops tracking what it describes the moment the real one changes, and the test then reports green on a broken property, which is worse than no test at all because the subject looks covered.
- **A green suite proves nothing until the console says the game's assemblies built.** `Unity_RunCommand` compiles its own snippet, not the project: its success says nothing about whether `Game.Presentation` and the rest compiled, and the test runner will happily run the previous assembly and report every test passing. Measured: 653 green while a missing `using` had broken Presentation entirely. Always read the console for errors after a run — this is the same defect as a test reading a fixture constant, or a PlayMode suite that has not run for weeks: green that proves nothing.
- Anything **derived rather than stored** — recomputed at load instead of saved — must not draw randomness from `System.Random` or `string.GetHashCode`. Neither is guaranteed stable across runtime versions, and a change would recompose the world underneath state that *was* saved. Use `Game.Core.DeterministicHash`, and pin it with hard-coded expected values: a test that recomputes its own expectation moves with the change and sees nothing.
- **`DeterministicHash.Mix(seed, index, salt)`** is a splitmix-style avalanche (multiply, xor-shift, repeat) over one `uint`; `Unit(seed, index, salt)` divides that by 2^32 for a value in [0, 1). Every real call site names its own salt constant, never zero or shared: it is what lets several independent-looking draws come out of the very same `(seed, index)` pair — terrain's X and Y offsets, a decor clump's placement versus its kind versus its look — without one leaking into another. Terrain, sectors, wrecks, decor and the explorer robots' own wandering all depend on it, so a wrong answer here is wrong for all of them at once.
- **A tested predicate is not an applied predicate.** The test that catches an unwired seam always starts from the **real entry point**, never from the piece: `TryLaunch` refuses an out-of-band target, not `EligibilityOf` answers correctly. Both halves of a seam can be right, tested and documented while nothing joins them, and no unit test on either side can see it. Measured four times in one chantier: 527 rocks left at sorting order zero, a save field nobody wrote, a startup sweep that ran before the generator that fed it, and a mission range built, tested, documented and never called - that last one is no longer
  verifiable in the tree, the mission system having since been removed, which is precisely why the
  measurement is recorded here rather than left in the code it described.
- When a limit can be made **structurally unreachable**, prefer that to a test that catches it being exceeded. A guard that fires is a design that has run out: sector names were one per sector with a test that the vocabulary was large enough, and it duly failed at 390 625 sectors for 768 names. Capping the derived region count removed the failure instead. Keep the test afterwards, but as a statement that the construction still holds — not as a tripwire waiting.
- **A test that stays green when you break what it tests is not a test.** That is the criterion, and it is a surer one than "a negative assertion is suspect": recopying a formula into the assertion, comparing two literals, or bounding a draw without exercising the draw each leave a green suite over code that was never reached.
- Do not pursue arbitrary coverage percentages at the expense of useful tests.

## 8. Documentation

Permanent documentation describes the current accepted state.

Do not put:

- historical reasoning;
- rejected alternatives;
- temporary work reports;
- obsolete architecture

into permanent architecture documents.

**The same rule governs a comment in code. Cut the narrative, keep the invariant.** "X is this way because otherwise Y" stays; "X used to be that way, and we changed it" goes — the history is in git, and in a file it makes a past choice look like one still in force.

**And what remains is cut to the smallest form that still carries its reason.** A ten-line comment that could be two is a comment people stop reading, and a comment nobody reads protects nothing.

**A file opened for another reason is cleaned of its narrative comments on the way through, in the same commit.** It costs nothing, the file already being read; it stays reviewable, the diff staying small; and it converges, since the files touched often are the ones that matter. A file nobody opens misleads nobody. A campaign across a hundred files is the opposite trade: unreviewable, and exactly the diff in which a line of code leaves by accident.

**A document names the source, it does not copy the value.** "The Core's furthest reach, derived from the research effects" rather than "80 today"; `ComputeSystem.ReserveCap` rather than 70 000. Code can derive a figure from where it lives; a document has nothing to derive with, so the only defence is not to quote the number at all. A value written in two places is a value that will eventually disagree with itself — measured four times here: the CU reserve cap, the building cap (36 in the code and 40 six sections further down the same document), the builder robots' speed, and the robots' own range quoted four times in a document that opens by calling it "one figure".

Architecturally significant decisions should have an ADR when the decision is important enough to constrain future implementation.

**A carnet is working memory, not an archive.** An entry earns its place for exactly as long as its conclusion is nowhere else; once absorbed into a permanent document it becomes a duplicate, and the duplicate ages worse than the original because nobody rereads it when the original changes. **Rereading a carnet at the end of a chantier is part of the chantier.** What survives that reread is what no permanent document can hold:

- false trails, and why they were false;
- measurements that contradicted an intuition, with their figures;
- tooling traps;
- deviations from a spec, with their reason.

What leaves is the description of what the system does today, any decision already stated elsewhere with its reason, and the chronological account of a finished chantier. If an entry's conclusion is *not* elsewhere and deserves to be, move it to the permanent document rather than leaving it in the carnet — that is the point of the exercise, and it is how `MATERIALISATION.md` came to exist.

**A directive has a shorter life still.** It is useful between the decision and the delivery; once the chantier is finished it can only drift from the code, with its title still claiming authority. Retire it, and move what it designed but never built into `design/`.

**A purely documentary task changes no code.** Perform a read-only analysis of the current state, do not touch code, scenes, prefabs or assets, validate consistency across documents, and report inconsistencies rather than silently inventing implementation details to resolve them.

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

Every non-trivial report should carry:

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
