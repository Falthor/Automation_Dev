# Tâche 03 — Datacenter : baies, usure, amorçage et répartition

**Objectif : faire du Datacenter le bâtiment décrit par le GDD — deux baies de chaque type
extensibles par la recherche, une usure qui se voit avant de tuer, une séquence
d'amorçage, et un curseur de répartition entre axes.**

Référence : `ALIGNEMENT_PROJET.md` §3.5, §4 et §5, GDD §2.3 et §3.5.

Prérequis : les tâches 01, 01B et 02 sont livrées.

---

## 1. Dette à solder d'abord

La tâche 02 a renommé l'asset `extra_cpu_slot` en `storage_box` via `git mv`, ce qui
conserve le GUID mais change l'identifiant. Or `DataCenterRuntime` reconnaît cette
recherche par une **chaîne littérale** :

```csharp
const string ExtraCpuSlotResearchId = "extra_cpu_slot";
if (researchSystem.IsUnlocked(ExtraCpuSlotResearchId)) _cpuSlots.Add(null);
```

Aucune recherche du jeu ne porte plus cet identifiant. **Le chemin est mort** : plus rien
ne peut ajouter une baie CPU.

Le pire a été évité — si la correspondance s'était faite par référence d'asset, la
conservation du GUID aurait fait que rechercher la Boîte de stockage ajoutait une baie
CPU au Datacenter. Mais deux choses restent à corriger.

**`DataCenterRuntimeTests` passe au vert sur un chemin inatteignable.** Trois tests
construisent une recherche synthétique nommée `"extra_cpu_slot"` et vérifient l'ajout de
baie. Ils ne gardent plus rien. À réécrire contre les deux nouvelles recherches
d'extension.

**Et il faut vérifier explicitement que `storage_box` n'ajoute aucune baie.** C'est le
premier test à écrire, avant toute autre modification : si le GUID réutilisé a laissé
traîner une référence quelque part, il vaut mieux le découvrir maintenant.

---

## 2. Périmètre

### Modifiable

- `DataCenterRuntime`, `ComponentInstance`, `DataCenterDefinition`
- `Assets/Data/Items/cpu_mkI.asset`, `Memory_MK1.asset` — durée de vie
- `DataCenterPanelController`, son `.uxml` et son `.uss`
- `AdvancedFoundryDefinition.unlockResearch`
- assets de recherche à créer
- `ComputeSystem` — l'amorçage est un second consommateur continu
- `SaveData` / `Capture`-`Restore` du Data Center
- `CONTRACTS.md` §10 et §14
- tests concernés

### Non modifiable

- `ResearchSystem` — le modèle CU/absorption livré en tâche 02 ne bouge pas
- `WorldGenerator`, `DepositRuntime` — tâche ultérieure
- le système de bridage, le plafond de bâtiments, le rayon d'action — tâches 04 et 05
- le menu radial — il viendra quand la branche armement existera

---

## 3. Les baies

| Élément | Actuel | Cible |
|---|---|---|
| `InitialCpuSlots` | 4 | **2** |
| `InitialMemorySlots` | 4 | **2** |
| Extension | `extra_cpu_slot`, +1 baie CPU | **deux recherches, chacune +1 baie CPU et +1 baie mémoire** |
| Maximum | 5 CPU | **4 CPU et 4 mémoire** |

La production dépend de ce qui est installé, jamais du bâtiment lui-même.

| Composant | Production | Durée de vie nominale |
|---|---|---|
| CPU MkI | **15 CU/s** | **120 s** |
| Memory MK1 | **10 CU/s** | **120 s** |

| Configuration | Production maximale |
|---|---|
| 2 CPU + 2 mémoire — départ | 50 CU/s |
| 3 + 3 — après Extension I | 75 CU/s |
| 4 + 4 — après Extension II | 100 CU/s |

`StabilityInterval` et `ReplacementDuration` restent à 5 secondes.

---

## 4. Le modèle d'usure

Le modèle actuel est purement linéaire — un point toutes les 30 secondes — et l'usure
**n'a aucun effet** sur le rendement avant de déclencher brutalement le remplacement. Elle
est invisible jusqu'à ce qu'elle compte, ce qui est le pire des deux mondes.

Le système de stabilité et de fluctuation est **conservé intégralement**. Ce qui change,
c'est que l'usure le pilote au lieu de vivre à côté.

### 4.1 L'usure pilote la stabilité

```
stabilité = 95 − 65 × (1 − usure/100)
```

95 % à neuf, 30 % en fin de vie. La stabilité cesse d'être la constante 80.

### 4.2 La fluctuation s'élargit avec l'usure

```
plancher_fluctuation = 1,0 − 0,70 × (1 − usure/100)
```

