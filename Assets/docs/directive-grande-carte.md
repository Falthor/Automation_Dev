# Grande carte — génération, stockage et chargement

Document directeur. Il traite d'une seule question : ce qui change quand la carte passe de 300 à
plusieurs milliers de cases de côté. Il ne décrit ni le brouillard ni le zonage, qui ont leur propre
document — il dit seulement ce que leur implémentation actuelle ne supporte pas au-delà d'une
certaine taille.

---

## 1. Pourquoi ce document

La carte fait 300 aujourd'hui parce que rien ne justifiait plus grand. **La taille cible est
tranchée : 10 000 cases de côté**, soit 100 millions de cases, 24 336 chunks de 64 et 390 625
secteurs de 16.

À cette échelle, aucun des trois systèmes dimensionnés pour 300 ne tient, et un quatrième casse que
le document ne prévoyait pas : les rangs de tri par profondeur.

Trois systèmes ont été dimensionnés pour 300 et ne tiendront pas. Ils sont **indépendants** et
peuvent être traités séparément, dans l'ordre indiqué.

| Système | Tient jusqu'à | Ce qui casse au-delà |
|---|---|---|
| **Rangs de tri par profondeur** | **~2 000** | **`sortingOrder` est borné à ±32 767 ; 10 000 cases en demandent 160 000** |
| Texture du brouillard | ~2 000 | mémoire vidéo, puis taille maximale de texture |
| État de découverte | ~4 000 | 16 Mo à 4 000, 100 Mo à 10 000, alloués au lancement |
| Découpage secteur 12 | — | ne tombe juste sur aucun chunk : passe à 16 |
| Génération du terrain | ~2 000 | durée de lancement et mémoire, pour 97 % de terrain jamais regardé |

### Les rangs de tri deviennent relatifs à la caméra

Le rang de profondeur est calculé depuis la rangée en Y monde, quantifié à 4 pas par case avec un
stride de 4 sous-couches. Sur 300 cases cela faisait environ 4 800 valeurs, largement dans le
`short` que Unity utilise. Sur 10 000 cases il en faudrait 160 000 : le schéma ne tient plus, et il
casse en silence puisque la valeur est écrêtée.

**La parade est de calculer le rang relativement à la caméra, pas en absolu.** Seuls les objets
simultanément visibles ont besoin d'être ordonnés correctement entre eux, et le dézoom plafonné
borne cette étendue. Le rang devient une fonction de la distance à l'origine de la vue courante, et
la plage nécessaire ne dépend plus de la taille du monde.

C'est un changement de conception et non un ajustement de constante : à traiter **avant** le
renumérotage des sorting orders, sans quoi celui-ci partirait sur une hypothèse fausse.

Repères chiffrés, un octet par case :

| Côté | Cases | État de découverte | Secteurs 12×12 | Sites de Noyau à 250 |
|---|---|---|---|---|
| 300 | 90 000 | 0,1 Mo | 625 | 0 — trop petite |
| **10 000** | **100 M** | **100 Mo** | **390 625** | **~1 777 — cible retenue** |
| 1 000 | 1 M | 1 Mo | 6 889 | ~11 |
| 2 000 | 4 M | 4 Mo | 27 556 | ~67 |
| 4 000 | 16 M | 16 Mo | 110 889 | ~263 |
| 10 000 | 100 M | 100 Mo | 693 889 | ~1 777 |

## 2. La texture du brouillard suit la caméra

**Le problème.** Une texture d'un texel par case couvrant toute la carte pèse 16 Mo de mémoire vidéo
à 4 000 et dépasse la taille maximale de texture de beaucoup de GPU au-delà de 8 192.

**La solution est acquise par une contrainte de jeu déjà posée : le dézoom est plafonné.** Le joueur
ne voit jamais qu'une portion limitée du monde. La texture n'a donc pas à couvrir la carte — elle
couvre un peu plus que le champ visible au dézoom maximal, et suit la caméra.

