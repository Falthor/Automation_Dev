# Brouillard de guerre et zonage — carnet

> **AVERTISSEMENT — carnet partiellement périmé depuis la décision de passer la carte à 10 000.**
>
> Ce carnet décrit une implémentation dimensionnée pour une carte de 300. La taille cible est
> désormais **10 000 cases de côté**, avec des **chunks de 64** et des **secteurs de 16**. Six points
> de ce carnet ne tiennent plus. Ils sont traités dans `directive-grande-carte.md`, qui fait
> autorité sur tout ce qui touche à l'échelle.
>
> | Ce que dit ce carnet | Ce qui le remplace |
> |---|---|
> | 625 secteurs, vocabulaire de noms de 768 combinaisons | **390 625** secteurs : la bijection ne suffit plus, vocabulaire ou méthode à revoir |
> | couronne de mission « rayon du Noyau + 30 » | **deux portées** : exploration à 250 et au-delà, minière entre le rayon courant et 250, toutes deux dérivées |
>
> **Traité depuis :** les rangs de profondeur sont devenus relatifs à la caméra (§3.9), le découpage est passé aux secteurs de 16 alignés sur des chunks de 64 (§3.10), l'état de découverte est devenu épars (§3.11) et la texture du brouillard suit la caméra (§3.12).
>
> Restent valables sans réserve : la séparation « le rayon écrit, il ne définit pas », le RLE de
> sauvegarde, la scission `ValueNoise.hlsl` / `NanoNoise.hlsl`, le `linear: true` sur la texture R8,
> le test d'orientation sur sonde asymétrique, et la décision de ne pas généraliser le shader.
>
> Ce qui n'était pas fait le reste : la matérialisation du contenu en gisements réels, et l'étape 4,
> la carte dézoomée.


> **L'état courant du sous-système est décrit dans
> [`architecture/MAP.md`](architecture/MAP.md)**, qui fait autorité. Ce carnet garde le *pourquoi* :
> les décisions prises, les écarts par rapport aux spécifications, les pièges rencontrés et les
> mesures qui ont tranché. Pour savoir ce que fait le code aujourd'hui, lire `MAP.md` ; pour savoir
> pourquoi il le fait ainsi, lire ici.

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
entre ce qu'on voit et ce qu'on devine qui donne envie d'explorer autour.

Les secteurs vides ne relèvent plus d'un tirage indépendant « un sur huit » : voir 3.7bis, la densité
est désormais stratifiée par blocs. L'intention reste la même — s'il y a quelque chose partout, la
direction n'a pas d'importance — mais elle est portée par le nombre de grappes par bloc.

**Couronne** : `SectorMissionRange` reçoit le rayon à chaque appel et n'en garde rien, donc étendre le
rayon déplace la couronne sans autre chiffre à corriger. Les 30 cases sont une propriété, pas une
constante dans une comparaison. L'appartenance se teste sur le **centre** du secteur.

Le vidage de la couronne est traité par élargissement : tant qu'il reste moins de 3 destinations
inconnues, la limite extérieure recule d'un cran. `SectorRangeResult` rapporte `Widened` et
`Exhausted`, pour que le système de missions ne puisse jamais devenir silencieux sans dire pourquoi —
une liste vide et une carte entièrement explorée ne se ressemblent pas.

### 3.7 La matérialisation du contenu — décision prise

Le contenu est dérivé de façon déterministe et testé, mais pas encore transformé en `DepositRuntime`
réels. L'arrêt était volontaire : il fallait trancher le conflit entre les six grappes que
`WorldGenerator` place autour du Noyau et le contenu dérivé des secteurs proches, découverts dès la
première frame par le rayon.

**La règle retenue n'est pas géométrique mais fondée sur la donnée : un secteur qui porte du contenu
placé garde ce contenu ; la dérivation ne remplit que les secteurs qui n'en ont aucun.**

Pas de test de périmètre, pas de rayon de départ à maintenir. La zone de départ reste composée à la
main, ce qui est nécessaire — l'introduction dépend d'avoir les bonnes ressources à la bonne
distance, et une dérivation aléatoire ne le garantirait pas. La formulation couvre aussi ce qui
viendra : une épave scénarisée, un nid particulier ou un secteur d'événement posés n'importe où
échappent à la dérivation par le seul fait d'exister.

`SectorCatalog.ContentsOf` reste la couture ; il manque qui l'appelle et ce qu'il en fait.

### 3.7bis La densité des gisements dérivés : stratifiée, pas tirée secteur par secteur

Exigence posée : au-delà des six grappes du Noyau, la répartition doit **changer d'une partie à
l'autre** — une grappe ne tombe pas forcément dans le même secteur — tout en garantissant un
**nombre minimum de grappes** dans ce que le joueur peut explorer.

