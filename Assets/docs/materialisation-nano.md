# Matérialisation nano — carnet

Décisions, écarts et limites. La spécification est `Intro/directive-materialisation-nano.md` ;
ce document ne la répète pas, il consigne ce qui ne s'y trouve pas.

**État : dissolve et couverture au sol faits.** Un bâtiment posé s'assemble à l'écran au rythme de
ses livraisons, et le sol sous lui se convertit au même rythme. Les particules ne sont pas faites.

---

## Où est quoi

| Fichier | Rôle |
|---|---|
| `Art/Shaders/BuildDissolve.shader` | Découpe le sprite sous un front de révélation bruité, avec liseré lumineux. |
| `Scripts/Presentation/BuildDissolveView.cs` | Pilote l'effet : lissage de l'avancement, flash de livraison, écriture du `MaterialPropertyBlock`, retrait à l'achèvement. |
| `Scripts/Presentation/ConstructionSiteVisualSync.cs` | Les trois états d'un segment de chantier, et le passage de main à la vraie vue. |
| `Art/Shaders/GroundCoverage.shader` | Teinte le sol converti et allume son liseré, à partir de la texture de couverture de la zone. |
| `Scripts/Presentation/GroundCoverageRenderer.cs` | Le champ de couverture : une texture, un quad et un material **par zone**, réuploadés seulement quand le champ a changé. |
| `Scripts/Presentation/NanoConstructionSettings.cs` | La classe de réglages. |
| `Data/Presentation/NanoConstructionSettings.asset` | L'instance unique. C'est le seul fichier à ouvrir pour changer l'aspect. |
| `Scripts/Tests/EditMode/Presentation/BuildDissolveViewTests.cs` | 17 tests : lissage, flash, achèvement, dépendance à la taille du bâtiment, plancher de durée, isolation entre bâtiments, propagation des réglages. |
| `Scripts/Tests/EditMode/Presentation/GroundCoverageRendererTests.cs` | 11 tests : écriture sur l'emprise et sur elle seule, front du centre vers les coins, seuils statiques, décroissance, réupload conditionnel, cycle de vie des zones. |
| `Scripts/Tests/EditMode/Presentation/ConstructionSiteVisualSyncTests.cs` | 7 tests : les trois états, la démolition et l'annulation en cours d'assemblage, le glissé multi-segments. |
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
| `assemblyRate` | Vitesse d'assemblage, **en cases par seconde**. Plus bas = matérialisation plus lente. Un bâtiment de N cases met `N / assemblyRate` secondes. |
| `minAssemblyDuration` | Plancher de durée. Inerte aux valeurs actuelles (une case prend déjà 0,56 s) ; garde-fou si `assemblyRate` remonte. |
| `deliveryFlashDuration` | Durée du flash à chaque arrivée de matière. |
| `deliveryFlashIntensity` | Puissance de ce flash. |
| `groundIntensity` | Force de la teinte du sol converti. Volontairement discrète. |
| `groundRimColor` | Couleur du sol converti **et** de son liseré. |
| `groundRimIntensity` | Force du liseré au sol. Découplé de `groundIntensity` : régler l'un ne doit pas éteindre l'autre. |
| `groundNoiseWeight` | Perturbation du seuil par case. À 0 la conversion part du centre en anneaux nets ; à 1 elle est dispersée. |
| `groundFrontSoftness` | Largeur, en unités de seuil, sur laquelle une case passe de non convertie à convertie. Bas = les cases s'allument l'une après l'autre ; haut = houle continue. |
| `groundRimWidth` | Distance d'échantillonnage de la frontière, **en cases**. Élargit la bande lumineuse, ne la déplace pas. |
| `coverageFadeSeconds` | Temps que met une case sans chantier à revenir à zéro. |
| `groundCoverageSortingOrder` | Rang de la couche de sol. **3**, entre le terrain (0 et 1) et la dalle de béton (5). |
| `sitePlaceholderAlpha` | Opacité de la silhouette bleue **pendant** l'assemblage. En attente elle reste à l'alpha de `siteTint` (sur le composant, pas dans l'asset). |
| `siteSilhouetteSortingOrder` | Rang de la silhouette. À 7 elle passe sous l'ombre portée et sous le sprite — voir *Limites connues* pour le cas de l'Extracteur. |

`dissolveShader` pointe sur `Custom/BuildDissolve`. Ne le vide pas : sans shader, le composant
laisse le sprite intact et ne fait rien.

## D'où viennent les valeurs

### Le bruit et le liseré : réglés dans le jeu, pas sur le prototype

**`noiseScale = 12`, `noiseWeight = 0.045`, `rimWidth = 0.059`.** Ces trois valeurs ont été
trouvées **à l'œil, dans le jeu, à la distance de caméra réelle**. Elles ne ressemblent pas à
celles du prototype navigateur, et c'est normal : le prototype montrait un bâtiment isolé, plein
cadre. En jeu, la centrale gaz occupe une fraction de l'écran, donc il faut un grain nettement plus
fin (12 au lieu de 6,3) et une perturbation bien plus discrète (0,045 au lieu de 0,30) pour obtenir
la même lecture à l'écran.

**Ne les « corrige » jamais vers les chiffres du prototype.** La conversion 6,3 était
arithmétiquement juste et visuellement fausse : le prototype travaillait en pixels, grain de 0,06
par pixel sur 320 px de large, soit ≈ 19 périodes sur la largeur de la centrale, donc 19 ÷ 3 ≈ 6,3
périodes par case. Le raisonnement tient, la référence était la mauvaise. Ce paragraphe est
conservé pour qu'on ne refasse pas la dérivation en croyant corriger une erreur.

**Ces valeurs n'ont pas été vérifiées au zoom le plus éloigné.** À grande distance le grain à 12
périodes par case peut passer sous la résolution écran et se lire comme un bruit uniforme, ou
moirer. À contrôler avant de considérer le réglage clos.

Comme le bruit est échantillonné en coordonnées monde, `noiseScale` vaut pour tous les bâtiments
sans recalcul : un bâtiment plus grand reçoit plus de périodes, avec un grain de même taille
physique. **Ne jamais coder 0,06 en dur**, ce serait des dizaines de fois trop grossier.

### Le rythme et le flash

**`assemblyRate = 1.8`** cases par seconde. Le taux d'avancement d'un bâtiment est
`assemblyRate / surface en cases`, donc la centrale gaz (9 cases) tourne à 0,2 avancement par
seconde : une matérialisation complète y prend au minimum 5 secondes même si tous les matériaux
arrivent d'un coup. C'est ce plancher qui est réglé, pas une durée « moyenne » : la durée réelle
est proportionnelle à la matière livrée. Un convoyeur (1 case) s'assemble en 0,56 s.

Le 1,8 vient de `0,2 × 9`, c'est-à-dire du rythme réglé à l'œil sur la centrale — pas du 0,25 par
défaut d'origine, qui aurait donné 2,25 et raccourci la centrale à 4 s. **Le repère de réglage est
la centrale gaz**, et c'est sur elle qu'il faut rejuger toute nouvelle valeur.

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
| Le bruit est un fBm à trois octaves suivi d'un étirement analytique, alors que §5 décrivait « hash + smoothstep » | **Défaut de la spécification, pas de l'implémentation.** « Bruit de valeur, hash plus smoothstep » décrit une seule octave ; le prototype qui a servi à trouver les réglages en empilait trois. Une octave unique n'a qu'une échelle de détail : combinée au dégradé, elle ne produit qu'une ondulation molle, et monter `noiseWeight` amplifie les vagues au lieu de découper le bord. L'étirement `saturate((fbm - 0.25) / 0.5)` est indispensable : un fBm se concentre autour de 0,5 sur une plage utile d'environ 0,25 à 0,75, donc sans lui `noiseWeight` rend moitié moins d'irrégularité que sa valeur ne le suggère. Le prototype normalisait sur toute l'image, ce qu'un fragment ne peut pas faire — d'où les constantes fixes. §5 est corrigée. |
| `RenderOverscan` sort de `BuildingSpawner` et devient `BuildingSpawner.ArtWorldSize` | L'overscan n'était appliqué que dans `BuildingSpawner`. Tout ce qui prévisualise un bâtiment — aperçu de pose, silhouette de chantier, sprite en dissolve — se dimensionnait sur `FootprintSize` seul et sortait donc plus petit que ce qui allait être construit : **9 % sur la Fonderie**, visible à l'œil nu à côté d'un bâtiment fini. Quatre définitions sont concernées (Fonderie 1,09, Splitter et Crossroad 1,08, Convoyeur Coin 1,02). La taille de l'art est maintenant calculée en un seul endroit, dont tous ces chemins dépendent. Le convoyeur garde l'ajustement uniforme de `ConveyorView`, overscan compris et sous la même condition (uniquement si la bande porte sa propre art, jamais le sprite procédural). **La dalle de béton est délibérément exclue** : elle marque les cases occupées, pas l'étendue de l'art. Verrouillé par `SilhouetteAndAssembly_AreSizedToTheArtTheRealViewWillUse_OverscanIncluded`. |
| **Un seuil statique par case**, comparé à l'avancement — et non l'avancement écrit tel quel | Deuxième erreur de conception, corrigée après coup d'œil à l'écran. Écrire `displayedProgress` sur toutes les cases de l'emprise leur donne **la même valeur** : le carré s'allume et s'éteint d'un bloc, sans aucun front qui le traverse. Chaque case porte maintenant son propre seuil statique — distance au centre de l'emprise, normalisée pour qu'un coin vaille 1, perturbée par un hachage de la case monde — et la couverture vaut `saturate((progress × (1 + softness) − seuil) / softness)`. C'est exactement la construction du dissolve du bâtiment (`field` comparé à `_Progress`), transposée à la case. La conversion part donc du centre et gagne les coins. Le facteur `(1 + softness)` est ce qui garantit qu'à l'avancement 1 même un coin, de seuil 1, est entièrement converti — le même souci de bornes que l'epsilon du dissolve. Le hachage porte sur les coordonnées **monde**, pour la même raison que le bruit du dissolve : deux chantiers voisins ne redémarrent pas le même motif. |
| Le liseré au sol mesure la **frontière spatiale**, pas la valeur de couverture | Première version fausse, et le rendu était un pavé bleu opaque : `rim = 1 - smoothstep(0, w, coverage)` s'allume partout où la couverture est faible, or à l'intérieur d'une emprise le champ est un **plateau uniforme**. Un chantier à 0,2 avait donc toute son emprise « près du seuil » et brillait en entier. Le liseré échantillonne maintenant les quatre voisins à `groundRimWidth` cases et prend le plus grand écart : nul sur un plateau, non nul seulement dans la retombée du bord. Il se met aussi à l'échelle tout seul — un chantier à peine commencé a une petite marche au bord, donc un liseré discret. `groundRimWidth` change de sens au passage : ce n'est plus une largeur en unités de couverture mais **une distance d'échantillonnage en cases**, d'où le passage de 0,35 à 1. |
| Texture de couverture **allouée au rayon maximal**, pas au rayon courant | Le rayon d'action est extensible par recherche (22 → `CoreRuntime.ExtendedActionRadiusCells` = 32). §7 laisse le choix entre réallouer à l'agrandissement et allouer d'emblée pour le plafond ; c'est la seconde qui est prise, parce qu'elle supprime entièrement le chemin de réallocation. Le coût est dérisoire — 65×65 en R8, soit ~4 Ko par zone — et les cases hors rayon restent simplement à zéro, donc `clip()`. |
| Le sol converti et son liseré partagent `groundRimColor` | §9 ne prévoit pas de couleur de teinte distincte. Comme la consigne est justement que les deux liserés soient de la même famille, une seule couleur pour la couche de sol est cohérente et fait un réglage de moins. À séparer si le réglage à l'œil le demande. |
| **Un seul `_RimBoost` par zone**, pas par chantier | La couche est une texture et un material par zone ; le flash ne peut donc pas être par chantier sans un material par chantier, ce qui reviendrait à annuler le regroupement. La zone prend le plus fort flash de ses chantiers. Visible seulement quand deux chantiers d'une même zone reçoivent une livraison en même temps, et ça se lit comme une pulsation unique plutôt que comme une fausse. |
| La couverture lit `ConstructionSiteVisualSync.CollectDrawnSegments`, pas `ConstructionSiteSystem.Sites` | Il **faut** passer par là : un segment matérialisé mais encore en assemblage a quitté `Sites` alors qu'il est toujours dessiné, et c'est justement le moment où sa couverture doit continuer de monter. C'est aussi le seul endroit qui détient l'avancement *affiché*, que §3 impose aux deux couches. Accesseur en lecture seule, remplissant une liste fournie par l'appelant pour ne rien allouer par frame. |
| **La paire canonique : `ArtWorldSize` / `FootprintSize`** | `ArtWorldSize` pour tout ce qui doit **coïncider avec le dessin** — aperçu de pose, silhouette, sprite en dissolve, vue réelle. `FootprintSize` pour tout ce qui **marque les cases occupées** — la dalle de béton, et **la couverture au sol de l'étape 2**, qui exprime les cases converties et non l'étendue du dessin. La question est donc tranchée d'avance pour l'étape 2, inutile de la reposer. C'est la même distinction que celle déjà notée pour `_BuildBounds`, qui est l'AABB *visuelle* précisément parce qu'il normalise un dégradé sur ce qui est dessiné. Les deux valeurs coïncident sur un sprite bien cadré sans overscan, ce qui rend l'erreur invisible sur les cas simples et flagrante sur la Fonderie. |
| Le sens de révélation a été mis en cause, mesuré, et trouvé **déjà correct** | Un doute a été levé sur une inversion du dégradé (bâtiment construit par le haut). Deux mesures indépendantes dans l'éditeur : `BuildDissolveView` écrit bien `renderer.bounds.min` dans `_BuildBounds.xy`, donc `normalized.y` vaut 0 à la base et 1 au sommet ; et un rendu hors écran à l'avancement 0,35 avec `noiseWeight` à 0 montre 768 pixels visibles dans la bande basse contre 0 dans la bande haute. **Le dépôt révèle du sol vers le haut.** Aucune correction appliquée, ni dans le shader ni à la source — un `1.0 - normalized.y` aurait inversé le mode radial avec lui, les deux modes lisant le même `normalized`. Verrouillé par `BuildBounds_CarriesTheWorldAabbsBottomLeftCorner_NotItsCentre`. |
| Plus de décalage ±0,001 avant le `clip()` | Il compensait un `field` dont on ne savait pas s'il couvrait vraiment [0, 1]. Avec l'étirement, `base` et `n` valent tous deux 0–1, donc `field` aussi, et `_Progress` se compare directement. Retiré. |
| Aucun prefab | Le projet n'a aucun prefab de bâtiment : toutes les vues sont construites en code (`BuildingSpawner`, `WorldContentSpawner`). Le composant s'ajoute donc à n'importe quel GameObject portant un `SpriteRenderer`. |
| **`ConstructionSiteRuntime` gagne `SegmentProgress(index)` — la spec §1 interdit toute modification dans `Game.Gameplay`** | Contrainte **levée sciemment sur ce seul point**, après arbitrage. `TotalCost` et `Delivered` sont agrégés par chantier : un glissé de convoyeurs se serait dissous en bloc au lieu de s'assembler segment par segment. Calculer l'avancement d'un segment demande de savoir quelle livraison alimente quel segment — une règle que `ConstructionSiteRuntime` possède en privé (`ConsumedByMaterializedSegments`). La refaire dans Presentation aurait mis cette règle à deux endroits, dont l'un se désynchronise en silence : ça viole l'esprit de la contrainte (Presentation ne doit pas s'approprier la logique du jeu) plus gravement qu'un accesseur n'en viole la lettre. L'accesseur est en **lecture seule** et n'expose **que le résultat**, jamais la somme préfixe — sinon l'arithmétique redescendait dans Presentation et le problème restait entier. Consigné dans `architecture/CONTRACTS.md` §15. |

