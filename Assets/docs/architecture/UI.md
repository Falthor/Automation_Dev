# Interface

What the player reads and clicks: the Top Bar, the Bottom Navigation, the panels, and the three rules
that keep them from fighting each other - one selection, one Escape, one binding table.

UI Toolkit throughout: UXML defines structure, USS defines style, C# defines behaviour. The UI reads
public runtime contracts and must not read private inventories or timers, mutate the grid, determine
occupancy from a Tilemap, or infer gameplay state from a renderer. It may request a public state, issue
an allowed command, display static definition data, and react to a notification.

**It must not duplicate a simulation calculation.** Where a figure exists in a system, the screen reads
it; where two screens show the same figure, they read the same place.

## 1. Selection: four slots, at most one set

Selection is runtime state (`SelectionRuntime`), and it owns what is currently inspected plus the named
global panel. Four slots - an inspected **building**, an inspected **construction site**, an inspected
**explorer robot**, and a **global panel** - of which at most one is ever set: opening any closes the
others.

```text
Select(building)   SelectSite(site)   SelectExplorerRobot(robot)   Clear()
OpenGlobalPanel(name)   CloseGlobalPanel()
```

with one observable changed-notification per inspection slot. `Clear()` empties whichever slot was set
and notifies **only** that one: a slot already empty raises nothing, so a panel never re-runs its close
path for a selection it did not hold.

**A construction site gets its own slot** rather than riding the building slot. Its segments *are*
`BuildingRuntime`s and are what the grid returns for those cells, so routing one through `Select` would
open the panel of the building it is going to become - a production panel over a machine that does not
exist yet. What a site is waiting for and what a building is doing are different questions about the
same cell.

**An explorer robot gets one for the same reason read backwards**: it is not a `BuildingRuntime` at
all, and every per-building panel keys off the building slot with an `as` cast, so riding that slot
would have needed each of them to learn to ignore it. It is also not a grid occupant, so unlike the
other two there is no cell to mark - a robot is found by distance from the click.

**A global panel may also name one building, without taking the building slot.**
`SetGlobalPanelSubject`/`GlobalPanelSubject` let a global panel that is about one specific place -
the Storage panel inspecting one box among the aggregate - give the world something to mark,
without competing with the building slot's own exclusivity. Cleared with the panel: a freshly
opened one names no subject until it says otherwise.

`GameRuntime.IsUIBlockingInput` is true while any of the four is set. **Routing a world click to a
panel is one map**, owned by whichever component resolves clicks and reused rather than copied. Only
building types that actually have a panel may become the selection: selecting one that has none would
block world input with nothing able to clear it.

A contextual inspector reacts to the notification; it does not search the world to work out what is
selected.

## 2. Escape has one arbiter, not fourteen readers

**`GameRuntime.Escape` is the only reader of the key**, and the only place the priority between the
things Escape can close is written down. A consumer asks `IsClaimedBy(EscapeClaimant)` with its own
tier and acts only if it gets `true`. It must not read the key itself, and must not infer its turn from
its own state alone.

The stack, in order: **the menu overlay when it is open** - the in-game menu, or the shortcuts screen it
hands over to, since closing what is in front of the player outranks everything behind it - then an
**armed construction tool**, then a **contextual panel**, then a **global panel**, then **the menu
overlay again when it is closed**, which is how Escape with nothing open reaches the menu.

The overlay is the only tier appearing twice, because both ends are the same gesture on the same
object. `EscapeArbiter.Claimant` derives the answer from the construction service's selection and the
selection runtime's slots, so exactly one tier is ever the claimant: there is no consumed flag, and no
dependence on which component's `Update` runs first. `EscapeClaimant.None` describes a state and is
never something a consumer can claim.

Two consequences a new consumer has to know. A contextual and a global panel cannot both be open, so
their relative order never decides anything today - it is stated so that it stays decided here if that
changes. And an armed tool **can** coexist with an open panel, since nothing disarms a tool when a
panel opens: a consumer serving the armed-tool tier must therefore not sit behind a gate on
`IsUIBlockingInput`, or the key is awarded to a reader that never runs.

