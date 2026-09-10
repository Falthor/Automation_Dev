# Entrées clavier et souris — carnet

Ce carnet ne décrit pas ce que le système fait : les conventions d'interface vivent dans
[`../architecture/GLOBAL_UI.md`](../architecture/GLOBAL_UI.md), et les surfaces publiques dans
[`../architecture/CONTRACTS.md`](../architecture/CONTRACTS.md).

Il garde ce qu'aucun des deux ne peut porter : **les prémisses fausses, ce qu'un inventaire a trouvé
que personne ne cherchait, et les écarts assumés.**

---

## 1. `Key` nomme une position, pas une lettre

Le pan de la caméra lisait `zKey` et `qKey` sous un commentaire annonçant « AZERTY keys (Z=North,
Q=West) ». Les deux affirmations ne pouvaient pas être vraies ensemble : l'enum `Key` de l'Input
System **nomme les touches par leur position physique, avec la disposition US pour référence**. Sur
un clavier français, la position US-Z est la touche marquée **W**, et la position US-Q la touche
marquée **A**.

Le code bindait donc le nord et l'ouest sur W et A — un amas en diagonale, et aucune touche marquée
Z ne faisait quoi que ce soit. Les positions correctes sont `wKey` et `aKey`.

**Le commentaire est le vrai coupable.** Il ne s'est pas contenté de se tromper : il a affirmé que la
question était traitée, ce qui est la seule façon pour un défaut de cette nature de survivre à une
relecture. Un lecteur qui vérifie « est-ce que le clavier français est géré ? » trouve le mot
AZERTY et passe.

**La conséquence dépasse le correctif.** Tout ce qui *nomme* une touche au joueur doit demander au
contrôle son propre `displayName`, qui suit la disposition, et jamais le nom de l'enum — sinon un
menu de raccourcis affichera « Z » sur la touche marquée W. C'est la seule conclusion de l'inventaire
qui ne souffre aucune exception.

## 2. Espace avait un lecteur que personne n'avait écrit

La pause lit Espace directement, délibérément non gatée : mettre en pause derrière un panneau ouvert
est attendu. Mais UI Toolkit **active un `Button` focalisé sur Espace** (un `NavigationSubmitEvent`),
et un clic donne le focus. Donc après avoir cliqué n'importe quoi dans l'interface, mettre en pause
réactivait aussi le dernier bouton cliqué.

Le cas le plus net est le bouton Pause lui-même : cliqué, il gardait le focus, et Espace basculait
la pause **deux fois** — une par la lecture directe, une par la soumission. Espace avait l'air de ne
rien faire.

C'est **le focus** qui est fautif, pas la pause. Ce projet active ses boutons au clic ou par leur
raccourci chiffré, jamais en soumettant un bouton focalisé : un bouton cliqué n'a aucun usage du
focus qu'on lui donne.

**Un seul point de traitement, sur la racine du document.** Chaque contrôleur clone son arbre dans
la même racine, donc un callback en phase de bulle couvre le Top Bar, la barre du bas, le menu
bâtiments et tous les panneaux. Huit endroits à éditer — et huit à oublier — deviennent un.

**Il demande ce qui a le focus, pas ce qui a été cliqué.** Une carte du Top Bar est un `Button` qui
contient une icône et des libellés, et ces enfants prennent le clic pour eux : la cible de
l'événement n'est presque jamais le `Button` qui se retrouve focalisé. Lire le `focusController` est
la seule forme de la question qui survive à un bouton avec des enfants — et c'est déjà celle que les
raccourcis chiffrés posent (`IsTextFieldFocused`).

Seul un `Button` est défocalisé. Un champ de texte doit garder le focus qu'un clic lui donne, sans
quoi on ne peut pas y écrire.

## 3. Ce que l'inventaire a trouvé et qu'un grep ne trouve pas

Une recherche sur `KeyCode|Keyboard.current|Mouse.current` remonte 21 fichiers. Deux entrées bien
réelles n'y sont pas :

- **`SectorMapElement`** ne lit ni clavier ni souris : il reçoit `PointerDownEvent`,
  `PointerMoveEvent` et `WheelEvent`. C'est-à-dire tout le pan et tout le zoom de la carte.
- **`StoragePanelController`** teste `evt.button != 1` — un bouton de souris écrit en chiffre, sous
  aucune constante nommée.

Et une troisième n'existe sous **aucune** forme de constante : la soumission par Espace du §2, qui
est un comportement du framework.

**Correction à l'inventaire lui-même.** Il affirmait que `InputSystem_Actions.inputactions` n'était
référencé « nulle part ». Il l'est : c'est l'asset **project-wide actions** du package, désigné
depuis `ProjectSettings/ProjectSettings.asset` et `EditorBuildSettings.asset`. La recherche avait
couvert les scripts, les scènes et les prefabs — pas `ProjectSettings/`. Un inventaire d'entrées doit
lire les réglages du projet, parce que c'est un endroit où une référence ne ressemble pas à du code.
