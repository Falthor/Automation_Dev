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
| grain | ~12 périodes/unité (dents sous la case) | ~3 (voir §2.6) |

Les fusionner demanderait un mode, et chaque mode rendrait la moitié des paramètres de l'autre sans
objet — `_RimWidth`, `_RimBoost`, `_Tint` n'ont aucun sens pour du brouillard, et un `_FogColor`
n'en a aucun pour une couverture. C'est le cas d'école de l'abstraction sans consommateur réel que
`DEVELOPMENT_RULES.md` §1 interdit.

**Ce qui est partagé, en revanche : le bruit — mais le primitif seulement.** Première version : le
brouillard incluait `NanoNoise.hlsl` en entier et appelait `NanoFrontJitter`. Ça marchait, et ça
créait un couplage qu'il a fallu documenter — régler le dissolve déplaçait la bordure du brouillard.

Le couplage n'était pas nécessaire. La protection contre le banding, qui est la raison d'être de ce
fichier, tient **entièrement dans le hash de Dave Hoskins** : un hash maison montrait des bandes
périodiques, celui-ci non. Les poids d'octaves de `NanoFbm`, eux, ne protègent de rien — ce sont un
choix de composition, et c'est précisément l'identité visuelle de la matérialisation.

D'où la séparation :

| fichier | contenu | qui l'inclut |
|---|---|---|
| `ValueNoise.hlsl` | `Hash21`, `ValueNoise2D` — la source d'aléa | tout le monde |
| `NanoNoise.hlsl` | `NanoFbm`, `NanoFrontJitter` — 3 octaves à 0,62/0,27/0,11 et leur remap | les trois couches de la matérialisation, et elles seules |

Le brouillard compose ses propres octaves (`FogBorderFbm`, dans son shader). Les constantes y sont
aujourd'hui les mêmes — c'est un point de départ, pas une contrainte : elles lui appartiennent, et
les changer ne déplace plus rien d'autre. Aucun changement de rendu au moment de la scission, les
deux compositions étant arithmétiquement identiques.

`NanoHash21` et `NanoValueNoise` n'avaient aucun appelant hors de leur propre fichier ; renommés
sans préfixe en passant dans le fichier neutre, ils ne sont plus rattachés à un effet qui ne les
possède pas.

**Ce que la scission a fait tomber : le hash existait en trois exemplaires.** La compilation a
échoué sur `redefinition of 'Hash21'` — `BuildingGroundSlab.shader` portait sa propre copie mot pour
mot alors qu'il incluait déjà `NanoNoise.hlsl`, et `ShadedGroundTiled.shader` une troisième, celle
d'origine. Les trois sont désormais le même fichier.

