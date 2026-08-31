# Gameplay Action System V1 — Extraction and Interaction Migration

## Status

Design approved for implementation planning.

This document specifies the first extraction of QuestWorld's generic action execution pipeline from
`interaction_plugin` into a new `gameplay_action_plugin`. It deliberately defines a small action framework,
not a Gameplay Ability System replacement.

## 1. Summary

Interaction already contains a reusable action runtime: stable action identity, pure availability rules, one
explicit executor, authoritative reservations, instant and long-running executions, prediction,
acknowledgements, progress presentation, replication visibility, cancellation, and host-local concurrency.
The inventory spike exposed the missing boundary: carrying a battery may grant a `Drop` action and owning a
potion may enable `Heal`, even when no physical interactive is in front of the player.

The V1 therefore extracts those generic mechanics into `addons/gameplay_action_plugin` and rebuilds
Interaction as a spatial discovery, access-validation, authoring, and presentation integration. Interaction
must remain functionally equivalent after the migration. The implementation also adds generic tests for
owned actions and externally hosted actions, so the extraction is proven without requiring a demo gameplay
integration.

`GameplayActionComponent` is the concrete action host. `ActionHost` is only an architectural role; V1 does
not introduce an `ActionHost` class, interface, or extra scene node.

## 2. Goals

- Execute actions owned by the requester, such as `Heal` or `DropBattery`.
- Temporarily bind actions owned by another component, such as `Open` on a door.
- Drive both cases through one input, prediction, request, acknowledgement, and execution pipeline.
- Support authority-only programmatic execution without a local binding or spatial access check.
- Preserve pure gameplay rules for both player-requested and programmatic execution.
- Preserve instant, open-ended, timed, and externally progressed executions.
- Preserve requester-only, replicated, and authority-only execution presentation.
- Preserve Interaction's current behavior, multiplayer guarantees, presentation, and authoring capabilities.
- Keep ownership, access, rules, input, execution, and presentation as separate concepts.
- Expose primitive nodes and APIs directly so game code can build custom integrations without privileged
  framework hooks.

## 3. Non-goals

V1 does not implement:

- gameplay tags, granted tags, tag queries, effects, attributes, costs, cooldowns, or stacking policies;
- arbitrary target data or replicated invocation payloads;
- requester-wide or cross-host concurrency locks;
- generic replication of dynamically added action nodes or action specifications;
- a global ability catalog, action-spec serializer, or generic grant synchronizer;
- a generic action HUD, prompt system, or opinionated presentation policy;
- Inventory rules, pickup/drop gameplay, or the real `DropBattery` action;
- compatibility aliases whose only purpose is to preserve Interaction-owned names for generic primitives.

These features may be layered later through rules, integrations, granted tags, or a V2 grant replication
system when real gameplay requires them. V1 must not pre-build them.

## 4. Core invariants

1. **The component is the host.** A `GameplayActionComponent` owns action registration, authoritative
   executions, execution IDs, concurrency, and execution presentation slots.
2. **Every action has one stable ID on its host.** Network commands identify an action by
   `GameplayActionComponentPath + ActionId`.
3. **One ActionId has at most one active execution.** Concurrency groups add exclusion between different
   actions; they do not replace per-action uniqueness.
4. **Concurrency is host-local.** A door action blocks only actions on that door's component. It does not
   implicitly block a heal on the player's component.
5. **One action has one executor.** Signals are past-tense notifications, never command dispatch or an
   execution fallback.
6. **Bindings are local references, not grants or permissions.** A binding makes an action available to a
   local input loop. It does not transfer ownership and is never trusted by the authority.
7. **Actions remain owned by exactly one component.** An external binding refers to the owner's component and
   ActionId; it never clones or reparents the action.
8. **Access and gameplay rules are different gates.** Access answers whether this requester may reach this
   action through this request path. Rules answer whether gameplay currently permits it.
9. **Programmatic execution bypasses access, not rules.** A locked door remains locked when opened remotely
   unless authoritative gameplay explicitly changes or bypasses that lock outside the normal API.
10. **Presentation is optional and consumer-relative.** The core exposes queryable state but never decides
    that an action is globally presentable.
