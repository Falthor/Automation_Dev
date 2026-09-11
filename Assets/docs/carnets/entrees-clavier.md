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
raccourcis chiffrés posent (`UIFocus.IsTypingInAField`, qui a remplacé `IsTextFieldFocused` —
voir §6).

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

## 4. Quatorze `if` ne peuvent pas tenir une priorité

Échap était lu en quatorze endroits. Aucun n'arbitrait : chacun sortait tôt sur son propre état —
« suis-je le panneau actif », « quelque chose est-il sélectionné », « suis-je ouvert ». Ça
fonctionnait, et uniquement parce que ces états se trouvaient être mutuellement exclusifs. **Rien ne
le garantissait et aucun test ne regardait.**

Et un cas se recouvre pour de bon : rien ne désarme un outil de construction quand un panneau
s'ouvre, et cliquer une notification de datacard ouvre le panneau d'un robot. Un joueur peut donc
avoir un fantôme sur le curseur et un panneau ancré en même temps.

**L'exclusivité est dérivée, pas gagnée à la course.** Pas de drapeau « déjà consommé », pas d'ordre
entre les lecteurs, parce qu'il n'y a rien à consommer : le revendiquant est une fonction pure de
l'état, donc il ne peut nommer qu'un seul étage, et il nomme le même quel que soit l'ordre dans
lequel Unity fait tourner les composants. Un drapeau aurait rendu la réponse dépendante du premier
`Update` exécuté — le même défaut, déplacé d'un cran.

**Ce que l'arbitre a rendu visible en arrivant.** L'étage de l'outil armé était derrière la barrière
`IsUIBlockingInput` de `ConstructionInputAdapter`. Or l'outil armé gagne contre un panneau ouvert :
laissé sous la barrière, le lecteur n'aurait jamais tourné dans exactement le cas que l'arbitre lui
attribue, et la touche serait allée à personne. Échap est donc remonté au-dessus de la barrière,
tandis que R et T restent en dessous — ils n'ont de sens que quand le fantôme est affiché.

**Le quatrième étage demandé n'existait pas.** La consigne parlait d'« outil armé, panneau
contextuel, panneau global, puis le menu ». Mais `BuildingMenuController.IsOpen` n'est pas un état
indépendant : il est écrit depuis `GlobalPanelChanged` et n'est qu'un miroir de
`ActiveGlobalPanel == PanelName`. Le menu bâtiments **est** un panneau global. Trois étages, pas
quatre.

**Ce que le test peut tenir, et ce qu'il ne peut pas.** `IsClaimedBy` lit la touche physique, et un
test EditMode n'a pas de clavier à presser. Ce n'est pas un trou : l'exclusivité ne vit pas dans la
lecture de la touche. `IsClaimedBy` vaut `revendiquant == le mien && la touche est baissée`, et le
revendiquant est une valeur unique — donc épingler le revendiquant épingle qu'au plus un lecteur peut
agir. La table des huit combinaisons est écrite en littéraux plutôt que bouclée : un changement
d'ordre doit échouer contre une table que quelqu'un a décidée, pas contre une règle que le test
recalcule comme le code.

## 5. La table : ce qui a été fusionné, ce qui a été gelé

**L'asset existait déjà et n'était pas un leurre inerte.** `InputSystem_Actions.inputactions` est
l'asset **project-wide actions** du package, désigné depuis `ProjectSettings/ProjectSettings.asset`
et `EditorBuildSettings.asset` — deux références qui ne ressemblent pas à du code, et que le premier
inventaire avait manquées (§3). Le réécrire sur place plutôt qu'en créer un autre garde ces deux
références valides, et donne `InputSystem.actions` à l'exécution sans aucun champ sérialisé ni câblage
de scène. Ça compte : le menu de raccourcis vit dans `MainMenu.unity`, où il n'y a pas de
`GameRuntime` à quoi accrocher quoi que ce soit.

Le nom du fichier **et** le champ `"name"` sont restés identiques, parce que le `fileID` du sous-asset
principal en dépend. La preuve que ça a tenu est indirecte mais nette : les tests résolvent les 18
actions par `InputSystem.actions`, donc la référence pointe bien sur la table réécrite.

**La caméra et la carte partagent leurs quatre touches au lieu d'en posséder quatre chacune.** Elles
lisaient les mêmes touches physiques dans deux copies littérales séparées — la duplication même que
l'inventaire signalait. Une seule série d'actions, lue par deux consommateurs : réassigner « vers le
nord » déplace les deux, ce qui est ce que veut dire réassigner « vers le nord ». Et le menu montre
quatre lignes au lieu de huit identiques.

**Conséquence sur les sections demandées.** La consigne demandait « caméra, carte, construction,
interface ». Une fois le pan fusionné, « carte » n'a plus d'action propre : la section s'appelle
*Caméra et carte*, et une quatrième — *Barre d'outils* — porte les huit emplacements, qui ne sont ni
de la construction ni vraiment de l'interface.

