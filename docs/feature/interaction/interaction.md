# Interaction

## Purpose

Addon Godot C# réutilisable pour les interactions offline et multiplayer high-level : sélection d’une cible, conditions data-driven, interaction instantanée ou longue, état autoritaire répliqué et présentation locale remplaçable.

Le code runtime et ses contrats sont dans le namespace `QuestWorld.Interaction`.

## Delivered V1

L’addon autonome est sous [`addons/interaction_plugin`](../../addons/interaction_plugin) et ne crée ni autoload ni action Input Map.

- `InteractionInteractor` maintient les candidats d’indication/de portée dans un état privé, calcule le score regard-distance, émet les signaux de focus/statut/requête/refus et d’ajout/retrait d’indication, puis expose `TryStartInteractionInput(inputActionName)` / `TryEndInteractionInput(inputActionName)`. Le nœud reste sous autorité réseau serveur ; `OwnerPeerId` identifie uniquement le client qui calcule le focus, présente l’UI et envoie les intentions.
- `InteractiveComponent` compose des références Inspector explicites vers `InteractionArea`, `IndicationArea?`, `InteractionAnchor`, et `Stateful?`. `InteractionAnchor` est obligatoire et fournit la position monde utilisée pour la portée, le focus et la projection UI. Le composant possède la réservation de l’interacteur actif et le cycle des phases longues.
- `InteractionStateful` gère exclusivement l’état autoritaire/répliqué, la persistance et les notifications de changement. Il émet `InteractionStateChanged` dans tous les contextes, `InteractionStateChangedAuthority` uniquement sur le serveur et `InteractionStateChangedPresentation` partout sauf sur un serveur dédié. Il reste utilisable seul dans une scène et n’impose aucun owner ni aucune interface.
- Les RPC fiables résident dans l’interacteur. Le client prévalide le statut avant d’émettre une intention. Le serveur vérifie ensuite l’identité du peer, le chemin reçu, l’appartenance aux candidats serveur, distance/angle, état et rules avant d’émettre le signal gameplay. Les refus serveur vers client partent d’un nœud dont l’autorité réseau reste au serveur.
- `InteractionRule` est le point d’extension gameplay pour les conditions de quête, inventaire, progression, permissions ou état du monde. Il fournit `AlwaysBlockedInteractionRule` et `InteractorGroupInteractionRule`; le pipeline s’arrête à la première raison bloquante et autorise l’interaction si toutes les rules l’acceptent. Une rule reste une query synchrone, pure et sans état runtime mutable partagé.
- `InteractionPresenter`, `InteractionPromptWidget` et `InteractionIndicatorWidget` constituent la présentation facultative screen-space. Le prompt reste unique et centré sur l’ancre de la cible focusée ; un widget d’indication est présenté pour chaque interactable présent dans sa `IndicationArea`, sauf la cible focusée. Un widget projet peut implémenter `IInteractionWidget`.
- `scenes/InteractiveActor.tscn` et `examples/InteractionDemo.tscn` fournissent un objet à activation longue avec réservation, transition vers `Activated`, synchroniseur d’état et widgets de démonstration.

## V2 migration status

### Task 1 — Mutation/dispatch boundaries

La première étape de migration est terminée sans modifier le comportement public V1.

- `InteractionStateful` applique désormais sa valeur dans `ApplyStateCore()` et retourne une `InteractionStateTransition`. Les signaux universel, autoritaire et de présentation sont émis ensuite par `DispatchStateTransition()`.
- `InteractiveComponent` sépare la validation et les mutations locales de `StartInteraction`, `StartInteractionPhase`, `EndInteractionPhase` et `ReleaseInteractionInput` de leurs appels externes et notifications.
- La réservation d'une phase est complète avant l'appel à `InteractionStateful.SetState()`. La libération de l'interacteur actif est complète avant tout signal de fin ou de statut.
- `InteractionInteractor` calcule et applique le nouveau focus dans `RecalculateFocusCore()`, puis émet le changement de focus et le statut dans `DispatchFocusChange()`.
- Les résultats core sont des types `internal` afin de tester directement les transitions sans agrandir l'API publique du plugin.

