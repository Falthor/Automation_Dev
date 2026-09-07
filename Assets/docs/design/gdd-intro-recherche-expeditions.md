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
à côté de l'existant. Un robot explorateur continue de se comporter comme un robot explorateur quand les
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
(apparition des robots explorateurs, signal anormal, découverte du nid) ne dépendent jamais d'un
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
de slot. Le parc nécessaire est de 31 slots, chaîne énergétique comprise (le Storage Box
n'en fait plus partie depuis la tâche 01B), ce qui laisse neuf erreurs possibles au joueur.
La limite se fait sentir dans la dernière ligne droite sans jamais enfermer. L'Allocation
mémoire, qui relève ce plafond, devient la première recherche d'après-introduction.

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
| 3 | Passage sous **25 000 CU**. | seuil de réserve |
| 4 | **Introduction du système de missions** : deux robots explorateurs apparaissent, la carte dézoomée devient accessible, les secteurs limitrophes sont marqués *non reconnu*. | étape 3 |
| 5 | **Premières missions.** Reconnaissance à 500 CU, Récupération à 1 500 CU. Le joueur découvre que l'exploration paie. | choix du joueur |
| 6 | Une expédition découvre un **signal anormal** : le nœud ??? se révèle et donne une **troisième robot explorateur**. | site scénarisé, révélé à coup sûr |
| 7 | Recherche **Assembleur** (bâtiment + composant mécanique). | choix du joueur |
| 8 | Recherche **Modules de calcul** (CPU MkI + Memory MK1). | choix du joueur |
| 9 | Recherche **Datacenter MK1**, puis production de masse et construction. | choix du joueur |
| 10 | **Amorçage** : 90 s de consommation sans production. | pose du bâtiment |
| 11 | En interne, **la mission de découverte du nid devient disponible**. Rien n'est annoncé au joueur : le site apparaît simplement parmi les cibles possibles. | fin de l'amorçage |
| 12 | Le menu de recherche se transforme, **trois noyaux** apparaissent dont un éteint. La Fonderie avancée devient accessible. **Fin de la survie.** | fin de l'amorçage |
| 13 | **Découverte du nid dormant.** Le troisième noyau s'allume. Le dernier robot explorateur s'éteint au même moment. | mission débloquée à l'étape 11 |

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

**Règle : une centrale brûle en continu, qu'il y ait une demande ou non.** C'est un choix
délibéré. Le joueur ne doit pas pouvoir poser dix centrales en se disant qu'il est
tranquille — surdimensionner son énergie doit se payer.

Chiffré : une centrale consomme 0,8 CU/s en combustible, donc les trois nécessaires en
coûtent 2,4, soit environ 4 300 CU sur une introduction de trente minutes — 7 % de la
réserve. C'est ce qui rend le choix tenable : la dépense court à l'horloge, mais elle
reste petite tant que le joueur dimensionne juste.

Celui qui en pose dix paie sur trois fronts. Huit CU par seconde en combustible, deux de
plus en extraction puisqu'il lui faut quatre extracteurs de charbon pour les alimenter,
soit **18 000 CU sur l'introduction, près d'un tiers de sa réserve**. Et surtout quatorze
slots engloutis sur les quarante, alors que le parc en réclame vingt-sept : il devient
mécaniquement incapable de finir. La démolition remboursant intégralement, l'erreur reste
rattrapable, mais elle se paie en CU brûlé et en temps perdu.

Le charbon pose le plafond dur. Une grappe n'offre que quatre emplacements d'extracteur,
soit un charbon par seconde au maximum, donc dix centrales au grand maximum. Le plafond
de bâtiments mord bien avant.

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

> **⚠ Le code livre 70 000, pas 60 000** (`ComputeSystem.ReserveCap`). Ce n'est pas une copie
> périmée d'une constante : toute l'analyse ci-dessus est dérivée de 60 000, et à 70 000 la marge
> sèche passe de 25 % à **36 %** — un jeu sensiblement plus permissif que celui décrit ici.
>
> L'écart n'est **pas corrigé dans ce document**, parce que le trancher est une décision
> d'équilibrage et non une mise à jour de documentation : soit la réserve redescend à 60 000 et ce
> chiffrage redevient exact, soit elle reste à 70 000 et le chemin critique doit être rechiffré.
> Les deux demandent de jouer, pas de relire.

Trois profils de jeu en découlent, et c'est le signe que l'équilibrage est sain : le
méthodique passe sans expéditions ni options, le curieux prend les deux optionnelles et
se finance par l'exploration, le brouillon se rattrape aux expéditions.

### 4.5 Durée et parc

Parc cible : 8 extracteurs de minerai, 2 extracteurs de charbon, 6 fonderies,
8 factories, 3 assembleurs, 3 centrales gaz, plus le Datacenter. **31 slots sur les 40
disponibles.** Le Storage Box ne fait plus partie du parc de référence depuis la tâche 01B :
les tampons internes sont accessibles à la construction et la Boîte de stockage est passée en
recherche optionnelle.

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
| ??? | — | — | troisième robot explorateur | via expédition |

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

**Cette section a été retirée au profit de [`SPEC_EXPEDITIONS.md`](SPEC_EXPEDITIONS.md)**, qui la
détaillait déjà et qui a reçu depuis les amendements de la grande carte — les deux Reconnaissances
distinguées par la bande, le seuil dérivé, le disque inscrit, la révision du gisement fini. Personne
ne lira jamais l'un sans l'autre, et deux textes sur le même sujet vieillissent différemment : celui
qui n'est pas amendé finit par contredire l'autre sans que rien ne le signale.

La spec couvre le rôle et le cadre, le modèle de secteur, les exécutants, les huit types de mission
un par un, le lancement, la machine à états, la résolution, les sites de l'introduction, les temps
forts scénarisés, et la liste explicite de ce qui n'est pas décidé.

Ce que le GDD garde ici, parce que le reste en dépend : l'économie CU (§2 et §3), le déroulé de
l'introduction (§4) et son chiffrage. Le potentiel de 11 500 CU des expéditions y entre comme une
ligne du budget (§4.4), pas comme une description du système qui le produit.

---

## 7. L'interface générale

> **Cette section ne recoupe pas [`../architecture/GLOBAL_UI.md`](../architecture/GLOBAL_UI.md)**, et
> les deux répondent à des questions différentes. GLOBAL_UI décrit **ce que le HUD est** — les cartes,
> leur ancrage, l'expansion au survol, le routage des panneaux. Cette section-ci décrit **quand ses
> parties apparaissent** au fil de l'introduction, et un composant que GLOBAL_UI ne mentionne nulle
> part.
>
> **Rien de ce qui suit n'est implémenté.** Les cinq cartes de la top bar existent toutes dès la
> première frame, et il n'y a pas de widget de bridage dans le projet. C'est de l'intention, à sa
> place dans `design/`. La « révélation progressive » que GLOBAL_UI mentionne est autre chose : le
> détail d'une carte qui se déplie au survol, pas une carte qui apparaît en cours de partie.

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

**Runtime.** `Game.Gameplay.Research.ResearchSystem` expose des events pour que bâtiments et recettes
se débloquent en réaction, sans que l'UI soit recâblée à chaque ajout. Son contrat public est
`architecture/CONTRACTS.md` §11.

**Rendu du menu neuronal — en UI Toolkit, pas en uGUI.** Ce paragraphe recommandait l'inverse, pour
« plus de liberté sur les effets de tracé et de pulsation ». La recommandation contredit
`architecture/DEVELOPMENT_RULES.md` §6, qui pose UI Toolkit comme technologie primaire — et le besoin
qui la motivait est déjà résolu dans le projet : `HistoryGraphElement`, `HatchFillElement` et
`ClockGlyphElement` tracent tous en **Painter2D**, à l'angle et à la taille exacts, sans texture à
importer ni durée de vie à gérer. Les synapses relèvent du même geste. Une seconde technologie d'UI
coûterait deux thèmes, deux systèmes d'entrée et deux façons de router un panneau, pour un effet que
la première sait produire.

**Éditeur.** Un outil custom pour visualiser l'arbre et déplacer les ajustements à la
souris. Sans lui, à trente nœuds, le placement devient un gouffre de temps.

**Génération de carte.** Les gisements de départ, le site du signal anormal et le nid
sont **ancrés à des distances imposées**. L'aléatoire ne s'exprime qu'au-delà.

---

## 9. Points à trancher

**Tout ce qui touche aux expéditions est dans [`SPEC_EXPEDITIONS.md`](SPEC_EXPEDITIONS.md) §10**, qui
tient la liste à jour — durées des missions tardives, entretien des unités, taille d'escouade, repli
en cours de mission, et ce qui a été tranché depuis. Deux listes du même sujet divergent, et c'est
celle qu'on n'amende pas qui finit par mentir.

Restent ici les points qui n'appartiennent qu'à l'économie de l'introduction :

| Sujet | Question | Recommandation provisoire |
|---|---|---|
| Durée de vie des composants | 120 s donne un ratio de rentabilité de 14:1 pour le CPU et 7:1 pour la mémoire | Volontairement large : le facteur limitant doit rester le nombre de baies. À raccourcir si le stock de CPU devient trivial |
| Second Datacenter | Quand, et avec quel appétit différencié ? | Après les deux extensions de baies, quand l'axe armement existe |
| Plafond de réserve | Le code livre 70 000, ce chiffrage est bâti sur 60 000 | Voir l'avertissement en §4.4 : trancher demande de jouer, pas de relire |

---

## 10. Ordre d'implémentation

| | Étape | État |
|---|---|---|
| 1 | Refonte de l'économie CU : suppression du RP, de la Data Card et du Laboratoire, suppression de la production de CU du Core, réserve finie, nouveaux `computeCost`, arrêt des machines à buffer plein | **fait** |
| 2 | Nouvelles recettes et coûts de construction, plafond de bâtiments | **fait** |
| 3 | Modèle de recherche en processus, prérequis multiples, file d'attente, pause à zéro CU | **fait** |
| 4 | Menu de recherche linéaire de l'introduction, ses cinq états et son panneau de détail | **fait** |
| 5 | Datacenter MK1, amorçage, baies, curseur de répartition, formule de rendement | **fait** |
| 6 | Système d'expéditions : robots explorateurs, deux types de mission, sites finis | **le processus est fait**, l'interface non |
| 7 | Carte dézoomée : l'image existe (`SectorMapImage`), l'écran non | **en cours** |
| 8 | Transformation du menu en réseau radial, trois noyaux, algorithme de placement | à faire |
| 9 | Nid, branche armement, unités, usure, entretien, réparation | à faire |

L'étape qui ouvrait cette liste — modifier `CONTRACTS.md` §10, parce que le modèle de recherche par
débit d'absorption contredisait « CU est une monnaie, pas un flux » — est **accomplie** : §10 porte
maintenant l'exception, nommée, avec ses deux seuls appelants (`SpendUpTo` pour la recherche et
l'amorçage du Datacenter) et le renvoi à §13. Elle est retirée d'ici plutôt que marquée faite : une
première ligne qui demande de modifier un contrat déjà modifié se lit comme une consigne, pas comme
un historique.