## Le pipeline de vues

Trois états, un seul propriétaire. `ConstructionSiteVisualSync` dessine lui-même le sprite en
dissolve et passe la main à `BuildingSpawner.SpawnView` quand l'assemblage est fini.
`BuildingSpawner` n'a pas été modifié et n'a gagné aucun mode.

| État | Ce qu'on voit | Sortie |
|---|---|---|
| **En attente** | Silhouette bleue à l'alpha plein de `siteTint`, sprite intégralement découpé. | rang 7 (silhouette) + rang 10 (sprite, invisible) |
| **En assemblage** | Silhouette à `sitePlaceholderAlpha`, sprite qui se matérialise par-dessus. | idem |
| **Terminé** | La vraie vue. | `BuildingSpawner` |

**L'ensemble « en cours d'assemblage » survit au chantier.** Un segment se matérialise à l'instant
où sa dernière pièce arrive — bien avant d'avoir fini de s'assembler à l'écran — et quitte alors la
plage en attente de `ConstructionSiteSystem`. Les vues concernées sont donc *détachées* : elles
restent dans le composant, pilotées à la cible 1, et ne sont libérées qu'à `DisplayedProgress == 1`.
Leur critère de vie n'est plus le chantier mais la grille : une entrée dont la cellule ne la
contient plus a été démolie ou écrasée, et disparaît sans jamais devenir une vraie vue.

