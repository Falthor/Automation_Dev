# Alignement du projet sur le GDD

Le GDD est la référence. Tout ce qui le contredit dans le projet actuel doit changer.

Ce document liste chaque valeur à modifier, avec sa valeur actuelle relevée dans le
dépôt et sa valeur cible. Toutes les valeurs cibles sont des valeurs de test.

---

## État d'application

- **§1 (Items), §2 (Recettes), §3 (Bâtiments/Core)** — appliquées
  (`TASK_01_REBALANCE_DATA.md`), à l'exception du verrou `unlockResearch` de
  `AdvancedFoundryDefinition` vers la recherche *Fonderie avancée* : cette recherche
  n'existe pas encore et sa création sort du périmètre d'une tâche de données. Le champ
  reste `null`.
- **§6 (`ComputeSystem.ReserveCap`)** — appliquée.
- **§8, sous-section « Placement — état actuel et cible »** — appliquée
  (`WorldGenerator.cs`) : 4 grappes de fer + 1 grappe d'invitation, 2 de cuivre, 1 de
  charbon dans le rayon garanti chacune (échec = exception), `minDistance` à 10,
  grappe d'invitation entre 28 et 34 cellules hors rayon.
- **§8, reste (« Les gisements sont illimités », suppression de `InitialQuantity`/
  `RemainingQuantity`/`TryExtract`)** — non appliqué.
- **§4 (Data Center), §5 (Usure des composants), §6 (refonte recherche, plafond de
  bâtiments), §7 (Recherches), §9, §10 (brouillard de guerre)** — non appliquées, hors
  périmètre.

---

## 1. Items — `Assets/Data/Items/`

| Asset | Champ | Actuel | Cible |
|---|---|---|---|
| `cpu_mkI` | `cuOutput` | 1000 | **15** |
| `cpu_mkI` | `powerKw` | 2 | inchangé |
| `Memory_MK1` | `cuOutput` | 500 | **10** |
| `Memory_MK1` | `powerKw` | 1 | inchangé |
| `Data_Card` | — | existe | **supprimé** |

Le `cuOutput` actuel produit 6 000 CU/s avec les huit baies remplies, soit le plafond de
réserve rempli en quatre secondes. C'est la correction la plus urgente du projet.

## 2. Recettes — `Assets/Data/Recipes/`

| Recette | Entrées actuelles | Entrées cibles | Sortie | Temps | `computeCost` |
|---|---|---|---|---|---|
| `Iron_Ingot` | 1 iron_ore | inchangé | 1 | 2 → **3 s** | 100 → **2** |
| `copper_Ingot` | 1 copper_ore | inchangé | 1 | 2 → **3 s** | 100 → **2** |
| `Gear` | 1 Iron_Ingot | inchangé | 2 | 2 s | 200 → **4** |
| `Iron_Plate` | 2 Iron_Ingot | inchangé | 2 | 5 → **3 s** | 300 → **8** |
| `copper_wire` | 2 copper_Ingot | **1 copper_Ingot** | 3 → **2** | 3 → **2 s** | 300 → **4** |
| `Screw` | 1 Iron_Ingot + 2 copper_Ingot | **1 + 1** | 1 → **2** | 3 s | 200 → **8** |
| `Printed_Circuit_Board` | 2 Screw + 3 copper_wire | inchangé | 1 | 10 → **6 s** | 1000 → **24** |
| `Steel` | 3 iron_ore + 2 Coal_ore | **2 + 1** | 1 | 6 → **4 s** | 600 → **12** |
| `cpu_mkI` | 5 copper_Ingot + 3 Gear + 6 PCB | **3 + 2 + 3** | 2 | 5 → **6 s** | 2000 → **80** |
| `Memory_MK1` | 4 PCB + 5 Iron_Ingot | **2 + 3** | 1 | 3 → **4 s** | 1500 → **48** |
| `mechanical_component` | 1 cpu_mkI + 1 Memory_MK1 + 2 Iron_Plate | **2 Gear + 4 Screw + 2 Iron_Plate** | 1 | 6 → **4 s** | 600 → **32** |
| `Data_Card` | — | — | — | — | **recette supprimée** |