**La souris n'entre pas dans la table, et c'est un choix, pas un oubli.** Elle n'est pas
réassignable ; il n'y a donc aucun binding à tenir en cohérence, et l'arbitrage clic/glisser est un
contrat entre trois composants qui partagent un seuil unique. La rendre réassignable permettrait une
configuration où cliquer ne sélectionne plus rien. « À toucher ensemble ou pas du tout » : pas du
tout.

Le glisser de la carte, lui, **ne pourrait pas** y entrer même s'il était réassignable : c'est un
`PointerDownEvent` d'UI Toolkit, où le bouton arrive en entier sur l'événement, pas une lecture de
l'Input System. Il est fixé au bouton gauche par une constante nommée — avant, il pannait sur
n'importe quel bouton, molette et clic droit compris, ce qu'aucun autre glisser du jeu ne fait.

**La duplication qu'on ne pouvait pas supprimer.** Les noms d'actions apparaissent deux fois : dans
l'asset, qui dit sur quelle touche chacune est, et dans `InputActionCatalogue`, qui dit lesquelles
existent et comment les nommer en français. Le format `.inputactions` n'a nulle part pour ranger un
libellé. Donc on ne l'a pas supprimée, on l'a **épinglée** : le test compare les deux ensembles dans
les deux sens, et une action ajoutée d'un côté et oubliée de l'autre fait échouer la suite au lieu
d'arriver en jeu comme une ligne vide ou un raccourci mort.

**Le rechargement de domaine désactivé mord ici aussi.** L'instance de l'asset survit aux sessions
de Play, donc appliquer les surcharges par-dessus ce qui restait laisserait la réassignation non
sauvegardée d'une session fuiter dans la suivante. `ApplyStoredOverrides` efface tout avant
d'appliquer : l'idempotence est l'exigence, pas une élégance.

**Le piège d'outillage du jour.** Le build hors ligne a compilé les huit assemblies au vert alors
que le projet ne compilait pas : `Game.Tests.EditMode` ne référençait pas `Unity.InputSystem`, et le
script exclut délibérément cet assembly (le NUnit d'Unity est net472 et ne se mélange pas à la façade
netstandard). Huit lignes vertes ne disent rien des tests. Et une seconde fois dans la même heure :
`[RuntimeInitializeOnLoadMethod]` tire un `[Preserve]` interne qui vit dans `Unity.Scripting.dll`,
que la liste de références du script ne ramassait pas — une erreur qui n'existait que hors ligne.

## 6. Le menu : trois comportements et un garde qui ne gardait rien

**`IsTextFieldFocused` ne pouvait pas fonctionner.** Les deux copies demandaient
`focusedElement is TextField`, ce qui ne répond jamais vrai. Un `TextField` est un
`BaseField<string>` et il **délègue son focus** : l'élément qui se retrouve focalisé est le champ de
saisie *à l'intérieur*, pas le champ lui-même. Le garde se lisait juste, compilait, et ne pouvait pas
partir. C'est le genre de défaut qui n'apparaît que le jour où quelque chose a vraiment du texte à
taper - sous la forme d'une touche qui écrit un caractère *et* déclenche un raccourci.

La question se pose donc a l'ascendance, pas a l'élément focalise seul. Une seule copie
(`UIFocus.IsTypingInAField`) pour les deux appelants.

**Et il ne protégeait de toute façon pas la capture.** On m'avait dit que le menu de raccourcis
serait le premier vrai cas de saisie du projet. Il ne l'est pas : une capture de touche n'est pas un
champ de texte et ne focalise rien. Ce qui protège la capture est autre chose - les action maps sont
éteintes le temps de la saisie (`InputBindings.Suspend`), pour deux raisons cumulées :
`PerformInteractiveRebinding` refuse de tourner sur une action activée, et la touche qu'on est en
train d'assigner ne doit pas *faire* son ancien travail. Appuyer sur B pour le réassigner ouvrirait
le menu des bâtiments derrière la boîte de dialogue.

**Le focus revient mordre une seconde fois, ailleurs.** Le clic qui lance une capture laisse le
bouton focalisé, et UI Toolkit active un `Button` focalisé sur Espace **et** sur Entrée. Assigner
l'une des deux aurait terminé la capture et, sur la même frappe, resoumis le bouton qui l'avait
ouverte — donc rouvert la capture que le joueur venait de fermer. Le bouton se défocalise lui-même
au démarrage de la capture. Le correctif du commit 1 ne couvrait pas ce cas : il vit sur
`TopBarController`, qui n'est que dans la scène de jeu.

**Le conflit s'annonce, il ne se refuse pas.** Refuser une touche que le joueur a choisie le laisse
deviner laquelle des dix-huit lignes la détient déjà. Donc la touche prise est acceptée, la ligne qui
la détenait est **nommée**, et l'écrasement la laisse *visiblement* « non assigné » - un trou dans la
liste qu'il peut combler, plutôt qu'un échange silencieux.