11. **Mutation finishes before callbacks.** Core state is reserved or transitioned before executors, signals,
    RPC acknowledgements, or integration callbacks run.
12. **An accepted execution has one terminal outcome.** `Started` is followed exactly once by `Completed`,
    `Cancelled`, or `Failed`. A rejection never emits `Started`.
13. **Dynamic action replication is cause-driven in V1.** Inventory, equipment, quest state, or other
    authoritative systems replicate their own state; integrations reconstruct matching local action nodes.

## 5. Package boundaries

### 5.1 `gameplay_action_plugin`

The new add-on owns only generic mechanics:

- action definitions and action occurrences;
- action components and runtime registration;
- local bindings and input gesture resolution;
- player request routing and authority validation;
- availability rules;
- executors and execution lifecycle;
- timing, progress samples, prediction, acknowledgements, and optional execution synchronization;
- generic configuration diagnostics.

Its public C# namespace is `QuestWorld.GameplayActions`. It does not introduce `Ability`, `Effect`, or GAS
terminology for concepts that are only actions and executions in V1.

It has no dependency on Interaction, Inventory, Quest, Dialog, Character, Stateful, persistence, or a
project-specific transport abstraction.

### 5.2 `interaction_plugin`

Interaction depends on `gameplay_action_plugin` and retains only spatially specific concerns:

- detection, focus, distance, angle, line of sight, areas, and anchors;
- interaction request access and sustained-presence validation;
- target-level interaction rules and interaction-specific typed contexts where still valuable;
- construction and removal of contextual bindings on the local runner;
- interaction labels, descriptions, prompt data, world widgets, and indicators;
- interaction-specific editor validation and authoring facade.

Generic execution code must not depend back on Interaction.

### 5.3 Other integrations

Inventory and later gameplay systems may add or remove actions from a player's component and may create local
bindings for them. They are not part of this implementation. Stateful executors that are no longer spatially
specific should move to a generic GameplayAction/Stateful integration rather than remain Interaction
primitives under a misleading name.

## 6. Runtime model

### 6.1 `GameplayActionDefinition : Resource`

The base definition contains the stable `Id` and only data that is intrinsic to the action definition.
It contains no input mapping and no generic presentation fields.

Specialized frameworks may derive definitions. `InteractionActionDefinition` can retain label, description,
and interaction authoring data, while its stable identity comes from the generic base definition.

V1 definitions do not describe how to instantiate or replicate an action. A future V2 catalog may associate a
stable definition ID with a scene or factory, but V1 makes no such contract.

### 6.2 `GameplayAction : Node`

One action node is one occurrence owned by one `GameplayActionComponent`. It carries:

- its required `GameplayActionDefinition`;
- exactly one `GameplayActionExecutor`;
- ordered `GameplayActionRule` resources;
- `HostConcurrencyGroup`, defaulting to `default`;
- execution visibility;
- generic hooks used by specialized action types for access and additional availability evaluation.

It does not carry runtime input state. Input name, activation mode, hold duration, sustain requirement, and
input priority belong to bindings.

`InteractionAction : GameplayAction` adds only Interaction authoring and adaptation. It must not duplicate the
generic execution lifecycle.

### 6.3 `GameplayActionComponent : Node`

This is the only concrete host in V1. It:

- registers authored and runtime action nodes;
- rejects missing, empty, or duplicate IDs;
- resolves actions by ID;
- owns active execution records and allocates host-wide execution IDs;
- enforces per-ActionId uniqueness and host-local concurrency groups;
- invokes rules and the single executor;
- completes, cancels, or fails active executions;
- owns prediction/reconciliation-facing execution presentation slots;
- optionally exposes those slots through a `GameplayActionExecutionSynchronizer`;
- emits authoritative lifecycle notifications after mutations complete.

The component's network-relative `NodePath`, not its parent entity path and not an action node path, is the
host identity transported by requests and acknowledgements. This permits an entity to own more than one
component without ambiguity, even though the common topology has one.

Authored action nodes are conventionally direct children of the component and are listed in its explicit
authored action collection; the component does not recursively discover actions from the scene tree. Runtime
`AddAction` accepts an unowned action node, establishes the same component ownership, and places it under the
component when necessary. An action cannot be registered with two components.

