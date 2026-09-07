# Matérialisation nano — carnet

Ce carnet ne décrit pas ce que le système fait, ni comment le régler : cela vit dans
[`../architecture/MATERIALISATION.md`](../architecture/MATERIALISATION.md), créé en élaguant ce
carnet — il en était jusque-là la seule description.

Il garde ce qu'aucun document permanent ne peut porter : **les fausses pistes, les mesures qui ont
contredit une intuition, les pièges, et les écarts assumés par rapport à la spec.**

**État : dissolve et couverture au sol faits. Reste les particules de l'essaim** — un
`ParticleSystem` suivant le robot constructeur, purement décoratif, la seule des trois étapes de
livraison non faite.

---

## 1. Les valeurs qu'il ne faut pas « corriger »

**`noiseScale = 12`, `noiseWeight = 0.045`, `rimWidth = 0.059`** ont été trouvées **à l'œil, dans le
jeu, à la distance de caméra réelle**. Elles ne ressemblent pas à celles du prototype navigateur, et
c'est normal : le prototype montrait un bâtiment isolé plein cadre, alors qu'en jeu la centrale gaz
occupe une fraction de l'écran.

**La conversion depuis le prototype était arithmétiquement juste et visuellement fausse.** 0,06 par
pixel sur 320 px de large donnait ≈ 19 périodes sur la largeur de la centrale, soit 6,3 périodes par
case. Le raisonnement tenait ; la référence était la mauvaise. Ce paragraphe existe pour qu'on ne
refasse pas la dérivation en croyant corriger une erreur — le grain réel est **deux fois plus fin**
(12) et la perturbation **presque sept fois plus discrète** (0,045 contre 0,30).

Comme le bruit est échantillonné en coordonnées monde, `noiseScale` vaut pour tous les bâtiments sans
recalcul. **Ne jamais coder 0,06 en dur.**

**Non vérifié :** ces valeurs n'ont jamais été jugées au zoom le plus éloigné, où 12 périodes par case
peuvent passer sous la résolution écran et moirer.

**Les valeurs de la couche de sol sont closes.** `groundLeadShare` 0,5, `groundOverflowCells` 0,45,
`groundRimWidth` 0,08, `groundTexelsPerCell` 4 avaient été posées par raisonnement plutôt que
mesurées ; jugées à l'écran depuis et conservées.

## 2. Les mesures qui ont contredit une intuition

**La texture était juste et n'était pas dessinée.** Le projet rend en espace linéaire, et
`new Texture2D(..., TextureFormat.R8, false)` produit une texture marquée **sRGB** : le GPU décode
donc chaque échantillon en gamma avant que le shader ne le voie. L'octet « pile sur le front » (128)
arrivait à **0,216 au lieu de 0,502**, soit une distance de −0,57 au lieu de 0 — **tout ce qui était
à moins de 0,47 du front était découpé**. Symptôme : une tache bien plus petite que l'emprise, aux
bords déchiquetés, le bord visible tombant là où le champ est le plus raide.

**Le défaut était là depuis le début et invisible.** Tant que la texture portait une couverture 0–1,
la courbe gamma — monotone, laissant 0 à 0 — ne faussait que le dégradé intérieur. Déplacer le zéro
au gris moyen l'a mis exactement là où la courbe fait le plus de dégâts. Verrouillé par un test sur
`graphicsFormat` : rien côté C# ne peut observer le décodage, il a lieu dans le sampler.

**Le grain plat du sol n'était pas un réglage, mais une limite d'échantillonnage.** Le front du
bâtiment était dentelé, celui du sol restait plat *quoi qu'on règle*. Le bâtiment évalue son bruit par
fragment à 12 périodes par unité sur 3 octaves ; le sol lisait une texture à 8 texels par case au
maximum. Représenter 12 périodes demande **au moins 24 échantillons par case** : cuit, le grain n'est
pas seulement plus grossier, il est **sous la fréquence d'échantillonnage**, donc il ressort en
ondulation lente puis en bord parfaitement droit. **Aucune valeur ne pouvait corriger ça** — c'est le
stockage qu'il fallait changer.

**Le sens de révélation, mis en cause et trouvé déjà correct.** Un doute a été levé sur une inversion
du dégradé. Deux mesures indépendantes : `BuildDissolveView` écrit bien `renderer.bounds.min` dans
`_BuildBounds.xy`, et un rendu hors écran à l'avancement 0,35 sans bruit montre **768 pixels visibles
en bande basse contre 0 en bande haute**. Aucune correction appliquée — un `1.0 - normalized.y` aurait
inversé le mode radial avec lui, les deux modes lisant le même `normalized`.

**Un écart de 9 % visible à l'œil nu.** `RenderOverscan` n'était appliqué que dans `BuildingSpawner`,
donc tout ce qui prévisualise un bâtiment se dimensionnait sur `FootprintSize` seul et sortait plus
petit que ce qui allait être construit. Et la valeur elle-même périme avec le fichier : le 1,09 de la
Fonderie avait été mesuré sur v3 (marges de 21 px), v4 n'en a que 4 — la valeur périmée dessinait le
bâtiment **7 % plus large que ses propres cases**. C'est la deuxième fois que ce décalage s'est vu à
l'écran avant d'être vu dans le code, d'où le test qui relit le PNG source.

## 3. Trois versions pour poser le bon problème

Le champ de conversion a demandé trois tentatives, et la troisième est la seule qui pose la bonne
question :

1. **L'avancement écrit tel quel sur chaque case de l'emprise** — toutes portent la même valeur, le
   carré s'allume d'un bloc.
