# Matérialisation nano — document directeur

Spécification d'implémentation pour la couche visuelle de construction des bâtiments.
À remettre à Claude Code. Les valeurs marquées `<À RÉGLER>` sont à remplacer par celles
relevées dans le prototype avant de lancer le travail.

---

## 1. Périmètre

Habillage visuel uniquement. La logique de construction ne change pas : les robots constructeurs,
les chantiers, `ConstructionService` et le coffre du Noyau restent tels quels.

**Interdits explicites**, à ne contourner sous aucun prétexte :

- aucune modification dans Game.Core, Game.Gameplay ou Game.Construction
- aucun appel à `Tilemap.SetTile` pour représenter l'effet — la Tilemap reste statique
- aucune donnée nouvelle dans la grille runtime autoritaire

Tout ce qui est décrit ici vit dans **Game.Presentation**, en lecture seule sur l'état du chantier.
Si l'avancement d'un chantier n'est pas lisible proprement depuis Presentation avec l'architecture
actuelle, arrête-toi et signale-le plutôt que d'ouvrir un accès en écriture ou de dupliquer l'état.

## 2. Principe

Trois couches indépendantes, reliées par une seule valeur : l'avancement du chantier, entre 0 et 1.

1. **La donnée** — l'avancement, déjà calculé côté construction. Rien à ajouter.
2. **Le transport** — un champ de couverture, un float par case, écrit dans une texture d'un texel
   par case en filtrage bilinéaire. C'est l'interpolation matérielle qui produit le dégradé continu
   aux frontières.
3. **Le rendu** — deux shaders : un dissolve sur le sprite du bâtiment, un overlay sur le sol.

Coût visé : un draw call pour l'ensemble de la couverture au sol, quel que soit le nombre de
chantiers actifs, et un matériau par bâtiment en cours de construction seulement.

## 3. Avancement affiché ≠ avancement réel

**C'est la contrainte structurante de cette fonctionnalité, à lire avant le reste.**

Les matériaux arrivent au chantier par lots, pas un par un : typiquement 10 composants sur 15
livrés d'un coup, puis les 5 derniers. Si `_Progress` vaut directement `livrés / total`, le dissolve
saute de 0 à 0,67 en une frame et l'effet perd tout son sens.

La vue tient donc sa propre valeur, distincte de l'avancement du chantier :

- `targetProgress` — l'avancement réel, discret, qui saute par paliers à chaque livraison
- `displayedProgress` — ce qui pilote les shaders, qui rattrape la cible à **vitesse bornée**

Cette vitesse est une **surface par seconde**, pas un avancement par seconde, et se divise par
l'emprise du bâtiment :

```
progressRate = assemblyRate / surface du bâtiment en cases
```

Un avancement par seconde serait indépendant de la taille : un convoyeur d'une case et une centrale
de neuf cases mettraient le même temps à s'assembler. C'est trop long sur les convoyeurs, et comme
les segments d'un glissé se matérialisent en série, une ligne de dix convoyeurs devient
interminable. Avec `assemblyRate = 1.8`, la centrale garde ses 5 secondes et un convoyeur
s'assemble en 0,56 s.

`minAssemblyDuration` plafonne le taux à `1 / minAssemblyDuration` pour qu'un petit bâtiment ne
surgisse pas d'un coup. La surface se prend sur l'**emprise logique** (`FootprintSize`), jamais sur
l'AABB visuelle `_BuildBounds` : un sprite peut volontairement déborder de son emprise, et un toit
en surplomb n'a pas à ralentir le bâtiment.

Conséquences voulues :

- la durée de matérialisation devient proportionnelle à la quantité de matière livrée — un lot de
  10 composants met bien plus longtemps à s'assembler qu'un lot de 2
- `displayedProgress` ne dépasse **jamais** `targetProgress` : le bâtiment ne peut pas finir avant
  que les matériaux soient arrivés
- même si les 15 composants arrivent d'un seul coup, la vitesse bornée garantit une durée de
  matérialisation minimale au lieu d'une apparition instantanée

Les paliers ne doivent pas être camouflés mais rendus lisibles : à chaque livraison, un flash du
liseré sur `deliveryFlashDuration` secondes, et une bouffée de particules quand l'essaim sera en
place. Le joueur voit *quand* la matière arrive.

