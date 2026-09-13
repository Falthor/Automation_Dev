# Construire hors du rayon d'action : la fausse piste du Relais de convoyeur

## La première version

La toute première implémentation de la construction hors rayon (CONSTRUCTION.md §8) plafonnait un
tapis droit/coin à 40 cases consécutives hors de tout rayon, remis à zéro par un second bâtiment, le
"Relais de convoyeur", qui ne pouvait lui-même être posé que dans le rayon d'un Relais de communication
déjà actif.

## Pourquoi elle a été retirée

Un Relais de communication est déjà la seule façon de construire quoi que ce soit hors de son propre
rayon initial - c'est son unique raison d'être. Ajouter une limite de longueur de tapis en plus rendait
le Relais de convoyeur un bâtiment que le joueur devait parfois poser seulement pour pouvoir poser un
autre bâtiment identique en intention (étendre le réseau) : la contrainte n'ajoutait aucune décision, elle
ajoutait un détour. Le vrai levier qui pousse le joueur à sortir du rayon du Noyau est l'emplacement du
minerai, pas la longueur du tapis qui y mène - voir la refonte de la génération de carte dans MAP.md.

Le research `belt_relay`, le bâtiment `BeltRelayDefinition`, `ConveyorDefinition.IsRunLengthReset`,
`ConstructionService.MaxOutOfRadiusConveyorRun` et le refus `ConveyorRunTooLong` ont tous été retirés
dans la foulée - rien de ce mécanisme ne survit.