2. **Un seuil statique par case**, comparé à l'avancement — le front traverse enfin l'emprise, mais
   s'arrête à son bord : la frontière extérieure reste le rectangle et **la forme finale est un
   carré**.
3. **Le seuil continue de monter dans l'anneau autour du bâtiment** — c'est lui, plus le bruit, qui
   décide où la conversion s'arrête.

**La leçon vaut bien au-delà de ce champ : tant qu'une frontière est *bornée* par une géométrie, c'est
cette géométrie qu'on voit, quelle que soit la finesse de ce qui se passe à l'intérieur.**

Corollaires trouvés en chemin, chacun après une version fausse :

- **Le seuil est la distance à un rectangle aux coins arrondis.** Une distance au centre donne un
  anneau dont la largeur dépend de la taille du bâtiment ; une distance à la boîte nue fait grandir un
  carré à l'intérieur. Le SDF arrondi est le seul des trois qui soit rond au centre, continu au bord,
  et croissant à l'extérieur **en cases** — donc réglable par un nombre qui veut dire la même chose
  sur un convoyeur et sur une Fonderie.
- **Normalisé sur le coin, pas sur le contour.** Normalisé sur le contour, la région entre le
  rectangle arrondi et le vrai coin dépasse 1 **à partir d'une emprise 8×8** : les coins ne se
  convertissaient jamais.
- **Le débordement est pris en `max`, jamais additionné.** Additionné, la région sous l'emprise (dont
  le rang vaut 0) aurait tout le budget d'avancement à dépenser en débordement, et la tache
  s'ouvrirait en éventail vers le bas.
- **Le fondu est un front qui recule, pas un champ qu'on assombrit.** Faire baisser les valeurs
  stockées ferait traverser la bande de liseré à tout le plateau converti **en même temps** : la tache
  entière s'allumerait au moment de s'éteindre — la version « pavé bleu opaque » revenue par la porte
  de derrière.
- **Le sol doit prendre de l'avance.** Le sprite couvre exactement son emprise et le sol est dessous :
  sur la même horloge, la couverture est **invisible pendant tout le chantier**. Constaté à l'écran —
  la tache complète n'était visible qu'en démolissant.

## 4. Les écarts assumés par rapport à la spec

**Le bruit est un fBm à trois octaves, alors que la spec décrivait « hash + smoothstep ».** Défaut de
la spécification, pas de l'implémentation : une octave unique n'a qu'une échelle de détail, et
combinée au dégradé elle ne produit qu'une ondulation molle — monter `noiseWeight` amplifie les vagues
au lieu de découper le bord. L'étirement `saturate((fbm - 0.25) / 0.5)` est indispensable, un fBm se
concentrant autour de 0,5 sur une plage utile d'environ 0,25 à 0,75.

**`ConstructionSiteRuntime` gagne `SegmentProgress(index)`, alors que la spec interdisait toute
modification dans `Game.Gameplay`.** Contrainte levée sciemment sur ce seul point. `TotalCost` et
`Delivered` sont agrégés par chantier : un glissé de convoyeurs se serait dissous en bloc au lieu de
s'assembler segment par segment. Refaire le calcul dans Presentation aurait mis une règle de jeu à
deux endroits, dont l'un se désynchronise en silence — **ça viole l'esprit de la contrainte plus
gravement qu'un accesseur n'en viole la lettre**. Lecture seule, et n'expose que le résultat, jamais
la somme préfixe. Consigné dans `CONTRACTS.md` §15.

**Le cyan est réservé par la matérialisation, donc l'UI de chantier distingue par la texture.** Le
joueur a appris cette couleur comme « construction en cours », pas comme une catégorie de stock : le
panneau ne peut donc pas s'en servir pour opposer *arrivé* et *en route*. Il met les deux dans
l'accent et les sépare par une **hachure à 45°**, tracée en Painter2D faute d'équivalent USS de
`repeating-linear-gradient`. Une distinction portée par la seule teinte ne dirait rien non plus à un
daltonien.

**Le panneau répond à deux questions à deux endroits.** Les barres décrivent le **stock** engagé
(« dois-je produire ? »), la ligne de service décrit les **robots** (« est-ce que ça avance ? »). Les
confondre est ce qui faisait afficher « en route » à quatre chantiers posés ensemble alors qu'un seul
était servi : leur matière était réservée à tous les quatre, mais un seul avait un robot qui marchait
vers lui.

**La couleur de danger se lit sur « manquant > 0 », jamais sur « livré < requis ».** La distinction
utile n'est pas entre arrivé et en route mais entre « le système s'en occupe » et « je dois
produire » : se déclencher sur `livré < requis` peindrait en rouge un ingrédient pendant tout le temps
où les robots vont justement le chercher.

## 5. Un oubli qui se répète

**Le second `BuildingSpawner` du chemin de restauration était construit sans dalle, sans linker et
sans ombre.** Un bâtiment rechargé depuis une sauvegarde n'avait donc ni dalle de béton ni ombre,
alors que le même bâtiment posé à la main en avait. Corrigé — mais le **dédoublement du spawner
lui-même ne l'est pas**, et c'est exactement le piège que `SetViewSpawner` évite côté chantiers.

Même classe de défaut que l'overscan et que l'ajustement par axe : **quatre chemins dessinent un
bâtiment, et une correction appliquée à un seul reste invisible jusqu'à ce qu'on regarde le bon.**
Trois fois de suite, la parade a été la même — un seul endroit répond à la question.
