# Tâche 05 — Robots constructeurs et logistique physique

> **État : réalisée, avec deux règles ajoutées au ticket.**
>
> Chantiers (`ConstructionSiteRuntime`), deux robots (`BuilderRobotRuntime`), réservation
> localisée par couples contenant-quantité, rapatriement à la démolition avec destruction de
> cargaison après 20 s, bandeau de notifications générique, et inversion du contrat
> `GlobalStock` (vue agrégée en lecture seule, plus jamais un contenant) — voir
> `CONTRACTS.md` §15.
>
> **Règle ajoutée n°1 — le refus de pose pour cause de coût disparaît.** Tel qu'écrit, le
> ticket décrivait deux comportements (§3 plusieurs chantiers en attente, §4 état d'échec
> nommant les matériaux manquants) inatteignables tant que la pose exigeait de pouvoir payer.
> `PlacementRefusalReason.CannotAfford` est donc retiré : poser réussit toujours sous réserve
> du verrou de recherche, du rayon d'action, du plafond de bâtiments et de la cellule libre.
>
> **Règle ajoutée n°2 — un chantier bloqué est sauté, jamais bloquant.** « Un seul chantier à
> la fois » porte sur l'exécution simultanée (les deux robots servent le même chantier), pas
> sur l'ordre strict de la file : les robots servent le chantier le plus ancien qui a de quoi
> être servi, et reviennent au précédent dès que ses pièces manquantes apparaissent. Sans
> cela, un chantier attendant un circuit imprimé gèlerait un extracteur posé derrière lui.

**Objectif : plus rien n'apparaît ni ne disparaît instantanément. Tout objet a un lieu, et
deux robots font la navette entre les contenants et les chantiers.**

Motivation : le Noyau n'a pas de corps. Construire d'un claquement de doigts est le seul
endroit de l'introduction où la fiction craque — et à l'usage, poser quatre extracteurs,
trois fonderies et les convoyeurs en vingt secondes ne fait ressentir aucun coût de
décision.

L'objectif n'est pas de ralentir le joueur mais de **rendre une décision perceptible**.
L'attente doit rester légère et ne jamais bloquer.

---

## 1. `GlobalStock` change de nature

`GlobalStock` ne disparaît pas, mais son contrat s'inverse.

Il porte aujourd'hui **deux rôles confondus** : il agrège pour l'affichage, et il détient
les objets qui n'ont de lieu nulle part — le stock de départ, les remboursements de
démolition. Le premier rôle est légitime, le second est la fiction à supprimer.

| Avant | Après |
|---|---|
| Contenant : détient des objets | **Vue agrégée : ne détient rien** |
| Source de prélèvement à la construction | lecture seule |
| Destination des remboursements de démolition | plus jamais crédité |
| Sérialisé dans `SaveData` | **recalculé**, plus sauvegardé |

**Périmètre de l'agrégat** : coffre du Noyau, Storage posés, tampons internes des bâtiments
de production. C'est-à-dire exactement l'ordre de collecte des robots.

L'invariant à préserver, et à tester : **ce que `GlobalStock` affiche est exactement ce dans
quoi un robot peut aller puiser.** Y ajouter les objets en transit sur les convoyeurs ou
dans les cargaisons donnerait un chiffre fluctuant qui ne correspondrait plus à ce qui est
réellement mobilisable.

Le nom reste identique alors que le contrat s'inverse : ce qui était une source devient un
miroir. **À écrire explicitement dans `CONTRACTS.md`**, sinon la confusion est garantie
pour quiconque lira le code dans six mois.

### La réservation devient localisée

Conséquence directe et non négociable. Le contrôle « ai-je de quoi construire » se fait
aujourd'hui contre un total. Après le changement, un total ne suffit plus : chaque pièce
promise doit savoir **de quel contenant** elle viendra.

La réservation s'exprime donc en couples contenant-quantité, jamais en total. Sans cela,
deux chantiers réservent les mêmes plaques et le second reste bloqué sans explication.

`GlobalStock` reste strictement en lecture, pour l'affichage et pour le test de faisabilité
d'une pose.

### Affichage

