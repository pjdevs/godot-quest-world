# Releasable access reservation

> **Status: planned.** This proposal allows an authority-side access reservation to end before the
> Gameplay Action execution that acquired it, but only after that execution has stopped depending on its
> requester. Carry may exercise cleanup timing, but it is not sufficient justification for the feature:
> its preferred final behavior is still to complete its Gameplay Action at the gameplay commit point.

## Motivation

A requested Gameplay Action currently has several related lifetimes:

```text
request accepted
    ↓
Gameplay Action execution
    ├── host/action reservation
    ├── requester/access-presence dependency
    ├── optional sustained-input dependency
    └── optional access-provider reservation
```

The framework already allows one of those lifetimes to end early:

```text
GameplayActionContext.ReleaseRequesterDependency()
```

After an irreversible gameplay commit, an executor may stop depending on the requester while leaving the
Gameplay Action execution alive for additional authoritative work or recovery.

The requester dependency is currently only partially released: authority-side access revalidation and
requester teardown stop cancelling the execution, but a remembered `Pressed` input can still cancel it when
the player releases the button. Before adding another release operation, `ReleaseRequesterDependency()`
must be hardened so its public meaning includes that sustained-input dependency too.

Once an execution is truly detached from its requester, the same early-release capability may be needed
for the optional access-provider reservation.

Example: an actor uses a workshop.

```text
Engage ----- Work ----- COMMIT ----- GetUp ----- Complete
|                        |
| actor/action busy      | actor/action still busy
| workshop reserved      | workshop FREE
```

At the commit point the workshop may be safe for another actor to use even though the first actor remains
inside a running action while performing authoritative disengagement or recovery. The committed execution
must first stop depending on the requester and its held input; it may then release the workshop claim while
retaining only its host/action reservation.

Carry exposes the same timing distinction in a smaller form. During a Take/Drop animation, the carried
object may be committed before the actor has finished the recovery animation. Take can temporarily
exercise the new release API as a limited cleanup pressure test, but the preferred final Carry behavior is
still:

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
reservation acquired and stored as pending
    ↓
ExecuteRequestedAction()
    ├── synchronous terminal result
    │       ↓
    │   pending reservation.Release()
    │
    └── Running
            ↓
        execution-start notification
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

The missing capability is an explicit early release operation plus completion of the existing requester
detachment contract.

## Decision

Add an explicit generic operation:

```csharp
GameplayActionContext.ReleaseAccessReservation()
```

The call releases the optional authority-side request reservation associated with the current active
requested execution while leaving the execution itself active. It is accepted only after
`ReleaseRequesterDependency()` has successfully detached that execution from requester presence and any
sustained input.

Conceptually:

```text
Before
------
execution                 = Running
host/action reservation   = Held
requester dependency      = Released
access reservation        = Held

ReleaseAccessReservation()

After
-----
execution                 = Running
host/action reservation   = Held
requester dependency      = Released
access reservation        = Released
```

The operation remains distinct from `ReleaseRequesterDependency()` and `CompleteExecution()`, but the
valid transition order is deliberately constrained:

```text
ReleaseRequesterDependency()
    ↓
ReleaseAccessReservation()
```

No current gameplay case justifies keeping a `Pressed` interaction sustained while making the same target
available to another requester. Shared cooperative interactions should instead declare multiple slots or a
shared/capacity policy from admission; an exclusive-to-shared phase change should complete the exclusive
action and expose a new shared phase.

## API boundary

### GameplayActionContext

Expose:

```csharp
public bool ReleaseAccessReservation()
```

The context remains the executor-facing API. It forwards the execution identity to its owning
`GameplayActionComponent`, exactly as `ReleaseRequesterDependency()` already does.

The method should return `true` when the current active requested execution can be resolved, is already
detached from its requester, and its access reservation is already released or is successfully released.
It returns `false` for invalid execution IDs, non-requested executions, executions that are no longer
active, or executions that still depend on requester presence or sustained input.

Repeated calls must be safe and must not reacquire or release another request's reservation.

### GameplayActionComponent

Add the corresponding forwarding method:

```csharp
public bool ReleaseAccessReservation(ulong executionId)
```

The component resolves the active execution, action and requester, then delegates those identities to the
requesting `GameplayActionRunner`. The component does not know the concrete reservation type or access
domain.

### GameplayActionRunner / request pipeline

The runner forwards the release to `GameplayActionRequestPipeline`.

The pipeline normally finds the matching `GameplayActionRequestedExecution` by component + execution ID.
It must detach the lease from the record *before* calling domain code:

