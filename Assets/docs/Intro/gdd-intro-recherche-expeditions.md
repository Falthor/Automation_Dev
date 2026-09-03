# Document directeur — Introduction, Recherche et Expéditions

Version de travail. Toutes les valeurs chiffrées sont des points de départ destinés
à être testés, pas des constantes définitives. Les sections marquées **À TRANCHER**
signalent les points encore ouverts.

---

## 1. Principes directeurs

Cinq règles gouvernent toutes les décisions de ce document. En cas de doute pendant
l'implémentation, y revenir.

**Une seule grammaire économique du début à la fin.** Le CU se paie au lancement de
la production d'un objet. Cette règle est vraie à la première seconde et à la
dixième heure. La phase de survie n'est pas un mode économique séparé : c'est la
même règle avec le robinet fermé. Aucune couture, aucun système transitoire.

**Ne jamais changer une règle, seulement en ajouter.** Chaque enrichissement se pose
à côté de l'existant. Une sonde continue de se comporter comme une sonde quand les
unités de combat arrivent ; les unités sont un type nouveau. Si un élément change de
comportement en cours de partie, le joueur a le sentiment qu'on a modifié le contrat
dans son dos.

**Simplifier au début, enrichir ensuite.** L'introduction n'a pas à exhiber les
systèmes, seulement à les enseigner. La densité arrive après l'amorçage du premier
Datacenter.

**La précision se gagne, elle n'est jamais offerte.** Aucun pourcentage de réussite
n'est affiché. L'information précise existe, mais elle s'achète — par une recherche,
par une mission dédiée. C'est ce qui distingue le joueur prudent du joueur pressé.

**Déterministe pour la narration, aléatoire pour le butin.** Les temps forts
(apparition des sondes, signal anormal, découverte du nid) ne dépendent jamais d'un
tirage. Le hasard porte sur ce qu'on ramène, jamais sur ce qu'on révèle.

---

## 2. Le CU et l'économie

### 2.1 Nature de la ressource

Le CU est la ressource unique. Il n'y a ni RP, ni Data Card, ni Laboratoire —
ces trois éléments sont supprimés du projet.

Le CU est prélevé **en entier au lancement d'un craft**, jamais étalé. Conséquence
voulue : quand le CU se raréfie, les gros crafts deviennent inaccessibles avant les
petits. Un circuit imprimé se bloque pendant qu'un lingot passe encore, et le joueur
sent l'étau se resserrer progressivement.

**Règle d'implémentation impérative** : si le CU disponible est insuffisant, le craft
ne démarre pas du tout. Il ne démarre jamais pour échouer ensuite.

**Règle d'implémentation impérative** : une machine dont le buffer de sortie est plein
s'arrête et ne prélève aucun CU. Sans cela, la consommation est pilotée par le temps
de fonctionnement et non par la nomenclature, et toute la budgétisation de
l'introduction s'effondre.

Aucune consommation passive pendant l'introduction. Un bâtiment posé et inactif ne
coûte rien. L'entretien continu n'apparaît qu'avec les unités militaires, une fois le
joueur en possession d'un revenu.

### 2.2 Phase de survie

Le Noyau ne produit pas de CU. Il démarre avec une **réserve finie de 60 000 CU** et
un robinet fermé. Chaque CU dépensé est irréversible tant que le premier Datacenter
n'est pas amorcé.