Sa taille cesse alors de dépendre de celle du monde. Une carte de 10 000 et une carte de 1 000
coûtent exactement la même chose en mémoire vidéo.

Points d'attention :

- la marge autour du champ visible doit absorber un déplacement de caméra sans réuploader à chaque
  frame ; on ne réuploade que lorsque la caméra sort de la marge
- la conversion position monde → UV se recalcule depuis l'origine courante de la texture, pas depuis
  l'origine du monde
- l'extérieur de la carte reste opaque, comme l'intérieur non découvert : tout ce qui n'a pas d'état
  explicite est inconnu

## 3. Un seul découpage : le chunk de 64, le secteur de 16

**Tout s'aligne sur le même découpage** — la génération du terrain, l'état de découverte, l'image de
la carte, et plus tard tout ce qui se calcule par région. C'est le choix de Factorio, qui utilise le
même chunk de 32×32 pour la génération, la cartographie, la pollution et l'expansion ennemie. Deux
découpages concurrents produiraient des désalignements permanents.

**Chunk : 64 cases de côté. Secteur : 16 cases, soit 4×4 secteurs par chunk.**

La mémoire ne dépend pas de la taille du chunk — 3 % d'une carte de 10 000 explorés font 3 Mo quelle
que soit la découpe. Le critère est le nombre d'objets à gérer : 24 000 chunks à 64 contre 97 000 à
32. Et le secteur passe de 12 à 16 pour tomber juste dans le chunk ; son disque inscrit a un rayon
de 8 et révèle 201 cases au lieu de 113.

### 3.1 L'état de découverte devient épars

Un tableau d'un octet par case est plein dès son allocation, alors que 99 % des cases resteront
« inconnu » pendant toute la partie. C'est le stockage runtime qui coûte, pas la sauvegarde : le RLE
en place encode déjà « 99 millions de cases inconnues » en quelques octets.

L'état est donc stocké **par chunk, créé à la première écriture**. Un chunk jamais visité n'existe
pas ; répondre « inconnu » pour ses cases ne coûte rien. Le changement est **interne à
`DiscoveryRuntime`** : les appelants ne voient aucune différence.

Le seul vrai risque est le sens de la valeur par défaut. Un chunk absent signifie **inconnu**,
jamais découvert — sinon la carte entière apparaît d'un coup. À verrouiller par un test.

## 4. Le terrain se génère à la découverte

C'est le chantier le plus lourd des trois, et le seul qui touche à autre chose que sa propre classe.

**Le problème.** Générer 16 millions de cases de terrain au lancement coûte du temps et de la
mémoire, pour un terrain dont l'immense majorité ne sera jamais regardée.

**La solution : générer par tuiles, à la première fois qu'on en a besoin.** Le terrain touche la
Tilemap, les collisions et la génération de végétation, donc l'effort dépasse celui des deux points
précédents.

### 4.0 Ce qui déclenche la génération — plus simple que Factorio

Factorio doit générer en continu et par anticipation, parce que le joueur se déplace physiquement
dans le monde : file d'attente, génération étalée sur plusieurs ticks, prédiction devant le
déplacement. **Rien de tout cela n'est nécessaire ici.**

La caméra est désincarnée et le brouillard est opaque : cliquer sur une région inconnue amène la vue
au-dessus de noir, il n'y a rien à afficher, donc rien à générer. **La génération n'est déclenchée
que par la découverte, jamais par la position de la caméra.** Une règle, un point d'entrée.

Et la découverte est un événement **discret et rare** : un rayon qui s'étend, une mission qui
revient. Pas de budget par frame à tenir, pas de file d'attente.

**Le meilleur moment pour générer est le lancement de la mission, pas son retour.** Une mission dure
plusieurs minutes ; les chunks de sa destination peuvent être préparés pendant ce temps, sans
contrainte de latence. À son retour, la révélation est instantanée parce que tout est déjà là. Cela
utilise une contrainte de jeu déjà posée au lieu d'ajouter un mécanisme.