Les tests distinguent maintenant la mutation pure des notifications : état et focus finaux sans signal pendant les appels core, puis chaque signal attendu exactement une fois pendant le dispatch.

### Task 2 — Generic Stateful

Le composant d'état générique existe désormais dans son propre addon [`addons/stateful_plugin`](../../../addons/stateful_plugin), documenté dans [stateful.md](../state/stateful.md).

- `StatefulComponent` possède une valeur `StringName` libre (`closed`, `open`, `flooded`), autoritaire, répliquée, persistable et observable, sans aucune dépendance à `interaction_plugin`.
- `StateSchema` déclare optionnellement les valeurs acceptées pour la validation runtime et editor, sans devenir une machine à états.
- Le composant applique dès l'origine la frontière `ApplyStateCore()` / `DispatchStateTransition()` et les trois scopes de signaux `StateChanged`, `StateChangedAuthority` et `StateChangedPresentation`.
- `InteractionStateful` n'est pas supprimé : les deux composants coexistent jusqu'à la Task 12. Aucune scène existante n'est migrée à cette étape.

### Task 3 — Action model and Availability

Un target expose désormais N actions explicites, chacune évaluée indépendamment.

- `InteractionActionDefinition` (`Resource`) porte les données partageables : `Id` (identité gameplay/réseau stable), `Label`, `Description` et `InputActionName`. Le label n'est jamais une identité.
- `InteractionAction` (`Node`) lie une definition à une occurrence de target et porte les `Rules` propres à cette occurrence. Il n'évalue rien lui-même et ne mute aucun gameplay.
- `InteractiveComponent.Actions` référence explicitement les actions, comme les autres références du plugin : rien n'est découvert dans l'arbre. Les actions sont enfants du composant dans les scènes fournies.
- `InteractionAvailability` remplace `InteractionStatus` : union `InteractionAllowed | InteractionBlocked | InteractionHidden`. Seul `Blocked` porte une raison ; `Hidden` signifie « absent des choix présentés », donc rien à expliquer.
- `InteractionContext` devient action-aware `(Interactor, Interactive, Action)` et `InteractionRule.Evaluate()` retourne une `InteractionAvailability`. Il n'existe plus de chemin d'évaluation sans action : une rule voit toujours l'action évaluée, y compris une rule target-level.
- Le pipeline par action est ordonné : invariants de configuration, réservation, `TargetRules`, puis `Action.Rules`. Le premier résultat non-`Allowed` gagne. Une action sans `Definition`, ou qui n'appartient pas au target, est `Blocked("Interaction is not configured.")`.
- `EvaluateStatus()` disparaît. `EvaluateAvailability(interactor, action)` évalue une action ; `EvaluateAvailability(interactor)` agrège les actions du target (Allowed > Blocked > Hidden) pour alimenter la présentation V1, la prévalidation client et la validation serveur jusqu'à la présentation action-aware de la Task 4. Un target sans action est `Hidden` et n'offre aucune interaction.
- L'interprétation `Idle == interactible` quitte le core : `BusyReason`, `ActivatedReason` et la lecture de `Stateful` pendant l'évaluation sont supprimés. `LegacyStatefulInteractionRule` reproduit explicitement l'ancien comportement là où une scène V1 le demande, et disparaîtra avec la Task 12. `InteractiveComponent.Stateful` ne sert plus qu'à invalider le statut sur changement d'état.
- `InteractiveComponent.InteractionRules` est renommé `TargetRules` pour distinguer les conditions du target de celles d'une action.
- `InteractiveActor.tscn` et `Button.tscn` déclarent chacune une action `activate`. L'exécution reste portée par le signal V1 `InteractionInputStarted` jusqu'à la Task 6, et le RPC transporte encore uniquement le target jusqu'à la Task 5.

