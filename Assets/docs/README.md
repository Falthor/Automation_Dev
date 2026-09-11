# Documentation du projet

Trois dossiers, trois questions. Un document qui ne répond à aucune n'a pas sa place ici.

| Dossier | La question à laquelle il répond |
|---|---|
| [`architecture/`](architecture/) | **ce qui est** — le code livré, ses contrats, ses règles |
| [`design/`](design/) | **ce qui est voulu** — la conception, pas encore construite |
| [`carnets/`](carnets/) | **pourquoi c'est comme ça** — les décisions, les écarts, les mesures |
| [`archive/`](archive/) | rien. Ne fait pas foi, conservé comme trace. |

---

## Ce qui fait foi

**`architecture/` décide.** Dans cet ordre en cas de contradiction :

1. [`DEVELOPMENT_RULES.md`](architecture/DEVELOPMENT_RULES.md) — les règles de travail
2. [`PROJECT_ARCHITECTURE.md`](architecture/PROJECT_ARCHITECTURE.md) — les systèmes et leurs frontières
3. [`CONTRACTS.md`](architecture/CONTRACTS.md) — les contrats publics, dont le format de sauvegarde
4. [`WORKFLOW.md`](architecture/WORKFLOW.md) — la marche à suivre

Puis les documents de sous-système, qui font autorité sur leur propre domaine et seulement sur lui :
[`MAP.md`](architecture/MAP.md) (découpage, découverte, brouillard, secteurs),
[`TERRAIN.md`](architecture/TERRAIN.md) (terrain, sol, décor),
[`MATERIALISATION.md`](architecture/MATERIALISATION.md) (l'assemblage nano d'un bâtiment et la
conversion du sol sous lui), [`GLOBAL_UI.md`](architecture/GLOBAL_UI.md)
(barre haute, navigation, panneaux — spécification Godot importée, lire son propre en-tête pour savoir
ce qui est implémenté).

[`BUILD.md`](BUILD.md) reste à la racine : il ne décrit pas le jeu mais la façon de le produire, et
les contraintes qu'un build impose que l'éditeur ne révèle jamais.

## Ce qui est voulu mais pas construit

[`design/`](design/) — à lire comme une intention, jamais comme un état.

- [`gdd-intro-recherche-expeditions.md`](design/gdd-intro-recherche-expeditions.md) — le GDD de
  l'introduction : économie CU, déroulé en treize étapes, recherche, expéditions.
- [`SPEC_EXPEDITIONS.md`](design/SPEC_EXPEDITIONS.md) — le système d'expéditions en détail. **Rien
  n'en subsiste dans le code** : le processus a été construit puis retiré avec `MissionSystem` et
  `ExpeditionZoneSystem`, et les robots explorateurs qui restent errent sans mission. À relire comme
  une intention, comme le reste de `design/`.
- [`expansion-territoriale.md`](design/expansion-territoriale.md) — Noyaux secondaires, zones
  minières, densité des gisements, paramètres de génération. **Rien n'en est implémenté.**
- [`maquettes/`](design/maquettes/) — les écrans, en HTML.

En cas de contradiction entre le GDD et l'architecture : l'architecture décide *comment*, le GDD
décide *quoi*.

## Pourquoi c'est comme ça

[`carnets/`](carnets/) — un carnet par chantier. Ils ne décrivent pas l'état du code : ils gardent ce
que l'état du code ne peut pas dire, c'est-à-dire les décisions prises, les écarts assumés par rapport
aux spécifications, les pièges rencontrés et les mesures qui ont tranché.

- [`brouillard-et-zonage.md`](carnets/brouillard-et-zonage.md) — brouillard, découverte, secteurs,
  puis le passage de la carte à 10 000
- [`materialisation-nano.md`](carnets/materialisation-nano.md) — dissolve et couverture au sol
- [`expeditions.md`](carnets/expeditions.md) — le processus de mission
- [`entrees-clavier.md`](carnets/entrees-clavier.md) — clavier et souris : `Key` est une
  position et non une lettre, le focus qui volait Espace, ce qu'un grep ne trouve pas
- [`datacenter-usure.md`](carnets/datacenter-usure.md) — usure, stabilité et rendement des pièces :
  les formules, les chiffres qu'elles donnent, et les deux sens du mot « rendement »

**Les directives ont disparu.** Chaque chantier en avait une, qui disait ce qu'il fallait faire ; une
fois le chantier fini, elle ne pouvait plus que diverger du code sans que rien ne le signale. Ce
qu'elles contenaient de vrai a migré dans `architecture/` (l'état) et dans les carnets (les raisons),
ce qu'elles contenaient de non construit dans `design/`. Git garde leur texte.

## Ce qui ne fait pas foi

[`archive/`](archive/) — voir son propre README. Rien de ce qui s'y trouve ne décrit l'état courant,
et plusieurs de ces documents ont été présentés comme des références jusqu'à ce rangement.
