# Interaction

## Purpose

Addon Godot C# réutilisable pour les interactions offline et multiplayer high-level : sélection d’une cible, conditions data-driven, interaction instantanée ou longue, état autoritaire répliqué et présentation locale remplaçable.

La spec complète reste dans [`docs/superpowers/specs/2026-08-14-interaction-addon-design.md`](../superpowers/specs/2026-08-14-interaction-addon-design.md). Le code runtime et ses contrats sont dans le namespace `QuestWorld.Interaction`.

## Delivered V1

L’addon autonome est sous [`addons/interaction_plugin`](../../addons/interaction_plugin) et ne crée ni autoload ni action Input Map.

- `InteractionInteractor` maintient les candidats d’indication/de portée, calcule le score regard-distance, émet les signaux de focus/statut/requête/refus et expose `TryStartInteractionInput()` / `TryEndInteractionInput()`. Le nœud reste sous autorité réseau serveur ; `OwnerPeerId` identifie uniquement le client qui calcule le focus, présente l’UI et envoie les intentions.
- `InteractiveComponent` compose un `Area3D`, une ancre `Marker3D`, des métadonnées de présentation, des scènes de widgets et une liste ordonnée de `InteractionRule`.
- `InteractionStateful` expose `Idle`, `Activating`, `Activated`, `Deactivating`, réserve l’interacteur actif, applique les callbacks d’autorité/présentation et expose `SaveState()` / `LoadState()`. Seul `Idle` est interactif. La fin d’une phase libère la réservation sans simuler un relâchement d’input ; le callback de fin d’input reste réservé au relâchement, à la sortie de zone ou à la disparition de l’interacteur.
- Les RPC fiables résident dans l’interacteur. Le client prévalide le statut avant d’émettre une intention. Le serveur vérifie ensuite l’identité du peer, le chemin reçu, l’appartenance aux candidats serveur, distance/angle, état, règles et hook custom avant d’appeler le handler. Les refus serveur vers client partent d’un nœud dont l’autorité réseau reste au serveur.
- `InteractionRule` fournit `AlwaysBlockedInteractionRule` et `InteractorGroupInteractionRule`. Le pipeline s’arrête à la première raison bloquante avant le hook custom.
- `InteractionPresenter`, `InteractionPromptWidget` et `InteractionIndicatorWidget` constituent la présentation facultative screen-space. Le prompt reste unique et centré sur l’ancre de la cible focusée ; un widget d’indication est présenté pour chaque interactable présent dans sa `IndicationArea`, sauf la cible focusée. Un widget projet peut implémenter `IInteractionWidget`.
- `scenes/InteractiveActor.tscn` et `examples/InteractionDemo.tscn` fournissent un objet à activation longue avec réservation, transition vers `Activated`, synchroniseur d’état et widgets de démonstration.

## Integration

1. Pour le Character du projet, `quest_world/character/Character.tscn` dérive de `addons/dummy_character_plugin/Character.tscn` et ajoute `InteractionInteractor` (distance calculée depuis le player propriétaire, direction calculée depuis la caméra) ainsi que `InteractionPresenter`. Le script global `quest_world/character/Character.cs` échantillonne l'action `interact` (`E` par défaut) et appelle les deux points d'entrée de l'interactor.
2. Pour un personnage custom, ajouter `InteractionInteractor` au personnage local et assigner `ViewOriginPath` vers un `Marker3D` ou une caméra, puis appeler `TryStartInteractionInput()` / `TryEndInteractionInput()` depuis son contrôleur d'input.
3. Ajouter `InteractionArea`, `InteractiveComponent` et `InteractionStateful` au même propriétaire Node3D ; les chemins explicites peuvent être configurés dans l'inspecteur.
4. Implémenter `IInteractionHandler` sur le propriétaire. Pour une phase longue, appeler `Stateful.StartInteractionPhase(context.Interactor)` synchroniquement dans `OnStartInteractionInput`, puis `EndInteractionPhase(nextState)` quand l'opération métier se termine.
5. Ajouter `InteractionPresenter` seulement si une UI est souhaitée, avec `InteractorPath` et `CameraPath`. L'absence de scène de widget est valide.

## Base scene

[`scenes/InteractiveActor.tscn`](../../addons/interaction_plugin/scenes/InteractiveActor.tscn) est le prefab de départ duplicable : zones d'interaction et d'indication, ancre, composant, état répliqué et widgets par défaut. Son script d'exemple réalise une activation longue avec réservation, annulation au relâchement et passage à `Activated`; il suffit de remplacer/étendre le handler pour un objet métier.

## Persistence boundary

`InteractionSavedState` contient uniquement une version (`1`) et un `InteractionState`. `LoadState` réutilise le chemin commun de changement d’état, rejoue les callbacks même lorsque la valeur restaurée est identique à la valeur courante, et rejette explicitement une version inconnue. Aucun fichier, service global ou backend n’est créé ; le projet hôte collecte et stocke les snapshots.

## Validation

```powershell
dotnet format quest-world.csproj
dotnet build
$env:GODOT_BIN = (Get-Command godot).Source
dotnet test
godot --headless --path . --scene res://addons/interaction_plugin/examples/InteractionDemo.tscn --quit-after 2 --log-file .godot/interaction-demo.log
```

Les tests couvrent les deux cas du statut union, l’ordre des règles, le focus, la réservation concurrente, la prévalidation, la séparation fin de phase/fin d’input, le nettoyage serveur d’un interacteur distant, l’autorité réseau serveur, le chemin offline, le snapshot/version y compris la restauration d’un état identique, la scène composable, le binding widget et la multiplicité/exclusivité des indications.

## Assumptions and deferred work

- Le transport reste `SceneMultiplayer`; les personnages/interactables dynamiques doivent conserver des chemins identiques via le système de spawn du projet.
- La synchronisation est portée par `MultiplayerSynchronizer` sur `ReplicatedState`; l’identité/progression de l’interacteur actif restent server-only en V1.
- Godot 4.7.1 Mono charge les assemblies avec .NET 10. Le projet cible donc `net10.0`, conserve `LangVersion=preview` et fournit un shim minimal `IUnion`/`UnionAttribute` pour utiliser le contrat union C# preview sans référence runtime .NET 11. Voir [`godot-dotnet-runtime-target.md`](../memory/godot-dotnet-runtime-target.md).
- La persistance réelle, les intégrations Quest/Dialog/Inventory, les combinateurs de règles, l'occlusion, les widgets 3D cliquables et les transports hors `SceneMultiplayer` restent hors V1.
- Le Character projet utilise la touche `E` via l'action projet `interact`; un jeu hôte peut remplacer cette action dans `Character.InteractionActionName`.
- L'addon Character générique reste sous `QuestWorld.Character` et ne référence pas `QuestWorld.Interaction`; seule la sous-classe globale du projet compose les deux systèmes.
