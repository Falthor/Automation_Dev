# Zones d'expédition et missions d'introduction — document directeur

Conception arrêtée du système de zones, de la carte et du déroulé de l'introduction des expéditions.
Complète `SPEC_EXPEDITIONS.md`, qui reste la référence sur le processus de mission lui-même — la
machine à états, les sondes, le tirage au lancement, le budget de récompenses.

Ce document traite de ce que la spec ne couvre pas : **où** les missions se lancent, **comment** le
joueur les choisit, et **dans quel ordre** l'introduction les lui présente.

---

## 1. Les six zones

Le monde est découpé en **six tranches angulaires de 60°** autour du Noyau principal. Une zone est
l'unité que le joueur choisit et cartographie ; ce n'est pas un secteur, ni un territoire de signal.

**Bornes radiales.** Une zone commence au rayon d'action courant du Noyau et se termine au **seuil
d'exploration plus le rayon maximal d'un Noyau**. Cette borne extérieure est ce qui rend la zone
finie, donc cartographiable à 100 % — une tranche angulaire non bornée irait jusqu'au bord de la
carte et ne se terminerait jamais.

Le seuil d'exploration reste dérivé, jamais écrit : `2 × rayon maximal + espacement territorial`.

**Six zones, six sites de Noyau secondaire.** Ce n'est pas une coïncidence à arranger : c'est la même
géométrie vue deux fois. Chaque zone contient exactement un site secondaire, et choisir une direction
revient à choisir son futur voisin.

**Variations.** ±20 cases sur le rayon et ±10 % sur l'angle, comme pour le placement des Noyaux
secondaires. La contrainte de distance minimale entre territoires reste posée directement plutôt que
déduite de ces bornes.

**Conséquence de forme à assumer.** Une tranche de 60° est bien plus large au loin qu'au près : son
arc extérieur fait plus du double de l'intérieur. Les sites y seront naturellement plus espacés en
s'éloignant, ce qui va dans le sens de la densité décroissante et des grappes plus grosses au loin.

### Le choix initial et son verrouillage

Au déblocage des robots, les six zones sont présentées disponibles. Le joueur en choisit une ; les
cinq autres se verrouillent. Il faut cartographier la zone courante avant de passer à la suivante.

Pendant l'introduction, les six zones sont équivalentes — le choix ne porte que sur la direction.
La différenciation viendra plus tard.

**La complétion à 100 % n'a pas à tenir dans l'introduction.** Les missions d'intro servent d'abord
à rapporter des CU si le joueur est bloqué. Rien ne force à finir une zone dans le tutoriel.

---

## 2. Contenu d'une zone

| Type | Nombre | Note |
|---|---|---|
| Prospection minière | 3 | |
| Exploration lointaine | 2 | en bordure de zone |
| Récupération | 2 à 3 | rapporte des CU aujourd'hui, à enrichir plus tard |
| Étude de civilisation ancienne | 1 à 2 | |
| Reconnaissance / étude de terrain | plusieurs | voir §3 |

**Les deux explorations lointaines et le Noyau secondaire.** Le site du Noyau secondaire est tiré
aléatoirement **entre les deux** points d'exploration lointaine. Certains joueurs le trouvent à la
première, d'autres à la seconde.

C'est ce qui rend la seconde exploration non redondante : sans elle, refaire une reconnaissance au
même endroit n'aurait aucun sens. L'écart maximal entre deux parties est d'une mission, donc la
variance ne crée pas d'injustice.

**Celle qui ne contient pas le Noyau doit quand même donner quelque chose** — le début de la zone
voisine, une trace de civilisation ancienne, ou simplement beaucoup de terrain. Sinon la moitié des
joueurs découvre qu'une de leurs quêtes était vide.

Une fois le site secondaire trouvé, la mission **Restauration du datacenter abandonné** devient
disponible dans cette zone.

---

## 3. Les missions d'étude de terrain

Quatre types seulement sont réalisables par les robots explorateurs : prospection minière,
exploration lointaine, récupération, et reconnaissance — appelée ici **étude de terrain**.

L'étude de terrain est le seul type sans site prédéfini. Elle existe pour trois raisons.

