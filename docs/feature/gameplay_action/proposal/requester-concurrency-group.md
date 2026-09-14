# Requester Concurrency Groups

## Status

Proposed for implementation.

## Problem

`GameplayAction` already supports host-local concurrency through `HostConcurrencyGroup`, while Interaction supports target-side concurrency through `InteractionOffer.TargetConcurrencyGroup`.

These two axes do not cover one common case: a single `GameplayActionRunner` may request actions hosted by different `GameplayActionComponent` instances at the same time.

For example, while a player-owned `Drop` action is still running during its post-commit animation, the same player can currently request a long-running interaction hosted by a world object. The two actions do not conflict because their host concurrency groups are evaluated independently on different components.

Games can already prevent this with custom rules such as “is this player already doing something?”, but that duplicates request-state logic outside the framework and must be repeated across actions or integrations. The framework already owns the relevant request lifecycle, so requester-level concurrency should be a native primitive.

## Goals

- Allow actions requested by the same `GameplayActionRunner` to exclude one another even when they are hosted by different `GameplayActionComponent` instances.
- Keep requester concurrency independent from host concurrency and target reservations.
- Preserve the existing distinction between player/requester-driven execution and programmatic execution.
- Integrate requester concurrency into local availability/presentation and authoritative request validation.
- Reuse the existing request lifecycle instead of introducing a global scheduler or a new public reservation subsystem.

## Non-goals

- Do not add global or instigator-wide gameplay locks.
- Do not affect `GameplayActionComponent.ExecuteAction()` programmatic execution.
- Do not merge requester concurrency with `HostConcurrencyGroup` or `InteractionOffer.TargetConcurrencyGroup`.
- Do not add a generic global concurrency registry.
- Do not add early/manual requester-concurrency release in V1.

## Terminology and scope

Requester concurrency is scoped to one `GameplayActionRunner`.

It answers:

> Can this requester start another incompatible action right now?

It does not answer whether the action host is busy or whether a target/resource is reserved.

The three concurrency axes are intentionally orthogonal:

- `HostConcurrencyGroup`: local to one `GameplayActionComponent`; prevents that host from running incompatible actions concurrently.
- `RequesterConcurrencyGroup`: local to one `GameplayActionRunner`; prevents that requester from requesting incompatible actions concurrently across hosts.
- `TargetConcurrencyGroup`: local to one `InteractiveComponent`; prevents incompatible uses of the same interaction target/resource.

## Authored action contract

`GameplayAction` gains two authored properties:

```csharp
[Export]
public StringName RequesterConcurrencyGroup { get; set; } = "default";

[Export]
public GameplayActionUnavailableKind WhenRequesterBusy { get; set; } =
    GameplayActionUnavailableKind.Blocked;
```

### Default group

`RequesterConcurrencyGroup` defaults to `"default"`.

This makes the common case safe by default: two ordinary actions requested by the same runner are considered incompatible unless the author deliberately opts out or assigns different groups.

An empty requester concurrency group opts the action out of requester concurrency entirely.

This differs from the current host-group fallback behavior: an empty requester group is meaningful and must remain empty.

### Busy presentation

`WhenRequesterBusy` controls how an otherwise valid requested action is presented when its requester group is occupied:

- `Blocked`: keep the action visible with an unavailable reason.
- `Hidden`: remove it from offered choices/presentation.

This property remains separate from existing presentation policies because each one corresponds to a different concurrency axis:

- `WhenExecutingBySelf` / `WhenExecutingByOther` describe host-local execution occupancy.
- `WhenRequesterBusy` describes requester occupancy.
- `InteractionOffer.WhenReservedBySelf` / `WhenReservedByOther` describe target reservation occupancy.

## Request lifecycle semantics

Requester concurrency applies only to executions that enter through `GameplayActionRunner`.

The group is considered occupied while a matching request is any of the following:

1. pending locally or authoritatively;
2. acknowledged as running for the requester;
3. tracked as an active requested execution by the authoritative request pipeline.

This deliberately covers the request/acknowledgement latency window so a quick second request cannot bypass concurrency before the first request is acknowledged.

The group is released when the request/execution leaves the runner lifecycle through rejection or a terminal result:

- `Completed`;
- `Cancelled`;
- `Failed`.

Normal cleanup paths, requester loss, rollback, and runner teardown must leave no stale requester occupancy.

## Relationship with requester dependency and access reservation

Requester concurrency is independent from `RequiresRequesterPresence`, `ReleaseRequesterDependency()`, and `ReleaseAccessReservation()`.

In V1, neither release operation frees the requester concurrency group.

This is required for actions such as `Drop` and `Take`, whose authoritative gameplay commit can release requester presence and target/access claims before their post-commit animation has finished. The action may no longer depend on sustained input or target ownership while the player is still intentionally busy performing that action.

For example:

```text
Drop starts
  -> requester/default occupied

Drop commits world state
  -> ReleaseRequesterDependency()
  -> optional ReleaseAccessReservation()
  -> requester/default remains occupied

Drop post-commit animation finishes
  -> CompleteExecution()
  -> requester/default becomes free
```

V1 does not introduce `ReleaseRequesterConcurrency()` or an equivalent early-release API. A concrete gameplay need should justify such a lifecycle before adding it.

## Programmatic execution

`GameplayActionComponent.ExecuteAction()` is unaffected.

Programmatic execution already bypasses requester/access/binding semantics and is intentionally governed only by the action rules and host-local reservations of the owning `GameplayActionComponent`.

Requester concurrency therefore applies only to `GameplayActionRunner` request paths, including owned actions and external/contextual bindings.

This keeps the abstraction truthful: the feature is request arbitration, not global instigator state.

## Runner responsibility

`GameplayActionRunner` and its `GameplayActionRequestPipeline` own requester concurrency.

This follows the existing architecture:

- the runner owns bindings and request arbitration;
- the request pipeline owns pending requests, requester acknowledgements, requested executions, cleanup, and request transport;
- one runner can request actions from multiple `GameplayActionComponent` instances.

No global registry is required.

The existing host-group request checks are component-scoped. Requester-group checks are the cross-component counterpart and must inspect the runner's own pending/acknowledged/requested state without filtering by action component.

## Availability and arbitration

Requester concurrency must participate in binding availability before input arbitration.

When evaluating a binding, if:

- the candidate action has a non-empty `RequesterConcurrencyGroup`; and
- the same runner already has another pending or active request in that group;

then availability becomes `action.WhenRequesterBusy`.

This is required for correct presentation and selection behavior. A requester-busy action configured as `Hidden` must disappear rather than remain an `Allowed` candidate that later fails only when requested.

The runner's existing selection rule—availability before priority—continues unchanged. Requester concurrency only contributes another availability condition.

The current request itself must not conflict with its own acknowledged/pending state when re-evaluating presentation. Occupancy checks therefore need enough request identity to distinguish the candidate's own request from another request in the same group where appropriate.

## Authoritative validation

Local availability is presentation and prediction only; it is not authority evidence.

The authoritative request path must re-check requester concurrency before executing the requested action.

The server-side check uses the authoritative runner/request pipeline state and the action's authored `RequesterConcurrencyGroup`. Client binding data is not transported or trusted.

A request that loses the concurrency race is rejected without entering action execution. Any request-side access reservation acquired before the rejection must still follow existing rollback guarantees; implementation should therefore place the authoritative requester-concurrency check before acquiring domain reservations when practical.

## State changes and invalidation

Requester-group occupancy changes can alter the availability of bindings that target unrelated action components. Binding invalidation therefore cannot rely solely on one component's execution-change notifications.

When a requester group transitions between free and occupied, the runner must invalidate/re-evaluate bindings whose resolved action belongs to that requester group so presenters receive the updated `Allowed` / `Blocked` / `Hidden` state.