```csharp
IGameplayActionRequestReservation? reservation = execution.Reservation;
requestedExecutions[index] = execution with { Reservation = null };
reservation?.Release();
```

`Release()` may synchronously emit domain signals. Clearing the reference first ensures re-entrant terminal
cleanup cannot release the same lease twice or update a list index whose record has already been removed.

Keeping the record is essential: terminal acknowledgements, stale input-cancellation rejection and host
execution lifecycle continue normally.

### Admission window

The executor receives a non-zero execution context while `ExecuteRequestedAction()` is still on the stack,
before a `Running` result has been converted from a pending request into a
`GameplayActionRequestedExecution`. Both requester and access release operations must have explicit
semantics in this window.

The chosen contract is to support them. The pipeline keeps pending request state, keyed by the resolved
component/action and validated against the active execution ID supplied through the component. A release
performed during `Execute()` updates that pending state. If the executor then returns `Running`, the
created requested-execution record inherits the released flags and no released lease is transferred into
it. If the executor returns synchronously terminal, normal rollback remains idempotent.

This avoids an API whose result changes merely because the same authoritative commit occurred inside
`Execute()` rather than from a later timer or domain callback.

## Required requester-dependency hardening

`ReleaseRequesterDependency()` must become the complete transition promised by its name. For the matching
execution it must:

- stop periodic sustained access validation;
- prevent requester teardown from cancelling the execution;
- remove the authority-side execution from sustained-input cancellation;
- ensure a later/stale client cancellation request for that execution is ignored;
- tell the requesting client to stop tracking the acknowledged execution as sustained without removing
  its execution presentation.

Any authority-to-requester notification must carry the execution ID. An old notification must not detach a
newer execution of the same component/action. This is lifecycle acknowledgement, not permission for the
client to choose the transition; only the authoritative executor may initiate it.

## Sustained-access semantics

Interaction currently re-validates a running offer partly by requiring its target reservation group to
still be `ReservedBySelf`. That remains the correct invariant while an execution depends on its requester:

```text
requester-dependent sustained interaction
    → spatial/target/offer access must remain valid
    → held input must remain held when required
    → exclusive target claim must remain ReservedBySelf
```

`ReleaseAccessReservation()` is legal only after requester dependency has ended, so the released execution
is no longer subject to Interaction sustained validation on the next frame. There is therefore no need to
weaken `InteractiveComponent.EvaluateAccess(..., sustained: true)`: losing a reservation before the
explicit requester-detachment transition remains real access loss and cancels the interaction.

Once detached and released, another requester may acquire the target group while the first execution
continues only its own authoritative post-commit work.

## Lifecycle semantics

The framework then has three explicit lifecycle operations and one constrained transition:

```text
ReleaseRequesterDependency()
    requester/access presence is no longer required
    sustained input release no longer cancels the execution
    execution remains Running
    host/action reservation remains Held
    access reservation remains unchanged

ReleaseAccessReservation()
    valid only after requester dependency was released
    access-provider claim is released
    execution remains Running
    host/action reservation remains Held

CompleteExecution()
    execution reaches terminal success
    host/action reservation is released
    remaining requester/request reservation state is cleaned up
```

An executor may release requester dependency without releasing access reservation, for example when work
has become world-owned but must keep a resource exclusive. It may not release access reservation first.

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

Collaborative repair
    several players may contribute while holding input
      → shared/capacity admission from the start
      → no exclusive reservation released mid-execution

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
unclaimed. The releasing execution is already detached from Interaction sustain at that point. Any
remaining availability restriction must then come from another real source:

- action rules;
- offer or target rules;
- the resolved action owner's host concurrency;
- another request that subsequently acquires the target reservation.

This distinction is important for instigator-owned actions. A released target may become available to a
second actor while the first actor's own `GameplayActionComponent` remains busy finishing a recovery.
Those are separate hosts and separate constraints.

## Carry validation limits

Carry must not be used as the proof that target reuse works. `Take` deletes its world target at commit, so
there is no same target for another player to acquire. `Drop` is currently an owned action with no access
provider and therefore has no access reservation to release.

Before changing Carry to complete at commit, Take may temporarily call
`ReleaseRequesterDependency()` followed by `ReleaseAccessReservation()` at its gameplay commit point. This
can pressure-test the pending/running transition and idempotent cleanup, but not concurrent target reuse.

The experiment should confirm all of the following in multiplayer/offline play:

1. Take starts and obtains the Interaction target reservation normally.
2. Before commit, another player is prevented from acquiring the same target reservation.
3. At commit, the executor detaches requester/input dependency and then releases the access reservation
   while the Gameplay Action remains Running.
