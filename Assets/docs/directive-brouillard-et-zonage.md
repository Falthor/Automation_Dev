# Brouillard de guerre et zonage — document directeur

Prérequis du système de missions. À faire avant les missions elles-mêmes : une mission d'intro n'a
rien à donner tant que la révélation de carte n'a pas d'état persistant et que la carte n'est pas
découpée en zones désignables.

---

## 1. Ce qui existe et pourquoi ça ne suffit pas

Le brouillard actuel est un disque statique centré sur le Noyau, purement visuel. Il n'a aucun
état : rien n'est « découvert », donc rien ne peut être révélé par une mission. Et la carte n'a
aucune notion de zone, alors que la sélection de mission se fait sur carte dézoomée en survolant une
zone pour voir son risque et ses types de mission.

Deux manques distincts, à traiter dans cet ordre : l'état de découverte, puis le zonage.

## 2. L'état de découverte

**Autoritatif par case, pas par zone.** La carte fait 300×300, soit 90 000 cases : un octet par case
tient dans 90 Ko, le coût mémoire reste négligeable et la révélation peut prendre n'importe quelle
forme. Une révélation par zone se
contente d'écrire l'état des cases de cette zone — l'inverse n'est pas vrai, un état par zone
interdirait toute forme libre.

**Un enum, pas un booléen.** Deux états suffisent aujourd'hui — inconnu, découvert — mais stocke un
enum : un troisième état « découvert mais non observé actuellement » deviendra nécessaire quand il y
aura des nids et des unités à surveiller, et le passage de `bool` à enum touche alors tout le code
appelant.

**La découverte est permanente.** Une case découverte le reste, y compris hors du rayon du Noyau.

**Sources de révélation, aujourd'hui :** le rayon d'action du Noyau, qui révèle en continu et suit
son extension ; et les missions, qui révèlent une zone entière.

**Persistance.** L'état de découverte doit entrer dans la sauvegarde. C'est de la progression du
joueur, pas un cache reconstructible.

## 3. Le rendu

Un seul quad couvrant la carte, avec une texture d'un texel par case en `FilterMode.Bilinear`,
réuploadée uniquement quand l'état change. Le shader échantillonne la texture en coordonnées monde,
applique un bruit sur le seuil, et `clip()` là où c'est découvert.

**C'est le troisième usage de la même mécanique** dans le projet — masque de transition de terrain,
couverture nano, et maintenant le brouillard. Regarde ce qui existe déjà avant d'en écrire une
troisième version : si le shader de couverture est généralisable, généralise-le ; sinon dis-moi
pourquoi, et note-le au carnet.

Deux différences avec la couverture nano, à ne pas rater : le brouillard couvre **toute la carte**
et non une zone, donc une seule texture globale — ici c'est le bon choix, l'argument du découpage
par zone ne s'applique pas. Et il se place dans la bande la plus haute de l'échelle des ordres,
au-dessus de tout, y compris de la bande d'information.

Le bord doit être irrégulier et non un cercle net. La densité de texels par case est le levier :
commence à 1, passe à 2×2 si le bord est trop grossier.

## 4. Le zonage

Une zone est l'unité que le joueur désigne pour lancer une mission.

**Génération.** Un pavage régulier : la carte est découpée en carrés de 12×12 cases, soit 25×25 =
625 zones sur une carte de 300. L'index d'une zone, son centre et ses cases se calculent directement
depuis une coordonnée — rien à parcourir, rien à stocker, ce qui est ce qui rend la génération
paresseuse plus bas immédiate. La régularité du pavage n'a pas à être camouflée par la forme des
zones : c'est le disque inscrit, ci-dessous, qui l'empêche d'apparaître à l'écran. Les zones doivent
avoir une taille du même ordre que le rayon du Noyau : assez grandes pour qu'une mission soit un vrai
gain, assez petites pour qu'il y en ait plusieurs à portée.

