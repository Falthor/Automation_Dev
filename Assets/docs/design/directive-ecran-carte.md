# Écran de carte — document directeur

Refonte de l'écran de carte. Deux maquettes l'accompagnent :
`maquette-carte-ensemble.png` et `maquette-carte-zone.png`.

**Les maquettes fixent la disposition, la hiérarchie et le langage visuel. Pas les dimensions
exactes, pas les couleurs au pixel près.** Ce document dit le reste.

Les décisions de conception du système de zones sont dans
`directive-zones-et-missions-intro.md`, qui fait autorité sur le contenu ; celui-ci ne traite que de
l'écran.

---

## 1. Le point de départ

`SectorMapImage` construit déjà la texture de carte, de façon incrémentale, à 0,25 ms par mission
qui rentre.

> **Corrigé à la livraison.** Ce paragraphe disait « elle n'a aucun consommateur — une pièce posée sans
> être raccordée » : c'était vrai deux chantiers plus tôt, et `SectorMapPanelController` la branchait
> déjà. Et l'image ne pouvait pas produire ce que §4 décrit : elle faisait **un texel par secteur**, pas
> par case, donc un disque révélé de rayon 8 remplissait un carré de 16 cases d'une couleur unie. Le
> chantier a donc bien été « dessiner du terrain » — un texel par case, une tuile par chunk découvert.
> Voir `architecture/MAP.md` §6.

L'écran actuel affiche une grille de carrés à la place. Le chantier n'est donc pas « dessiner du
terrain » mais brancher ce qui existe et empiler ce qui va dessus.

**La grille disparaît.** Elle occupe tout l'écran et ne dit rien : personne ne compte des cases sur
une carte stratégique. Le contenu, c'est le terrain révélé.

---

## 2. L'empilement

Du bas vers le haut :

| Couche | Source | Note |
|---|---|---|
| Terrain révélé | `SectorMapImage` | par chunk découvert, rien ailleurs |
| Traces de trajet | trajets des missions rentrées | bande d'un tiers du disque d'arrivée |
| Rayon d'action du Noyau | `CoreRuntime` | disque teinté, bord pointillé |
| Séparateurs de zone | découpage angulaire | six traits, échelle d'ensemble seulement |
| Sites | contenu de zone | faits, disponibles, mis en évidence, verrouillés |
| Sélection | état d'UI | |

**Le noir n'est pas une couleur de fond, c'est l'absence.** Rien n'est dessiné là où rien n'est
découvert — pas de fond de carte, pas de grille, pas de silhouette de continent. C'est ce qui donne
tout leur poids aux taches révélées et aux traces qui les relient.

---

## 3. Les trois échelles

Le clic sur une zone est un **raccourci de cadrage**, jamais un changement d'écran : la vue se
recentre et se zoome avec une animation courte. La molette et le glissement restent actifs en
permanence, et mènent au même endroit à la main.

| Échelle | Ce qu'on voit |
|---|---|
| Monde | le Noyau, son rayon, les six zones, le terrain révélé. Pas de sites, pas de libellés |
| Zone du Noyau | la base, à une échelle que le dézoom de jeu ne permet pas |
| Zone d'exploration | les disques révélés, les traces, les sites avec leurs libellés au survol |

**Le niveau « monde » s'arrête à ce qui a du sens** — le Noyau et ses six zones, soit environ deux
fois le seuil d'exploration. Une vue de la carte entière serait un point au milieu du vide.

### Le fil d'Ariane

`Monde › Zone nord-est › Cratère de Suie`. Les segments précédents sont cliquables et recadrent ; le
dernier est le lieu courant, donc inerte.

Il fait deux choses à la fois — dire où l'on est, permettre d'en sortir — et il n'a pas besoin d'être
expliqué. Un bouton retour seul dirait moins.

---

## 4. Ce que chaque couche dessine

### Le terrain

Un texel par case pour les chunks découverts, rien pour les autres. Les disques révélés apparaissent
donc comme des taches irrégulières séparées par du noir, et la vue montre surtout du vide troué.

**C'est voulu.** L'écart entre ce qui est ouvert et ce qui reste se lit d'un coup, et les taches
donnent envie d'être reliées.

### Les traces de trajet

Bandes le long des chemins parcourus, **un tiers de la largeur du disque d'arrivée**. Elles
convergent vers le Noyau et racontent l'histoire des explorations sans qu'aucun texte ne la dise.

Deux missions vers la même cible suivent le même chemin, donc les traces se superposent au lieu de
proliférer.

### Le rayon du Noyau

Disque légèrement teinté, bord en pointillé. **Proportion à respecter :** 40 cases de rayon contre
près de 200 pour la profondeur d'une zone. Sur les maquettes le rapport est d'environ un à quatre —
un rayon trop gros écraserait tout le reste et fausserait la lecture des distances.

### Les séparateurs de zone

Six traits partant du bord du rayon vers l'extérieur, à l'échelle d'ensemble uniquement. Ils sont
sourds : leur rôle est de montrer qu'il existe cinq autres directions, pas de les mettre en avant.

**Les zones verrouillées sont visibles mais éteintes** — leurs séparateurs existent, leur terrain
non. Le joueur voit qu'il y a d'autres directions sans qu'elles lui demandent quoi que ce soit.

