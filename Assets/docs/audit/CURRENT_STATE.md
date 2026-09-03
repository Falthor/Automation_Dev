# Audit — état actuel des systèmes économie / recherche / bâtiments

Document de constat, en lecture seule. Il décrit ce que le code fait au moment de l'audit,
sans recommandation ni proposition de refonte. Toute affirmation portant sur le code est
accompagnée d'un chemin et d'un numéro de ligne. Les points non établis avec certitude sont
regroupés dans la dernière section.

Emplacement : le dépôt n'a pas de dossier `docs/` à la racine ; toute la documentation vit sous
`Assets/docs/`. Ce fichier est donc écrit dans `Assets/docs/audit/CURRENT_STATE.md`.

---

## 1. Arborescence

### 1.1 `Assets/Scripts` (profondeur 3)

```
Assets/Scripts
├── Construction
├── Core
├── Data
├── Gameplay
│   ├── Buildings
│   ├── Compute
│   ├── Inventory
│   ├── Items
│   ├── Power
│   ├── Research
│   ├── Selection
│   ├── Transport
│   └── WorldGeneration
├── Grid
├── Presentation
├── Tests
│   ├── EditMode
│   │   ├── Construction
│   │   ├── Core
│   │   ├── Data
│   │   ├── Gameplay
│   │   ├── Grid
│   │   └── TestSupport
│   └── PlayMode        (dossier présent, aucun fichier .cs)
└── UI
```

Fichiers par dossier :

- `Construction/` : `ConstructionService.cs`
- `Core/` : `Direction.cs`, `GridCoord.cs`
- `Data/` : 24 fichiers — les définitions ScriptableObject (`BuildingDefinition`, `ItemDefinition`,
  `RecipeDefinition`, `ResearchDefinition`, `OreDepositDefinition`, `WorldGenerationSettings`,
  `TerrainGenerationSettings`, une définition par type de bâtiment) plus `ItemDatabase`,
  `RecipeDatabase`, `ItemType`, `ConveyorShapeKind`
- `Gameplay/Buildings/` : 17 fichiers (un runtime par type de bâtiment + `BuildingRuntime`,
  `ProductionBuildingRuntime`, `ComponentInstance`, `ConveyorOrientation`, `CrossFootprint`,
  `ProductionState`)
- `Gameplay/` (autres) : `Compute/ComputeSystem.cs`, `Inventory/Inventory.cs`,
  `Items/PooledItemStock.cs`, `Power/PowerSystem.cs`, `Research/ResearchSystem.cs`,
  `Selection/SelectionRuntime.cs`, `Transport/TransportSystem.cs`,
  `WorldGeneration/WorldGenerator.cs`
- `Grid/` : `DepositRuntime.cs`, `GridRuntime.cs`, `TerrainRuntime.cs`, `TerrainType.cs`
- `Presentation/` : 20 fichiers (vues, caméra, spawners, `GameRuntime.cs`, `FogOfWarView.cs`,
  `ActionRadiusView.cs`)
- `UI/` : 17 fichiers (un contrôleur par panneau + `TopBarController`, `BottomNavController`,
  `BuildingMenuController`, `BuildingSelectionInput`, `HistoryGraphElement`, `BuildingCategory`)
- `Tests/EditMode/` : 19 fichiers de test + `TestSupport/TestDataFactory.cs`

### 1.2 Assets de contenu

| Type | Emplacement | Nombre |
|---|---|---|
| `ItemDefinition` | `Assets/Data/Items/*.asset` | 15 |
| `RecipeDefinition` | `Assets/Data/Recipes/*.asset` | 12 |
| `BuildingDefinition` (sous-classes) | `Assets/Data/Buildings/*.asset` | 13 |
| `CoreDefinition`, `OreDepositDefinition`, `WorldGenerationSettings` | `Assets/Data/World/*.asset` | 5 |
| `ResearchDefinition` | `Assets/Data/Research/*.asset` | 6 |
| Registres | `Assets/Data/ItemDatabase.asset`, `Assets/Data/RecipeDatabase.asset` | 2 |
| Terrain | `Assets/Data/Terrain/*.asset` | 3 |

`ItemDatabase.asset` référence 15 items, `RecipeDatabase.asset` 12 recettes : tous les assets
existants sont enregistrés dans leur registre.

### 1.3 Assembly definitions

| `.asmdef` | Références déclarées |
|---|---|
| `Game.Core` | *(aucune)* |
| `Game.Data` | `Game.Core` |
| `Game.Grid` | `Game.Core`, `Game.Data` |
| `Game.Gameplay` | `Game.Core`, `Game.Data`, `Game.Grid` |
| `Game.Construction` | `Game.Core`, `Game.Data`, `Game.Grid`, `Game.Gameplay` |
| `Game.Presentation` | `Game.Core`, `Game.Data`, `Game.Grid`, `Game.Gameplay`, `Game.Construction`, `Unity.InputSystem` |
| `Game.UI` | `Game.Core`, `Game.Data`, `Game.Grid`, `Game.Gameplay`, `Game.Construction`, `Game.Presentation`, `Unity.InputSystem` |
| `Game.Tests.EditMode` | `Game.Core`, `Game.Data`, `Game.Grid`, `Game.Gameplay`, `Game.Construction`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `nunit.framework.dll` — `includePlatforms: [Editor]`, `defineConstraints: [UNITY_INCLUDE_TESTS]` |
| `Game.Tests.PlayMode` | `Game.Core`, `Game.Data`, `Game.Grid`, `Game.Gameplay`, `Game.Construction`, `Game.Presentation`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `Unity.InputSystem`, `Unity.InputSystem.TestFramework` — **aucun fichier de test dans cet assembly** |

`Game.Tests.EditMode` ne référence ni `Game.Presentation` ni `Game.UI` : aucun test ne peut
aujourd'hui porter sur un contrôleur d'UI ou une vue.

---

## 2. ComputeSystem

### 2.1 Fichier complet

`Assets/Scripts/Gameplay/Compute/ComputeSystem.cs` (51 lignes) :

```csharp
namespace Game.Gameplay.Compute
{
    public sealed class ComputeSystem
    {
        public const float ReserveCap = 25000f;

        const float IncomeWindowSeconds = 5f;

        float _grantedInWindow;
        float _windowTimer;

        public float Reserve { get; private set; } = ReserveCap;

        public float IncomePerSecond { get; private set; }

        public void Grant(float amount)
        {
            if (amount <= 0f) return;

            float before = Reserve;
            Reserve = System.Math.Min(Reserve + amount, ReserveCap);
            _grantedInWindow += Reserve - before;
        }

        public bool CanSpend(float cost) => cost <= Reserve;

        public void Spend(float cost) => Reserve -= cost;

        public void Tick(float deltaTime)
        {
            _windowTimer += deltaTime;
            if (_windowTimer < IncomeWindowSeconds) return;

            IncomePerSecond = _grantedInWindow / _windowTimer;
            _grantedInWindow = 0f;
            _windowTimer = 0f;
        }
    }
}
```

Surface publique complète : `ReserveCap` (const), `Reserve`, `IncomePerSecond`, `Grant`,
`CanSpend`, `Spend`, `Tick`. Aucune autre méthode, aucun évènement, aucune remise à zéro.

### 2.2 `ReserveCap`

`public const float ReserveCap = 25000f;` — `ComputeSystem.cs:12`. C'est une constante de code,
pas un champ d'asset : elle n'est configurable nulle part dans l'éditeur. La réserve démarre
**pleine** (`Reserve { get; private set; } = ReserveCap;`, ligne 20).

### 2.3 Appelants

**`Grant`** (2 appelants de production) :

| Chemin:ligne | Contexte |
|---|---|
| `Assets/Scripts/Gameplay/Buildings/CoreRuntime.cs:54` | `_computeSystem.Grant(_definition.CuOutput);` — dans le bloc périodique de `Tick` |
| `Assets/Scripts/Gameplay/Buildings/DataCenterRuntime.cs:123` | `if (_powerSystem.IsPowered()) _computeSystem.Grant(TotalComputeOutput() * deltaTime);` |

Appels de test : `ComputeSystemTests.cs:42, 63, 64, 76, 89, 101`.

**`CanSpend`** (4 appelants de production) :

| Chemin:ligne | Contexte |
|---|---|
| `ExtractorRuntime.cs:80` | avant de lancer une extraction |
| `LaboratoryRuntime.cs:68` | avant de lancer une conversion carte → RP |
| `PowerplantGazRuntime.cs:66` | avant d'allumer une unité de charbon |
| `ProductionBuildingRuntime.cs:219` | avant de démarrer un cycle de recette |

Appels de test : `ComputeSystemTests.cs:21, 33`.

**`Spend`** (4 appelants de production, exactement les mêmes emplacements + 1) :

| Chemin:ligne | Montant |
|---|---|
| `ExtractorRuntime.cs:81` | `_definition.CuCostPerCycle` |
| `LaboratoryRuntime.cs:69` | `_definition.CuCostPerCycle` |
| `PowerplantGazRuntime.cs:67` | `_definition.CuCostPerCycle` |
| `ProductionBuildingRuntime.cs:228` | `recipe.ComputeCost` |

Appels de test : `ComputeSystemTests.cs:22, 31, 40, 51, 61, 73, 86, 99`,
`DataCenterRuntimeTests.cs:86, 101`, `FoundryRuntimeTests.cs:153`.

**`Tick`** : un seul appelant, `Assets/Scripts/Presentation/GameRuntime.cs:122`
(`Compute.Tick(Time.deltaTime);`).

**Construction du système** : `GameRuntime.cs:82` (`Compute = new ComputeSystem();`). Il est
ensuite passé par constructeur à `ConstructionService` (`GameRuntime.cs:103`), à `WorldGenerator`
(`GameRuntime.cs:100`), puis à chaque runtime de bâtiment construit par `ConstructionService`
(lignes 212, 228, 236, 244, 252, 260, 268, 276).

### 2.4 Existe-t-il un prélèvement continu de CU ?

**Non.** Preuve : `Spend` n'a que 4 sites d'appel de production, listés ci-dessus, et aucun
n'est proportionnel à `deltaTime` :