Le passage de main **spawn la vraie vue et détruit les objets d'assemblage dans le même appel**
(`HandOver`), donc aucune image ne montre les deux ni aucun. Le sprite en dissolve porte déjà la
position, la taille (`BuildingSpawner.SetSpriteToWorldSize`, par axe, overscan compris) et la
rotation de la vraie vue : rien ne bouge au moment du basculement.

`ConstructionInputAdapter.OnSegmentMaterialized` ne spawne plus la vue ; il **prête son
`BuildingSpawner`** au composant via `SetViewSpawner`. Un second spawner aurait son propre
dictionnaire de vues par cellule, et la démolition ne retrouverait plus les vues créées par
l'autre. En revanche `ItemVisuals.Register` reste immédiat : le segment est vivant dans
`TransportSystem` dès la matérialisation, et les items qui le parcourent doivent être visibles
pendant qu'il s'assemble.

**Chargement d'une sauvegarde** : la restauration passe par `Start()` de `GameRuntime`, donc sur
une scène fraîche où l'ensemble « en cours » est vide. Les bâtiments restaurés reçoivent
directement leur vraie vue ; les segments encore en attente d'un chantier restauré repartent en
silhouette au premier `LateUpdate`, avec `SegmentProgress` comme source. Le chemin de restauration
ne traverse jamais l'ensemble « en cours ».

