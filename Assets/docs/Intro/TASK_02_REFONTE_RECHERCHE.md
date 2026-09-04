# Tâche 02 — Refonte du système de recherche

**Objectif : remplacer le modèle RP/laboratoire par le modèle CU/absorption, rendre
l'arbre pilotable par les données, et supprimer la Data Card et le Laboratoire.**

Référence : `ALIGNEMENT_PROJET.md` §5.1 et §7, GDD §5, maquette
`01-menu-recherche-intro.html`.

Prérequis : les tâches 01 et 01B sont livrées et la mesure d'introduction est faite.

---

## 1. Pourquoi cette tâche est urgente

Le Laboratoire et la Data Card sont restés à l'ancienne échelle de prix. Une Data Card
coûte 500 CU et une conversion en RP 250, alors qu'un circuit imprimé est descendu à 24.
**Une seule carte vaut donc vingt circuits imprimés.** Tant que ces deux assets existent,
une poignée de recherches suffit à faire exploser une réserve de 60 000 et toute mesure
devient impossible.

C'est aussi ce qui a obligé la tâche 01 à neutraliser cinq verrous de recherche. Ils
restent neutralisés jusqu'à cette tâche.

---

## 2. Modification de contrat

`CONTRACTS.md` §10 affirme que « CU est une monnaie, pas un flux » et que **rien ne
consomme du CU par seconde**. Le modèle de recherche par débit d'absorption contredit
directement cette affirmation.

C'est une évolution de contrat public au sens de `CONTRACTS.md` §13 : identifier tous les
consommateurs, mettre à jour la documentation, adapter les tests, signaler explicitement
le changement de comportement. **À traiter comme telle, pas comme un ajout discret.**

Le reste du §10 est inchangé : le CU est toujours prélevé en entier au démarrage d'un
craft, et le crédit du Data Center reste ce qu'il est.

---

## 3. Le modèle

### 3.1 Définition

`ResearchDefinition` gagne et perd des champs :

| Champ | Statut |
|---|---|
| `id`, `displayName`, `description`, `icon` | inchangés |
| `cost` en RP | **remplacé** par `cuCost` |
| `requiresResearch` — référence unique | **remplacé** par `prerequisites` — liste |
| `absorptionRatePerSecond` | **nouveau** — plafond d'absorption en CU/s |
| `tier` | **nouveau** — palier, pour le futur placement radial |

### 3.2 Runtime

```
durée = coût / min(absorptionRatePerSecond, CU disponible par seconde)
```

On ne définit jamais une durée : on définit un coût et un plafond d'absorption. Le temps
en est la conséquence, ce qui donnera plus tard tout son sens au curseur du Datacenter.

Pendant l'introduction, le CU vient de la réserve unique. La séparation par axe viendra
avec le Datacenter, à une tâche ultérieure.

### 3.3 Règles impératives

- **Une seule recherche active à la fois.**
- **Une file d'attente** où le joueur enfile les suivantes, réordonnable.
- **Si le CU tombe à zéro, la recherche se met en pause en conservant sa progression.**
  Elle ne se perd jamais. C'est non négociable.
