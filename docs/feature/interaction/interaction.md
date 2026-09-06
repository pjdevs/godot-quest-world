# Interaction

## Purpose

Addon Godot C# réutilisable pour les interactions offline et multiplayer high-level : sélection d’une cible, conditions data-driven, interaction instantanée ou longue, état autoritaire répliqué et présentation locale remplaçable.

Le code runtime et ses contrats sont dans le namespace `QuestWorld.Interaction`.

## Architecture courante — intégration Gameplay Action (tranche 5)

Interaction conserve la sélection spatiale, le focus, ses règles contextuelles et l'adaptation de
présentation. Il ne constitue plus le moteur d'exécution actif : toutes les actions passent par
`GameplayActionComponent` côté cible et `GameplayActionRunner` côté demandeur, documentés dans
[gameplay-action.md](../gameplay_action/gameplay-action.md).

- `InteractionAction` hérite de `InputGameplayAction`; il utilise `GameplayActionDefinition` et
  `GameplayActionBindingConfig` directement. `InteractionRule` et `InteractionActionExecutor`
  restent des adaptateurs Interaction utiles, sans sous-types de données vides.
- `InputGameplayAction.DefaultBindingConfig` est optionnelle pour les actions génériques. Une
  `InteractionAction` destinée au focus doit la renseigner avec l'input, le mode, le hold, la
  contrainte de présence et la priorité.
- Les `TargetRules` sont évaluées par un adaptateur placé avant les rules propres à l'action. Les
  exécutions programmatiques ignorent l'accès spatial, mais conservent toutes ces rules.
- Le focus construit des bindings génériques contextuels ; sa perte nettoie leur source. La portée
  est revérifiée sur l'autorité et pendant une exécution longue qui exige la présence du requester.
- Les signaux et snapshots Interaction sont des projections du lifecycle, des ACK et du store de
  présentation génériques. `GameplayActionExecutionSynchronizer` transporte ce même store.

### Topologie authorée

La tranche 5 supprime la passerelle de migration différée et l'alias `ConcurrencyGroup` : plus
aucune installation implicite, la scène déclare la topologie finale. Le host générique est un nœud
**frère** de l'`InteractiveComponent`, et les actions sont ses **enfants directs** :

```
Interaction/
  GameplayActions/                     # GameplayActionComponent : Actions = [OpenAction, ...]
    OpenAction/                        # InteractionAction
      OpenExecutor                     # SetStateGameplayActionExecutor, Stateful = ../../../...
  GameplayActionExecutionSynchronizer  # Component = ../GameplayActions
  InteractiveComponent                 # ActionComponent = ../GameplayActions
  StatefulComponent
```

L'`InteractiveComponent` ne déclare **aucune** action : `Actions` y est une projection en lecture
seule des `InteractionAction` de son host, dans l'ordre où celui-ci les déclare. Une seule liste
authorée fait foi, celle du host qui possède réellement les exécutions, et elle ne peut plus diverger
de la cible qui les présente. Une action générique hébergée à côté n'est pas une offre d'interaction
et est ignorée.

Deux contraintes découlent de cette forme, et le validateur les signale :

- une action authorée doit être enfant direct de son `GameplayActionComponent`, sinon le host refuse
  de l'enregistrer et la cible se présente comme non configurée ;
- l'`ActionComponent` doit être assigné avant l'entrée dans l'arbre, parce que l'`InteractiveComponent`
  s'abonne à son host au `_Ready`.

Le host frère — plutôt qu'enfant de l'interactive — garde à chaque action la profondeur qu'elle avait
avant l'extraction : tout `NodePath` relatif authoré depuis une action ou son executor reste valable
sans réécriture.

Côté demandeur, `Character.tscn` authore de même `GameplayActions` et `GameplayActionRunner` à côté
de l'`InteractionInteractor`, dont la propriété `Runner` les désigne explicitement.

### Instigator plutôt qu'index inverse

L'`InteractionInteractor` s'approprie l'`Instigator` de son `GameplayActionRunner` : l'instigator
d'une exécution d'interaction *est* l'interactor qui l'a menée. C'est ce qui rend le contexte
générique suffisant pour les rules et les executors d'Interaction, et ce qui supprime les deux
dictionnaires statiques qui indexaient les runners et les hosts vers leurs propriétaires. Dans
l'autre sens, une cible se retrouve par appartenance : le host nomme l'action, et l'action connaît
l'interactive qui l'a préparée.

Une exécution programmatique ne nomme donc aucun *requester* — personne ne l'a demandée sur le
réseau, personne n'attend d'acquittement — mais elle nomme son instigator. C'est cet instigator qui
distingue « tu utilises déjà ceci » de « quelqu'un d'autre l'utilise », quelle que soit l'origine de
l'exécution.

Corollaire assumé : un executor d'interaction ne tourne que pour un interactor. Une exécution pilotée
par le monde sans interactor n'est pas une interaction et est refusée, avec un motif qui dit laquelle
des deux moitiés manque.

Les tests couvrent notamment le bind/unbind de focus, le refus autoritaire hors portée, le bypass
spatial programmatique avec rules, l'annulation sur perte d'accès soutenu, les ACK, la réplication
requester/observer/late joiner, les lectures de présentation issues du composant générique, et la
topologie authorée des scènes du projet.

### NodePath des rules d'etat

`StatefulPath` est resolu relativement a l'`InteractionAction` proprietaire de la rule. Le selecteur
de noeud Godot et le runtime utilisent ainsi le meme point de depart ; un chemin choisi depuis l'action
peut etre conserve tel quel dans la scene.

## Delivered V1

L’addon autonome est sous [`addons/interaction_plugin`](../../addons/interaction_plugin) et ne crée ni autoload ni action Input Map.

- `InteractionInteractor` maintient les candidats d’indication/de portée dans un état privé, calcule le score regard-distance, émet les signaux de focus/statut/requête/refus et d’ajout/retrait d’indication, puis rafraîchit le focus et les bindings avant une pression. Le `GameplayActionRunner` porte la boucle d’input générique et reste la façade des requêtes ; l’interactor fournit uniquement l’accès spatial et la présentation Interaction.
- `InteractiveComponent` compose des références Inspector explicites vers `InteractionArea`, `IndicationArea?` et `InteractionAnchor`. `InteractionAnchor` est obligatoire et fournit la position monde utilisée pour la portée, le focus et la projection UI. Le composant possède la réservation de l’interacteur actif et le cycle des phases longues.
- L’état monde a quitté cet addon à la Task 12 : il appartient à `StatefulComponent` (addon [`stateful_plugin`](../../../addons/stateful_plugin), documenté dans [stateful.md](../state/stateful.md)), que l’interaction ne consomme que par des rules pures. Le composant V1 `InteractionStateful` et l’enum `InteractionState` n’existent plus.
- Les RPC fiables résident dans le `GameplayActionRunner`, qui porte aussi l’autorité réseau serveur de ses acquittements. Le client prévalide le statut avant d’émettre une intention. Le serveur vérifie ensuite l’identité du peer, le chemin reçu, l’appartenance aux candidats serveur, distance/angle, état et rules avant d’émettre le signal gameplay. L’`InteractionInteractor` reste dédié à la détection, au focus, à l’accès spatial et à la présentation ; il délègue son contrôle local au runner et ne possède aucune autorité RPC.
- `InteractionRule` est le point d’extension gameplay pour les conditions de quête, inventaire, progression, permissions ou état du monde. Il fournit `AlwaysBlockedInteractionRule` et `InteractorGroupInteractionRule`; le pipeline s’arrête à la première raison bloquante et autorise l’interaction si toutes les rules l’acceptent. Une rule reste une query synchrone, pure et sans état runtime mutable partagé.
- `InteractionPresenter`, `InteractionPromptWidget` et `InteractionIndicatorWidget` constituent la présentation facultative screen-space. Le prompt reste unique et centré sur l’ancre de la cible focusée ; un widget d’indication est présenté pour chaque interactable présent dans sa `IndicationArea`, sauf la cible focusée. Un widget projet peut implémenter `IInteractionWidget`.
- La scène d’indication par défaut utilise `resources/indicator.svg` dans son `TextureRect` pour afficher un cercle non rempli de 32×32 pixels.
- [`integration/stateful/examples/LongActionExample.tscn`](../../../addons/interaction_plugin/integration/stateful/examples/LongActionExample.tscn) fournit un objet à action longue avec réservation, état répliqué, synchroniseur et widgets de démonstration.

## V2/V3 migration history — superseded by V4

> Les sections Task ci-dessous documentent les décisions historiques V2/V3. Elles ne décrivent plus
> l'API courante ; le contrat livré est celui de la section « Interaction V4 — delivered ».

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

- `GameplayActionDefinition` (`Resource`) porte les données partageables : `Id` (identité gameplay/réseau stable), `Label` et `Description`. Le label n'est jamais une identité.
- `InteractionAction` (`Node`) lie une definition à une occurrence de target et porte les `Rules` propres à cette occurrence. Il n'évalue rien lui-même et ne mute aucun gameplay.
- `InteractiveComponent.Actions` référence explicitement les actions, comme les autres références du plugin : rien n'est découvert dans l'arbre. Les actions sont enfants du composant dans les scènes fournies.
- `InteractionAvailability` remplace `InteractionStatus` : union `InteractionAllowed | InteractionBlocked | InteractionHidden`. Seul `Blocked` porte une raison ; `Hidden` signifie « absent des choix présentés », donc rien à expliquer.
- `InteractionContext` devient action-aware `(Interactor, Interactive, Action)` et `InteractionRule.Evaluate()` retourne une `InteractionAvailability`. Il n'existe plus de chemin d'évaluation sans action : une rule voit toujours l'action évaluée, y compris une rule target-level.
- Le pipeline par action est ordonné : invariants de configuration, réservation, `TargetRules`, puis `Action.Rules`. Le premier résultat non-`Allowed` gagne. (La Task 7 déplace la réservation en **fin** de pipeline, conformément au §7 de la spec.) Une action sans `Definition`, ou qui n'appartient pas au target, est `Blocked("Interaction is not configured.")`.
- `EvaluateStatus()` disparaît. `EvaluateAvailability(interactor, action)` évalue une action ; `EvaluateAvailability(interactor)` agrège les actions du target (Allowed > Blocked > Hidden) pour alimenter la présentation V1, la prévalidation client et la validation serveur jusqu'à la présentation action-aware de la Task 4. Un target sans action est `Hidden` et n'offre aucune interaction.
- L'interprétation `Idle == interactible` quitte le core : `BusyReason`, `ActivatedReason` et la lecture de `Stateful` pendant l'évaluation sont supprimés. `LegacyStatefulInteractionRule` reproduit explicitement l'ancien comportement là où une scène V1 le demande, et disparaîtra avec la Task 12. `InteractiveComponent.Stateful` ne sert plus qu'à invalider le statut sur changement d'état.
- `InteractiveComponent.InteractionRules` est renommé `TargetRules` pour distinguer les conditions du target de celles d'une action.
- `InteractiveActor.tscn` et `Button.tscn` déclarent chacune une action `activate`. L'exécution reste portée par le signal V1 `InteractionInputStarted` jusqu'à la Task 6, et le RPC transporte encore uniquement le target jusqu'à la Task 5.

Cette étape historique a depuis été complétée : `InteractionAction` porte maintenant un
`DefaultBindingConfig` générique optionnel, qui regroupe input, mode, hold, présence et priorité.

### Task 4 — Action-aware presentation and focus

La présentation expose désormais une entrée par action, et le focus ignore les targets sans action présentable.