## Limites connues

- **L'étendue dessinée déborde l'emprise d'environ une demi-case de chaque côté.** La couverture
  est *écrite* sur `FootprintSize`, mais le filtrage bilinéaire — celui que §7 demande pour obtenir
  une frontière continue — fait retomber la valeur de la case de bord vers zéro sur toute la case
  voisine. Une emprise 3×3 se voit donc sur ~4×4. La correspondance texel/case est exacte et
  vérifiée (chaque centre de case tombe sur son centre de texel) : ce n'est pas un étirement, c'est
  la retombée. À juger à l'écran ; un seuil de `clip()` plus haut resserrerait le bord au prix de
  sa douceur. Aucun élargissement ni resserrement n'a été ajouté.
- **Une seule zone existe aujourd'hui**, celle du Noyau. Le composant traite les zones comme un
  ensemble et le seul endroit à étendre pour un Agent IA est `CollectZones`. Le partitionnement
  repose sur la garantie qu'un robot ne travaille que dans sa zone ; **les zones ne se recouvrent
  pas**, et si cela changeait deux quads se superposeraient et leurs couvertures s'additionneraient
  visuellement — à revoir avant, comme §7 le signale.
- **Les gisements échappent encore à `ArtWorldSize`.** `WorldContentSpawner` garde son propre
  ajustement par axe pour les gisements, et trois arts de gisement ne sont pas carrés (Cuivre 1,25,
  Fer 1,21, Charbon 1,03 de rapport largeur/hauteur) sur une emprise 2×2 carrée : ils sont donc
  étirés verticalement aujourd'hui. Les aligner changerait leur apparence, ce qui n'a pas été
  demandé — mais c'est le dernier chemin qui répond seul à la question de la taille de l'art.