Restent volontairement absents de cette étape : `Executor`, `Priority`, `ConcurrencyGroup`, `Automatic` et `CancelOnInputReleased` sur `InteractionAction` (Tasks 5 à 7), la résolution d'action par input (Task 5), et les diagnostics Inspector des actions (Task 11). `InteractionActionName` et `AutomaticInteraction` restent temporairement sur `InteractiveComponent`, en doublon avec la definition, jusqu'aux Tasks 4 et 5.

### Task 4 — Action-aware presentation and focus

La présentation expose désormais une entrée par action, et le focus ignore les targets sans action présentable.

- `InteractionActionPresentation` porte `ActionId`, `Label`, `Description`, `InputActionName` et l'`Availability` d'**une** action. `IsAllowed` et `BlockReason` y sont per-action : il n'existe plus de statut allowed/blocked au niveau du target.
- `InteractionTargetPresentation` remplace `InteractionPresentation` et porte ce qui reste target-level : `Interactive`, `DisplayName`, `Description`, la liste ordonnée des actions présentables et `IsFocused`. Les actions `Hidden` en sont absentes, les `Blocked` y restent pour être expliquées.
- `HasAllowedAction` est le seul agrégat conservé. Il sert exclusivement à choisir entre `IndicationScene` et `BlockedIndicationScene`, l'indication étant par nature un unique visuel pour tout l'objet. Un prompt ne doit jamais l'utiliser.
- `InteractiveComponent.HasVisibleAction(interactor)` alimente le focus : `RecalculateFocusCore()` ignore un candidat dont toutes les actions sont `Hidden`, et le focus bascule alors sur le meilleur candidat suivant. Le focus reste target-level : aucune sélection par action n'est introduite.
- `InteractionStatusChanged` devient une notification pure `(interactive)`. Le résumé `isAllowed`/`reason` disparaît du signal : un consommateur relit `GetPresentation()`, conformément à l'invariant « signals are notifications ».
- La présentation se compose de deux niveaux. Le Presenter instancie `PromptContainerScene` pour la cible focusée — le widget global qui porte le nom de l'objet et expose son `ActionsContainer` — puis y empile une instance de `InteractiveComponent.ActionPromptScene` par action présentée. Sans scène de conteneur, un `VBoxContainer` nu sert de point d'empilement. Le conteneur, les prompts d'action et les indications sont donc trois points d'override indépendants.
- `IInteractionWidget` reste le contrat target-level (`Bind(in InteractionTargetPresentation)`), `IInteractionActionWidget` le contrat par action (`Bind(in InteractionActionPresentation)`), et `IInteractionPromptContainer` ajoute au premier l'`ActionsContainer` recevant les widgets d'action.
- `InteractionPromptWidget` devient le conteneur par défaut (`Content/Label` + `Content/Actions`) et `InteractionActionPromptWidget` le prompt d'une action (`[input] Label` si allowed, `Label: raison` si blocked). `InteractionIndicatorWidget` n'affiche plus de raison : le blocage est porté par la scène d'indication choisie, les raisons appartiennent aux prompts d'action.
- `InteractiveComponent.InteractionActionName` est supprimé (§22) : l'input d'une action vient de `Definition.InputActionName`. `PromptScene` devient `ActionPromptScene` et change de sens — un widget par action, plus un widget par target.
- Une seule projection écran subsiste pour la cible focusée, celle du conteneur, et une par indication. Les scènes `InteractiveActor.tscn`, `Button.tscn` et `Character.tscn` sont migrées en conséquence.

Restent volontairement absents de cette étape : la résolution d'action par input et `AutomaticInteraction` au niveau action (Task 5), le routage réseau de l'`actionId` (Task 5) et le refus action-aware (Task 5/§20). `GetPresentation()` alloue une liste par appel ; l'optimisation est différée tant que le coût n'est pas mesuré.