Le composant mécanique est la modification la plus structurante : il ne contient plus ni
CPU ni mémoire, ce qui divise par six son coût cumulé et le rend réellement utilisable
ailleurs.

Gates de recherche à corriger : `Screw_Recipe.unlockResearch` passe à `null` (les vis
sont disponibles dès le départ), `Memory_MK1_Recipe` et `cpu_mkI_Recipe` pointent tous
deux vers la nouvelle recherche *Modules de calcul*.

## 3. Bâtiments — `Assets/Data/Buildings/` et `Assets/Data/World/`

| Asset | Champ | Actuel | Cible |
|---|---|---|---|
| `CoreDefinition` | `actionRadiusCells` | 50 | **22** |
| `CoreDefinition` | `cuOutput` | 3000 | **0** |
| `CoreDefinition` | `powerOutputKw` | 20 | inchangé |
| `ExtractorDefinition` | `extractionIntervalSeconds` | 2 | **4** (bridé) |
| `ExtractorDefinition` | `cuCostPerCycle` | 50 | **2** |
| `PowerplantGazDefinition` | `powerOutputKw` | 10 | **25** |
| `PowerplantGazDefinition` | `cuCostPerCycle` | 150 | **8** |
| `PowerplantGazDefinition` | `selfPowerDemandKw` | 2 | inchangé |
| `AdvancedFoundryDefinition` | `cost` | 10 Iron_Plate | **20 Iron_Plate + 10 mechanical_component** |
| `AdvancedFoundryDefinition` | `unlockResearch` | null | **Fonderie avancée** (nouvelle) |
| `DataCenterDefinition` | `cost` | 50 Steel + 40 mech + 20 cpu + 30 mem | **120 Iron_Plate + 80 copper_wire + 160 PCB + 48 cpu_mkI + 36 Memory_MK1 + 24 mechanical_component** |
| `LaboratoryDefinition` | — | existe | **supprimé** |

Le coût actuel du Data Center réclame 50 aciers alors que la Fonderie avancée n'est pas
débloquée à ce stade : c'est une dépendance circulaire, corrigée par le nouveau coût.

Bâtiment à créer : **Forge d'unités**, 20 Iron_Plate + 10 mechanical_component +
4 cpu_mkI, débloquée par la recherche du même nom.

## 4. Data Center — `DataCenterRuntime`

| Élément | Actuel | Cible |
|---|---|---|
| `InitialCpuSlots` | 4 | **2** |
| `InitialMemorySlots` | 4 | **2** |
| Extension | `extra_cpu_slot` ajoute 1 baie CPU | **deux recherches ajoutant chacune 1 baie CPU et 1 baie mémoire**, jusqu'à 4 + 4 |
| `StabilityInterval` | 5 s | inchangé |
| `ReplacementDuration` | 5 s | inchangé |
| Répartition par axe | inexistante | **à ajouter** (voir GDD §2.3) |
| Amorçage | inexistant | **à ajouter**, 1 500 CU sur 90 s |

Le mécanisme `OnResearchCompleted` qui ajoute une baie existe déjà et fonctionne : il
suffit de le brancher sur deux recherches au lieu d'une, et de lui faire ajouter aussi
une baie mémoire.

## 5. Usure des composants — `ComponentInstance`

Le modèle actuel est purement linéaire — un point toutes les 30 secondes, de 100 à 5,
soit 47 minutes — et l'usure n'a **aucun effet** sur le rendement avant de déclencher
brutalement le remplacement. Elle est donc invisible jusqu'à ce qu'elle compte.

Trois changements, en conservant intégralement le système de stabilité et de
fluctuation.

**L'usure pilote la stabilité au lieu d'être un compteur indépendant.**

```
stabilité = 95 − 65 × (1 − usure/100)
```

95 % à neuf, 30 % en fin de vie. La stabilité cesse d'être la constante 80 et devient la
traduction directe de l'état du composant.

**La fourchette de fluctuation s'élargit avec l'usure.**

```
plancher_fluctuation = 1,0 − 0,70 × (1 − usure/100)
```