Deux détails qui font que ça marche : refuser est un **annuler**, pas un non-événement, parce que
l'operation de rebinding a déjà appliqué la nouvelle touche - il faut remettre l'état d'avant, et
`null` (pas de surcharge) et `""` (délibérément non assigné) sont deux réponses différentes qu'on ne
peut pas confondre. Et la règle compare les chemins **effectifs**, pas ceux de l'asset : deux actions
qui ne se rejoignent que par leurs défauts sont bien en conflit, une que sa surcharge a déplacée ne
l'est plus.

**Échap annule au lieu de se faire assigner**, via `WithCancelingThrough` - ce qui a pour
conséquence que rien ne pourra jamais être assigné à Échap. C'est le bon arbitrage : une ligne ouverte
par erreur doit avoir une sortie, et ça compte plus que de pouvoir mettre Échap ailleurs.

**Ce que le test peut tenir ici.** La capture demande une frappe, et un test EditMode n'a pas de
clavier - donc « Échap annule », la ligne ambre qui écoute et la boîte de conflit ne sont pas
assertables. Ce qui l'est, c'est tout ce qui entoure la frappe : la liste se construit depuis le
catalogue dans son ordre, une section n'imprime son en-tête qu'une fois, la règle de conflit est une
fonction nommée qu'on interroge sur la vraie table, et une action sans touche se lit comme telle.

La chaîne affichée, elle, **ne peut pas être assertée** : elle dépend de la disposition de qui fait
tourner la suite, ce qui est exactement la raison pour laquelle il ne faut pas imprimer le chemin. Ce
qui est épinglé, c'est qu'elle n'est ni le chemin, ni le nom interne de l'action.

**Et les tests remettent la table où ils l'ont trouvée.** Le rechargement de domaine désactivé fait
que l'instance de l'asset est partagée avec la session d'éditeur : un test qui laisserait une
surcharge derrière lui changerait ce que lit la partie suivante.

## 7. Le même écran depuis deux endroits

Le menu devait s'ouvrir aussi en jeu, par le bouton Menu du Top Bar - que la spec importée décrit
comme un emplacement réservé sans fonction. Lui en donner une est un écart assumé, noté dans le bloc
d'état Unity de `GLOBAL_UI.md`.

**Le balisage devient un template plutôt qu'une copie.** `Shortcuts.uxml` est instancié par
`MainMenu.uxml` et par `TopBar.uxml` : un `<ui:Template>` plus un `<ui:Instance>`, résolus à
l'import, donc aucun champ sérialisé et **aucune édition de scène**. La deuxième copie du balisage
aurait été le même problème que les deux copies du cluster ZQSD, en plus gros.

**Il porte sa propre feuille de style.** Les styles empruntaient `.main-menu-button` et `.confirm-*`
a `MainMenu.uss`, ce qui marchait dans un écran et aurait rendu des boîtes nues dans l'autre. Tout ce
dont il a besoin est désormais dans `Shortcuts.uss`.

**Et cette feuille est attachée à l'overlay, pas a la racine du template.** Une feuille déclarée en
UXML s'attache à l'élément où elle est déclarée. `TopBarController` reparente l'overlay sur la racine
du document - pour passer au-dessus des arbres de tous les autres contrôleurs, dont l'ordre dépend
seulement de l'ordre dans lequel Unity a lancé leurs `Start`. Déclarée un niveau plus haut, la
feuille serait restée derrière : l'écran se serait affiché **stylé dans le menu principal et nu en
jeu**. C'est le genre d'écart qui ne se voit pas à la compilation et qui ne se voit pas non plus dans
l'écran où on l'a testé.

`BringToFront()` à l'ouverture plutôt qu'une confiance dans l'ordre des `Start` : le même
raisonnement, dit à l'endroit où il s'applique.

**Les actions sont éteintes tout le temps que l'écran est ouvert, pas seulement pendant une saisie.**
En jeu il se pose au-dessus d'un monde qui tourne : laisser les raccourcis vivants voudrait dire que
B ouvre le menu des bâtiments derrière, et qu'Espace met en pause dessous.

Ce choix a **une conséquence qu'il faut assumer et dire** : Échap ne ferme pas cet écran. Échap est
une action comme les autres et elle est éteinte avec le reste. Échap annule une *saisie*, parce que
l'opération de rebinding écoute sous la couche des actions. La sortie est FERMER, dans les deux
écrans pareillement - et c'est déjà le comportement qu'avait le menu principal, donc les deux hôtes
se ressemblent au lieu d'avoir chacun sa sortie.

Le corollaire côté code : `EndCapture` **ne** réactive pas, il ré-affirme la suspension. Réactiver là
aurait rendu les touches de jeu vivantes sous l'overlay dès la première réassignation — un défaut qui
n'apparaît qu'à la deuxième manipulation, donc jamais pendant qu'on écrit le code.