**Les deux couches lisent `displayedProgress`**, jamais l'avancement brut. Sinon le sol saute
pendant que le bâtiment glisse.

## 4. Fichiers attendus

```
Assets/Shaders/BuildDissolve.shader
Assets/Shaders/GroundCoverage.shader
Assets/Scripts/Presentation/BuildDissolveView.cs
Assets/Scripts/Presentation/GroundCoverageRenderer.cs
Assets/Data/Settings/NanoConstructionSettings.asset  (+ sa classe ScriptableObject)
```

Respecte l'organisation en place si elle diffère : regarde où vivent les shaders et les
ScriptableObjects existants avant de créer des dossiers.

## 5. Shader de dissolve

Propriétés : `_MainTex`, `_Progress` (0-1), `_NoiseScale`, `_NoiseWeight`, `_RimWidth`,
`_RimColor`, `_RimBoost` (0-1, pour le flash de livraison), `_RevealMode` (0 = bas vers haut,
1 = radial).

Fragment :

```
base   = selon _RevealMode
noise  = bruit de valeur échantillonné en COORDONNÉES MONDE (worldPos.xy * _NoiseScale)
field  = base * (1 - _NoiseWeight) + noise * _NoiseWeight
clip(_Progress - field)
si (_Progress - field) < _RimWidth :
    ajouter _RimColor * (1 - d/_RimWidth) * (1 + _RimBoost)
```

**Point critique : le bruit s'échantillonne en coordonnées monde, jamais en UV de sprite.**
Sur une feuille animée, les UV changent à chaque frame ; un bruit accroché aux UV ferait sauter
le motif de dissolution d'une image à l'autre. En coordonnées monde, le motif reste accroché au
terrain : le shader n'a besoin de connaître ni la taille du sprite, ni son pivot, ni sa feuille.

Deux conséquences voulues. La question du pivot des frames disparaît d'elle-même. Et deux bâtiments
en chantier côte à côte partagent le même champ de bruit continu, ce qui se lit comme un seul essaim
travaillant sur la zone plutôt que comme deux motifs indépendants juxtaposés.

Prérequis : les bâtiments ne bougent ni ne tournent après placement. Sur une grille, c'est acquis.

Écris le bruit de valeur à la main (hash + interpolation smoothstep), sans dépendance externe.
Shader CG non éclairé sans tag `RenderPipeline`, comme les autres shaders 2D du projet.

**Trois octaves, pas une.** Une octave unique n'a qu'une échelle de détail : combinée au dégradé
de bas en haut, elle ne peut produire qu'une ondulation molle, et augmenter `noiseWeight` amplifie
les vagues au lieu de découper le bord. `p` est la position monde multipliée par `noiseScale` :

```
n1  = vnoise(p)
n2  = vnoise(p * 2.2)
n3  = vnoise(p * 4.5)
fbm = 0.62*n1 + 0.27*n2 + 0.11*n3
n   = saturate((fbm - 0.25) / 0.5)
```

L'étirement final n'est pas cosmétique. Un fBm ne couvre pas 0 à 1 : la somme pondérée de trois
bruits se concentre autour de 0,5, avec une plage utile d'environ 0,25 à 0,75. Sans le remap,
`noiseWeight` produit à peu près moitié moins d'irrégularité que sa valeur ne le laisse croire. Le
prototype normalisait son champ sur toute l'image, ce qu'un shader ne peut pas faire pixel par
pixel — d'où l'étirement par constantes fixes.

Le champ final est `field = base * (1 - noiseWeight) + n * noiseWeight`, et le poids ne doit être
appliqué **qu'une seule fois** : toute normalisation ou pondération supplémentaire ailleurs dans le
fragment diviserait encore l'amplitude réelle du bruit.

`n` étant correctement étiré, `field` couvre exactement 0 à 1 et `progress` se compare directement
à lui — pas de décalage epsilon avant le `clip()`.

## 6. Composant `BuildDissolveView`

Sur le prefab du bâtiment, actif seulement pendant le chantier.