**Elle rend le 100 % atteignable.** Les autres missions visent des sites finis ; sans un type qui
révèle du terrain sans objet particulier, la cartographie plafonnerait à ce que les sites
permettent.

**Elle est le seul endroit où le joueur choisit sa destination.** Les autres ont leur cible imposée
par un site. Ici il désigne une portion non révélée de sa zone — c'est la mission qu'on lance sur
une intuition, pour vérifier d'où vient un signal ou pour relier deux disques révélés.

**Elle est le seul type dont le résultat est ouvert.** Un site connu donne ce qu'il annonce ; une
étude de terrain peut ne rien donner, ou trouver quelque chose que la zone n'annonçait pas.

### Ce qu'elle peut ajouter

**Elle ajoute des sites au lieu d'en consommer.** C'est ce qui la rend désirable sans la rendre
rentable : les autres missions épuisent la zone, celle-ci peut y faire apparaître de nouveaux points.

**Stock fini de sites cachés par zone** — trois ou quatre, définis à la génération comme le reste du
contenu. Les études les révèlent progressivement ; une fois le stock épuisé, elles ne trouvent plus
que du terrain. La zone reste bornée tout en étant plus riche que ce que le premier rapport
annonçait.

Cela donne un sens supplémentaire au relevé imprécis : quand il annonce « trois à cinq gisements »,
la fourchette haute correspond à ce que les études permettront de découvrir. Le relevé ne mentait
pas, il ne voyait que la moitié.

**Elle affine les fourchettes du rapport**, même quand elle ne trouve rien. Le joueur gagne de
l'information plutôt que des CU — et l'information est ce qui manque le plus au début.

**Elle est plus rapide et moins chère**, pas plus rentable. La mission qu'on lance quand on a un
robot disponible plutôt que de le laisser inactif.

**Conséquence sur la mesure de cartographie.** Puisque le nombre de sites peut remonter, la
progression ne se mesure **pas en sites** — la barre reculerait. Elle se mesure en **surface
révélée**. Les sites trouvés sont une conséquence, pas la mesure.

---

## 4. Les rapports de mission

### Imprécis, jamais faux

**Un rapport approximatif ne doit pas mentir, il doit borner son incertitude.** Si le rapport annonce
quatre gisements et qu'il y en a deux, le joueur apprend que l'interface ment, et il cesse de la
lire. S'il annonce « trois à cinq » et « nature indéterminée », il dit la vérité tout en étant
incomplet, et le joueur veut affiner parce qu'il sait qu'il manque quelque chose.

C'est la même distinction que celle entre observé et souvenu : **le Noyau ne se trompe jamais, il
connaît mal.** Une IA qui délire est un bug ; une IA qui borne son incertitude est un personnage.

### La précision varie d'une ligne à l'autre

Ce n'est pas un ton d'incertitude appliqué uniformément — chaque mesure porte sa précision propre :

```
Gisements                3 à 5           détecté sans compter
Structures               2               vues, donc exactes
Nature des structures    indéterminée    pas approchées
Signal non identifié     bord de zone    direction seule
```

C'est cette variation qui rend le relevé crédible.

### Ce que le rapport ne fait pas

**Il ne propose pas d'action.** Un bouton « lancer la mission suivante » ferait sortir l'action de
l'endroit où elle se joue : le joueur cliquerait dans un rapport et se retrouverait dans un système
qu'il n'a pas encore vu.

Le rapport se termine sur son relevé et sur la progression de cartographie. La carte s'ouvre ensuite,
et c'est elle qui guide (§6).

---

## 5. L'écran de carte

### Trois échelles, un seul écran

Le clic sur une zone est un **raccourci de cadrage**, jamais un changement d'écran. Cliquer recadre
la vue avec une animation courte ; la molette et le glissement restent actifs en permanence. Rien
n'est empilé, rien n'est à quitter.

Trois échelles utiles : l'ensemble, la zone du Noyau, une zone d'exploration.

**Le niveau d'ensemble s'arrête à ce qui a du sens** — le Noyau et ses six zones, soit environ deux
fois le seuil d'exploration. Une vue du monde entier serait un point au milieu du vide.

### Le fil d'Ariane

En tête de carte : `Monde › Zone nord-est › Cratère de Suie`. Les segments précédents sont
cliquables et recadrent ; le dernier est le lieu courant, donc inerte.

