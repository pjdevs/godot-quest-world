# Predicted carry execution

> **Status: planned.** Follow-up to [animated-carry-execution.md](animated-carry-execution.md). This proposal
> adds owner-side prediction and reconciliation to take/drop after the authoritative animated carry slice
> is stable. It does not generalize choreography yet.

## Dependency

This proposal assumes the previous slice already exists:

- `TakeExecutor` / `DropExecutor` start a `CarryComponent` operation and return
  `GameplayActionExecutionRunning`;
- `CarryComponent` owns authoritative take/drop timing and semantic operation replication;
- the authoritative commit point invokes the existing atomic `TryTake()` / `TryDrop()` transaction;
- generic execution completes at that commit point;
- movement/jump/etc. can interrupt requester-owned executions through the generic runner interruption
  path;
- observers currently begin the carry animation only when authoritative semantic carry state arrives.

Do not implement prediction before those server-driven semantics are independently correct.

## Why this step exists

Gameplay Action already predicts one narrow form of presentation. In
`GameplayActionRequestPipeline.TryRequestBinding()`, a non-authoritative requester:

```text
adds pending request
    ↓
PredictExecution(binding, action)
    ↓
action.Executor.GetPredictionSample(context with ExecutionId = 0)
    ↓
GameplayActionComponent.AddPendingExecutionPresentation(...)
```

The pending execution uses `ExecutionId = 0` and relation `RequestedLocally`. When the authority accepts
the action, `ClientActionStarted()` confirms that local slot with the authoritative `ExecutionId`; on
refusal, the pending slot is removed. Timed actions already use this for immediate local progress via
`TimedExecution.BuildPredictionSample()`.

This is useful precedent, but it does **not** yet predict carry presentation:

- executors only expose an optional progress sample, not a domain-specific predicted start hook;
- the authoritative carry operation lives on the character, not on the target action component;
- generic replicated execution entries contain only action ID, execution ID and optional progress;
- observers do not need prediction, but the requesting player should not wait one RTT before bending
  down, attaching the item visually, or beginning a drop.

The goal is therefore not "run the executor on the client". The goal is to predict the **carry-domain
presentation** associated with the local action request while preserving server-only gameplay mutation.

## Goal

For the locally controlled requester:

```text
input/request
    ↓ immediately
predicted carry animation
    ↓
predicted visual commit at authored contact point
    ↓
authoritative acknowledgement / commit arrives
    ↓
confirm silently or reconcile
```

For everyone else:

```text
authoritative carry operation replication
    ↓
normal observed animation
```

At no point does a client prediction call:

- `Inventory.AddItem` / `RemoveItem`;
- authoritative `CarriedItemId` mutation;
- `WorldSpawner.TrySpawn`;
- `QueueFree()` on the authoritative world object;
- `GameplayActionComponent.CompleteExecution` / `CancelExecution` / `FailExecution`.

## 1. Predict presentation, never executor gameplay

`GameplayActionExecutor.Execute()` remains authority-only. There should be no second predictive executor
path that duplicates gameplay logic.

Instead, the locally controlled actor needs enough information at request time to begin the carry motion
that would normally be triggered by authoritative `CarryComponent` state.

The cleanest first vertical slice is project-local:

```text
Character / carry request adapter
    receives local GameplayActionRequested signal or equivalent request edge
        ↓
identifies Take/Drop occurrence + semantic carry request
        ↓
CarryComponent.BeginPredictedTake / BeginPredictedDrop
```

The exact wiring should minimize knowledge duplication. Prefer a carry-specific action/executor contract or
small request metadata adapter over hard-coding action IDs in `Character`.

Prediction data must be read-only authoring/presentation data: semantic motion kind, duration, commit time,
item/world target identity needed for visual staging. It must not contain mutable authoritative results.

## 2. Predicted start

On the requester, prediction starts before the server acknowledgement:

```text
client t=0.000  request Take
                BeginPredictedTake(local request identity)
                play TakeGround immediately

server t≈RTT/2 validates request
                starts authoritative carry operation
                sends GameplayAction started ack
                replicates carry operation

client receives ack/state
                correlate with prediction
                mark prediction confirmed
```

This removes the most visible input latency while preserving authority.

The authoritative semantic carry replication may arrive before or after the generic action started ack.
Reconciliation must tolerate either ordering and treat both as observations of the same accepted request,
not as two animations to start.

## 3. Predicted visual commit

The second useful prediction is the contact point itself.

### Take

At local predicted commit time:

- hide/suppress the local rendering of the target world object;
- show the item visual attached to the local carry anchor;
- continue the animation tail.

Do **not** delete the networked world object or modify inventory/`CarriedItemId`.

When authoritative truth arrives:

- successful take: authoritative world removal and `CarriedItemId` converge with the predicted visual;
  replace/clear the prediction without a visible pop;