### Task 5 — Action command routing and RPC

L'input devient une intention locale résolue en action, et l'identité de l'action traverse le réseau.

- `TryStartInteractionInput(inputActionName)` remplace la version sans paramètre : l'interactor demande au target focusé l'action correspondant à cet input via `InteractiveComponent.ResolveActionForInput()`. Les actions `Hidden` sont ignorées, ce qui permet à `open` et `close` de partager la touche `E` sans rebinding.
- L'ordre de résolution est `Allowed` avant `Blocked`, puis `Priority` décroissante, puis `ActionId` croissant. Le dernier critère existe pour que deux actions équivalentes restent déterministes ; l'éditeur les signalera en Task 11. Une action `Blocked` est volontairement résolue quand aucune `Allowed` ne correspond, afin que le refus soit expliqué au lieu que la touche ne fasse rien.
- Le RPC transporte désormais `ServerTryStartInteraction(targetPath, actionId)` et `ServerTryEndInteraction(actionId)`. Le serveur ne reçoit jamais l'action elle-même : il la re-résout depuis sa propre scène avec `ResolveAction(actionId)`, puis réévalue `EvaluateAvailability(interactor, action)`. Un `actionId` inconnu ou vide est refusé comme indisponible, sans révéler qu'il n'existe pas.
- Le refus devient action-aware (§20) : `InteractionRequested(interactive, actionId)`, `InteractionRejected(interactive, actionId, reason)` et `ClientInteractionRejected(targetPath, actionId, reason)`. `Hidden` et `Blocked` renvoient la même formulation côté serveur pour ne pas divulguer l'existence d'une action masquée.
- `TryEndInteractionInput(inputActionName)` relâche l'action que l'interactor **se souvient** d'avoir démarrée pour cet input, jamais une re-résolution. Une re-résolution au relâchement viserait une autre action dès que l'état du monde a changé pendant l'appui, et laisserait la réservation serveur bloquée. Le serveur ne relâche que si son action active correspond à l'`actionId` reçu. Ce souvenir local disparaîtra avec l'`ExecutionId` de la Task 7.
- `InteractionInputStarted` et `InteractionInputEnded` portent `(interactor, action)`, et `StartInteractionPhase(interactor, action)` prend l'action reçue. Le gameplay sait donc quelle action s'exécute avant que les Executors de la Task 6 ne remplacent ce chemin. `InteractiveComponent` mémorise l'action réservée en plus de l'interacteur.
- `AutomaticInteraction` quitte `InteractiveComponent` pour `InteractionAction.Automatic` (§22). L'action automatique est tentée depuis `DispatchFocusChange`, après la mutation du focus, et non plus depuis `_Process` : le focus est le plus souvent établi par `AddInteractive()` hors frame, et la version target-level ne se déclenchait donc quasiment jamais.
- Une action automatique reste dans `GetPresentation()` — sinon elle ne prendrait pas le focus et ne se déclencherait jamais — mais porte `IsAutomatic`. Le Presenter l'exclut des prompts, et `HasPromptableAction` remplace le test `AutomaticInteraction` target-level.
- `InteractionInteractor.InteractionActionName` est supprimé : le nom de l'input appartient à l'appelant (`Character.InteractionActionName`) et à `Definition.InputActionName`, plus à l'interactor.

Restent volontairement absents de cette étape : l'`Executor` explicite et les signaux after-the-fact (Task 6), le lifecycle `Running`/`ExecutionId` et le hold (Task 7), `ConcurrencyGroup` et `CancelOnInputReleased` (Tasks 6/7), et le warning éditeur sur deux actions `Allowed` de même input et même priorité (Task 11).


### Task 6 — Explicit executor replaces broadcast execution

L'exécution gameplay quitte les signaux : elle appartient désormais à un unique `Executor` explicitement configuré.