Un composant neuf fluctue entre 70 % et 100 %, un composant en fin de vie entre 30 % et
100 %. Le rendement moyen passe donc d'environ 99 % à 75 % sur la durée de vie, et
surtout **la production devient visiblement instable avant de tomber**. Le joueur voit
son graphe de CU trembler : c'est le signal de réapprovisionnement, et il est diégétique.

**La décroissance accélère au lieu d'être constante.**

```
perte_par_seconde = perte_base × (1 + 2 × (1 − usure/100))
```

Un composant neuf s'use à vitesse nominale, un composant à 20 % s'use 2,6 fois plus
vite. La fin de vie arrive plus tôt qu'on ne l'anticipe, sans être injuste puisque la
jauge est visible.

**Et la durée de vie est tirée par composant, pas constante.**

À l'installation, chaque composant tire sa durée de vie nominale dans une fourchette de
± 25 % autour de **120 secondes**, soit 90 à 150 secondes. C'est indispensable : dans le
modèle actuel, entièrement déterministe, des composants installés ensemble meurent
ensemble, ce qui produit des à-coups de production périodiques. La dispersion lisse le
flux et rend le réapprovisionnement continu plutôt que pulsé.

`perte_base` se déduit de la durée de vie tirée, de façon que l'intégrale de la courbe
accélérée amène l'usure de 100 à 5 exactement en ce temps-là.

### Seuil de remplacement configurable

`ReplacementThreshold` est aujourd'hui une constante à 5 %. Il devient un **réglage du
joueur**, exposé dans le panneau du Data Center : un curseur de 5 % à 60 %, valeur par
défaut **25 %**, applicable séparément aux baies CPU et aux baies mémoire.

C'est un arbitrage réel, et il porte sur trois coûts qui ne se ressemblent pas.

Remplacer tôt — seuil élevé — garde des composants toujours proches du neuf, donc une
stabilité haute et une production régulière. Mais on jette une part importante de la vie
restante, et la consommation de CPU et de mémoire grimpe fortement. À 40 %, on
n'exploite que 63 % de la durée de vie : il faut produire environ une fois et demie plus
de composants.

Remplacer tard — seuil bas — tire le maximum de chaque composant, mais impose de longues
périodes de rendement instable, précisément dans la zone où la décroissance accélère.

Et dans les deux cas, chaque remplacement coûte 5 secondes pendant lesquelles la baie ne
produit rien : un seuil trop élevé multiplie aussi ces micro-coupures.

Le joueur qui produit des composants en abondance remplace tôt et achète de la
régularité. Celui qui est à court les use jusqu'au bout et accepte le tremblement. La
décision se reprend à tout moment, gratuitement.

**Note sur l'affichage** : ici, contrairement aux missions, les chiffres exacts sont
légitimes. Le principe « la précision se gagne » s'applique au monde inconnu, pas à ses
propres machines. Le joueur doit voir l'usure de chaque baie en pourcentage, sa
stabilité courante et le seuil qu'il a fixé.

## 6. Systèmes

| Élément | Actuel | Cible |
|---|---|---|
| `ComputeSystem.ReserveCap` | 25 000 | **60 000** |
| `ComputeSystem.Reserve` initiale | = cap | **60 000** |
| `ComputeSystem` — flux par seconde | interdit par contrat | **autorisé** (recherche, amorçage) — modification de `CONTRACTS.md` §10 |
| `ResearchSystem` | RP + laboratoires | **refonte complète** : coût en CU, débit d'absorption, pause à 0, file d'attente, prérequis multiples |
| `ResearchDefinition.RequiresResearch` | référence unique | **liste** |
| `ResearchDefinition` | id, nom, coût | **+ débit d'absorption, + palier, + effets** |
| Plafond de bâtiments | inexistant | **40**, hors convoyeurs et splitters |
| Consommation de combustible | continue | **inchangée** — une centrale brûle qu'il y ait demande ou non, c'est délibéré (GDD §4.3) |

### Registre de recherches

L'arbre de recherche est aujourd'hui défini par un **tableau sérialisé dans
`Bootstrap.unity`**, sur le `ResearchPanelController`. Conséquence : `memoire`,
`datacenter` et `extra_cpu_slot` existent en assets mais sont absents du tableau, donc
inatteignables en jeu — et avec eux, Memory MK1 et le Data Center.

