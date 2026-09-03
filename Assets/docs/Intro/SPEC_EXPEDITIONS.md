# Spécification — Système d'expéditions

Document exhaustif du système de missions. Complète la section 6 du GDD, qui en donne les
principes ; ici, chaque mission, chaque règle et chaque état sont détaillés.

Ce qui n'a pas été décidé figure en section 10, explicitement, plutôt que d'être comblé
par une invention.

---

## 1. Rôle et cadre

Le Noyau est aveugle hors de son rayon d'action. Les expéditions sont le seul moyen de
voir, et donc la seule source de croissance non industrielle du jeu.

Pendant l'introduction, elles font aussi office de filet de sécurité en CU. **Ce rôle
disparaît après l'amorçage du Datacenter** : au-delà, les missions rapportent de la carte,
des sites, des plans — jamais de la monnaie. Sans cette coupure, deux économies parallèles
coexistent et le joueur arbitre entre construire une usine et envoyer des sondes.

**Lancer une mission ne coûte jamais de CU.** C'est la condition pour que le plancher à
zéro CU reste une sortie et non une impasse.

---

## 2. Le modèle de secteur

Le brouillard de guerre n'est pas géré cellule par cellule mais **par secteur**. Un secteur
est l'unité de mission : on ne reconnaît pas une case, on reconnaît une zone.

| Propriété | Règle |
|---|---|
| État | non reconnu, reconnu, ou perdu (contact rompu) |
| Révélation | définitive — un secteur reconnu ne se referme jamais |
| Contenu | zéro à plusieurs points d'intérêt, révélés avec le secteur |
| Zone de départ | le rayon d'action du Noyau plus une marge, révélée d'emblée |
| Affichage | sur la carte dézoomée, chaque secteur est soit connu, soit marqué *non reconnu* |

Le survol d'un secteur non reconnu n'affiche **ni risque ni type de mission** — seulement
son état. Le survol ne révèle jamais ce qu'une sonde n'a pas rapporté.

Un secteur déjà reconnu ne peut plus faire l'objet d'une reconnaissance. Il peut en
revanche accueillir des missions ciblant les points d'intérêt qu'il contient.

---

## 3. Les exécutants

### 3.1 Les sondes

| Propriété | Valeur |
|---|---|
| Apparition | deux sondes, quand la réserve descend sous 35 000 CU |
| Coût | offertes, aucun slot de bâtiment, aucun CU à l'usage |
| Autonomie | **10 missions chacune**, affichée dès la première |
| Destruction | **jamais** — une sonde s'éteint, batterie vide |
| Fin de vie | la dernière sonde s'éteint à la découverte du nid |
| Troisième sonde | débloquée par le nœud ??? de l'introduction |

L'autonomie est comptée en missions et non en temps, ce qui la rend garantissable. Elle
doit couvrir largement l'introduction : si les sondes s'épuisent avant que le joueur ait de
quoi produire des unités, et qu'il est à court de CU au même moment, il n'a plus aucune
sortie.

### 3.2 Les unités

Un type nouveau posé **à côté** de la sonde, jamais une transformation de celle-ci. La
sonde continue de se comporter exactement comme avant après leur arrivée.

| Propriété | Règle |
|---|---|
| Production | bâtiment Forge d'unités, débloqué par la branche armement |
| Entretien | CU armement en continu, **coût légèrement croissant par unité** |
| Usure | à chaque mission, plus ou moins vite selon le risque, avec une part d'aléatoire |
| État | affiché par unité ; le joueur peut délibérément envoyer une unité abîmée |
| Réparation | centre de réparation, immobilise l'unité un certain temps |
| Mort | possible sur échec grave |

L'entretien croissant crée un plafond mou : une petite escouade reste confortable, une
armée dormante devient ruineuse. C'est aussi ce qui donne son poids à l'axe CU armement du
Datacenter — entretenir une armée, c'est du calcul en moins pour la recherche.

---

## 4. Les missions, une par une

### 4.1 Reconnaissance