- `InteractionActionExecutor` (`Node`, abstract) est le seul propriétaire supporté de la mutation gameplay d'une action. `Execute(in InteractionExecutionContext)` est appelé synchroniquement par le target autoritaire, une fois l'action déjà autorisée et réservée. Le contexte porte `(Interactor, Interactive, Action)` ; l'`ExecutionId` du §11 arrive en Task 7 avec le stockage qui lui donne un sens.
- `InteractionAction.Executor` est **requis**. Une action sans executor est une erreur de configuration : `_Ready()` la signale et `EvaluateAvailability()` la renvoie `Blocked("Interaction is not configured.")`, au même titre qu'une `Definition` manquante. Il n'existe aucun fallback « pas d'executor → on émet un signal et quelqu'un s'en chargera peut-être ».
- `InteractionExecutionResult` est une union `Completed | Running | Rejected(reason) | Failed(reason)`, conformément à la note de dev du §12 : seuls les cas concernés portent une raison, et l'enum `Disposition` disparaît. `Rejected` doit rester rare — une condition gameplay normale appartient à une rule, où la présentation la voit aussi ; `Failed` est une erreur découverte **après** acceptation.
- `InteractiveComponent.ExecuteAction(interactor, action)` remplace `StartInteraction()` et applique le flux du §15 : `EvaluateAvailability` → `ReserveExecutionCore()` → *mutation terminée* → `Executor.Execute(context)` → `ApplyExecutionResultCore()` → *mutation terminée* → dispatch. La réservation existe donc **avant** l'appel externe, y compris pour une action instantanée, et un test vérifie que l'executor voit déjà `ActiveInteractor` positionné.
- Les quatre signaux sont des notifications after-the-fact : `InteractionActionStarted`, `InteractionActionCompleted`, `InteractionActionCancelled(reason)`, `InteractionActionRejected(reason)`. `InteractionInputStarted` et `InteractionInputEnded` sont supprimés — un signal qui n'est plus le chemin de commande mais continue d'être émis invite à être réutilisé par erreur.
- L'invariant de séquence est : un `Started` est toujours suivi d'exactement un `Completed` ou un `Cancelled`, et un `Rejected` n'est jamais précédé d'un `Started`. `Failed` est donc notifié `Started` puis `Cancelled(reason)` : de l'extérieur, l'action a bien commencé et n'a pas abouti. Un `Rejected` n'invalide aucun statut puisque rien n'a eu lieu.
- `InteractionExecution` remplace le couple `_activeInteractor`/`_activeAction`. `StartInteractionPhase` et `EndInteractionPhase` disparaissent avec leur couplage au `Stateful` : le core ne décide plus d'un état monde. Une exécution `Running` conserve la réservation jusqu'à `CompleteExecution()` ou `CancelExecution(reason)` ; la surcharge `CancelExecution(interactor, reason)` n'annule que si cet interacteur possède l'exécution. Ces méthodes prendront un `ExecutionId` en Task 7.
- L'interactor ne mémorise `_activeInteractive` que pour une exécution `Running` : une action instantanée ne réserve rien, donc le relâchement de sa touche n'annule rien. Les raisons d'annulation sont explicites (`input released`, `interactor left`) au lieu du chemin unique `ReleaseInteractionInput` de la V1.
- Les exemples sont migrés vers le modèle owner + executor. `InteractiveActor` garde l'état et la durée et expose `BeginActivation()` (`Running`) ; `InteractiveActorExecutor` est le nœud placé sous l'action qui l'appelle. La fin de l'activation passe par `CompleteExecution()`, et l'annulation arrive comme notification `InteractionActionCancelled`. `Button.cs` disparaît entièrement : tout le comportement du bouton est dans `ButtonInteractionExecutor`, qui porte `TargetStateful` et `TargetState` et n'a plus aucun abonnement. La racine de `Button.tscn` n'a plus de script.
- `IStatefulProvider` est supprimé (§24) : l'executor référence directement le nœud `InteractionStateful` concret au lieu de passer par une interface C#, conformément au contrat cross-language du §25. `LeverWall` n'implémente plus rien et reste un simple script de présentation abonné à `InteractionStateChangedPresentation`. Comme la cible vit dans une autre scène instanciée, `test_world.tscn` déclare `[editable path="Level/Button"]` et override `ActivateExecutor.TargetStateful` vers `LeverWall/InteractionStateful` : le câblage inter-objets appartient au level, pas au prefab.

