# Gameplay Action

## Status

V1 extraction is complete. Tranches 1 to 4 provide the standalone authoritative host, its full
execution lifecycle, generic progress/timing, optional replicated presentation, local bindings and
gestures, typed access validation, requester prediction, lifecycle acknowledgements, and the
functional migration of Interaction onto these primitives. Tranche 5 closes the extraction: scenes,
Stateful integrations, and editor diagnostics author only the final topology, and the temporary
Interaction compatibility lifecycle is gone.

The approved design is in
[`planned/gameplay-action-system-v1.md`](planned/gameplay-action-system-v1.md).

## Package boundary

`addons/gameplay_action_plugin` owns generic action identity, availability rules, execution,
host-local reservations, and action lifetime. It has no dependency on Interaction, Inventory,
Stateful, Character, Quest, Dialog, or persistence.

Its public namespace is `QuestWorld.GameplayActions`, with action nodes under
`QuestWorld.GameplayActions.Runtime.Actions`, execution helpers under
`QuestWorld.GameplayActions.Runtime.Execution`, bindings under
`QuestWorld.GameplayActions.Runtime.Bindings`, requester routing under
`QuestWorld.GameplayActions.Runtime.Runner`, typed access contracts under
`QuestWorld.GameplayActions.Runtime.Access`, and rules under
`QuestWorld.GameplayActions.Runtime.Rules`.

## Tranche 1 runtime model

### Definition and occurrence

`GameplayActionDefinition` is a reusable `Resource` containing:

- `Id`, the stable gameplay and network identity;
- optional intrinsic `Label` and `Description` metadata.

`GameplayAction` is one owned occurrence. It references exactly one definition, exactly one
executor, an ordered rule collection, a host concurrency group, and its future execution visibility.
Input configuration does not belong to `GameplayAction`. An `InputGameplayAction` may optionally
expose a `DefaultBindingConfig`; the runner uses that resource only as the authored source for a
local binding, whose input fields are snapshot values rather than a live reference.

### Authoritative host

`GameplayActionComponent` is the concrete host. Its exported `Actions` collection registers only
explicit authored direct children during `_Ready`; it never discovers the scene tree recursively.
`AddAction` registers and parents an unowned runtime action, then appends it to `Actions`, which
`RemoveAction` already takes it out of: the declared collection is the ordered action set of the
host, not only what the scene authored, so a consumer reading it never has to guess which runtime
additions it can see. Registration rejects missing definitions, empty IDs, missing executors,
duplicate IDs, invalid parents, and actions already owned by another component; a refused
registration declares nothing. Successful runtime mutations emit `GameplayActionAdded` and
`GameplayActionRemoved` after the action set has changed. `GameplayActionRemoved` is emitted at the
logical removal boundary, before an active action's retiring window ends, so local bindings can be
cleaned immediately.

The main public operations delivered by this tranche are:

- `ResolveAction(ActionId)` for stable host-local lookup;
- `EvaluateAction(ActionId, ...)` for pure ordered rule evaluation;
- `ExecuteAction(ActionId, out ExecutionId, instigator)` for authority-only execution that bypasses
  binding/access checks but preserves rules and reservations;
- `IsActionExecuting(ActionId)` for reservation queries;
- `CompleteExecution`, `CancelExecution`, and `FailExecution` for terminal control of an execution an
  executor left running;
- `RemoveAction(ActionId)` for safe retirement.

There is one way to run an action and where the call comes from does not change it. What separates a
player request is not a different operation but a requester waiting to be acknowledged, and only the
request transport attaches one, through the internal `ExecuteRequestedAction`. An execution therefore
carries a requester exactly when a runner asked for it, which is the single condition the host reads
before sending a started, progress, or terminal acknowledgement — there is no invocation-kind flag.

Execution evaluates only the action's explicit ordered `Rules` collection; action
subclasses have no parallel availability hook. It then reserves the stable action ID, the host-local
concurrency group, and a host-wide execution ID before invoking the one executor. Different
components never share reservations.

Executors return the union `Completed | Running | Rejected(reason) | Failed(reason)`. A completed or
failed synchronous result releases its reservation immediately. A running result remains reserved
until one terminal method succeeds. Every accepted execution notifies its executor exactly once after
the reservation is released, including synchronous completion/failure. Rejection has no terminal
callback. An executor exception is logged, converted to `Failed`, and released rather than leaving a
stale reservation. Terminal calls are idempotent for stale IDs and notify only the executor that owns
that execution.

Null entries in the exported rule array are ignored so they cannot break the ordered evaluation of
the remaining authored rules; editor diagnostics can still report the empty slot.

### Retirement