| | |
|---|---|
| **Objectif** | révéler un secteur non reconnu et les points d'intérêt qu'il contient |
| **Disponibilité** | dès l'apparition des sondes, jamais obsolète |
| **Cible** | un secteur non reconnu |
| **Exécutants** | sondes, puis unités |
| **Durée** | 3 min (escouade minimale) |
| **Risque annoncé** | bas |
| **Récompense** | le secteur révélé, ses points d'intérêt, et **500 CU pendant l'introduction seulement** |
| **Échec** | la révélation ne peut pas échouer avec une sonde ; avec des unités, une reconnaissance à risque bas peut malgré tout tomber sur un nid |
| **Révèle** | le secteur entier, définitivement |

C'est la mission d'ouverture et le socle de tout le reste : la Récupération n'existe que
grâce à elle.

### 4.2 Récupération

| | |
|---|---|
| **Objectif** | exploiter un point d'intérêt déjà repéré — épave, cache, dépôt |
| **Disponibilité** | dès qu'une reconnaissance a révélé un point d'intérêt |
| **Cible** | un point d'intérêt précis, jamais un secteur |
| **Exécutants** | sondes, puis unités |
| **Durée** | 6 min (escouade minimale) |
| **Risque annoncé** | moyen |
| **Récompense** | matériaux, et **1 500 CU pendant l'introduction seulement** |
| **Échec** | la récolte échoue ; le site n'est pas consommé |
| **Révèle** | rien de nouveau, le secteur est déjà connu |

C'est la **boucle en deux temps**, imposée par la structure plutôt que par une règle : on
explore à l'aveugle, puis on choisit en connaissance de cause.

### 4.3 Étude de civilisation ancienne

| | |
|---|---|
| **Objectif** | fouiller une ruine et en tirer une technologie perdue |
| **Disponibilité** | après l'amorçage du Datacenter |
| **Cible** | une ruine repérée par une reconnaissance |
| **Exécutants** | unités uniquement |
| **Durée** | à définir — plus longue que la Récupération |
| **Risque annoncé** | élevé |
| **Récompense** | **la seule source de nœuds ??? après l'introduction** |
| **Échec** | pertes possibles ; le site n'est pas consommé |
| **Révèle** | rien de nouveau |

Rare, et le joueur doit apprendre à la reconnaître comme l'occasion à ne pas manquer.

### 4.4 Relevé de menace

| | |
|---|---|
| **Objectif** | mesurer ce qui vit dans une zone hostile |
| **Disponibilité** | après la découverte du premier nid |
| **Cible** | un secteur hostile déjà reconnu |
| **Exécutants** | unités |
| **Durée** | à définir |
| **Risque annoncé** | moyen |
| **Récompense** | **le décompte exact des ennemis présents**, et rien de matériel |
| **Échec** | pertes possibles, aucune information rapportée |
| **Révèle** | la composition et les défenses du nid |

**C'est la seule mission qui donne un chiffre exact**, et c'est cohérent avec le principe
directeur : cette précision a coûté une expédition entière. Elle rend viables deux styles
de jeu — celui qui reconnaît d'abord et celui qui fonce.

### 4.5 Restauration de datacenter abandonné

| | |
|---|---|
| **Objectif** | remettre en service un datacenter distant pour ouvrir une seconde emprise |
| **Disponibilité** | milieu de partie |
| **Cible** | un datacenter abandonné repéré par une reconnaissance |
| **Exécutants** | unités, escouade importante |
| **Durée** | à définir — la plus longue du jeu |
| **Risque annoncé** | élevé |
| **Récompense** | une nouvelle emprise territoriale, de nouvelles zones exploitables |
| **Échec** | pertes lourdes ; le site n'est pas consommé |
| **Révèle** | la zone autour du datacenter restauré |

C'est le pont entre le jeu actuel — une base qu'on défend — et ce qu'il vise : une
conquête de territoire. Toute la progression d'après-introduction peut s'appuyer dessus.

### 4.6 Éradication

| | |
|---|---|
| **Objectif** | détruire un nid |
| **Disponibilité** | après les premières recherches d'armement |
| **Cible** | un nid repéré |
| **Exécutants** | unités de combat |
| **Durée** | à définir |
| **Risque annoncé** | élevé |
| **Récompense** | suppression définitive de la menace, accès à la zone |
| **Échec** | pertes lourdes ; le nid subsiste et, s'il s'agit d'une Sentinelle, **renforce ses défenses** |
| **Révèle** | la zone du nid |

Le renforcement des Sentinelles après un assaut manqué est déjà une règle du monde : elle
s'applique ici sans traitement particulier.

### 4.7 Récapitulatif

