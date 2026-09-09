# Interaction Offers and Targeted Owned Actions

## Status

**Planned architectural proposal.**

This proposal comes from exercising the current Gameplay Action + Interaction architecture against a real animated carry flow.

It is intentionally broader than carry because the carry spike exposed an ownership assumption in Interaction itself rather than a carry-specific problem.

The implementation should remain driven by the concrete `Take` use case. The goal is not to reproduce the full Unreal Gameplay Ability System target-data model.

---

# 1. Executive summary

The proposed model can be summarized as:

> **An Interactive offers an invocation. The action may come from the target or from the instigator. Interaction validates target-side rules, Gameplay Action validates the owned action, and targeted interaction executions may additionally reserve the target.**

Or, more mechanically:

```text
Interactive offers something
        ↓
resolve the actual GameplayAction owner
        ↓
validate target / offer rules
        +
validate owned action rules + owner concurrency
        ↓
optionally reserve the interaction target
        +
reserve the action on its real GameplayActionComponent
        ↓
execute the action with the Interactive as invocation target
```

The important separation is:

```text
ACTION OWNERSHIP
    who owns the capability and execution lifecycle?

INTERACTION OFFER
    what does this target currently let this interactor invoke?

TARGET
    what world object is this particular invocation acting on?

ACTION RESERVATION
    what other work may this action owner perform concurrently?

TARGET RESERVATION
    may another actor concurrently interact with this same target?
```

These concepts frequently coincide, but they are not the same thing.

The current Interaction model makes them coincide by design:

```text
target
  == action owner
  == execution owner
  == interaction offer source
  == target concurrency owner
```

That remains a valid and useful configuration.

It must no longer be the only one.

---

# 2. Why this proposal exists

## 2.1 The carry spike

The first implementation of battery pickup naturally put `Take` on the battery:

```text
Battery
├── GameplayActionComponent
│   └── Take
│       └── TakeExecutor
└── InteractiveComponent
```

This matched the existing Interaction model perfectly.

The interaction focused the Battery, created a binding for `Battery.Take`, the battery's `GameplayActionComponent` owned the execution, and its host-local concurrency naturally prevented two players from executing the same interaction simultaneously.

That looked correct until pickup gained a real animation lifecycle:

```text
0 ms                     700 ms                    1400 ms
|--------------------------|--------------------------|
pickup animation =====================================
Take execution =======================================
                           ^
                           gameplay commit
                           Inventory.AddItem
                           Battery.QueueFree()
```

At commit, `TryTake()` destroys the battery.

That also destroys:

```text
Battery.GameplayActionComponent
```

which owns the still-running execution.

The execution therefore cannot naturally survive through the animation recovery phase.

This is not fundamentally an async bug.

It is an ownership problem.

The spike reveals that the meaningful operation is actually:

```text
Player.Take(Battery)
```

not:

```text
Battery.Take(Player)
```

The player:

- owns the pickup choreography;
- plays the pickup animation;
- is movement/input constrained during the operation;
- owns the inventory receiving the item;
- owns the carried state after commit;
- may need to remain in recovery after the battery disappears;
- should eventually expose/replicate the visible execution from the player actor.

The battery is the target and consumable world object.

It should not need to own one copy of the generic `Take` capability.

With ten different carriable object types, this should still be one player action:

```text
Player
└── Take

Take(Battery)
Take(Fuse)
Take(Keycard)
Take(Crate)
...
```

The target provides data and target-side conditions; it does not define a new occurrence of the player's ability to take something.

---

## 2.2 The Door + Force counterexample

Moving every interaction action to the player would solve `Take`, but immediately makes another common case worse.

Consider a door exposing:

```text
Open
Close
Force
```

`Open` and `Close` naturally belong to the door.

The door is the thing capable of opening and closing. Those operations may also be triggered without a player:

```text
quest -> Door.Open()
button -> Door.Open()
AI -> Door.Open()
script -> Door.Close()
```

Their rules, state transitions and host-local concurrency belong naturally to the door.

`Force`, however, can reasonably be interpreted as:

```text
Player.Force(Door)
```

The player owns:

- the physical attempt;
- its animation;
- movement restrictions;
- equipment/strength/stamina requirements;
- interruption;
- execution lifecycle and recovery.

The door owns:

- whether it is forceable;
- whether it is locked;
- resistance/difficulty;
- its resulting broken/open state.

The correct interaction UI may therefore legitimately be:

```text
Door

[E] Open
[F] Force
```

while the executions resolve to two different owners:

```text
Open
  -> Door.GameplayActions.Open

Force
  -> Player.GameplayActions.Force
     Target = Door
```

This is not an exceptional edge case.

It demonstrates that an Interactive's offered actions are a **presentation/request set**, not an ownership set.

---

# 3. Core architectural insight

An interaction offer and a gameplay action occurrence are different concepts.

Today Interaction effectively says:

```text
Interactive.Actions
    =
InteractionAction occurrences owned by Interactive.ActionComponent
```

The proposal changes that to:

```text
Interactive.Offers
    =
ordered descriptions of actions this target allows an interactor to invoke
```

An offer resolves to an existing gameplay action occurrence.

It does not own that occurrence.

It does not grant it.

It does not clone it.

It does not move it.

Conceptually:

```text
                     InteractiveComponent
                            │
                         Offers
                  ┌─────────┴─────────┐
                  │                   │
            target-owned        instigator-owned
                  │                   │
                  ▼                   ▼
          Door.GameplayActions  Player.GameplayActions
                  │                   │
                Open                Force
```

Interaction remains responsible for discovery, target-specific availability, input projection and spatial request validation.

Gameplay Action remains responsible for action ownership, action rules, action-local concurrency, authoritative execution and terminal lifecycle.

---

# 4. Goals

This proposal should make the following scenarios natural.

### One reusable player-owned targeted action

A player owns one `Take` action and can invoke it against any compatible Interactive target.

```text
Player.Take(BatteryA)
Player.Take(BatteryB)
Player.Take(Fuse)
```

The execution remains alive if the target disappears at commit.

### Existing target-owned interactions remain first-class

A Door may continue owning `Open` and `Close`.

No target-data indirection should be required simply because Interaction learned how to target actor-owned actions.

### Mixed ownership on one Interactive

The same Door may expose:

```text
Door.Open
Door.Close
Player.Force(Door)
```

in one ordered interaction UI.

### Target-side and owner-side validation remain separate

