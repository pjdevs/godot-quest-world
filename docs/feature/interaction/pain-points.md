# Interaction Pain Points

- LeverWall redefine state enum in code so it duplicates resource.
- A lot of LeverWall machinerie, check how it was done in Unreal to see if we become too heavy or just more complete.
- ~~AutoInteraction does not trigger after interaction become available.~~ Réglé en Task 7 : le retry tourne à chaque frame focusée, avec mémoire de la requête en cours pour ne rien spammer.
- To check in Unreal but everything was not evaluated every frame. Check if we want to reintroduce NotifyStatusChanged or if we are more flexible evaluating every frame when focused. Moreover, this should be relatively lightweight calculations. Le presenter ne dépend plus de ce
  push pour la fraîcheur du prompt : il rebind depuis son `_Process` depuis la Task 13, donc gater
  `InteractionStatusChanged` ne périmerait plus l'UI, seulement le retry des actions automatiques.
