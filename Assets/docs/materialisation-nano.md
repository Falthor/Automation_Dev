# Matérialisation nano — carnet

Décisions, écarts et limites. La spécification est `Intro/directive-materialisation-nano.md` ;
ce document ne la répète pas, il consigne ce qui ne s'y trouve pas.

**État : étape 1 sur 3.** Le dissolve seul. La couverture au sol et les particules ne sont pas
faites, et le composant n'est branché sur aucun chantier réel — voir *Limites connues*.

---

## Où est quoi

| Fichier | Rôle |
|---|---|
| `Art/Shaders/BuildDissolve.shader` | Découpe le sprite sous un front de révélation bruité, avec liseré lumineux. |
| `Scripts/Presentation/BuildDissolveView.cs` | Pilote l'effet : lissage de l'avancement, flash de livraison, écriture du `MaterialPropertyBlock`, retrait à l'achèvement. |
| `Scripts/Presentation/NanoConstructionSettings.cs` | La classe de réglages. |
| `Data/Presentation/NanoConstructionSettings.asset` | L'instance unique. C'est le seul fichier à ouvrir pour changer l'aspect. |
| `Scripts/Tests/EditMode/Presentation/BuildDissolveViewTests.cs` | 13 tests : lissage, flash, achèvement, isolation entre bâtiments, propagation des réglages. |
| `Scenes/DissolveTest.unity` | Scène de validation à l'œil : une caméra, une Fonderie à sa taille de jeu (3 cases), le composant câblé. Hors Build Settings, sans aucune dépendance au reste du jeu. Ouvrir, lancer le Play, monter `Target Progress` dans l'inspecteur du composant. |

## Comment régler

Tout se règle dans `Data/Presentation/NanoConstructionSettings.asset`. Aucune valeur n'est
réglable bâtiment par bâtiment, c'est délibéré. Les changements se voient immédiatement pendant
le Play : le composant relit l'asset à chaque tick.

| Champ | Effet visuel |
|---|---|
| `noiseScale` | Taille du grain. Plus haut = taches plus fines et plus nombreuses. |
| `noiseWeight` | À 0 le front est un balayage net ; plus haut, il se déchiquette. |
| `rimWidth` | Épaisseur de la bande lumineuse qui suit le front. |
| `rimColor` | Couleur de cette bande. |
| `revealMode` | 0 = le bâtiment monte du sol, 1 = il se forme depuis son centre. |
| `catchUpRate` | Vitesse d'assemblage. Plus bas = matérialisation plus lente et plus lisible. |
| `deliveryFlashDuration` | Durée du flash à chaque arrivée de matière. |
| `deliveryFlashIntensity` | Puissance de ce flash. |

`groundRimColor`, `groundIntensity`, `groundRimIntensity` et `coverageFadeSeconds` sont présents
dans l'asset mais **ne sont lus par personne** tant que l'étape 2 n'est pas faite. Ils y sont pour
que l'asset ne change pas de forme entre les étapes.

`dissolveShader` pointe sur `Custom/BuildDissolve`. Ne le vide pas : sans shader, le composant
laisse le sprite intact et ne fait rien.

## D'où viennent les valeurs

Elles ont été trouvées sur un prototype navigateur, hors dépôt — pas mesurées dans Unity. Aucune
n'a été redérivée ici.

**`noiseScale = 6.3`** est le seul chiffre converti. Le prototype travaillait en pixels : grain de
0,06 par pixel sur une largeur de 320 px, soit ≈ 19 périodes sur la largeur de la centrale gaz.
La centrale fait 3×3 cases, donc 19 ÷ 3 ≈ **6,3 périodes par case**. Comme le bruit est
échantillonné en coordonnées monde, cette valeur vaut ensuite pour tous les bâtiments sans
recalcul : un bâtiment plus grand reçoit plus de périodes, avec un grain de même taille physique.
**Ne jamais coder 0,06 en dur**, ce serait des dizaines de fois trop grossier.

**`catchUpRate = 0.25`** signifie qu'une matérialisation complète prend au minimum 4 secondes,
même si tous les matériaux arrivent d'un coup. C'est ce plancher qui est réglé, pas une durée
« moyenne » : la durée réelle est proportionnelle à la matière livrée.

