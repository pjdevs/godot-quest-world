# Replicated execution observability

> **Status: exploratory.**  
> This proposal captures a possible evolution of Gameplay Action execution networking. It is not required for the current carry spike and should not block project gameplay work.

## Motivation

Gameplay Action already replicates a read-only representation of running executions through `GameplayActionExecutionPresentation`.

A replicated execution presentation currently provides enough information for UI and progress display:

- authoritative execution ID;
- action ID;
- optional progress;
- requester/observer relation;
- prediction for the local requester;
- sparse authoritative corrections;
- observer replication;
- late-join reconstruction.

This works well when another peer only needs to know:

> “this action is currently executing, at approximately this progress.”

Some gameplay actions however have an **actor-visible execution lifecycle**:

- picking up or dropping an object;
- reviving another player;
- searching a container;
- planting an object;
- operating a heavy mechanism;
- other choreographed interactions.

For these actions, remote peers need more than a progress bar. They need enough execution context to locally reproduce the presentation associated with the authoritative execution.

Without generic support, each gameplay domain risks introducing its own replicated transient operation state such as `CarryOperation`, `ReviveOperation`, `SearchOperation`, etc.

## Observation

The existing execution presentation system already solves most of the difficult networking problems:

```text
authority
    ↓
authoritative GameplayActionExecution
    ↓
replicated execution presentation
    ├── stable ExecutionId
    ├── ActionId
    ├── progress sample
    └── lifecycle by presence/removal
         ↓
requester prediction / reconciliation
observers
late joiners
```

The missing capability is therefore probably **not replication of the executor object itself**.

Instead, Gameplay Action could eventually support a richer concept of an **observable remote execution**.

## Desired model

Authority remains the only peer performing authoritative gameplay execution.

```text
authority
    ↓
real execution
    ↓
executor performs gameplay mutations
```

Other peers receive a shadow/read-only execution:

```text
replicated execution
    ↓
resolve local action occurrence
    ↓
reconstruct remote execution context
    ↓
presentation/choreography hooks
```

The same executor definition may participate in remote presentation, but remote observation must never invoke authoritative gameplay mutation paths.

Conceptually:

```text
                    authority
                        │
                  real execution
                        │
              replicated execution state
                  /                \
                 /                  \
        requesting peer          observer
        predicted shadow         observed shadow
        RequestedLocally         Observed
```

## Additional information likely required

The current execution presentation contains:

```text
ExecutionId
ActionId
Progress
Relation
```

A remotely usable execution would likely need enough additional identity to answer:

### Who

Which actor/instigator is performing the action.

### Where

Which gameplay action occurrence/component/host owns the execution.

### What state

Optional semantic execution state beyond normalized progress when required, for example:

```text
Windup
Committed
Recovery
```

This should remain generic and minimal rather than becoming an arbitrary replicated executor object graph.

## Presentation hooks

Executors should not simply execute normally on every peer.

The framework could eventually expose a distinct observation lifecycle, conceptually similar to:

```text
OnExecutionObserved(...)
OnObservedExecutionUpdated(...)
OnObservedExecutionEnded(...)
```

or an equivalent presentation-facing abstraction.

An observer can then translate a semantic execution into local presentation:

```text
Take execution observed
    ↓
resolve remote instigator
    ↓
play character TakeGround animation
    ↓
seek/reconcile using execution progress
```

Only authority performs:

```text
TryTake()
inventory mutation
world object deletion
execution completion
```

## Lifecycle considerations

Current replicated execution presentation mostly represents lifecycle by presence:

```text
entry appears      -> running
entry updates      -> still running
entry disappears   -> no longer running
```

That is sufficient for UI.

Choreography may eventually require stronger terminal information:

```text
Completed
Cancelled
Failed
```

This should only be added if real gameplay cases require different remote presentation for those outcomes.

## Execution lifetime versus gameplay commit

Choreographed actions introduce an important distinction:

```text
animation      start ----- commit -------- recovery ----- end
gameplay                     ^
                             mutation
```

Two models remain possible.

### Execution ends at commit

The authoritative gameplay execution completes as soon as the gameplay mutation succeeds.

Presentation may outlive the execution.

This keeps gameplay semantics clean but requires a second transient presentation lifetime.

### Execution survives through recovery

The execution remains running until the visible choreography ends.

The executor tracks whether gameplay has already committed so later cancellation cannot roll back committed state.

This makes replicated execution itself sufficient to drive the full remote choreography.

The framework should not decide between these models until concrete gameplay pressure makes the tradeoff clear.

## Relationship with current execution presentation

This proposal should evolve the existing system rather than replace it.

The current execution presentation already provides:

- prediction;
- requester reconciliation;
- observer visibility;
- sparse progress replication;
- progress extrapolation;
- stable execution identity;
- late-join state.

A richer observable execution should reuse these mechanisms.

The likely evolution is therefore:

```text
GameplayActionExecutionPresentation
            ↓
richer replicated execution identity/context
            ↓
optional remote presentation lifecycle
```

rather than introducing a second generic execution networking system.

## Non-goals

This proposal does not imply:

- replicating C# executor instances;
- running authoritative gameplay mutations on observers;
- reproducing the entire GAS ability replication model;
- generic choreography authoring immediately;
- replacing domain state replication;
- requiring every gameplay action to be observer-visible;
- adding arbitrary replicated payloads without demonstrated need.

Simple actions should continue to rely on authoritative domain state replication.

Only actions whose **execution itself is visibly meaningful** need richer observability.

## Example: carry

Today a carry implementation could replicate a domain-specific transient operation:

```text
CarryOperation
    Kind = TakeGround
    Progress = ...
    Active = true
```

A future replicated execution model could instead allow:

```text
Take GameplayActionExecution
    Instigator = Player3
    Host = Battery
    Progress = 0.42
    Relation = Observed
```

The remote side resolves the same Take action and asks its presentation path to reproduce the appropriate character animation.

This avoids teaching `CarryComponent` how to implement generic transient execution networking.

## Open questions

Before implementation, real gameplay usage should answer:

- whether instigator identity alone is sufficient;
- whether host/component identity must also be replicated;
- whether normalized progress covers most choreography;
- whether semantic phases are actually required;
- whether terminal reason must be replicated;
- whether execution should normally end at gameplay commit or presentation end;
- whether presentation hooks belong directly on executors or on a separate companion abstraction;
- which execution visibility mode should opt into richer observation.

## Direction

The important architectural hypothesis is:

> `GameplayActionExecutionPresentation` may already be the foundation of a generic replicated execution model.

Rather than building replicated transient operation machinery independently in each gameplay domain, Gameplay Action could eventually expose enough execution identity and lifecycle for remote peers to reconstruct **presentation-only shadow executions**.

Authority would continue to own gameplay truth.

Remote execution would exist only to make that authoritative execution visible.