Il fait deux choses à la fois — il dit où l'on est et il permet d'en sortir — et personne n'a besoin
qu'on le lui explique. Un bouton retour seul dirait moins.

### Ce que la carte montre

- les disques révélés, séparés par du brouillard ; la vue montre surtout du noir troué, et c'est
  bien : l'écart entre ce qui est ouvert et ce qui reste se lit d'un coup ;
- les sites connus, cliquables ;
- **les sites déjà faits gardent une marque discrète non cliquable** — un cercle sourd ou l'icône de
  ce qui a été trouvé. Un point qui disparaît donnerait l'impression que la zone se vide, alors que
  le joueur y a gagné quelque chose. La carte devient la trace de ce qu'on a fait, et la progression
  se voit sans lire la barre ;
- un panneau latéral comptant les sites par type, ce qui rend la cartographie lisible sans survoler
  chaque point ;
- la barre du bas annonce les trois interactions : molette, glissement, **et clic pour cadrer**.

---

## 6. La mise en évidence — le jeu montre une fois

### La première récupération

Après la première reconnaissance, la carte s'ouvre avec **un site de récupération mis en évidence**
parmi les autres : anneau, halo, ou libellé affiché en permanence alors que les autres demandent le
survol.

**Distinct sans écraser.** Pas de marqueur d'objectif clignotant, qui donnerait l'impression d'une
quête obligatoire. Un point mis en évidence reste un choix ; l'expérimenté passe outre sans avoir à
refuser quoi que ce soit.

**Elle s'éteint définitivement au premier lancement** — pas à la complétion. Si elle persistait, elle
deviendrait un rail ; si elle revenait à la zone suivante, ce serait un tutoriel qui ne finit jamais.

### L'apparition de la mission du signal perturbé

Cette mission n'est **pas verrouillée : elle est absente** jusqu'à l'amorçage du Datacenter, puis
elle apparaît. C'est le jeu qui bouge plutôt que le joueur qui obtient une permission, et ça préserve
la surprise — le joueur ne l'a jamais vue, donc il ne s'est jamais demandé ce qu'elle contenait.

**Signalement :** un halo autour de l'icône de carte. **Pas vert** — le vert dit déjà « acquis » dans
la palette, un halo vert se lirait comme « rien à faire ici ». Cyan pour l'attention active, ou rose
pour marquer l'événement.

**Le halo s'éteint à l'ouverture de la carte**, pas au lancement : il signale une nouveauté, pas une
tâche.

**À cette ouverture, la mission est sélectionnée par défaut**, pour que le joueur n'ait pas à la
chercher parmi les autres points. Cette sélection automatique ne vaut que cette fois-là.

---

## 7. Le déroulé de l'introduction

### Missions disponibles dans la zone de départ

| Mission | Nombre | État |
|---|---|---|
| Récupération | 3 | disponible, dont une mise en évidence |
| Prospection minière | 1 | disponible — sans intérêt réel aujourd'hui, gardée pour les tests |
| Exploration lointaine | 2 | disponible |
| Étude de terrain | 3 | disponible |
| Signal perturbé | 1 | **absente** jusqu'à l'amorçage du Datacenter |
| Nécessitant des unités | 6 | verrouillées — étude de civilisation, relevé de menace, etc. |

**Neuf quêtes réalisables**, plus une après le Datacenter.

**Ce tableau décrit un cas particulier, et §2 le contenu par défaut.** Les deux ne coïncident pas — §2
donne 3 prospections et 2 à 3 récupérations, celui-ci l'inverse — et rien ne le disait, ce qui en
faisait une contradiction plutôt qu'une intention. La zone de départ est composée autrement : moins de
prospection, qui n'a pas de contenu réel aujourd'hui, plus de récupération, qui paie.

**Construit**, et pas sur une tranche particulière : c'est la **première zone choisie**, quelle qu'elle
soit, qui reçoit cette composition, au moment du choix — les six restent équivalentes tant qu'elles sont
offertes, comme §1 l'exige. Les cinq autres gardent la dérivation de §2.

