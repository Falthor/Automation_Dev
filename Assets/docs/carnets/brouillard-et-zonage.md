# Brouillard, zonage, grande carte — carnet

Ce carnet ne décrit pas ce que le système fait : cela vit dans
[`../architecture/MAP.md`](../architecture/MAP.md) (découpage, découverte, brouillard, secteurs,
portées), [`../architecture/TERRAIN.md`](../architecture/TERRAIN.md) (sol, biomes, décor),
[`../architecture/PROJECT_ARCHITECTURE.md`](../architecture/PROJECT_ARCHITECTURE.md) §10.1 (ordre de
tri) et [`../architecture/CONTRACTS.md`](../architecture/CONTRACTS.md) §14 (sauvegarde).

Il garde ce qu'aucun de ces documents ne peut porter : **les fausses pistes, les mesures qui ont
contredit une intuition, les pièges d'outillage, et les écarts assumés par rapport à une spec.**

Il couvre deux chantiers successifs — le brouillard et le zonage sur une carte de 300, puis le
passage à 10 000 avec le décor et les portées de mission.

---

## 1. Les prémisses qui ne tenaient pas

**« Le troisième usage de la même mécanique ».** La spec demandait de regarder deux mécaniques
existantes avant d'en écrire une troisième : le masque de transition de terrain et la couverture
nano. Vérification faite, `TilemapMaskTransition.shader` **n'a aucun utilisateur** — ni script, ni
matériau, ni scène. Du code mort. Il n'y avait qu'un usage vivant, et le brouillard était le
deuxième. Une généralisation à trois cas se serait construite sur un cas imaginaire.

**Le Voronoï de la directive.** Elle décrivait deux partitions incompatibles : « des points d'ancrage
dispersés, chaque case appartenant au point le plus proche » d'un côté, et de l'autre « le disque
inscrit dans ce carré » plus « sans jamais laisser voir le pavage ». Une cellule de Voronoï n'a ni
carré ni disque inscrit, et trois chiffres de la directive ne tombaient juste que sur une grille
régulière. **Rejeté au profit du pavage régulier** : le disque inscrit rend la régularité invisible,
donc la partition n'a plus besoin d'être organique pour l'être — et la grille est ce qui rend la
dérivation immédiate, là où un Voronoï aurait demandé une requête spatiale pour `IndexAt` et un
balayage pour connaître les cases d'un secteur.

**L'itérateur `CellsOf` accusé à tort.** La reconstruction de l'image de carte coûtait 20 ms, et
`DiscoveryOf` parcourait les cases via un itérateur — donc une allocation par secteur, ce que le
commentaire de `IsWhollyUnknown` juste au-dessus disait explicitement d'éviter. Coupable désigné,
mesuré : **2 ms sur 20**. La vraie cause était de tout recalculer à chaque changement au lieu de ce
qui avait changé. Le passage aux boucles simples est resté, l'allocation étant réelle — mais elle
n'était pas le coût.

## 2. Les mesures qui ont contredit une intuition

**Le grain du brouillard : un facteur 7,5 dans le mauvais sens.** J'avais posé qu'une frontière de
brouillard devait se lire *à travers plusieurs cases*, contre les dents sous la case de la
matérialisation. Le réglage à la main dit l'inverse : `noiseScale` retenu à **3** contre 0,4 proposé.
Un bruit grossier ne casse pas la lecture d'un cercle, il produit un renflement lent — un cercle
déformé reste un cercle. **`noiseWeight` déforme, `noiseScale` décide si la déformation ressemble à
une côte ou à une ellipse.** Valeurs retenues, à ne pas « rétablir » à des nombres ronds :
`borderSoftness` 0,114, `noiseScale` 3, `noiseWeight` 0,396.

**Le port CPU du biome : être plus précis que le GPU rend plus faux.** Un premier port avait divergé
du shader, et la lecture naturelle de cet échec — la mienne comme celle qui m'était proposée — était
« float32 n'est pas assez précis, passer en double ». Mesuré : le port **double** est en désaccord
avec le GPU sur **43 % des échantillons**, près du hasard ; le port **float** sur **1 sur 3 000**. La
raison est que `frac((p3x+p3y)*p3z)` sur une valeur de magnitude ~3 000 amplifie une erreur relative
de 1e-7 jusqu'à O(1) : ce qui compte n'est pas l'exactitude mais **d'être identiquement imprécis**.
La classe `BiomeField` porte cette leçon en tête pour que personne ne « nettoie » son float en double.