- Une exécution `Running` refuse toute nouvelle exécution sur le même target, y compris à l'interacteur qui la possède, avec sa raison propre `"This is already in use."`. L'`Availability` n'est volontairement pas modifiée pour bloquer les autres actions pendant une exécution : décider si deux actions d'un même target peuvent tourner ensemble est la question de concurrence du §14, tranchée en Task 7 (`ConcurrencyGroup` ou `IsExclusive`).

Restent volontairement absents de cette étape : l'`ExecutionId` et le stockage multi-executions, `ConcurrencyGroup`, `CancelOnInputReleased` et le hold (Task 7), le `SetStateInteractionExecutor` générique et la `StatefulStateInteractionRule` (Task 8), et les diagnostics editor de l'`Executor` (Task 11). Un executor ne doit pas appeler `CompleteExecution()`/`CancelExecution()` depuis son propre `Execute()` : il retourne `Completed` ou `Failed`.


## Integration

1. Pour le Character du projet, `quest_world/character/Character.tscn` dérive de `addons/dummy_character_plugin/Character.tscn` et ajoute `InteractionInteractor` (distance calculée depuis le player propriétaire, direction calculée depuis la caméra) ainsi que `InteractionPresenter`. Le script global `quest_world/character/Character.cs` échantillonne l'action `interact` (`E` par défaut) et appelle les deux points d'entrée de l'interactor.
2. Pour un personnage custom, ajouter `InteractionInteractor` au personnage local et assigner `ViewOrigin` vers un `Marker3D` ou une caméra, puis appeler `TryStartInteractionInput(inputActionName)` / `TryEndInteractionInput(inputActionName)` depuis son contrôleur d'input, en passant le nom de l'action projet réellement pressée. `InteractionOrigin` est facultatif et utilise explicitement le parent `Node3D` comme fallback documenté.
3. Ajouter `InteractionArea`, `InteractionAnchor` et `InteractiveComponent` au propriétaire Node3D, puis assigner `InteractionArea` et `InteractionAnchor` dans l'inspecteur. Ajouter et assigner un `InteractionStateful` seulement si l'objet a besoin d'un état persistant/répliqué. `IndicationArea` reste facultatif.
4. Ajouter au moins une `InteractionAction` sous le composant, lui assigner une `InteractionActionDefinition` (`Id`, `Label`, `InputActionName`), un `Executor` obligatoire, et la référencer dans `Actions`. `Priority` départage deux actions qui partagent un input, et `Automatic` déclenche l'action au focus sans input. Sans action, le target n'offre aucune interaction. Mettre dans `Action.Rules` les conditions propres à l'action, et dans `TargetRules` celles communes à toutes les actions. Une rule reste une query pure : pour dépendre de l'état du monde, la rule lit l'état, elle ne le modifie jamais.
5. Écrire un `InteractionActionExecutor` par action, le placer sous l'`InteractionAction` et l'assigner à `Action.Executor`. `Execute()` retourne `Completed` pour une action instantanée, ou `Running` pour une action longue ; dans ce cas le gameplay appelle plus tard `Interactive.CompleteExecution()` ou `Interactive.CancelExecution(reason)`. Les signaux `InteractionActionStarted/Completed/Cancelled/Rejected` sont des notifications : les utiliser pour l'audio, la VFX, les quêtes ou l'UI, jamais pour effectuer l'action. Pour réagir aux changements d’état, s’abonner au signal universel, autoritaire ou de présentation selon la responsabilité du consommateur.
6. Ajouter `InteractionPresenter` seulement si une UI est souhaitée, avec `Interactor`, `Camera` et éventuellement `PromptContainerScene`. Assigner `ActionPromptScene` sur le composant pour le prompt d'une action, `IndicationScene` et `BlockedIndicationScene` pour l'indication globale de l'objet. L'absence de scène de widget est valide : sans conteneur les prompts d'action sont empilés dans un `VBoxContainer` nu, sans `ActionPromptScene` aucun prompt d'action n'est créé.