- `InteractionActionPresentation` porte `ActionId`, `Label`, `Description`, `InputActionName` et l'`Availability` d'**une** action. `IsAllowed` et `BlockReason` y sont per-action : il n'existe plus de statut allowed/blocked au niveau du target.
- `InteractionTargetPresentation` remplace `InteractionPresentation` et porte ce qui reste target-level : `Interactive`, `DisplayName`, `Description`, la liste ordonnée des actions présentables et `IsFocused`. Les actions `Hidden` en sont absentes, les `Blocked` y restent pour être expliquées.
- `HasAllowedAction` est le seul agrégat conservé. Il sert exclusivement à déterminer dans `IndicationScene` si une action est possible, l'indication étant par nature un unique visuel pour tout l'objet.
- `InteractiveComponent.HasVisibleAction(interactor)` alimente le focus : `RecalculateFocusCore()` ignore un candidat dont toutes les actions sont `Hidden`, et le focus bascule alors sur le meilleur candidat suivant. Le focus reste target-level : aucune sélection par action n'est introduite.
- `InteractionStatusChanged` devient une notification pure `(interactive)`. Le résumé `isAllowed`/`reason` disparaît du signal : un consommateur relit `GetPresentation()`, conformément à l'invariant « signals are notifications ».
- La présentation se compose de deux niveaux. Le Presenter instancie `PromptContainerScene` pour la cible focusée — le widget global qui porte le nom de l'objet et expose son `ActionsContainer` — puis y empile une instance de `InteractiveComponent.ActionPromptScene` par action présentée. Sans scène de conteneur, un `VBoxContainer` nu sert de point d'empilement. Le conteneur, les prompts d'action et les indications sont donc trois points d'override indépendants.
- `IInteractionWidget` reste le contrat target-level (`Bind(in InteractionTargetPresentation)`), `IInteractionActionWidget` le contrat par action (`Bind(in InteractionActionPresentation)`), et `IInteractionPromptContainer` ajoute au premier l'`ActionsContainer` recevant les widgets d'action.
- `InteractionPromptWidget` devient le conteneur par défaut (`Content/Label` + `Content/Actions`) et `InteractionActionPromptWidget` le prompt d'une action (`[input] Label` si allowed, `Label: raison` si blocked). `InteractionIndicatorWidget` n'affiche plus de raison : le blocage est porté par la scène d'indication choisie, les raisons appartiennent aux prompts d'action.
- `InteractiveComponent.InteractionActionName` est supprimé (§22) : l'input d'une action vient de
  `DefaultBindingConfig.InputActionName`. `PromptScene` devient `ActionPromptScene` et change de sens
  — un widget par action, plus un widget par target.
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


### Task 7 — Execution lifecycle, concurrence et hold (historique V3)

Une exécution devient une entité identifiée que le target possède, et le chrono d'une action longue passe du gameplay au serveur.

- La V3 avait placé le temps dans l'exécution core. La V4 a supprimé ce stockage : l'exécution ne porte
  plus que son identité, son action, son groupe et son ownership ; les producteurs de présentation sont
  composés séparément.
- La concurrence est un `ConcurrencyGroup : StringName` par action, défaut `"default"`. Deux exécutions d'un même target dans le même groupe s'excluent, deux groupes distincts coexistent. C'est la forme du §14 plutôt que le `bool IsExclusive` de la note de dev : le coût est un export et une comparaison de `StringName`, et ça évite de casser l'API le jour où une inspection doit rester disponible pendant un hack. L'exclusivité ne traverse jamais les targets, ce n'est pas un lock manager.
- **Décision V3 remplacée.** Le chrono n'appartient plus au core Interaction. Une action temporisée
  compose `TimedExecution`, tandis qu'une exécution ouverte reste terminée explicitement par le gameplay.