- les trois runtimes à coût fixe utilisent un drapeau booléen d'idempotence
  (`_cycleCharged` `ExtractorRuntime.cs:31`, `_conversionCharged` `LaboratoryRuntime.cs:25`,
  `_burnCharged` `PowerplantGazRuntime.cs:25`) remis à `false` uniquement à la fin du cycle
  (`ExtractorRuntime.cs:89`, `LaboratoryRuntime.cs:78`, `PowerplantGazRuntime.cs:76`) ;
- `ProductionBuildingRuntime.cs:228` est à l'intérieur du bloc `if (!_crafting)`
  (ligne 205), donc exécuté une seule fois par cycle.

Le seul flux au `deltaTime` du système est un **crédit**, pas un débit :
`DataCenterRuntime.cs:123`, `Grant(TotalComputeOutput() * deltaTime)`.

Corollaire documenté dans le code : `BuildingRuntime.cs:224-231` indique explicitement que
« Compute plays no part here — CU is a reserve spent in one shot when a cycle starts (§10),
never a continuous draw that throttles a building's speed ». Il n'existe donc aucun mécanisme
de bridage par CU : un bâtiment qui ne peut pas payer **ne démarre pas** son cycle, il ne
tourne pas au ralenti.

### 2.5 Comportement de `Grant` au-delà du plafond

`ComputeSystem.cs:26-33`. Un montant `<= 0` est ignoré (retour immédiat). Sinon la réserve est
portée à `min(Reserve + amount, ReserveCap)` : **le surplus est perdu, jamais banké, et aucun
signal n'est émis**. La part réellement créditée (`Reserve - before`) est celle accumulée dans
`_grantedInWindow`, donc `IncomePerSecond` reflète le CU effectivement entré, pas le CU offert.
Deux tests verrouillent ce comportement : `Grant_NeverExceedsCap` et
`IncomePerSecond_ExcludesWhatTheCapDiscarded` (`ComputeSystemTests.cs`).

Conséquence à noter : la réserve démarrant pleine et le Core créditant en continu, le surplus est
perdu en permanence tant que la consommation est inférieure au revenu.

---

## 3. Core

### 3.1 `CuOutput` / `CuOutputIntervalSeconds`

Définis dans la **définition**, pas le runtime : `Assets/Scripts/Data/CoreDefinition.cs`
(champs sérialisés, lus via propriétés). Valeurs actuelles dans
`Assets/Data/World/CoreDefinition.asset` :

```
cuOutput: 3000
cuOutputIntervalSeconds: 5
powerOutputKw: 20
actionRadiusCells: 50
footprintSize: {x: 4, y: 4}
cost: []
```

Lecteurs :

- `Assets/Scripts/Gameplay/Buildings/CoreRuntime.cs:51` (`_definition.CuOutputIntervalSeconds`)
  et `:54` (`_definition.CuOutput`) — le seul consommateur gameplay ;
- `CoreRuntime.cs:57` lit `PowerOutputKw` ;
- `Assets/Scripts/Construction/ConstructionService.cs:393` lit `ActionRadiusCells` ;
- `Assets/Scripts/Gameplay/WorldGeneration/WorldGenerator.cs:31` lit `ActionRadiusCells` ;
- `Assets/Scripts/UI/CorePanelController.cs` affiche le contenu du Core (via
  `CoreRuntime.GetContents()`, `CoreRuntime.cs:39`).

Mécanique exacte (`CoreRuntime.cs:46-58`) : un timer accumule `deltaTime` ; dès qu'il atteint
`CuOutputIntervalSeconds` il est décrémenté de cet intervalle et `Grant(CuOutput)` est appelé.
Le Power, lui, est reporté **en continu** à chaque tick (`ReportSupply(PowerOutputKw)`, ligne 57).
Aucune condition : le Core produit CU et Power sans alimentation, sans carburant, sans recherche.

Le Core possède aussi un inventaire poolé non plafonné (`PooledItemStock(int.MaxValue)`,
`CoreRuntime.cs:25`) qui démarre **vide** ; il accepte des items par toutes les faces
(`CanAcceptInput` ligne 41, sans filtre de direction).

### 3.2 Rayon d'action

**Oui, il existe.** `CoreDefinition.actionRadiusCells = 50`.

- Contrainte de construction : `ConstructionService.IsWithinActionRadius`
  (`ConstructionService.cs:388-404`), appelée depuis `IsPlaceable` (`ConstructionService.cs:326`).
  Chaque cellule du gabarit doit satisfaire `sqrt(dx² + dy²) <= radius`, la distance étant
  mesurée depuis la **cellule d'origine** du Core (`_core.Cell`), pas depuis son centre —
  le Core faisant 4×4, le disque autorisé est donc décentré de 2 cellules.
- Si `_core == null` (test headless), **aucune restriction** (`ConstructionService.cs:390`).
- Contrainte de génération : les gisements sont placés dans ce même rayon
  (`WorldGenerator.cs:87`, `maxDistance = ActionRadiusCells - …`).
- Rendu : `ActionRadiusView` (anneau) initialisé en `GameRuntime.cs:165-168`.

Le rayon est fixe : rien dans le code ne le fait croître (aucune écriture de
`ActionRadiusCells` hors de la définition).

### 3.3 Visibilité / brouillard de guerre

**Oui, un brouillard existe**, mais uniquement en présentation.

`Assets/Scripts/Presentation/FogOfWarView.cs` : quad plein écran avec un shader
`Custom/FogOfWar`, révélant un disque unique centré sur le Core, de rayon
`World.ActionRadiusCells * Grid.CellSize` (`GameRuntime.cs:170-173`). Le quad se recale sur la
caméra chaque `LateUpdate` (lignes 48-60).

Ce brouillard n'a **aucune conséquence gameplay** : il n'existe ni notion de cellule explorée,
ni mémoire de vision, ni source de vision autre que le Core, et aucun système ne consulte
l'état du brouillard. Le commentaire de tête du fichier (lignes 5-9) l'énonce explicitement :
« The Core is the only vision source that exists today, so this is a single static circular
reveal, not a multi-source/explored-memory system. »

---

## 4. ResearchSystem

### 4.1 Fichier complet

`Assets/Scripts/Gameplay/Research/ResearchSystem.cs` (77 lignes) :

```csharp
public sealed class ResearchSystem
{
    const float BaseRate = 1f / 60f;

    readonly HashSet<string> _unlocked = new HashSet<string>();

    int _pendingActiveLabs;
    int _settledActiveLabs;

    public float Rp { get; private set; }
    public ResearchDefinition ActiveResearch { get; private set; }
    public float Progress { get; private set; }

    public event Action<string> ResearchCompleted;

    public void AddRp(float amount) => Rp += amount;

    public bool HasActiveResearch() => ActiveResearch != null;
    public ResearchDefinition GetActiveResearch() => ActiveResearch;
    public float GetProgress() => Progress;
    public int GetActiveLabCount() => _settledActiveLabs;
    public void ReportActiveLab() => _pendingActiveLabs++;

    public bool IsUnlocked(string researchId) => researchId != null && _unlocked.Contains(researchId);

    public bool ArePrerequisitesMet(ResearchDefinition research)
    {
        return research == null || research.RequiresResearch == null || IsUnlocked(research.RequiresResearch.Id);
    }

    public bool Start(ResearchDefinition research)
    {
        if (research == null || ActiveResearch != null || IsUnlocked(research.Id) || Rp < research.Cost) return false;
        if (!ArePrerequisitesMet(research)) return false;

        Rp -= research.Cost;
        ActiveResearch = research;
        Progress = 0f;
        return true;
    }

    public void Tick(float deltaTime)
    {
        _settledActiveLabs = _pendingActiveLabs;
        _pendingActiveLabs = 0;

        if (ActiveResearch == null) return;

        Progress += _settledActiveLabs * BaseRate * deltaTime;
        if (Progress < 1f) return;

        string completedId = ActiveResearch.Id;
        _unlocked.Add(completedId);
        ActiveResearch = null;
        Progress = 0f;
        ResearchCompleted?.Invoke(completedId);
    }
}
```

Point de modèle important, car il n'est pas déductible des valeurs d'assets : le **coût en RP
et la durée sont deux choses indépendantes**. `Start` prélève le coût intégral au lancement
(ligne 54) ; la progression, elle, avance de `N × (1/60) × deltaTime` où `N` est le nombre de
laboratoires ayant reporté au tick précédent (ligne 67). Une recherche dure donc
**60 secondes divisées par le nombre de laboratoires actifs, quel que soit son coût**. Un seul
laboratoire ⇒ 60 s pour n'importe quelle recherche. Verrouillé par les tests
`Tick_OneActiveLab_CompletesAfter60Seconds` et `Tick_TwoActiveLabs_CompletesTwiceAsFast`
(`ResearchSystemTests.cs`).

`Rp` n'a aucun plafond et n'est jamais remis à zéro ; `_unlocked` n'est jamais vidé.
Une seule recherche active à la fois (`ActiveResearch != null` bloque `Start`).

### 4.2 Champs de `ResearchDefinition`

`Assets/Scripts/Data/ResearchDefinition.cs:13-16` :

| Champ | Type | Rôle | Lecteurs |
|---|---|---|---|
| `id` | `string` | Clé stable, seule chose stockée dans `_unlocked` | `ResearchSystem.cs:45, 51, 70`, `ConstructionService.cs:321`, `BuildingMenuController.cs:171, 229, 339`, `ProductionBuildingRuntime.cs:85`, `DataCenterRuntime.cs:58` |
| `displayName` | `string` | Libellé UI | `ResearchPanelController.cs:162`, `TopBarController.cs:214` |
| `cost` | `float` (`Min(0)`) | RP prélevés au lancement | `ResearchSystem.cs:51, 54`, `ResearchPanelController.cs:143` |
| `requiresResearch` | `ResearchDefinition` | Prérequis unique, `null` = disponible d'emblée | `ResearchSystem.cs:45`, `ResearchPanelController.cs:162` |

Il n'y a **pas** de champ décrivant ce que la recherche débloque : le lien est inversé, ce sont
`BuildingDefinition.UnlockResearch` et `RecipeDefinition.UnlockResearch` qui pointent vers la
recherche. Il n'y a pas non plus de durée, de catégorie, de description, ni d'icône.

