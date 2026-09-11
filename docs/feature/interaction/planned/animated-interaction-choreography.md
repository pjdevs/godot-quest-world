# Animated interaction choreography

> **Status: planned.** Generalization target after the authoritative and predicted carry vertical slices.
> This proposal defines the problem and architectural boundaries for multi-phase animated interactions;
> it should not be implemented until at least one non-carry interaction demonstrates the need.

## Motivation

Carry is intentionally the proving ground, not the abstraction target.

The preceding proposals exercise a narrow sequence:

```text
start motion
    ↓
commit carry transaction
    ↓
complete gameplay execution
    ↓
cosmetic animation tail
```

That shape is ideal for take/drop because gameplay can become terminal at the contact point. A richer
interaction has a different lifecycle.

Example: search a container or body.

```text
Engage       bend down / align to target
Work         search, force, lift, loop or wait
Commit       add found item / mutate target state
PostCommit   inspect found object / reaction
Disengage    stand up / return control
Complete
```

The generic action must remain reserved while the actor is still engaged with the interaction even though
the irreversible gameplay mutation may have happened before the final animation ends.

This creates two concepts that carry level 2 deliberately avoided:

1. **commit is not completion**;
2. cancellation before commit and interruption after commit do not necessarily mean the same thing.

The purpose of this proposal is to extract the smallest reusable choreography model once real content
needs those semantics.

## Existing architecture to preserve

The current Interaction architecture is intentionally layered over Gameplay Action:

```text
Interaction
    detection / focus / spatial access / target presentation
        ↓
Gameplay Action
    rules / reservation / execution / requester networking / progress
        ↓
domain executor
    actual gameplay command
```

`docs/feature/interaction/interaction.md` explicitly states that Interaction owns no second execution
engine. `GameplayActionComponent` is the authoritative owner of reservations and lifecycle. A
choreography executor receives the generic execution context directly; it does not need an
Interaction-specific executor adapter or context.

That remains the core invariant.

A choreography system must therefore **compose a running Gameplay Action execution**. It must not add an
Interaction-specific execution ID, reservation table, RPC lifecycle, or completion protocol.

Current pieces already provide much of the substrate:

- `GameplayActionExecutionRunning` holds the reservation;
- `GameplayActionComponent.CompleteExecution`, `CancelExecution`, and `FailExecution` own terminal state;
- executor terminal callbacks clean up composed helpers;
- `TimedExecution` is precedent for a reusable helper that attaches time/progress to one execution;
- requester presence/access is already revalidated while relevant executions run;
- the planned generic interrupt path routes movement/jump/damage/etc. to the same existing cancellation
  lifecycle;
- execution presentation already supports requester prediction and replicated observed progress where
  useful.

The choreography abstraction should be built from these pieces rather than replacing them.

## Scope trigger

Do not implement a generic choreography executor merely because the carry proposals resemble one.

The extraction becomes justified when at least one non-carry feature requires a sequence such as:

```text
Engage -> Work -> Commit -> Disengage
```

and reproduces the same problems already solved locally for carry:

- authoritative phase timing;
- actor-side semantic presentation;
- requester interruption;
- an irreversible gameplay commit boundary;
- post-commit continuation;
- network observation and possibly requester prediction.

Likely first candidates are search/loot, installing a battery, forcing/opening a heavy mechanism, or a
long interaction that aligns/poses the player before and after the actual world mutation.

## Goal

Provide a generic **execution choreography policy** that can drive phases around one existing Gameplay
Action execution while delegating actual gameplay mutation to domain code.

Conceptual model:

```text
GameplayAction execution
|-----------------------------------------------------------|

Engage       Work            Commit      PostCommit    Disengage
|------------|---------------|-----------|-------------|------|
                             ^
                     irreversible gameplay boundary
```

The choreography owns sequencing and phase boundaries. It does not know what inventory, doors, quests,
carry, dialogue, or stateful transitions mean.

## 1. Choreography is not an animation playlist

The generic model should expose **semantic phases/events**, not concrete animations.

Bad abstraction:

```text
PlayAnimation("BendDown")
Wait(0.4)
PlayAnimation("Search")
CallInventoryAdd(...)
PlayAnimation("LookAtItem")
PlayAnimation("StandUp")
```

That couples gameplay execution to one rig and turns a gameplay helper into a cutscene scripting system.

Preferred abstraction:

```text
phase = Engage
phase = Work
commit boundary reached
phase = PostCommit
phase = Disengage
```