Un composant neuf fluctue entre 70 % et 100 %, un composant en fin de vie entre 30 % et
100 %. Le rendement moyen glisse d'environ 99 % à 75 % — mais surtout **la production
devient visiblement instable avant de tomber**. Le joueur ne lit pas un chiffre d'usure,
il voit son graphe de CU trembler. C'est le signal de réapprovisionnement, et il est
diégétique.

### 4.3 La décroissance accélère

```
perte_par_seconde = perte_base × (1 + 2 × (1 − usure/100))
```

Un composant neuf s'use à vitesse nominale, un composant à 20 % s'use 2,6 fois plus vite.
La fin arrive plus tôt qu'on ne l'anticipe, sans être injuste puisque la jauge est visible.

### 4.4 La durée de vie est tirée par composant

À l'installation, chaque composant tire sa durée de vie nominale dans **± 25 %** autour de
120 secondes, soit 90 à 150 secondes.

C'est indispensable. Dans le modèle actuel, entièrement déterministe, des composants
installés ensemble meurent ensemble et la production part en à-coups périodiques. La
dispersion transforme ça en flux continu.

`perte_base` se déduit de la durée de vie tirée, de façon que l'intégrale de la courbe
accélérée amène l'usure de 100 au seuil de remplacement exactement en ce temps-là.

**Le tirage doit passer par un générateur seedé**, conformément à la règle de déterminisme
du projet : même seed et mêmes paramètres, même résultat.

---

## 5. Seuil de remplacement configurable

`ReplacementThreshold` est aujourd'hui une constante à 5 %. Il devient un **réglage du
joueur** : curseur de 5 % à 60 %, valeur par défaut **25 %**, réglable séparément pour les
baies CPU et les baies mémoire, modifiable à tout moment et gratuitement.

C'est un arbitrage à trois faces. Remplacer tôt garde une production régulière mais jette
la vie restante — à 40 %, on n'exploite que 63 % de la durée de vie, donc il faut produire
une fois et demie plus de composants. Remplacer tard tire le maximum de chaque composant
mais impose de longues plages instables, précisément là où la décroissance accélère. Et
dans les deux cas, chaque remplacement immobilise la baie cinq secondes : un seuil trop
haut multiplie les micro-coupures.

---

## 6. L'amorçage

À la pose, le Data Center entre en séquence d'amorçage : **1 500 CU consommés sur 90
secondes, sans aucune production**.

C'est ce qui oblige le joueur à garder une marge jusqu'au bout au lieu de dépenser à zéro
dès qu'il voit la fin. Sans elle, la courbe remonte au moment même où elle allait devenir
intéressante.

Si la réserve tombe à zéro pendant l'amorçage, celui-ci **se met en pause en conservant sa
progression**, exactement comme une recherche. Il ne se perd jamais.

**Conséquence de contrat** : l'amorçage est un second consommateur continu de CU après la
recherche. `CONTRACTS.md` §10 doit être étendu en conséquence — c'est une évolution de
contrat public au sens du §13, à traiter comme telle.

---

## 7. La répartition par axe

Le Data Center répartit sa production entre **recherche** et **bâtiments**. L'axe armement
n'existe pas encore et ne doit pas être anticipé.

```
concentration  = Σ (part_axe)²
rendement      = 0,20 + 0,80 × concentration
production_axe = production_installée × rendement × part_axe
```

| Répartition | Production |
|---|---|
| 100 / 0 | 100 % / 0 |
| 90 / 10 | 77 % / 8,6 % |
| 70 / 30 | 46 % / 20 % |
| 50 / 50 | 30 % / 30 % |

Répartition par défaut : **50 / 50**. La bascule est **gratuite et instantanée** — le
rendement fait déjà tout le travail, un coût de reconfiguration en plus rendrait le système
rigide.

Le plancher de 0,20 passera à 0,35 quand l'axe armement s'ouvrira, pour qu'un partage à
trois reste tenable. **Ne pas l'anticiper**, mais écrire la formule de façon que le
plancher soit un paramètre et non une constante enfouie.

Tant que la recherche puise dans la réserve unique, l'axe recherche alimente cette réserve.
La séparation en réserves distinctes par type de CU relève d'une tâche ultérieure.

---

## 8. Recherches à créer

| id | Nom | `cuCost` | Absorption | Prérequis | Effet |
|---|---|---|---|---|---|
| `datacenter_bay_1` | Extension de baies I | 2 000 | 30 CU/s | `datacenter` | +1 baie CPU, +1 baie mémoire |
| `datacenter_bay_2` | Extension de baies II | 2 000 | 30 CU/s | `datacenter_bay_1` | +1 baie CPU, +1 baie mémoire |
| `advanced_foundry` | Fonderie avancée | 2 000 | 40 CU/s | `datacenter` | bâtiment Fonderie avancée + recette acier |