| Type | Risque | Durée | Récompense CU intro | Nœud ??? | Exécutants |
|---|---|---|---|---|---|
| Reconnaissance | bas | 3 min | 500 | non | sondes puis unités |
| Récupération | moyen | 6 min | 1 500 | non | sondes puis unités |
| Étude de civilisation ancienne | élevé | à définir | — | **oui** | unités |
| Relevé de menace | moyen | à définir | — | non | unités |
| Restauration de datacenter | élevé | à définir | — | non | unités |
| Éradication | élevé | à définir | — | non | unités de combat |

**Pendant l'introduction, seuls les deux premiers types existent.**

---

## 5. Lancement

### 5.1 Ce que le joueur choisit

1. une cible sur la carte dézoomée — un secteur ou un point d'intérêt ;
2. un type de mission parmi ceux que la cible autorise ;
3. combien d'unités il engage.

### 5.2 Ce qu'il voit

- le **risque estimé**, en niveau qualitatif, toujours qualifié d'*estimé* ;
- un **indicateur qualitatif** qui se déplace avec l'effectif : téméraire, juste, prudent,
  surdimensionné ;
- la **durée estimée**, qui augmente avec l'effectif ;
- l'**état de chaque unité** disponible.

**Aucun pourcentage de réussite n'est jamais affiché.** Un pourcentage transformerait la
mission en calcul et ferait disparaître l'inconnu.

### 5.3 Conditions de lancement

Une mission ne peut pas être lancée si :

- les deux emplacements de mission simultanée sont occupés ;
- aucun exécutant n'est disponible — sondes éteintes, unités en réparation ou déjà parties ;
- la cible ne correspond à aucun type de mission disponible.

**Deux missions simultanées au maximum.** Le compteur est affiché en permanence sur la top
bar, avec le temps restant de chacune, pour que le joueur n'ait pas à ouvrir la carte.

### 5.4 Le frein à la surenchère

Plus d'unités réduit le danger **et allonge la mission** : une grosse escouade se déplace
au rythme du plus lent. Le joueur paie sa sécurité en temps, sans qu'on lui montre un
chiffre.

Et surtout, engager tout son effectif sur une mission, c'est renoncer à l'autre pendant ce
temps — puis, une fois le nid découvert, laisser la base sans défense. **C'est le coût
d'opportunité qui arbitre, pas le calcul de risque.**

---

## 6. Machine à états d'une mission

```
        Lancée
          ↓
        En route      (durée estimée, aucune visibilité)
          ↓
       Résolution     (tirage interne)
          ↓
        Retour
          ↓
        Rapport       (notification au joueur)
          ↓
        Close
```

Pendant *En route*, le joueur ne voit rien du déroulement. Il connaît seulement le temps
restant, affiché sur la top bar.

La *Résolution* est intégralement tirée en interne. Le joueur n'en apprend le résultat
qu'au *Rapport*.

---

## 7. Résolution

### 7.1 Ce qui peut échouer

| Élément | Avec une sonde | Avec des unités |
|---|---|---|
| Révélation de la carte | **jamais** | possible si l'escouade tombe sur un nid |
| Récolte | oui | oui |
| Survie de l'exécutant | **jamais** — la sonde s'use, elle ne meurt pas | oui |

### 7.2 Le risque est une estimation, pas un contrat

Une zone donnée à risque bas peut, rarement, se révéler autre chose. C'est ce qui empêche
l'étiquette de devenir une garantie, et ce qui justifie qu'aucun chiffre ne soit affiché.

### 7.3 Perte totale d'escouade

Le secteur est révélé **jusqu'au point de rupture** — le Noyau a reçu les données jusqu'à
la dernière transmission, puis plus rien. Un **marqueur permanent de contact perdu** reste
posé à cet endroit.

La catastrophe rapporte donc une information et laisse une cicatrice sur la carte, un
endroit que le joueur regardera longtemps avant d'y retourner. Sans rendre la mort
indolore pour autant.

### 7.4 Le rapport

Il dit **ce qui s'est réellement passé** : nid croisé, zone effondrée, escouade repérée,
blindage insuffisant. Le joueur ne connaît jamais les probabilités mais il apprend le
monde. Au bout de quelques expéditions, il *sent* qu'une étude de civilisation ancienne
demande du monde, sans qu'on le lui ait jamais écrit.