Removing an idle action makes it unresolvable and queues its node for deletion immediately. Removing
a running action makes it unresolvable for new requests but retains its component ownership, node,
stable ID reservation, and execution presentation until the execution reaches a terminal outcome.
The same ID cannot be registered again during this retiring window. Retirement does not implicitly
cancel gameplay. Removing a locally reconstructed action that only carries replicated presentation
purges that presentation immediately.

## Tranche 1 verification coverage

The generic tests use no Interaction fixture. They cover intrinsic definition metadata, explicit
authored registration, runtime ownership and parenting, invalid and duplicate registration,
multi-host ownership rejection, ordered rule short-circuiting, rule-preserving programmatic
execution, null rule entries, mutation-before-executor dispatch, executor-exception cleanup, one
active execution per ID, same-host group exclusion, different-host independence, distinct same-host
groups, safe retirement, and retirement-time ID reservation.

## Tranche 2 execution lifecycle

### Notifications

The authoritative component emits past-tense Godot signals only after its state mutation and direct
executor callback have completed:

- `GameplayActionStarted` and `GameplayActionCompleted` carry the execution ID, action, optional
  instigator, and optional requester;
- `GameplayActionCancelled` and `GameplayActionFailed` carry the same context plus a reason;
- `GameplayActionRejected` carries the refused context and reason without emitting `Started`.

Godot signals transport execution identifiers as non-negative signed 64-bit values because that is
the engine `Variant` integer representation. Runtime contexts and component methods retain the
existing `ulong` API, whose allocator is explicitly capped at `long.MaxValue`. A refusal before
reservation uses the zero identifier; an executor-boundary refusal carries the short-lived allocated
identifier. Unknown action IDs cannot emit an action-bearing signal and are returned directly as a
rejected result.

Synchronous completion and failure emit `Started` followed immediately by their terminal signal.
`Running` emits `Started` and retains the reservation until exactly one terminal method succeeds.
Stale terminal calls remain no-ops.

### Execution presentation and progress

Every authoritative running execution creates one local
`GameplayActionExecutionPresentation(ExecutionId, ActionId, Progress?)` slot, independently of its
visibility policy. Consumers read a snapshot through `GetExecutionPresentations()` or look up one
action with `TryGetExecutionPresentation()`.

`ReportExecutionProgress()` publishes a finite discrete value clamped to `[0, 1]`, or `null` to clear
generic progress. `SetExecutionProgressSource()` attaches a local callable for derived progress and
`ClearExecutionProgressSource()` returns to the last transported/published value. Linear samples use
a monotonic real-time clock and are extrapolated locally; revisions prevent an older sample from
rewinding a newer value even if it arrives inside a newer transport envelope.

The component remains the public lifecycle owner. Slot state, extrapolation, and snapshot codecs live
in an internal `GameplayActionExecutionPresentationStore` so network/presentation mechanics do not
inflate the action registry and authoritative dispatcher into another monolith.

### Timing

`TimedGameplayActionExecutor` is the author-facing inheritance path. It computes a strictly positive
finite duration, publishes sparse linear corrections, and completes the generic execution at its
deadline. `TimedExecution` exposes the same policy compositionally to executors that already require
another hierarchy. Both use monotonic real time, so disabling the component's process mode does not
freeze completion. Open-ended work continues to return `Running` and is completed explicitly.

### Visibility and synchronization

`GameplayActionExecutionVisibility` controls transport, not whether the authority owns a local slot:

- `RequesterOnly` remains local to the authority until the runner/ACK path arrives in tranche 3;
- `Replicated` is included in snapshots from an explicitly wired
  `GameplayActionExecutionSynchronizer`;
- `AuthorityOnly` never enters those snapshots.

The synchronizer transports current transient presentation only. It neither executes actions nor
replicates dynamic action grants. Snapshot and per-execution revisions reject stale state, absent
entries remove completed executions, and native `MultiplayerSynchronizer` spawn replication gives a
late joiner the current running slot. Persistent gameplay truth remains the responsibility of its
domain component.

## Tranche 2 verification coverage

The standalone generic suite adds lifecycle ordering and uniqueness, all execution outcomes,
running-slot creation/removal, callable and discrete progress, visibility filtering, stale envelope
and stale sample handling, replicated-action removal, active retirement presentation, invalid timed
durations, monotonic timed completion, real ENet observer replication, and late join. Client copies
prove that replication never invokes their executors.

## Tranche 3 requester pipeline

### Bindings and gestures