`Battery can currently be taken` and `Player can currently perform Take` are two distinct questions and both must pass.

### Two distinct concurrency scopes

An actor-owned targeted action must be able to reserve:

```text
Player full-body interaction channel
```

while also preventing another actor from using the same target:

```text
Battery interaction target
```

### Server authority remains explicit

A client-supplied target is never sufficient proof that the invocation is legal.

The authority must resolve the target, verify that it actually offers the requested action to this interactor, re-run target rules, re-run action rules and acquire authoritative reservations.

### No dynamic grant churn

Focusing a battery must not grant `Take`, and unfocusing it must not ungrant `Take`.

The player simply owns `Take`.

Interaction temporarily offers a binding to invoke it against the focused target.

---

# 5. Non-goals

This proposal does not attempt to:

- make every Gameplay Action targeted;
- move every interaction action onto the player;
- implement arbitrary GAS-style target-data structures;
- support target locations, hit results, AoE target sets or arbitrary serialized payloads yet;
- replicate executor instances;
- solve generic choreography authoring;
- solve remote execution observation;
- introduce cross-world/global action concurrency;
- replace persistent target state with transient reservations;
- make Interaction a second execution/network engine;
- automatically infer action ownership;
- implement dynamic action grants for focus;
- make programmatic Gameplay Action execution automatically obey Interaction offer rules.

The first implementation only needs a concrete target reference sufficient for real `Take` and `Force`-style interactions.

---

# 6. Terminology

## Gameplay Action owner

The `GameplayActionComponent` that owns the actual action occurrence.

It owns:

```text
action occurrence
action rules
action-local concurrency
execution reservation
execution lifecycle
execution presentation
```

This remains unchanged.

## Instigator

The gameplay actor attributed to the action execution.

For normal player interaction this is the player actor.

## Interactive target

The world target discovered and focused through Interaction.

It provides target-specific interaction context.

## Interaction Offer

An authored statement on an `InteractiveComponent` saying:

> this target currently offers this action endpoint to an interactor, using this input/presentation configuration and these target-specific rules.

The offer does not imply that the target owns the action.

## Targeted action

An action whose execution receives a target distinct from its owner.

Example:

```text
owner = Player
action = Take
target = Battery
```

## Target reservation

A transient authoritative claim on an Interactive target or target concurrency group.

It answers:

> may another interaction request begin conflicting work against this target?

This is separate from the action owner's normal `GameplayActionComponent` reservation.

---

# 7. Ownership rule of thumb

Action ownership should follow the entity that intrinsically owns the capability and execution lifecycle, not simply the entity currently focused by Interaction.

A useful heuristic is:

```text
Intrinsic actor capability applied to different things
    -> actor-owned targeted action

Intrinsic world-object capability
    -> target-owned action
```

Examples:

```text
Take                Player-owned
Drop                Player-owned
Revive              usually player-owned
Attack              player/actor-owned
Force               usually player-owned

Open                Door-owned
Close               Door-owned
Unlock              Door-owned
Activate             Machine-owned
CallElevator         potentially elevator-owned
```

Some actions will remain game-design decisions.

For example, `Search` could legitimately be:

```text
Player.Search(Container)
```

if the operation is primarily an actor choreography,

or:

```text
Container.Search()
```

if the container owns an involved world-side process.

The framework should support both without forcing a universal answer.

---

# 8. InteractionOffer

The proposal introduces an explicit authored offer concept.

The exact API names may change during implementation, but conceptually an offer owns interaction-specific authoring such as:

```text
InteractionOffer
├── ActionSource
├── ActionId
├── BindingConfig
├── Rules
├── TargetConcurrencyGroup
├── WhenReservedBySelf
└── WhenReservedByOther
```

## ActionSource

The first version should support two explicit source modes.

### Target

Resolve the action from the target's configured Gameplay Action host.

Example:

```text
Door offer:
    Source = Target
    ActionId = open
```

resolves to:

```text
Door.GameplayActions.Open
```

### Instigator

Resolve the action from the requesting runner's owned Gameplay Action host.

Example:

```text
Battery offer:
    Source = Instigator
    ActionId = take
```

resolves to:

```text
Player.GameplayActions.Take
```

Architecturally an offer resolves an action endpoint:

```text
(GameplayActionComponent, ActionId)
```

so other source strategies could be added later if a concrete game case requires them.

For example, a button might someday offer an action owned by a separate elevator controller.

That extension should not be implemented until needed.

---

# 9. Binding ownership moves to the offer

Interaction-specific input configuration belongs naturally to the offer.

A player-owned `Take` action must not have to say:

```text
Press E whenever Take exists
```

because `Take` exists permanently on the player while the valid target changes continuously.

Instead:

```text
Battery Take offer
└── BindingConfig = interact / press
```

creates a temporary binding:

```text
E
 -> Player.Take
 -> target Battery
```

Focus loss removes the binding.

It does not remove the action.

This preserves the existing principle:

> bindings are local derived input state, not action ownership.

For target-owned interactions, the same offer model applies:

```text
Door Open offer
└── BindingConfig = interact / press
```

resolves:

```text
E
 -> Door.Open
 -> target Door
```

This means Interaction-specific binding configuration should ultimately live on `InteractionOffer`, not on an `InteractionAction` subclass.

---

# 10. Target data in Gameplay Action

Gameplay Action needs a minimal representation of the target of one invocation.

The first implementation should remain deliberately narrow.

Conceptually:

```text
GameplayActionContext
├── ExecutionId
├── Instigator
├── Requester
├── Component
├── Action
├── Host
├── World
└── Target          # new, optional
```

The same target must be available while:

```text
evaluating action rules
executing
running terminal callbacks
predicting initial execution presentation where relevant
```

The target belongs to the **invocation**, not the `GameplayAction` occurrence.

Therefore:

```text
Player.Take
```

may successively execute as:

```text
Take(Target = BatteryA)
Take(Target = FuseB)
Take(Target = CrateC)
```

without mutating the action occurrence itself.

The generic API should expose a typed convenience similar to the existing host/world helpers:

```text
GetTarget<T>()
```

The concrete serialized representation required by the network should stay small.

For Interaction V1 of this feature, a target Node/network path is sufficient.

Arbitrary GAS-style target payloads remain deferred.

---

# 11. Interaction rules and action rules become intentionally separate

The current architecture injects target rules into the action's generic `Rules` pass.

That works because every interaction action currently belongs to exactly one Interactive.

