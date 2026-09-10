# Expéditions — carnet

Carnet d'implémentation de [`../design/SPEC_EXPEDITIONS.md`](../design/SPEC_EXPEDITIONS.md), version amendée
pour la grande carte. Les décisions prises, les écarts par rapport à la spec et pourquoi. La spec
reste la référence de conception ; ce document enregistre ce qui a réellement été construit.

Périmètre de la brique 1 : **le processus et les robots explorateurs, aucune interface.** La carte dézoomée et
l'écran de lancement viennent après et ne doivent pas influencer la forme du modèle.

---

## 1. Où le tirage se fait — et pourquoi au lancement

La contrainte est qu'une mission sauvegardée en vol doit atterrir à l'identique : ni retirée, ni
perdue. Deux façons de la tenir :

| | |
|---|---|
| tirer au lancement, porter le résultat | **retenu** |
| stocker une graine, tirer à l'arrivée | écarté |

Les deux sont reproductibles en principe. Porter le résultat rend la propriété **structurelle** plutôt
que disciplinaire : un tirage fait à l'arrivée dépend de l'état du monde à cet instant, et un
rechargement déplace cet instant par rapport à tout le reste — la garantie reposerait alors sur le fait
que rien d'autre n'a bougé, ce qui est exactement le genre de promesse qui cesse d'être vraie sans
prévenir. Le résultat porté, une mission rechargée **ne peut pas** différer : il n'y a plus rien à
décider.

Le joueur n'en apprend rien avant le rapport, et connaître le résultat tôt ne coûte rien puisque
personne ne le lit.

Verrouillé par `AMissionSavedInFlight_LandsIdentically` : la même mission est menée à terme dans le
monde d'origine et dans un monde restauré depuis une sauvegarde prise à 25 % du trajet, puis les deux
réserves de CU sont comparées.

## 2. Le seuil des robots explorateurs est une fraction, pas un nombre

Le plafond de réserve est passé de **25 000 à 60 000 puis 70 000**. Le seuil, écrit en absolu, n'a
pas suivi : un déclencheur d'urgence est devenu un déclencheur d'introduction sans que rien ne le
signale, deux fois. `MissionSettings` n'expose donc qu'une **fraction du plafond**, et le seuil en CU
est une méthode qui prend le plafond en argument — ce qui rend impossible de stocker un seuil ayant
cessé d'être d'accord avec la réserve qu'il décrit.

0,357143 du plafond livré de 70 000 fait 25 000. `MovingTheReserveCap_MovesTheRobotThreshold` vérifie
que doubler le plafond double le seuil.

## 3. L'écart assumé : §8 a été écrite pour une carte de 300

La spec pose un gisement de missions fini — 8 secteurs reconnaissables à 500 CU, 5 points de
récupération à 1 500 — pour un total de 11 500 CU. C'était cohérent sur une carte de 300, où huit
secteurs représentaient l'essentiel de ce qu'un joueur pouvait atteindre.

À 10 000, la bande minière (rayon courant → seuil de 154) contient environ **291 secteurs**. Payer
chacun ferait 145 000 CU : exactement la fontaine à monnaie que le gisement fini existe pour empêcher.

**Résolution : le gisement devient un budget de paiements, pas un ensemble de lieux.** Les 8 premières
reconnaissances et les 5 premières récupérations paient ; au-delà, la carte se révèle toujours mais ne
rapporte plus. Les totaux de §8 sont conservés à l'unité près, et la règle ne bouge pas avec la taille
du monde.

C'est un écart d'interprétation, pas une correction de la spec : elle dit « une fois visités, les
sites sont épuisés », ce qui reste vrai des cinq points de récupération (suivis individuellement) ;
seule la reconnaissance passe d'un décompte de lieux à un décompte de paiements.

## 4. Le plancher à zéro CU

`APlayerAtZeroCu_WithNoProduction_CanClimbBackOut` est le test qui empêche une partie d'être perdue
sans recours. Il met la réserve à zéro, épuise le budget, et vérifie que trois missions successives
font remonter la réserve à chaque fois.

Ce qui le rend vrai tient à deux règles, pas à une : **lancer ne coûte jamais de CU** (§1), donc
l'action reste disponible ; et le paiement régénérant reste possible une fois le budget épuisé, donc
l'action paie. L'une sans l'autre laisse un cul-de-sac.

**Écart de forme** : la spec dit « au moins un site se régénère ». Un site particulier ne peut pas
garantir la sortie — le joueur peut ne jamais l'avoir révélé. C'est donc un **paiement** régénérant,
attaché au système plutôt qu'à un lieu, délibérément trop pauvre pour qu'on le ferme par choix.

## 5. L'effectif porté à 1

Les unités n'existent pas, donc toute la §5.4 — le frein à la surenchère — est sans objet. Le
paramètre est néanmoins porté partout (`MissionRuntime.Crew`, `TryLaunch(..., crew)`,
`DurationOf(..., crew)`) et vaut 1. Il coûte presque rien maintenant et évite que le code s'organise
autour de « une mission a un exécutant », ce qu'il faudrait défaire à l'arrivée des escouades. La
durée en tient déjà compte — une grosse escouade avance au rythme du plus lent — mais l'effet est nul
tant que l'effectif vaut 1.

## 5bis. Rien ne parvient au Noyau tant que le robot est dehors

**Le Noyau ne peut pas communiquer au-delà de son rayon d'action.** Il y est aveugle et muet — c'est
la raison d'être des expéditions. Un robot en campagne n'a donc personne à qui transmettre : il
rapporte ses données.

Cela transforme le « le joueur ne voit rien du déroulement » de §6, qui se lisait comme une règle
d'interface, en **fait du monde**. Et cela condamne quelque chose que la première version faisait :
elle révélait le disque à mi-parcours, à l'état *Résolution*, en s'appuyant sur la formule de §7.3
« le Noyau a reçu les données jusqu'à la dernière transmission ». Une transmission depuis l'extérieur
du rayon est précisément ce qui n'existe pas.

La carte, le site et les CU arrivent donc **tous à l'amarrage**, quand le robot est de retour dans le
rayon. `Résolution` subsiste comme état parce que §6 le nomme, mais ne produit plus rien
d'observable : c'est le moment où le robot atteint sa cible, pas celui où le Noyau l'apprend.

`TheMapDoesNotMoveWhileTheRobotIsStillOut` échantillonne le trajet à dix reprises et exige que ni la
carte ni la réserve ne bougent avant l'amarrage.

**Une tension restante, à trancher avec les unités.** §7.3 dit qu'une escouade perdue révèle « jusqu'au
point de rupture » et laisse une cicatrice sur la carte. Sous la règle ci-dessus, une escouade qui ne
revient pas ne transmet rien, donc ne révèle rien — et la cicatrice n'existe pas non plus. Les deux
lectures sont défendables (un relais laissé derrière soi, une boîte noire récupérée plus tard), mais
elles ne se décident pas maintenant : les sondes ne meurent pas, seules les unités le peuvent.

## 5ter. Une couture construite des deux côtés et jamais faite

`SectorMissionRange` a été bâti, testé, et documenté dans `MAP.md` — et `MissionSystem` ne l'appelait
jamais. Aucune référence. Les deux moitiés étaient justes et le joint entre elles n'existait pas :
une prospection pouvait viser du terrain déjà révélé, ou franchir le seuil pour aller chasser dans la
bande de l'exploration.

**Aucun test unitaire ne pouvait le voir**, puisque chaque moitié passait de son côté. C'est la
quatrième occurrence de ce profil dans le chantier — les 527 rochers restés à l'ordre zéro, le champ
de sauvegarde que personne n'écrivait, le balayage qui passait avant son générateur — et c'est ce qui
a fait naître la règle : **un prédicat testé n'est pas un prédicat appliqué**, et le test qui l'attrape
part du point d'entrée réel, jamais de la pièce (`DEVELOPMENT_RULES.md` §7).