**Périmètre à générer :** les chunks touchés par le disque révélé, **plus un anneau d'un chunk
autour**. Sans cette marge, le masque de transition de terrain n'a pas ses voisines en bordure et
les coutures apparaissent en lignes droites sur les bords de chunk — le défaut le plus visible qui
soit, puisqu'il révèle la structure interne au joueur.

### 4.0bis Le terrain ne se sauvegarde pas, il se redérive

Puisque le terrain est une fonction pure de la graine et des coordonnées (voir 4.1), il n'a pas à
figurer dans la sauvegarde : il se régénère à l'identique au chargement. Seuls entrent en
sauvegarde l'état de découverte, déjà compressé, et les **écarts** dus au joueur — bâtiments posés,
gisements entamés, décor détruit.

C'est ce qui garde une sauvegarde minuscule sur une carte de 10 000, et c'est une raison de plus de
tenir la propriété de pureté : si le terrain n'est pas exactement reproductible, une sauvegarde
rechargée montre un autre monde.

### 4.1 La propriété qui garantit la cohérence

**Le terrain doit être une fonction pure de la graine et des coordonnées.** La case (500, 300) doit
produire exactement le même résultat qu'elle soit générée en premier ou en dernier, seule ou avec
ses voisines. Si c'est vrai, l'ordre de découverte n'a aucune importance : une zone révélée d'un
coup est identique à ce qu'elle serait découverte case par case.

Trois choses cassent cette propriété et sont donc interdites :

- **regarder ses voisins pendant la génération.** Un placement du type « y a-t-il déjà quelque chose
  à côté ? » dépend de ce qui a été généré avant.
- **tout élément plus grand qu'une case placé de proche en proche.** Une grappe, un bosquet, une
  formation rocheuse doivent se dériver d'un ancrage déterministe — même principe que la
  stratification par blocs des gisements décrite dans le carnet du zonage.
- **un générateur séquentiel à état.** S'il parcourt la carte dans l'ordre en faisant avancer un
  état interne, il produit autre chose quand on ne lui demande qu'un morceau.

### 4.2 Le piège des bords de tuile

Le masque de transition de terrain échantillonne les cases voisines pour produire sa frontière
lissée. À la limite d'une tuile générée, si la tuile d'à côté n'existe pas, la transition est fausse
— et la couture apparaît **exactement sur les bords de tuile**, c'est-à-dire en lignes droites
régulières, le défaut le plus visible qui soit.

La parade est de **générer avec une marge** : une ou deux cases au-delà de ce qui est demandé, pour
que les transitions disposent de leurs voisines.

### 4.3 Le test qui verrouille tout

Générer une même région dans deux ordres différents et vérifier que le résultat est identique. Il
attrape les trois erreurs de 4.1 et le défaut de 4.2, et il coûte quelques lignes.

## 4.5 Points tranchés autour de la génération

**La Tilemap ne contient que ce qui est généré.** Le brouillard ne cache donc plus du terrain, il
couvre du vide. Conséquence à assumer : **le brouillard devient un élément de correction, pas de
décor.** Un trou dans son opacité ne laisserait pas voir un paysage mais le néant. Son étanchéité
n'est plus une question esthétique, y compris au-delà des limites du monde.

**La Tilemap ne doit jamais porter d'état autoritaire.** C'est déjà la règle d'architecture du
projet, et c'est elle qui rend le déchargement possible plus tard : décharger les chunks visuels
loin de la caméra et les régénérer au retour ne coûte rien, puisque la génération est une fonction
pure.

**Décidé : le déchargement n'est pas fait maintenant.** C'est une optimisation à déclencher sur
mesure, quand un profilage la justifiera, pas par principe. La seule chose à tenir dès aujourd'hui
est l'invariant ci-dessus — tant que rien d'autoritaire ne vit dans la Tilemap, le déchargement
reste possible à tout moment, par n'importe qui, sans rien remettre en cause.

