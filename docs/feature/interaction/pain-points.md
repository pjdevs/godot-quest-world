# Interaction Pain Points

- LeverWall redefine state enum in code so it duplicates resource.
- A lot of LeverWall machinerie, check how it was done in Unreal to see if we become too heavy or just more complete.
- ~~AutoInteraction does not trigger after interaction become available.~~ Réglé en Task 7 : le retry tourne à chaque frame focusée, avec mémoire de la requête en cours pour ne rien spammer.
- ~~To check in Unreal but everything was not evaluated every frame. Check if we want to reintroduce NotifyStatusChanged or if we are more flexible evaluating every frame when focused.~~ Tranché en
  Task 13, dans le sens « ne pas pousser à chaque frame » : le presenter tire sa fraîcheur de son propre
  `_Process`, donc `InteractionStatusChanged` est redevenu un événement (focus qui bouge, entrée en
  détection, invalidation gameplay). Le retry des actions automatiques reste par frame, c'est un appel
  séparé et non le signal. Reste vrai que l'évaluation par frame doit rester légère : le vrai coût est
  `GetPresentation`, qui alloue une liste et réévalue toutes les rules par cible présentée.