**Brancher la couture a fait rougir quatre tests d'un coup**, ce qui est le meilleur signe possible :
ils lançaient des missions impossibles, donc ils validaient un comportement qui n'aurait jamais dû
exister. Mon aide de test produisait ces cibles parce qu'elle avait été écrite avant que la contrainte
n'existe.

**Et une erreur de mémoire, sur le cas le plus favorable.** J'ai rapporté l'adjacence comme « tranchée »
et absente du code. Le document dit l'inverse — « tranché, et autrement que par l'adjacence : c'est la
bande qui décide » — et c'est **un paragraphe que j'avais rédigé moi-même** quelques jours plus tôt.
J'ai lu mon souvenir de la conversation plutôt que le document. Un test épingle maintenant l'absence
d'adjacence avec sa raison : une propriété délibérément écartée n'existe nulle part si personne ne
l'écrit, et le prochain lecteur la « corrigera ».

## 6. La révélation n'est pas réimplémentée

Une mission appelle `SectorGrid.RevealInscribedDisc`, qui possède déjà la forme. Le disque inscrit et
non le carré : les quatre coins restent dans le brouillard, et un secteur ouvert par une mission reste
donc à `SectorDiscovery.Partial` pour toujours — ce n'est pas un état transitoire mais son état de
repos. Un test le vérifie plutôt que de supposer que l'appel a la bonne forme.

Avec un robot explorateur, la carte ne peut pas échouer (§7.1). `WithARobot_TheMapNeverFails` envoie vingt
missions et vérifie qu'aucune ne revient aveugle.

## 7. Ce qui entre en sauvegarde

`SaveData.Missions`, un `JObject` : les missions en vol avec leur horloge et leur issue déjà tirée,
les charges restantes de chaque robot explorateur, les sites consommés, les budgets entamés, le minuteur de
régénération. Champ additif avec repli par champ — pas de bump de `CurrentVersion`. Une sauvegarde
antérieure se charge en partie dont les robots explorateurs ne sont pas arrivés.

**Les rapports non lus ne sont pas sauvegardés**, délibérément : un rapport non lu est redélivré au
prochain atterrissage plutôt que perdu.

`SaveFormatTests` épingle l'ensemble des clés et est tombé à l'ajout, ce qui est son rôle. Les
nouvelles clés sont assertées **à travers l'aller-retour JSON**, pas seulement présentes dans la
fixture — c'est le trou trouvé la fois précédente sur `Discovered`.

## 7bis. Une décision qui prenait son stock de l'extérieur

`CoreDirectiveSystem.CanValidate` et `Validate` recevaient le dictionnaire de stock **en argument**.
Le jeu passait `GameRuntime.DirectiveStock` (= `GetAvailableForCoreHaul()`, l'agrégat moins la réserve
du Noyau) ; les tests passaient `GetAvailableAggregate()` et mettaient tout le matériel dans cette
réserve. La validation acceptait donc sur un stock que la passe de réservation, elle, n'avait plus le
droit de réclamer : rien n'était réservé, les robots partaient vides, et **sept tests sont restés
rouges** le jour où la règle « la réserve ne compte pas pour une directive » a été posée.

**Le paramètre était le défaut.** Tant que l'appelant choisit le dictionnaire, un appelant peut choisir
le mauvais — et c'est arrivé. `CoreDirectiveSystem` détient déjà `ConstructionSiteSystem` ; il lit donc
maintenant `GetAvailableForCoreHaul()` lui-même. Décider et réserver lisent la même vue par
construction, plus par discipline d'appel. Le commentaire de `GetAvailableForCoreHaul` prévoyait
exactement ce désaccord — « les deux doivent exclure à l'identique » — dans l'autre sens.

**Et la règle n'avait aucune couverture verte.** C'est le vrai coût : la suite affirmait le contraire
de la règle et échouait plus loin, pour une raison que personne ne lisait. Un rouge qu'on garde devient
un rouge qu'on ignore. `TheCoreReserve_CannotSatisfyADirective` et
`TheHaul_DrainsTheBox_AndLeavesTheCoreReserveAlone` la tiennent maintenant des deux côtés — la décision
et la réservation étant deux passes, c'est leur désaccord qu'il faut épingler, pas seulement chacune.

Mesuré des deux côtés plutôt que supposé : `git stash`, suite complète sur `7713de9` propre —
**665 verts, 7 rouges**, exactement les mêmes. Après correction : **694 verts, 0 rouge.**

## 8. Un rappel coûteux sur la vérification

La suite a affiché **653 verts pendant que `Game.Presentation` ne compilait pas** : un `using`
manquant sur `Game.Gameplay.Missions`, et les tests tournaient contre l'assembly précédente. Le
symptôme visible était ailleurs — un `SerializedObject.FindProperty` rendant `null` sur un champ
pourtant présent sur le disque.

`Unity_RunCommand` compile son propre extrait, pas le projet : son `isCompilationSuccessful` ne dit
rien de l'état des assemblies du jeu. **Seule la console le dit.** Le vert d'une suite qui n'a pas
recompilé ne vaut rien, et c'est la deuxième fois que ce piège se referme.

## 9. Les zones d'expédition — ce que le mot « zone » a coûté, et ce que la surface a révélé

**Le mot.** `MAP.md` interdit déjà « zone » tout court : il désigne les territoires de signal du Noyau
et des Agents IA. Trois découpages cohabitent maintenant — le secteur qu'une mission vise, la zone de
signal, et la tranche qu'une partie explore. Le type s'appelle donc `ExpeditionZone`, jamais `Zone`,
et il n'existe nulle part de champ ni de variable locale nommée `zone` seule dans ce sens. Inventer un
mot neuf (« sextant » a été envisagé) aurait été pire : il aurait figé « six » dans un nom alors que le
nombre de zones est un réglage.

**La mesure qui contredit l'intuition, et qui a décidé de la borne intérieure.** La directive dit
qu'une zone commence au rayon d'action **courant**. Lu littéralement à chaque instant, cela fait
reculer la cartographie : quand la recherche élargit le Noyau, les cellules qui sortent de la zone sont
celles qui la touchent, donc les plus certainement découvertes. Retirer `k` cellules toutes découvertes
des deux membres donne `(D−k)/(T−k) ≤ D/T` dès que `D ≤ T` — la barre descend en récompense d'un
progrès. Et les sites placés près du bord intérieur sortiraient de leur propre zone.

La borne intérieure est donc **figée à la pose des zones** et voyage dans la sauvegarde. C'est un écart
assumé de lecture, pas une correction : les zones sont posées une fois, et « le rayon courant » décrit
cette pose. `RestoringIntoAWiderCore_KeepsTheZonesTheRunWasMapping` l'épingle.

C'est aussi le **second** piège de la même famille que celui que la directive nomme. Elle prévient
contre un décompte de sites qui recule quand une étude de terrain en ajoute ; celui-ci recule sans que
personne n'ajoute rien. Sur un ensemble de cellules fixe et une découverte qui n'enlève jamais rien,
la monotonie cesse d'être une propriété à surveiller.

**L'exploration lointaine qui ne porte pas le Noyau secondaire porte la trace de civilisation** — donc
le site d'étude de civilisation de la zone. Choisi parmi les trois options de §2 parce que c'est la
seule qui ne crée aucun contenu nouveau : le site existe déjà dans la zone, l'exploration est
simplement ce qui le met sur la carte. Les deux reconnaissances rapportent alors la même *sorte* de
chose sans rapporter la même chose, ce dont « non redondant » a réellement besoin.

**Un site est placé là où sa propre mission a le droit d'aller.** Les explorations lointaines au-delà
du seuil, tout le reste en deçà — c'est-à-dire exactement les deux bandes de `SectorMissionRange`. La
cohérence n'est pas décorative : elle rend impossible une quête que le système de lancement refuserait.

