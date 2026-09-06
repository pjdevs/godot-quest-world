# Interaction Execution Presentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement task-by-task.

**Goal:** distinguer proprement une exécution demandée localement d’une exécution observée, et permettre à chaque `InteractionAction` de choisir `Hidden` ou `Blocked` selon que l’exécution concurrente vient de soi ou d’un autre joueur.

**Architecture:** `Availability` reste `Allowed / Blocked / Hidden`. L’exécution gagne uniquement une relation locale `Observed / RequestedLocally`. `InteractionAction` gagne deux petites policies `WhenExecutingBySelf / WhenExecutingByOther`; `InteractiveComponent` les applique uniquement à l’étape concurrency, après les rules comme aujourd’hui. `InteractionAction` est actuellement très petit, donc les deux exports restent bien localisés. 

**Tech:** Godot 4 C#, GameplayAction + Interaction plugins.

**Invariant important:** aucune nouvelle donnée réseau pour la relation. Le requester apprend `RequestedLocally` par prediction/ACK ; la réplication produit `Observed`.

- [x] **Task 1 — Ajouter `GameplayActionExecutionRelation` au read model d’exécution.** Modifier `addons/gameplay_action_plugin/runtime/GameplayActionTypes.cs`, `runtime/execution/GameplayActionExecutionProgressState.cs` et `GameplayActionExecutionPresentationStore.cs`. Introduire :
  ```csharp
  public enum GameplayActionExecutionRelation
  {
      Observed,
      RequestedLocally,
  }

  public readonly record struct GameplayActionExecutionPresentation(
      ulong ExecutionId,
      StringName ActionId,
      float? Progress = null,
      GameplayActionExecutionRelation Relation =
          GameplayActionExecutionRelation.Observed
  );
  ```
  Le slot interne conserve cette relation. `AddPrediction()` crée `RequestedLocally`; `ConfirmRequesterExecution()` force/préserve `RequestedLocally`; une exécution découverte par réplication ou exécution générique reste `Observed`. Cas critique : si le requester reçoit ensuite la réplication du **même `ExecutionId`**, ne jamais downgrader `RequestedLocally` vers `Observed`. Ajouter dans `GameplayActionExecutionTest.cs` les tests `PredictionIsRequestedLocally`, `RequesterAckIsRequestedLocally`, `ReplicatedExecutionIsObserved`, `ReplicationDoesNotDowngradeLocalRequester`. Puis compléter `GameplayActionExecutionNetworkTest`/`InteractionNetworkTest` : requester = local, observer et late joiner = observed.

- [x] **Task 2 — Ajouter les deux outcomes de concurrency sur `InteractionAction`.** Dans `addons/interaction_plugin/runtime/actions/InteractionAction.cs`, ajouter :
  ```csharp
  [ExportGroup("Execution Availability")]
  [Export]
  public GameplayActionUnavailableKind WhenExecutingBySelf { get; set; } =
      GameplayActionUnavailableKind.Blocked;

  [Export]
  public GameplayActionUnavailableKind WhenExecutingByOther { get; set; } =
      GameplayActionUnavailableKind.Blocked;
  ```
  Les defaults `Blocked/Blocked` garantissent **zéro changement de comportement des scènes existantes**. Réutiliser `GameplayActionUnavailableKind`, pas créer un deuxième enum : le refacto précédent l'a déjà généralisé et il exprime exactement `Hidden | Blocked`. Ajouter un test de configuration/defaults dans `InteractionConfigurationTest`.