### 6.4 `GameplayActionRunner : Node`

The runner belongs to one requester/player and owns:

- its explicit `OwnedActionComponent` reference;
- local runtime bindings;
- input gesture state and deterministic conflict resolution;
- local rule/access prevalidation;
- requester prediction and pending requests;
- reliable request RPCs and lifecycle acknowledgements;
- requester-owned execution tracking;
- cancellation caused by input requirements, requester teardown, or specialized sustained-access loss.

The runner never becomes the owner of externally hosted actions. A door execution stays in the door's action
component even when the player's runner requested it.

### 6.5 `GameplayActionBinding`

A binding is a lightweight local runtime record, not a node and not replicated state. It contains at least:

- binding identity;
- the referenced `GameplayActionComponent` and `ActionId`;
- a source identity used for scoped cleanup;
- `InputActionName`;
- `ActivationMode`;
- `HoldDuration` when applicable;
- `InputRequirement` for an accepted running execution;
- absolute authored `Priority`;
- optional source-specific presentation metadata or an opaque reference owned by the integration.

A generic `GameplayActionBindingConfig : Resource` may provide reusable authoring data. Integrations may also
expose their own higher-level facade and copy its values into the runtime binding. In every case the runtime
input mapping belongs to the binding, never to the action.

Binding sources can add and remove their own bindings without disturbing other sources. The source is a
lifecycle/cleanup identity only; it grants no authority and adds no hidden priority bonus.

### 6.6 Add/remove versus bind/unbind

The APIs have intentionally different meanings:

| Operation | Meaning | Ownership | Typical example |
| --- | --- | --- | --- |
| `AddAction` | Add a real action occurrence to a component | Component owns it | Inventory grants `DropBattery` |
| `RemoveAction` | Retire a real owned action occurrence | Component stops offering it | Battery leaves inventory |
| `BindAction` | Add a local input reference to an existing action | Ownership unchanged | Focus door `Open` |
| `UnbindAction` | Remove a local input reference | Ownership unchanged | Leave door focus |

Unbinding prevents new activation but does not cancel an accepted execution. Removing an action immediately
removes it from new resolution and activation. If it still has an active execution, it enters a retiring state
and its node lifetime is deferred until that execution reaches a terminal result. V1 has this single safe
policy rather than a configurable removal matrix. Gameplay that wants cancellation must explicitly cancel the
execution before or while removing the action.

## 7. Availability, access, and invocation context

### 7.1 Generic context

The generic rule/execution context contains only:

- optional instigator;
- optional requester `GameplayActionRunner`;
- owning `GameplayActionComponent`;
- `GameplayAction`;
- invocation kind: `PlayerRequest` or `Programmatic`;
- `ExecutionId` only for execution callbacks.

There is no arbitrary target or payload in V1. The host is the natural target for a door, the player's own
component is enough for a heal or drop, and an attack executor may perform its own hit detection.

### 7.2 Gameplay rules

`GameplayActionRule : Resource` remains synchronous, side-effect free, cheap, and free of mutable runtime
state. Rules return `Allowed`, `Blocked(reason)`, or `Hidden` and run during local presentation/prevalidation
and again authoritatively on the server.

Specialized actions may prepend additional rule layers. Interaction evaluates target rules before action
rules, preserving its existing short-circuit order. Missing requester data during a programmatic invocation is
not fabricated: a rule that requires a requester must return an appropriate unavailable result unless the
caller supplied one.

### 7.3 Request access

Request access is deliberately outside the rule list and never produces presentable `Blocked` or `Hidden`
state. It is a trust-boundary check.

The generic default permits a player request only when the requested host is the runner's
`OwnedActionComponent`. Externally hosted player-requestable actions must opt into an integration-provided
access validator. The runner supports a small registry of typed/named `IGameplayActionAccessProvider`s;
specialized action types select the provider they require. The action resolved by the authority selects the
provider, not client payload data and not the existence of a local binding.

`InteractionInteractor` registers as the Interaction access provider on its associated runner on every peer.
For an `InteractionAction`, the authoritative provider resolves the corresponding `InteractiveComponent` and
uses the server-side detector to validate that it is currently interactible. The same provider may perform
sustained-presence checks for running executions.

