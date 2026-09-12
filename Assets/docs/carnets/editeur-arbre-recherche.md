# L'éditeur d'arbre de recherche

Pourquoi l'arbre se pose à la main dans une scène, et pourquoi cette scène ne fait foi de rien. L'état
du code est dans `CONTRACTS.md` §11 ; ici, les raisons.

## Ce qu'il fait

Tools > Research Tree > Open Editor Scene ouvre `Assets/Scenes/Tools/ResearchTree.unity` : un objet
par recherche de la base, noyaux compris, posé à sa distance du centre et à son angle.

- **Créer** : Tools > Research Tree > New Research. Avec un nœud sélectionné, la nouvelle recherche se
  pose un anneau plus loin, au même angle, et le prend pour prérequis ; sans sélection, elle arrive
  sur le deuxième anneau, hors de toute branche — et signalée comme inatteignable.
- **Remplir** : l'inspecteur d'un nœud est celui de son asset (nom, coût, effets typés, prérequis).
  La hiérarchie montre le nom affiché, et renommer un nœud dans la hiérarchie renomme la recherche. Le
  fichier prend le nom de l'identifiant, comme toutes les recherches livrées, une seconde après la
  dernière frappe — pas à chaque touche. Un identifiant qui ne peut pas être un nom de fichier, ou déjà
  pris, est signalé une fois dans la console, et le fichier garde son nom. Renommer ne casse aucune
  référence : la base et les prérequis pointent l'asset, pas son nom. L'identifiant, lui, est ce que
  les sauvegardes retiennent : c'est le nom technique, le nom affiché n'est que ce qu'on lit.
- **Relier** : l'outil *Link* de la barre d'outils de la scène, proposé dès qu'un nœud est
  sélectionné. Un clic sur un nœud, puis sur un autre : le premier devient prérequis du second.
  Recliquer la même paire retire le lien ; cliquer dans le vide abandonne.
- **Déplacer** : l'outil de translation habituel, par le carré au centre de ses flèches. Le nœud suit
  la souris et reste où on le lâche, sur un anneau ou entre deux, au dixième d'anneau près ; à moins
  de 0,15 anneau d'un anneau, il s'y aimante. Le caler pendant le glissement le clouait à son anneau :
  il semblait ne pas bouger.
- **Regarder** : un overlay *Research Tree*, dans la scène, règle la taille des boules — un curseur
  pour les noyaux, un pour les recherches. Chaque boule porte l'icône de sa recherche, celle de son
  propre champ `icon` ; sans icône, elle reste une boule nue. Les noms ne sont plus dessinés en
  permanence : seule la boule sous le curseur se nomme.

Tout passe par l'annulation d'Unity. Rien n'est écrit sur disque avant File > Save Project, sauf la
création, qui enregistre tout de suite l'asset qu'elle fabrique.

## La scène ne stocke rien

**Les objets sont des poignées, et l'asset est la seule vérité.** Lâcher un nœud écrit sa distance et
son angle dans l'asset, puis repose le nœud là où l'asset le dit.
Toute autre modification de l'asset — l'inspecteur, une annulation, une fusion git — déplace le nœud.
Il n'existe aucun moment où la scène et l'asset peuvent dire deux choses différentes.

La scène se reconstruit depuis la base à l'ouverture et à chaque changement de sa hiérarchie : un nœud
manquant est recréé, un nœud dont la recherche a disparu est retiré, un doublon aussi. Supprimer un
nœud ne supprime donc rien — il revient. Le fichier de scène est jetable ; il est versionné pour
qu'on le trouve, pas parce qu'il contiendrait quoi que ce soit.

## Le polaire

**Une distance comptée en anneaux et un angle, jamais une position.** La distance n'est pas un
entier : un nœud peut rester entre deux anneaux, et un palier entier le pose sur un anneau. Une
position figerait l'échelle et le centre : le menu ne pourrait plus se redimensionner, et déplacer le
centre de l'arbre décalerait tout.

