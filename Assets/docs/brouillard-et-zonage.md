# Brouillard de guerre et zonage — carnet

Carnet d'implémentation de [`directive-brouillard-et-zonage.md`](directive-brouillard-et-zonage.md).
Les décisions prises, les écarts par rapport à la spec et pourquoi, et ce qu'il faut savoir pour
reprendre. Le document directeur reste la référence de conception ; celui-ci enregistre ce qui a
réellement été construit.

Périmètre traité : étapes 1 à 3 de la section 5. La carte dézoomée (étape 4) et les missions n'en
font pas partie.

---

## 0. La taille de la carte — écart levé avant de coder

La spec se contredit : §2 annonce 60×60 (3 600 cases), §4 raisonne sur 256×256 (400+ zones).

**Ni l'un ni l'autre. La carte fait 300×300.**

| source | valeur | statut |
|---|---|---|
| `TerrainGenerationSettings.size`, défaut C# | 60 | jamais utilisé — l'asset l'écrase |
| `Assets/Data/Terrain/DefaultTerrain.asset` | **300** | ce qui tourne |
| spec §2 | 60 | reprend le défaut C# |
| spec §4 | 256 | ne correspond à rien dans le projet |

Ce ne sont pas deux notions différentes : c'est le même champ, lu au mauvais endroit. La chaîne
autoritaire est `GameRuntime.Awake` → `TerrainRuntime(terrainSettings.Size, …)` →
`WorldGenerator.Generate(grid, Terrain.Size, …)`, et la valeur est persistée dans
`SaveData.TerrainSize` — une partie sauvegardée garde donc la taille qu'elle avait, indépendamment
de l'asset. Le Noyau est posé au centre (`mapSizeCells / 2`), soit (150, 150).

**Décision : 300 fait foi**, aucun asset modifié. Conséquences chiffrées :

- 90 000 cases d'état de découverte (et non 3 600) ;
- 625 zones à 12×12 (et non 441) — la génération paresseuse n'en est que plus nécessaire ;
- la sauvegarde ne peut pas être un tableau par case (voir §1.3).

À corriger quand l'occasion se présente : le commentaire de `SortingBands.AddressableRows` affirme
« le monde fait 60 cases aujourd'hui ». Faux, et la marge annoncée (« un ordre de grandeur ») n'est
en réalité que 1,7× sur 300. Aucun bug : 512 rangées couvrent 300, et l'ordre le plus haut calculé
(`Fog` = 8493) tient largement dans le `short` que la spec §4 s'inquiétait de dépasser.

---

## 1. Étape 1 — l'état de découverte

### 1.1 Où il vit

`Game.Grid`, à côté de `TerrainRuntime` : c'est de l'état monde par case, de la même forme, écrit
par Gameplay et lu par Presentation, donc il doit être sous les deux
(`PROJECT_ARCHITECTURE.md` §7).

- `DiscoveryState` — l'enum. Deux valeurs (`Unknown`, `Discovered`), la troisième documentée mais
  pas déclarée : la spec §7 la motive (le statique reste affiché hors observation, le vivant non),
  et l'ajouter d'avance serait une valeur que rien ne produit.
- `DiscoveryRuntime` — le tableau, les révélations, la capture.

### 1.2 Le rayon écrit, il ne définit pas

C'est le point que la spec insiste le plus (§7), et il est tenu par la structure plutôt que par la
discipline : **`DiscoveryRuntime` ne sait pas ce qu'est un Noyau**. `RevealDisc(centre, rayon)`
prend une forme géométrique, pas un bâtiment. Rien dans `Game.Grid` ne peut donc recalculer une
distance au Noyau à la lecture, parce que rien n'y a accès au Noyau.