`AdvancedFoundryDefinition.unlockResearch` pointe désormais vers `advanced_foundry`. C'est
la valeur que la tâche 01 n'avait pas pu appliquer faute d'asset existant.

Ces trois recherches sont ajoutées au `ResearchDatabase`.

---

## 9. Sauvegarde

`DataCenterRuntime.Capture`/`Restore` change de forme : nombre de baies, contenu et usure
de chaque baie, durée de vie tirée, seuil de remplacement par type, répartition entre axes,
progression de l'amorçage.

**Règle impérative : `Restore` doit tolérer une clé absente** et retomber sur une valeur
par défaut raisonnable, plutôt que de supposer la forme d'une sauvegarde antérieure. Le
blob par bâtiment est libre, donc l'ajout de champs passe sans casse — à cette condition
seulement.

Vérifier au passage que `SaveData.ResearchRp` a bien disparu à la tâche 02. S'il subsiste,
c'est un résidu du modèle RP.

**À trancher dans cette tâche** : que fait le jeu d'une sauvegarde dont la `Version` ne
correspond plus ? Refus de chargement avec message clair, ou tolérance et valeurs par
défaut ? Vu le rythme auquel l'état runtime change en ce moment, la question ne peut plus
attendre. Recommandation : refus explicite, parce que la tolérance masque les
incompatibilités réelles jusqu'à ce qu'elles se manifestent sous une forme
incompréhensible.

---

## 10. Interface du panneau Data Center

Le panneau doit montrer, par baie : le composant installé, son **usure en pourcentage**, sa
**stabilité courante**, et son état de remplacement en cours le cas échéant.

Plus, pour le bâtiment : la production courante par axe, le curseur de répartition, les deux
curseurs de seuil de remplacement, et la progression de l'amorçage quand il est en cours.

**Ici, les chiffres exacts sont légitimes.** Le principe « la précision se gagne » vaut pour
le monde inconnu, pas pour ses propres machines.

---

## 11. Tests

**À écrire en premier**, avant toute autre modification : `storage_box` complétée n'ajoute
aucune baie.

Puis :

- 2 baies CPU et 2 mémoire à la construction ;
- `datacenter_bay_1` ajoute une baie de chaque, `datacenter_bay_2` aussi, plafond à 4 + 4 ;
- la stabilité suit la formule d'usure aux bornes — 95 % à neuf, 30 % à l'usure minimale ;
- la fourchette de fluctuation s'élargit comme spécifié ;
- la décroissance accélère : un composant à 20 % s'use bien 2,6 fois plus vite qu'un neuf ;
- la durée de vie tirée est **déterministe à seed égale** ;
- le seuil de remplacement déclenche à la valeur réglée, pour chaque type indépendamment ;
- l'amorçage consomme 1 500 CU sur 90 s sans rien produire ;
- l'amorçage se met en pause à zéro CU et reprend exactement où il en était ;
- la formule de rendement donne bien 100/0 et 30+30 aux deux bornes ;
- aller-retour `Capture`/`Restore` complet ;
- `Restore` sur un blob amputé d'une clé ne lève pas.

`DataCenterRuntimeTests` est à réécrire : ses trois tests `extra_cpu_slot` ne gardent plus
rien.

---

## 12. Critères d'acceptation

1. Le projet compile, tous les tests passent.
2. Un Data Center neuf a 2 baies CPU et 2 baies mémoire, et produit 50 CU/s à pleine
   concentration une fois alimenté.
3. Rechercher la Boîte de stockage n'ajoute aucune baie.
4. Les deux recherches d'extension portent le bâtiment à 4 + 4 et 100 CU/s.
5. L'amorçage bloque toute production pendant 90 secondes et consomme 1 500 CU.
6. Un composant en fin de vie produit visiblement de façon irrégulière avant d'être
   remplacé.
7. Deux composants installés simultanément ne meurent pas au même instant.
8. La Fonderie avancée n'est constructible qu'après sa recherche.
9. Une sauvegarde antérieure à cette tâche se charge sans exception, ou est refusée avec un
   message explicite.
10. `CONTRACTS.md` §10 mentionne l'amorçage comme second consommateur continu.

---

## 13. Rapport attendu

Format de `WORKFLOW.md` §11, avec en plus :

- le résultat du test `storage_box` n'ajoute aucune baie, en premier ;
- la liste des tests supprimés ou réécrits, avec la raison ;
- la décision prise sur la `Version` de sauvegarde et sa justification ;
- la production réelle mesurée en jeu pour 2+2, 3+3 et 4+4 baies, comparée aux 50, 75 et
  100 CU/s attendus ;
- le temps réel entre l'installation d'un composant et son remplacement, sur plusieurs
  composants, pour vérifier que la dispersion fonctionne.