It no longer works for:

```text
Player.Take
```

because the same action occurrence may target:

```text
BatteryA
BatteryB
Fuse
Crate
```

Each target may have different rules.

Mutating `Player.Take.Rules` whenever focus changes would be incorrect and unsafe.

The new model therefore has two explicit rule domains.

## Target / offer rules

Interaction evaluates:

```text
Interactive.TargetRules
+
InteractionOffer.Rules
```

These answer target-oriented questions such as:

```text
is this battery carriable?
is this door currently forceable?
does this target allow this interaction?
is the player spatially eligible?
is this offer currently hidden?
```

Their context contains at least:

```text
Interactor
Interactive
Offer
resolved GameplayAction
```

They remain synchronous and side-effect free.

Target-local NodePath resolution should be relative to the Interactive target, not the action owner.

This is particularly important for actor-owned actions: a rule about `Door.StatefulComponent` must not suddenly resolve relative to `Player.Take`.

## Owned action rules

The resolved `GameplayActionComponent` independently evaluates the real action.

Examples for `Player.Take`:

```text
player alive
hands available
inventory accepts item
not currently stunned
full-body action group available
```

Examples for `Player.Force`:

```text
player has required capability
stamina available
not performing another full-body action
```

Examples for `Door.Open`:

```text
door state permits Open
door action host concurrency permits it
```

The invocation target is available through `GameplayActionContext.Target`, but these remain rules of the owned action.

The evaluation model is therefore intentionally:

```text
target validity
    AND
owned action validity
```

not one synthetic merged rule array.

---

# 12. Availability pipeline

For an Interaction offer, local presentation and authoritative validation conceptually evaluate:

```text
offer configuration
        ↓
resolve action endpoint
        ↓
spatial / interaction access
        ↓
shared target rules
        ↓
offer-specific rules
        ↓
target reservation availability
        ↓
owned GameplayAction rules
        ↓
owned GameplayAction concurrency
        ↓
Allowed / Blocked / Hidden
```

The exact internal ordering between pure checks may be optimized, but two invariants matter:

1. both target-side and owner-side validation must run;
2. the server repeats all authoritative checks before starting anything.

A locally `Allowed` offer is only prediction/presentation.

It is never proof to the authority.

---

# 13. Request access is no longer synonymous with external ownership

The current Gameplay Action runner treats actions on its own `OwnedActionComponent` as automatically accessible, while externally-owned actions resolve an `IGameplayActionAccessProvider`.

That ownership shortcut is no longer sufficient.

`Player.Take(Target = Battery)` is owned by the player but still requires Interaction to validate:

```text
Battery is a valid Interactive
Battery actually offers Take
Battery is in range / visible / otherwise detected
Battery target rules allow Take
Battery is not claimed incompatibly
```

Therefore `AccessProviderId` should mean:

> which domain validates player request access to this action?

not:

> is this action externally owned?

An owned action may require an access provider.

For example:

```text
Player.Take.AccessProviderId = "interaction"
Player.Force.AccessProviderId = "interaction"
```

Programmatic `GameplayActionComponent.ExecuteAction()` may continue to bypass requester access exactly as it does today.

The important runner change is conceptual:

```text
owned + no provider
    -> locally accessible

owned + provider
    -> provider validates request

external + provider
    -> provider validates request

external + no provider
    -> not requestable through runner
```

This preserves server authority without moving access policy into bindings where a malicious client could simply omit it.

---

# 14. Interaction request validation

An Interaction request targeting an actor-owned action must not permit a client to forge:

```text
Player.Take(Target = object on the other side of the map)
```

or:

```text
Player.Force(Target = object that never offered Force)
```

The authoritative Interaction access provider must verify all of the following:

```text
requested target resolves
target has a live InteractiveComponent
offer resolves on that target
offer resolves to the exact requested action endpoint
target is spatially accessible to this runner
target/offer rules pass
target reservation permits the request
```

For an instigator-owned offer:

```text
offer says:
    Source = Instigator
    ActionId = take

authority resolves:
    sender's runner
      -> OwnedActionComponent
      -> take
```

For a target-owned offer:

```text
offer says:
    Source = Target
    ActionId = open

authority resolves:
    Interactive.ActionComponent
      -> open
```

The client may transport enough identity to identify the target and requested endpoint, but the server reconstructs and compares the expected endpoint from its own authoritative offer configuration.

Client binding data remains evidence of intent, never proof of permission.

---

# 15. Target reservation

Correct action ownership creates a second concurrency problem.

Consider:

```text
Player1.Take(Battery)
Player2.Take(Battery)
```

The actual executions now belong to:

```text
Player1.GameplayActionComponent
Player2.GameplayActionComponent
```

Their owner-local concurrency systems are independent.

Both can legally reserve their own:

```text
full_body_interaction
```

group.

Without another mechanism, both pickup animations can begin against the same battery.

The final `TryTake()` transaction can still make inventory mutation authoritative and allow only one success, but Player2 may spend the entire pickup windup interacting with an object already claimed by Player1.

That is correct state but poor interaction semantics.

The target therefore needs an optional **Interaction reservation**.

---

# 16. Two orthogonal reservation scopes

The important model is:

```text
Player1.Take(Battery)
```

acquires:

```text
PLAYER / ACTION OWNER
    full_body_interaction
        ↓
    prevents Player1 starting incompatible actor work

BATTERY / TARGET
    take or interaction target group
        ↓
    prevents Player2 starting incompatible work on Battery
```

These reservations answer different questions.

## Action reservation

Owned by the action's `GameplayActionComponent`.

Existing system.

Examples:

```text
Player:
    full_body_interaction

Door:
    door_operation
```

## Target reservation

Owned by Interaction on the Interactive target.

New system.

Examples:

```text
Battery:
    interaction

DownedPlayer:
    revive

Door:
    door_operation
```

The target claim does not move the action execution to the target.

It merely protects interaction access to that target.

---

# 17. TargetConcurrencyGroup

An offer may declare an optional target-side concurrency group.

Conceptually:

```text
InteractionOffer.TargetConcurrencyGroup
```

Empty means:

```text
this offer does not claim the target
```

A shared name makes offers mutually exclusive.

Example:

```text
Door
├── Open offer
│   TargetConcurrencyGroup = door_operation
├── Close offer
│   TargetConcurrencyGroup = door_operation
└── Force offer
    TargetConcurrencyGroup = door_operation
```

