# Les effets de recherche

Pourquoi une recherche porte elle-même ce qu'elle fait, et pourquoi plus aucun système ne compare un
identifiant de recherche. L'état du code est dans `CONTRACTS.md` §11 ; ici, les raisons.

## Le défaut

Avant ce chantier, **aucune recherche ne portait ses effets**. Ils vivaient à deux endroits, aucun
sur la recherche :

- les déblocages étaient déclarés **à l'envers** : un bâtiment ou une recette désignait la recherche
  qui l'ouvrait (`unlockResearch`) ;
- les effets chiffrés étaient **codés en dur par identifiant** : une table dans `CoreRuntime` pour le
  rayon, une dans `ConstructionService` pour le plafond, deux constantes dans `DataCenterRuntime` pour
  les baies, deux autres pour les noyaux que le Datacenter alimente.

Le second point est un défaut réel, pas une question de style : **renommer une recherche détachait son
effet sans rien signaler**. L'identifiant était comparé à une chaîne dans le code ; la comparaison
devenait fausse, et la recherche continuait d'exister, de coûter et de se terminer — sans rien faire.

Le premier rendait impossible un éditeur d'arbre utile : une recherche créée dans l'éditeur aurait été
une coquille tant que personne n'écrivait son cas en dur, et le rayon, le plafond et les baies —
précisément ce qu'on veut régler — seraient restés hors de portée.

## Ce qui a été décidé

**La recherche porte une liste d'effets, et rien d'autre ne les porte.** Cinq types, liste fermée :
débloquer un bâtiment, débloquer une recette, rayon d'action, plafond de bâtiments, paires de baies.
Chaque type est lu par exactement un système, nommé dans le code sur la valeur de l'énumération.

**Un enregistrement étiqueté plutôt qu'une hiérarchie de classes.** `ResearchEffect` est une structure
avec un type et trois champs dont un seul est lu selon le type. Une hiérarchie (`SerializeReference`)
aurait été plus « propre » à lire en code, mais Unity n'offre pas de sélecteur de type pour ces champs
dans l'inspecteur par défaut : il aurait fallu écrire un tiroir personnalisé avant même d'avoir un
éditeur. La structure plate se sérialise et s'affiche telle quelle. Un tiroir est venu ensuite, pour
le confort et non par nécessité (`ResearchEffectDrawer`) : il n'affiche que le champ que le type lit,
vide les deux autres au changement de type, et teinte une référence manquante. Sans lui, chaque
effet montrait trois champs dont deux ignorés.

**Rayon et plafond sont des cibles, pas des incréments.** La plus haute cible parmi les recherches
terminées l'emporte. L'ordre de complétion n'a donc jamais d'importance — une directive accordée tard,
une sauvegarde rechargée, une recherche plus basse terminée après une plus haute ne réduisent rien.
Les baies, elles, s'additionnent : une paire par effet, plafonnée par le Datacenter.

**Un index inverse, construit une fois.** Un bâtiment ne dit plus quelle recherche l'ouvre ; c'est la
recherche qui le dit. `ResearchCatalog` indexe donc chaque recherche par ce qu'elle débloque, et un
verrou lui demande « quelles recherches ouvrent ce bâtiment ? ». Un bâtiment qu'aucune recherche ne
nomme n'est pas verrouillé. Plusieurs recherches peuvent ouvrir la même chose : il suffit d'une.

**Les directives sont des recherches comme les autres.** Leurs déblocages (`core_directive_1` à `4`)
sont des `ResearchDefinition` hors de l'arbre ; elles portent leur liste d'effets, et le catalogue les
inclut. Rien ne distingue « débloqué par une directive » de « débloqué par une recherche ».

**Les noyaux alimentés par le Datacenter sont des références, pas des noms.** `DataCenterDefinition`
porte `poweredCores`. C'est le même défaut que les effets, dans l'autre sens : un bâtiment qui accorde
une recherche par son nom cesserait de l'accorder au premier renommage.

## Le rayon maximal se dérive

La portée maximale du Noyau — le disque que la génération du monde garde vide de minerai dérivé, et
sur lequel la couverture au sol dimensionne sa texture — était une constante, `ExtendedActionRadiusCells`
= 80, écrite à côté de la table qui accordait ce même 80. Le même nombre à deux endroits : le défaut
qui avait déjà mordu deux fois sur le plafond de CU.

