# Animated carry execution

> **Status: planned.** Project-local vertical slice for authoritative take/drop animation, exact gameplay
> commit timing, and execution interruption. This deliberately stops before client prediction and before
> extracting a generic choreography system.

## Why this step exists

The current carry path is deliberately simple and already has a strong transactional core:

```text
TakeExecutor / DropExecutor
        ↓
ICarrier.TryTake / TryDrop
        ↓
CarryComponent
        ↓
Inventory + CarriedItemId + world object
```

`quest_world/character/CarryComponent.cs` owns the authoritative carry transaction. `TryTake()` adds the
new inventory item, drops an already carried item if necessary, updates `CarriedItemId`, and only then
queues the world object for deletion. `TryDrop()` removes inventory first, restores it if `IWorldSpawner`
rejects the spawn, and clears `CarriedItemId` only after a successful spawn. The existing
`quest_world/tests/CarryComponentTest.cs` already locks down replacement, failed-drop rollback,
successful drop, and drop-on-exit behavior.

That transaction should **not** be stretched across animation frames.

Today `TakeExecutor.cs` and `DropExecutor.cs` call the transaction synchronously and return
`GameplayActionExecutionCompleted`. The character scene separately replicates
`CarryComponent.CarriedItemId` through a `MultiplayerSynchronizer`, and the property setter immediately
removes/applies the carried visual. This is sufficient for logical carry replication but gives no
animated transition or authored commit point.

Gameplay Action already has the lifecycle needed for the next step:

- `GameplayActionExecutionRunning` keeps the reservation;
- `GameplayActionComponent.CompleteExecution`, `CancelExecution`, and `FailExecution` end it later;
- executor terminal callbacks already exist;
- `TimedExecution` demonstrates the intended pattern for a composed authoritative clock rather than a
  second execution engine.

The character presentation also already has the relevant local precedent. `CharacterAnimationController`
uses the UAL `AnimationPlayer` and `AnimationTree`, and landing is layered through the existing
`LandOneShot` node rather than by replacing locomotion ownership.

## Goal

Make take/drop behave as one server-authoritative carry operation:

```text
request accepted
    ↓
carry operation starts
    ↓
semantic animation state replicates from the actor
    ↓
authoritative commit point
    ↓
existing atomic TryTake / TryDrop transaction
    ↓
GameplayAction execution completes
    ↓
remaining animation tail is cosmetic
```

The executor remains the adapter between Gameplay Action and carry. It must not choose UAL clips,
replicate animation state, or mutate inventory/world state itself.

## 1. Carry owns the animated operation

`CarryComponent` should gain a transient operation lifecycle around the existing atomic transaction.
Candidate public shape:

```text
TryStartTake(itemId, worldObject, ...)
TryStartDrop(itemId, ...)
CancelOperation(operationId)

operation state:
    operation id
    kind: Take | Drop
    item id
    phase/progress needed for presentation
```

The exact callback/handle API can stay implementation-driven. The important boundary is:

- `TryStartTake` / `TryStartDrop` start a carry-domain operation;
- `TryTake` / `TryDrop` remain the authoritative atomic commit implementation;
- carry owns the operation clock and semantic presentation state;
- the executor observes operation success/failure and maps that back to the generic execution lifecycle.

Do **not** move inventory addition/removal, world spawning, `QueueFree`, or rollback into animation
callbacks. One commit callback calls the existing transaction once.

### Executor flow

`TakeExecutor.Execute()` should evolve from synchronous completion to roughly:

```text
resolve ICarrier + host object
    ↓
start carry Take operation
    ↓
if start refused -> Failed/Rejected as appropriate
if started       -> GameplayActionExecutionRunning
```

When the carry operation commits successfully, the executor calls
`context.Component.CompleteExecution(context.ExecutionId)`. If the carry transaction fails at the commit
point, it calls `FailExecution`. `OnExecutionCancelled` asks carry to cancel the still-pending operation.
`DropExecutor` follows the same shape.

This is intentionally the same composition style already used by `TimedGameplayActionExecutor` and
`TimedExecution`: the Gameplay Action reservation remains generic; a focused helper/domain object owns
the additional clock and cleans itself up from the three terminal callbacks.

## 2. Commit point is authoritative and completes gameplay

For the first real implementation, the Gameplay Action execution should end **at the gameplay commit
point**, not at the end of the animation:

```text
animation     start ---------------- commit ---------------- tail ---- end
execution     start ---------------- complete
transaction                         TryTake / TryDrop
```

Example for a ground pickup:

```text
0.00 s  StartTake
0.00 s  TakeGround presentation begins
0.55 s  authority calls TryTake
0.55 s  success -> CompleteExecution
1.20 s  local animation tail ends
```

This keeps cancellation semantics trivial:

- before commit, cancellation means no carry mutation happened;
- at commit, the transaction is atomic and the generic execution becomes terminal;
- after commit, later movement/input cannot accidentally "cancel" a pickup that already exists in the
  inventory.

This also avoids a dangerous alternative where presence revalidation cancels a still-running action after
the item has already been added and forces a semantic rollback that the carry transaction was never
designed to represent.

The commit clock must be authoritative gameplay data. An `AnimationPlayer` call-method track or animation
marker may mirror the authored point for tooling, but must not be the source that decides when inventory
or world state mutates. The server/headless path must be able to commit without evaluating visual
animation playback.

## 3. Carry replicates semantic motion, not animation names

The actor-side `CarryComponent` should own replication of the transient carry operation. This is separate
from generic `GameplayActionExecutionPresentation`.

That choice fits the current topology particularly well:

- every player already owns a `CarryComponent` under `quest_world/character/Character.tscn`;
- `CarriedItemId` is already synchronized from that component;
- an observer therefore knows *which character* is taking/dropping simply from the component whose state
  changed, without teaching generic Gameplay Action presentation about character identities.

Replicate semantic carry state such as:

```text
OperationId
Kind = TakeGround | DropGround
ItemId
active/progress information needed to enter the current operation
```

Do **not** replicate a UAL animation path such as `Take_Ground_01`. `CharacterAnimationController` (or a
small carry-presentation adapter beside it) translates semantic `TakeGround` into the animation supported
by that character rig.

This preserves the possibility of a different character implementation using another skeleton,
first-person arms, a robot pickup, or no skeletal animation at all while sharing the same carry gameplay.

### Authoring data

A project-local carry motion profile is enough at this stage. It should live on the actor/carry
presentation side rather than in `CarriableItemDefinition`:

```text
TakeGround duration
TakeGround commit time
DropGround duration
DropGround commit time
semantic motion ids / presentation mapping as needed
```

`CarriableItemDefinition` currently describes inventory/world carry data (`SpawnDefinition`, drop binding,
`ItemVisualScene`). Character animation timing is a property of the carrier rig, not of the battery item.
If real content later needs categories such as `Heavy`, `SmallGround`, or `TwoHanded`, the item may select
a semantic carry class without owning concrete animation clips.

## 4. Separate persistent carry truth from transient presentation

`CarriedItemId` remains authoritative persistent-enough carry truth and keeps using the existing
synchronizer. The animated operation is transient state around a transition.

For level 2 it is acceptable that `OnCarriedItemChanged()` still immediately applies the attached visual:
when the authoritative commit replicates, observers will switch the object into the hand at roughly the
same semantic point at which their replicated operation reaches the pickup moment.

Do not pre-emptively split `CarryComponent` only for layering purity. If level 2.5 prediction requires a
predicted visual override independent of `CarriedItemId`, that is the concrete pressure that justifies a
separate presentation state/component.

## 5. Generic execution interrupts

Animated take immediately exposes a generic runtime need: requester presence/access is not the same thing
as an event that should interrupt an execution.

Current behavior in `GameplayActionRequestPipeline.ValidateSustainedExecutions()` already revalidates
requester access once per process frame for executions whose executor requires requester presence. This is
correct for spatial access, detector validity, requester departure, and similar continuous relationships.
`IGameplayActionAccessProvider` intentionally has one `CanRequest()` query; the removed
`HasSustainedAccess()` hook previously delegated to exactly the same access predicate and should **not** be
reintroduced merely to represent movement/damage.

Movement, jump, damage, knockback, death, weapon switch, etc. are different: they are semantic requester
events.

Add a small generic interruption path rather than a Gameplay Ability System tag subsystem.

Candidate base policy on `GameplayActionExecutor`:

```text
ExecutionInterrupts : Array<StringName>
ShouldCancelOnInterrupt(context, interrupt) -> bool
```

The default query can simply test membership in `ExecutionInterrupts`. Keeping the virtual pure query is
useful for a later choreography executor whose answer may depend on whether gameplay has already
committed.

Candidate runner entry point:

```text
GameplayActionRunner.NotifyInterrupt(interrupt)
```

`GameplayActionRequestPipeline` already owns the small `_requestedExecutions` list containing component,
action ID, execution ID, and requester-presence policy. Interruption should iterate this same list,
resolve the current action/executor, and route matching entries through the existing
`GameplayActionComponent.CancelExecution(executionId, reason)` path.

There is no action scan and no second cancellation lifecycle.

```text
input release --------┐
access lost -----------┤
requester disconnect --┤
movement interrupt ----┤
jump interrupt --------┤
damage interrupt ------┘
                       ↓
                 CancelExecution
                       ↓
              executor callback + ack
```

### Why the policy belongs with execution