- Une recherche n'est lançable que si **tous** ses prérequis sont acquis.
- Le débit réel d'une recherche est `min(absorptionRatePerSecond, ce que la réserve peut
  fournir)`. Elle ne consomme jamais plus que son plafond, même en cas d'abondance.

### 3.4 Ce qui disparaît

`AddRp`, `ReportActiveLab`, `GetActiveLabCount`, le pool de RP, et toute la logique de
progression liée au nombre de laboratoires actifs.

---

## 4. ResearchDatabase

L'arbre est aujourd'hui défini par un **tableau sérialisé dans `Bootstrap.unity`**, sur le
`ResearchPanelController`. Conséquence constatée à l'audit : `memoire`, `datacenter` et
`extra_cpu_slot` existent comme assets mais sont absents du tableau, donc inatteignables en
jeu — et avec eux Memory MK1 et le Data Center.

Ce n'est pas un oubli à rattraper, c'est une classe de bug à supprimer.

Créer un **`ResearchDatabase`** sur le modèle exact d'`ItemDatabase` et de
`RecipeDatabase`. L'arbre devient une donnée, l'UI le lit au lieu de le définir, et le
tableau sérialisé disparaît de la scène.

C'est aussi la condition du futur menu radial : un menu qui calculerait ses angles à partir
d'un tableau planqué dans une scène serait ingérable.

---

## 5. Suppressions

**Laboratoire** — runtime, définition, prefab, panneau UI, entrée de base de données,
tests. Tout part.

**Data Card** — l'item et sa recette. Vérifier qu'aucun `recipeIds` de bâtiment n'y fait
encore référence.

Le rapport doit lister précisément ce qui a été supprimé.

---

## 6. Assets de recherche à créer

**Ne créer que les recherches dont l'effet peut réellement être câblé aujourd'hui.** Une
recherche qui ne fait rien est pire qu'une recherche absente : elle coûte du CU au joueur
et ment sur sa promesse.

Les cinq ci-dessous sont de simples verrous `unlockResearch`, le mécanisme existe déjà.

| id | Nom | `cuCost` | Absorption | Prérequis | Effet |
|---|---|---|---|---|---|
| `circuit_board` | Circuit imprimé | 1 500 | 35 CU/s | aucun | recette PCB |
| `assembler` | Assembleur | 2 500 | 45 CU/s | `circuit_board` | bâtiment Assembleur + recette composant mécanique |
| `compute_modules` | Modules de calcul | 3 500 | 50 CU/s | `assembler` | recettes CPU MkI et Memory MK1 |
| `datacenter` | Datacenter MK1 | 5 000 | 60 CU/s | `compute_modules` | bâtiment Data Center |
| `storage_box` | Boîte de stockage | 1 000 | 30 CU/s | aucun | bâtiment Storage Box — **optionnelle** |

Les assets existants `screw`, `memoire`, `cpu_assembler`, `extra_cpu_slot` sont
**supprimés ou renommés** selon la correspondance du §7 d'`ALIGNEMENT_PROJET.md`. Les vis
restent disponibles dès le départ, sans recherche.

### Reportées, et pourquoi

| Recherche | Bloquée par |
|---|---|
| Optimisation de fabrication | demande un multiplicateur global de `computeCost`, mécanique inexistante |
| Extraction renforcée | demande le système de bridage/débridage, inexistant |
| Allocation mémoire | demande un plafond de bâtiments, inexistant |
| Bande passante étendue | demande un rayon d'action modifiable en cours de partie |
| Convoyeur MK2 | demande un convoyeur de second niveau |
| Extension de baies I et II | demande le Datacenter à baies configurables |
| Forge d'unités | demande la branche armement et le système de combat |

Chacune viendra avec sa mécanique. **Ne pas les créer par anticipation.** La section 12
indique quelle tâche future porte chacune.

---

## 7. Restaurations

Les quatre verrous neutralisés temporairement par la tâche 01 sont rétablis, en pointant
vers les nouveaux assets :

| Asset | `unlockResearch` |
|---|---|
| `Printed_Circuit_Board_Recipe` | `circuit_board` |
| `Memory_MK1_Recipe` | `compute_modules` |
| `AssemblerDefinition` | `assembler` |
| `DataCenterDefinition` | `datacenter` |
| `StorageDefinition` | `storage_box` — **nouveau** |

Il faut aussi ajouter `cpu_mkI_Recipe` → `compute_modules`, qui n'était pas verrouillé
auparavant.

**`Screw_Recipe` reste à `null` définitivement.** Ne pas le restaurer.

`AdvancedFoundryDefinition.unlockResearch` reste à `null` : la Fonderie avancée fait partie
des recherches reportées.

---

## 8. Interface

Le menu linéaire de l'introduction, conforme à `01-menu-recherche-intro.html`.

**Cinq états**, tous distinguables sans lire de texte, et jamais par la couleur seule :
acquis, en cours, disponible et payable, disponible mais CU insuffisant, verrouillé.
Chaque état porte aussi une forme — coche, anneau de progression, cadenas.

**Panneau de détail** affichant nom, description, effet chiffré, coût avec barre de
progression, **temps estimé au débit courant**, débit d'absorption, et la liste des
prérequis avec leur statut individuel.

**File d'attente** visible et réordonnable.

**Un clic sur un nœud verrouillé** met en évidence les prérequis manquants plutôt que de
refuser silencieusement.

Le vocabulaire visuel arrêté ici sera repris tel quel par le futur menu radial. Seule la
mise en page changera. C'est pour cette raison que les états et le panneau doivent être
posés proprement maintenant.

---

## 9. Tests

| Fichier | Action |
|---|---|
| `ResearchSystemTests` | **réécrire entièrement** |
| `LaboratoryRuntimeTests` | **supprimer** |
| `ComputeSystemTests` | étendre — prélèvement continu, plancher à zéro |

Cas à couvrir impérativement :

- une recherche progresse à `min(absorption, disponible)`, jamais plus ;
- à zéro CU, elle se met en pause **et conserve sa progression** ;
- elle reprend exactement où elle en était quand le CU revient ;
- une recherche à prérequis multiples n'est lançable que si **tous** sont acquis ;
- une seule recherche active à la fois ;
- la file d'attente enchaîne dans l'ordre ;
- `ResearchCompleted` est levé une fois et une seule.

---

## 10. Critères d'acceptation

1. Le projet compile, tous les tests passent.
2. Plus aucune référence au RP, à la Data Card ni au Laboratoire dans le code, les assets
   ou les scènes.
3. Les cinq recherches sont atteignables en jeu depuis le `ResearchDatabase`, sans tableau
   sérialisé dans `Bootstrap.unity`.
4. Une recherche lancée sans CU suffisant se met en pause et reprend sans perte.
5. Le Data Center n'est plus constructible sans la recherche correspondante.
6. Une partie neuve permet d'enchaîner Circuit imprimé → Assembleur → Modules de calcul →
   Datacenter MK1 et d'atteindre l'amorçage.
7. `CONTRACTS.md` §10 décrit le nouveau comportement, et uniquement ce qui a changé.

---

## 11. Rapport attendu

Format de `WORKFLOW.md` §11, avec en plus :

- la liste exhaustive des fichiers et assets supprimés ;
- la correspondance ancien asset → nouvel asset pour chaque recherche ;
- l'état final de chaque `unlockResearch` du projet, en distinguant ceux qui pointent vers
  une recherche de ceux volontairement laissés à `null` ;
- le temps réel constaté pour chacune des quatre recherches obligatoires en partie, à
  comparer au calcul `coût / absorption`.

---

## 12. Suites — les recherches reportées

Chaque recherche reportée est bloquée par une mécanique précise, et c'est cette mécanique
qui détermine la tâche qui la portera. Une recherche ne se crée jamais seule : elle arrive
avec ce qu'elle débloque, testée dans la même livraison.

| Recherche | Mécanique à construire d'abord | Tâche porteuse |
|---|---|---|
| Extension de baies I et II | Datacenter à baies configurables, usure, amorçage, curseur de répartition | **Tâche 03 — Datacenter** |
| Allocation mémoire | plafond de bâtiments, compteur en top bar | **Tâche 04 — Plafond et rayon** |
| Bande passante étendue | rayon d'action modifiable en cours de partie, révélation des grappes extérieures | **Tâche 04 — Plafond et rayon** |
| Extraction renforcée | système de bridage : capacité nominale, butoir, cause affichée, levée | **Tâche 05 — Bridage** |
| Optimisation de fabrication | multiplicateur global de `computeCost` appliqué à toute production | **Tâche 05 — Bridage** |
| Convoyeur MK2 | convoyeur de second niveau, débit doublé, remplacement sur place | **Tâche 06 — Logistique** |
| Fonderie avancée | rien de bloquant techniquement, mais elle n'a de sens qu'après l'amorçage | **Tâche 03 — Datacenter** |
| Forge d'unités | branche armement, nid, unités, combat | **Tâche 08 et suivantes** |

### Deux règles pour ces suites

**Une recherche arrive avec son effet.** Ne jamais créer l'asset avant que la mécanique
existe et soit testée. Une recherche qui ne fait rien coûte du CU au joueur et ment sur sa
promesse — c'est pire qu'une recherche absente.

**Le tableau §7 d'`ALIGNEMENT_PROJET.md` reste la référence des valeurs.** Les coûts et
débits d'absorption y sont déjà fixés pour les treize recherches. Chaque tâche porteuse
reprend les siens tels quels plutôt que de les redéfinir.