**A rebinding row waiting for a key keeps Escape**, and the Top Bar does nothing that frame: the
rebinding operation cancels on Escape, and a row opened by accident needs that more than the screen
needs closing.

## 3. One binding table, and nothing reads a key directly

Every keyboard shortcut is an action in `InputSystem_Actions.inputactions`, the Input System's
**project-wide actions asset**, named from `ProjectSettings.asset` and `EditorBuildSettings.asset` -
two references that do not look like code. `InputBindings` is the only way in: `Find(name)` resolves an
action, `IsPressed`/`WasPressedThisFrame` read it null-tolerantly. A consumer resolves once in `Start`
and holds the reference; `FindAction` walks the maps and has no business running per frame.

`InputActionCatalogue` names every reassignable action once, with its French label and its section, in
display order. **The names appear both there and in the asset, and that is the one duplication that
could not be designed away** - the asset format has nowhere to put a label. A test asserts the two sets
are exactly equal in both directions, so an action added to one and forgotten in the other fails the
suite instead of reaching play as a blank row or a dead shortcut.

**The table is keyboard-only, and a test enforces it.** The mouse buttons and the wheel are read
straight from the device and are deliberately not reassignable: the click/drag arbitration is a
contract between the camera pan, the building selection and the construction adapter sharing one slop
threshold, and a reassignable button could produce a configuration in which clicking selects nothing.
The map's pointer drag could not be in the table even if it were reassignable - it is a UI Toolkit
`PointerDownEvent`, not an Input System read - and is pinned to the left button by a named constant.
The intro and Genesis screens answer to any key at all, which is not a binding.

**The camera and the map share four pan actions** rather than owning four each. They always read the
same physical keys; reassigning "vers le nord" now moves both, which is what reassigning it means.

**A binding path names a physical position**, with the US layout as its reference, exactly as the `Key`
enum does - so `<Keyboard>/w` is the key an AZERTY board prints "Z" on. **Anything shown to the player
asks the control for its own `displayName`**, which is layout-aware; the path and the enum name never
are.

`PreferencesService` owns `preferences.json`, **beside the saves and never inside one**: a keyboard
layout belongs to the person playing, not to the run, so it survives starting a new game and must not
travel with a save file. An absent or unreadable file means "no preferences", which is the truthful
default; writing an empty override set **erases** the key rather than keeping the last non-default
value. `ApplyStoredOverrides` clears every override before applying, because Domain Reload is disabled
and the asset instance survives Play sessions - idempotence is the requirement, not a nicety.

## 4. The shortcuts screen, from two places

`RACCOURCIS` on the main menu and the Top Bar's **Menu** button both open `ShortcutsPanel`. The markup
is one template both hosts instantiate, so it is one list opened from two places rather than two that
can drift; the stylesheet is attached to the overlay element itself, because the Top Bar reparents that
element onto the document root to get above every other controller's tree, and a stylesheet declared one
level up would have stayed behind.

It lists every reassignable action grouped by section, each row carrying its French label, its key and a
reset. **A conflict is announced and resolved, never refused**: the taken key is accepted, the row that
held it is named, and overwriting leaves that action visibly unassigned - a hole the player can fill,
rather than a silent swap. The rule compares **effective** paths, not the asset's defaults, so two
actions agreeing only on their defaults are a clash while one whose override moved it away is not.

**Every action is off for as long as the screen is up**, not only during a capture: in game it sits over
a running world, where `B` would open the building menu behind it and Space would pause underneath.
`Show` suspends and `Hide` resumes, and that is the only path that restores them - so **Escape does not
close this screen**, being an action itself. Escape cancels a *capture*, which the rebinding operation
handles below the action layer; the way out is FERMER, in both hosts alike. A consequence worth stating:
nothing can ever be bound to Escape.

**A clicked button never keeps the focus**, and that rule has two halves. In game the Top Bar blurs it
from the document root; in the shortcuts screen the key button blurs itself when a capture starts. UI
Toolkit activates a focused `Button` on Space **and** on Enter, so binding either would have completed
the capture and re-submitted the button that opened it on the same keypress.

