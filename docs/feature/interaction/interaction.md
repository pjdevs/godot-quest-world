# Interaction

## Purpose

Addon Godot C# réutilisable pour les interactions offline et multiplayer high-level : sélection d’une cible, conditions data-driven, interaction instantanée ou longue, état autoritaire répliqué et présentation locale remplaçable.

Le code runtime et ses contrats sont dans le namespace `QuestWorld.Interaction`.

## Delivered V1

L’addon autonome est sous [`addons/interaction_plugin`](../../addons/interaction_plugin) et ne crée ni autoload ni action Input Map.

- `InteractionInteractor` maintient les candidats d’indication/de portée dans un état privé, calcule le score regard-distance, émet les signaux de focus/statut/requête/refus et d’ajout/retrait d’indication, puis expose `TryStartInteractionInput()` / `TryEndInteractionInput()`. Le nœud reste sous autorité réseau serveur ; `OwnerPeerId` identifie uniquement le client qui calcule le focus, présente l’UI et envoie les intentions.
- `InteractiveComponent` compose des références Inspector explicites vers `InteractionArea`, `IndicationArea?`, `InteractionAnchor`, et `Stateful?`. `InteractionAnchor` est obligatoire et fournit la position monde utilisée pour la portée, le focus et la projection UI. Le composant possède la réservation de l’interacteur actif et le cycle des phases longues.
- `InteractionStateful` gère exclusivement l’état autoritaire/répliqué, la persistance et les notifications de changement. Il émet `InteractionStateChanged` dans tous les contextes, `InteractionStateChangedAuthority` uniquement sur le serveur et `InteractionStateChangedPresentation` partout sauf sur un serveur dédié. Il reste utilisable seul dans une scène et n’impose aucun owner ni aucune interface.
- Les RPC fiables résident dans l’interacteur. Le client prévalide le statut avant d’émettre une intention. Le serveur vérifie ensuite l’identité du peer, le chemin reçu, l’appartenance aux candidats serveur, distance/angle, état et rules avant d’émettre le signal gameplay. Les refus serveur vers client partent d’un nœud dont l’autorité réseau reste au serveur.
- `InteractionRule` est le point d’extension gameplay pour les conditions de quête, inventaire, progression, permissions ou état du monde. Il fournit `AlwaysBlockedInteractionRule` et `InteractorGroupInteractionRule`; le pipeline s’arrête à la première raison bloquante et autorise l’interaction si toutes les rules l’acceptent. Une rule reste une query synchrone, pure et sans état runtime mutable partagé.
- `InteractionPresenter`, `InteractionPromptWidget` et `InteractionIndicatorWidget` constituent la présentation facultative screen-space. Le prompt reste unique et centré sur l’ancre de la cible focusée ; un widget d’indication est présenté pour chaque interactable présent dans sa `IndicationArea`, sauf la cible focusée. Un widget projet peut implémenter `IInteractionWidget`.
- `scenes/InteractiveActor.tscn` et `examples/InteractionDemo.tscn` fournissent un objet à activation longue avec réservation, transition vers `Activated`, synchroniseur d’état et widgets de démonstration.

## Integration

1. Pour le Character du projet, `quest_world/character/Character.tscn` dérive de `addons/dummy_character_plugin/Character.tscn` et ajoute `InteractionInteractor` (distance calculée depuis le player propriétaire, direction calculée depuis la caméra) ainsi que `InteractionPresenter`. Le script global `quest_world/character/Character.cs` échantillonne l'action `interact` (`E` par défaut) et appelle les deux points d'entrée de l'interactor.
2. Pour un personnage custom, ajouter `InteractionInteractor` au personnage local et assigner `ViewOrigin` vers un `Marker3D` ou une caméra, puis appeler `TryStartInteractionInput()` / `TryEndInteractionInput()` depuis son contrôleur d'input. `InteractionOrigin` est facultatif et utilise explicitement le parent `Node3D` comme fallback documenté.
3. Ajouter `InteractionArea`, `InteractionAnchor` et `InteractiveComponent` au propriétaire Node3D, puis assigner `InteractionArea` et `InteractionAnchor` dans l'inspecteur. Ajouter et assigner un `InteractionStateful` seulement si l'objet a besoin d'un état persistant/répliqué. `IndicationArea` reste facultatif. Configurer `BusyReason` et `ActivatedReason` lorsque les blocages internes nécessitent un texte métier, par exemple `Talking...` pendant un dialogue.
4. Abonner le script gameplay aux signaux `InteractionInputStarted` et `InteractionInputEnded`. Pour une phase longue, appeler `Interactive.StartInteractionPhase(interactor)` synchroniquement depuis le signal de début, puis `Interactive.EndInteractionPhase(nextState)` quand l'opération métier se termine. Ajouter des `InteractionRule` custom pour les conditions gameplay. Pour réagir aux changements d’état, s’abonner au signal universel, autoritaire ou de présentation selon la responsabilité du consommateur.
5. Ajouter `InteractionPresenter` seulement si une UI est souhaitée, avec `Interactor` et `Camera`. L'absence de scène de widget est valide.

## Explicit configuration and validation