### Les sites

Quatre états, tous distinguables sans lire de texte :

| État | Rendu |
|---|---|
| Disponible | cercle plein, couleur du type |
| Mis en évidence | anneau plus large, libellé affiché en permanence |
| Fait | cercle sourd, **non cliquable**, garde l'icône de ce qui a été trouvé |
| Verrouillé | cercle vide, contour sourd |

**Un site fait ne disparaît pas.** Un point qui s'efface donnerait l'impression que la zone se vide,
alors que le joueur y a gagné quelque chose. La carte devient la trace de ce qu'on a fait, et la
progression se voit sans lire la barre.

**Un seul libellé est affiché en permanence, celui du site mis en évidence.** Tous les autres
demandent le survol. C'est ce qui rend la suggestion visible sans clignotement.

---

## 5. Le panneau latéral

Il change selon l'échelle.

**Au niveau monde** : le nom de la zone choisie, sa progression de cartographie, et le décompte des
missions par état — disponibles, faites, nécessitant des unités.

> **Corrigé à la livraison.** Ces trois lignes ne valent qu'**après** le rapport de la première mission.
> Avant, elles décriraient des sites que personne n'a vus : le panneau porte à la place une carte de
> direction — image, cartographie, statut, ce que le lancement coûte aux cinq autres, une action. Voir
> `architecture/MAP.md` §5 et §7.

**Au niveau zone** : la progression, le nombre de sites restants, et le **décompte par type**. C'est
lui qui rend la cartographie lisible sans survoler chaque point : le joueur sait qu'il lui reste sept
sites et de quelle nature.

**Le décompte des disponibles ne compte que ce qui est lançable.** Les missions nécessitant des
unités figurent sur une ligne séparée et n'entrent jamais dans « disponibles » — un compteur qui les
inclurait mentirait.

---

## 6. La barre du bas

Elle annonce les trois interactions : **molette pour zoomer, glisser pour déplacer, cliquer une zone
pour la cadrer.** La version actuelle omet la troisième, qui a besoin d'être annoncée une fois.

---

## 7. Style

Les mêmes règles que le restyle des panneaux de bâtiment :

- **valeurs numériques en monospace à chiffres tabulaires** — compteurs, pourcentages, charges ;
- **petites capitales espacées réservées aux libellés de section** — `CARTOGRAPHIE`, `SITES CONNUS`,
  `MISSIONS`. Le reste en casse normale ;
- **angles droits**, séparateurs pleine largeur, panneau ancré ;
- français partout, virgule décimale, espace avant l'unité.

---

## 8. Ce que les maquettes ne montrent pas

- **Le halo d'alerte** autour de l'icône de carte à l'apparition de la mission du signal perturbé.
  Pas vert — le vert dit « acquis » dans la palette. Il s'éteint à l'ouverture de la carte, pas au
  lancement.
- **Le panneau de lancement**, qui s'ouvre à la sélection d'un site : type de mission, durée estimée,
  bouton de lancement. C'est un chantier voisin, pas celui-ci.
- **Le survol d'un site**, qui affiche son nom, son type et son risque estimé. Un secteur qu'aucun
  robot n'a visité n'affiche que « non reconnu » — jamais son risque ni ses types de mission, qu'on
  ne peut pas connaître sans y être allé.
- Les dimensions exactes, les couleurs au pixel, les durées d'animation.

---

## 9. Documentation à mettre à jour

Comme pour tout chantier : la documentation permanente décrit l'état accepté, et elle doit suivre.

- **`MAP.md`** est le document de sous-système concerné. Sa §5 liste « la carte dézoomée, son survol
  et son affichage du risque » comme non construite — cette ligne sort quand le chantier est livré.
  Ce qui est décrit ici — l'empilement des couches, les trois échelles, ce que `SectorMapImage`
  alimente — y a sa place, avec ses raisons.
- **`GLOBAL_UI.md`** si le routage des panneaux, l'ancrage ou le vocabulaire visuel changent.
- **`CONTRACTS.md`** si une surface publique est ajoutée — un accesseur de trajet, un état de site,
  un décompte exposé à l'UI.
- **`DEVELOPMENT_RULES.md`** si une règle nouvelle émerge, comme celles ajoutées cette semaine.

**Ce document-ci et les maquettes ne sont pas de la documentation permanente.** Ce sont des
intentions de conception : une fois le chantier livré, ce qui a été construit se décrit dans
`MAP.md`, et le raisonnement — décisions, écarts, mesures qui ont contredit une intuition — va au
carnet. Ne les recopie pas dans les documents d'architecture.

Signale-moi tout ce qui, en écrivant, s'avère contredire un document existant. La relecture d'un
sous-système est la meilleure occasion de trouver une divergence qu'aucun audit ciblé ne verrait.

---

## 10. Points ouverts

- **Les zones voisines apparaissent-elles en bordure de la vue d'une zone**, ou pas du tout ?
- **Le terrain révélé par un trajet traversant une zone verrouillée** sera visible. Le refus doit
  être clair au clic, sinon le joueur croira à un bug.
- La forme exacte de la marque d'un site fait.