L'agrégat mérite un **panneau dédié**, ouvert depuis la navigation du bas. La top bar est
déjà à quatre cartes et un stock complet y tiendrait mal. Un panneau prendra par ailleurs
tout son sens quand plusieurs zones existeront, chacune avec son propre agrégat.

---

## 1b. Le coffre du Noyau

Le stock de départ n'est plus détenu par `GlobalStock` : il est déposé dans un **coffre
physique placé sous le Noyau**, présent dès le démarrage, alimenté depuis
`WorldGenerationSettings.StartingStock`.

| Propriété | Valeur |
|---|---|
| Emplacements | **6** |
| Capacité par emplacement | **200** |
| Entrée par convoyeur | **interdite** — aucun convoyeur ne peut s'y raccorder |
| Compte dans le plafond de bâtiments | **non** |

L'interdiction de raccordement garde ce coffre dans son rôle : une réserve de construction,
pas un dépôt de production.

**Attention au nombre de types.** Six emplacements, c'est six types simultanés, dont trois
sont déjà occupés au démarrage par les plaques, les fils et les engrenages. Or une
démolition de Datacenter en rend six à elle seule. Le débordement n'est donc pas un cas
limite mais un cas courant : la contrainte réelle est le nombre de types, pas la capacité —
les 1 200 unités ne seront jamais atteintes.

---

## 2. Les robots

| Propriété | Valeur |
|---|---|
| Nombre pendant l'introduction | **2** |
| Capacité | **4 unités chacun** |
| Vitesse | le diamètre du rayon initial en 10 s, soit **4,4 cellules/s** |
| Déplacement | libre, lignes droites et diagonales, sans contournement d'obstacle |
| Au repos | reviennent se garer à côté du Noyau |
| Chantiers simultanés | **un seul** — les deux robots servent le même chantier |

Un aller-retour moyen prend environ 7 secondes — 15 cellules de distance moyenne dans un
disque de rayon 22. Les deux robots transportent donc 8 unités par vague.

### Coût en vagues

| Bâtiment | Unités | Vagues | Temps approximatif |
|---|---|---|---|
| Extracteur | 5 | 1 | ~7 s |
| Fonderie | 10 | 2 | ~14 s |
| Centrale gaz | 15 | 2 | ~14 s |
| Assembleur | 17 | 3 | ~21 s |
| Factory | 20 | 3 | ~21 s |
| Fonderie avancée | 30 | 4 | ~28 s |
| Datacenter MK1 | 468 | 59 | ~7 min |

L'infrastructure complète de l'introduction représente environ **50 vagues, soit six
minutes** réparties sur toute la partie.

Le Datacenter est volontairement un morceau à part, juste avant l'amorçage de 90 secondes.
C'est le bâtiment final, et les robots qui font la navette pendant que le joueur regarde
sont un moment en soi. Si l'usage montre que c'est trop long, le levier sera **un troisième
robot débloqué par recherche**, pas une réduction du coût.

### Représentation visuelle

**Pour ce premier jet, un simple carré noir qui se déplace suffit.** Aucun asset, aucune
animation, aucune orientation.

L'enjeu de cette tâche est le comportement — la file de chantiers, la réservation
localisée, le rapatriement, le blocage — pas l'apparence. Un carré qui va d'un point à un
autre permet de valider et de régler tout cela. L'asset définitif viendra quand le
comportement sera figé, et il n'aura rien à changer au runtime puisque la vue ne fait
qu'interpoler une position dont le runtime est seul autoritaire.

Une seule exigence d'affichage : que les deux robots soient distinguables l'un de l'autre à
l'œil, ne serait-ce que par une nuance, pour pouvoir observer leur répartition sur un même
chantier.

### Réservation de part

**Chaque robot réserve sa part du reste à livrer avant de partir.** Sans cela, deux robots
partent chercher la même pièce manquante et livrent en double. C'est le défaut classique de
ce type de système, et il faut le traiter dès la conception plutôt qu'après coup.

---

## 3. Le chantier

Poser un bâtiment ne le crée plus : ça crée un **chantier**.