- **Zéro veut dire « pas d'échéance », pas « instantané ».** Un executor qui retourne `Running` sans durée garde son exécution réservée jusqu'à ce que quelque chose la termine : une animation qui finit, un dialogue qui se ferme, une machine qui se déclare prête. C'est le modèle d'avant, conservé tel quel. Une exécution ouverte ne peut pas réserver un target indéfiniment pour autant : sortir de portée ou relâcher un input soutenu la termine.
- **L'executor est appelé au début, pas à la fin.** L'alternative — attendre la durée puis appeler l'executor — supprimait la restauration d'état à l'annulation, mais supprimait du même coup l'état intermédiaire : la porte qui s'ouvre pendant le channel, le noyau qui passe en `charging`. Cet état intermédiaire est précisément ce qu'on veut, donc la restauration n'est pas du boilerplate mais le comportement voulu, et elle tient en une ligne. On retrouve la forme V1 `start → l'objet fait son truc → end`, avec le chrono côté serveur au lieu de l'objet.
- `InteractionActionExecutor` reçoit `OnExecutionCompleted` et `OnExecutionCancelled`, appelés **directement** sur l'executor qui possède l'exécution. Plus d'abonnement à `InteractionActionCancelled`, plus de filtre `action.Executor != this` ; un test vérifie qu'un executor voisin tournant au même instant n'est jamais notifié de la fin d'un autre. Seule une exécution ayant survécu à son appel `Execute` est rapportée, donc une action instantanée ne voit jamais ces callbacks.
- **Le client ne connaît aucun ExecutionId.** Il dit « j'ai appuyé sur cet input », « j'ai relâché cet input », le reste appartient au système. Sur le fil, le press porte `targetPath + actionId` (inchangé depuis la Task 5) et le release porte le **nom d'input** ; le serveur retrouve seul l'exécution concernée parmi celles que cet interactor possède. Ça supprime l'ack serveur→client, l'ExecutionId sur le fil, la mémoire `input → actionId` de la Task 5, et le cas tordu du tap relâché avant l'arrivée de l'ack.
- L'ownership du release devient structurel : `InteractionInteractor` ne connaît que ses propres `ExecutionId`, donc il ne *peut pas* terminer l'exécution d'autrui. Ce n'est plus une vérification, c'est une impossibilité.
- `InteractionActionDefinition.CancelOnInputReleased` déclare qu'une action est soutenue. Seule une action ainsi déclarée est annulée par le relâchement de son input ; une action instantanée ou auto-portée l'ignore.
- **Seuil de geste ≠ durée d'exécution**, et les deux sont livrés. `HoldThreshold` (sur la Definition) est un seuil local qui existe uniquement pour départager plusieurs actions sur un même input — tap pour ouvrir, maintenir pour défoncer — résolu côté client et n'atteignant jamais la commande autoritaire. la durée calculée par l'executor est la durée d'engagement dont le serveur possède le chrono. « Hold E 5 s pour hacker » est donc un seuil de `0` avec une exécution de 5 s et `CancelOnInputReleased`, pas un geste de 5 s. Empiler les deux additionne les attentes.
- Au seuil, la résolution préfère l'action au **plus long seuil atteint**, avant le rang et la priorité : sans ça, tenir la touche ne pourrait jamais atteindre l'action pour laquelle le seuil existe. Une action soutenue part au seuil et jamais au relâchement, sinon elle naîtrait annulée.
- La V4 sépare la progression d'exécution du geste de sélection. Le requester peut prédire un sample
  initial et le réconcilier à l'ACK ; une action world-observable publie ses samples révisionnés via son
  synchronizer.
- Le retry automatique est traité côté execution : `TryStartAutomaticInteraction` tourne à chaque frame focusée et non plus au seul changement de focus, avec mémoire de la requête en cours. Une action automatique qui devient `Allowed` sans que le focus bouge part donc toute seule, et une action qui reste indisponible ne spamme aucun `InteractionRejected`.
- L'exemple d'action longue tombe de 164 lignes à une quinzaine de code réel : appliquer l'état de départ, et deux callbacks d'une ligne. Le timer, la garde `IsServer`, la référence à l'`InteractiveComponent`, l'`ExecutionId` mémorisé et l'abonnement au signal ont tous disparu.
- Ce qui restait n'avait plus rien d'un exemple, donc `LongActionInteractionExecutor` est promu en brique générique : [`TransitionStateInteractionExecutor`](../../../addons/interaction_plugin/integration/stateful/TransitionStateInteractionExecutor.cs), à côté de `SetStateInteractionExecutor` dont il est le pendant long. Il applique l'état de course au départ, l'état de fin à la complétion, et restaure l'état d'annulation sinon. Il n'a plus de compteurs publics d'instrumentation : les tests de scène observent désormais les signaux et l'état, ce qu'un consommateur réel observerait. Il couvre le cas courant et ne doit pas grossir — une action qui joue aussi un son ou avance une quête écrit son propre executor, libre de réutiliser le même motif à trois états.
- **Décision V3 remplacée.** La V4 n'expose plus de contrat de durée sur l'executor générique. Le helper
  temporisé possède sa configuration, son chrono autoritaire et ses samples ; l'executor générique
  retourne simplement `Running()` jusqu'à une terminaison gameplay.
- Une longueur qui doit être réglable est un `[Export]` de l'executor timed, répondu par sa query : `TimedTransitionStateInteractionExecutor.Duration` → `ComputeTimedDuration`. La donnée reste dans la scène — donc présente sur tous les peers — mais elle traverse le code qui l'utilise, donc l'Inspector, le chrono et la barre ne peuvent plus diverger.
- **Lire un état que le client n'a pas n'est pas interdit, c'est une incohérence choisie.** Un jet de dés serveur ou un inventaire non répliqué donnent une prédiction fausse pendant un aller-retour, puis l'acquittement recale. C'est une note pour l'implémenteur, pas une règle du core : une durée assez importante pour varier se calcule presque toujours depuis des données que tout le monde a déjà. Et c'est le même contrat qu'une `InteractionRule`, qui tourne déjà des deux côtés.
- **L'autorité a le dernier mot sur la longueur, sans rien répliquer.** La durée ne passe par aucun `MultiplayerSynchronizer` : elle voyage une fois, dans l'acquittement `InteractionStarted`, au seul demandeur. Cet acquittement arme la barre si la prédiction avait renoncé, la retime si la query a répondu autrement sur ce peer, et l'efface si l'exécution n'a finalement pas d'échéance. Trois tests réseau couvrent les trois branches (`ARemoteClientDrawsItsBarBeforeTheAuthorityAnswers`, `TheAcknowledgementArmsABarTheClientCouldNotPredict`, `TheAcknowledgementClearsABarTheClientInvented`).
- **Le recalage conserve la valeur visible et ajuste seulement la pente sur le temps autoritaire restant.** Sans compensation, une prédiction parfaitement juste finit un RTT trop tôt : le serveur n'a démarré son chrono qu'après l'aller de la commande et la complétion doit encore revenir. L'ACK garde donc la progression déjà rendue — aucun rewind — puis ralentit ou accélère la pente afin que le temps restant corresponde à l'échantillon autoritaire. `ADelayedAcknowledgementExtendsTheRemainingTimeWithoutVisibleRewind` verrouille cette continuité.
- La prédiction porte l'`ExecutionId` acquitté, `0` tant qu'elle n'est qu'une prédiction, et cette distinction est ce qui rend deux comportements compatibles : un refus efface la barre que le client s'était inventée (le client qui perd la course, `TheClientThatLostTheRaceClearsItsPredictionAtOnce`) mais **jamais** celle d'une exécution acquittée — rappuyer sur le hack qu'on est déjà en train de faire renvoie « This is already in use. » et ne doit pas éteindre sa barre (`ARejectionLeavesTheBarOfARunningExecutionAlone`). Un acquittement terminal l'efface aussi, même sans relâchement d'input, pour qu'une exécution annulée avant l'heure n'aille pas descendre vers une fin qui n'existe plus (`AnExecutionEndedBeforeItsDeadlineTakesItsBarWithIt`).
- **Une exécution en cours bloque son action pour tout le monde, son propre interactor compris**, au lieu de rester `Allowed` pour lui. `Blocked` n'est pas `Hidden` : l'action reste présentée avec sa raison, donc le prompt garde où afficher l'explication et la barre, mais il ne prétend plus proposer une action que le target refuserait aussitôt. Les deux situations gardent des mots différents — « This is already in use. » pour soi, « Someone else is using this. » pour un autre.
- Ce changement a exigé de **remettre la concurrence en fin de pipeline**, là où le §7 la place déjà : invariants → `TargetRules` → `Action.Rules` → concurrence. L'implémentation la vérifiait en premier, ce qui faisait remonter en `Blocked` des actions que leurs rules avaient déjà cachées — une porte fermée se mettait à proposer « Fermer » dès que quelqu'un l'ouvrait. Évaluée en dernier, la concurrence ne touche qu'une action qui serait autrement disponible.
- Conséquence assumée : pendant une exécution, **toutes** les actions du même groupe deviennent `Blocked`, pas seulement celle qui tourne. C'est la définition du groupe par défaut — les actions d'un objet s'excluent — et c'est la raison d'être d'un groupe nommé quand ce n'est pas voulu.
- L'oubli de la requête automatique se fait sur `Hidden` seulement, et non sur tout résultat non-`Allowed`. Sinon l'exécution de l'action automatique elle-même la bloquerait, ferait oublier la requête, et la relancerait à sa propre complétion, indéfiniment. Une action bloquée n'est pas demandée non plus, donc aucun `InteractionRejected` ne part en boucle.
- `InteractionInteractor.GetRelevantInputs()` agrège les inputs qui méritent d'être échantillonnés : ceux des actions présentables de la cible focusée, actions `Automatic` exclues puisqu'aucune touche ne les demande, **plus** ceux que l'interactor a consommés ou croit être en train de soutenir. Cette seconde moitié n'est pas du confort : sans elle, détourner le regard en gardant la touche enfoncée perdrait le relâchement. Le `Character` du projet itère cette liste au lieu de coder en dur `interact`, donc lier une action à une autre touche dans une scène ne demande plus une ligne de code côté personnage. L'interactor n'échantillonne toujours rien lui-même : il informe, et arbitrer entre interagir et autre chose partageant la même touche reste le travail du jeu. Son `InteractionActionName` disparaît.
- **Preuve concrète** : un test monte le scénario multi-phase complet — activer le noyau (`charging` → `primed`), le réactiver avec un objet spécial exigé par une rule (`recharging` → `activated`), puis plus rien d'interactible, pendant qu'un script de quête ouvre une porte en réagissant à l'état `activated`. Les deux phases ne diffèrent que par de la donnée authored : même executor générique, mêmes rules génériques, zéro executor sur mesure.


### Task 8 — Stateful integration primitives

Le monde et l'interaction se rejoignent par deux primitives génériques, sans qu'aucun des deux cores ne connaisse l'autre.

- Les deux primitives vivent dans [`addons/interaction_plugin/integration/stateful`](../../../addons/interaction_plugin/integration/stateful), seul endroit du plugin qui référence `stateful_plugin`. La dépendance est à sens unique et localisée : `StatefulComponent` reste sans aucune notion d'interaction (§4), et supprimer ce dossier suffit à retirer l'intégration.
- `InteractionUnavailableKind` (`Hidden | Blocked`) est ajouté au core parce qu'une union n'est pas exportable dans l'Inspector : une rule qui laisse choisir son refus expose cet enum, et `ToAvailability(reason)` le convertit en `InteractionAvailability`. L'enum ne déclare volontairement pas `Allowed` — une rule qui choisirait « autorisé » en cas de mismatch n'a pas de condition.
- `StatefulStateInteractionRule` (`Resource`) lit un `StatefulComponent` et n'écrit jamais. `ExpectedStates` est une **liste** : plusieurs valeurs décrivent une *phase* et non un instant (`closed` + `opening` = « ouvrir est encore le choix pertinent »). `Invert` inverse la condition, `MismatchAvailability` choisit `Hidden` ou `Blocked`, et `BlockReason` porte la raison affichée.
- `StatefulPath` est résolu **relativement à l'`InteractionAction` qui possède la rule**, ce qui aligne le runtime sur le contexte du sélecteur NodePath de l'Inspector et permet aussi de lire l'état d'un autre objet. Un path vide, non résolvable ou une liste `ExpectedStates` vide sont des erreurs de configuration : la rule renvoie `Blocked("Interaction is not configured.")`, jamais `Allowed`. Le diagnostic editor correspondant arrive en Task 11.
- Deux rules ordonnées décrivent complètement une action, et c'est ce qui rend la liste `ExpectedStates` nécessaire :

  ```text
  RaiseAction
  ├─ phase : ExpectedStates = [lowered, raising]   mismatch → Hidden
  └─ ready : ExpectedStates = [lowered]            mismatch → Blocked("The wall is moving.")
  ```

  La première fait disparaître l'action quand elle n'a aucun sens, la seconde l'explique pendant la fenêtre transitoire. À tout instant une seule des deux actions est présentée : `Raise` pendant `lowered`/`raising`, `Lower` pendant `raised`/`lowering`.
- `SetStateInteractionExecutor` (`Node`) applique un unique `SetState(TargetState)` : `true` → `Completed`, `false` → `Failed`. Il porte une référence de nœud **typée** `Stateful`, donc la cible peut vivre dans une autre scène. Il ne gère volontairement ni animation, ni délai, ni effets multiples, ni audio, ni quête, ni inventaire (§16). Atteindre un état déjà appliqué est un `Failed` et non un succès silencieux : empêcher ce cas est le travail des rules, où le joueur le voit aussi. `_Ready()` signale une cible manquante, un `TargetState` vide, ou un `TargetState` absent du `Schema` de la cible.
- `ButtonInteractionExecutor` est supprimé : le bouton n'a plus aucun script. `Button.tscn` déclare deux actions `raise`/`lower` avec leurs definitions et deux `SetStateInteractionExecutor` (`raising`, `lowering`), et c'est la preuve « Closed door → Open / Open door → Close » du §31 sur une scène réelle.
- `LeverWall` passe sur `StatefulComponent` avec le schéma [`lever_wall_states.tres`](../../../quest_world/interactibles/lever_wall/lever_wall_states.tres) (`lowered`, `raising`, `raised`, `lowering`) et `InitialState = lowered`. Le script ne garde que ce qui est réellement spécifique au mur : la durée d'une transition, qu'il possède côté autorité (`raising` → `raised`, `lowering` → `lowered`), et la géométrie animée. Le bouton, lui, se contente de demander un état.
- L'animation est branchée sur le signal **universel** `StateChanged` et non sur `StateChangedPresentation` comme l'écrit le §24 : le mesh animé porte sa collision, donc le déplacer est de la simulation monde et doit aussi avoir lieu sur un dedicated server. Le comportement en offline, client et listen host est identique.
- Le blocage du bouton pendant la montée ne passe **pas** par la réservation d'exécution : l'action est instantanée et libère le target immédiatement, et c'est l'état du monde lu par les rules qui rend le bouton indisponible. C'est l'illustration directe du §1.1 — le cycle de vie d'une interaction et l'état du monde sont indépendants.
- Les tests de la porte (`BuildDoorWorld`) tournent désormais sur les vraies primitives : le faux `DoorState` et la fausse `DoorStateInteractionRule` sont supprimés, remplacés par un `StatefulComponent` et la rule générique résolue par NodePath.

**Point ouvert assumé.** Une `InteractionRule` est une `Resource`, donc Godot ne peut pas lui faire porter une référence de nœud typée, et une sous-ressource déclarée dans un prefab est partagée par toutes ses instances sans override possible par instance. Conséquence : une rule qui lit l'état d'un **autre** objet appartient au level, pas au prefab. `test_world.tscn` déclare donc les quatre rules du bouton et les deux cibles d'executor, sous `[editable path="Level/Button"]`, exactement comme le câblage inter-objets de la Task 6. L'alternative « la rule lit le stateful ciblé par l'executor de son action » supprimerait le NodePath mais ferait dépendre une rule du *type* de son executor, soit le couplage implicite que la V2 supprime. Le NodePath explicite est donc conservé, et la question sera rebrainstormée à l'usage.

Restent volontairement absents de cette étape : les diagnostics editor des nœuds d'intégration (Task 11), les combinateurs `AllOf`/`AnyOf`/`Not` (Task 9), l'`ExecutionId`, `ConcurrencyGroup` et le hold (Task 7), et la suppression d'`InteractionStateful`, de `LegacyStatefulInteractionRule` et de `InteractiveComponent.Stateful` (Task 12).


### Task 12 — V1 compatibility layer removed

L'interaction ne connaît plus aucun état monde : elle ne le lit qu'à travers des rules pures.

- Supprimés : `InteractionStateful`, l'enum `InteractionState`, `InteractionSavedState`, `InteractiveComponent.Stateful`, `LegacyStatefulInteractionRule`, et l'entrée `InteractionStateful` de l'`InteractionValidator`. Le dossier `runtime/state/` disparaît de l'addon. Les autres éléments de la liste du §12 (`StartInteractionPhase`/`EndInteractionPhase`, `InteractionInputStarted`, `IStatefulProvider`, `ActivatedReason`/`BusyReason`, l'hypothèse `Idle == interactible`) étaient déjà partis aux Tasks 3 à 6.
- L'invalidation de statut par changement d'état disparaît **sans remplacement**, et c'est un choix vérifié plutôt qu'un oubli. La présentation est un modèle *pull* : le presenter re-`Bind()` un `GetPresentation()` frais à chaque frame depuis son propre `_Process`, prompt comme indications (Task 13). Une condition gameplay qui bascule — un dialogue qui démarre, une quête qui avance — apparaît donc à la frame suivante sans qu'on ait besoin de pousser quoi que ce soit. `InteractionStatusChanged` a été émis inconditionnellement à chaque frame focusée jusqu'à la Task 13, ce qui donnait cette fraîcheur par accident ; il y est redevenu un événement. `NotifyStatusChanged()` reste `internal` et n'est plus émis que par les transitions d'exécution.
- Le coût de cette évaluation par frame et le coût de la sélection spatiale sont deux sujets séparés, et il faut les garder séparés. Évaluer les rules d'une poignée d'objets déjà filtrés est négligeable — ce sont des comparaisons de `StringName` et des lectures de nœuds. Le coût réel vit dans la couche spatiale (distance, angle, et l'occlusion de la Task 10), dont le travail est justement de réduire les candidats **avant** qu'on évalue quoi que ce soit. C'est donc à la sélection spatiale d'être économe, pas au pipeline d'availability de devenir paresseux.
- Un test le prouve directement : une rule qui passe à `Blocked` sans aucun composant d'état, sans signal et sans invalidation explicite, et le prompt qui suit le changement en une frame avec **zéro** `InteractiveStatusChanged` émis.
- **Limite connue à cette étape, réglée depuis** : une action `Automatic` n'était déclenchée que sur *changement* de focus, donc une action devenant `Allowed` sans que le focus bouge ne partait pas toute seule. Le push V1 ne la rejouait pas non plus — même gate — donc cette étape ne régressait rien. La Task 7 la rejoue à chaque frame focusée.
- L'exemple d'activation longue est migré, déplacé dans `integration/stateful/examples/` avec sa scène et son schéma puisqu'il dépend de `stateful_plugin`, et **fusionné en un seul nœud**. `InteractiveActor` et `InteractiveActorExecutor` sont remplacés par un `LongActionInteractionExecutor` unique — promu en `TransitionStateInteractionExecutor` par la Task 7 — et la scène devient `LongActionExample.tscn`. La racine n'a plus aucun script, comme celle de `Button.tscn` depuis la Task 8.
- Cette fusion corrige une incohérence de la Task 6 : l'executor n'y contenait que `Actor.BeginActivation()` et toute la logique — l'état, la durée, la réponse à l'annulation — vivait dans un script à côté. Un executor qui ne fait que déléguer remet le comportement hors du seul nœud que le core appelle, alors que l'invariant du §2.2 est précisément qu'une action a un propriétaire unique de sa mutation. Le nouveau nœud porte le `StatefulComponent`, les trois états (`RunningState`, `CompletedState`, `CancelledState`) et la durée, et il apprend son `InteractiveComponent` par le contexte d'exécution plutôt qu'en fouillant l'arbre.
- Les trois états deviennent `idle`/`activating`/`activated`, et les deux raisons de blocage V1 sont reproduites à l'identique par deux `StatefulStateInteractionRule` ordonnées (`activated` → « This is already activated. », `activating` → « This is busy. »). Le `_Process` est gardé par `Multiplayer.IsServer()` : la V1 laissait un client accumuler la durée puis appeler `SetState`, ce qui produisait un warning par frame.
- Le shape « l'executor rappelle un script gameplay existant » reste légitime — une porte qui possède déjà son `AnimationPlayer`, un ascenseur, une machine — et le §15 l'autorise explicitement. Il n'est simplement plus illustré par un exemple où il n'apportait rien ; `LeverWall` montre l'autre moitié du tableau, un script gameplay qui réagit à l'état sans être appelé par un executor.
- Les tests d'état dupliqués entre les deux addons sont supprimés au profit de `StatefulBehaviorTest`, qui couvre déjà la frontière core/dispatch, les scopes de signaux, le schéma et le snapshot/restauration de `StatefulComponent`. Le monde de test `BuildWorld` passe sur `StatefulComponent` et sur les rules génériques, et `WorldStateAndInteractionStayIndependent` vérifie par réflexion que les types V1 ont bien disparu de l'assembly et que `InteractiveComponent` n'expose plus de `Stateful`.

Restaient volontairement absents de cette étape : l'`ExecutionId`, `ConcurrencyGroup`, `CancelOnInputReleased` et le hold, tous livrés par la Task 7 ci-dessus. Restent hors périmètre les combinateurs de rules (Task 9), l'occlusion (Task 10) et les diagnostics editor des nœuds d'intégration (Task 11).


### Task 11 — Editor diagnostics

- `InteractionValidator` couvre maintenant huit types au lieu de quatre : il ajoute `InteractionAction`,
  `InteractionActionDefinition`, `SetStateInteractionExecutor` et `StatefulStateInteractionRule` aux
  composants principaux déjà validés. La résolution de type et la lecture des propriétés continuent de
  passer par la classe globale / le chemin de script et par `Object.Get()`, donc les diagnostics
  fonctionnent sur les placeholders editor sans rendre le runtime `[Tool]`.
- Les diagnostics du §26 sont livrés : target sans action, entrée nulle dans `Actions`, action sans
  `Definition` ou sans `Executor`, `Id` vide, `Id` dupliqué sur un même target, deux actions non
  automatiques partageant le triplet (`InputActionName`, `HoldThreshold`, `Priority`) — le couple seul
  était un faux positif corrigé par P2.3, puisque le resolver départage par disponibilité puis priorité
  et que seul un ex æquo laissé à l'ordre des identifiants mérite un warning —, action non automatique sans
  input, `ConcurrencyGroup` vide, `StatefulPath` vide ou non résolvable, `ExpectedStates` vide, état
  absent du `StateSchema` assigné (rules et executors d'état), et `Duration`/`HoldThreshold` négatifs.
  `InputActionName` est aussi confronté à l'`InputMap` du projet.
- Le `StatefulPath` d'une `StatefulStateInteractionRule` est **résolu depuis l'`InteractionAction` qui
  possède la rule**, jamais depuis l'interactive ni depuis la rule inspectée seule : le path est relatif
  à l'action (§Task 8), donc l'action est le seul objet capable de dire qu'il ne pointe sur rien.
  Inspecter la rule ou l'action isolée ne signale que ce qui est vérifiable sans arbre (path vide, liste
  vide). La résolution est également gardée par `IsInsideTree()`, ce qui évite un faux positif hors scène
  éditée.
- Les diagnostics croisés (id dupliqué, conflit d'input) vivent sur l'`InteractiveComponent` parce
  qu'une action seule ne connaît pas ses voisines. Une action dont la `Definition` est absente est
  signalée puis ignorée pour ces croisements, afin de ne pas produire une cascade de warnings dérivés.
- Deux tests de configuration qui attendaient zéro warning sur un interactive sans action utilisent
  maintenant un helper qui déclare une action complète : « aucune action » est désormais une erreur de
  configuration, ce qui était l'intention du §26.
### Task 10 — Détecteur remplaçable et présence de l'interacteur

- La détection sort de `InteractionInteractor` et devient un Node abstrait `InteractionDetector` exporté
  sur lui (`runtime/detection/`). Les quatre modèles de détection envisagés sont un seul pipeline —
  source de candidats, prédicats, sélection — dont seule la source varie : c'est donc la source qui est
  remplaçable, pas « le système d'interaction ». La fenêtre de portée/angle et le scoring par défaut
  vivent sur la classe de base, avec `ViewOrigin`, `InteractionOrigin` et `DistanceScoreCoefficient`,
  qui quittent l'interacteur.
- **Deux rythmes, un seul code.** Le client propriétaire déroule la boucle complète par frame
  (`GetCandidates` → `Detect` → `Score`) ; le pair autoritaire n'appelle que `Detect` sur une cible
  unique, pour valider une commande et pour continuer à valider une exécution en vol. La divergence
  client/serveur devient impossible par construction, et c'est pour ça que `Detect` doit rester une
  **fenêtre tolérante** : le serveur voit une transform vieille d'un ping. Un cast, qui est binaire,
  appartiendrait à `GetCandidates`. Comme la source n'est lue que par le client propriétaire,
  l'interacteur le dit à son détecteur (`IsCandidateSourceActive`) : une source qui coûte une requête
  physique cesse de la payer sur toutes les copies distantes du personnage.
- `AreaInteractionDetector` reproduit le comportement actuel **sans aucune migration de scène cible** :
  les areas restent sur la cible, seule capable de les posséder, et l'`InteractiveComponent` pousse ses
  overlaps vers le détecteur de chaque interacteur concerné, sur tous les pairs. Le serveur valide donc
  contre le même overlap que le client sans jamais dérouler la boucle.
- Les paliers de `InteractionDetectionKind` sont **cumulatifs, et monotones dans les deux sens**.
  `Interactible` implique `Indicated`, sinon devenir utilisable retirerait le widget qui disait « il y a
  un truc là » ; et perdre la fenêtre de visée **retrograde** vers `Indicated` au lieu de tomber sur
  `None`. Un objet qu'on cesse de regarder est toujours là : il perd le focus, jamais son indication.
  Le détecteur d'area renvoie donc `Indicated` pour toute cible dans l'une de ses deux areas et hors
  fenêtre, `IndicationArea` authorée ou pas. Masquer l'indication de la cible focusée reste une décision
  du Presenter.
- `GetCandidates` est `abstract` et non virtual-avec-défaut : un registre global n'aurait aucun
  consommateur avant le détecteur de proximité, donc le choix (groupe Godot vs liste statique interne)
  est repoussé jusqu'à ce qu'il ait un client. Le repasser en virtual sera additif.
- Un interacteur sans détecteur ne détecte rien et le dit, dans le validator et au `_Ready` : deviner le
  modèle de détection voulu par le jeu est exactement ce que cette couche refuse de faire. Aucun
  fallback implicite n'est fourni.
- **Validation continue.** L'annulation « l'interacteur a quitté l'area » disparaît des callbacks et
  devient une boucle serveur par frame sur les seules exécutions que l'interacteur possède : son coût est
  borné par le nombre de channels en vol, jamais par le nombre de candidats. Elle réutilise la fenêtre
  qui a accepté la commande, donc s'éloigner **et** se détourner annulent — décidé ainsi parce qu'une
  action qui exige de faire face à l'objet ne doit pas laisser le joueur se retourner pendant qu'il
  hacke, et l'angle reste authorable.
- **`Multiplayer.IsServer()` exige un peer.** Sans peer assigné, l'appel pousse une erreur *et répond
  non*, donc chaque chemin autoritaire se refusait à lui-même hors session — y compris pendant le
  `_ExitTree` d'une fin de partie. Les quatre classes concernées (`InteractionInteractor`,
  `InteractiveComponent`, `StatefulComponent`, `LeverWall`) portent désormais un `IsAuthoritative`
  privé qui traite l'absence de peer comme « je suis l'autorité », ce qui était déjà l'intention
  documentée du Stateful. Le garde est dupliqué plutôt que partagé, pour que les deux addons restent
  indépendants. Voir
  [`godot-multiplayer-isserver-requires-peer.md`](../../memory/godot-multiplayer-isserver-requires-peer.md).
- **Deux axes de soutien, plus un seul.** `InteractionActionExecutor.RequiresInteractorPresence`
  (défaut `true`) sépare le channel lié au joueur du processus lié au monde ; une Definition qui déclare
  `CancelOnInputReleased` l'implique de toute façon. Une exécution qui ne réclame pas la présence n'est
  simplement **jamais enregistrée** par l'interacteur : elle appartient au monde dès son démarrage, donc
  ni la perte de fenêtre, ni le relâchement, ni la sortie d'arbre de l'interacteur ne la terminent. Le
  revers à connaître : sans durée déclarée, elle réserve sa cible jusqu'à ce que le gameplay la complète
  par identifiant. `TransitionStateInteractionExecutor` expose le drapeau (`RequiresPresence`) puisqu'il
  sert précisément les deux usages.
- **Line of sight.** `HasLineOfSight` est un prédicat de la classe de base — pas un détecteur — appelé
  par les trois détecteurs, y compris celui de visée dont le `ShapeCast3D` ne rapporte que des areas et
  ne s'arrête donc pas sur un mur. Le ray part du `ViewOrigin` vers l'ancre, sur les seules layers
  d'`OcclusionMask` (défaut layer 2 « Occluder »), en excluant le corps de l'interacteur et la géométrie
  propre de la cible : s'arrêter sur la cible, c'est l'avoir atteinte. Perdre le LOS renvoie `None` et
  non `Indicated`, seule asymétrie avec la fenêtre de visée et elle est voulue — perdre la fenêtre veut
  dire qu'on regarde ailleurs, perdre le LOS qu'il n'y a rien à regarder.
- **Deux rythmes pour le LOS aussi.** Les rays vivent en `_PhysicsProcess` derrière un cache à
  hystérésis à sens unique (regain immédiat, perte temporisée par `LineOfSightLossGrace`, 0,15 s), donc
  rien ne clignote derrière un poteau et une frame de client coûte un lookup par candidat. Une cible
  encore inconnue est castée **sur le champ** : le pair autoritaire valide une commande one-shot hors de
  toute frame de physique, et différer la réponse refuserait une commande légitime pour une raison
  invisible — le refus dû au ping seul que ce chantier interdit ailleurs.
- **C'est l'occluder qui décide**, donc un seul `OcclusionMask` et il vit sur le détecteur. Un mur porte
  la layer, une grille qu'on veut traverser ne la porte pas, et aucun interactible ne déclare quoi que ce
  soit : occluder est une propriété de la géométrie, pas de la cible. L'area découpée à la main reste donc
  seule juge de la visibilité là où le level designer le décide. Un mask à zéro désactive le LOS sans
  branche de code.

#### Spike — `ProximityInteractionDetector` et `AimInteractionDetector`

**Statut : spike à évaluer, pas un contrat livré.** Les deux sont là pour être essayés dans une scène et
gardés ou jetés sur cette base ; ils ne portent qu'un smoke test chacun, qui partira avec eux. Ce qu'ils
prouvent déjà : **aucune ligne du framework n'a bougé pour les écrire**, ce qui était le test annoncé du
placement du joint. Seul l'ajout du registre et des rayons par cible touche du code existant.

- **Registre** : `InteractiveComponent` tient une liste statique interne des cibles présentes dans
  l'arbre (`_EnterTree` / `_ExitTree`), lue par les détecteurs sans source propre. Choix provisoire de la
  question ouverte : un groupe Godot serait l'idiome §25 mais `GetNodesInGroup` alloue à chaque appel, et
  un détecteur la parcourt par frame.
- **Proximité (C)** : ni area, ni collider, ni événement d'overlap. La portée est un nombre que la cible
  authore (`InteractionRadius` / `IndicationRadius`, `0` = « prends le défaut du détecteur »), donc aucune
  scène existante n'a besoin d'être retouchée pour l'essayer. Le palier d'indication est
  **omnidirectionnel** : savoir qu'un truc est autour de soi ne demande pas de le regarder. Ce que ce
  modèle ne sait pas exprimer, c'est une **forme** — pour ça la cible garde le détecteur d'area.
- **Visée (D)** : un `ShapeCast3D` que le détecteur crée lui-même en enfant (un détecteur est un Node
  précisément pour ça) et qui balaie les `InteractionArea` déjà authorées — donc zéro collider à ajouter.
  `AimRadius` est le pardon : à zéro c'est un rayon précis, élargi il touche encore ce que le réticule
  rate de peu. Le sweep s'arrête à son premier impact, donc il rapporte l'area la **plus proche** et ce
  qui la chevauche là, pas une liste d'objets à des profondeurs différentes — vérifié en scène. Ce qui
  décide du palier et du focus reste la fenêtre et le score, mesurés sur l'**ancre** : viser le bord d'un
  gros objet donne `Indicated`, pas `Interactible`. `Score` classe sur l'**angle** et non sur la distance : viser est une intention plus forte
  qu'être à côté. Le cast tourne en `_PhysicsProcess` et **reste une source** : `Detect` n'est qu'une
  fenêtre, le serveur ne rejoue jamais le cast, sans quoi le ping seul suffirait à refuser une commande.
- **Pour essayer** : remplacer le nœud détecteur sous `InteractionInteractor` par celui qu'on veut et
  réassigner `Detector`. `ViewOrigin` reste à câbler sur le détecteur dans les deux cas.

### Task 13 — Distance et progressions dans la présentation

- Trois grandeurs descendent dans la présentation : `Distance` sur `InteractionTargetPresentation`,
  `HoldProgress` et `ExecutionProgress` sur `InteractionActionPresentation`. Le score brut de la couche
  de détection reste privé — celui d'un détecteur de visée est un angle, celui d'un détecteur de
  proximité un ratio, donc un widget qui le lirait casserait au changement de détecteur.
- La `Distance` est mesurée depuis l'`InteractionOrigin` et remplie par le détecteur
  (`GetInteractionDistance`), qui porte les origines depuis la Task 10. C'est la grandeur que la fenêtre
  de portée applique, donc un widget qui s'anime dessus est d'accord avec le moment où l'interaction
  devient possible.
- Les deux progressions sont **par action** et non par cible, pour qu'un widget d'action n'ait pas à
  refiltrer par identifiant. `HoldProgress` va aux actions qui déclarent un `HoldThreshold` sur l'input
  tenu, **normalisé sur le seuil de chacune** : une barre dessinée autour de la touche atteint un quand
  l'action qu'elle représente devient sélectionnable, ce que la plus courte de deux actions partageant un
  input ne ferait jamais si tout se normalisait sur le seuil le plus long. `HoldElapsed` accompagne le
  ratio parce qu'un widget ne peut pas le reconstruire — le seuil n'est pas dans la présentation — et
  l'interacteur l'expose désormais par `TryGetGestureElapsed`. Une action sans seuil ne rapporte rien,
  puisque rien ne la sélectionne par le maintien. `ExecutionProgress` va à la seule action dont
  l'exécution est prédite localement.
- L'absence est **l'absence de valeur** (`float?`), comme `GetInteractionPresentation()` le fait déjà
  pour l'absence de focus : zéro veut dire zéro, ce qu'une barre qui s'anime doit pouvoir distinguer.
- `IsHoldable` reste disponible au repos et dérive de `HoldThreshold > 0`. La progression d'exécution
  n'est plus une capacité de l'action : elle existe seulement dans un
  `InteractionExecutionPresentation` actif et reste nullable quand aucun producteur ne l'expose.
- Le prompt est **rebindé par frame** depuis `InteractionPresenter._Process`, comme les indications
  l'étaient déjà. Il l'était en réalité déjà par accident, parce que l'interacteur émet
  `InteractionStatusChanged` à chaque frame focusée ; la fraîcheur ne dépend plus de ce signal. Le
  rebind ne fait qu'appeler `Bind` : les widgets d'action ne sont recréés que si leur nombre ou leur
  scène change, sans quoi une barre repartirait de zéro à chaque frame. Le presenter ne s'abonne plus
  du tout à `FocusedInteractiveChanged` ni à `InteractionStatusChanged` (P1) : le pull étant la
  stratégie, s'abonner faisait tourner toute la présentation deux fois sur les frames où quelque chose
  changeait vraiment.
- Le prompt d'action par défaut affiche sa barre de hold dès que `IsHoldable` est vrai, à zéro tant que
  le geste n'a pas commencé. Il n'affiche pas la progression d'exécution ; un widget d'objet ou une
  présentation de jeu dédiée reçoit séparément l'`InteractionExecutionPresentation` optionnelle.
- **`InteractionStatusChanged` redevient un événement.** Il était émis inconditionnellement à chaque
  frame focusée : une notification qui n'annonçait rien de neuf, soixante fois par seconde, et qui
  faisait payer à chaque abonné un snapshot par cible présentée et par frame — le presenter le faisait
  deux fois, une par le signal et une par sa frame. Il n'est plus émis que quand le focus bouge, quand
  une cible entre en détection, et quand le gameplay invalide explicitement. Ce qui le rend possible,
  c'est le rebind par frame ci-dessus : la fraîcheur ne dépend plus de lui, donc un focus stable
  n'annonce plus rien. Un consommateur qui a besoin de savoir qu'une rule s'est mise à refuser tire le
  snapshot, il ne l'attend pas.
- La garde `RemoveWhere(!IsUsable)` de l'interacteur reste, mais derrière un `Count > 0` : une cible qui
  quitte l'arbre prévient déjà ses interacteurs, sauf quand le détecteur a une source que la cible ne
  connaît pas — un registre, un cast — et il faut alors éviter de désenregistrer depuis une instance
  libérée. Le compteur est ce qui la rend gratuite au repos, le prédicat capturant `this` allouant
  sinon un delegate par frame pour un ensemble vide. La liste de retrait du presenter est réutilisée
  pour la même raison.

### Task 14 — Acknowledgement autoritaire côté client

Un signal Godot est local : les quatre notifications de `InteractiveComponent` ne quittaient jamais
l'autorité. Le client demandeur ne connaissait donc que sa prédiction, un éventuel état répliqué et un
RPC de rejet — il ne savait pas génériquement que le serveur avait accepté, ni quand l'action avait
fini. `InteractionInteractor` porte maintenant le lifecycle autoritaire de **sa propre** requête.

- Quatre signaux s'ajoutent à `InteractionRejected` : `InteractionStarted(interactive, actionId,
  executionId)`, `InteractionCompleted(interactive, actionId)`,
  `InteractionCancelled(interactive, actionId, reason)` et `InteractionFailed(interactive, actionId,
  reason)`, alimentés par les RPC `Authority` `ClientInteractionStarted/Completed/Cancelled/Failed`.
- **Un seul terminal par requête** (`Completed | Cancelled | Failed | Rejected`), et `Started` précède
  les trois premiers, jamais `Rejected`. Une action instantanée est donc acquittée `Started` puis
  `Completed`, miroir exact de ce que l'autorité notifie : il n'y a qu'un lifecycle à apprendre.
- **La corrélation est `(target, actionId)`**, pas un numéro de requête. Le demandeur garde un marqueur
  pending par paire, donc une seule requête peut être en vol pour cette paire ; l'`ExecutionId` protège
  ensuite le slot confirmé contre les acquittements terminaux obsolètes.
- **Délivré exactement une fois au peer propriétaire, listen host inclus, jamais diffusé.** Le host
  reçoit l'acquittement par appel local direct plutôt que par RPC, comme le faisait déjà le rejet. Les
  autres joueurs observent le monde par l'état répliqué ou par le système métier : c'est late-join-safe
  là où un acquittement transitoire ne l'est pas, et ça ne divulgue pas une action qui leur est cachée.
- **`Failed` cesse d'être rapporté comme `Rejected`.** L'autorité notifie une défaillance comme
  `Started` puis `Failed` — l'action *a* démarré — alors que le client recevait un rejet, c'est-à-dire
  « ça n'a jamais commencé ». `TryStartInteractionAuthoritatively` retourne donc l'`InteractionExecutionResult`
  au lieu d'un `bool` + `out string` qui écrasait quatre issues en une : seul un `Rejected` produit un
  refus, un `Failed` ayant déjà été acquitté par la cible elle-même.
- **Un refus réconcilie la prédiction.** `ClientInteractionRejected` nettoie le slot local `ExecutionId = 0`
  et le marqueur pending de l'action refusée avant d'émettre son signal. Une requête **automatique** est oubliée elle
  aussi, mais la paire refusée est retenue : l'oublier seule aurait réémis la même requête à la frame
  suivante, transformant un refus en flot. La paire est relâchée dès que la situation change — focus qui
  bouge, action qui quitte les choix offerts, ou gameplay qui invalide la cible.
- **Le relâchement garde son nettoyage optimiste.** `TryEndInteractionInput` vide le slot prédit tout de
  suite, sans aller-retour : la barre appartient au local. L'acquittement terminal qui suit est ce sur
  quoi tout le reste se referme, et le recevoir alors que la prédiction est déjà partie est normal.
- **Les chemins qui traversent le réseau sont nommés relativement à la racine multijoueur**
  (`SceneMultiplayer.RootPath`) et non à la racine de scène. Dans un jeu normal les deux sont le même
  nœud et rien ne change ; elles diffèrent quand plusieurs peers partagent un process, ce qui est
  exactement ce qui rend possible un test avec un vrai serveur et deux vrais clients. Voir
  [`godot-multiplayer-in-process-peers.md`](../../memory/godot-multiplayer-in-process-peers.md).

Deux canaux de présentation coexistent donc, et il ne faut pas les confondre :

| Canal | Portée | Late join | Ce qu'il porte |
| --- | --- | --- | --- |
| `StatefulComponent` répliqué | tous les peers | sûr | ce qui est vrai dans le monde |
| Acquittement autoritaire | demandeur seul | non | ce qui n'est vrai que pour lui, son UI locale |

Une fenêtre locale — vendeur popup, dialogue non bloquant — s'ouvre sur `InteractionStarted` et se
referme sur le terminal, sans aucune session réseau downstream. L'ouvrir sur `InteractionRequested`
serait optimiste : ce n'est qu'une intention que l'autorité peut encore refuser. Et la règle du double
feedback vaut ici comme pour l'état : une UI ouverte par l'acquittement ne doit pas l'être aussi par
l'état répliqué, sinon le host la joue deux fois.


## Interaction V4 — delivered

L'Impl 1 a séparé la présentation de l'action et celle de l'exécution. L'Impl 2 a retiré la sémantique
de durée du core et introduit les producteurs génériques de progression. L'Impl 3 ferme la V4 avec la
visibilité explicite, la réplication des présentations world-observable et les preuves d'extensibilité.

- `InteractionActionPresentation` porte uniquement l'identité, le texte, l'input, la disponibilité,
  l'automaticité et le maintien (`HoldProgress` / `HoldElapsed`). Il n'expose plus la capacité timed ni
  la progression d'exécution.
- `InteractionExecutionPresentation` porte `ExecutionId`, `ActionId` et une progression nullable.
  `InteractiveComponent.GetExecutionPresentations()` renvoie un snapshot dans l'ordre des actions et
  `TryGetExecutionPresentation(actionId, out presentation)` permet un accès direct.
- Toute exécution `Running` de l'autorité et du mode offline alimente un slot local par `ActionId` ; une
  exécution sans producteur expose `Progress = null`. La complétion, l'annulation ou l'échec retire
  immédiatement le slot et émet `ExecutionPresentationChanged(actionId)`.
- Une seconde réservation portant le même `ActionId` est refusée avant l'évaluation de la
  `ConcurrencyGroup`. Les actions de groupes différents restent concurrentes lorsqu'elles ont des
  identifiants distincts.
- `IInteractionActionWidget.Bind` reçoit maintenant l'action et son exécution optionnelle. Le
  `InteractionPresenter` effectue la jointure par `ActionId`; le widget par défaut conserve uniquement
  l'affichage de l'input, de la disponibilité et du maintien.

`ReportExecutionProgress(id, value)` publie une valeur discrète finie et clampée, `null` la retire,
et `SetExecutionProgressSource` / `ClearExecutionProgressSource` branchent une `Callable` locale.
La résolution suit l'ordre `Callable` valide, échantillon linéaire reçu, snapshot publié, puis
`null`; les sources invalides ou non numériques retombent proprement sur ce fallback.

`TimedExecution` porte le chrono autoritaire, la source de progression locale, les échantillons linéaires
et la complétion automatique. Ce helper n'est pas une seconde exécution Interaction : il se compose dans
n'importe quel executor. `TimedInteractionExecutor` garde l'ergonomie d'authoring par héritage avec
`Duration`, `CorrectionInterval` et `RunningTimed(context)`. `InteractionExecutionRunning` reste sans
payload et le core ne connaît ni durée ni type d'executor.

La durée timed est un contrat strictement positif et fini. `0`, une valeur négative, `NaN` ou une
infinité font échouer l'exécution acceptée ; le chemin open-ended utilise explicitement
`InteractionActionExecutor` ou `TransitionStateInteractionExecutor`. `TimedExecution.Start` retourne
un résultat détaillé au lieu d'un booléen ambigu. Son deadline et l'extrapolation utilisent tous deux
le temps monotone réel : désactiver le processing d'un node ne désynchronise pas le chrono serveur de
la barre distante.

Le slot de présentation compose maintenant un `InteractionExecutionProgressState` interne. Cet objet
encapsule callable, valeur publiée, sample linéaire, révision, réconciliation monotone et `Resolve()` ;
`InteractiveComponent` ne calcule plus lui-même `ProgressBase + ProgressPerSecond * elapsed`.

`TransitionStateInteractionExecutor` est la variante générique sans timer : elle attend une terminaison
du gameplay. `TimedTransitionStateInteractionExecutor` réutilise le même cycle d'états et compose
`TimedExecution`. Annulation et échec restaurent tous deux `CancelledState`, afin qu'aucune terminaison
non complétée ne laisse l'objet bloqué dans `RunningState`.

Le requester garde un slot par `(target, ActionId)` avec `ExecutionId = 0` pendant la prédiction et un
marqueur pending même sans progression. L'ACK `InteractionStarted(interactive, actionId,
executionId)` peut transporter un échantillon interne. La valeur visible reste monotone ; seule sa pente
est recalculée pour respecter le temps autoritaire restant. Les
corrections discrètes/timed ciblées passent par un RPC fiable owner-only. Les ACKs terminaux portent
l'identifiant d'exécution afin qu'un message obsolète ne retire pas une exécution plus récente.

`FailExecution(id, reason)` et `OnExecutionFailed` distinguent l'échec de l'annulation, sur l'autorité
comme dans le signal `InteractionActionFailed` et l'ACK `InteractionFailed`.

Chaque action choisit son `ExecutionVisibility` : `RequesterOnly` par défaut, `Replicated` pour une
exécution transitoire observable dans le monde, ou `AuthorityOnly` pour ne montrer aucun slot distant.
Les ACK lifecycle restent envoyés au demandeur dans les trois cas. Une action `Replicated` exige un
`InteractionExecutionSynchronizer` enfant explicitement relié à son `InteractiveComponent`; le
validator signale l'oubli. Ce synchronizer transporte des snapshots révisionnés en ordre d'authoring,
applique les corrections sans rewind, supprime les slots terminés et hydrate un late joiner. Sa
visibilité réseau utilise les API natives de `MultiplayerSynchronizer`.

Cette réplication ne transporte que la présentation transitoire. La vérité durable du monde reste dans
`StatefulComponent`. `LongActionExample.tscn` montre les deux canaux ensemble; Door et Button restent
requester-only car leurs actions sont instantanées et leur état monde est déjà répliqué par Stateful.
Le Door traite une synchronisation comme une pose silencieuse : il seek l'animation correspondant à
l'état sans rejouer l'audio one-shot.

## Integration

1. Pour le Character du projet, `quest_world/character/Character.tscn` dérive de `addons/dummy_character_plugin/Character.tscn` et ajoute `GameplayActionRunner`, `InteractionInteractor` (distance calculée depuis le player propriétaire, direction calculée depuis la caméra) ainsi que `InteractionPresenter`. Le script global `quest_world/character/Character.cs` échantillonne les inputs exposés par le runner, rafraîchit le focus Interaction avant une pression et lui transmet directement les press/release.
2. Pour un personnage custom, ajouter un `GameplayActionRunner` au personnage, lui assigner son `OwnedActionComponent`, puis ajouter `InteractionInteractor` avec un détecteur en enfant (`AreaInteractionDetector` pour le modèle par area) et l'assigner à `Detector` — sans détecteur, l'interacteur ne détecte rien. Assigner `ViewOrigin` **sur le détecteur** vers un `Marker3D` ou une caméra, appeler le refresh de focus/bindings de l'interactor avant une pression, puis appeler `Runner.TryStartActionInput(inputActionName)` / `Runner.TryEndActionInput(inputActionName)`. Itérer `Runner.GetRelevantInputs()` : la liste contient les actions owned et les actions d'interaction focalisées, ainsi que les inputs consommés ou soutenus jusqu'à leur relâchement. Une pression acceptée est consommée jusqu'au release : un changement d'état pendant un hold ne peut donc pas réutiliser la même pression pour lancer l'action devenue disponible. `InteractionOrigin` reste facultatif sur le détecteur et utilise le premier ancêtre `Node3D` comme fallback documenté, ce qui donne le personnage quand le détecteur est enfant de l'interacteur. `MaxDistance` et `MaxAngleDegrees` appartiennent au détecteur d'area, pas à l'interacteur : la portée est une décision du modèle de détection. `OcclusionMask` porte les layers qui bloquent la vue (défaut layer 2 « Occluder ») et `LineOfSightLossGrace` la temporisation de perte ; mettre le mask à zéro désactive le LOS. La géométrie censée occlure doit porter la layer : dans ce projet `test_world` et le mur mobile du `LeverWall` sont en `collision_layer = 1|2`, `facility_blockout` non.
3. Ajouter `InteractionArea`, `InteractionAnchor` et `InteractiveComponent` au propriétaire Node3D, puis assigner `InteractionArea` et `InteractionAnchor` dans l'inspecteur. Ajouter et assigner un `StatefulComponent` (addon `stateful_plugin`) seulement si l'objet a besoin d'un état monde persistant/répliqué. `InteractiveComponent` n'a aucune référence vers lui : seules les rules et les executors le connaissent. `IndicationArea` reste facultatif. Rien n'est à authorer pour le LOS sur la cible : ce sont les occluders qui portent la layer, donc une grille qu'on veut traverser est simplement gardée hors de la layer d'occlusion.
4. Ajouter au moins une `InteractionAction` sous le composant, lui assigner un `GameplayActionDefinition` (`Id`, `Label`, `Description`), un `DefaultBindingConfig` (`InputActionName`, `ActivationMode`, `HoldDuration`, `InputRequirement`, `Priority`), un `Executor` obligatoire, et la référencer dans `Actions`. Un binding `Automatic` utilise un input vide et se déclenche au focus ; les autres modes utilisent l'Input Map. `InputRequirement.Pressed` déclare une action soutenue que le relâchement annule. `HostConcurrencyGroup` décide de quelles autres actions du même host elle s'exclut (défaut `"default"`, donc exclusive de toutes). `ExecutionVisibility` vaut `RequesterOnly` par défaut ; choisir `Replicated` uniquement pour une exécution transitoire que les autres peers doivent voir, puis ajouter un `GameplayActionExecutionSynchronizer` enfant explicitement relié au composant. `AuthorityOnly` conserve les ACK lifecycle mais aucun slot distant. `HoldDuration` sert uniquement à départager plusieurs actions sur un même input, et n'est pas la durée d'exécution. Sans action configurée, le target n'offre aucune interaction. Mettre dans `Action.Rules` les conditions propres à l'action, et dans `TargetRules` celles communes à toutes les actions. Une rule reste une query pure : pour dépendre de l'état du monde, la rule lit l'état, elle ne le modifie jamais. Pour une condition d'état, utiliser `StatefulStateInteractionRule` plutôt qu'une rule custom : une rule par phase présentée, une seconde rule ordonnée pour expliquer la fenêtre transitoire.
5. Écrire un `InteractionActionExecutor` par action, le placer sous l'`InteractionAction` et l'assigner à `Action.Executor`. Quand la mutation est un simple changement d'état monde, utiliser `SetStateInteractionExecutor` : aucun script n'est nécessaire, y compris pour agir sur un objet d'une autre scène. `Execute()` retourne `Completed` pour une action instantanée, ou `Running` pour une action longue. Un executor déclare aussi s'il exige la présence de l'interacteur (`RequiresInteractorPresence`, défaut `true`) : le laisser vrai pour un channel que le joueur soutient, le décocher pour un état que le monde possède une fois lancé (la machine qu'on démarre, la porte qui finit de s'ouvrir), ce que `TransitionStateInteractionExecutor` expose sous `RequiresPresence`. Une exécution qui ne réclame pas la présence survit au joueur qui s'en va, et n'a donc plus que le gameplay pour la terminer. Depuis l'Impl 2, un executor qui veut une échéance hérite de `TimedInteractionExecutor` ou compose directement `TimedExecution` ; le chemin par héritage surcharge `ComputeTimedDuration(context)` et retourne `RunningTimed(context)`. Le helper possède le chrono autoritaire et complète l'exécution générique à l'échéance. Un executor générique retourne `Running()` : le gameplay appelle plus tard `Interactive.CompleteExecution(id)`, `Interactive.CancelExecution(id, reason)` ou `Interactive.FailExecution(id, reason)` avec l'identifiant reçu dans `InteractionExecutionContext.ExecutionId`. La query de durée reste pure parce que le client propriétaire peut aussi prédire la présentation : elle ne doit lire que des données disponibles localement. Dans les deux cas, l'executor apprend la fin de sa propre exécution par `OnExecutionCompleted` / `OnExecutionCancelled` / `OnExecutionFailed`, appelés directement sur lui : il n'a jamais à s'abonner à un signal ni à filtrer les exécutions de ses voisins. Les signaux `InteractionActionStarted/Completed/Cancelled/Failed/Rejected` sont des notifications : les utiliser pour l'audio, la VFX, les quêtes ou l'UI, jamais pour effectuer l'action. Ils restent **locaux à l'autorité** : une UI qui appartient au seul joueur demandeur s'abonne aux acquittements de son propre `InteractionInteractor` (`InteractionStarted`, puis `Completed`/`Cancelled`/`Failed`/`Rejected`), jamais aux notifications de la cible. Pour réagir aux changements d'état, s'abonner au signal universel, autoritaire ou de présentation selon la responsabilité du consommateur.
6. Choisir entre une **exécution** et une **rule** en se demandant ce qu'on modélise. Une exécution dit « cet interactor est engagé avec ce target, maintenant » ; une rule dit « le monde est dans un état où cette action est (in)disponible ». Le test qui tranche : si une rule lit un drapeau qu'un executor pose et retire, la réservation a été réimplémentée à la main — et sans son filet, puisque sortir de portée, relâcher un input soutenu ou quitter l'arbre terminent une exécution alors qu'un drapeau reste posé. Un dialogue avec un PNJ est donc une exécution ouverte : l'executor ouvre le dialogue et retourne `Running()`, la fermeture appelle `CompleteExecution(id)`. Les rules gardent les deux autres portées : une condition propre au joueur, lue sur `context.Interactor`, et une condition de monde, lue sur l'état.
7. Un widget lit les grandeurs nommées de la présentation plutôt que d'aller chercher l'interacteur : `Distance` sur la cible (unités monde, mesurée depuis l'`InteractionOrigin`), puis `HoldProgress` et `HoldElapsed` sur chaque action. Une barre d'exécution éventuelle se lit séparément sur `InteractiveComponent.TryGetExecutionPresentation(actionId, out presentation)` ; `Progress` reste nullable quand aucun producteur ne l'expose. `HoldProgress` est normalisé sur le seuil de l'action lue, donc une barre par action est correcte sans que le widget connaisse ses voisines. Ne pas confondre les deux barres : le hold est la **sélection** entre plusieurs actions d'un même input, l'exécution est l'action elle-même. Les empiler est légal et produit deux barres successives ; un widget qui n'en veut qu'une choisit laquelle, il ne les additionne pas.
8. Ajouter `InteractionPresenter` seulement si une UI est souhaitée, avec `Interactor`, `Camera` et éventuellement `PromptContainerScene`. Assigner `ActionPromptScene` sur le composant pour le prompt d'une action et `IndicationScene` pour l'indication globale de l'objet. L'absence de scène de widget est valide : sans conteneur les prompts d'action sont empilés dans un `VBoxContainer` nu, sans `ActionPromptScene` aucun prompt d'action n'est créé.

Depuis l'Impl 2, un executor qui veut une échéance hérite de `TimedInteractionExecutor`, ou compose
directement `TimedExecution` si sa hiérarchie possède déjà une autre base. Le chemin par héritage répond sa
durée dans `ComputeTimedDuration(context)` et retourne `RunningTimed(context)`. Un executor générique retourne
`Running()` puis termine avec `CompleteExecution`, `CancelExecution` ou `FailExecution`. Pour afficher une
progression de gameplay, l'autorité appelle `ReportExecutionProgress(executionId, value)` ; une progression
locale dérivée utilise `SetExecutionProgressSource(executionId, callable)`. Le renderer lit toujours
`InteractiveComponent.TryGetExecutionPresentation(actionId, out presentation)` et ne connaît pas le
producteur.

## Explicit configuration and validation

Les composants principaux (`InteractiveComponent`, `InteractionInteractor` et `InteractionPresenter`) sont des classes globales Godot. Le plugin editor `InteractionEditorPlugin` enregistre `InteractionInspectorPlugin`, qui délègue toutes les validations à `InteractionValidator` (`InteractionArea`/`InteractionAnchor`, `Detector` sur l'interacteur, `ViewOrigin`, portées et temporisation de perte de vue non négatives sur le détecteur, `Interactor`/`Camera`, et les diagnostics d'actions, de definitions, d'executors d'état et de rules d'état listés en Task 11). `InteractionAnchor` est obligatoire pour tout `InteractiveComponent`. `TransitionStateInteractionExecutor` impose séparément sa référence `Stateful`. La validation de `StatefulComponent` et de `StateSchema` appartient au `StatefulValidator` de son propre addon. Les scripts runtime ne sont plus marqués `[Tool]` pour exposer ces warnings ; leurs gardes et erreurs runtime restent locales, et aucun booléen `IsConfigurationValid` n’est maintenu.

`InteractionInteractor.GetInteractionPresentation()` retourne `InteractionTargetPresentation?`; l’absence de focus est donc représentée par l’absence de valeur. Le Presenter maintient sa propre liste d’indications à partir des signaux `InteractiveIndicationAdded` et `InteractiveIndicationRemoved` — ses deux seuls abonnements — sans lire les collections privées de détection : celles-ci vivent maintenant dans le détecteur, pas dans l'interacteur. Le focus et le statut sont tirés par frame, jamais écoutés.

Les warnings sont compilés sous `TOOLS` dans les scripts du plugin editor et affichés directement dans l’Inspector. `plugin.cfg` charge `editor/InteractionEditorPlugin.cs`, qui couvre les huit types validés. L’Inspector identifie les scripts par leur classe globale ou leur chemin et lit leurs propriétés exportées via l’API Godot, afin de fonctionner avec les placeholders editor sans rendre les composants runtime `[Tool]`. `TransitionStateInteractionExecutor` signale séparément l’absence de sa référence `Stateful`.

## XML API documentation

Les types et membres publics du runtime, des rules fournies et de la présentation possèdent des commentaires XML courts destinés à l’intégration. Ils précisent notamment les appels réservés au serveur, les RPC appelés par Godot plutôt que par le gameplay, les signaux locaux de présentation et les différences entre client, listen host et dedicated server. Les implémentations de `InteractionRule` documentent aussi leur contrainte de pureté et leur double évaluation client/serveur.

## Base scene

[`integration/stateful/examples/LongActionExample.tscn`](../../../addons/interaction_plugin/integration/stateful/examples/LongActionExample.tscn) est le prefab de départ duplicable : zones d'interaction et d'indication, ancre, composant, `StatefulComponent` avec son schéma (`idle`, `activating`, `activated`), synchroniseur et widgets par défaut. **Sa racine ne porte aucun script.** Son action porte un `TimedTransitionStateInteractionExecutor` de 1,5 s qui applique `activating` au départ ; `TimedExecution` complète l'exécution générique à l'échéance et l'executor applique alors `activated` — ou restaure `idle` si l'exécution est annulée ou échoue. Deux `StatefulStateInteractionRule` ordonnées bloquent l'action pendant puis après l'activation. Il vit dans `integration/stateful/` parce qu'il dépend de `stateful_plugin`.

## Persistence boundary

La persistance appartient entièrement à `stateful_plugin` : `StatefulSavedState` porte une version (`1`) et un `StringName`, et `InteractionSavedState` est supprimé avec la Task 12. L’addon d’interaction ne persiste plus rien — une exécution en cours est transitoire et authority-owned, même lorsque sa présentation est répliquée. Aucun fichier, service global ou backend n’est créé ; le projet hôte collecte et stocke les snapshots.

## Validation

```powershell
dotnet format quest-world.csproj
dotnet build
$env:GODOT_BIN = (Get-Command godot).Source
dotnet test
godot --headless --path . --scene res://quest_world/levels/test_world.tscn --quit-after 3 --log-file .godot/test-world-runtime.log
```

Sur macOS, invoquer le binaire Godot Mono par son chemin complet et exporter `GODOT_BIN` vers ce même binaire. Voir [`godot-cli-headless-workflow.md`](../../memory/godot-cli-headless-workflow.md).

Les tests couvrent les trois cas de l’union d’availability, l’ordre `TargetRules` puis `Action.Rules`, l’arrêt au premier résultat non-`Allowed`, la porte dont `Open` et `Close` s’excluent selon l’état du monde, l’agrégation target-level Allowed > Blocked > Hidden, la pureté et la répétabilité de l’évaluation, l’action non configurée ou étrangère au target, le target sans action, l’accès d’une rule au parent gameplay via `context.Interactive`, les raisons de blocage configurées sur la rule, l'exécution par un unique executor quel que soit le nombre d'observers, l'action sans executor bloquée et jamais exécutée, la cible déjà réservée pendant l'appel executor, les séquences `started,completed`, `rejected` seul et `started,cancelled` pour un échec, la réservation conservée par une exécution `Running` puis libérée par `CompleteExecution`/`CancelExecution`, le refus d'annuler l'exécution d'un autre interacteur, les signaux d’état spécialisés, le focus, la réservation concurrente, la prévalidation, la séparation fin de phase/fin d’input, le nettoyage serveur d’un interacteur distant, l’autorité réseau serveur, le chemin offline, le Stateful autonome sans owner, le snapshot/version y compris la restauration d’un état identique, l’invalidation par signal, la scène composable avec son action, la présentation à une entrée par action visible avec omission des `Hidden`, une action `Blocked` présentée avec sa propre raison, le target dont toutes les actions sont `Hidden` ignoré par le focus, le report du focus sur le candidat suivant, l’empilement d’un prompt par action dans le conteneur, le binding des widgets d’action et de conteneur, la multiplicité/exclusivité des indications, la rule d'état générique sur une phase multi-états, son mismatch `Blocked` avec sa raison, son mode `Invert`, ses trois cas non configurés, sa lecture de l'état d'un autre objet, le cycle complet open/close piloté par les seules primitives génériques, les échecs de `SetStateInteractionExecutor` (no-op, cible absente, état hors schéma), le câblage du bouton déclaré par le level, le mur qui atteint seul son état final en possédant sa durée, l'exemple d'action longue dont les deux rules d'état génériques reproduisent les refus V1, sa scène sans script sur la racine, son exécution qui reste réservée puis se complète seule, sa restauration d'état à l'annulation, le prompt qui suit une rule basculant sans aucune notification de statut, l'absence des types V1 dans l'assembly, deux groupes de concurrence distincts coexistant sur un même target, l'executor prévenu directement de la fin de sa propre exécution sans que son voisin le soit, l'action instantanée jamais rapportée comme finissant plus tard, l'identifiant inconnu qui n'annule rien, le relâchement d'input qui ne peut pas terminer l'exécution d'un autre interacteur, l'action automatique rejouée quand elle redevient `Allowed` sans que le focus bouge, le target qui possède le chrono d'un executor à durée et complète seul, l'executor sans durée qui attend un événement externe, le maintien qui sélectionne l'action au seuil le plus long atteint, le relâchement avant le seuil qui sélectionne l'action sans seuil, la progression prédite localement sans réplication, le scénario multi-phase complet monté uniquement à partir de pièces génériques, l'executor qui reprend le chrono à son compte en surchargeant sa durée déclarée, l'exécution ouverte qui reste présentée mais bloquée pour son propre interactor comme pour les autres sans faire remonter l'action que les rules avaient cachée, l'agrégat d'inputs qui couvre les actions présentables de la cible focusée, exclut les automatiques et conserve un input soutenu après la perte du focus, l'overlap d'area qui devient interactible dans la fenêtre et prend le focus, la cible derrière le joueur qui est indiquée au lieu de disparaître, la cible focusée dont on se détourne qui retombe sur son indication sans qu'aucun retrait ne soit émis, la cible trop loin qui retombe sur son palier d'indication, les paliers cumulatifs qui n'émettent ni ajout ni retrait d'indication quand une cible devient utilisable, la cible qui quitte l'arbre et que le détecteur oublie de lui-même, l'area de la cible qui alimente le détecteur de chaque interacteur en overlap à travers une vraie frame de physique, l'interacteur sans détecteur qui ne détecte et ne demande rien, l'exécution possédée par le monde qui survit à la perte de fenêtre puis à la sortie d'arbre de son interacteur, l'action `CancelOnInputReleased` qui reste liée à la présence quoi qu'en dise son executor, la cible hors arbre qui exécute quand même son action faute de peer multijoueur, le mur entre la vue et la cible qui la retire de la détection au lieu de la retrograder, la cible encore inconnue castée sur le champ pour le pair autoritaire, la perte de vue qui attend sa grâce quand le regain est immédiat, la grille gardée hors de la layer d'occlusion qui laisse passer l'interaction, le détecteur sans layer d'occlusion qui ne refuse jamais, la cible que sa propre géométrie n'occlut pas, la distance présentée depuis le corps et non depuis la vue, chaque action tenue qui se remplit sur son propre seuil — la plus courte atteignant un pendant que la plus longue continue — et le maintien qui ne remplit que les actions déclarant un seuil, la progression d'exécution portée par sa seule action, le rebind par frame qui ne recrée aucun widget d'action pendant un maintien, et le focus stable qui ne notifie rien pendant cinq frames là où un dispatch de focus inchangé n'émet plus ni focus ni statut.

`InteractionAckTest` couvre le protocole d'acquittement sur le host, qui est aussi le jeu offline :
l'action instantanée acquittée `started,completed`, la cible et l'identifiant d'action que
l'acquittement corrèle, l'échantillon de présentation timed confirmé puis retimé, la
complétion et l'annulation d'une exécution longue avec sa raison, la défaillance acquittée
`started,failed` au lieu d'un rejet, le refus à la frontière d'exécution qui ne démarre jamais, le host
refusé par sa propre autorité qui l'apprend exactement une fois, le refus qui nettoie la prédiction de
sa seule requête et l'input soutenu qu'elle avait créé, le refus qui laisse la prédiction d'une autre
action intacte, le relâchement qui vide la prédiction sans attendre son acquittement, l'action
automatique refusée qui n'est pas redemandée à la frame suivante puis qui retente dès que le gameplay
invalide sa cible ou que le focus a bougé, et la fenêtre de vendeur qui s'ouvre puis se referme sur le
seul acquittement, ne s'ouvre jamais sur une requête refusée, et se referme sur une défaillance.

`InteractionNetworkTest` monte un vrai serveur et deux vrais clients dans un seul process — une
`MultiplayerApi` et un pair ENet par sous-arbre, chaque branche peuplée seulement une fois son API
attachée — et vérifie ce qu'aucun test en arbre unique ne peut prouver : que les déclarations RPC, les
types de payload, le ciblage et la réplication `MultiplayerSynchronizer` sont justes.

Acquittement : le démarrage acquitté au seul demandeur avec son identifiant et son échantillon de
présentation optionnel, la copie du target résolue dans sa propre branche, la commande exécutée une fois
sur l'autorité et nulle part ailleurs, la complétion
acquittée au seul demandeur, la défaillance qui traverse en `started,failed`, et le refus qui traverse
sans jamais démarrer.

Concurrence : deux clients qui demandent la même action dans la même frame ne démarrent qu'une
exécution — un `started` et un `rejected`, jamais deux de l'un — le perdant qui vide sa prédiction
pendant que le gagnant continue de dessiner la sienne, le relâchement qui libère la cible pour l'autre
client, l'interacteur qui quitte l'arbre côté serveur et libère de même, le joueur que la fenêtre
autoritaire perd et dont l'exécution est annulée alors que l'autre peut commencer, deux clients sur deux
cibles distinctes qui démarrent tous les deux sans rien entendre l'un de l'autre, deux clients sur deux
groupes de concurrence distincts d'une **même** cible qui démarrent tous les deux, et l'action longue
que le chrono de l'autorité complète toute seule.

Stateful : une transition répliquée qui joue le feedback de chaque peer **exactement une fois** —
`StateChanged` et `StateChangedPresentation` partout, `StateChangedAuthority` sur le seul listen host —
un état réécrit à l'identique qui ne réplique rien, deux transitions séparées d'une frame qui arrivent
dans l'ordre et jouent une fois chacune, et deux transitions dans la **même** frame qui n'arrivent que
comme la dernière valeur : une propriété répliquée porte une valeur, pas un historique, ce qui est la
raison pour laquelle une pose s'applique depuis l'état courant et seuls les one-shots suivent les
transitions vécues. Le scénario complet ferme la boucle des deux canaux : une interaction de A mute l'état, cet
état atteint le serveur, A et B, B présente l'action comme busy par la seule rule d'état sans avoir reçu
le moindre événement d'interaction, et pourtant l'acquittement n'est allé qu'à A.

Late join : un quatrième pair qui rejoint la session en cours arrive à l'état courant, ne voit jamais
les états intermédiaires qu'il a manqués, et présente correctement comme busy une action déjà prise —
et il reçoit son état d'arrivée comme la transition `idle > activated`, **marquée
`isSynchronization = true`**. La transition est émise délibérément — c'est elle qui fait jouer son
ouverture à une porte trouvée déjà ouverte, donc qui amène la pose et la collision à la bonne valeur — et
le flag est ce qui permet à un feedback de garder ses one-shots pour un changement vécu. Un second test
tient l'autre sens : un arrivant sur une cible intacte ne reçoit rien à l'arrivée, puis vit sa première
vraie transition avec le flag à faux. `oldState` reste l'état initial et non l'état réellement précédent.
Voir « P0 bis » dans [`interaction-v3.md`](planned/interaction-v3.md) et
[`stateful.md`](../state/stateful.md).

Déconnexion : un pair qui tombe sans que personne ne retire son nœud **libère quand même sa
réservation**. `InteractionInteractor` s'abonne à `MultiplayerApi.PeerDisconnected` et annule ses
exécutions quand le pair qui part est son `OwnerPeerId` : le plugin ne dépend plus de la couche de spawn
du projet, et reste correct si celle-ci dépeuple aussi le joueur, l'exécution étant déjà terminée et un
identifiant n'étant jamais réutilisé. L'acquittement de cette annulation n'est pas envoyé, faute de
destinataire encore joignable. Voir « P0 ter » dans [`interaction-v3.md`](planned/interaction-v3.md).

Le harnais s'auto-garde : il assert que les trois pairs sont bien distincts et que l'executor n'a tourné
que sur l'autorité, sans quoi la suite dégénérerait silencieusement en appels locaux.

## Placement des ancres et LOS

`InteractionAnchor` doit rester au-dessus du sol et hors de toute géométrie portant la layer
d’occlusion. Le raycast LOS part de la vue vers cette ancre ; une ancre légèrement enterrée peut donc
faire classer une cible `None`, avant toute création de widget. En cas d’absence complète d’UI, le
diagnostic doit suivre la chaîne `Area3D → candidat → LOS → tier → signal → widget`. Voir aussi la
[mémoire dédiée au placement des ancres](../../memory/interaction-los-anchor-must-clear-ground.md).

## Assumptions and deferred work

- Lorsqu'une session réseau se termine, `InteractionInteractor.IsLocallyControlled` réutilise son dernier résultat connu lorsque `MultiplayerPeer` devient nul ; le mode offline conserve ainsi le contrôle local sans appeler `GetUniqueId()` hors réseau.
- Le transport reste `SceneMultiplayer`; les personnages/interactables dynamiques doivent conserver des chemins identiques via le système de spawn du projet.
- Stateful et Interaction utilisent chacun un `MultiplayerSynchronizer` explicite. Le premier transporte la vérité durable `ReplicatedState`; le second transporte seulement les slots de présentation des actions marquées `Replicated`. Leurs propriétés techniques exportées restent visibles dans l'inspecteur — un `[Export]` privé garde son flag `Editor`, voir [`godot-private-export-inspector-visibility.md`](../../memory/godot-private-export-inspector-visibility.md). Le gameplay continue de muter l'état par `SetState` et de produire la progression par les API d'exécution, jamais en écrivant les snapshots réseau.
- Godot 4.7.1 Mono charge les assemblies avec .NET 10. Le projet cible donc `net10.0`, conserve `LangVersion=preview` et fournit un shim minimal `IUnion`/`UnionAttribute` pour utiliser le contrat union C# preview sans référence runtime .NET 11. Voir [`godot-dotnet-runtime-target.md`](../memory/godot-dotnet-runtime-target.md).
- Le LOS ne voit que ce qui porte une layer d'occlusion. `test_world` et le mur mobile du `LeverWall` sont passés en `collision_layer = 1|2` ; `facility_blockout` ne l'est pas — c'est un blockout, ses 175 volumes n'occluent donc rien tant qu'ils ne portent pas la layer. Une layer dédiée plutôt que « tout le physique » est un choix : sinon une caisse d'un tas de loot occlut l'ancre de sa voisine, ce qui est physiquement correct et faux en gameplay.
- Les détecteurs de proximité et de visée sont des **spikes** : un smoke test chacun, aucune validation editor spécifique (ils héritent des diagnostics de la classe de base), et le registre statique qu'ils introduisent est un choix provisoire. Le registre est désormais doublé d'un index `area → propriétaire` pour que `FindByArea` ne le parcoure plus, et un détecteur dont la source coûte une requête physique lit `IsCandidateSourceActive` pour ne pas la payer sur les copies distantes du personnage. Les garder demandera de trancher le registre pour de bon et de leur donner une vraie couverture.
- La persistance réelle, les intégrations Quest/Dialog/Inventory, les combinateurs de règles, l'occlusion, les widgets 3D cliquables et les transports hors `SceneMultiplayer` restent hors V1.
- Le Character projet n'a plus de nom d'input codé en dur : il itère `GameplayActionRunner.GetRelevantInputs()` et échantillonne les bindings owned et focalisés. Les noms d'input viennent donc des configurations de binding des scènes, `interact` (touche `E`) étant simplement celui des configurations actuelles.
- L'addon Character générique reste sous `QuestWorld.Character` et ne référence pas `QuestWorld.Interaction`; seule la sous-classe globale du projet compose les deux systèmes.

## Current presentation boundary

La disponibilité d'une action d'interaction est désormais directement un
`GameplayActionAvailability` (`GameplayActionAllowed`, `GameplayActionBlocked` ou
`GameplayActionHidden`) : Interaction ne maintient plus une seconde union ni des conversions.
`InteractionTargetPresentation.Actions` transporte le read model générique
`GameplayActionPresentation`, tandis que `InteractionTargetPresentation` reste le seul niveau propre
à la cible interactive. Le widget d'une action est `IGameplayActionWidget` et son implémentation par
défaut `GameplayActionPromptWidget` appartient à `gameplay_action_plugin`.

La progression de sélection n'est plus reconstruite par Interaction : chaque action liée lit son
`binding.Id` via le `GameplayActionRunner`, qui expose `HoldProgress` et `HoldElapsed` sur son propre
seuil. `InteractionInteractor` ne fournit donc plus de query de gesture ni de seuil agrégé target-level.
