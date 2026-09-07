# Expéditions — carnet

Carnet d'implémentation de [`Intro/SPEC_EXPEDITIONS.md`](Intro/SPEC_EXPEDITIONS.md), version amendée
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

## 8. Un rappel coûteux sur la vérification

La suite a affiché **653 verts pendant que `Game.Presentation` ne compilait pas** : un `using`
manquant sur `Game.Gameplay.Missions`, et les tests tournaient contre l'assembly précédente. Le
symptôme visible était ailleurs — un `SerializedObject.FindProperty` rendant `null` sur un champ
pourtant présent sur le disque.

`Unity_RunCommand` compile son propre extrait, pas le projet : son `isCompilationSuccessful` ne dit
rien de l'état des assemblies du jeu. **Seule la console le dit.** Le vert d'une suite qui n'a pas
recompilé ne vaut rien, et c'est la deuxième fois que ce piège se referme.