even though the actual action owners differ:

```text
Open  -> Door
Close -> Door
Force -> Player
```

This is the crucial property.

The target reservation domain belongs to the **Interactive offers**, so it can coordinate actions that do not share an action owner.

---

# 18. Door + Force with both reservation systems

The complete model becomes:

```text
Player1
└── GameplayActionComponent
    └── Force
        HostConcurrencyGroup = full_body_interaction

Door
├── GameplayActionComponent
│   ├── Open
│   │   HostConcurrencyGroup = door_operation
│   └── Close
│       HostConcurrencyGroup = door_operation
│
└── InteractiveComponent
    └── Offers
        ├── Open
        │   Source = Target
        │   TargetConcurrencyGroup = door_operation
        │
        ├── Close
        │   Source = Target
        │   TargetConcurrencyGroup = door_operation
        │
        └── Force
            Source = Instigator
            TargetConcurrencyGroup = door_operation
```

Starting `Player1.Force(Door)` reserves:

```text
Player1.full_body_interaction
+
Door.interaction:door_operation
```

Player2 therefore cannot begin another `Force`.

A normal interaction request for `Door.Open` also sees the same target claim and is refused.

Likewise, if `Door.Open` began first through Interaction, its offer claims the target and prevents `Player1.Force(Door)`.

This makes mixed ownership coherent.

---

# 19. Target reservations do not replace action concurrency

A target reservation is not a universal world lock.

`Door.GameplayActionComponent` should still keep its own `door_operation` concurrency if the Door needs that invariant for non-Interaction execution.

For example:

```text
quest -> Door.Open()
button -> Door.Close()
```

do not necessarily pass through Interaction.

Their world-side correctness still belongs to Door's action/state model.

Interaction target reservation primarily answers:

> which player-facing interaction requests may concurrently act on this target?

The overlap between target-owned action concurrency and Interaction target reservation is intentional.

They protect different entry scopes.

Persistent gameplay correctness must never depend solely on a transient Interaction claim.

---

# 20. Target reservation acquisition

On authority, starting a targeted interaction should conceptually perform:

```text
validate target
validate offer
resolve action
validate target rules
validate action rules
check target claim
check owner action reservation
        ↓
acquire target claim
        ↓
start GameplayAction execution
```

If the action fails to start after the target claim has been acquired:

```text
Rejected
Failed synchronously
owner reservation unavailable
unexpected setup failure
```

the target claim is immediately released.

A synchronous `Completed` action releases it immediately after completion.

A `Running` execution associates the claim with that execution.

The target claim is released when the execution reaches a terminal state:

```text
Completed
Cancelled
Failed
```

or when the target itself is destroyed.

The authoritative acquisition must happen before another request can pass target availability.

Godot's single-threaded gameplay path makes this straightforward, but the code must still treat:

```text
claim acquired
+
execution failed to start
```

as a rollback case.

---

# 21. Target reservation relation and presentation

The existing Interaction model distinguishes:

```text
busy by self
busy by other
```

for target-owned executions.

That semantic remains useful and should move to the offer/target reservation layer.

Conceptually an offer may select:

```text
WhenReservedBySelf  = Hidden | Blocked
WhenReservedByOther = Hidden | Blocked
```

Examples remain:

```text
Dialogue-like:
    self  Hidden
    other Blocked

Silent contextual:
    self  Hidden
    other Hidden

Visible busy interaction:
    self  Blocked
    other Blocked
```

For a targeted actor-owned action, relation comes from the target claim's instigator/requester identity rather than from an execution owned by the target.

This is another reason these presentation policies belong on the Interaction Offer rather than on the owned Gameplay Action.

---

# 22. Target reservation replication

Target reservations influence target availability on remote peers.

They therefore need enough transient replication for clients to answer:

```text
is this target currently claimed?
is it claimed by me or someone else?
which target concurrency group is claimed?
```

This state is not persistent gameplay truth.

It should be treated like transient Interaction/execution presentation.

A late joiner should be able to receive the current claim snapshot rather than waiting for the next claim event.

The implementation may initially synchronize a small target-side claim snapshot directly from `InteractiveComponent`.

A future richer replicated-execution model may allow some of this presentation to be derived from observable executions, but this proposal must not depend on that future work.

Persistent states such as:

```text
door broken
battery consumed
container searched
```

remain domain state, not Interaction claim state.

---

# 23. Execution lifetime remains owned by the real action owner

The most important payoff for carry is that target destruction no longer constrains execution lifetime.

With player-owned `Take`:

```text
Player
└── GameplayActionComponent
    └── Take execution
```

the target may disappear during the execution:

```text
0 ms                     700 ms                    1400 ms
|--------------------------|--------------------------|

Player.Take execution =================================

pickup animation ======================================

Battery ===================X
                           commit / QueueFree
```

The execution owner still exists.

The executor may therefore:

```text
start animation
wait windup
commit TryTake(Battery)
Battery disappears
continue recovery
complete execution
```

without inventing a carry-specific execution lifetime solely to survive the target.

This proposal does **not** require all actions to keep running after commit.

It merely makes that lifecycle possible when the real action owner survives.

---

# 24. Take end-to-end example

## Authoring

Player:

```text
Player
└── GameplayActionComponent
    └── Take
        ├── Rules
        ├── HostConcurrencyGroup = full_body_interaction
        ├── AccessProviderId = interaction
        └── TakeExecutor
```

Battery:

```text
Battery
├── Carriable data/components
└── InteractiveComponent
    ├── TargetRules
    └── Offers
        └── Take
            ├── Source = Instigator
            ├── ActionId = take
            ├── Binding = interact
            ├── OfferRules = carriable / etc.
            └── TargetConcurrencyGroup = take
```

The battery does not own a `TakeExecutor`.

It does not own a `Take` Gameplay Action.

## Local focus

```text
detector focuses Battery
        ↓
Battery offers Instigator.take
        ↓
resolve Player.GameplayActions.Take
        ↓
evaluate Battery target/offer rules
        ↓
evaluate Player.Take rules
        ↓
evaluate Battery target claim
        ↓
create binding
```

The binding conceptually represents:

```text
Input = interact
Action = Player.Take
Target = Battery
Source = Battery Interactive
```

## Authority request

The authority:

```text
resolves Battery
verifies Battery offers instigator.take
revalidates spatial access
revalidates Battery rules
revalidates Player.Take rules
checks Battery target claim
checks Player full_body_interaction group
claims Battery
starts Player.Take(Target = Battery)
```