### 4.3 Consommateurs

**`IsUnlocked`**

| Chemin:ligne | Usage |
|---|---|
| `ConstructionService.cs:321` | refuse la pose d'un bâtiment dont `UnlockResearch` n'est pas acquise |
| `ProductionBuildingRuntime.cs:85` | exclut une recette verrouillée de `GetRecipeIds()` |
| `DataCenterRuntime.cs:58` | ajoute un slot CPU si `extra_cpu_slot` est déjà acquise à la construction |
| `BuildingMenuController.cs:171` | masque un bâtiment verrouillé de la liste |
| `BuildingMenuController.cs:229, 339` | style/état « locked » d'une carte |
| `ResearchPanelController.cs:140` | état « terminé » d'une ligne |
| `ResearchSystem.cs:45, 51` | usage interne |

**`ArePrerequisitesMet`** : `ResearchSystem.cs:52` (interne) et `ResearchPanelController.cs:143, 162`.
Aucun autre consommateur.

**`RequiresResearch`** : `ResearchSystem.cs:45`, `ResearchPanelController.cs:162`.

**`ResearchCompleted`** (évènement) : un seul abonné dans tout le projet —
`DataCenterRuntime.cs:61` (abonnement), `:73` (désabonnement dans `OnUnregistered`),
`:65-68` (handler, qui ne réagit qu'à `extra_cpu_slot`). Testé par
`ResearchCompleted_EventFires_WithCompletedId` et
`OnUnregistered_StopsReactingToFutureResearchCompletions`.

**`BuildingDefinition.UnlockResearch`** (`BuildingDefinition.cs:44`) : lu par
`ConstructionService.cs:321` et `BuildingMenuController.cs:171, 229, 339`.
Deux assets seulement l'utilisent : `AssemblerDefinition` (→ `cpu_assembler`) et
`DataCenterDefinition` (→ `datacenter`).

**`RecipeDefinition.UnlockResearch`** (`RecipeDefinition.cs:41`) : lu uniquement par
`ProductionBuildingRuntime.cs:85`. Trois assets l'utilisent : `Screw_Recipe` (→ `screw`),
`Printed_Circuit_Board_Recipe` (→ `circuit_board`), `Memory_MK1_Recipe` (→ `memoire`).

### 4.4 Assets `ResearchDefinition`

| Fichier | `id` | `displayName` | `cost` (RP) | `requiresResearch` | Débloque |
|---|---|---|---|---|---|
| `Research/screw.asset` | `screw` | Vis | 10 | — | recette `Screw` |
| `Research/circuit_board.asset` | `circuit_board` | Circuit Imprime | 50 | `screw` | recette `Printed_Circuit_Board` |
| `Research/cpu_assembler.asset` | `cpu_assembler` | CPU Assembler | 100 | `circuit_board` | bâtiment `Assembler` |
| `Research/memoire.asset` | `memoire` | Memoire | 100 | — | recette `Memory_MK1` |
| `Research/datacenter.asset` | `datacenter` | Data Center | 200 | — | bâtiment `Data Center` |
| `Research/extra_cpu_slot.asset` | `extra_cpu_slot` | Extra CPU Slot | 20 | — | 1 slot CPU supplémentaire au Data Center (`DataCenterRuntime.cs:67`) |

### 4.5 Panneau UI de recherche

Oui. Fichiers concernés :

- `Assets/UI/ResearchPanel.uxml` — racine `ResearchPanelRoot`, classes `overlay-root hidden`,
  panneau `panel panel-accent research-panel`, conteneur `ResearchList`, label `ResearchRpLabel`,
  bouton `ResearchCloseButton`
- `Assets/UI/GameUI.uss` — feuille de style unique, partagée par tous les panneaux
- `Assets/Scripts/UI/ResearchPanelController.cs` — `PanelName = "research"`, ouvert depuis la
  Bottom Nav via `SelectionRuntime.GlobalPanelChanged`

Fonctionnement : les lignes sont construites une seule fois à l'ouverture et conservées dans
`_rows` (`ResearchPanelController.cs:36`) ; seul leur état est rafraîchi par frame. Le
commentaire lignes 32-35 documente pourquoi (un `Button` reconstruit chaque frame ne termine
jamais son clic). Le statut affiché vaut `REQUIERT : {DisplayName}` quand le prérequis manque
(`:162`), et une ligne est « verrouillée » si une recherche est déjà active, si le prérequis
manque, ou si les RP sont insuffisants (`:143`).

**La liste des recherches offertes n'est pas déduite des assets** : c'est un tableau sérialisé
`[SerializeField] ResearchDefinition[] researches` (`ResearchPanelController.cs:26`), rempli
dans la scène. Dans `Assets/Scenes/Bootstrap.unity` (bloc `&60635895`) il ne contient que
**trois** entrées : `screw`, `circuit_board`, `cpu_assembler`. `memoire`, `datacenter` et
`extra_cpu_slot` ne sont donc **atteignables par aucun moyen en jeu** aujourd'hui.

---

## 5. Laboratoire et Data Card

### 5.1 Références au Laboratoire

| Chemin | Nature |
|---|---|
| `Assets/Scripts/Data/LaboratoryDefinition.cs` (33 l.) | définition complète |
| `Assets/Scripts/Gameplay/Buildings/LaboratoryRuntime.cs` (82 l.) | runtime complet |
| `Assets/Scripts/UI/LaboratoryPanelController.cs` (89 l.) | panneau |
| `Assets/UI/LaboratoryPanel.uxml` (24 l.) | markup |
| `Assets/Scripts/Tests/EditMode/Gameplay/Buildings/LaboratoryRuntimeTests.cs` (95 l.) | 5 tests |
| `Assets/Data/Buildings/LaboratoryDefinition.asset` | asset |
| `Assets/Scripts/Construction/ConstructionService.cs:266-272` | branche de pose |
| `Assets/Scripts/UI/BuildingSelectionInput.cs:96` | branche de sélection |
| `Assets/Scripts/Presentation/BuildingSpawner.cs` | mention en commentaire |
| `Assets/Scripts/Presentation/ConstructionInputAdapter.cs` | mention en commentaire |
| `Assets/Scripts/Data/BuildingDefinition.cs:114` | mention en commentaire (`HasInputArrows`) |
| `Assets/Scripts/Gameplay/Buildings/BuildingRuntime.cs:200` | mention en commentaire |
| `Assets/Scripts/Gameplay/Research/ResearchSystem.cs:7` | mention en commentaire |
| `Assets/Scripts/UI/ResearchPanelController.cs:13` | mention en commentaire |
| `Assets/Scripts/Tests/EditMode/TestSupport/TestDataFactory.cs:~170` | fabrique `NewLaboratory` |
| `Assets/Scenes/Bootstrap.unity` | GameObject `LaboratoryPanelController` |

Il n'y a **pas de prefab** : les bâtiments sont instanciés par code (`BuildingSpawner`), pas
depuis un prefab. Le Laboratoire n'est **pas enregistré dans une base de données de bâtiments** :
il n'existe pas de `BuildingDatabase` ; la liste des bâtiments constructibles est un tableau
sérialisé du `BuildingMenuController` dans la scène.

Modèle réel (`LaboratoryRuntime.cs:49-80`) : stock interne de `Data_Card` plafonné à
`MaxCardStack` (100) ; toutes les `cardConvertIntervalSeconds` (2 s) une carte est consommée et
`RpPerCard` (2) RP sont ajoutés ; 250 CU sont prélevés au lancement de chaque conversion
(lignes 64-71) ; `ReportActiveLab()` est appelé à chaque tick **seulement si une recherche est
active** (ligne 55), tandis que la conversion carte → RP tourne indépendamment de cela
(test `GeneratesRp_IndependentOfWhetherAResearchIsActive`).

### 5.2 Références à Data Card

| Chemin | Nature |
|---|---|
| `Assets/Data/Items/Data_Card.asset` | item (`id: Data_Card`, `displayName: Magnetic Card`, type `Component`) |
| `Assets/Data/Recipes/Data_Card_Recipe.asset` | recette : 3 `Iron_Plate` + 2 `copper_wire` → 1, 10 s, 500 CU, non gatée |
| `Assets/Data/Buildings/FactoryDefinition.asset` | `recipeIds` contient `Data_Card` |
| `Assets/Data/Buildings/LaboratoryDefinition.asset` | `cardItem` pointe dessus |
| `Assets/Scripts/Data/LaboratoryDefinition.cs:12, 21` | champ `cardItem` |
| `Assets/Scripts/Data/FactoryDefinition.cs` | mention en commentaire |
| `Assets/Scripts/Gameplay/Buildings/LaboratoryRuntime.cs:42, 47` | seul consommateur runtime |
| `Assets/Scripts/UI/LaboratoryPanelController.cs` | affichage |
| Tests | `LaboratoryRuntimeTests.cs`, `TestDataFactory.cs` |

La Data Card n'a **aucun autre consommateur** : elle n'est ingrédient d'aucune autre recette,
n'entre dans le coût d'aucun bâtiment, et n'est acceptée en entrée par aucun autre bâtiment
que le Laboratoire.

### 5.3 Volume de suppression si les deux disparaissent

Fichiers supprimables intégralement (~323 lignes de code + 2 assets + 1 asset de recette) :

| Fichier | Lignes |
|---|---|
| `LaboratoryRuntime.cs` | 82 |
| `LaboratoryDefinition.cs` | 33 |
| `LaboratoryPanelController.cs` | 89 |
| `LaboratoryPanel.uxml` | 24 |
| `LaboratoryRuntimeTests.cs` | 95 |
| **Total** | **323** |

Assets : `LaboratoryDefinition.asset`, `Data_Card.asset`, `Data_Card_Recipe.asset` (+ leurs
`.meta`), plus une entrée à retirer de `ItemDatabase.asset`, une de `RecipeDatabase.asset`, une
de `FactoryDefinition.recipeIds`.

Modifications ponctuelles requises ailleurs (estimation ~40 lignes touchées) :
`ConstructionService.cs:266-272` (branche de pose), `BuildingSelectionInput.cs:96` (branche),
`TestDataFactory.NewLaboratory`, le GameObject `LaboratoryPanelController` dans
`Bootstrap.unity`, l'entrée du Laboratoire dans le tableau du `BuildingMenuController` de la
scène, plus le retrait de `ReportActiveLab`/`GetActiveLabCount`/`BaseRate` de `ResearchSystem`
si le modèle de progression change (voir §14).

### 5.4 Répartition CU/RP actuelle de la chaîne recherche

Fait mesurable, utile au rééquilibrage : 1 Data Card coûte 500 CU (fabrication) + 250 CU
(conversion) = 750 CU pour 2 RP, soit **375 CU/RP**. La durée d'une recherche, elle, ne dépend
pas des RP (§4.1).

---

## 6. DataCenter

### 6.1 Fichier runtime

`Assets/Scripts/Gameplay/Buildings/DataCenterRuntime.cs` (209 lignes). Constantes de tête
(lignes 19-26) :

```csharp
const int InitialCpuSlots = 4;
const int InitialMemorySlots = 4;
const float StabilityInterval = 5f;
const float WearInterval = 30f;
const float ReplacementDuration = 5f;
const string ExtraCpuSlotResearchId = "extra_cpu_slot";
const string CpuItemId = "cpu_mkI";
const string MemoryItemId = "Memory_MK1";
```

Aucune de ces valeurs n'est configurable en asset.

### 6.2 Que signifient « composants installés »

Le modèle est réel et typé, ce n'est pas un simple inventaire :

- **Deux listes de slots typés** : `_cpuSlots` et `_memorySlots`, initialisées à 4 entrées
  `null` chacune (`:56-57`). Un slot CPU n'accepte que `cpu_mkI`, un slot mémoire que
  `Memory_MK1` (`InstallInto`, `:127-137`, appelé avec l'id correspondant `:91-92`).
- **Une capacité extensible par recherche** : `extra_cpu_slot` ajoute un slot CPU, jamais un
  slot mémoire (`:58` à la construction, `:65-68` à chaud). Pas de limite au nombre d'ajouts.
- **Un inventaire d'entrée séparé** : `PooledItemStock(definition.MaxStackPerItem)` (`:54`),
  plafond 10 par item dans l'asset. Les items arrivent là, puis sont installés dans le premier
  slot libre à chaque tick ; s'il n'y a plus de slot, ils **restent dans l'inventaire**
  (`:132`, comportement de bourrage volontaire).
- **De l'usure, oui** : chaque `ComponentInstance` porte `Wear` (100 % à l'installation, −1
  point toutes les 30 s via `DecayWear`, plancher à 0), `Stability` (fixée à 80, jamais
  modifiée) et `EffectivePerformance` (0..1, retiré tous les 5 s :
  80 % de chance d'être à 1, sinon un tirage uniforme entre 0,70 et 1,00 —
  `ComponentInstance.cs:46-49`).
- **Un cycle de remplacement** : dès que `Wear <= 5 %` le slot passe `IsReplacing`
  (`:164-168`), ce qui force sa sortie CU **et** sa consommation Power à 0
  (`ComponentInstance.cs:59, 62`). Après 5 s, si une pièce de rechange est présente en entrée
  elle est installée, sinon le slot est vidé (`:181-189`). Un composant dont l'usure atteint 0
  avant la fin du délai est purement et simplement supprimé (`:172-176`).

Aucune notion de niveau, de qualité variable à l'installation, ni de réparation.

### 6.3 Production de CU

`TotalComputeOutput()` (`:193-199`) somme `slot.EffectiveCu()` sur les deux listes.
`EffectiveCu()` vaut `BaseCu × EffectivePerformance`, ou 0 pendant un remplacement
(`ComponentInstance.cs:59`). `BaseCu` est figé à l'installation depuis `ItemDefinition.CuOutput`
(`ComponentInstance.cs:41`) : une modification ultérieure du registre n'affecte pas les
composants déjà posés.

C'est un **taux CU/s**, crédité à chaque frame au prorata du tick :
`Grant(TotalComputeOutput() * deltaTime)` (`:123`). Fréquence de crédit = fréquence de frame,
contrairement au Core qui crédite par paliers de 5 s.

Valeurs actuelles : `cpu_mkI` → 1000 CU/s et 2 kW ; `Memory_MK1` → 500 CU/s et 1 kW.
Un Data Center plein (4 CPU + 4 mémoires) produit donc nominalement 6000 CU/s pour 12 kW,
soit dix fois le revenu du Core, mais l'apport net est plafonné par `ReserveCap` (§2.5).

### 6.4 Dépendance au Power

Deux mécanismes distincts, volontairement différents (commentaire `:119-122`) :

1. **La sortie CU** est testée directement : `if (_powerSystem.IsPowered())` (`:123`).
   Hors alimentation, **aucun CU n'est crédité ce tick**, immédiatement.
2. **Les timers d'usure/stabilité/remplacement** passent par le pipeline commun
   `ComputeEffectivePerformance(_previousPowerDemand, powerActive: true, _powerSystem)`
   (`:97`, implémenté `BuildingRuntime.cs:224-233`), qui renvoie 0 hors alimentation :
   `effectiveDelta` vaut alors 0 et **tous les timers gèlent** au lieu de progresser.

La demande reportée est celle calculée au tick **précédent** (`_previousPowerDemand`, écrit
`:124`) — décalage d'une frame identique au reste du système. `TotalPowerDemand()` (`:201-207`)
ne somme que les composants installés : un Data Center vide ne consomme rien, et
`DataCenterDefinition` n'a d'ailleurs pas de champ `powerDemandKw`
(`BuildingDefinition.PowerDemandKw` reste à 0 par défaut, cf. commentaire
`BuildingDefinition.cs:50-53`).

L'installation, elle, n'est pas gelée : `InstallInto` est appelé avant le calcul de performance
(`:91-92`), donc un Data Center non alimenté continue d'absorber les composants livrés.

### 6.5 Panneau UI

Oui : `Assets/UI/DataCenterPanel.uxml` (racine `DataCenterPanelRoot`, classe
`overlay-root overlay-root-right`, panneau `panel panel-accent datacenter-panel`) et
`Assets/Scripts/UI/DataCenterPanelController.cs`. Ouvert par
`BuildingSelectionInput.cs:100` (`occupant is DataCenterRuntime`). Le contrôleur est présent
dans `Bootstrap.unity`.

Le Data Center n'est cependant **pas constructible en jeu** aujourd'hui : sa pose exige la
recherche `datacenter` (`DataCenterDefinition.asset`, `unlockResearch`), qui n'est pas dans la
liste du panneau de recherche (§4.5), et son coût réclame des `Memory_MK1` et
`mechanical_component` (§7.4).

---

## 7. Définitions

### 7.1 `BuildingDefinition` (`Assets/Scripts/Data/BuildingDefinition.cs`)

Classe **abstraite**, base de tous les bâtiments.

| Champ / membre | Type | Ligne | Rôle | Lecteurs |
|---|---|---|---|---|
| `id` | `string` | 8 | identifiant | peu utilisé (diagnostic) |
| `displayName` | `string` | 9 | libellé UI | `BuildingMenuController`, panneaux |
| `footprintSize` | `Vector2Int` | 10 | emprise rectangulaire | `ConstructionService` (`SetOccupantFootprint`), `BuildingRuntime.ComputeInputCells`, `BuildingSpawner` |
| `placeholderColor` | `Color` | 11 | couleur du sprite procédural de secours | `BuildingSpawner`, `ConstructionInputAdapter` |
| `unlockResearch` | `ResearchDefinition` | 12 | verrou de pose | `ConstructionService.cs:321`, `BuildingMenuController.cs:171, 229, 339` |
| `sprite` | `Sprite` | 13 | art réel | `BuildingSpawner`, `ConstructionInputAdapter`, `BuildingMenuController` |
| `animationFrames` | `Sprite[]` | 14 | flipbook | `SpriteFlipbook` via `BuildingSpawner` |
| `animationFps` | `float` | 15 | vitesse du flipbook | idem |
| `cost` | `RecipeIngredient[]` | 16 | coût de construction | `ConstructionService.CanAfford/PayCost/RefundCost` (75, 102, 142) |
| `RenderOverscan` | `virtual float` | 41 | débord de rendu | `BuildingSpawner` |
| `PowerDemandKw` | `virtual float` | 54 | demande Power fixe (0 par défaut) | runtimes + `BuildingMenuController` (aperçu) |
| `CuCostPerCycle` | `virtual float` | 62 | CU one-shot par cycle (0 par défaut) | `ExtractorRuntime.cs:80-81`, `LaboratoryRuntime.cs:68-69`, `PowerplantGazRuntime.cs:66-67` |
| `FootprintCells` | `virtual Vector2Int[]` | 71 | cellules réellement occupées | `ConstructionService` (occupation, démolition, rayon) |
| `HasOutputArrow` | `virtual bool` | 106 | flèche de sortie | `BuildingSpawner`, `ConstructionInputAdapter` |
| `HasInputArrows` | `virtual bool` | 117 | flèches d'entrée **et** restriction des cellules d'entrée | `BuildingRuntime.GetInputCells`, vues |

Les sous-classes ajoutent leurs propres champs (`recipeIds`, `acceptedItemIds`,
`maxStackPerItem`, etc.) et surchargent ces `virtual`.

### 7.2 `RecipeDefinition` (`Assets/Scripts/Data/RecipeDefinition.cs`)

| Champ | Type | Ligne | Rôle | Lecteurs |
|---|---|---|---|---|
| `id` | `string` | 27 | **toujours l'id de l'item produit** (convention) | `RecipeDatabase`, `ProductionBuildingRuntime` (sortie, `:241`) |
| `ingredients` | `RecipeIngredient[]` | 28 | entrées consommées par cycle | `ProductionBuildingRuntime.cs:229-232`, `GetRequiredIngredients()` |
| `outputAmount` | `int` (`Min 1`) | 29 | quantité produite | `ProductionBuildingRuntime.cs:213, 241` |
| `timeSeconds` | `float` (`Min 0.01`) | 30 | durée du cycle | `ProductionBuildingRuntime.cs:239` |
| `computeCost` | `float` (`Min 0`) | 31 | CU one-shot au démarrage | `ProductionBuildingRuntime.cs:219, 228` |
| `unlockResearch` | `ResearchDefinition` | 32 | verrou | `ProductionBuildingRuntime.cs:85` |

`RecipeIngredient` (struct, lignes 8-15) : `item` (`ItemDefinition`) + `amount` (`int`). La même
structure sert aux coûts de construction et au stock de départ.

### 7.3 `ItemDefinition` (`Assets/Scripts/Data/ItemDefinition.cs`)

| Champ | Type | Ligne | Rôle | Lecteurs |
|---|---|---|---|---|
| `id` | `string` | 13 | clé de tous les contrats | partout |
| `type` | `ItemType` | 14 | `Ore` / `Ingot` / `Component` | `FoundryRuntime.cs:32` uniquement |
| `displayName` | `string` | 15 | libellé UI | panneaux |
| `icon` | `Sprite` | 16 | icône UI | `StoragePanelController`, `ProductionPanelController` |
| `fallbackColor` | `Color` | 17 | couleur du sprite d'item sur convoyeur | `ItemVisualSync`, `ProceduralSpriteFactory` |
| `cuOutput` | `float` (`Min 0`) | 18 | CU/s **une fois installé en Data Center** | `ComponentInstance.cs:41` |
| `powerKw` | `float` (`Min 0`) | 19 | kW une fois installé en Data Center | `ComponentInstance.cs:42` |

`ItemType` (`Assets/Scripts/Data/ItemType.cs`) : `Ore` n'est utilisé que par le filtre de la
Fonderie ; `Coal_ore` est délibérément classé `Component` pour que la Fonderie le refuse.

### 7.4 Tableau des assets

**Items** (`Assets/Data/Items/`) — 15 assets :

| id | displayName | type | cuOutput | powerKw |
|---|---|---|---|---|
| `iron_ore` | Minerai de fer | Ore | 0 | 0 |
| `copper_ore` | Minerai de cuivre | Ore | 0 | 0 |
| `Coal_ore` | Charbon | Component | 0 | 0 |
| `Iron_Ingot` | Iron Ingot | Ingot | 0 | 0 |
| `copper_Ingot` | Lingot de cuivre | Ingot | 0 | 0 |
| `Steel` | Acier | Ingot | 0 | 0 |
| `Iron_Plate` | Iron Plate | Component | 0 | 0 |
| `copper_wire` | Fil de cuivre | Component | 0 | 0 |
| `Gear` | Engrenage | Component | 0 | 0 |
| `Screw` | Vis | Component | 0 | 0 |
| `Printed_Circuit_Board` | Circuit Board | Component | 0 | 0 |
| `Data_Card` | Magnetic Card | Component | 0 | 0 |
| `mechanical_component` | Composant mecanique | Component | 0 | 0 |
| `cpu_mkI` | CPU Mk.I | Component | **1000** | **2** |
| `Memory_MK1` | Memoire Mk.I | Component | **500** | **1** |

Tous ont une icône assignée.

**Recettes** (`Assets/Data/Recipes/`) — 12 assets :

| id | Ingrédients | Sortie | `timeSeconds` | `computeCost` | `unlockResearch` | Bâtiment(s) déclarant la recette |
|---|---|---|---|---|---|---|
| `Iron_Ingot` | 1 `iron_ore` | 1 | 2 | 100 | — | Foundry |
| `copper_Ingot` | 1 `copper_ore` | 1 | 2 | 100 | — | Foundry |
| `Gear` | 1 `Iron_Ingot` | 2 | 2 | 200 | — | Factory |
| `Iron_Plate` | 2 `Iron_Ingot` | 2 | 5 | 300 | — | Factory |
| `copper_wire` | 2 `copper_Ingot` | 3 | 3 | 300 | — | Factory |
| `Screw` | 1 `Iron_Ingot` + 2 `copper_Ingot` | 1 | 3 | 200 | `screw` | Factory |
| `Printed_Circuit_Board` | 2 `Screw` + 3 `copper_wire` | 1 | 10 | 1000 | `circuit_board` | Factory |
| `Data_Card` | 3 `Iron_Plate` + 2 `copper_wire` | 1 | 10 | 500 | — | Factory |
| `Memory_MK1` | 4 `Printed_Circuit_Board` + 5 `Iron_Ingot` | 1 | 3 | 1500 | `memoire` | **Factory et Assembler** |
| `Steel` | 3 `iron_ore` + 2 `Coal_ore` | 1 | 6 | 600 | — | Advanced Foundry |
| `cpu_mkI` | 5 `copper_Ingot` + 3 `Gear` + 6 `Printed_Circuit_Board` | 2 | 5 | 2000 | — | Assembler |
| `mechanical_component` | 1 `cpu_mkI` + 1 `Memory_MK1` + 2 `Iron_Plate` | 1 | 6 | 600 | — | Assembler |

**Bâtiments** (`Assets/Data/Buildings/` et `Assets/Data/World/`) — 14 assets :

| id | Emprise | Coût | Power | CU/cycle | Verrou | Champs propres |
|---|---|---|---|---|---|---|
| `core` | 4×4 | — | +20 kW (fourni) | +3000 CU / 5 s | — | `actionRadiusCells: 50` |
| `extractor` | 2×2 | 5 `Iron_Plate` | 1 kW | 50 | — | `extractionIntervalSeconds: 2`, `itemsPerCycle: 1` |
| `foundry` | 3×3 | 5 `Iron_Plate` + 5 `copper_wire` | 2 kW | 0 | — | `maxStackPerItem: 20`, `intakeIntervalSeconds: 1`, `recipeIds: [Iron_Ingot, copper_Ingot]` |
| `factory` | 4×4 | 10 `Iron_Plate` + 10 `Gear` | 3 kW | 0 | — | `maxStackPerItem: 20`, 7 `recipeIds`, `acceptedItemIds: [Iron_Ingot, copper_Ingot, Iron_Plate, copper_wire, Screw]` |
| `fonderie_avancee` | 3×3 | 10 `Iron_Plate` | 4 kW | 0 | — | `maxStackPerItem: 100`, `recipeIds: [Steel]`, `acceptedItemIds: [iron_ore, Coal_ore]` |
| `assembler` | 4×4 | 2 `Printed_Circuit_Board` + 10 `Screw` + 5 `Iron_Plate` | 4 kW | 0 | `cpu_assembler` | `maxStackPerItem: 100`, `recipeIds: [cpu_mkI, mechanical_component, Memory_MK1]`, 7 `acceptedItemIds` |
| `laboratory` | 3×3 | 10 `Iron_Plate` + 5 `copper_wire` | 3 kW | 250 | — | `cardItem: Data_Card`, `maxCardStack: 100`, `cardConvertIntervalSeconds: 2`, `rpPerCard: 2` |
| `powerplant_gaz` | 3×3 | 10 `Iron_Plate` | −2 kW (auto) / +10 kW | 150 | — | `fuelItem: Coal_ore`, `maxFuelStack: 20`, `fuelCycleTimeSeconds: 10` |
| `datacenter` | 2×2 | 50 `Steel` + 40 `mechanical_component` + 20 `cpu_mkI` + 30 `Memory_MK1` | 0 (dépend des composants) | 0 | `datacenter` | `maxStackPerItem: 10`, `acceptedItemIds: [cpu_mkI, Memory_MK1]` |
| `storage` | 1×1 | 10 `Iron_Plate` | 0 | 0 | — | — |
| `ConveyorStraight` | 1×1 | 1 `Iron_Plate` | 0 | 0 | — | `defaultShape: Straight` |
| `ConveyorCorner` | 1×1 | 1 `Iron_Plate` | 0 | 0 | — | `defaultShape: Corner` |
| `splitter` | 3×3 (croix) | 1 `Iron_Plate` | 0 | 0 | — | `artNativeEntrySide: 3` |
| `crossroad` | 3×3 (croix) | 1 `Iron_Plate` | 0 | 0 | — | — |

**Gisements** (`Assets/Data/World/`) — les trois sont identiques hors item et couleur :
emprise 2×2, `initialQuantity: 1000`, sprite assigné, coût vide.

**`WorldGenerationSettings.asset`** : `resourceSeed: 12345`, `startingStock` =
150 `Iron_Plate` + 50 `copper_wire` + 30 `Gear`.

---

## 8. Extracteur

### 8.1 Cadence d'extraction

Champ de **définition** : `extractionIntervalSeconds` (`ExtractorDefinition.cs:9`, `Min(0.01)`,
valeur 2) et `itemsPerCycle` (`:10`, `Min(1)`, valeur 1). Le runtime les lit
`ExtractorRuntime.cs:86` et `:92`.

Une seule constante de code intervient : `InternalStorageCapacity = 20`
(`ExtractorRuntime.cs:20`), tampon interne non configurable. Tampon plein ⇒ la production
s'arrête complètement et le Power n'est plus demandé (`:66-73`, `powerActive: !bufferFull`).

### 8.2 Application de `CuCostPerCycle`

`ExtractorRuntime.cs:78-83`, avant l'avance du timer :

```csharp
if (!_cycleCharged)
{
    if (!_computeSystem.CanSpend(_definition.CuCostPerCycle)) return;
    _computeSystem.Spend(_definition.CuCostPerCycle);
    _cycleCharged = true;
}
```

Réserve insuffisante ⇒ `return` : le timer **ne progresse pas du tout**, il ne redémarre pas non
plus (il conserve sa valeur). Le drapeau retombe à `false` à la complétion (`:89`), donc chaque
extraction est facturée exactement une fois. Même schéma exact pour le Laboratoire (`:64-71`) et
la Centrale (`:62-69`).

### 8.3 Bridage / multiplicateur global

Il n'existe **aucun multiplicateur de vitesse configurable, ni global ni par bâtiment**.

Le seul modificateur de cadence du projet est binaire et vient du Power :
`ComputeEffectivePerformance` (`BuildingRuntime.cs:224-233`) renvoie **1 ou 0**, jamais une
valeur intermédiaire, et l'appelant multiplie son `deltaTime`. Les appelants sont
`ExtractorRuntime.cs:71`, `LaboratoryRuntime.cs:53`, `ProductionBuildingRuntime.cs:176`,
`DataCenterRuntime.cs:97`.

Le seul facteur continu du projet est `ComponentInstance.EffectivePerformance` (0,70..1,00),
mais il ne s'applique qu'à la **sortie CU** d'un composant de Data Center, jamais à une cadence
de production (`ComponentInstance.cs:59`).

Les seules autres cadences sont des constantes ou champs fixes :
`TransportSystem.ConveyorSpeedCellsPerSecond = 1.5f` (`TransportSystem.cs:33`, constante),
`BuildingRuntime.PushIntervalSeconds => 1f` (`BuildingRuntime.cs:53`, **jamais surchargée** —
vérifié : aucune occurrence de `override float PushIntervalSeconds` dans le projet), et
`FoundryDefinition.intakeIntervalSeconds` (1 s, `FoundryRuntime.cs:42`).

---

## 9. Power

### 9.1 Bâtiments déclarant une demande ou une fourniture

| Bâtiment | Valeur | Site d'appel | Condition |
|---|---|---|---|
| Core | **+20 kW** | `CoreRuntime.cs:57` | inconditionnel, chaque tick |
| Centrale gaz | **+10 kW** | `PowerplantGazRuntime.cs:71` | seulement si carburant **et** 150 CU payés |
| Centrale gaz (auto-conso) | **−2 kW** | `PowerplantGazRuntime.cs:53` | inconditionnel, même sans carburant |
| Extracteur | −1 kW | `ExtractorRuntime.cs:71` via `ComputeEffectivePerformance` | seulement si le tampon n'est pas plein |
| Fonderie | −2 kW | `ProductionBuildingRuntime.cs:176` | seulement si l'état était `Producing` au tick précédent |
| Factory | −3 kW | idem | idem |
| Fonderie avancée | −4 kW | idem | idem |
| Assembleur | −4 kW | idem | idem |
| Laboratoire | −3 kW | `LaboratoryRuntime.cs:53` (`powerActive: true`) | **inconditionnel** |
| Data Center | somme des composants (2 kW/CPU, 1 kW/mémoire) | `DataCenterRuntime.cs:97` | demande du tick précédent |
| Storage, convoyeurs, splitter, crossroad | 0 | — | — |

### 9.2 Où le gel par manque d'énergie est appliqué

Un seul endroit : `BuildingRuntime.ComputeEffectivePerformance` (`BuildingRuntime.cs:224-233`).
Il reporte la demande puis renvoie `0f` si `!power.IsPowered()`. Chaque appelant multiplie son
propre `deltaTime` par ce retour avant d'avancer un timer :

- `ExtractorRuntime.cs:71` → `:85` (`_productionTimer += deltaTime * performance`)
- `LaboratoryRuntime.cs:53` → `:73`
- `ProductionBuildingRuntime.cs:176-180` (gèle la machine à états entière, sans perdre les
  ingrédients ni le CU déjà consommés — test `Unpowered_FreezesProductionProgress…`)
- `DataCenterRuntime.cs:97-98`

`PowerSystem` lui-même (`Assets/Scripts/Gameplay/Power/PowerSystem.cs`) est binaire :
`IsPowered() => SettledDemand <= SettledSupply` (`:22`), aucune dégradation partielle, aucune
temporisation, récupération immédiate. `Settle()` (`:25-31`) est appelé une fois par frame
depuis `GameRuntime.cs:121`, avant les ticks, d'où un décalage volontaire d'une frame.

### 9.3 La Centrale gaz est-elle le seul producteur ?

**Non** : le Core fournit 20 kW en permanence et sans condition (`CoreRuntime.cs:57`). Ce sont
les deux seules sources — `ReportSupply` n'a que ces deux appelants dans tout le projet.

---

## 10. Gisements et génération de monde

### 10.1 `WorldGenerationSettings`

Champs (`Assets/Scripts/Data/WorldGenerationSettings.cs:14-21`) et valeurs
(`Assets/Data/World/WorldGenerationSettings.asset`) :

| Champ | Type | Valeur |
|---|---|---|
| `coreDefinition` | `CoreDefinition` | `CoreDefinition.asset` |
| `ironOreDefinition` | `OreDepositDefinition` | `IronOreDeposit.asset` |
| `copperOreDefinition` | `OreDepositDefinition` | `CopperOreDeposit.asset` |
| `coalOreDefinition` | `OreDepositDefinition` | `CoalOreDeposit.asset` |
| `resourceSeed` | `int` | `12345` |
| `startingStock` | `RecipeIngredient[]` | 150 `Iron_Plate`, 50 `copper_wire`, 30 `Gear` |

La taille de carte n'est pas ici : elle vient de `TerrainGenerationSettings.Size`, passée par
`GameRuntime.cs:100` (`Terrain.Size`).

### 10.2 Placement des gisements

`Assets/Scripts/Gameplay/WorldGeneration/WorldGenerator.cs`.

- **Aléatoire, mais déterministe** : `new System.Random(settings.ResourceSeed)` (`:40`). Même
  seed ⇒ même monde.
- **Le Core d'abord**, au centre exact de la carte (`:33-38`).
- **Six grappes** sont tentées, dans l'ordre fixe fer, fer, cuivre, cuivre, charbon, charbon
  (`:45-50`).
- Chaque grappe est un **carré 2×2 de gisements individuels** (`:60`, `:70-82`) : la grappe
  occupe `footprintSize × 2` = 4×4 cellules et crée **4 `DepositRuntime` distincts** de 2×2.
  Il y a donc jusqu'à **24 gisements** exploitables en jeu.
- **Contraintes de distance** (`TryFindFreeSpot`, `:84-107`) : tirage en coordonnées polaires
  autour du centre du Core, avec
  `minDistance = max(coreFootprint)/2 + max(depositFootprint)` = 2 + 4 = 6 cellules, et
  `maxDistance = ActionRadiusCells − max(depositFootprint)` = 50 − 4 = 46 cellules.
  500 tentatives maximum (`DepositPlacementAttempts`, `:19`) ; la seule autre contrainte est
  que la zone soit libre (`grid.IsAreaFree`, `:98`).
- **Échec silencieux** : si les 500 tentatives échouent, la grappe n'est simplement pas placée
  (`:62`, pas de `else`) et rien n'est signalé.

Tous les gisements sont donc **à l'intérieur du rayon d'action** : il n'existe aucune ressource
hors de portée à débloquer.

### 10.3 Combien d'extracteurs par gisement

Un extracteur fait 2×2, un `DepositRuntime` fait 2×2, et la pose exige que **toutes** les
cellules du gabarit appartiennent au **même** `DepositRuntime`
(`ConstructionService.IsSameExploitableDeposit`, `:411-436`). Donc : **exactement un extracteur
par `DepositRuntime`**, et **4 par grappe visible**. La règle est à cet endroit unique ; il n'y
a pas de compteur d'extracteurs, la contrainte est purement géométrique (une fois l'extracteur
posé, il occupe les cellules du gisement, qui ne sont plus libres).