## Explicit configuration and validation

Les composants principaux (`InteractiveComponent`, `InteractionInteractor`, `InteractionStateful` et `InteractionPresenter`) sont des classes globales Godot. Le plugin editor `InteractionEditorPlugin` enregistre `InteractionInspectorPlugin`, qui délègue toutes les validations à `InteractionValidator` (`InteractionArea`/`InteractionAnchor`, `ViewOrigin`, `Interactor`/`Camera`). `InteractionAnchor` est obligatoire pour tout `InteractiveComponent`. `InteractionStateful` n’a aucune référence owner à valider. L’exemple `InteractiveActor` impose séparément ses références `Interactive` et `Stateful`. Les scripts runtime ne sont plus marqués `[Tool]` pour exposer ces warnings ; leurs gardes et erreurs runtime restent locales, et aucun booléen `IsConfigurationValid` n’est maintenu.

`InteractionInteractor.GetInteractionPresentation()` retourne `InteractionTargetPresentation?`; l’absence de focus est donc représentée par l’absence de valeur. Le Presenter maintient sa propre liste d’indications à partir des signaux `InteractiveIndicationAdded` et `InteractiveIndicationRemoved`, sans lire les collections privées de détection.

Les warnings sont compilés sous `TOOLS` dans les scripts du plugin editor et affichés directement dans l’Inspector. `plugin.cfg` charge `editor/InteractionEditorPlugin.cs`, qui couvre les cinq types validés. L’Inspector identifie les scripts par leur classe globale ou leur chemin et lit leurs propriétés exportées via l’API Godot, afin de fonctionner avec les placeholders editor sans rendre les composants runtime `[Tool]`. `InteractiveActor` signale séparément l’absence de ses références `Interactive` et `Stateful`.

## XML API documentation

Les types et membres publics du runtime, des rules fournies et de la présentation possèdent des commentaires XML courts destinés à l’intégration. Ils précisent notamment les appels réservés au serveur, les RPC appelés par Godot plutôt que par le gameplay, les signaux locaux de présentation et les différences entre client, listen host et dedicated server. Les implémentations de `InteractionRule` documentent aussi leur contrainte de pureté et leur double évaluation client/serveur.

## Base scene

[`scenes/InteractiveActor.tscn`](../../addons/interaction_plugin/scenes/InteractiveActor.tscn) est le prefab de départ duplicable : zones d'interaction et d'indication, ancre, composant, état répliqué et widgets par défaut. Son `InteractiveComponent` possède la réservation et écoute le signal d’état universel pour invalider automatiquement le statut interactif. Son action porte un `InteractiveActorExecutor` qui démarre une activation longue (`Running`), complétée en `Activated` par le script d'exemple ou annulée au relâchement de la touche.

## Persistence boundary

`InteractionSavedState` contient uniquement une version (`1`) et un `InteractionState`. `LoadState` réutilise le chemin commun de changement d’état, rejoue les signaux même lorsque la valeur restaurée est identique à la valeur courante, et rejette explicitement une version inconnue. Aucun fichier, service global ou backend n’est créé ; le projet hôte collecte et stocke les snapshots.

## Validation

