# Research

What a research costs, how it progresses, what it unlocks, and where it sits on the network.

Implemented by `ResearchSystem` (`Game.Gameplay.Research`), owned by `GameRuntime`. A CU/absorption
model, not RP and laboratories.

```csharp
public bool HasActiveResearch()
public ResearchDefinition GetActiveResearch()
public float AbsorbedCu
public float GetProgress()
public float GetEstimatedSecondsRemaining()
public IReadOnlyList<ResearchDefinition> GetQueue()
public ResearchSystem(ComputeSystem computeSystem, ResearchCatalog catalog = null)
public bool IsUnlocked(string researchId)
public bool IsBuildingUnlocked(BuildingDefinition building)
public bool IsRecipeUnlocked(RecipeDefinition recipe)
public ResearchDefinition Definition(string researchId)
public IEnumerable<string> GetUnlockedIds()
public void Grant(string researchId)
public bool ArePrerequisitesMet(ResearchDefinition research)
public bool CanQueue(ResearchDefinition research)
public bool Enqueue(ResearchDefinition research)
public bool Dequeue(ResearchDefinition research)
public bool ReorderQueue(int fromIndex, int toIndex)
public void CancelActive()
public void Tick(float deltaTime)
public event Action<string> ResearchCompleted
```

## 1. Duration is a consequence, never a setting

A research declares a total cost (`CuCost`) and an absorption ceiling (`AbsorptionRatePerSecond`), never
a duration. The duration follows: `cost / min(ceiling, what the reserve can currently give)`.

`ResearchSystem` holds a constructor-injected `ComputeSystem` and draws from it every tick through
`SpendUpTo` - never more than the research's own ceiling, never more than the reserve holds. Progress
(`AbsorbedCu`) is a running total that is never rolled back: at zero reserve the draw for that tick is
simply zero, which is what makes "pause without loss" a consequence of the model rather than a special
case to implement.

One active research at a time; everything else waits in a reorderable queue. `Enqueue` starts one
immediately if nothing is active, otherwise appends; either way it first checks `CanQueue` - not
unlocked, not already active or queued, every prerequisite met - and **never** CU availability, which is
a precondition to progressing, not to queueing. When the active research completes, the next is pulled
off the head of the queue on the following tick. `CancelActive` abandons it, discarding its absorbed CU -
the same "switching abandons the cycle without refunding" precedent a production building already sets.

A research may require any number of others (`Prerequisites`, a list). `ArePrerequisitesMet` is the
read-only form the UI uses to show *why* a row is unavailable instead of only greying it out.

## 2. A research carries its effects, and nothing else carries them

`ResearchDefinition.Effects` is a list of `ResearchEffect`, and **a building or a recipe does not name
the research that opens it - the research names it**. The list is closed:

| Effect | Read by |
|---|---|
| `UnlockBuilding` | the placement gate and the building menu |
| `UnlockRecipe` | a production building's offered recipes |
| `ActionRadius` | the Core - a target, highest completed wins |
| `BuildingCap` | the construction service - a target, highest completed wins |
| `DataCenterBayPairs` | the Data Center - a number of pairs, summed |
| `ExtractorItemsPerMinute` | every Extractor - a target, highest completed wins |
| `UnlockOutOfRadiusConstruction` | the construction service - a flag, `Value` unused, never goes back once completed (CONSTRUCTION.md §8) |

Radius, cap and extraction rate are **targets, not increments**, so completion order never matters: a
directive granted late, a save reloaded, a lower research finished after a higher one - none of them
reduce anything. Bays add up, capped by the Data Center.

**No system compares a research id.** An id is how a save names an unlock and how `ResearchCompleted`
reports one, never what an effect is keyed on - renaming a research cannot silently detach its effect.
That was the defect this model replaced: the figures lived in hard-coded tables keyed by string, and a
rename left a research that still existed, still cost, still completed, and did nothing.

`ResearchCatalog` (`Game.Data`) indexes every research the game knows - the tree, its cores, and the
unlocks the Core directives grant, which are researches too - by id and by what it unlocks.
`GameRuntime` builds it once and hands it to the system. `HighestActionRadius(startingRadius)` is the
highest radius target any known research carries: the Core's furthest reach, derived rather than written
down a second time.