### 10.4 Épuisement

**Oui, le modèle existe.** `DepositRuntime.RemainingQuantity` (`Assets/Scripts/Grid/DepositRuntime.cs:15`)
part de `definition.InitialQuantity` (**1000** pour les trois types) et décroît à chaque
`TryExtract` (`:27-38`). `ExtractorRuntime.cs:93` consomme via cette méthode.

Comportement à épuisement : `TryExtract` renvoie `false`, donc l'extracteur ne reçoit rien,
mais **son cycle continue de tourner et de payer 50 CU indéfiniment** — le paiement (`:78-83`)
et la remise à zéro du timer (`:88-89`) précèdent l'appel à `TryExtract` et ne dépendent pas de
son résultat. Aucun code ne supprime, ne masque ni ne signale un gisement épuisé ; seule la
pose d'un **nouvel** extracteur est refusée (`ConstructionService.cs:435`,
`deposit.RemainingQuantity > 0`).

À la cadence actuelle (1 item / 2 s), un gisement de 1000 dure 2000 s ≈ 33 min par extracteur.

### 10.5 `StartingStock`

150 `Iron_Plate`, 50 `copper_wire`, 30 `Gear`. Injecté dans `GameRuntime.GlobalStock`
(`PooledItemStock(int.MaxValue)`) au `Awake` (`GameRuntime.cs:85-93`), **pas** dans un
inventaire de bâtiment. Le Core démarre vide.