**Le brouillard opaque : une contrainte, pas un goût.** À `alpha = 0,96`, les 4 % qui passaient
montraient du terrain à l'intérieur de la carte mais **la skybox** au-delà de son bord — la limite du
monde se dessinait toute seule. Mesuré pixel par pixel : à alpha 1, extérieur et intérieur non
découvert donnent exactement `(0.020, 0.031, 0.051)` ; à 0,96, `(0.075, 0.098, 0.114)` contre
`(0.071, 0.059, 0.063)`. La raison est écrite dans le commentaire du champ `fogColor` lui-même, sans
quoi le prochain lecteur baissera l'alpha en croyant faire un réglage esthétique.

**Le décor : l'inquiétude portait sur la mauvaise grandeur.** La question posée était la *taille* du
jeu de deltas qu'une vieille sauvegarde produit en dégageant sous chaque bâtiment restauré. Elle est
négligeable — **440 octets** pour 200 bâtiments, 4 Ko pour 2 000. C'est le *temps* qui ne l'était
pas : **231 ms**, une saccade visible au chargement, croissant avec la base. Une mémoïsation des
chunks dérivés ramène à **3 ms**, et 2 000 bâtiments coûtent alors autant que 200.

La même mesure a rendu évidente une correction que personne ne cherchait : 1 800 cases d'emprise
balayées pour **49** vrais dégagements. Une emprise se compte en cases, le décor pousse à raison d'un
objet pour cent cases — enregistrer toute l'emprise mettait une centaine d'entrées inutiles par
rocher réellement dégagé.

**La teinte des rochers : une régression qui n'avait jamais eu lieu.** J'avais rapporté que les
grands rochers n'étaient plus teintés vers le ton du sol. Faux. L'ancienne formule était
`min(1, sol / brut)`, plafonnée parce qu'une couleur de `SpriteRenderer` ne peut qu'assombrir — et
tous les rochers sont déjà plus sombres que le sol dans les trois canaux (ton du sol
0,431/0,343/0,259 contre 0,310/0,169/0,089 pour `large_rock`). Le plafond se déclenchait partout :
**la teinte rendait blanc pour chaque rocher**, 58 sur 61 après bake. **L'enseignement n'est pas sur
la teinte** : j'avais rapporté un écart en comparant deux morceaux de code plutôt qu'en mesurant deux
images, et cet écart annoncé sans mesure a fait demander une fonctionnalité pour combler un manque
inexistant.

**Le 250 de la directive n'était pas dérivé.** Elle le présentait comme `2 × 80 + 90`, mais le rayon
maximal réellement livré est 32, donc le seuil vaut **154**. Écrire 250 en dur aurait violé la règle
que la directive posait elle-même. Un test reproduit 250 depuis (80, 90) et 290 depuis (100, 90) :
il vérifie la fonction, pas la valeur du jour.

## 3. Les pièges

**`linear: true` sur une Texture2D R8, et `FilterMode.Bilinear`.** Repris de la couverture nano
plutôt que redécouverts. Un R8 échantillonné à travers une courbe gamma n'arrive pas au shader avec
la valeur écrite, et un champ comparé à un seuil en est détruit. Le bilinéaire, lui, est ce qui fait
d'un champ binaire par case une frontière qu'un seuil peut couper ailleurs que sur un bord de case ;
en point on obtient un escalier qu'aucun bruit ne rattrape.

**`wrapMode = Clamp` sur une texture qui bouge.** Tant que la texture couvrait la carte entière, le
clamp lisait « inconnu » hors carte parce que le bord *était* inconnu. Une fenêtre qui suit la caméra
a des texels découverts sur son bord, et le clamp les étalerait vers l'extérieur en traînée de
brouillard dissipé. Le shader force `discovered = 0` hors de l'intervalle UV. **Ce défaut n'existait
pas avant que la fenêtre bouge, et il ne se serait vu qu'en pannant** — jamais sur une image fixe,
jamais dans un test qui ne déplace pas la caméra.

