# Tâche 01 — Rééquilibrage par les données

**Objectif : rendre l'introduction mesurable sans construire aucun système nouveau.**

Toutes les valeurs de ce ticket viennent de `Assets/docs/Intro/ALIGNEMENT_PROJET.md`, qui
fait foi. Le GDD (`Assets/docs/Intro/gdd-intro-recherche-expeditions.md`) donne le
raisonnement derrière chacune.

Cette tâche ne touche **ni au système de recherche, ni au Data Center, ni aux
expéditions**. Elle prépare une mesure, pas une fonctionnalité.

> **Préalable obligatoire.** Les versions de `gdd-intro-recherche-expeditions.md` et
> `ALIGNEMENT_PROJET.md` présentes sur la branche doivent être à jour avant de lancer
> cette tâche. Une version périmée conduirait à implémenter des règles contradictoires —
> notamment sur la combustion des centrales gaz, sur le plafond de bâtiments et sur les
> gisements illimités.

---

## 1. Périmètre

### Fichiers qui peuvent changer

- `Assets/Data/Items/cpu_mkI.asset`, `Memory_MK1.asset`
- `Assets/Data/Recipes/*.asset` — sauf `Data_Card_Recipe.asset`
- `Assets/Data/Buildings/ExtractorDefinition.asset`, `PowerplantGazDefinition.asset`,
  `DataCenterDefinition.asset`, `AssemblerDefinition.asset`,
  `AdvancedFoundryDefinition.asset`
- `Assets/Data/World/CoreDefinition.asset`
- `Assets/Scripts/Gameplay/Compute/ComputeSystem.cs` — deux constantes
- `Assets/Scripts/Tests/EditMode/Gameplay/Compute/ComputeSystemTests.cs`
- Tout test cassé par un changement de `computeCost`

### Fichiers qui ne doivent pas changer

- `ResearchSystem.cs`, `ResearchDefinition.cs`, `ResearchPanelController.cs`
- `LaboratoryRuntime.cs`, `LaboratoryDefinition.asset` et son panneau
- `DataCenterRuntime.cs`, `ComponentInstance.cs`
- `WorldGenerator.cs`, `DepositRuntime.cs`, `OreDepositDefinition.cs`
- `ConstructionService.cs`
- Toute la couche `Game.Presentation` et `Game.UI` hors correction de compilation

**Ne supprime pas encore le Laboratoire ni la Data Card.** Le système de recherche actuel
en dépend entièrement ; les retirer avant sa refonte casse la compilation pour rien. Ils
disparaîtront à la tâche suivante.

---

## 2. Valeurs à appliquer

Reporter les tableaux de `ALIGNEMENT_PROJET.md` :

- **§1** — `cuOutput` du CPU et de la mémoire
- **§2** — entrées, sorties, temps et `computeCost` de chaque recette
- **§3** — extracteur, centrale gaz, Core, coûts de construction

Trois précisions sur le §3 :

- `CoreDefinition.cuOutput` passe à **0**. Le Core cesse de produire du CU. Laisse
  `cuOutputIntervalSeconds` tel quel, il devient sans effet.
- `CoreDefinition.actionRadiusCells` passe de 50 à **22**.
- `ExtractorDefinition.extractionIntervalSeconds` passe à **4** : c'est la valeur bridée,
  qui est celle du début de partie. Le débridage viendra avec la recherche
  *Extraction renforcée*, plus tard.

### `ComputeSystem`

```csharp
public const float ReserveCap = 60000f;
```

`Reserve` démarre déjà à `ReserveCap`, donc la réserve initiale suit automatiquement.
Vérifie-le plutôt que de l'assumer.

---

## 3. Neutralisation temporaire des verrous de recherche

**Ceci est temporaire et devra être annulé à la tâche suivante.** On veut mesurer
l'économie de production seule, sans que la couche recherche brouille le chiffre.

Passe `unlockResearch` à `null` sur :