C'est la duplication la plus coûteuse possible : une fonction dont la **raison d'être est d'éviter un
défaut précis** (le banding d'un hash maison). Trois copies, c'est une correction future qui n'en
corrige qu'une sur trois, et deux shaders qui continuent de bander sans que personne ne comprenne
pourquoi. Qu'elles compilent et qu'elles relèvent de sous-systèmes documentés séparément n'y change
rien. Le commentaire le plus complet sur le défaut — le motif en échelle — vivait dans
`ShadedGroundTiled.shader` : remonté dans `ValueNoise.hlsl` avec la contrainte de petite graine qui
l'accompagnait, et `TERRAIN.md` §2.4 pointe maintenant vers le fichier partagé.

**Règle qui en sort :** partager la source d'aléa, jamais la composition. Deux effets qui bruitent un
bord ont en commun le besoin d'un hash bien distribué, pas le nombre d'octaves — c'est ce nombre qui
donne des dents sous la case à l'un et une ondulation sur plusieurs cases à l'autre.

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

### 2.6 Réglages retenus, et l'hypothèse qu'ils ont démentie

Validés à l'écran sur la carte réelle, reportés dans `Bootstrap.unity` **et** dans les défauts de
`FogOfWarView` — ce ne sont pas des valeurs dérivées, ne pas les « rétablir » à des nombres plus
ronds :

| réglage | retenu | proposé au départ |
|---|---|---|
| `borderSoftness` | 0,114 | 0,18 |
| `noiseScale` | **3** | 0,4 |
| `noiseWeight` | 0,396 | 0,35 |
| `texelsPerCell` | 1 | 1 |

Le raisonnement initial sur l'échelle était faux, et l'écart n'est pas un ajustement : un facteur
7,5. J'avais posé qu'une frontière de brouillard doit se lire **à travers plusieurs cases**, là où la
matérialisation fait des dents sous la case à ~12 périodes/unité. Le réglage à la main dit l'inverse :
3 périodes/unité, soit du détail au tiers de case environ — quatre fois plus grossier que la
matérialisation, pas trente.

Ce qu'un bruit grossier produit en réalité, c'est un renflement lent et lisse : un cercle déformé,
qui se lit encore comme un cercle. Casser cette lecture demande du détail plus petit que ce que l'œil
suit le long du périmètre. À retenir si le rendu est repris : **`noiseWeight` déforme, `noiseScale`
décide si la déformation ressemble à une côte ou à une ellipse.**

Corollaire pour `texelsPerCell` : il reste à 1 et le grain fin vient du shader, calculé par fragment.
C'est la même leçon que `materialisation-nano.md` a tirée pour le sol — un grain cuit dans une
texture à un texel par case ne peut pas représenter 3 périodes par unité, et ressortirait en
ondulation lente puis en bord droit. Monter `texelsPerCell` affine la marche d'escalier du champ,
jamais le grain.

Les trois molettes sont réglables **en cours de partie** (`FogOfWarView.OnValidate`) ; `texelsPerCell`
aussi, il reconstruit la texture. Rappel Unity : ce qui est réglé en Play mode est perdu à l'arrêt,
d'où *Copy Component* / *Paste Component Values*.

---

## 3. Étape 3 — le zonage

### 3.1 Une contradiction dans la directive, tranchée puis supprimée

La directive mise à jour décrivait deux partitions incompatibles. §4 *Génération* : « des points d'ancrage
dispersés, chaque case appartenant au point le plus proche… des régions aux formes organiques » — un
Voronoï. §4 *La zone désigne, le disque révèle*, ajouté plus tard : « le **disque inscrit dans ce
carré**, celui qui touche le milieu de chaque côté », et « sans jamais laisser voir le **pavage**
sous-jacent ».

Une cellule de Voronoï n'a ni carré, ni disque inscrit. Trois autres chiffres de la directive ne
tombent juste que sur une grille régulière : 625 zones (300/12 = 25, 25² = 625 exactement), 12×12, et
« environ 140 cases ».

**Décision prise avec l'utilisateur : le pavage régulier.** Le disque inscrit est ce qui rend la
régularité invisible, donc la partition n'a plus besoin d'être organique pour l'être. Et la grille est
ce qui rend la génération paresseuse immédiate : index, origine, centre et cases sont de l'arithmétique
sur une coordonnée, sans rien à parcourir ni à stocker. Un Voronoï aurait demandé une requête spatiale
pour `IndexAt` et un balayage pour connaître les cases d'une zone — exactement ce que la directive
interdit plus bas.

Le paragraphe Voronoï a été **supprimé** de la directive, pas laissé à côté du nouveau. §5 étape 3
disait aussi « une zone révélée doit marquer toutes ses cases » : corrigé en « les cases de son disque
inscrit ».

### 3.2 Secteur, pas zone

`Sector`, comme la directive l'autorise : « zone » est déjà pris par les zones de signal du Noyau et
des Agents IA. Le mot n'apparaît nulle part dans le nouveau code.

### 3.3 Où vit quoi

| type | assembly | rôle |
|---|---|---|
| `SectorGrid`, `SectorDiscovery` | `Game.Grid` | géométrie pure et état dérivé des cases |
| `SectorCatalog`, `SectorIdentity`, `SectorContents`, `SectorRisk`, `SectorFeature` | `Game.Gameplay` | le sens : nom, risque, contenu |
| `SectorMissionRange`, `SectorRangeResult` | `Game.Gameplay` | la couronne |

La coupure suit celle de l'étape 1 : `Game.Grid` porte l'état monde par case, `Game.Gameplay` porte
les règles. `SectorGrid` ne connaît ni graine, ni risque, ni Noyau.

**Rien n'est stocké.** Il n'existe aucune liste de secteurs, aucun dictionnaire, aucun cache : chaque
réponse est une fonction pure de la graine et de l'index. La génération paresseuse n'est pas un
mécanisme, c'est une conséquence — les 625 secteurs existent, une partie n'en interroge que quelques
dizaines.

### 3.4 La graine : celle du terrain, pas celle des ressources

`WorldGenerator.ResourceSeed` était le choix intuitif et c'est le mauvais : **il n'est pas
sauvegardé**, et sa propre doc dit qu'il est « meaningless after RestoreState ». Une partie rechargée
aurait renommé tous les secteurs et déplacé tout contenu pas encore matérialisé.

La seule graine que la sauvegarde restaure est `SaveData.TerrainSeed`. `TerrainRuntime` la recevait
sans la garder ; elle est maintenant exposée (`TerrainRuntime.Seed`), à côté de `Size`, `TerrainScale`
et `Proportion` qu'il conservait déjà.

**Signalé, pas corrigé :** `GameRuntime.SaveCurrentGame` écrit `TerrainSeed = terrainSettings.Seed`,
c'est-à-dire la valeur de l'asset et non celle avec laquelle la partie tourne. Sans effet aujourd'hui
— `TerrainGenerationSettings.Seed` est un champ d'asset fixe, jamais tiré au hasard — mais modifier
l'asset entre deux sessions réécrirait la graine d'une sauvegarde existante, et changerait donc son
terrain **et** ses secteurs. À traiter si la graine devient un jour aléatoire par partie.

### 3.5 Les noms : une bijection, pas un tirage

16 formes × 48 compléments = **768 combinaisons**, et l'index se transforme en combinaison par
`(index × 397 + rotation) mod 768`. 397 est premier avec 768 (= 2⁸ × 3 : 397 est impair et non
multiple de 3), donc la transformation est **injective** — deux secteurs ne peuvent pas recevoir le
même nom, sans aucune vérification globale, ce qui aurait ruiné la génération paresseuse. Un tirage
aléatoire aurait collisionné constamment : 625 tirages dans 768 rendent la répétition quasi certaine.
La graine ne fait que tourner la séquence, ce qui donne des noms différents d'un monde à l'autre sans
casser l'injectivité.

Un test vérifie que 768 ≥ nombre de secteurs. **C'est lui qui tombera le jour où la carte grandira**,
au lieu de laisser apparaître des doublons.

Les compléments sont toujours introduits par une préposition — « de Fer », « des Naufragés » — et
jamais des adjectifs. C'est une décision de grammaire, pas de style : « Épave » est féminin,
« Cratère » masculin, et un adjectif tiré au sort produirait « Cratère Rouillée ». Un complément ne
s'accorde avec rien.

### 3.6 Risque, contenu, couronne

**Risque** : bande croissante avec la distance au secteur du Noyau (anneaux de Chebyshev), puis un
décalage d'une bande dans un cas sur deux tiré de la graine. Le gradient porte la lecture — une
mission lointaine doit se lire comme un pari plus gros avant tout chiffre — et le bruit empêche deux
mondes d'avoir la même carte de risque.

**Contenu** : un point d'intérêt au centre, donc toujours dans le disque révélé — une mission réussie
montre toujours ce qu'elle a trouvé. Les gisements sont dispersés dans tout le carré, coins compris,
**sans contrainte de position** : un test vérifie qu'il en tombe hors du disque, parce que c'est l'écart
entre ce qu'on voit et ce qu'on devine qui donne envie d'explorer autour. Un secteur sur huit est vide,
sinon « il y a quelque chose partout » revient à « la direction n'a pas d'importance ».

**Couronne** : `SectorMissionRange` reçoit le rayon à chaque appel et n'en garde rien, donc étendre le
rayon déplace la couronne sans autre chiffre à corriger. Les 30 cases sont une propriété, pas une
constante dans une comparaison. L'appartenance se teste sur le **centre** du secteur.

Le vidage de la couronne est traité par élargissement : tant qu'il reste moins de 3 destinations
inconnues, la limite extérieure recule d'un cran. `SectorRangeResult` rapporte `Widened` et
`Exhausted`, pour que le système de missions ne puisse jamais devenir silencieux sans dire pourquoi —
une liste vide et une carte entièrement explorée ne se ressemblent pas.

### 3.7 Ce qui n'est pas fait, et pourquoi

**La matérialisation du contenu.** La directive dit « le contenu d'une zone est généré à sa
découverte ». Le contenu est **dérivé** de façon déterministe et testé ; il n'est pas encore
transformé en `DepositRuntime` réels.

C'est un arrêt volontaire, pas un oubli, parce qu'il y a un vrai conflit à trancher : `WorldGenerator`
place déjà six grappes de gisements autour du Noyau à la génération du monde. Les secteurs proches du
Noyau sont découverts dès la première frame par le rayon — leur contenu dérivé se superposerait donc à
des grappes déjà posées. Il faut choisir : soit `WorldGenerator` cesse de placer et la zone de départ
reçoit son contenu par le même chemin que le reste, soit le contenu à la découverte ne s'applique
qu'au-delà du périmètre de départ. C'est une décision de conception, et rien ne peut découvrir un
secteur lointain tant que les missions n'existent pas.

`SectorCatalog.ContentsOf` est la couture : elle donne déjà la réponse, il ne manque que qui l'appelle
et ce qu'il en fait.

### 3.8 `SortingBands`, corrigé comme la directive le demande

Le commentaire affirmait deux choses fausses : « le monde fait 60 cases » et une marge « d'un ordre de
grandeur ». Vérifié : `Steps` = 2048, `SortedLast` = 8291, `Fog` = **8493** contre 32 767 pour un
`short` — 3,9× de marge, le `short` n'est pas le risque. Le vrai plafond est `AddressableRows` = 512
contre 300 cases, soit **1,7×**, et il casse en silence : `Sorted()` clampe, donc tout ce qui
dépasserait la rangée 512 s'écraserait sur un seul ordre et cesserait d'être trié par profondeur. Le
commentaire dit maintenant ça.

---

## 4. Dette de test soldée avant l'étape 1

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
