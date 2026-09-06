# Gameplay Action

## Status

The V1 extraction is complete. `gameplay_action_plugin` is the generic execution layer used by
Interaction and by owned player actions such as `Drop Battery`; there is no remaining Interaction
compatibility execution path.

The documentation below describes the current contract. Historical implementation plans are not kept
as roadmap documents once their decisions have been absorbed here.

## Package boundary

`addons/gameplay_action_plugin` owns:

- stable action identity and occurrence ownership;
- ordered availability rules;
- host-local reservations and concurrency groups;
- authoritative execution and terminal lifecycle;
- local input bindings and gesture arbitration;
- requester transport, acknowledgements and prediction;
- transient execution presentation and optional replication;
- a small generic action-presentation read model and default presenter/widget.

The core does not know about Interaction, Inventory, Character, quests, dialogs or persistence.
Stateful adapters live under `integration/stateful`; they are optional integration code, not state
semantics baked into the action model.

## Runtime model

### Definition, occurrence and executor

`GameplayActionDefinition : Resource` contains the stable `Id` used for lookup/network identity plus
optional `Label` and `Description` presentation metadata.

`GameplayAction : Node` is one occurrence owned by one `GameplayActionComponent`. It references one
definition, one executor, an ordered `Rules` collection, a host-local concurrency group and an
execution visibility policy. `InputGameplayAction` adds only an optional `DefaultBindingConfig` so an
owned action may opt into automatic local binding without making input a concern of every action.

`GameplayActionExecutor` is the single command owner. `Execute()` returns:

```text
Completed
Running
Rejected(reason)
Failed(reason)
```

A running execution remains reserved until `CompleteExecution`, `CancelExecution` or `FailExecution`
reaches it. Terminal callbacks are sent directly to the executor that owns the execution; Godot
signals are notifications, not the supported place to perform the gameplay mutation.

### Authoritative host

`GameplayActionComponent` is the concrete host. Authored actions are explicit direct children listed
in `Actions`; runtime `AddAction()` joins the same ordered collection. Registration rejects missing
configuration, duplicate IDs, foreign parents and occurrences already owned by another component.

`EvaluateAction()` is a pure ordered rule pass. `ExecuteAction()` is the authority-only programmatic
entry point: it bypasses requester/access/binding checks, but still applies action rules and host
reservations. Player requests ultimately enter the same execution path after the runner has validated
their requester-specific access.

Reservations are local to one component. One `ActionId` can have at most one active execution and all
actions sharing a `HostConcurrencyGroup` exclude one another. Different components never share a
lock.

Removing an idle action makes it unresolvable and frees it. Removing a running action makes it
unresolvable immediately but keeps its node, ID reservation and transient presentation alive until
that execution terminates. The same ID cannot be re-added during this retiring window.

### Execution context

Rules and executors receive one `GameplayActionContext` containing:

- `ExecutionId` (`0` while evaluating before a reservation exists);
- optional `Instigator`, the gameplay actor the action is attributed to;
- optional `Requester`, present only when a runner requested the action and expects acknowledgements;
- the owning `Component` and current `Action`;
- `Host`, defaulting to the component parent unless explicitly overridden;
- `World`, defaulting to `SceneTree.CurrentScene` unless explicitly overridden.

`GetInstigator<T>()`, `GetHost<T>()` and `GetWorld<T>()` are the integration seam for typed game
context. The framework deliberately does not replace them with a global game manager.

## Input and requester pipeline

`GameplayActionBinding` is local runtime state, not ownership and not replicated authority. It
references an action still owned by its component and snapshots the input configuration used for that
binding:

- `Press`, `Hold`, `Release` or `Automatic` activation;
- optional hold threshold;
- `None` or `Pressed` input requirement;
- priority;
- cleanup source and opaque presentation context.

`GameplayActionRunner` owns the input/request boundary. When `OwnedActionComponent` contains an
`InputGameplayAction` with a `DefaultBindingConfig`, the runner creates/removes that binding with the
action lifecycle. Integrations such as Interaction add external bindings explicitly.

`GetRelevantInputs()` is the game input-loop boundary: a locally controlled runner returns all
non-automatic bound inputs plus inputs still consumed by an active gesture/sustained request. Remote
runner copies return none. `TryStartActionInput()` / `TryEndActionInput()` feed edges into the gesture
resolver.

Hold is a selection gesture, not execution duration. Candidate bindings are captured at the press
edge; `TryGetBindingHoldProgress()` exposes progress for that captured binding only. A timed gameplay
execution is a separate lifecycle owned by `TimedGameplayActionExecutor` or compositional
`TimedExecution`.

Owned actions are always accessible through their runner. An external action names an
`AccessProviderId`; the authoritative runner resolves its own `IGameplayActionAccessProvider`, validates
the RPC sender and access, then lets the host re-run rules/reservations. Client bindings and access
claims never cross the network as proof.

Executors require requester presence by default. An executor may opt out through
`RequiresRequesterPresence == false` when accepted work becomes world-owned and should survive
requester/access loss.

## Execution presentation and networking

A running execution owns a transient `GameplayActionExecutionPresentation` slot with stable execution
and action IDs plus optional progress. Progress can be:

- published discretely with `ReportExecutionProgress()`;
- derived locally from a callable;
- represented as a sparse linear sample and extrapolated from monotonic real time.

The internal presentation store owns reconciliation, revisions and extrapolation so
`GameplayActionComponent` stays the lifecycle owner rather than becoming a network/UI monolith.

`GameplayActionExecutionVisibility` controls transport only:

- `RequesterOnly` — requester acknowledgements, no observer snapshot;
- `Replicated` — included in an explicitly wired `GameplayActionExecutionSynchronizer` snapshot;
- `AuthorityOnly` — no remote presentation slot.

