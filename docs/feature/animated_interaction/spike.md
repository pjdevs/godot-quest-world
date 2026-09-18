# Animated Interaction — Minimal Spike

> **Status:** experimental spike, minimally implemented
> **Goal:** validate a minimal reusable choreography layer for animated gameplay actions before generalizing it.

The current implementation and known limits are documented in
[animated-interaction.md](./animated-interaction.md).

## 1. Motivation

Some gameplay actions require several animation stages while the authoritative gameplay execution remains active.

Examples:

```text
Force locked door
Enter → ForceLoop ↻ → Open → Exit

Search
BendDown → SearchLoop ↻ → Inspect → StandUp

Terminal
Enter → HackLoop ↻ → Exit

Carry
Take animation
        ↑ commit during animation
```

Gameplay Action already owns:

- execution lifecycle;
- requester/host concurrency;
- sustained input;
- access/reservations;
- authoritative completion/cancellation;
- gameplay progress.

Animated Interaction must **not duplicate those responsibilities**.

Its only responsibility is to coordinate actor presentation while a Gameplay Action is running.

---

# 2. Core model

An action may request an animated interaction.

The actor must expose the capability to play one.

```text
GameplayAction
    ↓
Executor
    ↓
IAnimatedInteractor
    ↓
AnimatedInteractionComponent
    ↓
AnimatedInteractionOperation
```

The action/executor decides **what interaction should happen**.

The actor decides **how that interaction is presented**.

---

# 3. AnimatedInteractionComponent

A character may own one:

```text
Character
├── GameplayActionComponent
├── GameplayActionRunner
└── AnimatedInteractionComponent
```

For the spike, the component supports at most **one active animated interaction**.

Conceptual API:

```csharp
AnimatedInteractionOperation? TryStart(
    AnimatedInteractionProfile profile
);

void CancelCurrentOperation();
```

The component owns:

```text
current operation
current phase
phase transitions
animation playback
loop playback
authoritative presentation / RPC
cancel presentation
commit-point timing when presentation owns it
```

It does **not** own:

```text
gameplay work duration
Gameplay Action progress policy
input requirements
door state
inventory
loot
carry state
GameplayAction completion
```

---

# 4. AnimatedInteractionProfile

A profile defines an ordered sequence of presentation phases.

```text
AnimatedInteractionProfile
    Phases[]
```

A phase contains presentation information:

```text
AnimatedInteractionPhase

Animation

Playback:
    OneShot
    Loop

Transition:
    Automatic
    Controlled

OptionalCommitPoint
```

### Automatic

The phase advances when its presentation finishes.

```text
Approach
animation = ReachHandle
playback = OneShot
transition = Automatic
```

### Controlled

The phase remains active until gameplay explicitly requests advancement.

```text
Force
animation = ShoulderPush
playback = Loop
transition = Controlled
```

Gameplay later calls:

```csharp
operation.Advance();
```

Animated Interaction does not know why.

---

# 5. AnimatedInteractionOperation

An active operation exposes only the small choreography lifecycle needed by the spike.

Conceptually:

```csharp
CurrentPhase

Advance()
ReachCommit()
Cancel()

PhaseEntered
CommitReached
Finished
Cancelled
```

There is **one semantic commit boundary per operation**.

There are no arbitrary string markers such as:

```text
"attach_item"
"unlock"
"grant"
"open"
```

and no generic list of timeline events.

If future gameplay proves that multiple independent gameplay markers are required, that need must be evaluated separately.

---

# 6. Commit boundary

`CommitReached` means:

> the choreography/gameplay has reached the point where the executor should attempt the irreversible domain mutation.

Animated Interaction never performs that mutation itself.

There are two ways to reach the same boundary.

## Presentation-driven commit

Some gameplay mutation must happen at a precise point inside an animation.

Carry is the reference case:

```text
TakeGround
|----------------------------|
          ↑
     commit point
```

The authored phase may contain a commit offset.

When that point is reached:

```text
AnimatedInteraction
↓
CommitReached
↓
TakeExecutor / Carry domain
↓
TryCommitTake()
```

## Gameplay-driven commit

Other interactions reach their commit because gameplay work finishes.

Door:

```text
ForceLoop ↻
     ↑
Gameplay work reaches 100 %
```

The executor requests the commit boundary:

```text
TimedExecution.Expired
↓
operation.ReachCommit()
↓
CommitReached
↓
Door.TryForceOpen()
```

Search and terminal can follow the same pattern.

The choreography therefore exposes **one commit concept**, independent of what caused it.