**`deliveryFlashDuration = 0.40`** est calé pour être vu sans être clignotant — assez long pour
que l'œil accroche l'arrivée d'un lot, assez court pour que deux livraisons rapprochées restent
deux évènements distincts.

## Les écarts

| Écart | Pourquoi |
|---|---|
| Shader dans `Art/Shaders/`, asset dans `Data/Presentation/` — pas `Shaders/` ni `Data/Settings/` | §4 demande de suivre l'organisation en place. Les huit shaders du projet sont dans `Art/Shaders/`, et `Data/Presentation/` contient déjà `BuildingShadowSettings`. Aucun dossier `Settings` n'existe. |
| Propriété shader `_BuildBounds` en plus des huit listées en §5 | Le dégradé de base doit être normalisé sur l'étendue du bâtiment. Les UV ne conviennent pas (une frame d'atlas occupe un sous-rectangle arbitraire, `uv.y` n'est pas 0–1), l'espace objet non plus (pivot et demi-tailles inconnus du shader). Le composant passe donc l'AABB monde du renderer. Même logique que le bruit : monde, indépendant du pivot et de la feuille. |
| Le shader est référencé comme asset depuis le ScriptableObject, pas résolu par `Shader.Find` | Un shader atteint uniquement par son nom est supprimé du build s'il n'est pas listé dans Always Included Shaders — voir `BUILD.md`. Une référence d'asset ne peut pas être supprimée, donc cette dépendance ne peut pas casser un build en silence. Les cinq autres vues du projet utilisent encore `Shader.Find`. |
| `BuildDissolveView.Tick(deltaTime)` public, appelé depuis `LateUpdate` | §10 demande des tests EditMode ; `Update` n'y tourne pas. Le composant reçoit donc son delta au lieu de lire `Time` lui-même. |
| `Game.Presentation` ajouté aux références de `Game.Tests.EditMode.asmdef` | Sans quoi les tests EditMode de §10 ne voient pas le composant. Changement de configuration de test uniquement ; aucune assembly de production n'a bougé. |
| Le shader décale l'intervalle utile de ±0,001 avant le `clip()` | `field` vaut [0, 1] : un `_Progress - field` brut laisse un éclat visible à 0 et en découpe un à 1. Le décalage garantit que 0 ne montre strictement rien et que 1 montre strictement tout, ce que §10 demande. |
| Aucun prefab | Le projet n'a aucun prefab de bâtiment : toutes les vues sont construites en code (`BuildingSpawner`, `WorldContentSpawner`). Le composant s'ajoute donc à n'importe quel GameObject portant un `SpriteRenderer`. |
| **`ConstructionSiteRuntime` gagne `SegmentProgress(index)` — la spec §1 interdit toute modification dans `Game.Gameplay`** | Contrainte **levée sciemment sur ce seul point**, après arbitrage. `TotalCost` et `Delivered` sont agrégés par chantier : un glissé de convoyeurs se serait dissous en bloc au lieu de s'assembler segment par segment. Calculer l'avancement d'un segment demande de savoir quelle livraison alimente quel segment — une règle que `ConstructionSiteRuntime` possède en privé (`ConsumedByMaterializedSegments`). La refaire dans Presentation aurait mis cette règle à deux endroits, dont l'un se désynchronise en silence : ça viole l'esprit de la contrainte (Presentation ne doit pas s'approprier la logique du jeu) plus gravement qu'un accesseur n'en viole la lettre. L'accesseur est en **lecture seule** et n'expose **que le résultat**, jamais la somme préfixe — sinon l'arithmétique redescendait dans Presentation et le problème restait entier. Consigné dans `architecture/CONTRACTS.md` §15. |

## Limites connues

**Le composant n'est branché sur aucun chantier réel, et le brancher demande une décision.**
Aujourd'hui, un chantier en attente n'a **pas** de vue de bâtiment : `ConstructionSiteVisualSync`
dessine une silhouette bleue, et `BuildingSpawner` ne crée la vraie vue qu'à la matérialisation du
segment. Il n'y a donc rien à dissoudre pendant la construction. Faire vivre le dissolve dans le
jeu suppose de trancher ce que devient la silhouette bleue : remplacée par le sprite dissous,
ou conservée derrière lui. Ce n'est pas un détail d'implémentation, c'est un choix de langage
visuel — non tranché ici.