- rejected/cancelled/failed take: reveal the world object again, remove the predicted hand visual, and
  blend/cancel the motion;
- different authoritative carry result: reconcile to authoritative state immediately.

### Drop

At local predicted commit time:

- hide/suppress the carried visual locally;
- optionally instantiate a **presentation-only drop proxy** at the predicted spawn transform;
- never add that proxy to authoritative spawn/gameplay registries.

When the server's `WorldSpawner` result replicates:

- success: remove the proxy as the authoritative spawned object appears;
- failure/cancellation: restore the carried visual and remove the proxy.

Drop is the stronger pressure test because current authoritative behavior removes inventory and then asks
`IWorldSpawner.TrySpawn()`; the existing rollback restores inventory if spawning fails. Prediction must sit
outside this transaction and therefore remain cheap to discard.

## 4. Predicted state must be separate from authoritative `CarriedItemId`

Level 2 can continue to let `CarriedItemId` immediately drive the final attached visual. Prediction creates
a concrete reason to distinguish:

```text
authoritative carry truth
    CarriedItemId

transient local presentation override
    predicted take/drop operation
    predicted world suppression
    predicted hand visual / drop proxy
```

Do not write predicted values into `CarriedItemId`. That property is already synchronized in
`quest_world/character/Character.tscn`; overloading it with speculative state would make property
replication/reconciliation ambiguous and could trigger inventory-grant presentation incorrectly.

At this point it may become worthwhile to split the current `CarryComponent.OnCarriedItemChanged()` visual
instancing into a focused presentation helper/component. That split should happen **because prediction now
needs two visual sources**, not merely for aesthetic layering.

Candidate responsibilities:

```text
CarryComponent
    authoritative operation + carried truth + transaction

CarryPresentationComponent (candidate)
    resolve authoritative CarriedItemId visual
    apply predicted visual overrides
    play semantic carry motions
    reconcile prediction -> authority
```

The exact class name is not important; the separation of speculative presentation from replicated truth
is.

## 5. Correlation is the core protocol problem

The requester must know that:

```text
this predicted Take request
```

became:

```text
this authoritative GameplayAction execution
this authoritative Carry operation
```

Today `GameplayActionRequestPipeline` keys pending requests by
`GameplayActionRequestKey(Component, ActionId)`. `_pendingRequests` is a set and
`_acknowledgedExecutions` maps that key to the authoritative execution ID. This works because the runner
already forbids another pending/local execution of the same action/concurrency group while the request is
unresolved.

That existing invariant may be sufficient for the **first carry prediction implementation**: one pending
request key can own one predicted carry operation and later be confirmed by the action started ack.

Do not introduce a generic request sequence/token solely because prediction systems often have one.
First test whether the current one-pending-request-per-key/group invariant gives an unambiguous mapping.

A generic `LocalRequestId` becomes justified only if a real case appears where:

- multiple in-flight requests with the same action identity must coexist;
- an authoritative semantic carry operation can arrive without being attributable through current
  requester/action state;
- retries/out-of-order refusal/ack messages cannot be disambiguated by the current pipeline.

If that pressure appears, add a monotonically increasing runner-local request ID to the request RPC and
echo it in the requester acknowledgement. It should remain requester transport metadata, not an
authoritative gameplay execution identity.

## 6. Reuse generic requester acknowledgement, do not reuse observer execution presentation as carry state

The generic requester path is already reliable and should remain the source for request outcome:

- `ClientActionStarted` confirms accepted execution;
- `ClientActionRejected` removes an unacknowledged prediction;
- `ClientActionCancelled`, `Completed`, and `Failed` end the requester-visible execution.

Carry prediction should listen to/compose that lifecycle for confirmation and rollback.

By contrast, do not force carry animation state into
`GameplayActionExecutionPresentation`. Current replicated execution snapshots contain:

```text
action_id
execution_id
progress_present
progress_base
progress_per_second
revision
```

and intentionally represent transient action execution on the **host action component**. They do not carry
an instigator identity. A take action hosted by a battery therefore cannot, from that snapshot alone, tell
an observer which character rig should animate.

Level 2 already solves this locally by replicating semantic carry state from the actor's `CarryComponent`.
Keep that design for 2.5. Generic Gameplay Action should not gain actor identity merely to predict carry.

The missing-instigator question should be revisited only when the future generic choreography system has
multiple concrete domains that all need actor-attributed observed execution presentation.

## 7. Predicted interruption

The level-2 generic interrupt mechanism should also clear local prediction immediately.

Example:

```text
client starts predicted Take
    ↓
movement input remains/appears before commit
    ↓
local runner NotifyInterrupt("movement")
    ↓
matching pending/acknowledged requester action is locally retired for presentation
    ↓
Carry prediction is cancelled immediately
    ↓
server receives authoritative movement/request cancellation path
    ↓
authority cancels the real execution if still pre-commit
```