**La simulation ne dépend pas de la génération — comportement voulu et confirmé.** Les bâtiments
n'existent que là où le joueur les a posés, donc dans des zones nécessairement générées. Un Noyau
secondaire à l'autre bout de la carte continue de tourner parce que la simulation s'exécute sur la
grille runtime, indépendamment de la caméra et de la Tilemap. Le coût est proportionnel au nombre de
bâtiments, jamais à la taille de la carte. Factorio fait de même : l'usine entière tourne quel que
soit ce qu'on regarde.

À vérifier plutôt qu'à construire : il est probable que ce soit déjà le cas, la simulation ne
consultant ni la caméra ni la Tilemap. Un test suffit à le verrouiller — faire tourner un bâtiment
très éloigné et vérifier qu'il produit.

**Un seul point d'entrée pour la découverte.** `Discover(cases)` génère les chunks nécessaires avec
leur marge, puis écrit l'état. Toute source future — un robot qui s'aventure hors zone, un drone,
une portée d'artillerie — appelle le même point et ne coûte rien de plus à écrire.

**L'image de carte est produite à la découverte**, une par chunk découvert. Elle n'entre pas en
sauvegarde : le terrain étant redérivable, elle se reconstruit au chargement, chunk par chunk et à
la demande à la première ouverture de la carte. C'est aussi ce qui alimentera une mini-carte si tu
en ajoutes une.

**Le monde a un bord.** Au-delà de la limite, un mur infranchissable, et du brouillard opaque comme
partout ailleurs. Deux conséquences à traiter : les secteurs hors carte ne sont jamais des
destinations valides, et l'élargissement de la couronne de mission doit s'arrêter au bord au lieu de
chercher indéfiniment — un joueur installé dans un coin ne doit pas provoquer de boucle.

**La graine doit venir de la partie, pas de l'asset.** `SaveCurrentGame` écrit aujourd'hui
`TerrainSeed` depuis l'asset de réglages. Sur une carte entièrement redérivée au chargement, ce
n'est plus un défaut bénin : modifier l'asset entre deux sessions régénérerait un monde différent
sous les bâtiments existants. Corriger pour lire la graine de la partie en cours, et verrouiller par
un test — sauvegarder, modifier l'asset, recharger, et vérifier que le terrain est inchangé.

## 4.6 Bornes restantes

**Le bloc de densité vaut exactement un chunk.** La stratification des gisements avait été définie
sur des blocs de 3×3 secteurs quand les secteurs faisaient 12. Avec des secteurs de 16, un bloc de
**4×4 secteurs fait 64 cases, soit exactement un chunk**. Une seule grille, un seul index, une seule
graine — au lieu de deux découpages décalés à traduire l'un dans l'autre à chaque fois qu'une règle
par bloc apparaîtra.

**La zone de départ doit être posée avant toute dérivation.** `WorldGenerator` place six grappes
autour du Noyau. Avec une génération paresseuse il n'y a plus de « génération du monde » : il faut
dire explicitement quand ce contenu est posé, et garantir qu'il l'est **avant** que la dérivation ne
s'applique aux secteurs concernés. La règle « un secteur qui porte du contenu placé garde ce
contenu » suppose que ce contenu existe déjà ; si l'ordre s'inverse, la zone de départ se fait
écraser par du contenu aléatoire et l'introduction devient injouable. À traiter comme une contrainte
d'ordonnancement explicite, pas comme une conséquence supposée.

**Au premier lancement, générer largement.** Rayon de 60 cases autour du Noyau, marges comprises.
Ce n'est pas un enjeu de performance, mais il faut le nommer : sans cela, une implémentation
littérale de « rien n'est généré tant que rien n'est découvert » ferait apparaître le joueur dans le
noir.

**Les vieilles sauvegardes cassent pendant le développement — accepté.** Puisque le terrain est
redérivé au chargement, toute modification de la génération — bruit, densité, seuils — rend une
sauvegarde antérieure incohérente avec les chunks générés après. C'est le prix de la redérivation et
il est assumé pour l'instant. À revoir avant toute diffusion : une version de génération dans la
sauvegarde permettrait alors d'invalider proprement plutôt que de laisser apparaître des coutures
incompréhensibles.

