# Documentation du projet

**Une information, un document.** Un sujet est décrit à un seul endroit et absent de tous les autres :
pas de résumé ailleurs, pas de rappel, pas de renvoi. Tous les documents sont lus, donc un renvoi
n'apporte rien et coûte une ligne à maintenir.

| Dossier | La question à laquelle il répond |
|---|---|
| [`architecture/`](architecture/) | **ce qui est** — le code livré, ses surfaces, ses règles |
| [`design/`](design/) | **ce qui est voulu** — conçu, pas encore construit |
| [`carnets/`](carnets/) | **pourquoi c'est comme ça** — fausses pistes, mesures, pièges |

---

## Qui possède quoi

**Les règles d'abord.** [`DEVELOPMENT_RULES.md`](architecture/DEVELOPMENT_RULES.md) gouverne toute
modification du projet et l'emporte sur le reste.

| Sujet | Document |
|---|---|
| Comment le projet est structuré : assemblages, principes, ordre de tri, amorçage | [`PROJECT_ARCHITECTURE.md`](architecture/PROJECT_ARCHITECTURE.md) |
| La carte : la grille (occupation, conversion), découpage, découverte, brouillard, secteurs, gisements, épaves, robots explorateurs | [`MAP.md`](architecture/MAP.md) |
| Le terrain, le sol, les biomes, le décor | [`TERRAIN.md`](architecture/TERRAIN.md) |
| L'assemblage nano d'un bâtiment et la conversion du sol sous lui | [`MATERIALISATION.md`](architecture/MATERIALISATION.md) |
| Le déplacement des objets : surfaces, géométrie, débit, tapis, splitters | [`TRANSPORT.md`](architecture/TRANSPORT.md) |
| Les bâtiments à recette : cycles, états, tampons | [`PRODUCTION.md`](architecture/PRODUCTION.md) |
| Poser, déplacer, démolir ; chantiers, robots constructeurs, GlobalStock | [`CONSTRUCTION.md`](architecture/CONSTRUCTION.md) |
| L'énergie et sa répartition par priorité | [`ENERGIE.md`](architecture/ENERGIE.md) |
| La réserve de CU | [`CALCUL.md`](architecture/CALCUL.md) |
| La recherche, ses effets, l'arbre et ses validations | [`RECHERCHE.md`](architecture/RECHERCHE.md) |
| Le matériel du Datacenter : usure, stabilité, rendement | [`DATACENTER.md`](architecture/DATACENTER.md) |
| L'interface : sélection, Échap, raccourcis, barres et panneaux | [`UI.md`](architecture/UI.md) |
| La sauvegarde : mécanisme, version, liste des clés | [`SAUVEGARDE.md`](architecture/SAUVEGARDE.md) |
| Produire un build, et les outils d'éditeur du projet | [`BUILD.md`](BUILD.md) |

Chaque champ de sauvegarde est décrit par le document du système qui le produit ;
[`SAUVEGARDE.md`](architecture/SAUVEGARDE.md) ne tient que le mécanisme et la liste des clés.

## Ce qui est voulu mais pas construit

[`design/`](design/) — à lire comme une intention, jamais comme un état.

**Un document de design est supprimé à la livraison, pas amendé.** Ce qui a été construit est décrit
dans `architecture/`, le raisonnement va au carnet. Le garder ferait un troisième exemplaire, qui
divergerait — c'est ainsi qu'une spécification finit par décrire un système retiré du code.

La règle est celle du mouvement, pas du contenu : ce qui entre dans `design/` en ressort à la
livraison. Un dossier de design presque vide n'est pas un défaut, c'est le bon signal — ce qui a été
conçu a été construit.

## Pourquoi c'est comme ça

[`carnets/`](carnets/) — un carnet par chantier. Ils ne décrivent pas l'état du code : ils gardent ce
que l'état du code ne peut pas dire — les fausses pistes et pourquoi elles l'étaient, les mesures qui
ont contredit une intuition avec leurs chiffres, les pièges d'outillage, et les écarts assumés par
rapport à une spécification.

**Un carnet est une mémoire de travail, pas une archive.** Une entrée n'y a sa place que tant que sa
conclusion n'est nulle part ailleurs ; une fois absorbée dans un document permanent, elle devient un
doublon qui vieillit plus mal que l'original. Relire un carnet à la fin d'un chantier fait partie du
chantier.

## Ce qui n'est plus ici

Les directives de chantier et les audits datés ont été supprimés : une fois le chantier fini, ils ne
pouvaient plus que diverger du code en gardant l'air de faire autorité. **Git garde leur texte**, ce
qui est tout ce qu'une trace doit être.
