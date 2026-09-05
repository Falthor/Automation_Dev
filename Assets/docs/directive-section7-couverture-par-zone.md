## 7. Couverture au sol — une texture par zone

**Ne pas utiliser une texture unique couvrant la carte.** Ce serait viable avec les deux robots
constructeurs d'aujourd'hui et intenable ensuite : chaque Agent IA arrive avec les siens, le nombre
de chantiers simultanés croît donc mécaniquement avec le territoire conquis. Une texture globale
grossirait avec la carte, serait presque entièrement vide, et devrait être réuploadée dès qu'un
seul chantier bouge n'importe où.

Le découpage n'est pas à inventer, il existe : c'est la **zone**. Un robot constructeur ne travaille
que dans la zone de son Noyau ou de son Agent IA, donc tout chantier appartient nécessairement à
une zone et à une seule. C'est cette garantie qui rend le partitionnement correct — ne pas la
remplacer par un pavage arbitraire en carrés.

Conception attendue :

- une texture de couverture par zone, dimensionnée sur le rayon d'action de cette zone
- allouée à l'ouverture de la zone, libérée si la zone tombe
- un drapeau de modification par zone : seule une zone dont le champ a changé pendant le tick est
  réuploadée
- un quad de rendu par zone, portant sa propre texture et son propre material

Le quad par zone est ce qui simplifie le shader : il n'a aucune zone à résoudre, aucune indirection
à faire, il échantillonne la texture qui lui est attachée. La conversion position monde → UV se fait
à partir de l'origine et de l'étendue de la zone, exactement comme la version globale le faisait à
partir de l'origine et de l'étendue de la carte.

Deux points à traiter :

- le rayon d'action d'une zone est extensible. Soit tu alloues la texture pour le rayon maximal
  atteignable, soit tu la réalloues à l'agrandissement — événement rare, déclenché par une
  recherche, jamais en cours de frame.
- les zones ne se recouvrent pas aujourd'hui. Si cela devait changer, deux quads se superposeraient
  et leurs couvertures s'additionneraient visuellement. À vérifier avant d'écrire le mélange.

Si la zone se révélait un mauvais découpage à l'usage, le repli est un pavage en régions fixes de
taille constante, avec le même drapeau de modification par région. C'est l'alternative, pas le
choix par défaut : elle perd le lien avec la structure du jeu.

Le reste de la section 7 est inchangé : `float[]` alloué une fois, `R8`, `FilterMode.Bilinear`,
`TextureWrapMode.Clamp`, `SetPixelData` + `Apply` seulement si le champ a changé, `clip()` là où la
couverture est nulle, et lecture de `displayedProgress` et non de l'avancement brut.