Persistent gameplay truth is never inferred from these slots. The synchronizer transports only
transient execution presentation and never executes actions or replicates dynamic grants.

`GameplayActionExecutionRelation` is local presentation knowledge:

- `RequestedLocally` for prediction/requester acknowledgement;
- `Observed` for generic/replicated observations.

It is not a network field. If the requester later receives the replicated copy of the same execution,
its more informative `RequestedLocally` relation is preserved.

The request payload is intentionally small: component path + stable `ActionId`. The authority returns
started/progress/terminal acknowledgements. Terminal reconciliation includes the `ExecutionId`, so an
old acknowledgement cannot close a newer execution of the same action.

## Generic action presentation

`GameplayActionPresentation` is the read model for one offered binding: identity, label/description,
input, availability, activation mode and optional per-binding hold progress.

`GameplayActionPresenter` presents only bindings owned by the runner's `OwnedActionComponent`; external
bindings remain with their integration-specific presenter. `Hidden` and `Automatic` bindings are not
shown, while `Blocked` remains presentable with its reason. Controls are reconciled by binding ID, not
`ActionId`, so two bindings of the same action remain distinct.

`IGameplayActionWidget` and `GameplayActionPromptWidget` are the default generic widget contract and
implementation. Interaction reuses this action-level read model while adding target-level projection,
focus and indication.

## Architecture decisions

### AD-01 — Share definitions, own occurrences

V0-style action metadata evolved into `GameplayActionDefinition : Resource` plus
`GameplayAction : Node`. Shareable identity/presentation data can be reused safely, while executors,
scene references and runtime ownership stay per occurrence.

### AD-02 — One explicit executor, notifications after the fact

Execution is a command with exactly one configured executor. Signals describe what already happened;
they are not a broadcast fallback where an unknown number of subscribers may mutate gameplay.

### AD-03 — Rules are the only availability extension point

Availability is `Allowed | Blocked(reason) | Hidden`. Action subclasses do not get a second hidden
`CanExecute` path: explicit ordered rules are the complete gameplay availability pass. `Hidden` means
absent from offered choices; `Blocked` remains explainable.

### AD-04 — One execution path, requester is data

Programmatic and player-triggered actions do not have different execution semantics. A requester is
attached only by request transport and means “this runner is waiting for acknowledgements”; there is
no invocation-kind flag.

### AD-05 — Concurrency is deliberately host-local

Reservations are by `ActionId` and concurrency group inside one `GameplayActionComponent`. V1 does not
introduce global locks or a cross-host arbitration service.

### AD-06 — Action removal has a retirement window

Logical removal and node lifetime are separate. A running action disappears from new resolution and
bindings immediately, but its occurrence survives until its accepted execution reaches a terminal
state. This avoids cancelling gameplay as a side effect of ownership cleanup.

### AD-07 — Input binding is local derived state

An action is not an input binding. `InputGameplayAction.DefaultBindingConfig` is only an authored source
for a runner-local binding; bindings are neither replicated grants nor authority evidence. This lets
the same execution model serve interaction, inventory-granted actions and non-input gameplay.

### AD-08 — The runner owns request networking

`GameplayActionRunner` is the single requester/RPC boundary. The server resolves its own component,
action and access provider rather than trusting client-side binding data. Interaction therefore adds
spatial access and bindings without owning a second network execution protocol.

### AD-09 — Progress is presentation, completion is gameplay

Generic progress can be discrete, callable or time-derived, but it never decides whether an execution
has completed. `TimedExecution` is an explicit execution policy that owns a real deadline; arbitrary
progress remains a read model.

### AD-10 — Availability, execution and relation stay separate

“May this be offered?”, “what is running?” and “did this peer request it?” are different facts.
`GameplayActionExecutionRelation` therefore augments transient presentation without changing
availability or adding a second network state.

### AD-11 — Rich game context is explicit and typed

`Instigator`, `Host` and `World` provide the minimum reach needed by real executors without introducing
a static service locator. Integration-specific resolution stays outside the generic core; for example,
Interaction resolves its interactor from the generic instigator through one replaceable seam.

### AD-12 — Presentation ownership follows the generic boundary

Action availability, gesture state, execution presentation and the default action widget belong to
Gameplay Action. Interaction owns only target-specific detection/focus/projection. This is what lets an
inventory-granted action and an interaction share the same runner without pretending they are the same
target UX.

## Deliberately deferred

These are boundaries, not partially implemented features or roadmap commitments:

- gameplay tags, attributes and cooldown frameworks;
- cross-host locks or global concurrency arbitration;
- arbitrary target data / invocation payloads;
- a generic inventory contract;
- standardized menus/HUD policy beyond the small presentation model;
- generic replication of dynamic action grants;
- multiple simultaneous executions for one `ActionId`.

Each should start from a concrete game use case and preserve the V1 ownership, authority and lifecycle
invariants above.

## Open hardening notes

Four review findings remain worth keeping visible because they concern lifecycle/input robustness rather
than new features:

- terminal executor callbacks currently run between reservation release and final notification;
  exception handling should eventually guarantee retirement finalization and terminal notification;
- a world-owned running execution may outlive the requester node stored with it; terminal requester
  notification should explicitly guard or detach a freed requester;
- a gesture snapshots candidate binding IDs on press but re-reads their current cached availability at
  hold/release resolution, so availability may change the winner inside an already-started gesture;
- when a `Press` binding shares an input with a `Release` binding, the gesture resolver currently waits
  for release before resolving that press candidate; this is deterministic but should be confirmed as
  intended semantics or split into explicit press/release snapshots.

They are intentionally documented as hardening work, not as alternate architecture.