**`OnValidate` ne se déclenche pas à la frappe.** Unity l'appelle à l'import et au rechargement de
l'asset, pas quand on tape une valeur dans l'Inspector. Un avertissement qui n'y est écrit que là
peut donc rester invisible toute une session. **Laisser un garde-fou décoratif est pire que ne pas en
avoir** : la garde réelle est un test qui lit l'asset livré.

**`Unity_RunCommand` compile son propre extrait, pas le projet.** Un run de tests lancé juste après
une édition exécute l'ancienne assembly et rend un résultat faux. Mesuré au pire moment : **653 tests
verts pendant que `Game.Presentation` ne compilait pas**, sur un `using` manquant. Le symptôme est
apparu ailleurs — un `FindProperty` rendant `null` sur un champ pourtant présent sur le disque. Seule
la console dit l'état des assemblies. Écrit dans `DEVELOPMENT_RULES.md` §7.

## 4. Ce que la vérification a attrapé, et qu'aucun test ne voyait

**527 rochers au fond du sol.** Le décor en relief semblait un chemin mort, aucune scène ne portant
de rang cuit. `WildDecorationAutoRegenerate` relançait en fait le générateur à chaque entrée en Play,
*après* `GameRuntime.Start()` — donc le balayage d'inscription ne trouvait rien et les rochers créés
ensuite gardaient `sortingOrder = 0`, la bande sol, derrière absolument tout. Vu seulement en entrant
en Play sur la scène réelle. La fenêtre de décor a fait disparaître le problème par construction : un
sprite s'inscrit en entrant, se désinscrit en sortant, plus aucun balayage ne court après un
générateur.

**Des gisements hors carte.** J'avais annoncé que la dispersion suivrait seule le passage des
secteurs de 12 à 16, puisqu'elle lit `SectorSizeCells`. Elle suit la taille du secteur mais pas le
**rognage par le bord de carte** : 300 n'est pas un multiple de 16, donc les secteurs des deux
derniers rangs sont tronqués et `ContentsOf` dispersait hors carte. Avec 12 le cas n'existait pas,
300/12 tombant juste. C'est la liste d'impact que j'avais fournie qui l'avait manqué ; le test l'a
trouvé.

**Un test qui promettait plus qu'il ne tenait.** Mon test « traverse une frontière de région » ne
traversait rien : `371 / 16 = 23`, dont l'origine à 368 est encore dans la région 0 — la seconde
région commence à la colonne 24. Il passait en vérifiant le milieu confortable d'une seule région. Il
assère maintenant que les deux côtés du joint portent des noms de région différents, avant de
compter.

**Trois attentes inventées, démenties par la mesure.** Un test attendait 5 chunks matérialisés là où
4 étaient justes (mon premier disque en partageait un avec le second) ; un autre supposait que panner
à l'intérieur d'un chunk ne bouge pas la fenêtre, alors que c'est le *bord de la vue* qui décide, pas
le centre de la caméra ; un troisième attendait un déplacement de fenêtre par chunk traversé, alors
qu'il y en a **deux**, un par bord de vue — mesuré à 20 sur dix chunks. Le troisième a été réécrit
pour asserter le régime plutôt qu'un nombre.

**L'ordre dans `CreateAndRegister`.** Le décor est dégagé **avant** que le bâtiment prenne la case.
Rien ne lit l'occupant aujourd'hui, mais un filtre d'occupation ajouté plus tard verrait une case
déjà prise, conclurait que rien n'y poussait, n'enregistrerait aucun retrait — et le rocher
reviendrait au rechargement, sans qu'aucun test ne bouge et sans que personne ne rattache le symptôme
à une ligne écrite des mois plus tôt. **Corriger un ordre avant qu'il ne compte est le seul moment où
c'est gratuit.**

**Une suite qui n'avait pas tourné.** La suite EditMode était rouge de 13 tests avant que ce chantier
commence, aucun échec ne venant du brouillard. Deux d'entre eux étaient un test écrit sans jamais
avoir été exécuté, qui affirmait le contraire du code. **Un test écrit sans être exécuté vaut zéro** —
et le même défaut s'est reproduit plus tard avec la suite PlayMode, restée non lancée pendant
plusieurs commits pendant qu'un `sed` aveugle avait inversé le sens d'une assertion.