La conversion n'est écrite qu'à un endroit, `ResearchNetworkPlacement`, lue par le menu du jeu et par
l'éditeur. Un test la vérifie — ce n'est pas de l'ergonomie : si les deux la lisaient différemment,
chaque nœud posé dans l'éditeur apparaîtrait ailleurs dans le jeu, en miroir ou tourné.

## La disposition dérivée a disparu

`NeuralLayout` calculait les angles : secteurs subdivisés au prorata des feuilles, palier corrigé pour
qu'un enfant soit toujours au-delà de son parent. Placer à la main et calculer une position répondent à
la même question ; en garder deux, c'est choisir laquelle ment. Il est supprimé.

Les angles de l'arbre livré ont été repris **de sa propre sortie** : douze nœuds, aucun palier
modifié, chaque position identique au pixel près. Le menu n'a pas bougé.

Deux choses sont parties avec lui :

- **les nœuds « ? »** qu'il répartissait autour d'un noyau vide. L'armement est aujourd'hui un noyau
  seul. Le GDD (§5.4) décrit toujours des neurones en silhouette autour de lui : c'est un souhait de
  design, qui reviendra le jour venu comme contenu posé, pas comme un calcul ;
- **la correction du palier.** Le palier stocké fait foi, même s'il place un enfant sur l'anneau de
  son parent ou en dedans. L'éditeur ne le signale pas encore.

## Les secteurs sont un repère

Chaque noyau a son secteur, une part égale du cercle centrée sur lui — 120° pour trois. Ils sont
dessinés pour voir où poser, pas pour contraindre : un nœud peut en sortir, et il est alors signalé
en magenta, dans le résumé de la scène et dans son inspecteur. La branche d'une recherche est le
noyau auquel remontent ses premiers prérequis.

## Les deux validations

Ce sont celles de `ResearchTreeValidation`, pas une copie. Dans la scène : un nœud sur un cycle est
rouge, et ses liens aussi ; un nœud inatteignable est orange ; un résumé en haut à gauche nomme les
fautifs. Relier deux nœuds qui ferment un cycle écrit aussi un avertissement dans la console. Le jeu,
lui, les repasse au démarrage en développement.

## Un assemblage de plus

`Game.Tools` n'existe que parce qu'Unity refuse d'attacher un composant venu d'un assemblage
d'éditeur : la poignée, `ResearchNodeHandle`, doit vivre dans un assemblage d'exécution. Il porte la
contrainte `UNITY_EDITOR`, donc il n'est compilé que dans l'éditeur et aucun build ne l'embarque. Le
reste de l'outil vit dans `Assets/Editor/ResearchTree/`.

## Ce qui n'y est pas

- **La suppression d'une recherche.** Il faudrait la retirer de la base et des prérequis de toutes les
  autres ; ce n'était pas dans le minimum utile.
- **Annuler une création laisse le fichier.** L'annulation retire la recherche de la base et son nœud
  de la scène, mais l'asset reste sur disque, hors de la base — donc hors du jeu.
- **Aucune fenêtre, aucun raccourci, aucune annulation maison.** Menu, inspecteur, outil de la barre de
  scène, overlay et Undo d'Unity : c'était la consigne, et elle a suffi.

## La taille des boules n'est pas une donnée de l'arbre

Les deux rayons vivent dans les `EditorPrefs`, pas dans la scène ni dans un asset. La scène se
reconstruit depuis la base et ne retient rien : un rayon rangé là serait effacé au premier rebuild.
Un asset serait pire — il ferait entrer *la façon dont quelqu'un aime regarder l'arbre* dans les
données de l'arbre, donc dans les commits de tout le monde. C'est un réglage de confort, il reste sur
la machine qui l'a posé.

L'icône, elle, est bien une donnée : c'est le champ `icon` de la recherche, celui que le jeu possédait
déjà. La scène ne fait que le montrer, et n'ajoute aucun champ.