## Execution

```text
Take starts
    ↓
play pickup choreography
    ↓
commit point
    ↓
carrier.TryTake(Battery)
    ↓
inventory/state mutation succeeds
Battery QueueFree
    ↓
target reservation disappears with target
    ↓
Player.Take continues recovery
    ↓
CompleteExecution()
```

The action's lifecycle no longer depends on the lifetime of the item being consumed.

---

# 25. Door Open + Force end-to-end example

Door authoring:

```text
Door
├── GameplayActions
│   ├── Open
│   └── Close
│
└── Interactive
    └── Offers
        ├── Open
        │   Source = Target
        │   ActionId = open
        │
        ├── Close
        │   Source = Target
        │   ActionId = close
        │
        └── Force
            Source = Instigator
            ActionId = force
```

Player:

```text
Player.GameplayActions
└── Force
```

When the Door is focused, Interaction can present:

```text
[E] Open
[F] Force
```

The first binding resolves:

```text
Door.Open(Target = Door)
```

The second resolves:

```text
Player.Force(Target = Door)
```

Door target rules can independently decide:

```text
Open allowed only while unlocked
Force offered only while locked and forceable
```

Player Force rules can independently decide:

```text
player has enough stamina
player has required equipment
player is not performing another full-body action
```

Both offers may share:

```text
TargetConcurrencyGroup = door_operation
```

so Open and Force remain mutually exclusive despite having different execution owners.

---

# 26. Revive as a second validation case

`Revive` provides another strong example of the same architecture.

```text
PlayerA.Revive(PlayerB)
```

PlayerA naturally owns:

```text
revive animation
movement lock
input cancellation
duration
equipment/consumables
execution lifecycle
```

PlayerB naturally owns target-side facts such as:

```text
is downed
is still alive
is already being revived
```

Two different rescuers should not both independently start Revive on PlayerB.

Therefore:

```text
PlayerA action reservation
    full_body_interaction

PlayerB target reservation
    revive
```

is the same structure discovered by `Take`.

If both Take and Revive need this model, target reservation is clearly a framework concept rather than a carry workaround.

---

# 27. Interaction presentation becomes offer-oriented

Today target presentation effectively walks target-owned actions.

The new presenter walks offers.

For each offer it resolves:

```text
offer
    ↓
real GameplayAction
    ↓
GameplayActionDefinition
    ↓
label / description / generic action metadata
```

while taking interaction-specific data from the offer:

```text
binding config
target availability
target busy relation
offer ordering
```

Execution presentation must be looked up on the **resolved action owner**, not assumed to live on the Interactive's target ActionComponent.

This matters for:

```text
Battery -> Player.Take
```

because the execution presentation belongs to:

```text
Player.GameplayActionComponent
```

The Interaction presenter still renders it inside the focused Battery UI because the binding/offer associates that execution with Battery.

Thus:

```text
presentation container ownership
!=
execution ownership
```

which is the same separation as the runtime architecture.

---

# 28. InteractionAction becomes optional or obsolete

The current `InteractionAction` subtype exists largely because one target-owned action currently needs to carry several Interaction-specific responsibilities:

```text
Interaction AccessProviderId
WhenExecutingBySelf
WhenExecutingByOther
Interactive reference
TargetRules adapter injection
Default interaction binding config
```

The offer model relocates those concerns:

```text
Access validation
    -> request/action access + Interaction target context

busy target presentation
    -> InteractionOffer / target claim

Interactive reference
    -> invocation target

TargetRules
    -> explicit Interaction-side rule pass

interaction binding
    -> InteractionOffer
```

This makes `InteractionAction` much less necessary.

The desired final architecture should allow a target-owned interaction offer to reference an ordinary `GameplayAction` occurrence.

For example:

```text
Door.Open : GameplayAction
```

can still be exposed through:

```text
InteractionOffer(Source=Target, ActionId=open)
```

without requiring the action itself to know that Interaction exists.

`InteractionAction` may temporarily remain as migration sugar while scenes are converted, but it should not remain a second permanent conceptual model unless implementation pressure reveals a responsibility that still genuinely belongs on the action subtype.

---

# 29. InteractionRule also changes responsibility

Current Interaction rules participate in the generic Gameplay Action rule pass because the action and target are currently the same ownership domain.

That should no longer be required.

Interaction rules are target/offer rules.

They should evaluate an Interaction-specific context directly and should not need to mutate or decorate a player-owned action's `Rules` collection.

This eliminates a particularly dangerous possibility:

```text
BatteryA focused
 -> inject BatteryA rules into Player.Take

BatteryB focused
 -> replace/inject BatteryB rules into same Player.Take
```

The player action remains stable.

Target state remains target-local.

The two are composed only during one invocation evaluation.

---

# 30. InteractionActionExecutor

The existing Interaction executor convenience layer may remain useful, but its target resolution must stop depending on the action itself being target-owned.

A future Interaction execution context can conceptually be reconstructed from:

```text
GameplayActionContext.Instigator
+
GameplayActionContext.Target
```

rather than:

```text
InteractionAction.Interactive
```

This allows an Interaction-specific executor to back either:

```text
Door.Open
```

or:

```text
Player.Force(Door)
```

depending on which entity owns the actual action occurrence.

Generic executors remain free to consume `GameplayActionContext.Target` directly without depending on Interaction.

---

# 31. GameplayActionBinding

A binding continues to reference the real action endpoint:

```text
GameplayActionComponent
ActionId
```

but targeted invocation requires it to additionally carry local invocation target/context.

Conceptually:

```text
GameplayActionBinding
├── Component
├── ActionId
├── Source
├── Target          # invocation target
├── input config
└── presentation context
```

For Interaction, `Source` continues to own cleanup:

```text
focused Interactive lost
    ↓
remove bindings created from that target
```

`Target` answers a different question:

```text
what does this invocation act on?
```

No ownership is transferred.

---

# 32. GameplayActionContext and programmatic execution

The generic context should expose the target to rules/executors/terminal callbacks.

Programmatic execution may also optionally provide a target:

```text
PlayerActions.ExecuteAction("take", target: battery)
```

Such programmatic execution still bypasses Interaction access, exactly as generic programmatic execution currently bypasses requester access.

That means it does **not** automatically evaluate:

```text
Interactive.TargetRules
InteractionOffer.Rules
Interaction target reservation
```