The user should never wait a round trip to visually stop an action they just interrupted locally.

However, local interruption remains speculative until authority confirms the terminal outcome. If the
server had already committed before seeing the interrupt, authoritative `CarriedItemId`/inventory/world
truth wins and the client reconciles to "item carried" rather than attempting gameplay rollback.

This is another reason the level-2 rule "generic execution completes at commit" is valuable: the
post-commit state is terminal and unambiguous.

## 8. Clock reconciliation

Carry motion has an authored duration and commit time. Prediction and authority will not start at exactly
the same wall-clock instant.

The first implementation does not need a generic networked animation timeline. It needs only enough
reconciliation to prevent obvious restart/pop behavior:

- predicted requester continues from its local phase when authoritative acceptance arrives;
- if authoritative phase error is small, keep local playback and let final truth reconcile at commit;
- if authoritative semantic operation arrives substantially ahead/behind, seek or adjust presentation
  locally as needed;
- observers start from replicated operation phase/progress rather than always from zero, so late state
  application does not replay a completed pickup from the beginning.

Use the same design principle as `TimedExecution`: authority owns the gameplay clock; clients derive smooth
presentation locally from sparse authoritative information rather than receiving a network update every
frame.

## 9. Failure and reconciliation matrix

### Request rejected before authoritative start

```text
predicted animation -> cancel/blend out
predicted visual commit -> undo if it already occurred
world/inventory truth -> unchanged
```

### Authority cancels before commit

```text
predicted animation -> cancel/blend out
predicted visual override -> clear
world/inventory truth -> unchanged
```

### Authority fails at commit

```text
predicted hand/drop visual -> undo
animation -> failure/cancel recovery
atomic carry transaction -> already preserved/restored authoritative truth
```

### Authority commits before local interrupt arrives

```text
local prediction may have cancelled visually
server truth says committed
-> restore/confirm carried or dropped authoritative presentation
-> no gameplay rollback
```

### Observer/late join

No prediction. `CarriedItemId` remains final carried truth. If a transient carry operation snapshot is
present, presentation may enter the appropriate current phase; otherwise the final state is sufficient.

## Expected code pressure

Gameplay Action core, only where required by real correlation/interruption behavior:

- `GameplayActionRequestPipeline.cs`
  - expose/notify enough requester lifecycle to associate local predicted carry presentation;
  - possibly extend local interruption handling to pending predictions;
  - add a request ID only if current request-key uniqueness proves insufficient;
- `GameplayActionRunner.cs`
  - public signals/API should remain the local requester-facing integration point;
- `GameplayActionExecutionPresentationStore.cs`
  - continue owning generic progress prediction; no carry-specific payload should be added here.

QuestWorld carry:

- `CarryComponent.cs`
  - authoritative operation remains unchanged in principle;
  - expose semantic authoritative operation state needed for confirmation/reconciliation;
- probable new carry presentation helper/component
  - own predicted animation and visual overrides;
- `Character.cs`
  - bridge local action request/interrupt lifecycle to carry prediction without hard-coded target-specific
    mutation;
- `CharacterAnimationController.cs` / `Character.tscn`
  - allow semantic motion start/seek/cancel without making the animation graph authoritative;
- network/runtime tests
  - requester sees immediate predicted take/drop;
  - observer stays server-driven;
  - rejection/cancellation clears prediction;
  - server commit confirms prediction;
  - interrupt-vs-commit race reconciles to authority;
  - failed authoritative drop restores carried presentation;
  - late join sees final carry truth.

## Non-goals

- no client execution of `TakeExecutor` / `DropExecutor`;
- no speculative inventory quantities;
- no speculative authoritative `CarriedItemId`;
- no client-owned spawned gameplay object;
- no generic rollback framework;
- no generic choreography yet;
- no requirement to add instigator identity to generic replicated execution presentation;
- no generic request ID unless the current request-key invariant actually fails;
- no per-frame networked animation position.

## Success criteria

A 100–200 ms RTT session should feel local to the requester while remaining authority-correct:

1. pressing Take begins the local character motion immediately;
2. other peers begin only from authoritative carry replication;
3. the requester may visually attach/hide the item at the predicted contact point without mutating
   inventory/world truth;
4. accepted authority confirms prediction without replaying/restarting the motion;
5. rejection, cancellation and commit failure visibly undo speculative presentation;
6. a local movement/jump interrupt stops predicted presentation immediately;
7. an interrupt racing an already-authoritative commit resolves to authoritative carried/dropped truth;
8. no executor gameplay code runs on a client;
9. existing generic Gameplay Action progress prediction remains unchanged in responsibility.

Once this slice is stable, carry has exercised authority, semantic replication, interruption, prediction,
visual commit and reconciliation. That is enough real pressure to design the general animated interaction
system in [animated-interaction-choreography.md](animated-interaction-choreography.md).