A type no research names is not gated; one that is named opens when **any** research naming it
completes.

## 3. The network: cores, branches, position

`ResearchDatabase.GetCores()` lists the roots - Research, Buildings, Armament. **A core is never
bought**: it is an ordinary unlock id granted through `Grant` by whatever powers it, and a research
belongs to a branch by naming its core as a prerequisite. The Data Center grants the cores its own
definition lists once its priming is done; nothing grants the armament core yet.

`GetAll()` excludes the cores, so nothing lists or counts them as research; `Get(id)` still finds them.
A research left out of the database is on no branch and cannot be reached.

**Where a node sits on the network is data, not layout.** `Tier` - its distance from the centre,
counted in rings, on a ring or between two - and `Angle` live on the research's own asset, placed by
hand. `ResearchNetworkPlacement` (`Game.Data`) is the one conversion between those two numbers and a
position: a ring every step from the centre, the angle counter-clockwise from the right. What draws the
network and what places it read it the same way, so nothing computes, spreads or corrects a position.

`EnlargedDisplay` is a sibling flag, also asset data and also read only by the panel: a research so
marked is drawn at a core's own size instead of an ordinary node's. Cosmetic emphasis only - it does not
make the research a core, does not skip `Prerequisites` or `CuCost`, and `ResearchDatabase.GetCores()`
is unaffected.

`ResearchDatabase` is the id-keyed registry, on the same model as the item and recipe databases - one
asset assigned on `GameRuntime`, `Get(id)` for a lookup, `GetAll()` to enumerate.

## 4. The two validations a tree cannot recover from

`ResearchTreeValidation` (`Game.Data`, pure):

- `FindCycles(researches)` returns every research on a prerequisite cycle, one requiring itself
  included. It can never start, and nothing behind it can.
- `FindUnreachable(cores, researches)` returns every research that can never be unlocked from the
  cores. Read **strictly**: every non-null prerequisite must be a core or reachable itself, and a
  research with no prerequisite is not a root - only the cores are. A research with one prerequisite on
  the tree and one off it has a path to the cores and will still never start, since it needs them all;
  the loose reading would let exactly that case through.

The shipped tree is held to both by a test that reads the assets, and in the editor and development
builds `GameRuntime` runs both at startup and logs an error naming any research they find - so a cycle
created by hand is seen at launch rather than when a run quietly stops progressing.

## 5. Core directives

**A directive (`CoreDirectiveDefinition`) is a bill of materials the Core asks for, not a research and
not a recipe.** A research is bought with CU and chosen from a tree; a directive is a delivery the Core
requests, validated by the player and physically carried by the builder robots. Directives are ordered
by `CoreDirectiveDatabase` and consumed one at a time — the Core panel shows the current one and
nothing once they run out, so adding the next one is adding an asset, not writing a panel.

What it grants, though, is an ordinary unlock id: the Core's directives are researches like any other,
their unlocks are `ResearchDefinition` assets outside the tree, they carry their own effect lists, and
the catalogue includes them. Nothing distinguishes "unlocked by a directive" from "unlocked by a
research".

`CoreDirectiveSystem` owns which directive is current, and what it counts is the aggregate **minus the
Core's own reserve**: a directive asks for material to be brought to the Core, and what already sits in
the hatch beneath it was never brought anywhere. Whether a directive may be validated is asked of that
system, never of a dictionary handed in by a caller - while the decision took its stock from outside, a
caller could hand in the wrong one, and the directive tests did, accepting on stock the haul was then
forbidden to claim.

## 6. What travels in the save

`ResearchActiveId`, `ResearchProgress`, `ResearchQueue` and `ResearchUnlocked` round-trip the active
research, its absorbed CU, the queue and the completed set. `CoreDirectives` carries which directive is
current; absent in an older save, which restores as "the first one" - the state a new game starts in.

Unlocks are stored as **identifiers**, and that has not changed: an id remains how a save names an
unlock. What it stopped being is the key of an effect. The radius and the cap are saved as values
rather than re-derived, so an existing run reloads identically.