**Les portées de mission : tranchées.** `SectorMissionRange` est aujourd'hui implémenté sur une
couronne de « rayon du Noyau + 30 », héritée d'une spécification antérieure. Elle est remplacée par
deux portées distinctes, correspondant à deux types de mission :

| Type de mission | Portée | Ce qu'on y cherche |
|---|---|---|
| Exploration | **250 cases et au-delà** | sites de Noyau secondaire, nids, points d'intérêt |
| Exploration minière | entre le rayon du Noyau et 250 | nouveaux gisements |

**D'où vient le 250.** Un Noyau secondaire doit être assez loin pour que son rayon maximal ne touche
jamais celui du principal : 80 + 80 = 160, plus un vide de 90 cases entre les deux, soit 250. Ce
n'est donc pas un nombre arbitraire mais une conséquence de deux autres — le rayon maximal d'un
Noyau et l'espacement voulu entre deux territoires.

**À exposer comme réglages, pas comme constantes**, et surtout à dériver plutôt qu'à recopier : la
distance minimale d'exploration se calcule depuis le rayon maximal d'un Noyau et l'espacement voulu.
Le jour où le rayon maximal passe de 80 à 100, la portée doit suivre à 290 sans qu'aucun autre
chiffre ne soit à corriger — même exigence que celle déjà posée sur la couronne, qui se calcule
depuis le rayon courant et n'en garde rien.

L'anneau minier entre le rayon initial de 40 et 250 couvre environ 191 000 cases, soit 47 blocs, et
747 secteurs de 16. Il ne se rétrécit presque pas quand le rayon atteint 80 : 43 blocs. La place ne
manque pas.

## 4.7 Les six Noyaux secondaires et la densité des gisements

### Six Noyaux sur un cercle de 250

Six sites répartis à 60° sur un cercle de rayon 250 autour du Noyau principal. La géométrie tombe
juste : dans un hexagone, le côté vaut le rayon, donc **deux Noyaux secondaires voisins sont eux
aussi à 250 cases l'un de l'autre**. Le même vide de 90 cases sépare toutes les paires, y compris
avec le Noyau principal. Rien à ajuster.

**Variation retenue : ±20 cases sur le rayon, ±10 % sur l'angle.** Le cercle parfait se lirait comme
une construction ; ces deux variations suffisent à le casser.

Vérifié : 10 % de 60° font ±6° par site, donc au pire 48° entre deux voisins. À cet écart et au
rayon minimal de 230, la distance tombe à **187 cases, soit 27 de vide** entre deux rayons maximaux.
Les rayons ne se touchent pas, mais la marge est nettement plus courte que les 90 cases nominales —
c'est le prix de l'irrégularité, et il est acceptable tant qu'on ne l'augmente pas.

**Pose malgré tout la contrainte directement plutôt que de te fier à ces bornes** : la distance
minimale entre deux Noyaux quelconques vaut `2 × rayon maximal + vide minimal`, et les positions
sont tirées par rejet déterministe depuis la graine jusqu'à la satisfaire. La garantie ne dépend
alors d'aucun réglage, et elle survit à un changement du rayon maximal, du nombre de sites ou de
l'amplitude des variations. Sans elle, augmenter la variation d'angle à 20 % un jour ferait se
toucher deux territoires sans que rien ne le signale.

**Les zones minières entrent dans la même contrainte.** Elles ne doivent chevaucher ni un autre
gisement, ni un site de Noyau secondaire — en tenant compte de leur **rayon maximal** de 30, pas de
leur rayon initial de 10, puisqu'elles grandissent avec le nombre de gisements trouvés.

### Le contenu garanti d'un site de Noyau secondaire

Un Noyau secondaire doit être **autonome dès sa fondation**. Son site reçoit donc, garanti, au moins
une grappe de chacune des ressources de base — charbon, fer, cuivre — comme la zone de départ du
Noyau principal.

