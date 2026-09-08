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