Le reste :

- **L'avancement pondère chaque item par son nombre d'unités.** Une vis vaut un circuit imprimé.
  L'Assembleur coûtant 2 circuits + 10 vis + 5 plaques, livrer les 10 vis remplit 59 % de la
  barre et livrer les 2 circuits 12 %. Le biais est réel et connu ; l'alternative retenue le
  jour où ça gênera est la moyenne des taux par ligne d'ingrédient (chaque ligne vaut `1/N`),
  calculable depuis les données déjà publiques. Une pondération par valeur réelle de l'item
  demanderait une notion de valeur que le jeu n'a pas. **Non tranché.**
- La barre ne compte que le matériel **livré**, pas celui en vol (`_committed`). Elle avance donc
  par à-coups à l'arrivée des robots plutôt que pendant leur trajet. Axe indépendant du
  précédent, également non tranché.
- `revealMode = 1` (radial) est implémenté mais n'a jamais été regardé à l'œil.
- Le gel de la feuille animée pendant le chantier est implémenté ; la 12ᵉ frame de la centrale gaz,
  doublon exact de la première signalé en §6, n'a pas été corrigée — c'est une donnée
  (`Data/Buildings/PowerplantGazDefinition.asset`), hors périmètre de l'étape 1.
- Rien n'est testé sur le rendu lui-même, par consigne.

## Pour l'étape 2

**`_BuildBounds` est l'AABB visuelle du sprite, pas l'emprise logique du bâtiment.** Le sprite
déborde volontairement de son emprise (`PROJECT_ARCHITECTURE.md` §12 : « un bâtiment peut
visuellement déborder de son emprise logique »), et `_BuildBounds` vient de `renderer.bounds`.
C'est le bon choix pour le dissolve, qui normalise un dégradé sur ce qui est *dessiné*.

La couverture au sol a besoin de l'autre notion : les **cases** occupées, c'est-à-dire
`BuildingDefinition.FootprintSize` à partir de `BuildingRuntime.Cell`. Écrire le champ de
couverture depuis `renderer.bounds` déborderait sur les cases voisines et donnerait une dalle
plus large que le bâtiment. Les deux valeurs coïncident pour un sprite bien cadré, ce qui rend
l'erreur difficile à voir sur les cas simples et flagrante sur la Fonderie ou le Datacenter.

**Piste notée, non codée :** cliquer une silhouette pourrait ouvrir un panneau de chantier
listant le livré et le manquant. Avec des livraisons par lots et des chantiers qui peuvent
stagner faute de matériaux, l'information a de la valeur — aujourd'hui le clic est simplement
neutralisé (voir `BuildingSelectionInput`).

## Comment re-régler

Si l'aspect ne convient plus, régler dans cet ordre — chaque étape suppose la précédente figée :

1. **La forme du front** : `noiseWeight` à 0, puis remonter jusqu'à ce que le bord soit assez
   irrégulier. Se juge sur un bâtiment immobile, à mi-avancement.
2. **La taille du grain** : `noiseScale`. Le repère utile est *combien de taches sur la largeur du
   bâtiment*. Si tu re-mesures sur un prototype en pixels, note la largeur du bâtiment en pixels
   **et** son emprise en cases, sinon la conversion est impossible à refaire.
3. **Le liseré** : `rimWidth` puis `rimColor`. Le liseré doit rester lisible sur le sol le plus
   clair de la carte, pas seulement sur le sol rouge.
4. **Le rythme** : `catchUpRate`, en observant une vraie livraison par lots, jamais une valeur
   poussée à la main. Ce qui se règle est le plancher de durée.
5. **Le flash** : en dernier, une fois le rythme fixé — sa lisibilité dépend de la vitesse
   d'assemblage.

À mesurer si une session de réglage est refaite : la durée réelle entre deux livraisons sur un
bâtiment courant, et la largeur en cases du plus petit et du plus grand bâtiment. Ces deux
chiffres conditionnent `catchUpRate` et `noiseScale`.