---

## 11. Construction

### 11.1 Limite du nombre de bâtiments

**Il n'en existe aucune.** `ConstructionService.IsPlaceable` (`:319-362`) n'applique que quatre
contrôles : verrou de recherche (`:321`), rayon d'action (`:326`), coût payable (`:331`), et
occupation/géométrie du terrain (`:336-361`). Aucun compteur d'instances, aucun plafond global
ou par type, nulle part dans le projet.

### 11.2 Remboursement à la démolition

Confirmé. `TryDemolish` (`ConstructionService.cs:295-317`) appelle `RefundCost(removed.Definition)`
**avant** de libérer les cellules (`:303`). `RefundCost` (`:138-147`) rend
l'intégralité de `definition.Cost` dans **`_globalStock` uniquement** (jamais le Core ni un
Storage), et ne fait rien si `_globalStock` est `null` (cas des tests headless).

Le remboursement est donc intégral et sans perte : construire puis démolir est neutre. Deux
réserves factuelles :

- il n'y a **aucun contrôle de propriété** : `TryDemolish` accepte n'importe quel
  `BuildingRuntime` occupant la cellule, y compris le Core (dont `Cost` est vide, donc
  remboursement nul) ;
- le remplacement d'un convoyeur par un autre (« overtake », `ConstructionService.cs:164-171` et
  `:349-356`) détruit l'ancien **sans passer par `TryDemolish`**, donc **sans remboursement**,
  tout en faisant payer le nouveau (`PayCost` ligne 162, avant les branches).