Les composants principaux (`InteractiveComponent`, `InteractionInteractor`, `InteractionStateful` et `InteractionPresenter`) sont des classes globales Godot. Le plugin editor `InteractionEditorPlugin` enregistre `InteractionInspectorPlugin`, qui délègue toutes les validations à `InteractionValidator` (`InteractionArea`/`InteractionAnchor`, `ViewOrigin`, `Interactor`/`Camera`). `InteractionAnchor` est obligatoire pour tout `InteractiveComponent`. `InteractionStateful` n’a aucune référence owner à valider. L’exemple `InteractiveActor` impose séparément ses références `Interactive` et `Stateful`. Les scripts runtime ne sont plus marqués `[Tool]` pour exposer ces warnings ; leurs gardes et erreurs runtime restent locales, et aucun booléen `IsConfigurationValid` n’est maintenu.

`InteractionInteractor.GetInteractionPresentation()` retourne `InteractionPresentation?`; l’absence de focus est donc représentée par l’absence de valeur. Le Presenter maintient sa propre liste d’indications à partir des signaux `InteractiveIndicationAdded` et `InteractiveIndicationRemoved`, sans lire les collections privées de détection.

Les warnings sont compilés sous `TOOLS` dans les scripts du plugin editor et affichés directement dans l’Inspector. `plugin.cfg` charge `editor/InteractionEditorPlugin.cs`, qui couvre les cinq types validés. L’Inspector identifie les scripts par leur classe globale ou leur chemin et lit leurs propriétés exportées via l’API Godot, afin de fonctionner avec les placeholders editor sans rendre les composants runtime `[Tool]`. `InteractiveActor` signale séparément l’absence de ses références `Interactive` et `Stateful`.

## XML API documentation

Les types et membres publics du runtime, des rules fournies et de la présentation possèdent des commentaires XML courts destinés à l’intégration. Ils précisent notamment les appels réservés au serveur, les RPC appelés par Godot plutôt que par le gameplay, les signaux locaux de présentation et les différences entre client, listen host et dedicated server. Les implémentations de `InteractionRule` documentent aussi leur contrainte de pureté et leur double évaluation client/serveur.

## Base scene

[`scenes/InteractiveActor.tscn`](../../addons/interaction_plugin/scenes/InteractiveActor.tscn) est le prefab de départ duplicable : zones d'interaction et d'indication, ancre, composant, état répliqué et widgets par défaut. Son `InteractiveComponent` possède la réservation et écoute le signal d’état universel pour invalider automatiquement le statut interactif. Son script d'exemple s'abonne aux signaux d'input et réalise une activation longue avec annulation au relâchement et passage à `Activated`.

## Persistence boundary

`InteractionSavedState` contient uniquement une version (`1`) et un `InteractionState`. `LoadState` réutilise le chemin commun de changement d’état, rejoue les signaux même lorsque la valeur restaurée est identique à la valeur courante, et rejette explicitement une version inconnue. Aucun fichier, service global ou backend n’est créé ; le projet hôte collecte et stocke les snapshots.

## Validation

```powershell
dotnet format quest-world.csproj
dotnet build
$env:GODOT_BIN = (Get-Command godot).Source
dotnet test
godot --headless --path . --scene res://addons/interaction_plugin/examples/InteractionDemo.tscn --quit-after 2 --log-file .godot/interaction-demo.log
```

Les tests couvrent les deux cas du statut union, l’ordre des rules, l’accès d’une rule au parent gameplay via `context.Interactive`, les raisons internes configurables, les signaux d’input et d’état spécialisés, le focus, la réservation concurrente, la prévalidation, la séparation fin de phase/fin d’input, le nettoyage serveur d’un interacteur distant, l’autorité réseau serveur, le chemin offline, le Stateful autonome sans owner, le snapshot/version y compris la restauration d’un état identique, l’invalidation par signal, la scène composable, le binding widget et la multiplicité/exclusivité des indications.

## Assumptions and deferred work

- Lorsqu'une session réseau se termine, `InteractionInteractor.IsLocallyControlled` réutilise son dernier résultat connu lorsque `MultiplayerPeer` devient nul ; le mode offline conserve ainsi le contrôle local sans appeler `GetUniqueId()` hors réseau.
- Le transport reste `SceneMultiplayer`; les personnages/interactables dynamiques doivent conserver des chemins identiques via le système de spawn du projet.
- La synchronisation est portée par `MultiplayerSynchronizer` sur la propriété technique privée `ReplicatedState`. Elle reste enregistrée auprès de Godot pour le chemin `.:ReplicatedState`, mais elle est masquée dans l’inspecteur ; le gameplay passe exclusivement par `SetState`. La réservation `InteractiveComponent.ActiveInteractor` reste transitoire et server-only en V1.
- Godot 4.7.1 Mono charge les assemblies avec .NET 10. Le projet cible donc `net10.0`, conserve `LangVersion=preview` et fournit un shim minimal `IUnion`/`UnionAttribute` pour utiliser le contrat union C# preview sans référence runtime .NET 11. Voir [`godot-dotnet-runtime-target.md`](../memory/godot-dotnet-runtime-target.md).
- La persistance réelle, les intégrations Quest/Dialog/Inventory, les combinateurs de règles, l'occlusion, les widgets 3D cliquables et les transports hors `SceneMultiplayer` restent hors V1.
- Le Character projet utilise la touche `E` via l'action projet `interact`; un jeu hôte peut remplacer cette action dans `Character.InteractionActionName`.
- L'addon Character générique reste sous `QuestWorld.Character` et ne référence pas `QuestWorld.Interaction`; seule la sous-classe globale du projet compose les deux systèmes.