- **Le plancher `minAssemblyDuration` ne mord jamais aux valeurs actuelles.** Il plafonne le taux à
  `1 / 0,25 = 4` avancement par seconde, alors qu'un bâtiment d'une case — le plus petit possible —
  tourne déjà à 1,8. Il ne servirait qu'au-delà de `assemblyRate = 4`. C'est un garde-fou pour un
  réglage futur, pas une contrainte active : le test qui le couvre doit donc monter `assemblyRate`
  pour le déclencher.
- **À `noiseWeight = 1`, « 0 ne montre rien » n'est plus strictement garanti.** Le champ devient le
  bruit seul, qui vaut exactement 0 sur une surface non nulle après l'étirement `saturate`, et ces
  pixels-là survivent au `clip()` à l'avancement 0. En dessous de 1, le terme `base` est strictement
  positif partout sauf sur le bord inférieur exact, donc le cas ne se pose pas. L'ancien décalage
  ±0,001 le couvrait accessoirement ; il n'était pas là pour ça, et la valeur 1 n'est pas un réglage
  utile (le dégradé disparaît entièrement).
- **La silhouette est au rang 7, ce qui est faux pour l'Extracteur.** Le rang 7 la place sous
  l'ombre portée (8) et sous le sprite (10), ce qu'il faut pour un bâtiment normal. Mais un
  Extracteur se pose sur un gisement, et le gisement est dessiné plus haut : la silhouette d'un
  Extracteur en attente passe donc *derrière* le gisement qu'il vient réserver, alors que c'est
  précisément l'information de pose la plus utile — et l'Extracteur est le bâtiment le plus posé du
  jeu. Corriger ça demande de renuméroter l'échelle des rangs (une quinzaine de couches dispersées
  dans une dizaine de fichiers plus deux `.asset`, avec des égalités), ce qui est une session
  d'infrastructure à part sur `main` : une renumérotation partielle casserait items, flèches et
  robots. **Hors périmètre ici, sciemment.**