**Ce qui manque à ce tableau tant que l'étude de terrain n'existe pas.** Trois de ses neuf quêtes en
sont, donc la première zone n'en offre que **six** de lançables — 1 prospection, 2 explorations
lointaines, 3 récupérations — plus le signal perturbé après l'amorçage, et 2 études de civilisation
verrouillées. Le stock caché de 4 sites n'a aucun consommateur.

Conséquence sur les charges : 6 quêtes contre 20 charges (2 robots × 10). Le solo en dépense 6, le
tout-en-binôme 12 — **la tension que ce paragraphe décrit n'existe pas**, quelle que soit la valeur
retenue pour les charges. Et l'intervalle avant l'amorçage s'allonge d'autant, alors que §3 comptait sur
les études de terrain pour le combler.

### Les charges

**Dix charges par robot**, pas un budget total. **Deux robots, et deux seulement** : le troisième que
ce paragraphe annonçait venait du signal anormal, retiré de la conception (§7 du nid, et
`SPEC_EXPEDITIONS.md` §9).

L'arbitrage est réel : lancer les deux robots ensemble réduit légèrement la durée d'une mission mais
consomme deux charges.

**La marge est de deux charges par robot, et c'est elle qu'il faut juger.** Ce paragraphe disait neuf ;
neuf était déduit du « neuf quêtes » ci-dessus, jamais mesuré, et l'asset livré dit dix depuis
toujours. Corrigé dans ce sens plutôt que l'inverse : aligner le code sur un document qui a inventé son
chiffre serait le mauvais sens.

Le raisonnement reste ce que le chiffrage doit trancher. À neuf, la marge serait nulle par construction
et le tout-en-binôme demanderait dix-huit charges pour dix-huit disponibles. À dix, le joueur en solo
garde deux relances et le binôme reste possible sur deux quêtes. Laquelle des deux tensions on veut est
une question de jeu, pas de nombre.

**Et le chiffrage ne peut pas se faire tel quel.** Sans l'étude de terrain (§3), qui n'existe pas encore
comme type de mission, la zone de départ n'offre pas neuf quêtes lançables mais six ou sept, et le stock
caché ne se révèle jamais. Mesurer avant de l'avoir construite lirait une partie courte comme un
problème d'équilibrage.

La durée d'une mission varie déjà avec la distance au Noyau.

### Les six missions verrouillées

Elles demandent des unités, qui viennent de la branche armement, qui s'allume à la découverte du nid.
Ces missions arrivent donc **après l'introduction entière** — c'est l'intention.

**Elles découvrent les deux types de gisement restants**, non encore révélés à ce stade.

**Elles ne comptent jamais dans le décompte des missions restantes.** Elles ne sont pas
« restantes », elles sont d'un autre chapitre. Un compteur qui les inclurait mentirait.

### Distinguer les verrous

Deux verrous ne se ressemblent pas : « nécessite le Datacenter » est une attente de quelques
minutes, « nécessite des unités » est une attente de toute la suite du jeu. Affichés pareil, ils se
lisent pareil, et le joueur classe les deux dans « pas pour maintenant ».

- **Verrou proche** — nomme son prérequis, dans une couleur qui appelle : `Nécessite le Datacenter`,
  voire `— recherche en cours` si elle l'est. Le joueur reconnaît la chose et la poursuit.
- **Verrou lointain** — `Nécessite des unités`, dans le gris des choses inertes. Une catégorie dont
  le joueur n'a encore aucune idée.
- **L'ordre d'affichage fait le reste** dans le panneau latéral : disponibles d'abord, verrou proche
  ensuite, verrous lointains en dernier.

### Le compteur de missions

**Il compte ce qui est lançable, rien d'autre.** Il descend à mesure que le joueur avance et remonte
d'une unité à l'apparition du signal perturbé — cette remontée fait l'alerte à elle seule, un chiffre
qui monte après vingt minutes de descente se remarque sans clignoter.

Une pastille sur l'icône de carte, apparaissant à la nouveauté et disparaissant à l'ouverture, coûte
moins qu'un compteur permanent.

### Le nid dormant

Déclenché par **une seule condition** : l'amorçage du Datacenter effectué. La mission du signal
perturbé le révèle — elle n'est pas verrouillée, elle est absente jusque-là puis apparaît (§6).