`GameplayActionExecutor` already owns `RequiresRequesterPresence`, so interruption policy belongs naturally
beside it. Executors are per-action Node instances in the current topology, so this still allows two
action occurrences to configure different interruption sets without adding policy to reusable
`GameplayActionDefinition` metadata.

Rules remain untouched. `GameplayActionRule` is explicitly an availability pass evaluated before
reservation; re-running every rule while an action is active would make rules such as "not already
carrying" or "target idle" capable of becoming false because the execution itself changed gameplay.

### Character integration

The project character is already the composition root joining `CharacterMovement`,
`GameplayActionRunner`, Interaction, inventory, and `CarryComponent`. It is therefore the correct place to
translate character events into generic interrupt IDs.

Examples:

```text
movement input non-zero -> "movement"
frame.Jumped            -> "jump"
damage system event     -> "damage"
```

Use movement **intent**, not residual velocity, for a player-requested locomotion interrupt. A platform or
small deceleration velocity should not look like the player chose to break the pickup. External movement
such as knockback can emit its own semantic interrupt.

Movement should be reported while intent remains active, not only on a rising edge. Otherwise a player
already holding movement before pressing Take would never produce a new edge after the execution starts.
The requested-execution list is normally empty or tiny, and after the matching execution cancels repeated
movement interrupts become no-ops.

## 6. Network behavior for level 2

Authority remains the only peer that commits gameplay and ends the generic execution.

For this level:

- requester sends the normal action request;
- authority accepts it and `TakeExecutor`/`DropExecutor` returns Running;
- authority `CarryComponent` starts and replicates the semantic carry operation;
- every character copy plays its local presentation when that operation arrives;
- authority reaches commit time and calls the existing atomic transaction;
- `CarriedItemId` / inventory / spawned world state replicate through their existing domain paths;
- executor completes or fails the generic execution.

The owning client is allowed to see one RTT before the take animation starts at this level. Removing that
latency is explicitly the scope of [predicted-carry-execution.md](predicted-carry-execution.md).

## Expected code pressure

Gameplay Action core:

- `addons/gameplay_action_plugin/runtime/actions/GameplayActionExecutor.cs`
  - authored interruption IDs + pure cancellation-policy query;
- `addons/gameplay_action_plugin/runtime/runner/GameplayActionRunner.cs`
  - public interrupt entry point;
- `addons/gameplay_action_plugin/runtime/runner/GameplayActionRequestPipeline.cs`
  - apply interrupt to requester-owned running executions using the existing cancellation path;
- tests around matching/non-matching interrupts, cleanup, and no rule re-evaluation.

QuestWorld carry vertical slice:

- `quest_world/character/CarryComponent.cs`
  - transient operation state, authoritative commit timer, semantic replication/presentation source;
  - preserve `TryTake` / `TryDrop` as atomic transactions;
- `quest_world/core/ICarrier.cs`
  - evolve from only synchronous transactions to the minimum operation-start/cancel surface required by
    the executors;
- `quest_world/interactibles/carriable/TakeExecutor.cs`
- `quest_world/interactibles/carriable/DropExecutor.cs`
  - become thin Running lifecycle adapters;
- `quest_world/character/QuestWorldCharacter.cs`
  - translate character simulation events into generic interrupts and wire carry presentation if needed;
- `quest_world/character/Character.tscn`
  - add carry operation replication/presentation wiring and the required AnimationTree one-shot/blend;
- `addons/dummy_character_plugin/runtime/CharacterAnimationController.cs`
  - consume semantic carry motion without taking ownership of carry gameplay;
- `quest_world/tests/CarryComponentTest.cs`
  - keep all existing transaction tests and add operation timing/cancel behavior.

## Non-goals

- no client prediction yet;
- no generic choreography executor;
- no Gameplay Ability System-style owned tags, blocked tags, granted tags, tag query graph, or global tag
  container;
- no reintroduction of `HasSustainedAccess` / `CanSustain`;
- no gameplay mutation from `AnimationPlayer` callbacks;
- no concrete UAL animation names in carry network state;
- no rollback of a successfully committed pickup because the animation tail was interrupted;
- no requirement that generic Gameplay Action execution presentation identify the actor.

## Success criteria

A multiplayer ground pickup should satisfy all of the following:

1. authority accepts Take and the generic action becomes Running;
2. all peers see the correct character begin the semantic take animation;
3. the battery remains world-owned until the authored authoritative commit point;
4. commit invokes the existing atomic `TryTake()` transaction exactly once;
5. generic execution completes immediately after successful commit;
6. movement/jump interrupts before commit cancel through `CancelExecution`, stop/blend the carry operation,
   and leave inventory/world state untouched;
7. persistent `CarriedItemId` still drives final carried truth and late-join state;
8. synchronous transaction rollback tests continue to pass unchanged in meaning.

Once these invariants work without prediction, the next slice is
[predicted-carry-execution.md](predicted-carry-execution.md).
