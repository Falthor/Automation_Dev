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
- **Build Settings scene order**: `MainMenu.unity` at index 0, `Bootstrap.unity` at index 1, nothing
  else enabled. `GameRuntime.Awake()` branches on `PendingGameStart.LoadedSave`, which only
  `MainMenu` ever sets — a build that opens `Bootstrap` first has no way to reach New Game/Load.
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

## 5. Shaders looked up by name — the one real trap

Six shaders are obtained at runtime through `Shader.Find`, not through a material asset:

| Shader | Used by |
| --- | --- |
| `Custom/ShadedGroundTiled` | `TerrainView` |
| `Custom/CloudShadowOverlay` | `TerrainView` |
| `Custom/BuildingGroundSlab` | `ProceduralSpriteFactory` |
| `Custom/ActionRadiusOverlay` | `ActionRadiusView` |
| `Custom/FogOfWar` | `FogOfWarView` |
| `Custom/GridLinesOverlay` | `GridLineView` |

Nothing references them as an asset, so the build strips any one of them that is not listed in
**Project Settings > Graphics > Always Included Shaders**. A stripped shader makes `Shader.Find`
return `null`, and `new Material(null)` throws — killing the rest of `GameRuntime.Start()` from
wherever it happened. The Editor never reproduces this: nothing is stripped there.

**Adding a shader that will be found by name means adding it to Always Included Shaders in the same
change.** All six above are currently listed.

The symptom is distinctive: terrain and builder drones still render (initialised before `Start()`,
and driven from `LateUpdate` respectively) while the Core, its chest, ore deposits, action radius
and fog are all missing and the camera never centres. That is one exception, not five bugs.

---

## 6. Where the build writes

Under `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Automation_Dev\`:

- `Player.log` — the run's full log, including the stack trace of any startup exception. First place
  to look when a build misbehaves.
- `save.json` — the save. **Quitting overwrites it**, in a build and in the Editor alike
  (`GameRuntime.OnApplicationQuit`). Playing `Bootstrap` directly, without going through MainMenu,
  generates a fresh world and replaces the existing save on exit. Copy the file aside before any
  throwaway session.
