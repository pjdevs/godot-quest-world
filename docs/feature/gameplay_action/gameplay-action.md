# Gameplay Action

## Status

V1 extraction is in progress. Tranches 1 and 2 provide the standalone authoritative host, its full
execution lifecycle, generic progress/timing, and optional replicated presentation. Input bindings,
the runner, player-request access, prediction, and acknowledgements remain intentionally deferred to
the following tranches.

The approved design is in
[`planned/gameplay-action-system-v1.md`](planned/gameplay-action-system-v1.md).

## Package boundary

`addons/gameplay_action_plugin` owns generic action identity, availability rules, execution,
host-local reservations, and action lifetime. It has no dependency on Interaction, Inventory,
Stateful, Character, Quest, Dialog, or persistence.

Its public namespace is `QuestWorld.GameplayActions`, with action nodes under
`QuestWorld.GameplayActions.Runtime.Actions`, execution helpers under
`QuestWorld.GameplayActions.Runtime.Execution`, and rules under
`QuestWorld.GameplayActions.Runtime.Rules`.

## Tranche 1 runtime model

### Definition and occurrence

`GameplayActionDefinition` is a reusable `Resource` containing:

- `Id`, the stable gameplay and network identity;
- optional intrinsic `Label` and `Description` metadata.

`GameplayAction` is one owned occurrence. It references exactly one definition, exactly one
executor, an ordered rule collection, a host concurrency group, and its future execution visibility.
Input configuration does not belong to either type.

### Authoritative host

`GameplayActionComponent` is the concrete host. Its exported `Actions` collection registers only
explicit authored direct children during `_Ready`; it never discovers the scene tree recursively.
`AddAction` registers and parents an unowned runtime action. Registration rejects missing
definitions, empty IDs, missing executors, duplicate IDs, invalid parents, and actions already owned
by another component.

The main public operations delivered by this tranche are:

- `ResolveAction(ActionId)` for stable host-local lookup;
- `EvaluateAction(ActionId, ...)` for pure ordered rule evaluation;
- `ExecuteProgrammatic(ActionId, out ExecutionId, ...)` for authority-only execution that bypasses
  future binding/access checks but preserves rules and reservations;
- `IsActionExecuting(ActionId)` for reservation queries;
- `CompleteExecution`, `CancelExecution`, and `FailExecution` for terminal control of an execution an
  executor left running;
- `RemoveAction(ActionId)` for safe retirement.

Programmatic execution evaluates only the action's explicit ordered `Rules` collection; action
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