- lit `targetProgress` depuis le chantier, met à jour `displayedProgress` selon la règle de la
  section 3, écrit `_Progress` et `_RimBoost` sur un `MaterialPropertyBlock` — pas sur le matériau
  partagé, sinon deux bâtiments du même type en chantier simultané se dissolvent au même rythme,
  celui du dernier qui a écrit
- détecte une livraison par une hausse de `targetProgress` entre deux ticks, et déclenche le flash
- à l'achèvement, c'est-à-dire quand `displayedProgress` atteint 1 et non quand les matériaux sont
  livrés : retour au matériau standard et retrait du composant
- pour un bâtiment à feuille animée : frame figée sur la première pendant le chantier, animation
  démarrée quand `displayedProgress` atteint 1 — la machine s'allume en devenant opérationnelle
- attention, la feuille de la centrale gaz contient 12 cases mais seulement **11 frames utiles** :
  la douzième est un doublon exact de la première et provoque une micro-pause dans la boucle

## 7. Couverture au sol

`GroundCoverageRenderer`, une seule instance dans la scène.

- un tableau `float[]` d'une valeur par case, alloué une fois, jamais réalloué par frame
- à chaque tick : pour chaque chantier actif, écrire son `displayedProgress` — pas son avancement
  brut — sur les cases de son emprise ; les cases sans chantier décroissent vers zéro sur
  `coverageFadeSeconds`
- upload dans une `Texture2D` en `R8`, `FilterMode.Bilinear`, `TextureWrapMode.Clamp`, via
  `SetPixelData` + `Apply`, **uniquement si le champ a changé** depuis la frame précédente
- un quad unique aligné sur la grille porte `GroundCoverage.shader`, qui échantillonne la texture
  en coordonnées monde — même conversion que le shader de transition de terrain déjà en place
- `clip()` là où la couverture est nulle, pour que le terrain intact ne coûte rien

Le shader applique une teinte au sol converti et un liseré émissif dans la bande où la couverture
approche le seuil, avec la même couleur que le liseré du dissolve : c'est ce qui relie visuellement
les deux couches. Le flash de livraison s'applique aussi à cette couche.

## 8. Ordre de rendu

Sorting layers, du fond vers l'avant :

```
Ground  →  GroundCoverage  →  Shadows  →  Buildings
```

Crée `GroundCoverage` s'il n'existe pas, sans réorganiser les layers déjà utilisés.

## 9. Réglages partagés

Un ScriptableObject `NanoConstructionSettings`, source unique pour les deux shaders et pour le
lissage. Aucune valeur ne doit être réglable bâtiment par bâtiment : je dois pouvoir changer
l'aspect de toute la base depuis un seul asset.

| Champ | Valeur | Remarque |
|---|---|---|
| `noiseScale` | `12` | périodes de bruit par case, valeur unique |
| `noiseWeight` | `0.045` | |
| `rimWidth` | `0.059` | |
| `rimColor` | `#3CB9EB` | liseré du bâtiment |
| `groundRimColor` | `#1E8CB9` | liseré du sol, variante plus sourde |
| `revealMode` | `0` | 0 = bas vers haut, 1 = radial |
| `groundIntensity` | `0.15` | teinte du sol converti, volontairement discrète |
| `groundRimIntensity` | `0.6` | **découplé** de `groundIntensity`, voir ci-dessous |
| `coverageFadeSeconds` | `4` | |
| `assemblyRate` | `1.8` | **cases assemblées par seconde** |
| `minAssemblyDuration` | `0.25` | secondes, plancher de durée d'assemblage |
| `deliveryFlashDuration` | `0.40` | secondes |
| `deliveryFlashIntensity` | `0.28` | |

**`noiseScale`** : c'est **un seul nombre, en périodes de bruit par case**, valable pour tous les
bâtiments — il n'y a pas de valeur par bâtiment.

Les valeurs de bruit et de liseré ci-dessus (`noiseScale`, `noiseWeight`, `rimWidth`) ont été
trouvées **à l'œil, dans le jeu, à la distance de caméra réelle**. Elles remplacent celles dérivées
du prototype navigateur (6,3 / 0,30 / 0,09), qui étaient justes en tant que conversion mais fausses
à l'écran — voir le carnet, section *D'où viennent les valeurs*. Ne pas les « corriger » vers les
chiffres du prototype.

