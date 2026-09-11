# Datacenter — usure, stabilité, rendement

Les trois quantités du matériel du Datacenter, leurs formules et les chiffres qu'elles donnent avec
les valeurs livrées. **Rien de tout ceci n'est montré au joueur** : le panneau lui donne un nombre,
une bande et une barre (`GLOBAL_UI.md` §5a), et sa fenêtre d'explication n'énonce aucune formule.
Ce carnet est pour qui modifie l'équilibrage.

Source de vérité : `ComponentInstance.cs` et `DataCenterRuntime.cs`. Si un chiffre ci-dessous
diverge du code, le code a raison — et ce carnet est à corriger.

---

## Trois quantités, souvent confondues

| | Ce que c'est | Formule |
|---|---|---|
| **Usure** | la seule qui évolue d'elle-même, 100 (neuf) → 0 (mort) | décroît en accélérant, voir plus bas |
| **Stabilité** | une **probabilité**, jamais un multiplicateur | `95 % − 65 %·(1 − usure/100)` |
| **Rendement d'une pièce** | le multiplicateur réellement appliqué à son CU de base | tiré toutes les **2 s** |

Le tirage, **toutes les deux secondes** (`DataCenterRuntime.StabilityInterval`) : **Stabilité %**
de chance d'obtenir `1,00` exactement, sinon un tirage uniforme entre le **plancher de fluctuation**
et `1,00`, ce plancher valant `0,70 − 0,40·(1 − usure/100)`.

L'intervalle est descendu de 5 s à 2 s pour que le trait du panneau bouge assez souvent pour se lire
comme une instabilité et non comme un gel. **Le tirage lui-même est inchangé** : la moyenne ne bouge
pas, seule la variance sur une minute se resserre. À ne pas confondre avec `ReplacementDuration`,
qui vaut cinq secondes et n'a pas changé.

| Usure | Stabilité | Plancher | Rendement moyen |
|---|---|---|---|
| 100 (neuf) | 95 % | 0,70 | **0,993** |
| 25 (seuil par défaut) | 46 % | 0,40 | **0,839** |
| 5 (plancher de calibrage) | 33 % | 0,32 | **0,773** |

C'est cette largeur de fourchette que le panneau dessine, et non la stabilité en chiffres : une
pièce usée saute visiblement d'un tirage à l'autre, ce qui se lit avant tout pourcentage.

## La courbe d'usure

```
dUsure/dt = −baseLoss · (1 + 2·(1 − usure/100))
```

Le multiplicateur vaut **1** à l'installation, **2,5** à 25 % d'usure, **2,9** à 5 %. `baseLoss` est
résolu une fois à la construction pour que l'usure aille de 100 à **`LifetimeFloorPercent` = 5** en
exactement la durée de vie tirée par cette pièce :

```
baseLoss = 100 · ln(2,9) / (2·L)          →  0,887 pt/s pour L = 60 s
t(usure w) = L · ln(3 − 2w) / ln(2,9)
```

**Le plancher de calibrage est une constante, pas le seuil de remplacement**, et c'est le point le
plus facile à casser en retouchant ceci. Calibrer la courbe sur le seuil rendrait la durée de vie
*indépendante* du seuil : déplacer le curseur ne changerait plus rien, puisque la courbe se
redresserait pour arriver au seuil au même instant. Le seuil ne décide que d'où l'on s'arrête sur
une courbe fixe.

Durée de vie **nominale 60 s, dispersée ±25 %** (`ItemDefinition.NominalLifetimeSeconds`, tiré par
un `System.Random` ensemencé que possède `DataCenterRuntime`) → 45 à 75 s. La dispersion existe pour
que deux pièces posées ensemble ne meurent pas ensemble.

Temps jusqu'au remplacement, pour L = 60 s :

| Seuil | Temps | En fraction de L |
|---|---|---|
| 60 % | 33 s | 0,552 |
| 25 % (défaut) | 52 s | 0,861 |
| 5 % | 60 s | 1,000 |

## Le remplacement

Curseur **5 à 60 %**, défaut **25 %**, relu en continu — le déplacer agit immédiatement, la valeur
n'est pas figée à l'installation. Au franchissement : la pièce passe en remplacement, son CU tombe à
**0 pendant 5 s** (`ReplacementDuration`), puis l'emplacement prend une pièce de rechange dans
l'entrée s'il y en a une, sinon il se vide et l'installation automatique le reprendra plus tard.
L'usure continue de courir pendant ces cinq secondes, et si elle atteint 0 l'emplacement se vide sur
le champ.

## Ce qui multiplie tout le reste

**L'arbitrage des axes.** `concentration = r² + (1−r)²`, `rendement = plancher + (1−plancher)·concentration`
avec `DataCenterDefinition.AxisYieldFloor` = 0,2. Réparti 50/50 → **0,6** ; tout sur un axe →
**1,0**. Les mêmes pièces produisent donc 40 % de moins à parts égales.

**Le courant, en tout ou rien.** `ComputeEffectivePerformance` rend 1 ou 0 — il n'y a pas de
dégradation progressive. Sans courant : aucune production **et aucune usure**. Idem pendant
l'amorçage (1500 CU absorbés sur 90 s) : ni production ni usure.

## Les valeurs livrées

| | CU/s | kW | Vie nominale |
|---|---|---|---|
| CPU MkI | 40 | 5,3 | 60 s |
| Memory MK1 | 25 | 2,5 | 60 s |

La consommation a suivi la production dans la même proportion quand celle-ci est passée de 15 à
40 et de 10 à 25 CU/s : **7,5 CU par kW pour le CPU, 10 pour la mémoire**, comme avant. Un
Datacenter plein (4 + 4) tire donc 31,2 kW, soit plus que les 30 kW du Noyau à lui seul — il faut
une centrale.

CPU et mémoire n'ont **aucune différence de mécanique** : mêmes formules, mêmes seuils, même courbe.
Seuls les chiffres et la baie changent. Emplacements : 1 + 1 au départ, +1+1 par recherche de baie
(`datacenter_bay_1`, `datacenter_bay_2`), plafond 4 + 4.

## Le mot « rendement » désigne deux choses

À surveiller en lisant le code : `DataCenterRuntime.GetYield()` est le facteur de **concentration
des axes** et ne dépend pas de l'usure. Le facteur que le panneau affiche sous la production
(`nominal · rendement`) est `production réelle / nominal`, qui replie l'usure, le tirage de
stabilité, un remplacement en cours **et** la concentration. Les deux sont légitimes, ils ne
répondent simplement pas à la même question — et c'est le second que le joueur regarde, parce que
c'est le seul qui bouge quand ses baies fatiguent.