**Densité : 4 à 8 cases par grappe, tirées aléatoirement.** La garantie porte sur la présence des
trois ressources, pas sur leur abondance : deux sites ne se valent pas, et un site pauvre reste
viable sans être confortable.

**Contrainte de portée, à ne pas rater : les grappes garanties doivent tomber dans le rayon
*initial* du Noyau secondaire, pas dans son rayon maximal.** Une grappe placée à 70 cases d'un Noyau
qui démarre avec un rayon de 40 est inexploitable au moment où le joueur en a le plus besoin, et le
site n'est pas autonome malgré la garantie. La place ne manque pas : un disque de rayon 40 compte
plus de 5 000 cases, contre au plus 24 pour les trois grappes.

Ce contenu garanti relève de la **troisième catégorie** aux côtés du contenu placé à la main et du
contenu dérivé : il est écrit au moment où le site de Noyau est déterminé, donc **avant** que la
dérivation générale ne s'applique à ces secteurs. La règle « un secteur qui porte du contenu placé
garde ce contenu » suffit ensuite à le protéger — c'est la même contrainte d'ordonnancement que
celle de la zone de départ.

**Question ouverte :** trois ressources de base suffisent-elles à l'autonomie, ou un Noyau secondaire
doit-il pouvoir produire toute la chaîne ? La réponse décide si la garantie porte sur trois
ressources ou sur davantage.

### Les zones minières : garantie par couloir, un seul minerai chacune

**Une zone minière ne contient qu'un seul type de minerai.** Elle a ainsi une identité, et choisir sa
destination devient une décision plutôt qu'un tirage.

**Garantie : au moins une zone minière de chaque type entre chaque Noyau secondaire et le Noyau
principal.** Avec six Noyaux et trois ressources, cela fait dix-huit zones disponibles.

**Disponible ne veut pas dire nécessaire.** Ces dix-huit ne sont pas dix-huit avant-postes à gérer :
le joueur en exploite autant que sa demande l'exige, et si deux suffisent, les seize autres restent
des points sur la carte. La garantie porte sur l'offre, jamais sur l'obligation.

Ce qui décide du nombre réellement construit, c'est **le débit d'un avant-poste** — donc la taille de
la grappe, puisque plus de tuiles signifie plus d'extracteurs au même endroit. Le critère
d'équilibrage est mesurable : combien d'avant-postes de fer faut-il pour saturer la demande d'une
zone secondaire à plein régime ? Multiplié par six, on obtient le nombre nécessaire, et la garantie
doit le dépasser confortablement.

**Les gisements ne s'épuisent jamais.** Un avant-poste produit indéfiniment une fois installé, sans
réinstallation ni maintenance. Deux conséquences : le nombre d'avant-postes ne peut que croître,
puisque rien ne le régule à la baisse — seule la demande arrête le joueur — et la taille des grappes
devient le seul levier pour que ce nombre se stabilise tôt. Et la garantie de dix-huit zones ne coûte
rien, puisque le joueur n'a aucune raison d'en exploiter plus qu'il n'en a besoin.

(Un avant-poste peut être perdu si le joueur en perd le contrôle. Hors périmètre de ce document.)

**Placement.** Aléatoire dans le couloir entre les deux Noyaux, sans toucher ni un territoire de
Noyau ni une autre zone minière, en tenant compte du rayon **maximal** de chacune. La contrainte ne
mord jamais : l'anneau entre 80 et 250 accueille 27 zones de rayon 20 ou 80 zones de rayon 10, bien
au-delà de ce qui sera garanti.

### Des grappes plus rares et plus grosses en s'éloignant

Une grappe par bloc donnerait 47 avant-postes dans le seul anneau initial : trop de bases à gérer,
trop de missions à lancer pour une quantité de ressources qui n'augmente pas d'autant.