Un extracteur démoli restaure le gisement sous-jacent au lieu de laisser des cellules vides
(`:307-310`).

### 11.3 Vérification du déblocage par recherche à la pose

`ConstructionService.cs:321`, première ligne de `IsPlaceable` :

```csharp
if (Selected.UnlockResearch != null && !_researchSystem.IsUnlocked(Selected.UnlockResearch.Id))
{
    return false;
}
```

`IsPlaceable` est appelée par `CanPlace` (`:66`, aperçu du ghost) et par `TryPlace` (`:157`) :
le verrou couvre donc à la fois l'affichage et l'action. L'UI double ce contrôle côté menu
(`BuildingMenuController.cs:171, 229, 339`). Tests :
`CanPlace_False_WhenUnlockResearchNotUnlocked`, `CanPlace_True_AfterUnlockResearchIsUnlocked`.

Le verrou de **recette** est ailleurs et indépendant : `ProductionBuildingRuntime.cs:85`
(exclusion de `GetRecipeIds()`), doublé d'un refus dans `SetSelectedRecipe`
(test `SetSelectedRecipe_RejectsGatedRecipe_BeforeUnlock`).

---

## 12. GameRuntime et tick

### 12.1 Ordre exact dans `Update()`

`Assets/Scripts/Presentation/GameRuntime.cs:115-133` :