```powershell
dotnet format quest-world.csproj
dotnet build
$env:GODOT_BIN = (Get-Command godot).Source
dotnet test
godot --headless --path . --scene res://quest_world/levels/test_world.tscn --quit-after 3 --log-file .godot/test-world-runtime.log
```

Sur macOS, invoquer le binaire Godot Mono par son chemin complet et exporter `GODOT_BIN` vers ce même binaire. Voir [`godot-cli-headless-workflow.md`](../../memory/godot-cli-headless-workflow.md).

Les tests couvrent les trois cas de l’union d’availability, l’ordre `TargetRules` puis `Action.Rules`, l’arrêt au premier résultat non-`Allowed`, la porte dont `Open` et `Close` s’excluent selon l’état du monde, l’agrégation target-level Allowed > Blocked > Hidden, la pureté et la répétabilité de l’évaluation, l’action non configurée ou étrangère au target, le target sans action, l’accès d’une rule au parent gameplay via `context.Interactive`, les raisons de blocage configurées sur la rule, l'exécution par un unique executor quel que soit le nombre d'observers, l'action sans executor bloquée et jamais exécutée, la cible déjà réservée pendant l'appel executor, les séquences `started,completed`, `rejected` seul et `started,cancelled` pour un échec, la réservation conservée par une exécution `Running` puis libérée par `CompleteExecution`/`CancelExecution`, le refus d'annuler l'exécution d'un autre interacteur, les signaux d’état spécialisés, le focus, la réservation concurrente, la prévalidation, la séparation fin de phase/fin d’input, le nettoyage serveur d’un interacteur distant, l’autorité réseau serveur, le chemin offline, le Stateful autonome sans owner, le snapshot/version y compris la restauration d’un état identique, l’invalidation par signal, la scène composable avec son action, la présentation à une entrée par action visible avec omission des `Hidden`, une action `Blocked` présentée avec sa propre raison, le target dont toutes les actions sont `Hidden` ignoré par le focus, le report du focus sur le candidat suivant, l’empilement d’un prompt par action dans le conteneur, le binding des widgets d’action et de conteneur, et la multiplicité/exclusivité des indications.

## Assumptions and deferred work

- Lorsqu'une session réseau se termine, `InteractionInteractor.IsLocallyControlled` réutilise son dernier résultat connu lorsque `MultiplayerPeer` devient nul ; le mode offline conserve ainsi le contrôle local sans appeler `GetUniqueId()` hors réseau.
- Le transport reste `SceneMultiplayer`; les personnages/interactables dynamiques doivent conserver des chemins identiques via le système de spawn du projet.
- La synchronisation est portée par `MultiplayerSynchronizer` sur la propriété technique privée `ReplicatedState`. Elle reste enregistrée auprès de Godot pour le chemin `.:ReplicatedState`, mais elle est masquée dans l’inspecteur ; le gameplay passe exclusivement par `SetState`. La réservation `InteractiveComponent.ActiveInteractor` reste transitoire et server-only en V1.
- Godot 4.7.1 Mono charge les assemblies avec .NET 10. Le projet cible donc `net10.0`, conserve `LangVersion=preview` et fournit un shim minimal `IUnion`/`UnionAttribute` pour utiliser le contrat union C# preview sans référence runtime .NET 11. Voir [`godot-dotnet-runtime-target.md`](../memory/godot-dotnet-runtime-target.md).
- La persistance réelle, les intégrations Quest/Dialog/Inventory, les combinateurs de règles, l'occlusion, les widgets 3D cliquables et les transports hors `SceneMultiplayer` restent hors V1.
- Le Character projet utilise la touche `E` via l'action projet `interact`; un jeu hôte peut remplacer cette action dans `Character.InteractionActionName`.
- L'addon Character générique reste sous `QuestWorld.Character` et ne référence pas `QuestWorld.Interaction`; seule la sous-classe globale du projet compose les deux systèmes.
