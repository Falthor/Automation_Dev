# Expansion territoriale — conception

Les Noyaux secondaires, les zones minières, la densité des gisements et les paramètres de génération
qui les gouvernent. **Rien de ce document n'est implémenté** : c'est de la conception, pas un état.

Extrait de `directive-grande-carte.md`, dont le reste — la carte à 10 000, le découpage en chunks et
secteurs, le terrain qui ne se matérialise pas, le brouillard qui suit la caméra, les deux portées de
mission — a été livré et vit désormais dans [`../architecture/MAP.md`](../architecture/MAP.md),
[`../architecture/TERRAIN.md`](../architecture/TERRAIN.md) et
[`../carnets/brouillard-et-zonage.md`](../carnets/brouillard-et-zonage.md). La directive elle-même a
été supprimée : elle décrivait comme à faire ce qui était fait.

---

## 1. Le seuil est une formule, pas une distance

Tout ce qui suit se place par rapport à un seuil, et **ce seuil ne s'écrit jamais** :

```
seuil = 2 × rayon maximal d'un Noyau + vide minimal entre deux territoires
```

Il vaut ce qu'il vaut : assez loin pour que le rayon maximal d'un Noyau secondaire ne touche jamais
celui du principal, plus l'espace qu'on veut voir entre les deux.

| | rayon maximal | vide minimal | seuil |
|---|---|---|---|
| **La vision** | 80 | 90 | **250** |
| **Le code livré** | 80 (`GameRuntime.FurthestActionRadiusCells`, dérivé des effets de recherche) | 90 | **250** |

**Le code livré rejoint la vision.** Les trois recherches `extended_bandwidth` portent le Noyau à
42, 60 puis 80 cellules. Les nombres qui suivent supposent ce rayon, et le seuil les suit
automatiquement si on le change.

## 2. Six Noyaux secondaires sur un cercle de rayon égal au seuil

Six sites répartis à 60° autour du Noyau principal, sur un cercle dont le rayon **est** le seuil.

**L'écart nominal est invariant, par construction.** Dans un hexagone le côté vaut le rayon, donc
deux voisins sont séparés du seuil lui-même ; en retirer leurs deux rayons maximaux redonne
exactement le vide configuré. Le même vide sépare toutes les paires, y compris avec le Noyau
principal, **et cela reste vrai quel que soit le rayon maximal** — ce n'est pas une coïncidence à
revérifier à chaque changement de valeur.

**Variation retenue : ±20 cases sur le rayon, ±10 % sur l'angle.** Le cercle parfait se lirait comme
une construction ; ces deux variations suffisent à le casser. Elles mangent la marge, et c'est le
seul endroit où le rayon maximal change vraiment quelque chose :

| | seuil | pire écart angulaire | rayon minimal | distance au pire | vide restant |
|---|---|---|---|---|---|
| rayon max 80 | 250 | 48° | 230 | 187 | **27** |
| rayon max 32 | 154 | 48° | 134 | 109 | **45** |

Les rayons ne se touchent dans aucun des deux cas. La géométrie n'est donc pas cassée à 154, elle est
seulement plus resserrée en absolu et **plus confortable en marge** — les deux rayons à soustraire y
sont bien plus petits.

**Pose malgré tout la contrainte directement plutôt que de te fier à ces bornes.** La distance
minimale entre deux Noyaux quelconques vaut `2 × rayon maximal + vide minimal`, et les positions sont
tirées par rejet déterministe depuis la graine jusqu'à la satisfaire. La garantie ne dépend alors
d'aucun réglage, et elle survit à un changement du rayon maximal, du nombre de sites ou de
l'amplitude des variations. Sans elle, porter la variation d'angle à 20 % un jour ferait se toucher
deux territoires sans que rien ne le signale.

**Les zones minières entrent dans la même contrainte.** Elles ne doivent chevaucher ni un autre
gisement, ni un site de Noyau secondaire — en tenant compte de leur **rayon maximal** de 30, pas de
leur rayon initial de 10, puisqu'elles grandissent avec le nombre de gisements trouvés.

## 3. Le contenu garanti d'un site de Noyau secondaire

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
garde ce contenu » suffit ensuite à le protéger — c'est la même contrainte d'ordonnancement que celle
de la zone de départ, et elle est déjà documentée dans
[`../architecture/MAP.md`](../architecture/MAP.md) §5.