`GameplayActionBinding` is a local runtime reference to an action still owned by its original
`GameplayActionComponent`. It carries its cleanup source, input name, `Press | Hold | Release |
Automatic` activation mode, optional hold threshold, `None | Pressed` input requirement, priority,
and opaque presentation context. It is neither replicated nor accepted by the authority as proof of
access.

`GameplayActionRunner` exposes bind, unbind, source cleanup, availability query, and binding/source/
action invalidation APIs. Invalidation re-evaluates only the requested cached bindings and emits
`GameplayActionBindingInvalidated`; automatic bindings latch one continuous eligibility window and
competing edges select at most one deterministic winner.

`GetRelevantInputs()` is the input-loop boundary for the game. On a locally controlled runner it
returns every non-automatic bound input, plus inputs whose gesture or sustained request is still
consumed after a binding disappears. It returns no inputs for a remote runner.

`InputGameplayAction` is the opt-in action type for default input binding. A missing
`DefaultBindingConfig` is valid and means that no default binding is created; generic actions remain
free of input concerns.

When a runner has an `OwnedActionComponent`, it observes only that component. At `_Ready` it scans
the actions already registered; later `GameplayActionAdded` signals bind input actions with the
action itself as the cleanup source, and `GameplayActionRemoved` unbinds that source immediately.
This lifecycle also drives local `Automatic` actions through the normal runner request pipeline.

Gesture resolution snapshots candidates at the press edge. A competing hold delays press/release
selection, the longest reached captured threshold wins, and a consumed gesture cannot trigger a
second action before release. `HoldDuration` is only the local selection delay. Gameplay duration is
owned by an executor: a timed action is accepted as `Running`, then completes later from the
authoritative `TimedGameplayActionExecutor` or `TimedExecution` clock.

`GameplayActionInputRequirement.Pressed` is also local. It remembers which accepted request should
receive a cancel command when the originating input is released, even if its binding has since been
removed. Neither the requirement nor the input name crosses the network.

### Access and authoritative execution

Owned actions are requestable through the runner's explicit `OwnedActionComponent`. External actions
must select a named `IGameplayActionAccessProvider` through their authoritative action type. The
server resolves its own component and action, validates the RPC sender against `OwnerPeerId`, asks its
own provider for access, then lets the component re-run gameplay rules and reservations before the
executor.

Long-running player requests are tracked by the authoritative runner. Executors require requester
presence by default; while such an external execution is running, the provider's sustained access is
checked by the server and loss cancels it. Requester teardown or peer disconnection follows the same
executor policy. An executor that overrides `RequiresRequesterPresence` to `false` makes its work
world-owned, so spatial loss and requester departure do not cancel it.

### Network, prediction, and acknowledgements

The reliable request payload contains only the component path and stable `ActionId`. Bindings,
activation modes, hold durations, input requirements, providers, and gameplay rules are never client
claims. The authority returns requester-only started, progress, rejected, completed, cancelled, and
failed acknowledgements.

`GameplayActionRunner` owns the network boundary for both owned actions and actions supplied by an
Interaction interactor. It applies `SetMultiplayerAuthority(ServerPeerId)` to its own node during
`_Ready` (and when that configuration changes). This is required because a character root can inherit
the player peer authority recursively, while the runner's `Authority` acknowledgement RPCs must be
sent by the server. `InteractionInteractor` therefore owns no RPC authority and delegates its local
control query to the runner.

A timed executor can seed a local progress prediction before the round trip. The started ACK replaces
that prediction with the authoritative execution ID and sample; rejection clears it. Active ACK IDs
also guard `AuthorityOnly` actions, which intentionally expose no requester presentation, against
duplicate local requests. Terminal reconciliation is correlated by component, ActionId, and
ExecutionId, so a duplicate or stale terminal ACK cannot close a newer execution.

The replicated execution codec now uses
`Array<Dictionary<string, Variant>>` internally. An untyped Godot array exists only for the immediate
`Variant` deserialization boundary in `GameplayActionExecutionSynchronizer`, where every element is
validated before conversion; malformed input does not consume its snapshot revision.

## Tranche 3 verification coverage

The focused generic tests cover binding ownership and cleanup, strict input configuration, all four
activation modes, tap/hold snapshots, deterministic conflicts, automatic latching and batched
competition, scoped invalidation notifications, release cancellation after binding loss, external
access and sustained-access loss, requester teardown with world-owned survival, prediction,
AuthorityOnly deduplication, stale terminal ACKs, sender spoof rejection, authoritative access
rejection, release cancellation, peer disconnection, and real ENet requester/observer separation.

The tranche gate formats all C# sources and builds with zero warnings or errors. The complete test run
passes 290 of 291 tests; its sole failure is the already tracked Interaction scene regression
`DoorSynchronizationConvergesPresentationWithoutReplayingUnlockAudio`, which expects `RESET` but
receives an empty animation name. No GameplayAction test fails in that run.