## 5. Les écarts assumés

**Le hash existait en trois exemplaires mot pour mot.** La scission du bruit l'a fait tomber : la
compilation a échoué sur `redefinition of 'Hash21'` — `BuildingGroundSlab.shader` portait sa propre
copie alors qu'il incluait déjà `NanoNoise.hlsl`, et `ShadedGroundTiled.shader` une troisième. C'est
la duplication la plus coûteuse possible : une fonction **dont la raison d'être est d'éviter un
défaut précis** (le banding d'un hash maison). Trois copies, c'est une correction future qui n'en
corrige qu'une sur trois, et deux shaders qui continuent de bander sans que personne ne comprenne
pourquoi.

**La règle qui en sort : partager la source d'aléa, jamais la composition.** Deux effets qui bruitent
un bord ont en commun le besoin d'un hash bien distribué, pas le nombre d'octaves — c'est ce nombre
qui donne des dents sous la case à l'un et une ondulation sur plusieurs cases à l'autre. D'où
`ValueNoise.hlsl` (hash + bruit de valeur, pour tout le monde) et `NanoNoise.hlsl` (la composition à
trois octaves, pour la matérialisation seule). Fusionner les deux *shaders* aurait au contraire
demandé un mode, et chaque mode aurait rendu la moitié des paramètres de l'autre sans objet — le cas
d'école de l'abstraction sans consommateur réel.

**Les seuils de risque sont des réglages, pas des dérivées.** Les dériver du rayon maximal d'un Noyau
les coupleraient à une valeur qui ne dit rien du danger, et qui resterait à définir si l'extension de
rayon disparaissait. Leurs défauts se lisent comme la géométrie de l'expansion : 40 cases le
territoire de départ, 250 l'anneau minier, 330 la portée d'un Noyau secondaire.

**Ce qui reste en suspens.** `DiscoveryRuntime.CaptureState` coûte 27 ms sur une carte de 10 000, et
croît linéairement — acceptable tant que la sauvegarde est manuelle, à reprendre si une sauvegarde
automatique arrive. Et le premier tracé de l'image de carte sur une partie bien explorée coûte
~170 ms, une seule fois, au chargement.

## 6. Le troisième état — ce qui est observé contre ce qui est souvenu

`DiscoveryState` attendait ce moment : il était enum plutôt que booléen pour lui. Il l'est maintenant —
`Unknown`, `Remembered`, `Observed` — et l'état courant est décrit dans `MAP.md` §2.

**Le point qui rend tout le reste simple : l'observation ne se stocke pas.** La découverte reste
permanente et acquise ; l'observation est reconstruite de zéro à chaque frame depuis la position des
observateurs, et `ObservationRuntime` ne garde rien par case. Rien à écrire quand un robot avance, rien
à effacer quand il s'éloigne : la case sort de la liste. Un état d'observation stocké serait une seconde
source de vérité, libre de contredire la position réelle des observateurs — et il faudrait le nettoyer,
ce qui est le bug que cette forme de code produit toujours.

**Conséquence : il n'y a pas de paire Capture/Restore, et cette absence est le contrat.** La première
frame après un chargement reconstruit tout le champ depuis le Noyau et les robots que le chargement a
remis. Le test qui garde ça n'assure pas « rien n'est sauvegardé » — impossible à écrire directement —
mais que ce que la découverte capture est identique à l'octet, avec ou sans observateur.

**Seulement deux des trois valeurs sont stockées.** `Remembered` garde le 1 qu'avait `Discovered`, donc
les sauvegardes existantes se relisent sans conversion ; `Observed` n'est jamais écrit dans un chunk.
`GetState` répond donc avec les deux valeurs stockées et `StateOf` avec les trois. Le renommage a coûté
cinq lignes dans un seul fichier : tout le reste du projet passait déjà par `IsDiscovered()`, ce qui est
exactement le bénéfice qu'un accesseur nommé achète.

**La découverte commande l'observation, dans cet ordre.** Une case jamais découverte reste noire même
avec un observateur dessus. Cela ne peut pas arriver en jeu — tout ce qui observe révèle aussi — mais
c'est ce qui rend « jamais découvert ne devient jamais souvenu » vrai par construction plutôt que par
un ordre d'appel heureux. La règle est appliquée deux fois exprès : dans `StateOf`, et de nouveau à
l'empaquetage des texels, pour que le shader ne reçoive jamais la contradiction.

**Deux canaux d'une texture, pas deux textures.** RG16 : R la découverte, G l'observation. Même nombre
d'octets que deux R8, et trois choses en plus — un seul upload, un seul échantillonnage, et une fenêtre
sur laquelle les deux champs ne peuvent pas être en désaccord, puisque c'est la même lecture.

**Mais les deux champs ne changent pas à la même horloge, et c'est ce qui a dicté le découpage.** La
découverte bouge rarement ; l'observation bouge dès qu'un observateur bouge, donc à chaque frame où un
robot marche. Un repack complet demande au stockage par chunks l'état de chaque texel de la fenêtre —
65 000 recherches de dictionnaire par frame à la taille livrée. D'où deux chemins : un changement de
découverte repack les deux canaux, un changement d'observation ne repack que G et relit la découverte
**dans le canal d'à côté** au lieu de la redemander. Le coût par frame retombe à quelques distances au
carré par texel.

**Le voile est le seul écart visible entre le deuxième et le troisième état**, donc c'est lui qui peut
faire disparaître toute la fonctionnalité : à 1 le souvenu se lit comme de l'inconnu et la carte n'a
plus que deux états, à 0 il n'y a pas de voile et elle n'en a que deux dans l'autre sens. Posé à 0,55,
et ça se juge à l'écran.

**Le shader prend le plus fort des deux alphas, jamais leur somme.** L'inconnu est déjà parfaitement
opaque ; lui ajouter un voile ne ferait que dépasser 1 et aplatir précisément la différence que le
troisième état existe pour dessiner. Les deux frontières sont coupées par le **même** grain en espace
monde : deux bruits indépendants auraient donné deux ondulations sans rapport, et un grain en espace
écran aurait fait ramper le bord de l'observation en suivant le robot.

**Un coût assumé.** `clip` demande maintenant que les deux termes soient dépensés, donc le sol observé
ne coûte toujours rien, mais le sol souvenu — c'est-à-dire l'essentiel de la carte explorée — paie un
blend qu'il ne payait pas. C'est le prix de la fonctionnalité, pas un oubli.

## 8. Un gisement qui existait sans appartenir à personne

Les robots ouvraient bien des gisements : la surbrillance jaune au survol se déclenchait. Mais rien
n'était dessiné, et — ce que le symptôme ne disait pas — rien n'était sauvegardé.

`SectorMaterialisation` appelait `GridRuntime.PlaceDeposit` et **jetait la valeur de retour**. Or
`PlaceDeposit` crée le `DepositRuntime` et le rend ; c'est `WorldGenerator.OreDeposits` qui est lu par
les deux seuls consommateurs qui comptent : la boucle de `GameRuntime.Start` qui instancie les vues, et
la capture de sauvegarde.

Donc le gisement était **réel pour tout ce qui interroge la grille** - le survol le trouvait, un
extracteur aurait pu être posé dessus - et **inexistant pour tout le reste**. Aucune exception, aucun
log, aucun test rouge. Le seul indice visible était une surbrillance sur du vide.

**Le correctif ne rajoute pas un appel, il déplace la frontière.** `WorldGenerator.AddDeposit` place
et enregistre en un seul appel, puis annonce par `DepositAppeared`. Un appelant n'a plus le droit
d'atteindre `PlaceDeposit` : la seule façon de faire naître un gisement passe par son propriétaire.
Ajouter un `_oreDeposits.Add(...)` à côté de l'appel existant aurait marché aujourd'hui et laissé la
même porte ouverte au suivant.

**Deux assertions, pas une.** Le test vérifie le compte dans la liste *et* le nombre d'annonces,
parce que la vue est pilotée par l'événement et la sauvegarde par la liste : n'en tenir qu'une aurait
corrigé la moitié du défaut, et la moitié restante se serait vue au rechargement suivant, des heures
plus tard.

**Et sans monde, on n'écrit rien.** Une scène sans génération de monde n'a nulle part où enregistrer
un gisement : `Materialise` retourne 0 plutôt que d'écrire dans la grille ce que personne ne
possède - ce qui est exactement la forme du défaut d'origine.
