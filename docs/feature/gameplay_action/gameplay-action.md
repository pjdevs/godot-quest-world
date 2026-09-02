# Gameplay Action

## Status

V1 extraction is in progress. Tranche 1 provides the standalone authoritative host and its generic
contracts. Input bindings, the runner, prediction, acknowledgements, execution presentation, timing,
and replication remain intentionally deferred to the following tranches.

The approved design is in
[`planned/gameplay-action-system-v1.md`](planned/gameplay-action-system-v1.md).

## Package boundary

`addons/gameplay_action_plugin` owns generic action identity, availability rules, execution,
host-local reservations, and action lifetime. It has no dependency on Interaction, Inventory,
Stateful, Character, Quest, Dialog, or persistence.

Its public namespace is `QuestWorld.GameplayActions`, with action nodes under
`QuestWorld.GameplayActions.Runtime.Actions` and rules under
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
and stable ID reservation until the execution reaches a terminal outcome. The same ID cannot be
registered again during this retiring window. Retirement does not implicitly cancel gameplay.

## Tranche 1 verification coverage

The generic tests use no Interaction fixture. They cover intrinsic definition metadata, explicit
authored registration, runtime ownership and parenting, invalid and duplicate registration,
multi-host ownership rejection, ordered rule short-circuiting, rule-preserving programmatic
execution, null rule entries, mutation-before-executor dispatch, executor-exception cleanup, one
active execution per ID, same-host group exclusion, different-host independence, distinct same-host
groups, safe retirement, and retirement-time ID reservation.