## Tranche 4 — Interaction integration

### Ownership and compatibility bridge

`InteractionAction`, `InteractionRule`, and `InteractionActionExecutor` are now the useful
specialization/adapters of the generic contracts. Interaction actions reuse
`GameplayActionDefinition` and `GameplayActionBindingConfig` directly; no empty Interaction data
subtypes remain.
`InteractiveComponent` delegates authoritative evaluation, reservations, execution, progress, and
presentation storage to `GameplayActionComponent`; `InteractionInteractor` delegates bindings,
gesture resolution, request transport, acknowledgements, and sustained execution tracking to
`GameplayActionRunner`.

> Superseded by tranche 5: the bridge and the alias described below no longer exist. This subsection
> records the tranche-4 checkpoint.

Existing scenes are deliberately still accepted during this checkpoint. When an authored
`GameplayActionComponent` or `GameplayActionRunner` is absent, a small deferred migration bridge
installs it and moves/registers the existing action nodes. The bridge refreshes an already-focused
interactor after registration, and the Interaction execution synchronizer initializes after that
deferred installation. These are temporary runtime accommodations, not the final authoring model;
tranche 5 replaces them with explicit scene nodes and removes the parallel legacy lifecycle.

`InteractionAction.ConcurrencyGroup` remains as a tranche-4 compatibility alias for the generic
`HostConcurrencyGroup`. It is removed with the migrated scene properties in tranche 5.

### Spatial access, rules, and bindings

Interaction owns only its domain-specific policy:

- the detector and focus model decide which targets are locally relevant;
- the registered `interaction` access provider revalidates authoritative range/candidate access and
  sustained access for long-running player requests;
- programmatic execution bypasses spatial access while still evaluating target and action rules;
- one dynamic target-rule adapter runs before the authored action rules without copying either
  collection;
- focus creates contextual generic bindings, focus loss cleans their source, and Interaction
  invalidates focused bindings as its pull-style rules change;
- Interaction presentation converts generic availability, lifecycle, progress, and rejection reasons
  without owning a second execution store.

The generic runner is the sole request path. An input release, lost authoritative access, requester
teardown, or peer departure cancels a sustained execution according to the executor's requester
presence policy.

### Tranche 4 verification coverage

The Interaction suites retain behavior-level coverage of focus binding and cleanup, authoritative
out-of-range refusal, programmatic spatial bypass with rule preservation, sustained-access
cancellation, requester/observer visibility, late join, progress, prediction, acknowledgements,
automatic actions, concurrency, and presentation reads projected from the generic store. Obsolete
unit tests that invoked the former private Interaction execution core directly were removed; the
generic component suites now own those lifecycle invariants, while Interaction tests cover the
adapter boundary.

At the tranche checkpoint, formatting and compilation succeed with zero warnings or errors and the
complete suite passes. The previously tracked door synchronization test now asserts the observable
closed pose rather than `AnimationPlayer.CurrentAnimation`, which is empty after seeking past the
very short `RESET` clip on the current Godot runtime.

## Tranche 5 — authored topology and closeout

### Final scene topology

Nothing is installed implicitly any more. The deferred migration bridge and the
`InteractionAction.ConcurrencyGroup` alias are removed, so a scene declares what it uses:

```
Interaction/
  GameplayActions/                     # GameplayActionComponent
    OpenAction/                        # GameplayAction (or InteractionAction)
      OpenExecutor                     # GameplayActionExecutor
  GameplayActionExecutionSynchronizer  # Component = ../GameplayActions
  InteractiveComponent                 # ActionComponent = ../GameplayActions
```

Two host invariants shape that layout, and both are diagnosed rather than guessed:

- an authored action must be a **direct child** of its `GameplayActionComponent`; the host refuses to
  register an action parented elsewhere instead of silently hosting it;
- a consumer that subscribes to the host on `_Ready` — `InteractiveComponent` does — needs the host
  assigned before it enters the tree, which an exported `NodePath` guarantees.

The host is authored **beside** its consumer rather than below it. That is not cosmetic: it keeps
every action at the depth it had before the extraction, so authored relative paths — a
`StatefulStateInteractionRule.StatefulPath`, a stateful executor's `Stateful` — survive the migration
unchanged.

### Stateful integration and diagnostics

The generic Stateful executors moved to `addons/gameplay_action_plugin/integration/stateful` as
`SetStateGameplayActionExecutor`, `TransitionStateGameplayActionExecutor`, and
`TimedTransitionStateGameplayActionExecutor`; nothing about applying a state is spatial. Interaction
keeps only `StatefulStateInteractionRule`, which is a spatial-context rule.