L'affichage n'est pas un revenu mais une **autonomie** : `48 320 CU · −14/min ·
autonomie 42 min`. À l'amorçage du Datacenter, le même widget bascule en `48 320 CU ·
+38/min` et le chiffre passe au vert. C'est le moment de bascule, et il ne coûte rien
à implémenter.

**Plancher à zéro CU** : pas de game over. Le Noyau passe en veille, toute production
s'arrête. Les expéditions restent le seul revenu et ne coûtent jamais de CU à lancer.
Au moins un site de mission se régénère lentement, très peu rentable, pour garantir
mathématiquement qu'un joueur tombé à zéro puisse remonter.

### 2.3 Après l'amorçage

Le Datacenter produit du CU en continu en consommant des CPU et de la mémoire, dont
les composants s'usent. L'usure **ne démarre qu'après l'amorçage** — sinon on ajoute
une fuite pendant la phase sans revenu.

Le Datacenter répartit sa production entre plusieurs axes (recherche, bâtiments, puis
armement). La bascule du curseur est **gratuite et instantanée**.

Le total n'est pas conservé quand on se disperse :

```
concentration = Σ (part_axe)²
rendement     = 0,2 + 0,8 × concentration
production_axe = débit_nominal × rendement × part_axe
```

| Répartition | Production |
|---|---|
| 100 / 0 | 100 % / 0 |
| 90 / 10 | 77 % / 8,6 % |
| 70 / 30 | 46 % / 20 % |
| 50 / 50 | 30 % / 30 % |

Se concentrer est un vrai gain, se disperser un vrai confort payé cher. Le joueur
alterne par phases — plein bâtiment pendant qu'il construit, plein recherche pendant
qu'il cherche. C'est pour cela que la bascule doit rester gratuite : le rendement fait
déjà tout le travail, un coût de reconfiguration en plus rendrait le système rigide.

**Le plancher passe de 0,20 à 0,35 quand l'axe armement s'ouvre.** À trois axes, un
plancher de 0,20 donnerait 15,6 % par axe pour un partage équilibré, ce qui est
intenable. Avec 0,35, un partage à trois donne 25,7 % par axe et le 100/0 reste à
100 %. La concentration garde tout son intérêt, sans que la polyvalence devienne une
punition au moment précis où le joueur en a le plus besoin.

---

## 3. Chiffrage complet

### 3.1 Coût CU par cycle — bâtiments à coût fixe

| Bâtiment | Coût | Fréquence | Coût moyen |
|---|---|---|---|
| Extracteur (bridé) | 2 CU | 1 minerai / 4 s | 0,5 CU/s |
| Extracteur (débridé) | 2 CU | 1 minerai / 2 s | 1 CU/s |
| Centrale gaz | 8 CU | 1 charbon / 10 s | 0,8 CU/s |

Tous les autres bâtiments ne paient que le `computeCost` de leur recette. Convoyeurs,
storage, splitters et crossroads sont gratuits à l'usage.

### 3.2 Ingrédients par recette

| Recette | Bâtiment | Entrées | Sortie | Temps |
|---|---|---|---|---|
| Iron Ingot | Fonderie | 1 Iron Ore | 1 | 3 s |
| Copper Ingot | Fonderie | 1 Copper Ore | 1 | 3 s |
| Gear | Factory | 1 Iron Ingot | 2 | 2 s |
| Iron Plate | Factory | 2 Iron Ingot | 2 | 3 s |
| Copper Wire | Factory | 1 Copper Ingot | 2 | 2 s |
| Screw | Factory | 1 Iron Ingot + 1 Copper Ingot | 2 | 3 s |
| Printed Circuit Board | Factory | 2 Screw + 3 Copper Wire | 1 | 6 s |
| CPU MkI | Assembleur | 3 Copper Ingot + 2 Gear + 3 PCB | 2 | 6 s |
| Memory MK1 | Assembleur | 2 PCB + 3 Iron Ingot | 1 | 4 s |
| Mechanical Component | Assembleur | 2 Gear + 4 Screw + 2 Iron Plate | 1 | 4 s |
| Steel | Fonderie avancée | 2 Iron Ore + 1 Coal Ore | 1 | 4 s |

Le composant mécanique ne contient plus ni CPU ni mémoire. C'est ce changement qui
remet toute la facture d'aplomb, et il rend le composant réellement réutilisable
ailleurs — tourelles, structures, véhicules.

Le ratio à retenir, et que le joueur apprendra : **quatre extracteurs alimentent trois
fonderies**.

### 3.3 Coût CU par item produit

| Recette | CU / cycle | CU / item | CU cumulé (amont compris) |
|---|---|---|---|
| Minerai (extraction) | 2 | 2 | 2 |
| Iron / Copper Ingot | 2 | 2 | 4 |
| Gear | 4 | 2 | 4 |
| Copper Wire | 4 | 2 | 4 |
| Iron Plate | 8 | 4 | 8 |
| Screw | 8 | 4 | 8 |
| Steel | 12 | 12 | 18 |
| Printed Circuit Board | 24 | 24 | 52 |
| Mechanical Component | 32 | 32 | 88 |
| CPU MkI | 80 | 40 | 128 |
| Memory MK1 | 48 | 48 | 164 |

### 3.4 Coût de construction

| Bâtiment | Coût | Slot |
|---|---|---|
| Convoyeur droit / virage | 1 Iron Plate | non |
| Splitter / Crossroad | 1 Iron Plate | non |
| Extracteur | 5 Iron Plate | oui |
| Fonderie | 5 Iron Plate + 5 Copper Wire | oui |
| Storage Box | 10 Iron Plate | oui |
| Centrale gaz | 10 Iron Plate + 5 Copper Wire | oui |
| Factory | 10 Iron Plate + 10 Gear | oui |
| Assembleur | 5 Iron Plate + 10 Screw + 2 PCB | oui |
| Fonderie avancée | 20 Iron Plate + 10 Mechanical Component | oui |
| **Datacenter MK1** | **120 Iron Plate + 80 Copper Wire + 160 PCB + 48 CPU MkI + 36 Memory MK1 + 24 Mechanical Component** | oui |

**Plafond de bâtiments : 40 dès le départ.** Convoyeurs et splitters ne consomment pas
de slot. Le parc nécessaire est de 32 slots, chaîne énergétique comprise, ce qui laisse
huit erreurs possibles au joueur. La limite se fait sentir dans la dernière ligne droite
sans jamais enfermer. L'Allocation mémoire, qui relève ce plafond, devient la première
recherche d'après-introduction.

### 3.5 Le Datacenter MK1

**Il n'existe qu'un seul Datacenter, et il grandit.** Pas de MK2 pour l'instant : toute
la progression passe par l'amélioration du premier. Un second bâtiment viendra plus
tard, quand la spécialisation par type de CU prendra son sens.

**La production n'est pas une constante du bâtiment : elle dépend de ce qu'on met
dedans.** Le Datacenter possède des baies, chaque baie accueille un composant, et
chaque composant installé produit son propre débit. C'est ce qui rend l'amélioration
désirable sans jamais obliger à construire un second bâtiment.

| Composant installé | Production | Durée de vie |
|---|---|---|
| CPU MkI | 15 CU/s | 120 s |
| Memory MK1 | 10 CU/s | 120 s |

| Paramètre du MK1 | Valeur |
|---|---|
| Amorçage | 1 500 CU consommés sur 90 s, sans production |
| Baies de départ | 2 CPU + 2 Memory |
| Production maximale de départ | 50 CU/s à 100 % de concentration |
| Consommation induite | 1 CPU / 60 s + 1 Memory / 60 s |
| Répartition par défaut | 50 % recherche / 50 % bâtiments |

**Rentabilité vérifiée.** Un CPU MkI coûte 128 CU à produire, chaîne complète comprise,
et rend 1 800 CU sur sa durée de vie. Une Memory MK1 coûte 164 CU et rend 1 200 CU.
Chaque composant installé est donc largement rentable, comme il se doit — sinon le
joueur ne remplacerait jamais rien.

**Le vrai facteur limitant est le nombre de baies, pas la rentabilité.** C'est
volontaire, et c'est ce qui construit toute la progression d'après-introduction. Deux
paliers d'extension sont prévus :

| Recherche | Coût | Absorption max | Effet | Production résultante |
|---|---|---|---|---|
| Extension de baies I | 2 000 | 30 CU/s | +1 baie CPU, +1 baie Memory | 75 CU/s |
| Extension de baies II | 2 000 | 30 CU/s | +1 baie CPU, +1 baie Memory | 100 CU/s |

À pleine extension, le Datacenter monte à 4 CPU et 4 Memory, soit 100 CU/s maximum et
une consommation de 1 CPU et 1 Memory toutes les 30 secondes. Le joueur ne construit pas
un second Datacenter, il fait grandir le sien.

**La tension réelle est ailleurs** : les CPU et les mémoires ne servent pas qu'à nourrir
le Datacenter, ils entrent aussi dans la construction des bâtiments à venir — la Forge
d'unités en réclame. Le joueur doit donc produire au-delà de la seule consommation de
ses baies, pour se constituer un stock. Alimenter le présent ou préparer l'avenir :
c'est là que se joue l'arbitrage, pas dans le ratio de rentabilité.

**Aucune raison de construire un second Datacenter au début.** Elle viendra plus tard,
avec la spécialisation par type de CU et l'appétit différencié en composants.

La séquence d'amorçage est essentielle : elle consomme sans produire, ce qui oblige le
joueur à garder une marge jusqu'au bout au lieu de dépenser à zéro dès qu'il voit la
fin. Sans elle, la courbe remonte au moment même où elle allait devenir intéressante.

---

## 4. L'introduction, du début à la fin

### 4.1 Situation de départ

Le Noyau se réveille sur une alimentation de secours. Il dispose de 60 000 CU, d'un
rayon d'action et de visibilité limité, et ne voit rien au-delà.

Dans ce rayon initial se trouvent **quatre groupes de gisements de fer, deux de cuivre et
un de charbon**. Un gisement fait 2×2 comme un extracteur, mais les gisements sont
groupés par quatre : **un groupe accepte donc quatre extracteurs**. Ce placement est
imposé par le générateur, il n'est pas aléatoire.

La ressource est donc volontairement surabondante par rapport au parc nécessaire — dix
extracteurs suffisent, le terrain en autorise vingt-huit. **Le joueur doit apprendre à
ne pas surdimensionner alors que le jeu lui en donne l'occasion.** Chaque extracteur de
trop coûte cinq plaques, occupe un slot sur les quarante, et alimente une ligne qui n'en
a pas besoin. La leçon est douce mais réelle, et elle prépare tout le reste de la partie.

Un gisement visible mais hors de portée, en lisière, sert d'invitation permanente et
vaut tous les tutoriels.

Tout est **bridé**. Les extracteurs ne sont pas faibles, ils tournent à une fraction de
leur régime parce que le Noyau n'a pas la puissance de calcul pour les piloter à plein.
Le menu de l'extracteur doit le montrer explicitement : une jauge où les 60 items/min
nominaux sont dessinés, un butoir net au quart, et sous la barre la cause — *bridé par
le Noyau, mode survie*. Le joueur comprend que ce n'est pas la machine le problème,
c'est lui.

**Règle** : la cause d'un bridage affichée doit toujours être actionnable. Le jour où
un bridage existe pour une raison que le joueur ne peut pas encore résoudre, il faut
l'indiquer autrement, sinon l'indicateur devient une frustration au lieu d'un objectif.

### 4.2 Déroulé

| Étape | Ce qui se passe | Déclencheur |
|---|---|---|
| 1 | Réserve finie, chaque objet produit en consomme. Le joueur pose extracteurs, fonderies, premières lignes. Les vis sont disponibles d'emblée. | début de partie |
| 2 | Recherche **Circuit imprimé**. Le joueur monte ses lignes de vis et de PCB. | choix du joueur |
| 3 | Passage sous **35 000 CU**. | seuil de réserve |
| 4 | **Introduction du système de missions** : deux sondes apparaissent, la carte dézoomée devient accessible, les secteurs limitrophes sont marqués *non reconnu*. | étape 3 |
| 5 | **Premières missions.** Reconnaissance à 500 CU, Récupération à 1 500 CU. Le joueur découvre que l'exploration paie. | choix du joueur |
| 6 | Une expédition découvre un **signal anormal** : le nœud ??? se révèle et donne une **troisième sonde**. | site scénarisé, révélé à coup sûr |
| 7 | Recherche **Assembleur** (bâtiment + composant mécanique). | choix du joueur |
| 8 | Recherche **Modules de calcul** (CPU MkI + Memory MK1). | choix du joueur |
| 9 | Recherche **Datacenter MK1**, puis production de masse et construction. | choix du joueur |
| 10 | **Amorçage** : 90 s de consommation sans production. | pose du bâtiment |
| 11 | En interne, **la mission de découverte du nid devient disponible**. Rien n'est annoncé au joueur : le site apparaît simplement parmi les cibles possibles. | fin de l'amorçage |
| 12 | Le menu de recherche se transforme, **trois noyaux** apparaissent dont un éteint. La Fonderie avancée devient accessible. **Fin de la survie.** | fin de l'amorçage |
| 13 | **Découverte du nid dormant.** Le troisième noyau s'allume. La dernière sonde s'éteint au même moment. | mission débloquée à l'étape 11 |

Les recherches **Optimisation de fabrication** et **Extraction renforcée** sont
optionnelles et peuvent être prises à n'importe quel moment, ou jamais.

L'ordre des étapes 7, 8 et 9 est contraint par les prérequis, mais le joueur reste
libre d'intercaler ses missions et ses recherches optionnelles à sa guise. Les seules
étapes imposées sont celles déclenchées par un seuil ou par un site scénarisé.

### 4.3 L'énergie pendant la survie

L'introduction comporte de l'énergie, mais volontairement légère : **deux extracteurs de
charbon et trois centrales gaz suffisent** à couvrir tout le parc de l'introduction. Les
ajustements doivent rester minimes — l'énergie n'est pas le sujet de cette phase, elle
est une seconde dépendance qui apprend au joueur qu'une usine ne tourne pas toute seule.

**Règle impérative : une centrale ne brûle du charbon qu'à hauteur de la demande.** Le
contrat de Power (§9 de `CONTRACTS.md`) fait déjà déclarer la demande uniquement pendant
`PRODUCING`. Il faut que la consommation de combustible suive cette demande, et non le
temps qui passe.

C'est un point non négociable, et il touche au principe fondateur de l'introduction. Si
les centrales brûlent en continu, la réserve de CU se vide à l'horloge et non à
l'activité du joueur : un joueur lent se retrouve puni pour avoir réfléchi, ce qui est
exactement ce que toute la conception de cette phase cherche à éviter.

**Risque d'enfermement à vérifier.** Si un extracteur de charbon a besoin d'énergie pour
fonctionner et qu'il n'y a plus d'énergie, le joueur ne peut plus produire le charbon qui
lui redonnerait de l'énergie. Le Core fournit une énergie de base, et il faut s'assurer
qu'elle suffit à faire tourner une chaîne charbon minimale — un extracteur et son
convoyeur — quelles que soient les circonstances. À vérifier dans l'audit.

Coût induit : environ 4 000 CU de combustible et d'extraction sur la durée de
l'introduction, plus 380 CU de matériaux de construction.

### 4.4 Budget

| Poste | CU |
|---|---|
| Production (objets + infrastructure) | 26 520 |
| Chaîne énergétique (combustible + matériaux) | 4 400 |
| Recherches obligatoires | 12 500 |
| Amorçage | 1 500 |
| **Chemin critique** | **44 920** |
| Recherches optionnelles | 3 500 |
| Potentiel des sites d'expédition | + 11 500 |

**Réserve de départ : 60 000 CU.** Marge sèche de 25 % sans rien acheter d'optionnel,
18 % en prenant les deux, près de 45 % pour qui exploite les expéditions.

Trois profils de jeu en découlent, et c'est le signe que l'équilibrage est sain : le
méthodique passe sans expéditions ni options, le curieux prend les deux optionnelles et
se finance par l'exploration, le brouillon se rattrape aux expéditions.

### 4.5 Durée et parc

Parc cible : 8 extracteurs de minerai, 2 extracteurs de charbon, 6 fonderies,
8 factories, 3 assembleurs, 3 centrales gaz, 1 storage, plus le Datacenter.
**32 slots sur les 40 disponibles.**

Quantités totales à produire : 958 minerais de fer, 965 de cuivre, 1 040 fils, 746 vis,
394 plaques, 310 circuits imprimés.

Temps théorique 18,6 min, **environ 30 min en pratique** avec la montée en puissance,
les attentes de recherche et un équilibrage imparfait. Environ 24 min si le joueur
débride tôt ses extracteurs — il achète littéralement six minutes contre 2 000 CU.

Les goulets tombent presque ensemble : vis 1 119 s, fils 1 040 s, minerai de cuivre
965 s, minerai de fer 958 s. Aucune ligne unique à optimiser, il faut tout faire monter
en même temps. C'est exactement l'effet recherché.

**Contrainte vérifiée** : avec une seule machine de chaque type, il faudrait 96 min de
fonderie, 80 min d'usine et 64 min d'extraction du fer. Et structurellement, la Factory
doit faire tourner cinq recettes distinctes en parallèle — engrenage, plaque, fil, vis,
circuit imprimé — donc une usine unique est impossible sans changer la recette à la
main en permanence.

### 4.6 Pacing

Quatre recherches obligatoires sur trente minutes, soit une décision toutes les sept à
huit minutes. Entre deux, il y a réellement de quoi construire. Ce rythme est
volontaire : quatorze recherches en trente minutes transformeraient le menu en péage
qu'on traverse plutôt qu'en moment de choix.

---

## 5. La recherche

### 5.1 Modèle

Une recherche est un **processus**, pas un achat. On ne définit jamais une durée : on
définit un **coût total en CU** et un **débit d'absorption maximum**. Le temps devient
une conséquence.

```
durée = coût / min(débit_absorption_max, CU_recherche_disponible_par_seconde)
```

Ce modèle donne enfin un sens mécanique fort au curseur du Datacenter : allouer plus
de CU recherche accélère réellement la recherche en cours.

**Une seule recherche à la fois**, avec une **file d'attente** où le joueur enfile les
suivantes. La file est peu de travail et change beaucoup le confort.

**Règle impérative** : si le CU tombe à zéro pendant une recherche, elle se met en
pause en conservant sa progression. La perdre serait une punition insupportable.

### 5.2 Contenu de l'introduction

| Recherche | Coût | Absorption max | Effet | Statut |
|---|---|---|---|---|
| Circuit imprimé | 1 500 | 35 CU/s | recette PCB | obligatoire |
| Assembleur | 2 500 | 45 CU/s | bâtiment + composant mécanique | obligatoire |
| Modules de calcul | 3 500 | 50 CU/s | CPU MkI + Memory MK1 | obligatoire |
| Datacenter MK1 | 5 000 | 60 CU/s | le bâtiment cible | obligatoire |
| Optimisation de fabrication | 1 500 | 40 CU/s | −10 % de CU par item produit | optionnelle |
| Extraction renforcée | 2 000 | 40 CU/s | lève le bridage, débit ×2 | optionnelle |
| ??? | — | — | troisième sonde | via expédition |

Les deux optionnelles sont le seul vrai choix de l'introduction, et elles ne promettent
pas la même chose : l'Optimisation économise du CU, l'Extraction économise du temps.
Deux monnaies différentes, donc un arbitrage réel plutôt qu'un ordre d'achat.

L'Optimisation, achetée tôt, rapporte environ 1 150 CU net ; achetée tard, elle fait
perdre de l'argent. Le joueur doit estimer ce qu'il lui reste à produire — exactement
le raisonnement que le jeu lui demandera pendant des heures.

### 5.3 Présentation — menu d'introduction

Pendant la survie, le menu est **classique et linéaire**. Une liste verticale, ou une
colonne de cartes, dans un overlay plein écran assombri. Quatre nœuds obligatoires en
ligne droite, deux nœuds optionnels sur le côté, un nœud ??? en silhouette.

Dessiner un réseau neuronal pour une chaîne linéaire serait de la pose, et brûlerait la
révélation pour rien.

Chaque entrée affiche :

- **nom et icône**
- **état** parmi cinq : verrouillé, disponible mais CU insuffisant, disponible et
  payable, en cours, acquis
- **coût** avec barre de remplissage et **temps estimé au débit actuel**
- **effet chiffré**
- **prérequis** avec leur statut

Le nœud ??? est visible dès le premier écran, en silhouette, avec le statut *signal non
identifié*. Un point d'interrogation qu'on voit sans pouvoir le toucher travaille le
joueur bien plus qu'une surprise surgie de nulle part.

### 5.4 Présentation — menu neuronal

À l'amorçage du Datacenter, le menu se transforme. **Le vocabulaire visuel ne change
pas** — mêmes cinq états, même panneau de détail, même barre de coût avec autonomie.
Seule la mise en page change. Si le joueur doit réapprendre à lire, la transformation
devient une corvée au lieu d'une récompense.

**Disposition radiale.** Le rayon encode le palier de progression : le joueur lit sa
position dans la partie à sa distance au centre, sans aucun texte.

**Contrainte angulaire.** Chaque branche reçoit un secteur exclusif ; un enfant reste
dans le secteur de son parent. C'est la règle qui garantit mathématiquement zéro
croisement de câbles, et elle permet de calculer le placement automatiquement.

```
angle_enfant  = intervalle du parent, subdivisé au prorata du nombre de feuilles
rayon         = palier × pas_de_rayon
position      = centre + polaire(rayon, angle) + ajustement_manuel
```

Le joueur n'ordonne que la liste des enfants, l'algorithme répartit les angles. Un
`Vector2` d'ajustement par nœud reste stocké pour les retouches esthétiques, mais
aucun nœud n'est jamais placé à zéro à la main.

**Trois noyaux au lieu d'un.** Recherche et bâtiments s'allument à l'amorçage.
L'armement reste **éteint**, non alimenté, avec quelques neurones en silhouette autour.
Ça réserve visuellement la place et pose une question dans la tête du joueur au moment
exact où il vient de résoudre la précédente.

**Les synapses.** Le lien est un câble fibre optique, dans la palette du signal.
Éteint quand le parent n'est pas acquis, pulsant avec un point lumineux qui circule
pendant une recherche en cours — un simple défilement d'offset de texture — plein et
allumé une fois acquis.

**Le dessin et la donnée sont séparés.** Le prérequis existe toujours dans le graphe,
mais le câble ne se dessine que quand son parent est acquis. Pour un nœud à plusieurs
parents : les parents acquis sont reliés par un câble plein, les parents manquants ne
sont pas reliés du tout — seulement un moignon de synapse qui part du nœud dans leur
direction, court et flottant. Si le parent manquant est lui-même bloqué plus loin, rien
n'est dessiné entre eux : il n'y a pas de chemin, il ne doit pas y en avoir à l'écran.

Le joueur voit littéralement un cerveau incomplet dont les connexions poussent.

**Le texte prend le relais.** Le panneau de détail liste chaque prérequis avec son
statut — acquis, disponible, ou verrouillé lui-même derrière tel autre. Un clic sur un
prérequis verrouillé recentre la vue sur lui en surlignant toute la chaîne. Le visuel
raconte ce qui est fait, le texte explique ce qui manque, la navigation relie les deux.

**Ce qui est payable maintenant doit attirer l'œil.** C'est la seule information que le
joueur cherche en ouvrant le menu. Cadre épaissi, jamais une distinction par la couleur
seule : chaque état porte aussi une forme — cadenas, anneau de progression, coche.

**Prévoir le zoom molette et un bouton de recentrage dès maintenant**, plutôt que
d'écraser les pas de rayon jusqu'à ce que les anneaux se touchent.

**Tout l'arbre est visible, rien n'est masqué.** Les nœuds lointains s'affichent avec
leur nom, leur coût et leur effet, quel que soit leur éloignement. Masquer une partie de
l'arbre uniquement pour donner un objet à une recherche de révélation reviendrait à
fabriquer un problème pour vendre sa solution. Le *Diagnostic cortical* est donc
abandonné.

Seuls restent invisibles les nœuds **???**, qui ne sont pas masqués mais **inconnus** :
leur contenu n'existe pas encore pour le joueur parce qu'il n'a pas été découvert sur le
terrain. La distinction est importante — l'un est une information retenue, l'autre une
information qui n'a pas encore été acquise.

### 5.5 Après l'amorçage

Le troisième noyau s'allume à la **découverte du premier nid**. Il ouvre immédiatement
sur **deux branches** : défense statique d'un côté, unités mobiles de l'autre.

Ce timing est important. Le joueur vient de passer une introduction entièrement
linéaire ; lui offrir sa première vraie liberté de trajectoire le jour où le réseau
neuronal apparaît fait coïncider la révélation de l'interface avec la première
décision de stratégie. Les deux se renforcent au lieu de se disputer l'attention.

**Pour l'instant, cette branche ne contient qu'une seule recherche** : la **Forge
d'unités**, le bâtiment qui produit les unités mobiles.

| Recherche | Coût | Absorption max | Effet |
|---|---|---|---|
| Forge d'unités | 6 000 | 70 CU/s | bâtiment de production d'unités |

| Bâtiment | Coût de construction | Slot |
|---|---|---|
| Forge d'unités | 20 Iron Plate + 10 Mechanical Component + 4 CPU MkI | oui |

Ses quatre CPU MkI sont délibérés : ils entrent en concurrence directe avec
l'alimentation du Datacenter. Le joueur doit produire au-delà de ce que ses baies
consomment pour se constituer un stock, et c'est exactement l'arbitrage qu'on veut
installer à ce moment.

Un seul nœud allumé dans un secteur qui en contiendra beaucoup, c'est aussi la bonne
image : la branche armement s'ouvre à peine, et le vide autour d'elle raconte tout ce
qui reste à faire. La défense statique, les tourelles et le centre de réparation
viendront s'y greffer ensuite.

---

## 6. Les expéditions

### 6.1 Rôle

Le Noyau est aveugle hors de son rayon. Les expéditions sont le seul moyen de voir, et
donc la seule source de croissance non industrielle du jeu.

Pendant l'introduction, elles font aussi office de filet de sécurité en CU. **Ce rôle
disparaît après l'amorçage** : au-delà, les missions rapportent de la carte, des sites,
des plans — jamais de la monnaie. Sinon on se retrouve avec deux économies parallèles,
et le joueur arbitre entre construire une usine et envoyer des sondes.

### 6.2 Déclenchement et coût

Deux sondes apparaissent quand la réserve descend **sous 35 000 CU**. Elles sont
offertes et ne consomment aucun slot.

**Lancer une mission ne coûte jamais de CU.** C'est la condition pour que le plancher à
zéro reste une sortie et non une impasse.

### 6.3 Les sondes

**Autonomie comptée en missions, pas en temps : 10 missions chacune**, affichées dès la
première. Une sonde qui tombe en panne sans prévenir serait vécue comme une trahison ;
une sonde qui affiche son compteur installe une urgence honnête et pousse à explorer
tôt.

Les sondes ne sont **jamais détruites**. Elles s'éteignent, batterie vide.

Vingt missions avec deux sondes, trente avec la troisième : très largement de quoi
couvrir l'introduction. **L'extinction de la dernière sonde doit être calée sur la
découverte du nid.** Le Noyau perd ses yeux à l'instant précis où il apprend qu'il est
menacé, et la branche armement s'allume sur un aveuglement plutôt que sur une
abondance.

**Contrainte de sécurité** : l'autonomie doit dépasser l'introduction. Si les sondes
s'épuisent avant que le joueur ait de quoi produire des unités, et qu'il est à court de
CU au même moment, il n'a plus aucune sortie.

### 6.4 Les unités

Après les sondes, tout passe par des unités produites. C'est un type nouveau posé à
côté de la sonde, jamais une transformation de celle-ci.

Les unités **peuvent mourir**. Sans cela, le joueur finit avec des centaines d'unités et
le système perd tout enjeu.

Trois pressions distinctes empêchent l'accumulation, et elles ne doivent pas se
ressembler :

**L'entretien** pèse sur l'économie. Chaque unité consomme du CU armement en continu —
le cerveau dépense de l'énergie à les contrôler. Coût **légèrement croissant par
unité** plutôt que fixe : une petite escouade reste confortable, une armée dormante
devient ruineuse. Plafond mou, sans jamais rien interdire. C'est aussi ce qui donne son
poids réel à l'axe CU armement du Datacenter — entretenir une armée, c'est du calcul en
moins pour la recherche.

**L'usure** pèse sur la disponibilité. Les pièces se dégradent au fil des missions, plus
ou moins vite selon le risque, avec une part d'aléatoire. L'état de chaque unité est
affiché, et le joueur peut délibérément envoyer une unité abîmée. Le choix « j'attends
la réparation ou je pars maintenant à effectif réduit » est excellent, et il explique
les catastrophes après coup.

**La mort** pèse sur l'effectif. Elle survient sur échec grave.

Le **centre de réparation** est un bâtiment débloqué par la branche armement. Il
immobilise les unités un certain temps ; sa capacité devient un palier d'amélioration
naturel.

### 6.5 Types de mission

| Type | Risque | Durée | Cible | Récompense | Disponibilité |
|---|---|---|---|---|---|
| Reconnaissance | bas | 3 min | zone inexplorée | carte + points d'intérêt | dès le départ |
| Récupération | moyen | 6 min | point d'intérêt repéré | matériaux, CU (intro seulement) | dès le départ |
| Étude de civilisation ancienne | élevé | à définir | ruine repérée | nœuds ??? | après amorçage |
| Relevé de menace | moyen | à définir | zone hostile | décompte précis des ennemis | après le nid |
| Restauration de datacenter | élevé | à définir | datacenter abandonné | nouvelle emprise, zones | milieu de partie |
| Éradication | élevé | à définir | nid | suppression de la menace | après armement |

Ces durées sont celles d'une escouade minimale. Chaque unité supplémentaire les
allonge.

Le type ne change pas seulement le danger, il change **ce qu'on va chercher**. Le joueur
arbitre donc même à risque égal.

La Récupération n'existe que grâce à une reconnaissance préalable. C'est la **boucle en
deux temps**, rendue obligatoire par la structure plutôt que par une règle : on explore
à l'aveugle, puis on choisit en connaissance de cause.

Le **Relevé de menace** est la seule mission qui donne un chiffre exact. C'est cohérent
avec le principe directeur : cette précision a coûté une expédition entière. C'est aussi
ce qui rend viables deux styles de jeu — celui qui reconnaît d'abord, celui qui fonce.

Pendant l'introduction, seuls les deux premiers types existent.

### 6.6 Écran de lancement

**Vue carte fortement dézoomée.** Le Noyau au centre avec son rayon d'action en
pointillé, la zone révélée en clair, le brouillard autour.

**Au survol d'un secteur** : une infobulle donne son nom, le risque estimé et les types
de mission disponibles. Un secteur jamais approché n'affiche ni l'un ni l'autre, juste
*non reconnu* — le survol ne révèle jamais ce qu'une sonde n'a pas rapporté.

**À la sélection**, un panneau latéral affiche :

- le nom du secteur
- les types de mission disponibles, en pastilles sélectionnables
- le **risque estimé**, en niveau qualitatif uniquement, toujours qualifié d'*estimé*
- le sélecteur d'escouade, unité par unité, avec l'état de chacune
- une **jauge qualitative** qui se déplace selon l'effectif : téméraire, juste, prudent,
  surdimensionné
- la **durée estimée**, qui augmente avec l'effectif
- le bouton de lancement

**Aucun pourcentage n'est jamais affiché.** Un pourcentage transformerait la mission en
calcul et ferait disparaître l'inconnu.

### 6.7 Résolution

Tout est tiré en interne.

Pendant l'introduction, avec les sondes, **la révélation de carte ne peut pas échouer** —
seule la récolte le peut. La sonde revient toujours avec le fragment de carte.

Avec les unités, la catastrophe devient possible : même une reconnaissance à risque
faible peut tomber sur un nid. Le risque annoncé est une **estimation, pas un contrat**.
C'est ce qui empêche l'étiquette de devenir une garantie.

**Quand une escouade ne revient pas**, la carte est révélée **jusqu'au point de
rupture** : le Noyau a reçu les données jusqu'à la dernière transmission, puis plus
rien. Un marqueur reste posé là où le contact a été perdu. La catastrophe rapporte donc
une information et laisse une cicatrice sur la carte — un endroit que le joueur
regardera longtemps avant d'y retourner. Sans rendre la mort indolore pour autant.

**Le rapport de retour dit ce qui s'est réellement passé** : nid croisé, zone effondrée,
escouade repérée, blindage insuffisant. Le joueur ne connaît jamais les probabilités
mais il apprend le monde. Au bout de quelques expéditions, il *sent* qu'une étude de
civilisation ancienne demande du monde, sans qu'on le lui ait jamais écrit. Chaque
raison d'échec est aussi une piste vers une amélioration future.

### 6.8 Frein à la surenchère

**Deux missions peuvent tourner en parallèle**, et le compteur est affiché en permanence
sur la top bar avec le temps restant de chacune. Le joueur n'a jamais à ouvrir la carte
pour savoir où il en est.

Envoyer tout son effectif sur une seule mission, c'est renoncer à l'autre pendant ce
temps — et plus tard, laisser la base sans défense. C'est le coût d'opportunité qui
arbitre, pas le calcul de risque.

Plus d'unités réduit le danger **et allonge la mission** : une grosse escouade se
déplace au rythme du plus lent. Le joueur paie sa sécurité en temps, sans qu'on lui
montre un chiffre.

### 6.9 Temps forts scénarisés

Trois moments ne dépendent d'aucun tirage :

1. **L'apparition des sondes** au seuil de 35 000 CU.
2. **La découverte du signal anormal**, qui révèle le nœud ??? et donne la troisième
   sonde. Récompense choisie exprès pour faire regretter de ne pas avoir exploré plus
   tôt, sans jamais bloquer quoi que ce soit.
3. **La découverte du nid dormant**, qui allume la branche armement. Site posé par le
   générateur à une distance donnée, révélé à coup sûr par une mission précise une fois
   la troisième sonde acquise, et **uniquement après l'amorçage du Datacenter**.

Le nid est découvert **dormant**. Le joueur sait qu'il se réveillera sans savoir quand.
Être attaqué dans la minute qui suit ferait de la découverte une punition ; savoir
qu'une menace existe et avoir le temps de s'y préparer est exactement le plaisir de la
tower defense, et ça donne une raison d'exister aux premières recherches d'armement
avant que le premier ennemi n'arrive.

### 6.10 Sites de l'introduction

Le gisement de missions est **fini**. Une fois visités, les sites sont épuisés. Ce ne
sont pas des missions répétables : sans cela, les expéditions deviennent une fontaine à
CU qui annule toute la tension de la réserve finie.

| Cible | Nombre | Récompense unitaire | Total |
|---|---|---|---|
| Secteurs reconnaissables | 8 | 500 CU | 4 000 CU |
| Points de récupération découverts | 5 | 1 500 CU | 7 500 CU |
| **Potentiel total** | | | **11 500 CU** |

Les cinq points de récupération n'existent que si les reconnaissances les ont révélés :
un joueur qui n'explore pas ne touche rien, un joueur qui explore partiellement touche
une fraction. Le rendement de l'exploration est donc progressif, jamais tout ou rien.

**Exception** : au moins un site se régénère lentement, très peu rentable, pour garantir
la sortie du plancher à zéro.

---

## 7. L'interface générale

La top bar se révèle **au même rythme que le cerveau se répare** :

| Phase | Contenu |
|---|---|
| Survie | stock de CU avec autonomie, plafond de bâtiments |
| Ouverture des expéditions | ajout du compteur de missions en cours, 2 au maximum, avec le temps restant |
| Après amorçage | apparition du débit net, le chiffre passe au vert |
| Ensuite | séparation des types de CU |

Le joueur n'a jamais plus de deux chiffres à comprendre à la fois.

Le widget de **bridage** est un composant réutilisable : jauge où la capacité nominale
est dessinée, butoir à la valeur réelle, cause en dessous. Chaque bâtiment bridé
l'affiche avec sa propre cause, et le joueur apprend à le lire une fois pour toute la
partie. Quand un bridage est levé, le butoir glisse vers la droite — récompense
visuelle gratuite et bien plus satisfaisante qu'un chiffre qui change.

---

## 8. Notes d'implémentation Unity

**Données.** Un `ScriptableObject` par recherche : id, nom, description, icône, coût CU,
débit d'absorption, prérequis, effets, palier, angle ou ajustement de position, état
révélé ou masqué. Idem pour les recettes, les bâtiments, les types de mission et les
sites.

**Runtime.** Un `ResearchManager` qui expose des events pour que bâtiments et recettes
se débloquent en réaction, sans que l'UI soit recâblée à chaque ajout.

**Rendu du menu neuronal.** uGUI plutôt qu'UI Toolkit : plus de liberté sur les effets
de tracé et de pulsation. Les synapses via un composant de tracé custom (`VertexHelper`)
ou des segments `Image` en 9-slice, avec un `Material` dont on anime l'offset de
texture pour le point lumineux qui circule.

**Éditeur.** Un outil custom pour visualiser l'arbre et déplacer les ajustements à la
souris. Sans lui, à trente nœuds, le placement devient un gouffre de temps.

**Génération de carte.** Les gisements de départ, le site du signal anormal et le nid
sont **ancrés à des distances imposées**. L'aléatoire ne s'exprime qu'au-delà.

---

## 9. Points à trancher

| Sujet | Question | Recommandation provisoire |
|---|---|---|
| Durée de vie des composants | 120 s donne un ratio de rentabilité de 14:1 pour le CPU et 7:1 pour la mémoire | Volontairement large : le facteur limitant doit rester le nombre de baies. À raccourcir si le stock de CPU devient trivial |
| Durées des missions tardives | Étude, relevé, restauration, éradication | À caler entre 6 et 15 min selon l'enjeu, une fois les deux premières validées |
| Entretien des unités | Quelle courbe pour le coût croissant par unité ? | À définir avec la branche armement |
| Second Datacenter | Quand, et avec quel appétit différencié ? | Après les deux extensions de baies, quand l'axe armement existe |

---

## 10. Ordre d'implémentation suggéré

1. **Modification de `CONTRACTS.md` §10.** Le contrat actuel affirme que « CU est une
   monnaie, pas un flux » et que « rien ne consomme du CU par seconde ». Le modèle de
   recherche par débit d'absorption contredit directement cette affirmation. C'est une
   évolution de contrat public au sens de `CONTRACTS.md` §13 : identifier tous les
   consommateurs, mettre à jour la documentation, adapter les tests, signaler
   explicitement le changement de comportement.
2. Refonte de l'économie CU : suppression du RP, de la Data Card et du Laboratoire,
   suppression de la production de CU du Core, `ReserveCap` porté à la réserve de départ,
   nouveaux `computeCost`, arrêt des machines à buffer plein, réserve finie.
3. Nouvelles recettes et coûts de construction, plafond à 40.
4. Consommation de combustible des centrales gaz proportionnelle à la demande.
5. Modèle de recherche en processus, prérequis multiples, file d'attente, pause à zéro CU.
6. Menu de recherche linéaire de l'introduction, avec ses cinq états et son panneau de
   détail.
7. Datacenter MK1, amorçage, baies, curseur de répartition, formule de rendement.
8. Système d'expéditions : sondes, carte dézoomée, deux types de mission, sites finis.
9. Transformation du menu en réseau radial, trois noyaux, algorithme de placement.
10. Nid, branche armement, unités, usure, entretien, réparation.