```csharp
void Update()
{
    Power.Settle();                 // :121
    Compute.Tick(Time.deltaTime);   // :122

    Transport.Tick(Time.deltaTime); // :124
    Research.Tick(Time.deltaTime);  // :125

    if (gridLineView != null) gridLineView.SetVisible(Construction.Selected != null); // :132
}
```

`GameRuntime` ne tique **aucun bâtiment directement**. C'est `TransportSystem.Tick`
(`TransportSystem.cs:110-173`) qui orchestre tout, dans cet ordre :

1. `_allOthers[i].Tick(deltaTime)` — machines à états de tous les bâtiments non-convoyeurs
   (`:112-115`) ;
2. avance de **tous** les convoyeurs (`:126-129`) ;
3. lecture par **tous** les bâtiments de leurs cellules d'entrée (`TryGenericPull`, `:134-137`) ;
4. passage de relais entre convoyeurs : traction arrière puis fusion latérale (`:139-154`) ;
5. splitters puis crossroads (`:156-157`) ;
6. poussée des bâtiments, cadencée à `PushIntervalSeconds` = 1 s (`:159-172`).

Le découpage de la phase convoyeur en deux moitiés autour de l'étape 3 est documenté
`:117-125` : c'est ce qui donne la priorité à un bâtiment sur la continuation de la bande.

L'ordre de `Update` sur les autres MonoBehaviour (contrôleurs d'UI, vues) n'est pas contraint
par un `Script Execution Order` explicite — non vérifié dans les settings du projet.

### 12.2 Tick fixe ou `deltaTime` ?

**Tout est en `Time.deltaTime` de frame.** Il n'y a aucun `FixedUpdate`, aucun accumulateur de
pas fixe, aucune constante de fréquence de simulation dans le projet. La simulation est donc
dépendante du framerate en granularité (pas en vitesse moyenne, les intervalles restant
exprimés en secondes).

Une seule commande globale de temps existe : `Time.timeScale = _paused ? 0f : 1f;`
(`Assets/Scripts/UI/TopBarController.cs:143`), bouton Pause de la Top Bar. À `timeScale = 0`,
`Time.deltaTime` vaut 0 et toute la simulation gèle.

### 12.3 Systèmes possédés par `GameRuntime`

Propriétés publiques (`GameRuntime.cs:38-57`) :

| Propriété | Type | Créé à |
|---|---|---|
| `Grid` | `GridRuntime` | `:79` |
| `Terrain` | `TerrainRuntime` | `:80` |
| `Power` | `PowerSystem` | `:81` |
| `Compute` | `ComputeSystem` | `:82` |
| `Research` | `ResearchSystem` | `:83` |
| `Transport` | `TransportSystem` | `:84` |
| `GlobalStock` | `PooledItemStock` | `:85` |
| `World` | `WorldGenerator` | `:99` |
| `Construction` | `ConstructionService` | `:103` |
| `Selection` | `SelectionRuntime` | `:104` |
| `Items` / `Recipes` | `ItemDatabase` / `RecipeDatabase` | champs sérialisés |
| `ItemVisuals` | `ItemVisualSync` | champ sérialisé |

Plus deux états d'entrée : `IsUIBlockingInput` (`:68`) et `LastMenuCloseFrame` (`:75`).

Ordre de construction contraint et documenté (`:95-97`) : la génération du monde doit précéder
`ConstructionService`, qui a besoin de l'instance du Core.

---

## 13. UI

### 13.1 Fichiers `.uxml` et panneaux correspondants

| `.uxml` | Racine | Contrôleur | Type |
|---|---|---|---|
| `GameUI.uxml` | *(vide, ne contient que `<Style>`)* | — | document hôte |
| `TopBar.uxml` | `TopBarRoot` | `TopBarController` | barre permanente |
| `BottomNav.uxml` | `BottomNavRoot` | `BottomNavController` | barre permanente |
| `BuildingMenu.uxml` | `BuildingMenuRoot` | `BuildingMenuController` | panneau global |
| `StoragePanel.uxml` | `StoragePanelRoot` | `StoragePanelController` | panneau global |
| `ComputePanel.uxml` | `ComputePanelRoot` | `ComputePanelController` | panneau global |
| `PowerPanel.uxml` | `PowerPanelRoot` | `PowerPanelController` | panneau global |
| `ResearchPanel.uxml` | `ResearchPanelRoot` | `ResearchPanelController` | panneau global |
| `ExtractorPanel.uxml` | `ExtractorPanelRoot` | `ExtractorPanelController` | inspecteur (droite) |
| `ProductionPanel.uxml` | `ProductionPanelRoot` | `ProductionPanelController` | inspecteur, générique sur `ProductionBuildingRuntime` |
| `LaboratoryPanel.uxml` | `LaboratoryPanelRoot` | `LaboratoryPanelController` | inspecteur |
| `PowerplantGazPanel.uxml` | `PowerplantGazPanelRoot` | `PowerplantGazPanelController` | inspecteur |
| `DataCenterPanel.uxml` | `DataCenterPanelRoot` | `DataCenterPanelController` | inspecteur |
| `CorePanel.uxml` | `CorePanelRoot` | `CorePanelController` | inspecteur |

Les inspecteurs portent `overlay-root overlay-root-right`, les panneaux globaux `overlay-root`
seul. Tous démarrent avec la classe `hidden`. Les 13 contrôleurs sont instanciés dans
`Assets/Scenes/Bootstrap.unity`.

Routage de sélection : `BuildingSelectionInput.cs:74-104`, une branche par type
(`StorageRuntime`, `ExtractorRuntime`, `ProductionBuildingRuntime` — famille couvrant
Foundry/Factory/AdvancedFoundry/Assembler —, `PowerplantGazRuntime`, `LaboratoryRuntime`,
`DataCenterRuntime`, `CoreRuntime`). Splitter, crossroad et convoyeurs n'ont pas de panneau.

### 13.2 Top Bar

Implémentée en Unity. `TopBar.uxml` : une bande de 36 px contenant `TopBarCardsRow`
(les cartes, ajoutées par code), un bouton menu `☰`, un bouton pause `II`, et un overlay
`PAUSE`.

Contenu réel — trois cartes construites par `TopBarController.cs:68-70`, chacune cliquable
pour ouvrir le panneau correspondant :

| Carte | Valeur affichée | Lignes de détail |
|---|---|---|
| Power | `{demande} / {fourniture} kW` (`:177`) | Consumption, Production, Balance (`:180-183`) |
| Compute | `{réserve} CU` (`:193`) | `Reserve: x / 25000 CU`, `Production: n CU/s` (`:198-199`) |
| Research | nom de la recherche active, sinon `{n} RP` (`:214`, `:223`) | pourcentage, temps restant / nombre de labos (`:215-226`) |

Le bouton menu `☰` est décrit dans l'en-tête du fichier comme une icône réservée et
non fonctionnelle. Chaque carte a une barre de remplissage proportionnelle.

### 13.3 Tokens de style partagés

Il n'y en a **pas**. `Assets/UI/GameUI.uss` est la feuille unique importée par tous les `.uxml`,
mais elle **ne définit aucune variable CSS** (`--*`) et aucun bloc `:root` : chaque couleur,
taille et espacement est écrit en dur dans la règle de classe concernée. Les seules occurrences
de `--` dans le fichier sont des sélecteurs internes Unity (`.unity-scroller--vertical`,
lignes 144-158). Les classes partagées font office de tokens : `overlay-root`, `panel`,
`panel-accent`, `header-row`, `title`, `hidden`, `category-button`, `building-card`.

Quelques constantes de style vivent aussi en C#, pas en USS : les largeurs des cartes de la
Top Bar sont des paramètres numériques passés à `BuildCard` (`TopBarController.cs:68-70`).

---

## 14. Tests

19 fichiers, tous dans `Game.Tests.EditMode` ; `Game.Tests.PlayMode` existe mais est vide.
98 méthodes `[Test]`, dont plusieurs paramétrées par `[TestCase]` (35 attributs répartis sur
`DirectionTests`, `ConveyorOrientationTests`, `GridRuntimeTests`), pour **133 cas exécutés**.