`GameplayActionValidator` owns their diagnostics, including the schema checks the Interaction
validator used to carry: a `TargetState`, `RunningState`, `CompletedState`, or `CancelledState`
absent from the assigned `StateSchema` is reported at authoring time.

### Tranche 5 verification coverage

Configuration and scene suites assert the final topology directly: the project scenes host their
actions under `GameplayActions`, the level still overrides the button's cross-scene `Stateful` paths,
and every required diagnostic is covered. One requester-side lifecycle rule was restored with its
test: a refusal clears a prediction only, and never the bar of an execution the authority already
acknowledged and is still driving.

At the tranche checkpoint, `csharpier format .` and `dotnet build` succeed with zero warnings or
errors, the complete GdUnit4 suite passes (285/285), and the project boots headless with no error or
warning.

## Tranche 6 — execution context roles

`GameplayActionContext` now exposes the four execution roles without coupling the generic framework
to a game's scene layout:

- `Instigator` is the gameplay actor responsible for the action;
- `Host` is the gameplay object owning the action;
- `World` is the current gameplay world root;
- `Requester` remains the request transport source, when a runner requested the action.

The context provides `GetInstigator<T>()`, `GetHost<T>()`, and `GetWorld<T>()` typed helpers. The
`GameplayActionComponent` owns optional `Host` and `World` overrides; when they are absent, the host
falls back to the component parent and the world to `SceneTree.CurrentScene`. `Node.Owner` is not
used for either role. `Component` remains available for framework lifecycle operations and is not
an integration-specific host alias.

The demo `Character` is now the default instigator of its runner, while Interaction derives its
interactor adapter from that instigator. The Battery executors consume `Character.Inventory`, the
typed `QuestWorldWorld`, and its `BatterySpawner` instead of resolving global scene paths.

## Deferred, not partially implemented

V1 deliberately stops at the invariants above. The following are **not** present in any partial form,
and nothing in the current API should be read as a first step towards them:

- **Gameplay tag systems** — granted or required tags consumed by rules. Rules are plain synchronous
  predicates; there is no tag container, no tag matching, and no tag replication.
- **Cross-host locks** — concurrency and reservations are strictly host-local, per `ActionId` and per
  concurrency group. There is no lock spanning two hosts, and no cross-requester cancellation policy.
- **Target data and invocation payloads** — an execution carries an id, an instigator, a requester,
  its host, and its action. It carries no arbitrary target or payload.
- **Generic inventory integration** — the framework still owns no inventory contract. The demo's
  Battery actions are an application-level consumer that reaches the typed instigator and world
  context explicitly.
- **Richer presentation schema** — a definition offers optional intrinsic label/description metadata
  and a binding preserves opaque integration-owned context. That is the whole contract: no generic
  HUD policy, no presenter model, no standardized action menu.
- **Generic grant replication** — a binding is local and never a grant. Granting stays a component
  ownership change, and no replicated grant synchronizer exists.
- **Multiple concurrent executions for one `ActionId`** — one active execution maximum, by design.

Each of these must start from an observed use case and preserve the V1 ownership, authority, and
lifecycle invariants. Their listing here is documentation of a boundary, not a roadmap commitment.

## Action presentation boundary

`GameplayActionPresentation` is now the generic read model for one input-bound action. It carries the
stable identity, player-facing text, input name, `GameplayActionAvailability`, activation mode, and
optional per-binding hold progress. `IsAllowed`, `IsAutomatic`, `IsHoldable`, and `BlockReason` are
derived properties; the activation mode remains the source of truth.

`IGameplayActionWidget` and the default `GameplayActionPromptWidget` live in this addon as well. A
generic presenter may therefore render actions from any `GameplayActionComponent`; an interaction
presenter only projects its target-level view and does not define a second availability vocabulary.

Le `GameplayActionRunner` expose aussi `TryGetBindingHoldProgress(bindingId, out progress, out
elapsed)`. La valeur n'existe que pour un binding `Hold` capturé au début du gesture correspondant ;
elle est normalisée sur son propre threshold. Les bindings ajoutés après le press, retirés pendant le
gesture, ou configurés en `Press`/`Release` ne produisent pas de progression.

`GameplayActionPresenter` consomme cette même liste de bindings et ne présente que ceux dont le
`Component` est le `OwnedActionComponent` du runner. Il ignore les actions `Hidden` et `Automatic`,
conserve les `Blocked`, réconcilie les contrôles par `binding.Id`, et vide sa vue quand le runner n'est
pas local.