This seam is intentionally minimal. It is not a generic permission policy graph, and access providers are not
authorable gameplay rules.

### 7.4 Programmatic execution

`GameplayActionComponent` exposes an authority-only programmatic entry point. It:

- resolves its own action by ID;
- bypasses bindings, sender ownership, access providers, detection, distance, and line of sight;
- evaluates all normal gameplay rules;
- applies per-action uniqueness and host-local concurrency;
- invokes the same executor and creates the same execution lifecycle and presentation state.

The caller may supply an instigator and requester when rules need them. A plain programmatic invocation is not
owned by an input gesture and is not cancelled merely because a player is far away. A separate force/debug API
that bypasses rules is outside V1; authoritative gameplay needing that behavior should change the underlying
gameplay state or call its domain object directly.

## 8. Input model

### 8.1 Activation mode

V1 freezes the following binding enum:

```text
GameplayActionActivationMode
├── Press
├── Hold
├── Release
└── Automatic
```

- `Press` activates on the press edge when no hold disambiguation is needed.
- `Hold` activates once `HoldDuration` is reached. `HoldDuration` must be finite and strictly positive.
- `Release` activates on the release edge if the gesture has not already been consumed.
- `Automatic` activates on the edge where a binding becomes locally eligible, not every frame.

To preserve Interaction's tap-versus-hold behavior, the presence of competing `Hold` bindings defers a
`Press` candidate for that input. The runner snapshots a deterministic gesture-resolution plan at the press
edge. On release it selects the longest reached hold threshold, or the press/release-edge candidates if no
hold threshold was reached. Reaching the longest captured hold threshold selects it without waiting for
release. Availability changes and newly added bindings do not rewrite the captured plan. Once one action wins,
the gesture is consumed until release and cannot activate another action halfway through the same press.

`HoldDuration` is selection time, not execution duration. A timed executor is a separate mechanism. Combining
the two intentionally creates two consecutive waits.

### 8.2 Input requirement

Activation and continued execution are independent. V1 defines:

```text
GameplayActionInputRequirement
├── None
└── Pressed
```

`Pressed` means an accepted running execution is cancelled when the input that activated it is released.
`None` means release does nothing to that execution. Additional required states are deferred until a real use
case exists.

The resolved requirement and originating input are captured into the execution at acceptance. Losing or
replacing the binding later cannot erase the execution's input-lifetime contract.

### 8.3 Conflict resolution

One input gesture may activate at most one action. At each applicable activation edge, the runner filters
bindings by input, activation mode/gesture phase, validity, and local availability, then orders them by:

1. `Allowed` before `Blocked`; `Hidden` is absent;
2. highest absolute authored `Priority`;
3. stable ascending `GameplayActionComponentPath + ActionId` tie-break.

Priority has no implicit source, focus, Interaction, Inventory, or ownership bonus. Equal-priority conflicts
remain deterministic but must emit an editor/runtime diagnostic so designers can resolve them explicitly.
Blocked winners are not requested; their result remains available to presentation or feedback.

The candidate plan and, once resolved, the winning binding are captured for the gesture. The runner does not
re-query the live binding set every frame. If the winning/candidate binding disappears before activation, it
is removed from that fixed plan; the gesture aborts when no candidate remains. If execution was already
accepted, binding loss alone does not cancel it.

## 9. Execution lifecycle

### 9.1 Results and callbacks

`GameplayActionExecutor` preserves the existing four execution results:

- `Completed`: finished synchronously;
- `Running`: accepted and reserved until later completion;
- `Rejected(reason)`: refused at the executor boundary before start; expected to remain rare;
- `Failed(reason)`: accepted, started, then failed.

Executors receive direct terminal callbacks for their own execution. They never subscribe to global signals to
discover whether their work ended.

### 9.2 Reservation and concurrency

Before calling an executor, the component reserves:

- the action's `ActionId`;
- the action's `HostConcurrencyGroup`;
- a new host-wide `ExecutionId`;
- requester/input/sustained-access lifecycle data;
- the initial execution presentation slot when one exists.