Ce n'est pas un oubli à rattraper, c'est une classe de bug à supprimer. Le projet a déjà
`ItemDatabase` et `RecipeDatabase` ; il lui faut un **`ResearchDatabase`** sur le même
modèle. L'arbre devient une donnée, plus un champ de scène, et l'UI le lit au lieu de le
définir. C'est aussi la condition pour que le menu radial puisse se construire tout seul :
un menu qui calcule ses angles à partir d'un tableau sérialisé dans une scène serait
ingérable.

## 6b. Règle : vérifier avant de payer

**Un cycle n'est débité qu'une fois toutes les conditions de sa réussite réunies.** Le CU
reste prélevé en entier au démarrage du cycle, jamais étalé — mais ce démarrage n'a lieu
que si le cycle peut effectivement aboutir. Aucun cycle ne démarre pour échouer ensuite.

L'extracteur est déjà conforme sur ce point : il vérifie son buffer et la réserve avant de
débiter. Le seul cas d'échec restant, le gisement vide, disparaît avec la suppression du
modèle d'épuisement (§8).

La règle reste à faire respecter partout ailleurs. Avec une réserve finie, toute
facturation qui ne débouche sur rien est une fuite silencieuse : elle ne produit aucun
symptôme visible, juste une jauge qui descend plus vite que prévu. À auditer sur chaque
site de `Spend`, avec un test de régression par site.

La centrale gaz est hors de ce champ : elle brûle en continu par choix de conception, et
son combustible n'est donc pas une facturation sans contrepartie mais un coût de
fonctionnement assumé. **Vérifié dans le code** : `PowerplantGazRuntime.Tick` sort avant
tout débit quand `HasFuel` est faux, et un indicateur `_cycleCharged` garantit qu'une
unité de combustible n'est facturée qu'une fois. Rien à corriger.

Un détail relevé au passage : une centrale sans combustible continue de déclarer sa
`SelfPowerDemandKw` de 2 kW. Elle est donc une charge nette sur le réseau tant qu'elle
n'est pas alimentée. C'est cohérent — une centrale à l'arrêt consomme quand même — mais
plusieurs centrales en panne sèche peuvent faire tomber la base. À garder en tête au
moment d'équilibrer, ce n'est pas un bug.

## 7. Recherches — `Assets/Data/Research/`

Les coûts passent de RP à CU et gagnent un débit d'absorption. Le tableau ci-dessous est
l'inventaire complet des recherches définies à ce jour.

### Introduction — menu linéaire

| Asset actuel | Coût actuel | Devient | Coût cible | Absorption | Statut |
|---|---|---|---|---|---|
| `screw` | 10 RP | **supprimée** — vis disponibles dès le départ | — | — | — |
| `circuit_board` | 50 RP | Circuit imprimé | **1 500** | 35 CU/s | obligatoire |
| `cpu_assembler` | 100 RP | Assembleur (+ composant mécanique) | **2 500** | 45 CU/s | obligatoire |
| `memoire` | 100 RP | fusionnée dans Modules de calcul | — | — | — |
| — | — | **Modules de calcul** (CPU MkI + Memory MK1) | **3 500** | 50 CU/s | obligatoire |
| `datacenter` | 200 RP | Datacenter MK1 | **5 000** | 60 CU/s | obligatoire |
| — | — | **Optimisation de fabrication** — −10 % de CU par objet | **1 500** | 40 CU/s | optionnelle |
| — | — | **Extraction renforcée** — lève le bridage, débit ×2 | **2 000** | 40 CU/s | optionnelle |
| — | — | **???** — troisième sonde | gratuite | — | via expédition |

Total obligatoire : **12 500 CU**. Optionnel : 3 500 CU.

### Après l'amorçage

