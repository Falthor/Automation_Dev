# Tâche 04 — Plafond de bâtiments et rayon d'action

> **État : réalisée, avec un correctif.**
>
> Plafond de bâtiments (40 → 52 par `memory_allocation`) et rayon d'action du Noyau (22 → 32,
> corrigé depuis 30 - voir §4.2) implémentés comme état runtime, extensibles par la recherche,
> persistés directement (`ConstructionService.BuildingCap`, `CoreRuntime.ActionRadiusCells`),
> avec repli sur les valeurs de départ pour une sauvegarde antérieure.
>
> Correctif : `FogOfWarView` révélait exactement le rayon constructible (22), masquant
> intégralement les grappes d'invitation dès le départ - la mécanique d'invitation n'existait
> pas. La bande de placement des grappes (28-34) dépassait aussi le rayon étendu (32) sur
> environ 70 % des tirages. Le brouillard révèle désormais `ActionRadiusCells +
> fogRadiusMarginCells` (10, valeur nommée) et suit la recherche sans rechargement ; la bande
> de placement est resserrée à 26-29. Vérifié sur dix seeds. Voir le rapport pour le détail.

**Objectif : rendre réelles les deux contraintes spatiales de l'introduction — le plafond de
bâtiments et le rayon d'action du Noyau — et les rendre extensibles par la recherche.**

Référence : `ALIGNEMENT_PROJET.md` §3.4, §8 et §9, GDD §3.4 et §4.1.

Prérequis : les tâches 01, 01B, 02 et 03 sont livrées.

## 1. Pourquoi maintenant

Deux contraintes structurent toute l'introduction et aucune des deux n'existe dans le code.

Le plafond de bâtiments est cité partout — 31 slots occupés sur 40 disponibles (le Storage
Box ne fait plus partie du parc de référence depuis la tâche 01B), l'erreur de
surdimensionnement qui se paie en emplacements, la top bar qui affiche le compteur. Il n'a
jamais été implémenté. Le joueur peut aujourd'hui construire sans limite, et la leçon du
surdimensionnement ne peut pas être donnée.

Le rayon d'action existe mais il est figé. Depuis la tâche de placement des gisements, trois
grappes d'invitation sont posées entre 28 et 34 cellules (pas 25-28 comme énoncé ici
initialement - mesuré en jeu, voir le rapport de la première livraison de cette tâche), hors
du rayon de 22 — visibles et définitivement inexploitables, puisque rien ne peut étendre le
rayon. La promesse est affichée au joueur sans qu'aucune mécanique ne puisse la tenir.

## 2. Périmètre