**Question ouverte :** trois ressources de base suffisent-elles à l'autonomie, ou un Noyau secondaire
doit-il pouvoir produire toute la chaîne ? La réponse décide si la garantie porte sur trois
ressources ou sur davantage.

## 4. Les zones minières : garantie par couloir, un seul minerai chacune

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

*(Un avant-poste peut être perdu si le joueur en perd le contrôle. Hors périmètre de ce document.)*

**Placement.** Aléatoire dans le couloir entre les deux Noyaux, sans toucher ni un territoire de
Noyau ni une autre zone minière, en tenant compte du rayon **maximal** de chacune. La contrainte ne
mord jamais : l'anneau entre le rayon maximal et le seuil accueille des dizaines de zones, bien
au-delà de ce qui sera garanti — 27 zones de rayon 20 ou 80 de rayon 10 dans l'anneau 80→250.

## 5. Des grappes plus rares et plus grosses en s'éloignant

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
de la distance au centre de la carte. C'est le levier d'équilibrage de toute l'expansion, et il ne se
règlera qu'en jouant.

## 6. Paramètres de génération — rien en dur, tout dérivé

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

*(Ce principe est déjà appliqué au seuil de mission : `SectorMissionRange.ExplorationMinimumCells` est
une propriété calculée, jamais un champ. Voir [`../architecture/MAP.md`](../architecture/MAP.md) §4.)*

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

### Paramètres exposés au joueur, avec leurs valeurs par défaut

Les lignes *en italique* sont **dérivées** : elles apparaissent ici pour être lues, jamais pour être
réglées.

**Monde**

| Paramètre | Défaut | Note |
|---|---|---|
| Graine | aléatoire | |
| Taille de la carte | 10 000 | bord infranchissable au-delà — **livré** |
| Taille de chunk | 64 | technique, probablement non exposé — **livré** |
| Taille de secteur | 16 | 4×4 par chunk, technique — **livré** |

**Noyaux**

| Paramètre | Défaut | Note |
|---|---|---|
| Rayon initial du Noyau principal | 40 | livré à 22, puis 42 / 60 / 80 par les trois `extended_bandwidth` |
| Rayon maximal d'un Noyau | 80 | **livré** |
| Rayon initial d'un Noyau secondaire | 40 | à confirmer |
| Nombre de Noyaux secondaires | 6 | répartis à 360°/n |
| Vide minimal entre deux territoires | 90 | **livré** (`SectorSettings.territorySpacingCells`) |
| Variation de rayon | ±20 | casse le cercle parfait |
| Variation d'angle | ±10 % | au-delà, la marge devient courte |
| *Distance nominale d'un Noyau secondaire* | *250 à rayon max 80 ; 154 à 32* | **dérivée** : 2 × rayon max + vide minimal |

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

**Missions** — livrées, voir [`../architecture/MAP.md`](../architecture/MAP.md) §4 et
[`SPEC_EXPEDITIONS.md`](SPEC_EXPEDITIONS.md).

| Paramètre | Défaut | Note |
|---|---|---|
| *Portée de l'exploration lointaine* | *le seuil et au-delà* | **dérivée** — livrée |
| *Portée de la prospection minière* | *du rayon courant au seuil* | **dérivée** — livrée |
| Robots explorateurs au démarrage | 2 | **livré** |
| Missions par robot | 10 | **livré** |
| Seuil de CU déclenchant les missions | 25 000 | **livré**, en fraction du plafond de réserve (70 000) |

### Pourquoi cette liste maintenant

La composition des ressources n'est pas figeable à ce stade : un joueur voudra spécialiser chaque
zone secondaire dans un produit qu'il transportera ensuite, un autre voudra pouvoir tout faire
partout, un troisième ne verra une zone que comme une source de minerai brut. Ces trois façons de
jouer demandent des mondes différents, et c'est précisément ce que la personnalisation doit
permettre — plutôt que de trancher aujourd'hui pour l'un des trois.

Les valeurs par défaut, elles, doivent servir le joueur qui découvre : garanties généreuses,
autonomie assurée, aucune partie injouable par malchance.