Un tirage indépendant par secteur ne le garantit pas. Chaque secteur déciderait seul, et une
mauvaise série laisserait une région entière stérile : irreproductible, et incorrigible par un
réglage.

**La parade est la stratification.** Les secteurs sont regroupés par blocs — 3×3 secteurs — et chaque
bloc contient un nombre fixe de grappes, dont les secteurs porteurs sont tirés depuis la graine du
bloc. Ce que ça donne :

- densité garantie partout, y compris dans la couronne accessible aux missions, sans aucun comptage
  global
- répartition variable avec la graine : d'une partie à l'autre, ce ne sont pas les mêmes secteurs qui
  portent les grappes
- dérivation toujours en O(1) et paresseuse : pour connaître un secteur, on calcule son bloc, on
  dérive quels secteurs y sont porteurs, et on regarde si celui-ci en fait partie. Aucun registre,
  aucun balayage

**La variété compte autant que la densité.** Un bloc donnant trois grappes de fer et aucun cuivre
bloquerait le joueur autant qu'un bloc vide. Les types se tirent **sans remise** dans le bloc.

La taille du bloc et le nombre de grappes par bloc sont des **réglages exposés**, pas des constantes :
c'est le levier d'équilibrage de la densité de ressources, à ajuster une fois l'introduction mesurée.

Cette stratification remplace la règle « un secteur sur huit est vide » notée en 3.6, qui relevait
d'un tirage indépendant.

### 3.8 `SortingBands`, premier passage

Le commentaire affirmait deux choses fausses : « le monde fait 60 cases » et une marge « d'un ordre de
grandeur ». Vérifié : `Steps` = 2048, `SortedLast` = 8291, `Fog` = **8493** contre 32 767 pour un
`short` — 3,9× de marge, le `short` n'est pas le risque. Le vrai plafond est `AddressableRows` = 512
contre 300 cases, soit **1,7×**, et il casse en silence : `Sorted()` clampe, donc tout ce qui
dépasserait la rangée 512 s'écraserait sur un seul ordre et cesserait d'être trié par profondeur. Le
commentaire dit maintenant ça.

### 3.9 Les rangs deviennent relatifs à la caméra

Le §3.8 ci-dessus a corrigé un commentaire faux et mesuré la vraie marge. La marge n'était pas le
problème : **le schéma lui-même ne passe pas l'échelle.** 10 000 cases à 4 pas et 4 sous-couches
demandent 160 000 valeurs pour un `short` borné à 32 767, et l'échec est silencieux — `Sorted()`
écrête, donc tout ce qui dépasse s'écrase sur un seul rang et cesse d'être trié, sans une seule
erreur.

**Ce qui rend la parade possible est une contrainte de jeu déjà posée : le dézoom est plafonné**
(`maxOrthographicSize` = 30, soit 60 cases de haut au maximum). Seuls les objets simultanément
visibles ont besoin d'être ordonnés entre eux. `DepthSortLadder` classe donc contre une **fenêtre de
256 cases qui suit la caméra**, et la taille de la bande ne dépend plus de celle du monde : une carte
de 300 et une carte de 10 000 coûtent les mêmes 4 096 rangs.

**Le panoramique ne reclasse rien.** Tous les rangs se décalent de la même quantité quand la fenêtre
bouge, donc leur comparaison — la seule chose qu'Unity lit — est inchangée. Les rangs ne sont
recalculés qu'à un ré-ancrage, soit environ tous les 98 cases de déplacement vertical, pas par frame.

**Le compromis, assumé et testé :** deux objets loin hors de la fenêtre s'écrasent sur le même rang.
Ils sont hors écran, et c'est précisément ce qu'on échange contre un rang borné à n'importe quelle
taille de carte.

**Ce que ça interdit désormais : cuire un rang de la bande triée dans une scène.** Un nombre figé
n'est vrai que pour la fenêtre contre laquelle il a été calculé. Le décor en relief porte donc un
marqueur `DepthSortedDecor` et `GameRuntime` l'inscrit sur l'échelle au démarrage. Le test qui
recalculait les rangs cuits pour les comparer a changé de nature : il vérifie maintenant qu'**aucune
scène ne porte de rang de la bande triée**, ce qui est à la fois plus simple et plus fort.

**Un bug attrapé en vérifiant plutôt qu'en supposant.** Le décor en relief semblait être un chemin
mort — aucune scène ne contient de rang cuit. Il ne l'est pas : `WildDecorationAutoRegenerate`
relance le générateur **à chaque entrée en Play**, et il attend que `World` et `Grid` existent, donc
il tourne *après* `GameRuntime.Start()`. Le balayage d'inscription placé dans `Start()` ne trouvait
donc rien, et les 527 rochers créés ensuite gardaient `sortingOrder = 0` — la bande sol, derrière
absolument tout. Le balayage est devenu `GameRuntime.RegisterSceneDepthSortedDecor()`, public, appelé
aussi à la fin de `RegenerateAll()`.