**La densité décroît avec la distance au Noyau principal, la taille des grappes augmente.** À
quantité totale égale, on passe d'un chapelet de petites bases à quelques exploitations
importantes :

| grappes par bloc | gisements par grappe | avant-postes dans l'anneau | gisements au total |
|---|---|---|---|
| 1 | 2 | 47 | 94 |
| 0,5 | 4 | 24 | 94 |
| 0,25 | 8 | 12 | 94 |

La taille de la grappe pilote déjà le rayon de la zone minière — 2 gisements pour un rayon de 10,
6 pour 20, 10 pour 30. Une grappe lointaine donne donc un avant-poste plus grand et plus productif,
ce qui récompense l'éloignement au lieu de le punir par de la gestion.

Les deux courbes — densité décroissante, taille croissante — sont des **réglages exposés**, dérivés
de la distance au centre de la carte. C'est le levier d'équilibrage de toute l'expansion, et il ne
se règlera qu'en jouant.

## 4.8 Paramètres de génération — rien en dur, tout dérivé

**Principe.** Aucune des valeurs de ce document n'est définitive. Le rayon maximal peut passer de 80
à 60 ou à 100, le nombre de grappes garanties de 3 à 2, la taille de carte peut changer. Tout ce qui
est calculé doit l'être **depuis ces paramètres**, jamais depuis un nombre recopié. Le test de
validité est simple : changer un paramètre et vérifier qu'aucun autre n'a besoin d'être corrigé à la
main.

**Corollaire pour l'écran de personnalisation : n'expose jamais une valeur dérivée.** La distance des
Noyaux secondaires ne doit pas être réglable directement — elle se calcule depuis le rayon maximal et
le vide minimal. Sinon un joueur qui règle un rayon maximal de 100 et une distance de 200 obtient des
territoires qui se chevauchent, et le générateur doit arbitrer entre deux consignes contradictoires.
Expose les causes, calcule les conséquences.

### Le mécanisme, pas seulement l'écran

La personnalisation de partie n'est pas une commodité offerte au joueur : c'est **le mécanisme par
lequel tout réglage futur se fera**. Sa conséquence est structurelle.

Le générateur lit toutes ses valeurs depuis un objet de réglages unique. L'écran de nouvelle partie
édite cet objet. Le développeur qui ajuste l'équilibrage édite le même objet, via ses valeurs par
défaut. **Il n'existe aucun troisième chemin** — pas de constante dans le générateur, pas de nombre
recopié dans un système voisin.

Ce qui en découle : passer la taille des grappes de 8 à 6, retirer une ressource garantie, faire
passer le rayon maximal de 80 à 100, changer le nombre de Noyaux secondaires — aucune de ces
modifications ne doit demander une ligne de code. Si l'une d'elles en demande une, c'est qu'une
valeur a été écrite en dur quelque part, et c'est un défaut à corriger.

Le test de validité est mécanique : changer un paramètre, relancer, et vérifier qu'aucun autre n'a
eu besoin d'être ajusté à la main.

### Paramètres exposés au joueur, avec leurs valeurs par défaut

**Monde**

| Paramètre | Défaut | Note |
|---|---|---|
| Graine | aléatoire | |
| Taille de la carte | 10 000 | bord infranchissable au-delà |
| Taille de chunk | 64 | technique, probablement non exposé |
| Taille de secteur | 16 | 4×4 par chunk, technique |

**Noyaux**

| Paramètre | Défaut | Note |
|---|---|---|
| Rayon initial du Noyau principal | 40 | |
| Rayon maximal d'un Noyau | 80 | |
| Rayon initial d'un Noyau secondaire | 40 | à confirmer |
| Nombre de Noyaux secondaires | 6 | répartis à 360°/n |
| Vide minimal entre deux territoires | 90 | |
| Variation de rayon | ±20 | casse le cercle parfait |
| Variation d'angle | ±10 % | au-delà, la marge devient courte |
| *Distance nominale d'un Noyau secondaire* | *250* | **dérivée** : 2 × rayon max + vide minimal |

