# Building the game

How to produce a runnable Windows build, and the two things that silently break one.

Not an architecture document: this describes the build procedure only. Source of truth for the
project itself stays `architecture/DEVELOPMENT_RULES.md` → `PROJECT_ARCHITECTURE.md` →
`CONTRACTS.md` → `WORKFLOW.md`.

---

## 1. Why build at all

The Editor throttles `Update()` hard when it loses focus, which distorts every in-game timing.
Any measurement session — wave counts, time-to-Datacenter, robot round trips — must be run on a
standalone build, not in Play mode.

Use the **development** build for that: it keeps the console and the profiler, and its performance
cost is irrelevant to the measurements.

---

## 2. Before building

- The console must be clean. A build started on a project with compile errors is wasted time.
- **Build Settings scene order**: `MainMenu.unity` at index 0, then `Intro`, `Genesis` and
  `Bootstrap`, all four enabled. Index 0 is what the build opens, and it must be `MainMenu`:
  `GameRuntime.Awake()` branches on `PendingGameStart.LoadedSave`, which only `MainMenu` ever sets,
  so a build opening `Bootstrap` first has no way to reach New Game/Load. The other three are
  reached by name (`SceneManager.LoadScene(<name>)` in `MainMenuController` → `IntroController` →
  `GenesisController`), so **disabling any of them breaks that chain at runtime** — their order
  among themselves does not matter, their presence does.
- **Project Settings > Player > Run In Background** must be on, or the build suffers the same
  out-of-focus throttling as the Editor and the measurement is worthless.

---

## 3. Building

`Assets/Editor/BuildCommand.cs` is the single entry point. It lives in an `Editor` folder because
it must not: `UnityEditor` code compiled into the player breaks the build.

**From the Editor** — menu `Build` ▸ `Windows 64 (Development)` or `Windows 64 (Release)`.

**From the command line** — Unity must not be open on the project (the `Library` folder is locked
by a running Editor):

```text
Unity.exe -quit -batchmode -projectPath <project> \
          -executeMethod Game.EditorTools.BuildCommand.BuildWindowsDevelopment \
          -buildOutput <output dir>
```

`-buildOutput` is optional; without it the build goes to `Builds/Windows` under the project root.
`BuildWindowsRelease` is the same without `Development`/`AllowDebugging`.

Output: `Automation.exe` plus `Automation_Data/`. Both are needed — the exe alone does nothing.
`Builds/` is git-ignored.

---

## 4. Reading the result

A batch-mode build can exit 0 while the build actually failed, so the exit code is not the answer.
The answer is `BuildReport.summary.result`, which must be `Succeeded`, and `summary.totalErrors`,
which must be 0. `BuildCommand` already checks both and logs loudly on failure.

`Automation.exe` is often left untouched between builds because only the data changes. To confirm a
build actually landed, look at the timestamps inside `Automation_Data/` (`level0`, `level1`,
`globalgamemanagers`), not at the exe.

---

## 5. Shaders are referenced as assets — do not go back to `Shader.Find`

**Every shader the game needs is now a serialized asset reference on the component that uses it.**
`Shader.Find` has no remaining caller in shipped code; the only occurrences left are doc comments
explaining why it is not used, and editor-side tests, where it is harmless.

This section used to say the opposite — six shaders resolved by name, all six kept alive by
**Project Settings > Graphics > Always Included Shaders** — and to instruct that a new
name-resolved shader be added to that list. Following that today would reintroduce exactly the
fragility the asset references removed.

**Why an asset reference and not a name.** A shader reached only by name is stripped from a player
build unless something lists it; `Shader.Find` then returns `null`, `new Material(null)` throws, and
the rest of `GameRuntime.Start()` dies from wherever that happened. The Editor never reproduces it —
nothing is stripped there. An asset reference cannot be stripped: the build sees the dependency.

**The rule now:** a component that needs a shader takes it as a `[SerializeField] Shader` and is
wired in the scene. A class with no inspector of its own (`ProceduralSpriteFactory`) is handed one
through a settings object rather than finding its own. `ShaderReferenceTests` pins this.

**Always Included Shaders still lists the six**, which is now redundant rather than load-bearing —
it inflates build time and size slightly and hides whether the references really work. Clearing it
is safe only if verified by a real build, not in the Editor, so it is left listed and flagged here
rather than removed on reasoning alone.

The old symptom is worth keeping for recognition, in case a name lookup ever comes back: terrain and
builder drones still render while the Core, its chest, ore deposits, action radius and fog are all
missing and the camera never centres. That is one exception, not five bugs.

---

## 6. Where the build writes

Under `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Automation_Dev\`:

- `Player.log` — the run's full log, including the stack trace of any startup exception. First place
  to look when a build misbehaves.
- `save.json` — the save. **Quitting never writes it**: only Save in the in-game menu does, and
  a New Game writes its starting state once. Playing `Bootstrap` directly, without going through
  MainMenu, generates a fresh world and writes it over the save at once, as a New Game does - copy
  the file aside before such a session.