4. The original execution is not cancelled by sustained access validation after the release.
5. The first player's host concurrency remains active until its action completes.
6. Terminal cleanup after an early release does not call the released lease again.

The reusable concurrency proof belongs in a synthetic instigator-owned Interaction fixture or in the first
real workshop-like action whose target survives the commit. Carry should then move to
`CompleteExecution()` at commit and leave the remaining animation tail outside Gameplay Action lifetime.

Do not implement this generic feature solely to preserve Carry's animation tail. Without a concrete action
that needs authoritative post-commit work while freeing a still-existing target, the proposal should remain
planned.

## Failure and cleanup rules

- Releasing a missing/null reservation from a valid requester-detached execution is safe and leaves the
  execution unchanged.
- Releasing the same reservation twice after requester detachment has no effect after the first successful
  release.
- Releasing access before requester dependency has ended returns `false` and leaves the lease held.
- Cancellation/failure/completion after early release must not attempt to release another request's lease.
- A requester-dependent execution is cancelled on requester teardown and releases its lease.
- A requester-detached execution survives requester teardown, but the runner releases any still-held
  request-owned access lease as fail-safe cleanup. Exclusivity that must survive its requester must be
  represented by authoritative domain state or host concurrency, not by an orphaned request lease.
- After that teardown cleanup, terminal execution must not call back through an invalid requester node;
  the component must guard or detach terminal requester notification for the surviving execution.
- Early release is authority-only through the authoritative execution context; no client RPC is added for
  releasing a reservation directly.
- A client cannot request early release by supplying request metadata. The domain executor decides when
  the authoritative gameplay state has reached the release point.

## Tests

### Gameplay Action request pipeline

Add coverage for a running requested execution with a recording reservation:

- reservation is acquired and bound at execution start;
- requester dependency is released before access release;
- `context.ReleaseAccessReservation()` calls `Release()` exactly once;
- execution remains active after release;
- requester dependency remains released;
- releasing while requester/input dependency is still held returns `false` and does not release the lease;
- releasing requester dependency prevents a later held-input release from cancelling the execution;
- the requester client stops tracking sustained input for only the matching execution ID and retains its
  acknowledged execution presentation;
- a stale client cancel received after requester detachment is ignored;
- host concurrency remains active;
- repeated release is safe;
- terminal completion after early release does not call `Release()` a second time;
- another execution's reservation is unaffected.

Also cover invalid contexts:

- non-requested programmatic execution returns `false`;
- unknown/finalized execution returns `false`;
- detached active requested execution with no reservation succeeds as an idempotent no-op;
- release calls made from inside `Execute()` are reflected in the pending request and remain released if it
  returns `Running`.

### Interaction

Add a target-reservation test where:

```text
requester A starts an action
    ↓
target group reserved by A
    ↓
A releases requester/input dependency
    ↓
A releases access reservation while action remains Running
    ↓
target group becomes unclaimed
    ↓
A is no longer registered for sustained Interaction validation or held-input cancellation
    ↓
requester B can acquire the target group
    ↓
another validation pass does not cancel A and B's reservation remains held
```

Keep separate coverage proving that real spatial/offer access loss or loss of the self reservation still
cancels a requester-dependent execution *before* it has detached and released its target reservation.

### Carry integration

If Take is temporarily used as a validation slice, cover only commit/recovery detachment and cleanup. Use a
surviving instigator-owned target fixture for the two-player reuse window.

## Non-goals

- no executor access to `IGameplayActionRequestReservation` objects;
- no client-controlled release RPC;
- no automatic release at every gameplay commit;
- no supported state where an exclusive access reservation is released while the same interaction still
  depends on requester presence or held input;
- no change to host/action concurrency semantics;
- no generic shared/capacity interaction policy in this slice;
- no generic choreography framework;
- no requirement that Carry permanently keep its execution alive after commit;
- no reservation reacquisition API for the same running execution.

If a future action needs to release and later reacquire a target during one execution, that is a separate
requirement and should be designed from a concrete use case rather than inferred from this API.

## Architecture decision

> **Access reservations are request-owned leases whose lifetime may end before the Gameplay Action
> execution, but not while that execution still depends on its requester.**
>
> `GameplayActionContext.ReleaseAccessReservation()` is the executor-facing transition. The request
> pipeline remains the sole owner of the concrete lease. Requester detachment is an explicit prerequisite;
> early access release does not imply host reservation release or execution completion.

This keeps one execution lifecycle while allowing requester presence, resource exclusivity and actor/host
occupancy to end at distinct semantic points without inventing the unsupported state “still sustaining the
interaction, but no longer owning its exclusive target.”