**La gigue ne peut pas sortir un site de sa zone**, parce que l'échelle régulière est posée sur
l'ouverture *moins* la gigue des deux côtés. Un clamp après coup aurait empilé les sites sur une
frontière et masqué le fait que les réglages avaient dépassé la géométrie.

**Un défaut trouvé en l'écrivant, et qu'aucun test de déterminisme n'aurait vu.** La gigue tirait sur
`(graine, canal, sel)` sans l'index de la zone : les six zones recevaient la même gigue pour le même
type et le même rang, donc étaient six rotations exactes l'une de l'autre. Chaque zone paraissait
variée vue de l'intérieur, et le monde était un pochoir vu d'en haut. `TheSixZones_AreNotRotationsOfOneAnother`
existe pour ça — le déterminisme et la variété sont deux propriétés distinctes, et tester la première
ne dit rien de la seconde.

### La première zone choisie, et pourquoi ce n'est pas une tranche

**« La zone de départ » n'existe pas comme lieu.** Le joueur choisit sa direction parmi six et §1 les
déclare équivalentes pendant l'introduction : composer une tranche à la main aurait contredit §1, et en
composer six aurait été six fois le travail pour cinq qu'on ne verra jamais. C'est donc **le choix** qui
compose, pas la géométrie — `IsComposed(zone)` répond « c'est celle qu'on a choisie », et il n'existe
nulle part de test « est-ce la zone 0 ».

C'est la forme de la règle déjà en place pour les secteurs (`MAP.md` §6) : une règle sur la donnée, pas
sur la géométrie, donc aucun périmètre de départ à entretenir.

**Ce qui est posé, c'est le compte, pas les coordonnées.** Une position ne peut pas être écrite à
l'avance pour une tranche que personne n'a encore choisie. Le placement, les trouvailles et les types du
stock caché restent dérivés de la graine et de l'index — deux parties qui choisissent la même direction
obtiennent la même carte. C'est un écart de forme avec la règle des secteurs, où le contenu posé est un
gisement à une cellule précise, et il vaut d'être noté plutôt que d'être présenté comme la même chose.

**Le piège d'ordonnancement, et sa forme ici.** Le contenu d'une zone est dérivé à la première demande
puis conservé. Un écran qui présente les six avant le choix aurait donc figé le contenu dérivé, et la
composition serait arrivée trop tard pour être vue — le contenu posé écrasé par la dérivation, exactement
le danger que `MAP.md` §6 nomme pour les secteurs, dans son autre sens. `Choose` jette ce que la zone
avait dérivé, ce qui rend l'ordre des deux sans importance au lieu d'en faire une étape à respecter.
Rien n'est perdu : aucune mission ne part avant qu'une zone soit choisie, donc aucun site ne porte
encore d'état — et un test l'énonce plutôt que de le supposer.

**Rien n'entre en sauvegarde**, et c'est le bon résultat plutôt qu'une économie. La composition est une
fonction de la zone choisie — déjà sauvegardée — et d'un asset. `SaveFormatTests` n'avait donc pas à
tomber. Ce qui est épinglé à la place est plus fort : un rechargement qui redemande les six zones *avant*
que la sauvegarde ait dit laquelle est choisie retrouve quand même la zone composée, sites et positions
identiques.