| État | Rendu |
|---|---|
| Prévisualisation valide | vert, inchangé |
| Prévisualisation invalide | rouge, inchangé |
| **Chantier en attente ou en cours** | **bleu** |
| Terminé | le bâtiment apparaît et devient fonctionnel |

Le bâtiment n'existe pas tant que la totalité des matériaux n'a pas été livrée. Il ne
produit rien et ne transporte rien.

Plusieurs chantiers peuvent être en attente. Ils sont traités **dans l'ordre de pose**, et
l'un est terminé avant que le suivant ne commence.

### Cas particulier des convoyeurs et splitters

Un glissé de convoyeurs crée **un seul chantier**, pas un par segment. Les robots y livrent
huit plaques par vague et les segments se matérialisent au fur et à mesure le long du
tracé — la ceinture pousse derrière eux.

Cinquante-cinq convoyeurs deviennent ainsi sept vagues au lieu de cinquante-cinq chantiers.
C'est indispensable : sans ce regroupement, poser une ligne serait une punition.

---

## 4. Réservation et sources

**Les matériaux sont réservés à la pose, prélevés au chargement.**

Poser un chantier marque les pièces comme promises. Elles restent physiquement dans leur
contenant, visibles, mais ne sont plus disponibles pour autre chose. Elles n'en sortent
qu'au moment où un robot les charge.

Sans réservation, le joueur consomme ailleurs ce que le robot allait chercher et le
chantier reste bloqué sans que rien ne l'explique.

**Ordre de collecte** : coffre du Noyau en priorité, puis les Storage posés, puis les
tampons de sortie des bâtiments de production. C'est le périmètre exact de `GlobalStock`
(§1).

**État d'échec** : si aucune source ne contient les ingrédients, le chantier l'affiche
explicitement — matériaux manquants, et lesquels. Jamais une attente silencieuse.

**Annulation** : libère la réservation. Rien ne bouge si aucun robot n'a chargé. Si un robot
transporte déjà des pièces, il les dépose lors de son retour au repos.

---

## 5. La démolition

Le bâtiment **disparaît immédiatement** — le joueur veut la place, c'est souvent la raison
même de la démolition. Mais ses matériaux ne réapparaissent nulle part : un robot doit
physiquement les rapatrier.

**Destination** : le coffre du Noyau en priorité, puis n'importe quel Storage ayant de la
place.

Coût en vagues : une Factory représente trois vagues de retour, un Datacenter cinquante-neuf.
C'est assumé — démolir un gros bâtiment est une décision, pas un réflexe.

### Débordement

Si le coffre du Noyau est plein **et qu'aucun Storage du monde ne peut recevoir la
cargaison**, le robot la garde et un compte à rebours de **20 secondes** démarre, au terme
duquel la cargaison est détruite.

Cette destruction est l'**anti-blocage** du système. Sans elle, un robot chargé
indéfiniment ne peut plus rien construire — et poser un coffre pour se libérer exige
justement un robot. C'est aussi la raison d'être du second robot : pendant que l'un porte un
surplus indéposable, l'autre reste disponible pour construire un stockage.

Le message ne doit donc pas présenter la perte comme évitable, puisque le joueur ne peut
généralement rien y faire dans les 20 secondes. Il doit **nommer la cause et la parade pour
la prochaine fois** : plus aucun stockage disponible, construire un coffre.

**Le surplus perdu est un choix assumé de simplicité, punitif et silencieux.** À documenter
comme tel dans `CONTRACTS.md`, pour que le jour où le comportement devra changer — dépôt au
sol, refus de démolition — on sache que c'était une décision et non un oubli.

---

## 6. Système de notifications

Cette tâche introduit un **bandeau de notifications sur le bord gauche de l'écran**, à
créer, puisque aucun mécanisme n'existe aujourd'hui pour informer le joueur d'un événement
qu'il n'a pas provoqué.

Premier usage : un robot ne peut pas se vider, avec son compte à rebours.

Le composant doit rester générique. D'autres événements l'utiliseront — chantier sans
matériaux, plafond de bâtiments atteint, plus tard le retour d'une expédition ou la
découverte d'un nid. Ne pas le concevoir autour du seul cas du robot.