**Taille d'une zone : 12×12 cases**, soit environ 140 cases. Le critère n'est pas géométrique mais
fonctionnel — une zone doit contenir **un point d'intérêt et un seul**. Les gisements étant groupés
par quatre, une zone qui abrite une grappe, une épave ou un nid est une récompense identifiable :
le joueur peut dire ce qu'il a gagné. Plus petite, elle risque d'être vide et la mission déçoit ;
plus grande, elle en contient plusieurs et le choix de destination devient indifférent.

**La zone désigne, le disque révèle.** Une mission cible une zone, mais ne révèle pas son carré :
elle révèle le **disque inscrit** dans ce carré, celui qui touche le milieu de chaque côté. Les
quatre coins restent dans le brouillard. Deux zones voisines révélées laissent donc un liseré non
découvert entre elles, comblé seulement en explorant autour — la carte se découvre par taches
rondes qui se rejoignent, sans jamais laisser voir le pavage sous-jacent.

**Le contenu d'une zone est généré à sa découverte**, pas à la génération du monde. Le point
d'intérêt est placé **au centre de la zone**, donc toujours dans le disque : une mission réussie
montre toujours ce qu'elle a trouvé. Les gisements sont dispersés **dans la zone** sans contrainte
de position : certains tombent dans le disque et apparaissent, d'autres restent dans les coins. Le
joueur voit qu'il y a quelque chose là et qu'il n'a pas tout vu.

Cette génération à la découverte règle aussi le fait que le monde n'est peuplé qu'à proximité du
Noyau : il n'y a rien à pré-générer sur une carte que le joueur ne visitera peut-être jamais.

La génération doit être déterministe : même graine, même zone, même contenu, quel que soit l'ordre
de découverte. Verrouillé par un test.

**Point resté ouvert :** le rapport de mission mentionne-t-il ce qui se trouve dans les coins non
révélés ? Si oui, l'écart entre le texte et la carte invite à explorer autour ; si non, le joueur
ignore leur existence. À trancher quand le système de missions sera écrit, pas maintenant.

**Portée des missions.** Une zone n'est une destination possible que si elle se trouve dans une
couronne : au-delà du rayon d'action du Noyau, et jusqu'à **30 cases au-delà de ce rayon**.

Cette portée se calcule **toujours à partir du rayon courant du Noyau**, jamais depuis une valeur
absolue en dur. Si le rayon de départ change, ou s'il s'étend en cours de partie, la couronne suit
sans qu'aucun autre chiffre ne soit à corriger. Les 30 cases sont un réglage exposé, pas une
constante enfouie dans le code.

Le test d'appartenance porte sur le **centre de la zone**, pas sur ses cases : une zone de 12×12
peut chevaucher la limite, et un critère par case rendrait l'éligibilité floue et coûteuse à
calculer.

**Risque à traiter : la couronne peut se vider.** Quand toutes les zones de la couronne ont été
découvertes et que le rayon n'a pas encore été étendu, il n'y a plus aucune destination et le
système de missions devient silencieux, sans que le joueur comprenne pourquoi. Deux parades
possibles, à choisir : élargir la couronne quand elle ne contient plus de zone inconnue, ou
garantir un minimum de destinations disponibles en repoussant la limite extérieure jusqu'à
l'atteindre. Dans tous les cas, ne laisse pas le cas se produire en silence.

**Génération paresseuse.** Sur une carte de 300×300, un découpage en 12×12 produit six cent
vingt-cinq zones. N'en matérialise pas la liste complète au démarrage : l'identité d'une zone — nom,
risque, contenu — se dérive à la demande, de façon déterministe depuis la graine et l'index de la
zone. Rien n'est calculé pour les zones que le joueur ne verra jamais.

**La taille du monde est 300.** Deux endroits affirment le contraire et sont à corriger :
`SortingBands.AddressableRows`, dont le commentaire dit « le monde fait 60 cases », et toute
analyse qui s'appuierait sur `TerrainGenerationSettings.size = 60`. Ça touche le budget de rangs
calculés : 300 cases à 4 pas par case et un stride de 4 font environ 4 800 valeurs, ce qui tient
dans un `short` mais avec une marge bien plus courte que celle annoncée. À revérifier avant le
renumérotage des sorting orders.