This is intentional.

Gameplay Action and Interaction remain separate layers.

If a future game system needs to programmatically execute an Interaction Offer with full target-side semantics, that should be an explicit Interaction API rather than silently changing `GameplayActionComponent.ExecuteAction()`.

---

# 33. Network transport

The current requester protocol identifies:

```text
GameplayActionComponent
ActionId
```

Targeted execution additionally needs a network-resolvable target reference.

Conceptually:

```text
ComponentPath
ActionId
TargetPath?
```

The client binding chooses the invocation.

The server:

```text
validates sender
resolves authoritative Component
resolves authoritative Action
resolves authoritative Target
runs configured access provider
```

For Interaction access, the provider verifies that the target actually offers that endpoint.

The network does not need to replicate dynamic grants.

It transports one invocation.

The target reference is data attached to this request/execution and remains in the authoritative `GameplayActionContext` for the execution lifetime.

More complex target data remains deferred until a real use case requires it.

---

# 34. Execution prediction

Actor-owned targeted actions are compatible with the current prediction model.

For example, `Player.Take` may expose an initial predicted linear progress sample.

The focused Interaction binding refers to that player's real action occurrence, so the existing requester-side predicted execution presentation can be created against the correct owner before authority acknowledgement.

The target itself is not predicted into durable gameplay state.

At most, Interaction may locally project a pending target claim for UX.

Authoritative target reservation and final target state remain server-owned.

No additional prediction framework is required by this proposal.

---

# 35. Relationship to replicated execution observability

This proposal and replicated execution observability solve different problems.

This proposal answers:

```text
who should own Take?
what is its target?
who validates the target?
how do two players avoid racing the same target?
```

Replicated execution observability answers:

```text
how does another peer observe Player.Take(Battery)?
how does it play/reconcile the remote choreography?
how does a late joiner enter an execution already in progress?
```

Moving `Take` onto the player is compatible with that future direction.

In fact it improves it:

```text
Player replicated execution
    Take(Target = Battery)
```

is a natural actor-visible execution.

This proposal must not wait for replicated execution observability, however.

Carry animation may continue using temporary domain-specific presentation replication until the generic execution proposal is implemented.

---

# 36. Relationship to persistent state

Target reservation is transient interaction state.

It must never replace domain transactions.

For pickup:

```text
Battery target claim
```

does not prove that the inventory mutation succeeded.

`TryTake()` remains the authoritative atomic gameplay transaction.

For a door:

```text
door_operation target claim
```

does not mean the Door is open.

`StatefulComponent` or equivalent domain state remains truth.

The model remains:

```text
claim
    -> prevents conflicting interaction starts

execution
    -> performs gameplay

persistent/domain state
    -> records the resulting truth
```

---

# 37. Failure and cancellation semantics

Target claims must not leak.

Every accepted claim must have exactly one cleanup path.

Conceptually:

```text
claim acquired
    ↓
execution rejected before start
    -> release

execution completes synchronously
    -> release

execution running
    -> retain

running execution completes
    -> release

running execution cancels
    -> release

running execution fails
    -> release

target freed
    -> target-side claim disappears

action owner freed
    -> execution invalidated / claim cleanup
```

The claim is a reservation resource, not a gameplay transaction.

It does not roll back gameplay already committed.

If a target disappears because the authoritative execution intentionally consumed it, that is a normal claim termination.

---

# 38. Sustained access

This proposal does not redefine the existing distinction between:

```text
request access
```

and:

```text
accepted execution lifetime
```

An action may still require requester/interactor presence while running.

However targeted actor-owned actions make it clearer that this is an execution policy rather than ownership.

For `Take`, an implementation may reasonably decide that once accepted and the actor has entered a locked choreography, continuous spatial access is no longer required; the executor performs a final authoritative target validation at commit.

Other actions may require target/access validity throughout their duration.

Phase-sensitive interruption policies belong to the later animated interaction/choreography work rather than this ownership proposal.

---

# 39. Current model preserved as a special case

A normal existing target-owned interaction becomes:

```text
Interactive
└── Offer
    Source = Target
    ActionId = existing action
```

The actual execution topology remains:

```text
Target.GameplayActionComponent
└── action
    └── execution
```

Therefore this proposal does not invalidate the existing architecture.

It generalizes:

```text
Interactive == action owner
```

from a requirement into one valid configuration.

---

# 40. Alternatives considered

## Keep every interaction target-owned

Rejected.

This makes `Take` ownership wrong, duplicates generic actor capabilities across every target and prevents execution from outliving targets that are intentionally destroyed at commit.

## Move every interaction action to the player

Rejected.

This makes intrinsic world capabilities such as `Door.Open` awkward, weakens world-object ownership and turns programmatic world actions into artificial player-targeted abilities.

## Dynamically grant target actions to the player

Example:

```text
focus Battery
 -> grant TakeBattery

unfocus Battery
 -> remove TakeBattery
```

Rejected.

It creates unnecessary lifecycle churn, duplicated action occurrences, grant replication questions and target-specific player state for something that is naturally one stable player capability plus invocation data.

## Keep target-owned proxy actions

Example:

```text
Battery.TakeProxy
    -> triggers Player.Take(Battery)
```

Rejected as the main architecture.

It preserves target concurrency but reintroduces one action occurrence per target and adds a second execution hop solely to compensate for incorrect ownership.

## Make GameplayAction executions autonomous from their owner

That could allow:

```text
Battery.Take execution
```

to survive destruction of `Battery.GameplayActionComponent`.

Deferred.

It is a much larger change to occurrence lifetime, executor lifetime, reservations and replication, and does not fix the deeper semantic fact that `Take` belongs to the player.

## Full arbitrary GAS-style target data immediately

Deferred.

A Node target solves the concrete architectural pressure.

Locations, hit results, multiple targets and arbitrary payloads should be added only from real gameplay cases.

---

# 41. Authoring examples

## Battery

```text
Battery
├── CarriableComponent
├── InteractiveComponent
│   └── Offers
│       └── Take
│           Source = Instigator
│           ActionId = take
│           TargetConcurrencyGroup = take
└── visuals / collision
```

No Battery Gameplay Action host is required solely for pickup.

Player:

```text
Player
└── GameplayActionComponent
    ├── Take
    └── Drop
```

## Door

```text
Door
├── GameplayActionComponent
│   ├── Open
│   └── Close
│
└── InteractiveComponent
    └── Offers
        ├── Open -> Target.open
        ├── Close -> Target.close
        └── Force -> Instigator.force
```

