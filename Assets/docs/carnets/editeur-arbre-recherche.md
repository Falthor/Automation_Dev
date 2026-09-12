# L'éditeur d'arbre de recherche — carnet

Ce carnet ne décrit pas ce que l'outil fait : cela vit dans [`../BUILD.md`](../BUILD.md).

Il garde ce qu'aucun document permanent ne peut porter : **les fausses pistes, et les écarts assumés.**

---

## La disposition dérivée a disparu

`NeuralLayout` calculait les angles : secteurs subdivisés au prorata des feuilles, palier corrigé pour
qu'un enfant soit toujours au-delà de son parent. **Placer à la main et calculer une position répondent
à la même question ; en garder deux, c'est choisir laquelle ment.** Il est supprimé.

Les angles de l'arbre livré ont été repris **de sa propre sortie** : douze nœuds, aucun palier modifié,
chaque position identique au pixel près. Le menu n'a pas bougé.

Deux choses sont parties avec lui :

- **les nœuds « ? »** qu'il répartissait autour d'un noyau vide. L'armement est aujourd'hui un noyau
  seul. Le GDD décrit toujours des neurones en silhouette autour de lui : c'est un souhait de design,
  qui reviendra le jour venu comme contenu posé, pas comme un calcul ;
- **la correction du palier.** Le palier stocké fait foi, même s'il place un enfant sur l'anneau de son
  parent ou en dedans. L'éditeur ne le signale pas.

## Pourquoi un polaire et jamais une position

Une position figerait l'échelle et le centre : le menu ne pourrait plus se redimensionner, et déplacer
le centre de l'arbre décalerait tout. La distance n'est pas un entier, pour qu'un nœud puisse rester
entre deux anneaux.

La conversion n'est écrite qu'à un endroit, lu par le menu du jeu **et** par l'éditeur. Ce n'est pas de
l'ergonomie : si les deux la lisaient différemment, chaque nœud posé dans l'éditeur apparaîtrait
ailleurs dans le jeu, en miroir ou tourné.

## Les secteurs sont un repère, pas une contrainte

Chaque noyau a son secteur, une part égale du cercle centrée sur lui. Ils sont dessinés pour voir où
poser, **pas pour contraindre** : un nœud peut en sortir, et il est alors signalé plutôt que refusé.

## L'écart assumé sur la consigne

La consigne disait « aucune fenêtre, aucun raccourci, aucune annulation maison ». Elle a tenu : menu,
inspecteur, outil de la barre de scène, overlay et l'annulation d'Unity ont suffi. Un overlay est
arrivé après coup pour les rayons — ce n'est pas une fenêtre, et c'est l'endroit où un réglage
d'affichage se règle.