| Fichier | Cas | Ce qui est couvert |
|---|---|---|
| `Core/DirectionTests.cs` | 4 + TestCases | opposés, rotations, offsets, degrés |
| `Core/GridCoordTests.cs` | 2 | égalité par valeur, addition d'une direction |
| `Grid/GridRuntimeTests.cs` | 2 + TestCases | conversion monde↔cellule, occupation |
| `Grid/TerrainRuntimeTests.cs` | 4 | déterminisme du terrain par seed, hors-bornes |
| `Data/ItemDatabaseTests.cs` | 3 | lookup par id, id inconnu, `Coal_ore` classé `Component` |
| `Construction/ConstructionServiceTests.cs` | 9 | pose, pose sans sélection, overtake convoyeur, `CanPlace` non mutant, démolition, **verrou de recherche** |
| `Gameplay/BuildingRuntimeFlowDefaultsTests.cs` | 2 | valeurs neutres du contrat Flow |
| `Gameplay/Buildings/BuildingRuntimeEdgeGeometryTests.cs` | 4 | cellules de sortie/bord par orientation |
| `Gameplay/ConveyorOrientationTests.cs` | 4 + TestCases | orientation des virages, déterminisme |
| `Gameplay/Items/PooledItemStockTests.cs` | 5 | ajout, plafond par id, retrait, contenu |
| `Gameplay/Power/PowerSystemTests.cs` | 5 | report/settle, seuil `IsPowered`, récupération immédiate |
| `Gameplay/Compute/ComputeSystemTests.cs` | 9 | réserve initiale, `Spend`/`CanSpend`, plafond de `Grant`, montants négatifs, fenêtre `IncomePerSecond` |
| `Gameplay/Research/ResearchSystemTests.cs` | 8 | RP insuffisants, déduction du coût, refus si déjà active/acquise, **60 s à 1 labo**, **2× plus vite à 2 labos**, décalage d'une frame, évènement de complétion |
| `Gameplay/Buildings/FoundryRuntimeTests.cs` | 14 | machine à états complète, filtres d'entrée, cadence d'absorption, CU une seule fois par cycle, `WaitingCompute`, `OutputBlocked`, changement de recette en cours, gel sans énergie |
| `Gameplay/Buildings/FactoryRuntimeTests.cs` | 4 | recettes gatées par recherche (exclusion, inclusion, refus de sélection), liste d'items acceptés |
| `Gameplay/Buildings/LaboratoryRuntimeTests.cs` | 5 | conversion carte→RP, indépendance vis-à-vis d'une recherche active, report de labo actif, gel sans énergie, refus d'item |
| `Gameplay/Buildings/PowerplantGazRuntimeTests.cs` | 6 | sans carburant, avec carburant, consommation par cycle, gel du timer, filtres d'entrée |
| `Gameplay/Buildings/DataCenterRuntimeTests.cs` | 9 | slots initiaux, installation, surplus en entrée, crédit CU nul hors énergie, crédit au prorata du tick, slot supplémentaire par recherche, désabonnement, filtre d'entrée |
| `Gameplay/Buildings/ComponentInstanceTests.cs` | 6 | snapshot CU/Power, usure plancher, seuil de remplacement, CU nul en remplacement, borne de performance |

Aucun test ne couvre : `TransportSystem` (aucun fichier), `WorldGenerator`, `SelectionRuntime`,
`Inventory`, ni aucun contrôleur d'UI (l'assembly de test ne référence pas `Game.UI`). **Le
chaînage de prérequis de recherche (`ArePrerequisitesMet`, `RequiresResearch`) n'a pas de test
dédié** malgré son implémentation.

### 14.1 Tests cassés par un passage de RP/laboratoires à CU/débit d'absorption

**Cassés à coup sûr — dépendent directement du modèle de progression** (`ResearchSystemTests.cs`) :

- `Tick_OneActiveLab_CompletesAfter60Seconds`
- `Tick_TwoActiveLabs_CompletesTwiceAsFast`
- `ReportActiveLab_IsOneFrameLagged_LikePowerAndCompute`

Ces trois-là supposent l'existence de `ReportActiveLab`/`GetActiveLabCount` et la règle
`N × 1/60`.

**Cassés si `Rp`/`Start(cost)` disparaissent au profit d'un coût en CU** (mêmes fichiers) :

- `Start_Fails_WhenInsufficientRp`
- `Start_Succeeds_DeductsCost`
- `Start_Fails_WhenAlreadyActive` (survit si `Start` garde sa signature)
- `Start_Fails_WhenAlreadyUnlocked` (idem)
- `ResearchCompleted_EventFires_WithCompletedId` (survit si l'évènement est conservé)

**Cassés par la disparition du Laboratoire** : les 5 tests de `LaboratoryRuntimeTests.cs`
et la fabrique `TestDataFactory.NewLaboratory`.

**Non affectés** : tous les tests de `ComputeSystemTests` (le système de CU lui-même est
indépendant du modèle de recherche), `PowerSystemTests`, `FoundryRuntimeTests`,
`PowerplantGazRuntimeTests`, `ComponentInstanceTests`, `PooledItemStockTests`, ainsi que les
tests de grille/terrain/direction.

**Affectés indirectement** : `FactoryRuntimeTests` (3 tests sur 4 utilisent
`research.Start(...)` puis `Tick` pour débloquer `memoire` — ils continueront de fonctionner
tant qu'un moyen de marquer une recherche comme acquise existe, mais leur mise en place devra
être réécrite), `ConstructionServiceTests` (2 tests utilisant le même schéma),
`DataCenterRuntimeTests` (2 tests utilisant `extra_cpu_slot`).

---

## 15. Ce qui n'existe pas

| Élément | État |
|---|---|
| **Brouillard de guerre / visibilité** | **Existe en présentation seulement.** `FogOfWarView.cs` masque tout hors d'un disque unique et statique centré sur le Core (rayon = rayon d'action). Aucune notion de cellule explorée, aucune mémoire, aucune autre source de vision, aucun effet gameplay. |
| **Unités mobiles, escouades** | **N'existe pas.** Aucune classe d'unité, aucun déplacement d'entité. Les seuls objets qui bougent sont les items sur convoyeur (`ConveyorRuntime.AdvanceItem`, position lerpée le long d'une cellule). |
| **Missions / exploration** | **N'existe pas.** Aucun fichier, aucune classe, aucun asset. |
| **Temps de trajet** | **N'existe pas** au sens d'un déplacement point à point. Le seul temps de transport est celui d'un item sur une bande, à `ConveyorSpeedCellsPerSecond = 1.5f` (`TransportSystem.cs:33`, constante non configurable). |
| **Usure de composants** | **Existe**, mais strictement à l'intérieur du Data Center : `ComponentInstance.Wear` (−1 point / 30 s), seuil de remplacement à 5 %, cycle de remplacement de 5 s (§6.2). Aucun autre bâtiment n'a d'usure. |
| **Allocation en pourcentage d'une production** | **N'existe pas.** Le seul répartiteur est le Splitter, en **round-robin strict** item par item sur les sorties valides (`SplitterRuntime.cs`, commentaire de tête + `_cursor`), sans ratio configurable. Le Crossroad, lui, ne répartit pas : il fait traverser. |
| **Séquence d'amorçage d'un bâtiment** | **N'existe pas.** Un bâtiment est pleinement opérationnel dès l'instant où `TryPlace` renvoie `true` : aucun temps de construction, aucun état « en cours de montage », aucune phase de démarrage. Le commentaire de `PowerplantGazRuntime.cs:9` le dit explicitement (« Immediately operational once built »). |
| **Plafond de bâtiments** | **N'existe pas** (§11.1). |
| **File d'attente de recherche** | **N'existe pas.** Un seul emplacement actif, `Start` refuse tant que `ActiveResearch != null` (`ResearchSystem.cs:51`). Aucune structure de file nulle part. |
| **Arbre de recherche à prérequis multiples** | **N'existe pas.** `ResearchDefinition.requiresResearch` est une **référence unique**, pas une liste, et le commentaire (`ResearchDefinition.cs:22-26`) précise que c'est un choix assumé : « One direct reference, not a list: the tree is a chain today ». La chaîne réelle est `screw → circuit_board → cpu_assembler` ; les trois autres recherches n'ont aucun prérequis. |

---

## Incertitudes

Points que je n'ai pas pu établir avec certitude, ou qui méritent une vérification avant
d'être utilisés comme base de décision :

1. **`Script Execution Order`** — je n'ai pas ouvert les ProjectSettings. L'ordre relatif de
   `GameRuntime.Update()` par rapport aux `Update()` des contrôleurs d'UI et des vues n'est
   donc pas établi ; il pourrait être imposé par un réglage projet plutôt que par l'ordre par
   défaut d'Unity.
2. **Contenu de la scène `Bootstrap.unity`** — je l'ai inspectée par recherche ciblée
   (contrôleurs présents, tableau `researches` du panneau de recherche), pas lue intégralement.
   Le contenu exact du tableau de bâtiments du `BuildingMenuController` (quels bâtiments sont
   proposés, dans quelle catégorie) n'a pas été relevé.
3. **`Memory_MK1` est déclarée dans deux bâtiments** — la recette figure à la fois dans
   `FactoryDefinition.recipeIds` et dans `AssemblerDefinition.recipeIds`. Je ne sais pas si la
   présence dans la Factory est intentionnelle ou résiduelle. À noter : la Factory ne peut pas
   la produire en pratique, `Printed_Circuit_Board` n'étant pas dans ses `acceptedItemIds`.
4. **Trois recherches sont inatteignables** (`memoire`, `datacenter`, `extra_cpu_slot`) parce
   qu'absentes du tableau sérialisé du `ResearchPanelController` dans la scène. Je n'ai pas pu
   déterminer s'il s'agit d'un oubli de câblage ou d'un retrait volontaire.
5. **Le shader `Custom/FogOfWar`** — je n'ai pas ouvert le fichier de shader ; la description du
   brouillard repose sur les paramètres passés par `FogOfWarView` (`_Center`, `_Radius`,
   `_EdgeSoftness`, `_FogColor`) et sur le commentaire de tête, pas sur le code du shader.
6. **`Inventory.cs`** (`Gameplay/Inventory/`) — non lu. Son rôle exact et ses éventuels
   consommateurs restent à établir ; `PooledItemStock` semble être la structure de stock
   réellement utilisée partout.
7. **Comportement à `RemainingQuantity == 0`** — j'ai établi par lecture que l'extracteur
   continue de payer 50 CU par cycle sur un gisement épuisé (§10.4), mais ce comportement n'est
   couvert par aucun test et je ne l'ai pas vérifié en exécution.
8. **`ProductionBuildingRuntime`** — j'ai lu les lignes 1-51 et 150-260. Les lignes 52-149
   (notamment `GetRecipeIds`, `SetSelectedRecipe`, `GetRequiredIngredients`,
   `HasRequiredResources`, les accesseurs de progression) n'ont pas été lues intégralement ;
   les descriptions les concernant s'appuient sur leurs sites d'appel et sur les tests.
9. **`GLOBAL_UI.md`** — non consulté pour cet audit. Il peut décrire des éléments d'UI marqués
   comme non implémentés qui contrediraient en apparence la §13 ; seule la §13 décrit le code
   réel.