- **`GameRuntime` construit un second `BuildingSpawner`** pour le chemin de restauration
  (`GameRuntime.cs`, fin de `Start()`). C'est exactement le piège que `SetViewSpawner` évite côté
  chantiers : ce spawner-là a son propre dictionnaire de vues par cellule, distinct de celui de
  `ConstructionInputAdapter`. Antérieur à cette tâche, non touché ici.
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

**C'est tranché, pas à rediscuter** : la couverture au sol suit `FootprintSize`, comme la dalle de
béton — voir la paire canonique dans *Les écarts*.

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
4. **Le rythme** : `assemblyRate`, en observant une vraie livraison par lots, jamais une valeur
   poussée à la main. Ce qui se règle est le plancher de durée, **et il se règle sur un bâtiment de
   taille connue** : la valeur est en cases par seconde, donc juger sur une Fonderie (9 cases) et
   sur un convoyeur (1 case) ne donne pas le même chiffre. Le repère est la centrale gaz.
5. **Le flash** : en dernier, une fois le rythme fixé — sa lisibilité dépend de la vitesse
   d'assemblage.

À mesurer si une session de réglage est refaite : la durée réelle entre deux livraisons sur un
bâtiment courant, et la largeur en cases du plus petit et du plus grand bâtiment. Ces deux
chiffres conditionnent `catchUpRate` et `noiseScale`.