## Simple Button

```text
Button
├── GameplayActionComponent
│   └── Activate
└── InteractiveComponent
    └── Offer
        -> Target.activate
```

Semantically almost identical to today.

## Revivable player

```text
DownedPlayer
└── InteractiveComponent
    └── Revive offer
        Source = Instigator
        ActionId = revive
        TargetConcurrencyGroup = revive
```

Every rescuer owns its own `Revive` action.

---

# 42. Impact on current components

## GameplayActionComponent

Remains the owner of action occurrences, execution reservations and terminal lifecycle.

Needs targeted evaluation/execution context.

Its host-local concurrency semantics remain unchanged.

## GameplayActionContext

Gains optional invocation target.

## GameplayActionRunner

Continues to own request transport.

Must no longer interpret `OwnedActionComponent` as an unconditional access bypass when an action declares an access provider.

Requests must transport the optional invocation target.

## GameplayActionBinding

May carry invocation target/context in addition to its real action endpoint and cleanup source.

## GameplayActionAccessContext

Must expose the target required for domain-specific access validation.

Its semantics broaden from:

```text
access to externally-owned action
```

to:

```text
domain-specific access to this player request
```

## InteractiveComponent

Moves from:

```text
one ActionComponent
+
projection of its InteractionActions
```

toward:

```text
ordered InteractionOffers
+
target rules
+
target reservation state
```

A target action host remains assignable for `Source = Target` offers but should no longer be mandatory for every Interactive.

## InteractionInteractor

Resolves offers to real action endpoints, evaluates them, creates targeted bindings and validates authoritative Interaction requests.

## InteractionPresenter

Presents offers and resolves execution state through each offer's actual action owner.

## InteractionAction

Becomes unnecessary in the desired final model unless a remaining responsibility justifies it after the split.

## InteractionRule

Stops being injected into arbitrary action rule arrays and becomes an explicit target/offer availability mechanism.

# 43. Capabilities, bindings, grants and availability

Actor-owned targeted actions do not imply permanent input bindings, and this proposal does not make either dynamic grants or `InputGameplayAction` obsolete.

The four concepts must remain distinct:

```text
GameplayAction
    = capability

InputGameplayAction
    = capability + default local binding

InteractionOffer
    = contextual binding/invocation of a capability against a target

Grant
    = mutation of the actor's owned capability set

Availability
    = whether an already-owned capability or offer is currently usable
```

This distinction is important for keeping permanent actor capabilities lightweight while preserving real dynamic capability acquisition.

## Permanent capability does not mean permanent binding

A player may permanently own:

```text
Take
Force
Revive
```

without any of those actions having a default input binding.

For example:

```text
Player
└── GameplayActions
    └── Take
```

does not by itself contribute an input candidate.

A focused Battery instead creates a contextual Interaction binding:

```text
Battery Take offer
    ↓
Interact input
    -> Player.Take(Target = Battery)
```

When focus is lost, the binding disappears.

`Player.Take` remains owned by the player.

The same action can therefore be offered by any compatible target without grants, clones or permanent input candidates:

```text
BatteryA -> Player.Take(BatteryA)
BatteryB -> Player.Take(BatteryB)
Fuse     -> Player.Take(Fuse)
```

## InputGameplayAction remains useful

`InputGameplayAction` should have a narrow meaning:

> An owned Gameplay Action that provides its own default local binding.

It remains appropriate when the input is intrinsic to the actor capability rather than supplied by an external interaction context.

Examples include:

```text
Drop
Reload
ToggleFlashlight
UseEquippedItem
```

and dynamically granted capabilities such as:

```text
Grapple
VehicleBoost
ScannerPulse
```

when those capabilities should automatically become locally bound while owned.

Conversely, an action does not need to be an `InputGameplayAction` merely because a player eventually triggers it through an input.

For example:

```text
Take
Force
Revive
```

may remain ordinary `GameplayAction` occurrences because their local input binding belongs to the `InteractionOffer` that invokes them.

## Grants represent capability ownership changes

Dynamic grants remain a first-class Gameplay Action use case.

They should be used when the actor genuinely gains or loses a capability.

Examples:

```text
equip grappling hook
    -> grant Grapple

unequip grappling hook
    -> ungrant Grapple

enter vehicle
    -> grant VehicleBoost / Horn / ExitVehicle

leave vehicle
    -> ungrant those capabilities
```

A grant may contain an `InputGameplayAction`, in which case its default binding naturally appears and disappears with the granted action.

Grants should not be used merely to make a permanently-owned capability temporarily usable.

## Availability represents temporary usability

If an actor always conceptually owns a capability but current gameplay state prevents its use, the action should normally remain owned and express that state through rules/availability.

For example:

```text
Player.Drop
```

is naturally a permanent capability:

```text
CarriedItemId == null
    -> Hidden

CarriedItemId != null
    -> Allowed
```

The action and its default binding may remain stable for the player's lifetime while availability changes when carry state changes.

The binding store already treats availability as cached state refreshed through explicit invalidation, so permanent actions do not imply that every action rule must be evaluated every frame.

## Carry consequence

The desired carry model is therefore:

| Capability | Ownership | Binding | Lifetime |
| --- | --- | --- | --- |
| `Take(target)` | Player-owned | Supplied by `InteractionOffer` | Binding follows focused offer |
| `Force(target)` | Player-owned | Supplied by `InteractionOffer` | Binding follows focused offer |
| `Drop()` | Player-owned | Default binding | Permanent action, conditional availability |
| `Grapple()` from equipment | Dynamically player-owned | Default binding | Follows grant lifetime |

For the current Battery implementation, `Drop` should therefore no longer need to be created once per carried item.

Instead:

```text
Player
└── GameplayActions
    ├── Take
    └── Drop
```

`DropExecutor` acts on the player's current carried item rather than storing a target item definition captured when the action was granted.

The transition after a successful pickup becomes:

```text
Battery disappears
    -> InteractionOffer binding for Take disappears

CarriedItemId changes
    -> Drop availability invalidated
    -> Drop becomes Allowed
```

and after dropping:

```text
CarriedItemId becomes empty
    -> Drop availability invalidated
    -> Drop becomes Hidden
```

This removes unnecessary action grant/ungrant churn from carry while preserving grants for gameplay cases where the actor's actual capability set changes.

## Decision