L'écrivain est `GameRuntime.RevealDiscoveredByCore()`, appelé une fois à l'initialisation (après
les deux branches — le Noyau existe qu'il ait été généré ou restauré) puis à chaque tick, **après
`Research.Tick`** pour que l'extension du rayon soit écrite dans la frame où elle est accordée.

L'appel par tick est gardé sur le rayon (`_lastRevealedCoreRadius`) : le disque n'est parcouru que
si le rayon a bougé. « Révèle en continu » de la spec §2 est donc satisfait sans coût — le Noyau ne
se déplace pas, seul son rayon change, et il ne change qu'à une recherche.

### 1.3 La persistance : RLE, et pourquoi

`SaveData.Discovered` est une **chaîne**, pas un `JObject`, pour deux raisons :

1. `Game.Grid` ne référence que `Game.Core` et `Game.Data`. Rendre un `JObject` l'obligerait à
   dépendre de la bibliothèque JSON. Le contrat §14 a déjà ce précédent pour l'état simple :
   `ComputeSystem.RestoreReserve(float)`, `PlayClock.Restore(float?)`,
   `ConstructionService.RestoreBuildingCap(int?)`.
2. La sauvegarde est écrite en `Formatting.Indented`. 90 000 entrées à raison d'une par ligne
   pèseraient de l'ordre du mégaoctet.

Format : paires `état:longueur` séparées par des virgules, en ordre ligne par ligne. Mesuré sur la
carte réelle avec le disque de départ étendu à 32 cases : **129 segments, 713 caractères**, contre
~351 Ko pour un tableau brut avant indentation.

`RestoreState` est tolérant comme tous les autres Restore : `null`, chaîne vide, segment malformé
ou plus long que la carte laissent le reste inconnu au lieu de lever. Une sauvegarde antérieure à
ce champ se charge donc en carte vierge, et le rayon du Noyau réécrit son disque au premier tick —
le joueur ne perd que ce qu'il avait exploré au-delà.

Pas de bump de `SaveData.CurrentVersion` : champ additif avec repli par champ, exactement comme
`actionRadiusCells` et `BuildingCap` avant lui. `SaveFormatTests` épingle la nouvelle clé.

### 1.4 `Version`, pour le rendu

`DiscoveryRuntime.Version` n'avance que lorsqu'un appel a réellement changé quelque chose. C'est ce
que l'étape 2 comparera pour décider de réuploader la texture — la contrainte « la texture n'est
réuploadée que lorsque l'état a changé » se réduit alors à une comparaison d'entiers. Un disque
identique re-révélé ne le bouge pas.

### 1.5 Ce qui a été touché en périphérie

`WorldGenerator.CoreCenterCells` (nouveau, public) : le centre du Noyau en espace cases, **dérivé**
de `CoreOrigin` et du Noyau plutôt que stocké, donc identique que le monde ait été généré ou
restauré. L'expression existait déjà en local dans `Generate` ; elle y est maintenant lue depuis la
propriété plutôt que dupliquée.

---

## 2. Étape 2 — le rendu

### 2.1 « Le troisième usage » : la prémisse ne tient pas

La spec §3 demande de regarder les deux mécaniques existantes avant d'en écrire une troisième —
masque de transition de terrain et couverture nano. Vérification faite :

**`TilemapMaskTransition.shader` n'a aucun utilisateur.** Ni script, ni matériau, ni scène ne le
référence. C'est du code mort. Il n'y avait donc qu'**un** usage vivant, la couverture nano, et le
brouillard est le deuxième — pas le troisième.

### 2.2 Décision : ne pas généraliser le shader, partager le bruit

`Custom/GroundCoverage` et `Custom/FogOfWar` partagent une silhouette — échantillonner un champ en
coordonnées monde, bruiter le seuil, `clip()` — et rien d'autre :

| | couverture nano | brouillard |
|---|---|---|
| ce qui est dessiné | une teinte + un liseré lumineux | une couleur plate |
| le `clip()` | garde l'**intérieur** du champ | garde l'**extérieur** |
| découpage | une texture par zone, avec ses bornes | une seule texture globale |
| grain | ~12 périodes/unité (dents sous la case) | ~0,4 (ondulation sur plusieurs cases) |

Les fusionner demanderait un mode, et chaque mode rendrait la moitié des paramètres de l'autre sans
objet — `_RimWidth`, `_RimBoost`, `_Tint` n'ont aucun sens pour du brouillard, et un `_FogColor`
n'en a aucun pour une couverture. C'est le cas d'école de l'abstraction sans consommateur réel que
`DEVELOPMENT_RULES.md` §1 interdit.

**Ce qui est partagé, en revanche : `NanoNoise.hlsl`.** Le hash de Dave Hoskins et les octaves, pas
la mise au point. Écrire un quatrième hash maison est exactement ce qui avait produit le banding que
ce fichier existe pour éviter. L'en-tête du fichier a été amendé : il annonçait « le grain de tout
l'effet de matérialisation », ce qui devenait faux avec un appelant qui n'en fait pas partie.

**Couplage assumé et noté :** modifier les poids d'octaves de `NanoFbm` déplace aussi la bordure du
brouillard. Modifier l'échelle ou le poids de l'un des appelants, non — ce sont des paramètres par
appelant.

### 2.3 Deux pièges repris de la couverture nano plutôt que redécouverts

- **`linear: true` sur la Texture2D R8.** `GroundCoverageRenderer` documente longuement qu'un R8
  échantillonné à travers une courbe gamma n'arrive pas au shader avec la valeur écrite. Le champ du
  brouillard est comparé à un seuil : la même erreur y serait aussi destructrice.
- **`FilterMode.Bilinear`.** Le champ est binaire par case ; c'est l'interpolation entre texels qui
  en fait une frontière qu'un seuil peut couper ailleurs que sur un bord de case. En point, on
  obtient un escalier qu'aucun bruit ne rattrape.

### 2.4 Ce que le rendu ne fait plus

`FogOfWarView.Initialize` prenait `(centreMonde, rayonMonde)` et recalculait un disque. Il prend
maintenant `(DiscoveryRuntime, GridRuntime)`. Il n'y a **plus aucune référence au Noyau ni au rayon**
dans ce fichier, et l'abonnement `Research.ResearchCompleted` a disparu de `GameRuntime` : étendre le
rayon révèle des cases, et les cases révélées sont ce que le brouillard lit déjà.

### 2.5 Le quad et la texture

- Un quad centré sur la carte, agrandi de `outsideMarginCells` (400 par défaut) de chaque côté. La
  caméra est libre ; sans marge, sortir de la carte montrerait du vide non brouillardé. Hors bornes,
  l'échantillonnage se rabat sur le texel de bord — inconnu — donc le brouillard continue.
- `_MapBounds` est passé explicitement au shader plutôt que déduit de la transform du quad, comme
  `_ZoneBounds` pour la couverture. Le quad peut ainsi être plus grand que la carte sans décaler la
  correspondance texel/case.
- 300×300 en R8, soit **87 Ko**. Le tampon d'octets est alloué une fois ; `LateUpdate` ne fait qu'une
  comparaison d'entiers, et ne réuploade que si `DiscoveryRuntime.Version` a bougé.
- `texelsPerCell` exposé, à 1 par défaut comme la spec le demande. À 2, la texture passe à 360 000
  texels et la rampe d'interpolation se resserre à une demi-case.

Ordre de tri : `SortingBands.Fog` existait déjà, au-dessus de la bande d'information — rien à créer.

**Note scène :** le champ `edgeSoftness` (en unités monde, propre au disque) est devenu
`borderSoftness` (en unités de seuil). Le renommage est délibéré : `Bootstrap.unity` sérialise
`edgeSoftness: 2`, valeur qui n'a aucun sens dans la nouvelle unité et qui aurait rendu le brouillard
presque transparent si le champ avait gardé son nom. La clé orpheline disparaîtra à la prochaine
sauvegarde de la scène.

---

## 3. Dette de test soldée avant l'étape 1

La suite EditMode était rouge (13 tests) avant que ce chantier commence — elle n'avait pas tourné
depuis plusieurs lots d'équilibrage. Aucun de ces échecs ne venait du brouillard. Soldé dans un
commit séparé pour que l'étape 1 atterrisse sur une base verte :

| cause | tests |
|---|---|
| `SaveData.CoreDirectives` jamais ajouté à `ExpectedRootKeys` | 2 |
| boîtes de stockage exemptées du plafond — des tests remplissaient le plafond avec des boîtes | 3 |
| capacité robot 4 → 5 — arithmétique en dur dans deux fixtures | 2 |
| convoyeurs gratuits — un segment non payé reste en chantier, et `TryDemolish` refuse ceux-là | 2 |
| `ReserveCap` 60 000 → 70 000, plafond 50 → 52 — assertions littérales | 2 |
| cadence d'admission de la fonderie opposée à un test de capacité | 1 |
| `ACostedSegment_StillWaits…` écrit sur une prémisse fausse : la pose **est** refusée faute de stock | 1 |

Deux leçons à garder :

- **Un test écrit sans être exécuté vaut zéro.** Le dernier de la liste affirmait le contraire du
  code et personne ne l'a su.
- **`Unity_RunCommand` compile contre le domaine chargé, pas contre le disque.** Un run de tests
  lancé juste après une édition exécute l'ancienne assembly et rend un résultat faux. Vérifier
  `Unity_GetConsoleLogs` pour les erreurs, et attendre que la DLL soit plus récente que la source
  *et* que le domaine ait rechargé.