**Deux chiffres que §7 ne donne pas** et que j'ai dû poser : les études de civilisation de la première
zone (§7 dit « 6 nécessitant des unités » sans les répartir par type, et un seul de ces types existe) et
son stock caché (§7 n'en parle pas). Ils sont dans les réglages comme le reste, signalés comme non lus
sur le tableau.

**Mesuré sur l'asset livré** : 1 prospection, 2 explorations lointaines, 3 récupérations, 2 études
verrouillées, 4 sites cachés. **Six quêtes lançables** au lieu des neuf de §7, puisque les trois études
de terrain n'existent pas encore. Contre 20 charges (2 × 10), le solo en dépense 6 et le tout-en-binôme
12 : la tension que §7 décrit — « la marge est nulle par construction » — **n'existe simplement pas**,
et aucune valeur de charge ne la ferait apparaître. Le chiffrage attend l'étude de terrain, pas un
réglage.

### L'étude de terrain, et le mot « reconnaissance » qui désignait déjà autre chose

**Rien de neuf n'a été conçu**, et c'était la consigne : un membre d'énumération, ses valeurs, et
l'appel à `RevealNextHiddenSite` — construit, testé, documenté, et appelé par rien. **Cinquième
occurrence** de cette forme dans le projet.

**Un conflit de nom que le code a rendu visible.** `MissionSettings.reconnaissanceReward` existait déjà
et paie les *deux* reconnaissances (prospection et exploration lointaine), au titre du gisement fini de
§8. Ajouter `reconnaissanceSeconds` à côté aurait fait deux champs voisins dont l'un ne concerne pas le
type qu'il nomme. Les réglages du nouveau type s'appellent donc `fieldStudy*` — l'autre nom que la
conception lui donne — pendant que le membre d'énumération reste `Reconnaissance` comme demandé.
Le désaccord vient des documents, pas du code : §3 de la directive des zones appelle le type
« reconnaissance — appelée ici étude de terrain », et `SPEC_EXPEDITIONS` §8 appelle « reconnaissances »
les deux autres.

**Elle ne tire sur aucun des deux budgets**, et ce n'est pas une fontaine pour autant : ce qui la borne
est le nombre de sites d'étude qu'une zone contient, fini comme tous les autres. Un budget de paiements
en plus aurait été un second mécanisme pour la même propriété.

**Le tirage du site caché est fait au lancement et porté par la mission**, comme l'issue et la
récompense. Mais *savoir s'il reste quelque chose à trouver* est demandé à l'atterrissage : une autre
étude peut avoir vidé le stock entre-temps, et une étude qui trouve un stock épuisé ne trouve que du
terrain — ce que la conception dit déjà.

**Un défaut dans mes propres tests, pas dans le code.** Un seul gros `Tick` ne fait pas atterrir une
mission : `MissionRuntime.Advance` avance d'un cran par appel, donc un `Tick` de la durée totale laisse
la mission en `Résolution`. Le helper `RunToReport` existait déjà. Deux tests ont menti dans le bon sens
— ils ont échoué — mais un test qui aurait *affirmé* qu'il ne se passe rien serait passé au vert pour la
mauvaise raison.

### Une assertion négative passe aussi quand le mécanisme n'a pas tourné

Troisième occurrence de la même famille — après le test dont le nom sur-promettait, et celui qui
n'assertait rien de réel. Le motif : **une assertion négative est toujours suspecte**, parce qu'elle est
satisfaite par deux mondes différents — celui où le mécanisme a tourné et n'a rien changé, et celui où
il n'a pas tourné du tout. Le vert ne les distingue pas.

Ici, `AFieldStudyThatDrewNothing_LeavesTheStockAlone` affirme que le stock ne bouge pas. Avec un `Tick`
qui n'atterrissait pas, il serait passé — en vérifiant que rien ne se passe quand rien ne se passe. Ce
sont ses deux voisins, qui affirment un changement, qui ont échoué et l'ont dénoncé.

**La parade n'est pas de supprimer l'assertion négative** : « le stock ne bouge pas » est exactement ce
qu'il faut vérifier. C'est de la doubler d'une positive dans le même test, ou de vérifier d'abord que le
mécanisme a bien tourné — ici, que la mission a atteint `Rapport`. Sans ça, le test dit « je n'ai rien
observé », pas « il ne s'est rien passé ».

Candidat pour `DEVELOPMENT_RULES.md` §7, où vivent déjà les deux autres membres de la famille. Laissé au
carnet le temps qu'une quatrième occurrence dise si la formulation tient.

**L'index des sites a bougé**, `Reconnaissance` s'insérant après `Prospection` dans la liste dérivée.
`ExpeditionZoneSystem.CaptureState` référence les sites par index, donc une sauvegarde antérieure
appliquerait son état aux mauvais sites. Sans effet réel : rien ne pouvait écrire ce tableau
jusqu'ici — `RevealNextHiddenSite` n'avait pas d'appelant et `Consume` n'en a toujours pas — donc il est
vide dans toute sauvegarde existante. C'était le dernier moment où ce déplacement était gratuit.

**Mesuré sur les assets livrés** : la première zone offre 1 prospection, 3 études de terrain, 2
explorations lointaines et 3 récupérations — **neuf quêtes lançables**, ce que §7 annonçait. Contre 20
charges, le solo en dépense 9 et le binôme 18 : l'arbitrage existe enfin, avec deux charges de marge.

### Le terrain de la carte : pourquoi la teinte de l'inconnu a été levée

**Une demande explicite, satisfaite puis retirée — et il faut que la levée soit écrite là où la demande
se lit.** `SectorMapImage` peignait l'inconnu en `(30,36,44)`, une teinte au-dessus du fond du panneau,
parce qu'un secteur invisible ne pouvait pas être visé. C'était une réponse juste à un vrai problème.

La conception l'a résolu autrement depuis : **on ne vise plus un secteur nu, on vise un site.** Les
sites sont dessinés par-dessus, donc cliquables même sur du noir ; et l'étude de terrain elle-même, seul
type qui aurait pu demander un clic libre sur du terrain vide, est devenue un site posé. Le besoin qui
justifiait la teinte a disparu, et le noir redevient l'absence.

Quelqu'un qui lisait l'ancien commentaire y trouvait la demande sans savoir qu'elle avait été levée.
C'est le même principe que le conflit « reconnaissance » : **une décision se documente à l'endroit où
quelqu'un serait tenté de la défaire**, pas seulement là où elle a été prise.

**Le texel par secteur était la bonne réponse à la mauvaise question.** « Comment couvrir 10 000 cases »
donne 400 Mo par case contre 1,5 Mo par secteur ; le secteur gagne à l'arithmétique. Mais un secteur
fait 16 cases et une mission révèle un disque de rayon 8 : la révélation était **plus petite que le
texel où on la peignait**, donc `DiscoveryOf` rendait `Partial` et le carré entier prenait une couleur
unie. La carte se lisait comme une grille de blocs — précisément ce que la directive veut supprimer.

Retirer les traits de grille sans toucher à l'image aurait laissé les blocs **privés de ce qui les
expliquait** : un écran pire que l'actuel. C'est ce raisonnement qui a fait grossir le commit plutôt que
de le livrer conforme au périmètre et inutile.

Le bon découpage était celui utilisé partout ailleurs : le chunk, créé à la première écriture, exactement
comme l'état de découverte. Mesuré en jeu : **4 tuiles, 64 Ko**, pour le disque du Noyau à cheval sur
quatre chunks.

### La trace de trajet : il n'y avait pas de couche à dessiner

**J'ai signalé « aucune source » alors que le problème était l'inverse.** §8 dit que le trajet
**révèle** une bande. C'est de la découverte : les tuiles de terrain la dessinent déjà, il n'y a pas de
couche de rendu, rien à stocker, rien de plus en sauvegarde. Le travail se réduisait à révéler la bande
en plus du disque à l'amarrage.

Le point qui manquait était l'origine, et c'était le Noyau — ce qui explique aussi la forme : les traces
convergent en étoile parce qu'elles partent toutes du même endroit.

**Une contradiction dans §8, tranchée.** Elle listait l'index de mission parmi les entrées de la
dérivation, puis énonçait que deux missions vers la même cible suivent le même chemin. Les deux ne
peuvent pas tenir. La graine et la cible suffisent à la reproductibilité ; l'index la détruisait sans
rien apporter. §8 est corrigée pour que la contradiction ne survive pas à sa résolution.

**Et `RevealDisc` en pas le long de la courbe plutôt qu'une forme de bande** — la règle de §6 : une
mission demande la forme, elle ne la réimplémente pas.

**Une conséquence que la couture a fait apparaître, et que seuls les tests ont vue.** Une reconnaissance
ne vise que du vierge ; une trace ouvre du sol en chemin. Quatre tests sont passés au rouge parce qu'ils
comptaient sur un ensemble de cibles figé — dont un qui prenait des secteurs à offset croissant et
tombait sur du sol qu'un trajet précédent avait ouvert.

**Mesuré** : une mission vers une cible à 144 cases ouvre 909 cases et retire **huit** secteurs des 288
visables. C'est le « revenir ne révèle presque rien » vu de l'autre côté. Ce n'est pas un défaut, mais
c'est un coût que rien n'annonçait, et il se paie surtout près du Noyau, que **toutes** les traces
traversent.

**Et ce que ça produit vaut mieux que ce que ça coûte.** La zone se ferme **depuis l'intérieur**, comme
la bande minière se ferme quand le rayon grandit : le joueur ne perd pas des destinations au hasard, il
perd les plus proches, donc chaque mission repousse mécaniquement la suivante vers le lointain. Une
exploration qui s'éloigne à mesure qu'on explore est une progression naturelle, et elle donne un sens à
la durée qui croît avec la distance.

**Personne ne l'a décidé** : c'est la rencontre de deux règles écrites séparément — la trace révèle en
chemin, une reconnaissance ne vise que du vierge. Ça se juge en jouant, pas sur le papier.

**Le levier, si ça gêne, est la règle « vierge » de `SectorMissionRange`, pas la trace.** Une cible
partiellement découverte a encore du terrain à révéler et pourrait rester visable. Noté ici plutôt que
tranché : la mesure viendra de l'introduction jouée.

### Le test du pochoir est faible, et voici par où le renforcer

Écrit **avant** d'avoir la mesure, délibérément : plus tard, cette note deviendrait la justification
d'un chiffre déjà choisi. Elle dit quoi mesurer et pourquoi ce n'est pas encore mesuré.

**La projection démasque la rotation, pas la statistique.** Le test compare les gisements *relatifs au
centre de chaque zone* (`Mathf.DeltaAngle(zone * SliceDegrees, ...)`). En coordonnées monde, six
rotations auraient l'air de six choses différentes et rien n'aurait été visible. Le cadre de mesure est
donc déjà le bon ; c'est la statistique qui est faible. **À ne pas confondre en renforçant** : quelqu'un
qui durcirait la projection travaillerait sur la moitié qui va déjà bien.

**Ce qui trahit un pochoir n'est pas que deux zones soient proches, c'est qu'un motif de similarité se
répète.** Sur des zones tirées indépendamment, la distribution des écarts entre paires est étalée ; sur
des rotations, elle est concentrée. C'est cette forme-là qu'il faut regarder, et elle ne dépend pas du
nombre de zones — contrairement à un seuil de proximité, qu'il faudrait rejuger à chaque changement de
réglage.

**Le repère ne supprime pas le seuil, il dit où le poser.** Le pochoir complet donne des écarts
*exactement* nuls, donc il se teste sans nombre inventé — c'est le cas qui s'est produit, et le test
actuel l'attrape. Le pochoir **partiel** non : six familles de trois donneraient deux valeurs au lieu
d'un étalement, « pas tous identiques » passerait, et couper entre « étalé » et « concentré » sur
quinze paires redemande un chiffre.

Ce chiffre viendra de la vue d'ensemble de la carte, où l'on aura vu à quoi ressemble un étalement
normal. Le durcir maintenant produirait une valeur inventée qui contraint le code sans rien décrire.
C'est aussi là que le défaut se serait vu : un motif régulier sur la carte d'ensemble, dont on aurait
cherché la cause dans le rendu.

**Rien ne se lance avant qu'une zone soit choisie**, et c'est la règle du document, pas un effet de
bord. `MissionSystem.CanLaunch` appelle `MayTarget` avant les règles propres à chaque type, donc une
récupération est refusée hors zone comme une reconnaissance — vérifié séparément, parce qu'un garde
placé dans la branche d'un type passerait le test général en manquant celui-là. Conséquence à
connaître tant que l'écran de carte n'existe pas : le choix se fait par script.

---

## 10. L'écran de carte — trois défauts que seul l'écran pouvait révéler

### Un bouton reconstruit à chaque frame n'est pas cliquable

Aucune mission ne pouvait être lancée depuis la carte. Le panneau latéral se redessine à chaque frame,
et c'est voulu : un refus n'est pas une propriété de la cible — un créneau se libère, une charge se
dépense — donc un bouton actif au moment du choix ne doit pas le rester quand la raison a disparu. Mais
la liste était vidée sans condition, si bien que le bouton était **détruit entre l'enfoncement et le
relâchement**. Un `Button` d'UI Toolkit ne déclenche qu'au relâchement sur le même élément.

**Le correctif n'est pas de dessiner moins souvent, c'est de ne reconstruire que ce qui a changé.** Une
signature (secteur, refus par type, verrou en jeu) est comparée avant de vider — la garde que les
décomptes de sites utilisaient déjà, et que la liste des missions n'avait pas.

**Ce que ça dit du reste.** Aucun test EditMode ne pouvait l'attraper : la logique de lancement était
juste, la liste affichait les bonnes lignes, et le défaut vivait entièrement dans la durée de vie d'un
élément. C'est la troisième forme de couture rencontrée sur ce chantier — les deux premières étaient un
prédicat jamais appelé et un contrat écrit à l'envers du code. Celle-ci ne se voit qu'en cliquant.

### Le premier écran disait ne rien savoir, puis parlait

Il affichait « aucune donnée, les six directions se valent tant qu'aucun robot n'y est allé », et
listait dessous trois missions avec leurs durées et un refus fondé sur le risque — pour un secteur où
personne n'était allé. `directive-ecran-carte.md` §8 l'interdit explicitement. La contradiction ne
venait pas d'une négligence de texte : la méthode qui rend les missions d'un secteur était réutilisée
telle quelle pour une direction, et une direction n'est pas un secteur.

**Une seule action avant le choix** : la *découverte*. Le type n'est pas montré comme un choix —
nommer quatre types, c'est décrire un lieu que personne n'a vu.

**J'en avais d'abord fait une prospection sous un autre nom, et c'était ma décision, pas la sienne.**
Je l'avais annoncée comme telle, ce qui ne la rend pas moins prise à la place de quelqu'un d'autre : le
raccourci économisait un type d'énumération et coûtait la chose que la mission est censée être. Elle est
un type à part entière — `MissionKind.Decouverte`, une par zone, sans bande, deux minutes forfaitaires.
Vérifié avant de la construire : **aucun des quatre documents de conception ne nommait cette mission**,
donc il n'y avait rien à retrouver ; c'était une décision à prendre, et elle n'était pas la mienne.

**La durée forfaitaire n'est pas un raccourci.** Une découverte ne va jamais qu'au secteur d'entrée de
sa zone, et les six sont un même coin tourné six fois. Le terme de distance n'apporterait donc que
l'écart de quelques secondes produit par l'arrondi d'une case en secteur — mesuré : 3 min 15 à 3 min 19
selon la direction, pour une même intention. « Deux minutes » veut dire deux minutes dans les six.

**Ce qui ne change pas est aussi une décision.** La découverte tire sur le budget de reconnaissance de
l'introduction, exactement comme la prospection qu'elle remplaçait. Lui donner une récompense à elle
aurait déplacé l'économie de l'introduction alors que rien, dans ce changement, ne portait là-dessus.

### Choisir une direction n'est pas y aller

Le contenu d'une zone est dérivé à l'instant du choix, et il était **affiché** à cet instant : les
ronds apparaissaient avant que le premier robot ne parte, ce qui laissait la mission de découverte sans
rien à découvrir. La correction est une règle de jeu, pas d'affichage — `IsSurveyed`, posée par le
rapport de mission, quel que soit le type.

**Ce que la règle a ouvert, c'est un intervalle.** Entre le lancement et le rapport, l'écran n'avait
plus rien à dire : la carte de zone disparaissait au moment du choix, donc la chose qu'on venait de
lancer sortait de l'écran. Elle reste maintenant jusqu'au rapport, avec l'horloge de la mission comme
barre. **La barre est le trajet, pas la cartographie** : le sol s'ouvre à l'arrivée, donc une barre de
cartographie resterait à zéro la moitié du voyage puis sauterait — ce qui se lit comme un blocage.

**Une clé de sauvegarde qui ne se restaure pas à « rien n'a eu lieu ».** Une sauvegarde antérieure à
`surveyed` vient d'une version où le choix montrait les sites : la restaurer en « jamais visitée »
reprendrait ce que la partie avait déjà et redemanderait une exploration déjà faite. Absente, la zone
choisie compte comme reconnue. C'est la seule exception au défaut tolérant habituel, et elle est écrite
dans `CONTRACTS.md` §16 pour qu'elle ne passe pas pour un oubli.

---

## 11. Le panneau parlait de secteurs, la carte montrait des sites

Sept remarques d'une même séance, et six sortent de la même racine : **le découpage en secteurs avait
disparu de l'écran, mais pas du panneau.** Un clic sur un rond vert de récupération répondait par une
prospection et une étude de terrain — les missions que le *carré* sous la marque acceptait, pas celle du
site. Et le titre du panneau nommait ce carré : « Secteur non reconnu », ou pire, « Sillon de basalte
J10 » quand il était connu.

**Un nom de secteur n'est jamais une information pour le joueur.** Il désigne une case de 16 sur
laquelle il n'a pas cliqué, dont il ne peut rien faire, et qui ne correspond à aucun objet du jeu. Le
retirer du survol ne suffisait pas : il fallait le retirer de la cible. Ce que le pointeur trouve est un
site ; le site porte son type ; le type porte sa mission. La chaîne est directe et il n'y a plus de carré
dedans.

**La règle générale, écrite ici parce qu'elle a coûté trois passes :** une unité interne qui reste dans
le vocabulaire de l'interface finit par répondre à la place de l'unité que le joueur manipule.

## 12. Une découverte qui ne découvrait rien

La mission de découverte rapportait la liste des sites et les posait **dans le noir** : les ronds
apparaissaient sur du sol jamais révélé, et la zone restait visuellement intacte. La seule mission dont
le but est de montrer ce qu'il y a dehors ne montrait rien.

**Elle ouvre maintenant le sol autour de chaque site qu'elle rapporte** — un disque par site, du rayon
qu'ouvre déjà l'arrivée d'une mission, donc aucun chiffre nouveau.

**Pas la zone entière, et c'est un couplage à connaître :** la cartographie est mesurée en surface, et
le déverrouillage des cinq autres directions lit cette cartographie. Ouvrir tout le coin d'un coup
porterait la zone à 100 %, ce qui rouvrirait les cinq autres à la première mission — le verrou ne
tiendrait plus une minute. Les taches laissent entre elles ce que le reste de la partie a à faire.

---

## 13. Trois parties, le même monde

« Les points et les explorations lointaines sont exactement au même endroit. » Ce n'était pas le
placement : **`TerrainGenerationSettings.seed` valait 0, et une nouvelle partie le prenait tel quel.**
Toutes les parties neuves partageaient donc un seul monde — même terrain, mêmes secteurs, mêmes sites
aux mêmes coordonnées.

**Le correctif est à l'étage de la graine, pas à celui du tirage.** Ajouter de l'aléatoire dans le
placement aurait cassé la règle qui tient tout le projet — un site doit se redériver identique après un
rechargement. Une partie neuve tire maintenant sa graine une fois, et tout ce qui suit continue de
passer par `DeterministicHash`. C'est le seul tirage non déterministe du projet, il a lieu une fois, et
il part immédiatement dans la sauvegarde.

`randomiseSeedEachRun` peut être décoché pour rejouer un monde précis — ce qui est la seule façon de
retrouver un bug lié à une carte particulière.

## 14. Cinq échelles empilées sur la même diagonale

Les ronds se touchaient. La cause n'était pas leur taille : **chaque type posait sa propre échelle
régulière sur toute la zone, sans savoir que les quatre autres faisaient la même chose.** Et comme
l'angle *et* le rayon croissaient tous deux avec le rang, chaque échelle était une diagonale — cinq
diagonales superposées sur le même coin.

Deux corrections, aucune n'étant un « écart minimal » à faire respecter après coup :

- **une échelle par tranche, partagée par tous les types** : le rang d'un site est son rang parmi tous
  les sites de sa tranche, pas parmi ceux de son type ;
- **le cap vient du nombre d'or** : les multiples successifs de 1/φ modulo 1 sont aussi étalés qu'une
  suite peut l'être, donc les marques couvrent la surface du coin au lieu d'une corde. C'est de
  l'arithmétique, pas un tirage : la même zone se dessine identiquement à chaque fois.

Le jitter est en plus borné à la moitié de l'écart entre deux rangs, ce qui rend le chevauchement
impossible par construction plutôt que surveillé.

**Mesuré : la paire la plus proche passe de 1,0 à 10,0 cellules**, sur quatre graines. Le test tient le
seuil à 8 — sous la mesure, parce que ce qui est figé est la propriété, pas le chiffre exact d'un
arrangement donné.

---

## 15. Le plafond du rayon n'était pas celui qu'on croyait

Le seuil d'exploration se construisait sur `CoreRuntime.ExtendedActionRadiusCells` — **32**, ce que la
recherche `extended_bandwidth` accorde aujourd'hui. Ce n'est pas le chiffre dont le seuil a besoin : il
lui faut **jusqu'où un Noyau ira un jour**, parce qu'un Noyau secondaire posé au seuil doit pouvoir
grandir jusqu'à son propre maximum sans que les deux territoires se touchent. Ce plafond est 80.

**Le projet le savait déjà ailleurs.** Dans le même asset, `moderateRiskWithinCells: 250` et
`highRiskWithinCells: 330` étaient écrits pour un plafond de 80 — 250 est le seuil, 330 le bord d'une
zone. Seule la portée des missions était restée sur 32. Deux lectures du même monde cohabitaient dans un
fichier de quinze lignes, et c'est la question « à quelle distance sont les explorations lointaines ? »
qui les a mises face à face.

Les deux termes vivent maintenant ensemble sur `SectorSettings`, parce qu'ils forment une seule phrase :
à quelle distance deux territoires doivent se tenir.

**La tolérance a créé une seconde couture.** Le site d'un Noyau secondaire est posé sur le seuil à ±20
cases, donc entre 230 et 270 — mais la bande d'exploration lointaine s'ouvrait au seuil, à 250. Les
sites entre 230 et 250 étaient donc **inatteignables par leur propre mission**. La bande s'ouvre
maintenant à `BandBoundaryCells` = seuil − tolérance, et surtout : `ExpeditionZoneSystem` lit cette
tolérance **sur la portée**, pas sur ses propres réglages. Deux copies du même chiffre auraient dérivé,
et la première chose que produit cette dérive est un site que rien ne peut viser.

## 16. Trois échelles arithmétiques font un monde unique

Après avoir tiré une graine par partie, le joueur voyait toujours **exactement le même placement**. La
graine ne mentait pas : les échelles — le rang radial, le cap tiré du nombre d'or — sont de
l'arithmétique pure, et la graine n'atteignait plus que le jitter, trois cases sur une tranche profonde
de cent soixante-dix. Une correction de lisibilité avait supprimé la variété.

**Chaque échelle est maintenant tournée d'un décalage tiré par zone et par graine.** Un décalage
constant laisse une suite à faible discrépance exactement aussi bien étalée qu'elle l'était : la
variété ne coûte rien en séparation. Mesuré : un site de la zone 0 se déplace de **29 cases en moyenne**
d'une graine à l'autre, et la paire la plus proche reste au-dessus de 6.

**Et le jitter angulaire demandait la même borne que le radial.** Porter la tolérance à ±10° pour le
site du Noyau secondaire — qui dispose de deux rangs de 20° — donnait dix degrés aux quinze sites
proches, qui se partagent des rangs de moins de trois : la paire la plus proche retombait de 8,5 à 2,0
cases. Borné à une fraction de son propre rang, un site lointain garde ses ±10° et un site proche prend
ce que sa part permet.

## 17. Errer, prototype — pourquoi un cap et non une destination

Un prototype, posé à côté du système de missions et branché sur rien de lui : aucune charge dépensée,
aucun rapport, aucune zone choisie, aucun site. `ExplorerRobotSystem` (`Game.Gameplay.Exploration`),
état courant décrit dans `MAP.md` §2.1. Les valeurs sont posées pour que ça tourne.

**La destination était le piège, et elle était tentante.** Un robot avec une cible et un déplacement
rectiligne révèle un rayon. Trois sorties donnent trois traits partant du Noyau, et la carte se remplit
en étoile — exactement ce que les traînées de mission font déjà, en plus lent. Le robot n'a donc pas de
cible : il a un **cap**, que trois choses courbent en continu.

**Un bruit tiré à chaque frame ne fait rien du tout.** C'est le point non évident de la dérive. Des
tirages indépendants s'annulent sur une seconde : le robot tremble et avance droit. Il faut un bruit qui
**évolue** — une valeur échantillonnée sur une phase qui avance avec le temps, lissée par un smoothstep
pour que la *vitesse de rotation* soit continue elle aussi. Une interpolation linéaire mettrait un
angle dans la trajectoire à chaque phase entière, ce qui se lit comme un tressaillement une fois par
cycle.

**Un taux de rotation est un rayon de courbure, lu contre la vitesse.** À `v` cases/s et `w` degrés/s le
robot tourne sur un cercle de rayon `v / (w · π/180)`. C'est la chose à savoir avant de toucher aux trois
forces : à 2 et 6, c'est un arc de 19 cases, un méandre large ; à 30 degrés/s ce serait 3,8 cases, un
robot qui tourne sur lui-même près de la base. La dérive est petite pour cette raison, et pour aucune
autre.

**Le bord du monde devait repousser.** L'attirance vers l'inconnu lit deux sondes à ±45°, et hors carte
compte comme **découvert** : il n'y a rien à trouver là-bas, donc la bordure repousse comme du sol déjà
foulé. Lue comme inconnue, elle serait la chose la plus attirante de la carte et tous les robots
partiraient droit dessus.

**Le nombre d'or plutôt qu'un tirage, pour la même raison que les sites.** « Deux sorties successives ne
doivent pas se superposer » est ce qu'on regarde ; un tirage équitable est parfaitement libre de placer
deux caps à cinq degrés l'un de l'autre. Le pas de 1/φ donne 137,5° entre deux sorties consécutives.
Sur six sorties le minimum par paires descend nécessairement à 32° — le théorème des trois distances —
et c'est encore ample : le robot ouvre une bande de 12 cases de large, donc deux traces à 32° cessent de
se recouvrir à une vingtaine de cases de la base. Ce que le pas achète, c'est que ce plancher **existe**.

**Mesuré, parce que la forme de la trace est le livrable.** Sur 480 cases parcourues la trajectoire
s'écarte de 62,8 cases de la corde entre ses deux extrémités : ce n'est pas une règle. Sur 240 cases elle
finit à 179 du départ, soit 0,75 du chemin : elle ne tourne pas en rond. Partie de 360 cases, elle culmine
à 364 et revient à 254 en une minute : la limite est un virage, pas un mur. Ces trois chiffres sont dans
`ExplorerRobotSystemTests`, qui les imprime — un test qui surveille une forme doit rendre ses mesures
lisibles, sinon un passage vert ne dit rien de ce qui a été vérifié.

**Une note d'outillage.** Le pont d'automatisation de l'éditeur refuse désormais `TestRunnerApi.Execute`
comme appel interactif, donc la suite ne peut plus être lancée par là. `Assets/Editor/RunEditModeTests.cs`
la démarre depuis un fichier sentinelle (`Temp/run-tests`) au rechargement des scripts, et écrit son
rapport dans `Temp/test-report.txt` — un fichier plutôt qu'un log parce qu'une exécution traverse un
domain reload : celui qui l'a demandée n'est plus là pour lire la console.

## 18. Attraper le sol — la souris déplace le monde, le curseur suit

Glisser le monde au clic gauche maintenu : la souris déplace le sol dans son sens, donc la caméra dans
l'autre. `CameraPanController`, qui ne touche que la position — le contrôleur de zoom ne touche que
`orthographicSize`, et c'est ce partage qui leur permet de tourner ensemble sans se connaître.

**Le facteur d'échelle n'est pas un réglage.** Pour que le sol suive le curseur au pixel à n'importe quel
zoom, il vaut `2 × orthographicSize / Screen.height`. Et aucun `deltaTime` : c'est un déplacement que la
main a déjà fait, pas un débit. Le multiplier par un temps de frame ferait que le même geste déplace le
monde différemment selon le framerate.

**Le curseur suit la souris et reste visible.** Une première version le figeait — `CursorLockMode.Locked`,
caché, puis remis à sa position d'appui via `user32` parce que déverrouiller le gare au centre de la
fenêtre. Ça marchait, mais ce n'est pas ce qui était voulu : un curseur qui bouge est le geste habituel
d'une carte, et le contrat visible devient « le point du sol attrapé reste sous le curseur ». Tout
l'appareillage a disparu avec le verrou — le P/Invoke Windows, la mémorisation de la position d'appui, le
no-op multiplateforme.

**Et ce changement impose de lire la *position* du pointeur, pas le delta du périphérique.** Les deux ne
sont pas le même nombre : l'accélération du pointeur en met un à l'échelle et pas l'autre, et au bord de
l'écran la position cesse de changer alors que le périphérique continue de rapporter du mouvement. Avec le
delta brut, le sol glisserait sous le curseur au lieu d'y rester collé. Avec le curseur ancré, la question
ne se posait pas — c'est le genre de dépendance qu'un changement d'apparence révèle.

**Trois choses possèdent le bouton gauche avant la caméra**, et les oublier casse des gestes existants :
un élément d'UI sous le curseur possède ses propres clics ; un panneau global ouvert possède le clic
même en dehors de lui, puisque cliquer à côté est ce qui le ferme ; et un outil de construction armé
possède le glisser gauche entièrement — c'est le geste qui pose une ligne de convoyeurs. Un panneau
contextuel de bâtiment n'est délibérément pas dans la liste : il est ancré à droite et laisse voir le
monde, donc glisser ce qui reste visible est légitime.

**Un geste commencé va jusqu'à la relâche.** Les conditions d'autorisation ne sont pas revérifiées
pendant le glisser : un curseur qui passe au-dessus d'un panneau en chemin ne doit pas lâcher le sol en
pleine lancée.

## 19. Le glisser a cassé la sélection, et la réparation était de la décaler

**Le seuil de quelques pixels n'était pas du confort.** Sans lui, chaque clic déplacerait le monde d'un
pixel ou deux. Mais son vrai rôle est apparu après : la sélection était validée à **l'appui**, ce qui ne
coûtait rien tant que le bouton gauche ne servait qu'à sélectionner. Dès qu'il déplace le monde, chaque
glisser commencé sur une Fonderie, une Usine ou un coffre ouvrait aussi son panneau. Une fonctionnalité
qui casse un comportement voisin le répare dans la foulée.

Le clic se décide donc **au relâchement**, et un geste qui a voyagé est un glisser, pas un clic. Le seuil
qui séparait déjà les deux sert exactement à ça.

**Le seuil est lu, jamais recopié.** `BuildingSelectionInput` demande au contrôleur de caméra son
`DragSlopPixels` en même temps que le trajet parcouru. Deux compteurs d'appui avec deux copies du même
nombre auraient divergé le jour où l'un des deux bouge — et surtout : demander un total **conservé
jusqu'à l'appui suivant** rend la réponse indépendante de l'ordre dans lequel Unity exécute les deux
composants sur la frame de relâchement. Réinitialiser le total à la relâche aurait introduit un bug
d'ordre invisible une fois sur deux.

**Un détail qui aurait mordu :** le total doit être remis à zéro à **chaque** appui, y compris ceux que
la caméra refuse (sur l'UI, ou avec un outil armé). Ne le remettre à zéro que dans la branche des appuis
acceptés, et un appui refusé hérite du trajet du dernier vrai glisser — après quoi le routeur, le
lisant, avale un clic qui n'a pas bougé d'un pixel.

**Une nuance laissée telle quelle :** un appui qui commence sur un panneau et se relâche sur le monde
route maintenant vers le monde. Rare, et il faut le faire exprès.

## 20. Payer le terrain neuf, et rien d'autre

Le robot ramasse des datacards en errant, dépensées instantanément en CU au retour. Prototype, suite du
§17 ; les valeurs sont posées pour tourner.

**La règle est la fréquence comptée en terrain neuf**, jamais en temps ni en distance : payer au temps
paierait l'immobilité, et un robot qui tourne dans ce qu'il a déjà ouvert doit rapporter zéro.

**Et cette règle était déjà gratuite.** `DiscoveryRuntime.RevealDisc` retourne depuis toujours le nombre
de cases qu'il a réellement changées — c'est exactement le chiffre sur lequel la récolte se paie. Aucun
parcours supplémentaire, aucun compteur parallèle : le nombre est un sous-produit de la révélation. Un
champ « déjà récolté » par case aurait été une seconde source de vérité, et il aurait fallu le nettoyer.

**Le seuil de chaque carte est tiré à part**, ±30 % autour de 2 500. Sans ça la carte tombe à intervalle
exact et le joueur lit un métronome au lieu d'une trouvaille. Mesuré sur quarante cartes : de 1 818 à
3 227.

**Tiré de l'ordinal de la carte, pas de l'horloge.** `CardsDrawnEver` voyage donc dans la sauvegarde :
sans lui, un rechargement re-tire le seuil vers lequel le robot était déjà à mi-chemin. Même raison que
la phase de dérive au §17, et même passage par `DeterministicHash`.

**Au plafond, le robot arrête de récolter *et* d'accumuler.** Continuer à banquer le terrain ouvert
pendant qu'il ne peut plus rien porter le paierait pour un travail qu'il n'a pas pu faire. Il continue
d'errer — être plein n'est pas une raison de rentrer, c'est au joueur de décider.

**La ligne la plus importante du panneau n'est pas le compteur, c'est « Récolte ».** Un robot plein et un
robot qui repasse sur sa propre trace rapportent tous les deux zéro, mais un seul mérite d'être rappelé.
D'où un état à quatre valeurs plutôt qu'un booléen, et aucun chiffre de rendement : un débit n'est pas
une décision. La détection du terrain connu a une hystérésis de quatre révélations stériles — une seule
arrive constamment au bord d'une trace, et la ligne clignoterait alors que le robot travaille visiblement.

**L'alerte mène à l'action, et c'est ce qui a dicté qui la poste.** Cliquer la notification recentre la
caméra sur le robot et ouvre son panneau, prêt pour le rappel. Le système de robots ne sait pas ce qu'est
une caméra ni une sélection : il lève un événement, et `GameRuntime` poste la notification en refermant
sur les deux. `Notification` a donc gagné une `Action` optionnelle — un délégué fourni par le posteur,
ce qui laisse la couche gameplay entièrement à l'écart de la présentation.

**Seule une ligne actionnable prend le pointeur.** La bannière promettait de ne jamais bloquer
l'interaction, racine en `PickingMode.Ignore` ; cette promesse tient toujours, parce que seules les
lignes qui mènent quelque part deviennent pickables. Une teinte les distingue, sinon la moitié des
lignes auraient l'air cliquables sans l'être.

**Une fois par remplissage, jamais deux.** Le drapeau se réarme quand le robot se vide, pas quand il
descend sous le plafond. Répéter l'alerte serait du harcèlement pour une décision que le joueur a déjà
prise en l'ignorant — et un robot plein rechargé ne s'annonce pas de nouveau, puisque le drapeau voyage.

**Un écart connu et non corrigé :** `NotificationSystem.Active` alloue une liste à chaque appel, et la
bannière l'appelle chaque frame. C'est antérieur à ce prototype et hors de son périmètre ; la récolte
elle-même n'alloue rien.

### Le journal de mesure — À RETIRER

`ExplorerHarvestLog` est un **instrument, pas une fonctionnalité**, et il est fait pour être supprimé.
Il répond à une seule question : le seuil de 2 500 suppose du terrain vierge à chaque pas, ce qui
n'arrive qu'en pleine frontière — l'attirance vers l'inconnu incline le cap sans l'obliger, et le rappel
à 330 fait longer une frontière déjà ouverte. Le rendement réel est inconnu.

Un CSV à côté de la sauvegarde (`BUILD.md` §6), une ligne par minute, derrière un interrupteur désactivé
par défaut. La colonne de distance ne compte que l'exploration : le trajet de retour ne révèle rien par
construction, donc l'inclure ferait dépendre le rendement de la fréquence à laquelle le joueur rappelle.

**Pour le retirer :** supprimer `ExplorerHarvestLog.cs`, le champ `logHarvestMeasurements` sur
`ExplorerRobotSettings`, et les quatre lignes qui l'alimentent dans `ExplorerRobotSystem` plus sa
construction dans `GameRuntime`. Il n'est branché à rien d'autre et documenté nulle part ailleurs.

### Une leçon d'outillage, payée deux fois

Mon script de compilation hors ligne couvre les huit assemblies du jeu et **pas** `Game.Tests.EditMode` —
le NUnit livré par Unity est un build net472 qui référence `mscorlib`, inconciliable avec la façade
netstandard contre laquelle le reste compile. « OK ×8 » ne dit donc rien des tests, et j'ai lu ça comme
une vérification. Le symptôme est trompeur : l'assembly de tests ne compilant pas, Unity ne recharge pas,
donc le hook `[InitializeOnLoadMethod]` ne part pas, donc la sentinelle reste en place et le rapport
n'arrive jamais — ça ressemble à de la lenteur, c'est une erreur de compilation.

La bonne boucle est : tout écrire, **puis** `AssetDatabase.Refresh`, **puis lire la console** avec
`Unity_GetConsoleLogs` — qui rend les erreurs immédiatement — et seulement ensuite sonder le rapport. Le
rafraîchissement lancé avant la fin des éditions ne sert à rien, et le sondage à l'aveugle transforme une
erreur de compilation en trois minutes d'attente. La raison est maintenant écrite dans le script lui-même.

## 21. Les missions supprimées — et le trou qu'elles laissaient

`MissionSystem`, `ExpeditionZoneSystem`, `SectorMissionRange`, les cinq `MissionKind`, les six zones de
60°, tous les sites, l'écran de désignation et **93 tests** sont supprimés. Ce qui reste est l'errance :
des robots qu'on envoie vagabonder, qui ouvrent le sol, ramassent des datacards et matérialisent les
gisements qu'ils croisent. L'état courant est dans `MAP.md`.

**Le trou n'était pas dans les missions, il était dans les gisements.** C'est la seule question qui
valait d'être posée avant de couper : `SectorMaterialisation` transforme les gisements *dérivés* d'un
secteur en gisements réels **quand quelque chose rapporte sur ce secteur**, et l'unique appelant était
`MissionSystem.Deliver`. Supprimer les missions sans déplacer cet appel aurait donné un monde où plus
aucun gisement hors zone de départ ne devient réel, quelle que soit la distance explorée — le robot
aurait ouvert une carte vide, et rien n'aurait échoué bruyamment. Une suppression se juge à ce qu'elle
débranche, pas à ce qu'elle enlève.

**Ce sont donc les robots qui trouvent les gisements**, et ça a demandé de choisir *quand*. Une
matérialisation à chaque révélation coûte ~256 lectures de grille par secteur, deux fois par seconde et
par robot. Elle se déclenche donc quand le robot **change de secteur**, et elle matérialise le **bloc
3×3** autour de lui — le bloc, parce qu'un disque de révélation de 12 cases chevauche jusqu'à quatre
secteurs de 16 : ne matérialiser que celui du dessous laisserait du minerai manquant sur du sol que le
robot a manifestement découvert. Un bloc de 48 cases de côté couvre tout ce que le disque peut toucher
pendant que le robot est dans la case centrale.

**Le retour révèle maintenant comme l'aller**, et l'ancienne règle était fausse pour une raison
géométrique que seul l'écran montre : l'aller serpente, le retour est une **ligne droite**. La droite
coupe donc à travers les vides entre les méandres, et on voyait le robot traverser du noir. « Le retour
repasse sur du sol déjà foulé » était vrai de l'intention et faux du tracé. Les deux jambes passent
maintenant par un seul point d'appel — deux copies auraient rediverge.

**La carte a perdu tout ce qui désignait quelque chose.** Plus de fil d'Ariane, plus de volet droit,
plus de survol, plus de sélection, plus de séparateurs, plus de ronds de sites. Il reste le terrain
révélé, la base, le Noyau, l'anneau des 330 et **les robots** — qui deviennent la raison d'ouvrir
l'écran, puisqu'un robot errant est quelque part que le joueur n'a pas choisi. Une conséquence agréable :
sans clic à interpréter, il n'y a plus de seuil ni d'arbitrage clic/glisser dans l'élément, le pointeur
ne fait que déplacer.

**Deux réglages sont devenus orphelins et sont partis avec.** `SectorSettings.maxCoreRadiusCells` (80)
et `territorySpacingCells` (90) n'existaient que pour dériver le seuil d'exploration dans
`SectorMissionRange` — celui-là même qu'on avait corrigé au §15. Les laisser aurait été garder deux
chiffres que rien ne lit, dans le fichier qui se présente comme « le seul endroit où ces nombres
existent ». La seule portée qui reste est `ExplorerRobotSettings.maxRadiusCells`.

**L'ouverture de la carte a changé de source, pas de règle.** Le bouton CARTE apparaissait sur
`Missions.RobotsHaveAppeared` ; il apparaît sur `ExplorerRobots.RobotsHaveAppeared`, avec le même
déclencheur — la réserve de CU **retombée** à 25 000. Une chute et non une montée : l'introduction
consomme du CU, et la chose qui paie qui arrive quand le joueur s'assèche est une sortie, pas une
récompense.

**La notification a gagné une action, et c'est resté propre parce que le délégué appartient au
posteur.** Cliquer l'alerte de stock plein recentre la caméra et ouvre le panneau ; le système de robots
ne sait ni ce qu'est une caméra ni ce qu'est une sélection, donc il lève un événement et `GameRuntime`
referme sur les deux. Seules les lignes actionnables prennent le pointeur, donc la bannière tient
toujours sa promesse de ne rien bloquer.

### Une leçon d'outillage, payée une troisième fois

Une coupe par « chercher le membre, remonter au commentaire, couper jusqu'au suivant » a emporté
`SaveData.ExplorerRobots` **avec** `Missions` et `ExpeditionZones` : il était entre les deux et la
borne de fin. Le compilateur l'a dit tout de suite, mais la leçon tient — une suppression par bornes
textuelles doit énumérer ce qu'elle garde, pas seulement ce qu'elle vise.

Et le rappel du §20 vaut toujours : mon script hors ligne ne compile pas `Game.Tests.EditMode`, donc
« OK ×8 » ne dit rien des tests. La vérification passe par Unity — rafraîchir, puis **lire la console**.
