# Global UI Specification

> **Imported reference from the Godot source project** (`docs/ui/GLOBAL_UI.md`), copied verbatim. Per `CLAUDE.md`, the Godot project is a *behavioral reference for migration*, not an instruction to reproduce its implementation mechanisms (`.gd` scripts, `Control`/`Panel` nodes, `.tscn` scenes, `.tres` theme resources) — none of that applies to this Unity project. Only the **behavioral rules and file-organization principles** below are binding here, and only once actually built in Unity.
>
> **Status in this Unity project (not the "Status: implemented" line below, which describes the Godot original):**
> - **Already implemented**: the Top Bar (§2-3) with its status cards and hover-expand, and Pause (§4 - the button, the Space shortcut, `Time.timeScale`, the PAUSE banner); per-building contextual panels docked to the right column rather than centered (§5, `.overlay-root-right`); one `.uxml` per panel + one shared `GameUI.uss` (§14-15's "reuse existing theme, don't duplicate"); the `Selection`/`SelectionRuntime` single-coordinator model (§5, §8) — `Select`/`SelectSite`/`Clear`/`OpenGlobalPanel`/`CloseGlobalPanel`, plus `SelectExplorerRobot`, mutual exclusion across all four slots; Bottom Navigation's 3 categories + permanent 8-slot construction toolbar with manual assignment (§7); per-building contextual panel opened via `Selection.Select` (§5), starting with Extractor; a construction site's own contextual panel, opened via `Selection.SelectSite` on a click on its unbuilt silhouette (`CONTRACTS.md` §7/§15) — same frame and right-hand dock as the per-building panels, showing a supply (delivered / en route / missing per ingredient) rather than a production, and handing over to the finished building's panel when the site completes; an explorer robot's own contextual panel, opened via `Selection.SelectExplorerRobot` on a click on the robot itself (`MAP.md` §2.1) — found by distance rather than by grid lookup, since a wandering robot occupies no cell; left-drag camera panning (`CameraPanController`), which takes the left button only after UI under the cursor, an open global panel and an armed construction tool have declined it - and which is why a click on the world commits on release rather than on press. The Top Bar's missions counter was removed with the mission system; the map opens from the Bottom Nav, once the explorer robots have arrived.
> - **Not implemented yet, for later**: the hover-vs-selected visual distinction for the building outline (§6, we currently only have one outline style - it does follow the inspected building rather than the cursor, but there is no separate "selected" look); minimap beyond a bare placeholder (§12); the full style-token system (§14) beyond what individual panels already happen to match.
> - **Deliberate deviations in this Unity project**: the aggregate Storage view (§9) is a straight read of `GameRuntime.GlobalStock`, which since TASK_05_ROBOT_CONSTRUCTEUR.md holds nothing itself — it is a computed aggregate over the Core chest, every placed Storage and every production building's output, minus what construction sites have reserved (`CONTRACTS.md` §15). It therefore shows exactly what a builder robot could still be sent to fetch, and never the Core's own (permanently empty) inventory. Global panels also close on a click outside the panel, in addition to §8's ESC / X / another category (explicitly requested behavior). The Top Bar carries two things the Godot spec has no equivalent for: a **run chronometer** at its left edge (`Game.Gameplay.Session.PlayClock`, simulated seconds, so it stops with Pause and resumes where it stood - saved and restored with the run), and a **building-cap card** (`TASK_04_PLAFOND_RAYON.md` §5). The Building menu's hover details also carry a **DEBIT** line for the buildings whose output is a rate - a belt's metered intake and an Extractor's interval and yield - read from whoever owns those numbers rather than restated (`CONTRACTS.md` §3a). A Foundry/Factory/Assembler deliberately gets none: its rate belongs to the recipe currently selected, not to the building.
>
> - **A shortcuts screen, from two places.** `RACCOURCIS` on `MainMenu.unity` and the Top Bar's **Menu** button in game both open `ShortcutsPanel` - the Menu button being a **deliberate deviation** from §3, where the imported spec leaves it a reserved placeholder. The markup is one template (`Shortcuts.uxml`, carrying its own `Shortcuts.uss`) that both `MainMenu.uxml` and `TopBar.uxml` instantiate, so it is one list opened from two places rather than two that can drift; the stylesheet is attached to the overlay element itself, because `TopBarController` reparents that element onto the document root to get it above every other controller's tree, and a stylesheet declared one level up would have stayed behind. `ShortcutsPanel` is a plain class handed that element - no component to add to the scene, no serialized reference to wire. It lists every reassignable action grouped by section, each row carrying its French label, its key, and a reset. Keys are shown through `GetBindingDisplayString`, never a path or an enum name, so a row reads "Z" on the key an AZERTY board prints Z on. **A conflict is announced and resolved, never refused**: the taken key is accepted, the row that held it is named, and overwriting leaves that action visibly `non assigne`. **Escape cancels a capture** rather than being captured, which is also why nothing can ever be bound to it. Reset exists per row and globally. A keyboard layout is not part of a run, which is why it is reachable before there is a world and survives starting a new one.
> - **Every action is off for as long as the shortcuts screen is up**, not only during a capture: in game it sits over a running world, and left live, `B` would open the building menu behind it and Space would pause underneath. `Show` suspends and `Hide` resumes. **The consequence is that Escape does not close this screen** - Escape is an action like any other and it is off with the rest. Escape cancels a *capture*, because the rebinding operation listens below the action layer; `FERMER` is the way out, in both screens alike.
> - **A clicked button never keeps the focus, and that rule has two halves.** In game, `TopBarController` blurs it from the document root. In the shortcuts screen, the key button blurs itself when a capture starts: UI Toolkit activates a focused `Button` on Space and Enter, so binding either would have completed the capture and re-submitted the button that opened it on the same keypress.
> - **`UIFocus.IsTypingInAField` is the one answer to "is the player typing?"**, and it asks the ancestry rather than the focused element. A `TextField` delegates its focus to the text input inside it, so `focusedElement is TextField` never answers true - which is how both copies of that guard read correctly and could not fire. Nothing in the project has a text field yet, so this is a correctness fix rather than an observed one; what protects a shortcut capture is `InputBindings.Suspend`, not this.
> - **Escape has one arbiter, not fourteen readers.** `GameRuntime.Escape` reads the key once and awards it to a single tier: the armed construction tool first, then a contextual panel, then a global panel. Every panel asks `IsClaimedBy` with its own tier instead of reading the key and guarding on its own state. §8's ESC-closes-a-panel behaviour is unchanged from the player's side; what changed is that two panels can no longer answer one keypress. The building menu is a **global panel** for this purpose - `BuildingMenuController.IsOpen` looks like independent state but is set from `GlobalPanelChanged`. See `CONTRACTS.md` §12a.
> - **Keyboard, in this Unity project.** Every shortcut is an action in one binding table and nothing reads a key directly - see `CONTRACTS.md` §12b. Camera and map pan on the **ZQSD cluster as printed on an AZERTY board**, bound as `<Keyboard>/w`,`/a`,`/s`,`/d`: a binding path names a *physical position* using the US layout as its reference, exactly as the `Key` enum does, so `<Keyboard>/w` is the key an AZERTY board prints "Z" on. **Anything shown to the player asks the control for its own `displayName`**, which is layout-aware; the path and the enum name never are. The mouse is not reassignable and is read from the device. Space is the pause and is deliberately ungated, so **a clicked button must not keep the focus** - UI Toolkit activates a focused `Button` on Space, which made pausing re-fire whatever was last clicked. `TopBarController` blurs it from one callback on the document root; only a `Button` is blurred, since a text field has to keep the focus a click gives it.
>
> Read the relevant section below for intent/rationale before building any of the "not implemented yet" items, but re-derive the actual Unity implementation from this project's own architecture docs and code — do not port Godot node names or script structure.

Authoritative specification (**for the Godot source project**) for the game's **Global UI / HUD only**: the Top Bar, the top-left game-menu affordance, pause, the Bottom Navigation, and the shared behavioral/visual rules that tie them together.

**Status: implemented** (in the Godot source project). Every element below (Top Bar with hover-expand, Power/Compute/Research, the Main Menu placeholder icon, Pause, building hover/selection feedback, Bottom Navigation, global panel routing) exists in Godot code as described. This document remains authoritative for any future change to the Godot project's Global UI — it is imported here for reference only, per the status breakdown above.

This document does **not** own contextual interfaces (`BuildingPanel`, `ProductionPanel`, `StoragePanel`, `ResearchPanel`, `PowerPanel`, `ComputePanel`, `CorePanel`, `StorageBoxPanel`, `PowerplantGazPanel`, `DataCenterPanel`). Where the Global UI must integrate with one of them, this document says so and points at the panel's own implementation rather than redefining it.

No concept-illustration image file exists in the repository at the time of writing. The visual direction below is derived entirely from (a) the direction described when this document was requested (dark industrial UI, subtle cyan/blue luminous outlines, restrained accents, compact blocks, progressive disclosure) and (b) the visual language already implemented in `resources/ui/panel_theme.tres` and `scripts/ui/chamfer_panel.gd`. If a concrete concept image exists outside this repository, it should be added under `docs/ui/` and referenced here before implementation begins.

## Related documents

- [`docs/architecture/PROJECT_ARCHITECTURE.md`](../architecture/PROJECT_ARCHITECTURE.md) — system/autoload architecture, UI's allowed dependencies.
- [`docs/architecture/CONTRACTS.md`](../architecture/CONTRACTS.md) — `Selection`'s exact contract, panel-routing rules.
- `PROTOTYPE_ANALYSIS.md` — the Building construction menu's own implementation detail (categories, icons, cost display) is owned there / in `PROJECT_ARCHITECTURE.md`, not duplicated here (see §10).

---

## 1. Design goals

The Global UI must:
- be coherent with the existing game's visual identity (the same language already used by every contextual panel via `panel_theme.tres` / `ChamferPanel`);
- remain readable over the game world;
- minimize permanent information — show the strict minimum by default, reveal detail progressively (hover / click);
- avoid clutter;
- use consistent icons, spacing, typography, borders, glow, and panel-open/close behavior;
- read as **one unified interface**, not a collection of unrelated menus.

Visual direction: dark industrial UI, subtle cyan/blue luminous outlines, restrained accent colors, compact information blocks, clear hierarchy — see §14 for the concrete style system.

---

## 2. Top Bar

Always visible during gameplay. Intentionally compact: it shows only the strict minimum in its default (collapsed) state, and reveals more on hover.

Three information areas, left to right: **Power, Compute, Research**. There is deliberately no Resources card — it was removed; Storage remains reachable only via the Bottom Navigation's Storage entry (§7/§9).

The Top Bar is a single full-width **header band** (`Root/HeaderBg`, a plain dark `Panel` with a thin cyan bottom border), anchored `left=0, top=0, right=1` so it always spans the complete viewport width and reads as one coherent HUD element — not independent floating cards. The header's height is pinned to exactly `TopBarCard.COLLAPSED_HEIGHT` (56px) — no taller than the cards it contains at rest.

The three cards sit inside a `CardsArea` (`Root/CardsArea`, a plain `Control` whose rect excludes a fixed right-hand strip reserved for Menu/Pause, §3) containing `CardsRow`, an `HBoxContainer` anchored to fill that same area with `alignment = ALIGNMENT_CENTER` — this centers the cards horizontally in the working area while leaving them anchored to the *top* of the row (each card keeps `size_flags_vertical = 0`, i.e. shrink-to-top, never stretched or vertically centered). This deliberately replaces an earlier `CenterContainer`-based layout: centering both axes meant a card's top edge shifted upward every time it grew taller on hover. With top-alignment, a hovering card's top edge never moves — only its bottom edge grows downward, past the header's bottom border, like a dropdown over the game world. Each card is a pure view over an existing autoload, opening its own global panel via `Selection.open_global_panel`. Hover-expand is shared behavior (`scripts/ui/top_bar_card.gd`, see §2.5).

### 2.2 Power

Collapsed content: Power icon (`power_top_icon.png`, a dedicated Top Bar icon) + `demand / supply kW` (Consumption first, matching the hover detail's line order below), in a deliberately small value font (13px) so the collapsed card stays compact. **No status bar in the collapsed state** — the thin progress bar lives inside `DetailClip/Detail` alongside the hover-only text lines (see §2.5), not in the always-visible row, so the collapsed card shows only the bare numbers.

Hover reveals the detailed breakdown, in this order — Consumption, Production, Balance in kW (matching the level of detail `PowerPanel` already shows), followed last by the status bar (green fill, width = `demand/supply` clamped to `[0,1]`, empty when there's no demand) — the bar reads as "how much of current production is being consumed," so it sits after the numbers that explain it, not before.

Click opens `PowerPanel` (`scenes/ui/PowerPanel.tscn`), which **already** implements everything this spec asks for: a `HistoryGraph` (`scripts/ui/history_graph.gd`) sampled every 5 seconds (`SAMPLE_INTERVAL := 5.0`) retaining 60 samples (`MAX_SAMPLES := 60`, i.e. 5 minutes), plotting `Power.get_power_supply()` vs `Power.get_power_demand()`. No new Power simulation, no new graph mechanism — this requirement is already satisfied by existing code and only needs to stay wired the same way once the Top Bar changes.

### 2.3 Compute

**Headline figure is the pooled reserve, not a CU/s flow figure.** Since the recipe-Compute-cost rework (see `CONTRACTS.md`'s Compute section), recipe-based production buildings spend Compute as a one-time per-cycle cost from `Compute.reserve`, not a continuous CU/s demand — showing a "CU/s consumption" number as the primary value would misrepresent that model, so the collapsed card instead shows the reserve itself, comma-formatted (e.g. `8,200 CU`).

Collapsed content: Compute icon (`compute_top_icon.png`, a dedicated Top Bar icon, cropped via `AtlasTexture`) + `Compute.reserve` formatted as `#,### CU`, in the same compact 13px value font as every other card. No status bar in the collapsed state (see §2.2's note — the same hover-only placement applies to every card).

Hover reveals three lines, deliberately limited to values the existing architecture already represents correctly (no new spend-history tracking was introduced to embellish this further), followed last by the cyan status bar:
- **Available**: `Compute.reserve`, restated (comma-formatted CU) — the same headline figure, for clarity in the expanded view.
- **Production**: `Compute.get_available()` CU/s — the continuous flow supply (Core's 10,000 CU/s plus any Data Center output), which is also what feeds the reserve's growth every tick.
- **Continuous draw**: `Compute.get_requested()` CU/s — ongoing CU/s consumers only (Extractor, Laboratory, PowerplantGaz). Never described as related to recipe costs, which are always stated in CU, never CU/s.
- **Status bar** (last): the *flow* system's saturation, `requested/available` clamped to `[0,1]` — a distinct, still-accurate metric for how loaded the continuous CU/s consumers are, not the reserve's own utilization, which has no fixed ceiling. Sits after the three lines (unlike Power, Compute's line order is unchanged — only the bar moved).

Click opens `ComputePanel`, which still shows the flow model's own Available/Requested/Balance/Performance in CU/s — that panel was intentionally left unchanged, since the flow model remains valid, accurate information for continuous consumers; only the Top Bar's headline figure changed.

### 2.4 Research

Collapsed content: active research id, or RP pool total when nothing is active (matches `TopStatusBar`'s prior Research card), in the same compact 13px value font as every other card. No status bar in the collapsed state (see §2.2's note). Icon: `research_bottom_icon.png`, the same file `BottomNav` uses for its own Research entry, cropped to the same `AtlasTexture` region for visual consistency between the two surfaces (§16 item 4 below). Hover reveals the cyan progress bar (`get_progress()`, 0 when idle) followed by percentage and a remaining-time estimate (`Temps restant mm:ss`) using the exact same formula already established in `research_panel.gd` (`(1 - progress) * 60 / active_lab_count` seconds; shows `--:--` when no laboratory is currently contributing). Click opens `ResearchPanel`. No separate research simulation — `Research` autoload remains authoritative (see §11).

### 2.5 Hover expansion

Default: compact, matches §2's per-section "collapsed content." On hover, the hovered section expands in place to show its detailed content (§2.2–2.4), styled consistently with the existing panel language (§14). Opening tween: 150 ms, ease-out. Closing tween: 120 ms, ease-in (`scripts/ui/top_bar_card.gd`).

Constraint: hover expansion must never permanently occupy a large portion of the screen — it reverts to collapsed when the mouse leaves, and only one section expands at a time.

### 2.6 Responsive width and header height

Design reference resolution: 1920×1080. Each card has a reference/minimum/maximum width (Power 340/250/410, Compute 390/270/460, Research 340/250/410, all in px) defined in `top_status_bar.gd`'s `CARD_WIDTHS` — widened from an earlier, narrower set now that the collapsed value font is smaller (§2.2–2.4), giving the cards a bit more breathing room. On `_ready` and on every `Viewport.size_changed` (`_update_layout()`), each card's width is recomputed as `reference * (viewport_width / 1920)`, clamped to its own `[min, max]` — cards compress toward their minimum on a narrow viewport and expand toward their maximum on a wide one, and the card group (bounded by its own max widths and by the fixed `RESERVED_RIGHT := 190` px strip excluded from `CardsArea`'s rect) never overflows or collides with Menu/Pause. The header's height does **not** scale with the viewport — it always equals exactly `TopBarCard.COLLAPSED_HEIGHT` (56px, reduced from an earlier 64px now that the collapsed content is just the icon/title row plus the smaller value line — no status bar, see §2.2) regardless of resolution, since its job is to hug the collapsed cards, not to occupy extra vertical space. Menu/Pause keep a fixed 56×56 size, recentered vertically within that fixed header height. Verified at 1366×768, 1920×1080 and 3440×1440 — header reaches both edges, cards stay inside it, no overflow, no collision with Menu/Pause at any of them.

Each card's hover-expanded `detail_height` (§2.5) is sized per card so its status bar plus 2–3 detail lines never clip against the `DetailClip` bound: Power (bar + 3 lines: Production/Consumption/Balance) and Compute (bar + 3 lines: Available/Production/Continuous draw, §2.3) both use `100px`, Research (bar + 2 lines: percentage/remaining time) uses `86px` — each bumped by 10px from an earlier, bar-less set to make room for the status bar now living inside `DetailClip/Detail` instead of the always-visible collapsed row.

---

## 3. Top-right / game menu

A small Main Menu icon, visually consistent with the rest of the Global UI (§14 card style), placed in the **top-right** corner, inside the same full-width header band as the four cards (§2) — not a separate floating control. Pause sits immediately to its left, both anchored to the top-right and vertically re-centered within the header's current height on every resize. Both buttons are `56×56` px at the 1920×1080 reference, 12px apart, 24px from the right edge (`TopStatusBar.tscn`'s `MenuButton`/`PauseButton`).

**This phase reserves the UI location and interaction concept only.** It will eventually open a menu offering Resume / Load / Save / Settings / Exit — none of that is implemented now, and no menu scene/script should be created yet. There is intentionally no in-game clock.

---

## 4. Pause

The Global UI must expose a clearly identifiable way to pause the game. Only pause itself is in scope — no additional time controls (speed-up, slow-motion, step-frame, etc.) are required at this stage, and none should be built.

Resolved (see §16 #2): native `SceneTree.paused`, toggled by `PauseButton`, communicated by `PauseOverlay` — no per-system pause flag, no building script touched.

---

### Escape's stack

One arbiter (`EscapeArbiter`), and the key has exactly one owner per frame. In order: **the menu
overlay when it is open** (the in-game menu, or the shortcuts screen it hands over to - closing
what is in front of the player outranks everything behind it), then **an armed construction
tool**, then **a contextual panel**, then **a global panel**, then **the menu overlay again when
it is closed** - which is how Escape with nothing open reaches the menu.

The overlay is the only tier that appears twice, because both ends are the same gesture on the
same object. Its owner is `TopBarController`, which also reports the overlay's state to the
arbiter (`SetMenuProbe`): the arbiter is built in `GameRuntime.Awake` and the overlay in
`TopBarController.Start`, so the fact arrives by delegate rather than by constructor. Unset means
"there is no menu", which is the truth in a scene without a Top Bar and in every test.

**A rebinding row waiting for a key keeps Escape**, and the Top Bar does nothing that frame: the
rebinding operation cancels on Escape, and a row opened by accident needs that more than the
screen needs closing.

## 5. Building / contextual selection

The Global UI must coexist cleanly with contextual building interfaces without visually fighting them or introducing a second routing system.

Existing interaction model (`Selection` autoload, see `CONTRACTS.md`'s "Selection" section) is authoritative and unchanged:

- Click building → contextual panel opens (`Selection.select`).
- Click another building → contextual panel switches.
- Click outside → contextual panel closes (`Selection.clear`, where already wired per building/tool).
- ESC → contextual panel closes.
- X (panel's own close button) → contextual panel closes.

A contextual panel and a global panel remain mutually exclusive, exactly as `Selection` already enforces (`select()`/`open_global_panel()` each clear the other). The Global UI must never bypass this by holding its own "what's open" state.

**Screen position**: every contextual panel (`ProductionPanel`, `CorePanel`, `StorageBoxPanel`, `DataCenterPanel`) is anchored to the right side of the screen, not centered — each one's existing `Center` (`CenterContainer`) node is anchored to a fixed-width column flush against the right edge (`anchor_left = anchor_right = 1.0`, a ~560px-wide column with a 20px margin from the edge) instead of the full screen width, and still centers the panel within that column exactly as before. Global panels (Storage/Building/Research/Power/Compute) stay centered — only per-building contextual panels moved.

`BuildingPanel`'s details column shows the hovered card's info (§10) and hides it once no card is hovered — never sticky. Its `Center` node (`BuildingPanel.tscn`) is horizontally centered but pinned to a fixed top offset rather than vertically centered: as the details column grows taller (e.g. the Consumption section appearing), the panel only expands downward, never upward — so its top edge never shifts while the player is browsing cards.

---

## 5a. Datacenter panel

`DataCenterPanelController` + `DataCenterPanel.uxml`, laid out from
[`../design/maquette-panneau-datacenter.png`](../design/maquette-panneau-datacenter.png). A
contextual panel like any other (§5); what is worth writing down is the order and one drawing
decision.

**Five sections, each one explaining the next**: Production (what it really produces), Répartition
(where that goes), Baies (the state of the hardware), Pièces neuves (what is in reserve), Remplacer
automatiquement sous (when to replace). The chain reads top to bottom - the bays tire, the factor
falls, the production follows - and it was unreadable before because the three subjects sat at one
typographic weight with the headline figure showing the **nominal**, which does not move.

**The headline figure is the real production, and the factor under it is `real / nominal`.** Not
`DataCenterRuntime.GetYield()`, which is the axis concentration and has nothing to do with wear: a
figure called "rendement" that stays put while the bays wear out is the defect this panel was
rebuilt to fix. The two meanings of the word are set out in
[`../carnets/datacenter-usure.md`](../carnets/datacenter-usure.md).

**Stability is drawn, not written**, and this is the panel's main decision. A bay shows a track
covering 0..1 of its output, a lighter band from its fluctuation floor to 1,00, and a tick at the
roll it is currently on - moving on every roll, which is `DataCenterRuntime.StabilityInterval`,
two seconds. The band
widens as wear falls, so two bays are comparable at a glance: a new one keeps a narrow band with its
tick pinned right, a worn one has an open band and a tick that visibly jumps. The wear is read in
the movement before anyone reads a percentage. The numbers are still there, small, under the track.

**The axis bar is the control.** Its colour boundary is the slider's handle rather than a knob on
top of a picture of the split, and each side carries its own value - which is what the old row could
not say, and why "Recherche" on the left of a slider pushed left receiving nothing read as a bug.

A bay at its threshold, or with no spare behind it, carries an alert line and an alert frame; a
spare stock of zero is alert-coloured with its icon dimmed, and sits directly above the sentence
that says a bay is only replaced *if a spare is in stock*, so the cause and effect need no wording.
Bay blocks are built once per unlocked-bay count and updated in place - ten elements per bay rebuilt
every frame is the kind of per-frame allocation `DEVELOPMENT_RULES.md` rules out.

**The Explication button** opens three paragraphs in plain language and contains no formula: wear
accelerates, a worn part becomes irregular before it dies, and it is replaced under the threshold if
a spare exists. The curves and coefficients live in the carnet, for whoever changes the balance.

## 6. Visual building feedback

Selected (and, in the future, hovered) buildings should show a luminous outline/contour consistent with the Global UI's cyan/blue accent language (§14), clearly distinguishing:
- the currently selected building;
- (future) a hovered-but-not-selected building;
- contextual focus in general.

Constraints: keep buildings' existing world placement and sprites unchanged — this is an outline/highlight effect layered on top, not a redesign of building art; it must not touch `BuildingDefinition`/sprite data (§14 keeps that boundary explicit).

Resolved (see §16 #3): a native `_draw()` polyline outline on `Building` itself.

---

### Recipe overlay (Alt)

`RecipeOverlayView` draws the output item's icon over every machine with a recipe selected, for as
long as the view is open. `InputActionCatalogue.ShowRecipes`, Alt by default and reassignable like
every other row.

**A toggle, not a hold** - a player reading their base keeps both hands free. The cost of that
choice on Alt specifically: alt-tabbing counts as a press, so coming back from another window can
leave the overlay showing. The key is rebindable, which is the answer if it grates.

**An icon, not a label.** The output item's own sprite, which the player already reads on the
belts and in the panels - and the project has no world-space text at all, so a label would mean
bringing in a text stack for one overlay. A machine with no recipe selected shows nothing, which
is the state most worth seeing.

Ranked like a building's own arrows (`SortingBands.SubOverlay`) - over the machine, never under
it. The icons are pooled per machine and rebuilt only when the recipe behind one changes; nothing
about the overlay allocates per frame, and it costs nothing at all while closed.

## 7. Bottom Navigation

Existing `BottomNav` (`scenes/ui/BottomNav.tscn`, `scripts/ui/bottom_nav.gd`) remains part of the Global UI, unchanged in role: exactly three categories, Storage / Building / Research, each a plain button routed through `Selection.open_global_panel(...)`.

**Structure**: like the Top Bar (§1), the Bottom Navigation is a single full-width **band** (`Root/BottomBg`, a plain dark `Panel` with a thin cyan top border), anchored `left=0, right=1, bottom=1` so it always spans the complete viewport width. Its height is pinned to exactly `60px` — the height of the Storage/Building/Research icon slots it contains, no taller. The minimap placeholder (§12) keeps its original `180×140` size and position (`Root/Minimap`, a direct child of `Root`, not part of the thin band) at the bottom-left corner, extending upward past the band's top edge. The three category buttons and the construction toolbar sit in `Root/BottomRow` (an `HBoxContainer`, `60px` tall, matching the band), positioned to start immediately to the right of the minimap rather than at the screen's left edge.

Icons: `Storage_icon.png` / `Production_icon.png` / `research_icon.png` (cropped to content via `AtlasTexture` regions in `BottomNav.tscn`, since the source PNGs carry inconsistent padding) — note this is a distinct icon set from the Building-menu's *category* icons (also `Production_icon.png`, reused, plus `Power_icon.png`/`convoyer_icon.png`/`Storage_icon.png`, §10); the filenames were updated once the current assets were supplied, superseding the original `Storage.png`/`Building.png`/`research.png` names.

Building shortcut: `B` opens the Building interface; pressing `B` again closes it when Building is the currently active global panel (already implemented exactly this way in `bottom_nav.gd`'s `_unhandled_input`).

All three categories share the same global-panel routing described in §8 — clicking a different category replaces whichever global panel is currently open.

**Active-state highlight**: each of the 3 buttons sits in front of a `Highlight` panel (the same cyan-bordered/teal-fill treatment as `ProductionPanel`'s active tab), shown only when that button's own panel name matches `Selection.active_global_panel` — driven entirely by `Selection.global_panel_changed`, no separate active-state tracking.

**Construction toolbar** (quick access, positioned immediately to the right of the 3 main buttons): a **permanent** row of exactly `BuildingPanel.SLOT_COUNT` (8) slots, always visible regardless of which global panel (if any) is currently open — not gated to Building being active. Slots are filled by manual assignment, not recency: while the Building panel is open and a card is hovered (mouse over it, see §5), pressing `1`–`8` puts that building's key in the matching slot (`BuildingPanel.assign_slot()`), overwriting whatever was there — there is no automatic "recently built" tracking. Occupied slots show an icon (`BuildingPanel.icon_for(key)`, reused not duplicated) with a small remaining-count badge (`BuildManager.remaining_for(key)` — "∞" for any type with no per-type cap, e.g. Extractor/Conveyor; a real shrinking number for a capped type, e.g. Laboratory/PowerplantGaz/DataCenter). Unoccupied slots render as an empty dark bordered square (the same visual language as an empty Storage Box slot, §9) rather than being omitted, so the row's layout never shifts as slots fill in. Every slot — filled or empty — also shows a small hotkey number (`1`–`8`) in its top-left corner, matching the numeric key that assigns/activates that position, so the mapping is visible at all times rather than discoverable only by trial. Clicking a toolbar icon, or pressing `1`–`8` while no card is hovered in an open Building panel (works regardless of which global panel is open since the toolbar is now always present), calls `BuildingPanel.activate(key)` — the exact same single-click entry point a grid card's own click uses, so this is a shortcut onto the existing flow, never a second construction/payment system.

---

## 8. Global panel behavior

One common interaction model for every global panel (Storage / Building / Research / Power / Compute today):

- Opening a global panel closes/replaces whichever global panel is currently active. Multiple overlapping global panels must never exist.
- ESC closes the active panel.
- X (the panel's own close button) closes the active panel.
- Clicking a different Top Bar section or Bottom Navigation category replaces the current global panel.
- Clicking a different building switches contextual selection (§5); a global panel and a contextual panel remain mutually exclusive.

This logic must not be duplicated per-panel. `Selection` (`open_global_panel`/`close_global_panel`/`global_panel_changed`, see `CONTRACTS.md`) remains the single coordinator every panel and every Global UI element reads from and writes to.

---

## 8a. Awakening message (new game only)

The Core's first seconds, shown once over the running game as a new run begins
(`AwakeningController`, `Awakening.uxml`, styled by the shared `Narrative.uss`). Not a scene: `Intro.unity` and
`Genesis.unity` are scenes played before Bootstrap, this is an overlay inside it, added to
the same `UIDocument` every other HUD controller uses and brought to the front.

**Gated on `GameRuntime.StartedFromNewGame`** (CONTRACTS.md), never on the scene loading -
a player reloading an established run must not see it again.

**It can be left, by one button.** "Passer" sits in the overlay's own corner (outside the
panel, so it neither moves as blocks appear nor shifts the message to make room) from the
first frame, and dismisses the whole screen. It was unskippable, which is right for a first
run and wrong for the twentieth.

**No key skips it, still.** Escape has one arbiter (`EscapeArbiter`) and this overlay is not
one of its tiers, so it does not read Escape at all. Neither button is ever given focus - a
focused UI Toolkit `Button` answers to Space, and Space is Pause, so focusing one would hand
the player a second skip key by accident; the skip button is declared non-focusable for that
reason, which costs it nothing since a click still reaches it. "Commencer" still does not
exist - not disabled, absent - until the last line has landed: skipping is a deliberate way
out, not the same gesture as the end of the message.

Every wait is unscaled (`WaitForSecondsRealtime`, `Time.unscaledDeltaTime`), so pausing
behind the overlay cannot stall the message half-way through.

Two typefaces, deliberately: the three measures are a right-aligned table in the tabular
monospace every value in the game uses, with the red kept for something actually going
wrong; the conclusions are prose in the current face. The Core reads its state, it does not
narrate it.

The displayed reserve is seeded from `ComputeSystem.Reserve` and then drifts down on its
own - the first figure is true, the drift is presentation and never touches the reserve.

Pacing is six serialized fields on the component (fade, ordinary pause, the two longer
pauses, the pause before the button, and the reserve's tick), adjustable in the Inspector
while the game runs.

## 8c. Power panel - the Priorité tab

Two tabs over one header (`PowerPanelController`, `PowerPanel.uxml`, laid out from
[`../design/maquette-onglet-priorite.png`](../design/maquette-onglet-priorite.png)). **Courbes** is
what the whole panel used to be - demand, production, balance, the saturation bar and the 5-minute
history graph; it became a tab rather than moving. **Priorité** is the order building types receive
power in, and the player drags handles to change it.

**A reorderable list states an order but not its consequences, and the consequence is the point.**
The cut line is drawn where the running total of demand crosses production: above it served, below
it stopped and dimmed. The player sees their extractors are off, drags them up, and the line moves
as they drag - nothing has to be calculated. When production covers everything the line is simply
absent, which is the correct amount of urgency to show.

**The group the line falls on shows its partial service** - "1 sur 2" - rather than a binary state,
because a deficit rarely lands exactly between two groups. It is counted at the draw
(`PowerSystem.ServedInstancesOf`), not divided out of the kilowatts: see CONTRACTS.md §9 for why
dividing would be wrong for the Data Center.

**Every type is listed, built or not** (the player's call). One with no instances shows a dash for
its count and no state, and holds the place it was given. The rows come from the priority order,
which comes from the building catalogue - so a type added to the game appears here without this
screen being touched, which is the requirement the order of identifiers exists for (§9a).

**The line is positioned over the list, not inserted into it.** A child that is not a row would make
every index in the drag's arithmetic conditional. The drag updates the model live rather than on
release - that is what makes the line move while the player drags - and moves the row element
instead of rebuilding it, because a rebuilt row would drop the pointer capture mid-gesture.
`PowerPanelController.RowHeight` and `.pr-row`'s height in `GameUI.uss` have to agree: one row of
pointer travel is one place.

## 8b. In-game menu (Top Bar's Menu button)

`GameMenuPanel` + `GameMenu.uxml`, instanced by `TopBar.uxml` and reparented onto the document
root by `TopBarController` - the same move the shortcuts overlay makes, and for the same reason:
left inside the bar every other controller draws over, a full-screen overlay would open
underneath them.

Four entries. **Sauvegarder** asks for a name, prefilled with the name the session is already
writing to, so the ordinary gesture overwrites your own run rather than quietly making a second
copy of it; it reports a failed write instead of closing, because a write can fail on a locked
file and a menu that lies about it is how a run gets lost. **Charger** lists the saves on disk,
rebuilt on every open since this very session may have written one. **Options** hands over to the
shortcuts screen (§8a's neighbour, `ShortcutsPanel`), which the Menu button used to open directly.
**Quitter** calls `Application.Quit`, a no-op in the Editor by design.

Named saves, one folder each, are `CONTRACTS.md` §14's contract; the name travels on
`PendingGameStart` and lives on as `GameRuntime.CurrentSaveName`.

**The save-name field is the project's first in-game text field**, and that is why Pause is now
gated on `UIFocus.IsTypingInAField`: Pause is bound to Space, so naming a save would otherwise
pause and unpause the game once per word. The digit and letter readers were already gated; this one
was not.

## 9. Storage global interface

Already implemented as specified (`scripts/ui/storage_panel.gd`) — this section documents the existing, authoritative behavior for the Global UI to integrate with, not a new requirement:

- Aggregates exactly two sources: `Core`'s own pooled inventory, and every placed Storage Box's contents (`Building.get_contents()` on each).
- Excludes every production building's pooled inventory (Foundry, Factory, Advanced Foundry, Assembler, Laboratory) and the Data Center's installed-component slots — none of those are "storage" in this sense.
- Identical item ids are merged into one row (quantities summed) across Core + every box.
- Storage Box capacity (2 distinct item ids per box, 100 units per item id per box) is a Storage Box implementation detail (see `PROJECT_ARCHITECTURE.md`'s `StorageBox` entry), not something the Storage panel itself enforces or re-implements — it only reads already-capped contents.
- Visual style: same `ChamferPanel`/`panel_theme.tres` language as every other panel (§14) — already matches.

---

## 10. Building interface

Owned elsewhere: category navigation, hover-to-preview/single-click-to-build, construction details/cost/consumption, the exact category list (Production/Power/Logistic/Organisation and their building membership), and the category icon set (`Production_icon.png`/`Power_icon.png`/`convoyer_icon.png`/`Storage_icon.png`) are all specified and implemented in `scripts/ui/building_panel.gd` / `scenes/ui/BuildingPanel.tscn`, and referenced from `docs/architecture/PROJECT_ARCHITECTURE.md`'s "Building definition registries" section and `PROTOTYPE_ANALYSIS.md`.

The Global UI's only responsibility toward it: the Bottom Navigation's Building entry and the `B` shortcut (§7) open it through the same `Selection.open_global_panel("building")` routing every other global panel uses — no separate integration surface, no re-specification of its internals here.

---

## 11. Research interface

Reachable from both the Top Bar (§2.4) and the Bottom Navigation (§7), both opening the same `ResearchPanel` through `Selection.open_global_panel("research")` — one interface, two entry points, no duplicated state.

Current research content (`Research.RESEARCH_DEFS`, `scripts/autoload/research.gd`): `cpu_assembler` (CPU/Assembler unlock), `memoire` (Memory), `datacenter` (Data Center unlock), `extra_cpu_slot` (Extra CPU slot). This list is owned by `Research`/`ResearchPanel`; the Global UI only links to it and must not duplicate research logic or unlock rules.

---

## 12. Future UI elements (explicitly NOT implemented)

Documented as future direction only — none of the following should be built as part of implementing this specification:

- in-game clock;
- additional time controls beyond plain pause (speed-up, slow-motion, step-frame);
- richer resource breakdowns (beyond §2.1's single collapsed resource);
- detailed Power source/consumer breakdown (per-building, not just supply-vs-demand);
- detailed Compute source/consumer breakdown (per-building);
- notifications / event feed;
- alerts / warnings;
- minimap (a visual placeholder box — no map rendering, no camera-sync, no click-to-navigate — reserves the bottom-left screen space for it: `BottomNav.tscn`'s `Minimap` node, a `180×140` `ChamferPanel`-styled empty panel with a "MAP\n(reserved)" label, positioned at the bottom-left independently of the Bottom Navigation's thin band — the Storage/Building/Research buttons and construction toolbar start to its right rather than overlapping it);
- objectives / mission information;
- additional player/game statistics;
- Main Menu functionality (Resume/Load/Save/Settings/Exit) — only its icon/location is in scope now (§3).

---

## 13. Responsive / interaction principles

Priority order for the Global UI: gameplay visibility > information hierarchy > minimal permanent screen occupation > mouse accessibility > keyboard accessibility > visual consistency > predictable panel transitions.

Must coexist with, and never capture input meant for: WASD camera movement, mouse-edge camera movement, mouse-wheel zoom (`camera_controller.gd`). No additional camera controls are introduced by this spec.

---

## 14. Style system

Reusable visual language for every future Global UI element, derived from what every contextual panel already uses today (`resources/ui/panel_theme.tres`, `scripts/ui/chamfer_panel.gd`) — the Global UI should extend this language, not invent a second one.

- **Primary background treatment**: panels sit over the game world on a dark, near-opaque plate — `ChamferPanel`'s default `bg_color = Color(0.063, 0.078, 0.098, 0.98)`. Top Bar cards use a slightly lighter card tone, `Color(0.082, 0.098, 0.118, 1)` (see `TopStatusBar.tscn`'s `StyleBoxFlat_card`).
- **Panel treatment**: `ChamferPanel` (chamfered/straight-cut corners, not rounded) is the standard panel background+border+accent-inset draw for any panel-sized surface; a smaller Top-Bar-style card uses a plain 1px-bordered `StyleBoxFlat` instead (lighter weight, since it's a compact HUD element, not a panel).
- **Borders**: `border_color = Color(0.19, 0.23, 0.26, 1)`, `border_width = 2.0` on `ChamferPanel`; `1px` on Top-Bar-style cards.
- **Luminous outlines / accent**: cyan accent `Color(0.212, 0.780, 0.910, 0.4)` as `ChamferPanel`'s faint inset accent line (not a bright outline around the whole panel — it's an accent, not a frame, per `chamfer_panel.gd`'s own design note) plus decorative corner ticks at `alpha 0.85` of the same hue. The building-selection outline (§6) should use the same hue family at a higher, more visible alpha/width, since it needs to read at world scale, not panel scale.
- **Accent colors**: primary cyan `Color(0.333, 0.867, 0.961, 1)` for headline values/titles (already used for every panel's title text and Top Bar values); a secondary warm accent `Color(0.91, 0.66, 0.24, 1)` exists for a second graph series (`HistoryGraph.color_b`) and doubles as the unaffordable/warning tint family (§ below).
- **Text hierarchy**: panel titles at `18px`, cyan; Top-Bar card titles (`"POWER"`, `"COMPUTE"`, ...) at `11px`, uppercase, cyan `Color(0.333, 0.867, 0.961, 1)` (reduced from an earlier `13px` — the Top Bar's typography read as too large relative to its compact card size); Top-Bar primary values at `13px`, light `Color(0.937, 0.949, 0.957, 1)` (reduced from `20–24px`, then again from `16px`); Top-Bar secondary/detail text at `12px`, muted `Color(0.843, 0.878, 0.898, 1)` or `Color(0.6, 0.68, 0.72, 1)` for the least prominent line (reduced from `14px`); secondary/available-quantity text elsewhere (Building menu details, Storage rows) stays at muted `Color(0.6, 0.68, 0.72, 1)`.
- **Status/progress bars**: a `6px`-tall `Panel` with a dark background (`Color(0.04, 0.05, 0.06, 1)`) and small corner radius, containing a child `Panel` anchored `[0, 0, ratio, 1]` in the accent hue (green `Color(0.35, 0.82, 0.42, 1)` for Power, cyan `Color(0.333, 0.867, 0.961, 1)` for Compute/Research) — the ratio is set directly on `anchor_right` each frame, no shader/animation. Used by the Power/Compute/Research Top Bar cards (§2.2–2.4) -- lives inside each card's `DetailClip/Detail`, so it's revealed on hover along with the rest of the detail text, never part of the always-visible collapsed row.
- **Icon sizing**: `48×48` for a selectable card's own icon (Building menu), `24–32px` for an inline item/ingredient icon (cost rows, Storage rows), `28px` for a category icon (Building menu's left rail). Top Bar / Bottom Nav icons render at a fixed on-screen box (`60×60` in `BottomNav.tscn`) regardless of source resolution, cropped to content bounding box first if the source PNG carries inconsistent padding (established pattern, see `BottomNav.tscn`'s `AtlasTexture` regions).
- **Spacing**: `6–10px` separation between sibling elements inside a panel/card (`h_separation`/`v_separation`/`separation` values already in use across every panel scene); `14px` between major panel regions (e.g. Building menu's category column vs. its right pane).
- **Corner treatment**: chamfered (straight-cut), not rounded, per `ChamferPanel` — this is a deliberate identity choice, not a default; any new Global UI surface with a background should chamfer it the same way rather than using a plain rectangle or rounded corners.
- **Hover state**: for a plain themed `Button` (`panel_theme.tres`), hover swaps to a warm-tinted `StyleBoxFlat` (`bg_color = Color(0.235, 0.129, 0.129, 1)`, red-tinted border `Color(0.87, 0.42, 0.38, 1)`) — this is the existing close-button/card hover language; a Top-Bar hover-*expand* (§2.5) is a distinct, additional behavior (size/content change, not just a style swap) layered on top of this same hover-tint.
- **Selected state**: for a `toggle_mode` `Button` in a `ButtonGroup` (established pattern in `building_panel.gd`), the *pressed* StyleBox already doubles as "selected" — `bg_color = Color(0.06, 0.078, 0.078, 1)`, same red-tinted border as hover. No separate selected-state styling needed; reuse this.
- **Disabled state**: locked entries are both `disabled = true` and tinted with a grey `modulate` (`Color(0.5, 0.5, 0.5, 1)`) over their normal style/icon — combination of Godot's native disabled style plus this modulate is what currently reads as "locked."
- **Warning state**: unaffordable-but-unlocked entries use a warm-yellow `modulate`/text tint (`Color(1, 0.85, 0.4, 1)`) — distinct from the disabled grey, so "locked" and "unaffordable" never look the same. No red "error" tint currently exists in the language; if a true warning/alert state is needed later (§12 mentions alerts as future work), it should stay clearly distinct from both the locked-grey and unaffordable-yellow already established.
- **Transition/animation philosophy**: current panels open/close instantly (`visible` toggling on `global_panel_changed`/`building_selected`) with no animation anywhere in the codebase today. The Top Bar's hover-expand (§2.5) is the first place this spec asks for a smooth transition — keep it simple (e.g. a size/opacity tween) and consistent if/when introduced; do not add motion anywhere else in the Global UI (panel open/close, category switching, card selection) unless explicitly requested later, to avoid the interface feeling inconsistent between an animated and eleven un-animated interactions.

No external UI library or framework — native Godot `Control` nodes only, same as every existing panel.

---

## 15. Implementation rule

Before implementing any part of this specification:
- inspect the existing UI architecture (`TopStatusBar`, `BottomNav`, `ChamferPanel`, `panel_theme.tres`, every existing global/contextual panel script);
- reuse existing panel/theme components (§14) rather than introducing new ones;
- avoid duplicating existing systems — Storage aggregation, Power/Compute graphing, Research state, and Building-menu categorization are all already implemented and must only be *linked to*, never reimplemented;
- preserve `Selection` as the single UI/selection coordinator (§5, §8) — no parallel routing;
- consume `Compute`, `Power`, `Research`, `Buildings`, and `Items` exactly as they exist today;
- do not modify gameplay mechanics unless an integration strictly requires it (and if so, treat that as a decision to flag, not to make silently).

If an implementation detail is not specified in this document, do not silently invent a gameplay rule or a new architectural pattern — extend this document with the ambiguity instead (see the open questions in §2.1 and §4).

---

## 16. Open questions requiring a future design decision

Resolved during implementation (Sub-phase 4), recorded here for reference:

1. **§2.1 Resources card** — superseded: originally shown as `lingot_fer` (item icon + `Core.get_input_amount` + every `StorageBox.get_input_amount`, no capacity/`X/Y`), the Resources card was later removed from the Top Bar entirely per explicit request. Storage remains reachable only via the Bottom Navigation's Storage entry (§7/§9); no card in the Top Bar duplicates it.
2. **§4 Pause** — resolved: native `SceneTree.paused`, toggled by a dedicated `PauseButton` (`scripts/ui/pause_button.gd`) that sets its own `process_mode = PROCESS_MODE_ALWAYS` (the only Global UI element that needs to; every other node's default `PROCESS_MODE_INHERIT` already stops on its own). A `PauseOverlay` label (also `PROCESS_MODE_ALWAYS`) shows "PAUSED" whenever `get_tree().paused` is true.
3. **§6 Building highlight** — resolved: a native `_draw()` polyline outline on `Building` itself (no shader, no duplicated sprite), sized from the same `Grid.effective_size(base_size, rotation_deg)` every building already uses for its footprint. Hover uses the dimmer accent color/thinner line, selected the brighter/thicker one; selected wins if both are true. `Core` and `BeltBuilding` (`Conveyor`/`Splitter`) each fully override `_draw()` for their own reasons, so each calls the shared highlight draw explicitly rather than inheriting it for free.

Still open / future work:

4. **§3 Main Menu** — only the icon/location is implemented now (`MainMenuButton`, top-right, unwired); its menu contents (Resume/Load/Save/Settings/Exit) and any save-system design remain entirely future work.