This includes:

- request start/pending creation;
- authoritative/client acknowledgement where relevant;
- rejection/rollback;
- terminal completion/cancellation/failure;
- cleanup paths that remove tracked requests.

Interaction does not implement requester concurrency itself. Because Interaction projects offers into runner bindings, its presentation updates through the same runner binding invalidation path.

## Networking

No new requester-concurrency field is added to the request payload.

The server resolves the requested action from the authoritative `GameplayActionComponent` and reads `RequesterConcurrencyGroup` and `WhenRequesterBusy` from that action occurrence.

Requester concurrency state is not separately replicated. The requesting client already tracks pending and acknowledged request lifecycle through the runner protocol; observer clients do not need requester-group state because requester concurrency only affects the requester's own action choices.

## Examples

### Drop blocks an unrelated world interaction

```text
Player runner
  Drop          -> Player GameplayActionComponent
                  RequesterConcurrencyGroup = "default"

  UseConsole    -> Console GameplayActionComponent
                  RequesterConcurrencyGroup = "default"
```

While `Drop` is running, `UseConsole` is requester-busy even though the two actions belong to different hosts.

If `UseConsole.WhenRequesterBusy = Hidden`, its interaction prompt disappears until `Drop` reaches a terminal state.

### Two independent requester groups

```text
HeavyInteraction   requester group = "hands"
DialogueChoice     requester group = "dialogue"
```

The same runner may request both concurrently because the groups differ, assuming host, target, access, and action rules also allow it.

### Opt-out

```text
Ping               requester group = ""
```

`Ping` does not occupy or conflict with requester concurrency groups. It remains governed by its normal action rules and host concurrency.

### Programmatic execution

```csharp
component.ExecuteAction(...);
```

Requester concurrency is not evaluated because no runner is requesting the action.

## Implementation boundaries

The expected implementation should remain limited to existing gameplay-action responsibilities:

- `GameplayAction`: authored requester concurrency group and unavailable policy;
- `GameplayActionRunner` / binding evaluation: requester-busy availability and group invalidation;
- `GameplayActionRequestPipeline`: cross-component pending/acknowledged/requested occupancy and authoritative rejection;
- tests covering local arbitration, authoritative validation, lifecycle cleanup, networking, and cross-component behavior;
- documentation updates describing requester concurrency alongside host-local concurrency.

Interaction should require no requester-concurrency-specific state or reservation machinery.

## Required tests

At minimum, tests should cover:

1. two actions on different `GameplayActionComponent` instances with requester group `default` cannot both be requested by one runner;
2. another runner is unaffected;
3. different requester groups may run concurrently;
4. empty requester group opts out;
5. a pending request occupies the group before acknowledgement;
6. rejection frees requester occupancy;
7. `Completed`, `Cancelled`, and `Failed` executions free requester occupancy;
8. `ReleaseRequesterDependency()` does not free requester occupancy;
9. `ReleaseAccessReservation()` does not free requester occupancy;
10. `ExecuteAction()` programmatic execution does not participate;
11. `WhenRequesterBusy = Hidden` removes the candidate from presentation/arbitration;
12. `WhenRequesterBusy = Blocked` keeps it present but unavailable;
13. requester-group occupancy invalidates bindings across different components;
14. authoritative network validation rejects a concurrent request even if a client attempts to send it;
15. teardown/disconnect/rollback paths do not leave stale requester occupancy.

## Architecture decision

Requester concurrency is implemented as runner-local request arbitration rather than instigator-global execution state.

This preserves the framework's existing separation:

- `GameplayActionComponent` owns executions and host-local reservations;
- `GameplayActionRunner` owns requester/input/network request lifecycle;
- access providers own domain-specific access and optional request reservations;
- Interaction owns target reservation semantics.

The feature makes a common custom-rule pattern native without introducing a broader synchronization subsystem.