Chaque raison d'échec est aussi une piste vers une amélioration future. C'est ce qui
transforme une perte en objectif, pour le coût d'une table de messages.

---

## 8. Les sites de l'introduction

Le gisement de missions est **fini**. Une fois visités, les sites sont épuisés. Ce ne sont
pas des missions répétables : sans cela, les expéditions deviennent une fontaine à CU qui
annule toute la tension de la réserve finie.

| Cible | Nombre | Récompense unitaire | Total |
|---|---|---|---|
| Secteurs reconnaissables | 8 | 500 CU | 4 000 CU |
| Points de récupération découverts | 5 | 1 500 CU | 7 500 CU |
| **Potentiel total** | | | **11 500 CU** |

Les cinq points de récupération n'existent que si les reconnaissances les ont révélés : un
joueur qui n'explore pas ne touche rien, un joueur qui explore partiellement touche une
fraction. Le rendement de l'exploration est progressif, jamais tout ou rien.

**Exception obligatoire** : au moins un site se régénère lentement, très peu rentable, pour
garantir mathématiquement la sortie du plancher à zéro CU.

---

## 9. Les temps forts scénarisés

Trois moments ne dépendent d'aucun tirage. Le hasard porte sur ce qu'on ramène, jamais sur
ce qu'on révèle.

**1. L'apparition des sondes.** Déclenchée par le passage sous 35 000 CU. Ouvre la carte
dézoomée et le système de missions.

**2. La découverte du signal anormal.** Un site posé par le générateur, révélé à coup sûr
par une reconnaissance. Il débloque le nœud ??? de l'introduction, qui donne la
**troisième sonde** — une récompense choisie exprès pour faire regretter de ne pas avoir
exploré plus tôt, sans jamais bloquer quoi que ce soit.

**3. La découverte du nid dormant.** Site posé par le générateur à une distance donnée,
révélé à coup sûr par une mission précise, **et uniquement après l'amorçage du
Datacenter**. Il allume la branche armement.

Le nid est découvert **dormant**. Le joueur sait qu'il se réveillera sans savoir quand.
Être attaqué dans la minute qui suit ferait de la découverte une punition ; savoir qu'une
menace existe et avoir le temps de s'y préparer est exactement le plaisir de la tower
defense, et ça donne une raison d'exister aux premières recherches d'armement avant que le
premier ennemi n'arrive.

La dernière sonde s'éteint au même moment : le Noyau perd ses yeux à l'instant précis où
il apprend qu'il est menacé.

---

## 10. Ce qui n'est pas décidé

Ces points sont ouverts. Ne pas les trancher à l'implémentation sans validation.

**Rappel d'une expédition en cours.** Un bouton de repli, abandonnant la récompense pour
sauver les unités, a été évoqué puis jamais tranché. Il crée une décision intéressante —
savoir quand renoncer — mais il rogne sur l'inconnu voulu.

**Les sondes peuvent-elles faire de la Récupération**, ou seulement de la Reconnaissance ?
Recommandation : les deux pendant l'introduction, sinon les 1 500 CU par site sont hors
d'atteinte tant que les unités n'existent pas.

**Taille minimale et maximale d'une escouade.** Non fixée.

**Où atterrit le butin.** Recommandation : le stock global du joueur, cohérent avec le
`StartingStock` existant.

**Un secteur lointain met-il plus de temps à atteindre ?** Recommandation : oui, la durée
combine le type de mission, l'effectif et la distance.

**Peut-on reconnaître n'importe quel secteur, ou seulement ceux adjacents au territoire
révélé ?** Recommandation : adjacence, ce qui crée une expansion naturelle vers
l'extérieur au lieu d'un saut arbitraire.

**Durées des quatre missions tardives.** À caler entre 6 et 15 minutes selon l'enjeu, une
fois les deux premières validées en jeu.

**Nombre de missions simultanées au-delà de l'introduction.** Deux pendant l'introduction ;
l'augmentation avec les unités reste à chiffrer.

**Perte partielle d'escouade.** Le principe d'un retour avec survivants a été évoqué puis
écarté au profit d'un modèle où les unités meurent réellement. Reste à préciser si une
escouade peut revenir amputée ou seulement entière ou détruite.

**Courbe exacte de l'entretien croissant des unités.** À définir avec la branche armement.