Il se dérive maintenant du plus grand effet de rayon de la base (`ResearchCatalog.HighestActionRadius`),
calculé une fois par `GameRuntime` et lu par les deux consommateurs. Ajouter une recherche qui porte le
Noyau plus loin déplace la frontière du minerai dérivé sans qu'aucune autre valeur soit touchée — un
test le vérifie en ajoutant un tel effet à la base livrée. La constante voisine, le premier palier
qu'atteignent les gisements d'invitation, a suivi le même chemin : le test qui la vérifiait lit
maintenant le plus petit effet de rayon des assets au lieu de le recopier.

## Les deux validations

Deux défauts qu'aucune partie ne rattrape, et qu'un éditeur à clics rend faciles à créer :

- **un cycle de prérequis** — une recherche qui dépend d'elle-même par une chaîne ne peut jamais
  démarrer, ni rien derrière elle. C'est un blocage définitif de la partie, et il suffit de relier deux
  nœuds dans le mauvais sens pour le fabriquer ;
- **une recherche inatteignable** — celle qu'aucune chaîne ne relie aux trois noyaux.

Elles vivent dans `Game.Data` (`ResearchTreeValidation`) plutôt que dans l'éditeur à venir : elles ont
leur place sans lui, et un test les applique à l'arbre livré, lu dans les assets. Le jeu les appelle
aussi : dans l'éditeur et en build de développement, `GameRuntime` les passe sur la base au démarrage
et journalise une erreur. Un cycle créé dans l'éditeur se voit ainsi au lancement, pas au moment où
une partie cesse d'avancer.

**« Inatteignable » est lu au sens strict : « ne pourra jamais être débloquée ».** Une recherche dont
un prérequis est sur l'arbre et un autre ne l'est pas a bien un chemin jusqu'aux noyaux — et ne
démarrera pourtant jamais, puisqu'il les lui faut tous. La lecture « un chemin existe » l'aurait
laissée passer ; c'est précisément le cas qu'on veut voir. Une recherche sans aucun prérequis n'est pas
une racine pour autant : seuls les noyaux le sont. Les déblocages de directives ne sont pas des points
de départ ; une recherche de l'arbre qui dépendrait d'une directive serait signalée, et c'est une
décision à prendre le jour où l'on en voudra une.

## Ce qui n'a pas changé

**Les sauvegardes.** Elles retiennent les identifiants débloqués sous forme de texte, et continuent de
le faire : un identifiant reste la façon dont une sauvegarde nomme un déblocage, et la façon dont
`ResearchCompleted` en annonce un. Ce qu'il a cessé d'être, c'est la clé d'un effet. Le rayon et le
plafond restent sauvegardés comme valeurs, pas redérivés — une partie existante se recharge à
l'identique.

## La migration

Faite par un script, pas à la main : il a lu les références `unlockResearch` des assets et les trois
tables dans le code **avant** leur suppression, puis écrit les effets sur les recherches.

| Recherche | Effets |
|---|---|
| Directive 1 | recettes Engrenage, Bobine de cuivre |
| Directive 2 | bâtiments Usine, Répartiteur ; recettes Mécanisme électrique, Électroaimant |
| Directive 3 | recettes Moteur électrique, Module de contrôle |
| Directive 4 | bâtiment Datacenter ; recettes CPU, Mémoire |
| Fonderie avancée | bâtiment Fonderie avancée |
| Extension de baies I, II | une paire chacune |
| Bande passante 1, 2, 3 | rayon 42, 60, 80 |
| Allocation mémoire 1, 2, 3 | plafond 75, 100, 200 |

Rien n'est resté non migré. 29 lignes `unlockResearch` ont été retirées : les 12 renseignées et 17
vides. Le compte annoncé au départ — quinze — était une estimation ; le réel est de 12 références et
8 effets chiffrés.

## La vérification

« Aucun identifiant ne subsiste » ne se prouve pas par un seul grep : sur ce projet, une recherche
naïve a déjà manqué des occurrences qui passaient par une variable ou n'existaient sous aucune
constante nommée. La vérification fait donc trois passes et les imprime toutes pour relecture :
chaque identifiant livré en sous-chaîne, chaque racine d'identifiant et les mots dont ils sont faits,
puis les constructions qui pourraient fabriquer ou comparer un identifiant sans l'écrire —
concaténation sur un préfixe, interpolation, littéral passé à `IsUnlocked`/`Grant`/`Definition`,
`.Id ==` contre un littéral.

Les tests suivent la même règle : leurs recherches de test sont nommées d'après ce qu'elles font
(`radius_42`, `bays_a`), jamais d'après une recherche livrée, pour qu'aucun test ne passe grâce à un
nom.