Modifiable : `Game.Construction` (validation de placement, comptage, refus au plafond),
`CoreRuntime` ou l'équivalent porteur du rayon d'action, `CoreDefinition` (la valeur y devient
la valeur de départ, plus la valeur courante), `ActionRadiusView`, `FogOfWarView`,
`WorldGenerator` (bande de placement des grappes d'invitation), `TopBarController` (son
`.uxml`, son `.uss`), assets de recherche à créer, sauvegarde (le rayon et le plafond
deviennent de l'état runtime), tests concernés.

Non modifiable : `DataCenterRuntime`, `ComponentInstance` (livrés en tâche 03),
`ResearchSystem`, le système de bridage (tâche 05).

**Correction au périmètre** : `FogOfWarView` et `WorldGenerator` étaient déclarés non
modifiables dans la version initiale de ce ticket - à tort. Le brouillard reste purement
visuel et son mécanisme (cercle statique) n'a pas changé, mais son rayon doit suivre le rayon
d'action avec une marge, sans quoi les grappes d'invitation que cette tâche rend
constructibles restent masquées avant de l'être. C'est une correction de ce ticket, pas un
élargissement de périmètre.

## 3. Le plafond de bâtiments

### 3.1 Règles

| Élément | Règle |
|---|---|
| Valeur de départ | 40 |
| Comptent | tous les bâtiments occupant un slot |
| Ne comptent pas | convoyeurs, splitters, crossroads |
| Le Core | ne compte pas — il est posé au démarrage et n'est pas une décision du joueur |
| Démolition | libère l'emplacement immédiatement |

Le parc de référence de l'introduction occupe 31 emplacements. La marge de neuf est
délibérée : elle laisse au joueur la place de se tromper sans l'enfermer, tout en rendant la
limite perceptible dans la dernière ligne droite.

### 3.2 Comportement au plafond

Au plafond, l'outil de construction refuse la pose avec un message explicite — plafond
atteint, et non un échec silencieux ou un refus générique. Le compteur de la top bar passe en
couleur d'alerte à l'approche.

C'est un point de conception, pas de confort : la limite doit être comprise au moment où elle
mord, sinon le joueur cherche la cause ailleurs.

### 3.3 Recherche

| id | Nom | cuCost | Absorption | Prérequis | Effet |
|---|---|---|---|---|---|
| `memory_allocation` | Allocation mémoire | 2 500 | 40 CU/s | `datacenter` | plafond 40 → 52 |

## 4. Le rayon d'action

### 4.1 Ce qui change

Le rayon est aujourd'hui lu directement depuis `CoreDefinition.actionRadiusCells`. Il doit
devenir de l'état runtime, initialisé depuis la définition et modifiable en cours de partie.

`CoreDefinition.actionRadiusCells` conserve sa valeur de 22 et devient la valeur de départ,
pas la valeur courante. Toute lecture du rayon dans la validation de placement doit passer par
l'état runtime.

### 4.2 Recherche

| id | Nom | cuCost | Absorption | Prérequis | Effet |
|---|---|---|---|---|---|
| `extended_bandwidth` | Bande passante étendue | 3 000 | 45 CU/s | `datacenter` | rayon 22 → 32 cellules |

Cible corrigée à 32, pas 30 : mesure en jeu (assets réels, seed fixe), les trois grappes
d'invitation générées par `WorldGenerator` s'étendaient à l'origine jusqu'à environ 32
cellules du Noyau (fer 31,3 - cuivre 30,3 - charbon 31,9) avec la bande de placement 28-34,
pas 25-28 comme supposé ici initialement. Un rayon de 30 n'aurait rendu chaque grappe que
partiellement exploitable, et une grappe tirée entre 32 et 34 aurait été définitivement
inatteignable quelle que soit la seed (probabilité qu'une bande de 6 cellules tombe
entièrement dans les 4 premières : environ 30 % avec les trois grappes). La bande de
placement a donc été resserrée à **26-29** (`WorldGenerator.InvitationMinDistanceCells`/
`MaxDistanceCells`) : elle tient désormais entièrement au-delà du rayon de départ (22) et en
deçà du rayon étendu (32) avec marge, quelle que soit la seed - vérifié sur dix seeds dans le
rapport de correction. Le passage à 32 rend les trois grappes d'invitation intégralement
exploitables. C'est la raison d'être de cette recherche et le premier moment où le joueur
récolte ce qu'il regardait depuis le début.

### 4.3 Répercussions

`ActionRadiusView` doit refléter le nouveau rayon immédiatement à la complétion de la
recherche, sans rechargement.

`FogOfWarView` doit révéler, dès le départ, une zone plus large que le rayon constructible -
`ActionRadiusCells + fogRadiusMarginCells` (marge nommée, actuellement 10), pas
`ActionRadiusCells` seul - sinon les grappes d'invitation restent masquées avant même d'être
constructibles, ce qui vide la mécanique d'invitation de tout effet. Ce rayon doit lui aussi
suivre la complétion de `extended_bandwidth` sans rechargement, exactement comme
`ActionRadiusView`. Déclarer `FogOfWarView` hors périmètre dans la version initiale de ce
ticket était une erreur (voir §2) : son mécanisme visuel ne change pas, seul son rayon doit
suivre le rayon d'action au lieu d'en être indépendant.

Vérifier l'ordre de résolution : rien dans la validation de placement, dans la construction ou
dans la présentation ne doit continuer à lire la valeur de la définition une fois le rayon
devenu runtime. C'est le défaut le plus probable de cette tâche — une lecture oubliée qui
laisse le joueur voir un cercle agrandi sans pouvoir y bâtir.

## 5. Interface

La top bar affiche le compteur de bâtiments — 31/40 — dès le début de partie, à côté du stock
de CU et de son autonomie.

C'est la deuxième information de la top bar en phase de survie, conformément au principe de
révélation progressive : le joueur n'a jamais plus de deux chiffres à comprendre à la fois.

Le compteur passe en couleur d'alerte à l'approche du plafond. À l'atteinte, le refus de pose
renvoie un message nommant explicitement la cause.

## 6. Sauvegarde

Le plafond courant et le rayon courant deviennent de l'état persisté.

Restore doit tolérer leur absence et retomber sur les valeurs de départ — 40 et 22 — plutôt
que de supposer la forme d'une sauvegarde antérieure. Une sauvegarde d'avant cette tâche doit
se charger sans exception, ou être refusée avec un message clair conformément à la décision de
version prise en tâche 03.

## 7. Tests

- un bâtiment comptant pour un slot est refusé au plafond, avec la cause correcte ;
- un convoyeur, un splitter et un crossroad restent posables au plafond ;
- le Core ne compte pas dans le total ;
- la démolition libère un emplacement immédiatement ;
- `memory_allocation` porte le plafond de 40 à 52 ;
- `extended_bandwidth` porte le rayon de 22 à 32 (corrigé depuis 30 - voir §4.2) ;
- après extension, une cellule située à 27 du Noyau devient constructible alors qu'elle était
  refusée avant ;
- la validation de placement lit le rayon runtime et non celui de la définition ;
- aller-retour Capture/Restore du plafond et du rayon, et Restore sur un blob amputé de ces
  deux clés ;
- sur au moins dix seeds différentes, les trois grappes d'invitation sont intégralement sous
  le brouillard révélé au démarrage, intégralement hors du rayon constructible de départ, et
  intégralement constructibles après `extended_bandwidth` (correction de ce ticket - voir §4.3).

Le test des 27 cellules est le plus important pour le rayon constructible ; le test multi-seed
est le plus important pour la bande de placement et le brouillard, puisque c'est un défaut qui
ne se voyait pas sur la seed courante et qui serait revenu au premier changement de génération.

## 8. Critères d'acceptation

- Le projet compile, tous les tests passent.
- Une partie neuve démarre avec un plafond de 40 et un rayon de 22.
- La top bar affiche le compteur de bâtiments dès la première seconde.
- Au plafond, la pose est refusée avec un message nommant la cause.
- Convoyeurs, splitters et crossroads restent posables sans limite.
- Les trois grappes d'invitation sont visibles sous le brouillard dès le démarrage, quelle
  que soit la seed (correction de ce ticket - §4.3).
- Après `extended_bandwidth` (rayon 22 → 32, corrigé depuis 30 - voir §4.2), les trois
  grappes d'invitation sont exploitables jusqu'au dernier sous-gisement, le cercle affiché
  correspond à la zone réellement constructible, et le brouillard suit sans rechargement.
- Après `memory_allocation`, le compteur affiche bien 52 comme plafond.
- Une sauvegarde antérieure se charge sans exception ou est refusée explicitement.

## 9. Rapport attendu

Format de `WORKFLOW.md` §11, avec en plus :

- la liste exhaustive des endroits où le rayon était lu depuis la définition, et ce qui a été
  fait de chacun ;
- le nombre de bâtiments réellement comptés dans une partie menée jusqu'au Datacenter, comparé
  aux 31 attendus ;
- toute catégorie de bâtiment dont l'appartenance au comptage aurait demandé un arbitrage.