An execution starts only when both conditions hold:

```text
no active execution for this ActionId
and
no active execution in this HostConcurrencyGroup
```

The first condition preserves logical uniqueness even when actions use different groups. The second models
explicit exclusion between different actions on one host. Group names never coordinate different components.

### 9.3 Requester and sustained access

A running executor may require the requester to remain present. Player-requested executions with this policy
are tracked by the authoritative runner. Interaction supplies sustained detection through its access provider,
preserving cancellation when the player leaves the valid interaction window. An executor may declare the work
world-owned, in which case requester departure and access loss do not cancel it.

`GameplayActionInputRequirement.Pressed` always keeps the execution requester-owned until release, regardless
of the executor's world-owned preference. Peer disconnection and requester teardown cancel requester-owned
executions exactly as Interaction does today.

Programmatic invocations do not acquire Interaction presence or input requirements.

### 9.4 Timed and externally progressed executions

The generic add-on owns the current timing policy:

- `TimedGameplayActionExecutor` for the inheritance path;
- composable `TimedExecution` for executors with another hierarchy;
- monotonic real-time authority clock;
- sparse linear progress samples and local extrapolation;
- automatic authoritative completion;
- strict rejection of zero, negative, NaN, or infinite durations.

Open-ended executors may publish discrete normalized progress such as `0.33`, `0.66`, and `1.0` without a
linear timer. Presentation consumes the same nullable generic progress slot in both cases.

## 10. Network and prediction

### 10.1 Player request flow

```text
local input edge
→ resolve and capture one binding
→ local access/rule prevalidation
→ create optional prediction
→ reliable request to the requester's GameplayActionRunner
→ authority validates sender and resolves host path + ActionId
→ authority validates request access
→ authority evaluates gameplay rules
→ host checks ActionId and HostConcurrencyGroup reservations
→ host reserves and invokes executor
→ requester receives rejection or started acknowledgement
→ terminal acknowledgement reconciles requester state
```

The authoritative validation order is fixed:

1. RPC sender and runner ownership;
2. host path and action ID resolution;
3. non-presentable request access;
4. gameplay rules;
5. host-local ActionId and concurrency reservations;
6. executor invocation.

Unknown and hidden actions use neutral refusal text so network probes do not reveal undiscoverable actions.

### 10.2 Prediction and acknowledgement

The migration preserves the current Interaction guarantees:

- a requester may create one local predicted execution slot before the authority replies;
- the started acknowledgement reconciles it to an authoritative execution ID;
- rejection removes only the matching unacknowledged prediction;
- corrections are monotonic and cannot rewind progress through competing transport paths;
- terminal acknowledgements remove only the matching `(host, ActionId, ExecutionId)` slot;
- late or stale acknowledgements cannot end a newer execution.

The existing request correlation assumption remains valid because one ActionId may have only one active or
pending requester execution in the supported pipeline.

### 10.3 Execution visibility

`GameplayActionExecutionVisibility` preserves:

- `RequesterOnly` as the default;
- `Replicated` through an explicitly wired `GameplayActionExecutionSynchronizer`;
- `AuthorityOnly` while still sending lifecycle acknowledgements to the requester.

The synchronizer transports transient execution presentation samples, not execution authority or durable
world state. Persistent truth remains in Stateful or another domain component.

### 10.4 Dynamic action replication

V1 never serializes or spawns an arbitrary action node over the network. The cause of a grant is replicated:

- Inventory quantity changes;
- equipment state changes;
- quest or progression state changes;
- authored world scenes instantiate matching action components on each peer.

An integration observes that cause and constructs the same stable ActionId locally. The server always resolves
its own authoritative action occurrence. The component's network-relative path must resolve to the matching
component on every peer. Action node names and topology below that component may differ because only the
component path and ActionId cross the request protocol.

If a client's reconstructed action arrives early or late, the server's current authoritative registry wins and
may reject the request. A possible V2 may add a catalog-backed grant synchronizer using stable definition IDs;
that is an optimization/convenience layer, not a V1 correctness requirement.

## 11. Presentation

The generic core provides no widgets and no global `IsPresentable` flag. It exposes:

- the runner's current bindings;
- availability query APIs;
- hold/gesture progress for bindings;
- host execution presentation by ActionId;
- lifecycle and invalidation notifications.

A consumer decides which subset to render. `Heal` may remain permanently input-bound and never appear in a
HUD. `DropBattery` may appear while the battery action exists. A radial menu may present the same action that a
minimal HUD hides.

Interaction retains its opinionated world presentation because it owns meaningful spatial behavior: focused
prompt containers, per-target indicators, projection from an anchor, distance, action labels, and interaction
widget scenes. It may read generic binding and execution state, but those widgets do not move into the generic
add-on.

## 12. Interaction migration

### 12.1 Target topology

The intended composed scene is:

```text
Door
├── GameplayActionComponent
│   └── OpenAction : InteractionAction
│       └── OpenExecutor : GameplayActionExecutor
├── InteractiveComponent
├── InteractionArea
├── InteractionAnchor
└── optional durable world-state components
```

`InteractiveComponent` explicitly references its `GameplayActionComponent`. It exposes the component's
`InteractionAction` occurrences through Interaction availability and presentation. It is not a subclass of
`GameplayActionComponent`, and the generic component never discovers spatial nodes.

### 12.2 Requester topology

```text
Player
├── GameplayActionComponent
├── GameplayActionRunner
├── InteractionInteractor
└── optional InteractionPresenter
```

The runner explicitly references the player's action component. The interactor explicitly references the
runner and retains its detector. On local focus it binds the focused target's interaction actions; on focus
loss it removes only bindings from that interaction source. It also registers the server-verifiable
Interaction access provider used by `InteractionAction`.

### 12.3 Functional equivalence

After migration, Interaction must still support:

- area, proximity, and aim detection;
- focus, indication, distance, angle, and line of sight;
- target and action rules with `Allowed`, `Blocked`, and `Hidden`;
- tap/hold selection and automatic actions;
- cancellation on input release and sustained-presence loss;
- instant, open-ended, timed, and discretely progressed executions;
- unique ActionId execution and target-local concurrency groups;
- requester prediction, ACKs, rejection, disconnect cleanup, and terminal reconciliation;
- requester-only, replicated, and authority-only execution presentation;
- current prompts, indicators, labels, descriptions, progress, and editor diagnostics;
- explicit single-executor gameplay mutation and notification-only signals.

The migration may mechanically update scene references, class names, namespaces, and tests. Functional
equivalence does not require preserving generic classes under legacy `Interaction*` aliases. Names remain
Interaction-specific only when the type still owns spatial Interaction semantics.

## 13. Authoring strategy

The first implementation exposes every primitive as normal framework classes/nodes. This keeps custom action
authoring possible without hidden generated runtime graphs.

Specialized frameworks may later add a higher-level authoring facade:

- Interaction may group prompt data and a binding template on `InteractionAction` or its definition;
- Inventory may expose a declarative item-to-action grant;
- a combat framework may expose skill-specific configuration.

Those facades must adapt one-way into generic runtime actions and bindings. They must not maintain a second
competing execution state. Runtime bindings remain records; only objects that need Godot identity, lifecycle,
scene references, or replication become nodes.

## 14. Diagnostics and failure behavior

Editor/runtime diagnostics must cover at least:

- missing component, definition, executor, runner, or required access provider;
- empty or duplicate ActionIds on one component;
- one action registered to multiple components;
- empty concurrency groups;
- invalid input map names;
- invalid hold durations or irrelevant hold values on non-hold modes;
- automatic bindings carrying an input requirement;
- ambiguous same-input/same-phase bindings with equal authored priority;
- ambiguous hold bindings sharing one input and threshold;
- Interaction actions hosted without a matching Interactive integration;
- replicated visibility without an explicitly wired execution synchronizer.

Invalid configuration never falls back to signals or arbitrary scene-tree searches. It logs a precise warning
and keeps the action unavailable. Server-side malformed or unauthorized requests reject safely and do not
allocate an execution ID or mutate gameplay.

## 15. Test strategy

### 15.1 Generic add-on tests

Tests must prove the abstraction independently from Interaction:

- register, resolve, add, retire, and finally remove actions;
- reject missing/duplicate IDs and multi-host ownership;
- execute an owned action through a runner;
- bind and execute an action hosted on another component through a fake access provider;
- prove that a local binding is neither required nor trusted by server access validation;
- unbind before activation, unbind after acceptance, and remove while running;
- player-requested versus programmatic invocation;
- programmatic access bypass with normal rules still enforced;
- `Press`, `Hold`, `Release`, and edge-triggered `Automatic` activation;
- tap-versus-hold disambiguation and consumed-gesture behavior;
- `InputRequirement.None` and `Pressed` captured into executions;
- `Allowed > Blocked > Priority > stable tie` conflict resolution;
- deterministic equal-priority behavior plus diagnostics;
- one active execution per ActionId;
- same-host concurrency conflicts and different-host independence;
- completed, running, rejected, failed, cancelled, timed, and discrete-progress executions;
- requester teardown/disconnect and world-owned execution survival;
- prediction, started/rejected ACKs, correction, and every terminal ACK;
- requester-only, replicated, and authority-only presentation;
- late join and stale-sample behavior for replicated presentation.

Fake actions, rules, executors, access providers, and two-host scenes are preferred to an Interaction fixture for
these tests.

### 15.2 Interaction regression tests

All existing Interaction behavior, configuration, scene, detection, ACK, and real multiplayer tests must be
retained or migrated. Their assertions should remain behaviorally equivalent. Additional integration tests
must prove:

- focus creates contextual bindings and focus loss removes them;
- the door action remains owned and concurrent on the door component;
- the authority rejects an out-of-range request even if the client fabricated a binding;
- authority-only programmatic door execution ignores distance but keeps door/state rules;
- sustained Interaction access cancels requester-owned long executions;
- Interaction presentation reads the generic hold and execution state without moving UI into the core.

### 15.3 Manual acceptance

A later hand-authored action invoked without an Interactive is a useful project smoke test, but it is not a
deliverable of this migration. The automated generic suite must already demonstrate that the system is more
than an Interaction rename.

## 16. Implementation scope and sequencing constraints

The implementation plan may split the work into checkpoints, but the final migration is one coherent V1:

1. establish generic contracts and behavior tests;
2. move the execution host, rules, executors, timing, presentation state, and synchronization;
3. add the runner, binding, access-provider, and input gesture layers;
4. rebuild Interaction on the generic primitives;
5. migrate existing scenes, integrations, documentation, editor diagnostics, and tests;
6. remove superseded Interaction-owned generic runtime code and verify no legacy parallel lifecycle remains.

At no checkpoint may two authoritative execution pipelines coexist as the intended final architecture.
Temporary adapters are acceptable only inside the implementation branch and must be gone at closeout unless
they retain genuine Interaction-specific semantics.

## 17. Acceptance criteria

V1 is complete when:

- `gameplay_action_plugin` has no dependency on `interaction_plugin`;
- `interaction_plugin` executes all actions through `GameplayActionComponent` and `GameplayActionRunner`;
- no separate `ActionHost` node or duplicate Interaction execution host remains;
- owned and external actions share one request and execution pipeline;
- access, rules, and concurrency follow the specified authoritative order;
- programmatic execution ignores access while preserving rules;
- bindings remain local and action grants remain component ownership changes;
- every current Interaction feature listed in section 12.3 still passes;
- generic tests cover non-Interaction actions, dynamic add/remove, every input mode, network lifecycle, and
  host-local concurrency;
- the public documentation clearly marks tag systems, cross-host locks, target data, Inventory integration,
  and generic grant replication as deferred rather than partially implemented.

## 18. Deferred V2 directions

Real gameplay may later justify:

- granted and required gameplay tags consumed by rules;
- cross-host/requester concurrency or cancellation policy;
- a definition catalog and generic replicated grant synchronizer;
- costs, cooldowns, effects, attributes, and richer failure reasons;
- arbitrary target data or invocation payloads;
- reusable presenter models or standardized action menus;
- multiple concurrent executions for one ActionId.

Each addition must start from an observed use case and preserve the V1 ownership, authority, and lifecycle
invariants. The existence of those extension directions is not permission to approximate GAS inside V1.