Vérifié en Play sur la scène réelle, pas seulement en test : 527 marqueurs, **527 dans la bande
triée, 0 resté au sol**. Autour du Noyau, au-delà d'un pas de quantification : **51 rochers
correctement devant, 76 correctement derrière, 0 erreur**. Deux objets à moins de 0,25 case l'un de
l'autre partagent leur rang — c'est la quantification à 4 pas par case, inchangée depuis toujours.

**Une instance, pas un statique mutable.** Le projet tourne avec Domain Reload désactivé
(`DEVELOPMENT_RULES.md` §5) : une origine de fenêtre en `static` survivrait à la session de jeu et
distribuerait des rangs mesurés contre une caméra qui n'existe plus. `GameRuntime` possède l'unique
échelle et la passe aux trois spawners.

Mesuré avant de concevoir : **aucune scène ne portait de rang dans la bande triée** (tous à 0 ou 3),
donc la bande est entièrement peuplée à l'exécution et le registre à rafraîchir est petit.

### 3.10 Secteurs de 16, et la fin des constantes de découpage

**16 et non 12, pour une seule raison : 4×4 secteurs pavent exactement un chunk de 64.** 12 ne tombait
sur aucune frontière de chunk. Le disque inscrit passe donc d'un rayon de 6 à 8, et révèle 201 cases
au lieu de 113.

**`DefaultSectorSizeCells` a disparu, sans remplaçant.** C'était le troisième chemin que la directive
§4.8 interdit : une valeur dans le code à côté de la même valeur dans un réglage. `SectorGrid` exige
maintenant sa taille de secteur — pas de défaut du tout, donc un appelant qui oublie de la passer ne
compile pas, au lieu d'être silencieusement en désaccord avec l'asset. Même chose pour les seuils de
risque de `SectorCatalog`.

Les valeurs vivent dans **`Assets/Data/World/SectorSettings.asset`** : chunk 64, secteur 16, et les
trois seuils de risque. L'asset vérifie lui-même que le secteur divise le chunk, et que les seuils
sont croissants.

#### Le risque se mesure en cases, plus en anneaux de secteurs

C'était le seul changement de **comportement de jeu** du redimensionnement, et il était invisible :
`RiskOf` comptait des anneaux de secteurs (`ring <= 1 / <= 3 / <= 6`), donc passer de 12 à 16 étirait
tout le gradient de danger d'un tiers sans qu'aucun test ne puisse le voir.

La correction n'est pas de recalibrer les anneaux mais de **cesser de compter dans une unité de
circonstance**. Le risque est une propriété du monde ; il se mesure en distance au Noyau, en cases, et
veut dire la même chose quel que soit le découpage. Un test le verrouille : à distance égale, le
risque est le même que les secteurs fassent 8 ou 16.

**Les seuils sont des réglages d'équilibrage, pas des dérivées.** Les dériver du rayon maximal d'un
Noyau les coupleraient à une valeur qui ne dit rien du danger — et qui resterait à définir si
l'extension de rayon disparaissait. Leurs défauts se lisent comme la géométrie de l'expansion :

| bande | jusqu'à | ce que ça veut dire |
|---|---|---|
| Faible | 40 cases | le territoire de départ du joueur |
| Modéré | 250 | l'anneau minier |
| Élevé | 330 | aussi loin qu'un Noyau secondaire porte |
| Critique | au-delà | — |

Sur la carte actuelle de 300, le centre est à 212 cases du coin le plus éloigné : **Élevé et Critique
sont hors d'atteinte**. C'est attendu — ces défauts visent la carte de 10 000.

#### Un bug que la liste d'impact avait manqué

J'avais annoncé que la dispersion des gisements suivrait seule, puisqu'elle lit `SectorSizeCells`.
Faux : elle suit la taille du secteur mais pas le **rognage par le bord de carte**. 300 n'est pas un
multiple de 16, donc les secteurs des deux derniers rangs sont tronqués, et `ContentsOf` dispersait
des gisements hors carte — `IndexAt` renvoyait -1. Avec 12 le cas n'existait pas, 300/12 tombant
juste. `ContentsOf` calcule maintenant l'étendue réelle du secteur avant de tirer.

### 3.11 L'état de découverte devient épars

Un octet par case alloué au lancement fait 100 Mo sur une carte de 10 000 — pour une carte qui restera
inconnue à 99 % pendant toute la partie. `DiscoveryRuntime` stocke maintenant **un tableau par chunk,
créé à la première écriture**. Un chunk où personne n'a jamais rien révélé n'existe pas, et répondre
« inconnu » pour ses cases ne coûte rien.

