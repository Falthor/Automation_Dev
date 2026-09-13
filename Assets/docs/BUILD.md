# Building the game, and the project's own tools

How to produce a runnable Windows build, the two things that silently break one, and the editor tools
the project carries for its own content.

Not an architecture document: nothing here describes what the game does.

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

**From the command line** — Unity must not be open on the project: a running Editor locks the
`Library` folder, and the build dies on it.

**Check it before launching, and never resolve it by closing the Editor yourself** — it may hold
unsaved scene or asset changes, and killing it loses them. Ask for it to be closed, or build from
the Editor menu instead, which works precisely because the Editor is the one holding the project.

```text
Test-Path <project>\Temp\UnityLockfile     # true  -> an Editor holds the project
Get-Process Unity -ErrorAction SilentlyContinue
```

Both are worth asking: the lockfile is the authority, and the process answers the case where a
crash left a lockfile behind with no Editor running. If either says an Editor is up, stop there.

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

---

## 7. The research tree editor

`Tools > Research Tree > Open Editor Scene` opens `Assets/Scenes/Tools/ResearchTree.unity`: one object
per research in the database, cores included, placed at its distance from the centre and its angle.

- **Create** — `Tools > Research Tree > New Research`. With a node selected, the new research lands one
  ring further out at the same angle and takes it as a prerequisite; with nothing selected it arrives on
  the second ring, outside every branch, and is flagged unreachable.
- **Fill in** — a node's inspector is its asset's own. The hierarchy shows the display name, and
  renaming a node there renames the research. The file takes the name of the identifier a second after
  the last keystroke, not on every key. An identifier that cannot be a file name, or is already taken,
  is reported once in the console and the file keeps its name. Renaming breaks no reference: the
  database and the prerequisites point at the asset, not at its name.
- **Link** — the *Link* tool in the scene toolbar, offered as soon as a node is selected. Click one
  node, then another: the first becomes a prerequisite of the second. Clicking the same pair again
  removes the link; clicking empty ground drops the first.
- **Move** — the ordinary translate tool. The node follows the mouse and stays where it is dropped, on
  a ring or between two, to a tenth of a ring; within 0.15 of a ring it snaps to it. Settling it *during*
  the drag pinned it to its ring and made it look immobile.
- **Look** — a *Research Tree* overlay sets how big the balls are drawn, one slider for the cores and
  one for the researches. Each ball shows its research's own icon; names are not painted permanently,
  only the ball under the cursor names itself.

Everything goes through Unity's own undo. Nothing is written to disk before `File > Save Project`,
except creation, which saves the asset it makes immediately.

**The scene stores nothing.** The objects are handles and the asset is the only truth: dropping a node
writes its distance and angle to the asset, then puts the node back where the asset says. Any other
change to the asset - the inspector, an undo, a merge - moves the node. The scene is rebuilt from the
database on opening and on every hierarchy change: a missing handle is created, one whose research is
gone or already has a handle is removed. Deleting a node therefore deletes nothing; it comes back. The
scene file is disposable, and is versioned so it can be found rather than because it holds anything.

**The two validations are the game's own**, not a copy: a node on a prerequisite cycle is drawn red and
its links with it, an unreachable node orange, and a summary in the top-left names the offenders.
Linking two nodes so as to close a cycle also writes a console warning.

**How big the balls are drawn is a view preference, and it lives in `EditorPrefs`** - not in the scene,
which is rebuilt and would erase it, and not in an asset, which would put one person's reading comfort
into the tree's own data and into everyone's commits.

`Game.Tools` exists for this tool alone: Unity refuses to attach a component that comes from an editor
assembly, so the scene handle has to live in a runtime one. It carries the `UNITY_EDITOR` constraint, so
no build embeds it, and the rest of the tool lives in `Assets/Editor/ResearchTree/`.

**What it does not do.** Deleting a research - it would have to be removed from the database and from
every other research's prerequisites. And undoing a creation leaves the file: the undo removes the
research from the database and its node from the scene, but the asset stays on disk, outside the
database and therefore outside the game.