- [x] **Task 3 — Appliquer cette policy dans `InteractiveComponent.EvaluateAvailability()`.** Ne toucher ni aux rules ni à leur ordre. Conserver :
  ```text
  configuration
  → target/action rules
  → concurrency
  ```
  À l’étape concurrency, remplacer le hardcode `Blocked(SelfReason/OtherReason)` par :
  ```csharp
  GameplayActionUnavailableKind kind =
      startedByInteractor
          ? action.WhenExecutingBySelf
          : action.WhenExecutingByOther;

  return kind.ToAvailability(
      startedByInteractor ? AlreadyRunningReason : SomeoneElseReason
  );
  ```
  Il faut aussi couvrir le **client non autoritaire** : une exécution répliquée vit dans le presentation store, pas dans les authoritative executions. Ajouter côté `GameplayActionComponent` une query interne allocation-free du genre :
  ```csharp
  internal bool TryGetExecutionPresentationInGroup(
      StringName group,
      out GameplayActionExecutionPresentation presentation
  );
  ```
  Sur authority, `startedByInteractor` continue d’être déterminé par l’instigator comme maintenant. Sur client, une exécution visible `RequestedLocally` => self ; `Observed` => other. Si une action est `RequesterOnly`, un observer ne connaît volontairement pas l’exécution et peut rester optimiste jusqu’au refus serveur : **ne pas contourner `ExecutionVisibility` pour ce feature**.

- [x] **Task 4 — Verrouiller la matrice `Self/Other × Hidden/Blocked`.** Dans `InteractionBehaviorTest.cs`, tester au minimum :
  ```text
  defaults:
      self  → Blocked("This is already in use.")
      other → Blocked("Someone else is using this.")

  dialogue-like:
      WhenExecutingBySelf  = Hidden
      WhenExecutingByOther = Blocked

      requester → Hidden
      observer  → Blocked

  fully hidden:
      Self  = Hidden
      Other = Hidden

  inverse:
      Self  = Blocked
      Other = Hidden
  ```
  Ajouter aussi un test où une **rule retourne déjà `Hidden`** alors qu’un sibling du concurrency group tourne : elle doit rester `Hidden`. Ça protège l’invariant existant “rules before concurrency” et empêche cette feature de réintroduire le vieux bug où la concurrency faisait resurfacer une action cachée.

- [x] **Task 5 — Faire enfin exploiter la relation au widget générique.** Dans `addons/gameplay_action_plugin/presentation/ui/GameplayActionPromptWidget.cs`, ne plus traiter mécaniquement tout `Blocked` en rouge. Le rendu “normal/active” devient :
  ```csharp
  bool requestedLocally =
      execution?.Relation
      == GameplayActionExecutionRelation.RequestedLocally;

  if (presentation.IsAllowed || requestedLocally)
  {
      // label normal, no BlockReason, normal colors
  }
  else
  {
      // existing blocked presentation
  }
  ```
  **Ne pas** faire `execution != null => normal`, `Progress != null => normal` ou `RequestedLocally => Visible=false`. Hidden reste entièrement géré en amont : `TryGetActionPresentation()` ne produit déjà aucune entrée pour une action hidden. Le widget custom garde évidemment accès à `execution.Relation` pour afficher un spinner, texte “Hacking…”, progression, etc.

- [x] **Task 6 — Docs + regression finale.** Mettre à jour `docs/feature/gameplay_action/gameplay-action.md` avec la définition locale de `ExecutionRelation`, puis `docs/feature/interaction/interaction.md` avec les deux policies et trois exemples : `Hack = Blocked/Blocked`, `Dialogue = Hidden/Blocked`, action silencieuse = `Hidden/Hidden`. Insister sur :
  ```text
  Availability = peut/doit-on proposer l'action à cet interactor ?
  Execution    = qu'est-ce qui tourne ?
  Relation     = quelle est ma relation locale avec cette exécution ?
  ```
  Finir par toute la suite GameplayAction + Interaction + network, puis formatter C# avec le tooling habituel du repo.

### Les deux endroits où je serais particulièrement vigilant

**1. La réplication ne doit pas écraser `RequestedLocally`.** C’est probablement le seul petit piège technique réel. Pour une action `ExecutionVisibility.Replicated`, le requester peut connaître son execution à la fois via son ACK et via la réplication générale. Le slot doit fusionner ces sources en conservant la relation la plus informative localement.

**2. La policy doit fonctionner avec un sibling du concurrency group, pas seulement la même `ActionId`.** Exemple : `Talk` et `Trade` partagent `npc`. Si `Talk` tourne, l’outcome de `Trade` doit lui aussi appliquer son propre `WhenExecutingBySelf/Other`. On ne veut surtout pas coder la feature uniquement autour de `TryGetExecutionPresentation(actionId)`.