Comme le bruit est échantillonné en coordonnées monde, un bâtiment plus grand reçoit simplement plus
de périodes sur sa largeur, avec un grain de taille physique identique. C'est le comportement voulu :
un petit extracteur n'a pas de raison d'avoir le même nombre de taches qu'une grosse centrale, il a
juste des taches de la même taille. Ne code pas 0,06 en dur, ce serait des dizaines de fois trop
grossier.

**`groundRimIntensity`** : l'intensité du liseré au sol est un paramètre à part entière et ne doit
pas être multipliée par `groundIntensity`. Le sol converti est volontairement très discret, mais la
frontière lumineuse doit rester visible — c'est elle qui relie visuellement le sol au bâtiment. Si
tu les couples, régler l'un éteint l'autre.

## 10. Tests EditMode

- `displayedProgress` rattrape `targetProgress` à la vitesse configurée, et ne le dépasse jamais
- une hausse instantanée de `targetProgress` de 0 à 0,67 produit une montée étalée sur
  `0.67 / progressRate` secondes, pas un saut — donc une durée qui dépend de l'emprise
- un bâtiment d'une case s'assemble strictement plus vite qu'un bâtiment de neuf cases, dans le
  rapport exact de leurs surfaces, et `minAssemblyDuration` plafonne le cas d'une seule case
- une livraison déclenche le flash, et le flash retombe à zéro après `deliveryFlashDuration`
- le composant se retire quand `displayedProgress` atteint 1, pas quand les matériaux sont livrés
- à 0, rien du sprite n'est visible ; à 1, le sprite est intégralement visible
- le composant utilise bien un `MaterialPropertyBlock` : deux bâtiments à des avancements
  différents ne partagent pas la même valeur
- le champ de couverture est écrit sur les cases de l'emprise du chantier, et sur elles seules,
  avec la valeur lissée
- la texture n'est réuploadée que lorsque le champ a changé
- modifier le ScriptableObject se répercute sur les deux couches

Ne cherche pas à tester le rendu lui-même, je le valide à l'œil.

## 11. Ordre de livraison

Trois étapes, chacune validée visuellement avant de passer à la suivante :

1. le dissolve seul avec le lissage et le flash, sur un prefab de test
2. la couverture au sol
3. les particules de l'essaim (`ParticleSystem` suivant le trajet du robot constructeur existant,
   purement décoratif ; bouffée de particules à chaque livraison)

## 12. Document de suivi à produire

À la fin de chaque étape, écris ou complète `Assets/docs/materialisation-nano.md`. Ce n'est pas une
copie de la présente spécification — c'est ce qui manquerait pour reprendre le sujet dans six mois.

Contenu attendu :

- **Où est quoi** : chaque fichier livré et sa responsabilité en une ligne.
- **Comment régler** : pour chaque paramètre, quel asset ouvrir, quel champ modifier, et ce que ça
  change visuellement. Un développeur qui n'a pas suivi cette discussion doit pouvoir ajuster le
  rendu sans lire le code.
- **D'où viennent les valeurs** : elles ont été trouvées sur un prototype navigateur hors dépôt, pas
  dans Unity. Note la conversion `noiseScale` (19 périodes mesurées sur une largeur de 3 cases,
  d'où 6,3 par case) et le raisonnement derrière `catchUpRate` et `deliveryFlashDuration`, sinon ces
  nombres deviendront des constantes magiques que personne n'osera toucher.
- **Les écarts** : tout ce que tu as fait différemment de cette spécification, et pourquoi. C'est la
  partie la plus utile du document — si l'implémentation correspond exactement à la spec, cette
  section est vide et c'est très bien.
- **Les limites connues** : ce qui ne marche pas encore, ce qui a été laissé de côté, les cas non
  couverts.
- **Comment re-régler** : la procédure à suivre pour retrouver de nouvelles valeurs si le rendu ne
  convient plus, y compris ce qu'il faudrait mesurer.

Garde-le court et factuel. Un document qui paraphrase la spécification vieillit mal et finit par
contredire le code ; un document qui consigne les décisions et les écarts reste utile.