Une notification porte une gravité, un message, une durée d'affichage, et optionnellement
un compte à rebours. Elle ne doit jamais bloquer l'interaction.

---

## 7. Architecture

- `ConstructionSiteRuntime` — le fantôme, la nomenclature attendue, les livraisons, les
  parts réservées par robot, l'état d'échec. Dans `Game.Gameplay`.
- `BuilderRobotRuntime` — machine à états : au repos, en route vers une source, en
  chargement, en route vers le chantier, en livraison, en rapatriement, bloqué.
- Les deux sont pilotés par le **tick central**, jamais par des `Update` individuels.
- La vue interpole la position entre deux ticks. Le runtime est autoritaire.
- `ConstructionService` cesse de déduire les matériaux et de créer le bâtiment : il crée un
  chantier et réserve. Son ordre de prélèvement est remplacé par l'ordre de collecte.

---

## 8. Sauvegarde

Doivent être capturés : les chantiers avec leur nomenclature restante et leurs réservations,
la position, l'état et la cargaison de chaque robot, les rapatriements en cours et leur
compte à rebours éventuel.

`SaveData.GlobalStock` disparaît : l'agrégat est recalculé au chargement à partir des
contenants réels. Le contenu du coffre du Noyau est sauvegardé comme celui de n'importe quel
Storage.

`Restore` tolère l'absence des nouvelles clés et retombe sur deux robots au repos sans
chantier.

---

## 9. Tests

- un chantier posé ne produit rien et n'est pas fonctionnel ;
- il devient un bâtiment exactement quand la dernière pièce est livrée ;
- deux chantiers en attente sont traités dans l'ordre de pose, un à la fois ;
- **deux robots servant le même chantier ne livrent jamais en double** ;
- un glissé de convoyeurs crée un chantier unique ;
- la réservation empêche une autre consommation de puiser dans les pièces promises ;
- un chantier sans source disponible passe en état d'échec en nommant ce qui manque ;
- une démolition fait disparaître le bâtiment immédiatement et déclenche un rapatriement ;
- un rapatriement essaie le coffre du Noyau puis tous les Storage avant de bloquer ;
- un robot bloqué détruit sa cargaison après 20 s et redevient disponible ;
- aucun convoyeur ne peut se raccorder au coffre du Noyau ;
- `GlobalStock` égale exactement la somme du coffre du Noyau, des Storage et des tampons de
  production, et rien d'autre ;
- `GlobalStock` n'est jamais crédité par une démolition ;
- deux chantiers ne peuvent pas réserver les mêmes pièces dans le même contenant ;
- les coffres ne comptent pas dans le plafond de bâtiments ;
- aller-retour `Capture`/`Restore` avec chantier partiel, robot chargé et rapatriement en
  cours ;
- `Restore` sur un blob amputé de ces clés ne lève pas.

---

## 10. Critères d'acceptation

1. Le projet compile, tous les tests passent.
2. `GlobalStock` ne détient plus aucun objet : il est en lecture seule et n'est plus
   sauvegardé.
3. Une partie neuve démarre avec le stock initial dans le coffre du Noyau.
4. Un bâtiment posé reste bleu jusqu'à livraison complète.
5. Une Factory est opérationnelle une vingtaine de secondes après sa pose.
6. Un glissé de convoyeurs se construit progressivement le long du tracé.
7. Une démolition libère la place immédiatement et les matériaux reviennent physiquement.
8. Un robot qui ne peut pas se vider le signale par une notification avec compte à rebours.
9. Les robots reviennent se garer près du Noyau quand la file est vide.

---

## 11. Rapport attendu

Format de `WORKFLOW.md` §11, avec en plus :

- le nombre réel de vagues et le temps total des robots sur une partie menée jusqu'au
  Datacenter, comparé aux 50 vagues et 6 minutes estimés ;
- le temps réel entre la pose d'une Factory et sa mise en service ;
- le comportement observé sur un glissé long de convoyeurs ;
- le comportement observé lors d'une démolition de gros bâtiment avec un coffre presque
  plein.