An actor-side presentation component maps those semantic phases to its own animation graph, alignment,
IK, camera or first-person representation.

This is the same boundary established by animated carry: the actor/domain component owns semantic
presentation replication; concrete UAL animation names remain inside character presentation.

## 2. One generic execution, one choreography instance

A candidate generic helper can mirror `TimedExecution` structurally:

```text
ChoreographyExecution
    Start(component, executionId, profile/context)
    CurrentPhase
    HasCommitted
    Advance / authoritative clock or phase transitions
    Stop(executionId)
```

A candidate executor base may compose it:

```text
ChoreographedGameplayActionExecutor : GameplayActionExecutor
```

but inheritance should only be chosen if several real executors share enough lifecycle boilerplate. The
more important invariant is composition:

- choreography attaches to an already reserved `ExecutionId`;
- it does not reserve anything itself;
- completion/cancel/failure still go through `GameplayActionComponent`;
- it stops from all executor terminal callbacks;
- it may publish generic progress if the authored sequence has a meaningful normalized projection, but
  progress is optional and secondary to phase semantics.

`TimedExecution` should remain a focused linear timer. Do not stretch it into a phase state machine only to
avoid one new helper.

## 3. Commit is a first-class boundary, not a terminal state

The choreography needs an explicit one-way transition:

```text
PreCommit -> Committed
```

`Commit` means the domain executor performs the irreversible authoritative gameplay mutation exactly once.
It does **not** necessarily call `CompleteExecution`.

Example search flow:

```text
Engage
    actor bends down
Work
    actor searches for 2 s
Commit
    authoritative loot transfer succeeds
PostCommit
    actor looks at found item
Disengage
    actor stands up
CompleteExecution
```

The domain-specific executor remains responsible for the actual commit command. The generic choreography
helper should signal/callback "commit boundary reached"; it should not own arbitrary gameplay callbacks
encoded in Resources.

This preserves the existing Gameplay Action principle that one executor owns the gameplay command.

## 4. Cancellation semantics change at commit

Before commit, cancellation normally means the action did not happen:

```text
movement / jump / damage / access loss
    ↓
CancelExecution
    ↓
stop work
    ↓
optional disengage/recovery presentation
    ↓
no gameplay mutation to undo
```

After commit, simply calling `CancelExecution` may be semantically wrong. The item is already in inventory,
the door is already unlatched, or the searched container is already consumed.

The choreography therefore needs a policy distinction between:

```text
pre-commit cancellation
post-commit interruption
```

A post-commit interrupt should normally mean:

```text
skip/shorten optional PostCommit
    ↓
run or fast-forward Disengage/recovery
    ↓
CompleteExecution
```

not:

```text
rollback committed gameplay
```

### Interaction with generic interrupts

The planned generic runner interrupt path should ask the executor's pure interruption policy whether the
current event should cancel **now**.

This is why the level-2 proposal leaves room for something equivalent to:

```text
ShouldCancelOnInterrupt(context, interrupt)
```

rather than only a static membership test. A choreography-aware executor can answer based on
`HasCommitted`:

- before commit: movement may request real `CancelExecution`;
- after commit: movement may be consumed as a choreography interruption that changes the remaining phase
  flow but does not terminally cancel the Gameplay Action.

Do not add this complexity to the generic interrupt system until choreography exists. Static interrupt
sets are sufficient for carry level 2 because carry completes exactly at commit.

## 5. Disengage is presentation/control recovery, not gameplay rollback

An interrupted choreography often still needs to restore actor state:

- blend out a full-body animation;
- release alignment/root-motion constraints;
- restore locomotion or look controls;
- return camera state;
- leave the target pose safely.

This cleanup may outlive the decision that gameplay is cancelled or committed.

Do not model those actor concerns as Gameplay Action reservation state. The generic choreography can expose
a semantic `Disengage`/`Recover` phase while the actor presentation system owns the actual animation and
control restoration.

A hard interruption may be allowed to skip that phase when the character system already has a stronger
state transition (death, ragdoll, teleport). The choreography should define semantic intent, not require
all presentations to finish a particular clip.

## 6. Work phase must support more than a linear timer

The first search case may be time-based, but the abstraction should not assume every `Work` phase is one
linear duration.

Real future interactions may progress by:

- time;
- repeated authored steps;
- external gameplay events;
- server-side systems adding progress in chunks;
- waiting for another actor/system;
- looping until a condition or explicit commit request.