Ce paragraphe demandait deux conditions cumulatives, la première étant le troisième robot acquis. Le
signal anormal qui donnait ce robot a été retiré de la conception, la condition lui a survécu, et rien
n'incrémente `MissionSettings.explorerRobotCount` — le nid n'aurait donc jamais pu apparaître.

Le nid est découvert **dormant**. Le joueur sait qu'il se réveillera sans savoir quand — être attaqué
dans la minute ferait de la découverte une punition.

**Le dernier robot explorateur s'éteint au même moment** : le Noyau perd ses yeux à l'instant précis
où il apprend qu'il est menacé.

Conséquence à connaître : l'exploration s'arrête là, sur une zone probablement inachevée. La suite de
la cartographie attendra des sondes fabriquées, ce qui devient une raison de plus de vouloir la
production.

### Ce qui reste à vérifier en jouant

- **L'intervalle entre la dernière quête et l'amorçage.** Neuf quêtes à deux ou trois minutes font
  vingt à trente minutes, à comparer aux 25 000 CU relevés au déblocage de la recherche Datacenter.
- Si l'intervalle est vide, les études de terrain supplémentaires peuvent le combler — elles ne
  s'épuisent pas tant que du terrain reste à révéler.

---

## 8. La révélation par le trajet

Une mission ne révèle plus seulement le disque de sa destination : **le trajet du robot révèle une
bande le long de son chemin**. Un robot qui traverse la carte sans rien voir est incohérent.

**La bande fait un tiers de la largeur du disque d'arrivée**, et cette largeur est **dérivée** de
celle du disque plutôt qu'écrite à part — deux valeurs indépendantes finiraient par ne plus être
d'accord. Le robot passe vite en chemin et s'attarde à destination ; la carte raconte le voyage.

**Le chemin est courbe, jamais droit.** Une ligne parfaitement droite se lirait comme un trait, alors
que tous les autres bords du jeu sont bruités. La déviation reste **modeste** — de l'ordre du
dixième de la distance — au-delà elle se lit comme une erreur de calcul plutôt qu'un contournement.

**Le chemin est dérivé, pas tiré à l'exécution** : depuis la graine du monde, l'index du secteur
cible et celui de la mission. Deux conséquences voulues :

- une mission rechargée produit exactement le même chemin ;
- **deux missions vers la même cible suivent le même chemin.** C'est plus crédible — les robots
  empruntent une route qu'ils connaissent — et ça a un effet de jeu heureux : la seconde mission vers
  un secteur ne révèle presque rien de nouveau en chemin, donc explorer une direction neuve est plus
  intéressant que revenir au même endroit.

**Tout est révélé au retour du robot**, jamais progressivement. C'est cohérent avec la règle que rien
ne parvient au Noyau tant que le robot est dehors, et ça évite d'avoir à sauvegarder une trace
partielle : une mission en vol porte sa cible et son issue, la trace se calcule au retour.

**La génération de terrain se fait au lancement**, pendant les minutes de trajet, puisque le chemin
est connu dès le départ.

### Ce que le trajet fait voir

Un robot qui part vers le nord-est traverse le début des zones nord et est. **Le joueur verra donc du
terrain appartenant aux zones verrouillées.** Ce n'est pas contradictoire si c'est assumé : il voit
sans pouvoir agir, ce qui donne envie d'y aller. Mais le refus doit être clair au moment où il
cliquera, sinon il croira à un bug.

---

## 9. Points laissés ouverts

- **Les cinq zones non choisies doivent-elles être composées elles aussi ?** Elles gardent la
  dérivation de §2 aujourd'hui. La question ne se pose qu'à la seconde zone, et §9 dit déjà qu'elle
  sera moins guidée.

- **Ce qui distingue visuellement un site fait d'un site restant** — la marque discrète est décidée,
  sa forme exacte non.
- **Les zones voisines apparaissent-elles en bordure de la vue d'une zone**, ou pas du tout ?
- **La seconde zone sera moins guidée** que la première. Le degré reste à définir.
- **La prospection minière n'a pas de contenu réel aujourd'hui.** Gardée disponible pour les tests ;
  à verrouiller avec une raison lisible avant toute diffusion — une mission verrouillée promet, une
  mission vide déçoit.