| Asset actuel | Devient | Coût cible | Absorption | Effet |
|---|---|---|---|---|
| — | **Fonderie avancée** | **2 000** | 40 CU/s | bâtiment + recette acier |
| `extra_cpu_slot` | **Extension de baies I** | **2 000** | 30 CU/s | +1 baie CPU, +1 baie mémoire |
| — | **Extension de baies II** | **2 000** | 30 CU/s | +1 baie CPU, +1 baie mémoire |
| — | **Allocation mémoire** | **2 500** | 40 CU/s | plafond de bâtiments 40 → 52 |
| — | **Bande passante étendue** | **3 000** | 45 CU/s | rayon d'action 22 → 30 cellules |
| — | **Convoyeur MK2** | **3 500** | 50 CU/s | débit doublé |

### Branche armement

Elle s'allume à la découverte du premier nid et ne contient **qu'une recherche définie**.

| Recherche | Coût | Absorption | Effet |
|---|---|---|---|
| **Forge d'unités** | **6 000** | 70 CU/s | bâtiment de production d'unités |

### Ce qui reste indéfini

Nommé dans le GDD mais volontairement non chiffré, parce que ces éléments dépendent de
systèmes qui n'existent pas encore :

- **Tourelle** et **Centre de réparation** — branche défense statique, à définir avec le
  système de combat
- **Unités mobiles** au-delà de la Forge — types, coûts, entretien
- Tout ce qui relève du **second Datacenter**, des **Agents IA**, des **Répéteurs** et des
  signaux au-delà du premier

Ne pas les inventer au moment de l'implémentation : la branche armement doit rester à un
seul nœud tant que le combat n'est pas conçu.

## 8. Génération de monde — `WorldGenerationSettings`

**Règle des gisements.** Un gisement fait 2×2 et un extracteur aussi : une tuile de
gisement n'accueille donc qu'un extracteur. Mais dans le rayon initial du Noyau, les
gisements sont **groupés par quatre**, si bien qu'un groupe accepte quatre extracteurs.

Le parc cible demande 4 extracteurs de fer, 4 de cuivre et 2 de charbon. Le générateur
place volontairement bien plus, pour que le joueur puisse se surdimensionner — et
apprendre à ne pas le faire :

| Ressource | Groupes dans le rayon | Emplacements disponibles | Extracteurs nécessaires |
|---|---|---|---|
| Fer | **4** | 16 | 4 |
| Cuivre | **2** | 8 | 4 |
| Charbon | **1** | 4 | 2 |

Plus **au moins un groupe de fer visible hors du rayon d'action**, comme invitation
permanente.

### Les gisements sont illimités

**Règle : un gisement ne s'épuise jamais.** Sa quantité est infinie, un extracteur posé
dessus produit indéfiniment.

Le projet implémente aujourd'hui l'inverse — `OreDepositDefinition.InitialQuantity` vaut
1 000 pour les trois types et `DepositRuntime.RemainingQuantity` décroît à chaque
extraction. **Ce mécanisme est à supprimer**, pas à ajuster : retirer `InitialQuantity`,
`RemainingQuantity` et `TryExtract`, et avec eux le test
`deposit.RemainingQuantity > 0` de `ConstructionService.IsSameExploitableDeposit`.

Supprimer le mécanisme fait disparaître par construction le bug décrit en §6b, ce qui
vaut mieux que de le corriger dans un système qu'on ne veut pas.

Conséquence de conception : ce qui pousse le joueur à s'étendre n'est pas la raréfaction
mais le **débit**. Une grappe n'offre que quatre emplacements d'extracteur ; produire
davantage exige d'atteindre d'autres grappes, donc d'étendre le rayon du Noyau et
d'explorer. Le moteur de l'expansion est le plafond de production, pas la pénurie.

### Placement — état actuel et cible

Le générateur place aujourd'hui **six grappes**, dans l'ordre fixe fer, fer, cuivre,
cuivre, charbon, charbon, en coordonnées polaires aléatoires mais déterministes
(`ResourceSeed`), entre 6 et 46 cellules du centre. **Toutes sont donc à l'intérieur du
rayon d'action** : il n'existe aucune ressource hors de portée.