The architecture therefore deliberately keeps all four mechanisms:

```text
stable capability
contextual offer/binding
dynamic grant
runtime availability
```

They are complementary rather than competing lifecycle models.

An implementation should not use grants as a substitute for availability, nor default bindings as a substitute for contextual Interaction offers.

---

# 44. Migration direction

The migration should preserve one final production path rather than permanently supporting two parallel Interaction models.

Existing target-owned Interaction scenes map mechanically to target-owned offers.

Conceptually:

```text
old:
Interactive.ActionComponent
    -> InteractionAction Open
        DefaultBindingConfig
        WhenExecutingBySelf
        WhenExecutingByOther

new:
Interactive.Offers
    -> Open offer
        Source = Target
        ActionId = open
        BindingConfig
        target busy outcomes

GameplayActionComponent
    -> Open GameplayAction
```

Shared `TargetRules` remain on the Interactive.

Action-specific gameplay rules remain on the owned Gameplay Action.

Interaction-specific offer rules move onto the offer.

The Battery should be the first actor-owned targeted conversion and therefore the first proof that the new path is complete.

A mixed Door with target-owned `Open` and actor-owned `Force` should be the second proof.

Once both work, remaining target-owned scenes can migrate mechanically and obsolete compatibility structures can be removed.

---

# 45. Required behavioral tests

The implementation is not complete until the architecture is demonstrated by behavior rather than only type shape.

At minimum the resulting system must prove:

```text
1. Existing target-owned action
   Door.Open still works through Interaction.

2. Actor-owned targeted action
   Player.Take(Battery) works through the same runner/request path.

3. One owned Take action
   can target multiple different batteries without grants or clones.

4. Target destruction
   Battery may QueueFree at commit while Player.Take remains Running.

5. Owner concurrency
   Player cannot start incompatible full-body work while Take runs.

6. Target concurrency
   two players cannot both start Take against the same Battery.

7. Mixed ownership
   Door can present target-owned Open and actor-owned Force together.

8. Cross-model target reservation
   active Door.Open interaction blocks Player.Force(Door)
   and active Player.Force(Door) blocks Door.Open interaction
   when both use the same target group.

9. Double rule validation
   target rules and action rules may independently Hidden/Block.

10. Server reconstruction
    forged target/action combinations are rejected even if the
    client can construct a local request.

11. Cancellation/failure
    releases target claim.

12. Synchronous completion/rejection
    never leaks target claim.

13. Target removal
    never leaves an invalid target reservation.

14. Presentation
    focused UI may show actions owned by different components
    in one ordered offer list.

15. Requester relation
    target reservation can distinguish self from other.

16. Network
    target claim is authoritative and late join does not invent
    an unclaimed target while a relevant claim is still active.
```

---

# 46. Architecture decisions

## AD-01 — Interaction offers invocations, not owned actions

An Interactive describes what an interactor may invoke against it.

The action occurrence may belong to another Gameplay Action host.

## AD-02 — Gameplay Action ownership remains singular

Every action occurrence still has exactly one `GameplayActionComponent` owner.

There is no shared or transferred execution ownership.

## AD-03 — Target is invocation data

The target belongs to one evaluation/execution occurrence.

It does not mutate the action definition or occurrence.

## AD-04 — Target rules and action rules are separate

Interaction validates target/offer semantics.

Gameplay Action validates the resolved action owner and its own rules.

Neither rule collection is dynamically injected into the other.

## AD-05 — Action concurrency and target concurrency are orthogonal

The action owner protects itself through existing host-local reservations.

Interaction may additionally claim the target to prevent conflicting actors from using the same world object.

## AD-06 — Target reservation is offer-oriented

Mixed target-owned and instigator-owned offers may share one target concurrency group.

Therefore target reservation cannot simply be inferred from the resolved action owner.

## AD-07 — Owned actions may still require request access

Ownership does not bypass domain access when an action declares an access provider.

`Take` may be player-owned while Interaction still authoritatively validates its target.

## AD-08 — No dynamic grants for focus

Focus creates bindings/invocations, not action ownership.

## AD-09 — Existing target-owned Interaction remains valid

The current architecture becomes the `Source = Target` case rather than being removed.

## AD-10 — Interaction remains an adapter

Gameplay Action still owns execution.

Interaction owns target discovery, offer projection, target-side validation and optional target reservation.

There is no second execution engine.

---

# 47. Deliberately deferred

The following should remain outside the first implementation unless concrete work proves they are immediately necessary:

```text
arbitrary target payloads
position/hit-result targets
multiple targets
explicit third-party/world action source resolvers
predicted target claims
phase-sensitive target reservation release
cross-host generic reservation framework
programmatic InteractionOffer execution
generic choreography
replicated executor callbacks
dynamic action grants
multiple simultaneous executions of one ActionId
```

The design should leave these possible without paying for them now.

---

# 48. Success criterion

The strongest test of this proposal is that the Battery scene becomes simpler while the framework becomes more expressive.

After the migration, the desired conceptual Battery topology is:

```text
Battery
├── carriable/domain data
├── Interactive
│   └── "Take can be invoked against me"
└── world representation
```

while the player owns:

```text
Player
└── GameplayActions
    ├── Take
    └── Drop
```

and the actual command reads naturally:

```text
Player.Take(Battery)
```

At the same time a Door remains free to say:

```text
I own Open
I own Close
but I also offer Player.Force against me
```

without grants, proxy actions or a second execution engine.

That is the architectural target of this proposal.

Il y a notamment deux décisions dans la spec que je trouve importantes et que je n’avais pas totalement formulées avant : **`InteractionAction` devient probablement inutile à terme**, parce que presque tout ce qui le spécialise migre naturellement vers l’offer ; et l’`AccessProviderId` ne doit plus vouloir dire « action externe », puisqu’un `Player.Take` owned doit quand même passer par la validation Interaction. Le runner fait aujourd’hui explicitement `OwnedActionComponent => true`, donc c’est bien une hypothèse réelle à casser, pas juste une idée abstraite. 

Et surtout j’ai poussé le cas `Door.Open` vs `Player.Force(Door)` jusqu’au bout : **les deux offers doivent pouvoir prendre la même target reservation**, sinon on résout Take mais on ne résout pas vraiment le modèle mixte. L’action owner concurrency reste ensuite en plus, orthogonalement. C’est probablement le morceau le plus complexe mais aussi celui qui rend l’ensemble cohérent.
