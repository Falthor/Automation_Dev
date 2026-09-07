# Volume documentaire — élagage

Complément à `analyse-documentation.md`, qui traite de ce qui ment et de ce qui fait doublon. Ce
document-ci traite d'une autre question : **il y en a trop.**

---

## 1. Le chiffre

**86 600 mots**, environ 350 pages, pour un projet mené par une personne.

| Catégorie | mots | part |
|---|---|---|
| architecture | 23 000 | 27 % |
| archives à supprimer | 20 600 | 24 % |
| carnets | 17 300 | 20 % |
| conception | 14 500 | 17 % |
| directives de chantiers finis | 8 100 | 9 % |

Ça compte concrètement : Claude Code lit une partie de ce corpus au début de chaque tâche. Plus il y
a de mots, plus il en saute — et rien ne garantit qu'il saute les bons. Un corpus qui grandit sans
être élagué finit par être lu au hasard.

**Cible raisonnable : environ 45 000 mots**, soit la moitié.

---

## 2. Ce que le ménage déjà décidé retire

Les cinq `TASK_*`, `CURRENT_STATE.md`, `directive-brouillard-et-zonage.md` et les README à réécrire
pèsent **20 600 mots**, soit près d'un tiers du total. C'est acquis, sans arbitrage, et c'est le plus
gros gain disponible.

Les deux directives restantes ajoutent 8 100 mots, dont l'essentiel décrit des chantiers accomplis.
Après extraction de la géométrie de l'expansion, il en reste peu.

---

## 3. Le problème de fond : les carnets ne sont jamais élagués

| Document | mots |
|---|---|
| `materialisation-nano.md` | 8 174 |
| `brouillard-et-zonage.md` | 8 077 |
| `CONTRACTS.md` | 8 162 |
| `MAP.md` | **2 147** |

Un carnet fait quatre fois la taille du document qui décrit le système. Chacun pèse autant que le
contrat de tout le projet. Ce n'est pas un défaut d'écriture — les carnets sont bons — c'est qu'ils
n'ont jamais été relus après coup.

**Une entrée de carnet a une durée de vie.** Tant qu'une décision n'est pas absorbée dans la
documentation permanente, le carnet est le seul endroit qui la porte : il est alors indispensable.
Une fois qu'elle figure dans `MAP.md`, `TERRAIN.md` ou `CONTRACTS.md` — avec sa raison, comme
`MAP.md` le fait bien — l'entrée du carnet est un doublon qui vieillira moins bien que l'original.

### La règle

> Une entrée dont la conclusion est désormais énoncée dans un document permanent se réduit à une
> ligne et un renvoi. Ce qui reste dans le carnet est ce qui n'a trouvé sa place nulle part
> ailleurs.

Ce qui **reste** au carnet, et qui a de la valeur :

- les fausses pistes et pourquoi elles étaient fausses — la double précision qui rendait le port
  plus faux, le `1.0 - normalized.y` qui aurait cassé le mode radial, le bruit trop grossier qui
  renfle un cercle au lieu de le casser ;
- les mesures qui ont contredit une intuition — 43 % de désaccord contre 1 sur 3 000, deux
  ré-ancrages par chunk et non un, la taille du delta négligeable mais le temps à 231 ms ;
- les pièges d'outillage — l'asset modifié sur disque pendant qu'Unity le tient, une suite verte
  contre une assembly qui n'a pas recompilé ;
- les écarts par rapport à une spec, avec leur raison.

Ce qui **part** :

- la description de ce que le système fait aujourd'hui — c'est le rôle de `MAP.md` ;
- les décisions déjà énoncées ailleurs avec leur raison ;
- le récit chronologique d'un chantier terminé.

C'est d'ailleurs ce que dit déjà `DEVELOPMENT_RULES.md` §8 pour les documents d'architecture. La
règle vaut aussi pour les carnets, avec un délai : ils sont de la mémoire de travail, pas des
archives.

---

## 4. Deux documents sur les expéditions

`gdd-intro-recherche-expeditions.md` (6 961 mots) porte une section 6 sur les expéditions.
`SPEC_EXPEDITIONS.md` (3 656 mots) « complète la section 6 du GDD ». Personne ne lira jamais l'un
sans l'autre.

Deux sorties possibles :

- la section 6 du GDD se réduit à un renvoi, et la spec devient le seul document sur le sujet ;
- ou la spec est absorbée dans le GDD, qui redevient le document unique de l'introduction.

La première est préférable : la spec est plus détaillée, plus récente, et c'est elle qui a reçu les
amendements de la grande carte. Le GDD garde ce qu'il fait de mieux — l'économie, le déroulé, le
chiffrage.

À vérifier au passage : le GDD contient-il d'autres sections qui doublonnent avec un document plus
récent ? Il a été écrit avant la plupart des systèmes livrés.

---

## 5. Ce qui n'est pas à toucher

`MAP.md`, `TERRAIN.md`, `DEVELOPMENT_RULES.md`, `WORKFLOW.md` et `expeditions.md` sont au bon
format et à la bonne taille. `MAP.md` est le modèle : il décrit le code livré, donne les raisons, et
tient en §5 la liste de ce qui manque, mise à jour au fil de l'eau.

`CONTRACTS.md` (8 162) et `GLOBAL_UI.md` (5 133) sont gros mais décrivent des surfaces réellement
étendues. À relire pour vérifier qu'ils ne portent pas d'historique, sans objectif de réduction.

---

## 6. Après élagage

| Catégorie | avant | après |
|---|---|---|
| architecture | 23 000 | 23 000 |
| archives | 20 600 | 0 |
| carnets | 17 300 | ~6 000 |
| conception | 14 500 | ~11 000 |
| directives | 8 100 | ~3 000 |
| **total** | **86 600** | **~43 000** |

La moitié, sans perdre une décision.