| Élément | Actuel | Cible |
|---|---|---|
| Grappes de fer | 2 | **4** dans le rayon, **+1 hors rayon** |
| Grappes de cuivre | 2 | **2** dans le rayon |
| Grappes de charbon | 2 | **1** dans le rayon |
| Garantie | aucune | **au moins une grappe de fer, une de cuivre et une de charbon dans le rayon**, quoi qu'il arrive |
| `minDistance` | 6 cellules | **10 cellules** — laisser de la place pour bâtir autour du Noyau |
| `maxDistance` (dans le rayon) | `ActionRadiusCells − 4` | inchangé, soit 18 avec un rayon de 22 |
| Grappe d'invitation | impossible | placée **entre 28 et 34 cellules**, hors rayon, visible mais inexploitable |
| Échec de placement | silencieux | **doit lever une erreur** — une grappe manquante rend l'introduction infaisable |

Deux points méritent attention.

La distance minimale actuelle de 6 cellules place une grappe presque contre le Noyau,
qui fait lui-même 4×4. Le joueur se retrouve alors à construire ses lignes dans un
couloir. Dix cellules laissent de quoi respirer sans éloigner les ressources au point de
rendre les premiers convoyeurs pénibles.

Et l'échec silencieux est le vrai danger : aujourd'hui, si les 500 tentatives échouent,
la grappe n'est simplement pas placée et rien n'est signalé. Avec un équilibrage qui
suppose un nombre exact de grappes, ça produit une partie injouable sans le moindre
message. Les trois grappes garanties doivent être placées en premier, et un échec doit
lever une erreur plutôt que produire un monde incomplet.

Le placement de ces gisements est **ancré à des distances imposées**, pas aléatoire.
L'aléatoire ne s'exprime qu'au-delà du rayon initial.

`startingStock` actuel — 150 Iron_Plate, 50 copper_wire, 30 Gear — représente environ
1 500 CU d'économie sur le chemin critique. À conserver tel quel : c'est ce qui permet au
joueur de poser ses premiers bâtiments avant d'avoir la moindre ligne de production.

## 9. Rayon d'action du Core

`actionRadiusCells` passe de 50 à **22**, soit environ 1 500 cellules exploitables.

Le parc complet occupe à peu près 310 cellules de bâtiments — 8 extracteurs de 2×2, 6
fonderies de 3×3, 8 factories de 4×4, 3 assembleurs de 4×4, 3 centrales de 3×3, le Core
de 4×4 et le Datacenter de 2×2 — auxquelles s'ajoutent les convoyeurs et les espaces de
circulation. Un rayon de 22 laisse de quoi construire confortablement tout en rendant la
limite perceptible dans la dernière ligne droite.

## 10. Brouillard de guerre — `FogOfWarView`

Le brouillard actuel masque simplement la zone. Décision : **il reste à cette échelle-là,
mais découpé en secteurs.**

Le brouillard n'est pas géré cellule par cellule mais par **secteur**, chaque secteur
étant exactement l'unité de mission. Une reconnaissance réussie révèle un secteur entier,
définitivement — une zone révélée ne se referme jamais.

C'est le bon niveau de granularité pour trois raisons : c'est cohérent avec le système de
missions, où on n'explore pas une cellule mais une zone ; c'est peu coûteux à calculer et
à afficher ; et ça donne à la carte dézoomée une lisibilité immédiate, chaque secteur
étant soit connu, soit non reconnu.

La zone révélée au départ correspond au rayon d'action du Core plus une marge, de façon
que le joueur voie le gisement d'invitation sans pouvoir l'exploiter.

En cas de perte totale d'escouade, le secteur visé est révélé malgré tout — la dernière
transmission — et reçoit un marqueur permanent de contact perdu.

## 11. Tests impactés

| Fichier | Impact |
|---|---|
| `ResearchSystemTests` | à réécrire entièrement |
| `LaboratoryRuntimeTests` | à supprimer |
| `ComputeSystemTests` | à étendre — plafond, réserve initiale, prélèvement continu |
| `DataCenterRuntimeTests` | à adapter — baies 2+2, deux recherches d'extension, amorçage, répartition |
| `ComponentInstanceTests` | à réécrire — nouveau modèle d'usure |
| `PowerplantGazRuntimeTests` | à adapter — combustible proportionnel à la demande |
| `FactoryRuntimeTests`, `FoundryRuntimeTests` | à vérifier — les `computeCost` changent |