---

# 7. Gameplay Action relationship

Starting an Animated Interaction does **not** create another execution.

The existing Gameplay Action remains the only authoritative execution.

Typical flow:

```text
Execute()
↓
start AnimatedInteraction
↓
return GameplayActionExecutionRunning
```

The Gameplay Action stays `Running` while the executor considers the actor engaged in the action.

When the animated interaction finishes:

```text
AnimatedInteraction.Finished
↓
executor
↓
CompleteExecution()
```

If Gameplay Action is cancelled:

```text
OnExecutionCancelled()
↓
stop gameplay work
↓
AnimatedInteraction.Cancel()
```

Animated Interaction itself never calls:

```text
CompleteExecution
CancelExecution
FailExecution
```

Those remain Gameplay Action / executor responsibilities.

---

# 8. Gameplay work and presentation time are separate

Variable gameplay duration must not be authored as animation phase duration.

Example:

```text
Open locked container

high skill → 2 seconds work
low skill  → 5 seconds work
```

The choreography remains identical:

```text
Enter
↓
WorkLoop ↻
↓
Open
↓
Exit
```

Gameplay owns the variable work duration.

By default, work progress begins when the relevant work phase is entered:

```text
Execute
↓
Enter
    progress = 0

WorkLoop entered
↓
start gameplay work clock
    progress 0 → 100 %

work expires
↓
commit

Open / Exit
    progress = 100 %

Finished
↓
CompleteExecution
```

This keeps character-specific presentation timing independent from gameplay duration.

A game may explicitly choose total input-to-result duration instead, but that remains an executor/gameplay policy rather than Animated Interaction behavior.

---

# 9. Reusing TimedExecution as the work clock

The existing `TimedExecution` already owns most of the required primitive:

```text
authoritative monotonic clock
execution attachment
linear progress
local progress source
sparse authoritative corrections
```

Its current limitation is that expiration directly calls:

```text
CompleteExecution()
```

That makes it a completion policy rather than a generally composable execution clock.

For this spike, refactor the responsibility boundary.

## TimedExecution

`TimedExecution` becomes:

```text
TimedExecution

Start(component, executionId, duration)
Stop()

Progress

Expired
```

At expiry it:

```text
progress → 100 %
↓
Expired
```

It does **not** complete the Gameplay Action.

It should leave/freeze the execution progress at `1.0` while the Gameplay Action may remain Running through its presentation tail.

## Existing timed executors

Existing timed executors preserve their current behavior by handling:

```text
TimedExecution.Expired
↓
CompleteExecution()
```

Therefore:

```text
TimedGameplayActionExecutor
```

remains the policy:

> complete this Gameplay Action when its timer expires.

`TimedExecution` itself becomes only the reusable authoritative timing/progress primitive.

## Animated gameplay executors

A door/search executor can instead handle:

```text
TimedExecution.Expired
↓
operation.ReachCommit()
```

and keep Gameplay Action Running afterward.

This allows the same primitive to support both:

```text
Simple timed action
0 → 100 %
↓
CompleteExecution
```

and:

```text
Animated work
0 → 100 %
↓
commit
↓
Open
↓
Exit
↓
CompleteExecution
```

---

# 10. Sustained input remains Gameplay Action behavior

Animated Interaction has no concept of buttons or hold input.

For an interaction that must remain held:

```text
ActivationMode = Press
InputRequirement = Pressed
```

Gameplay Action starts immediately.

While input remains held:

```text
GameplayAction = Running
AnimatedInteraction = running
```

If input is released before the commit:

```text
runner
↓
CancelExecution
↓
executor.OnExecutionCancelled
↓
TimedExecution.Stop()
AnimatedInteraction.Cancel()
```

No `SustainedAnimatedInteraction` concept is required.

---

# 11. Commit does not imply completion

On successful commit, the domain mutation has happened but actor presentation may still need to finish.

Example:

```text
ForceLoop
↓
CommitReached
↓
Door.TryForceOpen()

success
↓
ReleaseRequesterDependency()
optional ReleaseAccessReservation()
↓
AnimatedInteraction.Advance()

Open
↓
Exit
↓
Finished
↓
CompleteExecution()
```

After `ReleaseRequesterDependency()`, releasing the original sustained input must no longer cancel the already committed operation.

This preserves:

```text
gameplay progress == 100 %
!=
GameplayAction completed
```

and:

```text
gameplay committed
!=
actor disengaged
```

---

# 12. Example — skill-based locked door

Profile:

```text
Enter
    ReachHandle
    OneShot
    Automatic

Force
    ShoulderPush
    Loop
    Controlled

Open
    ForceDoorOpen
    OneShot
    Automatic

Exit
    ReleaseDoor
    OneShot
    Automatic
```

Execution:

```text
Execute
↓
start AnimatedInteraction
↓
return Running

Enter
↓
Force entered
↓
duration = ComputeForceDuration(skill)
TimedExecution.Start(duration)

ForceLoop ↻
progress 0 → 100 %

TimedExecution.Expired
↓
operation.ReachCommit()
↓
CommitReached
↓
Door.TryForceOpen()
```

Failure:

```text
FailExecution()
↓
AnimatedInteraction.Cancel()
```

Success:

```text
ReleaseRequesterDependency()
↓
operation.Advance()

Open
↓
Exit
↓
Finished
↓
CompleteExecution()
```

With:

```text
InputRequirement = Pressed
```

release before commit cancels the action.

---

# 13. Example — fixed search

Profile:

```text
BendDown      Automatic
SearchLoop    Controlled
Inspect       Automatic
StandUp       Automatic
```

Execution:

```text
Execute
↓
start AnimatedInteraction
↓
Running

SearchLoop entered
↓
TimedExecution.Start(2.0)

progress 0 → 100 %

Expired
↓
operation.ReachCommit()
↓
TryGrantLoot()
```

Success:

```text
ReleaseRequesterDependency()
↓
operation.Advance()

Inspect
↓
StandUp
↓
Finished
↓
CompleteExecution()
```

The current `2.0` seconds still belongs to gameplay.

It may later depend on:

```text
skills
target properties
buffs
multiplayer contribution
other gameplay state
```

without modifying the choreography.

---

# 14. Example — terminal

Profile:

```text
Enter       Automatic
HackLoop    Controlled
Exit        Automatic
```

Gameplay work may be timed:

```text
HackLoop entered
↓
TimedExecution.Start(ComputeHackDuration())
```

or driven by another domain system:

```text
Terminal.Progress
0 → 100 %
```

Animated Interaction does not care which mechanism is used.

When gameplay reaches completion:

```text
operation.ReachCommit()
↓
CommitReached
↓
commit terminal state
↓
ReleaseRequesterDependency()
↓
operation.Advance()

Exit
↓
Finished
↓
CompleteExecution()
```

If sustained input is required, existing `InputRequirement.Pressed` handles pre-commit cancellation.

---

# 15. Example — carry

Carry should not be migrated immediately.

Its current behavior remains the reference:

```text
Take animation
       ↓
presentation reaches commit point
       ↓
CommitReached
       ↓
TryCommitTake()
       ↓
ReleaseRequesterDependency()
       ↓
animation tail
       ↓
Finished
       ↓
CompleteExecution()
```

Carry is different from Door/Search because its commit boundary is naturally **presentation-driven** rather than work-progress-driven.

The profile may therefore express:

```text
TakeGround
    OneShot
    Automatic
    CommitPoint = 0.45 s
```

During the spike, evaluate whether Animated Interaction can naturally replace the presentation/clock portion of `CarryOperation`.

Carry retains ownership of:

```text
inventory mutation
world spawn/despawn
CarriedItemId
pending take
drop-before-take
```

No carry-specific concern moves into Animated Interaction.

---

# 16. Explicit non-goals

This spike does not provide:

```text
generic cutscene scripting
arbitrary named timeline events
arbitrary callbacks stored in profiles
branching choreography graphs
multiple simultaneous animation channels
root-motion warping
prediction
late-join choreography recovery
generic multiplayer choreography snapshots
rollback
multiple gameplay commits per interaction
```

None of these should be added without a concrete gameplay case.

---

# 17. Spike success criteria

The abstraction is worth keeping if the same small primitives naturally express:

```text
skill-based locked door
fixed search
long sustained terminal
carry presentation
```

without moving domain logic into Animated Interaction and without making Gameplay Action understand animation phases.

The intended boundary is:

```text
Gameplay Action
    owns execution lifecycle

TimedExecution / domain progress
    owns gameplay work progression

Executor / domain
    owns gameplay meaning and commit

Animated Interaction
    owns presentation sequencing

Animation system
    owns actual playback
```

In particular, the spike should validate that:

```text
TimedExecution expiry
!= GameplayAction completion

commit
!= AnimatedInteraction finish

progress 100 %
!= GameplayAction completed
```

If one of the four reference cases requires significant special-case behavior, reconsider the primitives before turning the spike into framework API.
