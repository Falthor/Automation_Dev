# Pending Decisions

A registry of findings reported during a review or an audit that were not resolved on the spot - never
a description of what the project is, and never a history of what was decided. An entry leaves the
moment it is decided, whichever way; the decision and its reason live in the commit that resolves it,
not here.

Empty is the expected state between audits. A non-empty file when a chantier is declared closed means
either the entry gets a decision now, or it gets carried forward with a stated reason - never silently
dropped. See `DEVELOPMENT_RULES.md` section 8.

## Format

One entry per finding:

- **Subject** (`File.cs:line`) — what was found, why it was not resolved on the spot, which
  review/commit found it.

## Open

- **Crossroad cost vs. the new Conveyor cost** (`Assets/Data/Buildings/CrossroadDefinition.asset`) —
  the early-game-economy-rebalance chantier gave the Conveyor a real per-segment cost (1 Plaque de
  fer) but left the Crossroad free, per its own directive ("ne pas modifier son coût... il devra être
  réévalué ultérieurement"). A free Crossroad can now be laid in place of two Conveyor segments
  crossing, which is a way around the belt's own sink the design has not yet ruled on. Needs a
  deliberate design decision, not a default carried over from when both were free.
- **Steel recipe's exact values** (`Assets/Data/Recipes/Steel_Recipe.asset`) — 2 Plaque de fer + 1
  Charbon → 1 Acier, 8 s, 15 CU. The chantier asked for "la définition minimale nécessaire" without
  giving numbers; these were chosen to sit plausibly among the existing Foundry/Constructor tier
  recipes, not derived from a stated design target. Needs a real playtest pass before being treated
  as final.
- **Hors de portée's `absorptionRatePerSecond`** (`Assets/Data/Research/OutOfLimit.asset`) — raised
  40 → 120 alongside its cost going 2 000 → 20 000, to keep its absorption duration in the same order
  as neighbouring researches at that cost tier. Not requested explicitly; a judgment call bundled
  with the cost change the chantier did ask for.
- **Research network tier/angle for the repositioned nodes** (`advanced_foundry`,
  `memory_architecture`, `ordonnancement_1`, `OutOfLimit`, `com_relay`) — placed by hand to keep each
  child at a greater tier than its new parent(s) and avoid an obvious overlap, following the same
  process the project's own research-tree editor work already used (no automated layout tool
  exists). Not verified visually in the Research panel this pass - flagged the same way the original
  Data Center bay researches were when they were first added.
