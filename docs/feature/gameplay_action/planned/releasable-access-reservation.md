# Releasable access reservation

> **Status: planned.** This proposal separates the lifetime of an authority-side access reservation from
> the lifetime of the Gameplay Action execution that acquired it. The first validation may use Take/Drop,
> but Carry is expected to eventually complete its Gameplay Action at the gameplay commit point instead of
> depending on this feature permanently.

## Motivation

A requested Gameplay Action currently has several related but distinct lifetimes:

```text
request accepted
    ↓
Gameplay Action execution
    ├── host/action reservation
    ├── requester dependency
    └── optional access-provider reservation
```

The framework already allows one of those lifetimes to end early:

```text
GameplayActionContext.ReleaseRequesterDependency()
```

After an irreversible gameplay commit, an executor may stop depending on the requester while leaving the
Gameplay Action execution alive for additional authoritative work or recovery.

The same separation is now needed for the optional access-provider reservation.

Example: an actor uses a workshop.

```text
Engage ----- Work ----- COMMIT ----- GetUp ----- Complete
|                        |
| actor/action busy      | actor/action still busy
| workshop reserved      | workshop FREE
```

At the commit point the workshop may be safe for another actor to use even though the first actor remains
inside a running action while disengaging or recovering. Completing the action merely to free the target
would incorrectly couple target exclusivity to actor execution lifetime.

Carry exposes the same distinction in a smaller form. During a Take/Drop animation, the carried object may
be committed before the actor has finished the recovery animation. Take/Drop can temporarily exercise the
new release API as a pressure test, but the preferred final Carry behavior is still:

```text
commit
    ↓
CompleteExecution()
    ↓
target/access reservation and host concurrency end naturally
    ↓
remaining animation tail is presentation only
```

## Current model

`IGameplayActionAccessProvider.TryAcquireRequestReservation()` may return an
`IGameplayActionRequestReservation` after authoritative access validation succeeds.

The request pipeline currently owns that lease:

```text
access validated
    ↓
TryAcquireRequestReservation()
    ↓
reservation acquired
    ↓
action execution allocated
    ↓
reservation.BindExecution(executionId)
    ↓
reservation retained in GameplayActionRequestedExecution
    ↓
terminal execution / rollback / requester cleanup
    ↓
reservation.Release()
```

This ownership is correct and should remain unchanged. Executors and domain code must not receive the
reservation object directly.

The missing capability is only an explicit early release operation.

## Decision

Add an explicit generic operation:

```csharp
GameplayActionContext.ReleaseAccessReservation()
```

The call releases the optional authority-side request reservation associated with the current running
requested execution while leaving the execution itself active.

Conceptually:

```text
Before
------
execution                 = Running
host/action reservation   = Held
requester dependency      = Held or Released
access reservation        = Held

ReleaseAccessReservation()

After
-----
execution                 = Running
host/action reservation   = Held
requester dependency      = unchanged
access reservation        = Released
```

The operation is independent from `ReleaseRequesterDependency()` and `CompleteExecution()`.

## API boundary

### GameplayActionContext

Expose:

```csharp
public bool ReleaseAccessReservation()
```

The context remains the executor-facing API. It forwards the execution identity to its owning
`GameplayActionComponent`, exactly as `ReleaseRequesterDependency()` already does.

The method should return `true` when the current active requested execution can be resolved and its access
reservation is already released or is successfully released. It returns `false` for invalid execution
IDs, non-requested executions, or executions that are no longer active.

Repeated calls must be safe and must not reacquire or release another request's reservation.

### GameplayActionComponent

Add the corresponding forwarding method:

```csharp
public bool ReleaseAccessReservation(ulong executionId)
```

The component resolves the active execution and its requester, then delegates to the requesting
`GameplayActionRunner`. The component does not know the concrete reservation type or access domain.

### GameplayActionRunner / request pipeline

The runner forwards the release to `GameplayActionRequestPipeline`.

The pipeline finds the matching `GameplayActionRequestedExecution` by component + execution ID, calls:

```csharp
execution.Reservation?.Release();
```

and stores the requested-execution record with `Reservation = null`.

Keeping the record is essential: requester dependency, terminal acknowledgements, input cancellation and
host execution lifecycle continue normally.

## Required sustained-access change

Early reservation release must not cause the same execution to fail sustained access on the next frame.

Interaction currently re-validates a running offer partly by requiring its target reservation group to
still be `ReservedBySelf`. That assumption is valid only while reservation lifetime is forced to equal
execution lifetime.

Once early release exists, these concepts must be separated:

```text
sustained access
    = may this requester/execution continue using the domain relationship?

access reservation
    = should this request prevent another requester from acquiring the resource?
```

Therefore Interaction sustained access must not require the target claim to remain owned by the current
execution after the request was successfully admitted.

A sustained check should continue to validate the real access relationship, including the existing
spatial/target/offer predicates, but it must not interpret a deliberately released reservation as lost
access.

The target reservation still protects acquisition of a *new* request. Once released, another requester is
allowed to acquire the same target group even while the first action continues running on its own host.

## Lifecycle semantics

The framework then has three independent release operations:

```text
ReleaseRequesterDependency()
    requester/access presence is no longer required
    execution remains Running
    host/action reservation remains Held
    access reservation remains unchanged

ReleaseAccessReservation()
    access-provider claim is released
    execution remains Running
    requester dependency remains unchanged
    host/action reservation remains Held

CompleteExecution()
    execution reaches terminal success
    host/action reservation is released
    remaining requester/request reservation state is cleaned up
```

An executor may call the first two in either order when a real domain requires it.

Examples:

```text
Workshop
    commit
      → ReleaseRequesterDependency()
      → ReleaseAccessReservation()
    disengage
      → CompleteExecution()

Channelled terminal
    requester must stay present
    target must stay exclusive
      → no early release

Carry final form
    commit
      → CompleteExecution()
    animation tail continues outside Gameplay Action
```

## Interaction semantics

`InteractionOffer.TargetConcurrencyGroup` remains the authoring mechanism for target-side exclusivity.
`WhenReservedBySelf` / `WhenReservedByOther` continue to describe presentation while a target claim is
actually present.

Early release removes that claim and should immediately make the target reservation layer evaluate as
unclaimed. Any remaining availability restriction must then come from another real source:

- action rules;
- offer or target rules;
- the resolved action owner's host concurrency;
- another request that subsequently acquires the target reservation.

This distinction is important for instigator-owned actions. A released target may become available to a
second actor while the first actor's own `GameplayActionComponent` remains busy finishing a recovery.
Those are separate hosts and separate constraints.

## Carry validation slice

Before changing Carry to complete at commit, Take/Drop may temporarily use
`ReleaseAccessReservation()` at their gameplay commit point.

The experiment should confirm all of the following in multiplayer/offline play:

1. Take/Drop starts and obtains the Interaction target reservation normally.
2. Before commit, another player is prevented from acquiring the same target reservation.
3. At commit, the executor releases the access reservation while the Gameplay Action remains Running.
4. The original execution is not cancelled by sustained access validation after the release.
5. Another player can acquire/use the newly free target while the first player's recovery/action is still
   running.
6. The first player's host concurrency remains active until its action completes.
7. Terminal cleanup after an early release is idempotent and does not affect a later reservation acquired
   by another request.

This validation is intentionally temporary. If Carry feels better with movement and subsequent carry
inputs available immediately after commit, Take/Drop should then move to `CompleteExecution()` at commit
and leave the remaining animation tail outside Gameplay Action lifetime.

## Failure and cleanup rules

- Releasing a missing/null reservation is safe and leaves the execution unchanged.
- Releasing the same reservation twice has no effect after the first successful release.
- Cancellation/failure/completion after early release must not attempt to release another request's lease.
- Request pipeline shutdown/requester teardown must continue to clean up any reservation that is still
  held.
- Early release is authority-only through the authoritative execution context; no client RPC is added for
  releasing a reservation directly.
- A client cannot request early release by supplying request metadata. The domain executor decides when
  the authoritative gameplay state has reached the release point.

## Tests

### Gameplay Action request pipeline

Add coverage for a running requested execution with a recording reservation:

- reservation is acquired and bound at execution start;
- `context.ReleaseAccessReservation()` calls `Release()` exactly once;
- execution remains active after release;
- requester dependency remains unchanged;
- host concurrency remains active;
- repeated release is safe;
- terminal completion after early release does not call `Release()` a second time;
- another execution's reservation is unaffected.

Also cover invalid contexts:

- non-requested programmatic execution returns `false`;
- unknown/finalized execution returns `false`;
- active requested execution with no reservation succeeds as an idempotent no-op.

### Interaction

Add a target-reservation test where:

```text
requester A starts an action
    ↓
target group reserved by A
    ↓
A releases access reservation while action remains Running
    ↓
target group becomes unclaimed
    ↓
A's sustained validation still succeeds if spatial/offer access remains valid
    ↓
requester B can acquire the target group
```

Keep separate coverage proving that real spatial/offer access loss still cancels a requester-dependent
execution after its target reservation has been released.

### Carry integration

While Take/Drop are used as the validation slice, manually or automatically cover the commit/recovery
window with two players so target reuse is observable before the original action terminates.

## Non-goals

- no executor access to `IGameplayActionRequestReservation` objects;
- no client-controlled release RPC;
- no automatic release at every gameplay commit;
- no coupling between requester dependency and access reservation release;
- no change to host/action concurrency semantics;
- no generic choreography framework;
- no requirement that Carry permanently keep its execution alive after commit;
- no reservation reacquisition API for the same running execution.

If a future action needs to release and later reacquire a target during one execution, that is a separate
requirement and should be designed from a concrete use case rather than inferred from this API.

## Architecture decision

> **Access reservations are request-owned leases whose lifetime may end before the Gameplay Action
> execution.**
>
> `GameplayActionContext.ReleaseAccessReservation()` is the executor-facing transition. The request
> pipeline remains the sole owner of the concrete lease, and early release does not imply requester
> release, host reservation release, or execution completion.

This keeps the existing one-execution lifecycle while allowing resource exclusivity, requester presence
and actor/host occupancy to end at the distinct semantic points required by real gameplay.