`UIFocus.IsTypingInAField` is the one answer to "is the player typing?", and it asks the **ancestry**
rather than the focused element: a `TextField` delegates its focus to the text input inside it, so
`focusedElement is TextField` never answers true.

## 5. The Top Bar

A single full-width header band, always visible in play, deliberately compact. Every element is a
plain clickable icon-and-value pair - no card, no border, no background, no hover-to-reveal step.
Clicking one directly opens its global panel; there is nothing to see first.

Two groups, on either side of the run chronometer at the left edge. **Left, right after the
chronometer:** Power, Building Compute, Research Compute, and the current Core directive (when one is
active) - resource-shaped figures the player checks often. **Right:** Research and the building cap -
progress-shaped figures, none of which the imported Godot spec has an equivalent for. Menu and Pause
sit at the far right.

Each element is a pure view over a system and opens its own global panel; no detail block exists
beyond the single value shown.

**Building Compute and Research Compute each show their own pooled reserve, not a CU/s flow.**
Showing a rate as the primary value would misrepresent a model where production pays one-shot costs.
Building Compute is what every one-shot spender and the Core's own grant draw from (CALCUL.md);
Research Compute is what a research absorbs from by default.

**The Power element turns amber above 85 % of production drawn** - the warning that comes before a
deficit, not a milder version of one. Past 100 % it is a deficit, which the element already says in
red; amber there would replace "you are short" with "you are nearly short".

**The Research element and the Research category exist only once a core is powered.** Before that the
introduction runs on the Core's directives alone, and the menu offers nothing it cannot deliver.

## 6. The Bottom Navigation

A single full-width band: exactly three categories - Storage, Building, Research - each routed through
the one global-panel mechanism, plus a **permanent** eight-slot construction toolbar, always visible
whatever panel is open. Each button sits in front of a highlight shown only when its own panel is
active, driven entirely by the panel-changed notification rather than by separate state.

Toolbar slots are filled by **manual assignment, not recency**: while the Building panel is open and a
card is hovered, pressing `1`–`8` puts that building in the matching slot. Occupied slots show an icon
and a remaining-count badge; unoccupied ones render as an empty bordered square so the row's layout
never shifts. Every slot shows its hotkey number, so the mapping is visible rather than discoverable by
trial. Clicking a slot, or pressing its digit, goes through the same single entry point a grid card's
click uses - a shortcut onto the existing flow, never a second construction path.

**The zoomed-out map opens from the slot the layout already reserved for a minimap** - an element that
existed in the UXML with no consumer. It is not a placeholder for a live minimap: at ten thousand cells
a thumbnail of the whole world says nothing.

## 7. Global panels, and one interaction model

- Opening a global panel closes whichever is active. Two never overlap.
- Escape closes the active one, subject to the arbiter above.
- The panel's own X closes it.
- Clicking a different Top Bar element or Bottom Nav category replaces it.
- Clicking outside the panel closes it - a deliberate addition to the imported spec.
- A contextual panel and a global panel remain mutually exclusive.

This logic is not duplicated per panel: the selection runtime is the single coordinator every panel
reads from and writes to. The building menu is a **global panel** for this purpose - its `IsOpen` looks
like independent state but is written from the panel-changed notification.

Per-building contextual panels are docked to a fixed-width column against the right edge; global panels
stay centred.

## 8. The panels worth writing down

**The construction site panel** shows a supply - delivered, reserved, missing per ingredient - rather
than a production, and hands over to the finished building's own panel when the site completes. The
bars describe the **stock** committed ("must I produce?"); a separate service line describes the
**robots** ("is this moving?"). Conflating them is what made four sites placed at once all report
delivery under way when only one was being served. The danger colour keys on **missing material**, never
on "delivered < required", which would paint an ingredient red for the whole time robots are fetching
it. The count under a bar is what the bar shows as filled, so the two can never contradict each other.

Because materialisation reserves cyan for "under construction", the panel separates arrived from
en-route by a **45° hatch** rather than by a second hue - a distinction carried by hue alone would also
say nothing to a colour-blind player.