Gameplay Action progress already supports discrete published values, local derived sources and sparse
linear samples. A choreography should **use** those existing primitives where useful instead of defining a
parallel progress protocol.

The generic phase model can therefore be event/step driven while a particular choreography profile uses a
linear timer for Engage or Work.

## 7. Actor alignment and orientation are part of Engage, but remain replaceable

Complex animated interactions often need the actor to stand at a specific transform relative to the
target before the main animation looks correct.

This is likely part of the semantic `Engage` phase, but it should not create a hard dependency from
Gameplay Action or Interaction core to the dummy character motor.

A future actor choreography presentation/driver may support capabilities such as:

```text
align to interaction anchor
face target
lock/limit movement
apply root motion / motion warp
enter semantic pose
```

The interaction target already owns an `InteractionAnchor`; this is a useful authored spatial reference,
but the generic choreography system should consume an explicit target/alignment context rather than
silently search the scene tree.

Different actors can implement alignment differently or decline it entirely.

## 8. Target access remains separate from actor interruption

Do not merge choreography phases into Interaction's access provider.

The current sustained-access path answers whether the requester still has domain access to the externally
owned action: target remains detectable/valid, requester remains present, etc. It uses the same
`IGameplayActionAccessProvider.ResolveAccess()` semantics by design.

Runtime events such as movement, jump, damage or knockback belong to the generic interruption channel.
Choreography consumes both:

```text
lost target/access --------┐
movement/jump/damage ------┤
explicit input release ----┤
                           ↓
                  phase-aware interruption policy
```

Rules also remain start-time availability only. Do not repeatedly evaluate `GameplayActionRule` as a
choreography invariant.

If future content reveals genuine continuously evaluated execution invariants that are neither access nor
events, introduce a separate `ExecutionCondition` concept at that time. This proposal does not require
one.

## 9. Replication model

The carry proposals deliberately replicate semantic motion from the **actor-side domain component** because
generic execution presentation currently lives on the host action component and has no instigator
identity.

A generic choreography system will force this question again.

Two viable designs should be evaluated from the first non-carry implementation.

### Option A — actor-side choreography state

The instigator owns a `ChoreographyComponent`/driver that replicates:

```text
choreography id / semantic profile id
phase
phase start/progress
committed flag if presentation needs it
relevant target reference/identity if safe to transport
```

Advantages:

- observers naturally know which actor to animate;
- mirrors the proven carry topology;
- keeps host execution presentation focused on the action host;
- no generic Gameplay Action protocol expansion.

Cost:

- actor must have a choreography capability/component;
- domain executor must start/associate that actor choreography.

### Option B — enrich generic execution presentation with instigator identity

`GameplayActionExecutionPresentation`/replicated entries gain enough actor attribution for an observer to
find the instigator and project semantic choreography state from the execution itself.

Advantages:

- one generic observed-execution stream;
- may benefit non-animation systems that also need actor attribution.

Cost:

- network-safe Node identity/path semantics become part of generic Gameplay Action transport;
- not all actions have an instigator or need actor presentation;
- risks growing generic presentation for one feature family.

### Recommendation

Do **not** choose Option B from theory. Carry already proves Option A works locally. Start the first generic
animated interaction with actor-side choreography state unless at least two independent consumers need
instigator-attributed generic execution presentation. Revisit only with evidence.

## 10. Prediction model

Prediction should follow the same lessons as predicted carry:

- requester predicts actor presentation, never gameplay mutation;
- authority owns phase clock and commit;
- observed peers remain server-driven;
- rejection/pre-commit cancellation clears predicted presentation;
- post-commit authoritative truth wins over a local interruption race;
- sparse authoritative phase/timing data reconciles local playback.

A generic choreography predictor should only be extracted after predicted carry and one non-carry sequence
share the same correlation/reconciliation needs.

Do not make every choreography predicted by default. Some interactions may be too constrained,
server-dependent, or rare to justify speculative presentation.

## 11. Suggested semantic profile shape

The eventual authored data should be descriptive rather than imperative. A conceptual profile may expose:

```text
ChoreographyProfile
    Engage
        semantic motion id
        completion policy / duration if time-based
    Work
        semantic motion id
        optional progress mapping
    Commit boundary
    PostCommit
        optional semantic motion id / phase
    Disengage
        semantic motion id
```

This is intentionally not a final API. Real implementations should determine whether phases are Resources,
nodes, compact structs, or executor-owned policy.

Avoid a generic list of arbitrary commands/callback Resources such as:

```text
Wait
PlayAnimation
CallMethod
SetProperty
Spawn
GrantItem
```

That would become a second scripting language and weaken the current one-executor ownership model.

## 12. Example: search interaction

A concrete target shape could remain entirely within current Interaction topology:

```text
SearchableContainer
├── GameplayActions
│   └── Search : GameplayAction
│       └── SearchExecutor : choreographed executor
├── GameplayActionExecutionSynchronizer?   # only if generic execution UI needs it
├── InteractiveComponent
├── InteractionAnchor
└── StatefulComponent / loot state
```

Execution:

```text
SearchExecutor.Execute(context)
    ↓
resolve actor choreography capability
    ↓
start Search choreography
    ↓
return Running

Engage complete
    ↓
Work begins

Commit boundary
    ↓
SearchExecutor performs authoritative loot/state mutation once
    ↓
mark choreography committed
    ↓
continue PostCommit

Disengage complete
    ↓
context.Component.CompleteExecution(context.ExecutionId)
```

Pre-commit movement interrupt:

```text
runner interrupt
    ↓
CancelExecution
    ↓
executor/choreography stop or recovery
    ↓
no loot granted
```

Post-commit movement interrupt:

```text
runner interrupt
    ↓
executor sees committed choreography
    ↓
do not CancelExecution
    ↓
skip inspect / accelerate Disengage
    ↓
CompleteExecution
```

This distinction is the main semantic reason a generic choreography abstraction exists.

## 13. Relationship to carry after extraction

Do not require carry to migrate immediately to the generic system.

Carry level 2/2.5 is intentionally optimized for:

```text
start -> commit == GameplayAction completion -> cosmetic tail
```

If the generic choreography helper later makes that implementation materially simpler without weakening
its networking/prediction behavior, carry may adopt it as a one-commit sequence. If not, carry can remain a
small specialized implementation and still have served its architectural purpose.

Extraction success is measured by reduced duplicated semantics across complex interactions, not by forcing
every animation into one framework.

## Expected code pressure when the trigger exists

Gameplay Action:

- likely new focused helper under `runtime/execution/`, analogous in role to `TimedExecution` but phase
  oriented;
- optional executor base/composition helper only after multiple executors share terminal cleanup;
- generic interrupt policy may gain a phase-aware pure query hook;
- existing `GameplayActionComponent` terminal lifecycle should remain unchanged;
- existing progress presentation should be reused rather than replaced.

Interaction:

- choreography executors consume the generic Gameplay Action execution context directly;
- no new Interaction execution component or network synchronizer;
- `InteractionAnchor` may feed explicit alignment context;
- presence/access behavior remains as documented.

Character / project actor:

- likely actor-side choreography driver/presentation component;
- semantic phase -> UAL animation/alignment/control mapping;
- server-driven replication first, requester prediction later;
- integration with movement/jump/damage interrupts.

Domain feature (search/install/etc.):

- one executor owns commit mutation;
- one profile/semantic sequence describes actor presentation phases;
- domain tests prove commit-once and pre/post-commit interruption semantics.

## Non-goals

- no new Interaction execution engine;
- no arbitrary command graph or cutscene scripting language;
- no gameplay mutation authored as animation callbacks;
- no assumption that every phase is time-based;
- no automatic rollback of committed gameplay;
- no mandatory client prediction;
- no requirement that every actor uses skeletal animation;
- no Gameplay Ability System recreation;
- no repeated evaluation of availability rules during execution;
- no forced migration of simple carry if specialization remains cleaner.

## Success criteria for eventual implementation

The abstraction is justified only if a real non-carry interaction can demonstrate all of the following:

1. one `GameplayActionExecutionRunning` remains the sole reservation/lifecycle identity;
2. semantic Engage/Work/Commit/PostCommit/Disengage phases are visible on the correct actor without concrete
   animation names entering generic gameplay/network code;
3. the domain executor performs irreversible commit exactly once;
4. commit does not require generic execution completion;
5. pre-commit interruption can cancel with no gameplay rollback;
6. post-commit interruption can alter/shorten remaining choreography without undoing committed gameplay;
7. target access loss and actor semantic interrupts remain separate inputs to the phase policy;
8. generic progress and requester networking are reused where applicable;
9. observed/predicted presentation follows the authority boundaries proven by carry;
10. no second execution engine or imperative choreography scripting layer is introduced.

Until those needs exist in real content, keep this document as the extraction target and continue using the
smaller carry-specific implementation.