- `Screw_Recipe.asset`
- `Printed_Circuit_Board_Recipe.asset`
- `Memory_MK1_Recipe.asset`
- `AssemblerDefinition.asset`
- `DataCenterDefinition.asset`

Note ces cinq assets dans ton rapport final pour qu'on les restaure sans en oublier un.

**Exception** : `Screw_Recipe` est un cas différent. Les vis sont disponibles dès le début
de partie dans la conception définitive, donc son `unlockResearch` reste à `null` **de
façon permanente**. Ne pas le restaurer.

---

## 4. Ce que la tâche ne fait pas

Pour mémoire, et pour éviter que le périmètre ne dérive :

- pas de plafond de bâtiments — il n'est pas contraignant à 40 pour un parc de 32
- pas de modification du générateur de monde — deux grappes de fer offrent déjà huit
  emplacements pour les quatre extracteurs nécessaires
- pas de suppression du modèle d'épuisement — mille unités par gisement suffisent
  largement à couvrir l'introduction, le sujet n'interfère pas avec la mesure
- pas de baies, pas d'amorçage, pas de curseur de répartition
- pas de refonte de la recherche

Chacun de ces points est nécessaire à terme et documenté dans `ALIGNEMENT_PROJET.md`.
Aucun n'est nécessaire pour mesurer.

---

## 5. Critères d'acceptation

1. Le projet compile et tous les tests passent.
2. Une partie neuve démarre avec **60 000 CU** et une production de CU nulle.
3. Le Data Center est plaçable dès que ses composants sont produits, sans dépendre de
   l'acier ni d'aucune recherche.
4. Un extracteur produit un minerai toutes les 4 secondes et coûte 2 CU par extraction.
5. Une centrale gaz fournit 25 kW et coûte 8 CU par unité de charbon brûlée.
6. Aucun test ne référence encore une ancienne valeur de `computeCost`.
7. **Les six grappes de gisements sont effectivement présentes** en début de partie.

Ce dernier point demande une vérification explicite. Le rayon d'action passe de 50 à 22,
donc `maxDistance` tombe de 46 à 18 et l'anneau de placement se réduit de 6 600 à 900
cellules. Le générateur y place 6 grappes de 16 cellules, soit 11 % d'occupation : les 500
tentatives devraient suffire, mais **un échec de placement est aujourd'hui silencieux**.
Une partie amputée d'une grappe de charbon serait injouable sans le moindre message.
Compter les grappes au démarrage plutôt que de le supposer.

---

## 6. Protocole de mesure

Une fois la tâche livrée, une partie est jouée jusqu'à la construction du Data Center.
Relever :

| Mesure | Cible |
|---|---|
| Temps écoulé jusqu'au Data Center posé | **~30 min** |
| CU restant à ce moment | **~15 000** |
| Nombre de bâtiments posés, hors convoyeurs | **~32** |
| Extracteurs de fer / cuivre / charbon | 4 / 4 / 2 |
| Une ligne est-elle restée à l'arrêt faute d'entrées ? | à noter |
| Le rayon de 22 cellules a-t-il semblé à l'étroit ? | à noter |

Lecture des écarts :

- CU épuisé avant la fin → les coûts sont trop lourds
- plus de 40 000 CU restants au bout de quinze minutes → ils sont trop légers
- parc réel très éloigné de 32 → le modèle de goulets est faux, pas les prix
- une ligne systématiquement affamée → un ratio de recette est mal réglé

Ces quatre lectures pointent vers des corrections différentes. Note laquelle s'applique
avant de toucher à quoi que ce soit.

---

## 7. Rapport attendu

Format de `WORKFLOW.md` §11, avec en plus :

- la liste des cinq assets dont `unlockResearch` a été neutralisé
- tout test modifié et pourquoi
- toute valeur de `ALIGNEMENT_PROJET.md` qui s'est révélée impossible à appliquer telle
  quelle, avec la raison