**The Data Center panel** reads top to bottom as a chain: Production, Répartition, Baies, Pièces neuves,
Remplacer automatiquement sous. The headline figure is the **real** production and the factor under it
is `real / nominal` - not the axis concentration, which does not move as the bays wear out. **Stability
is drawn, not written**: a bay shows a track over 0..1 of its output, a lighter band from its
fluctuation floor to 1.00, and a tick at the roll it is currently on. The band widens as wear falls, so
two bays are comparable at a glance and the wear is read in the movement before anyone reads a
percentage. The axis bar **is** the control: its colour boundary is the handle, and each side carries
its own value. Bay blocks are built once per unlocked-bay count and updated in place.

**The Power panel** has two tabs over one header. *Courbes* is the demand, production, balance,
saturation and five-minute history. *Priorité* is the order building types receive power in, and the
player drags handles to change it. **A reorderable list states an order but not its consequences, and
the consequence is the point**: the cut line is drawn where the running total of demand crosses
production - above it served, below it stopped and dimmed - and it moves as the player drags, so nothing
has to be calculated. The group the line falls on shows its partial service ("1 sur 2"), counted at the
draw rather than divided out of the kilowatts. Every type that draws power is listed, built or not,
filtered on whether it draws power at all rather than on a kilowatt figure - the Data Center has no
fixed figure and would otherwise vanish from the screen it most belongs on. A drag places a row
**before its visible neighbour** rather than at an index, because hidden types sit between visible ones.

**The in-game menu** has four entries. *Sauvegarder* asks for a name, prefilled with the one the session
is already writing to, so the ordinary gesture overwrites your own run rather than quietly making a
second copy; it reports a failed write instead of closing. *Charger* lists the saves on disk, rebuilt on
every open. *Options* hands over to the shortcuts screen. *Quitter* never saves, and asks first when the
player has acted since their last save. Closing the window leaves at once, without asking and without
saving. The save-name field is the project's first in-game text field, which is why Pause is gated on
"is the player typing".

**The awakening message** plays once over a new run, gated on the run being fresh rather than on the
scene loading - a player reloading an established run must not see it again. It can be left by one
button, from the first frame; no key skips it, and neither button is ever focused, since a focused
button answers to Space and Space is Pause. Every wait is unscaled, so pausing behind it cannot stall
the message half-way.

**The recipe overlay** draws the output item's icon over every machine with a recipe selected, for as
long as it is open. A **toggle, not a hold** - a player reading their base keeps both hands free. An
icon, not a label: the project has no world-space text at all, and a machine with no recipe shows
nothing, which is the state most worth seeing. Ranked with a building's own arrows, over the machine and
never under it; pooled per machine and rebuilt only when a recipe changes.

**The Storage panel's aggregate view** is a straight read of the construction aggregate, so it shows
exactly what a builder robot could still be sent to fetch - never the Core's own permanently empty
inventory.

## 9. Visual feedback in the world

A selected building shows a luminous outline in the cyan accent family, and it follows the **inspected**
building rather than the cursor: the outline's job is to say which building the open panel is about, and
the cursor is somewhere else entirely - on the panel. A global panel has no building to point at, so the
outline steps aside. A hovered-but-not-selected style is not built yet; there is one outline style
today.

## 10. The style language

Dark industrial, subtle cyan accents, chamfered corners rather than rounded, compact blocks, progressive
disclosure. Panels sit on a dark near-opaque plate; borders are a muted slate; the primary accent is
cyan for headline values and titles, with a warm amber as the second graph series and the
unaffordable/warning tint. Locked entries are greyed and disabled; unaffordable-but-unlocked entries are
amber - **"locked" and "unaffordable" must never look the same**.

Status bars are a thin dark track with a child fill anchored to the ratio, no shader and no animation.
Panels open and close instantly, and so does the Top Bar - motion is not to be added there or elsewhere
without being asked for.

Icons render at a fixed on-screen box regardless of source resolution, cropped to content first where a
source carries inconsistent padding.

## 11. Not built yet

- the hover-versus-selected distinction for the building outline;
- a live minimap, as opposed to the zoomed-out map screen that opens from its slot;
- the full style-token system, beyond what individual panels already happen to match;
- richer per-building breakdowns of power and compute;
- alerts and warnings as a category distinct from the notification banner.