**Le défaut d'un chunk absent est le seul vrai risque, et il est verrouillé par test :** un chunk
absent vaut **inconnu**, jamais découvert. Alloué à `Discovered`, un seul chunk révélerait 4 096 cases
d'un coup, et la carte entière dès qu'on la touche.

**Rien n'est visible de l'extérieur.** C'est la contrainte que la directive §3.1 pose, et elle est
tenue : aucune signature n'a changé sauf le constructeur, qui reçoit la taille de chunk en `int` —
`Game.Grid` ne doit pas dépendre de `Game.Data`, même raison que `TerrainRuntime`.

**La forme sauvegardée est inchangée**, ce qui compte puisque `CONTRACTS.md` §14 l'épingle. Un test le
prouve autrement qu'en relisant le code : deux cartes avec les mêmes révélations mais des tailles de
chunk différentes (64 et 8) produisent **la même chaîne**. La capture saute les chunks absents en bloc
plutôt que case par case, et la restauration n'en matérialise aucun pour les segments inconnus — c'est
là que se trouve l'économie sur une carte majoritairement noire.

Mesuré par test : révéler le disque de départ sur une carte de 300 et sur une carte de **10 000**
matérialise exactement le même nombre de chunks. Le coût suit ce qui a été exploré, plus la taille du
monde.

Une erreur au passage, dans mon propre test : j'attendais 5 chunks là où 4 étaient justes, ayant placé
le premier disque dans un chunk que le second touchait déjà.

### 3.12 La texture du brouillard suit la caméra

Une texture d'un texel par case couvrant la carte pèse 16 Mo à 4 000 et dépasse la taille maximale de
beaucoup de GPU au-delà de 8 192. La parade vient de la même contrainte de jeu que les rangs de tri :
**le dézoom est plafonné**, donc le joueur ne voit jamais qu'une portion bornée du monde. La texture
couvre une **fenêtre de 256 cases** qui suit la caméra, et sa taille cesse de dépendre de celle du
monde.

Mesuré en Play : **256×256, soit 64 Ko**, contre 300×300 auparavant — et ce serait toujours 64 Ko sur
une carte de 10 000.

**Le ré-ancrage est rare.** Mesuré : 20 cases de panoramique ne bougent rien, 140 cases produisent
**un seul** ré-ancrage. Le reste du temps c'est une comparaison par frame.

**Un piège que le passage à une fenêtre crée de toutes pièces.** `wrapMode = Clamp` faisait lire
« inconnu » hors carte parce que le bord de la texture *était* inconnu. Une fenêtre qui bouge a des
texels découverts sur son bord, et le clamp les étalerait vers l'extérieur en une traînée de
brouillard dissipé. Le shader force donc `discovered = 0` hors de l'intervalle UV : ce qui n'a pas
d'état est inconnu, et hors de la fenêtre il n'y a pas d'état du tout. Sans effet à l'écran, la
fenêtre contenant toujours la vue.

**Ce défaut n'existait pas avant que la fenêtre bouge, et il ne se serait vu qu'en pannant** — jamais
sur une image fixe, jamais dans un test qui ne déplace pas la caméra. C'est la catégorie de bug que
seul un essai en mouvement attrape, et c'est la raison pour laquelle le ré-ancrage a été éprouvé sur
140 cases de panoramique plutôt que sur une capture.

#### Le brouillard devient opaque

La directive §2 exige que « l'extérieur de la carte reste opaque, comme l'intérieur non découvert ».
Il ne l'était pas : à `alpha = 0,96`, les 4 % qui passaient montraient un peu plus de terrain à
l'intérieur — ce qui se lisait comme de l'atmosphère — mais **la skybox de la caméra** au-delà du
bord du monde. Un lavis bleu là où le sol inconnu était brun : la limite de la carte se dessinait
toute seule pour le joueur.

Mesuré pixel par pixel : à `alpha = 1`, l'extérieur de la carte et l'intérieur non découvert donnent
exactement `(0.020, 0.031, 0.051)`. À 0,96 ils donnent `(0.075, 0.098, 0.114)` et
`(0.071, 0.059, 0.063)` — visiblement différents.

**L'alpha à 1 cesse d'être un choix esthétique pour devenir une contrainte fonctionnelle**, et c'est
la phrase à retenir. Tant que le terrain existait partout, un brouillard légèrement translucide
laissait deviner un paysage : discutable, inoffensif. Après la génération paresseuse il laissera voir
**le néant** — il n'y aura pas de terrain dessous à deviner.

Quiconque voudra un jour adoucir le brouillard doit trouver cette raison à côté de la valeur, sinon
il la baissera en croyant faire un réglage de goût. Elle est donc écrite dans le commentaire du champ
`fogColor` lui-même, pas seulement ici.

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