**Ressources garanties**

| Paramètre | Défaut | Note |
|---|---|---|
| Grappes par type dans la zone de départ du Noyau principal | 1 | charbon, fer, cuivre |
| Grappes par type dans la zone d'un Noyau secondaire | 1 | doit tomber dans le **rayon initial** |
| Grappes par type ajoutées à chaque extension de rayon | 1 | dans la couronne nouvellement couverte |
| Taille d'une grappe garantie | 4 à 8 cases | tirée aléatoirement |
| Types garantis | charbon, fer, cuivre | liste, pas un nombre en dur |

**Ressources dérivées, hors zones garanties**

Deux réglages indépendants, à ne pas confondre. Le **nombre de grappes** dit combien il y en a par
bloc de terrain ; la **taille d'une grappe** dit combien de tuiles elle contient. Douze grappes de 8
tuiles et quarante-sept grappes de 2 tuiles donnent presque le même minerai total, mais douze
avant-postes contre quarante-sept — c'est cette différence-là qu'on règle.

| Paramètre | Défaut | Note |
|---|---|---|
| Nombre de grappes par bloc, près du centre | à régler | 1 bloc = 1 chunk = 64×64 |
| Nombre de grappes par bloc, au loin | à régler | décroissant avec la distance : moins d'avant-postes |
| Taille d'une grappe entre Noyau principal et Noyaux secondaires | **8 tuiles** | |
| Taille d'une grappe au-delà des Noyaux secondaires | à régler | croissante avec la distance |
| Au moins une zone minière de chaque type entre un Noyau secondaire et le principal | oui | garantie, pas une probabilité |

**Missions**

| Paramètre | Défaut | Note |
|---|---|---|
| *Portée des missions d'exploration* | *250 et au-delà* | **dérivée** de la distance des Noyaux |
| Portée des missions minières | du rayon courant à la portée d'exploration | **dérivée** |
| Sondes au démarrage | 2 à 3 | |
| Missions par sonde | 10 | budget de l'introduction |
| Seuil de CU déclenchant les missions | 20 000 | |

### Pourquoi cette liste maintenant

La composition des ressources n'est pas figeable à ce stade : un joueur voudra spécialiser chaque
zone secondaire dans un produit qu'il transportera ensuite, un autre voudra pouvoir tout faire
partout, un troisième ne verra une zone que comme une source de minerai brut. Ces trois façons de
jouer demandent des mondes différents, et c'est précisément ce que la personnalisation doit
permettre — plutôt que de trancher aujourd'hui pour l'un des trois.

Les valeurs par défaut, elles, doivent servir le joueur qui découvre : garanties généreuses,
autonomie assurée, aucune partie injouable par malchance.

## 5. Ordre de traitement

Rien de tout cela n'est urgent tant que la carte reste à 300. À faire quand la taille définitive est
tranchée, et dans cet ordre :

1. **Trancher la taille.** Elle conditionne lesquels des trois chantiers sont nécessaires. Entre
   2 000 et 4 000, tout ce que la vision demande est possible — des dizaines de sites de Noyau, de
   la place pour les zones minières, une vraie immensité — sans refaire le stockage ni le rendu.
   Au-delà, les trois chantiers deviennent obligatoires.
2. **La texture qui suit la caméra.** Le moins coûteux, entièrement contenu dans `FogOfWarView`.
3. **L'état de découverte épars.** Interne à `DiscoveryRuntime`, invisible des appelants.
4. **La génération de terrain paresseuse.** Le plus lourd, et le seul à toucher plusieurs systèmes.

## 6. Ce que ce document ne traite pas

Le chargement et le déchargement des **objets** placés dans le monde — bâtiments, convoyeurs,
robots — quand ils sortent du champ visible. Sur une base d'automatisation, ils doivent continuer à
tourner même hors écran, donc la question est différente de celle du terrain : c'est un sujet de
simulation, pas de génération. À traiter séparément si le nombre de bâtiments devient un problème de
performance.