**Données d'une zone :** un identifiant, un nom, son ensemble de cases, son centre, un niveau de
risque, et son état de découverte dérivé de celui de ses cases.

**Le nom compte.** Une zone qui s'appelle « Zone 7 » n'invite pas ; un nom généré depuis un
vocabulaire cohérent avec l'univers — épaves, secteurs, vestiges — transforme une case de grille en
endroit. C'est peu coûteux et ça change la lecture de la carte dézoomée.

**Une zone n'est pas un territoire du joueur.** Ne pas confondre avec les zones de signal du Noyau
et des Agents IA, qui existent déjà et servent à autre chose. Si le mot prête à confusion dans le
code, prends-en un autre — secteur, région.

## 5. Ordre de livraison

Chaque étape est vérifiable seule, ne livre pas tout d'un bloc.

1. **L'état de découverte** : le modèle de données, la révélation par le rayon du Noyau, la
   persistance en sauvegarde. Testable sans rendu ni mission — on révèle par code et on vérifie
   l'état.
2. **Le rendu** : le quad, la texture, le shader, le bord irrégulier. C'est là que je juge à l'œil.
3. **Le zonage** : génération déterministe, données, noms. Testable sans interface — une zone
   révélée doit marquer exactement les cases de son disque inscrit, et aucune autre.
4. **La carte dézoomée** : affichage des zones, survol, risque. C'est l'interface, elle vient en
   dernier et seulement une fois les trois précédentes validées.

Les missions elles-mêmes viennent après, et ne sont pas dans le périmètre de ce document.

## 6. Contraintes

- la grille runtime reste autoritaire, la Tilemap reste de la présentation
- aucun `Tilemap.SetTile` pour le brouillard
- pas d'allocation par frame : les tableaux d'état sont alloués une fois
- la texture n'est réuploadée que lorsque l'état a changé
- si quelque chose ne s'intègre pas proprement dans l'architecture actuelle, arrête-toi et dis-le
  plutôt que de contourner

## 7. Décisions prises

- **Le déclencheur des missions est fixé : la réserve de CU passant sous 25 000.** La réserve
  démarre à son plafond, qui vaut **70 000** (`ComputeSystem.ReserveCap`) : les sondes arrivent
  donc quand près des deux tiers de la réserve ont été consommés, assez tard pour que le joueur
  l'ait sentie descendre. Ça ne change pas ce document, mais ça cadre la
  suite : au moment où les missions apparaissent, le joueur n'est pas encore en difficulté, il a
  seulement vu sa jauge descendre. Le premier retour de mission doit donc se lire comme du temps
  gagné, pas comme un sauvetage.
- **Le rayon du Noyau révèle définitivement.** Une case entrée dans le rayon reste découverte pour
  toujours, même si le rayon se rétractait un jour. La découverte est un acquis du joueur, pas une
  conséquence de sa portée courante. Le rayon d'action et le brouillard sont donc deux notions
  distinctes qui ne doivent pas être calculées l'une depuis l'autre : le rayon *écrit* dans l'état
  de découverte, il ne le *définit* pas.
- **Ce qui est visible d'une zone découverte hors du rayon du Noyau : le terrain et ce qui est posé
  dessus.** Le relief, les types de sol, la végétation, et les gisements. Pas les nids, pas les
  unités ennemies.

  La distinction est celle du statique et du vivant. Un gisement ne bouge pas : une fois vu, il est
  connu, et le joueur peut planifier son extension autour de lui — c'est ce qui donne à une mission
  une récompense visible plutôt qu'une simple ligne de rapport. Un nid ou une unité change d'état
  sans qu'on l'observe ; l'afficher en permanence reviendrait à donner une information périmée.

  C'est exactement ce qui motivera le troisième état de l'enum : les gisements resteront affichés
  hors observation, les nids devront disparaître ou se figer sur leur dernier état connu.
